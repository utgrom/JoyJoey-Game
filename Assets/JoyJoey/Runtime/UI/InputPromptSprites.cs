using System;
using System.Collections.Generic;
using UnityEngine;

namespace JoyJoey.Game.UI
{
    /// <summary>Resolves Input System binding paths to replaceable button artwork.</summary>
    public static class InputPromptSprites
    {
        const string Root = "InputPrompts/";
        const string Keyboard = "<keyboard>/", Mouse = "<mouse>/", Gamepad = "<gamepad>/";
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearCache() => Cache.Clear();

        /// <summary>Returns null when this binding has no artwork, so the caller can show its text label.</summary>
        public static Sprite Resolve(string controlPath, MenuInputFamily family)
        {
            if (string.IsNullOrWhiteSpace(controlPath)) return null;
            string path = controlPath.Trim().ToLowerInvariant();
            string resource;
            if (path.StartsWith(Keyboard, StringComparison.Ordinal))
                resource = "KeyboardMouse/keyboard_" + KeyboardName(path.Substring(Keyboard.Length));
            else if (path.StartsWith(Mouse, StringComparison.Ordinal))
            {
                string name = MouseName(path.Substring(Mouse.Length));
                if (name == null) return null;
                resource = "KeyboardMouse/" + name;
            }
            else if (path.StartsWith(Gamepad, StringComparison.Ordinal))
            {
                bool playStation = family == MenuInputFamily.PlayStation;
                var names = GamepadNames(path.Substring(Gamepad.Length));
                string name = playStation ? names.PlayStation : names.Xbox;
                if (name == null) return null;
                resource = (playStation ? "PlayStation/" : "Xbox/") + name;
            }
            else return null;

            if (!Cache.TryGetValue(resource, out var sprite))
            {
                sprite = Resources.Load<Sprite>(Root + resource);
                Cache.Add(resource, sprite);
            }
            return sprite;
        }

        static string KeyboardName(string name) => name switch
        {
            "anykey" => "any",
            "uparrow" => "arrow_up",
            "downarrow" => "arrow_down",
            "leftarrow" => "arrow_left",
            "rightarrow" => "arrow_right",
            "leftshift" or "rightshift" => "shift",
            "leftalt" or "rightalt" => "alt",
            "leftctrl" or "rightctrl" => "ctrl",
            "leftmeta" or "rightmeta" or "leftwindows" or "rightwindows" => "win",
            "backquote" => "tilde",
            "quote" => "apostrophe",
            "leftbracket" => "bracket_open",
            "rightbracket" => "bracket_close",
            "slash" => "slash_forward",
            "backslash" => "slash_back",
            "pageup" => "page_up",
            "pagedown" => "page_down",
            "scrolllock" => "scroll_lock",
            "numpadenter" => "numpad_enter",
            "numpadplus" => "numpad_plus",
            _ => name
        };

        static string MouseName(string name) => name switch
        {
            "leftbutton" => "mouse_left",
            "rightbutton" => "mouse_right",
            "middlebutton" => "mouse_scroll",
            "forwardbutton" => "mouse_side_forward",
            "backbutton" => "mouse_side_back",
            "delta" or "position" => "mouse_move",
            "scroll" or "scroll/y" => "mouse_scroll_vertical",
            "scroll/up" => "mouse_scroll_up",
            "scroll/down" => "mouse_scroll_down",
            _ => null
        };

        static (string Xbox, string PlayStation) GamepadNames(string control) => control switch
        {
            "buttonsouth" => ("xbox_button_a", "playstation_button_cross"),
            "buttoneast" => ("xbox_button_b", "playstation_button_circle"),
            "buttonwest" => ("xbox_button_x", "playstation_button_square"),
            "buttonnorth" => ("xbox_button_y", "playstation_button_triangle"),
            "leftshoulder" => ("xbox_lb", "playstation_trigger_l1"),
            "rightshoulder" => ("xbox_rb", "playstation_trigger_r1"),
            "lefttrigger" => ("xbox_lt", "playstation_trigger_l2"),
            "righttrigger" => ("xbox_rt", "playstation_trigger_r2"),
            "leftstickpress" or "leftstickbutton" => ("xbox_ls", "playstation_button_l3"),
            "rightstickpress" or "rightstickbutton" => ("xbox_rs", "playstation_button_r3"),
            "select" or "selectbutton" => ("xbox_button_view", "playstation5_button_create"),
            "start" or "startbutton" => ("xbox_button_menu", "playstation5_button_options"),
            "leftstick" => ("xbox_stick_l", "playstation_stick_l"),
            "rightstick" => ("xbox_stick_r", "playstation_stick_r"),
            "dpad" => ("xbox_dpad", "playstation_dpad"),
            "dpad/up" => ("xbox_dpad_up", "playstation_dpad_up"),
            "dpad/down" => ("xbox_dpad_down", "playstation_dpad_down"),
            "dpad/left" => ("xbox_dpad_left", "playstation_dpad_left"),
            "dpad/right" => ("xbox_dpad_right", "playstation_dpad_right"),
            _ => (null, null)
        };
    }
}
