using UnityEditor;
using UnityEditor.SceneManagement;

namespace JoyJoey.Sandbox.Editor
{
    internal static class EventExampleSceneMenu
    {
        const string ExampleScene = "Assets/JoyJoey/Scenes/Eventos_Ejemplo.unity";

        [MenuItem("Joy Joey/Escenas/Eventos de ejemplo", false, 5)]
        static void OpenExample()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ExampleScene);
        }
    }
}
