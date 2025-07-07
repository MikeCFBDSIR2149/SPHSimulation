using UnityEngine;

public class ParticleControllerSSF : ParticleControllerGPU
{
    private static readonly int ParticleSizeMultiplier = Shader.PropertyToID("_ParticleSizeMultiplier");

    public static ParticleControllerSSF Instance { get; private set; }
    
    [Header("SSF Settings")]
    public Material ssfDepthMaterial;
    public Material ssfThicknessMaterial;
    
    private void Awake()
    {
        if (Instance && Instance != this) Destroy(this);
        else Instance = this;
    }

    private void Update()
    {
        if (ssfDepthMaterial && particleBuffer != null)
        {
            ssfDepthMaterial.SetFloat(SmoothingRadiusH, smoothingRadiusH);
            ssfDepthMaterial.SetBuffer(Particles, particleBuffer);
            ssfDepthMaterial.SetFloat(ParticleSizeMultiplier, 1.0f);
        }
        if (ssfThicknessMaterial && particleBuffer != null)
        {
            ssfThicknessMaterial.SetFloat(SmoothingRadiusH, smoothingRadiusH);
            ssfThicknessMaterial.SetBuffer(Particles, particleBuffer);
            ssfThicknessMaterial.SetFloat(ParticleSizeMultiplier, 1.0f);
        }
    }

    protected override void LateUpdate()
    {
        
    }
    
    // 获取粒子数据缓冲区 (用于RenderPass)
    public ComputeBuffer GetArgsBuffer()
    {
        return argsBuffer;
    }
    
    // 获取粒子网格 (用于RenderPass)
    public ComputeBuffer GetParticleBuffer()
    {
        if (particleBuffer != null) 
            return particleBuffer;
        Debug.LogError("Particle buffer is not initialized.");
        return null;
    }
}
