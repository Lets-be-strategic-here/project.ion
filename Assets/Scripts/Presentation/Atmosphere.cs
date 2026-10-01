using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Presentation
{
    /// <summary>
    /// Sets up the scene's sun, gradient sky, trilight ambient and linear fog.
    /// Call once at startup (GameBootstrap). Safe to call again; it reuses the sun.
    /// </summary>
    public static class Atmosphere
    {
        public const string SkyShaderName = "Ion/GradientSky";
        public const string SunName = "Ion Sun";

        // Sky gradient (sRGB).
        public static readonly Color SkyTop = new Color32(0x8E, 0xC9, 0xEA, 0xFF);
        public static readonly Color SkyHorizon = new Color32(0xFF, 0xF1, 0xDE, 0xFF);
        public static readonly Color SkyBottom = new Color32(0xD8, 0xDE, 0xE6, 0xFF);

        // Ambient gradient (sRGB). Kept fairly bright: the toon ramp darkens the shade side.
        public static readonly Color AmbientSky = new Color32(0xB4, 0xD6, 0xEE, 0xFF);
        public static readonly Color AmbientEquator = new Color32(0xE6, 0xDC, 0xD2, 0xFF);
        public static readonly Color AmbientGround = new Color32(0xA8, 0x9C, 0xA8, 0xFF);

        public static readonly Color SunColor = new Color32(0xFF, 0xF0, 0xDA, 0xFF);

        public const float FogStart = 45f;
        public const float FogEnd = 240f;

        static readonly int IonAmbientSky = Shader.PropertyToID("_IonAmbientSky");
        static readonly int IonAmbientEquator = Shader.PropertyToID("_IonAmbientEquator");
        static readonly int IonAmbientGround = Shader.PropertyToID("_IonAmbientGround");
        static readonly int IonFogColor = Shader.PropertyToID("_IonFogColor");
        static readonly int IonFogParams = Shader.PropertyToID("_IonFogParams");

        static Material s_SkyMaterial;

        /// <summary>The fog / horizon colour (useful for camera background fallbacks).</summary>
        public static Color FogColor => SkyHorizon;

        public static Light Apply()
        {
            Light sun = CreateOrFindSun();

            // Sky.
            if (s_SkyMaterial == null)
            {
                var skyShader = Shader.Find(SkyShaderName);
                if (skyShader != null)
                {
                    s_SkyMaterial = new Material(skyShader) { name = "Ion_GradientSky" };
                    s_SkyMaterial.SetColor("_TopColor", SkyTop);
                    s_SkyMaterial.SetColor("_HorizonColor", SkyHorizon);
                    s_SkyMaterial.SetColor("_BottomColor", SkyBottom);
                    s_SkyMaterial.SetColor("_SunColor", SunColor);
                }
                else
                {
                    Debug.LogWarning("[Atmosphere] Shader '" + SkyShaderName +
                                     "' not found (add it to Always Included Shaders). Sky will use the default.");
                }
            }
            if (s_SkyMaterial != null)
                RenderSettings.skybox = s_SkyMaterial;

            // Ambient (Unity's own trilight, used by URP SH / any fallback shaders).
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.sun = sun;

            // Fog.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyHorizon;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;

            // Globals read by Ion/FlatToon (explicitly linear so no colour-space ambiguity).
            Shader.SetGlobalVector(IonAmbientSky, (Vector4)AmbientSky.linear);
            Shader.SetGlobalVector(IonAmbientEquator, (Vector4)AmbientEquator.linear);
            Shader.SetGlobalVector(IonAmbientGround, (Vector4)AmbientGround.linear);
            Shader.SetGlobalVector(IonFogColor, (Vector4)SkyHorizon.linear);
            Shader.SetGlobalVector(IonFogParams, new Vector4(FogStart, 1f / (FogEnd - FogStart), 1f, 0f));

            return sun;
        }

        static Light CreateOrFindSun()
        {
            var existing = GameObject.Find(SunName);
            Light sun = existing != null ? existing.GetComponent<Light>() : null;
            if (sun == null)
            {
                var go = existing != null ? existing : new GameObject(SunName);
                sun = go.AddComponent<Light>();
            }

            sun.type = LightType.Directional;
            sun.color = SunColor;
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.35f;
            sun.renderMode = LightRenderMode.ForcePixel;
            // Never light/shadow the UI layer.
            sun.cullingMask = ~(1 << 9);
            sun.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            return sun;
        }
    }
}
