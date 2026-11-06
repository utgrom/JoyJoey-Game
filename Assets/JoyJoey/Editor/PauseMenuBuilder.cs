using System;
using System.IO;
using JoyJoey.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JoyJoey.Game.Editor
{
    /// <summary>Authoring of the reusable, serialized pause overlay.</summary>
    public static class PauseMenuBuilder
    {
        public const string PrefabPath = "Assets/JoyJoey/Resources/Game/PauseUI.prefab";
        public const string ResourcePath = "Game/PauseUI";

        public static void EnsureAssets()
        {
            if (File.Exists(PrefabPath)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Salí de Play antes de preparar la pausa.");
            EnsureFolder("Assets/JoyJoey/Resources/Game");
            var previous = SceneManager.GetActiveScene();
            Scene scratch = default;
            GameObject root = null;
            try
            {
                scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scratch);
                root = CreatePrefab();
                if (!PrefabUtility.SaveAsPrefabAsset(root, PrefabPath))
                    throw new IOException("No se pudo guardar el prefab de pausa.");
            }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (scratch.IsValid() && scratch.isLoaded) EditorSceneManager.CloseScene(scratch, true);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static GameObject CreatePrefab()
        {
            var root = MenuUIFactory.Canvas("PauseUI");
            root.GetComponent<Canvas>().sortingOrder = 100;
            var controller = root.AddComponent<PauseMenuController>();
            controller.EventSystemRoot = MenuUIFactory.EventSystem(root.transform).gameObject;
            var pause = MenuUIFactory.Panel("Pausa", root.transform, new Color(.015f, .018f, .035f, .78f));
            controller.PausePanel = pause;
            var card = MenuUIFactory.Panel("Tarjeta", pause.transform, new Color(.055f, .06f, .10f, .98f));
            Place(card.transform, Vector2.zero, new Vector2(650, 640));
            var title = MenuUIFactory.Text("Titulo", card.transform, "PAUSA", 44, TextAnchor.MiddleCenter, new Color(1, .48f, .27f));
            Place(title.transform, new Vector2(0, 231), new Vector2(570, 66));
            var subtitle = MenuUIFactory.Text("Subtitulo", card.transform, "JOY JOEY", 18, TextAnchor.MiddleCenter, new Color(.63f, .66f, .75f));
            Place(subtitle.transform, new Vector2(0, 178), new Vector2(570, 38));
            controller.ResumeButton = Button("Reanudar", card.transform, "Reanudar", 91);
            controller.OptionsButton = Button("Opciones", card.transform, "Opciones", 13);
            controller.MainMenuButton = Button("Menu principal", card.transform, "Volver al menú principal", -65);
            controller.QuitButton = Button("Salir", card.transform, "Salir del juego", -143);
            MenuUIFactory.LinkVertical(controller.ResumeButton, controller.OptionsButton, controller.MainMenuButton, controller.QuitButton);
            controller.StatusText = MenuUIFactory.Text("Estado", card.transform, "", 17, TextAnchor.MiddleCenter, new Color(1, .7f, .5f));
            Place(controller.StatusText.transform, new Vector2(0, -210), new Vector2(570, 54));
            var hint = MenuUIFactory.Text("Ayuda", card.transform, "Esc / Start · Reanudar", 16, TextAnchor.MiddleCenter, new Color(.63f, .66f, .75f));
            Place(hint.transform, new Vector2(0, -267), new Vector2(570, 32));

            var options = MenuUIFactory.Panel("Opciones", root.transform, new Color(.015f, .018f, .035f, .82f));
            controller.OptionsPanel = options;
            var optionsCard = MenuUIFactory.Panel("Tarjeta", options.transform, new Color(.055f, .06f, .10f, .98f));
            Place(optionsCard.transform, Vector2.zero, new Vector2(740, 530));
            var optionsTitle = MenuUIFactory.Text("Titulo", optionsCard.transform, "OPCIONES", 40, TextAnchor.MiddleCenter, new Color(1, .48f, .27f));
            Place(optionsTitle.transform, new Vector2(0, 176), new Vector2(650, 70));
            var text = MenuUIFactory.Text("Contenido", optionsCard.transform,
                "Los ajustes de audio y pantalla se incorporarán\nen las próximas etapas del prototipo.", 23, TextAnchor.MiddleCenter, new Color(.82f, .83f, .89f));
            Place(text.transform, new Vector2(0, 32), new Vector2(650, 160));
            controller.BackButton = Button("Volver", optionsCard.transform, "Volver", -137);
            MenuUIFactory.LinkVertical(controller.BackButton);
            pause.SetActive(false);
            options.SetActive(false);
            controller.EventSystemRoot.SetActive(false);
            return root;
        }

        static Button Button(string name, Transform parent, string label, float y)
        {
            var button = MenuUIFactory.Button(name, parent, label);
            Place(button.transform, new Vector2(0, y), new Vector2(520, 62));
            return button;
        }

        static void Place(Transform transform, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
