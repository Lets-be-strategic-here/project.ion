// |project|ion — Ion/FlatToon
// Low-poly pastel toon shading for URP (Forward), WebGL2-compatible.
//  * banded main-light N.L (3-step ramp) multiplied by realtime main-light shadows
//  * shadowed/unlit side tinted towards a cool blue/purple
//  * gradient ambient (sky / equator / ground) from Atmosphere globals, SH fallback
//  * fog: URP MixFog when a fog keyword is active, otherwise Atmosphere's global linear fog
//  * ShadowCaster + DepthOnly passes so the object casts shadows and writes depth
Shader "Ion/FlatToon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _ShadowTint ("Shadow Tint", Color) = (0.62, 0.60, 0.86, 1)
        _RampSmooth ("Ramp Edge Softness", Range(0.001, 0.2)) = 0.03
        _LitStrength ("Direct Light Strength", Range(0, 2)) = 0.65
        _LitAmbient ("Ambient On Lit Side", Range(0, 1)) = 0.5
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Identical in every pass -> SRP Batcher compatible.
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadowTint;
            half _RampSmooth;
            half _LitStrength;
            half _LitAmbient;
            half4 _EmissionColor;
        CBUFFER_END
        ENDHLSL

        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Set by Ion.Presentation.Atmosphere (linear-space colours). If unset (all zero)
            // ambient falls back to the SH probe and custom fog is disabled.
            float4 _IonAmbientSky;
            float4 _IonAmbientEquator;
            float4 _IonAmbientGround;
            float4 _IonFogColor;
            float4 _IonFogParams;   // x = start, y = 1 / (end - start), z = enabled (0/1)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS);

                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half3 GradientAmbient(half3 n)
            {
                half3 sky = _IonAmbientSky.rgb;
                half3 equator = _IonAmbientEquator.rgb;
                half3 ground = _IonAmbientGround.rgb;
                if (dot(sky + equator + ground, half3(1, 1, 1)) <= 0.0001)
                    return SampleSH(n);

                half up = saturate(n.y);
                half down = saturate(-n.y);
                half3 c = lerp(equator, sky, up);
                return lerp(c, ground, down);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 n = normalize(input.normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight();
                half shadow = MainLightRealtimeShadow(shadowCoord);
                // Fade realtime shadows out towards the shadow distance (avoids a hard edge).
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    half fade = GetMainLightShadowFade(input.positionWS);
                    shadow = lerp(shadow, 1.0h, fade);
                #endif

                half ndl = saturate(dot(n, mainLight.direction));
                half lightTerm = ndl * shadow;

                // 3-step ramp: 0 (shade), 0.5 (half-lit), 1 (lit) with slightly soft edges.
                half s = _RampSmooth;
                half band = 0.5h * smoothstep(0.08h - s, 0.08h + s, lightTerm)
                          + 0.5h * smoothstep(0.45h - s, 0.45h + s, lightTerm);

                half3 albedo = _BaseColor.rgb;
                half3 ambient = GradientAmbient(n);

                half3 shadeLight = ambient * _ShadowTint.rgb;
                half3 litLight = ambient * _LitAmbient + mainLight.color * _LitStrength;
                half3 color = albedo * lerp(shadeLight, litLight, band);
                color += _EmissionColor.rgb;

                // URP fog when this variant has fog (static keyword, or dynamic_branch keywords on
                // newer URP). If the fog variants were stripped from the build, fall back to the
                // Atmosphere globals so the look stays identical.
                #if defined(FOG_LINEAR_KEYWORD_DECLARED) || defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    color = MixFog(color, input.fogFactor);
                #else
                    if (_IonFogParams.z > 0.5)
                    {
                        float dist = distance(input.positionWS, GetCameraPositionWS());
                        half f = saturate((dist - _IonFogParams.x) * _IonFogParams.y);
                        color = lerp(color, _IonFogColor.rgb, f);
                    }
                #endif

                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP's shadow caster pass.
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // DepthNormals: lets SSAO / depth-normal prepasses work if ever enabled.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DNVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DNFrag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
