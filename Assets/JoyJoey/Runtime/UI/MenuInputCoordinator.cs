using System;
using System.Collections.Generic;
using System.Linq;
using JoyJoey.Sandbox;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace JoyJoey.Game.UI
{
    public enum MenuInputFamily { KeyboardMouse, Xbox, PlayStation }

    /// <summary>One active UI owner: neutral input on entry, one selection source and contextual actions.</summary>
    // UGUI's EventSystem has order -1000 in this package; source and gating must settle first.
    [DefaultExecutionOrder(-1100), DisallowMultipleComponent, RequireComponent(typeof(EventSystem))]
    public sealed class MenuInputCoordinator : MonoBehaviour
    {
        public GameObject HintRoot;
        public GameObject PointerShield;
        public Text HintLeft, HintRight, ApplyHint;
        public Button FooterBackButton, FooterConfirmButton, FooterApplyButton;
        public bool InputReady { get; private set; } = true;
        public Transform FocusScope { get; private set; }
        public Selectable DefaultSelection { get; private set; }
        public event Action BackRequested, ApplyRequested;
        public static MenuInputCoordinator Active { get; private set; }
        public static bool PointerMode { get; private set; } = true;
        public static bool GamepadMode { get; private set; }
        public static MenuInputFamily Family { get; private set; }
        public static bool BackPressedThisFrame => ReadyForActions && (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame || AnyPadButton(pad => pad.buttonEast.wasPressedThisFrame));
        public static bool ApplyPressedThisFrame => ReadyForActions && (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame || AnyPadButton(pad => pad.buttonWest.wasPressedThisFrame));
        static bool ReadyForActions => !Active || Active.InputReady;

        EventSystem _system;
        InputSystemUIInputModule _module;
        InputActionAsset _sourceActions, _ownedActions;
        bool _gate, _hintsWired, _preparingActions, _smokeDiagnostics, _controlsDefaults;
        float _configuredDeadzone;
        Vector2 _mousePosition;
        readonly Dictionary<int, Vector4> _padSticks = new Dictionary<int, Vector4>();
        GameObject _lastContentSelection;
        string _backLabel = "Volver", _confirmLabel = "Confirmar", _applyLabel = "Aplicar cambios";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            Active = null; PointerMode = true; GamepadMode = false; Family = MenuInputFamily.KeyboardMouse;
        }

        public static MenuInputCoordinator Ensure(EventSystem system)
        {
            if (!system) return null;
            return system.GetComponent<MenuInputCoordinator>() ?? system.gameObject.AddComponent<MenuInputCoordinator>();
        }
        void Awake()
        {
            _smokeDiagnostics = Array.IndexOf(Environment.GetCommandLineArgs(), "-uiSmoke") >= 0;
            Cache(); WireHints();
        }
        void OnEnable()
        {
            Cache();
            if (!Application.isPlaying) return;
            Active = this;
            GameplayControls.Changed += RefreshLeftStickConfiguration;
            RefreshLeftStickConfiguration();
            if (_ownedActions) _ownedActions.Enable();
            _mousePosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            RememberSticks();
            UpdatePointerShield();
            _system.sendNavigationEvents = InputReady && !PointerMode;
            Cursor.visible = PointerMode;
        }
        void Cache()
        {
            if (!_system) _system = GetComponent<EventSystem>();
            if (!_module) _module = GetComponent<InputSystemUIInputModule>();
            if (Application.isPlaying && !_ownedActions && !_preparingActions) PrepareOwnedActions();
        }
        void PrepareOwnedActions()
        {
            if (!_module || !_module.actionsAsset) return;
            _preparingActions = true;
            bool resume = _module.enabled;
            _module.enabled = false;
            _sourceActions = _module.actionsAsset;
            _ownedActions = Instantiate(_sourceActions);
            _ownedActions.name = _sourceActions.name + " · menu runtime";
            _ownedActions.hideFlags = HideFlags.DontSave;
            _configuredDeadzone = StickMenuInput.DeadzonePercent;
            StickMenuInput.Configure(_ownedActions);
            _module.actionsAsset = _ownedActions;
            // Each menu owns its own input state. Hiding a menu can cancel it without
            // changing another menu's bindings or its user's gameplay action maps.
            _ownedActions.Enable();
            if (resume) _module.enabled = true;
            _preparingActions = false;
        }

        void RefreshLeftStickConfiguration()
        {
            if (!_ownedActions) return;
            float percent = StickMenuInput.DeadzonePercent;
            if (Mathf.Approximately(percent, _configuredDeadzone)) return;
            bool toggleBinding = (percent >= 100) != (_configuredDeadzone >= 100);
            _configuredDeadzone = percent;
            RememberSticks();
            // Intermediate values are read live by the processor. At 100% the physical
            // control leaves navigation arbitration altogether, so a drifting pad cannot win.
            if (!toggleBinding)
            {
                var navigate = _ownedActions.FindAction("UI/Navigate");
                var control = navigate?.activeControl;
                if (control?.device is Gamepad pad && StickMenuInput.IsLeftStickControl(control, pad) &&
                    StickMenuInput.ReadLeftMenuVector(pad, percent) == Vector2.zero) navigate.Reset();
                return;
            }
            bool resume = _ownedActions.enabled;
            if (resume) _ownedActions.Disable();
            StickMenuInput.Configure(_ownedActions);
            if (resume) _ownedActions.Enable();
        }

        /// <summary>The opening press and every already-held direction must be released before menu actions arm.</summary>
        public void BeginReleaseGate()
        {
            Cache(); _gate = true; InputReady = false;
            UpdatePointerShield();
            if (_system) _system.sendNavigationEvents = false;
            // Cancel while callbacks are still hooked so the module forgets held navigation.
            if (_ownedActions) _ownedActions.Disable();
            if (_module) _module.enabled = false;
        }
        /// <summary>Retires a hidden menu while its callbacks can still clear held navigation.</summary>
        public void SuspendInput()
        {
            Cache(); _gate = false; InputReady = false;
            UpdatePointerShield();
            if (_system) _system.sendNavigationEvents = false;
            if (_ownedActions) _ownedActions.Disable();
            if (_module) _module.enabled = false;
        }
        void Update()
        {
            if (!Application.isPlaying || !_system || !_system.isActiveAndEnabled) return;
            TraceSmokeInput("before");
            Active = this;
            RememberSelection();
            DetectInputSource();
            UpdatePointerShield();
            RefreshHints();
            if (_gate)
            {
                if (AnyInputHeld()) return;
                _gate = false; InputReady = true;
                if (_ownedActions) _ownedActions.Enable();
                if (_module) _module.enabled = true;
                _system.sendNavigationEvents = !PointerMode;
                UpdatePointerShield();
            }
            TraceSmokeInput("after");
        }
        void LateUpdate() { if (Application.isPlaying) { RememberSelection(); TraceSmokeInput("late"); } }
        void TraceSmokeInput(string phase)
        {
            if (!_smokeDiagnostics || Keyboard.current == null || !Keyboard.current.jKey.wasPressedThisFrame) return;
            Debug.Log("UI INPUT " + phase + " " + DescribeInput());
        }
        public string DescribeInput()
        {
            var submit = _module ? _module.submit?.action : null;
            var ownedSubmit = _ownedActions ? _ownedActions.FindAction("UI/Submit") : null;
            return "frame=" + Time.frameCount + " system=" + (_system ? _system.name : "none") +
                " globalCurrent=" + (EventSystem.current == _system) +
                " moduleEnabled=" + (_module && _module.enabled) + " moduleActive=" + (_module && _module.isActiveAndEnabled) +
                " current=" + (_system && _system.currentInputModule == _module) + " sendNav=" + (_system && _system.sendNavigationEvents) +
                " ready=" + InputReady + " pointer=" + PointerMode + " selected=" + (_system && _system.currentSelectedGameObject ? _system.currentSelectedGameObject.name : "none") +
                " asset=" + (_module && _module.actionsAsset ? _module.actionsAsset.name + ":" + _module.actionsAsset.GetEntityId() : "none") +
                " ownedAsset=" + (_ownedActions ? _ownedActions.name + ":" + _ownedActions.GetEntityId() : "none") +
                " sourceAsset=" + (_sourceActions ? _sourceActions.name + ":" + _sourceActions.GetEntityId() : "none") +
                " submit=" + (submit == null ? "none" : submit.name + "/enabled:" + submit.enabled + "/phase:" + submit.phase + "/performed:" + submit.WasPerformedThisFrame() +
                    "/asset:" + (submit.actionMap?.asset ? submit.actionMap.asset.GetEntityId().ToString() : "none") +
                    "/bindings:" + string.Join(",", submit.bindings.Select(binding => binding.effectivePath)) +
                    "/controls:" + string.Join(",", submit.controls.Select(control => control.path))) +
                " sameOwned=" + ReferenceEquals(submit, ownedSubmit) + " ownedEnabled=" + (ownedSubmit != null && ownedSubmit.enabled) +
                " ownedPerformed=" + (ownedSubmit != null && ownedSubmit.WasPerformedThisFrame());
        }
        void RememberSelection()
        {
            var selection = _system ? _system.currentSelectedGameObject : null;
            if (selection && WithinScope(selection) && !selection.GetComponent<Scrollbar>() &&
                (!HintRoot || !selection.transform.IsChildOf(HintRoot.transform)))
                _lastContentSelection = selection;
        }
        public void SetFocusScope(Transform scope, Selectable defaultSelection)
        {
            Cache(); FocusScope = scope; DefaultSelection = defaultSelection;
            if (DefaultSelection && !WithinScope(DefaultSelection.gameObject)) DefaultSelection = null;
            if (_lastContentSelection && !WithinScope(_lastContentSelection)) _lastContentSelection = null;
            if (_system && _system.currentSelectedGameObject && !WithinScope(_system.currentSelectedGameObject))
                _system.SetSelectedGameObject(null);
            if (!PointerMode && _system && !ValidForScope(_system.currentSelectedGameObject)) RestoreNavigationSelection();
        }
        Transform EffectiveScope()
        {
            if (FocusScope) return FocusScope;
            var canvas = GetComponentInParent<Canvas>();
            return canvas ? canvas.transform : transform;
        }
        bool WithinScope(GameObject selected)
        {
            if (!selected) return false;
            var scope = EffectiveScope();
            return selected.transform == scope || selected.transform.IsChildOf(scope);
        }
        bool ValidForScope(GameObject selected)
            => ValidSelection(selected) && WithinScope(selected) && !selected.GetComponent<Scrollbar>() &&
                (!HintRoot || !selected.transform.IsChildOf(HintRoot.transform));
        void DetectInputSource()
        {
            bool mouseUsed = false;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var position = mouse.position.ReadValue();
                mouseUsed = (position - _mousePosition).sqrMagnitude > 4 || mouse.leftButton.wasPressedThisFrame ||
                    mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame ||
                    mouse.forwardButton.wasPressedThisFrame || mouse.backButton.wasPressedThisFrame || mouse.scroll.ReadValue().sqrMagnitude > 0;
                _mousePosition = position;
            }
            Gamepad usedPad = null;
            foreach (var pad in Gamepad.all)
            {
                var state = StickState(pad);
                bool stickChanged = !_padSticks.TryGetValue(pad.deviceId, out var before) ? state.sqrMagnitude > .09f :
                    (state - before).sqrMagnitude > .025f && state.sqrMagnitude > .09f;
                _padSticks[pad.deviceId] = state;
                if (PadPressed(pad) || stickChanged) usedPad = pad;
            }
            bool keyboardUsed = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
            // Fresh controller input takes precedence over the mouse's stationary position.
            if (usedPad != null)
                SetInputSource(false, true, DetectFamily(usedPad.description.product, usedPad.description.manufacturer, usedPad.layout));
            else if (keyboardUsed) SetInputSource(false, false, MenuInputFamily.KeyboardMouse);
            else if (mouseUsed)
            {
                SetInputSource(true, false, MenuInputFamily.KeyboardMouse);
                // A mouse hover is the only visible selection while using the pointer.
                if (_system.currentSelectedGameObject) _system.SetSelectedGameObject(null);
            }
        }
        void SetInputSource(bool pointer, bool gamepad, MenuInputFamily family)
        {
            bool changed = pointer != PointerMode;
            PointerMode = pointer; GamepadMode = gamepad; Family = family;
            _system.sendNavigationEvents = InputReady && !_gate && !pointer;
            Cursor.visible = pointer;
            UpdatePointerShield();
            if (!changed) return;
            if (!pointer) RestoreNavigationSelection();
        }
        void UpdatePointerShield()
        {
            EnsurePointerShield();
            if (PointerShield) PointerShield.SetActive(Application.isPlaying && InputReady && !_gate && !PointerMode);
        }
        void EnsurePointerShield()
        {
            if (PointerShield) return;
            var canvas = GetComponentInParent<Canvas>();
            if (!canvas) return;
            var rect = new GameObject("Bloqueo de puntero de navegación", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var overlay = rect.GetComponent<Canvas>(); overlay.overrideSorting = true; overlay.sortingOrder = 31000;
            var image = rect.GetComponent<Image>(); image.color = Color.clear; image.raycastTarget = true;
            PointerShield = rect.gameObject;
            PointerShield.SetActive(false);
            // A transparent raycast target gives UGUI a natural PointerExit, including over
            // newly opened dropdown canvases, without cycling the module or losing a Submit.
        }
        void RestoreNavigationSelection()
        {
            if (!_system) return;
            var scope = EffectiveScope();
            // Clicking a popup's scrollbar must not strand controller focus on that scrollbar.
            // Prefer the last focused option, or the current value when the popup was opened by mouse.
            foreach (var dropdown in scope.GetComponentsInChildren<Dropdown>())
            {
                var list = dropdown.transform.Find("Dropdown List");
                if (!list || !list.gameObject.activeInHierarchy) continue;
                if (ValidForScope(_lastContentSelection) && _lastContentSelection.GetComponent<Toggle>() && _lastContentSelection.transform.IsChildOf(list))
                { _system.SetSelectedGameObject(_lastContentSelection); return; }
                Toggle first = null;
                foreach (var toggle in list.GetComponentsInChildren<Toggle>())
                {
                    if (!ValidForScope(toggle.gameObject)) continue;
                    if (toggle.isOn) { _system.SetSelectedGameObject(toggle.gameObject); return; }
                    if (!first) first = toggle;
                }
                if (first) { _system.SetSelectedGameObject(first.gameObject); return; }
            }
            if (ValidForScope(_lastContentSelection)) { _system.SetSelectedGameObject(_lastContentSelection); return; }
            if (DefaultSelection && ValidForScope(DefaultSelection.gameObject)) { _system.SetSelectedGameObject(DefaultSelection.gameObject); return; }
            foreach (var selectable in scope.GetComponentsInChildren<Selectable>())
            {
                if (!ValidForScope(selectable.gameObject) || selectable.navigation.mode == Navigation.Mode.None) continue;
                _system.SetSelectedGameObject(selectable.gameObject); return;
            }
        }
        static bool ValidSelection(GameObject selected)
        {
            var control = selected ? selected.GetComponent<Selectable>() : null;
            return selected && selected.activeInHierarchy && control && control.isActiveAndEnabled && control.IsInteractable();
        }
        void RememberSticks()
        { foreach (var pad in Gamepad.all) _padSticks[pad.deviceId] = StickState(pad); }
        static Vector4 StickState(Gamepad pad)
        { var l = StickMenuInput.ReadLeftMenuVector(pad); var r = pad.rightStick.ReadValue(); return new Vector4(l.x, l.y, r.x, r.y); }
        static bool AnyPadButton(Func<Gamepad, bool> condition)
        { foreach (var pad in Gamepad.all) if (condition(pad)) return true; return false; }
        static bool PadPressed(Gamepad p) => p.buttonSouth.wasPressedThisFrame || p.buttonEast.wasPressedThisFrame ||
            p.buttonWest.wasPressedThisFrame || p.buttonNorth.wasPressedThisFrame || p.startButton.wasPressedThisFrame ||
            p.selectButton.wasPressedThisFrame || p.leftShoulder.wasPressedThisFrame || p.rightShoulder.wasPressedThisFrame ||
            p.leftTrigger.wasPressedThisFrame || p.rightTrigger.wasPressedThisFrame || p.leftStickButton.wasPressedThisFrame ||
            p.rightStickButton.wasPressedThisFrame || p.dpad.up.wasPressedThisFrame || p.dpad.down.wasPressedThisFrame ||
            p.dpad.left.wasPressedThisFrame || p.dpad.right.wasPressedThisFrame;
        public static bool AnyInputHeld()
            => StickMenuInput.AnyInputHeld();
        public static MenuInputFamily DetectFamily(string product, string manufacturer, string layout)
        {
            string identity = (product + " " + manufacturer + " " + layout).ToLowerInvariant();
            if (identity.Contains("xbox") || identity.Contains("xinput") || identity.Contains("microsoft")) return MenuInputFamily.Xbox;
            return identity.Contains("dualshock") || identity.Contains("dualsense") || identity.Contains("playstation") ||
                identity.Contains("sony") || identity.Contains("wireless controller") ? MenuInputFamily.PlayStation : MenuInputFamily.Xbox;
        }

        public void SetHints(bool visible, bool back, bool confirm, bool apply,
            string backLabel = "Volver", string confirmLabel = "Confirmar", string applyLabel = "Aplicar cambios", bool controlsDefaults = false)
        {
            EnsurePointerShield();
            EnsureHints();
            _backLabel = backLabel; _confirmLabel = confirmLabel; _applyLabel = applyLabel;
            _controlsDefaults = controlsDefaults;
            if (HintRoot) HintRoot.SetActive(visible);
            if (FooterBackButton) FooterBackButton.gameObject.SetActive(back);
            if (FooterConfirmButton) FooterConfirmButton.gameObject.SetActive(confirm);
            if (FooterApplyButton) FooterApplyButton.gameObject.SetActive(apply);
            RefreshHints();
        }
        void EnsureHints()
        {
            if (HintRoot) { WireHints(); return; }
            var canvas = GetComponentInParent<Canvas>();
            if (!canvas) return;
            var rect = new GameObject("Ayudas de navegación", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var overlay = rect.GetComponent<Canvas>(); overlay.overrideSorting = true; overlay.sortingOrder = canvas.sortingOrder + 25;
            HintRoot = rect.gameObject;
            FooterBackButton = HintButton(rect, "Atrás", false, 40, 350, out HintLeft);
            FooterConfirmButton = HintButton(rect, "Confirmar", true, -40, 250, out HintRight);
            FooterApplyButton = HintButton(rect, "Aplicar cambios", true, -320, 280, out ApplyHint);
            WireHints();
        }
        static Button HintButton(RectTransform parent, string name, bool right, float x, float width, out Text label)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(right ? 1 : 0, 0);
            rect.anchoredPosition = new Vector2(x, 20); rect.sizeDelta = new Vector2(width, 48);
            var image = rect.GetComponent<Image>(); image.color = Color.white;
            var button = rect.GetComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.clear; colors.highlightedColor = new Color(1, .45f, .24f, .2f);
            colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(1, .45f, .24f, .4f);
            button.colors = colors; button.navigation = new Navigation { mode = Navigation.Mode.None };
            var textRect = new GameObject("Tecla y acción", typeof(RectTransform), typeof(Text)).GetComponent<RectTransform>();
            textRect.SetParent(rect, false); textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 0); textRect.offsetMax = new Vector2(-10, 0);
            label = textRect.GetComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 22; label.color = new Color(.85f, .86f, .90f);
            label.alignment = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return button;
        }
        void WireHints()
        {
            if (_hintsWired || !FooterBackButton || !FooterConfirmButton || !FooterApplyButton) return;
            FooterBackButton.onClick.AddListener(() => { if (InputReady) BackRequested?.Invoke(); });
            FooterApplyButton.onClick.AddListener(() => { if (InputReady) ApplyRequested?.Invoke(); });
            FooterConfirmButton.onClick.AddListener(ConfirmSelected);
            _hintsWired = true;
        }
        void ConfirmSelected()
        {
            if (!InputReady || !_system || !ValidForScope(_lastContentSelection)) return;
            ExecuteEvents.Execute(_lastContentSelection, new BaseEventData(_system), ExecuteEvents.submitHandler);
        }
        void RefreshHints()
        {
            string back, confirm, apply;
            if (!GamepadMode) { back = "ESC"; confirm = "J"; apply = _controlsDefaults ? "H" : "K"; }
            else if (Family == MenuInputFamily.PlayStation)
            {
                bool glyphs = HintRight && HintRight.font && HintRight.font.HasCharacter('○') && HintRight.font.HasCharacter('×') && HintRight.font.HasCharacter('□');
                back = glyphs ? "○" : "Círculo"; confirm = glyphs ? "×" : "Cruz";
                apply = _controlsDefaults ? (ApplyHint && ApplyHint.font && ApplyHint.font.HasCharacter('△') ? "△" : "Triángulo") :
                    (glyphs ? "□" : "Cuadrado");
            }
            else { back = "B"; confirm = "A"; apply = _controlsDefaults ? "Y" : "X"; }
            if (HintLeft) HintLeft.text = back + "  " + _backLabel;
            if (HintRight) HintRight.text = confirm + "  " + _confirmLabel;
            if (ApplyHint) ApplyHint.text = apply + "  " + _applyLabel;
        }
        void OnDisable()
        {
            if (!Application.isPlaying) return;
            GameplayControls.Changed -= RefreshLeftStickConfiguration;
            if (_ownedActions) _ownedActions.Disable();
            if (PointerShield) PointerShield.SetActive(false);
            if (Active == this) { Active = null; Cursor.visible = true; }
        }
        void OnDestroy()
        {
            if (Application.isPlaying && PointerShield) Destroy(PointerShield);
            if (!_ownedActions) return;
            _ownedActions.Disable();
            if (_module && _module.actionsAsset == _ownedActions) _module.actionsAsset = _sourceActions;
            Destroy(_ownedActions);
        }
    }
}
