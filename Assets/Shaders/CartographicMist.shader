Shader "Fantasy Kingdom/Cartographic Mist"
{
    Properties
    {
        _Color ("Deep slate", Color) = (0.055,0.075,0.105,1)
        _MistColor ("Silver mist", Color) = (0.16,0.21,0.26,1)
        _MainTex ("Mist texture A", 2D) = "white" {}
        _DetailTex ("Mist texture B", 2D) = "white" {}
        _Scale ("Cloud scale", Range(0.01,1)) = 0.09
        _Speed ("Drift speed", Range(0,0.1)) = 0.018
        [HideInInspector] _SrcBlend ("Source blend", Float) = 5
        [HideInInspector] _DstBlend ("Destination blend", Float) = 10
        [Toggle] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color, _MistColor;
            float _Scale, _Speed;
            sampler2D _MainTex, _DetailTex;
            struct v2f { float4 pos : SV_POSITION; float2 plane : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;
                float3 n=abs(UnityObjectToWorldNormal(v.normal));
                // Axis-aligned fog faces need only one projection, not six triplanar texture reads.
                o.plane=n.y > 0.5 ? world.xz : (n.x > 0.5 ? world.zy : world.xy); return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 p=i.plane*_Scale + _Time.y*_Speed*float2(1,0.37);
                float n=tex2D(_MainTex,p).r*0.65 + tex2D(_DetailTex,i.plane*_Scale*1.37-_Time.y*_Speed*float2(.31,.6)).r*0.35;
                fixed3 color=lerp(_Color.rgb,_MistColor.rgb,smoothstep(0.15,0.9,n));
                return fixed4(color,_Color.a);
            }
            ENDCG
        }
    }
}
