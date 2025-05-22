using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class ParticleController : MonoBehaviour
{
    public struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 force;
        public float density;
        public float pressure;
        public float mass;
    }
    
    [Header("SPH Parameters")]
    public float particleMass = 0.02f; // 粒子质量
    
    [Header("Settings")]
    public int maxParticles;
    public Vector3 spawnVolumeCenter = Vector3.zero; // 生成区域的中心
    public Vector3 spawnVolumeSize; // 生成区域的大小
    
    [Header("Gizmos Settings (Deprecated)")]
    public float particleDisplayRadius = 0.1f; // Gizmos 中显示的粒子半径
    public Color particleColor = Color.blue;   // Gizmos 中显示的粒子颜色
    
    [Header("Rendering Settings (Instanced)")]
    public Mesh particleMesh; // 用于渲染每个粒子的网格
    public Material particleMaterial; // 用于渲染每个粒子的材质
    public float particleRenderScale = 0.2f; // 渲染出来的粒子大小 (因为 TRS 的 Scale 是 Vector3)
    
    private List<Particle> particles;
    private Matrix4x4[] particleMatrices;

    private void Start()
    {
        InitializeParticles();
    }

    private void InitializeParticles()
    {
        particles = new List<Particle>();
        particleMatrices = new Matrix4x4[maxParticles];
        
        for (int i = 0; i < maxParticles; i++)
        {
            // 在定义的 spawnVolumeSize 区域内随机生成位置
            float x = Random.Range(-spawnVolumeSize.x / 2f, spawnVolumeSize.x / 2f);
            float y = Random.Range(-spawnVolumeSize.y / 2f, spawnVolumeSize.y / 2f);
            float z = Random.Range(-spawnVolumeSize.z / 2f, spawnVolumeSize.z / 2f);

            Vector3 initialPosition = spawnVolumeCenter + new Vector3(x, y, z);

            Particle newParticle = new Particle
            {
                position = initialPosition
                // velocity = Vector3.zero, // 稍后
            };
            particles.Add(newParticle);
            
            // 初始化变换矩阵 (虽然位置会在Update中更新，但先设置一下)
            particleMatrices[i] = Matrix4x4.TRS(
                initialPosition,
                Quaternion.identity, // 没有旋转
                Vector3.one * particleRenderScale // 初始大小
            );
        }
    }

    /*private void OnDrawGizmosSelected()
    {
        if (particles == null || particles.Count == 0) return;

        Gizmos.color = particleColor;

        foreach (Particle p in particles)
        {
            Gizmos.DrawSphere(p.position, particleDisplayRadius);
        }
        
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(spawnVolumeCenter, spawnVolumeSize);
    }*/

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
