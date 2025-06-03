using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSFShadingPass : ScriptableRenderPass
{
    private readonly SSFRenderFeature.SSFSettings settings;
    private readonly Material shadingMaterial;
    private readonly SSFRenderFeature feature;
    private new readonly ProfilingSampler profilingSampler = new ProfilingSampler("SSF Shading Pass");

    private RTHandle m_SmoothedDepthRT;
    
    private static readonly int FluidSmoothedDepthTexture = Shader.PropertyToID("_FluidSmoothedDepthTexture");
    private static readonly int FluidThicknessTexture = Shader.PropertyToID("_FluidThicknessTexture");

    public SSFShadingPass(SSFRenderFeature.SSFSettings settings, Material material, SSFRenderFeature feature)
    {
        this.settings = settings;
        this.renderPassEvent = settings.renderPassEvent + 2;
        this.shadingMaterial = material;
        this.feature = feature;
    }
    
    [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        if (feature?.SmoothPass != null)
        {
            m_SmoothedDepthRT = feature.SmoothPass.SmoothedDepthRT;
        }
        else
        {
            Debug.LogError("Shading Pass CONFIGURE - Feature or SmoothPass is NULL!");
            m_SmoothedDepthRT = null;
        }
    }

    [Obsolete("Obsolete")]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        RTHandle sourceRT = m_SmoothedDepthRT;
        RTHandle thicknessRT = feature.ParticlePass?.ThicknessRT;
        
        if (!Application.isPlaying) return;

        if (!shadingMaterial || m_SmoothedDepthRT == null || !Application.isPlaying)
        {
            Debug.LogWarning("SSF Shading Pass: Missing material or smoothed depth RT, skipping pass.");
            return;
        }
        

        CommandBuffer cmd = CommandBufferPool.Get("SSF Shading Pass");
        RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

        using (new ProfilingScope(cmd, profilingSampler))
        {
            cmd.SetGlobalTexture(FluidSmoothedDepthTexture, sourceRT);
            cmd.SetGlobalTexture(FluidThicknessTexture, thicknessRT);
            // settings.finalShadingMaterial.SetTexture(FluidSmoothedDepthTexture, sourceRT);
            // settings.finalShadingMaterial.SetTexture(FluidThicknessTexture, thicknessRT);
            // cmd.Blit(sourceRT, cameraColorTarget);
            cmd.SetRenderTarget(cameraColorTarget);
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, shadingMaterial, 0, 0);
        }
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}