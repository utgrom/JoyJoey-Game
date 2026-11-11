using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoyJoey.Game;
using JoyJoey.Game.Editor;
using JoyJoey.Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class FrontEndCheckpointTests
    {
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        T Own<T>(T value) where T : UnityEngine.Object { _owned.Add(value); return value; }

        [TearDown]
        public void Cleanup()
        {
            foreach (var value in _owned) if (value) UnityEngine.Object.DestroyImmediate(value);
            _owned.Clear();
        }

        LevelRunner Level()
        {
            var root = Own(new GameObject("Checkpoint test level"));
            var runner = root.AddComponent<LevelRunner>();
            var player = new GameObject("Joey"); player.transform.SetParent(root.transform);
            runner.Player = player.AddComponent<WorldPlayer>();
            var spawn = new GameObject("Spawn"); spawn.transform.SetParent(root.transform);
            runner.SpawnPoint = spawn.transform;
            var geometry = new GameObject("Geometry"); geometry.transform.SetParent(root.transform);
            runner.GeometryRoot = geometry.transform;
            var floor = new GameObject("Floor"); floor.transform.SetParent(geometry.transform);
            var box = floor.AddComponent<BoxCollider2D>();
            box.size = new Vector2(20, 1); box.offset = new Vector2(0, -.5f);
            floor.AddComponent<LevelSurface>().Collider = box;
            var wall = new GameObject("Wall"); wall.transform.SetParent(geometry.transform);
            wall.transform.position = new Vector3(4, 1);
            var wallBox = wall.AddComponent<BoxCollider2D>(); wallBox.size = new Vector2(1, 2);
            wall.AddComponent<LevelSurface>().Collider = wallBox;
            runner.LeftBoundary = -12; runner.RightBoundary = 12; runner.RespawnBelowY = -4;
            runner.Catalog = Own(CombatDefaults.CreateCatalog());
            runner.Tuning = Own(CombatDefaults.CreateTuning());
            runner.Initialize();
            return runner;
        }

        [Test]
        public void CheckpointMustHaveSupportAndLeaveTheCharacterOutsideWalls()
        {
            var runner = Level();
            Assert.That(runner.IsSafeCheckpoint(new Vector2(2, 0)), Is.True);
            Assert.That(runner.IsSafeCheckpoint(new Vector2(2, .5f)), Is.False, "Do not resume in midair.");
            Assert.That(runner.IsSafeCheckpoint(new Vector2(2, -.5f)), Is.False, "Do not resume inside the floor.");
            Assert.That(runner.IsSafeCheckpoint(new Vector2(4, 0)), Is.False, "Do not resume inside a wall.");
            Assert.That(runner.IsSafeCheckpoint(new Vector2(11, 0)), Is.False, "A point without supporting ground is unsafe.");
        }

        [Test]
        public void CheckpointCaptureRequiresGroundedIdleRatherThanAnAirborneAttackOrDash()
        {
            var runner = Level();
            Assert.That(runner.TryGetCheckpoint(out var point), Is.True);
            Assert.That(point, Is.EqualTo(Vector2.zero));
            runner.Simulation.Player.Grounded = false;
            Assert.That(runner.TryGetCheckpoint(out _), Is.False);
            runner.Simulation.Player.Grounded = true;
            runner.Simulation.Player.Move = runner.Catalog.Find("jab1");
            Assert.That(runner.TryGetCheckpoint(out _), Is.False);
            runner.Simulation.Player.Move = null;
            runner.Simulation.Player.DashTicks = 5;
            Assert.That(runner.TryGetCheckpoint(out _), Is.False);
        }

        [Test]
        public void CinematicDestinationMovesTheSimulationAndKeepsJoeysDamageAndFacing()
        {
            var runner = Level();
            var actor = runner.Simulation.Player;
            actor.Damage = 12;
            actor.Facing = -1;
            actor.Grounded = false;
            actor.Move = runner.Catalog.Find("jab1");
            actor.Velocity = new Vector2(5, -2);

            Assert.That(runner.TryMoveJoeyTo(new Vector2(4, 0)), Is.False);
            Assert.That(actor.Position, Is.EqualTo(Vector2.zero));
            Assert.That(runner.TryMoveJoeyTo(new Vector2(2, 0)), Is.True);
            Assert.That(actor.Position, Is.EqualTo(new Vector2(2, 0)));
            Assert.That((Vector2)runner.Player.transform.position, Is.EqualTo(actor.Position));
            Assert.That(actor.Grounded, Is.True);
            Assert.That(actor.Velocity, Is.EqualTo(Vector2.zero));
            Assert.That(actor.Move, Is.Null);
            Assert.That(actor.Facing, Is.EqualTo(-1));
            Assert.That(actor.Damage, Is.EqualTo(12));
        }
    }

    public sealed class FrontEndSaveTests
    {
        string _directory;
        string _testRoot;
        SaveStore _store;

        [SetUp]
        public void Setup()
        {
            _testRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Validation", "TestSaves"));
            _directory = Path.Combine(_testRoot, Guid.NewGuid().ToString("N"));
            _store = new SaveStore(_directory);
        }

        [TearDown]
        public void Cleanup()
        {
            string resolved = Path.GetFullPath(_directory);
            if (!resolved.StartsWith(_testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a directory outside this test's save root.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }

        static SavedGameData Data(float x = 4) => new SavedGameData
        {
            Version = 1,
            SceneName = "Nivel_Prototipo",
            Position = new Vector2(x, 0),
            Facing = 1,
            HasCheckpoint = true,
            SavedUtc = DateTime.UtcNow.ToString("O"),
            ProgressFlags = new[] { "started", "room_one" }
        };

        [Test]
        public void MissingFileDoesNotPretendThereIsAStartedGame()
        {
            Assert.That(_store.TryLoad(out var data, out _), Is.False);
            Assert.That(data, Is.Null);
            Assert.That(File.Exists(_store.FilePath), Is.False);
        }

        [Test]
        public void SaveRoundTripPreservesSceneCheckpointFacingAndProgress()
        {
            var original = Data();
            original.Facing = -1;
            Assert.That(_store.TrySave(original, out var error), Is.True, error);
            Assert.That(_store.TryLoad(out var loaded, out error), Is.True, error);
            Assert.That(loaded.SceneName, Is.EqualTo(original.SceneName));
            Assert.That(loaded.Position, Is.EqualTo(original.Position));
            Assert.That(loaded.Facing, Is.EqualTo(-1));
            CollectionAssert.AreEqual(original.ProgressFlags, loaded.ProgressFlags);
            Assert.That(loaded.HasCheckpoint, Is.True);
        }

        [Test]
        public void ReplacingASaveKeepsThePreviousCompleteFileAsBackup()
        {
            Assert.That(_store.TrySave(Data(4), out var error), Is.True, error);
            string first = File.ReadAllText(_store.FilePath);
            Assert.That(_store.TrySave(Data(8), out error), Is.True, error);
            Assert.That(_store.TryLoad(out var current, out error), Is.True, error);
            Assert.That(current.Position.x, Is.EqualTo(8));
            Assert.That(File.ReadAllText(_store.BackupPath), Is.EqualTo(first));
        }

        [Test]
        public void CorruptPrimaryFallsBackToTheLastValidBackup()
        {
            Assert.That(_store.TrySave(Data(4), out var error), Is.True, error);
            Assert.That(_store.TrySave(Data(8), out error), Is.True, error);
            File.WriteAllText(_store.FilePath, "{broken json");
            Assert.That(_store.TryLoad(out var recovered, out error), Is.True, error);
            Assert.That(recovered.Position.x, Is.EqualTo(4));
        }

        [Test]
        public void EmptyOrCorruptFilesDoNotEnableContinue()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(_store.FilePath, "{}");
            File.WriteAllText(_store.BackupPath, "{broken json");
            Assert.That(_store.TryLoad(out var data, out _), Is.False);
            Assert.That(data, Is.Null);
        }

        [Test]
        public void SavingAfterPrimaryCorruptionPreservesAnExistingGoodBackup()
        {
            Assert.That(_store.TrySave(Data(4), out var error), Is.True, error);
            Assert.That(_store.TrySave(Data(8), out error), Is.True, error);
            string backup = File.ReadAllText(_store.BackupPath);
            File.WriteAllText(_store.FilePath, "{broken json");
            Assert.That(_store.TrySave(Data(12), out error), Is.True, error);
            Assert.That(File.ReadAllText(_store.BackupPath), Is.EqualTo(backup));
            Assert.That(_store.TryLoad(out var current, out error), Is.True, error);
            Assert.That(current.Position.x, Is.EqualTo(12));
        }

        [TestCase("MenuPrincipal")]
        [TestCase("CircoOnirico_Sandbox")]
        [TestCase("../AnotherScene")]
        public void NonCampaignScenesCannotReplaceAnExistingSave(string scene)
        {
            Assert.That(_store.TrySave(Data(), out var error), Is.True, error);
            string before = File.ReadAllText(_store.FilePath);
            var invalid = Data();
            invalid.SceneName = scene;
            Assert.That(_store.IsValid(invalid), Is.False);
            Assert.That(_store.TrySave(invalid, out _), Is.False);
            Assert.That(File.ReadAllText(_store.FilePath), Is.EqualTo(before));
        }

        [TestCase(float.NaN, 0)]
        [TestCase(0, float.PositiveInfinity)]
        public void NonFinitePositionsAreRejected(float x, float y)
        {
            var invalid = Data();
            invalid.Position = new Vector2(x, y);
            Assert.That(_store.IsValid(invalid), Is.False);
            Assert.That(_store.TrySave(invalid, out _), Is.False);
            Assert.That(File.Exists(_store.FilePath), Is.False);
        }

        [Test]
        public void FutureSchemaInvalidFacingAndInvalidFlagsAreRejected()
        {
            var future = Data(); future.Version = 999;
            var facing = Data(); facing.Facing = 0;
            var flags = Data(); flags.ProgressFlags = new[] { "" };
            var noCheckpoint = Data(); noCheckpoint.HasCheckpoint = false;
            foreach (var invalid in new[] { future, facing, flags, noCheckpoint })
            {
                Assert.That(_store.IsValid(invalid), Is.False);
                Assert.That(_store.TrySave(invalid, out _), Is.False);
            }
        }
    }

    /// <summary>Input checks use virtual devices and never read or write the player's real save.</summary>
    public sealed class FrontEndInputTests : InputTestFixture
    {
        [Test]
        public void PointerMovementAndAnalogStickDriftDoNotOpenTheTitleScreen()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            var pad = InputSystem.AddDevice<Gamepad>();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(480, 270), delta = new Vector2(20, 10) });
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(.25f, .1f), rightStick = Vector2.right });
            InputSystem.Update();
            Assert.That(MainMenuController.AnyButtonPressedThisFrame(), Is.False);
        }

        [Test]
        public void KeyboardPressCanOpenTheTitleScreen()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            Press(keyboard.enterKey);
            Assert.That(MainMenuController.AnyButtonPressedThisFrame(), Is.True);
            Release(keyboard.enterKey);
            Assert.That(MainMenuController.AnyButtonPressedThisFrame(), Is.False);
        }

        [Test]
        public void GamepadButtonCanOpenTheTitleScreen()
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            Press(pad.buttonSouth);
            Assert.That(MainMenuController.AnyButtonPressedThisFrame(), Is.True);
        }

        [Test]
        public void MouseClickCanOpenTheTitleScreen()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            Press(mouse.leftButton);
            Assert.That(MainMenuController.AnyButtonPressedThisFrame(), Is.True);
        }
    }

    public sealed class FrontEndAuthoringTests
    {
        [Test]
        public void MainMenuPrefabHasSerializedPagesButtonsAndInputBindings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MainMenuBuilder.MenuPrefabPath);
            Assert.That(prefab, Is.Not.Null, "Generate stage 2 assets before checking the saved interface.");
            var menu = prefab.GetComponentInChildren<MainMenuController>(true);
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.PressAnyPanel, Is.Not.Null);
            Assert.That(menu.MainPanel, Is.Not.Null);
            Assert.That(menu.ExtrasPanel, Is.Not.Null);
            Assert.That(menu.OptionsPanel, Is.Not.Null);
            Assert.That(menu.StartLabel, Is.Not.Null);
            Assert.That(menu.StatusLabel, Is.Not.Null);
            foreach (var button in new[] { menu.StartButton, menu.SandboxButton, menu.ExtrasButton, menu.OptionsButton,
                menu.QuitButton })
            {
                Assert.That(button, Is.Not.Null);
                Assert.That(button.navigation.mode, Is.Not.EqualTo(Navigation.Mode.None));
            }
            Assert.That(menu.ExtrasBackButton, Is.Not.Null);
            Assert.That(menu.OptionsBackButton, Is.Not.Null);
            Assert.That(menu.ExtrasBackButton.gameObject.activeSelf, Is.False);
            Assert.That(menu.OptionsBackButton.gameObject.activeSelf, Is.False);
            Assert.That(prefab.GetComponentsInChildren<Canvas>(true).Any(), Is.True);
            Assert.That(prefab.GetComponentsInChildren<EventSystem>(true).Length, Is.EqualTo(1));
            Assert.That(menu.InputModule, Is.Not.Null);
            Assert.That(menu.InputModule.move, Is.Not.Null);
            Assert.That(menu.InputModule.submit, Is.Not.Null);
            Assert.That(menu.InputModule.cancel, Is.Not.Null);
            Assert.That(AssetDatabase.Contains(menu.InputModule.actionsAsset), Is.True, "UI controls must survive saving and building the scene.");
            Assert.That(AssetDatabase.Contains(menu.InputModule.submit), Is.True);
            Assert.That(AssetDatabase.Contains(menu.InputModule.move), Is.True);
            Assert.That(AssetDatabase.Contains(menu.InputModule.cancel), Is.True);
            Assert.That(menu.EventSystem, Is.Not.Null);
        }

        [Test]
        public void PausePrefabHasSerializedPagesAndAResourcesPathForDirectLevelPlay()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PauseMenuBuilder.PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(Resources.Load<GameObject>(PauseMenuBuilder.ResourcePath), Is.SameAs(prefab));
            var pause = prefab.GetComponentInChildren<PauseMenuController>(true);
            Assert.That(pause, Is.Not.Null);
            Assert.That(pause.PausePanel, Is.Not.Null);
            Assert.That(pause.OptionsPanel, Is.Not.Null);
            Assert.That(pause.EventSystemRoot, Is.Not.Null);
            foreach (var button in new[] { pause.ResumeButton, pause.OptionsButton, pause.MainMenuButton, pause.QuitButton, pause.BackButton })
                Assert.That(button, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<EventSystem>(true).Length, Is.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<InputSystemUIInputModule>(true).Length, Is.EqualTo(1));
        }

        [Test]
        public void BootstrapSceneStartsAtTheMenuAndTheMenuIsASerializedPrefab()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene bootstrap = SceneManager.GetSceneByPath(WorldBuilder.BootstrapScenePath);
            Scene menu = SceneManager.GetSceneByPath(WorldBuilder.MenuScenePath);
            bool closeBootstrap = !bootstrap.IsValid() || !bootstrap.isLoaded;
            bool closeMenu = !menu.IsValid() || !menu.isLoaded;
            try
            {
                if (closeBootstrap) bootstrap = EditorSceneManager.OpenScene(WorldBuilder.BootstrapScenePath, OpenSceneMode.Additive);
                if (closeMenu) menu = EditorSceneManager.OpenScene(WorldBuilder.MenuScenePath, OpenSceneMode.Additive);
                var entry = bootstrap.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameBootstrap>(true)).Single();
                Assert.That(entry.InitialSceneName, Is.EqualTo("MenuPrincipal"));
                var controller = menu.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MainMenuController>(true)).Single();
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(controller.gameObject), Is.Not.Null);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (closeMenu && menu.IsValid() && menu.isLoaded) EditorSceneManager.CloseScene(menu, true);
                if (closeBootstrap && bootstrap.IsValid() && bootstrap.isLoaded) EditorSceneManager.CloseScene(bootstrap, true);
            }
        }

        [Test]
        public void RepeatingMenuSetupPreservesTheLevelSandboxAndExistingInterface()
        {
            string[] paths =
            {
                WorldBuilder.LevelScenePath, WorldBuilder.MenuScenePath, WorldBuilder.BootstrapScenePath,
                "Assets/JoyJoey/Scenes/CircoOnirico_Sandbox.unity", WorldBuilder.PlayerPrefabPath,
                WorldBuilder.SharedCatalogPath, WorldBuilder.SharedTuningPath,
                MainMenuBuilder.MenuPrefabPath, PauseMenuBuilder.PrefabPath
            };
            var before = paths.ToDictionary(p => p, File.ReadAllBytes);
            Scene active = SceneManager.GetActiveScene();
            MainMenuBuilder.EnsureAssets();
            PauseMenuBuilder.EnsureAssets();
            MainMenuBuilder.EnsureAssets();
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(active));
            foreach (var path in paths) CollectionAssert.AreEqual(before[path], File.ReadAllBytes(path), path);
        }
    }
}
