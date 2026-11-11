using System.Linq;
using JoyJoey.Game.Cinematics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class CutsceneExampleAssetsTests
    {
        const string PrefabPath = "Assets/JoyJoey/Prefabs/CinematicaVinetas.prefab";
        const string ScenePath = "Assets/JoyJoey/Scenes/Cinematica_Ejemplo.unity";

        [Test]
        public void ExampleTimelineBindsTenFullAspectPanelsAndABackdrop()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<CutscenePlayer>(), Is.Not.Null);

            var director = prefab.GetComponent<PlayableDirector>();
            Assert.That(director.playOnAwake, Is.False);
            Assert.That(director.playableAsset, Is.Not.Null);
            Assert.That(director.playableAsset.duration, Is.EqualTo(25).Within(.001));

            var targets = director.playableAsset.outputs
                .Select(output => director.GetGenericBinding(output.sourceObject) as GameObject)
                .ToArray();
            Assert.That(targets, Has.Length.EqualTo(11));
            Assert.That(targets.All(target => target && !target.activeSelf), Is.True);
            Assert.That(targets.Select(target => target.name), Does.Contain("Fondo"));
            for (int panel = 1; panel <= 10; panel++)
            {
                var target = targets.Single(item => item.name == $"Viñeta {panel:00}");
                var image = target.GetComponent<Image>();
                Assert.That(image.preserveAspect, Is.True);
                Assert.That(image.raycastTarget, Is.False);
                Assert.That(AssetDatabase.GetAssetPath(image.sprite),
                    Is.EqualTo($"Assets/JoyJoey/Cinematics/Vinetas/{panel:00}.png"));
            }
        }

        [Test]
        public void ExampleSceneCanBeOpenedFromTheEditor()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null);
        }
    }
}
