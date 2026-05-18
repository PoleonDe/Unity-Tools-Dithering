using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Control.Tools.PostProcessing.BlueNoiseDithering
{
    public sealed class BlueNoiseDitheringRendererFeature : ScriptableRendererFeature
    {
        private const string ShaderName = "Hidden/Control/PostProcessing/BlueNoiseDithering";

        [SerializeField]
        private RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [SerializeField]
        private Shader shader;

        private BlueNoiseDitheringPass pass;
        private Material material;

        public override void Create()
        {
            pass ??= new BlueNoiseDitheringPass();
            pass.renderPassEvent = renderPassEvent;

            EnsureMaterial();
        }

        private void OnValidate()
        {
            shader ??= Shader.Find(ShaderName);

            if (pass != null)
                pass.renderPassEvent = renderPassEvent;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview
                || renderingData.cameraData.cameraType == CameraType.Reflection
                || !renderingData.cameraData.postProcessEnabled
                || UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData))
            {
                return;
            }

            BlueNoiseDithering settings = VolumeManager.instance.stack.GetComponent<BlueNoiseDithering>();
            if (settings == null || !settings.IsActive())
                return;

            EnsureMaterial();
            if (material == null)
            {
                Debug.LogWarning($"{nameof(BlueNoiseDitheringRendererFeature)} could not find shader '{ShaderName}'.");
                return;
            }

            pass.renderPassEvent = renderPassEvent;
            pass.Setup(material, settings);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
        }

        private void EnsureMaterial()
        {
            Shader activeShader = shader != null ? shader : Shader.Find(ShaderName);
            if (activeShader == null)
                return;

            if (material != null && material.shader == activeShader)
                return;

            CoreUtils.Destroy(material);
            material = CoreUtils.CreateEngineMaterial(activeShader);
        }

        private sealed class BlueNoiseDitheringPass : ScriptableRenderPass
        {
            private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int BlueNoiseTexId = Shader.PropertyToID("_BlueNoiseTex");
            private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
            private static readonly int ColorStepsId = Shader.PropertyToID("_ColorSteps");

            private readonly ProfilingSampler profiling = new ProfilingSampler("Blue Noise Dithering");
            private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

            private Material material;
            private Texture blueNoiseTexture;
            private float intensity;
            private int colorSteps;

#if URP_COMPATIBILITY_MODE
            private RTHandle copiedColor;
#endif

            public BlueNoiseDitheringPass()
            {
                requiresIntermediateTexture = true;
            }

            public void Setup(Material material, BlueNoiseDithering settings)
            {
                this.material = material;
                blueNoiseTexture = settings.blueNoiseTexture.value;
                intensity = settings.intensity.value;
                colorSteps = settings.colorSteps.value;
                requiresIntermediateTexture = true;
            }

#if URP_COMPATIBILITY_MODE
            [System.Obsolete("Compatibility Mode is deprecated in Unity 6.3. Prefer RenderGraph.")]
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
#pragma warning disable CS0618
                ResetTarget();
#pragma warning restore CS0618

                RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.msaaSamples = 1;
                descriptor.depthStencilFormat = GraphicsFormat.None;

                RenderingUtils.ReAllocateHandleIfNeeded(
                    ref copiedColor,
                    descriptor,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "_BlueNoiseDitheringColorCopy");
            }

            [System.Obsolete("Compatibility Mode is deprecated in Unity 6.3. Prefer RenderGraph.")]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (material == null || blueNoiseTexture == null || copiedColor == null)
                    return;

                CommandBuffer cmd = CommandBufferPool.Get("Blue Noise Dithering");
                RTHandle cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;

                using (new ProfilingScope(cmd, profiling))
                {
                    Blitter.BlitCameraTexture(cmd, cameraColor, copiedColor);
                    CoreUtils.SetRenderTarget(cmd, cameraColor);
                    Draw(cmd, copiedColor);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
#endif

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                if (cameraData.camera.cameraType == CameraType.Preview
                    || resourceData.isActiveTargetBackBuffer
                    || material == null
                    || blueNoiseTexture == null)
                {
                    return;
                }

                TextureHandle source = resourceData.activeColorTexture;
                TextureHandle destination = resourceData.activeColorTexture;

                if (!source.IsValid() || !destination.IsValid())
                    return;

                TextureDesc copiedColorDesc = renderGraph.GetTextureDesc(source);
                copiedColorDesc.name = "_BlueNoiseDitheringColorCopy";
                copiedColorDesc.clearBuffer = false;
                TextureHandle copiedColor = renderGraph.CreateTexture(copiedColorDesc);

                renderGraph.AddBlitPass(source, copiedColor, Vector2.one, Vector2.zero, passName: "Copy Color Blue Noise Dithering");

                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>("Blue Noise Dithering", out PassData passData, profiling))
                {
                    passData.material = material;
                    passData.propertyBlock = propertyBlock;
                    passData.source = copiedColor;
                    passData.blueNoiseTexture = blueNoiseTexture;
                    passData.intensity = intensity;
                    passData.colorSteps = colorSteps;

                    builder.UseTexture(copiedColor, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        Draw(context.cmd, data);
                    });
                }
            }

            public void Dispose()
            {
#if URP_COMPATIBILITY_MODE
                copiedColor?.Release();
                copiedColor = null;
#endif
            }

#if URP_COMPATIBILITY_MODE
            private void Draw(CommandBuffer cmd, RTHandle source)
            {
                propertyBlock.Clear();
                propertyBlock.SetTexture(BlitTextureId, source);
                propertyBlock.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                propertyBlock.SetTexture(BlueNoiseTexId, blueNoiseTexture);
                propertyBlock.SetFloat(IntensityId, intensity);
                propertyBlock.SetFloat(ColorStepsId, colorSteps);

                cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, propertyBlock);
            }
#endif

            private static void Draw(RasterCommandBuffer cmd, PassData data)
            {
                data.propertyBlock.Clear();
                data.propertyBlock.SetTexture(BlitTextureId, data.source);
                data.propertyBlock.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                data.propertyBlock.SetTexture(BlueNoiseTexId, data.blueNoiseTexture);
                data.propertyBlock.SetFloat(IntensityId, data.intensity);
                data.propertyBlock.SetFloat(ColorStepsId, data.colorSteps);

                cmd.DrawProcedural(
                    Matrix4x4.identity,
                    data.material,
                    0,
                    MeshTopology.Triangles,
                    3,
                    1,
                    data.propertyBlock);
            }

            private sealed class PassData
            {
                public Material material;
                public MaterialPropertyBlock propertyBlock;
                public TextureHandle source;
                public Texture blueNoiseTexture;
                public float intensity;
                public int colorSteps;
            }
        }
    }
}
