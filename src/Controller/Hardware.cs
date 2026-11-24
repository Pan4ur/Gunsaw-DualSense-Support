using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace GunsawDualSenseSupport;

internal sealed class Hardware : IDisposable
{
    [DllImport("gunsaw-dualsense-bt-audio")] private static extern int gunsaw_macos_lightbar(byte red, byte green, byte blue);

    private const float FeedbackInterval = 0.022f;
    private readonly object effectGate = new();
    private readonly Timer triggerTimer;
    private long triggerStarted, triggerDeadline;
    private (int Force, int Hertz, bool Detent) triggerKick;
    private bool rightDetent;
    private IntPtr device;
    private float nextScan;
    private readonly Action<string> log;
    private bool macLightbarAvailable = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private float nextMacLightbarAttempt;
    private int? macLightbarStatus;
    private int? lastLeft;
    private (int Force, int Hertz, bool Detent)? lastRight;
    private bool shotRumbleActive;
    private float nextFeedback;
    private int rumbleStopRepeats;
    private bool rumbleErrorLogged;
    private bool improvedRumble;
    private (byte Low, byte High) lastRumble;
    private (byte Red, byte Green, byte Blue)? lightbar;
    private float? lightbarReleaseRetryAt;
    private bool routed;
    private readonly byte[] report = new byte[HidReport.Size];
    internal readonly Speaker Speaker;

