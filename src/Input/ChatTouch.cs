namespace GunsawDualSenseSupport;

internal sealed class ChatTouch
{
    private bool touching, moved;
    private int zone;
    private float startY, lastY;

    internal void Reset(bool held)
    {
        touching = held;
        zone = -1;
    }

    internal (char Key, float Scroll) Tick(Pad pad)
    {
        if (!pad.Touching)
        {
            var key = touching && !moved ? zone switch { 0 => '⌫', 2 => '↵', _ => '\0' } : '\0';
            touching = false;
            return (key, 0);
        }

        if (!touching)
        {
            touching = true;
            moved = false;
            zone = pad.TouchX < 1f / 3f ? 0 : pad.TouchX > 2f / 3f ? 2 : 1;
            startY = lastY = pad.TouchY;
        }

        moved |= Math.Abs(pad.TouchY - startY) >= 0.035f || (zone == 0 && pad.TouchX >= 1f / 3f) || (zone == 2 && pad.TouchX <= 2f / 3f);
        if (zone != 1 || !moved)
            return default;

        var scroll = pad.TouchY - lastY;
        lastY = pad.TouchY;
        return ('\0', scroll);
    }
}
