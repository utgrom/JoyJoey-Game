using System;
using System.Globalization;
using System.Linq;
using JoyJoey.Sandbox;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace JoyJoey.Game.UI
{
    /// <summary>Applies the gameplay stick deadzone to menu navigation and its release gate.</summary>
    public static class StickMenuInput
    {
        public const string ProcessorName = "joyJoeyMenuStick";
        public const string BindingName = "Joy Joey left stick";
        static StickInputSettings _settings;
        static bool _settingsLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSettings() { _settings = null; _settingsLoaded = false; }

        public static float DeadzonePercent
        {
            get
            {
                if (!_settingsLoaded)
                {
                    _settings = Resources.Load<StickInputSettings>(StickInputSettings.ResourceName);
                    _settingsLoaded = true;
                }
                return StickInputSettings.SanitizeDeadzonePercent(_settings ? _settings.LeftStickDeadzonePercent : StickInputSettings.DefaultDeadzonePercent);
            }
        }

        /// <summary>
        /// Configure only a disabled runtime copy, before assigning it to the UI module.
        /// Original keyboard, dpad, right-stick and Submit bindings remain intact.
        /// </summary>
        public static InputAction Configure(InputActionAsset ownedActions, StickInputSettings settings = null)
        {
            if (!ownedActions) throw new ArgumentNullException(nameof(ownedActions));
            if (ownedActions.enabled) throw new InvalidOperationException("Configure menu stick bindings before enabling the runtime actions.");
            if (InputSystem.TryGetProcessor(ProcessorName) != typeof(LeftMenuStickProcessor))
                InputSystem.RegisterProcessor<LeftMenuStickProcessor>(ProcessorName);
            var navigate = ownedActions.FindAction("UI/Navigate");
            if (navigate == null) return null;
            if (navigate.type != InputActionType.Value)
            {
                // PassThrough would allow a filtered-zero stick event to overwrite a held dpad.
                // The module remaps the runtime copy by map/action name, so a new Value action
                // retains its public role while letting the strongest source drive navigation.
                var map = navigate.actionMap;
                var bindings = navigate.bindings.ToArray();
                string processors = navigate.processors, interactions = navigate.interactions;
                var mask = navigate.bindingMask;
                navigate.RemoveAction();
                navigate = map.AddAction("Navigate", InputActionType.Value, interactions: interactions,
                    processors: processors, expectedControlLayout: "Vector2");
                navigate.bindingMask = mask;
                foreach (var binding in bindings) navigate.AddBinding(binding);
            }

            int custom = -1;
            for (int i = 0; i < navigate.bindings.Count; i++)
            {
                var binding = navigate.bindings[i];
                if (binding.name == BindingName) { custom = i; continue; }
                if (IsLeftStickPath(binding.path))
                    navigate.ApplyBindingOverride(i, new InputBinding { overridePath = "" });
            }
            float percent = settings ? StickInputSettings.SanitizeDeadzonePercent(settings.LeftStickDeadzonePercent) : DeadzonePercent;
            if (percent >= 100)
            {
                // Omit the control entirely: even a full-deflection faulty stick cannot win
                // Input System's source arbitration against the dpad when it is disabled.
                if (custom >= 0) navigate.ApplyBindingOverride(custom, new InputBinding { overridePath = "" });
                return navigate;
            }
            string processor = settings ? ProcessorName + "(deadzonePercent=" + percent.ToString("R", CultureInfo.InvariantCulture) + ")" : ProcessorName;
            if (custom < 0)
                navigate.AddBinding(new InputBinding { name = BindingName, path = "<Gamepad>/leftStick", processors = processor, groups = "Gamepad" });
            else
                navigate.ApplyBindingOverride(custom, new InputBinding { overridePath = "<Gamepad>/leftStick", overrideProcessors = processor });
            return navigate;
        }

        static bool IsLeftStickPath(string path)
            => !string.IsNullOrEmpty(path) && path.IndexOf("/leftStick", StringComparison.OrdinalIgnoreCase) >= 0;

        public static Vector2 ReadLeftMenuVector(Gamepad pad) => ReadLeftMenuVector(pad, DeadzonePercent);
        public static Vector2 ReadLeftMenuVector(Gamepad pad, float deadzonePercent)
            => pad == null ? Vector2.zero : StickDirections.ForDash(pad.leftStick.ReadUnprocessedValue(), deadzonePercent);

        public static bool IsLeftStickControl(InputControl control, Gamepad pad)
        {
            if (pad == null) return false;
            for (var current = control; current != null; current = current.parent)
                if (current == pad.leftStick) return true;
            return false;
        }

        /// <summary>Physical menu gate, excluding left-stick aliases when that axis is filtered out.</summary>
        public static bool AnyInputHeld() => AnyInputHeld(DeadzonePercent);

        public static bool AnyInputHeld(float leftStickDeadzonePercent)
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.isPressed) return true;
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed ||
                mouse.forwardButton.isPressed || mouse.backButton.isPressed)) return true;
            foreach (var pad in Gamepad.all)
            {
                if (ReadLeftMenuVector(pad, leftStickDeadzonePercent) != Vector2.zero || pad.rightStick.ReadValue().sqrMagnitude > .0225f) return true;
                foreach (var control in pad.allControls)
                    if (!IsLeftStickControl(control, pad) && control is ButtonControl button && button.isPressed) return true;
            }
            return false;
        }
    }

    /// <summary>Menu-only eight-way reading; raw input avoids applying Unity's built-in deadzone twice.</summary>
    public sealed class LeftMenuStickProcessor : InputProcessor<Vector2>
    {
        // A negative value uses the shared resource; an explicit value supports isolated profiles.
        public float deadzonePercent = -1;
        public override Vector2 Process(Vector2 value, InputControl control)
        {
            var raw = control is Vector2Control vector ? vector.ReadUnprocessedValue() : value;
            float percent = deadzonePercent < 0 ? StickMenuInput.DeadzonePercent : deadzonePercent;
            return StickDirections.ForDash(raw, percent) * StickDirections.Strength(raw, percent);
        }
    }
}
