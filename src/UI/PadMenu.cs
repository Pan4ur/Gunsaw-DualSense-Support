using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GunsawDualSenceSupport;

internal sealed class PadMenu
{
    private const string CharacterSelectLabel = "CHARACTER\nSELECT";
    private const string StartLevelKeyboard = "START LEVEL [SPACE]";
    private static readonly string StartLevelController = "START LEVEL <size=125%>" + MenuPromptSprites.Hint(Button.Cross) + "</size>";
    private static readonly string BackToTitleController = "BACK TO TITLE " + MenuPromptSprites.Hint(Button.Circle);
    private static readonly string CharacterSelectController = CharacterSelectLabel + " " + MenuPromptSprites.Hint(Button.Square);
    private static readonly string BackToLevelController = "BACK TO LEVEL " + MenuPromptSprites.Hint(Button.Square);
    private static readonly string AdvancedInfoController = "Advanced info " + MenuPromptSprites.Hint(Button.Triangle);
    private readonly UiRepeat repeat = new();
    private readonly PadKeyboard keyboard = new();
    private readonly List<Selectable> candidates = new();
    private readonly Vector3[] corners = new Vector3[4];
    private MainMenuManager? mainMenu, levelControlsOwner;
    private TMP_Text? previousLevelHint, nextLevelHint, backToTitle, characterSelect, backToLevel, switchCharacters, advancedInfo;
    private UnityEngine.UI.Button? startLevel;
    private GameObject? scope;
    private Selectable? focus, field;
    private bool controllerFocus;
    private float nextSearch;
    private int scene = int.MinValue;
    private Vector2 mouse;

    internal bool IsOpen => Active(scope);
    internal bool IsLevelSelect => mainMenu != null && mainMenu.currentScreen == 1 && scope == mainMenu.levelSelect;
    internal TMP_FontAsset? Font => mainMenu != null && mainMenu.startText != null ? mainMenu.startText.font : null;

