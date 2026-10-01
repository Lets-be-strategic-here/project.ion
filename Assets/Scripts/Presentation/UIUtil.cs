using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>Small helpers for building uGUI hierarchies from code.</summary>
    public static class UIUtil
    {
        public const int UILayer = 9; // "PhotoUI"
        public const string PhotoShaderName = "Ion/PhotoDisplay";

        static Font s_Font;
        static Sprite s_Rounded;
        static Sprite s_Circle;
        static Material s_PhotoMaterial;
        static bool s_PhotoMaterialTried;

        public static Font Font
        {
            get
            {
                if (s_Font == null)
                    s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return s_Font;
            }
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
            t.font = Font;
            t.text = content;
            t.fontSize = size;
            t.fontStyle = style;
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
    }
}
