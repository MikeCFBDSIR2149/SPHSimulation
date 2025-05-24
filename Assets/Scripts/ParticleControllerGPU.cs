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
    }
    
    [Header("GPU Compute")]
    public ComputeShader particleComputeShader;
    private ComputeBuffer particleBuffer;
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
    public Vector3 spawnVolumeCenter = Vector3.zero;
    public Vector3 spawnVolumeSize;
    
    [Header("Rendering Settings (Instanced)")]
    public Mesh particleMesh;
    public Material particleMaterial;
    public float particleRenderScale;

    private Matrix4x4[] particleMatrices;
    
    // Kernel Constant (核函数常数)
    private float poly6Constant;
    private float spikyGradientConstant;
    private float viscosityLaplacianConstant;
    
    private void Start()
    {
        PreCalculateConstants();
        InitializeParticlesCPU();
        InitializeComputeShader();
    }

    private void FixedUpdate()
    {
        if (particleBuffer == null || !particleComputeShader) return;
        
        int numGroupsX = (maxParticles + threadsPerGroupX - 1) / threadsPerGroupX;
        
        particleComputeShader.Dispatch(csDensityKernelID, numGroupsX, 1, 1);
        particleComputeShader.Dispatch(csPressureKernelID, numGroupsX, 1, 1);
        particleComputeShader.Dispatch(csForceKernelID, numGroupsX, 1, 1);
        particleComputeShader.Dispatch(csIntegrateKernelID, numGroupsX, 1, 1);
        
        particleBuffer.GetData(particleDataArray);
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
        particleDataArray = new ParticleGPU[maxParticles]; 
        particleMatrices = new Matrix4x4[maxParticles];
        
        for (int i = 0; i < maxParticles; i++)
        {
            float x = Random.Range(-spawnVolumeSize.x / 2f, spawnVolumeSize.x / 2f);
            float y = Random.Range(-spawnVolumeSize.y / 2f, spawnVolumeSize.y / 2f);
            float z = Random.Range(-spawnVolumeSize.z / 2f, spawnVolumeSize.z / 2f);

            Vector3 initialPosition = spawnVolumeCenter + new Vector3(x, y, z);

            particleDataArray[i] = new ParticleGPU
            {
                position = initialPosition,
                velocity = Vector3.zero,
                acceleration = Vector3.zero,
                density = 0f,
                pressure = 0f,
            };

            particleMatrices[i] = Matrix4x4.TRS(
                initialPosition,
                Quaternion.identity,
                Vector3.one * particleRenderScale
            );
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
        particleBuffer = new ComputeBuffer(maxParticles, particleStructSize);
        particleBuffer.SetData(particleDataArray);

        if (particleComputeShader)
        {
            csDensityKernelID = particleComputeShader.FindKernel("CSComputeDensity");
            csPressureKernelID = particleComputeShader.FindKernel("CSComputePressure");
            csForceKernelID = particleComputeShader.FindKernel("CSComputeForce");
            csIntegrateKernelID = particleComputeShader.FindKernel("CSIntegrate");
            
            // 绑定 ComputeBuffer
            particleComputeShader.SetBuffer(csDensityKernelID, Particles, particleBuffer);
            particleComputeShader.SetBuffer(csPressureKernelID, Particles, particleBuffer);
            particleComputeShader.SetBuffer(csForceKernelID, Particles, particleBuffer);
            particleComputeShader.SetBuffer(csIntegrateKernelID, Particles, particleBuffer);
            
            // 设置 Compute Shader 常量参数
            particleComputeShader.SetInt(MaxParticles, maxParticles);
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
        }
        else
        {
            Debug.LogError("Compute Shader NULL!");
        }
    }

    private void LateUpdate()
    {
        if (particleDataArray == null || particleBuffer == null || !particleMesh || !particleMaterial) return; // 增加了 particleDataArray 的检查
        
        for (int i = 0; i < maxParticles; i++)
        {
            particleMatrices[i] = Matrix4x4.TRS(
                particleDataArray[i].position,
                Quaternion.identity,
                Vector3.one * particleRenderScale
            );
        }
    
        // Unity Documentation: DrawMeshInstanced 一次最多绘制 1023 个实例
        const int maxInstancesPerCall = 1023;
        // Debug.Log("The first particle acceleration: " + particleDataArray[0].acceleration);

        if (maxParticles <= maxInstancesPerCall)
        {
            Graphics.DrawMeshInstanced(
                particleMesh,
                0,
                particleMaterial,
                particleMatrices,
                maxParticles
            );
            return;
        }

        // 数量超限批处理
        int numBatches = (maxParticles + maxInstancesPerCall - 1) / maxInstancesPerCall;
        for (int batchIndex = 0; batchIndex < numBatches; batchIndex++)
        {
            int startIndex = batchIndex * maxInstancesPerCall;
            int countInBatch = Mathf.Min(maxInstancesPerCall, maxParticles - startIndex);

            Matrix4x4[] batchMatrices = new Matrix4x4[countInBatch];
            System.Array.Copy(particleMatrices, startIndex, batchMatrices, 0, countInBatch);

            Graphics.DrawMeshInstanced(
                particleMesh,
                0,
                particleMaterial,
                batchMatrices,
                countInBatch
            );
        }
    }

    // OnDestroy 释放 ComputeBuffer
    private void OnDestroy()
    {
        if (particleBuffer == null) return;
        particleBuffer.Release();
        particleBuffer = null;
    }
}
