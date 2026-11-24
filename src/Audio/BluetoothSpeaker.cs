using System.Runtime.InteropServices;
using System.Threading;

namespace GunsawDualSenseSupport;

[StructLayout(LayoutKind.Sequential)]
internal struct MachTimebase
{
    internal uint Numerator, Denominator;
}

internal sealed class BluetoothSpeaker : IDisposable
{
    internal const int Frames = 1024;
    private const string MacSystem = "/usr/lib/libSystem.B.dylib";
    private const string Library = "gunsaw-dualsense-bt-audio";
    private static int inUse;
    private readonly object gate = new();
    private readonly float[][] queue = [new float[Frames], new float[Frames], new float[Frames], new float[Frames]];
    private readonly IntPtr handle;
    private readonly Action<string> log;
    private int read, count;
    private bool stopping, failed;
    private int error;
    private float gain = 1;
    private int packets, gaps;
    private ulong maxGap, maxWrite;
    private double millisecondsPerTick;
    private (byte Red, byte Green, byte Blue)? lightbarColor;
    private bool lightbarPending, lightbarUnavailable, lightbarRejected;
    private float nextLightbarRefresh;
    private int? lastLightbarResult;

    [DllImport(MacSystem)] private static extern ulong mach_absolute_time();
    [DllImport(MacSystem)] private static extern int mach_timebase_info(out MachTimebase timebase);
    [DllImport(MacSystem)] private static extern int mach_wait_until(ulong deadline);

    [DllImport(Library)] private static extern IntPtr gunsaw_bt_open(out int result);
    [DllImport(Library)] private static extern int gunsaw_bt_set_sender_priority(uint period);
    [DllImport(Library)] private static extern int gunsaw_bt_send(IntPtr handle, float[] samples);
    [DllImport(Library)] private static extern int gunsaw_bt_lightbar(IntPtr handle, byte red, byte green, byte blue);
    [DllImport(Library)] private static extern void gunsaw_bt_close(IntPtr handle);

    private BluetoothSpeaker(IntPtr handle, Action<string> log)
    {
        this.handle = handle;
        this.log = log;
        new Thread(Play) { IsBackground = true, Name = "Gunsaw DualSense audio" }.Start();
    }

    internal static BluetoothSpeaker? Open(Action<string> log, out string? problem)
    {
        problem = null;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || Interlocked.CompareExchange(ref inUse, 1, 0) != 0)
            return null;

