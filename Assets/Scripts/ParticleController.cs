using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class ParticleController : MonoBehaviour
{
    private struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 acceleration;
        public float density;
        public float pressure;
        // public float mass;
        // public float volume;
    }
    
    // Parameters
    [Header("Physical Parameters")]
    public float particleMass = 0.02f;          // 粒子模拟质量
    public float restDensityRho = 1000f;        // 水的静止密度
    public float gasConstantB = 2000f;          // Tait 状态方程中的流体刚度系数
    public float pressureExponentGamma = 7f;    // Tait 状态方程中的指数
    public float viscosityMu = 0.05f;           // 动力粘滞系数
    public Vector3 gravity = new Vector3(0.0f, -9.81f, 0.0f); // 重力加速度
    
    [Header("Simulation Parameters")]
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
    
    private List<Particle> particles;
    private Matrix4x4[] particleMatrices;
    
    // Kernel Constant (核函数常数)
    private float poly6Constant;
    private float spikyGradientConstant;
    private float viscosityLaplacianConstant;

    private void Start()
    {
        // Time.timeScale = 10;
        PreCalculateConstants();
        InitializeParticles();
    }

    // 对常用常数预计算缓存以提高性能
    private void PreCalculateConstants()
    {
        hSquared = smoothingRadiusH * smoothingRadiusH;
        poly6Constant = 315f / (64f * Mathf.PI * Mathf.Pow(smoothingRadiusH, 9));
        spikyGradientConstant = -45f / (Mathf.PI * Mathf.Pow(smoothingRadiusH, 6));
        viscosityLaplacianConstant = 45f / (Mathf.PI * Mathf.Pow(smoothingRadiusH, 6));
    }

    // 初始化粒子
    private void InitializeParticles()
    {
        particles = new List<Particle>();
        particleMatrices = new Matrix4x4[maxParticles];
        
        for (int i = 0; i < maxParticles; i++)
        {
            float x = Random.Range(-spawnVolumeSize.x / 2f, spawnVolumeSize.x / 2f);
            float y = Random.Range(-spawnVolumeSize.y / 2f, spawnVolumeSize.y / 2f);
            float z = Random.Range(-spawnVolumeSize.z / 2f, spawnVolumeSize.z / 2f);

            Vector3 initialPosition = spawnVolumeCenter + new Vector3(x, y, z);

            Particle newParticle = new Particle
            {
                position = initialPosition,
                velocity = Vector3.zero,
                acceleration = Vector3.zero,
                density = 0f,
                pressure = 0f, 
                // mass = particleMass
            };
            particles.Add(newParticle);
            
            particleMatrices[i] = Matrix4x4.TRS(
                initialPosition,
                Quaternion.identity,
                Vector3.one * particleRenderScale
            );
        }
    }
    
    // Poly6 核函数计算
    private float Poly6Kernel(float radiusSquared)
    {
        if (radiusSquared > hSquared) return 0f;
        return poly6Constant * Mathf.Pow(hSquared - radiusSquared, 3);
    }
    
    // Spiky 核函数计算
    private Vector3 SpikyKernelGradient(Vector3 radiusVector)
    {
        float radius = radiusVector.magnitude;
        if (radius > smoothingRadiusH) return Vector3.zero;

        float factor = spikyGradientConstant * Mathf.Pow(smoothingRadiusH - radius, 2);
        return factor * radiusVector.normalized;
    }
    
    // Viscosity 核函数计算
    private float ViscosityKernelLaplacian(float radius)
    {
        if (radius > smoothingRadiusH) return 0;
        return viscosityLaplacianConstant * (smoothingRadiusH - radius);
    }

    // Update 计算
    private void Update()
    {
        ComputeParticleDensityAndPressure();
        ComputeForceAcceleration();
        UpdateParticlesData();
    }
    
    // 计算粒子密度和压力
    private void ComputeParticleDensityAndPressure()
    {
        // 计算密度
        for (int i = 0; i < particles.Count; i++)
        {
            Particle pi = particles[i];
            pi.density = 0f;
            
            // 遍历所有粒子找到邻居粒子并计算密度
            foreach (Particle pj in particles)
            {
                Vector3 particleVector_ij = pj.position - pi.position;
                float radiusSquared = particleVector_ij.sqrMagnitude;
                
                if (radiusSquared < hSquared)
                {
                    pi.density += particleMass * Poly6Kernel(radiusSquared);
                }
            }
            
            if (pi.density < 0.0001f) pi.density = restDensityRho;
            
            // 计算压力
            pi.pressure = gasConstantB * (Mathf.Pow(pi.density / restDensityRho, pressureExponentGamma) - 1);
            
            if (pi.pressure < 0) pi.pressure = 0f;
            
            particles[i] = pi;
        }
    }
    
    // 计算所有外部加速度
    private void ComputeForceAcceleration()
    {
        // 计算粒子间的相互作用力
        for (int i = 0; i < particles.Count; i++)
        {
            Particle pi = particles[i];
            pi.acceleration = Vector3.zero;
            
            // 计算压力和粘滞力加速度
            for (int j = 0; j < particles.Count; j++)
            {
                if (i == j) continue;

                Particle pj = particles[j];
                Vector3 particleVector_ij = pj.position - pi.position;
                float radiusSquared = particleVector_ij.sqrMagnitude;

                if (!(radiusSquared < hSquared)) continue;

                // 计算压力梯度
                Vector3 pressureAcceleration = particleMass * 
                                             (pi.pressure / (pi.density * pi.density) + pj.pressure / (pj.density * pj.density))
                                             * SpikyKernelGradient(particleVector_ij);
                pi.acceleration += pressureAcceleration;

                // 计算粘滞力
                Vector3 viscosityAcceleration = (pj.velocity - pi.velocity) * 
                                         (viscosityMu * particleMass / (pi.density * pj.density) * 
                                          ViscosityKernelLaplacian(particleVector_ij.magnitude));
                
                pi.acceleration += viscosityAcceleration;
            }
            
            // 其他加速度
            // 重力
            pi.acceleration += gravity;

            particles[i] = pi;
        }
    }

    // 更新粒子数据
    private void UpdateParticlesData()
    {
        float dt = Time.deltaTime;

        Vector3 minBounds = spawnVolumeCenter - spawnVolumeSize / 2f;
        Vector3 maxBounds = spawnVolumeCenter + spawnVolumeSize / 2f;

        for (int i = 0; i < particles.Count; i++)
        {
            Particle pi = particles[i];

            // 更新速度
            pi.velocity += pi.acceleration * dt;

            // 更新位置
            pi.position += pi.velocity * dt;

            // 边界条件控制
            // X 轴
            if (pi.position.x < minBounds.x)
            {
                pi.position.x = minBounds.x;
                pi.velocity.x *= -boundaryDamping;
            }
            else if (pi.position.x > maxBounds.x)
            {
                pi.position.x = maxBounds.x;
                pi.velocity.x *= -boundaryDamping;
            }

            // Y 轴 (只检查下方边界)
            if (pi.position.y < minBounds.y)
            {
                pi.position.y = minBounds.y;
                pi.velocity.y *= -boundaryDamping;
            }
            // else if (pi.position.y > maxBounds.y)
            // {
            //     pi.position.y = maxBounds.y;
            //     pi.velocity.y *= -boundaryDamping;
            // }

            // Z 轴
            if (pi.position.z < minBounds.z)
            {
                pi.position.z = minBounds.z;
                pi.velocity.z *= -boundaryDamping;
            }
            else if (pi.position.z > maxBounds.z)
            {
                pi.position.z = maxBounds.z;
                pi.velocity.z *= -boundaryDamping;
            }

            particles[i] = pi;
        }
    }

    // LateUpdate 渲染
    private void LateUpdate()
    {
        if (particles == null || !particleMesh || !particleMaterial) return;

        // 更新每个粒子的变换矩阵
        for (int i = 0; i < particles.Count; i++)
        {
            particleMatrices[i] = Matrix4x4.TRS(
                particles[i].position,
                Quaternion.identity,
                Vector3.one * particleRenderScale
            );
        }
        
        // Unity Documentation: DrawMeshInstanced 一次最多绘制 1023 个实例
        const int maxInstancesPerCall = 1023;
        
        if (maxParticles <= maxInstancesPerCall)
        {
            Graphics.DrawMeshInstanced(
                particleMesh,
                0,
                particleMaterial,
                particleMatrices,
                particles.Count // 要绘制的实例数量
            );
            return;
        }

        // 数量超出 1023 限制，批处理
        int numBatches = (particles.Count + maxInstancesPerCall - 1) / maxInstancesPerCall;
        for (int batchIndex = 0; batchIndex < numBatches; batchIndex++)
        {
            int startIndex = batchIndex * maxInstancesPerCall;
            int countInBatch = Mathf.Min(maxInstancesPerCall, particles.Count - startIndex);

            Matrix4x4[] batchMatrices = new Matrix4x4[countInBatch];
            Array.Copy(particleMatrices, startIndex, batchMatrices, 0, countInBatch);

            Graphics.DrawMeshInstanced(
                particleMesh,
                0,
                particleMaterial,
                batchMatrices,
                countInBatch
            );
            
        }
    }
}
