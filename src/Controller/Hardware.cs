using System.Runtime.InteropServices;

namespace GunsawDualSenceSupport;

internal sealed class Hardware : IDisposable
{
    [DllImport("gunsaw-dualsense-bt-audio")] private static extern int gunsaw_macos_lightbar(byte red, byte green, byte blue);

    private const float FeedbackInterval = 0.022f;
    private IntPtr device;
    private float nextScan;
    private readonly Action<string> log;
    private bool macLightbarAvailable = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private float nextMacLightbarAttempt;
    private int? macLightbarStatus;
    private int? lastLeft;
    private (int Force, int Hertz, bool Detent)? lastRight;
    private bool lastShotRumble;
    private float nextFeedback;
    private bool rumbleErrorLogged;
    private bool improvedRumble;
    private (byte Low, byte High) lastRumble;
    private (byte Red, byte Green, byte Blue)? lightbar;
    private bool lightbarReleased;
    private float lightbarReleaseRetryAt;
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
    }

    internal bool Poll(float now, out Pad pad)
    {
        pad = default;
        Sdl.SDL_GameControllerUpdate();
        if (device != IntPtr.Zero && Sdl.SDL_GameControllerGetAttached(device) == 0)
        {
            ClosePad();
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

    internal void PlayShot(int force, int hertz, float low, float high)
    {
        if (device == IntPtr.Zero) return;
        var right = (Force: force, Hertz: hertz, Detent: false);
        var kick = force > 0 && (hertz <= 0 || lastRight != right);
        var rumble = HidReport.EncodeRumble(low, high);
        if (!kick && lastRumble == rumble) return;

        HidReport.WriteEffects(report, improvedRumble, rumble, null, kick ? right : null, false);
        if (Sdl.SDL_GameControllerSendEffect(device, report, report.Length) != 0) return;
        lastRumble = rumble;
        lastShotRumble = true;
        if (kick) lastRight = right;
    }

    internal void Send(Feedback effects, float now, bool shotThisFrame = false)
    {
        if (device == IntPtr.Zero) return;
        var hasUsbRoute = Speaker.HasUsbRoute;
        if (!hasUsbRoute) routed = false;
        var routeSpeaker = hasUsbRoute && !routed;
        var leftChanged = lastLeft != effects.Left;
        var right = (effects.RightKick, effects.RightHz, effects.RightDetent && effects.RightKick == 0);
        var rightChanged = lastRight != right;
        var rumble = HidReport.EncodeRumble(effects.Low, effects.High);
        var rumbleChanged = lastRumble != rumble;
        if (now < nextFeedback && !leftChanged && !rightChanged && !shotThisFrame &&
            lastShotRumble == effects.ShotRumble && !(rumbleChanged && rumble == default)) return;

        nextFeedback = now + FeedbackInterval;
        if (!leftChanged && !rightChanged && !routeSpeaker && !rumbleChanged && !effects.ShotRumble)
        {
            lastShotRumble = false;
            return;
        }

        HidReport.WriteEffects(report, improvedRumble, rumbleChanged || rumble != default ? rumble : null,
            leftChanged ? effects.Left : null, rightChanged ? right : null, routeSpeaker);
        if (Sdl.SDL_GameControllerSendEffect(device, report, report.Length) == 0)
        {
            lastLeft = effects.Left;
            lastRight = right;
            lastRumble = rumble;
            lastShotRumble = effects.ShotRumble;
            routed |= routeSpeaker;
        }
        else if (!rumbleErrorLogged && rumble != default)
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
        if (!lightbarReleased || (lightbarReleaseRetryAt > 0 && now >= lightbarReleaseRetryAt))
        {
            HidReport.WriteLightbar(report, true);
            if (Sdl.SDL_GameControllerSendEffect(device, report, report.Length) == 0)
            {
                lightbarReleaseRetryAt = lightbarReleased ? 0 : now + 4f;
                lightbarReleased = true;
                lightbar = null;
            }
            return;
        }

        var color = (red, green, blue);
        if (lightbar == color) return;
        HidReport.WriteLightbar(report, false, red, green, blue);
        if (Sdl.SDL_GameControllerSendEffect(device, report, report.Length) == 0)
            lightbar = color;
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
        Sdl.SDL_GameControllerClose(device);
        device = IntPtr.Zero;
        lastLeft = null;
        lastRight = null;
        lastShotRumble = false;
        nextFeedback = 0;
        rumbleErrorLogged = false;
        lastRumble = default;
        lightbar = null;
        lightbarReleased = false;
        lightbarReleaseRetryAt = 0;
        nextMacLightbarAttempt = 0;
        macLightbarStatus = null;
        routed = false;
    }

    public void Dispose()
    {
        Send(default, float.PositiveInfinity);
        ClosePad();
        Speaker.Dispose();
        Sdl.SDL_QuitSubSystem(Sdl.Controller);
    }
}
