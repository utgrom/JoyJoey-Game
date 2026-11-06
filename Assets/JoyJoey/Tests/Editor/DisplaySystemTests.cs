using System;
using System.IO;
using JoyJoey.Game.Display;
using NUnit.Framework;
using UnityEngine;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class DisplayPreferencesTests
    {
        string _directory, _testRoot;
        DisplayPreferencesStore _store;

        [SetUp]
        public void Setup()
        {
            _testRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Validation", "TestDisplaySettings"));
            _directory = Path.Combine(_testRoot, Guid.NewGuid().ToString("N"));
            _store = new DisplayPreferencesStore(_directory);
        }

        [TearDown]
        public void Cleanup()
        {
            string resolved = Path.GetFullPath(_directory);
            if (!resolved.StartsWith(_testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a directory outside this test's settings root.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
            if (File.Exists(resolved)) File.Delete(resolved);
        }

        static DisplayPreferences Settings(float gamma = 1.2f) => new DisplayPreferences
        {
            WindowMode = DisplayWindowMode.Windowed,
            WindowWidth = 1280, WindowHeight = 720,
            RenderScale = .67f, FpsCap = 165, VSync = true,
            Brightness = -.12f, Contrast = 1.3f, Gamma = gamma
        };

        static void EqualSettings(DisplayPreferences actual, DisplayPreferences expected)
        {
            Assert.That(JsonUtility.ToJson(actual), Is.EqualTo(JsonUtility.ToJson(expected)));
        }

        [Test]
        public void MissingSettingsLoadDefaultsWithoutWritingAnyFiles()
        {
            Assert.That(_store.TryLoad(out var loaded, out _), Is.False);
            EqualSettings(loaded, DisplayPreferences.Defaults());
            Assert.That(Directory.Exists(_directory), Is.False);
            Assert.That(_store.RecoverPending(out _, out _), Is.False);
        }

        [Test]
        public void AllConfirmedPreferencesSurviveAStoreRestart()
        {
            var expected = Settings();
            Assert.That(_store.TrySave(expected, out var error), Is.True, error);
            var reopened = new DisplayPreferencesStore(_directory);
            Assert.That(reopened.TryLoad(out var loaded, out error), Is.True, error);
            EqualSettings(loaded, expected);
        }

        [Test]
        public void DisplayChangesLeaveCampaignAndAudioFilesUntouched()
        {
            Directory.CreateDirectory(_directory);
            string audio = Path.Combine(_directory, "AudioSettings.json");
            string campaign = Path.Combine(_directory, "SaveGame.json");
            File.WriteAllText(audio, "audio fixture"); File.WriteAllText(campaign, "campaign fixture");
            Assert.That(_store.TrySave(Settings(), out var error), Is.True, error);
            Assert.That(_store.BeginPreview(Settings(), out error), Is.True, error);
            Assert.That(_store.ClearPreview(out error), Is.True, error);
            Assert.That(File.ReadAllText(audio), Is.EqualTo("audio fixture"));
            Assert.That(File.ReadAllText(campaign), Is.EqualTo("campaign fixture"));
            Assert.That(Path.GetFileName(_store.FilePath), Is.EqualTo("DisplaySettings.json"));
        }

        [Test]
        public void SavingAgainKeepsThePreviousConfirmedVersionAsBackup()
        {
            Assert.That(_store.TrySave(Settings(1.2f), out var error), Is.True, error);
            string previous = File.ReadAllText(_store.FilePath);
            Assert.That(_store.TrySave(Settings(.8f), out error), Is.True, error);
            Assert.That(File.ReadAllText(_store.BackupPath), Is.EqualTo(previous));
            Assert.That(_store.TryLoad(out var loaded, out error), Is.True, error);
            Assert.That(loaded.Gamma, Is.EqualTo(.8f));
        }

        [TestCase("{not json")]
        [TestCase("{}")]
        [TestCase("{\"Version\":1,\"WindowMode\":0,\"WindowWidth\":1280,\"WindowHeight\":720,\"RenderScale\":1,\"Contrast\":1,\"Gamma\":1}")]
        public void DamagedOrIncompletePrimaryRecoversTheCompleteBackup(string corrupt)
        {
            Assert.That(_store.TrySave(Settings(1.2f), out var error), Is.True, error);
            Assert.That(_store.TrySave(Settings(.8f), out error), Is.True, error);
            File.WriteAllText(_store.FilePath, corrupt);
            Assert.That(_store.TryLoad(out var loaded, out error), Is.True, error);
            EqualSettings(loaded, Settings(1.2f));
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void SavingAfterCorruptionPreservesTheLastValidBackup()
        {
            Assert.That(_store.TrySave(Settings(1.2f), out var error), Is.True, error);
            Assert.That(_store.TrySave(Settings(.8f), out error), Is.True, error);
            string previousBackup = File.ReadAllText(_store.BackupPath);
            File.WriteAllText(_store.FilePath, "{damaged");
            Assert.That(_store.TrySave(Settings(1.1f), out error), Is.True, error);
            Assert.That(File.ReadAllText(_store.BackupPath), Is.EqualTo(previousBackup));
            Assert.That(_store.TryLoad(out var loaded, out error), Is.True, error);
            EqualSettings(loaded, Settings(1.1f));
        }

        [Test]
        public void BeginningAPreviewNeverChangesTheConfirmedFile()
        {
            Assert.That(_store.TrySave(Settings(), out var error), Is.True, error);
            string confirmed = File.ReadAllText(_store.FilePath);
            Assert.That(_store.BeginPreview(Settings(), out error), Is.True, error);
            Assert.That(File.ReadAllText(_store.FilePath), Is.EqualTo(confirmed));
            Assert.That(File.Exists(_store.PendingPath), Is.True);
        }

        [Test]
        public void InterruptedPreviewRestoresTheLastConfirmedSettings()
        {
            Assert.That(_store.TrySave(Settings(), out var error), Is.True, error);
            Assert.That(_store.BeginPreview(Settings(), out error), Is.True, error);
            var reopened = new DisplayPreferencesStore(_directory);
            Assert.That(reopened.RecoverPending(out var recovered, out error), Is.True, error);
            EqualSettings(recovered, Settings());
            Assert.That(File.Exists(_store.PendingPath), Is.False);
        }

        [Test]
        public void FirstRunPreviewRecoversItsJournalEvenWithoutAPrimaryFile()
        {
            Assert.That(_store.BeginPreview(Settings(), out var error), Is.True, error);
            Assert.That(File.Exists(_store.FilePath), Is.False);
            Assert.That(_store.RecoverPending(out var recovered, out error), Is.True, error);
            EqualSettings(recovered, Settings());
        }

        [Test]
        public void ConfirmedPrimaryWinsIfProcessStoppedBeforeCleaningTheJournal()
        {
            Assert.That(_store.TrySave(Settings(1.2f), out var error), Is.True, error);
            Assert.That(_store.BeginPreview(Settings(1.2f), out error), Is.True, error);
            Assert.That(_store.TrySave(Settings(.8f), out error), Is.True, error);
            Assert.That(_store.RecoverPending(out var recovered, out error), Is.True, error);
            EqualSettings(recovered, Settings(.8f));
        }

        [Test]
        public void ConfirmedJournalWinsOverOlderBackupIfPrimaryIsDamagedDuringPreview()
        {
            Assert.That(_store.TrySave(Settings(1.2f), out var error), Is.True, error);
            Assert.That(_store.TrySave(Settings(.8f), out error), Is.True, error);
            Assert.That(_store.BeginPreview(Settings(.8f), out error), Is.True, error);
            File.WriteAllText(_store.FilePath, "{damaged");
            Assert.That(_store.RecoverPending(out var recovered, out error), Is.True, error);
            EqualSettings(recovered, Settings(.8f));
        }

        [Test]
        public void DamagedJournalFallsBackToValidConfirmedPrimary()
        {
            Assert.That(_store.TrySave(Settings(), out var error), Is.True, error);
            File.WriteAllText(_store.PendingPath, "{damaged");
            Assert.That(_store.RecoverPending(out var recovered, out error), Is.True, error);
            EqualSettings(recovered, Settings());
            Assert.That(File.Exists(_store.PendingPath), Is.False);
        }

        [Test]
        public void ClearingAPreviewIsIdempotentAndDoesNotChangeConfirmedSettings()
        {
            Assert.That(_store.TrySave(Settings(), out var error), Is.True, error);
            Assert.That(_store.BeginPreview(Settings(), out error), Is.True, error);
            Assert.That(_store.ClearPreview(out error), Is.True, error);
            Assert.That(_store.ClearPreview(out error), Is.True, error);
            Assert.That(_store.RecoverPending(out _, out _), Is.False);
            Assert.That(_store.TryLoad(out var loaded, out error), Is.True, error);
            EqualSettings(loaded, Settings());
        }

        [TestCase("width")]
        [TestCase("height")]
        [TestCase("mode")]
        [TestCase("version")]
        [TestCase("scale")]
        [TestCase("fps")]
        [TestCase("brightness")]
        [TestCase("contrast")]
        [TestCase("gamma")]
        [TestCase("nan")]
        [TestCase("infinity")]
        public void InvalidPreferencesCannotReplaceConfirmedSettings(string invalid)
        {
            Assert.That(_store.TrySave(Settings(), out var error), Is.True, error);
            string original = File.ReadAllText(_store.FilePath);
            var candidate = Settings();
            switch (invalid)
            {
                case "width": candidate.WindowWidth = 0; break;
                case "height": candidate.WindowHeight = 20000; break;
                case "mode": candidate.WindowMode = (DisplayWindowMode)99; break;
                case "version": candidate.Version = 99; break;
                case "scale": candidate.RenderScale = .1f; break;
                case "fps": candidate.FpsCap = -1; break;
                case "brightness": candidate.Brightness = -.7f; break;
                case "contrast": candidate.Contrast = 0; break;
                case "gamma": candidate.Gamma = 0; break;
                case "nan": candidate.Gamma = float.NaN; break;
                case "infinity": candidate.RenderScale = float.PositiveInfinity; break;
            }
            Assert.That(_store.TrySave(candidate, out error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(File.ReadAllText(_store.FilePath), Is.EqualTo(original));
            Assert.That(_store.BeginPreview(candidate, out _), Is.False);
        }

        [Test]
        public void AnUnwritableSettingsDirectoryReportsFailureWithoutThrowing()
        {
            Directory.CreateDirectory(_testRoot);
            File.WriteAllText(_directory, "A file occupies this directory path.");
            Assert.That(_store.TrySave(Settings(), out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(_store.BeginPreview(Settings(), out error), Is.False);
        }

        [Test]
        public void NormalizingForASmallerMonitorPreservesInternalScaleAndDoesNotMutateTheSavedObject()
        {
            var saved = Settings(); saved.WindowWidth = 3840; saved.WindowHeight = 2160;
            var normalized = GameDisplay.NormalizeForDisplay(saved, 1280, 720);
            Assert.That(normalized.WindowWidth, Is.EqualTo(1280));
            Assert.That(normalized.WindowHeight, Is.EqualTo(720));
            Assert.That(normalized.RenderScale, Is.EqualTo(.67f));
            Assert.That(saved.WindowWidth, Is.EqualTo(3840));
            Assert.That(saved.WindowHeight, Is.EqualTo(2160));
        }

        [Test]
        public void SmallDesktopCanStillUseItsNativeSize()
        {
            var normalized = GameDisplay.NormalizeForDisplay(Settings(), 800, 480);
            Assert.That(normalized.WindowWidth, Is.EqualTo(800));
            Assert.That(normalized.WindowHeight, Is.EqualTo(480));
            Assert.That(DisplayPreferences.IsValid(normalized), Is.True);
        }

        [TestCase(0)] [TestCase(30)] [TestCase(60)] [TestCase(90)]
        [TestCase(120)] [TestCase(144)] [TestCase(165)] [TestCase(240)]
        public void EveryOfferedFrameCapCanBeSaved(int fps)
        {
            var candidate = Settings(); candidate.FpsCap = fps;
            Assert.That(_store.TrySave(candidate, out var error), Is.True, error);
            Assert.That(_store.TryLoad(out var loaded, out error), Is.True, error);
            Assert.That(loaded.FpsCap, Is.EqualTo(fps));
        }

        [Test]
        public void EditingACopyDoesNotMutateConfirmedPreferences()
        {
            var confirmed = Settings();
            var draft = confirmed.Copy();
            draft.WindowMode = DisplayWindowMode.Borderless;
            draft.WindowWidth = 1920; draft.Gamma = .9f; draft.VSync = false;
            EqualSettings(confirmed, Settings());
        }
    }
}
