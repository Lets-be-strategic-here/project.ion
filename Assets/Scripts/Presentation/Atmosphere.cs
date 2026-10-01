using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Presentation
{
    /// <summary>
    /// Sets up the scene's sun, gradient sky, trilight ambient and linear fog.
    /// Call once at startup (GameBootstrap). Safe to call again; it reuses the sun.
    /// Look: Viewfinder-like pastel calm — warm sun ~38° up from the side (to the right of the +Z walking
    /// direction, slightly behind), so tops are lit, fronts half-lit and left faces in a soft blue-lilac
    /// shade; sky-blue / peach / lilac trilight; linear fog (28 → 150 m) into a peach horizon.
    /// Further ambience (clouds, motes, sway, sun glow, rim) lives in <c>Ion.Presentation.Ambience</c>.
    /// </summary>
    public static class Atmosphere
    {
        public const string SkyShaderName = "Ion/GradientSky";
        public const string SunName = "Ion Sun";

        // Sky gradient (sRGB): clear blue overhead -> warm peach-cream horizon -> soft lilac haze below.
        public static readonly Color SkyTop = new Color32(0x86, 0xC0, 0xE8, 0xFF);
        public static readonly Color SkyHorizon = new Color32(0xFC, 0xE4, 0xD2, 0xFF);
        public static readonly Color SkyBottom = new Color32(0xE2, 0xD4, 0xEA, 0xFF);

        // Trilight ambient (sRGB): sky blue from above, peach at the equator, lilac from below.
        // Kept fairly bright: the toon ramp tints the shade side blue-slate on top of this.
        public static readonly Color AmbientSky = new Color32(0xBF, 0xE3, 0xF2, 0xFF);
        public static readonly Color AmbientEquator = new Color32(0xEC, 0xDE, 0xD8, 0xFF);
        public static readonly Color AmbientGround = new Color32(0xC4, 0xB8, 0xD2, 0xFF);

        // Warm late-morning sun.
        public static readonly Color SunColor = new Color32(0xFF, 0xEA, 0xD2, 0xFF);
        public const float SunIntensity = 1.15f;
        /// <summary>Direction TO the sun (world): from +X, a little behind the +Z walking direction, ~37° up.</summary>
        public static readonly Vector3 ToSun = new Vector3(0.75f, 0.6f, -0.28f).normalized;

        public const float FogStart = 28f;
        public const float FogEnd = 150f;

        // Sky-shader sun disc / halo (Ion/GradientSky).
        const float SkySunSize = 900f;
        const float SkySunHalo = 0.32f;

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
                    if (s_SkyMaterial.HasProperty("_SunSize")) s_SkyMaterial.SetFloat("_SunSize", SkySunSize);
                    if (s_SkyMaterial.HasProperty("_SunHalo")) s_SkyMaterial.SetFloat("_SunHalo", SkySunHalo);
                    if (s_SkyMaterial.HasProperty("_TopExponent")) s_SkyMaterial.SetFloat("_TopExponent", 0.7f);
                    if (s_SkyMaterial.HasProperty("_BottomExponent")) s_SkyMaterial.SetFloat("_BottomExponent", 0.45f);
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
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            // Biases come from the URP asset (depth 1, normal 1 texel); the light's own values are unused.
            sun.renderMode = LightRenderMode.ForcePixel;
            // Never light/shadow the UI layer.
            sun.cullingMask = ~(1 << 9);
            // Side light: shadows fall to the left and slightly toward the player, faces read in three tones.
            sun.transform.rotation = Quaternion.LookRotation(-ToSun, Vector3.up);
            return sun;
        }
    }
}
