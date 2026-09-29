Shader "Fantasy Kingdom/Inspection Range"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Offset -1,-1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 position:POSITION; fixed4 color:COLOR; };
            struct Output { float4 position:SV_POSITION; fixed4 color:COLOR; };
            Output vert(Input v) { Output o; o.position=UnityObjectToClipPos(v.position); o.color=v.color; return o; }
            fixed4 frag(Output v):SV_Target { return v.color; }
            ENDCG
        }
    }
}
