using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunsawDualSenseSupport;

internal sealed class PadOverlay(Transform owner)
{
    private GameObject? root;
    private Image? panel;
    private readonly List<(Image Background, TMP_Text Label)> keys = new();
    private Image[] outline = [];
    private TMP_Text? hint;
    private TMP_FontAsset? font;
    private (string[] Layout, int Row, int Col, bool Shift, bool Top, bool Chat, int Width, int Height)? keyboardState;

    internal void SetFont(TMP_FontAsset? current)
    {
        if (current == null || current == font) return;
        font = current;
        if (hint != null) hint.font = current;
        foreach (var key in keys) key.Label.font = current;
    }

    private void Open()
    {
        if (root == null)
        {
            root = new GameObject("DualSense UI", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(owner, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            panel = Image("Keyboard", root.transform, new Color(0.08f, 0.08f, 0.08f, 0.94f));
            outline = Enumerable.Range(0, 4).Select(_ => Image("Focus", root.transform, new Color(0.8f, 0.83f, 0.85f))).ToArray();
            hint = Text("Controls", root.transform, 16);
        }
        root.SetActive(true);
    }

    internal void Hide()
    {
        if (root != null) root.SetActive(false);
    }

    internal void ShowKeyboard(string[] layout, int row, int col, bool shift, bool top, bool chat)
    {
        Open();
        var state = (layout, row, col, shift, top, chat, Screen.width, Screen.height);
        if (keyboardState == state) return;
        keyboardState = state;
        foreach (var edge in outline) edge.gameObject.SetActive(false);
        panel!.gameObject.SetActive(true);
        float width = Mathf.Min(Screen.width - 24, 540);
        float height = chat ? 190 : 170;
        float x = chat && Screen.width >= 1100 ? Screen.width - width - 16 : (Screen.width - width) * 0.5f;
        float y = top ? 16 : Screen.height - height - (chat && Screen.width < 1100 ? 70 : 14);
        Place(panel.rectTransform, x, y, width, height);
        int index = 0;
        for (int r = 0; r < layout.Length; r++)
        {
            float cell = (width - 16) / layout[r].Length;
            for (int c = 0; c < layout[r].Length; c++, index++)
            {
                if (index == keys.Count)
                {
                    var background = Image("Key", panel.transform, Color.black);
                    var label = Text("Symbol", background.transform, 19);
                    label.rectTransform.anchorMin = Vector2.zero;
                    label.rectTransform.anchorMax = Vector2.one;
                    label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                    keys.Add((background, label));
                }
                var key = keys[index];
                key.Background.gameObject.SetActive(true);
                key.Background.color = r == row && c == col ? new Color(0.38f, 0.45f, 0.48f) : new Color(0.2f, 0.2f, 0.2f);
                Place(key.Background.rectTransform, 8 + c * cell, 6 + r * 27, cell - 2, 25);
                char symbol = shift ? char.ToUpperInvariant(layout[r][c]) : layout[r][c];
                key.Label.text = symbol switch { ' ' => "Space", '⌫' => "Delete", '↵' => "Enter", _ => symbol.ToString() };
            }
        }
        for (; index < keys.Count; index++) keys[index].Background.gameObject.SetActive(false);
        hint!.alignment = TextAlignmentOptions.Center;
        hint.text = chat ? "D-pad: choose   X: type   L3: shift   R3: symbols\nTouchpad: left delete   center scroll   right send" : "X: type   Square: delete   Triangle: space   L3: shift   R3: symbols";
        Place(hint.rectTransform, x + 4, y + 142, width - 8, height - 144);
    }

    internal void ShowFocus(Rect bounds, string text)
    {
        Open();
        keyboardState = null;
        panel!.gameObject.SetActive(false);
        foreach (var edge in outline) edge.gameObject.SetActive(true);
        float x = bounds.xMin - 3, y = Screen.height - bounds.yMax - 3;
        float width = bounds.width + 6, height = bounds.height + 6;
        Place(outline[0].rectTransform, x, y, width, 2);
        Place(outline[1].rectTransform, x, y + height - 2, width, 2);
        Place(outline[2].rectTransform, x, y, 2, height);
        Place(outline[3].rectTransform, x + width - 2, y, 2, height);
        hint!.alignment = TextAlignmentOptions.Left;
        hint.text = text;
        Place(hint.rectTransform, 12, Screen.height - 34, Mathf.Min(560, Screen.width - 24), 26);
    }

    private TMP_Text Text(string name, Transform parent, int size)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(parent, false);
        label.font = font != null ? font : TMP_Settings.defaultFontAsset;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Truncate;
        return label;
    }

    private static Image Image(string name, Transform parent, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
