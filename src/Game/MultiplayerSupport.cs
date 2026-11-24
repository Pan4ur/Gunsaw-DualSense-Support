using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace GunsawDualSenseSupport;

internal static class MultiplayerSupport
{
    internal const string Guid = GunsawMultiplayerPlugin.PluginGuid;
    internal static bool Enabled { get; set; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static (bool ChatOpen, bool MenuOpen, bool BlocksPlay, bool InLobby) ReadInput() =>
        (MultiplayerHud.Instance != null && MultiplayerHud.Instance.ChatOpen,
            MultiplayerLobbyUi.IsOpenInGame || ArsenalMenu.IsOpen,
            MultiplayerHud.IsTyping || TeamSystem.BlocksPlayerInput || PropHuntSystem.BlocksInput, MultiplayerSession.IsInLobby);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool ArsenalConsumesVehicleButton() => ArsenalMenu.ConsumesWhineKey(KeyCode.B);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Vector2 VehicleVelocity(Rigidbody2D body)
    {
        var world = WorldReplication.Instance;
        return MultiplayerSession.IsConnected && !MultiplayerSession.IsHost && world != null &&
            world.bodies.renderedVelocities.TryGetValue(body, out var velocity) ? velocity : body.velocity;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Sound), nameof(Sound.Play)),
            postfix: new HarmonyMethod(typeof(MultiplayerSupport), nameof(OnLocatorSound)));
        harmony.Patch(AccessTools.Method(typeof(PropHuntHud), nameof(PropHuntHud.SetPerk)),
            prefix: new HarmonyMethod(typeof(MultiplayerSupport), nameof(UpdatePropHuntHint)));
        harmony.Patch(AccessTools.Method(typeof(MultiplayerHudUi), nameof(MultiplayerHudUi.Configure)),
            postfix: new HarmonyMethod(typeof(MultiplayerSupport), nameof(UpdateCarryHint)));
        harmony.Patch(AccessTools.Method(typeof(StationRuntime), nameof(StationRuntime.LateUpdate)),
            postfix: new HarmonyMethod(typeof(MultiplayerSupport), nameof(UpdateStationPrompt)));
        harmony.Patch(AccessTools.Method(typeof(ArsenalRuntime), nameof(ArsenalRuntime.Update)),
            postfix: new HarmonyMethod(typeof(MultiplayerSupport), nameof(UpdateArsenalPrompt)));
    }

    private static void OnLocatorSound(AudioClip clip)
    {
        if (Plugin.Instance != null && clip != null && clip == EmbeddedAudioLoader.LocatorSound)
            Plugin.Instance.Effects.Locate(clip);
    }

    private static void UpdatePropHuntHint(ref string label)
    {
        if (Plugin.Instance?.ControllerActive != true) return;
        label = label switch
        {
            "[J] Locator" => "[R3] Locator",
            "[LMB] STUN HUNTER" => "[R1] STUN HUNTER",
            _ => label
        };
    }

    private static void UpdateCarryHint(MultiplayerHudUi __instance)
    {
        var label = __instance.activationText;
        if (label == null || !label.gameObject.activeInHierarchy || Plugin.Instance?.ControllerActive != true) return;
        var text = label.text;
        if (text is not ("Press C to carry player" or "Press C to stop carrying player")) return;
        label.spriteAsset = MenuPromptSprites.Asset;
        label.text = "Press " + MenuPromptSprites.Hint(Button.Square) + text.Substring("Press C".Length);
    }

    private static void UpdateStationPrompt(StationRuntime __instance) => PlayerPromptPatch.Triangle.Apply(__instance.useIcon);

    private static void UpdateArsenalPrompt(ArsenalRuntime __instance) => PlayerPromptPatch.Square.Apply(__instance.pickupPrompt);
}