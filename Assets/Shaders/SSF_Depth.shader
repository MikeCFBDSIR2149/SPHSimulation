Shader "Custom/SSF_Depth"
{
    Properties
    {
        _ParticleSizeMultiplier ("Particle Size Multiplier", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Tags { "LightMode"="UniversalForward" } // 需要一个有效的 LightMode
            Cull Off // 关闭背面剔除，因为广告牌没有背面
            ZWrite On // 开启深度写入
            ZTest LEqual // 标准深度测试
            Blend Off // 关闭混合

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing // 启用 GPU Instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct ParticleGPU
            {
                float3 position;
                float3 velocity;     // 即使不用也要占位，保持与 C# 结构对齐
                float3 acceleration; // 保持对齐
                float density;       // 保持对齐
                float pressure;      // 保持对齐
                int type;            // *** 添加或确保存在 ***
            };

            StructuredBuffer<ParticleGPU> _Particles; // 粒子数据
            float _ParticleSizeMultiplier; // 粒子大小乘数
            float _SmoothingRadiusH; // SPH 平滑半径 H (我们需要这个来确定粒子大小)

            struct appdata
            {
                float4 positionOS   : POSITION;
                uint instanceID     : SV_InstanceID; // GPU Instancing ID
            };

            struct v2f
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0; // 用于 billboard 内部坐标
                float  radius       : TEXCOORD1; // 粒子半径
                int    type         : TEXCOORD2;
            };

            float3 GetParticleBillboardPosition(float3 particleCenterWS, float3 vertexOffsetOS, float radius)
            {
                float3 camRightWS = UNITY_MATRIX_V[0].xyz;
                float3 camUpWS = UNITY_MATRIX_V[1].xyz;

                float3 offsetWS = (camRightWS * vertexOffsetOS.x + camUpWS * vertexOffsetOS.y) * radius;

                return particleCenterWS + offsetWS;
            }

            v2f vert (appdata input)
            {
                v2f output;
                
                ParticleGPU particle = _Particles[input.instanceID];
                float3 particlePosWS = particle.position;
                output.type = particle.type;

                float radius = _SmoothingRadiusH * _ParticleSizeMultiplier;
                output.radius = radius;

                float3 offsetOS = input.positionOS.xyz * 2.0; // 放大到 -1 到 1 范围
                
                float3 posWS = GetParticleBillboardPosition(particlePosWS, offsetOS, radius);

                output.positionCS = TransformWorldToHClip(posWS);
                output.uv = input.positionOS.xy + 0.5;

                return output;
            }

            half4 frag (v2f input) : SV_Target
            {
                if (input.type == 1)
                {
                    discard;
                }
                float2 centeredUV = input.uv * 2.0 - 1.0;
                float distSq = dot(centeredUV, centeredUV);

                // 裁剪到圆内
                clip(1.0 - distSq);

                // --- 修改开始 ---
                // 计算 NDC 深度
                float depthNDC = input.positionCS.z / input.positionCS.w;

                // 计算球形偏移 (视图空间)
                float viewDepth = LinearEyeDepth(depthNDC, _ZBufferParams);
                float zOffset = sqrt(max(0.0, 1.0 - distSq)) * input.radius;
                float newViewDepth = viewDepth - zOffset;

                // 将新的视图深度转换回 NDC 深度
                // URP Core.hlsl 中没有直接的 View -> NDC 函数，但我们可以用 1/LinearEyeDepth 的逆过程
                // 或者更简单：我们修改 Clip Space Z，然后重新计算 NDC
                // H = Proj * View; View = InvProj * H
                // 我们需要修改 H.z 使其在 View 空间移动 zOffset
                // 一个更简单的方法是，我们先计算出近似的球形 NDC 深度
                // 我们知道中心点是 depthNDC，边缘点应该更远（NDC 更大）
                // 我们可以近似地认为 Z 偏移量与 NDC 偏移量成正比（这不完全准确，但常被使用）
                // float ndcOffset = zOffset / _ProjectionParams.z; // 用 Far Plane 粗略缩放
                // float sphereNDC = depthNDC + ndcOffset; // 更远 -> 更大
                // 这个方法不精确。
                // *** 让我们暂时简化：先不考虑球形深度，只输出平面 NDC 深度 ***
                // *** 这样可以确保法线计算的基础是正确的。球形深度我们可以在法线之后再优化 ***
                
                return half4(depthNDC, 0, 0, 1); // <--- 改回这一行！
            }
            ENDHLSL
        }
    }
}