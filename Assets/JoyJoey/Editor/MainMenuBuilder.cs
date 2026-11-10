using System;
using System.IO;
using System.Linq;
using JoyJoey.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JoyJoey.Game.Editor
{
    /// <summary>Adds the front end to stage 1 without rebuilding authored levels or menu layouts.</summary>
    public static class MainMenuBuilder
    {
        public const string MenuPrefabPath = "Assets/JoyJoey/Prefabs/MenuPrincipalUI.prefab";

        public static void EnsureAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Salí de Play y esperá a que Unity termine de importar antes de preparar el menú.");
            bool needsAuthoring = !File.Exists(MenuPrefabPath) || !File.Exists(PauseMenuBuilder.PrefabPath) || NeedsMenuUpgrade() || NeedsBootstrapUpgrade();
            if (needsAuthoring) PrepareSceneAuthoring();
            WorldBuilder.EnsureAssets();
            EnsureMenuPrefab();
            PauseMenuBuilder.EnsureAssets();
            MenuUIFactory.RepairMissingBindings(MenuPrefabPath);
            MenuUIFactory.RepairMissingBindings(PauseMenuBuilder.PrefabPath);
            if (NeedsMenuUpgrade()) EditScene(WorldBuilder.MenuScenePath, AddMenuToScene);
            if (NeedsBootstrapUpgrade()) EditScene(WorldBuilder.BootstrapScenePath, UpgradeBootstrap);
            WorldBuilder.ConfigureBuildScenes();
        }

        static bool NeedsMenuUpgrade()
        {
            if (!File.Exists(WorldBuilder.MenuScenePath) || !File.Exists(MenuPrefabPath)) return true;
            string guid = AssetDatabase.AssetPathToGUID(MenuPrefabPath);
            return string.IsNullOrEmpty(guid) || !File.ReadAllText(WorldBuilder.MenuScenePath).Contains(guid);
        }

        static bool NeedsBootstrapUpgrade()
        {
            return File.Exists(WorldBuilder.BootstrapScenePath) &&
                File.ReadAllText(WorldBuilder.BootstrapScenePath).Contains("InitialSceneName: Nivel_Prototipo");
        }

        static void PrepareSceneAuthoring()
        {
            var untitled = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .Where(scene => string.IsNullOrEmpty(scene.path)).ToArray();
            if (untitled.Length == 0) return;
            if (Application.isBatchMode && SceneManager.sceneCount == 1 && !untitled[0].isDirty && untitled[0].rootCount == 0 &&
                File.Exists(WorldBuilder.MenuScenePath))
            {
                EditorSceneManager.OpenScene(WorldBuilder.MenuScenePath, OpenSceneMode.Single);
                return;
            }
            throw new InvalidOperationException("Guardá la escena Sin título antes de preparar el menú. No se modificó su contenido.");
        }

        static void EnsureMenuPrefab()
        {
            if (File.Exists(MenuPrefabPath)) return;
            Scene previous = SceneManager.GetActiveScene();
            Scene scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject root = null;
            try
            {
                SceneManager.SetActiveScene(scratch);
                root = CreateMenu();
                PrefabUtility.SaveAsPrefabAsset(root, MenuPrefabPath);
            }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scratch, true);
            }
        }

        static GameObject CreateMenu()
        {
            var root = MenuUIFactory.Canvas("MenuPrincipalUI");
            var controller = root.AddComponent<MainMenuController>();
            var background = MenuUIFactory.Panel("Fondo", root.transform, MenuUIFactory.Ink);
            var accent = MenuUIFactory.Panel("Acento", background.transform, MenuUIFactory.Orange).GetComponent<RectTransform>();
            accent.anchorMin = new Vector2(.5f, 1);
            accent.anchorMax = new Vector2(.5f, 1);
            accent.pivot = new Vector2(.5f, 1);
            accent.anchoredPosition = new Vector2(0, -58);
            accent.sizeDelta = new Vector2(72, 5);
            Place(MenuUIFactory.Text("Logo Joy Joey", root.transform, "JOY JOEY", 102, TextAnchor.MiddleCenter, MenuUIFactory.Orange),
                new Vector2(0, 266), new Vector2(1100, 150));
            controller.PressAnyPanel = MenuUIFactory.Panel("Pulsa cualquier boton", root.transform, Color.clear);
            Place(MenuUIFactory.Text("Invitacion", controller.PressAnyPanel.transform, "Pulsa cualquier botón para empezar", 30,
                TextAnchor.MiddleCenter, MenuUIFactory.Paper), new Vector2(0, -25), new Vector2(1100, 70));

            controller.MainPanel = MenuUIFactory.Panel("Opciones principales", root.transform, Color.clear);
            controller.StartButton = MenuUIFactory.Button("Empezar o continuar", controller.MainPanel.transform, "Empezar juego");
            controller.StartLabel = controller.StartButton.GetComponentInChildren<Text>();
            controller.SandboxButton = MenuUIFactory.Button("Circo onirico", controller.MainPanel.transform, "Circo onírico");
            controller.ExtrasButton = MenuUIFactory.Button("Extras", controller.MainPanel.transform, "Extras");
            controller.OptionsButton = MenuUIFactory.Button("Opciones", controller.MainPanel.transform, "Opciones");
            controller.QuitButton = MenuUIFactory.Button("Salir del juego", controller.MainPanel.transform, "Salir del juego");
            var mainButtons = new[] { controller.StartButton, controller.SandboxButton, controller.ExtrasButton, controller.OptionsButton, controller.QuitButton };
            for (int i = 0; i < mainButtons.Length; i++) mainButtons[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 100 - i * 72);
            MenuUIFactory.LinkVertical(mainButtons);
            Place(MenuUIFactory.Text("Ayuda de navegacion", controller.MainPanel.transform,
                "Flechas o stick: navegar    ·    Intro / botón inferior: aceptar", 18, TextAnchor.MiddleCenter, MenuUIFactory.Muted),
                new Vector2(0, -302), new Vector2(1200, 40));

            controller.ExtrasPanel = MenuUIFactory.Panel("Pagina Extras", root.transform, Color.clear);
            Place(MenuUIFactory.Text("Titulo", controller.ExtrasPanel.transform, "EXTRAS", 36, TextAnchor.MiddleCenter, MenuUIFactory.Paper),
                new Vector2(0, 90), new Vector2(1100, 70));
            Place(MenuUIFactory.Text("Contenido", controller.ExtrasPanel.transform, "Todavía no hay contenidos adicionales disponibles.", 25,
                TextAnchor.MiddleCenter, MenuUIFactory.Muted), new Vector2(0, -10), new Vector2(1000, 110));
            controller.ExtrasBackButton = MenuUIFactory.Button("Volver", controller.ExtrasPanel.transform, "Volver");
            controller.ExtrasBackButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -170);
            MenuUIFactory.LinkVertical(controller.ExtrasBackButton);

            controller.OptionsPanel = MenuUIFactory.Panel("Pagina Opciones", root.transform, Color.clear);
            Place(MenuUIFactory.Text("Titulo", controller.OptionsPanel.transform, "OPCIONES", 36, TextAnchor.MiddleCenter, MenuUIFactory.Paper),
                new Vector2(0, 90), new Vector2(1100, 70));
            Place(MenuUIFactory.Text("Contenido", controller.OptionsPanel.transform,
                "Los ajustes de audio y pantalla aún no están disponibles.", 25, TextAnchor.MiddleCenter, MenuUIFactory.Muted),
                new Vector2(0, -10), new Vector2(1000, 110));
            controller.OptionsBackButton = MenuUIFactory.Button("Volver", controller.OptionsPanel.transform, "Volver");
            controller.OptionsBackButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -170);
            MenuUIFactory.LinkVertical(controller.OptionsBackButton);

            controller.StatusLabel = MenuUIFactory.Text("Estado", root.transform, "", 20, TextAnchor.MiddleCenter, new Color(1, .66f, .45f));
            Place(controller.StatusLabel, new Vector2(0, -388), new Vector2(1180, 65));
            controller.EventSystem = MenuUIFactory.EventSystem(root.transform);
            controller.InputModule = controller.EventSystem.GetComponent<InputSystemUIInputModule>();
            controller.InputModule.enabled = false;
            controller.MainPanel.SetActive(false);
            controller.ExtrasPanel.SetActive(false);
            controller.OptionsPanel.SetActive(false);
            return root;
        }

        static void Place(Text text, Vector2 position, Vector2 size)
        {
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static bool AddMenuToScene(Scene scene)
        {
            if (scene.GetRootGameObjects().Any(r => r.GetComponentInChildren<MainMenuController>(true))) return false;
            if (scene.isDirty) throw new InvalidOperationException("Guardá los cambios de MenuPrincipal antes de agregar su interfaz.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath);
            PrefabUtility.InstantiatePrefab(prefab, scene);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var placeholder in root.GetComponentsInChildren<TextMesh>(true))
                    if ((placeholder.name == "Logo provisional" && placeholder.text == "JOY JOEY") ||
                        (placeholder.name == "Marcador" && placeholder.text == "MENÚ PRINCIPAL"))
                        placeholder.gameObject.SetActive(false);
            return true;
        }

        static bool UpgradeBootstrap(Scene scene)
        {
            var bootstrap = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameBootstrap>(true)).FirstOrDefault();
            if (!bootstrap || bootstrap.InitialSceneName != "Nivel_Prototipo") return false;
            if (scene.isDirty) throw new InvalidOperationException("Guardá los cambios de Arranque antes de conectar el menú.");
            bootstrap.InitialSceneName = "MenuPrincipal";
            return true;
        }

        static void EditScene(string path, Func<Scene, bool> edit)
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                if (edit(scene) && !EditorSceneManager.SaveScene(scene, path))
                    throw new IOException("No se pudo guardar la escena: " + path);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Joy Joey/Escenas/Menú principal", false, 1)]
        public static void OpenMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(WorldBuilder.MenuScenePath)) EnsureAssets();
            EditorSceneManager.OpenScene(WorldBuilder.MenuScenePath);
        }

        public static void BuildWindows()
        {
            EnsureAssets();
            WorldBuilder.BuildWindows();
            File.Copy("Validation/stage1-build-result.txt", "Validation/stage2-build-result.txt", true);
        }
    }
}
