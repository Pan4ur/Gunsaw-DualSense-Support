using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace GunsawDualSenceSupport;

[BepInPlugin(Id, "GunsawDualSenceSupport", "1.1.17")]
[BepInDependency(GunsawMultiplayerPlugin.PluginGuid)]
[DefaultExecutionOrder(-1000)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string Id = "com.gunsaw.dualsensesupport";
    internal static Plugin? Instance;
    internal readonly Effects Effects = new();
    private readonly Gestures gestures = new();
    private readonly PadMenu menu = new();
    private readonly PadKeyboard keyboard = new();
    private readonly ChatTouch chatTouch = new();
    private PadOverlay overlay = null!;
    private Func<Button, bool> pressed = null!;
    private ConfigEntry<float> aim = null!, scopedAim = null!;
    private ConfigEntry<int> leftDeadZone = null!, rightDeadZone = null!;
    private const int MaxDeadZone = 45, MaxSpeakerVolume = 100;
    private ConfigEntry<int> speakerVolume = null!;
    private EmulatorLink? emulator;
    private Hardware? hardware;
    private Harmony? harmony;
    private Pad pad, previous;
    private int sampledFrame = -1;
    private bool connected, usable, play, chatOpen, wasChatOpen, aimActive, pauseAllowed;
    private Vector2 direction, physicalMouse;
    private bool swapTriggerHeld;
    private Rigidbody2D? levitationTarget;
    private Vector2 levitationStartPosition;
    private Vector3 levitationStartPointer;
    private float nextLightbar;
    private bool previousBackground;

    private void Awake()
    {
        if (Application.isBatchMode) return;
        overlay = new PadOverlay(transform);
        pressed = Pressed;
        aim = Config.Bind("Aim", "WindowPercent", 70f, Range("The maximum tilt of the right stick, expressed as a percentage of half the window."));
        scopedAim = Config.Bind("Aim", "ScopedWindowPercent", 95f, Range("Tilt for guns with farAim."));
        var deadZone = new ConfigDescription("Radial deadzone percent.", new AcceptableValueRange<int>(0, MaxDeadZone));
        leftDeadZone = Config.Bind("Sticks", "LeftDeadZone", 18, deadZone);
        rightDeadZone = Config.Bind("Sticks", "RightDeadZone", 15, deadZone);
        speakerVolume = Config.Bind("Audio", "SpeakerVolume", MaxSpeakerVolume, new ConfigDescription("DualSense speaker volume.", new AcceptableValueRange<int>(0, MaxSpeakerVolume)));
        var emulate = Config.Bind("Emulator", "Enabled", false, "Emulator test.").Value;
        try
        {
            if (emulate)
            {
                emulator = new EmulatorLink();
                previousBackground = Application.runInBackground;
                Application.runInBackground = true;
            }
            else hardware = new Hardware(message => Logger.LogInfo(message));

            Instance = this;
            harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo(emulate ? "DualSense emulator listening on 127.0.0.1:27355" : "DualSense SDL ready");
        }
        catch (Exception error)
        {
            Logger.LogError(error);
            OnDestroy();
        }
    }

    private static ConfigDescription Range(string text) => new(text, new AcceptableValueRange<float>(5, 100));

    private void Update()
    {
        if (Instance != this) return;
        Sample();
        if (usable && chatOpen) ChatKeyboard();
    }

    internal void Sample()
    {
        if (sampledFrame == Time.frameCount) return;
        sampledFrame = Time.frameCount;
        var now = Time.unscaledTime;
        previous = pad;
        var wasConnected = connected;
        connected = emulator != null ? emulator.Poll(now, out pad) : hardware != null && hardware.Poll(now, out pad);
        usable = connected && (emulator != null || Application.isFocused);
        if (!usable) pad = default;
        var player = PlayerScript.player;
        (pad.MoveX, pad.MoveY) = Sticks.Movement(pad.LX, pad.LY, leftDeadZone.Value / 100f);
        if (pad.R2 < Trigger.ButtonThreshold) swapTriggerHeld = false;
        pad.Firing = !swapTriggerHeld && Trigger.FireHeld(pad.R2, previous.Firing, Effects.HasFireWeapon(player != null ? player.bodyScript : null));
        if (!wasConnected) previous = pad;

        var manager = GameManager.main;
        chatOpen = MultiplayerHud.Instance != null && MultiplayerHud.Instance.ChatOpen;
        var blockingMenu = MultiplayerLobbyUi.IsOpenInGame || ArsenalMenu.IsOpen || Controls.IsTextInputSelected;
        pauseAllowed = usable && !chatOpen && !blockingMenu;
        var wasPlaying = play;
        play = pauseAllowed && manager != null && !manager.paused && player != null && !(MissionManager.main != null && MissionManager.main.finished) && !MultiplayerHud.IsTyping && !TeamSystem.BlocksPlayerInput && !PropHuntSystem.BlocksInput;
        gestures.Tick(pad, now, play && wasConnected, pauseAllowed && wasConnected && MultiplayerSession.IsInLobby);
        if (wasPlaying && !play && previous.L2 >= Trigger.ButtonThreshold && player != null && player.levit != null && !Input.GetKey(player.keys["Levitate"]))
            player.levit.UnGrab();

        Vector2 mouse = Input.mousePosition;
        if (!play || mouse != physicalMouse) aimActive = false;
        physicalMouse = mouse;
        Vector2 right = new(pad.RX, pad.RY);
        var aimScale = Sticks.AimScale(right.magnitude, rightDeadZone.Value / 100f);
       
        if (play && aimScale > 0)
        {
            aimActive = true;
            direction = right.normalized * aimScale;
        }
        
        if (chatOpen && !wasChatOpen)
        {
            keyboard.Reset();
            chatTouch.Reset(pad.Touching);
        }
        
        wasChatOpen = usable && chatOpen;
    }

    internal void OnShot(WeaponScript weapon)
    {
        var player = PlayerScript.player;
        if (player == null || weapon.body != player.bodyScript)
            return;
        
        Sample();
        var (force, hertz, low, high) = Effects.Shot(weapon, play && pad.Firing);
        
        if (play) hardware?.PlayShot(force, hertz, low, high);
    }

    private void LateUpdate()
    {
        if (Instance != this) return;
        if (sampledFrame != Time.frameCount) Sample();
        menu.Tick(pad, pressed, usable && !chatOpen);
        var now = Time.unscaledTime;
        var player = PlayerScript.player;
        if (!usable) overlay.Hide();
        else
        {
            overlay.SetFont(player != null && player.ammoText != null ? player.ammoText.font : menu.Font);
            if (chatOpen) keyboard.Show(overlay, false, true);
            else menu.Show(overlay);
        }
        var active = connected && play && GameManager.main != null && !GameManager.main.paused;
        var output = Effects.Tick(player, now, connected ? hardware?.Speaker : null, active, pad.Firing, speakerVolume.Value / (float)MaxSpeakerVolume, pad.R2);
        if (active && UsesLevitationTrigger(player) && player!.bodyScript != null && player.bodyScript.isAlive && player.levit != null)
        {
            var target = player.levit.currentlyLevitating;
            if (target != null) output.Left = Trigger.LevitationResistance(target.mass);
        }
        var shotThisFrame = Effects.ShotFrame == Time.frameCount;
        hardware?.Send(output, now, shotThisFrame);
        if (hardware != null && connected && now >= nextLightbar)
        {
            nextLightbar = now + 0.1f;
            var color = ScarfLightColor(player != null ? player.bodyScript : null, now);
            hardware.SetLightbar(color.r, color.g, color.b, now);
        }

        if (emulator != null)
        {
            var state = chatOpen ? "chat" : active ? "play" : "menu";
            if (player != null && player.bodyScript != null)
            {
                var body = player.bodyScript;
                state += FormattableString.Invariant($" hp={body.health:F0} slot={body.currentWeapon} x={body.transform.position.x:F1} aim={Pointer().x:F0},{Pointer().y:F0}");
            }
            if (output.RightKick > 0) state += $" r2kick={output.RightKick} r2hz={output.RightHz}";
            emulator.Send(now, output, state, shotThisFrame);
        }
    }

    private static Color32 ScarfLightColor(BodyScript? body, float now)
    {
        var scarf = body != null ? NetworkAvatarUtilities.CreateScarfState(body) : default;
        Color start = scarf.Visible ? scarf.StartColor : Color.HSVToRGB(PlayerPrefs.GetFloat("scStHue"), PlayerPrefs.GetFloat("scStSat"), PlayerPrefs.GetFloat("scStVal"));
        Color end = scarf.Visible ? scarf.EndColor : Color.HSVToRGB(PlayerPrefs.GetFloat("scEnHue"), PlayerPrefs.GetFloat("scEnSat"), PlayerPrefs.GetFloat("scEnVal"));
        return Color.Lerp(start, end, Mathf.SmoothStep(0f, 1f, Mathf.PingPong(now / 3f, 1f)));
    }

    internal bool ConsumesNativeNavigation(bool submit)
    {
        Sample();
        if (submit && menu.IsLevelSelect && Input.GetKeyDown(KeyCode.Space)) return true;
        if (!usable || (!chatOpen && !menu.IsOpen)) return false;
        if (submit) return pad.Has(Button.Cross) || pad.Has(Button.Circle);
        return Mathf.Abs(pad.LX) > 0.18f || Mathf.Abs(pad.LY) > 0.18f || pad.Has(Button.Up) || pad.Has(Button.Down) || pad.Has(Button.Left) || pad.Has(Button.Right);
    }

    internal bool ControllerActive => usable;

    internal void AddControllerSettings(MainMenuManager owner)
    {
        if (owner.startText == null)
            return;
        
        if (owner.loadSlider != null)
        {
            AddPercentSlider(owner.loadSlider, owner.startText, "Left stick dead zone", leftDeadZone, -48, MaxDeadZone);
            AddPercentSlider(owner.loadSlider, owner.startText, "Right stick dead zone", rightDeadZone, -93, MaxDeadZone);
        }
    }

    internal void AddPauseControllerSettings(GameManager owner)
    {
        if (owner.musicVolSlider != null && owner.pauseMenuText != null)
            AddPercentSlider(owner.musicVolSlider, owner.pauseMenuText, "Controller speaker", speakerVolume, -32, MaxSpeakerVolume);
    }

    private static void AddPercentSlider(UnityEngine.UI.Slider source, TMPro.TMP_Text template, string title, ConfigEntry<int> setting, float offset, int maximum)
    {
        var parent = source.transform.parent;
        var slider = Instantiate(source, parent, false);
        slider.name = title;
        slider.onValueChanged = new UnityEngine.UI.Slider.SliderEvent();
        slider.minValue = 0;
        slider.maxValue = maximum;
        slider.wholeNumbers = true;
        slider.SetValueWithoutNotify(setting.Value);
        var rect = (RectTransform)slider.transform;
        rect.anchoredPosition += new Vector2(0, offset);

        var label = Instantiate(template, parent, false);
        label.name = title + " label";
        label.raycastTarget = false;
        var labelRect = label.rectTransform;
        labelRect.anchorMin = rect.anchorMin;
        labelRect.anchorMax = rect.anchorMax;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = rect.anchoredPosition + new Vector2(-245, 0);
        labelRect.sizeDelta = new Vector2(240, 30);
        label.alignment = TMPro.TextAlignmentOptions.Right;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12;
        label.fontSizeMax = 20;
        label.text = $"{title}  {setting.Value}%";
        slider.onValueChanged.AddListener(value =>
        {
            setting.Value = (int)value;
            label.text = $"{title}  {setting.Value}%";
        });
    }

    internal void RefreshLevelControls(MainMenuManager owner)
    {
        Sample();
        menu.RefreshLevelControls(owner, ControllerActive);
    }

    internal bool FirePressed() => play && !swapTriggerHeld && pad.R2 >= Trigger.ButtonThreshold && previous.R2 < Trigger.ButtonThreshold;

    internal bool SwapPressed()
    {
        if (!FirePressed()) return false;
        swapTriggerHeld = true;
        pad.Firing = false;
        return true;
    }

    private bool Pressed(Button button) => pad.Has(button) && !previous.Has(button);

    internal bool MenuKey(KeyCode key, bool held, Type target)
    {
        if (target == typeof(GameManager))
            return !held && ((key == KeyCode.Escape && pauseAllowed && Pressed(Button.Options)) || (key == KeyCode.R && play && Pressed(Button.Right)));
        
        if (target == typeof(MultiplayerHudUi)) 
            return held && play && key == Controls.keys[Controls.SEE_PLAYER] && gestures.Players;
        
        if (target == typeof(MultiplayerHud))
            return !held && !chatOpen && key == Controls.keys[Controls.OPEN_CHAT] && gestures.OpenChat;
        
        if (!play || held)
            return false;
        
        if (target == typeof(ArsenalRuntime))
            return key == KeyCode.B && Pressed(Button.Square) && PlayerScript.player.bodyScript != null && !PlayerScript.player.bodyScript.inVehicle;
       
        if (!Pressed(Button.R3)) 
            return false;
        
        if (PropHuntSystem.Enabled)
            return target == typeof(PropHuntSystem) && key == KeyCode.J && PropHuntSystem.LocatorVisible;
        
        return target == typeof(BlackoutRule) && key == Controls.keys[Controls.TOGGLE_HEADLAMP];
    }

    private static bool Slow(Pad state) => state.Has(Button.Circle) || ((state.MoveX != 0 || state.MoveY != 0) && new Vector2(state.LX, state.LY).sqrMagnitude < 0.25f);

    private static bool PlayerKey(KeyCode key, Pad state, bool pickup, bool pickupReader)
    {
        var player = PlayerScript.player;
        if (player == null || player.keys == null || key == KeyCode.None) return false;
        var keys = player.keys;
        var walking = Slow(state) && !pickup;
        return (key == keys["MoveLeft"] && state.MoveX < 0) ||
            (key == keys["MoveRight"] && state.MoveX > 0) ||
            (key == keys["MoveUp"] && state.MoveY > 0) ||
            (key == keys["MoveDown"] && state.MoveY < 0) ||
            (key == keys["Walk"] && walking) ||
            (key == keys["Jump"] && state.Has(Button.L1)) ||
            (key == keys["Ragdoll"] && state.Has(Button.Cross)) ||
            (key == keys["Kick"] && state.Has(Button.R1)) ||
            (key == keys["Use"] && state.Has(Button.Triangle)) ||
            (key == keys["EnterVehicle"] && state.Has(Button.Square) && ((player.bodyScript != null && player.bodyScript.inVehicle) || !ArsenalMenu.ConsumesWhineKey(KeyCode.B))) ||
            (key == keys["Fire"] && state.Firing) ||
            (key == keys["Levitate"] && state.L2 >= Trigger.ButtonThreshold) ||
            (key == keys["Unarmed"] && state.Has(Button.Up)) ||
            (key == keys["Slowmo"] && state.Has(Button.Left)) ||
            (key == keys["Reload"] && state.Has(Button.Right)) ||
            (pickupReader && key == keys["PickUp"] && pickup);
    }

    internal bool GameplayKey(KeyCode key, int phase, bool pickupReader)
    {
        var was = PlayerKey(key, previous, false, pickupReader);
        var isHeld = play && PlayerKey(key, pad, gestures.Pickup, pickupReader);
        return phase == 0 ? isHeld : phase == 1 ? isHeld && !was : was && !isHeld;
    }

    internal float Vertical() => play && pad.MoveY != 0 ? pad.MoveY : Input.GetAxisRaw("Vertical");

    internal Vector3 Pointer()
    {
        if (!play || !aimActive) return Input.mousePosition;
        var player = PlayerScript.player;
        var body = player != null ? player.bodyScript : null;
        var far = body != null && !body.unarmed && body.weapon != null && body.weapon.stats != null && body.weapon.stats.farAim;
        var fraction = (far ? scopedAim.Value : aim.Value) / 100f;
        return new Vector3(Screen.width * (0.5f + 0.5f * direction.x * fraction), Screen.height * (0.5f + 0.5f * direction.y * fraction));
    }

    private bool UsesLevitationTrigger(PlayerScript? player) => play && pad.L2 >= Trigger.ButtonThreshold && player != null &&
        !Input.GetKey(player.keys["Levitate"]);

    internal Vector3 LevitationPointer() => UsesLevitationTrigger(PlayerScript.player) ? Pointer() : Input.mousePosition;

    internal void OnLevitationGrab(LevitatorScript levitator)
    {
        var player = PlayerScript.player;
        if (player == null || levitator != player.levit) return;
        levitationTarget = UsesLevitationTrigger(player) ? levitator.currentlyLevitating : null;
        if (levitationTarget == null) return;
        levitationStartPointer = Pointer();
        levitationStartPosition = levitationTarget.transform.position;
    }

    internal void RefreshLevitation(LevitatorScript levitator)
    {
        var player = PlayerScript.player;
        if (levitationTarget == null || player == null || levitator != player.levit) return;
        if (levitator.currentlyLevitating != levitationTarget || !UsesLevitationTrigger(player))
        {
            levitationTarget = null;
            return;
        }

        var pointer = Pointer();
        if (pointer == levitationStartPointer)
        {
            levitator.point = levitationTarget.transform.position;
            return;
        }
        var camera = Camera.main;
        if (camera == null) return;
        Vector2 offset = camera.ScreenToWorldPoint(pointer) - camera.ScreenToWorldPoint(levitationStartPointer);
        levitator.point = levitationStartPosition + offset;
    }

    internal void PlayerActions(PlayerScript player)
    {
        if (!play || player != PlayerScript.player || player.observed || player.bodyScript == null) return;
        if (Pressed(Button.Share))
        {
            GameManager.main.Restart();
            return;
        }

        var body = player.bodyScript;
        if (!body.IsConsc() || !body.isAlive) return;
        if (Pressed(Button.L3))
        {
            body.screamTime = -1;
            body.DoGrunt();
        }
        if (gestures.Drop) body.DropWeaponSingle();
        var step = gestures.WeaponStep;
        if (step == 0 || body.weapon == null) return;
        var count = body.weapons.Count;
        var current = body.unarmed || body.currentWeapon < 0 ? -1 : body.currentWeapon;
        var next = current;
        if (next < 0) next = step > 0 ? count - 1 : 0;
        for (var offset = 0; offset < count; offset++)
        {
            next = (next + step + count) % count;
            if (next == current) return;
            if (body.weapons[next] == null) continue;
            body.ChangeWeapon(next);
            player.BodyWeaponChanged();
            return;
        }
    }

    private void ChatKeyboard()
    {
        var hud = MultiplayerHud.Instance;
        if (hud == null || !hud.ChatOpen) return;
        if (Pressed(Button.Options) || Pressed(Button.Circle)) { hud.CloseChat(); return; }
        var touch = chatTouch.Tick(pad);
        if (touch.Scroll != 0 && hud.nativeUi != null && hud.nativeUi.chatScroll != null)
        {
            var scroll = hud.nativeUi.chatScroll;
            var height = scroll.viewport.rect.height;
            var overflow = scroll.content.rect.height - height;
            if (overflow > 0)
            {
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + touch.Scroll * height * 3 / overflow);
            }
        }
        var at = hud.ChatCaretPosition;
        if (touch.Key == '\0' && Pressed(Button.L1)) { hud.SetChatCaretPosition(at - 1); return; }
        if (touch.Key == '\0' && Pressed(Button.R1)) { hud.SetChatCaretPosition(at + 1); return; }

        var key = touch.Key != '\0' ? touch.Key : keyboard.Tick(pad, pressed, true);
        if (key == '↵') hud.Submit();
        else if (key == '⌫' && at > 0)
        {
            hud.ChatInput = hud.ChatInput.Remove(at - 1, 1);
            hud.SetChatCaretPosition(at - 1);
        }
        else if (key != '\0' && key != '⌫' && hud.ChatInput.Length < 160)
        {
            hud.ChatInput = hud.ChatInput.Insert(at, key.ToString());
            hud.SetChatCaretPosition(at + 1);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        harmony?.UnpatchSelf();
        if (emulator != null)
        {
            Application.runInBackground = previousBackground;
            emulator.Dispose();
        }
        hardware?.Dispose();
        harmony = null;
        emulator = null;
        hardware = null;
    }
}