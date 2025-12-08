Shader "Custom/BlurShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlurSize ("Blur Size", Range(0, 20)) = 10
        _Intensity ("Intensity", Range(0, 1)) = 1
        _DistortionStrength ("Distortion Strength", Range(0, 0.1)) = 0.02
        _DistortionSpeed ("Distortion Speed", Range(0, 10)) = 3.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _BlurSize;
            float _Intensity;
            float _DistortionStrength;
            float _DistortionSpeed;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                
                // Add distortion
                float time = _Time.y * _DistortionSpeed;
                float2 distortion = float2(
                    sin(uv.y * 10.0 + time) * _DistortionStrength,
                    cos(uv.x * 10.0 + time) * _DistortionStrength
                );
                uv += distortion;
                
                float4 color = float4(0, 0, 0, 0);
                float totalWeight = 0;
                
                float offset = _BlurSize * _MainTex_TexelSize.xy;
                int radius = 3;
                
                for (int x = -radius; x <= radius; x++)
                {
                    for (int y = -radius; y <= radius; y++)
                    {
                        float2 sampleUV = uv + float2(x, y) * offset;
                        float dist = sqrt(x * x + y * y);
                        float weight = exp(-dist * dist / (2.0 * (radius * 0.5) * (radius * 0.5)));
                        color += tex2D(_MainTex, sampleUV) * weight;
                        totalWeight += weight;
                    }
                }
                
                color /= totalWeight;
                color.a *= _Intensity;
                
                return color;
            }
            ENDCG
        }
    }
}

