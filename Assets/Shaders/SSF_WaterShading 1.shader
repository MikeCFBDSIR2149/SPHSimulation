Shader "Custom/SSF_WaterShading1" // 名称不变
{
    Properties
    {
        _FluidSmoothedDepthTexture ("Fluid Smoothed Depth Texture", 2D) = "white" {} // 流体平滑深度贴图
        _FluidThicknessTexture ("Fluid Thickness Texture", 2D) = "black" {} // 流体厚度贴图
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

            sampler2D _FluidSmoothedDepthTexture;
            sampler2D _FluidThicknessTexture;

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
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float depth = tex2D(_FluidSmoothedDepthTexture, i.texcoord).r;
                float thickness = tex2D(_FluidThicknessTexture, i.texcoord).r;
                return half4(depth, thickness, 0, 1); // R=深度，G=厚度
            }

            ENDHLSL
        }
    }
}