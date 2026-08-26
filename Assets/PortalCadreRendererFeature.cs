using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PortalCadreRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public LayerMask cadreLayer;
    }

    public Settings settings = new Settings();

    private PortalCadreRenderPass renderPass;

    public override void Create()
    {
        renderPass = new PortalCadreRenderPass(
            settings.cadreLayer
        );

        renderPass.renderPassEvent =
            RenderPassEvent.AfterRenderingOpaques;
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        renderer.EnqueuePass(renderPass);
    }

    private class PortalCadreRenderPass : ScriptableRenderPass
    {
        private LayerMask cadreLayer;

        private FilteringSettings filteringSettings;

        private ShaderTagId shaderTagId =
            new ShaderTagId("UniversalForward");

        private static readonly int StencilRef =
            Shader.PropertyToID("_StencilRef");

        public PortalCadreRenderPass(
            LayerMask layer)
        {
            cadreLayer = layer;

            filteringSettings =
                new FilteringSettings(
                    RenderQueueRange.all,
                    cadreLayer
                );
        }

        public override void Execute(
            ScriptableRenderContext context,
            ref RenderingData renderingData)
        {
            CommandBuffer cmd =
                CommandBufferPool.Get(
                    "Portal Cadre Stencil"
                );

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            DrawingSettings drawingSettings =
                CreateDrawingSettings(
                    shaderTagId,
                    ref renderingData,
                    SortingCriteria.CommonOpaque
                );

            drawingSettings.overrideMaterial =
                CreateStencilMaterial();

            context.DrawRenderers(
                renderingData.cullResults,
                ref drawingSettings,
                ref filteringSettings
            );

            context.ExecuteCommandBuffer(cmd);

            CommandBufferPool.Release(cmd);
        }

        private Material CreateStencilMaterial()
        {
            Shader shader =
                Shader.Find(
                    "Custom/PortalCadreMask"
                );

            if (shader == null)
            {
                Debug.LogError(
                    "PortalCadreMask shader not found."
                );

                return null;
            }

            Material material =
                new Material(shader);

            material.SetFloat(
                StencilRef,
                1
            );

            return material;
        }
    }
}