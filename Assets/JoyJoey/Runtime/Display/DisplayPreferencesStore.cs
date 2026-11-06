using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace JoyJoey.Game.Display
{
    /// <summary>Only confirmed display settings reach the primary file. Preview has a separate recovery journal.</summary>
    public sealed class DisplayPreferencesStore
    {
        readonly string _directory;
        public string FilePath { get; }
        public string BackupPath => FilePath + ".bak";
        public string PendingPath => FilePath + ".pending";
        public DisplayPreferencesStore(string directory)
        {
            _directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
            FilePath = Path.Combine(_directory, "DisplaySettings.json");
        }
        public static bool IsValid(DisplayPreferences data) => DisplayPreferences.IsValid(data);

        bool Read(string path, out DisplayPreferences data)
        {
            data = null;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 65536) return false;
                string json = File.ReadAllText(path);
                // JsonUtility supplies missing fields from their defaults. A truncated-but-valid
                // JSON object must not silently turn VSync off or remove a confirmed FPS cap.
                foreach (string field in new[] { "Version", "WindowMode", "WindowWidth", "WindowHeight", "RenderScale",
                    "FpsCap", "VSync", "Brightness", "Contrast", "Gamma" })
                    if (!json.Contains("\"" + field + "\"")) return false;
                data = JsonUtility.FromJson<DisplayPreferences>(json);
                if (IsValid(data)) return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException) { }
            data = null;
            return false;
        }

        public bool TryLoad(out DisplayPreferences data, out string error)
        {
            error = "";
            if (Read(FilePath, out data)) return true;
            if (Read(BackupPath, out data)) { error = "Se recuperó la última copia de los ajustes de pantalla."; return true; }
            data = DisplayPreferences.Defaults();
            if (File.Exists(FilePath) || File.Exists(BackupPath)) error = "Se restauraron los valores de pantalla predeterminados.";
            return false;
        }

        public bool TrySave(DisplayPreferences data, out string error) => WriteAtomic(FilePath, data, true, out error);
        public bool BeginPreview(DisplayPreferences confirmed, out string error) => WriteAtomic(PendingPath, confirmed, false, out error);

        bool WriteAtomic(string path, DisplayPreferences data, bool keepBackup, out string error)
        {
            error = "";
            if (!IsValid(data)) { error = "Los ajustes de pantalla no son válidos."; return false; }
            string temporary = Path.Combine(_directory, "DisplaySettings." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    bool valid = Read(path, out _);
                    if (!valid && keepBackup)
                        File.Copy(path, Path.Combine(_directory, "DisplaySettings.unreadable." + Guid.NewGuid().ToString("N") + ".json"));
                    File.Replace(temporary, path, valid && keepBackup ? BackupPath : null);
                }
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            { error = "No se pudieron guardar los ajustes de pantalla."; return false; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public bool ClearPreview(out string error)
        {
            error = "";
            try { if (File.Exists(PendingPath)) File.Delete(PendingPath); return true; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { error = "No se pudo limpiar la recuperación de pantalla."; return false; }
        }

        /// <summary>
        /// A candidate is never written to the primary before confirmation. If confirmation saved but
        /// journal cleanup was interrupted, the newer primary remains authoritative.
        /// </summary>
        public bool RecoverPending(out DisplayPreferences data, out string error)
        {
            data = null;
            error = "";
            if (!File.Exists(PendingPath)) return false;
            // The journal contains the settings in use immediately before the preview;
            // it can be newer than the normal backup if the primary was later damaged.
            if (!Read(FilePath, out data) && !Read(PendingPath, out data) && !Read(BackupPath, out data))
                data = DisplayPreferences.Defaults();
            error = "Se recuperó la última configuración de pantalla confirmada.";
            if (!ClearPreview(out string cleanup)) error += " " + cleanup;
            return true;
        }
    }
}
