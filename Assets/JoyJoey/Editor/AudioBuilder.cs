using System;
using System.IO;
using System.Linq;
using JoyJoey.Game.Audio;
using JoyJoey.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JoyJoey.Game.Editor
{
    /// <summary>Creates missing audio assets and adds the reusable audio page to authored menus.</summary>
    public static class AudioBuilder
    {
        public const string MixerPath = "Assets/JoyJoey/Audio/JoyJoeyAudio.mixer";
        public const string RigPrefabPath = "Assets/JoyJoey/Resources/Game/AudioRig.prefab";
        public const string OptionsPrefabPath = "Assets/JoyJoey/Prefabs/AudioOptions.prefab";
        public const string ClipsPath = "Assets/JoyJoey/Audio/Prototype";

        [MenuItem("Joy Joey/Etapa 3/Preparar audio y opciones")]
        public static void EnsureAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Salí de Play y esperá a que Unity termine de importar para preparar el audio.");
            bool needsObjects = !File.Exists(RigPrefabPath) || !File.Exists(OptionsPrefabPath);
            if (needsObjects) PrepareSceneAuthoring();
            MainMenuBuilder.EnsureAssets();
            EnsureFolder("Assets/JoyJoey/Audio");
            EnsureFolder(ClipsPath);
            EnsureFolder("Assets/JoyJoey/Resources/Game");
            if (!File.Exists(MixerPath)) AudioMixerAuthoring.Create(MixerPath);
            EnsureClip("TestMusic.wav", 2, 0);
            EnsureClip("TestSfx.wav", .25f, 1);
            EnsureClip("TestUi.wav", .12f, 2);
            EnsureClip("UiMove.wav", .045f, 3);
            EnsureClip("UiConfirm.wav", .09f, 4);
            EnsurePrefab(RigPrefabPath, CreateRig);
            EnsurePrefab(OptionsPrefabPath, CreateAudioPage);
            UpgradeOptions(MainMenuBuilder.MenuPrefabPath, typeof(MainMenuController));
            UpgradeOptions(PauseMenuBuilder.PrefabPath, typeof(PauseMenuController));
        }

        static void PrepareSceneAuthoring()
        {
            var untitled = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .Where(scene => string.IsNullOrEmpty(scene.path)).ToArray();
            if (untitled.Length == 0) return;
            if (Application.isBatchMode && SceneManager.sceneCount == 1 && !untitled[0].isDirty && untitled[0].rootCount == 0 &&
                File.Exists(WorldBuilder.MenuScenePath))
            { EditorSceneManager.OpenScene(WorldBuilder.MenuScenePath, OpenSceneMode.Single); return; }
            throw new InvalidOperationException("Guardá la escena Sin título antes de crear los prefabs de audio. No se modificó su contenido.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static void EnsurePrefab(string path, Func<GameObject> create)
        {
            if (File.Exists(path)) return;
            Scene previous = SceneManager.GetActiveScene();
            Scene scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
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

        static GameObject CreateRig()
        {
            var root = new GameObject("AudioRig");
            var audio = root.AddComponent<GameAudio>();
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            audio.Mixer = mixer;
            audio.MasterGroup = Group(mixer, "Master");
            audio.MusicGroup = Group(mixer, "MusicEffects");
            audio.SfxGroup = Group(mixer, "SfxEffects");
            audio.UiGroup = Group(mixer, "UiEffects");
            audio.NormalSnapshot = mixer.FindSnapshot("Normal");
            audio.PauseSnapshot = mixer.FindSnapshot("Pause");
            audio.CutsceneSnapshot = mixer.FindSnapshot("Cutscene");
            audio.TestMusicClip = Clip("TestMusic.wav");
            audio.TestSfxClip = Clip("TestSfx.wav");
            audio.TestUiClip = Clip("TestUi.wav");
            audio.UiMoveClip = Clip("UiMove.wav");
            audio.UiConfirmClip = Clip("UiConfirm.wav");
            return root;
        }
        static AudioMixerGroup Group(AudioMixer mixer, string name)
            => mixer.FindMatchingGroups("").First(group => group.name == name);
        static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(ClipsPath + "/" + name);

        static GameObject CreateAudioPage()
        {
            var root = MenuUIFactory.Panel("AudioOptions", null, MenuUIFactory.Ink);
            var controller = root.AddComponent<AudioOptionsController>();
            Label("Titulo", root.transform, "AUDIO", new Vector2(0, 346), new Vector2(1160, 80), 48, TextAnchor.MiddleCenter, MenuUIFactory.Orange);
            Label("Ayuda", root.transform, "Los cambios se guardan automáticamente.", new Vector2(0, 294), new Vector2(1160, 40), 18, TextAnchor.MiddleCenter, MenuUIFactory.Muted);
            controller.MasterSlider = VolumeRow(root.transform, "General", 221, out controller.MasterValue);
            controller.MusicSlider = VolumeRow(root.transform, "Música", 161, out controller.MusicValue);
            controller.SfxSlider = VolumeRow(root.transform, "Efectos", 101, out controller.SfxValue);
            controller.UiSlider = VolumeRow(root.transform, "Interfaz", 41, out controller.UiValue);
            Label("Etiqueta formato", root.transform, "Formato", new Vector2(-394, -43), new Vector2(255, 50), 24, TextAnchor.MiddleLeft, MenuUIFactory.Paper);
            controller.SpeakerModeDropdown = Dropdown("Formato", root.transform, new Vector2(140, -43), new Vector2(620, 50), new[] { "Estéreo", "Mono" });
            Label("Etiqueta dispositivo", root.transform, "Salida", new Vector2(-394, -111), new Vector2(255, 50), 24, TextAnchor.MiddleLeft, MenuUIFactory.Paper);
            controller.OutputDeviceDropdown = Dropdown("Dispositivo de salida", root.transform, new Vector2(58, -111), new Vector2(455, 50), new[] { "Predeterminado del sistema" });
            controller.RefreshDevicesButton = Button("Actualizar dispositivos", root.transform, "Actualizar", new Vector2(370, -111), new Vector2(170, 50), 20);
            controller.TestMusicButton = Button("Probar musica", root.transform, "Música: probar / detener", new Vector2(-345, -211), new Vector2(320, 54), 22);
            controller.TestSfxButton = Button("Probar efectos", root.transform, "Probar efectos", new Vector2(0, -211), new Vector2(320, 54), 22);
            controller.TestUiButton = Button("Probar interfaz", root.transform, "Probar interfaz", new Vector2(345, -211), new Vector2(320, 54), 22);
            controller.DefaultsButton = Button("Restaurar volumenes", root.transform, "Restaurar volúmenes", new Vector2(-177, -297), new Vector2(330, 56), 23);
            controller.BackButton = Button("Volver", root.transform, "Volver", new Vector2(177, -297), new Vector2(330, 56), 23);
            controller.StatusText = Label("Estado", root.transform, "", new Vector2(0, -376), new Vector2(1180, 76), 18, TextAnchor.MiddleCenter, new Color(1, .7f, .5f));
            LinkControls(new Selectable[] { controller.MasterSlider, controller.MusicSlider, controller.SfxSlider, controller.UiSlider,
                controller.SpeakerModeDropdown, controller.OutputDeviceDropdown, controller.RefreshDevicesButton,
                controller.TestMusicButton, controller.TestSfxButton, controller.TestUiButton, controller.DefaultsButton, controller.BackButton });
            LinkHorizontal(controller.TestMusicButton, controller.TestSfxButton, controller.TestUiButton);
            LinkHorizontal(controller.DefaultsButton, controller.BackButton);
            root.SetActive(false);
            return root;
        }

        static Slider VolumeRow(Transform parent, string title, float y, out Text valueLabel)
        {
            Label(title + " etiqueta", parent, title, new Vector2(-394, y), new Vector2(255, 50), 25, TextAnchor.MiddleLeft, MenuUIFactory.Paper);
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.name = title + " volumen";
            go.transform.SetParent(parent, false);
            Place(go.transform, new Vector2(58, y), new Vector2(455, 40));
            var slider = go.GetComponent<Slider>();
            slider.minValue = 0; slider.maxValue = 1; slider.value = 1;
            slider.fillRect.GetComponent<Image>().color = MenuUIFactory.Orange;
            var background = go.transform.Find("Background").GetComponent<Image>();
            background.color = new Color(.16f, .18f, .26f);
            slider.handleRect.GetComponent<Image>().color = MenuUIFactory.Paper;
            SetColors(slider);
            valueLabel = Label(title + " valor", parent, "100%", new Vector2(373, y), new Vector2(160, 50), 25, TextAnchor.MiddleRight, MenuUIFactory.Paper);
            return slider;
        }
        static Dropdown Dropdown(string name, Transform parent, Vector2 position, Vector2 size, string[] options)
        {
            var go = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent, false);
            Place(go.transform, position, size);
            var dropdown = go.GetComponent<Dropdown>();
            dropdown.ClearOptions();
            dropdown.AddOptions(options.ToList());
            foreach (var text in go.GetComponentsInChildren<Text>(true))
            {
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 21;
                text.color = MenuUIFactory.Paper;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            foreach (var image in go.GetComponentsInChildren<Image>(true)) image.color = new Color(.14f, .16f, .23f);
            dropdown.targetGraphic.color = Color.white;
            dropdown.template.GetComponent<Image>().color = new Color(.12f, .14f, .21f);
            dropdown.template.sizeDelta = new Vector2(dropdown.template.sizeDelta.x, 240);
            SetColors(dropdown);
            var arrow = go.transform.Find("Arrow");
            if (arrow) arrow.gameObject.SetActive(false);
            var glyph = Label("Abrir lista", go.transform, "▾", Vector2.zero, new Vector2(30, 40), 25, TextAnchor.MiddleCenter, MenuUIFactory.Paper);
            glyph.rectTransform.anchorMin = glyph.rectTransform.anchorMax = new Vector2(1, .5f);
            glyph.rectTransform.anchoredPosition = new Vector2(-21, 0);
            return dropdown;
        }
        static Button Button(string name, Transform parent, string label, Vector2 position, Vector2 size, int fontSize)
        {
            var button = MenuUIFactory.Button(name, parent, label);
            Place(button.transform, position, size);
            var text = button.GetComponentInChildren<Text>();
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        static Text Label(string name, Transform parent, string text, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color color)
        {
            var label = MenuUIFactory.Text(name, parent, text, fontSize, alignment, color);
            Place(label.transform, position, size);
            return label;
        }
        static void Place(Transform transform, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
        static void SetColors(Selectable selectable)
        {
            var colors = selectable.colors;
            colors.normalColor = selectable is Slider ? MenuUIFactory.Paper : new Color(.2f, .22f, .3f);
            colors.highlightedColor = new Color(1, .65f, .4f);
            colors.selectedColor = MenuUIFactory.Orange;
            colors.pressedColor = new Color(1, .75f, .5f);
            colors.disabledColor = new Color(.14f, .15f, .2f);
            colors.colorMultiplier = 1;
            colors.fadeDuration = .08f;
            selectable.colors = colors;
        }
        static void LinkControls(Selectable[] controls)
        {
            for (int i = 0; i < controls.Length; i++)
                controls[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = controls[(i + controls.Length - 1) % controls.Length], selectOnDown = controls[(i + 1) % controls.Length] };
        }
        static void LinkHorizontal(params Selectable[] controls)
        {
            for (int i = 0; i < controls.Length; i++)
            {
                var nav = controls[i].navigation;
                nav.selectOnLeft = controls[(i + controls.Length - 1) % controls.Length];
                nav.selectOnRight = controls[(i + 1) % controls.Length];
                controls[i].navigation = nav;
            }
        }

        static void UpgradeOptions(string prefabPath, Type controllerType)
        {
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                var controller = root.GetComponentInChildren(controllerType, true);
                var serialized = new SerializedObject(controller);
                var audioProperty = serialized.FindProperty("AudioPage");
                var buttonProperty = serialized.FindProperty("AudioButton");
                if (audioProperty == null || buttonProperty == null)
                    throw new InvalidOperationException("El controlador del menú debe exponer AudioPage y AudioButton.");
                if (audioProperty.objectReferenceValue && buttonProperty.objectReferenceValue) return;
                var options = (GameObject)serialized.FindProperty("OptionsPanel").objectReferenceValue;
                foreach (var text in options.GetComponentsInChildren<Text>(true))
                    if (text.name == "Contenido" && text.text.Contains("ajustes de audio y pantalla")) text.gameObject.SetActive(false);
                var audio = options.GetComponentInChildren<AudioOptionsController>(true);
                if (!audio)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OptionsPrefabPath), options.transform);
                    audio = instance.GetComponent<AudioOptionsController>();
                    var rect = instance.GetComponent<RectTransform>();
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                    instance.SetActive(false);
                }
                var audioButton = buttonProperty.objectReferenceValue as Button;
                if (!audioButton) audioButton = Button("Audio", options.transform, "Audio", new Vector2(0, -25), new Vector2(480, 58), 26);
                var back = serialized.FindProperty(controllerType == typeof(MainMenuController) ? "OptionsBackButton" : "BackButton").objectReferenceValue as Button;
                if (back) MenuUIFactory.LinkVertical(audioButton, back);
                audioProperty.objectReferenceValue = audio;
                buttonProperty.objectReferenceValue = audioButton;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                audio.transform.SetAsLastSibling();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { if (root) PrefabUtility.UnloadPrefabContents(root); }
        }

        static void EnsureClip(string name, float duration, int kind)
        {
            string path = ClipsPath + "/" + name;
            if (File.Exists(path)) return;
            const int rate = 48000;
            int count = Mathf.RoundToInt(duration * rate);
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 4);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4);
                writer.Write((short)4); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 4);
                for (int i = 0; i < count; i++)
                {
                    double t = i / (double)rate;
                    double local = kind == 0 ? t % .5 : t;
                    double noteLength = kind == 0 ? .5 : duration;
                    double envelope = Math.Min(1, local / .008) * Math.Min(1, (noteLength - local) / .035);
                    double frequency = kind == 0 ? new[] { 261.6256, 329.6276, 391.9954, 440.0 }[Math.Min(3, (int)(t / .5))] :
                        kind == 1 ? 240 + 720 * (1 - t / duration) : kind == 3 ? 660 : 880;
                    double amplitude = kind == 0 ? .12 : kind == 3 ? .055 : .14;
                    double left = amplitude * envelope * Math.Sin(t * Math.PI * 2 * frequency);
                    double right = amplitude * envelope * Math.Sin(t * Math.PI * 2 * (kind == 0 ? frequency * 1.5 : frequency));
                    writer.Write((short)Math.Round(Math.Max(-1, Math.Min(1, left)) * short.MaxValue));
                    writer.Write((short)Math.Round(Math.Max(-1, Math.Min(1, right)) * short.MaxValue));
                }
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.SaveAndReimport();
        }

        [MenuItem("Joy Joey/Etapa 3/Compilar juego Windows")]
        public static void BuildWindows()
        {
            EnsureAssets();
            MainMenuBuilder.BuildWindows();
            File.Copy("Validation/stage2-build-result.txt", "Validation/stage3-build-result.txt", true);
        }
    }
}
