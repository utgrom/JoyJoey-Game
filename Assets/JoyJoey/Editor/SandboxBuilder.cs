using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JoyJoey.Sandbox.Editor
{
    /// <summary>Creates only missing assets. Existing tuning is never replaced by a reload.</summary>
    [InitializeOnLoad]
    public static class SandboxBuilder
    {
        public const string ScenePath = "Assets/JoyJoey/Scenes/CircoOnirico_Sandbox.unity";
        const string CatalogPath = "Assets/JoyJoey/Data/SandboxCombat.asset";
        const string TuningPath = "Assets/JoyJoey/Data/SandboxMotor.asset";

        static SandboxBuilder()
        {
            if (!Application.isBatchMode) EditorApplication.delayCall += FirstImport;
        }

        static void FirstImport()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += FirstImport; return; }
            EnsureAssets();
            string sessionKey = Application.dataPath + "/JoyJoeySandboxOpened";
            if (SessionState.GetBool(sessionKey, false)) return;
            SessionState.SetBool(sessionKey, true);
            Scene active = SceneManager.GetActiveScene();
            // The fresh template can open directly into practice; preserve any user's unsaved scene.
            if (!EditorApplication.isPlayingOrWillChangePlaymode && active.path == "Assets/Scenes/SampleScene.unity" &&
                Enumerable.Range(0, SceneManager.sceneCount).All(i => !SceneManager.GetSceneAt(i).isDirty))
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Joy Joey/Escenas/Circo onírico (sandbox de combate)", false, 3)]
        public static void OpenSandbox()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) EnsureAssets();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Joy Joey/Crear assets faltantes/Del sandbox")]
        public static void EnsureAssets()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += EnsureAssets; return; }
            Directory.CreateDirectory("Assets/JoyJoey/Data");
            Directory.CreateDirectory("Assets/JoyJoey/Scenes");
            Directory.CreateDirectory("Assets/JoyJoey/Resources");
            AssetDatabase.Refresh();
            const string stickSettingsPath = "Assets/JoyJoey/Resources/JoyJoeyStickSettings.asset";
            if (!File.Exists(stickSettingsPath))
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<StickInputSettings>(), stickSettingsPath);
            var catalog = AssetDatabase.LoadAssetAtPath<CombatCatalog>(CatalogPath);
            if (catalog == null) { catalog = CombatDefaults.CreateCatalog(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            else CatalogMigrationMenu.UpgradeWithBackup(catalog);
            var tuning = AssetDatabase.LoadAssetAtPath<MotorTuning>(TuningPath);
            if (tuning == null) { tuning = CombatDefaults.CreateTuning(); AssetDatabase.CreateAsset(tuning, TuningPath); }
            const string lightPath="Assets/JoyJoey/Data/DummyLight.asset",heavyPath="Assets/JoyJoey/Data/DummyHeavy.asset";
            var light=AssetDatabase.LoadAssetAtPath<EnemyProfile>(lightPath);
            if(!light){light=CombatDefaults.CreateLightEnemy();AssetDatabase.CreateAsset(light,lightPath);}
            var heavy=AssetDatabase.LoadAssetAtPath<EnemyProfile>(heavyPath);
            if(!heavy){heavy=CombatDefaults.CreateHeavyEnemy();AssetDatabase.CreateAsset(heavy,heavyPath);}
            if (!File.Exists(ScenePath))
            {
                Scene previous = SceneManager.GetActiveScene();
                bool replaceUntitled = Application.isBatchMode || (SceneManager.sceneCount == 1 && string.IsNullOrEmpty(previous.path) && !previous.isDirty);
                if (!replaceUntitled && string.IsNullOrEmpty(previous.path))
                { Debug.LogWarning("Joy Joey: guarda la escena actual antes de crear el sandbox desde el menú."); return; }
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, replaceUntitled ? NewSceneMode.Single : NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var runner = new GameObject("CIRCO ONIRICO · Combat Sandbox").AddComponent<SandboxRunner>();
                runner.Catalog = catalog; runner.Tuning = tuning;
                runner.LightEnemy=light;runner.HeavyEnemy=heavy;
                EditorSceneManager.SaveScene(scene, ScenePath);
                if (!replaceUntitled)
                {
                    if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            if(Application.isBatchMode)
            {
                var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
                var runner=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<SandboxRunner>(true)).FirstOrDefault();
                if(runner){runner.LightEnemy=light;runner.HeavyEnemy=heavy;EditorSceneManager.SaveScene(scene);}
            }
            var existing = EditorBuildSettings.scenes;
            if (!existing.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(existing).ToArray();
            AssetDatabase.SaveAssets();
        }

        // Batch entry point: compile the actual runtime, serialize its scene/data and build a playable app.
        public static void BuildWindows()
        {
            EnsureAssets();
            var authoringCatalog=AssetDatabase.LoadAssetAtPath<CombatCatalog>(CatalogPath);
            string authoringError=AttackWorkshop.Validate(authoringCatalog);
            if(authoringError!=null)throw new Exception("Catálogo de ataques inválido: "+authoringError);
            Directory.CreateDirectory("Builds/Windows");
            PlayerSettings.companyName = "Joy Joey Prototype";
            PlayerSettings.productName = "Joy Joey - Circo Onirico";
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.runInBackground = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/JoyJoeySandbox.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/build-result.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings);
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Sandbox player build failed");
        }
    }
}
