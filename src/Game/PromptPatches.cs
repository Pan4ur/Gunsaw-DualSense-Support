using HarmonyLib;
using TMPro;
using UnityEngine;

namespace GunsawDualSenseSupport;

[HarmonyPatch(typeof(GameManager), nameof(GameManager.Update))]
internal static class ReformHintPatch
{
    private const string KeyboardHint = "[R] to REFORM BODY";
    private static readonly string ControllerHint = MenuPromptSprites.Hint(Button.Right) + " to REFORM BODY";

    private static void Postfix()
    {
        var player = PlayerScript.player;
        if (player == null || player.reformText == null || !player.reformText.activeInHierarchy) return;
        var label = player.reformText.GetComponentInChildren<TMP_Text>();
        if (label == null) return;
        if (Plugin.Instance?.ControllerActive == true)
        {
            if (label.text != KeyboardHint) return;
            if (label.spriteAsset != MenuPromptSprites.Asset) label.spriteAsset = MenuPromptSprites.Asset;
            label.text = ControllerHint;
        }
        else if (label.text == ControllerHint) label.text = KeyboardHint;
    }
}

[HarmonyPatch(typeof(PlayerScript), nameof(PlayerScript.Update))]
internal static class PlayerPromptPatch
{
    internal static readonly PromptSprite Triangle = new(Button.Triangle);
    internal static readonly PromptSprite Square = new(Button.Square);

    private static void Postfix(PlayerScript __instance)
    {
        Triangle.Apply(__instance.buttonKeyIcon);
        Square.Apply(__instance.vehicleKeyIcon);
    }
}

[HarmonyPatch(typeof(MultiplayerHudUi), nameof(MultiplayerHudUi.Configure))]
internal static class CarryHintPatch
{
    private static void Postfix(MultiplayerHudUi __instance)
    {
        var label = __instance.activationText;
        if (label == null || !label.gameObject.activeInHierarchy || Plugin.Instance?.ControllerActive != true) return;
        var text = label.text;
        if (text is not ("Press C to carry player" or "Press C to stop carrying player")) return;
        label.spriteAsset = MenuPromptSprites.Asset;
        label.text = "Press " + MenuPromptSprites.Hint(Button.Square) + text.Substring("Press C".Length);
    }
}

[HarmonyPatch(typeof(StationRuntime), nameof(StationRuntime.LateUpdate))]
internal static class StationPromptPatch
{
    private static void Postfix(StationRuntime __instance) => PlayerPromptPatch.Triangle.Apply(__instance.useIcon);
}

[HarmonyPatch(typeof(BodyWepPickup), nameof(BodyWepPickup.Update))]
internal static class WeaponPromptPatch
{
    private static readonly PromptSprite Prompt = new(Button.Down);

    private static void Postfix(BodyWepPickup __instance) => Prompt.Apply(__instance.pickupSprite);
}

[HarmonyPatch(typeof(ArsenalRuntime), nameof(ArsenalRuntime.Update))]
internal static class ArsenalPromptPatch
{
    private static void Postfix(ArsenalRuntime __instance) => PlayerPromptPatch.Square.Apply(__instance.pickupPrompt);
}
