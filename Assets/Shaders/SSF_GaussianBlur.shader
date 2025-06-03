Shader "Hidden/SSF_GaussianBlur"
{
    Properties
    {
        _BlurRadius ("Blur Radius", Int) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "GAUSSIAN_BLUR"
            ZTest Always
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            
            int _BlurRadius;
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
            
            float CalculateGaussianWeight(float offset, float sigma)
            {
                sigma = max(abs(sigma), 0.001); // 钳制
                return exp(-(offset * offset) / (2.0 * sigma * sigma));
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 accumulatedColor = half4(0.0, 0.0, 0.0, 0.0);
                float totalWeight = 0.0;
                
                float2 texelStep = _BlurDirection.xy * _BlitTexture_TexelSize.xy;
                
                float sigma = max(1.0f, (float)_BlurRadius / 2.0f); 

                [loop]
                for (int k = -_BlurRadius; k <= _BlurRadius; ++k)
                {
                    float currentPixelOffset = float(k);
                    
                    float weight = CalculateGaussianWeight(currentPixelOffset, sigma);
                    
                    float2 sampleUV = i.uv + texelStep * currentPixelOffset;
                    
                    accumulatedColor += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, sampleUV) * weight;
                    totalWeight += weight;
                }
                
                if (totalWeight > 0.0001)
                {
                    return accumulatedColor / totalWeight;
                }
                return SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, i.uv);
            }
            ENDHLSL
        }
    }
}