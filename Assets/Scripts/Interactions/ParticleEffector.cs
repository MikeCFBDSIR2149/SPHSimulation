using System.Runtime.InteropServices;
using UnityEngine;

namespace Interactions
{
    
    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleEffectorInfo
    {
        public Vector3 center;
        public Vector3 size;
        public Matrix4x4 worldToLocalMatrix;
        public float stiffness;
    }
    
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class ParticleEffector : MonoBehaviour
    {
        private static readonly int ActualParticleCount = Shader.PropertyToID("_ActualParticleCount");
        private static readonly int SmoothingRadiusH = Shader.PropertyToID("_SmoothingRadiusH");
        private static readonly int HSquared = Shader.PropertyToID("_HSquared");
        private static readonly int ParticleMass = Shader.PropertyToID("_ParticleMass");
        private static readonly int Poly6Constant = Shader.PropertyToID("_Poly6Constant");
        private static readonly int GridMin = Shader.PropertyToID("_GridMin");
        private static readonly int GridCellSize = Shader.PropertyToID("_GridCellSize");
        private static readonly int GridSize = Shader.PropertyToID("_GridSize");
        private static readonly int Particles = Shader.PropertyToID("_Particles");
        private static readonly int ParticleHashes = Shader.PropertyToID("_ParticleHashes");
        private static readonly int GridInfo = Shader.PropertyToID("_GridInfo");
        private static readonly int SamplePointsWorld = Shader.PropertyToID("_SamplePointsWorld");
        private static readonly int DensitiesResult = Shader.PropertyToID("_DensitiesResult");
        
        [Header("Fluid Attributes")]
        public float fluidDensity = 1000f; // Density of the fluid in kg/m^3
        
        [Header("Interaction Settings")]
        public float stiffness = 30000.0f; 
        
        [Header("Sampling Settings")]
        [Range(1, 10)] public int pointsPerAxis = 4;
        [Range(0.01f, 0.5f)] public float debugGizmoSize = 0.05f;

        private Rigidbody rb;
        private Collider col;

        private ParticleControllerGPU activeParticleController;
        private ComputeShader computeShader;

        private int csSamplePointDensityKernel;
        private ComputeBuffer worldSamplePointsBuffer;
        private ComputeBuffer densitiesBuffer;
        
        // 采样点
        private Vector3[] localSamplePoints;
        private Vector3[] worldSamplePoints;
        private float[] samplePointDensities;
        private float samplePointVolume;
        
        private bool isInitialized;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
        }

        private void Start()
        {
            SimulationGlobalStatus.Instance.RegisterParticleEffector(this);
            try
            {
                activeParticleController = SimulationGlobalStatus.Instance.GetSimulationController().ReturnCurrentController() as ParticleControllerGPU;
            }
            catch
            {
                Debug.LogWarning("未找到 ParticleControllerGPU", this);
                enabled = false;
                return;
            }
            
            if (activeParticleController == null)
            {
                Debug.LogWarning("未找到激活的 ParticleControllerGPU", this);
                enabled = false;
                return;
            }
            
            computeShader = activeParticleController.particleComputeShader;
            if (computeShader == null)
            {
                Debug.LogError("获取 Compute Shader 失败", this);
                enabled = false;
                return;
            }
            
            GenerateSamplePoints();
            InitializeBuffers();
            
            try
            {
                csSamplePointDensityKernel = computeShader.FindKernel("CSCalculateDensityAtPoints");
            }
            catch
            {
                enabled = false;
                return;
            }
        
            isInitialized = true;
        }
        
        public ParticleEffectorInfo GetEffectorInfo()
        {
            var info = new ParticleEffectorInfo();
            Bounds bounds = col.bounds;

            info.center = bounds.center;
            info.size = bounds.size;
            info.worldToLocalMatrix = transform.worldToLocalMatrix; 
            info.stiffness = this.stiffness;
            
            return info;
        }

