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

            TEXTURE2D(_BlueNoiseTex);
            SAMPLER(sampler_BlueNoiseTex);

            float4 _BlueNoiseTex_TexelSize;
            float _Intensity;
            float _ColorSteps;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord.xy;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float2 noiseUV = frac(input.positionCS.xy * _BlueNoiseTex_TexelSize.xy);
                half noise = SAMPLE_TEXTURE2D(_BlueNoiseTex, sampler_BlueNoiseTex, noiseUV).r - 0.5;

                float levels = max(_ColorSteps - 1.0, 1.0);
                half3 dithered = floor(saturate(color.rgb) * levels + 0.5 + noise * _Intensity) / levels;

                return half4(dithered, color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
