using TMPro;
using UnityEngine;

namespace GunsawDualSenseSupport;

internal static class PromptTextures
{
    internal static Texture2D Load(string name, FilterMode filter)
    {
        using var resource = typeof(PromptTextures).Assembly.GetManifestResourceStream($"GunsawDualSenseSupport.Prompts.{name}.png")
            ?? throw new InvalidOperationException($"Missing prompt texture: {name}");
        
        using var data = new MemoryStream();
        resource.CopyTo(data);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            filterMode = filter,
            wrapMode = TextureWrapMode.Clamp
        };
        return !texture.LoadImage(data.ToArray(), true) ? throw new InvalidOperationException($"Invalid prompt texture: {name}") : texture;
    }
}

internal sealed class PromptSprite(Button button)
{
    private const int Size = 21;
    private readonly Dictionary<Sprite, (Sprite Other, bool IsReplacement)> sprites = new();
    private Texture2D? texture;

    internal void Apply(SpriteRenderer? renderer)
    {
        if (renderer == null || !renderer.enabled)
            return;
        
        var sprite = renderer.sprite;
        if (sprite == null)
            return;
        
        var active = Plugin.Instance?.ControllerActive == true;
        if (!sprites.TryGetValue(sprite, out var pair))
        {
            if (!active || sprite.rect.width != Size || sprite.rect.height != Size || sprite.texture.width != Size || sprite.texture.height != Size)
                return;
            
            texture ??= PromptTextures.Load(button.ToString(), FilterMode.Point);
            var replacement = Sprite.Create(texture, new Rect(0, 0, Size, Size), sprite.pivot / Size, sprite.pixelsPerUnit);
            pair = (replacement, false);
            sprites.Add(sprite, pair);
            sprites.Add(replacement, (sprite, true));
        }
        
        if (active != pair.IsReplacement)
            renderer.sprite = pair.Other;
    }
}

internal static class MenuPromptSprites
{
    private static TMP_SpriteAsset? asset;
    internal static TMP_SpriteAsset Asset => asset ??= Create();
    internal static string Hint(Button button) => $"[<sprite name=\"{button}\" color=#FFD580>]";

    private static TMP_SpriteAsset Create()
    {
        const int size = 64;
        Button[] buttons = { Button.Circle, Button.Square, Button.Triangle, Button.Cross, Button.Right };
        var texture = PromptTextures.Load("Menu", FilterMode.Bilinear);
        var sprites = new List<TMP_Sprite>();
        for (int i = 0; i < buttons.Length; i++)
        {
            sprites.Add(new TMP_Sprite
            {
                name = buttons[i].ToString(),
                x = i * size,
                width = size,
                height = size,
                yOffset = size * 0.85f,
                xAdvance = size,
                scale = 0.8f
            });
        }

        var result = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        result.name = "DualSense Menu Icons";
        result.spriteSheet = texture;
        result.spriteInfoList = sprites;
        result.material = new Material(Shader.Find("TextMeshPro/Sprite")) { mainTexture = texture };
        result.UpdateLookupTables();
        for (int i = 0; i < result.spriteCharacterTable.Count; i++)
            result.spriteCharacterTable[i].glyphIndex = (uint)i;
        result.UpdateLookupTables();
        return result;
    }
}
