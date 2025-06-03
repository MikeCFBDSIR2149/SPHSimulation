using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSFRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class SSFSettings
    {
        public RenderPipelineAsset applicationRPAsset; // 应用的渲染管线资产
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        public Material particleDepthMaterial;
        public Material particleThicknessMaterial;
        public Material bilateralBlurMaterial;
        public Material gaussianBlurMaterial; 
        public Material finalShadingMaterial;
        
        [Header("Particle Settings")]
        [Range(0.01f, 1.0f)]
        public float particleSizeMultiplier = 0.5f; // 粒子渲染大小乘数 (基于SmoothingRadiusH)
        
        [Header("BilateralBlur Smoothing Settings")]
        [Range(1, 40)]
        public int blurIterations = 5; // 模糊半径
        [Range(0.001f, 1.0f)]
        public float depthSigma = 0.05f; // 深度权重 (深度相似性)
        [Range(0.1f, 60f)]
        public float spatialSigma = 2.0f; // 空间权重 (模糊范围)
        
        [Header("GaussianBlur Smoothing Settings")]
        [Range(0f, 5.0f)]
        public float blurAmount = 1.0f;
    }

    public SSFSettings settings = new SSFSettings();
    
    private SSFParticleRenderPass ssfParticleRenderPass;
    private SSFSmoothPass ssfSmoothPass; 
    private SSFShadingPass ssfShadingPass;

    public SSFParticleRenderPass ParticlePass => ssfParticleRenderPass;
    public SSFSmoothPass SmoothPass => ssfSmoothPass;

    public override void Create()
    {
        if (!settings.particleDepthMaterial || !settings.particleThicknessMaterial)
        {
            Debug.LogError("SSF Feature: Particle materials are not set!");
            return;
        }
        if (!settings.bilateralBlurMaterial)
        {
            Debug.LogError("SSF Feature: Bilateral Blur material is not set!");
            return;
        }
        if (!settings.finalShadingMaterial)
        {
            Debug.LogError("SSF Feature: Final Shading material is not set!");
            return;
        }

        ssfParticleRenderPass = new SSFParticleRenderPass(settings);
        ssfSmoothPass = new SSFSmoothPass(settings, this);
        ssfShadingPass = new SSFShadingPass(settings, settings.finalShadingMaterial, this);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (GraphicsSettings.defaultRenderPipeline != settings.applicationRPAsset || !SimulationGlobalStatus.Instance.inSimulation) return;
        if (ssfParticleRenderPass == null || ssfSmoothPass == null || !settings.bilateralBlurMaterial) return;

        renderer.EnqueuePass(ssfParticleRenderPass);
        renderer.EnqueuePass(ssfSmoothPass);
        renderer.EnqueuePass(ssfShadingPass);
    }
    
    protected override void Dispose(bool disposing)
    {
        ssfParticleRenderPass?.Dispose();
        ssfSmoothPass?.Dispose();
        base.Dispose(disposing);
    }
}
