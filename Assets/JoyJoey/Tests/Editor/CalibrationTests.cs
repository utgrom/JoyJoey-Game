using System.Linq;
using JoyJoey.Game.Display;
using JoyJoey.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class CalibrationTests
    {
        [TearDown]
        public void RestoreNeutralCalibration() => CalibrationSettings.Reset();

        [TestCase(0f)]
        [TestCase(.05f)]
        [TestCase(.18f)]
        [TestCase(.5f)]
        [TestCase(.9f)]
        [TestCase(1f)]
        public void NeutralCalibrationPreservesDisplayLevels(float level)
        {
            Assert.That(CalibrationMath.ApplyDisplay(level, 0, 1, 1), Is.EqualTo(level).Within(.000001f));
        }

        [Test]
        public void NeutralCalibrationPreservesLinearColorAndTransparency()
        {
            var input = new Color(.04f, .18f, .75f, .37f);
            var output = CalibrationMath.ApplyLinear(input, 0, 1, 1);
            Assert.That(output.r, Is.EqualTo(input.r).Within(.0001f));
            Assert.That(output.g, Is.EqualTo(input.g).Within(.0001f));
            Assert.That(output.b, Is.EqualTo(input.b).Within(.0001f));
            Assert.That(output.a, Is.EqualTo(input.a));
        }

        [Test]
        public void BrighterGammaLiftsMidtonesAndPreservesBlackAndWhite()
        {
            Assert.That(CalibrationMath.ApplyDisplay(.25f, 0, 1, 1.5f), Is.GreaterThan(.25f));
            Assert.That(CalibrationMath.ApplyDisplay(.25f, 0, 1, .75f), Is.LessThan(.25f));
            Assert.That(CalibrationMath.ApplyDisplay(0, 0, 1, 1.5f), Is.Zero);
            Assert.That(CalibrationMath.ApplyDisplay(1, 0, 1, 1.5f), Is.EqualTo(1));
        }

        [Test]
        public void ContrastPreservesMiddleGrayAndSeparatesDarkAndLightLevels()
        {
            Assert.That(CalibrationMath.ApplyDisplay(.5f, 0, 1.4f, 1), Is.EqualTo(.5f));
            Assert.That(CalibrationMath.ApplyDisplay(.25f, 0, 1.4f, 1), Is.LessThan(.25f));
            Assert.That(CalibrationMath.ApplyDisplay(.75f, 0, 1.4f, 1), Is.GreaterThan(.75f));
        }

        [Test]
        public void BrightnessMovesBothEndsAndClampsToDisplayableRange()
        {
            Assert.That(CalibrationMath.ApplyDisplay(0, .2f, 1, 1), Is.EqualTo(.2f).Within(.000001f));
            Assert.That(CalibrationMath.ApplyDisplay(1, -.2f, 1, 1), Is.EqualTo(.8f).Within(.000001f));
            Assert.That(CalibrationMath.ApplyDisplay(1, .5f, 1.5f, .6f), Is.EqualTo(1));
            Assert.That(CalibrationMath.ApplyDisplay(0, -.5f, 1.5f, .6f), Is.Zero);
        }

        [Test]
        public void LinearCorrectionKeepsGrayNeutralAndDoesNotAlterTransparency()
        {
            var output = CalibrationMath.ApplyLinear(new Color(.18f, .18f, .18f, .42f), .1f, 1.1f, 1.2f);
            Assert.That(output.r, Is.EqualTo(output.g));
            Assert.That(output.g, Is.EqualTo(output.b));
            Assert.That(output.r, Is.GreaterThan(.18f));
            Assert.That(output.a, Is.EqualTo(.42f));
        }

        [Test]
        public void InvalidCalibrationSettingsFallBackToNeutralFiniteValues()
        {
            CalibrationSettings.Set(float.NaN, float.PositiveInfinity, float.NegativeInfinity);
            Assert.That(CalibrationSettings.IsNeutral, Is.True);
            Assert.That(CalibrationSettings.Values, Is.EqualTo(new Vector4(0, 1, 1, 0)));
        }

        [Test]
        public void CalibrationLimitsRemainWithinTheSupportedUserRange()
        {
            CalibrationSettings.Set(10, -10, 10);
            Assert.That(CalibrationSettings.Values, Is.EqualTo(new Vector4(.5f, .5f, 1.6f, 0)));
            CalibrationSettings.Set(-10, 10, -10);
            Assert.That(CalibrationSettings.Values, Is.EqualTo(new Vector4(-.5f, 1.5f, .6f, 0)));
            CalibrationSettings.Reset();
            Assert.That(CalibrationSettings.IsNeutral, Is.True);
            Assert.That(CalibrationSettings.RenderedFrameCount, Is.Zero);
        }

        [Test]
        public void EachRendererHasExactlyOnePersistentCalibrationFeatureAndMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(CalibrationBuilder.MaterialPath);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Shader>(CalibrationBuilder.ShaderPath)));
            foreach (string path in CalibrationBuilder.RendererPaths)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                Assert.That(renderer, Is.Not.Null, path);
                var features = renderer.rendererFeatures.OfType<CalibrationRendererFeature>().ToArray();
                Assert.That(features, Has.Length.EqualTo(1), path);
                Assert.That(features[0].isActive, Is.True, path);
                Assert.That(features[0].Material, Is.EqualTo(material), path);
                Assert.That(AssetDatabase.GetAssetPath(features[0]), Is.EqualTo(path));
            }
        }
    }
}