        private void FixedUpdate()
        {
            if (!isInitialized || !gameObject.activeInHierarchy) return;
            
            for (int i = 0; i < localSamplePoints.Length; i++)
            {
                worldSamplePoints[i] = transform.TransformPoint(localSamplePoints[i]);
            }
            worldSamplePointsBuffer.SetData(worldSamplePoints);

            ParticleControllerParams param = ParticleControllerParams.Instance;

            computeShader.SetInt(ActualParticleCount, activeParticleController.actualParticleCount);
            computeShader.SetFloat(SmoothingRadiusH, param.smoothingRadiusH);
            computeShader.SetFloat(HSquared, param.smoothingRadiusH * param.smoothingRadiusH);
            computeShader.SetFloat(ParticleMass, activeParticleController.particleMass);
            computeShader.SetFloat(Poly6Constant, activeParticleController.poly6Constant);
           
            computeShader.SetFloats(GridMin, activeParticleController.gridWorldMin.x, activeParticleController.gridWorldMin.y, activeParticleController.gridWorldMin.z);
            computeShader.SetFloat(GridCellSize, param.smoothingRadiusH);
            computeShader.SetInts(GridSize, activeParticleController.gridSize.x, activeParticleController.gridSize.y, activeParticleController.gridSize.z);

            computeShader.SetBuffer(csSamplePointDensityKernel, Particles, activeParticleController.particleBuffer);
            computeShader.SetBuffer(csSamplePointDensityKernel, ParticleHashes, activeParticleController.particleHashBuffer);
            computeShader.SetBuffer(csSamplePointDensityKernel, GridInfo, activeParticleController.gridInfoBuffer);
            computeShader.SetBuffer(csSamplePointDensityKernel, SamplePointsWorld, worldSamplePointsBuffer);
            computeShader.SetBuffer(csSamplePointDensityKernel, DensitiesResult, densitiesBuffer);

            int threadGroups = Mathf.CeilToInt(localSamplePoints.Length / 64f);
            computeShader.Dispatch(csSamplePointDensityKernel, threadGroups, 1, 1);

            densitiesBuffer.GetData(samplePointDensities);

            ApplyBuoyancyForce();
        }

        // 简单竖直向上浮力
        private void ApplyBuoyancyForce()
        {
            for (int i = 0; i < localSamplePoints.Length; i++)
            {
                if (samplePointDensities[i] > ParticleControllerParams.Instance.restDensityRho * 0.5f)
                {
                    float buoyancyMagnitude = fluidDensity * Physics.gravity.magnitude * samplePointVolume;
                    Vector3 buoyancyForce = Vector3.up * buoyancyMagnitude;
                    rb.AddForceAtPosition(buoyancyForce, worldSamplePoints[i]);
                }
            }
        }

        private void GenerateSamplePoints()
        {
            Bounds bounds = col.bounds;
            int totalPoints = pointsPerAxis * pointsPerAxis * pointsPerAxis;
            localSamplePoints = new Vector3[totalPoints];
            
            float totalVolume = bounds.size.x * bounds.size.y * bounds.size.z;
            samplePointVolume = totalVolume / totalPoints;
            
            int i = 0;
            
            for (int x = 0; x < pointsPerAxis; x++)
            {
                for (int y = 0; y < pointsPerAxis; y++)
                {
                    for (int z = 0; z < pointsPerAxis; z++)
                    {
                        float tx = (pointsPerAxis == 1) ? 0.5f : (float)x / (pointsPerAxis - 1);
                        float ty = (pointsPerAxis == 1) ? 0.5f : (float)y / (pointsPerAxis - 1);
                        float tz = (pointsPerAxis == 1) ? 0.5f : (float)z / (pointsPerAxis - 1);

                        Vector3 pointLocal = new Vector3(
                            (tx - 0.5f) * bounds.size.x,
                            (ty - 0.5f) * bounds.size.y,
                            (tz - 0.5f) * bounds.size.z
                        );
                        
                        localSamplePoints[i] = transform.InverseTransformPoint(bounds.center) + pointLocal;
                        i++;
                    }
                }
            }
            
            worldSamplePoints = new Vector3[totalPoints];
            samplePointDensities = new float[totalPoints];
        }
        
        private void InitializeBuffers()
        {
            int count = localSamplePoints.Length;
            if (count == 0) return;

            worldSamplePointsBuffer = new ComputeBuffer(count, Marshal.SizeOf<Vector3>());
            densitiesBuffer = new ComputeBuffer(count, Marshal.SizeOf<float>());
        }

        private void OnDestroy()
        {
            if (SimulationGlobalStatus.Instance && SimulationGlobalStatus.Instance.ParticleEffectors.Contains(this))
            {
                SimulationGlobalStatus.Instance.ParticleEffectors.Remove(this);
            }
            worldSamplePointsBuffer?.Release();
            densitiesBuffer?.Release();
        }
        
        private void OnDrawGizmos()
        {
            if (localSamplePoints == null || !Application.isPlaying)
                return;
            foreach (Vector3 t in localSamplePoints)
            {
                Vector3 worldPos = transform.TransformPoint(t);
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(worldPos, debugGizmoSize);
            }
        }
    }
}