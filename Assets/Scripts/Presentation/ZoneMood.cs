using UnityEngine;

namespace Ion.Presentation
{
    /// <summary>
    /// Per-zone light (art bible §3.4): sky gradient, sun, ambient trilight, shadow tint and fog. Applied with
    /// <see cref="Atmosphere.ApplyMood"/> (immediately: captures, tests) or <see cref="Atmosphere.BlendTo"/>
    /// (2 s at play time). Everything a mood changes is a shader global or the one sun light, so materials stay
    /// shared across zones. Colours are sRGB (Atmosphere converts to linear for the shader globals).
    /// </summary>
    [System.Serializable]
    public struct ZoneMood
    {
        public Color SkyTop, SkyHorizon, SkyBottom, SunColor, ShadowTint, AmbientSky, AmbientEquator, AmbientGround;
        /// <summary>Direction TO the sun (world, normalised by Atmosphere).</summary>
        public Vector3 ToSun;
        public float SunIntensity, FogDensity, FogStart;
        /// <summary>Linear luma of the shade side (0 = <see cref="Atmosphere.ShadowTintLuma"/>). Lower = a darker zone.</summary>
        public float ShadeValue;

        /// <summary>Optional name (debug, tests).</summary>
        public string Name;

        // §3.4 moods. Plain static fields (not readonly) so a level can tune a copy at build time.
        public static ZoneMood Dawn, Darkroom, Noon, Mint, Rose, Golden;

        /// <summary>The pre-redesign look (Atmosphere's legacy constants): used until a zone applies its mood.</summary>
        public static ZoneMood Default;

        static ZoneMood()
        {
            Default = new ZoneMood
            {
                Name = "Default",
                SkyTop = Atmosphere.SkyTop,
                SkyHorizon = Atmosphere.SkyHorizon,
                SkyBottom = Atmosphere.SkyBottom,
                SunColor = Atmosphere.SunColor,
                ShadowTint = new Color(0.65f, 0.66f, 0.8f, 1f),
                AmbientSky = Atmosphere.AmbientSky,
                AmbientEquator = Atmosphere.AmbientEquator,
                AmbientGround = Atmosphere.AmbientGround,
                ToSun = Atmosphere.ToSun,
                SunIntensity = Atmosphere.SunIntensity,
                FogDensity = Atmosphere.FogDensity,
                FogStart = Atmosphere.FogStart,
            };

            // T1: soft, hopeful. Warm haze, sun low from the east (+X).
            Dawn = Make("Dawn", "#8FB8E6", "#F3E1D6", "#FFE2C2", "#6E7FB8",
                         new Vector3(0.86f, 0.30f, -0.22f), 1.10f, 0.0115f, 26f);
            // T2: dusk indoors. Dense haze, low warm sun; the safelight sconces carry the room.
            Darkroom = Make("Darkroom", "#3E4E86", "#E7A98F", "#FFC9A0", "#625E74",
                         new Vector3(0.62f, 0.26f, 0.38f), 0.92f, 0.0165f, 20f);
            // A darkroom, not a lavender room: lower ambient and a deeper (still airy) shade, so the safelight
            // sconces and the brass-framed cyanotype panels carry the colour.
            Darkroom.ShadeValue = 0.44f;
            Darkroom.AmbientSky = Scale(Darkroom.AmbientSky, 0.84f);
            Darkroom.AmbientEquator = Scale(Darkroom.AmbientEquator, 0.84f);
            Darkroom.AmbientGround = Scale(Darkroom.AmbientGround, 0.8f);
            // Hub: the most saturated sky, sun high.
            Noon = Make("Noon", "#1FA6D2", "#D6F0F5", "#FFF4E2", "#4D8FC4",
                         new Vector3(0.42f, 0.82f, -0.38f), 1.22f, 0.0080f, 34f);
            // A natural mint-cyan sky (was #0B9A84 → #8CC6B4: a pool-green that tinted the whole wing).
            Mint = Make("Mint", "#45B3C4", "#CFEDE6", "#FFF1DA", "#5E9EA0",
                         new Vector3(0.70f, 0.55f, -0.36f), 1.15f, 0.0085f, 32f);
            Rose = Make("Rose", "#A9BEE3", "#FBE6E6", "#FFE6D8", "#90849A",
                         new Vector3(0.74f, 0.48f, -0.40f), 1.15f, 0.0105f, 30f);
            // Gallery: the ending, sun low from the west (-X).
            // Shade tint cooled from #A0789A (it read magenta): golden-hour shadows are violet-blue.
            Golden = Make("Golden", "#F2B66B", "#FCE9C8", "#FFD9A6", "#8C8A9E",
                         new Vector3(-0.84f, 0.30f, -0.30f), 1.12f, 0.0105f, 30f);
        }

