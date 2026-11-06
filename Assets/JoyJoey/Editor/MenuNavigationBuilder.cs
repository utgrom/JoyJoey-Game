using System;
using System.IO;
using System.Linq;
using JoyJoey.Game.Audio;
using JoyJoey.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JoyJoey.Game.Editor
{
    /// <summary>Upgrades existing authored menus without replacing their pages or settings data.</summary>
    public static class MenuNavigationBuilder
    {
        [MenuItem("Joy Joey/Interfaz/Preparar navegación y desplegables")]
        public static void EnsureAssets() => DisplayBuilder.EnsureAssets();

        public static void UpgradeExistingAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Salí de Play antes de preparar la interfaz.");
            var active = SceneManager.GetActiveScene();
            if (Application.isBatchMode && SceneManager.sceneCount == 1 && string.IsNullOrEmpty(active.path) && !active.isDirty && active.rootCount == 0)
                EditorSceneManager.OpenScene(WorldBuilder.MenuScenePath, OpenSceneMode.Single);
            UpgradeAudio();
            UpgradeDisplay();
            UpgradeRoot(MainMenuBuilder.MenuPrefabPath, true);
            UpgradeRoot(PauseMenuBuilder.PrefabPath, false);
        }
        static void Edit(string path, Action<GameObject> edit)
        {
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(path);
                edit(root);
                UiAudioFeedback.EnsureForCanvas(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { if (root) PrefabUtility.UnloadPrefabContents(root); }
        }
        static void UpgradeAudio()
        {
            Edit(AudioBuilder.OptionsPrefabPath, root =>
            {
                var page = root.GetComponent<AudioOptionsController>();
                Hide(page.BackButton);
                foreach (var dropdown in root.GetComponentsInChildren<Dropdown>(true)) MenuDropdownAuthoring.Configure(dropdown);
                Vertical(page.MasterSlider, page.MusicSlider, page.SfxSlider, page.UiSlider, page.SpeakerModeDropdown,
                    page.OutputDeviceDropdown, page.RefreshDevicesButton, page.TestMusicButton, page.TestSfxButton,
                    page.TestUiButton, page.DefaultsButton);
                ((RectTransform)page.DefaultsButton.transform).anchoredPosition = new Vector2(0, -337);
                var nav = page.DefaultsButton.navigation; nav.selectOnLeft = nav.selectOnRight = null; page.DefaultsButton.navigation = nav;
            });
        }
        static void UpgradeDisplay()
        {
            Edit(DisplayBuilder.OptionsPrefabPath, root =>
            {
                var page = root.GetComponent<DisplayOptionsController>();
                Hide(page.BackButton); Hide(page.ApplyButton);
                foreach (var dropdown in root.GetComponentsInChildren<Dropdown>(true)) MenuDropdownAuthoring.Configure(dropdown);
                ((RectTransform)page.RevertButton.transform).anchoredPosition = new Vector2(0, -337);
                Vertical(page.ModeDropdown, page.ResolutionDropdown, page.FpsDropdown, page.VSyncDropdown,
                    page.RevertButton, page.BasicButton, page.AdvancedButton);
                HorizontalTabs(page.BasicButton, page.AdvancedButton);
                var nav = page.RevertButton.navigation; nav.selectOnLeft = nav.selectOnRight = null; page.RevertButton.navigation = nav;
            });
        }
        static void UpgradeRoot(string path, bool main)
        {
            Edit(path, root =>
            {
                var system = root.GetComponentInChildren<EventSystem>(true);
                var input = MenuInputCoordinator.Ensure(system);
                input.SetHints(false, true, true, false);
                if (main)
                {
                    var menu = root.GetComponentInChildren<MainMenuController>(true);
                    Hide(menu.ExtrasBackButton); Hide(menu.OptionsBackButton);
                    Vertical(menu.AudioButton, menu.DisplayButton);
                    // Both reused option pages render above every options-category button.
                    menu.AudioPage.transform.SetAsLastSibling();
                    menu.DisplayPage.transform.SetAsLastSibling();
                }
                else
                {
                    var menu = root.GetComponentInChildren<PauseMenuController>(true);
                    Hide(menu.BackButton);
                    Vertical(menu.AudioButton, menu.DisplayButton);
                    menu.AudioPage.transform.SetAsLastSibling();
                    menu.DisplayPage.transform.SetAsLastSibling();
                }
                foreach (var text in root.GetComponentsInChildren<Text>(true))
                    if (text.name == "Ayuda" && (text.text.Contains("Flechas o stick") || text.text.Contains("Esc / Start")))
                        text.gameObject.SetActive(false);
            });
        }
        static void Hide(Button button)
        { if (button) { button.gameObject.SetActive(false); button.navigation = new Navigation { mode = Navigation.Mode.None }; } }
        static void Vertical(params Selectable[] values)
        {
            var controls = values.Where(value => value && value.gameObject.activeSelf).ToArray();
            for (int i = 0; i < controls.Length; i++) controls[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnUp = controls[(i + controls.Length - 1) % controls.Length], selectOnDown = controls[(i + 1) % controls.Length] };
        }
        static void HorizontalTabs(Selectable basic, Selectable advanced)
        {
            var navigation = basic.navigation;
            navigation.selectOnLeft = navigation.selectOnRight = advanced;
            basic.navigation = navigation;
            navigation = advanced.navigation;
            navigation.selectOnLeft = navigation.selectOnRight = basic;
            advanced.navigation = navigation;
        }

        [MenuItem("Joy Joey/Interfaz/Compilar juego Windows")]
        public static void BuildWindows()
        {
            DisplayBuilder.BuildWindows();
            File.Copy("Validation/stage6-build-result.txt", "Validation/ui-fixes-build-result.txt", true);
        }
    }
}
