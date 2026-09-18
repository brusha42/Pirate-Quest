Shader "PirateQuest/BlackTide"
{
    Properties
    {
        _AnimTime ("Local scaled water time", Float) = 0
        _SurfaceY ("Nominal surface", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off Lighting Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 position : SV_POSITION; float2 world : TEXCOORD0; fixed4 color : COLOR; };
            float _AnimTime;
            float _SurfaceY;
            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float depth = max(0, _SurfaceY - i.world.y);
                float drift = sin(i.world.x * 1.4 + i.world.y * .52 + _AnimTime * .63);
                float strands = sin(i.world.x * 3.8 - i.world.y * 1.7 - _AnimTime * .46 + drift);
                float caustics = pow(saturate(strands * .5 + .5), 12) * exp(-depth * .2);
                float foam = saturate(1 - depth / .16) *
                    (.55 + .45 * sin(i.world.x * 9.4 - _AnimTime * 1.8));
                fixed3 color = i.color.rgb + caustics * fixed3(.012, .048, .047) +
                    foam * fixed3(.04, .095, .08);
                return fixed4(color, i.color.a);
            }
            ENDCG
        }
    }
}
