# Control Tools - Post Processing Dithering

Texture-driven blue-noise dithering for Unity 6.3 URP fullscreen Volume rendering.

## Use

1. Add `BlueNoiseDitheringRendererFeature` to the active URP renderer asset.
2. Add `Control Tools/Blue Noise Dithering` to a Volume profile.
3. Assign a blue-noise texture in the Volume override.
4. Enable Post Processing on the camera.

The blue-noise texture should use repeat wrapping and point filtering for crisp, stable thresholds.

## Notes

The renderer feature records a RenderGraph pass. A Compatibility Mode pass is also included behind Unity's `URP_COMPATIBILITY_MODE` scripting define for projects that intentionally keep RenderGraph disabled.
