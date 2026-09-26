Shader "Fantasy Kingdom/Movement Route"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 p:POSITION):SV_POSITION { return UnityObjectToClipPos(p); }
            fixed4 frag():SV_Target { return fixed4(.35,.9,1,.65); }
            ENDCG
        }
    }
}
