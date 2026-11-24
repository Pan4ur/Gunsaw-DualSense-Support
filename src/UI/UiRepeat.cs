namespace GunsawDualSenseSupport;

internal enum UiDirection { None, Left, Right, Up, Down }

internal sealed class UiRepeat
{
    private UiDirection held;
    private float next;

    internal UiDirection Tick(Pad pad, float now)
    {
        var direction = Direction(pad, held);
        if (direction == UiDirection.None)
        {
            held = direction;
            return direction;
        }
        if (direction != held)
        {
            held = direction;
            next = now + 0.33f;
            return direction;
        }
        if (now < next) return UiDirection.None;
        next = now + 0.095f;
        return direction;
    }

    private static UiDirection Direction(Pad pad, UiDirection held)
    {
        var x = (pad.Has(Button.Right) ? 1 : 0) - (pad.Has(Button.Left) ? 1 : 0);
        var y = (pad.Has(Button.Up) ? 1 : 0) - (pad.Has(Button.Down) ? 1 : 0);
        if (y != 0) return y > 0 ? UiDirection.Up : UiDirection.Down;
        if (x != 0) return x > 0 ? UiDirection.Right : UiDirection.Left;

        float horizontal = Math.Abs(pad.LX), vertical = Math.Abs(pad.LY);
        var same = held switch
        {
            UiDirection.Left => pad.LX < 0 && horizontal >= vertical,
            UiDirection.Right => pad.LX > 0 && horizontal >= vertical,
            UiDirection.Up => pad.LY > 0 && vertical > horizontal,
            UiDirection.Down => pad.LY < 0 && vertical > horizontal,
            _ => false
        };
        if (Math.Max(horizontal, vertical) < (same ? 0.38f : 0.57f)) return UiDirection.None;
        return horizontal >= vertical ? pad.LX > 0 ? UiDirection.Right : UiDirection.Left : pad.LY > 0 ? UiDirection.Up : UiDirection.Down;
    }
}
