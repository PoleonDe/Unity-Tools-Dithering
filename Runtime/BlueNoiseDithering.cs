using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Control.Tools.PostProcessing.BlueNoiseDithering
{
    [Serializable]
    [VolumeComponentMenu("Post-processing/Control Tools/Blue Noise Dithering")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class BlueNoiseDithering : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Multiplier for the additive blue-noise term. The sampled texture value is divided by 255 before this multiplier is applied.")]
        public MinFloatParameter strength = new MinFloatParameter(1f, 0f);

        [Tooltip("Offset the blue-noise lookup by Time.time for temporal decorrelation.")]
        public BoolParameter enableTemporalOffset = new BoolParameter(true);

        public bool IsActive()
        {
            return active
                && strength.value > 0f;
        }

        [Obsolete("Unused. #from(2023.1)")]
        public bool IsTileCompatible() => false;
    }
}
