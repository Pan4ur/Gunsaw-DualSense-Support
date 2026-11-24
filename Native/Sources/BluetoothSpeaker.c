#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <hidsdi.h>
#ifndef CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
#define CREATE_WAITABLE_TIMER_HIGH_RESOLUTION 0x00000002
#endif
#define EXPORT __declspec(dllexport)
#else
#include <CoreFoundation/CoreFoundation.h>
#include <IOKit/hid/IOHIDLib.h>
#include <mach/mach.h>
#include <mach/mach_time.h>
#include <pthread.h>
#define EXPORT __attribute__((visibility("default")))
#endif
#include <opus/opus.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

enum { ReportSize = 78, FrameBytes = 31 };

typedef struct {
#ifdef _WIN32
    HANDLE device, write_event, timer;
    DWORD output_size;
    uint8_t *output;
#else
    IOHIDDeviceRef device;
#endif
    OpusEncoder *encoder;
    uint8_t sequence, session;
    bool configured, lightbar_configured;
} Speaker;

EXPORT int gunsaw_bt_set_sender_priority(uint32_t period) {
#ifdef _WIN32
    (void)period;
    return SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_ABOVE_NORMAL) ? 0 : (int)GetLastError();
#else
    pthread_set_qos_class_self_np(QOS_CLASS_USER_INTERACTIVE, 0);
    mach_timebase_info_data_t timebase;
    mach_timebase_info(&timebase);
    thread_time_constraint_policy_data_t policy = {
        .period = period,
        .computation = (uint32_t)(2000000ull * timebase.denom / timebase.numer),
        .constraint = (uint32_t)(10000000ull * timebase.denom / timebase.numer),
        .preemptible = TRUE,
    };
    return thread_policy_set(pthread_mach_thread_np(pthread_self()), THREAD_TIME_CONSTRAINT_POLICY,
                             (thread_policy_t)&policy, THREAD_TIME_CONSTRAINT_POLICY_COUNT);
#endif
}

#ifdef _WIN32
EXPORT void gunsaw_bt_wait(Speaker *speaker, int64_t ticks) {
    LARGE_INTEGER delay = {.QuadPart = -ticks};
    if (SetWaitableTimer(speaker->timer, &delay, 0, NULL, NULL, FALSE))
        WaitForSingleObject(speaker->timer, INFINITE);
    else
        Sleep((DWORD)((ticks + 9999) / 10000));
}
#endif

static void seal(uint8_t *report, int size) {
    int payloadSize = size - 4;
    uint32_t crc = UINT32_MAX;
    for (int i = -1; i < payloadSize; ++i) {
        crc ^= i < 0 ? 0xa2 : report[i];
        for (int bit = 0; bit < 8; ++bit)
            crc = (crc >> 1) ^ ((crc & 1) ? 0xedb88320u : 0);
    }
    crc = ~crc;
    for (int i = 0; i < 4; ++i)
        report[payloadSize + i] = (uint8_t)(crc >> (8 * i));
}

static int send_report(Speaker *speaker, uint8_t report[ReportSize]) {
    report[0] = 0x31;
    report[1] = (speaker->sequence++ & 15) << 4;
    seal(report, ReportSize);
#ifdef _WIN32
    memcpy(speaker->output, report, ReportSize);
    OVERLAPPED request = {.hEvent = speaker->write_event};
    ResetEvent(speaker->write_event);
    DWORD written = 0;
    if (!WriteFile(speaker->device, speaker->output, speaker->output_size, &written, &request)) {
        DWORD error = GetLastError();
        if (error != ERROR_IO_PENDING) return (int)error;
        DWORD wait = WaitForSingleObject(speaker->write_event, 50);
        if (wait != WAIT_OBJECT_0) {
            error = wait == WAIT_TIMEOUT ? ERROR_TIMEOUT : GetLastError();
            CancelIoEx(speaker->device, &request);
            GetOverlappedResult(speaker->device, &request, &written, TRUE);
            return (int)error;
        }
        if (!GetOverlappedResult(speaker->device, &request, &written, FALSE)) return (int)GetLastError();
    }
    return written == speaker->output_size ? 0 : ERROR_WRITE_FAULT;
#else
    return IOHIDDeviceSetReport(speaker->device, kIOHIDReportTypeOutput, 0x31, report, ReportSize);
#endif
}

