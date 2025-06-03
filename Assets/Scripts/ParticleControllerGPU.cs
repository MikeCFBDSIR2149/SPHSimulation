using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Random = UnityEngine.Random;

public class ParticleControllerGPU : MonoBehaviour
{
    public static ParticleControllerGPU Instance { get; private set; } // 单例
    
    // 粒子
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
    
    // 渲染数据结构
    [StructLayout(LayoutKind.Sequential)]
    private struct MeshProperties
    {
        public Matrix4x4 mat;
    }
    
    // 哈希结构
    [StructLayout(LayoutKind.Sequential)]
    private struct ParticleHash
    {
        public uint hash;
        public uint index;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct GridInfo
    {
        public uint start; // 起始索引
        public uint end;   // 结束索引 (不包含)
    }
    
    // Compute Shader 相关参数
    [Header("GPU Compute")]
    public ComputeShader particleComputeShader;
    public ComputeShader bitonicSortShader;
    protected ComputeBuffer particleBuffer;
    private ComputeBuffer propertiesBuffer;
    protected ComputeBuffer argsBuffer;
    private ComputeBuffer particleHashBuffer;
    private ComputeBuffer gridInfoBuffer;
    
    private const int threadsPerGroupX_SPH = 64;
    private const int threadsPerGroupX_Sort = 256;
    
    private Vector3Int gridSize;                // 网格尺寸 (网格数量的尺寸)
    private Vector3 gridWorldMin;               // 网格世界坐标最小值
    private int gridTotalCells;
    
    private ParticleGPU[] particleDataArray;
    private int csDensityKernelID;
    private int csPressureKernelID;
    private int csForceKernelID;
    private int csIntegrateKernelID;
    protected static readonly int Particles = Shader.PropertyToID("_Particles");
    private static readonly int ActualParticleCount = Shader.PropertyToID("_ActualParticleCount");
    private static readonly int PaddedParticleCount = Shader.PropertyToID("_PaddedParticleCount");
    private static readonly int ParticleMass = Shader.PropertyToID("_ParticleMass");
    private static readonly int RestDensityRho = Shader.PropertyToID("_RestDensityRho");
    private static readonly int GasConstantB = Shader.PropertyToID("_GasConstantB");
    private static readonly int PressureExponentGamma = Shader.PropertyToID("_PressureExponentGamma");
    private static readonly int ViscosityMu = Shader.PropertyToID("_ViscosityMu");
    private static readonly int Gravity = Shader.PropertyToID("_Gravity");
    protected static readonly int SmoothingRadiusH = Shader.PropertyToID("_SmoothingRadiusH");
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
    
    private int csCalculateHashKernelID;
    private static readonly int ParticleHashes = Shader.PropertyToID("_ParticleHashes");
    private static readonly int GridCellSize = Shader.PropertyToID("_GridCellSize");
    private static readonly int GridMin = Shader.PropertyToID("_GridMin");
    private static readonly int GridSize = Shader.PropertyToID("_GridSize");
    
    private int csBuildGridInfoKernelID;
    private int csClearGridInfoKernelID;
    private static readonly int GridInfoBuffer = Shader.PropertyToID("_GridInfo");
    
    private int csSortKernelID;
    private static readonly int Data = Shader.PropertyToID("_Data");
    private static readonly int Level = Shader.PropertyToID("_Level");
    private static readonly int LevelMask = Shader.PropertyToID("_LevelMask");
    private static readonly int NumEntries = Shader.PropertyToID("_NumEntries");

    // Parameters
    [Header("Parameters")]
    public float particleMass = 0.02f;          // 粒子模拟质量
    public float pressureExponentGamma = 7f;    // Tait 状态方程中的指数
    public Vector3 gravity = new Vector3(0.0f, -9.81f, 0.0f); // 重力加速度
    public float smoothingRadiusH = 0.1f;       // 平滑核影响半径
    private float hSquared;                     // h^2 预计算
    public float boundaryDamping;               // 边界碰撞时的速度衰减系数
    
