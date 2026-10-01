using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>Small helpers for building uGUI hierarchies from code.</summary>
    public static class UIUtil
    {
        public const int UILayer = 9; // "PhotoUI"
        public const string PhotoShaderName = "Ion/PhotoDisplay";

        static Font s_Font, s_BoldFont;
        static Sprite s_Rounded;
        static Sprite s_Circle;
        static Material s_PhotoMaterial;
        static bool s_PhotoMaterialTried;

        /// <summary>
        /// UI font: Nunito SemiBold (OFL, Resources/Fonts), the rounded sans the web loader uses, so the
        /// in-game UI matches the page. Falls back to the built-in LegacyRuntime font.
        /// </summary>
        public static Font Font
        {
            get
            {
                if (s_Font == null) s_Font = Resources.Load<Font>("Fonts/Nunito-SemiBold");
                if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return s_Font;
            }
        }

        /// <summary>Heavy weight (Nunito ExtraBold) used for FontStyle.Bold text instead of faux bold.</summary>
        public static Font BoldFont
        {
            get
            {
                if (s_BoldFont == null) s_BoldFont = Resources.Load<Font>("Fonts/Nunito-ExtraBold");
                if (s_BoldFont == null) s_BoldFont = Font;
                return s_BoldFont;
            }
        }

        /// <summary>Sets the font + style pair: bold text uses the real heavy face (no synthetic emboldening).</summary>
        public static void SetFontStyle(Text t, FontStyle style)
        {
            bool bold = style == FontStyle.Bold || style == FontStyle.BoldAndItalic;
            Font heavy = BoldFont;
            bool realBold = bold && heavy != null && heavy != Font;
            t.font = realBold ? heavy : Font;
            t.fontStyle = realBold
                ? (style == FontStyle.BoldAndItalic ? FontStyle.Italic : FontStyle.Normal)
                : style;
        }

        /// <summary>Shared Polaroid-look material for RawImages (null -> default UI material).</summary>
        public static Material PhotoMaterial
        {
            get
            {
                if (s_PhotoMaterial == null && !s_PhotoMaterialTried)
                {
                    s_PhotoMaterialTried = true;
                    var sh = Shader.Find(PhotoShaderName);
                    if (sh != null) s_PhotoMaterial = new Material(sh) { name = "Ion_PhotoDisplay" };
                    else Debug.LogWarning("[UI] Shader '" + PhotoShaderName + "' not found; photos use the default UI shader.");
                }
                return s_PhotoMaterial;
            }
        }

        /// <summary>9-sliceable rounded rectangle sprite (radius ~12px).</summary>
        public static Sprite RoundedSprite
        {
            get
            {
                if (s_Rounded == null) s_Rounded = MakeRounded(48, 14);
                return s_Rounded;
            }
        }

        /// <summary>Anti-aliased filled circle sprite.</summary>
        public static Sprite CircleSprite
        {
            get
            {
                if (s_Circle == null) s_Circle = MakeRounded(32, 16);
                return s_Circle;
            }
        }

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Ion_Rounded" + radius,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color32[size * size];
            float r = radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float cx = Mathf.Clamp(fx, r, size - r);
                    float cy = Mathf.Clamp(fy, r, size - r);
                    float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = Mathf.Min(radius, size / 2 - 1);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UILayer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        public static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public static Image NewImage(string name, Transform parent, Color color, Sprite sprite = null, bool sliced = false)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            }
            return img;
        }

        public static RawImage NewRaw(string name, Transform parent)
        {
            var rt = NewRect(name, parent);
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.material = PhotoMaterial;
            return raw;
        }

        public static Text NewText(string name, Transform parent, string content, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal, bool shadow = true)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            SetFontStyle(t, style);
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            if (shadow)
            {
                var s = rt.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0.17f, 0.18f, 0.21f, 0.55f);
                s.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return t;
        }

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        /// <summary>
        /// Prompt key glyph in the bible's bracket style (§9.2): <c>Key("SHIFT")</c> → "[SHIFT]" with the
        /// brackets dimmed, so "[SHIFT] hold up photo" reads as a key cap followed by its action.
        /// </summary>
        public static string Key(string key) => "<color=#9FE3FFB3>[</color>" + key + "<color=#9FE3FFB3>]</color>";

        /// <summary>Same as <see cref="Key"/> for text on light (Paper) cards: Brass brackets.</summary>
        public static string KeyOnLight(string key) => "<color=#C59A45>[</color>" + key + "<color=#C59A45>]</color>";

        static readonly Vector3[] s_Corners = new Vector3[4];

        /// <summary>Screen-pixel rect (origin bottom-left) of a RectTransform on a Screen Space Overlay canvas.</summary>
        public static Rect ScreenRect(RectTransform rt)
        {
            rt.GetWorldCorners(s_Corners);
            Vector2 min = s_Corners[0], max = s_Corners[0];
            for (int i = 1; i < 4; i++)
            {
                min = Vector2.Min(min, s_Corners[i]);
                max = Vector2.Max(max, s_Corners[i]);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }

    /// <summary>
    /// UI colours, taken from the art bible palette (§3.1) so the HUD speaks the world's language:
    /// Paper cards, Graphite ink (never black), Ion for anything you can use, Brass for marks and the
    /// selected state, Cyanotype for rewind. Independent of Ion.Presentation.Palette (materials).
    /// </summary>
    public static class UIPalette
    {
        public static readonly Color Paper = new Color32(0xEF, 0xEB, 0xE3, 0xFF);
        public static readonly Color Plaster = new Color32(0xE4, 0xDD, 0xD0, 0xFF);
        public static readonly Color Limestone = new Color32(0xD7, 0xCD, 0xBB, 0xFF);
        public static readonly Color Graphite = new Color32(0x38, 0x3D, 0x47, 0xFF);   // the darkest value
        public static readonly Color GraphiteSoft = new Color32(0x5B, 0x61, 0x6C, 0xFF);
        public static readonly Color Ion = new Color32(0x9F, 0xE3, 0xFF, 0xFF);
        public static readonly Color Brass = new Color32(0xC5, 0x9A, 0x45, 0xFF);
        public static readonly Color Cyanotype = new Color32(0x2F, 0x5B, 0x88, 0xFF);
        public static readonly Color Frost = new Color32(0xF5, 0xF8, 0xF8, 0xFF);
        public static readonly Color TextileRed = new Color32(0xB2, 0x4A, 0x34, 0xFF);

        /// <summary>Dark translucent card / pill behind light text.</summary>
        public static Color Card(float alpha = 0.86f) => UIUtil.WithAlpha(Graphite, alpha);
        /// <summary>Light text on dark cards.</summary>
        public static Color TextLight => Paper;
        /// <summary>Dark text on light cards (Graphite, never black).</summary>
        public static Color TextDark => Graphite;
    }
}