EXPORT int gunsaw_bt_lightbar(Speaker *speaker, uint8_t red, uint8_t green, uint8_t blue) {
    if (!speaker->lightbar_configured) {
        uint8_t setup[ReportSize] = {[2] = 0x10, [41] = 0x02, [44] = 0x02};
        int result = send_report(speaker, setup);
        if (result != 0) return result;
        speaker->lightbar_configured = true;
    }

    uint8_t color[ReportSize] = {[2] = 0x10, [4] = 0x04, [47] = red, [48] = green, [49] = blue};
    return send_report(speaker, color);
}

static OpusEncoder *create_encoder(int *result) {
    OpusEncoder *encoder = opus_encoder_create(48000, 1, OPUS_APPLICATION_AUDIO, result);
    if (!encoder) return NULL;
    if ((*result = opus_encoder_ctl(encoder, OPUS_SET_BITRATE(FrameBytes * 800))) != OPUS_OK ||
        (*result = opus_encoder_ctl(encoder, OPUS_SET_VBR(0))) != OPUS_OK ||
        (*result = opus_encoder_ctl(encoder, OPUS_SET_EXPERT_FRAME_DURATION(OPUS_FRAMESIZE_10_MS))) != OPUS_OK) {
        opus_encoder_destroy(encoder);
        return NULL;
    }
    return encoder;
}

EXPORT void gunsaw_bt_close(Speaker *speaker) {
    if (!speaker) return;
    if (speaker->encoder) opus_encoder_destroy(speaker->encoder);
#ifdef _WIN32
    CloseHandle(speaker->device);
    if (speaker->write_event) CloseHandle(speaker->write_event);
    if (speaker->timer) CloseHandle(speaker->timer);
    free(speaker->output);
#else
    IOHIDDeviceClose(speaker->device, kIOHIDOptionsTypeNone);
    CFRelease(speaker->device);
#endif
    free(speaker);
}