    [Header("Parameters (Support Dynamic Change in Simulation)")]
    public float restDensityRho = 1000f;        // 水的静止密度
    public float gasConstantB = 2000f;          // Tait 状态方程中的流体刚度系数
    public float viscosityMu = 0.05f;           // 动力粘滞系数
    
    // Settings
    [Header("Settings")]
    public int targetParticleCount;             // 目标粒子数
    private int actualParticleCount;            // 实际总粒子数
    private int paddedParticleCount;            // Bitonic Sort 要求 2^n，因此必须填补未满的粒子数量
    public int boundaryLayers = 4;              // 边界层数
    public float spacingParameter = 1.8f;       // 粒子间距参数，影响粒子分布密度 (边界粒子)
    public Vector3 spawnVolumeCenter = Vector3.zero;
    public Vector3 spawnVolumeSize;
    
    [Header("Rendering Settings")]
    public Mesh particleMesh;
    
    [Header("Rendering Settings (Unavailable if using SSF)")]
    public Material particleMaterial;
    public float particleRenderScale;
    
    // Kernel Constant (核函数常数)
    private float poly6Constant;
    private float spikyGradientConstant;
    private float viscosityLaplacianConstant;
    
    private void Awake()
    {
        if (Instance && Instance != this) Destroy(this);
        else Instance = this;
    }
    
    private void OnEnable()
    {
        PreCalculateConstants();
        InitializeParticlesCPU();
        CalculateGrids();
        CalculatePaddedSize();
        InitializeComputeShader();
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
        while (fluidCount < targetParticleCount)
        {
            float buffer = spacing * 0.2f;
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
                type = 0
            });
            fluidCount++;
            if (particles.Count < 999999)
                continue;
            Debug.LogWarning("极高上限！熔断机制触发！");
            break;
        }

        actualParticleCount = particles.Count;
        particleDataArray = particles.ToArray();
        meshPropertiesArray = new MeshProperties[actualParticleCount];
        
