using System;
using System.Collections.Generic;
using JoyJoey.Game.Audio;
using JoyJoey.Sandbox;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace JoyJoey.Game.UI
{
    /// <summary>Gameplay bindings shared by the front-end and pause menus.</summary>
    [DefaultExecutionOrder(-1050), DisallowMultipleComponent]
    public sealed class ControlsOptionsController : MonoBehaviour
    {
        static readonly Color Ink = new Color(.055f, .06f, .095f);
        static readonly Color Paper = new Color(.94f, .94f, .96f);
        static readonly Color Muted = new Color(.68f, .71f, .79f);
        static readonly Color Accent = new Color(1, .45f, .24f);
        const float RowHeight = 48;
        const float RowSpacing = 56;

        readonly List<Selectable> _selectables = new List<Selectable>();
        readonly List<(GameplayControls.BindingRow Row, Text Name, Text Value)> _labels =
            new List<(GameplayControls.BindingRow, Text, Text)>();
        MenuInputCoordinator _input;
        ScrollRect _scroll;
        RectTransform _content;
        Text _device, _status, _deadzoneValue, _captureMessage, _captureCancel;
        Slider _deadzone;
        GameObject _captureLayer, _previousSelection;
        Font _font;
        Action _onBack;
        MenuInputFamily _family;
        GameplayControls.BindingRow _binding;
        int _captureFrame;

        public bool IsOpen => gameObject.activeInHierarchy;
        public bool IsCapturing { get; private set; }
        bool GamepadProfile => _family != MenuInputFamily.KeyboardMouse;

        internal static ControlsOptionsController AddTo(GameObject options, Button audio, Button display, Button back,
            Action open, out Button category)
        {
            category = null;
            if (!options || !display) return null;

            // Match the authored options and keep both existing prefab variants usable.
            category = Instantiate(display, options.transform, false);
            category.name = "Controles";
            var caption = category.GetComponentInChildren<Text>(true);
            if (caption) caption.text = "Controles";
            category.onClick = new Button.ButtonClickedEvent();
            category.onClick.AddListener(() => open());
            var categoryRect = (RectTransform)category.transform;
            float spacing = audio ? Mathf.Abs(((RectTransform)audio.transform).anchoredPosition.y - categoryRect.anchoredPosition.y) : 80;
            categoryRect.anchoredPosition += Vector2.down * Mathf.Max(categoryRect.rect.height + 10, spacing);
            if (back && back.gameObject.activeSelf)
                ((RectTransform)back.transform).anchoredPosition = categoryRect.anchoredPosition + Vector2.down * spacing;
            LinkVertical(new[] { audio, display, category, back });

            var root = new GameObject("Página Controles", typeof(RectTransform), typeof(Image));
            root.SetActive(false);
            Place((RectTransform)root.transform, options.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            root.GetComponent<Image>().color = Ink;
            var page = root.AddComponent<ControlsOptionsController>();
            page._font = caption && caption.font ? caption.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            page.CreateLayout();
            return page;
        }

        public void Open(MenuInputCoordinator input, Action onBack)
        {
            if (IsOpen) return;
            _input = input;
            _onBack = onBack;
            _status.text = "";
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            ShowProfile(MenuInputCoordinator.Family);
            SetMenuHints();
        }

        public void Close()
        {
            if (!gameObject.activeSelf) return;
            var back = _onBack;
            _onBack = null;
            gameObject.SetActive(false);
            back?.Invoke();
        }

        void OnEnable() => GameplayControls.Changed += RefreshValues;

        void OnDisable()
        {
            GameplayControls.Changed -= RefreshValues;
            _onBack = null;
            if (!IsCapturing) return;
            IsCapturing = false;
            _binding = default;
            _captureLayer.SetActive(false);
            if (_input && _input.isActiveAndEnabled) _input.BeginReleaseGate();
        }

        void Update()
        {
            if (IsCapturing) { UpdateCapture(); return; }
            var family = MenuInputCoordinator.Family;
            if (GamepadProfile != (family != MenuInputFamily.KeyboardMouse))
            {
                ShowProfile(family);
                return;
            }
            else if (_family != family)
            {
                _family = family;
                RefreshValues();
            }
            if (!_input || !_input.InputReady) return;
            if (GamepadProfile)
            {
                foreach (var pad in Gamepad.all)
                    if (pad.buttonNorth.wasPressedThisFrame) { RestoreShownDefaults(); break; }
            }
            else if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame) RestoreShownDefaults();
        }

        void LateUpdate()
        {
            if (MenuInputCoordinator.PointerMode || IsCapturing) return;
            var system = EventSystem.current;
            var selected = system ? system.currentSelectedGameObject : null;
            if (selected == _previousSelection) return;
            _previousSelection = selected;
            if (!selected || !selected.transform.IsChildOf(_content)) return;
            Canvas.ForceUpdateCanvases();
            MenuDropdown.EnsureVisible(_scroll, (RectTransform)selected.transform);
        }

        public void SetMenuHints()
        {
            if (_input) _input.SetHints(!IsCapturing, true, true, true, confirmLabel: "Editar", applyLabel: "Predeterminados", controlsDefaults: true);
        }

        public void RestoreShownDefaults()
        {
            if (IsCapturing || !_input || !_input.InputReady) return;
            _status.text = GameplayControls.RestoreDefaults(GamepadProfile, out string error)
                ? "Se restauraron los controles del dispositivo mostrado."
                : error;
            UiAudioFeedback.PlayConfirm();
        }

        void ShowProfile(MenuInputFamily family)
        {
            _family = family;
            _status.text = "";
            foreach (Transform row in _content)
            {
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            _selectables.Clear();
            _labels.Clear();
            _deadzone = null;
            _deadzoneValue = null;
            _previousSelection = null;

            // This shared setting is reachable with a keyboard/mouse even if the pad is drifting.
            CreateDeadzoneRow(0);
            int position = 1;
            foreach (var binding in GameplayControls.GetRows(GamepadProfile))
            {
                var row = Row(Caption(binding), position++, out var name);
                var value = Label("Asignación", row, "", 23, TextAnchor.MiddleRight, Paper,
                    new Vector2(.50f, 0), Vector2.one, Vector2.zero, new Vector2(-20, 0));
                _labels.Add((binding, name, value));
                if (!binding.IsEditable) continue;
                var button = row.gameObject.AddComponent<Button>();
                var image = row.GetComponent<Image>();
                image.color = Color.white;
                button.targetGraphic = image;
                var colors = SelectionColors();
                colors.normalColor = new Color(.13f, .15f, .22f);
                colors.highlightedColor = colors.selectedColor = new Color(.62f, .26f, .16f);
                button.colors = colors;
                button.onClick.AddListener(() => BeginCapture(binding));
                _selectables.Add(button);
            }

            _content.sizeDelta = new Vector2(0, position * RowSpacing);
            _content.anchoredPosition = Vector2.zero;
            _scroll.StopMovement();
            LinkVertical(_selectables);
            UiAudioFeedback.EnsureForCanvas(gameObject);
            if (_input) _input.SetFocusScope(transform, _selectables.Count > 1 ? _selectables[1] : _deadzone);
            RefreshValues();
        }

        static string Caption(GameplayControls.BindingRow row) => row.IsRevealed ? row.Name : "(UNUSED)";

        void RefreshValues()
        {
            if (_device)
                _device.text = _family == MenuInputFamily.PlayStation ? "Mando PlayStation" : GamepadProfile ? "Mando" : "Teclado y ratón";
            foreach (var label in _labels)
            {
                label.Name.text = Caption(label.Row);
                label.Value.text = GameplayControls.GetLabel(label.Row, _family == MenuInputFamily.PlayStation);
            }
            if (_deadzone)
            {
                float percent = GameplayControls.LeftStickDeadzonePercent;
                _deadzone.SetValueWithoutNotify(percent);
                _deadzoneValue.text = percent >= 100 ? "100% · Desactivado" : $"{percent:0}%";
            }
        }

        void BeginCapture(GameplayControls.BindingRow row)
        {
            if (IsCapturing || !_input.InputReady || !row.IsEditable) return;
            IsCapturing = true;
            _binding = row;
            _captureFrame = Time.frameCount;
            _captureMessage.text = "Pulsa un botón para asignarlo a esta acción";
            _captureCancel.text = GamepadProfile ? "Start · Cancelar" : "Esc · Cancelar";
            _captureLayer.SetActive(true);
            _captureLayer.transform.SetAsLastSibling();
            _input.SuspendInput();
            SetMenuHints();
        }

        void UpdateCapture()
        {
            if (Time.frameCount <= _captureFrame) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { EndCapture("Asignación cancelada."); return; }
            foreach (var pad in Gamepad.all)
                if (pad.startButton.wasPressedThisFrame) { EndCapture("Asignación cancelada."); return; }

            var button = FindPressedButton();
            if (button == null) return;
            if (GameplayControls.TryRebind(_binding, button, out string error)) EndCapture("Asignación guardada.");
            else _captureMessage.text = "Pulsa un botón para asignarlo a esta acción\n" + error;
        }

        ButtonControl FindPressedButton()
        {
            if (!GamepadProfile)
            {
                if (Keyboard.current != null)
                    foreach (var key in Keyboard.current.allKeys)
                        if (key.wasPressedThisFrame) return key;
                if (Mouse.current != null)
                {
                    var mouse = Mouse.current;
                    foreach (var control in mouse.allControls)
                        if (control is ButtonControl button && button.wasPressedThisFrame) return button;
                }
                return null;
            }
            foreach (var pad in Gamepad.all)
                foreach (var control in pad.allControls)
                    if (control is ButtonControl button && button.wasPressedThisFrame &&
                        !StickMenuInput.IsLeftStickControl(button, pad) && !IsWithin(button, pad.rightStick)) return button;
            return null;
        }

        static bool IsWithin(InputControl control, InputControl parent)
        {
            for (var current = control; current != null; current = current.parent)
                if (current == parent) return true;
            return false;
        }

        void EndCapture(string status)
        {
            IsCapturing = false;
            _binding = default;
            _captureLayer.SetActive(false);
            // Neither an assigned UI button nor the cancellation press may leak back to navigation.
            _input.BeginReleaseGate();
            SetMenuHints();
            _status.text = status;
            UiAudioFeedback.PlayConfirm();
        }

        void CreateLayout()
        {
            Label("Título", transform, "CONTROLES", 44, TextAnchor.MiddleCenter, Accent, new Vector2(.1f, .89f), new Vector2(.9f, .98f));
            _device = Label("Dispositivo", transform, "", 23, TextAnchor.MiddleCenter, Muted, new Vector2(.1f, .83f), new Vector2(.9f, .89f));
            var scrollRect = NewRect("Lista de controles", transform, new Vector2(.14f, .17f), new Vector2(.86f, .81f));
            _scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = RowSpacing;
            var viewport = NewRect("Área visible", scrollRect, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-24, 0));
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            _content = NewRect("Filas", viewport, Vector2.up, Vector2.one);
            _content.pivot = new Vector2(.5f, 1);
            _content.anchoredPosition = Vector2.zero;
            _scroll.viewport = viewport;
            _scroll.content = _content;
            var track = NewRect("Desplazamiento", scrollRect, Vector2.right, Vector2.one, new Vector2(-16, 0));
            track.gameObject.AddComponent<Image>().color = new Color(.12f, .14f, .20f);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            var slidingArea = NewRect("Carril", track, Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, -2));
            var handle = NewRect("Tirador", slidingArea, Vector2.zero, Vector2.one);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Accent;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _status = Label("Estado", transform, "", 20, TextAnchor.MiddleCenter, Muted, new Vector2(.12f, .08f), new Vector2(.88f, .16f));

            var layer = NewRect("Capturar asignación", transform, Vector2.zero, Vector2.one);
            layer.gameObject.AddComponent<Image>().color = new Color(.025f, .028f, .045f, .96f);
            _captureMessage = Label("Mensaje", layer, "", 28, TextAnchor.MiddleCenter, Paper, new Vector2(.17f, .44f), new Vector2(.83f, .59f));
            _captureCancel = Label("Cancelar", layer, "", 20, TextAnchor.MiddleCenter, Muted, new Vector2(.17f, .38f), new Vector2(.83f, .44f));
            _captureLayer = layer.gameObject;
            _captureLayer.SetActive(false);
        }

        void CreateDeadzoneRow(int index)
        {
            var row = Row("Zona muerta del stick izquierdo", index, out _);
            var rect = NewRect("Ajustar zona muerta", row, new Vector2(.44f, .14f), new Vector2(.77f, .86f));
            rect.gameObject.AddComponent<Image>().color = Color.clear;
            var fill = NewRect("Fondo", rect, new Vector2(0, .33f), new Vector2(1, .67f));
            var background = fill.gameObject.AddComponent<Image>();
            background.color = new Color(.27f, .29f, .36f);
            var slideArea = NewRect("Área del cursor", rect, Vector2.zero, Vector2.one, new Vector2(9, 0), new Vector2(-9, 0));
            var handle = NewRect("Cursor", slideArea, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(-8, -13), new Vector2(8, 13));
            var image = handle.gameObject.AddComponent<Image>();
            image.color = Color.white;
            _deadzone = rect.gameObject.AddComponent<Slider>();
            _deadzone.targetGraphic = image;
            _deadzone.handleRect = handle;
            _deadzone.colors = SelectionColors();
            _deadzone.maxValue = 100;
            _deadzone.wholeNumbers = true;
            _deadzoneValue = Label("Porcentaje", row, "", 20, TextAnchor.MiddleRight, Paper,
                new Vector2(.78f, 0), Vector2.one, Vector2.zero, new Vector2(-20, 0));
            _deadzone.onValueChanged.AddListener(value =>
            {
                if (!GameplayControls.TrySetLeftStickDeadzone(value, out string error))
                { _status.text = error; RefreshValues(); return; }
                _status.text = "Al 100%, la cruceta sigue disponible.";
            });
            _selectables.Add(_deadzone);
        }

        RectTransform Row(string caption, int index, out Text name)
        {
            float top = -index * RowSpacing;
            var row = NewRect(caption, _content, Vector2.up, Vector2.one, new Vector2(0, top - RowHeight), new Vector2(0, top));
            row.gameObject.AddComponent<Image>().color = new Color(.13f, .15f, .22f);
            name = Label("Acción", row, caption, 23, TextAnchor.MiddleLeft, Paper, Vector2.zero, new Vector2(.44f, 1), new Vector2(20, 0));
            return row;
        }

        Text Label(string name, Transform parent, string content, int size, TextAnchor alignment, Color color,
            Vector2 min, Vector2 max, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var text = NewRect(name, parent, min, max, offsetMin, offsetMax).gameObject.AddComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 15;
            text.resizeTextMaxSize = size;
            return text;
        }

        static RectTransform NewRect(string name, Transform parent, Vector2 min, Vector2 max,
            Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            Place(rect, parent, min, max, offsetMin, offsetMax);
            return rect;
        }

        static void Place(RectTransform rect, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
        }

        static ColorBlock SelectionColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.highlightedColor = new Color(1, .72f, .52f);
            colors.selectedColor = Accent;
            colors.pressedColor = new Color(.92f, .51f, .32f);
            return colors;
        }

        static void LinkVertical(IEnumerable<Selectable> controls)
        {
            var rows = new List<Selectable>();
            foreach (var control in controls)
                if (control && control.gameObject.activeSelf) rows.Add(control);
            for (int i = 0; i < rows.Count; i++)
                rows[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = rows[(i + rows.Count - 1) % rows.Count],
                    selectOnDown = rows[(i + 1) % rows.Count]
                };
        }
    }
}
