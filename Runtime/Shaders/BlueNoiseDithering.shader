Shader "Hidden/Control/PostProcessing/BlueNoiseDithering"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Blue Noise Dithering"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_BlueNoise);

            float _Strength;
            float _BlueNoiseTime;
            float _EnableTemporalOffset;

            static const float BlueNoiseTileSize = 256.0;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord.xy;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float2 pixel = floor(input.positionCS.xy);
                float2 noiseUV = (pixel + 0.5) / BlueNoiseTileSize;
                float timeOffset = _EnableTemporalOffset > 0.5 ? _BlueNoiseTime : 0.0;
                float3 blueNoise = SAMPLE_TEXTURE2D_LOD(_BlueNoise, sampler_PointRepeat, noiseUV + timeOffset.xx, 0).rgb;
                float3 dithered = color.rgb + blueNoise * (_Strength / 255.0);

                return half4(saturate(dithered), color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
