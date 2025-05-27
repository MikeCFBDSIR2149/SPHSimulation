Shader "Custom/SSF_WaterShading" // 名称不变
{
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
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"


            // --- 顶点着色器 (Blitter 标准) ---
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
                output.texcoord = output.texcoord * _BlitScaleBias.xy + _BlitScaleBias.zw;
                return output;
            }

            // --- 片元着色器 ---
            half4 frag (Varyings i) : SV_Target
            {
                // 1. 采样平滑后的深度 (NDC) - 现在从 _BlitTexture 读取
                float depthNDC = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, i.texcoord).r;

                // 2. 检查是否是背景
                if (depthNDC > 0.999)
                {
                    discard;
                }

                // 3. 重建世界坐标
                float3 worldPos = ComputeWorldSpacePosition(i.texcoord, depthNDC, UNITY_MATRIX_I_VP);

                // 4. 计算世界空间法线
                float3 ddx_worldPos = ddx(worldPos);
                float3 ddy_worldPos = ddy(worldPos);
                float3 worldNormal = normalize(cross(ddy_worldPos, ddx_worldPos));

                // --- 添加开始: 健壮性检查 ---
                // 计算叉乘结果的长度平方 (避免开方)
                float lenSq = dot(worldNormal, worldNormal);

                // 定义一个非常小的阈值
                const float Epsilon = 1e-6; // 可以根据需要调整

                // 如果长度非常小，或者结果不是有限数值 (检查 NaN/INF)
                if (lenSq < Epsilon || !isfinite(worldNormal.x) || !isfinite(worldNormal.y) || !isfinite(worldNormal.z) )
                {
                    // 使用一个默认法线，比如朝上
                    worldNormal = float3(0.0, 1.0, 0.0);
                }
                else
                {
                    // 只有在长度有效时才进行归一化
                    worldNormal = normalize(worldNormal);
                }
                // --- 添加结束 ---

                // 5. 可视化法线
                return half4(worldNormal * 0.5 + 0.5, 1.0);
            }
            ENDHLSL
        }
    }
}