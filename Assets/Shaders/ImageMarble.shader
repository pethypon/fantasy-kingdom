Shader "Fantasy Kingdom/Image Marble"
{
    Properties
    {
        _Color ("Stone color", Color) = (1,1,1,1)
        _PatternColor ("Fine vein color", Color) = (0.45,0.45,0.45,1)
        _DeepPatternColor ("Deep vein color", Color) = (0.2,0.2,0.2,1)
        _FineTint ("Fine vein color strength", Range(0,1)) = 0.65
        _DeepTint ("Deep vein color strength", Range(0,1)) = 0.8
        _VeinThreshold ("Vein darkness threshold", Range(0,1)) = 0.19
        _VeinFeather ("Vein edge softness", Range(0.001,0.3)) = 0.035
        _DeepThreshold ("Deep vein darkness threshold", Range(0,1)) = 0.29
        _DeepFeather ("Deep vein edge softness", Range(0.001,0.3)) = 0.035
        [Toggle] _MaskPreview ("Preview color regions", Float) = 0
        _MainTex ("Marble image", 2D) = "white" {}
        _PatternStrength ("Pattern contrast", Range(0,3)) = 1
        [Toggle] _Triplanar ("Project without UV", Float) = 0
        _ProjectionScale ("Projection tiles per local unit", Float) = 1
        [Normal] _BumpMap ("Stone normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,2)) = 0.12
        _Glossiness ("Polish / Smoothness", Range(0,1)) = 0.85
        _Metallic ("Metallic", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert
        #pragma target 3.0
        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        sampler2D _MainTex, _BumpMap;
        float4 _MainTex_ST, _BumpMap_ST;
        fixed4 _Color, _PatternColor, _DeepPatternColor;
        half _FineTint, _DeepTint, _VeinThreshold, _VeinFeather, _DeepThreshold, _DeepFeather, _MaskPreview;
        half _PatternStrength, _BumpScale, _Glossiness, _Metallic, _Triplanar;
        float _ProjectionScale;
        struct Input
        {
            float2 meshUV;

            float3 localPos;
            float3 localNormal;
            float3 worldNormal;
            INTERNAL_DATA
        };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.meshUV=v.texcoord.xy;
            o.localPos=v.vertex.xyz;
            o.localNormal=v.normal;
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half3 stone;
            if (_Triplanar > 0.5)
            {
                // Stable in object space: no UVs required and no sliding when units move.
                float3 p=IN.localPos*max(abs(_ProjectionScale),0.0001);
                float3 n=normalize(IN.localNormal);
                float3 w=pow(abs(n),4);
                w/=max(w.x+w.y+w.z,0.0001);
                stone=tex2D(_MainTex,p.zy*_MainTex_ST.xy+_MainTex_ST.zw).rgb*w.x
                     +tex2D(_MainTex,p.xz*_MainTex_ST.xy+_MainTex_ST.zw).rgb*w.y
                     +tex2D(_MainTex,p.xy*_MainTex_ST.xy+_MainTex_ST.zw).rgb*w.z;
                float3 nx=UnpackScaleNormal(tex2D(_BumpMap,p.zy*_BumpMap_ST.xy+_BumpMap_ST.zw),_BumpScale);
                float3 ny=UnpackScaleNormal(tex2D(_BumpMap,p.xz*_BumpMap_ST.xy+_BumpMap_ST.zw),_BumpScale);
                float3 nz=UnpackScaleNormal(tex2D(_BumpMap,p.xy*_BumpMap_ST.xy+_BumpMap_ST.zw),_BumpScale);
                // Blend projected slopes on the surface, then transform to the lighting tangent basis.
                float3 g=float3(0,nx.y,nx.x)/max(nx.z,0.1)*w.x
                        +float3(ny.x,0,ny.y)/max(ny.z,0.1)*w.y
                        +float3(nz.x,nz.y,0)/max(nz.z,0.1)*w.z;
                float3 wn=UnityObjectToWorldNormal(normalize(n+g-n*dot(n,g)));
                float3 t=normalize(WorldNormalVector(IN,float3(1,0,0)));
                float3 b=normalize(WorldNormalVector(IN,float3(0,1,0)));
                float3 normal=normalize(WorldNormalVector(IN,float3(0,0,1)));
                o.Normal=normalize(float3(dot(wn,t),dot(wn,b),dot(wn,normal)));
            }
            else
            {
                stone=tex2D(_MainTex,IN.meshUV*_MainTex_ST.xy+_MainTex_ST.zw).rgb;
                o.Normal=UnpackScaleNormal(tex2D(_BumpMap,IN.meshUV*_BumpMap_ST.xy+_BumpMap_ST.zw),_BumpScale);
            }
            // Classify source brightness before tint/contrast so colors cannot change their own masks.
            half3 maskSource=stone;
            #ifndef UNITY_COLORSPACE_GAMMA
            maskSource=LinearToGammaSpace(maskSource);
            #endif
            half darkness=1.0h-dot(maskSource,half3(0.2126h,0.7152h,0.0722h));
            half veins=smoothstep(_VeinThreshold,_VeinThreshold+max(_VeinFeather,0.001h),darkness);
            half deep=veins*smoothstep(_DeepThreshold,_DeepThreshold+max(_DeepFeather,0.001h),darkness);
            half fine=max(0.0h,veins-deep);
            half ground=1.0h-veins;
            half3 original=saturate(1.0h-(1.0h-stone)*_PatternStrength);
            // Three non-overlapping weights sum to one. Ground tint never multiplies vein colors.
            o.Albedo=original*_Color.rgb*ground
                    +lerp(original,_PatternColor.rgb,_FineTint)*fine
                    +lerp(original,_DeepPatternColor.rgb,_DeepTint)*deep;
            o.Metallic=_Metallic;
            o.Smoothness=_Glossiness;
            o.Occlusion=1;
            o.Alpha=1;
            if (_MaskPreview > 0.5)
            {
                o.Albedo=0;
                o.Emission=ground*half3(1,1,1)+fine*half3(0,0.3,1)+deep*half3(1,0,0);
                o.Metallic=0; o.Smoothness=0;
            }
        }
        ENDCG
    }
    CustomEditor "MarbleImageShaderGUI"
    FallBack "Standard"
}
