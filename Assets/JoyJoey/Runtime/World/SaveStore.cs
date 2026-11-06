using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace JoyJoey.Game
{
    /// <summary>One campaign slot, independent of options and sandbox practice.</summary>
    public sealed class SaveStore
    {
        readonly HashSet<string> _allowedScenes;
        readonly string _directory;
        public string FilePath { get; }
        public string BackupPath => FilePath + ".bak";
        public SaveStore(string directory, IEnumerable<string> allowedScenes = null)
        {
            _directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
            FilePath = Path.Combine(_directory, "SaveGame.json");
            _allowedScenes = new HashSet<string>(allowedScenes ?? new[] { "Nivel_Prototipo" }, StringComparer.Ordinal);
        }

        public bool IsValid(SavedGameData data)
        {
            if (data == null || data.Version != SavedGameData.CurrentVersion || !data.HasCheckpoint ||
                string.IsNullOrEmpty(data.SceneName) || !_allowedScenes.Contains(data.SceneName) ||
                (data.Facing != -1 && data.Facing != 1) || !Finite(data.Position.x) || !Finite(data.Position.y) ||
                Mathf.Abs(data.Position.x) > 100000 || Mathf.Abs(data.Position.y) > 100000) return false;
            if (data.ProgressFlags == null) return true;
            if (data.ProgressFlags.Length > 4096) return false;
            foreach (string flag in data.ProgressFlags)
                if (string.IsNullOrWhiteSpace(flag) || flag.Length > 256) return false;
            return true;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        bool Read(string path, out SavedGameData data)
        {
            data = null;
            if (!File.Exists(path)) return false;
            try
            {
                if (new FileInfo(path).Length > 2 * 1024 * 1024) return false;
                data = JsonUtility.FromJson<SavedGameData>(File.ReadAllText(path));
                if (!IsValid(data)) { data = null; return false; }
                if (data.ProgressFlags == null) data.ProgressFlags = Array.Empty<string>();
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            { data = null; return false; }
        }

        public bool TryLoad(out SavedGameData data, out string error)
        {
            error = "";
            if (Read(FilePath, out data)) return true;
            if (Read(BackupPath, out data))
            {
                error = "Se recuperó la última copia de la partida.";
                return true;
            }
            if (File.Exists(FilePath) || File.Exists(BackupPath))
                error = "No se pudo leer la partida. Los archivos anteriores se conservarán al empezar otra.";
            return false;
        }

        public bool TrySave(SavedGameData data, out string error)
        {
            error = "";
            if (!IsValid(data)) { error = "No se pudo guardar una posición válida de la partida."; return false; }
            string temporary = Path.Combine(_directory, "SaveGame." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
                if (File.Exists(FilePath))
                {
                    bool validPrevious = Read(FilePath, out _);
                    if (!validPrevious)
                        File.Copy(FilePath, Path.Combine(_directory, "SaveGame.unreadable." + Guid.NewGuid().ToString("N") + ".json"));
                    File.Replace(temporary, FilePath, validPrevious ? BackupPath : null);
                }
                else File.Move(temporary, FilePath);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                error = "No se pudo guardar la partida. Revisá el espacio y los permisos de la carpeta de guardado.";
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
