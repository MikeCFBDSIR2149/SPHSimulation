using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSFSmoothPass : ScriptableRenderPass
{
    private readonly SSFRenderFeature.SSFSettings settings;
    private new readonly ProfilingSampler profilingSampler = new ProfilingSampler("SSF Smooth Pass"); // 加 new
    
    private RTHandle m_TempBlurRT;      // 临时
    private RTHandle m_SmoothedDepthRT; // 输出

    public RTHandle SmoothedDepthRT => m_SmoothedDepthRT;

    private readonly Material bilateralBlurMat;
    private readonly Material gaussianBlurMat;
    
    private readonly SSFRenderFeature feature;
    
    private static readonly int BlurRadius = Shader.PropertyToID("_BlurRadius");
    private static readonly int DepthSigma = Shader.PropertyToID("_DepthSigma");
    private static readonly int SpatialSigma = Shader.PropertyToID("_SpatialSigma");
    private static readonly int BlurDirection = Shader.PropertyToID("_BlurDirection");
    private static readonly int FluidSmoothedDepthTexture = Shader.PropertyToID("_FluidSmoothedDepthTexture");
    private static readonly int BlurAmount = Shader.PropertyToID("_BlurAmount");

    public SSFSmoothPass(SSFRenderFeature.SSFSettings settings, SSFRenderFeature feature)
    {
        this.settings = settings;
        this.renderPassEvent = settings.renderPassEvent; 
        this.bilateralBlurMat = settings.bilateralBlurMaterial;
        this.gaussianBlurMat = settings.gaussianBlurMaterial;
        this.feature = feature;
    }

    [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        if (!bilateralBlurMat) return;
        
        if (m_TempBlurRT != null) { RTHandles.Release(m_TempBlurRT); m_TempBlurRT = null; }
        if (m_SmoothedDepthRT != null) { RTHandles.Release(m_SmoothedDepthRT); m_SmoothedDepthRT = null; }

        RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.colorFormat = RenderTextureFormat.RHalf;
        desc.depthBufferBits = 0;

        m_TempBlurRT = RTHandles.Alloc(desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TempBlurTexture");
        m_SmoothedDepthRT = RTHandles.Alloc(desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_FluidSmoothedDepthTexture");
    }

    [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (!bilateralBlurMat || !Application.isPlaying || m_TempBlurRT == null || m_SmoothedDepthRT == null || feature?.ParticlePass?.DepthRT == null)
            return;
        
        CommandBuffer cmd = CommandBufferPool.Get("SSF Bilateral Blur");
        RTHandle sourceDepthRT = feature.ParticlePass.DepthRT;

        using (new ProfilingScope(cmd, profilingSampler))
        {
            bilateralBlurMat.SetInt(BlurRadius, settings.blurIterations);
            bilateralBlurMat.SetFloat(DepthSigma, settings.depthSigma);
            bilateralBlurMat.SetFloat(SpatialSigma, settings.spatialSigma);

            cmd.SetGlobalVector(BlurDirection, new Vector4(1f, 0f, 0.0f, 0.0f));
            Blitter.BlitCameraTexture(cmd, sourceDepthRT, m_TempBlurRT, bilateralBlurMat, 0);

            cmd.SetGlobalVector(BlurDirection, new Vector4(0f, 1f, 0.0f, 0.0f));
            Blitter.BlitCameraTexture(cmd, m_TempBlurRT, m_SmoothedDepthRT, bilateralBlurMat, 0);
            
            gaussianBlurMat.SetFloat(BlurAmount, settings.blurAmount);
            
            gaussianBlurMat.SetVector(BlurDirection, new Vector4(1f, 0.0f, 0.0f, 0.0f));
            Blitter.BlitCameraTexture(cmd, m_SmoothedDepthRT, m_TempBlurRT, gaussianBlurMat, 0);

            gaussianBlurMat.SetVector(BlurDirection, new Vector4(0.0f, 0.2f, 0.0f, 0.0f));
            Blitter.BlitCameraTexture(cmd, m_TempBlurRT, m_SmoothedDepthRT, gaussianBlurMat, 0);

            cmd.SetGlobalTexture(FluidSmoothedDepthTexture, m_SmoothedDepthRT);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
    
    public void Dispose()
    {
        RTHandles.Release(m_TempBlurRT);
        RTHandles.Release(m_SmoothedDepthRT);
        m_TempBlurRT = null;
        m_SmoothedDepthRT = null;
    }
}