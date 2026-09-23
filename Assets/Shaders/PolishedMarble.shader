Shader "Fantasy Kingdom/Polished Marble"
{
    Properties
    {
        _Color ("Stone color", Color) = (0.92,0.90,0.85,1)
        _VeinColor ("Vein color", Color) = (0.16,0.19,0.22,1)
        _MainTex ("Original stone detail", 2D) = "white" {}
        _DetailStrength ("Original detail strength", Range(0,1)) = 0.18
        _BumpMap ("Stone normal", 2D) = "bump" {}
        _BumpScale ("Fine relief", Range(0,1)) = 0.12
        _Glossiness ("Polish", Range(0,1)) = 0.78
        _VeinFrequency ("Vein frequency", Range(1,16)) = 5
        _VeinStrength ("Vein contrast", Range(0,1)) = 0.8
        _ModelMin ("Local bounds minimum", Vector) = (0,0,0,0)
        _ModelSize ("Local bounds size", Vector) = (1,1,1,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0
        #include "UnityCG.cginc"
        sampler2D _MainTex, _BumpMap;
        fixed4 _Color, _VeinColor;
        half _DetailStrength, _BumpScale, _Glossiness, _VeinFrequency, _VeinStrength;
        float4 _ModelMin, _ModelSize;
        struct Input { float2 uv_MainTex; float2 uv_BumpMap; float3 stonePos; };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            // Object coordinates keep veins continuous across UV seams and fixed as the unit moves.
            o.stonePos = (v.vertex.xyz - _ModelMin.xyz) / max(_ModelSize.xyz, 0.0001);
        }
        float hash(float3 p) { return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453); }
        float noise(float3 p)
        {
            float3 i=floor(p), f=frac(p); f=f*f*(3-2*f);
            return lerp(lerp(lerp(hash(i),hash(i+float3(1,0,0)),f.x),
                             lerp(hash(i+float3(0,1,0)),hash(i+float3(1,1,0)),f.x),f.y),
                        lerp(lerp(hash(i+float3(0,0,1)),hash(i+float3(1,0,1)),f.x),
                             lerp(hash(i+float3(0,1,1)),hash(i+float3(1,1,1)),f.x),f.y),f.z);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 p=IN.stonePos;
            float warp = noise(p*3.1)*1.4 + noise(p*8.3)*0.32 + noise(p*21.0)*0.08;
            float phase=(dot(p,float3(0.8,1.0,0.45))+warp)*_VeinFrequency;
            float wave=abs(sin(phase*3.14159265));
            float aa=max(fwidth(wave),0.005);
            float vein=1-smoothstep(0.02-aa,0.12+aa,wave);
            float cloud=(1-smoothstep(0.08,0.65,wave))*0.20;
            float strength=saturate((vein*0.78+cloud)*_VeinStrength);
            fixed3 detail=tex2D(_MainTex,IN.uv_MainTex).rgb;
            o.Albedo=lerp(_Color.rgb,_VeinColor.rgb,strength)*lerp(fixed3(1,1,1),detail,_DetailStrength);
            o.Normal=UnpackScaleNormal(tex2D(_BumpMap,IN.uv_BumpMap),_BumpScale);
            o.Metallic=0;
            o.Smoothness=_Glossiness-vein*0.08;
            o.Occlusion=1;
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Standard"
}
