namespace GunsawDualSenceSupport;

internal static class Sticks
{
    internal static (float X, float Y) Movement(float x, float y, float deadZone)
    {
        if (x * x + y * y <= deadZone * deadZone) return default;
        return (Math.Abs(x) >= Math.Abs(y) * 0.5f ? x : 0, Math.Abs(y) >= Math.Abs(x) * 0.5f ? y : 0);
    }

    internal static float AimScale(float magnitude, float deadZone) =>
        Math.Max(0, Math.Min(1, (magnitude - deadZone) / (1 - deadZone)));
}
