using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSFParticleRenderPass : ScriptableRenderPass
{
    private readonly SSFRenderFeature.SSFSettings settings;
    private new readonly ProfilingSampler profilingSampler;
    
    private RTHandle m_DepthRT;
    private RTHandle m_ThicknessRT;
    
    public RTHandle DepthRT => m_DepthRT;
    public RTHandle ThicknessRT => m_ThicknessRT;

    private ParticleControllerSSF particleController;
    
    private static readonly int ParticleSizeMultiplier = Shader.PropertyToID("_ParticleSizeMultiplier");
    private static readonly int Particles = Shader.PropertyToID("_Particles");
    private static readonly int SmoothingRadiusH = Shader.PropertyToID("_SmoothingRadiusH");
    private static readonly int FluidDepthTexture = Shader.PropertyToID("_FluidDepthTexture");
    private static readonly int FluidThicknessTexture = Shader.PropertyToID("_FluidThicknessTexture");

    public SSFParticleRenderPass(SSFRenderFeature.SSFSettings settings)
    {
        this.settings = settings;
        this.renderPassEvent = settings.renderPassEvent;
        profilingSampler = new ProfilingSampler("SSF Particle Pass");
    }

    [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        if (m_DepthRT != null) { RTHandles.Release(m_DepthRT); m_DepthRT = null; }
        if (m_ThicknessRT != null) { RTHandles.Release(m_ThicknessRT); m_ThicknessRT = null; }
        
        RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.colorFormat = RenderTextureFormat.RHalf; 
        desc.depthBufferBits = 0;

        m_DepthRT = RTHandles.Alloc(desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_FluidDepthTexture");
        m_ThicknessRT = RTHandles.Alloc(desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_FluidThicknessTexture");
    }

    [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (!Application.isPlaying) return;

        if (!particleController) particleController = ParticleControllerSSF.Instance;

        if (!particleController)
        {
            return;
        }
        ComputeBuffer argsBuffer = particleController.GetArgsBuffer();
        Material depthMaterial = settings.particleDepthMaterial;
        Material thicknessMaterial = settings.particleThicknessMaterial;
        Mesh particleMesh = particleController.particleMesh;
        ComputeBuffer particleBuffer = particleController.GetParticleBuffer();
        
        if (argsBuffer == null || !depthMaterial || !thicknessMaterial || !particleMesh) return;
        
        CommandBuffer cmd = CommandBufferPool.Get("SSF Particle Render");

        using (new ProfilingScope(cmd, profilingSampler))
        {
            // 设置粒子数据 Buffer
            depthMaterial.SetBuffer(Particles, particleBuffer);
            thicknessMaterial.SetBuffer(Particles, particleBuffer);
            depthMaterial.SetFloat(SmoothingRadiusH, particleController.smoothingRadiusH);
            thicknessMaterial.SetFloat(SmoothingRadiusH, particleController.smoothingRadiusH);

            // 深度
            cmd.SetRenderTarget(m_DepthRT);
            cmd.ClearRenderTarget(false, true, Color.white * 1000f);
            depthMaterial.SetFloat(ParticleSizeMultiplier, settings.particleSizeMultiplier);
            cmd.DrawMeshInstancedIndirect(particleMesh, 0, depthMaterial, -1, argsBuffer, 0, null);

            // 厚度
            cmd.SetRenderTarget(m_ThicknessRT);
            cmd.ClearRenderTarget(false, true, Color.black);
            thicknessMaterial.SetFloat(ParticleSizeMultiplier, settings.particleSizeMultiplier);
            cmd.DrawMeshInstancedIndirect(particleMesh, 0, thicknessMaterial, -1, argsBuffer, 0, null);

            // 设置全局纹理
            cmd.SetGlobalTexture(FluidDepthTexture, m_DepthRT); 
            cmd.SetGlobalTexture(FluidThicknessTexture, m_ThicknessRT);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
    
    public void Dispose()
    {
        RTHandles.Release(m_DepthRT);
        RTHandles.Release(m_ThicknessRT);
        m_DepthRT = null;
        m_ThicknessRT = null;
    }
}