    internal void Tick(Pad pad, Func<Button, bool> pressed, bool available)
    {
        var physical = (Vector2)Input.mousePosition;
        if (controllerFocus && ((physical - mouse).sqrMagnitude >= 4 || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
        {
            controllerFocus = false;
            focus = field = null;
        }
        mouse = physical;
        if (!available)
        {
            Reset();
            return;
        }

        var owner = GunsawMultiplayerPlugin.Instance;
        var lobby = owner != null ? owner.multiplayerLobbyUi : null;
        var manager = GameManager.main;
        var root = CurrentScope(lobby, manager);
        if (root != scope)
        {
            field = focus = null;
            repeat.Tick(default, Time.unscaledTime);
            scope = root;
        }
        if (root == null)
        {
            Reset();
            return;
        }

        var move = repeat.Tick(pad, Time.unscaledTime);
        bool confirm = pressed(Button.Cross);
        bool back = pressed(Button.Circle);
        bool leftPage = pressed(Button.L1), rightPage = pressed(Button.R1);
        bool characterShortcut = mainMenu != null && root == mainMenu.levelSelect && pressed(Button.Square);
        bool characterScreen = mainMenu != null && root == mainMenu.characterObj;
        bool levelShortcut = characterScreen && pressed(Button.Square);
        bool advancedShortcut = characterScreen && pressed(Button.Triangle);
        bool lobbyShortcut = pressed(Button.Triangle) && manager != null && manager.paused && root == manager.pauseMenu && lobby != null;
        if (move != UiDirection.None || confirm || back || leftPage || rightPage || lobbyShortcut || pressed(Button.Options) ||
            characterShortcut || levelShortcut || advancedShortcut || (ArsenalMenu.IsOpen && pressed(Button.Square)))
            controllerFocus = true;
        if (!controllerFocus) return;

        if (mainMenu != null && root == mainMenu.levelSelect)
        {
            if (back) mainMenu.EnterTitle();
            else if (characterShortcut) mainMenu.EnterCharacterSelect();
            else if (confirm) StartSelectedLevel(mainMenu);
            else if (leftPage || rightPage) CycleMainMenu(mainMenu, rightPage ? 1 : -1);
            return;
        }
        if (levelShortcut && mainMenu != null)
        {
            mainMenu.EnterLevelSelect();
            return;
        }
        if (advancedShortcut && mainMenu != null && mainMenu.advInfoPanel != null && mainMenu.advInfoTip != null)
        {
            mainMenu.advInfoPanel.SetActive(true);
            mainMenu.advInfoTip.SetActive(false);
            return;
        }

        var events = EventSystem.current;
        if (events == null) return;
        var selected = events.currentSelectedGameObject;
        var selectedField = selected != null ? selected.GetComponent<Selectable>() : null;
        if (selectedField is not TMP_InputField && selectedField is not InputField) selectedField = null;
        if (selectedField != null && !Eligible(selectedField, root)) selectedField = null;
        if (field != null && field != selectedField) field = null;
        if (field == null && selectedField != null && IsEditing(selectedField))
            OpenField(selectedField);

        if (field != null)
        {
            EditField(pad, pressed, back || pressed(Button.Options));
            return;
        }
        if (back || (root != manager?.pauseMenu && pressed(Button.Options)))
        {
            Cancel(root, lobby, manager, events);
            return;
        }
        if (lobbyShortcut && owner != null)
        {
            owner.visible = true;
            owner.RefreshLobbies();
            return;
        }
        if ((leftPage || rightPage) && mainMenu != null && root == mainMenu.characterObj)
        {
            CycleMainMenu(mainMenu, rightPage ? 1 : -1);
            return;
        }

        if (focus == null || !Eligible(focus, root))
        {
            var existing = selected != null ? selected.GetComponent<Selectable>() : null;
            var first = existing != null && Eligible(existing, root) && Visible(existing) ? existing :
                InitialFocus(root, lobby);
            if (first != null) Focus(first, events);
            else if (confirm && MissionManager.main != null && root == MissionManager.main.finishScreen)
                SceneLoader.main.LoadScene("LevelSelect");
            return;
        }

        if (move != UiDirection.None && !AdjustSlider(focus, move))
        {
            var next = Find(root, focus, move);
            if (next != null) Focus(next, events);
        }
        if (!confirm || !Visible(focus)) return;
        if (focus is TMP_InputField tmp)
        {
            events.SetSelectedGameObject(tmp.gameObject);
            tmp.ActivateInputField();
            OpenField(tmp);
        }
        else if (focus is InputField legacy)
        {
            events.SetSelectedGameObject(legacy.gameObject);
            legacy.ActivateInputField();
            OpenField(legacy);
        }
        else ExecuteEvents.Execute(focus.gameObject, new BaseEventData(events), ExecuteEvents.submitHandler);
    }

    private GameObject? CurrentScope(MultiplayerLobbyUi? lobby, GameManager? manager)
    {
        if (Active(TeamSystem.panel)) return TeamSystem.panel;
        if (ArsenalMenu.IsOpen) return ArsenalMenu.instance.menu;
        if (lobby != null && Active(lobby.root))
        {
            if (Active(lobby.customLevelSuggestionDialog)) return lobby.customLevelSuggestionDialog;
            if (Active(lobby.passwordDialog)) return lobby.passwordDialog;
            if (Active(lobby.crossServerDialog)) return lobby.crossServerDialog;
            if (Active(lobby.resetSettingsDialog)) return lobby.resetSettingsDialog;
            if (Active(lobby.sawTunerWindow)) return lobby.sawTunerWindow;
            if (Active(lobby.weaponPicker)) return lobby.weaponPicker;
            if (Active(lobby.sequenceEditor)) return lobby.sequenceEditor;
            if (Active(lobby.teamsEditor)) return lobby.teamsEditor;
            if (Active(lobby.presetsPanel)) return lobby.presetsPanel;
            if (Active(lobby.serverBrowserPanel)) return lobby.serverBrowserPanel;
            var browser = lobby.customLevelBrowser;
            if (browser != null && browser.IsOpen && Active(browser.panel))
            {
                if (Active(browser.deleteLocalPanel)) return browser.deleteLocalPanel;
                if (Active(browser.editLocalPanel)) return browser.editLocalPanel;
                if (Active(browser.addLocalPanel)) return browser.addLocalPanel;
                return browser.panel;
            }
            if (Active(lobby.panel)) return lobby.panel;
        }
        if (manager != null && manager.paused && Active(manager.pauseMenu)) return manager.pauseMenu;
        var mission = MissionManager.main;
        if (mission != null && mission.finished && Active(mission.finishScreen)) return mission.finishScreen;

        var current = SceneManager.GetActiveScene();
        int handle = current.handle;
        if (scene != handle)
        {
            scene = handle;
            nextSearch = 0;
            mainMenu = null;
        }
        if (current.name != "LevelSelect") return null;
        if (mainMenu == null && Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + 0.5f;
            mainMenu = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
        }
        if (mainMenu == null) return null;
        return mainMenu.currentScreen switch
        {
            0 => mainMenu.title,
            1 => mainMenu.levelSelect,
            2 => mainMenu.settings,
            3 => mainMenu.characterObj,
            4 => mainMenu.credits,
            _ => null
        };
    }

    private static bool Active(GameObject? obj) => obj != null && obj.activeInHierarchy;

    internal void RefreshLevelControls(MainMenuManager owner, bool controller)
    {
        if (owner.startText == null || owner.levelSelect == null) return;
        if (levelControlsOwner != owner)
        {
            levelControlsOwner = owner;
            previousLevelHint = nextLevelHint = backToTitle = characterSelect = backToLevel = switchCharacters = advancedInfo = null;
            foreach (var label in owner.levelSelect.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.text == "(A)") previousLevelHint = label;
                if (label.text == "(D)") nextLevelHint = label;
                if (label.text == "BACK TO TITLE") backToTitle = label;
                if (label.text == CharacterSelectLabel) characterSelect = label;
            }
            if (owner.characterObj != null)
            {
                foreach (var label in owner.characterObj.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label.text == "BACK TO LEVEL") backToLevel = label;
                    if (label.text == "A-D to switch characters") switchCharacters = label;
                }
            }
            if (owner.advInfoTip != null) advancedInfo = owner.advInfoTip.GetComponentInChildren<TMP_Text>(true);
            FitBackPrompt(backToTitle);
            FitBackPrompt(backToLevel);
            startLevel = owner.startText.gameObject.AddComponent<UnityEngine.UI.Button>();
            startLevel.targetGraphic = owner.startText;
            startLevel.transition = Selectable.Transition.None;
            startLevel.navigation = new Navigation { mode = Navigation.Mode.None };
            owner.startText.raycastTarget = true;
            startLevel.onClick.AddListener(() => StartSelectedLevel(owner));
        }

        SetLabel(previousLevelHint, controller ? "(L1)" : "(A)");
        SetLabel(nextLevelHint, controller ? "(R1)" : "(D)");
        SetPrompt(backToTitle, controller, "BACK TO TITLE", BackToTitleController);
        SetPrompt(characterSelect, controller, CharacterSelectLabel, CharacterSelectController);
        SetPrompt(backToLevel, controller, "BACK TO LEVEL", BackToLevelController);
        SetLabel(switchCharacters, controller ? "L1-R1 to switch characters" : "A-D to switch characters");
        SetPrompt(advancedInfo, controller, "Advanced info [SPACE]", AdvancedInfoController);
        if (owner.currentScreen != 1) return;
        bool unlocked = LevelUnlocked(owner);
        if (startLevel != null && startLevel.interactable != unlocked) startLevel.interactable = unlocked;
        if (unlocked) SetLabel(owner.startText, StartLevelPrompt(owner, controller));
    }

