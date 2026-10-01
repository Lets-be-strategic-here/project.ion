using System.Collections.Generic;
using UnityEngine;

namespace Ion.Presentation
{
    /// <summary>
    /// Pastel colour palette and a cache of one shared Ion/FlatToon material per colour.
    /// NOTE: Shader.Find only works in a player build if "Ion/FlatToon" is included
    /// (GraphicsSettings > Always Included Shaders, or referenced by an asset in the build).
    /// </summary>
    public static partial class Palette
    {
        public const string ToonShaderName = "Ion/FlatToon";

        // Core palette (contract).
        public static readonly Color Sky = new Color32(0xBF, 0xE3, 0xF2, 0xFF);
        public static readonly Color Mint = new Color32(0xA8, 0xD8, 0xB9, 0xFF);
        public static readonly Color Coral = new Color32(0xF4, 0xA7, 0x9D, 0xFF);
        public static readonly Color Cream = new Color32(0xFF, 0xF4, 0xE0, 0xFF);
        public static readonly Color Sand = new Color32(0xE8, 0xD5, 0xB5, 0xFF);
        public static readonly Color Slate = new Color32(0x4A, 0x5A, 0x6A, 0xFF);
        public static readonly Color Ink = new Color32(0x2B, 0x2F, 0x36, 0xFF);

        // Extras.
        public static readonly Color Wood = new Color32(0xC0, 0x8F, 0x62, 0xFF);
        public static readonly Color DarkWood = new Color32(0x8A, 0x63, 0x45, 0xFF);
        public static readonly Color Grass = new Color32(0x9C, 0xCB, 0x7A, 0xFF);
        public static readonly Color Stone = new Color32(0xC4, 0xC4, 0xCC, 0xFF);
        public static readonly Color Lavender = new Color32(0xC9, 0xB8, 0xE8, 0xFF);
        public static readonly Color Butter = new Color32(0xF6, 0xE3, 0xA1, 0xFF);
        public static readonly Color Peach = new Color32(0xFF, 0xD3, 0xB0, 0xFF);
        public static readonly Color Teal = new Color32(0x7F, 0xC8, 0xC4, 0xFF);
        public static readonly Color White = new Color32(0xFA, 0xFA, 0xF7, 0xFF);

        static readonly Dictionary<int, Material> s_Cache = new Dictionary<int, Material>();
        static readonly Dictionary<int, Material> s_EmissiveCache = new Dictionary<int, Material>();
        static Shader s_Shader;
        static bool s_WarnedMissing;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int GroundVarId = Shader.PropertyToID("_GroundVar");
        static readonly int SelfLitId = Shader.PropertyToID("_SelfLit");
        static readonly int GlowBoostId = Shader.PropertyToID("_GlowBoost");

        /// <summary>Contact-shade colour under props (kept in sync with LevelColors.Contact).</summary>
        public static readonly Color ContactShade = new Color32(0x8A, 0xB8, 0x76, 0xFF);

        static bool SameColor(Color a, Color b) => Key(a) == Key(b);

        /// <summary>Shared (cached) toon material for a colour. Do not modify the returned material.</summary>
        public static Material Get(Color c)
        {
            int key = Key(c);
            if (s_Cache.TryGetValue(key, out var mat) && mat != null)
                return mat;

            mat = CreateMaterial(c, "Ion_" + ColorUtility.ToHtmlStringRGB(c));
            s_Cache[key] = mat;
            return mat;
        }

        /// <summary>Shared toon material that also glows (e.g. teleporters, highlights).</summary>
        public static Material GetEmissive(Color c, float intensity = 0.6f)
        {
            Color32 k = c;
            k.a = (byte)Mathf.Clamp(Mathf.RoundToInt(intensity * 50f), 0, 255); // distinguish intensities
            int key = Key(k);
            if (s_EmissiveCache.TryGetValue(key, out var mat) && mat != null)
                return mat;

            mat = CreateMaterial(c, "Ion_Glow_" + ColorUtility.ToHtmlStringRGB(c));
            if (mat.HasProperty(EmissionId))
                mat.SetColor(EmissionId, c * intensity);
            // Strong glows are self-lit: their colour holds in sun and shade and never clips to white.
            if (intensity >= 0.45f && mat.HasProperty(SelfLitId)) mat.SetFloat(SelfLitId, 1f);
            s_EmissiveCache[key] = mat;
            return mat;
        }

