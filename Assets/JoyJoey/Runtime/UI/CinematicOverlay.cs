using System.Collections.Generic;
using JoyJoey.Game.Audio;
using JoyJoey.Game.Cinematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace JoyJoey.Game.UI
{
    /// <summary>Owns the pause and skip interface while a cutscene is playing.</summary>
    [DefaultExecutionOrder(-1050), DisallowMultipleComponent]
    public sealed class CinematicOverlay : MonoBehaviour
    {
        const float HintSeconds = 3, FadeSeconds = .25f;
        static readonly Color TextColor = new Color(.94f, .94f, .96f);
        static readonly Color Muted = new Color(.68f, .71f, .79f);
        static readonly Color Accent = new Color(1, .45f, .24f);
        static readonly Color ButtonNormal = new Color(.16f, .18f, .24f);
        static readonly Color ButtonFocus = new Color(.48f, .22f, .16f);
        static int _capturedThroughFrame = -1;

        public static bool CapturesInput => CutscenePlayer.Active || Time.frameCount <= _capturedThroughFrame;

        readonly Dictionary<int, int> _stickDirections = new Dictionary<int, int>();
        readonly Image[] _options = new Image[2];
        CutscenePlayer _player;
        GameObject _screen, _pausePanel;
        CanvasGroup _hint;
        InputPromptView _pauseHint, _confirmHint;
        MenuInputFamily _family;
        Vector2 _mousePosition;
        float _hintDeadline;
        bool _pointer, _previousCursorVisibility, _ownsCursor;
        int _selected, _pointerPress = -1, _hovered = -1, _ignoreThroughFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _capturedThroughFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var host = new GameObject("Joy Joey · Interfaz de cinemáticas");
            DontDestroyOnLoad(host);
            host.AddComponent<CinematicOverlay>();
        }

        void OnEnable()
        {
            CutscenePlayer.ActiveChanged += SetPlayer;
            SetPlayer(CutscenePlayer.Active);
        }

        void OnDisable()
        {
            CutscenePlayer.ActiveChanged -= SetPlayer;
            SetPlayer(null);
        }

        void SetPlayer(CutscenePlayer player)
        {
            if (_player) _player.StateChanged -= PlaybackStateChanged;
            _player = player;
            if (!player)
            {
                if (_screen) _screen.SetActive(false);
                if (_ownsCursor)
                {
                    Cursor.visible = _previousCursorVisibility;
                    _ownsCursor = false;
                    // A button that ended playback also belongs to the cutscene.
                    _capturedThroughFrame = Time.frameCount;
                }
                return;
            }

            if (!_screen) CreateLayout();
            if (!_ownsCursor)
            {
                _previousCursorVisibility = Cursor.visible;
                _ownsCursor = true;
            }
            _family = MenuInputCoordinator.Family;
            _pointer = MenuInputCoordinator.PointerMode;
            _mousePosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            _screen.SetActive(true);
            player.StateChanged += PlaybackStateChanged;
            PlaybackStateChanged();
        }

        void PlaybackStateChanged()
        {
            bool paused = _player && _player.IsPaused;
            _pausePanel.SetActive(paused);
            _hint.gameObject.SetActive(false);
            _hintDeadline = 0;
            _ignoreThroughFrame = Time.frameCount;
            _selected = 0;
            _pointerPress = _hovered = -1;
            _stickDirections.Clear();
            foreach (var pad in Gamepad.all) _stickDirections[pad.deviceId] = StickDirection(pad);
            Cursor.visible = paused && _pointer;
            RefreshPrompts();
            RefreshSelection();
        }

        void Update()
        {
            if (!_player) return;
            Cursor.visible = _player.IsPaused && _pointer;
            UpdateHint();
            if (Time.frameCount <= _ignoreThroughFrame) return;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool keyboardPause = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            bool keyboardUsed = KeyPressed(keyboard);
            bool mousePressed = MousePressed(mouse);
            Vector2 pointerPosition = mouse != null ? mouse.position.ReadValue() : _mousePosition;
            bool mouseUsed = mousePressed || (pointerPosition - _mousePosition).sqrMagnitude > 4;
            _mousePosition = pointerPosition;

            Gamepad usedPad = null, pausePad = null;
            bool padConfirm = false, padBack = false, padMove = false;
            foreach (var pad in Gamepad.all)
            {
                int direction = StickDirection(pad);
                bool moved = _stickDirections.TryGetValue(pad.deviceId, out int before) && direction != 0 && direction != before;
                _stickDirections[pad.deviceId] = direction;
                if (moved || MenuInputCoordinator.PadPressed(pad)) usedPad = pad;
                if (pad.startButton.wasPressedThisFrame) pausePad = pad;
                padConfirm |= pad.buttonSouth.wasPressedThisFrame;
                padBack |= pad.buttonEast.wasPressedThisFrame;
                padMove |= moved || pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame;
            }

            if (pausePad != null) SetInputSource(pausePad, false);
            else if (keyboardPause) SetInputSource(null, false);
            else if (usedPad != null) SetInputSource(usedPad, false);
            else if (keyboardUsed) SetInputSource(null, false);
            else if (mouseUsed) SetInputSource(null, true);

            if (keyboardPause || pausePad != null)
            {
                UiAudioFeedback.PlayConfirm();
                if (_player.IsPaused) _player.Resume();
                else _player.Pause();
                return;
            }

            if (!_player.IsPaused)
            {
                if (keyboardUsed || mousePressed || usedPad != null) ShowHint();
                return;
            }

            bool keyboardMove = keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame ||
                keyboard.wKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame);
            bool keyboardConfirm = keyboard != null && (keyboard.jKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame ||
                keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame);
            if (padBack) Activate(0);
            else if (padConfirm || keyboardConfirm) Activate(_selected);
            else if (padMove || keyboardMove)
            {
                _selected = 1 - _selected;
                RefreshSelection();
                UiAudioFeedback.PlayMove();
            }
            else if (_pointer && mouse != null) UpdatePointer(mouse);
        }

        static int StickDirection(Gamepad pad)
        {
            float y = StickMenuInput.ReadLeftMenuVector(pad).y;
            return y > .5f ? 1 : y < -.5f ? -1 : 0;
        }

        static bool KeyPressed(Keyboard keyboard)
        {
            if (keyboard == null) return false;
            foreach (var key in keyboard.allKeys)
                if (key.wasPressedThisFrame) return true;
            return false;
        }

        static bool MousePressed(Mouse mouse) => mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame ||
            mouse.middleButton.wasPressedThisFrame || mouse.forwardButton.wasPressedThisFrame || mouse.backButton.wasPressedThisFrame);

        void SetInputSource(Gamepad pad, bool pointer)
        {
            MenuInputFamily family = pad == null ? MenuInputFamily.KeyboardMouse :
                MenuInputCoordinator.DetectFamily(pad.description.product, pad.description.manufacturer, pad.layout);
            if (_family == family && _pointer == pointer) return;
            _family = family;
            _pointer = pointer;
            _hovered = _pointerPress = -1;
            Cursor.visible = _player.IsPaused && _pointer;
            RefreshPrompts();
            RefreshSelection();
        }

        void UpdatePointer(Mouse mouse)
        {
            int hovered = -1;
            for (int i = 0; i < _options.Length; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(_options[i].rectTransform, _mousePosition)) hovered = i;
            if (hovered != _hovered)
            {
                _hovered = hovered;
                RefreshSelection();
                if (hovered >= 0) UiAudioFeedback.PlayMove();
            }
            if (mouse.leftButton.wasPressedThisFrame) _pointerPress = hovered;
            if (!mouse.leftButton.wasReleasedThisFrame) return;
            bool click = hovered >= 0 && _pointerPress == hovered;
            _pointerPress = -1;
            if (click) Activate(hovered);
        }

        void Activate(int option)
        {
            UiAudioFeedback.PlayConfirm();
            if (option == 0) _player.Resume();
            else _player.Skip();
        }

        void RefreshSelection()
        {
            int focused = _pointer ? _hovered : _selected;
            for (int i = 0; i < _options.Length; i++) _options[i].color = i == focused ? ButtonFocus : ButtonNormal;
        }

        void RefreshPrompts()
        {
            bool keyboard = _family == MenuInputFamily.KeyboardMouse;
            _pauseHint.Set(keyboard ? "<Keyboard>/escape" : "<Gamepad>/start", _family, keyboard ? "Esc" : "Start", "Pausar cinemática");
            _confirmHint.Set(keyboard ? "<Keyboard>/j" : "<Gamepad>/buttonSouth", _family,
                keyboard ? "J" : _family == MenuInputFamily.PlayStation ? "Cruz" : "A", "Confirmar");
        }

        void ShowHint()
        {
            _hintDeadline = Time.unscaledTime + HintSeconds;
            _hint.alpha = 1;
            _hint.gameObject.SetActive(true);
        }

        void UpdateHint()
        {
            if (!_hint.gameObject.activeSelf) return;
            float alpha = Mathf.Clamp01(1 - (Time.unscaledTime - _hintDeadline) / FadeSeconds);
            _hint.alpha = alpha;
            if (alpha == 0) _hint.gameObject.SetActive(false);
        }

        void CreateLayout()
        {
            _screen = new GameObject("Cinemática", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _screen.transform.SetParent(transform, false);
            var canvas = _screen.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = _screen.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.matchWidthOrHeight = .5f;

            // Absorbs pointer events from other canvases. The two pause choices handle pointer
            // press/release locally and also work in a scene without an EventSystem.
            CreateImage("Bloqueo de puntero", _screen.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.clear).raycastTarget = true;

            var hint = CreateImage("Ayuda de pausa", _screen.transform, Vector2.right, Vector2.right,
                new Vector2(-338, 23), new Vector2(-24, 80), new Color(.045f, .05f, .075f, .9f));
            _hint = hint.gameObject.AddComponent<CanvasGroup>();
            _hint.blocksRaycasts = false;
            _pauseHint = InputPromptView.Attach(CreateText("Indicación", hint.transform, new Vector2(14, 0), new Vector2(-14, 0), "", 23, TextColor), 37);

            var shade = CreateImage("Pausa de cinemática", _screen.transform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Color(.025f, .027f, .046f, .85f));
            _pausePanel = shade.gameObject;
            var card = CreateImage("Opciones", shade.transform, Vector2.one * .5f, Vector2.one * .5f,
                new Vector2(-300, -178), new Vector2(300, 178), new Color(.067f, .074f, .107f, .98f));
            CreateText("Título", card.transform, new Vector2(0, 248), new Vector2(0, -22), "Paused", 55, Accent);
            _options[0] = CreateOption(card.transform, "Resume", 161);
            _options[1] = CreateOption(card.transform, "Skip Cutscene", 77);
            _confirmHint = InputPromptView.Attach(CreateText("Indicación", card.transform,
                new Vector2(18, 13), new Vector2(-18, -287), "", 21, Muted), 33);
        }

        static Image CreateOption(Transform parent, string title, float y)
        {
            var image = CreateImage(title, parent, Vector2.zero, Vector2.right,
                new Vector2(57, y), new Vector2(-57, y + 64), ButtonNormal);
            CreateText("Texto", image.transform, Vector2.zero, Vector2.zero, title, 29, TextColor);
            return image;
        }

        static Image CreateImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 min, Vector2 max, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            Place(image.rectTransform, parent, anchorMin, anchorMax, min, max);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text CreateText(string name, Transform parent, Vector2 min, Vector2 max, string content, int size, Color color)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            Place(text.rectTransform, parent, Vector2.zero, Vector2.one, min, max);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        static void Place(RectTransform rect, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 min, Vector2 max)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }
    }
}
