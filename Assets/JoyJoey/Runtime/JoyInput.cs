using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Processors;

namespace JoyJoey.Sandbox
{
    /// <summary>
    /// Captures edges at the Input System update rate and drains them once into the
    /// fixed combat simulation. The editable Resources asset is cloned per owner.
    /// PassThrough buttons let us aggregate keyboard and pad without false releases.
    /// </summary>
    public sealed class JoyInput : IDisposable
    {
        private sealed class ButtonState
        {
            public readonly HashSet<InputControl> HeldControls = new HashSet<InputControl>();
            public readonly HashSet<InputControl> BlockedControls = new HashSet<InputControl>();
            public InputAction Action;
            public double PressedAt, LastTapAt = -100;
            public int Taps;
            public bool Held => HeldControls.Count != 0;
        }

        private readonly Dictionary<ActionId, ButtonState> buttons = new Dictionary<ActionId, ButtonState>();
        private readonly List<InputCommand> pending = new List<InputCommand>(32);
        private readonly Dictionary<InputAction, Action<InputAction.CallbackContext>> callbacks =
            new Dictionary<InputAction, Action<InputAction.CallbackContext>>();
        private readonly InputAction move;
        private readonly StickInputSettings stickSettings;
        private bool disposed, focused = true, gameplayPaused;
        public InputActionAsset Asset { get; private set; }
        public string Device { get; private set; } = "TECLADO";
        public bool GameplayPaused => gameplayPaused;

        public JoyInput(StickInputSettings settings = null)
        {
            stickSettings = settings ? settings : Resources.Load<StickInputSettings>(StickInputSettings.ResourceName);
            var template = Resources.Load<InputActionAsset>("JoyJoeyControls");
            Asset = template != null ? UnityEngine.Object.Instantiate(template) : CreateDefaultAsset();
            Asset.name = "JoyJoeyControls (runtime)";
            Asset.hideFlags = HideFlags.DontSave;
            var map = Asset.FindActionMap("Gameplay", true);
            move = map.FindAction("Move", true);
            move.performed += OnMove;
            foreach (ActionId id in Enum.GetValues(typeof(ActionId)))
            {
                var action = map.FindAction(id.ToString());
                if (action == null) continue;
                buttons.Add(id, new ButtonState { Action = action });
                ActionId captured = id;
                Action<InputAction.CallbackContext> callback = context => OnButton(captured, context);
                callbacks.Add(action, callback);
                action.performed += callback;
            }
            Application.focusChanged += OnFocusChanged;
            InputSystem.onDeviceChange += OnDeviceChange;
            Asset.Enable();
        }

        public InputFrame ConsumeFrame()
        {
            if (disposed) throw new ObjectDisposedException(nameof(JoyInput));
            var frame = new InputFrame
            {
                Move = focused ? ReadDirection() : Vector2.zero,
                JumpHeld = Held(ActionId.Jump),
                BasicHeld = Held(ActionId.Basic),
                RagHeld = Held(ActionId.Rag),
                BasicHeldSeconds = HeldSeconds(ActionId.Basic),
                RagHeldSeconds = HeldSeconds(ActionId.Rag),
                Device = Device
            };
            frame.Commands.AddRange(pending);
            foreach(var pair in buttons)if(Held(pair.Key))frame.HeldActions.Add(pair.Key);
            pending.Clear();
            return frame;
        }

        /// <summary>Discard unconsumed edges, retaining the physical held state.</summary>
        public void ClearPending() => pending.Clear();

        /// <summary>
        /// A pause cancels action intent without synthesizing a Release. Movement stays live.
        /// On resume, each already-actuated action control must return below its release
        /// threshold before it can begin another press; other controls remain independent.
        /// </summary>
        public void SetGameplayPaused(bool paused)
        {
            if (disposed) throw new ObjectDisposedException(nameof(JoyInput));
            if (gameplayPaused == paused) return;
            gameplayPaused = paused;
            ResetActionStates(blockHeldControls: !paused);
        }

        void ResetActionStates(bool blockHeldControls)
        {
            pending.Clear();
            foreach (var state in buttons.Values)
            {
                state.HeldControls.Clear();
                state.BlockedControls.Clear();
                state.PressedAt = 0;
                state.LastTapAt = -100;
                state.Taps = 0;
                if (!blockHeldControls) continue;
                foreach (var control in state.Action.controls)
                    if (control is AxisControl axis && axis.ReadValue() >= .4f)
                        state.BlockedControls.Add(control);
            }
        }

