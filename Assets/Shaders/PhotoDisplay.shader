// |project|ion — Ion/PhotoDisplay
// UGUI-compatible (RawImage / Image) unlit shader that gives photos a Polaroid look:
// slight desaturation, warm tint, lifted blacks and a soft vignette.
Shader "Ion/PhotoDisplay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Saturation ("Saturation", Range(0, 1)) = 0.8
        _WarmTint ("Warm Tint", Color) = (1.06, 1.0, 0.90, 1)
        _Lift ("Black Lift", Range(0, 0.3)) = 0.06
        _Vignette ("Vignette Strength", Range(0, 1)) = 0.28
        _VignetteStart ("Vignette Start", Range(0, 1)) = 0.35
        _VignetteEnd ("Vignette End", Range(0, 1.2)) = 0.85

        // Standard UGUI stencil / mask plumbing.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Saturation;
                half4 _WarmTint;
                half _Lift;
                half _Vignette;
                half _VignetteStart;
                half _VignetteEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 col = tex.rgb;

                half luma = dot(col, half3(0.299h, 0.587h, 0.114h));
                col = lerp(luma.xxx, col, _Saturation);
                col *= _WarmTint.rgb;
                col = col * (1.0h - _Lift) + _Lift;

                // Vignette over the photo's own UV rect (RawImage uvRect is assumed 0..1).
                float2 c = input.uv - 0.5;
                half dist = (half)length(c) * 1.4142h;   // 0 at centre, ~1 at corners
                half v = smoothstep(_VignetteStart, _VignetteEnd, dist);
                col *= 1.0h - _Vignette * v;

                half4 outCol = half4(saturate(col), tex.a) * input.color;
                return outCol;
            }
            ENDHLSL
        }
    }
    FallBack "UI/Default"
}