    private static void FitBackPrompt(TMP_Text? label)
    {
        if (label == null) return;
        float max = label.enableAutoSizing ? label.fontSizeMax : label.fontSize;
        label.fontSizeMax = max;
        label.fontSizeMin = Mathf.Min(label.fontSizeMin, max * 0.65f);
        label.enableWordWrapping = false;
        label.enableAutoSizing = true;
    }

    internal static string StartLevelPrompt(MainMenuManager owner, bool controller) =>
        PromptText(owner.startText, controller, StartLevelKeyboard, StartLevelController);

    private static void SetPrompt(TMP_Text? label, bool controller, string keyboard, string gamepad)
    {
        if (label != null) SetLabel(label, PromptText(label, controller, keyboard, gamepad));
    }

    private static string PromptText(TMP_Text label, bool controller, string keyboard, string gamepad)
    {
        if (!controller) return keyboard;
        if (label.spriteAsset != MenuPromptSprites.Asset) label.spriteAsset = MenuPromptSprites.Asset;
        return gamepad;
    }

    private static void SetLabel(TMP_Text? label, string text)
    {
        if (label != null && label.text != text) label.text = text;
    }

    private static bool LevelUnlocked(MainMenuManager owner)
    {
        var select = MissionSelect.main;
        return select != null && owner.curIndex >= 0 && owner.curIndex < select.missions.Count &&
            select.missions[owner.curIndex].progressReq <= PlayerPrefs.GetInt("progress");
    }

