Shader "Custom/SSF_WaterShading" // 名称不变
{
    Properties
    {
        _WaterBaseColor ("Water Base Color", Color) = (0.1, 0.3, 0.4, 1.0) // 水体基色 (浅色)
        _WaterDeepColor ("Water Deep Color", Color) = (0.0, 0.1, 0.2, 1.0) // 水体深色 (吸收色)
        _RefractionStrength ("Refraction Strength", Range(0, 0.1)) = 0.02 // 折射强度
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.5   // 反射强度
        _ThicknessMultiplier ("Thickness Multiplier", Range(0, 10)) = 1.0 // 厚度影响因子
        _FresnelPower ("Fresnel Power", Range(0, 10)) = 5.0             // 菲涅尔指数
        _EnvironmentCubemap ("Environment Cubemap", Cube) = "_Skybox" {} // 环境反射贴图
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            ZTest Always Cull Off ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl" // For light info
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl" // For _CameraDepthTexture
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl" // For _CameraOpaqueTexture

            sampler2D _FluidSmoothedDepthTexture;
            sampler2D _FluidThicknessTexture;

            TEXTURECUBE(_EnvironmentCubemap); // <--- 新增: 环境反射贴图
            SAMPLER(sampler_EnvironmentCubemap);

            float4 _WaterBaseColor;    // <--- 新增: 水体基色 (浅色)
            float4 _WaterDeepColor;    // <--- 新增: 水体深色 (吸收色)
            float _RefractionStrength; // <--- 新增: 折射强度
            float _ReflectionStrength; // <--- 新增: 反射强度 (Cubemap)
            float _ThicknessMultiplier; // <--- 新增: 厚度影响因子
            float _FresnelPower;      // <--- 新增: 菲涅尔指数

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
            };

            // --- 顶点着色器 (Blitter 标准) ---
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            // --- 片元着色器 ---
            half4 frag (Varyings i) : SV_Target
            {
                // 1. 采样深度 & 剔除背景
                float depthNDC = tex2D(_FluidSmoothedDepthTexture, i.texcoord).r;
                
                // 采样场景深度 (已经是 Linear 0-1)
                float sceneLinear01Depth = SampleSceneDepth(i.texcoord);
                // 将流体 NDC 深度转换为 Linear 0-1 深度
                float fluidLinear01Depth = Linear01Depth(depthNDC, _ZBufferParams);

                // 比较：如果流体深度 >= 场景深度 (意味着流体在场景后面或完全相同)，则丢弃
                // 减去一个很小的值 (Epsilon) 是为了处理精度问题，防止 Z-fighting
                // if (fluidLinear01Depth >= sceneLinear01Depth - 0.0001)
                // {
                //    discard;
                // }
                if (depthNDC > 0.999) { discard; }

                // 2. 重建世界坐标 & 计算法线 (与之前相同)
                float3 worldPos = ComputeWorldSpacePosition(i.texcoord, depthNDC, UNITY_MATRIX_I_VP);
                float3 ddx_worldPos = ddx(worldPos);
                float3 ddy_worldPos = ddy(worldPos);
                float3 worldNormal = normalize(cross(ddy_worldPos, ddx_worldPos));

                // 健壮性检查
                // 计算叉乘结果的长度平方 (避免开方)
                float lenSq = dot(worldNormal, worldNormal);

                // 阈值
                const float Epsilon = 1e-6;
                if (lenSq < Epsilon || !isfinite(worldNormal.x) || !isfinite(worldNormal.y) || !isfinite(worldNormal.z) )
                {
                    worldNormal = float3(0.0, 1.0, 0.0);
                }
                else
                {
                    worldNormal = normalize(worldNormal);
                }
                
                // 3. 获取视角方向 (世界空间)
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);

                // 4. 计算菲涅尔项
                float fresnelTerm = pow(1.0 - saturate(dot(viewDir, worldNormal)), _FresnelPower);

                // 5. 计算折射
                float4 clipPos = TransformWorldToHClip(worldPos);
                float2 screenUV = clipPos.xy / clipPos.w;
                float2 sceneUV = screenUV * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    sceneUV.y = 1.0 - sceneUV.y;
                #endif
                float2 refractOffset = worldNormal.xy * _RefractionStrength;
                half4 refractionColor = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_LinearClamp, sceneUV + refractOffset);

                // 6. 计算反射
                float3 reflectDir = reflect(-viewDir, worldNormal);
                half4 reflectionColor = SAMPLE_TEXTURECUBE(_EnvironmentCubemap, sampler_EnvironmentCubemap, reflectDir) * _ReflectionStrength;

                // 7. 计算水体颜色 (基于厚度)
                float thickness = tex2D(_FluidThicknessTexture, i.texcoord).r;
                half4 waterColor = lerp(_WaterBaseColor, _WaterDeepColor, saturate(thickness * _ThicknessMultiplier));

                // 8. 混合最终颜色
                half4 underwaterColor = refractionColor * waterColor;
                half4 finalColor = lerp(underwaterColor, reflectionColor, fresnelTerm);

                return half4(finalColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}