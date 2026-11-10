using System;
using System.IO;
using System.Linq;
using JoyJoey.Game.Audio;
using JoyJoey.Game.Display;
using JoyJoey.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JoyJoey.Game.Editor
{
    /// <summary>Adds display options without rebuilding the player's scenes or existing menu layout.</summary>
    public static class DisplayBuilder
    {
        public const string RigPrefabPath = "Assets/JoyJoey/Resources/Game/DisplayRig.prefab";
        public const string OptionsPrefabPath = "Assets/JoyJoey/Prefabs/DisplayOptions.prefab";

        public static void EnsureAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Salí de Play y esperá a que Unity termine de importar para preparar Pantalla.");
            if (!File.Exists(RigPrefabPath) || !File.Exists(OptionsPrefabPath)) PrepareSceneAuthoring();
            AudioBuilder.EnsureAssets();
            CalibrationBuilder.EnsureAssets();
            EnsurePrefab(RigPrefabPath, () => new GameObject("DisplayRig", typeof(GameDisplay)));
            EnsurePrefab(OptionsPrefabPath, CreatePage);
            EnsureAdaptiveContent();
            UpgradeOptions(MainMenuBuilder.MenuPrefabPath, typeof(MainMenuController));
            UpgradeOptions(PauseMenuBuilder.PrefabPath, typeof(PauseMenuController));
            MenuNavigationBuilder.UpgradeExistingAssets();
        }
        static void PrepareSceneAuthoring()
        {
            var untitled = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .Where(scene => string.IsNullOrEmpty(scene.path)).ToArray();
            if (untitled.Length == 0) return;
            if (Application.isBatchMode && SceneManager.sceneCount == 1 && !untitled[0].isDirty && untitled[0].rootCount == 0 &&
                File.Exists(WorldBuilder.MenuScenePath))
            { EditorSceneManager.OpenScene(WorldBuilder.MenuScenePath, OpenSceneMode.Single); return; }
            throw new InvalidOperationException("Guardá la escena Sin título antes de crear Pantalla. No se modificó su contenido.");
        }
        static void EnsurePrefab(string path, Func<GameObject> create)
        {
            if (File.Exists(path)) return;
            var previous = SceneManager.GetActiveScene();
            var scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject root = null;
            try
            {
                SceneManager.SetActiveScene(scratch);
                root = create();
                if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new IOException("No se pudo guardar: " + path);
            }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scratch, true);
            }
        }
        static GameObject CreatePage()
        {
            var root = MenuUIFactory.Panel("DisplayOptions", null, MenuUIFactory.Ink);
            var page = root.AddComponent<DisplayOptionsController>();
            page.Content = CreateContent(root.transform);
            var content = page.Content;
            Label("Titulo", content, "PANTALLA", new Vector2(0, 355), new Vector2(1160, 72), 46, TextAnchor.MiddleCenter, MenuUIFactory.Orange);
            Label("Ayuda", content, "Aplicá los cambios y confirmá que la imagen se vea correctamente.", new Vector2(0, 302), new Vector2(1200, 40), 18, TextAnchor.MiddleCenter, MenuUIFactory.Muted);
            page.BasicButton = Button("Basico", content, "Básico", new Vector2(-180, 246), new Vector2(330, 48), 23);
            page.AdvancedButton = Button("Avanzado", content, "Avanzado", new Vector2(180, 246), new Vector2(330, 48), 23);
            page.BasicPanel = MenuUIFactory.Panel("Ajustes basicos", content, Color.clear);
            page.AdvancedPanel = MenuUIFactory.Panel("Ajustes avanzados", content, Color.clear);

            page.ModeDropdown = RowDropdown(page.BasicPanel.transform, "Modo de ventana", 165, new[] { "Ventana", "Pantalla completa sin bordes" });
            page.ResolutionDropdown = RowDropdown(page.BasicPanel.transform, "Resolución de ventana", 91, new[] { "1440 × 900" });
            page.FpsDropdown = RowDropdown(page.BasicPanel.transform, "Límite de FPS", 17, new[] { "Sin límite", "30", "60", "90", "120", "144", "165", "240" });
            page.VSyncDropdown = RowDropdown(page.BasicPanel.transform, "VSync", -57, new[] { "Desactivado", "Activado" });
            page.ResolutionInfo = Label("Resolucion nativa", page.BasicPanel.transform, "", new Vector2(0, -130), new Vector2(1100, 52), 21, TextAnchor.MiddleCenter, MenuUIFactory.Paper);
            page.VSyncInfo = Label("Prioridad VSync", page.BasicPanel.transform, "", new Vector2(0, -197), new Vector2(1100, 72), 18, TextAnchor.MiddleCenter, MenuUIFactory.Muted);

            page.RenderScaleDropdown = RowDropdown(page.AdvancedPanel.transform, "Resolución interna", 168, new[] { "Nativa · 100%", "75%", "67%", "50%" });
            page.RenderInfo = Label("Dimensiones internas", page.AdvancedPanel.transform, "", new Vector2(0, 112), new Vector2(1160, 50), 19, TextAnchor.MiddleCenter, MenuUIFactory.Paper);
            page.BrightnessSlider = ImageRow(page.AdvancedPanel.transform, "Brillo", 45, -.5f, .5f, 0, out page.BrightnessValue);
            page.ContrastSlider = ImageRow(page.AdvancedPanel.transform, "Contraste", -12, .5f, 1.5f, 1, out page.ContrastValue);
            page.GammaSlider = ImageRow(page.AdvancedPanel.transform, "Gamma", -69, .6f, 1.6f, 1, out page.GammaValue);
            var pattern = new GameObject("Patron de calibracion", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            pattern.transform.SetParent(page.AdvancedPanel.transform, false);
            Place(pattern.transform, new Vector2(0, -141), new Vector2(920, 44));
            pattern.raycastTarget = false;
            page.CalibrationPattern = pattern;
            Label("Ayuda calibracion", page.AdvancedPanel.transform, "Vista previa de imagen. Aplicá para comprobarla en el juego.", new Vector2(0, -186), new Vector2(1160, 40), 17, TextAnchor.MiddleCenter, MenuUIFactory.Muted);
            page.ResetImageButton = Button("Restaurar imagen", page.AdvancedPanel.transform, "Restaurar imagen", new Vector2(0, -244), new Vector2(360, 48), 21);

            page.ApplyButton = Button("Aplicar", content, "Aplicar", new Vector2(-360, -337), new Vector2(330, 56), 24);
            page.RevertButton = Button("Descartar cambios", content, "Descartar cambios", new Vector2(0, -337), new Vector2(330, 56), 24);
            page.BackButton = Button("Volver", content, "Volver", new Vector2(360, -337), new Vector2(330, 56), 24);
            page.StatusText = Label("Estado", content, "", new Vector2(0, -400), new Vector2(1200, 48), 18, TextAnchor.MiddleCenter, new Color(1, .7f, .5f));

            page.ConfirmPanel = MenuUIFactory.Panel("Confirmar pantalla", content, new Color(.015f, .018f, .035f, .96f));
            var card = MenuUIFactory.Panel("Tarjeta", page.ConfirmPanel.transform, new Color(.08f, .09f, .15f));
            Place(card.transform, Vector2.zero, new Vector2(880, 400));
            Label("Titulo", card.transform, "CONFIRMAR PANTALLA", new Vector2(0, 129), new Vector2(800, 70), 32, TextAnchor.MiddleCenter, MenuUIFactory.Orange);
            page.ConfirmationText = Label("Cuenta atras", card.transform, "", new Vector2(0, 25), new Vector2(780, 120), 25, TextAnchor.MiddleCenter, MenuUIFactory.Paper);
            page.KeepButton = Button("Conservar", card.transform, "Conservar", new Vector2(-200, -113), new Vector2(350, 60), 25);
            page.CancelPreviewButton = Button("Revertir", card.transform, "Revertir", new Vector2(200, -113), new Vector2(350, 60), 25);

            LinkVertical(page.ModeDropdown, page.ResolutionDropdown, page.FpsDropdown, page.VSyncDropdown, page.ApplyButton, page.RevertButton, page.BackButton, page.BasicButton, page.AdvancedButton);
            LinkVertical(page.RenderScaleDropdown, page.BrightnessSlider, page.ContrastSlider, page.GammaSlider, page.ResetImageButton);
            LinkHorizontal(page.BasicButton, page.AdvancedButton);
            LinkHorizontal(page.ApplyButton, page.RevertButton, page.BackButton);
            LinkVertical(page.KeepButton, page.CancelPreviewButton);
            LinkHorizontal(page.KeepButton, page.CancelPreviewButton);
            // Crossing tabs and panels remains possible without pointer input.
            LinkBelow(page.AdvancedButton, page.RenderScaleDropdown);
            LinkAbove(page.RenderScaleDropdown, page.AdvancedButton);
            LinkBelow(page.BasicButton, page.ModeDropdown);
            LinkAbove(page.ModeDropdown, page.BasicButton);
            LinkBelow(page.ResetImageButton, page.ApplyButton);
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
                if (!selectable.GetComponent<UiAudioFeedback>()) selectable.gameObject.AddComponent<UiAudioFeedback>();
            page.AdvancedPanel.SetActive(false);
            page.ConfirmPanel.SetActive(false);
            root.SetActive(false);
            return root;
        }
        static RectTransform CreateContent(Transform parent)
            => MenuUIFactory.Rect("Contenido adaptable", parent, Vector2.one * .5f, Vector2.one * .5f,
                Vector2.zero, new Vector2(1440, 900));

        static void EnsureAdaptiveContent()
        {
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(OptionsPrefabPath);
                var page = root.GetComponent<DisplayOptionsController>();
                if (!page || page.Content) return;
                var children = Enumerable.Range(0, root.transform.childCount).Select(root.transform.GetChild).ToArray();
                var content = CreateContent(root.transform);
                foreach (var child in children)
                {
                    // Reparent existing authored controls without replacing them or their references.
                    if (child is RectTransform rect)
                    {
                        var anchorMin = rect.anchorMin; var anchorMax = rect.anchorMax;
                        var pivot = rect.pivot; var position = rect.anchoredPosition3D; var size = rect.sizeDelta;
                        var scale = rect.localScale; var rotation = rect.localRotation;
                        rect.SetParent(content, false);
                        rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = pivot;
                        rect.sizeDelta = size; rect.anchoredPosition3D = position;
                        rect.localScale = scale; rect.localRotation = rotation;
                    }
                    else child.SetParent(content, false);
                }
                page.Content = content;
                PrefabUtility.SaveAsPrefabAsset(root, OptionsPrefabPath);
            }
            finally { if (root) PrefabUtility.UnloadPrefabContents(root); }
        }
        static Dropdown RowDropdown(Transform parent, string title, float y, string[] options)
        {
            Label(title + " etiqueta", parent, title, new Vector2(-405, y), new Vector2(330, 54), 24, TextAnchor.MiddleLeft, MenuUIFactory.Paper);
            return Dropdown(title, parent, new Vector2(185, y), new Vector2(690, 52), options);
        }
        static Slider ImageRow(Transform parent, string title, float y, float min, float max, float value, out Text valueLabel)
        {
            Label(title + " etiqueta", parent, title, new Vector2(-405, y), new Vector2(330, 48), 24, TextAnchor.MiddleLeft, MenuUIFactory.Paper);
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.name = title;
            go.transform.SetParent(parent, false);
            Place(go.transform, new Vector2(125, y), new Vector2(550, 38));
            var slider = go.GetComponent<Slider>();
            slider.minValue = min; slider.maxValue = max; slider.value = value;
            slider.fillRect.GetComponent<Image>().color = MenuUIFactory.Orange;
            slider.handleRect.GetComponent<Image>().color = MenuUIFactory.Paper;
            go.transform.Find("Background").GetComponent<Image>().color = new Color(.16f, .18f, .26f);
            Colors(slider);
            valueLabel = Label(title + " valor", parent, "1.00", new Vector2(469, y), new Vector2(110, 48), 23, TextAnchor.MiddleRight, MenuUIFactory.Paper);
            return slider;
        }
        static Dropdown Dropdown(string name, Transform parent, Vector2 position, Vector2 size, string[] options)
        {
            var go = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            go.name = name; go.transform.SetParent(parent, false); Place(go.transform, position, size);
            var dropdown = go.GetComponent<Dropdown>();
            dropdown.ClearOptions(); dropdown.AddOptions(options.ToList());
            foreach (var text in go.GetComponentsInChildren<Text>(true))
            { text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = 22; text.color = MenuUIFactory.Paper; }
            foreach (var image in go.GetComponentsInChildren<Image>(true)) image.color = new Color(.14f, .16f, .23f);
            dropdown.targetGraphic.color = Color.white;
            dropdown.template.sizeDelta = new Vector2(dropdown.template.sizeDelta.x, 240);
            var item = dropdown.itemText.transform.parent as RectTransform;
            if (item) item.sizeDelta = new Vector2(item.sizeDelta.x, 32);
            Colors(dropdown);
            var arrow = go.transform.Find("Arrow"); if (arrow) arrow.gameObject.SetActive(false);
            var glyph = Label("Abrir lista", go.transform, "▾", Vector2.zero, new Vector2(32, 42), 25, TextAnchor.MiddleCenter, MenuUIFactory.Paper);
            glyph.rectTransform.anchorMin = glyph.rectTransform.anchorMax = new Vector2(1, .5f);
            glyph.rectTransform.anchoredPosition = new Vector2(-21, 0);
            return dropdown;
        }
        static Button Button(string name, Transform parent, string label, Vector2 position, Vector2 size, int fontSize)
        {
            var button = MenuUIFactory.Button(name, parent, label);
            Place(button.transform, position, size);
            var text = button.GetComponentInChildren<Text>(); text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        static Text Label(string name, Transform parent, string text, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color color)
        {
            var label = MenuUIFactory.Text(name, parent, text, fontSize, alignment, color);
            Place(label.transform, position, size); return label;
        }
        static void Place(Transform transform, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }
        static void Colors(Selectable selectable)
        {
            var colors = selectable.colors; colors.normalColor = selectable is Slider ? MenuUIFactory.Paper : new Color(.2f, .22f, .3f);
            colors.highlightedColor = new Color(1, .65f, .4f); colors.selectedColor = MenuUIFactory.Orange;
            colors.pressedColor = new Color(1, .75f, .5f); colors.disabledColor = new Color(.14f, .15f, .2f); colors.fadeDuration = .08f;
            selectable.colors = colors;
        }
        static void LinkVertical(params Selectable[] values)
        {
            for (int i = 0; i < values.Length; i++) values[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnUp = values[(i + values.Length - 1) % values.Length], selectOnDown = values[(i + 1) % values.Length] };
        }
        static void LinkHorizontal(params Selectable[] values)
        {
            for (int i = 0; i < values.Length; i++)
            { var nav = values[i].navigation; nav.selectOnLeft = values[(i + values.Length - 1) % values.Length]; nav.selectOnRight = values[(i + 1) % values.Length]; values[i].navigation = nav; }
        }
        static void LinkBelow(Selectable first, Selectable second) { var nav = first.navigation; nav.selectOnDown = second; first.navigation = nav; }
        static void LinkAbove(Selectable first, Selectable second) { var nav = first.navigation; nav.selectOnUp = second; first.navigation = nav; }

        static void UpgradeOptions(string path, Type controllerType)
        {
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(path);
                var component = root.GetComponentInChildren(controllerType, true);
                if (!component) throw new InvalidOperationException("Falta el controlador en " + path);
                var serialized = new SerializedObject(component);
                var pageProperty = serialized.FindProperty("DisplayPage");
                var buttonProperty = serialized.FindProperty("DisplayButton");
                if (pageProperty == null || buttonProperty == null) throw new InvalidOperationException("Agregá DisplayPage y DisplayButton al controlador antes de preparar Pantalla.");
                bool changed = false;
                var parent = ((GameObject)serialized.FindProperty("OptionsPanel").objectReferenceValue).transform;
                if (!buttonProperty.objectReferenceValue)
                {
                    var button = Button("Pantalla", parent, "Pantalla", new Vector2(0, -51), new Vector2(480, 58), 25);
                    button.gameObject.AddComponent<UiAudioFeedback>();
                    buttonProperty.objectReferenceValue = button;
                    var audio = serialized.FindProperty("AudioButton").objectReferenceValue as Button;
                    var back = serialized.FindProperty(controllerType == typeof(MainMenuController) ? "OptionsBackButton" : "BackButton").objectReferenceValue as Button;
                    if (audio)
                    {
                        var rect = audio.GetComponent<RectTransform>();
                        // Only adjust the known generated location; an authored placement belongs to its author.
                        if (Mathf.Abs(rect.anchoredPosition.x) < .1f && Mathf.Abs(rect.anchoredPosition.y - -32) < 45)
                            rect.anchoredPosition = new Vector2(0, 29);
                    }
                    if (back)
                    {
                        var rect = back.GetComponent<RectTransform>();
                        if (Mathf.Abs(rect.anchoredPosition.x) < .1f && rect.anchoredPosition.y > -220 && rect.anchoredPosition.y < -110)
                            rect.anchoredPosition = new Vector2(0, -157);
                    }
                    if (audio && back) MenuUIFactory.LinkVertical(audio, button, back);
                    changed = true;
                }
                if (!pageProperty.objectReferenceValue)
                {
                    var page = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OptionsPrefabPath), parent);
                    page.SetActive(false);
                    pageProperty.objectReferenceValue = page.GetComponent<DisplayOptionsController>();
                    changed = true;
                }
                if (changed)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
            }
            finally { if (root) PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void BuildWindows()
        {
            EnsureAssets(); AudioBuilder.BuildWindows();
            File.Copy("Validation/stage3-build-result.txt", "Validation/stage6-build-result.txt", true);
        }
    }
}
