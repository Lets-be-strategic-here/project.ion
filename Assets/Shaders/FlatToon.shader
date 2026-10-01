// |project|ion — Ion/FlatToon
// Low-poly pastel toon shading for URP (Forward), WebGL2-compatible.
//  * banded main-light N.L (3-step ramp) multiplied by realtime main-light shadows
//  * shadowed/unlit side tinted towards a cool blue/purple
//  * gradient ambient (sky / equator / ground) from Atmosphere globals, SH fallback
//  * aerial perspective (IonAtmosphere.hlsl): exponential distance fog towards the sky colour behind the
//    point (golden horizon towards the sun, sun halo), denser far below the eye, capped below 1 so
//    distant rooms stay soft silhouettes. Camera-relative, so diorama captures fog identically.
//  * ShadowCaster + DepthOnly passes so the object casts shadows and writes depth
//  * soft 3-band ramp (smoothstep edges), blue-slate shadow tint, faint sky-coloured rim light,
//    no specular
//  * optional foliage sway (_Sway > 0): world-position-phased horizontal offset scaled by the
//    vertex's object-space height above _SwayAnchorY, or (when _SwayFromColor = 1, merged decor) by
//    the vertex colour's alpha. Applied identically in every pass so depth, shadows and colour stay in
//    sync. Global kill switches via _IonToonParams (Ambience).
//  * vertex colour RGB multiplies the albedo (white for plain Geo meshes; merged decor bakes a
//    per-instance tint and contact shading into it; the slicer carries it through cuts)
//  * per-face colour jitter hashed from the flat normal (_FaceJitter) and, for grass (_GroundVar),
//    a soft world-space colour drift plus faint triangular facets that fade with distance
Shader "Ion/FlatToon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _ShadowTint ("Shadow Tint (blue-lilac)", Color) = (0.65, 0.66, 0.8, 1)
        _RampSmooth ("Ramp Edge Softness", Range(0.001, 0.2)) = 0.06
        _MidBand ("Mid Band Level", Range(0, 1)) = 0.55
        _LitStrength ("Direct Light Strength", Range(0, 2)) = 0.65
        _LitAmbient ("Ambient On Lit Side", Range(0, 1)) = 0.42
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.22
        _RimPower ("Rim Power", Range(1, 8)) = 3
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _Sway ("Sway Amount (m at height 1)", Float) = 0
        _SwayFreq ("Sway Frequency", Float) = 1.3
        _SwayAnchorY ("Sway Anchor (object-space Y)", Float) = -0.5
        _SwayFromColor ("Sway Weight From Vertex Alpha", Float) = 0
        _FaceJitter ("Per-face Colour Jitter", Range(0, 0.2)) = 0.035
        _GroundVar ("Ground Colour Variation", Range(0, 1)) = 0
        _AoBoost ("Vertex AO Boost (Low tier)", Range(0, 2)) = 0
        _TopLight ("Sunlit Top Highlight", Range(0, 1)) = 0
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
            half _MidBand;
            half _LitStrength;
            half _LitAmbient;
            half _RimStrength;
            half _RimPower;
            half4 _EmissionColor;
            float _Sway;
            float _SwayFreq;
            float _SwayAnchorY;
            float _SwayFromColor;
            half _FaceJitter;
            half _GroundVar;
            half _AoBoost;
            half _TopLight;
        CBUFFER_END

        // Global look switches, set by Ion.Presentation.Ambience:
        // x = rim multiplier, y = sway multiplier, z = 1 when set (otherwise both default to 1),
        // w = vertex-AO boost scale (1 on the Low tier, which has no realtime shadows; 0 otherwise).
        float4 _IonToonParams;

        // Object -> world with optional foliage sway. Cheap uniform branch when _Sway == 0.
        // swayWeight = vertex colour alpha (used when _SwayFromColor = 1: merged decor, 0 at the trunk).
        float3 IonObjectToWorld(float3 positionOS, float swayWeight)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            float amount = _Sway * (_IonToonParams.z > 0.5 ? _IonToonParams.y : 1.0);
            if (amount > 0.0)
            {
                float h = _SwayFromColor > 0.5 ? swayWeight * 1.4 : max(0.0, positionOS.y - _SwayAnchorY);
                float t = _Time.y * _SwayFreq;
                // Phase from the (unswayed) world position: coherent across cut / photo pieces and
                // between the split (flat-shaded) vertices of a face, so no cracks appear.
                float phase = dot(positionWS.xz, float2(0.37, 0.29));
                float gust = 0.7 + 0.3 * sin(t * 0.31 + phase * 0.5);
                float2 wave = float2(sin(t + phase), 0.6 * sin(t * 0.83 + phase * 1.3 + 1.7));
                positionWS.xz += wave * (gust * amount * h);
            }
            return positionWS;
        }
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
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "IonAtmosphere.hlsl"

            // Set by Ion.Presentation.Atmosphere (linear-space colours). If unset (all zero)
            // ambient falls back to the SH probe and fog is disabled.
            float4 _IonAmbientSky;
            float4 _IonAmbientEquator;
            float4 _IonAmbientGround;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half3  color      : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = IonObjectToWorld(input.positionOS.xyz, input.color.a);
                float4 positionCS = TransformWorldToHClip(positionWS);

                output.positionCS = positionCS;
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color.rgb;
                return output;
            }

            float IonHash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float IonValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = IonHash12(i), b = IonHash12(i + float2(1, 0));
                float c = IonHash12(i + float2(0, 1)), d = IonHash12(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Albedo dressing: per-face jitter (flat normals -> one value per face; continuous in the
            // normal, so it is stable across a face) and grass colour drift + faint triangular facets.
            half3 IonDressAlbedo(half3 albedo, float3 n, float3 positionWS)
            {
                float j = sin(dot(n, float3(17.3, 31.7, 23.1))) * 0.6 + sin(dot(n, float3(-29.1, 13.9, 41.3))) * 0.4;
                albedo *= 1.0h + _FaceJitter * (half)j;

                if (_GroundVar > 0.001h)
                {
                    half up = smoothstep(0.6h, 0.95h, (half)n.y) * _GroundVar;
                    float2 wp = positionWS.xz;
                    half macro = (half)(IonValueNoise(wp * 0.075) * 0.65 + IonValueNoise(wp * 0.21 + 17.0) * 0.35);
                    macro = smoothstep(0.2h, 0.8h, macro);
                    // Triangular lattice (skewed grid split along the diagonal), ~2.4 m cells.
                    float2 q = wp * 0.42;
                    float2 sk = float2(q.x + q.y * 0.57735, q.y * 1.1547);
                    float2 cell = floor(sk);
                    float2 f = frac(sk);
                    float tri = step(f.y, f.x);
                    half facet = (half)IonHash12(cell * 2.0 + tri * 7.3) - 0.5h;
                    half dist = (half)distance(positionWS, GetCameraPositionWS());
                    half facetFade = 1.0h - saturate((dist - 14.0h) / 22.0h);
                    half3 cool = half3(0.92h, 0.99h, 1.03h);
                    half3 warm = half3(1.07h, 1.03h, 0.84h);
                    half3 tint = lerp(cool, warm, macro) * (1.0h + facet * 0.05h * facetFade);
                    albedo *= lerp(half3(1.0h, 1.0h, 1.0h), tint, up);
                }
                return albedo;
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

                float3 nF = normalize(input.normalWS);
                half3 n = (half3)nF;

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

                // 3-band ramp: 0 (shade), _MidBand (half-lit), 1 (lit) with soft smoothstep edges.
                half s = _RampSmooth;
                half band = _MidBand * smoothstep(0.08h - s, 0.08h + s, lightTerm)
                          + (1.0h - _MidBand) * smoothstep(0.45h - s, 0.45h + s, lightTerm);

                // Vertex colour = baked tint / contact shade multiplier. On the Low tier (no realtime
                // shadows) contact-shade materials deepen it so props still sit on the ground.
                half aoBoost = _AoBoost * (half)_IonToonParams.w;
                half3 vc = 1.0h - (1.0h - input.color) * (1.0h + aoBoost);
                half3 albedo = IonDressAlbedo(_BaseColor.rgb * saturate(vc), nF, input.positionWS);
                half3 ambient = GradientAmbient(n);

                // Shade side: a soft blue-lilac (never black), only gently following the ambient
                // direction so cast shadows stay airy rather than dark teal.
                half3 shadeLight = _ShadowTint.rgb * (0.72h + 0.28h * ambient);
                half3 litLight = ambient * _LitAmbient + mainLight.color * _LitStrength;
                half3 color = albedo * lerp(shadeLight, litLight, band);

                // Subtle rim from the sky colour, strongest on the shade side. No specular.
                half rimScale = _IonToonParams.z > 0.5 ? (half)_IonToonParams.x : 1.0h;
                half rimAmount = _RimStrength * rimScale;
                if (rimAmount > 0.001h)
                {
                    half3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                    half fres = pow(1.0h - saturate(dot(n, v)), _RimPower);
                    half3 skyCol = dot(_IonAmbientSky.rgb, half3(1, 1, 1)) > 0.0001h ? (half3)_IonAmbientSky.rgb : SampleSH(half3(0, 1, 0));
                    // Less on up- and down-facing faces: distant ground at grazing angles is not washed
                    // out and canopy undersides do not light up as a pale ring.
                    half facing = 1.0h - 0.75h * abs(n.y);
                    color += skyCol * (fres * rimAmount * facing * (1.0h - 0.6h * band));
                }

                // Gentle warm highlight on sunlit tops (cloud crowns); no bloom involved.
                if (_TopLight > 0.001h)
                    color += mainLight.color * (_TopLight * saturate(n.y) * band);

                color += _EmissionColor.rgb;

                color = IonApplyFog(color, input.positionWS);
                color = (half3)IonGrade(color, input.positionCS.xy);

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
                half4  color      : COLOR;
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

                float3 positionWS = IonObjectToWorld(input.positionOS.xyz, input.color.a);
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
                half4  color      : COLOR;
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
                output.positionCS = TransformWorldToHClip(IonObjectToWorld(input.positionOS.xyz, input.color.a));
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
                half4  color      : COLOR;
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
                output.positionCS = TransformWorldToHClip(IonObjectToWorld(input.positionOS.xyz, input.color.a));
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
