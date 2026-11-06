using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JoyJoey.Game
{
    /// <summary>Explicit entry scene; opening a level directly in the editor remains supported.</summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Escena inicial del juego.")]
        public string InitialSceneName = "MenuPrincipal";

        public static GameBootstrap Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearStaticState() => Instance = null;

        void Awake()
        {
            if (Instance && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            GameSession.EnsureExists();
        }

        IEnumerator Start()
        {
            if (Instance != this) yield break;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-worldSmoke") >= 0)
                InitialSceneName = "Nivel_Prototipo";
            if (string.IsNullOrWhiteSpace(InitialSceneName) ||
                InitialSceneName == gameObject.scene.name ||
                InitialSceneName == SceneManager.GetActiveScene().name)
            {
                Debug.LogError("Joy Joey: el arranque necesita una escena inicial diferente.", this);
                yield break;
            }
            if (!Application.CanStreamedLevelBeLoaded(InitialSceneName))
            {
                Debug.LogError("Joy Joey: agrega la escena inicial a la lista de escenas del ejecutable: " + InitialSceneName, this);
                yield break;
            }
            yield return SceneManager.LoadSceneAsync(InitialSceneName, LoadSceneMode.Single);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
