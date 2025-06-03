Shader "Custom/ParticleIndirect"
{
    Properties { }
    SubShader {
        Tags { "RenderType"="Opaque" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
            };
            struct v2f {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            struct MeshProperties {
                float4x4 mat;
            };

            StructuredBuffer<MeshProperties> _Properties;

            v2f vert(appdata i, uint instanceID: SV_InstanceID)
            {
                v2f o;
                float4 pos = mul(_Properties[instanceID].mat, i.vertex);
                o.vertex = UnityObjectToClipPos(pos);
                o.color = fixed4(1,1,1,1);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
