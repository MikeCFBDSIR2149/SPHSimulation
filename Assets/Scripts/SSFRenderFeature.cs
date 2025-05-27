using UnityEngine;
using UnityEngine.Rendering.Universal;

public class SSFRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class SSFSettings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        public Material particleDepthMaterial; // 渲染深度的材质
        public Material particleThicknessMaterial; // 渲染厚度的材质
        
        [Header("Particle Settings")]
        [Range(0.01f, 1.0f)]
        public float particleSizeMultiplier = 0.5f; // 粒子渲染大小
        
        [Header("Smoothing Settings")]
        public Material bilateralBlurMaterial; // 用于双边滤波的材质
        [Range(1, 15)]
        public int blurIterations = 5; // 模糊半径 (采样点数 = iterations*2+1)
        [Range(0.001f, 1.0f)]
        public float depthSigma = 0.05f; // 深度权重 Sigma (控制深度相似性)
        [Range(0.1f, 10.0f)]
        public float spatialSigma = 2.0f; // 空间权重 Sigma (控制模糊范围)
    }

    public SSFSettings settings = new SSFSettings();
    
    private SSFParticleRenderPass ssfParticleRenderPass;
    private SSFSmoothPass ssfSmoothPass; 

    public SSFParticleRenderPass ParticlePass => ssfParticleRenderPass;

    public override void Create()
    {
        if (!settings.particleDepthMaterial || !settings.particleThicknessMaterial)
        {
            Debug.LogError("SSF Feature: Particle materials are not set!");
            return;
        }

        ssfParticleRenderPass = new SSFParticleRenderPass(settings);
        ssfSmoothPass = new SSFSmoothPass(settings, this);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (ssfParticleRenderPass == null || ssfSmoothPass == null || !settings.bilateralBlurMaterial) return;

        renderer.EnqueuePass(ssfParticleRenderPass);
        renderer.EnqueuePass(ssfSmoothPass);
    }
    
    protected override void Dispose(bool disposing)
    {
        ssfParticleRenderPass?.Dispose();
        ssfSmoothPass?.Dispose();
        base.Dispose(disposing);
    }
}
