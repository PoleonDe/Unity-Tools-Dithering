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
        private const RenderPassEvent BeforeTemporalAccumulationRenderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingPostProcessing - 1);
        private const string BlueNoiseResourcePath = "ControlTools/PostProcessing/BlueNoiseDithering/256_256_HDR_RGBA_0";

        [SerializeField, HideInInspector]
        private Shader shader;

        [SerializeField]
        private RenderPassEvent renderPassEvent = BeforeTemporalAccumulationRenderPassEvent;

        private BlueNoiseDitheringPass pass;
        private Material material;
        private Texture2D blueNoiseTexture;
        private bool loggedBlueNoiseLoadFailure;

        public override void Create()
        {
            pass ??= new BlueNoiseDitheringPass();
            pass.renderPassEvent = renderPassEvent;

            EnsureMaterial();
            EnsureBlueNoiseTexture();
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

            if (!VolumeManager.instance.IsComponentActiveInMask<BlueNoiseDithering>(renderingData.cameraData.volumeLayerMask))
                return;

            BlueNoiseDithering settings = VolumeManager.instance.stack.GetComponent<BlueNoiseDithering>();
            if (settings == null || !settings.IsActive())
                return;

            EnsureMaterial();
            if (material == null)
            {
                Debug.LogWarning($"{nameof(BlueNoiseDitheringRendererFeature)} could not find shader '{ShaderName}'.");
                return;
            }

            EnsureBlueNoiseTexture();
            if (blueNoiseTexture == null)
            {
                if (!loggedBlueNoiseLoadFailure)
                {
                    Debug.LogWarning($"{nameof(BlueNoiseDitheringRendererFeature)} could not load blue-noise texture from Resources/{BlueNoiseResourcePath}.");
                    loggedBlueNoiseLoadFailure = true;
                }

                return;
            }

            pass.renderPassEvent = renderPassEvent;
            pass.Setup(material, blueNoiseTexture, settings);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
            blueNoiseTexture = null;
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

        private void EnsureBlueNoiseTexture()
        {
            if (blueNoiseTexture != null)
                return;

            blueNoiseTexture = Resources.Load<Texture2D>(BlueNoiseResourcePath);
            loggedBlueNoiseLoadFailure = blueNoiseTexture != null ? false : loggedBlueNoiseLoadFailure;
        }

        private sealed class BlueNoiseDitheringPass : ScriptableRenderPass
        {
            private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int BlueNoiseId = Shader.PropertyToID("_BlueNoise");
            private static readonly int StrengthId = Shader.PropertyToID("_Strength");
            private static readonly int BlueNoiseTimeId = Shader.PropertyToID("_BlueNoiseTime");
            private static readonly int EnableTemporalOffsetId = Shader.PropertyToID("_EnableTemporalOffset");

            private readonly ProfilingSampler profiling = new ProfilingSampler("Blue Noise Dithering");
            private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

            private Material material;
            private Texture2D blueNoiseTexture;
            private float strength;
            private float blueNoiseTime;
            private float enableTemporalOffset;

#if URP_COMPATIBILITY_MODE
            private RTHandle copiedColor;
#endif

            public BlueNoiseDitheringPass()
            {
                requiresIntermediateTexture = true;
            }

            public void Setup(Material material, Texture2D blueNoiseTexture, BlueNoiseDithering settings)
            {
                this.material = material;
                this.blueNoiseTexture = blueNoiseTexture;
                strength = settings.strength.value;
                enableTemporalOffset = settings.enableTemporalOffset.value ? 1f : 0f;
                blueNoiseTime = settings.enableTemporalOffset.value ? Time.time : 0f;
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
                    passData.strength = strength;
                    passData.blueNoiseTime = blueNoiseTime;
                    passData.enableTemporalOffset = enableTemporalOffset;

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
                propertyBlock.SetTexture(BlueNoiseId, blueNoiseTexture);
                propertyBlock.SetFloat(StrengthId, strength);
                propertyBlock.SetFloat(BlueNoiseTimeId, blueNoiseTime);
                propertyBlock.SetFloat(EnableTemporalOffsetId, enableTemporalOffset);

                cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, propertyBlock);
            }
#endif

            private static void Draw(RasterCommandBuffer cmd, PassData data)
            {
                data.propertyBlock.Clear();
                data.propertyBlock.SetTexture(BlitTextureId, data.source);
                data.propertyBlock.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                data.propertyBlock.SetTexture(BlueNoiseId, data.blueNoiseTexture);
                data.propertyBlock.SetFloat(StrengthId, data.strength);
                data.propertyBlock.SetFloat(BlueNoiseTimeId, data.blueNoiseTime);
                data.propertyBlock.SetFloat(EnableTemporalOffsetId, data.enableTemporalOffset);

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
                public Texture2D blueNoiseTexture;
                public float strength;
                public float blueNoiseTime;
                public float enableTemporalOffset;
            }
        }
    }
}
