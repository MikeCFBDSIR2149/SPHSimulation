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
    
    [StructLayout(LayoutKind.Sequential)]
    private struct MeshProperties
    {
        public Matrix4x4 mat;
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
    // 性能优化区域
    private ComputeBuffer propertiesBuffer;
    private ComputeBuffer argsBuffer;        // 用于DrawMeshInstancedIndirect参数
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
    public Vector3 spawnVolumeCenter = Vector3.zero;
    public Vector3 spawnVolumeSize;
    
    [Header("Rendering Settings (Instanced)")]
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
        int numGroupsX = (maxParticles + threadsPerGroupX - 1) / threadsPerGroupX;
        
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
        particleDataArray = new ParticleGPU[maxParticles]; 
        meshPropertiesArray = new MeshProperties[maxParticles];
        
        for (int i = 0; i < maxParticles; i++)
        {
            float x = Random.Range(0, spawnVolumeSize.x / 2f);
            float y = Random.Range(-spawnVolumeSize.y / 2f, spawnVolumeSize.y / 2f);
            float z = Random.Range(-spawnVolumeSize.z / 2f, 0);

            Vector3 initialPosition = spawnVolumeCenter + new Vector3(x, y, z);

            particleDataArray[i] = new ParticleGPU
            {
                position = initialPosition,
                velocity = Vector3.zero,
                acceleration = Vector3.zero,
                density = 0f,
                pressure = 0f,
            };
            
            Vector3 pos = initialPosition;
            float scale = particleRenderScale;
            Matrix4x4 mat = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * scale);
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
        particleBuffer = new ComputeBuffer(maxParticles, particleStructSize);
        particleBuffer.SetData(particleDataArray);

        if (!particleComputeShader) Debug.LogError("Particle compute shader not initialized!");
        
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
        
        // 性能优化部分
        csBuildMatricesKernelID = particleComputeShader.FindKernel("CSBuildMatrices");
        
        propertiesBuffer = new ComputeBuffer(maxParticles, Marshal.SizeOf(typeof(MeshProperties)), ComputeBufferType.Structured);
        propertiesBuffer.SetData(meshPropertiesArray);

        // 初始化DrawMeshInstancedIndirect用的参数buffer
        argsBuffer?.Release();
        uint[] args = {
            particleMesh.GetIndexCount(0),
            (uint)maxParticles,
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
    public void DebugParticleTransforms()
    {
        for (int i = 0; i < maxParticles; i++)
        {
            Debug.Log($"Particle {i}: Position = {particleDataArray[i].position}, Velocity = {particleDataArray[i].velocity}");
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(spawnVolumeCenter, spawnVolumeSize);
    }
    #endregion
}
