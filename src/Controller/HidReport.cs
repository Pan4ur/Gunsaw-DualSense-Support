namespace GunsawDualSenseSupport;

internal static class HidReport
{
    internal const int Size = 47;
    internal const ushort ImprovedRumbleFirmware = 0x0224;
    private const int ValidFlags0 = 0, ValidFlags1 = 1, MotorRight = 2, MotorLeft = 3;
    private const int SpeakerVolume = 5, AudioControl = 7, RightTrigger = 10, LeftTrigger = 21;
    private const int ValidFlags2 = 38, LightbarRed = 44, LightbarGreen = 45, LightbarBlue = 46;
    private const byte CompatibleRumble = 0x01, SelectRumble = 0x02, SetRightTrigger = 0x04, SetLeftTrigger = 0x08;
    private const byte SetSpeakerVolume = 0x20, SetAudioControl = 0x80, InternalSpeaker = 0x30;
    private const byte ImprovedRumble = 0x04, SetLightbar = 0x04, ReleaseLightbar = 0x08;
    private const byte FullSpeakerVolume = 100;

    internal static (byte Low, byte High) EncodeRumble(float low, float high) =>
        ((byte)Math.Round(low * byte.MaxValue), (byte)Math.Round(high * byte.MaxValue));

    internal static void WriteEffects(byte[] report, bool improved, (byte Low, byte High)? rumble, int? left, (int Force, int Hertz, bool Detent)? right, bool usbSpeaker)
    {
        Array.Clear(report, 0, report.Length);
        if (rumble is { } motors)
        {
            report[ValidFlags0] = improved ? SelectRumble : (byte)(SelectRumble | CompatibleRumble);
            if (improved) report[ValidFlags2] = ImprovedRumble;
            report[MotorRight] = motors.High;
            report[MotorLeft] = motors.Low;
        }
        if (left is { } force)
        {
            report[ValidFlags0] |= SetLeftTrigger;
            Trigger.WriteLevitationResistance(report, LeftTrigger, force);
        }
        if (right is { } trigger)
        {
            report[ValidFlags0] |= SetRightTrigger;
            if (trigger.Detent) Trigger.WriteSoftDetent(report, RightTrigger);
            else Trigger.WriteKick(report, RightTrigger, trigger.Force, trigger.Hertz);
        }
        if (usbSpeaker)
        {
            report[ValidFlags0] |= SetSpeakerVolume | SetAudioControl;
            report[SpeakerVolume] = FullSpeakerVolume;
            report[AudioControl] = InternalSpeaker;
        }
    }

    internal static void WriteLightbar(byte[] report, bool release, byte red = 0, byte green = 0, byte blue = 0)
    {
        Array.Clear(report, 0, report.Length);
        report[ValidFlags1] = release ? ReleaseLightbar : SetLightbar;
        if (release) return;
        report[LightbarRed] = red;
        report[LightbarGreen] = green;
        report[LightbarBlue] = blue;
    }
}
