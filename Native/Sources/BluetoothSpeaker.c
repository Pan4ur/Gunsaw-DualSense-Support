#include <CoreFoundation/CoreFoundation.h>
#include <IOKit/hid/IOHIDLib.h>
#include <mach/mach.h>
#include <mach/mach_time.h>
#include <opus/opus.h>
#include <pthread.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>

enum { ReportSize = 78, FrameBytes = 31 };

typedef struct {
    IOHIDDeviceRef device;
    OpusEncoder *encoder;
    uint8_t sequence, session;
    bool configured, lightbar_configured;
} Speaker;

__attribute__((visibility("default"))) int gunsaw_bt_set_sender_priority(uint32_t period) {
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
}

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
    return IOHIDDeviceSetReport(speaker->device, kIOHIDReportTypeOutput, 0x31, report, ReportSize);
}

__attribute__((visibility("default"))) int gunsaw_bt_lightbar(Speaker *speaker, uint8_t red, uint8_t green, uint8_t blue) {
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

__attribute__((visibility("default"))) void gunsaw_bt_close(Speaker *speaker) {
    if (!speaker) return;
    if (speaker->encoder) opus_encoder_destroy(speaker->encoder);
    IOHIDDeviceClose(speaker->device, kIOHIDOptionsTypeNone);
    CFRelease(speaker->device);
    free(speaker);
}

__attribute__((visibility("default"))) Speaker *gunsaw_bt_open(int *result) {
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

__attribute__((visibility("default"))) int gunsaw_bt_send(Speaker *speaker, const float samples[1024]) {
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