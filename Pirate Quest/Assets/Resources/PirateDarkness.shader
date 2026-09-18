Shader "PirateQuest/Scout darkness"
{
    Properties { _Tint ("Darkness tint", Color) = (0.008,0.016,0.027,0.985) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            // This project uses Unity's Built-in forward renderer. An SRP-only pass
            // compiles successfully but is never drawn by that renderer.
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; };
            struct Interpolator { float4 vertex : SV_POSITION; float2 world : TEXCOORD0; };
            float4 _Tint;
            float4 _Pirate;
            float4 _Bird;
            float _HasBird;
            Interpolator vert(Input input)
            {
                Interpolator output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.world = mul(unity_ObjectToWorld, input.vertex).xy;
                return output;
            }
            fixed4 frag(Interpolator input) : SV_Target
            {
                float pirate = smoothstep(1.3, 2.7, distance(input.world, _Pirate.xy));
                float bird = lerp(1, smoothstep(_Bird.z - 1, _Bird.z + 0.5, distance(input.world, _Bird.xy)), _HasBird);
                return fixed4(_Tint.rgb, _Tint.a * min(pirate, bird));
            }
            ENDCG
        }
    }
}
