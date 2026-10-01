// |project|ion — Ion/AmbienceSoft
// Tiny unlit soft-dot shader for ambience billboards (dust motes, sun glow). No texture: the soft
// disc is computed from UV. Vertex colour (particle colour) x _BaseColor. No fog, no lighting,
// no keywords (1 variant), WebGL2-friendly. Blend mode is driven by _SrcBlend/_DstBlend so the same
// shader does alpha (SrcAlpha, OneMinusSrcAlpha) and additive (SrcAlpha, One).
Shader "Ion/AmbienceSoft"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Falloff ("Falloff Exponent", Range(0.5, 8)) = 2
        _Core ("Core Brightness", Range(0, 2)) = 0.4
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5   // SrcAlpha
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10  // OneMinusSrcAlpha
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "AmbienceSoft"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Falloff;
                half _Core;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _BaseColor;
                output.uv = input.uv * 2.0 - 1.0;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half d = saturate(length(input.uv));
                half a = pow(1.0h - d, _Falloff);
                // A slightly brighter centre so the dot reads as a glint, not a smudge.
                half core = pow(1.0h - d, _Falloff * 4.0h) * _Core;
                half4 c = input.color;
                return half4(c.rgb * (1.0h + core), saturate(c.a * (a + core)));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
