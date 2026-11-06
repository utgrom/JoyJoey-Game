Shader "Hidden/JoyJoey/DisplayCalibration"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "DisplayCalibration"
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _JoeyCalibration;

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, 0);
                // User controls operate on displayed SDR values. Preserve the project's linear pipeline.
                #ifndef UNITY_COLORSPACE_GAMMA
                    color.rgb = LinearToSRGB(color.rgb);
                #endif
                color.rgb = saturate((color.rgb - 0.5) * _JoeyCalibration.y + 0.5 + _JoeyCalibration.x);
                color.rgb = PositivePow(color.rgb, rcp(max(0.01, _JoeyCalibration.z)));
                #ifndef UNITY_COLORSPACE_GAMMA
                    color.rgb = SRGBToLinear(color.rgb);
                #endif
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
