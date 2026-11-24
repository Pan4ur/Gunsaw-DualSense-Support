namespace GunsawDualSenceSupport;

internal struct MotorRumble
{
    private float strength, nextUpdate;

    internal float Update(float target, float now, bool limit)
    {
        if (!limit || target <= 0 || strength <= 0 || now >= nextUpdate)
        {
            strength = limit ? (float)Math.Round(target * 64) / 64 : target;
            nextUpdate = now + 0.125f;
        }
        return strength;
    }
}
