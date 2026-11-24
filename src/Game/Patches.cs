using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GunsawDualSenseSupport;

[HarmonyPatch]
internal static class InputPatch
{
    private static readonly MethodInfo Key = AccessTools.Method(typeof(Input), nameof(Input.GetKey), [typeof(KeyCode)]);
    private static readonly MethodInfo Down = AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), [typeof(KeyCode)]);
    private static readonly MethodInfo Up = AccessTools.Method(typeof(Input), nameof(Input.GetKeyUp), [typeof(KeyCode)]);
    private static readonly MethodInfo Mouse = AccessTools.PropertyGetter(typeof(Input), nameof(Input.mousePosition));
    private static readonly MethodInfo MouseDown = AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonDown));
    private static readonly MethodInfo Axis = AccessTools.Method(typeof(Input), nameof(Input.GetAxisRaw));
    private static readonly FieldInfo Levitator = AccessTools.Field(typeof(PlayerScript), nameof(PlayerScript.levit));
    private static readonly FieldInfo LevitationLine = AccessTools.Field(typeof(PlayerScript), nameof(PlayerScript.levitLine));

    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(PlayerScript), nameof(PlayerScript.Update));
        yield return AccessTools.Method(typeof(PlayerScript), nameof(PlayerScript.LateUpdate));
        yield return AccessTools.Method(typeof(CameraFollow), nameof(CameraFollow.LateUpdate));
        yield return AccessTools.Method(typeof(BodyWepPickup), nameof(BodyWepPickup.Update));
        yield return AccessTools.Method(typeof(GunGameRule), nameof(GunGameRule.PickupKeyDown));
        yield return AccessTools.Method(typeof(GameManager), nameof(GameManager.Update));
        yield return AccessTools.Method(typeof(LevitatorScript), nameof(LevitatorScript.TryGrab));
        yield return AccessTools.Method(typeof(NetworkAvatarManager), nameof(NetworkAvatarManager.TryGrabRemotePlayer));
        yield return AccessTools.Method(typeof(NpcReplication), nameof(NpcReplication.TryGrabClientCorpse));
        yield return AccessTools.Method(typeof(MultiplayerHud), nameof(MultiplayerHud.Update));
        yield return AccessTools.Method(typeof(MultiplayerHudUi), nameof(MultiplayerHudUi.Configure));
        yield return AccessTools.Method(typeof(ArsenalRuntime), nameof(ArsenalRuntime.Update));
        yield return AccessTools.Method(typeof(BlackoutRule), nameof(BlackoutRule.HandleInput));
        yield return AccessTools.Method(typeof(PlayerCarrySystem), nameof(PlayerCarrySystem.FindCarryTarget));
        yield return AccessTools.Method(typeof(PlayerCarrySystem), nameof(PlayerCarrySystem.Tick));
        yield return AccessTools.Method(typeof(PlayerCarryPair), nameof(PlayerCarryPair.Tick));
        yield return AccessTools.Method(typeof(PropHuntSystem), nameof(PropHuntSystem.StunTarget));
        yield return AccessTools.Method(typeof(PropHuntSystem), nameof(PropHuntSystem.Tick));
        yield return AccessTools.Method(typeof(PropHuntDisguise), nameof(PropHuntDisguise.PrepareInput));
        yield return AccessTools.Method(typeof(WorldEnvironmentReplication), nameof(WorldEnvironmentReplication.UpdateZonePrompt));
        yield return AccessTools.Method(typeof(WorldEnvironmentReplication), nameof(WorldEnvironmentReplication.UpdateButtonReactivationPrompt));
        yield return AccessTools.Method(typeof(StationRuntime), nameof(StationRuntime.Update));
    }

    [HarmonyPriority(Priority.Last)]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var target = original.DeclaringType!;
        var player = target == typeof(PlayerScript);
        var pickup = target == typeof(BodyWepPickup) || target == typeof(GunGameRule);
        var gameplay = player || pickup || target == typeof(PropHuntDisguise) ||
                       target == typeof(WorldEnvironmentReplication) || target == typeof(StationRuntime);
        var swap = target == typeof(GameManager);
        var aim = player || swap || target == typeof(CameraFollow) || target == typeof(PlayerCarrySystem) || target == typeof(PropHuntSystem);
        var levitationRay = target == typeof(LevitatorScript) || target == typeof(NetworkAvatarManager) || target == typeof(NpcReplication);
        var codes = instructions.ToList();
        var levitationStart = player ? codes.FindIndex(code => code.LoadsField(Levitator)) : -1;
        var levitationEnd = player ? codes.FindLastIndex(code => code.LoadsField(LevitationLine)) : -1;

        for (var index = 0; index < codes.Count; index++)
        {
            var code = codes[index];
            if (code.Calls(Mouse))
            {
                if (levitationRay || (levitationStart >= 0 && index >= levitationStart && index <= levitationEnd))
                    code.operand = AccessTools.Method(typeof(InputPatch), nameof(LevitationPointer));
                else if (aim)
                    code.operand = AccessTools.Method(typeof(InputPatch), nameof(Pointer));
            }
            if (code.Calls(MouseDown) && (swap || target == typeof(PropHuntSystem)))
                code.operand = AccessTools.Method(typeof(InputPatch), swap ? nameof(SwapMouseDown) : nameof(StunMouseDown));
            if (code.Calls(Axis) && player)
                code.operand = AccessTools.Method(typeof(InputPatch), nameof(ReadAxis));

            var phase = code.Calls(Key) ? 0 : code.Calls(Down) ? 1 : code.Calls(Up) ? 2 : -1;
            if (phase < 0) { yield return code; continue; }
            if (gameplay)
            {
                yield return new CodeInstruction(OpCodes.Ldc_I4, phase).MoveLabelsFrom(code).MoveBlocksFrom(code);
                yield return new CodeInstruction(pickup ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                code.operand = AccessTools.Method(typeof(InputPatch), nameof(GameKey));
            }
            else
            {
                yield return new CodeInstruction(OpCodes.Ldc_I4, phase).MoveLabelsFrom(code).MoveBlocksFrom(code);
                yield return new CodeInstruction(OpCodes.Ldtoken, target);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Type), nameof(Type.GetTypeFromHandle)));
                code.operand = AccessTools.Method(typeof(InputPatch), nameof(MenuKey));
            }
            yield return code;
        }
    }

    public static Vector3 Pointer()
    {
        Plugin.Instance?.Sample();
        return Plugin.Instance?.Pointer() ?? Input.mousePosition;
    }

    public static Vector3 LevitationPointer()
    {
        Plugin.Instance?.Sample();
        return Plugin.Instance?.LevitationPointer() ?? Input.mousePosition;
    }

    public static bool SwapMouseDown(int button)
    {
        Plugin.Instance?.Sample();
        return (button == 0 && Plugin.Instance?.SwapPressed() == true) || Input.GetMouseButtonDown(button);
    }

    public static bool StunMouseDown(int button)
    {
        Plugin.Instance?.Sample();
        return Input.GetMouseButtonDown(button) || (button == 0 && Plugin.Instance?.StunPressed() == true);
    }

    public static float ReadAxis(string axis) => axis == "Vertical" && Plugin.Instance != null ? Plugin.Instance.Vertical() : Input.GetAxisRaw(axis);

    public static bool GameKey(KeyCode key, int phase, bool pickup)
    {
        var plugin = Plugin.Instance;
        plugin?.Sample();
        return ReadPhysical(key, phase) ||
            (plugin != null && plugin.GameplayKey(key, phase, pickup) && (phase != 2 || !Input.GetKey(key)));
    }

    public static bool MenuKey(KeyCode key, int phase, Type target)
    {
        var plugin = Plugin.Instance;
        plugin?.Sample();
        return ReadPhysical(key, phase) || (phase != 2 && plugin != null && plugin.MenuKey(key, phase == 0, target));
    }

    private static bool ReadPhysical(KeyCode key, int phase) => phase == 0 ? Input.GetKey(key) : phase == 1 ? Input.GetKeyDown(key) : Input.GetKeyUp(key);
}