    private void StartSelectedLevel(MainMenuManager owner)
    {
        if (owner.currentScreen != 1 || !LevelUnlocked(owner)) return;
        var multiplayer = GunsawMultiplayerPlugin.Instance;
        var lobby = multiplayer != null ? multiplayer.multiplayerLobbyUi : null;
        if (CurrentScope(lobby, GameManager.main) != owner.levelSelect) return;

        Sound.Play(owner.clickSound, Vector2.zero, twoDimensional: true, pitchShift: false);
        MissionSelect.main.TryLoadMission(owner.curIndex);
    }

    private static void CycleMainMenu(MainMenuManager menu, int delta)
    {
        if (menu.currentScreen == 1 && MissionSelect.main != null && MissionSelect.main.missions.Count > 0)
        {
            int count = MissionSelect.main.missions.Count;
            menu.curIndex = (menu.curIndex + delta + count) % count;
            menu.UpdateLevelText();
        }
        else if (menu.currentScreen == 3 && menu.characters.Count > 0)
        {
            menu.charIndex = (menu.charIndex + delta + menu.characters.Count) % menu.characters.Count;
            menu.UpdateCharacter();
        }
        Sound.Play(menu.clickSound, Vector2.zero, twoDimensional: true, pitchShift: false);
    }

    private void Cancel(GameObject root, MultiplayerLobbyUi? lobby, GameManager? manager, EventSystem events)
    {
        if (root == TeamSystem.panel) return;
        if (ArsenalMenu.IsOpen && root == ArsenalMenu.instance.menu) ArsenalMenu.instance.CloseAndEquip();
        else if (manager != null && root == manager.pauseMenu) manager.UnPause();
        else if (lobby != null && root == lobby.panel) GunsawMultiplayerPlugin.Instance.visible = false;
        else if (lobby != null && root == lobby.passwordDialog) lobby.ClosePasswordDialog();
        else if (lobby != null && root == lobby.serverBrowserPanel) lobby.SetServerBrowserOpen(false);
        else if (lobby != null && root == lobby.customLevelBrowser?.panel) lobby.customLevelBrowser.SetOpen(false);
        else if (lobby != null && (root == lobby.sawTunerWindow || root == lobby.weaponPicker || root == lobby.sequenceEditor ||
            root == lobby.teamsEditor || root == lobby.presetsPanel)) root.SetActive(false);
        else if (lobby != null && lobby.root != null && root.transform.IsChildOf(lobby.root.transform))
        {
            var cancel = CancelButton(root);
            if (cancel != null) ExecuteEvents.Execute(cancel.gameObject, new BaseEventData(events), ExecuteEvents.submitHandler);
        }
        else if (mainMenu != null && root == mainMenu.characterObj) mainMenu.EnterLevelSelect();
        else if (mainMenu != null && root != mainMenu.title)
        {
            var binder = root.GetComponentInChildren<ControlBinder>();
            if (binder != null && binder.binding) binder.binding = false;
            else mainMenu.EnterTitle();
        }
    }