    internal Hardware(Action<string> audioLog)
    {
        log = audioLog;
        Speaker = new Speaker(audioLog);
        Sdl.SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS5", "1");
        Sdl.SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS5_RUMBLE", "1");
        Sdl.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        if (Sdl.SDL_InitSubSystem(Sdl.Controller) != 0) throw new InvalidOperationException(Sdl.Error);
        Sdl.SDL_GameControllerEventState(0);
        triggerTimer = new Timer(_ => EndTriggerKick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    internal bool Poll(float now, out Pad pad)
    {
        pad = default;
        Sdl.SDL_GameControllerUpdate();
        if (device != IntPtr.Zero && Sdl.SDL_GameControllerGetAttached(device) == 0)
        {
            lock (effectGate) ClosePad();
            Speaker.Close();
        }
        if (device == IntPtr.Zero && now >= nextScan)
        {
            nextScan = now + 1;
            var count = Sdl.SDL_NumJoysticks();
            for (var i = 0; i < count && device == IntPtr.Zero; i++)
                if (Sdl.SDL_GameControllerTypeForIndex(i) == Sdl.DualSense)
                    device = Sdl.SDL_GameControllerOpen(i);
            
            if (device != IntPtr.Zero)
            {
                ushort firmware = Sdl.SDL_GameControllerGetFirmwareVersion(device);
                improvedRumble = firmware >= HidReport.ImprovedRumbleFirmware;
                log($"DualSense rumble: full output, firmware 0x{firmware:X4}, {(improvedRumble ? "improved" : "compatible")} mode");
            }
        }

        if (device == IntPtr.Zero)
            return false;
        
        for (var i = 0; i <= 20; i++)
            if (Sdl.SDL_GameControllerGetButton(device, i) != 0)
                pad.Buttons |= 1 << i;
        
        pad.LX = Axis(0);
        pad.LY = -Axis(1);
        pad.RX = Axis(2);
        pad.RY = -Axis(3);
        pad.L2 = Math.Max(0, Axis(4));
        pad.R2 = Math.Max(0, Axis(5));
        pad.Touching = Sdl.SDL_GameControllerGetTouchpadFinger(device, 0, 0, out var touch, out pad.TouchX, out pad.TouchY, out _) == 0 && touch != 0;
        return true;
    }

    private float Axis(int axis) => Math.Max(-1f, Sdl.SDL_GameControllerGetAxis(device, axis) / 32767f);

    internal void PlayShot(int force, int hertz, float low, float high, float duration)
    {
        lock (effectGate)
        {
            if (device == IntPtr.Zero) return;
            var right = (Force: force, Hertz: hertz, Detent: false);
            var kick = force > 0 && (hertz <= 0 || lastRight != right);
            var rumble = HidReport.EncodeRumble(low, high);
            SendEffects(lastRumble != rumble ? rumble : null, null, kick ? right : null, false);

            if (force > 0)
            {
                triggerKick = right;
                rightDetent = false;
                triggerStarted = Stopwatch.GetTimestamp();
                SetTriggerDeadline(triggerStarted, duration);
            }
        }
    }

    internal void Send(Feedback effects, float now, bool shotThisFrame = false)
    {
        lock (effectGate)
        {
            if (device == IntPtr.Zero) return;
            rightDetent = effects.RightDetent;
            var timestamp = Stopwatch.GetTimestamp();
            if (timestamp < triggerDeadline) SetTriggerDeadline(timestamp, effects.RightKickDuration);
            var kicking = timestamp < triggerDeadline;
            var right = kicking ? triggerKick : (0, 0, rightDetent);
            var rightChanged = lastRight != right;
            var leftChanged = lastLeft != effects.Left;
            var hasUsbRoute = Speaker.HasUsbRoute;
            if (!hasUsbRoute) routed = false;
            var routeSpeaker = hasUsbRoute && !routed;
            var rumble = HidReport.EncodeRumble(effects.Low, effects.High);
            var repeatStop = rumble == default && rumbleStopRepeats > 0 && now >= nextFeedback;
            var sendRumble = repeatStop || (rumble != lastRumble && (now >= nextFeedback || leftChanged || rightChanged ||
                shotThisFrame || shotRumbleActive != effects.ShotRumble || rumble == default));

            shotRumbleActive = effects.ShotRumble;
            if (sendRumble) nextFeedback = now + FeedbackInterval;
            SendEffects(sendRumble ? rumble : null, leftChanged ? effects.Left : null, rightChanged ? right : null, routeSpeaker);
            if (!kicking && triggerDeadline != 0 && lastRight == right) CancelTriggerTimer();
        }
    }

    private void SetTriggerDeadline(long now, float duration)
    {
        var deadline = triggerStarted + (long)Math.Round(duration * (double)Stopwatch.Frequency);
        if (deadline == triggerDeadline) return;
        triggerDeadline = deadline;
        triggerTimer.Change(TriggerDelay(now), Timeout.Infinite);
    }

    private int TriggerDelay(long now) =>
        (int)Math.Min(int.MaxValue, Math.Ceiling(Math.Max(0, triggerDeadline - now) * 1000.0 / Stopwatch.Frequency));

    private void EndTriggerKick()
    {
        lock (effectGate)
        {
            if (triggerDeadline == 0) return;
            var delay = TriggerDelay(Stopwatch.GetTimestamp());
            if (delay > 0)
            {
                triggerTimer.Change(delay, Timeout.Infinite);
                return;
            }

            var right = (0, 0, rightDetent);
            if (lastRight != right) SendEffects(null, null, right, false);
            if (lastRight == right) CancelTriggerTimer();
            else triggerTimer.Change((int)Math.Ceiling(FeedbackInterval * 1000), Timeout.Infinite);
        }
    }

    private void CancelTriggerTimer()
    {
        triggerDeadline = 0;
        triggerTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void SendEffects((byte Low, byte High)? rumble, int? left, (int Force, int Hertz, bool Detent)? right, bool usbSpeaker)
    {
        if (rumble == null && left == null && right == null && !usbSpeaker) return;

        var motors = rumble ?? (lastRumble == default ? null : lastRumble);
        HidReport.WriteEffects(report, improvedRumble, motors, left, right, usbSpeaker);
        if (Sdl.SDL_GameControllerSendEffect(device, report, report.Length) == 0)
        {
            lastLeft = left ?? lastLeft;
            lastRight = right ?? lastRight;
            if (rumble.HasValue)
            {
                if (rumble.Value != default) rumbleStopRepeats = 0;
                else if (lastRumble != default) rumbleStopRepeats = 2;
                else if (rumbleStopRepeats > 0) rumbleStopRepeats--;
                lastRumble = rumble.Value;
            }
            routed |= usbSpeaker;
        }
        else if (!rumbleErrorLogged && motors.HasValue)
        {
            rumbleErrorLogged = true;
            log($"DualSense rumble report failed: {Sdl.Error}");
        }
    }

    internal void SetLightbar(byte red, byte green, byte blue, float now)
    {
        if (device == IntPtr.Zero || lastRumble != default) return;
        if (TrySetMacLightbar(red, green, blue, now) || Speaker.SetBluetoothLightbar(red, green, blue, now))
        {
            lightbar = null;
            return;
        }
        var release = lightbarReleaseRetryAt == null || now >= lightbarReleaseRetryAt;
        var color = (red, green, blue);
        if (!release && lightbar == color) return;

        lock (effectGate)
        {
            HidReport.WriteLightbar(report, release, red, green, blue);
            if (Sdl.SDL_GameControllerSendEffect(device, report, report.Length) != 0) return;
        }
        if (release) lightbarReleaseRetryAt = lightbarReleaseRetryAt == null ? now + 4f : float.PositiveInfinity;
        lightbar = release ? null : color;
    }

    private bool TrySetMacLightbar(byte red, byte green, byte blue, float now)
    {
        if (!macLightbarAvailable || now < nextMacLightbarAttempt) return false;

        int status;
        try { status = gunsaw_macos_lightbar(red, green, blue); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            macLightbarAvailable = false;
            status = -2;
        }
        if (status != macLightbarStatus)
        {
            var message = status switch
            {
                1 => "GameController reports the requested color",
                -1 => "GameController did not report the requested color",
                0 => "GameController did not find a single DualSense light",
                _ => "GameController lightbar bridge unavailable"
            };
            log($"DualSense lightbar: {message} (#{red:X2}{green:X2}{blue:X2})");
            macLightbarStatus = status;
        }
        if (status == 1) return true;
        nextMacLightbarAttempt = now + 2;
        return false;
    }

    private void ClosePad()
    {
        if (device == IntPtr.Zero) return;
        CancelTriggerTimer();
        Sdl.SDL_GameControllerClose(device);
        device = IntPtr.Zero;
        lastLeft = null;
        lastRight = null;
        rightDetent = false;
        shotRumbleActive = false;
        nextFeedback = 0;
        rumbleStopRepeats = 0;
        rumbleErrorLogged = false;
        lastRumble = default;
        lightbar = null;
        lightbarReleaseRetryAt = null;
        nextMacLightbarAttempt = 0;
        macLightbarStatus = null;
        routed = false;
    }

    public void Dispose()
    {
        lock (effectGate)
        {
            Send(default, float.PositiveInfinity);
            ClosePad();
            triggerTimer.Dispose();
        }
        Speaker.Dispose();
        Sdl.SDL_QuitSubSystem(Sdl.Controller);
    }
}