        /// <summary>
        /// Builds a mood from the bible's hex values. The bottom sky and the trilight ambient are derived:
        /// ambient sky = the sky top lifted towards white, equator = the horizon, ground = the shadow tint lifted
        /// (shade is coloured, never grey), bottom sky = horizon mixed with the shadow tint (misty void).
        /// </summary>
        public static ZoneMood Make(string name, string skyTop, string skyHorizon, string sun, string shadowTint,
                                    Vector3 toSun, float sunIntensity, float fogDensity, float fogStart)
        {
            Color top = Hex(skyTop), horizon = Hex(skyHorizon), shade = Hex(shadowTint);
            return new ZoneMood
            {
                Name = name,
                SkyTop = top,
                SkyHorizon = horizon,
                SkyBottom = Color.Lerp(horizon, shade, 0.35f),
                SunColor = Hex(sun),
                ShadowTint = shade,
                AmbientSky = Color.Lerp(top, Color.white, 0.55f),
                AmbientEquator = Color.Lerp(horizon, Color.white, 0.15f),
                AmbientGround = Color.Lerp(shade, Color.white, 0.55f),
                ToSun = toSun.normalized,
                SunIntensity = sunIntensity,
                FogDensity = fogDensity,
                FogStart = fogStart,
            };
        }

        /// <summary>Component-wise blend (colours in sRGB, sun direction slerped).</summary>
        public static ZoneMood Lerp(ZoneMood a, ZoneMood b, float t)
        {
            t = Mathf.Clamp01(t);
            Vector3 sa = a.ToSun.sqrMagnitude > 1e-8f ? a.ToSun.normalized : Vector3.up;
            Vector3 sb = b.ToSun.sqrMagnitude > 1e-8f ? b.ToSun.normalized : Vector3.up;
            return new ZoneMood
            {
                Name = t < 0.5f ? a.Name : b.Name,
                SkyTop = Color.Lerp(a.SkyTop, b.SkyTop, t),
                SkyHorizon = Color.Lerp(a.SkyHorizon, b.SkyHorizon, t),
                SkyBottom = Color.Lerp(a.SkyBottom, b.SkyBottom, t),
                SunColor = Color.Lerp(a.SunColor, b.SunColor, t),
                ShadowTint = Color.Lerp(a.ShadowTint, b.ShadowTint, t),
                AmbientSky = Color.Lerp(a.AmbientSky, b.AmbientSky, t),
                AmbientEquator = Color.Lerp(a.AmbientEquator, b.AmbientEquator, t),
                AmbientGround = Color.Lerp(a.AmbientGround, b.AmbientGround, t),
                ToSun = Vector3.Slerp(sa, sb, t).normalized,
                SunIntensity = Mathf.Lerp(a.SunIntensity, b.SunIntensity, t),
                FogDensity = Mathf.Lerp(a.FogDensity, b.FogDensity, t),
                FogStart = Mathf.Lerp(a.FogStart, b.FogStart, t),
                ShadeValue = Mathf.Lerp(a.ShadeValue > 0f ? a.ShadeValue : Atmosphere.ShadowTintLuma,
                                        b.ShadeValue > 0f ? b.ShadeValue : Atmosphere.ShadowTintLuma, t),
            };
        }

        static Color Scale(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        static Color Hex(string html) => ColorUtility.TryParseHtmlString(html, out Color c) ? c : Color.magenta;

        public override string ToString() => string.IsNullOrEmpty(Name) ? "ZoneMood" : "ZoneMood " + Name;
    }
}