        for (int i = 0; i < actualParticleCount; i++)
        { 
            // 初始缩放设为 1
            Matrix4x4 mat = Matrix4x4.TRS(particleDataArray[i].position, Quaternion.identity, Vector3.one * particleRenderScale);
            meshPropertiesArray[i].mat = mat;
        }
    }

    private void CalculateGrids()
    {
        Vector3 worldMin = spawnVolumeCenter - spawnVolumeSize / 2f;
        Vector3 worldMax = spawnVolumeCenter + spawnVolumeSize / 2f;
        float boundaryOffset = boundaryLayers * (smoothingRadiusH / spacingParameter); 
        
        gridWorldMin = worldMin - new Vector3(boundaryOffset, boundaryOffset, boundaryOffset);
        Vector3 worldSize = worldMax + new Vector3(boundaryOffset, boundaryOffset, boundaryOffset) - gridWorldMin;
        gridSize = new Vector3Int(
            Mathf.CeilToInt(worldSize.x / smoothingRadiusH),
            Mathf.CeilToInt(worldSize.y / smoothingRadiusH),
            Mathf.CeilToInt(worldSize.z / smoothingRadiusH)
        );
        
        gridTotalCells = gridSize.x * gridSize.y * gridSize.z;
    }

    private void CalculatePaddedSize()
    {
        paddedParticleCount = Mathf.NextPowerOfTwo(actualParticleCount);
    }

    private void InitializeComputeShader()
    {
        if (particleDataArray == null || particleDataArray.Length == 0)
        {
            Debug.LogWarning("Particle data array not initialized!");
            return;
        }
        if (!particleComputeShader) Debug.LogError("Particle compute shader not initialized!");
        
        // 计算部分
        particleBuffer = new ComputeBuffer(actualParticleCount, Marshal.SizeOf<ParticleGPU>());
        particleBuffer.SetData(particleDataArray);

        particleHashBuffer = new ComputeBuffer(paddedParticleCount, Marshal.SizeOf<ParticleHash>());
        ParticleHash[] initialHashes = new ParticleHash[paddedParticleCount];
        for (int i = 0; i < paddedParticleCount; i++)
        {
            initialHashes[i].hash = uint.MaxValue;
            initialHashes[i].index = uint.MaxValue;
        }
        particleHashBuffer.SetData(initialHashes);
        
        gridInfoBuffer = new ComputeBuffer(gridTotalCells, Marshal.SizeOf<GridInfo>());
        GridInfo[] initialGridInfo = new GridInfo[gridTotalCells];
        for(int i = 0; i < gridTotalCells; i++)
        {
            initialGridInfo[i].start = 0xFFFFFFFF; // 无效
            initialGridInfo[i].end = 0;
        }
        gridInfoBuffer.SetData(initialGridInfo);
        
        csCalculateHashKernelID = particleComputeShader.FindKernel("CSCalculateHash");
        csDensityKernelID = particleComputeShader.FindKernel("CSComputeDensity");
        csPressureKernelID = particleComputeShader.FindKernel("CSComputePressure");
        csForceKernelID = particleComputeShader.FindKernel("CSComputeForce");
        csIntegrateKernelID = particleComputeShader.FindKernel("CSIntegrate");
        csBuildMatricesKernelID = particleComputeShader.FindKernel("CSBuildMatrices");
        
        csClearGridInfoKernelID = particleComputeShader.FindKernel("CSClearGridInfo");
        csBuildGridInfoKernelID = particleComputeShader.FindKernel("CSBuildGridInfo");
        
        csSortKernelID = bitonicSortShader.FindKernel("CSBitonicSort");
        
        particleComputeShader.SetBuffer(csCalculateHashKernelID, Particles, particleBuffer);
        particleComputeShader.SetBuffer(csCalculateHashKernelID, ParticleHashes, particleHashBuffer);
        
        particleComputeShader.SetBuffer(csBuildGridInfoKernelID, ParticleHashes, particleHashBuffer);
        particleComputeShader.SetBuffer(csBuildGridInfoKernelID, GridInfoBuffer, gridInfoBuffer);
        particleComputeShader.SetBuffer(csClearGridInfoKernelID, GridInfoBuffer, gridInfoBuffer);
        
        particleComputeShader.SetBuffer(csDensityKernelID, Particles, particleBuffer);
        particleComputeShader.SetBuffer(csDensityKernelID, ParticleHashes, particleHashBuffer);
        particleComputeShader.SetBuffer(csDensityKernelID, GridInfoBuffer, gridInfoBuffer);
        particleComputeShader.SetBuffer(csPressureKernelID, Particles, particleBuffer);
        particleComputeShader.SetBuffer(csForceKernelID, Particles, particleBuffer);
        particleComputeShader.SetBuffer(csForceKernelID, ParticleHashes, particleHashBuffer);
        particleComputeShader.SetBuffer(csForceKernelID, GridInfoBuffer, gridInfoBuffer);
        particleComputeShader.SetBuffer(csIntegrateKernelID, Particles, particleBuffer);
    
        bitonicSortShader.SetBuffer(csSortKernelID, Data, particleHashBuffer);
        
        particleComputeShader.SetInt(ActualParticleCount, actualParticleCount);
        particleComputeShader.SetInt(PaddedParticleCount, paddedParticleCount);
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
        
        particleComputeShader.SetFloat(GridCellSize, smoothingRadiusH);
        particleComputeShader.SetVector(GridMin, gridWorldMin);
        particleComputeShader.SetInts(GridSize, gridSize.x, gridSize.y, gridSize.z);
        particleComputeShader.SetInt(PaddedParticleCount, paddedParticleCount);
        bitonicSortShader.SetInt(NumEntries, paddedParticleCount);
        
        // 粒子渲染部分
        propertiesBuffer = new ComputeBuffer(actualParticleCount, Marshal.SizeOf(typeof(MeshProperties)), ComputeBufferType.Structured);
        propertiesBuffer.SetData(meshPropertiesArray);
        
        argsBuffer?.Release();
        uint[] args = {
            particleMesh.GetIndexCount(0),
            (uint)actualParticleCount,
            particleMesh.GetIndexStart(0),
            particleMesh.GetBaseVertex(0),
            0
        };
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);
        
        particleComputeShader.SetBuffer(csBuildMatricesKernelID, Particles, particleBuffer);
        particleComputeShader.SetBuffer(csBuildMatricesKernelID, Properties, propertiesBuffer);
    }

    private void FixedUpdate()
    {
        if (particleBuffer == null || !particleComputeShader) return;
        
        particleComputeShader.SetFloat(RestDensityRho, restDensityRho);
        particleComputeShader.SetFloat(GasConstantB, gasConstantB);
        particleComputeShader.SetFloat(ViscosityMu, viscosityMu);
        
        int numGroupsX_SPH = (actualParticleCount + threadsPerGroupX_SPH - 1) / threadsPerGroupX_SPH;
        int numGroupsX_Hash = (paddedParticleCount + threadsPerGroupX_SPH - 1) / threadsPerGroupX_SPH;
        int numGroupsX_Grid = (gridTotalCells + threadsPerGroupX_SPH - 1) / threadsPerGroupX_SPH;
        int numGroupsX_Sort = (paddedParticleCount / 2 + threadsPerGroupX_Sort - 1) / threadsPerGroupX_Sort;
        
        particleComputeShader.Dispatch(csCalculateHashKernelID, numGroupsX_Hash, 1, 1);
        
        DispatchSort(numGroupsX_Sort);
        
        particleComputeShader.Dispatch(csClearGridInfoKernelID, numGroupsX_Grid, 1, 1);
        particleComputeShader.Dispatch(csBuildGridInfoKernelID, numGroupsX_SPH, 1, 1);
        
        particleComputeShader.Dispatch(csDensityKernelID, numGroupsX_SPH, 1, 1);
        particleComputeShader.Dispatch(csPressureKernelID, numGroupsX_SPH, 1, 1);
        particleComputeShader.Dispatch(csForceKernelID, numGroupsX_SPH, 1, 1);
        particleComputeShader.Dispatch(csIntegrateKernelID, numGroupsX_SPH, 1, 1);
        
        particleComputeShader.SetFloat(ParticleRenderScale, particleRenderScale);
        particleComputeShader.Dispatch(csBuildMatricesKernelID, numGroupsX_SPH, 1, 1);
    }
    
    private void DispatchSort(int numGroups)
    {
        int N = paddedParticleCount;
        if (N <= 1) return;

        int logN = (int)Mathf.Log(N, 2);

        for (int level = 0; level < logN; level++)
        {
            for (int levelMask = 0; levelMask <= level; levelMask++) 
            {
                bitonicSortShader.SetInt(Level, level);
                bitonicSortShader.SetInt(LevelMask, levelMask);
                bitonicSortShader.Dispatch(csSortKernelID, numGroups, 1, 1);
            }
        }
        // CheckSortResult(); 
    }
    
    protected virtual void LateUpdate()
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

    // 释放 ComputeBuffer
    private void ClearBuffers()
    {
        particleBuffer?.Release();
        particleBuffer = null;
        particleHashBuffer?.Release();
        particleHashBuffer = null;
        propertiesBuffer?.Release();
        propertiesBuffer = null;
        argsBuffer?.Release();
        argsBuffer = null;
        gridInfoBuffer?.Release();
        gridInfoBuffer = null;
    }

    #region Debug
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(spawnVolumeCenter, spawnVolumeSize);
    }
    
    // private void CheckSortResult()
    // {
    //     ParticleHash[] sortedHashes = new ParticleHash[paddedParticleCount];
    //     particleHashBuffer.GetData(sortedHashes);
    //     bool sorted = true;
    //     for (int i = 0; i < actualParticleCount - 1; i++) 
    //     {
    //         if (sortedHashes[i].hash > sortedHashes[i + 1].hash && sortedHashes[i + 1].hash != uint.MaxValue)
    //         {
    //             Debug.LogError($"Sort Error at index {i}: ({sortedHashes[i].hash}, {sortedHashes[i].index}) > ({sortedHashes[i+1].hash}, {sortedHashes[i+1].index})");
    //             sorted = false;
    //             break;
    //         }
    //     }
    //     if(sorted) Debug.Log("Sort SUCCESSFUL!");
    // }
    #endregion
}
