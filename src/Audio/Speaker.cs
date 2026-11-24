using System.Runtime.InteropServices;
using UnityEngine;

namespace GunsawDualSenseSupport;

internal sealed class Speaker(Action<string> log, Func<IntPtr> controller) : IDisposable
{
    private const int Rate = 48000;
    private BluetoothSpeaker? bluetooth;
    private bool reportedFailure;
    private uint device;
    private int channels;
    private float nextScan, nextTiming;
    private bool initialized;
    private AudioClip? engineClip, locatorClip;
    private float[]? engine, locator;
    private double enginePosition, locatorPosition = -1;
    private int engineRate, locatorRate;
    private int heartbeatPosition = -1;
    private float[] buffer = [];

    internal bool HasUsbRoute => device != 0;
    internal bool HasBluetoothRoute => bluetooth != null;
    internal bool SetBluetoothLightbar(byte red, byte green, byte blue, float now) =>
        bluetooth?.SetLightbar(red, green, blue, now) == true;
    
    private bool IsOpen => HasUsbRoute || bluetooth != null;

    internal void Tick(float now, AudioSource? motor, float volume, float gain)
    {
        if (device != 0 && Sdl.SDL_GetAudioDeviceStatus(device) == 0) Close();
        if (bluetooth != null && bluetooth.Failed(out int error))
        {
            var timing = bluetooth.TakeTiming();
            if (timing != null) log(timing);
            log($"DualSense Bluetooth speaker write failed: 0x{error:X8}");
            bluetooth.Dispose();
            bluetooth = null;
            nextScan = now + 2;
        }
        
        if (!IsOpen && now >= nextScan)
        {
            nextScan = now + 2;
            Open();
        }

        if (!IsOpen) return;

        var clip = motor != null ? motor.clip : null;
        if (clip != engineClip)
        {
            engineClip = clip;
            engine = ReadMono(clip);
            engineRate = clip != null ? clip.frequency : Rate;
            enginePosition = 0;
        }

        if (bluetooth != null)
        {
            bluetooth.SetGain(gain);
            if (now >= nextTiming)
            {
                var timing = bluetooth.TakeTiming();
                if (timing != null) log(timing);
                nextTiming = now + 5;
            }
            if (gain == 0)
            {
                heartbeatPosition = -1;
                locatorPosition = -1;
                return;
            }
            var missing = bluetooth.AvailablePackets;
            for (var i = 0; i < missing && (volume > 0 && engine != null || locatorPosition >= 0 || heartbeatPosition >= 0); i++)
            {
                Mix(BluetoothSpeaker.Frames, motor, volume, 1);
                bluetooth.Enqueue(buffer);
            }
            return;
        }

        const int frames = 960;
        var bytes = (uint)(frames * channels * sizeof(float));
        if (Sdl.SDL_GetQueuedAudioSize(device) >= bytes * 2) return;
        Mix(frames, motor, volume, gain);
        Sdl.SDL_QueueAudio(device, buffer, bytes);
    }

    private void Mix(int frames, AudioSource? motor, float volume, float gain)
    {
        if (buffer.Length != frames * channels) buffer = new float[frames * channels];
        Array.Clear(buffer, 0, buffer.Length);
        if (engine == null && locatorPosition < 0 && heartbeatPosition < 0)
            return;
        
        double speed = motor != null ? Mathf.Clamp(motor.pitch, 0.1f, 3f) * engineRate / Rate : 0;
        for (var i = 0; i < frames; i++)
        {
            float sample = 0;
            if (engine != null)
            {
                sample += engine[(int)enginePosition] * volume * 0.65f;
                enginePosition = (enginePosition + speed) % engine.Length;
            }

            if (locator != null && locatorPosition >= 0)
            {
                sample += locator[(int)locatorPosition] * 0.75f;
                locatorPosition += (double)locatorRate / Rate;
                if (locatorPosition >= locator.Length)
                    locatorPosition = -1;
            }

            if (heartbeatPosition >= 0)
            {
                var time = (double)heartbeatPosition / Rate;
                var beat = time < 0.15 ? time : time - 0.15;
                sample += (float)(Math.Sin(2 * Math.PI * 72 * beat) * Math.Exp(-27 * beat) * (time < 0.15 ? 0.65 : 0.4));
                if (++heartbeatPosition >= Rate * 0.32) heartbeatPosition = -1;
            }

            buffer[i * channels] = Mathf.Clamp(sample, -1, 1) * gain;
            if (channels > 1) buffer[i * channels + 1] = buffer[i * channels];
        }

    }

    internal void Heartbeat()
    {
        if (IsOpen) heartbeatPosition = 0;
    }

    internal void Locator(AudioClip clip)
    {
        if (!IsOpen) return;
        if (locatorClip != clip)
        {
            locatorClip = clip;
            locator = ReadMono(clip);
            locatorRate = clip.frequency;
        }

        locatorPosition = locator != null ? 0 : -1;
    }

    private static float[]? ReadMono(AudioClip? clip)
    {
        if (clip == null || clip.samples <= 0 || clip.channels <= 0 || clip.samples > clip.frequency * 30) return null;
        var samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0)) return null;
        if (clip.channels == 1) return samples;
        var mono = new float[clip.samples];
        for (int i = 0; i < samples.Length; i++) mono[i / clip.channels] += samples[i] / clip.channels;
        return mono;
    }

    private void Open()
    {
        if (!initialized)
            initialized = Sdl.SDL_InitSubSystem(Sdl.Audio) == 0;

        var count = initialized ? Sdl.SDL_GetNumAudioDevices(0) : 0;
        for (var i = 0; i < count; i++)
        {
            var name = Sdl.SDL_GetAudioDeviceName(i, 0);
            var label = Marshal.PtrToStringAnsi(name) ?? "";
            if (label.IndexOf("DualSense", StringComparison.OrdinalIgnoreCase) < 0 &&
                label.IndexOf("Wireless Controller", StringComparison.OrdinalIgnoreCase) < 0) continue;

            Sdl.SDL_GetAudioDeviceSpec(i, 0, out var native);
            var desired = new SdlAudioSpec
            {
                Frequency = Rate, Format = Sdl.Float32LittleEndian, Channels = native.Channels == 4 ? (byte)4 : (byte)2, Samples = 512
            };
            device = Sdl.SDL_OpenAudioDevice(name, 0, ref desired, out var obtained, 0);
            if (device == 0) continue;
            channels = obtained.Channels;
            Sdl.SDL_PauseAudioDevice(device, 0);
            log($"DualSense USB speaker ready ({channels} channels)");
            return;
        }

        bluetooth = BluetoothSpeaker.Open(controller(), log, out var problem);
        if (bluetooth != null)
        {
            channels = 1;
            reportedFailure = false;
            nextTiming = 0;
            log("DualSense Bluetooth speaker ready (0x31, 24.8 kbps)");
        }
        else if (problem != null && !reportedFailure)
        {
            reportedFailure = true;
            log($"DualSense Bluetooth speaker unavailable: {problem}");
        }
    }

    internal void Close()
    {
        if (device != 0) Sdl.SDL_CloseAudioDevice(device);
        device = 0;
        bluetooth?.Dispose();
        bluetooth = null;
        reportedFailure = false;
        engineClip = locatorClip = null;
        engine = locator = null;
        heartbeatPosition = -1;
        locatorPosition = -1;
    }

    public void Dispose()
    {
        Close();
        if (initialized) Sdl.SDL_QuitSubSystem(Sdl.Audio);
    }
}
