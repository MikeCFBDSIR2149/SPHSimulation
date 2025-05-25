using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Random = UnityEngine.Random;

public class ParticleControllerGPU : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ParticleGPU
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 acceleration;
        public float density;
        public float pressure;
        public int type; // 0: fluid, 1: boundary
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct MeshProperties
    {
        public Matrix4x4 mat;
    }
    
    [Header("GPU Compute")]
    public ComputeShader particleComputeShader;
    private ComputeBuffer particleBuffer;
    private ComputeBuffer propertiesBuffer;
    private ComputeBuffer argsBuffer;
    private ParticleGPU[] particleDataArray;
    private const int threadsPerGroupX = 64; // numthreads in compute shader
    private int csDensityKernelID;
    private int csPressureKernelID;
    private int csForceKernelID;
    private int csIntegrateKernelID;
    private static readonly int Particles = Shader.PropertyToID("_Particles");
    private static readonly int MaxParticles = Shader.PropertyToID("_MaxParticles");
    private static readonly int ParticleMass = Shader.PropertyToID("_ParticleMass");
    private static readonly int RestDensityRho = Shader.PropertyToID("_RestDensityRho");
    private static readonly int GasConstantB = Shader.PropertyToID("_GasConstantB");
    private static readonly int PressureExponentGamma = Shader.PropertyToID("_PressureExponentGamma");
    private static readonly int ViscosityMu = Shader.PropertyToID("_ViscosityMu");
    private static readonly int Gravity = Shader.PropertyToID("_Gravity");
    private static readonly int SmoothingRadiusH = Shader.PropertyToID("_SmoothingRadiusH");
    private static readonly int HSquared = Shader.PropertyToID("_HSquared");
    private static readonly int Poly6Constant = Shader.PropertyToID("_Poly6Constant");
    private static readonly int SpikyGradientConstant = Shader.PropertyToID("_SpikyGradientConstant");
    private static readonly int ViscosityLaplacianConstant = Shader.PropertyToID("_ViscosityLaplacianConstant");
    private static readonly int DeltaTime = Shader.PropertyToID("_DeltaTime");
    private static readonly int BoundaryDamping = Shader.PropertyToID("_BoundaryDamping");
    private static readonly int SpawnVolumeCenter = Shader.PropertyToID("_SpawnVolumeCenter");
    private static readonly int SpawnVolumeSize = Shader.PropertyToID("_SpawnVolumeSize");

    private MeshProperties[] meshPropertiesArray;
    private int csBuildMatricesKernelID;
    private static readonly int ParticleRenderScale = Shader.PropertyToID("_ParticleRenderScale");
    private static readonly int Properties = Shader.PropertyToID("_Properties");


    // Parameters
    [Header("Parameters")]
    public float particleMass = 0.02f;          // 粒子模拟质量
    public float restDensityRho = 1000f;        // 水的静止密度
    public float gasConstantB = 2000f;          // Tait 状态方程中的流体刚度系数
    public float pressureExponentGamma = 7f;    // Tait 状态方程中的指数
    public float viscosityMu = 0.05f;           // 动力粘滞系数
    public Vector3 gravity = new Vector3(0.0f, -9.81f, 0.0f); // 重力加速度
    public float smoothingRadiusH = 0.1f;       // 平滑核影响半径
    private float hSquared;                     // h^2 预计算
    public float boundaryDamping;               // 边界碰撞时的速度衰减系数
    
    // Settings
    [Header("Settings")]
    public int maxParticles;
    private int actualParticles;
    public int boundaryLayers = 4; // 边界层数
    public float spacingParameter = 1.8f; // 粒子间距参数，影响粒子分布密度
    public Vector3 spawnVolumeCenter = Vector3.zero;
    public Vector3 spawnVolumeSize;
    
    [Header("Rendering Settings")]
    public Mesh particleMesh;
    public Material particleMaterial;
    public float particleRenderScale;
    
    // Kernel Constant (核函数常数)
    private float poly6Constant;
    private float spikyGradientConstant;
    private float viscosityLaplacianConstant;
    
    private void OnEnable()
    {
        // Time.timeScale = 100f;
        PreCalculateConstants();
        InitializeParticlesCPU();
        InitializeComputeShader();
    }

    private void FixedUpdate()
    {
        if (particleBuffer == null || !particleComputeShader) return;
        
        particleComputeShader.SetFloat(RestDensityRho, restDensityRho);
        particleComputeShader.SetFloat(GasConstantB, gasConstantB);
        particleComputeShader.SetFloat(PressureExponentGamma, pressureExponentGamma);
        particleComputeShader.SetFloat(ViscosityMu, viscosityMu);
        particleComputeShader.SetVector(Gravity, gravity);
        particleComputeShader.SetFloat(SmoothingRadiusH, smoothingRadiusH);
        particleComputeShader.SetFloat(HSquared, smoothingRadiusH * smoothingRadiusH);
        int numGroupsX = (actualParticles + threadsPerGroupX - 1) / threadsPerGroupX;
        
        particleComputeShader.Dispatch(csDensityKernelID, numGroupsX, 1, 1);
        particleComputeShader.Dispatch(csPressureKernelID, numGroupsX, 1, 1);
        particleComputeShader.Dispatch(csForceKernelID, numGroupsX, 1, 1);
        particleComputeShader.Dispatch(csIntegrateKernelID, numGroupsX, 1, 1);
        
        particleComputeShader.SetFloat(ParticleRenderScale, particleRenderScale);
        particleComputeShader.Dispatch(csBuildMatricesKernelID, numGroupsX, 1, 1);
        
        // particleBuffer.GetData(particleDataArray);
        // Debug.Log("Position:" + particleDataArray[0].position + 
        //           " Velocity:" + particleDataArray[0].velocity + 
        //           " Acceleration:" + particleDataArray[0].acceleration + 
        //           " Density:" + particleDataArray[0].density + 
        //           " Pressure:" + particleDataArray[0].pressure);
    }
    
    private void PreCalculateConstants()
    {
        hSquared = smoothingRadiusH * smoothingRadiusH;
        poly6Constant = 315f / (64f * Mathf.PI * Mathf.Pow(smoothingRadiusH, 9));
        spikyGradientConstant = -45f / (Mathf.PI * Mathf.Pow(smoothingRadiusH, 6));
        viscosityLaplacianConstant = 45f / (Mathf.PI * Mathf.Pow(smoothingRadiusH, 6));
    }
    
    private void InitializeParticlesCPU()
    {
        List<ParticleGPU> particles = new List<ParticleGPU>();

        float spacing = smoothingRadiusH / spacingParameter; // 粒子间距
        
        Vector3 minBounds = spawnVolumeCenter - spawnVolumeSize / 2f;
        Vector3 maxBounds = spawnVolumeCenter + spawnVolumeSize / 2f;
        
        // Debug.Log($"Bounds: Min={minBounds}, Max={maxBounds}");
        // Debug.Log($"Spacing: {spacing}, Layers: {boundaryLayers}");
        
        float boundaryExtent = (boundaryLayers - 1) * spacing; // 边界向外延伸的距离

        // 底部
        for (int l = 0; l < boundaryLayers; l++) {
            float y = minBounds.y - l * spacing;
            for (float x = minBounds.x - boundaryExtent; x <= maxBounds.x + boundaryExtent; x += spacing) {
                for (float z = minBounds.z - boundaryExtent; z <= maxBounds.z + boundaryExtent; z += spacing) {
                   particles.Add(new ParticleGPU { position = new Vector3(x, y, z), type = 1 });
                }
            }
        }

        // int startBoundaryCount = particles.Count;
        // Debug.Log($"Bottom boundary particles: {startBoundaryCount}");

        // 四周边界
        for (float y = minBounds.y + spacing; y <= maxBounds.y; y += spacing) {
             for (int l = 0; l < boundaryLayers; l++) {
                 // X-
                 float x_neg = minBounds.x - l * spacing;
                 for (float z = minBounds.z - boundaryExtent; z <= maxBounds.z + boundaryExtent; z += spacing) {
                     particles.Add(new ParticleGPU { position = new Vector3(x_neg, y, z), type = 1 });
                 }
                 // X+
                 float x_pos = maxBounds.x + l * spacing;
                 for (float z = minBounds.z - boundaryExtent; z <= maxBounds.z + boundaryExtent; z += spacing) {
                     particles.Add(new ParticleGPU { position = new Vector3(x_pos, y, z), type = 1 });
                 }
                 // Z-
                 float z_neg = minBounds.z - l * spacing;
                 for (float x = minBounds.x + spacing; x <= maxBounds.x - spacing; x += spacing) {
                     particles.Add(new ParticleGPU { position = new Vector3(x, y, z_neg), type = 1 });
                 }
                 // Z+
                 float z_pos = maxBounds.z + l * spacing;
                  for (float x = minBounds.x + spacing; x <= maxBounds.x - spacing; x += spacing) {
                     particles.Add(new ParticleGPU { position = new Vector3(x, y, z_pos), type = 1 });
                 }
             }
        }
        // int boundaryCount = particles.Count;
        // Debug.Log($"Total boundary particles: {boundaryCount}");
        
        int fluidCount = 0;
        while (fluidCount < maxParticles)
        {
            float buffer = spacing * 0.2f; // 防止生成在边界上
            float x = Random.Range(buffer, maxBounds.x - buffer);
            float y = Random.Range(minBounds.y + buffer, maxBounds.y - buffer);
            float z = Random.Range(minBounds.z + buffer, - buffer);

            particles.Add(new ParticleGPU
            {
                position = new Vector3(x, y, z),
                velocity = Vector3.zero,
                acceleration = Vector3.zero,
                density = 0f,
                pressure = 0f,
                type = 0 // 流体粒子
            });
            fluidCount++;
            if (particles.Count < 9999999)
                continue;
            Debug.LogWarning("极高上限！熔断机制触发！");
            break;
        }

        actualParticles = particles.Count;

        particleDataArray = particles.ToArray();
        meshPropertiesArray = new MeshProperties[actualParticles];
        
        for (int i = 0; i < actualParticles; i++)
        {
            Vector3 pos = particleDataArray[i].position;
            // 初始缩放设为 1，稍后 CSBuildMatrices 会隐藏边界粒子
            Matrix4x4 mat = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * particleRenderScale);
            meshPropertiesArray[i].mat = mat;
        }
    }

    private void InitializeComputeShader()
    {
        if (particleDataArray == null || particleDataArray.Length == 0)
        {
            Debug.LogWarning("Particle data array not initialized!");
            return;
        }

        int particleStructSize = Marshal.SizeOf<ParticleGPU>();
        particleBuffer = new ComputeBuffer(actualParticles, particleStructSize);
        particleBuffer.SetData(particleDataArray);

        if (!particleComputeShader) Debug.LogError("Particle compute shader not initialized!");
        
        csDensityKernelID = particleComputeShader.FindKernel("CSComputeDensity");
        csPressureKernelID = particleComputeShader.FindKernel("CSComputePressure");
        csForceKernelID = particleComputeShader.FindKernel("CSComputeForce");
        csIntegrateKernelID = particleComputeShader.FindKernel("CSIntegrate");
        csBuildMatricesKernelID = particleComputeShader.FindKernel("CSBuildMatrices");
        
        // 绑定 ComputeBuffer
        particleComputeShader.SetBuffer(csDensityKernelID, Particles, particleBuffer);
        particleComputeShader.SetBuffer(csPressureKernelID, Particles, particleBuffer);
        particleComputeShader.SetBuffer(csForceKernelID, Particles, particleBuffer);
        particleComputeShader.SetBuffer(csIntegrateKernelID, Particles, particleBuffer);
        
        // 设置 Compute Shader 常量参数
        particleComputeShader.SetInt(MaxParticles, actualParticles);
        particleComputeShader.SetFloat(ParticleMass, particleMass);
        particleComputeShader.SetFloat(RestDensityRho, restDensityRho);
        particleComputeShader.SetFloat(GasConstantB, gasConstantB);
        particleComputeShader.SetFloat(PressureExponentGamma, pressureExponentGamma);
        particleComputeShader.SetFloat(ViscosityMu, viscosityMu);
        particleComputeShader.SetVector(Gravity, gravity);
        particleComputeShader.SetFloat(SmoothingRadiusH, smoothingRadiusH);
        particleComputeShader.SetFloat(HSquared, hSquared);
        
        particleComputeShader.SetFloat(Poly6Constant, poly6Constant);
        particleComputeShader.SetFloat(SpikyGradientConstant, spikyGradientConstant);
        particleComputeShader.SetFloat(ViscosityLaplacianConstant, viscosityLaplacianConstant);
        
        particleComputeShader.SetFloat(DeltaTime, Time.fixedDeltaTime);
        particleComputeShader.SetFloat(BoundaryDamping, boundaryDamping);
        particleComputeShader.SetVector(SpawnVolumeCenter, spawnVolumeCenter);
        particleComputeShader.SetVector(SpawnVolumeSize, spawnVolumeSize);

        propertiesBuffer = new ComputeBuffer(actualParticles, Marshal.SizeOf(typeof(MeshProperties)), ComputeBufferType.Structured);
        propertiesBuffer.SetData(meshPropertiesArray);
        
        argsBuffer?.Release();
        uint[] args = {
            particleMesh.GetIndexCount(0),
            (uint)actualParticles,
            particleMesh.GetIndexStart(0),
            particleMesh.GetBaseVertex(0),
            0
        };
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);
        
        particleComputeShader.SetBuffer(csBuildMatricesKernelID, Particles, particleBuffer);
        particleComputeShader.SetBuffer(csBuildMatricesKernelID, Properties, propertiesBuffer);
    }

    private void LateUpdate()
    { 
        if (propertiesBuffer == null || !particleMaterial || !particleMesh || argsBuffer == null) return;
        
        particleMaterial.SetBuffer(Properties, propertiesBuffer);
        
        Bounds bounds = new Bounds(spawnVolumeCenter, spawnVolumeSize * 2f);

        Graphics.DrawMeshInstancedIndirect(
            particleMesh,
            0,
            particleMaterial,
            bounds,
            argsBuffer
        );
    }

    private void OnDisable()
    {
        ClearBuffers();
    }
    
    private void OnDestroy()
    {
        ClearBuffers();
    }

    // OnDestroy 释放 ComputeBuffer
    private void ClearBuffers()
    {
        if (particleBuffer != null)
        {
            particleBuffer.Release();
            particleBuffer = null;
        }
        if (propertiesBuffer != null)
        {
            propertiesBuffer.Release();
            propertiesBuffer = null;
        }
        if (argsBuffer != null) 
        { 
            argsBuffer.Release();
            argsBuffer = null;
            
        }
    }

    #region Debug
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(spawnVolumeCenter, spawnVolumeSize);
    }
    #endregion
}
