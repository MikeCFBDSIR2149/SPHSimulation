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

                clip(1 - distSq); // 裁剪到圆内

                // 1. 计算原始 NDC 深度 (广告牌平面的深度)
                float depthNDC = input.positionCS.z / input.positionCS.w;

                // 2. 将其转换为视图空间深度
                float viewDepth = LinearEyeDepth(depthNDC, _ZBufferParams);

                // 3. 计算球形偏移 (视图空间)
                float zOffset = sqrt(max(0.0, 1.0 - distSq)) * input.radius;

                // 4. 应用偏移得到球体表面的视图空间深度
                //    (球体表面比其中心平面更靠近相机，所以减去 zOffset)
                float newViewDepth_sphere = viewDepth - zOffset;

                // 5. 将球体表面的视图空间深度转换为线性 0-1 深度
                float linearDepth_sphere = (newViewDepth_sphere - _ProjectionParams.y) / (_ProjectionParams.z - _ProjectionParams.y);
                linearDepth_sphere = saturate(linearDepth_sphere); // 确保在 0-1 范围

                linearDepth_sphere = pow(linearDepth_sphere, 0.1); // 应用 Gamma 校正

                return half4(linearDepth_sphere, 0, 0, 1); // 输出包含球形效果的线性深度
            }
            ENDHLSL
        }
    }
}