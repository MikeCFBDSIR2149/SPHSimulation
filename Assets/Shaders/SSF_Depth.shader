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
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct ParticleGPU
            {
                float3 position;
                float3 velocity;
                float3 acceleration;
                float density;
                float pressure;
                int type;
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

            half4 frag (v2f input) : SV_Target
            {
                if (input.type == 1)
                {
                    discard;
                }
                float2 centeredUV = input.uv * 2.0 - 1.0;
                float distSq = dot(centeredUV, centeredUV);

                clip(1 - distSq); // 裁剪到圆
                
                float viewDepth_billboard_plane = LinearEyeDepth(input.positionCS.z / input.positionCS.w, _ZBufferParams);
                float zOffset = sqrt(max(0.0, 1.0 - distSq)) * input.radius;
                float newViewDepth_sphere = viewDepth_billboard_plane - zOffset;
                
                float4 clipPos_sphere = mul(UNITY_MATRIX_P, float4(0,0, newViewDepth_sphere, 1.0));
                float ndcDepth_sphere = clipPos_sphere.z / clipPos_sphere.w;
                
                if (newViewDepth_sphere < _ProjectionParams.y)
                {
                    ndcDepth_sphere = 0.0;
                }

                return half4(ndcDepth_sphere, ndcDepth_sphere, ndcDepth_sphere, 1.0);
            }
            ENDHLSL
        }
    }
}