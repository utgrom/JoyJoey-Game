using System;
using System.IO;
using System.Linq;
using JoyJoey.Sandbox;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JoyJoey.Game.Editor
{
    /// <summary>Creates the first editable game scenes. Never rebuilds existing authored assets.</summary>
    public static class WorldBuilder
    {
        public const string BootstrapScenePath = "Assets/JoyJoey/Scenes/Arranque.unity";
        public const string MenuScenePath = "Assets/JoyJoey/Scenes/MenuPrincipal.unity";
        public const string LevelScenePath = "Assets/JoyJoey/Scenes/Nivel_Prototipo.unity";
        public const string PlayerPrefabPath = "Assets/JoyJoey/Prefabs/Joey_Player.prefab";
        public const string GroundPrefabPath = "Assets/JoyJoey/Prefabs/Suelo.prefab";
        public const string CameraPrefabPath = "Assets/JoyJoey/Prefabs/CamaraNivel.prefab";
        public const string SpawnPrefabPath = "Assets/JoyJoey/Prefabs/PuntoAparicion.prefab";
        public const string SharedCatalogPath = "Assets/JoyJoey/Data/SandboxCombat.asset";
        public const string SharedTuningPath = "Assets/JoyJoey/Data/SandboxMotor.asset";
        public const string ArtPath = "Assets/JoyJoey/Art/Prototype";
        const string SourceArtPath = @"D:\PROYECTS\JOY JOEY\SPRITES";
        const string SandboxScenePath = "Assets/JoyJoey/Scenes/CircoOnirico_Sandbox.unity";

        [MenuItem("Joy Joey/Etapa 1/Crear elementos faltantes")]
        public static void EnsureAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Salí de Play antes de crear escenas o prefabs.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Esperá a que Unity termine de importar y compilar.");

            PrepareSceneAuthoring();

            EnsureFolder("Assets/JoyJoey/Scenes");
            EnsureFolder("Assets/JoyJoey/Prefabs");
            EnsureFolder("Assets/JoyJoey/Data");
            EnsureFolder(ArtPath);
            ImportSprite("old joey sprites/OLD_joey_idle.png", "Joey_Idle.png", true);
            ImportSprite("old joey sprites/joey_run.png", "Joey_Run.png", true);
            ImportSprite("old joey sprites/joey_jump.png", "Joey_Jump.png", true);
            ImportSprite("old joey sprites/joey_wall.png", "Joey_Wall.png", true);
            ImportSprite("ENTORNO/Intro/suelo.png", "Suelo.png", false);
            ImportSprite("ENTORNO/Intro/backgroudn.png", "Fondo.png", false);
            EnsureSolidSprite();
            EnsureSharedData();
            EnsurePrefab(PlayerPrefabPath, CreatePlayer);
            EnsurePrefab(GroundPrefabPath, CreateGround);
            EnsurePrefab(CameraPrefabPath, CreateCamera);
            EnsurePrefab(SpawnPrefabPath, () => new GameObject("PuntoAparicion"));
            EnsureScene(LevelScenePath, CreateLevelScene);
            EnsureScene(MenuScenePath, CreateMenuScene);
            EnsureScene(BootstrapScenePath, CreateBootstrapScene);
            ConfigureBuildScenes();
        }

        static void PrepareSceneAuthoring()
        {
            string[] authored = { PlayerPrefabPath, GroundPrefabPath, CameraPrefabPath, SpawnPrefabPath,
                LevelScenePath, MenuScenePath, BootstrapScenePath };
            if (authored.All(File.Exists)) return;
            var untitled = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .Where(scene => string.IsNullOrEmpty(scene.path)).ToArray();
            if (untitled.Length == 0) return;

            // Unity disallows additive scene creation while an untitled scene is open. A batch launch
            // starts with an empty, clean untitled scene; it contains no authored work to preserve.
            if (Application.isBatchMode && SceneManager.sceneCount == 1 && !untitled[0].isDirty &&
                untitled[0].rootCount == 0 && File.Exists(SandboxScenePath))
            {
                EditorSceneManager.OpenScene(SandboxScenePath, OpenSceneMode.Single);
                return;
            }
            throw new InvalidOperationException("Unity no permite crear escenas aditivas mientras hay una escena Sin título. Guardá esa escena antes de crear los elementos faltantes; su contenido no se modificó.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void ImportSprite(string sourceRelativePath, string filename, bool character)
        {
            string path = ArtPath + "/" + filename;
            // Existing sprites and importer settings are the author's property.
            if (File.Exists(path)) return;
            string source = Path.Combine(SourceArtPath, sourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
                throw new FileNotFoundException("No se encontró el sprite original. Copialo a " + path + " y volvé a crear los elementos faltantes.", source);
            File.Copy(source, path, false);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePivot = new Vector2(.5f, 0);
            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteAlignment = (int)SpriteAlignment.Custom;
            textureSettings.spritePivot = new Vector2(.5f, 0);
            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(textureSettings);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            importer.spritePixelsPerUnit = character ? texture.height / 1.9f : 64;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        static void EnsureSolidSprite()
        {
            string path = ArtPath + "/Solid.png";
            if (File.Exists(path)) return;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 2;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        static void EnsureSharedData()
        {
            if (!AssetDatabase.LoadAssetAtPath<CombatCatalog>(SharedCatalogPath))
                AssetDatabase.CreateAsset(CombatDefaults.CreateCatalog(), SharedCatalogPath);
            if (!AssetDatabase.LoadAssetAtPath<MotorTuning>(SharedTuningPath))
                AssetDatabase.CreateAsset(CombatDefaults.CreateTuning(), SharedTuningPath);
        }

        static Sprite Sprite(string filename) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "/" + filename);

        static void EnsurePrefab(string path, Func<GameObject> create)
        {
            if (File.Exists(path)) return;
            // Make the scratch scene active before creating objects so the user's scene stays clean.
            var previous = SceneManager.GetActiveScene();
            var scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject root = null;
            try
            {
                SceneManager.SetActiveScene(scratch);
                root = create();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scratch, true);
            }
        }

        static GameObject CreatePlayer()
        {
            var root = new GameObject("Joey_Player");
            var view = root.AddComponent<WorldPlayer>();
            var visual = new GameObject("Visual").AddComponent<SpriteRenderer>();
            visual.transform.SetParent(root.transform, false);
            visual.sortingOrder = 20;
            view.Visual = visual;
            view.IdleSprite = Sprite("Joey_Idle.png");
            view.RunSprite = Sprite("Joey_Run.png");
            view.JumpSprite = Sprite("Joey_Jump.png");
            view.WallSprite = Sprite("Joey_Wall.png");
            view.VisualHeight = 1.9f;
            visual.sprite = view.IdleSprite;
            view.RefreshPreview();
            return root;
        }

        static GameObject CreateGround()
        {
            var root = new GameObject("Suelo");
            var box = root.AddComponent<BoxCollider2D>();
            box.size = Vector2.one;
            box.offset = new Vector2(0, -.5f);
            var surface = root.AddComponent<LevelSurface>();
            surface.Collider = box;
            surface.OneWay = false;
            var visual = new GameObject("Visual").AddComponent<SpriteRenderer>();
            visual.transform.SetParent(root.transform, false);
            visual.sprite = Sprite("Suelo.png");
            visual.sortingOrder = 0;
            visual.transform.localPosition = new Vector3(0, -1, 0);
            visual.transform.localScale = new Vector3(1 / visual.sprite.bounds.size.x, 1 / visual.sprite.bounds.size.y, 1);
            var edge = Solid("Borde superior", root.transform, new Vector2(0, -.005f), new Vector2(1, .01f), new Color(.45f, .43f, .48f), 1);
            edge.gameObject.hideFlags = HideFlags.None;
            return root;
        }

        static GameObject CreateCamera()
        {
            var root = new GameObject("CamaraNivel");
            root.tag = "MainCamera";
            root.transform.position = new Vector3(0, 3.5f, -10);
            var camera = root.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.075f, .075f, .12f);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            root.AddComponent<AudioListener>();
            var follow = root.AddComponent<LevelCamera>();
            follow.Bounds = new Rect(-40, -2, 80, 18);
            follow.Offset = new Vector3(0, 2, -10);
            follow.FollowSharpness = 8;
            return root;
        }

        static void EnsureScene(string path, Action<Scene> create)
        {
            if (File.Exists(path)) return;
            var previous = SceneManager.GetActiveScene();
            Scene scene = default;
            try
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                RenderSettings.skybox = null;
                create(scene);
                if (!EditorSceneManager.SaveScene(scene, path))
                    throw new IOException("Unity no pudo guardar la escena: " + path);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static GameObject Instance(string prefabPath, string name, Transform parent = null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            instance.name = name;
            if (parent) instance.transform.SetParent(parent, false);
            return instance;
        }

        static void CreateLevelScene(Scene scene)
        {
            var root = new GameObject("Nivel Prototipo");
            var runner = root.AddComponent<LevelRunner>();
            var geometry = Group("Geometria", root.transform);
            var scenery = Group("Entorno", root.transform);
            var characters = Group("Personajes", root.transform);
            var markers = Group("Puntos de aparicion", root.transform);
            var ground = Instance(GroundPrefabPath, "Suelo caminable", geometry);
            ground.transform.localScale = new Vector3(80, 3, 1);
            Wall("Limite izquierdo", -40.5f, geometry);
            Wall("Limite derecho", 40.5f, geometry);

            var background = new GameObject("Fondo antiguo").AddComponent<SpriteRenderer>();
            background.transform.SetParent(scenery, false);
            background.sprite = Sprite("Fondo.png");
            background.sortingOrder = -20;
            // The source has its own floor near the bottom. Align that horizon with our real floor.
            background.transform.localPosition = new Vector3(0, -5.6f, 0);
            background.transform.localScale = Vector3.one * (80 / background.sprite.bounds.size.x);

            var spawn = Instance(SpawnPrefabPath, "Inicio del nivel", markers).transform;
            spawn.position = new Vector3(-10, 0, 0);
            var player = Instance(PlayerPrefabPath, "Joey", characters).GetComponent<WorldPlayer>();
            player.transform.position = spawn.position;
            var camera = Instance(CameraPrefabPath, "Camara del nivel").GetComponent<LevelCamera>();
            camera.Target = player.transform;
            camera.transform.position = new Vector3(-10, 3.5f, -10);
            runner.Catalog = AssetDatabase.LoadAssetAtPath<CombatCatalog>(SharedCatalogPath);
            runner.Tuning = AssetDatabase.LoadAssetAtPath<MotorTuning>(SharedTuningPath);
            runner.Player = player;
            runner.SpawnPoint = spawn;
            runner.GeometryRoot = geometry;
            runner.Surfaces = geometry.GetComponentsInChildren<LevelSurface>();
            runner.RespawnBelowY = -12;
            runner.LeftBoundary = -44;
            runner.RightBoundary = 44;
        }

        static Transform Group(string name, Transform parent)
        {
            var result = new GameObject(name).transform;
            result.SetParent(parent, false);
            return result;
        }

        static void Wall(string name, float x, Transform parent)
        {
            var wall = new GameObject(name);
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = new Vector3(x, 5, 0);
            var box = wall.AddComponent<BoxCollider2D>();
            box.size = new Vector2(1, 12);
            var surface = wall.AddComponent<LevelSurface>();
            surface.Collider = box;
            Solid("Visual", wall.transform, Vector2.zero, box.size, new Color(.10f, .10f, .15f), 2);
        }

        static SpriteRenderer Solid(string name, Transform parent, Vector2 position, Vector2 size, Color color, int order)
        {
            var renderer = new GameObject(name).AddComponent<SpriteRenderer>();
            renderer.transform.SetParent(parent, false);
            renderer.transform.localPosition = position;
            renderer.transform.localScale = new Vector3(size.x, size.y, 1);
            renderer.sprite = Sprite("Solid.png");
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        static void CreateMenuScene(Scene scene)
        {
            var camera = Instance(CameraPrefabPath, "Camara del menu").GetComponent<LevelCamera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0, 0, -10);
            Text("Logo provisional", "JOY JOEY", new Vector2(0, 1.2f), .12f, 92, new Color(1, .37f, .17f));
            Text("Marcador", "MENÚ PRINCIPAL", new Vector2(0, -.2f), .055f, 54, new Color(.70f, .71f, .79f));
        }

        static void CreateBootstrapScene(Scene scene)
        {
            var root = new GameObject("Arranque del juego");
            var bootstrap = root.AddComponent<GameBootstrap>();
            bootstrap.InitialSceneName = "Nivel_Prototipo";
            var camera = Instance(CameraPrefabPath, "Camara de arranque").GetComponent<LevelCamera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0, 0, -10);
            Text("Logo provisional", "JOY JOEY", Vector2.zero, .12f, 92, new Color(1, .37f, .17f));
        }

        static void Text(string name, string text, Vector2 position, float size, int fontSize, Color color)
        {
            var mesh = new GameObject(name).AddComponent<TextMesh>();
            mesh.transform.position = position;
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = size;
            mesh.fontSize = fontSize;
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.color = color;
            var renderer = mesh.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mesh.font.material;
            renderer.sortingOrder = 10;
        }

        public static void ConfigureBuildScenes()
        {
            string[] required = { BootstrapScenePath, MenuScenePath, LevelScenePath, SandboxScenePath };
            var scenes = required.Where(File.Exists).Select(path => new EditorBuildSettingsScene(path, true))
                .Concat(EditorBuildSettings.scenes.Where(s => !required.Contains(s.path))).ToArray();
            if (!EditorBuildSettings.scenes.Select(s => s.path + ":" + s.enabled).SequenceEqual(scenes.Select(s => s.path + ":" + s.enabled)))
                EditorBuildSettings.scenes = scenes;
        }

        [MenuItem("Joy Joey/Etapa 1/Abrir nivel prototipo")]
        public static void OpenLevel() => Open(LevelScenePath);
        [MenuItem("Joy Joey/Etapa 1/Abrir arranque")]
        public static void OpenBootstrap() => Open(BootstrapScenePath);
        [MenuItem("Joy Joey/Etapa 1/Abrir base de menu")]
        public static void OpenMenu() => Open(MenuScenePath);
        static void Open(string path)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureAssets();
            EditorSceneManager.OpenScene(path);
        }

        [MenuItem("Joy Joey/Etapa 1/Compilar prototipo Windows")]
        public static void BuildWindows()
        {
            EnsureAssets();
            PlayerSettings.companyName = "Joy Joey Prototype";
            PlayerSettings.productName = "Joy Joey - Prototipo";
            PlayerSettings.resizableWindow = false;
            Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { BootstrapScenePath, MenuScenePath, LevelScenePath, SandboxScenePath },
                locationPathName = "Builds/Windows/JoyJoeyPrototype.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/stage1-build-result.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("No se pudo compilar el prototipo de escenas.");
        }
    }
}
