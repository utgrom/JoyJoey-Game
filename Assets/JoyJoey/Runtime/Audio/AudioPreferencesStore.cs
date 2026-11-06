using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace JoyJoey.Game.Audio
{
    /// <summary>Audio preferences never share a file with campaign progress.</summary>
    public sealed class AudioPreferencesStore
    {
        readonly string _directory;
        public string FilePath { get; }
        public string BackupPath => FilePath + ".bak";
        public AudioPreferencesStore(string directory)
        {
            _directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
            FilePath = Path.Combine(_directory, "AudioSettings.json");
        }
        public static bool IsValid(AudioPreferences data)
        {
            if (data == null || data.Version != 1 || data.OutputDeviceId == null || data.OutputDeviceId.Length > 2048) return false;
            foreach (var value in new[] { data.Master, data.Music, data.Sfx, data.Ui })
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1) return false;
            return true;
        }
        bool Read(string path, out AudioPreferences data)
        {
            data = null;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 65536) return false;
                string json = File.ReadAllText(path);
                // Defaults on fields are useful to callers, but an empty file is not a saved profile.
                if (!json.Contains("\"Version\"") || !json.Contains("\"Master\"")) return false;
                data = JsonUtility.FromJson<AudioPreferences>(json);
                if (IsValid(data)) return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException) { }
            data = null;
            return false;
        }
        public bool TryLoad(out AudioPreferences data, out string error)
        {
            error = "";
            if (Read(FilePath, out data)) return true;
            if (Read(BackupPath, out data)) { error = "Se recuperó la última copia de los ajustes de audio."; return true; }
            data = AudioPreferences.Defaults();
            if (File.Exists(FilePath) || File.Exists(BackupPath)) error = "Se restauraron los valores de audio predeterminados.";
            return false;
        }
        public bool TrySave(AudioPreferences data, out string error)
        {
            error = "";
            if (!IsValid(data)) { error = "Los ajustes de audio no son válidos."; return false; }
            string temporary = Path.Combine(_directory, "AudioSettings." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
                if (File.Exists(FilePath))
                {
                    bool valid = Read(FilePath, out _);
                    if (!valid) File.Copy(FilePath, Path.Combine(_directory, "AudioSettings.unreadable." + Guid.NewGuid().ToString("N") + ".json"));
                    File.Replace(temporary, FilePath, valid ? BackupPath : null);
                }
                else File.Move(temporary, FilePath);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            { error = "No se pudieron guardar los ajustes de audio."; return false; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
