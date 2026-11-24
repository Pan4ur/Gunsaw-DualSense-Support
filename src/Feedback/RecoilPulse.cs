namespace GunsawDualSenseSupport;

internal readonly struct RecoilPulse
{
    private readonly float low, high, rumbleDuration, triggerDuration, shotInterval;
    private readonly int triggerForce, hertz;
    internal bool ShortRumble { get; }

    internal RecoilPulse(float projectileDamage, int projectiles, bool pistol, bool launcher, bool rocket, float shotInterval = 0.15f, bool automatic = false)
    {
        var damageScale = projectileDamage > 0 && projectiles > 0 ? Math.Min(projectileDamage * projectiles / 50f, 1f) : 0f;
        var automaticRumble = automatic && !launcher;
        var lightAutomatic = pistol && automaticRumble;
        var strength = 0.8f + 0.2f * damageScale;
        
        if (automaticRumble)
            strength = lightAutomatic ? 0.4f + 0.1f * damageScale : 0.6f + 0.2f * damageScale;
        
        low = strength;
        high = strength * (launcher && rocket ? 0.85f : 1f);
        triggerForce = (launcher ? 7 : pistol ? 5 : 6) + (automatic ? 1 : 0);
        this.shotInterval = Math.Max(0.03f, shotInterval);
        ShortRumble = !launcher && (!pistol || automatic);
        rumbleDuration =
            (lightAutomatic ? 0.03f : 0.04f) + 0.005f * damageScale;
        triggerDuration = Math.Min(0.024f, this.shotInterval * 0.35f);
        hertz = automatic ? Math.Max(1, (int) Math.Round(1 / this.shotInterval)) : 0;
    }

    internal static float TrackInterval(float nominal, float observed, float previous)
    {
        if (observed < nominal * 0.85f || observed > nominal + 0.09f) return 0;
        return previous > 0 ? previous + (observed - previous) * 0.2f : observed;
    }

    internal float TriggerDuration(bool canContinue) => hertz == 0 ? triggerDuration : canContinue
        ? shotInterval + Math.Min(0.018f, shotInterval * 0.2f)
        : Math.Min(0.035f, shotInterval * 0.6f);

    internal (int Force, int Hertz) TriggerAt(float elapsed, bool held, bool canContinue) =>
        held && elapsed >= 0 && elapsed < TriggerDuration(canContinue) ? (triggerForce, hertz) : default;

    internal (float Low, float High) At(float elapsed, bool continuesBurst = false)
    {
        var timeout = !ShortRumble && hertz > 0 && continuesBurst && shotInterval <= 0.15f
            ? Math.Max(rumbleDuration, shotInterval + 0.035f)
            : rumbleDuration;
        return elapsed >= 0 && elapsed < timeout ? (low, high) : default;
    }
}
