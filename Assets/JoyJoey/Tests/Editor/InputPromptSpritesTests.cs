using JoyJoey.Game.UI;
using NUnit.Framework;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class InputPromptSpritesTests
    {
        [TestCase("<Keyboard>/j", MenuInputFamily.KeyboardMouse, "keyboard_j")]
        [TestCase("<Keyboard>/rightShift", MenuInputFamily.PlayStation, "keyboard_shift")]
        [TestCase("<Mouse>/forwardButton", MenuInputFamily.KeyboardMouse, "mouse_side_forward")]
        [TestCase("<Gamepad>/buttonSouth", MenuInputFamily.Xbox, "xbox_button_a")]
        [TestCase("<Gamepad>/buttonSouth", MenuInputFamily.PlayStation, "playstation_button_cross")]
        [TestCase("<Gamepad>/dpad/down", MenuInputFamily.PlayStation, "playstation_dpad_down")]
        [TestCase("<Gamepad>/start", MenuInputFamily.Xbox, "xbox_button_menu")]
        public void ResolvesPhysicalControlAndVisualFamily(string path, MenuInputFamily family, string expected)
        {
            var sprite = InputPromptSprites.Resolve(path, family);
            Assert.That(sprite, Is.Not.Null);
            Assert.That(sprite.name, Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("<Keyboard>/numpad1")]
        [TestCase("<Touchscreen>/primaryTouch")]
        public void MissingArtLeavesTheTextFallbackAvailable(string path)
        {
            Assert.That(InputPromptSprites.Resolve(path, MenuInputFamily.KeyboardMouse), Is.Null);
        }
    }
}
