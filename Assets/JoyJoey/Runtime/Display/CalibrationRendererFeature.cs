using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace JoyJoey.Game.Display
{
    /// <summary>Final world correction, after artistic post processing and before the native overlay UI.</summary>
    [DisallowMultipleRendererFeature("Joy Joey · Calibración de imagen")]
    public sealed class CalibrationRendererFeature : ScriptableRendererFeature
    {
        [Tooltip("Material de calibración. La fuente permanece editable; el juego usa una copia temporal.")]
        public Material Material;
        Material _runtimeMaterial;
        Material _materialSource;
        CalibrationPass _pass;

        public override void Create()
        {
            _pass = new CalibrationPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (CalibrationSettings.IsNeutral || !Material || renderingData.cameraData.cameraType != CameraType.Game ||
                renderingData.cameraData.renderType == CameraRenderType.Overlay) return;
            if (_pass == null) Create();
            if (!_runtimeMaterial || _materialSource != Material)
            {
                CoreUtils.Destroy(_runtimeMaterial);
                _materialSource = Material;
                _runtimeMaterial = new Material(Material) { name = "Joy Joey · calibración temporal", hideFlags = HideFlags.HideAndDontSave };
            }
            _pass.Setup(_runtimeMaterial, CalibrationSettings.Values);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_runtimeMaterial);
            _runtimeMaterial = null;
            _materialSource = null;
        }

        sealed class CalibrationPass : ScriptableRenderPass
        {
            static readonly int CalibrationId = Shader.PropertyToID("_JoeyCalibration");
            readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();
            Material _material;
            Vector4 _values;

            public void Setup(Material material, Vector4 values)
            {
                _material = material;
                _values = values;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Color);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !_material) return;
                var source = resources.activeColorTexture;
                if (!source.IsValid()) return;
                var descriptor = renderGraph.GetTextureDesc(source);
                var camera = frameData.Get<UniversalCameraData>();
                CalibrationSettings.RecordDimensions(descriptor.width > 0 ? descriptor.width : camera.cameraTargetDescriptor.width,
                    descriptor.height > 0 ? descriptor.height : camera.cameraTargetDescriptor.height);
                descriptor.name = "Joy Joey · imagen calibrada";
                descriptor.clearBuffer = false;
                descriptor.depthBufferBits = DepthBits.None;
                var destination = renderGraph.CreateTexture(descriptor);
                _properties.Clear();
                _properties.SetVector(CalibrationId, _values);
                var parameters = new RenderGraphUtils.BlitMaterialParameters(source, destination, _material, 0)
                { propertyBlock = _properties };
                renderGraph.AddBlitPass(parameters, passName: "Joy Joey · brillo, contraste y gamma");
                // The final blit consumes the corrected texture; no redundant copy back to source.
                resources.cameraColor = destination;
            }
        }
    }
}
