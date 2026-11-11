using UnityEditor;
using UnityEditor.SceneManagement;

namespace JoyJoey.Sandbox.Editor
{
    internal static class CutsceneSceneMenu
    {
        const string ExampleScene = "Assets/JoyJoey/Scenes/Cinematica_Ejemplo.unity";

        [MenuItem("Joy Joey/Escenas/Cinemática de ejemplo", false, 4)]
        static void OpenExample()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ExampleScene);
        }
    }
}
