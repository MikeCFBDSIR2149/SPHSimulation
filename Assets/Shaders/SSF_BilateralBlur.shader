Shader "Custom/SSF_BilateralBlur_Blitter" // 改个名字以示区分
{
    Properties
    {
        _BlurRadius ("Blur Radius", Int) = 5
        _DepthSigma ("Depth Sigma", Float) = 0.05
        _SpatialSigma ("Spatial Sigma", Float) = 2.0
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
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            int    _BlurRadius;
            float  _DepthSigma;
            float  _SpatialSigma;
            float4 _BlurDirection;

            struct appdata
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                output.uv = output.uv * _BlitScaleBias.xy + _BlitScaleBias.zw;

                return output;
            }

            float Gaussian(float x, float sigma)
            {
                sigma = max(abs(sigma), 0.0001);
                return exp(-(x * x) / (2.0 * sigma * sigma));
            }

            half4 frag (v2f i) : SV_Target
            {
                float centerDepth = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, i.uv).r;

                if (centerDepth == 0)
                {
                   return centerDepth;
                }

                float totalWeight = 0.0;
                float totalDepth = 0.0;

                [loop]
                for (int j = -_BlurRadius; j <= _BlurRadius; ++j)
                {
                    float2 offset = _BlurDirection.xy * j * _BlitTexture_TexelSize.xy;
                    float2 sampleUV = i.uv + offset;
                    float sampleDepth = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, sampleUV).r;

                    if (sampleDepth <= 0)
                    {
                        continue;
                    }

                    float spatialWeight = Gaussian(j, _SpatialSigma);
                    float depthDiff = abs(centerDepth - sampleDepth);
                    float depthWeight = Gaussian(depthDiff, _DepthSigma);
                    float weight = spatialWeight * depthWeight;

                    totalDepth += sampleDepth * weight;
                    totalWeight += weight;
                }

                if (totalWeight < 0.0001)
                {
                    return centerDepth;
                }

                return totalDepth / totalWeight;
                // return half4(1, 1, 1, 0.1);
            }
            ENDHLSL
        }
    }
}