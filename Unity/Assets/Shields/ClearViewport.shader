Shader "Hawks/RoN/ClearViewport" {
 SubShader {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
  Pass {
   Cull Back ZWrite Off ZTest LEqual
   Blend SrcAlpha OneMinusSrcAlpha
   ColorMask RGB
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct Interpolated { float4 position:SV_POSITION; float3 normal:TEXCOORD0; float3 view:TEXCOORD1; };
   Interpolated vert(appdata_base v) {
    Interpolated o;
    o.position=UnityObjectToClipPos(v.vertex); o.normal=UnityObjectToWorldNormal(v.normal);
    o.view=WorldSpaceViewDir(v.vertex); return o;
   }
   fixed4 frag(Interpolated i):SV_Target {
    float edge=1-saturate(abs(dot(normalize(i.normal),normalize(i.view))));
    float3 reflected = reflect(-normalize(i.view), normalize(i.normal));
    float sky = smoothstep(-.45, .85, reflected.y);
    float3 glass = lerp(float3(.055,.105,.10), float3(.22,.31,.30), sky);
    return fixed4(glass, lerp(.16,.30,edge*edge));
   }
   ENDCG
  }
 }
 Fallback Off
}
