using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace JoyJoey.Sandbox
{
    /// <summary>Saved binding overrides for Gameplay. Menu and pause bindings are never edited here.</summary>
    public static class GameplayControls
    {
        public sealed class BindingRow
        {
            readonly string _name;
            readonly bool _initiallyRevealed;
            internal readonly string Action, Key, DefaultPath;
            internal readonly int Index;
            public bool IsGamepad { get; }
            public bool IsEditable { get; }
            public bool IsRevealed => !Reveals.TryGetValue(Action, out bool revealed) ? _initiallyRevealed : revealed;
            public string Name => IsRevealed ? _name : "(UNUSED)";

            internal BindingRow(string action, string key, string name, string path, int index,
                bool gamepad, bool editable, bool initiallyRevealed = true)
            {
                Action = action;
                Key = key;
                DefaultPath = path;
                Index = index;
                IsGamepad = gamepad;
                IsEditable = editable;
                _name = name;
                _initiallyRevealed = initiallyRevealed;
            }
        }

        [Serializable] sealed class SavedBinding { public string Key, Path; }
        [Serializable] sealed class SavedControls
        {
            public int Version = 1;
            public float LeftStickDeadzone = -1;
            public List<SavedBinding> Bindings = new List<SavedBinding>();
        }

        const string KeyboardPath = "<Keyboard>/", MousePath = "<Mouse>/", PadPath = "<Gamepad>/";
        const float UseDefaultDeadzone = -1;
        static readonly (string Action, string Name, bool Revealed)[] EditableActions =
        {
            (nameof(ActionId.Jump), "Saltar", true), (nameof(ActionId.Basic), "Ataque básico", true),
            (nameof(ActionId.Rag), "Ataque Rag", true), (nameof(ActionId.Trick), "Trick", true),
            (nameof(ActionId.Dash), "Dash", true), (nameof(ActionId.Transform), "Transformación", true),
            (nameof(ActionId.PreviousSuit), "Traje anterior", true), (nameof(ActionId.NextSuit), "Traje siguiente", true)
        };
        static readonly Dictionary<string, bool> Reveals = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> PadButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder", "leftTrigger",
            "rightTrigger", "leftStickPress", "rightStickPress", "select", "dpad/up", "dpad/down", "dpad/left", "dpad/right"
        };
        static readonly HashSet<string> MouseButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "leftButton", "rightButton", "middleButton", "forwardButton", "backButton" };

        static List<BindingRow> _rows;
        static IReadOnlyList<BindingRow> _keyboardRows, _gamepadRows;
        static HashSet<string> _fixedPaths;
        static Dictionary<string, string> _overrides;
        static string _file;
        static float _savedDeadzone = UseDefaultDeadzone, _defaultDeadzone;

        public static event Action Changed;
        internal static event Action BindingsChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntime()
        {
            _rows = null;
            _keyboardRows = _gamepadRows = null;
            _fixedPaths = null;
            _overrides = null;
            _file = null;
            _savedDeadzone = UseDefaultDeadzone;
            Reveals.Clear();
            Changed = null;
            BindingsChanged = null;
        }

        public static float LeftStickDeadzonePercent
        {
            get { EnsureLoaded(); return _savedDeadzone == UseDefaultDeadzone ? _defaultDeadzone : _savedDeadzone; }
        }

        public static IReadOnlyList<BindingRow> GetRows(bool gamepad)
        {
            EnsureLoaded();
            return gamepad ? _gamepadRows : _keyboardRows;
        }

        /// <summary>Call from progression when a previously hidden action is discovered.</summary>
        public static void SetActionRevealed(string actionName, bool revealed)
        {
            if (string.IsNullOrWhiteSpace(actionName)) return;
            if (Reveals.TryGetValue(actionName, out bool current) && current == revealed) return;
            Reveals[actionName] = revealed;
            Changed?.Invoke();
        }

        public static string GetPath(BindingRow row)
        {
            EnsureLoaded();
            return EffectivePath(row, _overrides);
        }

        public static string GetLabel(BindingRow row, bool playStation) => FormatPath(GetPath(row), playStation);

        internal static string ActionLabel(string action, bool gamepad, bool playStation)
        {
            var row = FindActionRow(action, gamepad);
            return row == null ? null : GetLabel(row, playStation);
        }

        /// <summary>Effective path for the first editable binding of an action and device profile.</summary>
        public static string ActionPath(string action, bool gamepad)
        {
            var row = FindActionRow(action, gamepad);
            return row == null ? null : GetPath(row);
        }

        /// <summary>Effective path for a movement composite part: up, down, left or right.</summary>
        public static string DirectionPath(string part, bool gamepad)
        {
            string key = "Move:" + gamepad + ":" + part;
            foreach (var row in GetRows(gamepad))
                if (Same(row.Key, key)) return GetPath(row);
            return null;
        }

        static BindingRow FindActionRow(string action, bool gamepad)
        {
            foreach (var row in GetRows(gamepad))
                if (row.Action == action && row.IsEditable) return row;
            return null;
        }

        public static bool TryRebind(BindingRow row, InputControl control, out string error)
        {
            EnsureLoaded();
            error = "";
            if (row == null || !row.IsEditable || !_rows.Contains(row))
            { error = "Este control no se puede editar."; return false; }
            string path = PathForButton(control);
            if (path == null || !IsDevicePath(path, row.IsGamepad))
            { error = row.IsGamepad ? "Pulsa un botón del mando." : "Pulsa una tecla o un botón del ratón."; return false; }
            if (!IsAllowedPath(path, row.IsGamepad))
            { error = "Ese botón está reservado o no se puede asignar."; return false; }

            string previous = EffectivePath(row, _overrides);
            if (Same(path, previous)) return true;
            var next = new Dictionary<string, string>(_overrides);
            foreach (var other in GetRows(row.IsGamepad))
            {
                if (!other.IsEditable || other == row || !Same(EffectivePath(other, _overrides), path)) continue;
                Assign(next, other, previous);
                break;
            }
            Assign(next, row, path);
            return Commit(next, _savedDeadzone, bindingsChanged: true, out error);
        }

        public static bool RestoreDefaults(bool gamepad, out string error)
        {
            var rows = GetRows(gamepad);
            var next = new Dictionary<string, string>(_overrides);
            bool bindingsChanged = false;
            foreach (var row in rows) bindingsChanged |= next.Remove(row.Key);
            float deadzone = gamepad ? UseDefaultDeadzone : _savedDeadzone;
            return Commit(next, deadzone, bindingsChanged, out error);
        }

        public static bool TrySetLeftStickDeadzone(float percent, out string error)
        {
            EnsureLoaded();
            return Commit(_overrides, StickInputSettings.SanitizeDeadzonePercent(percent), bindingsChanged: false, out error);
        }

        internal static void ApplyBindings(InputActionAsset asset)
        {
            EnsureLoaded();
            var map = asset.FindActionMap("Gameplay");
            if (map == null) return;
            foreach (var row in _rows)
            {
                if (!row.IsEditable) continue;
                var action = map.FindAction(row.Action);
                if (action == null || row.Index >= action.bindings.Count || !Same(action.bindings[row.Index].path, row.DefaultPath)) continue;
                var binding = action.bindings[row.Index];
                if (_overrides.TryGetValue(row.Key, out string path)) action.ApplyBindingOverride(row.Index, path);
                else if (binding.overridePath != null) action.RemoveBindingOverride(row.Index);
            }
        }

        static bool Commit(Dictionary<string, string> next, float deadzone, bool bindingsChanged, out string error)
        {
            error = "";
            if (!bindingsChanged && deadzone == _savedDeadzone) return true;
            if (!WriteSettings(next, deadzone, out error)) return false;
            _overrides = next;
            _savedDeadzone = deadzone;
            if (bindingsChanged) BindingsChanged?.Invoke();
            Changed?.Invoke();
            return true;
        }

        static void EnsureLoaded()
        {
            if (_rows != null) return;
            var template = Resources.Load<InputActionAsset>("JoyJoeyControls");
            bool fallback = !template;
            if (fallback) template = JoyInput.CreateDefaultAsset();
            var map = template.FindActionMap("Gameplay", true);
            var move = map.FindAction("Move", true);
            _rows = new List<BindingRow>();
            for (int i = 0; i < move.bindings.Count; i++)
            {
                var binding = move.bindings[i];
                bool gamepad = IsDevicePath(binding.path, true);
                string label = binding.name?.ToLowerInvariant() switch
                {
                    "up" => "Arriba", "down" => "Abajo", "left" => "Izquierda", "right" => "Derecha", _ => null
                };
                if (binding.isPartOfComposite && label != null && IsDevicePath(binding.path, gamepad))
                    _rows.Add(new BindingRow("Move", "Move:" + gamepad + ":" + binding.name.ToLowerInvariant(), label,
                        binding.path, i, gamepad, true));
            }
            if (move.bindings.Any(binding => Same(binding.path, PadPath + "leftStick")))
                _rows.Add(new BindingRow("Move", "Move:Stick", "Movimiento analógico", PadPath + "leftStick", -1, true, false));
            foreach (var entry in EditableActions)
            {
                var action = map.FindAction(entry.Action);
                if (action == null) continue;
                int keyboardCount = 0, padCount = 0;
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i];
                    bool gamepad = IsDevicePath(binding.path, true);
                    if (binding.isComposite || binding.isPartOfComposite || !IsDevicePath(binding.path, gamepad)) continue;
                    int ordinal = gamepad ? padCount++ : keyboardCount++;
                    string name = ordinal == 0 ? entry.Name : entry.Name + " (alternativo)";
                    _rows.Add(new BindingRow(action.name, action.name + ":" + gamepad + ":" + ordinal,
                        name, binding.path, i, gamepad, true, entry.Revealed));
                }
            }

            _keyboardRows = _rows.Where(row => !row.IsGamepad).ToList().AsReadOnly();
            _gamepadRows = _rows.Where(row => row.IsGamepad).ToList().AsReadOnly();
            _fixedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { KeyboardPath + "escape", PadPath + "start" };
            foreach (var action in map.actions)
                for (int i = 0; i < action.bindings.Count; i++)
                    if (!_rows.Any(row => row.IsEditable && row.Action == action.name && row.Index == i))
                        _fixedPaths.Add(action.bindings[i].path);

            if (fallback)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(template);
                else UnityEngine.Object.DestroyImmediate(template);
            }
            var defaults = Resources.Load<StickInputSettings>(StickInputSettings.ResourceName);
            _defaultDeadzone = StickInputSettings.SanitizeDeadzonePercent(defaults ? defaults.LeftStickDeadzonePercent : StickInputSettings.DefaultDeadzonePercent);
            _overrides = new Dictionary<string, string>();
            if (!Application.isPlaying) return;
            string[] args = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(args, "-joySettingsDirectory");
            string directory = option >= 0 && option + 1 < args.Length ? args[option + 1] : Application.persistentDataPath;
            _file = Path.Combine(directory, "ControlsSettings.json");
            if (!TryLoadSettings(_file)) TryLoadSettings(_file + ".bak");
        }

        static bool TryLoadSettings(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 65536) return false;
                var saved = JsonUtility.FromJson<SavedControls>(File.ReadAllText(path));
                if (saved == null || saved.Version != 1 || saved.Bindings == null ||
                    saved.LeftStickDeadzone != UseDefaultDeadzone &&
                    saved.LeftStickDeadzone != StickInputSettings.SanitizeDeadzonePercent(saved.LeftStickDeadzone)) return false;
                var next = new Dictionary<string, string>();
                foreach (var binding in saved.Bindings)
                {
                    if (binding == null) return false;
                    var row = _rows.Find(item => item.IsEditable && item.Key == binding.Key);
                    if (row == null) continue;
                    if (!IsAllowedPath(binding.Path, row.IsGamepad) || next.ContainsKey(row.Key)) return false;
                    Assign(next, row, binding.Path);
                }
                var used = new HashSet<string>(_fixedPaths, StringComparer.OrdinalIgnoreCase);
                foreach (var row in _rows)
                    if (row.IsEditable && !used.Add(EffectivePath(row, next))) return false;
                _overrides = next;
                _savedDeadzone = saved.LeftStickDeadzone;
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            { return false; }
        }

        static bool WriteSettings(Dictionary<string, string> bindings, float deadzone, out string error)
        {
            error = "";
            if (_file == null) return true;
            string temporary = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(_file);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var data = new SavedControls { LeftStickDeadzone = deadzone };
                foreach (var pair in bindings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    data.Bindings.Add(new SavedBinding { Key = pair.Key, Path = pair.Value });
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
                if (File.Exists(_file)) File.Replace(temporary, _file, _file + ".bak");
                else File.Move(temporary, _file);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                error = "No se pudieron guardar los controles.";
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        static void Assign(Dictionary<string, string> bindings, BindingRow row, string path)
        {
            if (Same(path, row.DefaultPath)) bindings.Remove(row.Key);
            else bindings[row.Key] = path;
        }
        static string EffectivePath(BindingRow row, Dictionary<string, string> bindings)
            => bindings.TryGetValue(row.Key, out string path) ? path : row.DefaultPath;
        static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        static bool StartsWith(string path, string prefix) => path?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true;
        static bool IsDevicePath(string path, bool gamepad) => gamepad ? StartsWith(path, PadPath) :
            StartsWith(path, KeyboardPath) || StartsWith(path, MousePath);

        static string PathForButton(InputControl control)
        {
            if (control is KeyControl) return KeyboardPath + control.name;
            if (!(control is ButtonControl)) return null;
            if (control.device is Mouse) return MousePath + control.name;
            if (control.device is Gamepad pad) return PadPath + (control.parent == pad.dpad ? "dpad/" : "") + control.name;
            return null;
        }

        static bool IsAllowedPath(string path, bool gamepad)
        {
            if (!IsDevicePath(path, gamepad) || _fixedPaths.Contains(path)) return false;
            if (gamepad) return PadButtons.Contains(path.Substring(PadPath.Length));
            if (StartsWith(path, MousePath)) return MouseButtons.Contains(path.Substring(MousePath.Length));
            string key = path.Substring(KeyboardPath.Length);
            return key.Length > 0 && key.All(character => char.IsLetterOrDigit(character)) &&
                !Same(key, "anyKey") && !Same(key, "imeSelected");
        }

        static string FormatPath(string path, bool playStation)
        {
            if (StartsWith(path, PadPath))
                return path.Substring(PadPath.Length).ToLowerInvariant() switch
                {
                    "buttonsouth" => playStation ? "Cruz" : "A", "buttoneast" => playStation ? "Círculo" : "B",
                    "buttonwest" => playStation ? "Cuadrado" : "X", "buttonnorth" => playStation ? "Triángulo" : "Y",
                    "leftshoulder" => playStation ? "L1" : "LB", "rightshoulder" => playStation ? "R1" : "RB",
                    "lefttrigger" => playStation ? "L2" : "LT", "righttrigger" => playStation ? "R2" : "RT",
                    "leftstickpress" => playStation ? "L3" : "LS", "rightstickpress" => playStation ? "R3" : "RS",
                    "leftstick" => "Stick izquierdo", "select" => playStation ? "Crear / Compartir" : "Vista",
                    "dpad/up" => "Cruceta arriba", "dpad/down" => "Cruceta abajo",
                    "dpad/left" => "Cruceta izquierda", "dpad/right" => "Cruceta derecha", _ => path
                };
            if (StartsWith(path, MousePath))
                return path.Substring(MousePath.Length).ToLowerInvariant() switch
                {
                    "leftbutton" => "Clic izquierdo", "rightbutton" => "Clic derecho", "middlebutton" => "Clic central",
                    "forwardbutton" => "Ratón adelante", "backbutton" => "Ratón atrás", _ => path
                };
            return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        }
    }
}
