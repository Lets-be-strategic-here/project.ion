// |project|ion — Ion/Backdrop
// The distant scenery ring (floating islands, mesas, mountains, a waterfall, a sea of clouds): one merged
// mesh, vertex colours, flat faces. Cheap: no shadows (no ShadowCaster pass, no shadow sampling), a
// wrapped two-tone sun term, a warm top highlight, and aerial perspective towards the same sky colour
// the sky shader draws (IonAtmosphere.hlsl). The haze is set per depth layer (uv0.x: near islands and
// clouds ~40 %, mesas ~60 %, mountains ~80 %) so the bands separate, plus a little more low down so
// the bases melt into the horizon haze. Vertex colour alpha = extra haze (1 = none, 0 = full sky colour).
//
// Diorama captures happen 1000 m below the world: when the camera is down there, the ring is shifted
// in the vertex shader by the same offset that maps the room onto its diorama (_IonBackdropMap), so a
// photo shows the backdrop exactly where it is seen from the matching spot in the world.
Shader "Ion/Backdrop"
{
    Properties
    {
        _HazeScale ("Layer Haze Scale", Range(0, 1.5)) = 1
        _LowHaze ("Extra Haze Low Down", Range(0, 1)) = 0.22
        _LowHazeTop ("Low Haze Top (object y)", Float) = 40
        _LowHazeBottom ("Low Haze Bottom (object y)", Float) = -110
        _ShadeTint ("Shade Tint", Color) = (0.68, 0.66, 0.86, 1)
        _TopLight ("Sunlit Top Highlight", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+50"
            "IgnoreProjector" = "True"
        }

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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "IonAtmosphere.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _HazeScale;
                half _LowHaze;
                float _LowHazeTop;
                float _LowHazeBottom;
                half4 _ShadeTint;
                half _TopLight;
            CBUFFER_END

            float4 _IonAmbientSky;
            float4 _IonAmbientEquator;
            // x = camera y below which the diorama mapping applies, y = diorama spacing (x),
            // z = x offset per diorama index (diorama spacing - room spacing), w = diorama y.
            float4 _IonBackdropMap;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;   // x = depth-layer haze
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;
                float2 heightHaze : TEXCOORD3;   // x = object-space height, y = layer haze
                half   soft       : TEXCOORD4;   // 1 = soft wrapped light (clouds)
            };

            float3 BackdropOffset()
            {
                float3 cam = GetCameraPositionWS();
                if (_IonBackdropMap.y <= 0.0 || cam.y > _IonBackdropMap.x) return float3(0, 0, 0);
                float i = round(cam.x / _IonBackdropMap.y);
                return float3(i * _IonBackdropMap.z, _IonBackdropMap.w, 0.0);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + BackdropOffset();
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = (half3)TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.heightHaze = float2(input.positionOS.y, input.uv.x);
                o.soft = (half)input.uv.y;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                Light sun = GetMainLight();
                half ndl = dot(n, (half3)sun.direction);
                // Soft two-tone: shade side lilac, lit side warm; a gentle ramp instead of shadows.
                half lit = smoothstep(-0.05h, 0.45h, ndl);
                // Clouds: wrapped, never fully in shade (their colour already carries the lilac belly).
                lit = lerp(lit, saturate(ndl * 0.45h + 0.62h), input.soft);
                half3 sky = dot(_IonAmbientSky.rgb, half3(1, 1, 1)) > 0.0001h ? (half3)_IonAmbientSky.rgb : half3(0.75h, 0.85h, 0.95h);
                half3 equator = dot(_IonAmbientEquator.rgb, half3(1, 1, 1)) > 0.0001h ? (half3)_IonAmbientEquator.rgb : half3(0.9h, 0.88h, 0.85h);
                half3 ambient = lerp(equator, sky, saturate(n.y));
                half3 shade = _ShadeTint.rgb * (0.75h + 0.25h * ambient);
                half3 light = ambient * 0.55h + sun.color * 0.55h;
                half3 color = input.color.rgb * lerp(shade, light, lit);
                color += sun.color * (_TopLight * saturate(n.y) * lit);

                // Aerial perspective: the layer's haze, a little more towards the bottom.
                float3 cam = GetCameraPositionWS();
                float3 v = input.positionWS - cam;
                float dist = length(v);
                float3 dir = v / max(dist, 1e-3);
                half haze = (half)input.heightHaze.y * _HazeScale;
                half low = saturate((_LowHazeTop - input.heightHaze.x) / max(1.0, _LowHazeTop - _LowHazeBottom));
                haze = saturate(haze + _LowHaze * low * low + (1.0h - input.color.a));
                half3 fogCol = dot(_IonSkyHorizon.rgb, float3(1, 1, 1)) > 0.0001 ? (half3)IonSkyColor(dir) : half3(0.98h, 0.9h, 0.84h);
                color = lerp(color, fogCol, haze);
                return half4(IonGrade(color, input.positionCS.xy), 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
