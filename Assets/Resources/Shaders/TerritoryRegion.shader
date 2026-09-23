Shader "Fantasy Kingdom/Territory Region"
{
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct Input { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 edges:TEXCOORD1; };
            struct Output { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 edges:TEXCOORD1; };
            Output vert(Input v)
            {
                Output o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color;
                o.uv=v.uv; o.edges=v.edges; return o;
            }
            float Corner(float2 p, float enabled, float d)
            {
                const float radius=0.14;
                if(enabled>0.5 && p.x<radius && p.y<radius)
                    return radius-length(p-radius);
                return d;
            }
            fixed4 frag(Output i):SV_Target
            {
                float2 p=i.uv;
                float4 distances=float4(p.x,1-p.x,p.y,1-p.y);
                distances=lerp(float4(10,10,10,10),distances,i.edges);
                float d=min(min(distances.x,distances.y),min(distances.z,distances.w));
                d=Corner(p,i.edges.x*i.edges.z,d);
                d=Corner(float2(1-p.x,p.y),i.edges.y*i.edges.z,d);
                d=Corner(float2(p.x,1-p.y),i.edges.x*i.edges.w,d);
                d=Corner(1-p,i.edges.y*i.edges.w,d);
                float aa=max(fwidth(d)*0.8,0.001);
                float coverage=smoothstep(-aa,aa,d);
                // A pale narrow rim, a fine inner accent and a soft faction-colored band.
                float rim=1-smoothstep(0.020-aa,0.020+aa,abs(d-0.037));
                float accent=1-smoothstep(0.008-aa,0.008+aa,abs(d-0.17));
                float halo=exp2(-max(d,0)*19);
                float alpha=saturate(i.color.a+halo*.22+rim*.80+accent*.24)*coverage;
                float3 color=lerp(i.color.rgb,lerp(i.color.rgb,float3(1,1,1),.48),rim*.85);
                return fixed4(color,alpha);
            }
            ENDCG
        }
    }
}