        static readonly Dictionary<int, Material> s_ContactCache = new Dictionary<int, Material>();
        static readonly int AoBoostId = Shader.PropertyToID("_AoBoost");

        /// <summary>
        /// Shared material for contact-shade discs on a ground of colour <paramref name="ground"/>: the same
        /// look as <see cref="Get"/>, but its baked radial shade deepens on the Low tier (no realtime
        /// shadows; see Ion/FlatToon _AoBoost and Ambience's tier globals).
        /// </summary>
        public static Material GetContact(Color ground)
        {
            int key = Key(ground);
            if (s_ContactCache.TryGetValue(key, out var mat) && mat != null)
                return mat;
            mat = CreateMaterial(ground, "Ion_Contact_" + ColorUtility.ToHtmlStringRGB(ground));
            if (mat.HasProperty(AoBoostId)) mat.SetFloat(AoBoostId, 1.5f);
            s_ContactCache[key] = mat;
            return mat;
        }

        /// <summary>Hex helper, e.g. Hex("#BFE3F2"). Returns magenta on parse failure.</summary>
        public static Color Hex(string html)
        {
            return ColorUtility.TryParseHtmlString(html, out var c) ? c : Color.magenta;
        }

        /// <summary>The toon shader, or a URP fallback if it is missing from the build.</summary>
        public static Shader ToonShader
        {
            get
            {
                if (s_Shader != null) return s_Shader;
                s_Shader = Shader.Find(ToonShaderName);
                if (s_Shader == null)
                {
                    if (!s_WarnedMissing)
                    {
                        s_WarnedMissing = true;
                        Debug.LogWarning("[Palette] Shader '" + ToonShaderName +
                                         "' not found (add it to Always Included Shaders). Falling back to URP Lit.");
                    }
                    s_Shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (s_Shader == null) s_Shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (s_Shader == null) s_Shader = Shader.Find("Sprites/Default");
                }
                return s_Shader;
            }
        }

        static int Key(Color32 c)
        {
            return (c.r << 24) | (c.g << 16) | (c.b << 8) | c.a;
        }

