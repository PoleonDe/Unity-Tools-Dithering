# Control Tools - Post Processing Dithering

Temporal blue-noise dithering for Unity 6.3 URP fullscreen Volume rendering.

This effect injects a subtle monochrome blue-noise error before temporal accumulation/TAA so gradients integrate with less visible banding. It is intended to disappear through temporal integration, not read as stylized film grain.

## Use

1. Add `BlueNoiseDitheringRendererFeature` to the active URP renderer asset.
2. Add `Control Tools/Blue Noise Dithering` to a Volume profile.
3. Enable Post Processing on the camera.

The renderer feature loads the packaged `Runtime/Resources/ControlTools/PostProcessing/BlueNoiseDithering/256_256_HDR_RGBA_0.png` texture internally. No noise texture or scale is exposed in the Volume.

Internal texture setup:

- 256x256 RGBA
- RGBA32, linear
- Point filtering
- Repeat wrapping
- Mipmaps off
- sRGB/source gamma off

The pass samples at exact 1:1 screen-pixel density using integer pixel coordinates, offsets the lookup by time when temporal offset is enabled, and applies:

```hlsl
outColor.rgb += tex2D(_BlueNoise, uv + time).rgb * (_Strength / 255.0);
```

## Notes

The renderer feature records a RenderGraph pass. A Compatibility Mode pass is also included behind Unity's `URP_COMPATIBILITY_MODE` scripting define for projects that intentionally keep RenderGraph disabled.

The pass is fixed to run immediately before URP's post-processing/TAA injection point (`BeforeRenderingPostProcessing - 1`) so the dither is present before temporal accumulation.