[HarmonyPatch(typeof(LevitatorScript), nameof(LevitatorScript.TryGrab))]
internal static class LevitationGrabPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(LevitatorScript __instance) => Plugin.Instance?.OnLevitationGrab(__instance);
}

[HarmonyPatch(typeof(LevitatorScript), nameof(LevitatorScript.FixedUpdate))]
internal static class LevitationPhysicsPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(LevitatorScript __instance) => Plugin.Instance?.RefreshLevitation(__instance);
}

[HarmonyPatch(typeof(PlayerScript), nameof(PlayerScript.DoBodyMouseOver))]
internal static class SwapHintPatch
{
    private const string MouseHint = "\nCLICK TO SWAP";

    private static void Postfix(PlayerScript __instance)
    {
        var hint = __instance.mouseOverText;
        if (Plugin.Instance?.ControllerActive == true && hint != null && hint.text.EndsWith(MouseHint, StringComparison.Ordinal))
            hint.text = hint.text.Substring(0, hint.text.Length - MouseHint.Length) + "\nR2 to swap";
    }
}

[HarmonyPatch(typeof(PlayerScript), nameof(PlayerScript.Update))]
internal static class ActionsPatch
{
    private static void Prefix(PlayerScript __instance)
    {
        Plugin.Instance?.Sample();
        Plugin.Instance?.PlayerActions(__instance);
    }
}

