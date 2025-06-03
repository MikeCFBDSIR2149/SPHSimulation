Shader "Custom/SSF_WaterShading"
{
    Properties
    {
        _WaterBaseColor ("Water Base Color", Color) = (0.1, 0.3, 0.4, 1.0)
        _WaterDeepColor ("Water Deep Color", Color) = (0.0, 0.1, 0.2, 1.0)
        _RefractionStrength ("Refraction Strength", Range(0, 0.1)) = 0.02
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.5
        _ThicknessMultiplier ("Thickness Multiplier", Range(0, 10)) = 1.0
        _FresnelPower ("Fresnel Power", Range(0, 10)) = 5.0
        _EnvironmentCubemap ("Environment Cubemap", Cube) = "_Skybox" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" } // 改为 Transparent

        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl" // For light info
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl" // For _CameraDepthTexture
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl" // For _CameraOpaqueTexture

            sampler2D _FluidSmoothedDepthTexture;
            sampler2D _FluidThicknessTexture;

            TEXTURECUBE(_EnvironmentCubemap);
            SAMPLER(sampler_EnvironmentCubemap);

            float4 _WaterBaseColor;     // 水体基色
            float4 _WaterDeepColor;     // 水体深色
            float _RefractionStrength;  // 折射强度
            float _ReflectionStrength;  // 反射强度
            float _ThicknessMultiplier; // 厚度影响因子
            float _FresnelPower;        // 菲涅尔指数

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
            };
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float3 ReconstructViewPos(float2 screenUV, float ndcDepth, float4x4 invProjMatrix)
            {
                float2 ndcXY = screenUV * 2.0 - 1.0;
                
                float4 clipPos = float4(ndcXY, ndcDepth, 1.0);
                
                float4 viewPosH = mul(invProjMatrix, clipPos);

                return viewPosH.xyz / viewPosH.w;
            }
            
            half4 frag (Varyings i) : SV_Target
            {
                float depthNDC = tex2D(_FluidSmoothedDepthTexture, i.texcoord).r;

                if (depthNDC > 1) { discard; }

                // 重建世界坐标+计算法线
                float3 viewPos = ReconstructViewPos(i.texcoord, depthNDC, UNITY_MATRIX_I_P);

                // return half4(viewPos, 1.0);
                
                float2 texelSize = _CameraDepthTexture_TexelSize.xy;
                
                float ndcDepth_xp = tex2D(_FluidSmoothedDepthTexture, i.texcoord + float2(texelSize.x, 0)).r;
                float ndcDepth_xn = tex2D(_FluidSmoothedDepthTexture, i.texcoord - float2(texelSize.x, 0)).r;
                float ndcDepth_yp = tex2D(_FluidSmoothedDepthTexture, i.texcoord + float2(0, texelSize.y)).r;
                float ndcDepth_yn = tex2D(_FluidSmoothedDepthTexture, i.texcoord - float2(0, texelSize.y)).r;
                
                float3 pos_xp_View = ReconstructViewPos(i.texcoord + float2(texelSize.x, 0), ndcDepth_xp, UNITY_MATRIX_I_P);
                float3 pos_xn_View = ReconstructViewPos(i.texcoord - float2(texelSize.x, 0), ndcDepth_xn, UNITY_MATRIX_I_P);
                float3 pos_yp_View = ReconstructViewPos(i.texcoord + float2(0, texelSize.y), ndcDepth_yp, UNITY_MATRIX_I_P);
                float3 pos_yn_View = ReconstructViewPos(i.texcoord - float2(0, texelSize.y), ndcDepth_yn, UNITY_MATRIX_I_P);
                
                float3 ddx_fwd_View = pos_xp_View - viewPos;
                float3 ddx_bwd_View = viewPos - pos_xn_View;
                float3 ddy_fwd_View = pos_yp_View - viewPos;
                float3 ddy_bwd_View = viewPos - pos_yn_View;
                
                float3 final_ddx_View = (dot(ddx_fwd_View, ddx_fwd_View) < dot(ddx_bwd_View, ddx_bwd_View)) ? ddx_fwd_View : ddx_bwd_View;
                float3 final_ddy_View = (dot(ddy_fwd_View, ddy_fwd_View) < dot(ddy_bwd_View, ddy_bwd_View)) ? ddy_fwd_View : ddy_bwd_View;

                float view_depth_thresh = 0.1;
                if (abs(pos_xp_View.z - viewPos.z) > view_depth_thresh) final_ddx_View = ddx_bwd_View;
                if (abs(pos_xn_View.z - viewPos.z) > view_depth_thresh) final_ddx_View = ddx_fwd_View;
                if (abs(pos_yp_View.z - viewPos.z) > view_depth_thresh) final_ddy_View = ddy_bwd_View;
                if (abs(pos_yn_View.z - viewPos.z) > view_depth_thresh) final_ddy_View = ddy_fwd_View;
                
                float3 viewNormal = normalize(cross(final_ddx_View, final_ddy_View)); 

                // return half4(viewNormal, 1.0);

                float3 worldNormal = mul((float3x3)UNITY_MATRIX_I_V, viewNormal);
                worldNormal = -normalize(worldNormal);

                // return half4(worldNormal, 1.0);
                
                float3 worldPos = ComputeWorldSpacePosition(i.texcoord, depthNDC, UNITY_MATRIX_I_VP);

                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos); // 世界空间观察方向

                // 菲涅尔项
                float fresnelTerm = pow(1.0 - saturate(dot(viewDir, worldNormal)), _FresnelPower);
                fresnelTerm = saturate(fresnelTerm);

                // 环境反射
                float3 reflectDir = reflect(-viewDir, worldNormal);
                half4 reflectionColor = SAMPLE_TEXTURECUBE_LOD(_EnvironmentCubemap, sampler_EnvironmentCubemap, reflectDir, 0) * _ReflectionStrength;

                // 折射
                float4 screenPos = TransformWorldToHClip(worldPos);
                float2 sceneUV = (screenPos.xy / screenPos.w) * 0.5 + 0.5;
                
                // 基于法线和深度进行扭曲
                float2 refractOffset = worldNormal.xy * _RefractionStrength * (1.0 - depthNDC); // 深度越大，折射越小
                half4 refractionColor = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_LinearClamp, sceneUV + refractOffset);

                // 水体自身颜色 (基于厚度)
                float thickness = tex2D(_FluidThicknessTexture, i.texcoord).r;
                half4 waterAbsorptionColor = lerp(_WaterBaseColor, _WaterDeepColor, saturate(thickness * _ThicknessMultiplier));
                half4 underwaterColor = refractionColor * waterAbsorptionColor;

                // 最终混合
                half4 finalColor = lerp(underwaterColor, reflectionColor, fresnelTerm);
                
                float alpha = saturate(lerp(0.2, 0.95, saturate(thickness * _ThicknessMultiplier * 0.1)) + fresnelTerm);
                alpha = saturate(alpha);

                return half4(finalColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}