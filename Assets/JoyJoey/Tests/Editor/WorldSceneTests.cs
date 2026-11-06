using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoyJoey.Game;
using JoyJoey.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JoyJoey.Sandbox.Tests
{
    /// <summary>Checks that authored levels use the combat motor without inheriting its practice room.</summary>
    public sealed class WorldSceneTests
    {
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }

        [TearDown]
        public void Cleanup()
        {
            foreach (var value in owned) if (value) UnityEngine.Object.DestroyImmediate(value);
            owned.Clear();
        }

        CombatSimulation Simulation(CombatWorld world = null)
        {
            var catalog = Own(CombatDefaults.CreateCatalog());
            var tuning = Own(CombatDefaults.CreateTuning());
            var target = Own(CombatDefaults.CreateLightEnemy());
            return new CombatSimulation(catalog, tuning, target, world);
        }

        static CombatWorld LevelWorld()
        {
            var world = new CombatWorld
            {
                PlayerSpawn = new Vector2(32, 0),
                DummySpawn = Vector2.zero,
                LeftBoundary = -75,
                RightBoundary = 75,
                RespawnBelowY = -15,
                TargetEnabled = false
            };
            world.Platforms.Add(new ArenaPlatform(-70, -1, 140, 1));
            return world;
        }

        static InputFrame Press(ActionId action, Vector2 direction = default)
        {
            var frame = new InputFrame { Move = direction };
            frame.Commands.Add(new InputCommand(action, InputEdge.Press, direction));
            return frame;
        }

        [Test]
        public void DefaultWorldStillHasThePracticeRoomAndSpawn()
        {
            var sim = Simulation();
            Assert.That(sim.Platforms.Count, Is.EqualTo(6));
            Assert.That(sim.Player.Position, Is.EqualTo(new Vector2(-3, 0)));
            Assert.That(sim.TargetEnabled, Is.True);
            Assert.That(sim.Platforms[0].Bounds, Is.EqualTo(new Rect(-14, -1, 28, 1)));
        }

        [Test]
        public void AuthoredWorldCanExtendPastTheOldTwentyUnitLimit()
        {
            var sim = Simulation(LevelWorld());
            Assert.That(sim.Platforms.Count, Is.EqualTo(1));
            for (int i = 0; i < 120; i++) sim.Step(new InputFrame { Move = Vector2.right });
            Assert.That(sim.Player.Position.x, Is.GreaterThan(40));
            Assert.That(sim.Player.Grounded, Is.True);
            Assert.That(sim.Player.Position.y, Is.EqualTo(0).Within(.001f));
        }

        [TestCase(80, 0)]
        [TestCase(-80, 0)]
        [TestCase(32, -16)]
        public void AuthoredWorldRespawnsAtItsMarkerRatherThanThePracticeSpawn(float x, float y)
        {
            var world = LevelWorld();
            var sim = Simulation(world);
            sim.Player.Position = new Vector2(x, y);
            sim.Player.Velocity = new Vector2(3, -4);
            sim.Step(new InputFrame());
            Assert.That(sim.Player.Position, Is.EqualTo(world.PlayerSpawn));
            Assert.That(sim.Player.Velocity, Is.EqualTo(Vector2.zero));
            Assert.That(sim.Player.Move, Is.Null);
        }

        [Test]
        public void ResetUsesTheAuthoredSpawnAndGeometry()
        {
            var world = LevelWorld();
            var sim = Simulation(world);
            sim.Player.Position = new Vector2(60, 3);
            sim.Reset();
            Assert.That(sim.Player.Position, Is.EqualTo(world.PlayerSpawn));
            Assert.That(sim.Platforms.Count, Is.EqualTo(1));
        }

        [Test]
        public void LevelWithoutATargetCannotHitAnInvisiblePracticeDummy()
        {
            var sim = Simulation(LevelWorld());
            sim.Player.Position = new Vector2(-1.25f, 0);
            sim.Dummy.Position = Vector2.zero;
            sim.Step(Press(ActionId.Basic));
            for (int i = 0; i < 40; i++)
            {
                sim.Step(new InputFrame());
                Assert.That(sim.Hitstop, Is.Zero);
                Assert.That(sim.Dummy.Damage, Is.Zero);
                Assert.That(sim.ComboHits, Is.Zero);
            }
            Assert.That(sim.Dummy.Position, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void TargetSeekingTrickDoesNotTeleportTowardAnAbsentPracticeDummy()
        {
            var sim = Simulation(LevelWorld());
            sim.Step(Press(ActionId.NextSuit));
            sim.Step(Press(ActionId.NextSuit));
            Assert.That(sim.SelectedRag, Is.EqualTo(RagStyle.Illusionist));
            sim.Step(Press(ActionId.Trick));
            Assert.That(sim.Player.MoveId, Is.EqualTo("trickIllusion"));
            Assert.That(sim.Player.Position.x, Is.EqualTo(32).Within(.001f));
        }

        [Test]
        public void SurfaceReadsColliderOffsetAndScaleInWorldSpace()
        {
            var go = Own(new GameObject("Authored collision"));
            go.transform.position = new Vector3(10, 4, 0);
            go.transform.localScale = new Vector3(-2, 3, 1);
            var box = go.AddComponent<BoxCollider2D>();
            box.offset = new Vector2(.5f, -.5f);
            box.size = new Vector2(4, 2);
            var surface = go.AddComponent<LevelSurface>();
            surface.Collider = box;
            surface.OneWay = true;
            var platform = surface.ToPlatform();
            Assert.That(platform.Bounds.xMin, Is.EqualTo(5).Within(.001f));
            Assert.That(platform.Bounds.yMin, Is.EqualTo(-.5f).Within(.001f));
            Assert.That(platform.Bounds.size, Is.EqualTo(new Vector2(8, 6)));
            Assert.That(platform.OneWay, Is.True);
        }

        [Test]
        public void LevelRunnerReadsEditedCollidersAndKeepsTheSharedAssets()
        {
            var root = Own(new GameObject("Authored level"));
            var runner = root.AddComponent<LevelRunner>();
            var player = new GameObject("Placed Joey"); player.transform.SetParent(root.transform);
            runner.Player = player.AddComponent<WorldPlayer>();
            var spawn = new GameObject("Placed spawn"); spawn.transform.SetParent(root.transform);
            spawn.transform.position = new Vector3(32, 0);
            runner.SpawnPoint = spawn.transform;
            var geometry = new GameObject("Authored geometry"); geometry.transform.SetParent(root.transform);
            runner.GeometryRoot = geometry.transform;
            var floor = new GameObject("Placed floor"); floor.transform.SetParent(geometry.transform);
            floor.transform.position = new Vector3(30, -1);
            var box = floor.AddComponent<BoxCollider2D>(); box.size = new Vector2(20, 2);
            var surface = floor.AddComponent<LevelSurface>(); surface.Collider = box;
            runner.Catalog = Own(CombatDefaults.CreateCatalog());
            runner.Tuning = Own(CombatDefaults.CreateTuning());
            runner.Initialize();
            Assert.That(runner.Simulation.Catalog, Is.SameAs(runner.Catalog));
            Assert.That(runner.Simulation.Tuning, Is.SameAs(runner.Tuning));
            Assert.That(runner.Simulation.TargetEnabled, Is.False);
            Assert.That(runner.Simulation.Platforms.Single().Bounds, Is.EqualTo(new Rect(20, -2, 20, 2)));
            Assert.That((Vector2)runner.Player.transform.position, Is.EqualTo(new Vector2(32, 0)));

            box.size = new Vector2(30, 2);
            runner.RefreshWorldGeometry();
            Assert.That(runner.Simulation.Platforms.Single().Bounds.width, Is.EqualTo(30));
            box.isTrigger = true;
            runner.RefreshWorldGeometry();
            Assert.That(runner.Simulation.Platforms, Is.Empty, "Trigger volumes must not become solid floors.");

            spawn.transform.position = new Vector3(40, 2);
            runner.ResetPlayer();
            Assert.That(runner.Simulation.Player.Position, Is.EqualTo(new Vector2(40, 2)));
            Assert.That((Vector2)runner.Player.transform.position, Is.EqualTo(new Vector2(40, 2)));
        }

        [Test]
        public void DiagonalSurfacesAreRejectedInsteadOfCreatingInvisibleBoundingBoxFloors()
        {
            var go = Own(new GameObject("Unsupported slope"));
            go.AddComponent<BoxCollider2D>();
            var surface = go.AddComponent<LevelSurface>();
            go.transform.rotation = Quaternion.Euler(0, 0, 25);
            Assert.Throws<InvalidOperationException>(() => surface.ToPlatform());
        }

        [Test]
        public void CameraClampsItsEntireViewportInsideTheLevel()
        {
            var go = Own(new GameObject("Authored camera"));
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.aspect = 2;
            var follow = go.AddComponent<LevelCamera>();
            follow.Bounds = new Rect(-20, -2, 40, 12);
            var upper = follow.ConstrainPosition(new Vector3(999, 999, -10));
            var lower = follow.ConstrainPosition(new Vector3(-999, -999, -10));
            Assert.That(upper, Is.EqualTo(new Vector3(10, 5, -10)));
            Assert.That(lower, Is.EqualTo(new Vector3(-10, 3, -10)));
        }

        [Test]
        public void CameraCentersAnAxisWhenTheAuthoredRegionIsSmallerThanItsView()
        {
            var go = Own(new GameObject("Small authored camera region"));
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.aspect = 2;
            var follow = go.AddComponent<LevelCamera>();
            follow.Bounds = new Rect(8, 2, 4, 2);
            Assert.That(follow.ConstrainPosition(new Vector3(999, -999, -10)), Is.EqualTo(new Vector3(10, 3, -10)));
        }

        [Test]
        public void SavedLevelContainsVisiblePlayerGeometryCameraAndSharedData()
        {
            Assert.That(File.Exists(WorldBuilder.LevelScenePath), Is.True, "Generate stage 1 assets before running scene checks.");
            Scene previous = SceneManager.GetActiveScene();
            Scene level = SceneManager.GetSceneByPath(WorldBuilder.LevelScenePath);
            bool openedHere = !level.IsValid() || !level.isLoaded;
            if (openedHere) level = EditorSceneManager.OpenScene(WorldBuilder.LevelScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = level.GetRootGameObjects();
                var runner = roots.SelectMany(r => r.GetComponentsInChildren<LevelRunner>(true)).Single();
                Assert.That(runner.Player, Is.Not.Null);
                Assert.That(runner.SpawnPoint, Is.Not.Null);
                Assert.That(runner.Player.Visual, Is.Not.Null);
                Assert.That(runner.Player.Visual.sprite, Is.Not.Null, "Joey must be visible before Play.");
                Assert.That(runner.Player.Visual.enabled, Is.True);
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Count(), Is.EqualTo(1));
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<LevelSurface>(true)).Any(), Is.True);
                Assert.That(runner.Catalog, Is.SameAs(AssetDatabase.LoadAssetAtPath<CombatCatalog>(WorldBuilder.SharedCatalogPath)));
                Assert.That(runner.Tuning, Is.SameAs(AssetDatabase.LoadAssetAtPath<MotorTuning>(WorldBuilder.SharedTuningPath)));
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(runner.Player.gameObject), Is.Not.Null);
                Assert.That(runner.GeometryRoot, Is.Not.Null);
                foreach (var surface in runner.GeometryRoot.GetComponentsInChildren<LevelSurface>())
                {
                    Assert.That(surface.GetComponent<BoxCollider2D>(), Is.Not.Null);
                    Assert.That(surface.GetComponentsInChildren<SpriteRenderer>().Any(v => v.enabled && v.sprite), Is.True,
                        "Collision geometry should have a visible authored representation.");
                }
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (openedHere) EditorSceneManager.CloseScene(level, true);
            }
        }

        [Test, Order(1000)]
        public void BuilderPreservesExistingScenesPrefabsDataAndAnUnsavedActiveScene()
        {
            string[] paths =
            {
                WorldBuilder.BootstrapScenePath, WorldBuilder.MenuScenePath, WorldBuilder.LevelScenePath,
                WorldBuilder.PlayerPrefabPath, WorldBuilder.GroundPrefabPath, WorldBuilder.CameraPrefabPath,
                WorldBuilder.SpawnPrefabPath, WorldBuilder.SharedCatalogPath, WorldBuilder.SharedTuningPath,
                "Assets/JoyJoey/Scenes/CircoOnirico_Sandbox.unity"
            };
            foreach (string path in paths) Assert.That(File.Exists(path), Is.True, path);
            var before = paths.ToDictionary(p => p, File.ReadAllBytes);
            Scene previous = SceneManager.GetActiveScene();
            Scene unsaved = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .FirstOrDefault(s => string.IsNullOrEmpty(s.path));
            bool createdHere = false;
            bool borrowedBatchScene = false;
            if (unsaved.IsValid())
            {
                // Unity cannot create another additive scene while an untitled scene is open.
                // The batch test runner owns its scene and may have hidden fixture objects in it.
                // Borrow it without saving, closing or removing any of those objects.
                borrowedBatchScene = Application.isBatchMode;
                if (!unsaved.isDirty && !borrowedBatchScene)
                    Assert.Ignore("This fixture requires a saved scene or an already dirty untitled scene; it does not modify the user's clean scene.");
            }
            else
            {
                unsaved = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                createdHere = true;
            }
            SceneManager.SetActiveScene(unsaved);
            if (createdHere || borrowedBatchScene) EditorSceneManager.MarkSceneDirty(unsaved);
            try
            {
                WorldBuilder.EnsureAssets();
                WorldBuilder.EnsureAssets();
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(unsaved));
                Assert.That(unsaved.isDirty, Is.True);
                foreach (string path in paths) CollectionAssert.AreEqual(before[path], File.ReadAllBytes(path), path);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (createdHere) EditorSceneManager.CloseScene(unsaved, true);
                // A borrowed runner scene keeps its dirty state and all its original objects.
            }
        }
    }
}
