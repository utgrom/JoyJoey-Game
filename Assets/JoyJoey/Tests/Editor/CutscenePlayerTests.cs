using System.Collections.Generic;
using System.Reflection;
using JoyJoey.Game.Cinematics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;

namespace JoyJoey.Tests
{
    public sealed class CutscenePlayerTests
    {
        sealed class TestClip : PlayableAsset
        {
            public override double duration => 5;

            public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
            {
                var playable = Playable.Create(graph);
                ScriptPlayableOutput.Create(graph, "Test").SetSourcePlayable(playable);
                return playable;
            }
        }

        readonly List<CutscenePlayer> _players = new List<CutscenePlayer>();
        TestClip _clip;

        [SetUp]
        public void SetUp() => _clip = ScriptableObject.CreateInstance<TestClip>();

        [TearDown]
        public void TearDown()
        {
            foreach (var player in _players)
            {
                player.Cancel();
                Object.DestroyImmediate(player.gameObject);
            }
            _players.Clear();
            Object.DestroyImmediate(_clip);
        }

        [Test]
        public void PauseAndResumeKeepTheActiveSlotAndTimelinePosition()
        {
            var player = CreatePlayer();
            var other = CreatePlayer();

            Assert.That(player.TryPlay(), Is.True);
            Assert.That(player.Director.timeUpdateMode, Is.EqualTo(DirectorUpdateMode.UnscaledGameTime));
            player.Director.time = 2;
            player.Pause();

            Assert.That(player.IsPaused, Is.True);
            Assert.That(CutscenePlayer.Active, Is.SameAs(player));
            Assert.That(other.TryPlay(), Is.False);
            player.Resume();
            Assert.That(player.IsPlaying, Is.True);
            Assert.That(player.Director.time, Is.EqualTo(2).Within(.001));
        }

        [TestCase(false, CutsceneEndReason.Completed)]
        [TestCase(true, CutsceneEndReason.Skipped)]
        public void CompletionReleasesTheSlotBeforeItsOneTimeFollowUp(bool skip, CutsceneEndReason reason)
        {
            var player = CreatePlayer();
            var followUp = CreatePlayer();
            var reasons = new List<CutsceneEndReason>();
            int finished = 0;
            player.Ended += reasons.Add;
            player.OnFinished.AddListener(() =>
            {
                finished++;
                Assert.That(CutscenePlayer.Active, Is.Null);
                Assert.That(followUp.TryPlay(), Is.True);
            });

            Assert.That(player.TryPlay(), Is.True);
            if (skip) player.Skip();
            else
            {
                player.Director.time = player.Director.duration;
                typeof(CutscenePlayer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(player, null);
            }
            player.Skip();

            Assert.That(finished, Is.EqualTo(1));
            Assert.That(reasons, Is.EqualTo(new[] { reason }));
            Assert.That(CutscenePlayer.Active, Is.SameAs(followUp));
        }

        [Test]
        public void CancellingDoesNotRunProgressionEvents()
        {
            var player = CreatePlayer();
            CutsceneEndReason? reason = null;
            int finished = 0;
            player.Ended += result => reason = result;
            player.OnFinished.AddListener(() => finished++);

            Assert.That(player.TryPlay(), Is.True);
            player.Cancel();

            Assert.That(reason, Is.EqualTo(CutsceneEndReason.Canceled));
            Assert.That(finished, Is.Zero);
            Assert.That(CutscenePlayer.Active, Is.Null);
        }

        CutscenePlayer CreatePlayer()
        {
            var player = new GameObject("Cutscene Test").AddComponent<CutscenePlayer>();
            _players.Add(player);
            player.Director.playableAsset = _clip;
            return player;
        }
    }
}
