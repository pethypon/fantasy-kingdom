Shader "Fantasy Kingdom/Map Grid"
{
    Properties
    {
        _Color ("Line color", Color) = (0.07,0.12,0.16,0.48)
        _Width ("Line width", Range(0.002,0.05)) = 0.012
    }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            fixed4 _Color;
            float _Width;
            v2f vert(appdata v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 edge = min(i.uv, 1 - i.uv);
                float2 aa = max(fwidth(i.uv), 0.0001);
                float2 coverage = saturate((_Width - edge) / aa + 0.5);
                // Pale inset against dark terrain; dark core stays legible on bright terrain.
                float2 inset = saturate((_Width + 0.009 - edge) / aa + 0.5);
                float core = max(coverage.x, coverage.y);
                float light = max(inset.x, inset.y) - core;
                float alpha = core * _Color.a + light * 0.16;
                float3 color = (_Color.rgb * core * _Color.a + float3(.75,.82,.84) * light * .16) / max(alpha, .0001);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}
