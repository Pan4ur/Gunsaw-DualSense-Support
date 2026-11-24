using UnityEngine;

namespace GunsawDualSenceSupport;

internal sealed class PadKeyboard
{
    private static readonly string[] Letters = { "1234567890", "qwertyuiop", "asdfghjkl-", "zxcvbnm.,?", " ⌫↵" };
    private static readonly string[] Symbols = { "!@#$%^&*()", "_+=[]{}<>/", ":;'\"\\|`~€-", "1234567890", " ⌫↵" };
    private readonly UiRepeat repeat = new();
    private int row, col;
    private bool symbols, shift;

    private string[] Layout => symbols ? Symbols : Letters;

    internal void Reset()
    {
        shift = false;
        symbols = false;
        row = col = 0;
        repeat.Tick(default, Time.unscaledTime);
    }

    internal char Tick(Pad pad, Func<Button, bool> pressed, bool chat = false)
    {
        if (pressed(Button.L3)) shift = !shift;
        if (pressed(Button.R3)) symbols = !symbols;
        var layout = Layout;
        col = Mathf.Min(col, layout[row].Length - 1);
        if (chat) pad.LX = pad.LY = 0;
        var move = repeat.Tick(pad, Time.unscaledTime);
        if (move == UiDirection.Left || move == UiDirection.Right)
        {
            int columns = layout[row].Length;
            col = (col + (move == UiDirection.Right ? 1 : columns - 1)) % columns;
        }
        else if (move == UiDirection.Up || move == UiDirection.Down)
        {
            float center = (col + 0.5f) / layout[row].Length;
            row = (row + (move == UiDirection.Down ? 1 : layout.Length - 1)) % layout.Length;
            col = Mathf.Min(layout[row].Length - 1, (int)(center * layout[row].Length));
        }

        if (!chat && pressed(Button.Square)) return '⌫';
        if (!chat && pressed(Button.Triangle)) return ' ';
        if (pressed(Button.Cross))
        {
            char key = layout[row][col];
            return shift ? char.ToUpperInvariant(key) : key;
        }
        return '\0';
    }

    internal void Show(PadOverlay overlay, bool top, bool chat = false) => overlay.ShowKeyboard(Layout, row, col, shift, top, chat);
}
