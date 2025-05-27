Shader "Custom/SSF_Thickness"
{
    Properties
    {
        _ParticleSizeMultiplier ("Particle Size Multiplier", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off // 关闭深度写入
            ZTest Always // 总是通过深度测试 (或者 LEqual 如果你想让被遮挡的粒子不贡献厚度)
            Blend One One // *** 关键：使用加法混合 ***

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

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

            StructuredBuffer<ParticleGPU> _Particles;
            float _ParticleSizeMultiplier;
            float _SmoothingRadiusH;

            struct appdata
            {
                float4 positionOS   : POSITION;
                uint instanceID     : SV_InstanceID;
            };

            struct v2f
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float  radius       : TEXCOORD1;
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
                float3 offsetOS = input.positionOS.xyz * 2.0;
                float3 posWS = GetParticleBillboardPosition(particlePosWS, offsetOS, radius);
                output.positionCS = TransformWorldToHClip(posWS);
                output.uv = input.positionOS.xy + 0.5;
                return output;
            }

            // 一个简单的厚度计算函数 (基于圆心距离)
            float GetThickness(float distSq, float radius)
            {
                // 可以使用更复杂的曲线，比如高斯或平滑核
                // 这里用一个简单的抛物线，中心厚度为 1 * radius * 2，边缘为 0
                return (1.0 - distSq) * radius * 2.0;
            }

            half4 frag (v2f input) : SV_Target
            {
                if (input.type == 1)
                {
                    discard; // 或者 clip(-1.0);
                }
                float2 centeredUV = input.uv * 2.0 - 1.0;
                float distSq = dot(centeredUV, centeredUV);
                clip(1.0 - distSq); // 裁剪到圆内

                // 计算厚度值
                float thickness = GetThickness(distSq, input.radius);

                // 输出厚度值 (R 通道)，由于是加法混合，它会被累加
                return half4(thickness, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}