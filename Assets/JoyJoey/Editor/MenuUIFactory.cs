using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace JoyJoey.Game.Editor
{
    /// <summary>Small authoring helpers shared by the main menu and pause prefab.</summary>
    public static class MenuUIFactory
    {
        public const string UIActionsPath = "Assets/JoyJoey/Resources/JoyJoeyUIControls.inputactions";
        public static readonly Color Ink = new Color(.055f, .06f, .095f);
        public static readonly Color Paper = new Color(.94f, .94f, .96f);
        public static readonly Color Muted = new Color(.62f, .65f, .73f);
        public static readonly Color Orange = new Color(1, .45f, .24f);

        public static GameObject Canvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            return go;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static GameObject Panel(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = color.a > 0;
            return rect.gameObject;
        }

        public static Text Text(string name, Transform parent, string text, int fontSize, TextAnchor alignment, Color color)
        {
            var rect = Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var label = rect.gameObject.AddComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        public static Button Button(string name, Transform parent, string label)
        {
            var rect = Rect(name, parent, Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(480, 58));
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = new Color(.13f, .15f, .22f);
            colors.highlightedColor = new Color(.62f, .26f, .16f);
            colors.selectedColor = new Color(.62f, .26f, .16f);
            colors.pressedColor = new Color(.82f, .36f, .20f);
            colors.disabledColor = new Color(.10f, .11f, .15f, .65f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var text = Text("Etiqueta", rect, label, 26, TextAnchor.MiddleLeft, Paper);
            text.rectTransform.offsetMin = new Vector2(28, 0);
            text.rectTransform.offsetMax = new Vector2(-20, 0);
            return button;
        }

        public static EventSystem EventSystem(Transform parent)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(parent, false);
            var system = go.GetComponent<EventSystem>();
            system.sendNavigationEvents = true;
            var module = go.GetComponent<InputSystemUIInputModule>();
            AssignPersistentUIActions(module);
            module.deselectOnBackgroundClick = false;
            module.moveRepeatDelay = .35f;
            module.moveRepeatRate = .12f;
            return system;
        }

        static void AssignPersistentUIActions(InputSystemUIInputModule module)
        {
            // Setting actionsAsset remaps existing references into transient ScriptableObjects.
            // SwapAction then ignores a persistent reference to the same action. Clear first.
            module.point = null;
            module.leftClick = null;
            module.rightClick = null;
            module.middleClick = null;
            module.scrollWheel = null;
            module.move = null;
            module.submit = null;
            module.cancel = null;
            module.trackedDevicePosition = null;
            module.trackedDeviceOrientation = null;
            module.actionsAsset = EnsureUIActions();
            module.point = ActionReference("Point");
            module.leftClick = ActionReference("Click");
            module.rightClick = ActionReference("RightClick");
            module.middleClick = ActionReference("MiddleClick");
            module.scrollWheel = ActionReference("ScrollWheel");
            module.move = ActionReference("Navigate");
            module.submit = ActionReference("Submit");
            module.cancel = ActionReference("Cancel");
            module.trackedDevicePosition = ActionReference("TrackedDevicePosition");
            module.trackedDeviceOrientation = ActionReference("TrackedDeviceOrientation");
        }

        /// <summary>Repairs only missing/transient bindings in the generated UI, preserving its layout.</summary>
        public static void RepairMissingBindings(string prefabPath)
        {
            if (!File.Exists(prefabPath)) return;
            var actions = EnsureUIActions();
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                bool changed = false;
                foreach (var module in root.GetComponentsInChildren<InputSystemUIInputModule>(true))
                {
                    // A deliberately assigned alternative controls asset belongs to the author.
                    if (module.actionsAsset && module.actionsAsset != actions) continue;
                    if (!module.actionsAsset) { module.actionsAsset = actions; changed = true; }
                    changed |= RepairBinding(() => module.point, value => module.point = value, "Point");
                    changed |= RepairBinding(() => module.leftClick, value => module.leftClick = value, "Click");
                    changed |= RepairBinding(() => module.rightClick, value => module.rightClick = value, "RightClick");
                    changed |= RepairBinding(() => module.middleClick, value => module.middleClick = value, "MiddleClick");
                    changed |= RepairBinding(() => module.scrollWheel, value => module.scrollWheel = value, "ScrollWheel");
                    changed |= RepairBinding(() => module.move, value => module.move = value, "Navigate");
                    changed |= RepairBinding(() => module.submit, value => module.submit = value, "Submit");
                    changed |= RepairBinding(() => module.cancel, value => module.cancel = value, "Cancel");
                    changed |= RepairBinding(() => module.trackedDevicePosition, value => module.trackedDevicePosition = value, "TrackedDevicePosition");
                    changed |= RepairBinding(() => module.trackedDeviceOrientation, value => module.trackedDeviceOrientation = value, "TrackedDeviceOrientation");
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { if (root) PrefabUtility.UnloadPrefabContents(root); }
        }

        static bool RepairBinding(Func<InputActionReference> get, Action<InputActionReference> set, string actionName)
        {
            var existing = get();
            if (existing && AssetDatabase.Contains(existing)) return false;
            set(null);
            set(ActionReference(actionName));
            return true;
        }

        public static InputActionAsset EnsureUIActions()
        {
            var existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(UIActionsPath);
            if (existing) return existing;
            var source = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Packages/com.unity.inputsystem/InputSystem/Plugins/PlayerInput/DefaultInputActions.inputactions");
            if (!source) throw new InvalidOperationException("No se encontraron las acciones UI del paquete Input System.");
            var copy = InputActionAsset.FromJson(source.ToJson());
            try
            {
                copy.name = "JoyJoeyUIControls";
                foreach (var map in copy.actionMaps.ToArray())
                    if (map.name != "UI") copy.RemoveActionMap(map);
                // Keep the project's combat-adjacent keyboard convention for newly generated menus.
                var submit = copy.FindAction("UI/Submit", true);
                if (!submit.bindings.Any(binding => binding.path == "<Keyboard>/j")) submit.AddBinding("<Keyboard>/j", groups: "Keyboard&Mouse");
                File.WriteAllText(UIActionsPath, copy.ToJson());
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
            AssetDatabase.ImportAsset(UIActionsPath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(UIActionsPath);
        }

        static InputActionReference ActionReference(string actionName)
        {
            var reference = AssetDatabase.LoadAllAssetsAtPath(UIActionsPath).OfType<InputActionReference>()
                .FirstOrDefault(r => r.action != null && r.action.actionMap.name == "UI" && r.action.name == actionName);
            if (!reference) throw new InvalidOperationException("Falta la acción UI: " + actionName);
            return reference;
        }

        public static void LinkVertical(params Button[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (!buttons[i]) continue;
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnUp = buttons[(i + buttons.Length - 1) % buttons.Length];
                nav.selectOnDown = buttons[(i + 1) % buttons.Length];
                buttons[i].navigation = nav;
            }
        }
    }
}