        IntPtr handle = IntPtr.Zero;
        bool started = false;
        try
        {
            handle = gunsaw_bt_open(out int result);
            if (handle != IntPtr.Zero)
            {
                var speaker = new BluetoothSpeaker(handle, log);
                started = true;
                return speaker;
            }
            problem = $"0x{result:X8}";
        }
        catch (DllNotFoundException)
        {
            problem = "native Bluetooth audio library unavailable";
        }
        catch (EntryPointNotFoundException)
        {
            problem = "native Bluetooth audio library incompatible";
        }
        finally
        {
            if (!started)
            {
                if (handle != IntPtr.Zero) CloseHandle(handle);
                else Volatile.Write(ref inUse, 0);
            }
        }
        return null;
    }

    internal int AvailablePackets
    {
        get { lock (gate) return queue.Length - count; }
    }

    internal bool Failed(out int result)
    {
        lock (gate)
        {
            result = error;
            return failed;
        }
    }

    internal void Enqueue(float[] samples)
    {
        lock (gate)
        {
            if (stopping || failed || count == queue.Length) return;
            Array.Copy(samples, queue[(read + count) % queue.Length], Frames);
            count++;
            Monitor.Pulse(gate);
        }
    }

    internal void SetGain(float value)
    {
        Volatile.Write(ref gain, value);
        if (value == 0)
        {
            lock (gate) count = 0;
        }
    }

    internal bool SetLightbar(byte red, byte green, byte blue, float now)
    {
        lock (gate)
        {
            if (stopping || failed || lightbarUnavailable || (lightbarRejected && now < nextLightbarRefresh)) return false;
            var color = (red, green, blue);
            if (lightbarColor == color && now < nextLightbarRefresh) return true;
            lightbarColor = color;
            lightbarPending = true;
            lightbarRejected = false;
            nextLightbarRefresh = now + 2;
            Monitor.Pulse(gate);
            return true;
        }
    }

    private void SendLightbar((byte Red, byte Green, byte Blue) color)
    {
        try
        {
            var result = gunsaw_bt_lightbar(handle, color.Red, color.Green, color.Blue);
            if (lastLightbarResult != result)
            {
                log(result == 0 ? $"DualSense lightbar: Bluetooth HID accepted RGB #{color.Red:X2}{color.Green:X2}{color.Blue:X2}" :
                    $"DualSense lightbar: Bluetooth HID rejected RGB (0x{result:X8})");
                lastLightbarResult = result;
            }
            if (result != 0)
                lock (gate) lightbarRejected = true;
        }
        catch (EntryPointNotFoundException)
        {
            lock (gate) lightbarUnavailable = true;
            log("DualSense lightbar: Bluetooth HID bridge unavailable");
        }
    }

    internal string? TakeTiming()
    {
        lock (gate)
        {
            var timing = packets > 0 && (gaps > 0 || maxWrite * millisecondsPerTick >= 32)
                ? FormattableString.Invariant($"DualSense Bluetooth audio: packets={packets}, gaps={gaps}, max gap={maxGap * millisecondsPerTick:F1} ms, max write={maxWrite * millisecondsPerTick:F1} ms")
                : null;
            packets = gaps = 0;
            maxGap = maxWrite = 0;
            return timing;
        }
    }

    private static ulong NextDeadline(ulong previous, ulong now, ulong interval)
    {
        ulong next = previous + interval;
        return now > next && now - next > interval ? now : next;
    }

    private void Play()
    {
        try
        {
            mach_timebase_info(out var timebase);
            var interval = 1_000_000_000UL * Frames * timebase.Denominator / (48000UL * timebase.Numerator);
            millisecondsPerTick = timebase.Numerator / (1_000_000.0 * timebase.Denominator);
            gunsaw_bt_set_sender_priority((uint)interval);
            var due = mach_absolute_time();
            ulong lastWrite = 0;
            var samples = new float[Frames];
            while (true)
            {
                due = NextDeadline(due, mach_absolute_time(), interval);
                mach_wait_until(due);

                (byte Red, byte Green, byte Blue)? pendingLightbar;
                bool hasAudio;
                lock (gate)
                {
                    while (count == 0 && !lightbarPending && !stopping)
                    {
                        Monitor.Wait(gate);
                        due = mach_absolute_time();
                        lastWrite = 0;
                    }
                    if (stopping) return;
                    pendingLightbar = lightbarPending ? lightbarColor : null;
                    lightbarPending = false;
                    hasAudio = count != 0;
                    if (hasAudio)
                    {
                        Array.Copy(queue[read], samples, Frames);
                        read = (read + 1) % queue.Length;
                        count--;
                    }
                }

                var volume = Volatile.Read(ref gain);
                if (hasAudio && volume != 0)
                {
                    for (var i = 0; i < Frames; i++) samples[i] *= volume;
                    var writeAt = mach_absolute_time();
                    var result = gunsaw_bt_send(handle, samples);
                    var writeDuration = mach_absolute_time() - writeAt;
                    lock (gate)
                    {
                        if (stopping) return;
                        if (result != 0)
                        {
                            failed = true;
                            error = result;
                            return;
                        }
                        packets++;
                        maxWrite = Math.Max(maxWrite, writeDuration);
                        if (lastWrite != 0)
                        {
                            ulong gap = writeAt - lastWrite;
                            maxGap = Math.Max(maxGap, gap);
                            if (gap > interval * 3 / 2) gaps++;
                        }
                    }
                    lastWrite = writeAt;
                }
                if (pendingLightbar.HasValue) SendLightbar(pendingLightbar.Value);
            }
        }
        catch (EntryPointNotFoundException)
        {
            lock (gate)
            {
                failed = true;
                error = unchecked((int)0xe00002c7);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static void CloseHandle(IntPtr handle)
    {
        try { gunsaw_bt_close(handle); }
        finally { Volatile.Write(ref inUse, 0); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            stopping = true;
            Monitor.Pulse(gate);
        }
    }
}