    private Selectable? InitialFocus(GameObject root, MultiplayerLobbyUi? lobby)
    {
        if (lobby != null && (root == lobby.resetSettingsDialog || root == lobby.crossServerDialog ||
            root == lobby.customLevelSuggestionDialog || root == lobby.customLevelBrowser?.deleteLocalPanel))
        {
            var cancel = CancelButton(root);
            if (cancel != null && Visible(cancel)) return cancel;
        }
        return Find(root, null, UiDirection.None);
    }

    private static Selectable? CancelButton(GameObject root)
    {
        foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>())
        {
            if (Eligible(button, root) && button.gameObject.name.TrimEnd('.').ToUpperInvariant() is "CANCEL" or "CLOSE" or "NO" or "BACK" or "DECLINE")
                return button;
        }
        return null;
    }

    private static bool Eligible(Selectable item, GameObject root) => item.isActiveAndEnabled && item.IsInteractable() &&
        item is not Scrollbar && (item.gameObject == root || item.transform.IsChildOf(root.transform));

    private Selectable? Find(GameObject root, Selectable? from, UiDirection direction)
    {
        var origin = from != null ? Bounds((RectTransform)from.transform).center : Vector2.zero;
        var vector = direction switch
        {
            UiDirection.Left => Vector2.left,
            UiDirection.Right => Vector2.right,
            UiDirection.Up => Vector2.up,
            UiDirection.Down => Vector2.down,
            _ => Vector2.zero
        };
        var scroll = from != null ? from.GetComponentInParent<ScrollRect>() : null;
        Selectable? best = null;
        float bestScore = float.PositiveInfinity;
        root.GetComponentsInChildren(false, candidates);
        foreach (var item in candidates)
        {
            if (item == from || !Eligible(item, root)) continue;
            var bounds = Bounds((RectTransform)item.transform);
            bool visible = Visible(item, bounds);
            if (!visible && (scroll == null || item.GetComponentInParent<ScrollRect>() != scroll)) continue;
            Vector2 point = bounds.center;
            float score = (Screen.height - point.y) * 2 + point.x * 0.1f;
            if (from != null)
            {
                Vector2 delta = point - origin;
                float forward = Vector2.Dot(delta, vector);
                if (forward <= 3) continue;
                float lateral = Mathf.Abs(Vector2.Dot(delta, new Vector2(-vector.y, vector.x)));
                score = forward + lateral * 2.25f;
                if (!visible) score += 20;
            }
            if (score >= bestScore) continue;
            bestScore = score;
            best = item;
        }
        return best;
    }

    private bool Visible(Selectable item) => Visible(item, Bounds((RectTransform)item.transform));

    private bool Visible(Selectable item, Rect bounds)
    {
        if (!bounds.Overlaps(new Rect(0, 0, Screen.width, Screen.height))) return false;
        var scroll = item.GetComponentInParent<ScrollRect>();
        return scroll == null || Bounds(Viewport(scroll)).Overlaps(bounds);
    }

    private Rect Bounds(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        rect.GetWorldCorners(corners);
        var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private void Focus(Selectable item, EventSystem events)
    {
        focus = item;
        events.SetSelectedGameObject(item is TMP_InputField or InputField ? null : item.gameObject);
        var scroll = item.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.content == null || !item.transform.IsChildOf(scroll.content)) return;
        var viewport = Viewport(scroll);
        var itemBounds = Bounds((RectTransform)item.transform);
        var view = Bounds(viewport);
        var content = Bounds(scroll.content);
        scroll.StopMovement();
        if (scroll.vertical && content.height > view.height)
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + Reveal(itemBounds.yMin, itemBounds.yMax, view.yMin, view.yMax) / (content.height - view.height));
        if (scroll.horizontal && content.width > view.width)
            scroll.horizontalNormalizedPosition = Mathf.Clamp01(scroll.horizontalNormalizedPosition + Reveal(itemBounds.xMin, itemBounds.xMax, view.xMin, view.xMax) / (content.width - view.width));
    }

    private static RectTransform Viewport(ScrollRect scroll) => scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;

    private static float Reveal(float min, float max, float viewportMin, float viewportMax) =>
        min < viewportMin ? min - viewportMin : max > viewportMax ? max - viewportMax : 0;

    private static bool AdjustSlider(Selectable item, UiDirection move)
    {
        if (item is not Slider slider) return false;
        bool vertical = slider.direction == Slider.Direction.BottomToTop || slider.direction == Slider.Direction.TopToBottom;
        if (vertical != (move == UiDirection.Up || move == UiDirection.Down)) return false;
        int sign = move == UiDirection.Up || move == UiDirection.Right ? 1 : -1;
        if (slider.direction == Slider.Direction.RightToLeft || slider.direction == Slider.Direction.TopToBottom) sign = -sign;
        slider.value += sign * (slider.wholeNumbers ? 1 : (slider.maxValue - slider.minValue) * 0.05f);
        return true;
    }

    private static bool IsEditing(Selectable item) => item is TMP_InputField tmp ? tmp.isFocused : item is InputField legacy && legacy.isFocused;

    private void OpenField(Selectable item)
    {
        focus = field = item;
        keyboard.Reset();
    }

    private void EditField(Pad pad, Func<Button, bool> pressed, bool back)
    {
        if (field == null) return;
        if (back)
        {
            if (field is TMP_InputField tmp) tmp.DeactivateInputField();
            else if (field is InputField legacy) legacy.DeactivateInputField();
            field = null;
            return;
        }

        char key = keyboard.Tick(pad, pressed);
        if (!IsEditing(field)) return;
        if (key == '↵')
        {
            var editing = field;
            field = null;
            if (editing is TMP_InputField tmpSubmit)
            {
                tmpSubmit.onSubmit.Invoke(tmpSubmit.text);
                tmpSubmit.DeactivateInputField();
            }
            else if (editing is InputField legacySubmit)
            {
                legacySubmit.onSubmit.Invoke(legacySubmit.text);
                legacySubmit.DeactivateInputField();
            }
            return;
        }

        KeyCode code = pressed(Button.L1) ? KeyCode.LeftArrow : pressed(Button.R1) ? KeyCode.RightArrow :
            key == '⌫' ? KeyCode.Backspace : KeyCode.None;
        if (key == '\0' && code == KeyCode.None) return;
        var input = new Event { type = EventType.KeyDown, keyCode = code, character = code == KeyCode.None ? key : '\0' };
        if (field is TMP_InputField tmpField)
        {
            tmpField.ProcessEvent(input);
            tmpField.ForceLabelUpdate();
        }
        else if (field is InputField legacyField)
        {
            legacyField.ProcessEvent(input);
            legacyField.ForceLabelUpdate();
        }
    }

    internal void Reset()
    {
        scope = null;
        controllerFocus = false;
        focus = field = null;
        repeat.Tick(default, Time.unscaledTime);
    }

    internal void Show(PadOverlay overlay)
    {
        if (!controllerFocus || focus == null || IsLevelSelect) { overlay.Hide(); return; }
        var bounds = Bounds((RectTransform)focus.transform);
        if (!Visible(focus, bounds)) { overlay.Hide(); return; }
        if (field != null)
        {
            keyboard.Show(overlay, bounds.center.y < Screen.height / 2f);
            return;
        }
        string hint = scope != null && GameManager.main != null && scope == GameManager.main.pauseMenu ? "   Triangle: Multiplayer" : "";
        overlay.ShowFocus(bounds, "D-pad / L stick: move   X: select   Circle: back" + hint);
    }
}
