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
    public static partial class Atmosphere
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
            EnsureSkyMaterial();
            if (s_SkyMaterial != null)
                RenderSettings.skybox = s_SkyMaterial;
            RenderSettings.sun = sun;
            // Keep the zone mood if one was applied already (Apply is safe to call again).
            ApplyMood(s_HasMood ? s_Current : ZoneMood.Default);
            return sun;
        }

        static void EnsureSkyMaterial()
        {
            if (s_SkyMaterial != null) return;
            var skyShader = Shader.Find(SkyShaderName);
            if (skyShader == null)
            {
                Debug.LogWarning("[Atmosphere] Shader '" + SkyShaderName +
                                 "' not found (add it to Always Included Shaders). Sky will use the default.");
                return;
            }
            s_SkyMaterial = new Material(skyShader) { name = "Ion_GradientSky" };
            s_SkyMaterial.SetColor("_SunColor", SunDisc);
            if (s_SkyMaterial.HasProperty("_SunSize")) s_SkyMaterial.SetFloat("_SunSize", SkySunSize);
            if (s_SkyMaterial.HasProperty("_SunHalo")) s_SkyMaterial.SetFloat("_SunHalo", SkySunHalo);
            if (s_SkyMaterial.HasProperty("_TopExponent")) s_SkyMaterial.SetFloat("_TopExponent", SkyTopExponent);
            if (s_SkyMaterial.HasProperty("_BottomExponent")) s_SkyMaterial.SetFloat("_BottomExponent", SkyBottomExponent);
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
            s_Sun = sun;
            return sun;
        }
    }

    // ====================================================================== zone moods (art bible §3.4)

    public static partial class Atmosphere
    {
        static readonly int IonShadowTintId = Shader.PropertyToID("_IonShadowTint");
        static readonly int IonShadowTintMixId = Shader.PropertyToID("_IonShadowTintMix");
        static readonly int IonShadowHueId = Shader.PropertyToID("_IonShadowHue");
        static readonly int IonPatternFadeId = Shader.PropertyToID("_IonPatternFade");
        static readonly int IonPatternOnId = Shader.PropertyToID("_IonPatternOn");
        static readonly int IonCutHatchId = Shader.PropertyToID("_IonCutHatch");

        /// <summary>
        /// Linear luma of the zone shade multiplier (art bible rule 7: the shade is a value drop TINTED by the sky).
        /// Raised from 0.41 (which, with the full sky hue, turned every zone one saturated colour) to a light grey.
        /// A mood may override it (<see cref="ZoneMood.ShadeValue"/>, e.g. the darker Darkroom).
        /// </summary>
        public const float ShadowTintLuma = 0.58f;
        /// <summary>Fraction of the sky tint's chroma the shade multiplier keeps (0 = grey shade, 1 = the sky's hue).</summary>
        public const float ShadowTintChroma = 0.26f;
        /// <summary>Zero-luma hue offset added on the shade side (FlatToon _IonShadowHue), as a fraction of the tint's chroma.</summary>
        public const float ShadowHueAdd = 0.035f;
        /// <summary>How much the zone tint replaces each material's own _ShadowTint (FlatToon _IonShadowTintMix).</summary>
        public const float ShadowTintMix = 1f;

        /// <summary>Pattern fade distances (m) per tier (§3.2): High/Medium 25 → 45, Low 15 → 30.</summary>
        public static readonly Vector2 PatternFade = new Vector2(25f, 45f), PatternFadeLow = new Vector2(15f, 30f);

        static ZoneMood s_Current;
        static bool s_HasMood;
        static bool s_PatternsEnabled = true;
        static Light s_Sun;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetMoodStatics()
        {
            s_HasMood = false;
            s_Current = default;
            s_Sun = null;
            s_PatternsEnabled = true;
            AtmosphereBlender.Stop();
        }

        /// <summary>The mood currently on screen (mid-blend values while a <see cref="BlendTo"/> runs).</summary>
        public static ZoneMood Current => s_HasMood ? s_Current : ZoneMood.Default;

        /// <summary>The mood a running blend is heading to (or <see cref="Current"/>).</summary>
        public static ZoneMood Target => AtmosphereBlender.IsBlending ? AtmosphereBlender.To : Current;

        /// <summary>True while a <see cref="BlendTo"/> is in progress.</summary>
        public static bool IsBlending => AtmosphereBlender.IsBlending;

        /// <summary>
        /// Applies a mood immediately (captures, tests, zone builds) and cancels any blend in progress.
        /// Binding (§3.4): call it with the zone's mood BEFORE capturing that zone's diorama shots.
        /// </summary>
        public static void ApplyMood(ZoneMood mood)
        {
            AtmosphereBlender.Stop();
            Push(mood);
        }

        /// <summary>Blends from the current look to <paramref name="mood"/> over <paramref name="seconds"/> (play time; immediate otherwise).</summary>
        public static void BlendTo(ZoneMood mood, float seconds = 2f)
        {
            if (!Application.isPlaying || seconds <= 0f)
            {
                ApplyMood(mood);
                return;
            }
            AtmosphereBlender.Begin(Current, mood, seconds);
        }

        /// <summary>Pattern master switch (debug / settings). Re-applies the pattern globals.</summary>
        public static bool PatternsEnabled
        {
            get => s_PatternsEnabled;
            set { s_PatternsEnabled = value; ApplyPatternGlobals(); }
        }

        /// <summary>
        /// Pushes the pattern globals of Ion/FlatToon for the current quality tier: _IonPatternOn, _IonPatternFade
        /// (25 → 45 m; Low 15 → 30 m) and _IonCutHatch (1; 0 on Low). Called by moods, Ambience and quality changes.
        /// </summary>
        public static void ApplyPatternGlobals()
        {
            int tier = QualityTier.Current;
            Vector2 fade = tier == QualityTier.Low ? PatternFadeLow : PatternFade;
            Shader.SetGlobalVector(IonPatternFadeId, new Vector4(fade.x, fade.y, 0f, 0f));
            Shader.SetGlobalFloat(IonPatternOnId, s_PatternsEnabled ? 1f : 0f);
            Shader.SetGlobalFloat(IonCutHatchId, tier == QualityTier.Low ? 0f : 1f);
        }

        /// <summary>
        /// The zone shadow tint as the linear shade multiplier FlatToon uses: a grey of luma
        /// <see cref="ShadowTintLuma"/> carrying <see cref="ShadowTintChroma"/> of the tint's chroma (its hue kept).
        /// </summary>
        public static Color ShadeMultiplier(Color shadowTintSrgb) => ShadeMultiplier(shadowTintSrgb, ShadowTintLuma);

        /// <summary>As above with an explicit shade luma (a mood's <see cref="ZoneMood.ShadeValue"/>).</summary>
        public static Color ShadeMultiplier(Color shadowTintSrgb, float shadeLuma)
        {
            Vector3 chroma = ShadeChroma(shadowTintSrgb, shadeLuma);
            return new Color(Mathf.Clamp01(shadeLuma + ShadowTintChroma * chroma.x),
                             Mathf.Clamp01(shadeLuma + ShadowTintChroma * chroma.y),
                             Mathf.Clamp01(shadeLuma + ShadowTintChroma * chroma.z), 1f);
        }

        /// <summary>The small zero-luma hue offset FlatToon adds on the shade side (linear).</summary>
        public static Color ShadeHue(Color shadowTintSrgb, float shadeLuma)
        {
            Vector3 chroma = ShadeChroma(shadowTintSrgb, shadeLuma) * ShadowHueAdd;
            return new Color(chroma.x, chroma.y, chroma.z, 0f);
        }

        /// <summary>The tint (linear) scaled to <paramref name="luma"/>, minus that luma: its chroma vector (zero luma).</summary>
        static Vector3 ShadeChroma(Color shadowTintSrgb, float luma)
        {
            Color lin = shadowTintSrgb.linear;
            float l = 0.2126f * lin.r + 0.7152f * lin.g + 0.0722f * lin.b;
            if (l < 1e-4f) return Vector3.zero;
            float k = luma / l;
            return new Vector3(lin.r * k - luma, lin.g * k - luma, lin.b * k - luma);
        }

        /// <summary>Writes a mood to the sun, the sky material, RenderSettings and the shader globals.</summary>
        internal static void Push(ZoneMood mood)
        {
            s_Current = mood;
            s_HasMood = true;

            Vector3 toSun = mood.ToSun.sqrMagnitude > 1e-8f ? mood.ToSun.normalized : ToSun;
            Light sun = FindSun();
            if (sun != null)
            {
                sun.color = mood.SunColor;
                sun.intensity = mood.SunIntensity;
                sun.transform.rotation = Quaternion.LookRotation(-toSun, Mathf.Abs(toSun.y) > 0.99f ? Vector3.forward : Vector3.up);
            }

            if (s_SkyMaterial != null)
            {
                s_SkyMaterial.SetColor("_TopColor", mood.SkyTop);
                s_SkyMaterial.SetColor("_HorizonColor", mood.SkyHorizon);
                s_SkyMaterial.SetColor("_BottomColor", mood.SkyBottom);
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = mood.AmbientSky;
            RenderSettings.ambientEquatorColor = mood.AmbientEquator;
            RenderSettings.ambientGroundColor = mood.AmbientGround;
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = mood.SkyHorizon;
            RenderSettings.fogStartDistance = mood.FogStart;
            RenderSettings.fogEndDistance = FogEnd;

            Shader.SetGlobalVector(IonAmbientSky, (Vector4)mood.AmbientSky.linear);
            Shader.SetGlobalVector(IonAmbientEquator, (Vector4)mood.AmbientEquator.linear);
            Shader.SetGlobalVector(IonAmbientGround, (Vector4)mood.AmbientGround.linear);
            Shader.SetGlobalVector(IonFogParams, new Vector4(mood.FogStart, mood.FogDensity, 1f, FogMax));
            Shader.SetGlobalVector(IonFogHeight, new Vector4(FogBelowBoost, FogBelowStart, 1f / FogBelowRamp, 0f));
            Color top = mood.SkyTop.linear, horizon = mood.SkyHorizon.linear;
            Shader.SetGlobalVector(IonSkyTop, new Vector4(top.r, top.g, top.b, SkyTopExponent));
            Shader.SetGlobalVector(IonSkyHorizon, new Vector4(horizon.r, horizon.g, horizon.b, SkyBottomExponent));
            Shader.SetGlobalVector(IonSkyBottom, (Vector4)mood.SkyBottom.linear);
            // Golden horizon towards the sun: the mood's sun colour, warmed.
            Color warm = Color.Lerp(mood.SunColor, SunWarm, 0.5f).linear;
            Shader.SetGlobalVector(IonSunWarm, new Vector4(warm.r, warm.g, warm.b, SunWarmStrength));
            Shader.SetGlobalVector(IonSunHalo, (Vector4)Color.Lerp(mood.SunColor, SunHaloColor, 0.5f).linear);
            Shader.SetGlobalVector(IonGrade, new Vector4(GradeContrast, GradeLift, GradePivot, 1f));
            Shader.SetGlobalVector(IonSunDirection, new Vector4(toSun.x, toSun.y, toSun.z, SkySunHalo));

            float shadeLuma = mood.ShadeValue > 0f ? mood.ShadeValue : ShadowTintLuma;
            Color shade = ShadeMultiplier(mood.ShadowTint, shadeLuma);
            Color hue = ShadeHue(mood.ShadowTint, shadeLuma);
            Shader.SetGlobalVector(IonShadowTintId, new Vector4(shade.r, shade.g, shade.b, 1f));
            Shader.SetGlobalVector(IonShadowHueId, new Vector4(hue.r, hue.g, hue.b, 0f));
            Shader.SetGlobalFloat(IonShadowTintMixId, ShadowTintMix);
            ApplyPatternGlobals();
        }

        static Light FindSun()
        {
            if (s_Sun != null) return s_Sun;
            var existing = GameObject.Find(SunName);
            if (existing != null) s_Sun = existing.GetComponent<Light>();
            return s_Sun;
        }
    }
}
