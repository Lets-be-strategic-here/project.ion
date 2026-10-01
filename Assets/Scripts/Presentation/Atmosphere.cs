using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Presentation
{
    /// <summary>
    /// Sets up the scene's sun, gradient sky, trilight ambient and linear fog.
    /// Call once at startup (GameBootstrap). Safe to call again; it reuses the sun.
    /// Look: Viewfinder-like pastel calm at a gentle golden hour — a warm sun ~30° up from the side (to the
    /// right of the +Z walking direction, slightly behind), so tops are lit, fronts half-lit and left faces
    /// in a soft blue-lilac shade; sky-blue / peach / lilac trilight; the horizon warms towards the sun.
    /// Aerial perspective (Ion/FlatToon, Ion/Backdrop via IonAtmosphere.hlsl): exponential fog from 30 m
    /// towards the sky colour behind each point, capped at 70 % so the neighbouring rooms and the distant
    /// backdrop read as soft silhouettes, and a little denser far below the eye (misty void).
    /// The sky shader, the fog, the sun-glow billboard and the shadows all take the sun direction from the
    /// one light, so they always agree.
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

        // Gentle golden-hour sun.
        public static readonly Color SunColor = new Color32(0xFF, 0xE2, 0xC4, 0xFF);
        public const float SunIntensity = 1.17f;
        /// <summary>Direction TO the sun (world): from +X, a little behind the +Z walking direction, ~30° up.</summary>
        public static readonly Vector3 ToSun = new Vector3(0.8f, 0.5f, -0.33f).normalized;

        /// <summary>Sky-shader sun disc core and its soft halo (warm, matching the golden horizon).</summary>
        public static readonly Color SunDisc = new Color32(0xFF, 0xF3, 0xD6, 0xFF);
        public static readonly Color SunHaloColor = new Color32(0xFF, 0xD9, 0xA0, 0xFF);

        /// <summary>Horizon colour towards the sun (golden hour), blended in near the horizon.</summary>
        public static readonly Color SunWarm = new Color32(0xFF, 0xD0, 0xA6, 0xFF);
        public const float SunWarmStrength = 0.55f;

        /// <summary>Aerial perspective: start (m), density (1/m), cap, extra density far below the eye.
        /// Starts past the room you stand in (30 m) and is capped at ~3/4 so neighbouring rooms keep their
        /// shape instead of washing out into milk.</summary>
        public const float FogStart = 30f;
        public const float FogDensity = 0.0105f;
        public const float FogMax = 0.7f;
        public const float FogBelowBoost = 0.6f, FogBelowStart = 10f, FogBelowRamp = 60f;
        /// <summary>Linear fog for Unity / URP shaders that use the built-in fog (fallbacks, particles).</summary>
        public const float FogEnd = 260f;

        // Sky-shader sun disc / halo (Ion/GradientSky). The halo is blended towards SunHaloColor.
        const float SkySunSize = 900f;
        const float SkySunHalo = 0.42f;

        /// <summary>
        /// Final grade (IonAtmosphere.hlsl IonGrade, in every world shader): luma contrast around a high
        /// pivot plus a small negative lift, so the bright pastel frame gets real darks (tree trunks, cast
        /// shadows) and depth without losing its colours.
        /// </summary>
        public const float GradeContrast = 1.4f, GradeLift = -0.035f, GradePivot = 0.85f;

        static readonly int IonAmbientSky = Shader.PropertyToID("_IonAmbientSky");
        static readonly int IonAmbientEquator = Shader.PropertyToID("_IonAmbientEquator");
        static readonly int IonAmbientGround = Shader.PropertyToID("_IonAmbientGround");
        static readonly int IonFogParams = Shader.PropertyToID("_IonFogParams");
        static readonly int IonFogHeight = Shader.PropertyToID("_IonFogHeight");
        static readonly int IonSkyTop = Shader.PropertyToID("_IonSkyTop");
        static readonly int IonSkyHorizon = Shader.PropertyToID("_IonSkyHorizon");
        static readonly int IonSkyBottom = Shader.PropertyToID("_IonSkyBottom");
        static readonly int IonSunDirection = Shader.PropertyToID("_IonSunDirection");
        static readonly int IonSunWarm = Shader.PropertyToID("_IonSunWarm");
        static readonly int IonSunHalo = Shader.PropertyToID("_IonSunHalo");
        static readonly int IonGrade = Shader.PropertyToID("_IonGrade");

        const float SkyTopExponent = 0.7f, SkyBottomExponent = 0.45f;

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
                    s_SkyMaterial.SetColor("_SunColor", SunDisc);
                    if (s_SkyMaterial.HasProperty("_SunSize")) s_SkyMaterial.SetFloat("_SunSize", SkySunSize);
                    if (s_SkyMaterial.HasProperty("_SunHalo")) s_SkyMaterial.SetFloat("_SunHalo", SkySunHalo);
                    if (s_SkyMaterial.HasProperty("_TopExponent")) s_SkyMaterial.SetFloat("_TopExponent", SkyTopExponent);
                    if (s_SkyMaterial.HasProperty("_BottomExponent")) s_SkyMaterial.SetFloat("_BottomExponent", SkyBottomExponent);
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
            Shader.SetGlobalVector(IonFogParams, new Vector4(FogStart, FogDensity, 1f, FogMax));
            Shader.SetGlobalVector(IonFogHeight, new Vector4(FogBelowBoost, FogBelowStart, 1f / FogBelowRamp, 0f));
            Color top = SkyTop.linear, horizon = SkyHorizon.linear;
            Shader.SetGlobalVector(IonSkyTop, new Vector4(top.r, top.g, top.b, SkyTopExponent));
            Shader.SetGlobalVector(IonSkyHorizon, new Vector4(horizon.r, horizon.g, horizon.b, SkyBottomExponent));
            Shader.SetGlobalVector(IonSkyBottom, (Vector4)SkyBottom.linear);
            Color warm = SunWarm.linear;
            Shader.SetGlobalVector(IonSunWarm, new Vector4(warm.r, warm.g, warm.b, SunWarmStrength));
            Shader.SetGlobalVector(IonSunHalo, (Vector4)SunHaloColor.linear);
            Shader.SetGlobalVector(IonGrade, new Vector4(GradeContrast, GradeLift, GradePivot, 1f));
            // Taken from the light itself, so the sky, the fog's sun side and the shadows agree.
            Vector3 toSun = -sun.transform.forward;
            Shader.SetGlobalVector(IonSunDirection, new Vector4(toSun.x, toSun.y, toSun.z, SkySunHalo));

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
