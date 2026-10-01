using System.Collections.Generic;
using UnityEngine;

namespace Ion.Presentation
{
    /// <summary>
    /// Pastel colour palette and a cache of one shared Ion/FlatToon material per colour.
    /// NOTE: Shader.Find only works in a player build if "Ion/FlatToon" is included
    /// (GraphicsSettings > Always Included Shaders, or referenced by an asset in the build).
    /// </summary>
    public static class Palette
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
            s_EmissiveCache[key] = mat;
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
}
