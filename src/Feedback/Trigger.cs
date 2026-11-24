namespace GunsawDualSenceSupport;

internal static class Trigger
{
    internal const int FirePercent = 50;
    internal const float ButtonThreshold = 0.16f;
    private const float FireThreshold = FirePercent / 100f, FireRelease = 0.28f, DetentReset = 0.18f;
    private const int ZoneCount = 10, KickZone = 4, StrengthBits = 3;
    private const int ZonesLow = 1, ZonesHigh = 2, Strengths = 3, Frequency = 9;
    private const int DetentStart = 1, DetentEnd = 2, DetentStrength = 3;
    private const int DetentWidthPercent = 10;
    private const byte SoftDetentForce = 16;
    private const byte Off = 0x05, Resistance = 0x21, Vibration = 0x26, Bow = 0x02;

    internal static int LevitationResistance(float mass)
    {
        if (!(mass > 0)) return 1;
        float weight = Math.Min(mass / 19f, 1f);
        return 1 + (int)Math.Round(7 * weight * weight * weight);
    }

    internal static void WriteLevitationResistance(byte[] report, int offset, int force) =>
        WriteResistance(report, offset, force <= 1 ? 2 : 6 - force / 2, force);

    internal static bool FireHeld(float travel, bool wasDown, bool hasWeapon) =>
        travel >= (hasWeapon ? (wasDown ? FireRelease : FireThreshold) : ButtonThreshold);

    internal static bool ArmFireDetent(bool armed, float travel) =>
        travel <= DetentReset || (armed && travel < FireThreshold);

    private static void WriteResistance(byte[] report, int offset, int start, int force)
    {
        if (force == 0)
        {
            report[offset] = Off;
            return;
        }

        int zones = 0, strengths = 0;
        for (var i = start; i < ZoneCount; i++)
        {
            zones |= 1 << i;
            strengths |= (force - 1) << (i * StrengthBits);
        }

        report[offset] = Resistance;
        report[offset + ZonesLow] = (byte)zones;
        report[offset + ZonesHigh] = (byte)(zones >> 8);
        for (var i = 0; i < sizeof(int); i++)
            report[offset + Strengths + i] = (byte)(strengths >> (i * 8));
    }

    internal static void WriteKick(byte[] report, int offset, int force, int hertz)
    {
        WriteResistance(report, offset, KickZone, force);
        if (hertz <= 0 || force <= 0) return;
        report[offset] = Vibration;
        report[offset + Frequency] = (byte)hertz;
    }

    internal static void WriteSoftDetent(byte[] report, int offset)
    {
        report[offset] = Bow;
        report[offset + DetentStart] = (FirePercent - DetentWidthPercent) * byte.MaxValue / 100;
        report[offset + DetentEnd] = FirePercent * byte.MaxValue / 100;
        report[offset + DetentStrength] = SoftDetentForce;
    }
}
