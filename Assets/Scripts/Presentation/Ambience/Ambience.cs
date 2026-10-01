using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Presentation
{
    /// <summary>
    /// "Alive world" layer: drifting low-poly clouds, warm dust motes around the camera, foliage sway,
    /// a soft sun-glow billboard and the toon rim light. Self-installs after the scene loads (after
    /// GameBootstrap.Awake has built the world and the player), so nothing has to call it.
    ///
    /// Every effect has a toggle and a cost knob and follows <see cref="QualityTier"/>:
    /// <list type="bullet">
    /// <item>Clouds: count 14 / 20 / 28 / 34 (Low / Med / High / Ultra) x <see cref="CloudDensity"/>; Low uses 20-face puffs.</item>
    /// <item>Motes: 30 / 90 / 180 / 320 particles x <see cref="MotesDensity"/>; noise turbulence on High and Ultra.</item>
    /// <item>Sway: off on Low, <see cref="SwayAmount"/> metres otherwise (vertex-only, Ion/FlatToon).</item>
    /// <item>Sun glow: 1 additive quad (Low/Med), 2 on High.</item>
    /// <item>Rim: <see cref="RimStrength"/> multiplier on every Ion/FlatToon material (global).</item>
    /// <item>Contact shade: deeper on Low (no realtime shadows), via the same global.</item>
    /// </list>
    /// </summary>
    public static class Ambience
    {
        /// <summary>
        /// Layer for camera-attached effects (motes, sun glow): layer 8 "Player" is never cut, captured
        /// or rendered into photo previews, so those effects never leak into photos.
        /// </summary>
        public const int CameraFxLayer = 8;
        public const string SoftShaderName = "Ion/AmbienceSoft";
        public const string ParticleShaderName = "Universal Render Pipeline/Particles/Unlit";

        // ------------------------------------------------------------------ toggles / knobs

        static bool s_Clouds = true, s_CloudDrift = true, s_Motes = true, s_Sway = true, s_SunGlow = true, s_Rim = true;
        static float s_CloudDensity = 1f, s_MotesDensity = 1f, s_SwayAmount = DefaultSway, s_RimStrength = 1f, s_SunGlowIntensity = 1f;

        public const float DefaultSway = 0.09f;

        public static bool CloudsEnabled { get => s_Clouds; set { if (s_Clouds == value) return; s_Clouds = value; Refresh(); } }
        /// <summary>Slow sine drift of the clouds (a CPU loop over ~10-30 transforms).</summary>
        public static bool CloudDriftEnabled { get => s_CloudDrift; set => s_CloudDrift = value; }
        public static bool MotesEnabled { get => s_Motes; set { if (s_Motes == value) return; s_Motes = value; Refresh(); } }
        public static bool SwayEnabled { get => s_Sway; set { if (s_Sway == value) return; s_Sway = value; Refresh(); } }
        public static bool SunGlowEnabled { get => s_SunGlow; set { if (s_SunGlow == value) return; s_SunGlow = value; Refresh(); } }
        public static bool RimEnabled { get => s_Rim; set { if (s_Rim == value) return; s_Rim = value; Refresh(); } }

        /// <summary>Cloud count multiplier (0..2).</summary>
        public static float CloudDensity { get => s_CloudDensity; set { s_CloudDensity = Mathf.Clamp(value, 0f, 2f); Refresh(); } }
        /// <summary>Mote count multiplier (0..2).</summary>
        public static float MotesDensity { get => s_MotesDensity; set { s_MotesDensity = Mathf.Clamp(value, 0f, 2f); Refresh(); } }
        /// <summary>Canopy tip displacement in metres (per unit of object-space height).</summary>
        public static float SwayAmount { get => s_SwayAmount; set { s_SwayAmount = Mathf.Clamp(value, 0f, 1f); Refresh(); } }
        /// <summary>Global rim-light multiplier for Ion/FlatToon (0..2).</summary>
        public static float RimStrength { get => s_RimStrength; set { s_RimStrength = Mathf.Clamp(value, 0f, 2f); Refresh(); } }
        public static float SunGlowIntensity { get => s_SunGlowIntensity; set { s_SunGlowIntensity = Mathf.Clamp(value, 0f, 2f); Refresh(); } }

        /// <summary>Current tier, clamped to 0..3.</summary>
        public static int Tier => Mathf.Clamp(QualityTier.Current, QualityTier.Low, QualityTier.Ultra);

        /// <summary>Re-applies all settings (toggles, knobs, quality tier). Cheap; not per frame.</summary>
        public static void Refresh()
        {
            ApplyGlobals();
            if (s_Director != null) s_Director.ApplySettings();
        }

        // ------------------------------------------------------------------ install

        static AmbienceDirector s_Director;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Director = null;
            s_Clouds = s_CloudDrift = s_Motes = s_Sway = s_SunGlow = s_Rim = true;
            s_CloudDensity = s_MotesDensity = s_RimStrength = s_SunGlowIntensity = 1f;
            s_SwayAmount = DefaultSway;
            s_SwayClones.Clear();
            s_SoftShader = null;
            s_ParticleShader = null;
            s_SoftDot = null;
            s_WarnedNoShader = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (s_Director != null) return;
            var go = new GameObject("Ambience");
            s_Director = go.AddComponent<AmbienceDirector>();
        }

        internal static void Unregister(AmbienceDirector d)
        {
            if (s_Director == d) s_Director = null;
        }

        // ------------------------------------------------------------------ toon globals / sway

        static readonly int ToonParamsId = Shader.PropertyToID("_IonToonParams");
        static readonly int SwayId = Shader.PropertyToID("_Sway");
        static readonly int SwayFreqId = Shader.PropertyToID("_SwayFreq");
        static readonly int SwayAnchorId = Shader.PropertyToID("_SwayAnchorY");

        /// <summary>Pushes the rim / sway multipliers read by every Ion/FlatToon material.</summary>
        internal static void ApplyGlobals()
        {
            float rim = s_Rim ? s_RimStrength : 0f;
            float sway = s_Sway && Tier > QualityTier.Low ? 1f : 0f;
            // w: the Low tier has no realtime shadows, so contact-shade discs deepen (FlatToon _AoBoost).
            // Ultra: the same baked contact shade at 45% as a cheap ambient-occlusion substitute (no SSAO on WebGL2).
            float contact = Tier == QualityTier.Low ? 1f : Tier >= QualityTier.Ultra ? 0.45f : 0f;
            Shader.SetGlobalVector(ToonParamsId, new Vector4(rim, sway, 1f, contact));
            // In-shader patterns (fade distances and the cut-face hatch depend on the tier).
            Atmosphere.ApplyPatternGlobals();
        }

        /// <summary>
        /// Sets <c>_Sway</c> on the shared tree-canopy materials (Kit.Tree's "Leaves" cones use
        /// <c>Palette.Get(LevelColors.Leaves / LeavesDark)</c>). Because cut and photo pieces copy the
        /// source renderer's shared materials, every canopy — original, cut or pasted — sways.
        /// The unit cone's base sits at object-space y = -0.5 (default anchor), so the base stays put.
        /// </summary>
        internal static void ApplyCanopySway()
        {
            SetSway(Palette.Get(Ion.Levels.LevelColors.Leaves), s_SwayAmount, 1.25f, -0.5f);
            SetSway(Palette.Get(Ion.Levels.LevelColors.LeavesDark), s_SwayAmount * 0.85f, 1.05f, -0.5f);
            // Kit.Tree canopies are merged per room (DecorCombiner): the sway weight lives in vertex alpha.
            Palette.Get(Ion.Levels.LevelColors.Leaves).SetFloat(SwayFromColorId, 1f);
            Palette.Get(Ion.Levels.LevelColors.LeavesDark).SetFloat(SwayFromColorId, 1f);
            // Light Table plant roles (PropKit plants, merged by Arch.Bake): sway weight in vertex alpha.
            SetSway(Palette.Get(Mat.Foliage), s_SwayAmount, 1.2f, -0.5f);
            SetSway(Palette.Get(Mat.FoliageLight), s_SwayAmount * 1.1f, 1.35f, -0.5f);
            SetSway(Palette.Get(Mat.FoliageDark), s_SwayAmount * 0.8f, 1.0f, -0.5f);
            SetSway(Palette.Get(Mat.Lilac), s_SwayAmount, 1.25f, -0.5f);
        }

        static void SetSway(Material m, float amount, float freq, float anchorY)
        {
            // The Ultra-only twin of a Palette role (extra plants) sways with it.
            if (m != null && Palette.TryGetMat(m, out Mat role) && !Palette.IsUltra(m))
            {
                Material twin = Palette.PeekUltra(role);
                if (twin != null && twin != m)
                {
                    SetSway(twin, amount, freq, anchorY);
                    if (m.HasProperty(SwayFromColorId)) twin.SetFloat(SwayFromColorId, m.GetFloat(SwayFromColorId));
                }
            }
            if (m == null || !m.HasProperty(SwayId)) return;
            m.SetFloat(SwayId, amount);
            if (m.HasProperty(SwayFreqId)) m.SetFloat(SwayFreqId, freq);
            if (m.HasProperty(SwayAnchorId)) m.SetFloat(SwayAnchorId, anchorY);
        }

        static readonly int SwayFromColorId = Shader.PropertyToID("_SwayFromColor");

        static readonly Dictionary<Material, Material> s_SwayClones = new Dictionary<Material, Material>();

        /// <summary>
        /// Makes a renderer sway (bushes, grass tufts, flags...). Each distinct source material is cloned
        /// once (cached) with <c>_Sway</c> set, keeping SRP-batcher compatibility. Only meaningful for
        /// Ion/FlatToon materials. <paramref name="anchorY"/> is the object-space height that stays fixed
        /// (-0.5 = bottom of Geo's unit meshes). Not per frame: allocates on first use of a material.
        /// </summary>
        public static void MakeSwaying(Renderer r, float amount = DefaultSway, float anchorY = -0.5f, float freq = 1.3f)
        {
            if (r == null) return;
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material src = mats[i];
                if (src == null || !src.HasProperty(SwayId)) continue;
                if (!s_SwayClones.TryGetValue(src, out Material clone) || clone == null)
                {
                    clone = new Material(src) { name = src.name + "_Sway" };
                    s_SwayClones[src] = clone;
                }
                SetSway(clone, amount, freq, anchorY);
                mats[i] = clone;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }

        // ------------------------------------------------------------------ soft billboard materials

        static Shader s_SoftShader, s_ParticleShader;
        static Texture2D s_SoftDot;
        static bool s_WarnedNoShader;

        /// <summary>
        /// Unlit soft-dot material for billboards/particles: Ion/AmbienceSoft if present, else URP
        /// Particles/Unlit (transparent, generated soft texture). Null if neither shader is in the build.
        /// </summary>
        internal static Material CreateSoftMaterial(string name, Color color, bool additive, float falloff = 2f, float core = 0.4f)
        {
            if (s_SoftShader == null) s_SoftShader = Shader.Find(SoftShaderName);
            if (s_SoftShader != null && s_SoftShader.isSupported)
            {
                var m = new Material(s_SoftShader) { name = name };
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Falloff", falloff);
                m.SetFloat("_Core", core);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                m.renderQueue = (int)RenderQueue.Transparent;
                return m;
            }

            if (s_ParticleShader == null) s_ParticleShader = Shader.Find(ParticleShaderName);
            if (s_ParticleShader != null && s_ParticleShader.isSupported)
            {
                var m = new Material(s_ParticleShader) { name = name };
                m.SetTexture("_BaseMap", SoftDot());
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Surface", 1f);                 // transparent
                m.SetFloat("_Blend", additive ? 2f : 0f);   // 0 alpha, 2 additive
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", (float)CullMode.Off);   // the sun-glow quad may face away
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                return m;
            }

            if (!s_WarnedNoShader)
            {
                s_WarnedNoShader = true;
                Debug.LogWarning("[Ambience] Neither '" + SoftShaderName + "' nor '" + ParticleShaderName +
                                 "' is in the build (Always Included Shaders); motes and sun glow are disabled.");
            }
            return null;
        }

        /// <summary>32x32 white texture with a soft radial alpha falloff (fallback path only).</summary>
        static Texture2D SoftDot()
        {
            if (s_SoftDot != null) return s_SoftDot;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "Ion_SoftDot",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                float a = (1f - d) * (1f - d);
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s_SoftDot = tex;
            return tex;
        }
    }
}