#ifdef _WIN32
EXPORT Speaker *gunsaw_bt_open_windows(const char *path, int *result) {
    wchar_t wide_path[512];
    *result = ERROR_NOT_FOUND;
    if (!path || !MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path, -1, wide_path, ARRAYSIZE(wide_path)))
        return NULL;
    HANDLE device = CreateFileW(wide_path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                                OPEN_EXISTING, FILE_FLAG_OVERLAPPED, NULL);
    if (device == INVALID_HANDLE_VALUE) {
        *result = (int)GetLastError();
        return NULL;
    }

    PHIDP_PREPARSED_DATA parsed;
    HIDP_CAPS caps;
    if (!HidD_GetPreparsedData(device, &parsed)) {
        *result = (int)GetLastError();
        CloseHandle(device);
        return NULL;
    }
    NTSTATUS status = HidP_GetCaps(parsed, &caps);
    HidD_FreePreparsedData(parsed);
    if (status != HIDP_STATUS_SUCCESS || caps.OutputReportByteLength < ReportSize) {
        *result = ERROR_NOT_SUPPORTED;
        CloseHandle(device);
        return NULL;
    }

    Speaker *speaker = calloc(1, sizeof(*speaker));
    if (!speaker) {
        *result = ERROR_OUTOFMEMORY;
        CloseHandle(device);
        return NULL;
    }
    speaker->device = device;
    speaker->output_size = caps.OutputReportByteLength;
    speaker->output = calloc(speaker->output_size, 1);
    speaker->write_event = CreateEventW(NULL, TRUE, FALSE, NULL);
    speaker->timer = CreateWaitableTimerExW(NULL, NULL, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_MODIFY_STATE | SYNCHRONIZE);
    if (!speaker->timer) speaker->timer = CreateWaitableTimerW(NULL, FALSE, NULL);
    *result = ERROR_OUTOFMEMORY;
    if (speaker->output && speaker->write_event && speaker->timer)
        speaker->encoder = create_encoder(result);
    if (speaker->encoder) return speaker;
    gunsaw_bt_close(speaker);
    return NULL;
}
#else
static IOHIDDeviceRef find_controller(void) {
    IOHIDManagerRef manager = IOHIDManagerCreate(kCFAllocatorDefault, kIOHIDOptionsTypeNone);
    if (!manager) return NULL;
    int vendor = 0x054c, product = 0x0ce6;
    CFNumberRef vid = CFNumberCreate(NULL, kCFNumberIntType, &vendor);
    CFNumberRef pid = CFNumberCreate(NULL, kCFNumberIntType, &product);
    const void *keys[] = {CFSTR(kIOHIDVendorIDKey), CFSTR(kIOHIDProductIDKey), CFSTR(kIOHIDTransportKey)};
    const void *values[] = {vid, pid, CFSTR(kIOHIDTransportBluetoothValue)};
    CFDictionaryRef match = CFDictionaryCreate(NULL, keys, values, 3, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    IOHIDManagerSetDeviceMatching(manager, match);
    CFSetRef devices = IOHIDManagerCopyDevices(manager);
    const void *device = NULL;
    if (devices && CFSetGetCount(devices) == 1) {
        CFSetGetValues(devices, &device);
        CFRetain(device);
    }
    if (devices) CFRelease(devices);
    CFRelease(match);
    CFRelease(pid);
    CFRelease(vid);
    CFRelease(manager);
    return (IOHIDDeviceRef)device;
}

EXPORT Speaker *gunsaw_bt_open(int *result) {
    IOHIDDeviceRef device = find_controller();
    if (!device) {
        *result = kIOReturnNotFound;
        return NULL;
    }
    *result = IOHIDDeviceOpen(device, kIOHIDOptionsTypeNone);
    if (*result != 0) {
        CFRelease(device);
        return NULL;
    }
    Speaker *speaker = calloc(1, sizeof(*speaker));
    if (!speaker) {
        *result = kIOReturnNoMemory;
        IOHIDDeviceClose(device, kIOHIDOptionsTypeNone);
        CFRelease(device);
        return NULL;
    }
    speaker->device = device;
    speaker->encoder = create_encoder(result);
    if (!speaker->encoder) {
        gunsaw_bt_close(speaker);
        return NULL;
    }

    return speaker;
}
#endif

EXPORT int gunsaw_bt_send(Speaker *speaker, const float samples[1024]) {
    if (!speaker->configured) {
        uint8_t config[ReportSize] = {[2] = 0x10, [3] = 0xa0, [4] = 0x82, [8] = 100, [10] = 0x31, [40] = 0x07};
        int result = send_report(speaker, config);
        if (result != 0) return result;
        speaker->configured = true;
    }

    enum { AudioBuffer = 96 };
    uint8_t report[ReportSize] = {[2] = 0x91, [3] = 6, [4] = 0x7e, [5] = AudioBuffer, [6] = AudioBuffer,
                                  [7] = AudioBuffer, [8] = AudioBuffer, [10] = 0xd3, [11] = FrameBytes};
    for (int frame = 0; frame < 2; ++frame) {
        float pcm[480];
        const float *input = samples + frame * 512;
        for (int i = 0; i < 479; ++i) {
            int scaled = i * 511;
            int start = scaled / 479;
            float fraction = (scaled % 479) / 479.0f;
            pcm[i] = input[start] + (input[start + 1] - input[start]) * fraction;
        }
        pcm[479] = input[511];
        if (opus_encode_float(speaker->encoder, pcm, 480, report + 12 + frame * FrameBytes, FrameBytes) != FrameBytes)
            return OPUS_INTERNAL_ERROR;
    }
    report[9] = speaker->session += 2;
    return send_report(speaker, report);
}