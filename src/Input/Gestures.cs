namespace GunsawDualSenceSupport;

internal sealed class Gestures
{
    internal const float HoldTime = 0.5f;
    internal bool Pickup, Drop, OpenChat, Players;
    internal int WeaponStep;
    private float downAt = -1, touchAt = -1;
    private bool fingerDown, canSwipe;
    private float swipeStartX, swipeStartY;
    private int swipeDirection;

    internal void Tick(Pad pad, float now, bool gameplayEnabled, bool chatEnabled)
    {
        Pickup = Drop = OpenChat = Players = false;
        WeaponStep = 0;
        if (!gameplayEnabled) downAt = -2;
        else if (pad.Has(Button.Down))
        {
            if (downAt == -1) downAt = now;
            if (downAt >= 0 && now - downAt >= HoldTime)
            {
                Drop = true;
                downAt = -2;
            }
        }
        else
        {
            Pickup = downAt >= 0 && now - downAt < HoldTime;
            Drop = downAt >= 0 && now - downAt >= HoldTime;
            downAt = -1;
        }

        var clicked = pad.Has(Button.Touch);
        if (!gameplayEnabled || clicked) canSwipe = false;
        if (pad.Touching && !fingerDown)
        {
            canSwipe = gameplayEnabled && !clicked;
            swipeStartX = pad.TouchX;
            swipeStartY = pad.TouchY;
            swipeDirection = 0;
        }
        else if (pad.Touching && canSwipe && swipeDirection == 0)
        {
            var dx = pad.TouchX - swipeStartX;
            var dy = pad.TouchY - swipeStartY;
            if (Math.Abs(dx) >= 0.22f && Math.Abs(dx) > Math.Abs(dy) * 1.3f)
                swipeDirection = Math.Sign(dx);
        }
        else if (!pad.Touching && fingerDown && canSwipe)
            WeaponStep = swipeDirection;
        fingerDown = pad.Touching;

        if (!chatEnabled)
            touchAt = -2;
        else if (clicked)
        {
            if (touchAt == -1)
                touchAt = now;
            
            Players = touchAt >= 0 && now - touchAt >= HoldTime;
        }
        else
        {
            OpenChat = touchAt >= 0 && now - touchAt < HoldTime;
            touchAt = -1;
        }
    }
}
