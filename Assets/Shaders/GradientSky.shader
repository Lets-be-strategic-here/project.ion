// [project]ion — Ion/GradientSky
// Three-colour vertical gradient skybox (top / horizon / bottom), golden horizon towards the sun, soft
// halo and sun disc. Uses the Atmosphere globals (IonAtmosphere.hlsl) so fog fades into exactly this sky.
Shader "Ion/GradientSky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.56, 0.78, 0.92, 1)
        _HorizonColor ("Horizon", Color) = (1.0, 0.96, 0.88, 1)
        _BottomColor ("Bottom", Color) = (0.80, 0.84, 0.88, 1)
        _TopExponent ("Top Falloff", Range(0.1, 4)) = 0.8
        _BottomExponent ("Bottom Falloff", Range(0.1, 4)) = 0.6
        _SunColor ("Sun Glow", Color) = (1.0, 0.95, 0.82, 1)
        _SunSize ("Sun Glow Tightness", Range(1, 2000)) = 600
        _SunHalo ("Sun Halo Strength", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "IonAtmosphere.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BottomColor;
                half _TopExponent;
                half _BottomExponent;
                half4 _SunColor;
                half _SunSize;
                half _SunHalo;
            CBUFFER_END
            // Ultra tier (UltraFx): w = HDR glow gain (0 elsewhere).
            float4 _IonUltraFx;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS      : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.dirOS = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.dirOS);
                half y = (half)dir.y;

                half3 col;
                if (dot(_IonSkyHorizon.rgb, float3(1, 1, 1)) > 0.0001)
                {
                    // Same gradient (with the golden horizon and sun halo) that the fog fades into.
                    col = (half3)IonSkyColor(dir);
                }
                else
                {
                    half up = pow(saturate(y), _TopExponent);
                    half down = pow(saturate(-y), _BottomExponent);
                    col = lerp(_HorizonColor.rgb, _TopColor.rgb, up);
                    col = lerp(col, _BottomColor.rgb, down);
                }

                // Sun disc (+ the material halo when the globals are missing) towards the main light.
                float3 sunDir = _MainLightPosition.xyz;
                float sunCore = 0.0;
                if (dot(sunDir, sunDir) > 0.0001)
                {
                    // float: pow(d, ~600) needs full precision near d = 1.
                    float d = saturate(dot(dir, normalize(sunDir)));
                    float halo = dot(_IonSkyHorizon.rgb, float3(1, 1, 1)) > 0.0001 ? 0.0 : _SunHalo * pow(d, 8.0);
                    float glow = pow(d, (float)_SunSize) + halo;
                    // Blended towards the warm disc colour (adding it clipped the core to cool white).
                    col = lerp(col, _SunColor.rgb, saturate((half)glow * saturate(y * 4.0h + 0.5h)));
                    sunCore = saturate(pow(d, (float)_SunSize * 0.5)) * saturate(y * 4.0 + 0.5);
                }

                half3 graded = IonGrade(col, input.positionCS.xy);
                // Ultra (HDR + bloom): the sun disc goes past 1 so it, and only it in the sky, blooms.
                graded *= 1.0h + (half)(sunCore * _IonUltraFx.w * 1.5);
                return half4(graded, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
