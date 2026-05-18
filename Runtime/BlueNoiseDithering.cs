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
        [Tooltip("Blue-noise threshold texture used to distribute quantization error across the screen.")]
        public NoInterpTextureParameter blueNoiseTexture = new NoInterpTextureParameter(null);

        [Tooltip("Strength of the blue-noise offset applied before color quantization.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 1f);

        [Tooltip("Number of discrete output values per color channel.")]
        public ClampedIntParameter colorSteps = new ClampedIntParameter(32, 2, 256);

        public bool IsActive()
        {
            return active
                && blueNoiseTexture.value != null
                && intensity.value > 0f
                && colorSteps.value > 1;
        }

        [Obsolete("Unused. #from(2023.1)")]
        public bool IsTileCompatible() => false;
    }
}
