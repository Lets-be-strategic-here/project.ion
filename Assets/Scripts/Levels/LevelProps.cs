using Ion.Gameplay;
using Ion.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Ion.Levels
{
    /// <summary>Spins (and optionally bobs) its transform. Visual only.</summary>
    public sealed class Spin : MonoBehaviour
    {
        public float DegreesPerSecond = 45f;
        public float BobHeight;
        public float BobSpeed = 2f;

        Vector3 _basePos;
        float _phase;

        void Awake()
        {
            _basePos = transform.localPosition;
            _phase = Random.value * 6.28f;
        }

        void Update()
        {
            float t = Time.time;
            transform.localRotation = Quaternion.Euler(0f, t * DegreesPerSecond + _phase * 57.3f, 0f);
            if (BobHeight > 0f)
                transform.localPosition = _basePos + new Vector3(0f, Mathf.Sin(t * BobSpeed + _phase) * BobHeight, 0f);
        }
    }

    /// <summary>
    /// Toasts <see cref="Text"/> when the player's feet enter a horizontal radius around this object
    /// (and are within 2.5 m vertically). Fires again only after the player has left.
    /// Uses a distance poll, so it needs no collider and survives being cut by a photo.
    /// </summary>
    public sealed class HintZone : MonoBehaviour
    {
        public string Text;
        public float Radius = 1.4f;
        public float Seconds = 4.5f;
        public bool Once;

        bool _inside;
        bool _fired;

        void Update()
        {
            var player = FirstPersonController.Current;
            if (player == null) return;
            Vector3 d = player.transform.position - transform.position;
            bool inside = d.y > -1f && d.y < 2.5f && (d.x * d.x + d.z * d.z) <= Radius * Radius;
            if (inside && !_inside && !(Once && _fired))
            {
                _fired = true;
                RoomContext.Toast(Text, Seconds);
            }
            _inside = inside;
        }
    }

    /// <summary>A floating instant camera; walking into it unlocks the player's camera.</summary>
    public sealed class CameraPickup : MonoBehaviour
    {
        public int Film = 3;
        public float Radius = 1.2f;
        public GameObject Visual;

        bool _taken;

        void Update()
        {
            if (_taken) return;
            var player = FirstPersonController.Current;
            if (player == null) return;
            Vector3 d = player.transform.position - transform.position;
            if (d.y < -1f || d.y > 2.5f || d.x * d.x + d.z * d.z > Radius * Radius) return;

            _taken = true;
            RoomContext.UnlockInstantCamera(Film);
            RoomContext.Toast("Instant camera!  [C] to take it out, hold RMB to aim, LMB to snap  (" + Film + " film)", 6f);
            if (Visual != null) Visual.SetActive(false);
            enabled = false;
        }
    }

    /// <summary>World-space legacy uGUI text (renders fine in URP, no TMP assets needed).</summary>
    public static class WorldText
    {
        static Font s_Font;

        public static Font Font
        {
            get
            {
                if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return s_Font;
            }
        }

        /// <summary>
        /// Creates a text label. The text reads correctly when viewed looking along the label's +Z
        /// (i.e. the canvas forward points away from the reader). <paramref name="width"/> is in metres.
        /// </summary>
        public static GameObject Create(Transform parent, Vector3 localPos, Quaternion localRotation, string text,
                                        int fontSize, Color color, float width)
        {
            const float pixelsPerMetre = 100f;
            var go = new GameObject("Label", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.localPosition = localPos;
            rt.localRotation = localRotation;
            rt.localScale = Vector3.one / pixelsPerMetre;
            rt.sizeDelta = new Vector2(width * pixelsPerMetre, fontSize * 2.6f);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var labelGo = new GameObject("Text", typeof(RectTransform));
            var lrt = (RectTransform)labelGo.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            var t = labelGo.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = color;
            t.raycastTarget = false;
            return go;
        }
    }

    /// <summary>Shared colours for level art (palette-derived).</summary>
    public static class LevelColors
    {
        public static readonly Color Grass = Palette.Grass;
        public static readonly Color Dirt = Palette.Sand;
        public static readonly Color Rock = Palette.Slate;
        public static readonly Color Leaves = Color.Lerp(Palette.Mint, Palette.Grass, 0.5f);
        public static readonly Color LeavesDark = Color.Lerp(Palette.Grass, Palette.Slate, 0.25f);
        public static readonly Color Trunk = Palette.DarkWood;
        public static readonly Color Wall = Palette.Cream;
        public static readonly Color Trim = Palette.Coral;
        public static readonly Color Bridge = Palette.Wood;
        public static readonly Color Stairs = Palette.Coral;
        public static readonly Color MarkerPhoto = Palette.Coral;
        public static readonly Color MarkerCamera = Palette.Lavender;
        public static readonly Color Cloud = Palette.White;
    }
}
