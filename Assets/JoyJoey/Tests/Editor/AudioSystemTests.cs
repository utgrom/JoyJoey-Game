using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoyJoey.Game.Audio;
using JoyJoey.Game.Editor;
using JoyJoey.Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class AudioPreferencesTests
    {
        string _directory, _testRoot;
        AudioPreferencesStore _store;

        [SetUp]
        public void Setup()
        {
            _testRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Validation", "TestAudioSettings"));
            _directory = Path.Combine(_testRoot, Guid.NewGuid().ToString("N"));
            _store = new AudioPreferencesStore(_directory);
        }

        [TearDown]
        public void Cleanup()
        {
            string resolved = Path.GetFullPath(_directory);
            if (!resolved.StartsWith(_testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a directory outside this test's settings root.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }

        static AudioPreferences Settings(float master = .7f)
        {
            var data = AudioPreferences.Defaults();
            data.Master = master;
            data.Music = .31f;
            data.Sfx = .62f;
            data.Ui = .43f;
            data.Mono = true;
            data.OutputDeviceId = "test-output-id";
            return data;
        }

        [Test]
        public void MissingSettingsDoNotCreateAFileJustByLoading()
        {
            Assert.That(_store.TryLoad(out _, out _), Is.False);
            Assert.That(File.Exists(_store.FilePath), Is.False);
        }

        [Test]
        public void AllAudioPreferencesSurviveARestartOfTheStore()
        {
            var original = Settings();
            Assert.That(_store.TrySave(original, out var error), Is.True, error);
            var reopened = new AudioPreferencesStore(_directory);
            Assert.That(reopened.TryLoad(out var loaded, out error), Is.True, error);
            Assert.That(loaded.Master, Is.EqualTo(.7f).Within(.0001f));
            Assert.That(loaded.Music, Is.EqualTo(.31f).Within(.0001f));
            Assert.That(loaded.Sfx, Is.EqualTo(.62f).Within(.0001f));
            Assert.That(loaded.Ui, Is.EqualTo(.43f).Within(.0001f));
            Assert.That(loaded.Mono, Is.True);
            Assert.That(loaded.OutputDeviceId, Is.EqualTo(original.OutputDeviceId));
        }

        [Test]
        public void SettingsUseASeparateFileAndLeaveCampaignBytesUntouched()
        {
            Directory.CreateDirectory(_directory);
            string campaign = Path.Combine(_directory, "SaveGame.json");
            const string campaignBytes = "campaign fixture: unchanged";
            File.WriteAllText(campaign, campaignBytes);
            Assert.That(_store.TrySave(Settings(), out var error), Is.True, error);
            Assert.That(Path.GetFileName(_store.FilePath), Is.EqualTo("AudioSettings.json"));
            Assert.That(File.ReadAllText(campaign), Is.EqualTo(campaignBytes));
        }

        [Test]
        public void ReplacingSettingsKeepsTheLastCompleteVersionAsBackup()
        {
            Assert.That(_store.TrySave(Settings(.7f), out var error), Is.True, error);
            string previous = File.ReadAllText(_store.FilePath);
            Assert.That(_store.TrySave(Settings(.2f), out error), Is.True, error);
            Assert.That(File.ReadAllText(_store.BackupPath), Is.EqualTo(previous));
            Assert.That(_store.TryLoad(out var loaded, out error), Is.True, error);
            Assert.That(loaded.Master, Is.EqualTo(.2f).Within(.0001f));
        }

        [Test]
        public void ACorruptPrimaryRecoversTheBackupInsteadOfLosingPreferences()
        {
            Assert.That(_store.TrySave(Settings(.7f), out var error), Is.True, error);
            Assert.That(_store.TrySave(Settings(.2f), out error), Is.True, error);
            File.WriteAllText(_store.FilePath, "{not json");
            Assert.That(_store.TryLoad(out var loaded, out error), Is.True, error);
            Assert.That(loaded.Master, Is.EqualTo(.7f).Within(.0001f));
            Assert.That(loaded.OutputDeviceId, Is.EqualTo("test-output-id"));
        }

        [Test]
        public void SavingAfterCorruptionPreservesTheExistingValidBackup()
        {
            Assert.That(_store.TrySave(Settings(.7f), out var error), Is.True, error);
            Assert.That(_store.TrySave(Settings(.2f), out error), Is.True, error);
            string backup = File.ReadAllText(_store.BackupPath);
            File.WriteAllText(_store.FilePath, "{not json");
            Assert.That(_store.TrySave(Settings(.4f), out error), Is.True, error);
            Assert.That(File.ReadAllText(_store.BackupPath), Is.EqualTo(backup));
            Assert.That(_store.TryLoad(out var loaded, out error), Is.True, error);
            Assert.That(loaded.Master, Is.EqualTo(.4f).Within(.0001f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-.1f)]
        [TestCase(1.1f)]
        public void InvalidVolumesCannotReplaceValidSettings(float invalid)
        {
            Assert.That(_store.TrySave(Settings(), out var error), Is.True, error);
            string previous = File.ReadAllText(_store.FilePath);
            var next = Settings();
            next.Master = invalid;
            Assert.That(_store.TrySave(next, out _), Is.False);
            Assert.That(File.ReadAllText(_store.FilePath), Is.EqualTo(previous));
        }

        [Test]
        public void UnknownSchemaCannotOverwriteTheCurrentSettings()
        {
            Assert.That(_store.TrySave(Settings(), out var error), Is.True, error);
            string previous = File.ReadAllText(_store.FilePath);
            var future = Settings();
            future.Version = 999;
            Assert.That(_store.TrySave(future, out _), Is.False);
            Assert.That(File.ReadAllText(_store.FilePath), Is.EqualTo(previous));
        }

        [Test]
        public void CopyDoesNotShareMutableVolumeState()
        {
            var original = Settings();
            var copy = original.Copy();
            copy.Master = .1f;
            copy.Mono = false;
            copy.OutputDeviceId = "other";
            Assert.That(original.Master, Is.EqualTo(.7f));
            Assert.That(original.Mono, Is.True);
            Assert.That(original.OutputDeviceId, Is.EqualTo("test-output-id"));
        }
    }

    public sealed class AudioSignalTests
    {
        [TestCase(1, 0)]
        [TestCase(.5f, -6.0206f)]
        [TestCase(.1f, -20)]
        [TestCase(0, -80)]
        public void VolumeUsesADecibelScaleAndZeroIsSilent(float linear, float expected)
            => Assert.That(GameAudio.LinearToDecibels(linear), Is.EqualTo(expected).Within(.002f));

        [Test]
        public void StereoRemainsUnchangedWhenMonoIsDisabled()
        {
            var signal = new[] { .8f, -.2f, -.4f, .2f };
            var original = (float[])signal.Clone();
            var processor = new AudioOutputProcessor { Mono = false };
            processor.Process(signal, 2);
            CollectionAssert.AreEqual(original, signal);
        }

        [Test]
        public void MonoProducesTheAverageOnBothChannelsWithoutIncreasingPeak()
        {
            var signal = new[] { .8f, -.2f, -.4f, .2f };
            var processor = new AudioOutputProcessor { Mono = true };
            processor.Process(signal, 2);
            Assert.That(signal[0], Is.EqualTo(.3f).Within(.00001f));
            Assert.That(signal[1], Is.EqualTo(.3f).Within(.00001f));
            Assert.That(signal[2], Is.EqualTo(-.1f).Within(.00001f));
            Assert.That(signal[3], Is.EqualTo(-.1f).Within(.00001f));
        }

        [Test]
        public void MonoAlsoFoldsSurroundChannelsAndSupportsAnAlreadyMonoSignal()
        {
            var surround = new[] { 1f, .5f, 0, -.5f, -1, 0 };
            var processor = new AudioOutputProcessor { Mono = true };
            processor.Process(surround, 6);
            foreach (float sample in surround) Assert.That(sample, Is.EqualTo(0).Within(.00001f));
            var mono = new[] { .2f, -.3f };
            processor.Process(mono, 1);
            CollectionAssert.AreEqual(new[] { .2f, -.3f }, mono);
        }
    }

    public sealed class AudioAuthoringTests
    {
        [Test]
        public void AudioRigSerializesSharedMixerGroupsSnapshotsAndTestClips()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AudioBuilder.RigPrefabPath);
            Assert.That(prefab, Is.Not.Null, "Generate stage 3 assets before checking authoring.");
            var rig = prefab.GetComponent<GameAudio>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.Mixer, Is.SameAs(AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioBuilder.MixerPath)));
            Assert.That(rig.MasterGroup, Is.Not.Null);
            Assert.That(rig.MusicGroup, Is.Not.Null);
            Assert.That(rig.SfxGroup, Is.Not.Null);
            Assert.That(rig.UiGroup, Is.Not.Null);
            Assert.That(rig.MusicGroup.name, Is.EqualTo("MusicEffects"));
            Assert.That(rig.SfxGroup.name, Is.EqualTo("SfxEffects"));
            Assert.That(rig.UiGroup.name, Is.EqualTo("UiEffects"));
            Assert.That(rig.NormalSnapshot, Is.Not.Null);
            Assert.That(rig.PauseSnapshot, Is.Not.Null);
            Assert.That(rig.CutsceneSnapshot, Is.Not.Null);
            foreach (var clip in new[] { rig.TestMusicClip, rig.TestSfxClip, rig.TestUiClip, rig.UiMoveClip, rig.UiConfirmClip })
            {
                Assert.That(clip, Is.Not.Null);
                Assert.That(clip.samples, Is.GreaterThan(0));
                Assert.That(AssetDatabase.Contains(clip), Is.True);
            }
            Assert.That(prefab.GetComponentsInChildren<AudioListener>(true), Is.Empty,
                "The scene camera owns its listener; the persistent rig must not add another one.");
        }

        [Test]
        public void MixerHasFourExposedUserVolumesAndIndependentEffectSnapshots()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioBuilder.MixerPath);
            Assert.That(mixer, Is.Not.Null);
            foreach (string parameter in new[] { "MasterVolume", "MusicVolume", "SfxVolume", "UiVolume" })
                Assert.That(mixer.GetFloat(parameter, out _), Is.True, "Missing exposed volume: " + parameter);
            foreach (string snapshot in new[] { "Normal", "Pause", "Cutscene" })
                Assert.That(mixer.FindSnapshot(snapshot), Is.Not.Null, snapshot);
            var names = mixer.FindMatchingGroups("").Select(g => g.name).ToArray();
            foreach (string group in new[] { "Music", "SFX", "UI", "MusicEffects", "SfxEffects", "UiEffects" })
                CollectionAssert.Contains(names, group);
        }

        [Test]
        public void AudioPageHasEditableNavigableControlsForEverySetting()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AudioBuilder.OptionsPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var page = prefab.GetComponent<AudioOptionsController>();
            Assert.That(page, Is.Not.Null);
            foreach (var slider in new[] { page.MasterSlider, page.MusicSlider, page.SfxSlider, page.UiSlider })
            {
                Assert.That(slider, Is.Not.Null);
                Assert.That(slider.minValue, Is.EqualTo(0));
                Assert.That(slider.maxValue, Is.EqualTo(1));
                Assert.That(slider.navigation.mode, Is.Not.EqualTo(Navigation.Mode.None));
            }
            Assert.That(page.SpeakerModeDropdown, Is.Not.Null);
            Assert.That(page.SpeakerModeDropdown.options.Count, Is.EqualTo(2));
            Assert.That(page.OutputDeviceDropdown, Is.Not.Null);
            foreach (var button in new[] { page.TestMusicButton, page.TestSfxButton, page.TestUiButton,
                page.RefreshDevicesButton, page.DefaultsButton })
            {
                Assert.That(button, Is.Not.Null);
                Assert.That(button.navigation.mode, Is.Not.EqualTo(Navigation.Mode.None));
            }
            Assert.That(page.StatusText, Is.Not.Null);
            Assert.That(page.BackButton.gameObject.activeSelf, Is.False, "Back is presented in the shared contextual footer.");
        }
    }
}