[HarmonyPatch(typeof(WeaponScript), nameof(WeaponScript.Shoot))]
internal static class ShotPatch
{
    [HarmonyPriority(Priority.Last)]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var ammo = AccessTools.Field(typeof(WeaponScript), nameof(WeaponScript.ammo));
        var cue = AccessTools.Method(typeof(ShotPatch), nameof(AfterAmmoSpent));
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.opcode != OpCodes.Stfld || !Equals(instruction.operand, ammo)) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, cue);
        }
    }

    private static void AfterAmmoSpent(WeaponScript weapon) => Plugin.Instance?.OnShot(weapon);
}

[HarmonyPatch(typeof(Sound), nameof(Sound.Play))]
internal static class LocatorPatch
{
    private static void Postfix(AudioClip clip)
    {
        if (Plugin.Instance != null && clip != null && clip == EmbeddedAudioLoader.LocatorSound)
            Plugin.Instance.Effects.Locate(clip);
    }
}


[HarmonyPatch(typeof(StandaloneInputModule), "SendMoveEventToSelectedObject")]
internal static class NativeMenuMovePatch
{
    private static bool Prefix() => Plugin.Instance?.ConsumesNativeNavigation(false) != true;
}

[HarmonyPatch(typeof(StandaloneInputModule), "SendSubmitEventToSelectedObject")]
internal static class NativeMenuSubmitPatch
{
    private static bool Prefix() => Plugin.Instance?.ConsumesNativeNavigation(true) != true;
}


[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Update))]
internal static class LevelControlsPatch
{
    private static void Postfix(MainMenuManager __instance) => Plugin.Instance?.RefreshLevelControls(__instance);
}

[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.UpdateLevelText))]
internal static class StartLevelPromptPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var prompt = typeof(StartLevelPromptPatch).GetMethod(nameof(Prompt), BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var code in instructions)
        {
            if (code.opcode == OpCodes.Ldstr && Equals(code.operand, "Press SPACE to start"))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code).MoveBlocksFrom(code);
                yield return new CodeInstruction(OpCodes.Call, prompt);
            }
            else yield return code;
        }
    }

    private static string Prompt(MainMenuManager owner)
    {
        var plugin = Plugin.Instance;
        if (plugin == null) return "Press SPACE to start";
        plugin.Sample();
        return PadMenu.StartLevelPrompt(owner, plugin.ControllerActive);
    }
}

[HarmonyPatch(typeof(PropHuntHud), nameof(PropHuntHud.SetPerk))]
internal static class PropHuntHintPatch
{
    private static void Prefix(ref string label)
    {
        if (Plugin.Instance?.ControllerActive != true) return;
        label = label switch
        {
            "[J] Locator" => "[R3] Locator",
            "[LMB] STUN HUNTER" => "[R1] STUN HUNTER",
            _ => label
        };
    }
}


[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
internal static class ControllerSettingsPatch
{
    private static void Postfix(MainMenuManager __instance) => Plugin.Instance?.AddControllerSettings(__instance);
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.Start))]
internal static class PauseControllerSettingsPatch
{
    private static void Postfix(GameManager __instance) => Plugin.Instance?.AddPauseControllerSettings(__instance);
}