        private bool Held(ActionId id) => focused && !gameplayPaused && buttons.TryGetValue(id, out var state) && state.Held;
        private float HeldSeconds(ActionId id) => focused && !gameplayPaused && buttons.TryGetValue(id, out var state) && state.Held
            ? Mathf.Max(0, (float)(InputState.currentTime - state.PressedAt)) : 0;

        private float LeftStickDeadzonePercent => stickSettings ? stickSettings.LeftStickDeadzonePercent : StickInputSettings.DefaultDeadzonePercent;
        private Vector2 ReadDirection() => ReadDirectionAtEvent(ActionId.Basic);

        private Vector2 ReadDirectionAtEvent(ActionId action)
        {
            // Multiple controls may change in one hardware event. Read their current
            // state directly: its Move callback can run after Attack, including
            // when the direction is released in the same hardware event.
            // Resolve paths from the editable bindings rather than hardcoding WASD.
            Vector2 best = Vector2.zero;
            float bestStrength = 0;
            int bestPriority = -1;
            bool hasUnsupportedComposite = false;
            bool dash = action == ActionId.Dash;
            var bindings = move.bindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                if (binding.isComposite && binding.effectivePath.StartsWith("2DVector", StringComparison.OrdinalIgnoreCase))
                {
                    Vector2 candidate = Vector2.zero;
                    for (int part = index + 1; part < bindings.Count && bindings[part].isPartOfComposite; part++)
                    {
                        float amount = 0;
                        foreach (var control in move.controls)
                            if (control is AxisControl axis && InputControlPath.Matches(bindings[part].effectivePath, control))
                                amount = Mathf.Max(amount, axis.ReadValue());
                        switch (bindings[part].name.ToLowerInvariant())
                        {
                            case "up": candidate.y += amount; break;
                            case "down": candidate.y -= amount; break;
                            case "right": candidate.x += amount; break;
                            case "left": candidate.x -= amount; break;
                        }
                    }
                    candidate = Vector2.ClampMagnitude(candidate, 1);
                    Consider(candidate, candidate.magnitude, 2, ref best, ref bestStrength, ref bestPriority);
                }
                else if (!binding.isPartOfComposite && !binding.isComposite)
                {
                    foreach (var control in move.controls)
                    {
                        if (!(control is Vector2Control vector) || !InputControlPath.Matches(binding.effectivePath, control)) continue;
                        if (vector.device is Gamepad pad && vector == pad.leftStick)
                        {
                            // The shared percentage is the only deadzone for this source. Reading
                            // unprocessed state avoids stacking the control and binding processors.
                            Vector2 raw = vector.ReadUnprocessedValue();
                            float strength = StickDirections.Strength(raw, LeftStickDeadzonePercent);
                            Vector2 direction = dash ? StickDirections.ForDash(raw, LeftStickDeadzonePercent) :
                                StickDirections.ForMovement(raw, LeftStickDeadzonePercent);
                            Consider(direction, strength, 1, ref best, ref bestStrength, ref bestPriority);
                            continue;
                        }
                        Vector2 value = vector.ReadValue();
                        if (!string.IsNullOrEmpty(binding.processors) && binding.processors.IndexOf("stickDeadzone", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var deadzone = new StickDeadzoneProcessor
                            {
                                min = Parameter(binding.processors, "min", InputSystem.settings.defaultDeadzoneMin),
                                max = Parameter(binding.processors, "max", InputSystem.settings.defaultDeadzoneMax)
                            };
                            value = deadzone.Process(value, vector);
                        }
                        value = Vector2.ClampMagnitude(value, 1);
                        Consider(value, value.magnitude, 2, ref best, ref bestStrength, ref bestPriority);
                    }
                }
                else if (binding.isComposite) hasUnsupportedComposite = true;
            }
            // Custom composites retain the action's own evaluation as a fallback.
            // Default supported bindings must never inherit an earlier direction.
            if (hasUnsupportedComposite)
            {
                Vector2 fallback = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1);
                var active = move.activeControl;
                if (active?.device is Gamepad pad && IsLeftStickControl(active, pad))
                {
                    Vector2 raw = pad.leftStick.ReadUnprocessedValue();
                    float strength = StickDirections.Strength(raw, LeftStickDeadzonePercent);
                    fallback = dash ? StickDirections.ForDash(raw, LeftStickDeadzonePercent) :
                        StickDirections.ForMovement(raw, LeftStickDeadzonePercent);
                    Consider(fallback, strength, 1, ref best, ref bestStrength, ref bestPriority);
                }
                else Consider(fallback, fallback.magnitude, 0, ref best, ref bestStrength, ref bestPriority);
            }
            return Vector2.ClampMagnitude(best, 1);
        }