        static Material CreateMaterial(Color c, string name)
        {
            var mat = new Material(ToonShader) { name = name, enableInstancing = true };
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, c);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, c);
            // Grass (and the contact shade on it) drifts in colour across the ground.
            if (mat.HasProperty(GroundVarId) && (SameColor(c, Grass) || SameColor(c, ContactShade)))
                mat.SetFloat(GroundVarId, 1f);
            return mat;
        }
    }

    // ====================================================================== Light Table roles (art bible §3.1)

    public static partial class Palette
    {
        /// <summary>Lit albedo of every <see cref="Mat"/> (sRGB hex from the art bible, indexed by the enum value).</summary>
        static readonly Color32[] s_MatColors =
        {
            new Color32(0xEF, 0xEB, 0xE3, 0xFF), // Paper
            new Color32(0xE4, 0xDD, 0xD0, 0xFF), // Plaster
            new Color32(0xD7, 0xCD, 0xBB, 0xFF), // Limestone
            new Color32(0xBD, 0xB8, 0xAE, 0xFF), // Concrete
            new Color32(0xC4, 0x77, 0x55, 0xFF), // Terracotta
            new Color32(0xE8, 0xCB, 0xC4, 0xFF), // Rose (lifted towards Paper: a tint, not a pink zone)
            new Color32(0xCA, 0xDD, 0xD3, 0xFF), // Mint (lifted towards Paper)
            new Color32(0x2F, 0x5B, 0x88, 0xFF), // Cyanotype
            new Color32(0x38, 0x3D, 0x47, 0xFF), // Graphite
            new Color32(0xC5, 0x9A, 0x45, 0xFF), // Brass
            new Color32(0x7A, 0x4E, 0x33, 0xFF), // Walnut
            new Color32(0xC8, 0xA2, 0x73, 0xFF), // Oak
            new Color32(0x5C, 0x8A, 0x48, 0xFF), // Foliage
            new Color32(0x94, 0xB8, 0x66, 0xFF), // FoliageLight
            new Color32(0x3D, 0x64, 0x37, 0xFF), // FoliageDark
            new Color32(0x9E, 0x86, 0xB4, 0xFF), // Lilac
            new Color32(0x84, 0xAC, 0x5C, 0xFF), // Lawn
            new Color32(0xB2, 0x4A, 0x34, 0xFF), // TextileRed
            new Color32(0xD6, 0xA4, 0x3B, 0xFF), // Mustard
            new Color32(0x3C, 0x8C, 0x88, 0xFF), // Teal
            new Color32(0xF5, 0xF8, 0xF8, 0xFF), // Frost
            new Color32(0x9F, 0xE3, 0xFF, 0xFF), // Ion
            new Color32(0xE9, 0x85, 0x3B, 0xFF), // Safelight
            new Color32(0xFF, 0xD9, 0xA0, 0xFF), // Warm
        };

        /// <summary>Material name prefix of the role materials (also used to recognise their clones).</summary>
        public const string MatPrefix = "Ion_Mat_";

        static Material[] s_MatCache;
        static readonly Dictionary<Material, Mat> s_MatOf = new Dictionary<Material, Mat>();

        static readonly int PatternStrengthId = Shader.PropertyToID("_PatternStrength");
        static readonly int FaceJitterId = Shader.PropertyToID("_FaceJitter");
        static readonly int SwayFromColorMatId = Shader.PropertyToID("_SwayFromColor");

        /// <summary>Number of <see cref="Mat"/> values.</summary>
        public static int MatCount => s_MatColors.Length;

        /// <summary>Lit albedo (sRGB) of a material role.</summary>
        public static Color ColorOf(Mat mat)
        {
            int i = (int)mat;
            return i >= 0 && i < s_MatColors.Length ? (Color)s_MatColors[i] : Color.magenta;
        }

        /// <summary>Emission strength of a role (0 for non-emissive roles): Frost 0.35, Ion 0.8, Safelight 0.6, Warm 0.7.</summary>
        public static float EmissionOf(Mat mat)
        {
            switch (mat)
            {
                case Mat.Frost: return 0.35f;
                case Mat.Ion: return 0.8f;
                case Mat.Safelight: return 0.6f;
                case Mat.Warm: return 0.7f;
                default: return 0f;
            }
        }

        /// <summary>True for the plant roles whose shared material sways by vertex alpha (merged decor / Arch.Bake).</summary>
        /// <summary>FlatToon _SelfLit level of an emissive role (its colour times this, in sun; ×0.86 in shade).</summary>
        public static float SelfLitLevel(Mat mat)
        {
            switch (mat)
            {
                case Mat.Frost: return 0.82f;
                case Mat.Ion: return 1f;
                case Mat.Safelight: return 1f;
                case Mat.Warm: return 1f;
                default: return 0f;
            }
        }

        public static bool IsFoliage(Mat mat) =>
            mat == Mat.Foliage || mat == Mat.FoliageLight || mat == Mat.FoliageDark || mat == Mat.Lilac;

        /// <summary>
        /// The ONE shared Ion/FlatToon material of a role (cached). Emissive for Frost / Ion / Safelight / Warm,
        /// grass drift (<c>_GroundVar</c>) for Lawn, sway-from-vertex-alpha for foliage (amount set by Ambience).
        /// Do not modify the returned material.
        /// </summary>
        public static Material Get(Mat mat)
        {
            int i = (int)mat;
            if (i < 0 || i >= s_MatColors.Length) i = 0;
            if (s_MatCache == null || s_MatCache.Length != s_MatColors.Length) s_MatCache = new Material[s_MatColors.Length];
            Material m = s_MatCache[i];
            if (m != null) return m;

            Mat role = (Mat)i;
            Color c = s_MatColors[i];
            m = CreateMaterial(c, MatPrefix + role);
            float e = EmissionOf(role);
            if (e > 0f && m.HasProperty(EmissionId)) m.SetColor(EmissionId, c * e);
            // Self-lit display level (FlatToon _SelfLit): Frost stays below ~0.9 display value so light-table tops,
            // slides and screens read as lit glass, not as a missing texture.
            if (e > 0f && m.HasProperty(SelfLitId)) m.SetFloat(SelfLitId, SelfLitLevel(role));
            // Ultra (HDR + bloom): interaction ion, lamps and safelights go past 1 and bloom; Frost never does.
            if (m.HasProperty(GlowBoostId)) m.SetFloat(GlowBoostId, role == Mat.Ion || role == Mat.Warm || role == Mat.Safelight ? 1f : 0f);
            if (m.HasProperty(GroundVarId)) m.SetFloat(GroundVarId, role == Mat.Lawn ? 1f : 0f);
            if (m.HasProperty(PatternStrengthId)) m.SetFloat(PatternStrengthId, 1f);
            if (IsFoliage(role))
            {
                // Plants: a little more per-face variety; sway weight comes from vertex alpha (0 at the base).
                if (m.HasProperty(FaceJitterId)) m.SetFloat(FaceJitterId, 0.06f);
                if (m.HasProperty(SwayFromColorMatId)) m.SetFloat(SwayFromColorMatId, 1f);
            }
            else if (role == Mat.Graphite || role == Mat.Brass || e > 0f)
            {
                // Metal and lamps read as one clean tone per face.
                if (m.HasProperty(FaceJitterId)) m.SetFloat(FaceJitterId, 0.015f);
            }
            s_MatCache[i] = m;
            s_MatOf[m] = role;
            return m;
        }

        /// <summary>Shared material of a surface (the pattern is per vertex, so it does not change the material).</summary>
        public static Material Get(Surf surf) => Get(surf.Mat);

        static Material[] s_UltraCache;
        static readonly HashSet<Material> s_UltraSet = new HashSet<Material>();
        static readonly int UltraOnlyId = Shader.PropertyToID("_UltraOnly");

        /// <summary>
        /// The Ultra-only twin of a role's material (cached): the same look with FlatToon <c>_UltraOnly</c> = 1, so its
        /// geometry collapses in the vertex shader on every tier but Ultra. Used for Ultra detail (Arch.UltraOnly
        /// scopes): tile bevels, trims, mullions, hardware, extra plants. Pieces cut from it keep the material, so
        /// a slice of Ultra detail stays Ultra-only too.
        /// </summary>
        public static Material GetUltra(Mat mat)
        {
            int i = (int)mat;
            if (i < 0 || i >= s_MatColors.Length) i = 0;
            if (s_UltraCache == null || s_UltraCache.Length != s_MatColors.Length) s_UltraCache = new Material[s_MatColors.Length];
            Material m = s_UltraCache[i];
            if (m != null) return m;
            Material src = Get((Mat)i);
            m = new Material(src) { name = src.name + "_Ultra" };
            if (m.HasProperty(UltraOnlyId)) m.SetFloat(UltraOnlyId, 1f);
            s_UltraCache[i] = m;
            s_UltraSet.Add(m);
            s_MatOf[m] = (Mat)i;
            return m;
        }

        /// <summary>The Ultra twin of a role if it was ever created (null otherwise).</summary>
        public static Material PeekUltra(Mat mat)
        {
            int i = (int)mat;
            return s_UltraCache != null && i >= 0 && i < s_UltraCache.Length ? s_UltraCache[i] : null;
        }

        /// <summary>True for a material made by <see cref="GetUltra"/>.</summary>
        public static bool IsUltra(Material m) => m != null && s_UltraSet.Contains(m);

        /// <summary>
        /// The role of a material made by <see cref="Get(Mat)"/> (or a clone of one, recognised by its
        /// <see cref="MatPrefix"/> name). False for legacy colour materials. Used by footsteps, debug and tests.
        /// </summary>
        public static bool TryGetMat(Material material, out Mat mat)
        {
            mat = Mat.Paper;
            if (material == null) return false;
            if (s_MatOf.TryGetValue(material, out mat)) return true;
            string name = material.name;
            if (name == null || !name.StartsWith(MatPrefix, System.StringComparison.Ordinal)) return false;
            int start = MatPrefix.Length, end = start;
            while (end < name.Length && char.IsLetter(name[end])) end++;
            if (end == start) return false;
            if (!System.Enum.TryParse(name.Substring(start, end - start), false, out mat)) return false;
            s_MatOf[material] = mat;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetMatStatics()
        {
            // Materials can survive a play-mode restart without a domain reload: rebuild the reverse map.
            s_MatOf.Clear();
            if (s_MatCache != null)
                for (int i = 0; i < s_MatCache.Length; i++)
                    if (s_MatCache[i] != null) s_MatOf[s_MatCache[i]] = (Mat)i;
            s_UltraSet.Clear();
            if (s_UltraCache != null)
                for (int i = 0; i < s_UltraCache.Length; i++)
                    if (s_UltraCache[i] != null) { s_MatOf[s_UltraCache[i]] = (Mat)i; s_UltraSet.Add(s_UltraCache[i]); }
        }
    }
}
