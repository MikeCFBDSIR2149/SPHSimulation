using System;
using UnityEngine;

public class ParticleControllerParams : MonoBehaviour
{
    public static ParticleControllerParams Instance { get; private set; }
    
    [Header("Common Static")]
    public int targetParticleCount = 80000;     // 目标粒子数量
    public float particleMass = 0.02f;          // 粒子模拟质量
    public float pressureExponentGamma = 7f;    // Tait 状态方程中的指数
    public Vector3 gravity = new Vector3(0.0f, -9.81f, 0.0f); // 重力加速度
    public float smoothingRadiusH = 0.1f;       // 平滑核影响半径
    public float boundaryDamping = 0.4f;               // 边界碰撞时的速度衰减系数
    
    [Header("Common Dynamic")]
    public float restDensityRho = 1000f;        // 水的静止密度
    public float gasConstantB = 2000f;          // Tait 状态方程中的流体刚度系数
    public float viscosityMu = 0.05f;           // 动力粘滞系数

    [Header("GPU-Only Static")]
    public float particleRenderScale = 0.015f;

    private void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }
}