        static void Consider(Vector2 direction, float strength, int priority, ref Vector2 best,
            ref float bestStrength, ref int bestPriority)
        {
            if (strength <= 0 || direction.sqrMagnitude <= 0) return;
            if (strength > bestStrength + .00001f || Mathf.Abs(strength - bestStrength) <= .00001f && priority > bestPriority)
            { best = direction; bestStrength = strength; bestPriority = priority; }
        }

        static bool IsLeftStickControl(InputControl control, Gamepad pad)
        {
            for (var current = control; current != null; current = current.parent)
                if (current == pad.leftStick) return true;
            return false;
        }

        private static float Parameter(string processors, string name, float fallback)
        {
            int start = processors.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return fallback;
            start += name.Length + 1;
            int end = start;
            while (end < processors.Length && processors[end] != ',' && processors[end] != ')') end++;
            return float.TryParse(processors.Substring(start, end - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }

        private void OnMove(InputAction.CallbackContext context)
        {
            if (!focused) return;
            var control = context.control;
            if (control.device is Gamepad pad && IsLeftStickControl(control, pad))
            {
                if (StickDirections.Strength(pad.leftStick.ReadUnprocessedValue(), LeftStickDeadzonePercent) > 0)
                    TrackDevice(pad);
                return;
            }
            if (control is Vector2Control vector && vector.ReadValue().sqrMagnitude > .01f ||
                control is AxisControl axis && Mathf.Abs(axis.ReadValue()) > .01f)
                TrackDevice(control.device);
        }

        private void OnButton(ActionId id, InputAction.CallbackContext context)
        {
            if (!focused || gameplayPaused) return;
            ButtonState state = buttons[id];
            float value = context.ReadValue<float>();
            if (state.BlockedControls.Contains(context.control))
            {
                // The release only rearms this physical control. It must not complete an
                // attack/charge that was cancelled at the pause boundary.
                if (value < .4f) state.BlockedControls.Remove(context.control);
                return;
            }
            bool wasHeld = state.Held;
            bool sourceHeld = state.HeldControls.Contains(context.control);
            bool pressed = value >= (sourceHeld ? .4f : .55f);
            if (pressed) state.HeldControls.Add(context.control);
            else state.HeldControls.Remove(context.control);
            if (wasHeld == state.Held) return;

            InputEdge edge = state.Held ? InputEdge.Press : InputEdge.Release;
            if (state.Held)
            {
                state.PressedAt = context.time;
                state.Taps = context.time - state.LastTapAt <= .30 ? state.Taps + 1 : 1;
                state.LastTapAt = context.time;
                TrackDevice(context.control.device);
            }
            if (pending.Count == 128) pending.RemoveAt(0);
            pending.Add(new InputCommand(id, edge, ReadDirectionAtEvent(id), context.time, state.Taps));
        }

        private void TrackDevice(InputDevice device)
        {
            if (device is Gamepad)
            {
                string layout = device.layout;
                Device = layout.IndexOf("Dual", StringComparison.OrdinalIgnoreCase) >= 0
                    || layout.IndexOf("PlayStation", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "MANDO PS" : "MANDO";
            }
            else if (device is Keyboard) Device = "TECLADO";
        }

        private void OnFocusChanged(bool focused)
        {
            this.focused = focused;
            if (disposed) return;
            // OS focus changes must never leave a charge/jump/dash held forever.
            // Regaining focus follows the same fresh-press rule as leaving a menu pause.
            ResetActionStates(blockHeldControls: focused && !gameplayPaused);
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (disposed || (change != InputDeviceChange.Removed && change != InputDeviceChange.Disconnected
                && change != InputDeviceChange.Disabled)) return;
            foreach (var pair in buttons)
            {
                bool wasHeld = pair.Value.Held;
                pair.Value.HeldControls.RemoveWhere(control => control.device == device);
                pair.Value.BlockedControls.RemoveWhere(control => control.device == device);
                if (wasHeld && !pair.Value.Held)
                    pending.Add(new InputCommand(pair.Key, InputEdge.Release, Vector2.zero, InputState.currentTime));
            }
        }

        public string BindingLabel(ActionId action)
        {
            bool pad = Device != "TECLADO";
            bool ps = Device == "MANDO PS";
            switch (action)
            {
                case ActionId.Jump: return pad ? (ps ? "Cruz" : "A") : "Space";
                case ActionId.Basic: return pad ? (ps ? "Cuadrado" : "X") : "J";
                case ActionId.Rag: return pad ? (ps ? "Triangulo" : "Y") : "K";
                case ActionId.Trick: return pad ? (ps ? "Circulo" : "B") : "L";
                case ActionId.Dash: return pad ? (ps ? "R1" : "RB") : "Shift";
                case ActionId.Transform: return pad ? (ps ? "L1" : "LB") : "H";
                case ActionId.PreviousSuit: return pad ? (ps ? "L2" : "LT") : "Q";
                case ActionId.NextSuit: return pad ? (ps ? "R2" : "RT") : "E";
                case ActionId.Reset: return pad ? "Select" : "R";
                case ActionId.TogglePause: return pad ? "Start" : "Esc";
                case ActionId.ToggleHelp: return "F1";
                case ActionId.ToggleDebug: return "F2";
                case ActionId.ToggleDummy: return "F3";
                case ActionId.ToggleDamage: return "F4";
                case ActionId.ToggleEnemy: return "F6";
                case ActionId.ToggleSlow: return "F5";
                case ActionId.Step: return ".";
                default: return action.ToString();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Application.focusChanged -= OnFocusChanged;
            InputSystem.onDeviceChange -= OnDeviceChange;
            move.performed -= OnMove;
            foreach (var pair in callbacks) pair.Key.performed -= pair.Value;
            Asset.Disable();
            pending.Clear();
            if (Application.isPlaying) UnityEngine.Object.Destroy(Asset);
            else UnityEngine.Object.DestroyImmediate(Asset);
            Asset = null;
        }

        /// <summary>Fallback also documents the complete binding contract for tools/tests.</summary>
        public static InputActionAsset CreateDefaultAsset()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "JoyJoeyControls";
            var map = new InputActionMap("Gameplay");
            asset.AddActionMap(map);
            var movement = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            movement.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            movement.AddBinding("<Gamepad>/leftStick", processors: "stickDeadzone(min=0.22,max=0.95)");
            movement.AddBinding("<Gamepad>/dpad");
            AddButton(map, ActionId.Jump, "space", "buttonSouth");
            AddButton(map, ActionId.Basic, "j", "buttonWest");
            AddButton(map, ActionId.Rag, "k", "buttonNorth");
            AddButton(map, ActionId.Trick, "l", "buttonEast");
            AddButton(map, ActionId.Dash, "leftShift", "rightShoulder");
            map.FindAction("Dash").AddBinding("<Keyboard>/rightShift");
            AddButton(map, ActionId.Transform, "h", "leftShoulder");
            AddButton(map, ActionId.PreviousSuit, "q", "leftTrigger");
            AddButton(map, ActionId.NextSuit, "e", "rightTrigger");
            AddButton(map, ActionId.Reset, "r", "select");
            AddButton(map, ActionId.ToggleDebug, "f2");
            AddButton(map, ActionId.ToggleHelp, "f1");
            AddButton(map, ActionId.TogglePause, null, "start");
            AddButton(map, ActionId.Step, "period");
            AddButton(map, ActionId.ToggleDummy, "f3");
            AddButton(map, ActionId.ToggleSlow, "f5");
            AddButton(map, ActionId.ToggleDamage, "f4");
            AddButton(map, ActionId.ToggleEnemy, "f6");
            asset.AddControlScheme("Keyboard").WithRequiredDevice("<Keyboard>");
            asset.AddControlScheme("Gamepad").WithRequiredDevice("<Gamepad>");
            return asset;
        }

        private static void AddButton(InputActionMap map, ActionId id, string key, string pad = null)
        {
            // Per-control float values permit trigger hysteresis and source aggregation.
            var action = map.AddAction(id.ToString(), InputActionType.PassThrough, expectedControlLayout: "Button");
            if (!string.IsNullOrEmpty(key)) action.AddBinding("<Keyboard>/" + key);
            if (pad != null) action.AddBinding("<Gamepad>/" + pad);
        }
    }
}
