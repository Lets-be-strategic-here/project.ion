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
    /// Teleporter shimmer, in the Light Table language: three thin octagonal Ion rings (the disc's own 8-gon)
    /// that rise from the pad and narrow away, and a few Ion / Frost motes drifting straight up, like dust in a
    /// projector beam. Plain transforms on the shared role materials (SRP-batched, no particle system, no
    /// shadows); animated only while the player is within <see cref="ActiveRange"/>. No spin: the architecture
    /// is rigid, and so is its light.
    /// </summary>
    public sealed class TeleporterFx : MonoBehaviour
    {
        const int RingCount = 3, MoteCount = 10;
        const float Height = 2.25f, ActiveRange = 45f;
        const string RingsName = "ShimmerRings", MotesName = "Motes";

        static Mesh s_Ring;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Ring = null;

        // Two draw calls per teleporter (was 13): the rings and the motes are each one dynamic mesh whose
        // vertices are rewritten per frame from a template (uniform xz / xyz scales keep the normals valid).
        Mesh _rings, _motes;
        Vector3[] _ringTemplate, _ringVerts, _cubeTemplate, _moteVerts;
        readonly float[] _seeds = new float[MoteCount];

        void Awake()
        {
            if (s_Ring == null) s_Ring = PhotoPickup.BuildRing(0.6f, 0.66f, 8);
            _ringTemplate = s_Ring.vertices;
            _cubeTemplate = Geo.CubeMesh.vertices;

            _rings = BuildCombined("Ion_TeleporterRings", s_Ring, RingCount, Quaternion.Euler(0f, 22.5f, 0f), out _ringVerts);
            _motes = BuildCombined("Ion_TeleporterMotes", Geo.CubeMesh, MoteCount, Quaternion.identity, out _moteVerts);
            // The pieces are fixed at the pad's origin (the rotation is baked into the template copy above).
            for (int i = 0; i < _ringTemplate.Length; i++) _ringTemplate[i] = Quaternion.Euler(0f, 22.5f, 0f) * _ringTemplate[i];

            // A clone (a photo of the teleporter pasted elsewhere) already carries the pieces: reuse the objects
            // but give them this instance's own meshes. Old 13-piece clones are tidied up.
            var doomed = new System.Collections.Generic.List<GameObject>();
            Transform ringsT = null, motesT = null;
            foreach (Transform child in transform)
            {
                if (child.name == RingsName && ringsT == null) ringsT = child;
                else if (child.name == MotesName && motesT == null) motesT = child;
                else if (child.name == "ShimmerRing" || child.name == "Mote") doomed.Add(child.gameObject);
            }
            foreach (GameObject go in doomed) Destroy(go);
            Attach(ref ringsT, RingsName, _rings, Palette.Get(Mat.Ion));
            Attach(ref motesT, MotesName, _motes, Palette.Get(Mat.Ion));

            var rng = new System.Random(GetInstanceID());
            for (int i = 0; i < MoteCount; i++) _seeds[i] = (float)rng.NextDouble();
            Animate(0f);
        }

        void OnDestroy()
        {
            if (_rings != null) Destroy(_rings);
            if (_motes != null) Destroy(_motes);
        }

        void Attach(ref Transform t, string name, Mesh mesh, Material m)
        {
            if (t == null)
            {
                var go = new GameObject(name);
                t = go.transform;
                t.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
            }
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            if (!t.TryGetComponent(out MeshFilter mf)) mf = t.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            if (!t.TryGetComponent(out MeshRenderer r)) r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static Mesh BuildCombined(string name, Mesh src, int copies, Quaternion rot, out Vector3[] verts)
        {
            Vector3[] v = src.vertices, n = src.normals;
            int[] t = src.triangles;
            verts = new Vector3[v.Length * copies];
            var normals = new Vector3[verts.Length];
            var tris = new int[t.Length * copies];
            for (int c = 0; c < copies; c++)
            {
                for (int i = 0; i < v.Length; i++) normals[c * v.Length + i] = n.Length == v.Length ? rot * n[i] : Vector3.up;
                for (int i = 0; i < t.Length; i++) tris[c * t.Length + i] = t[i] + c * v.Length;
            }
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.triangles = tris;
            // Fixed bounds around the beam (no per-frame recalculation).
            mesh.bounds = new Bounds(new Vector3(0f, Height * 0.5f + 0.3f, 0f), new Vector3(1.6f, Height + 1f, 1.6f));
            return mesh;
        }

        void Update()
        {
            var player = FirstPersonController.Current;
            if (player != null && (player.transform.position - transform.position).sqrMagnitude > ActiveRange * ActiveRange) return;
            Animate(Time.time);
        }

        void Animate(float time)
        {
            int rn = _ringTemplate.Length;
            for (int i = 0; i < RingCount; i++)
            {
                float p = Mathf.Repeat(time * 0.36f + i / (float)RingCount, 1f);
                float fade = Mathf.Clamp01(p / 0.12f) * Mathf.Clamp01((1f - p) / 0.3f);
                float s = Mathf.Lerp(1f, 0.55f, p) * Mathf.Lerp(0.9f, 1f, fade) * fade;
                float y = 0.27f + p * Height;
                for (int k = 0; k < rn; k++)
                {
                    Vector3 q = _ringTemplate[k];
                    _ringVerts[i * rn + k] = new Vector3(q.x * s, q.y + y, q.z * s);
                }
            }
            _rings.vertices = _ringVerts;

            int cn = _cubeTemplate.Length;
            for (int i = 0; i < MoteCount; i++)
            {
                float seed = _seeds[i];
                float p = Mathf.Repeat(time * (0.16f + seed * 0.1f) + seed, 1f);
                // Seeded spots on a 4 x 4 grid over the disc, drifting a little as they rise.
                int cell = Mathf.FloorToInt(seed * 16f);
                float gx = ((cell & 3) - 1.5f) * 0.28f, gz = ((cell >> 2) - 1.5f) * 0.28f;
                float drift = Mathf.Sin(time * 0.7f + seed * 9f) * 0.04f;
                var c = new Vector3(gx + drift, 0.3f + p * (Height + 0.2f), gz - drift);
                float size = 0.045f * Mathf.Sin(p * Mathf.PI);
                for (int k = 0; k < cn; k++) _moteVerts[i * cn + k] = c + _cubeTemplate[k] * size;
            }
            _motes.vertices = _moteVerts;
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

        /// <summary>Re-arms a one-shot hint (game restart).</summary>
        public void ResetZone()
        {
            _fired = false;
            _inside = false;
        }

        void Update()
        {
            var player = FirstPersonController.Current;
            if (player == null) return;
            Vector3 d = player.transform.position - transform.position;
            bool inside = d.y > -1f && d.y < 2.5f && (d.x * d.x + d.z * d.z) <= Radius * Radius;
            // While the room 1 tutorial runs, its line is the only instruction on screen.
            if (inside && !_inside && !(Once && _fired) && !Onboarding.IsGuiding)
            {
                _fired = true;
                RoomContext.Toast(Text, Seconds);
            }
            _inside = inside;
        }
    }

    // CameraPickup moved to Ion.Gameplay (Lead D, art bible §11); its stand visual is PropKit.CameraStand.

    /// <summary>World-space legacy uGUI text (renders fine in URP, no TMP assets needed).</summary>
    public static class WorldText
    {
        /// <summary>Same heavy face as the HUD (Nunito ExtraBold, LegacyRuntime fallback).</summary>
        public static Font Font => Ion.Presentation.UIUtil.BoldFont;

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
            Ion.Presentation.UIUtil.SetFontStyle(t, FontStyle.Bold);
            t.text = text;
            t.fontSize = fontSize;
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
        /// <summary>Overhanging grass lip on island edges: a touch deeper than the top.</summary>
        public static readonly Color GrassLip = new Color32(0x8F, 0xC2, 0x70, 0xFF);
        /// <summary>Island / ledge sides: warm clay-sand.</summary>
        public static readonly Color Dirt = new Color32(0xEC, 0xD3, 0xAE, 0xFF);
        public static readonly Color DirtBand = new Color32(0xDF, 0xBC, 0x95, 0xFF);
        /// <summary>Island undersides (core + spikes): dusty mauve rock.</summary>
        public static readonly Color Rock = new Color32(0xA6, 0x92, 0xA2, 0xFF);
        public static readonly Color RockDark = new Color32(0x8E, 0x7D, 0x92, 0xFF);
        /// <summary>Boulders on the grass: warm lilac-grey.</summary>
        public static readonly Color Boulder = new Color32(0xC3, 0xBA, 0xC6, 0xFF);
        public static readonly Color BoulderDark = new Color32(0xA9, 0xA0, 0xB2, 0xFF);
        public static readonly Color Leaves = Color.Lerp(Palette.Mint, Palette.Grass, 0.5f);
        public static readonly Color LeavesDark = new Color32(0x88, 0xB9, 0x7E, 0xFF);
        public static readonly Color Trunk = Palette.DarkWood;
        public static readonly Color TuftLight = new Color32(0xBA, 0xDD, 0x8E, 0xFF);
        public static readonly Color TuftDark = new Color32(0x8E, 0xC2, 0x70, 0xFF);
        /// <summary>Contact shade under props (slightly darker, cooler grass).</summary>
        public static readonly Color Contact = Palette.Grass; // radial gradient baked in Geo.DiscMesh
        public static readonly Color PathStone = new Color32(0xDE, 0xD6, 0xC6, 0xFF);
        /// <summary>Wall pilasters / piers: dusty lilac stone and a deeper cream.</summary>
        public static readonly Color Pier = new Color32(0xB9, 0xAC, 0xC1, 0xFF);
        public static readonly Color WallTrim = new Color32(0xFB, 0xEE, 0xD8, 0xFF);
        public static readonly Color Wall = Palette.Cream;
        public static readonly Color Trim = Palette.Coral;
        public static readonly Color Bridge = Palette.Wood;
        public static readonly Color Stairs = Palette.Coral;
        public static readonly Color MarkerPhoto = Palette.Coral;
        public static readonly Color MarkerCamera = Palette.Lavender;
        public static readonly Color Cloud = Palette.White;
        /// <summary>Flower bloom colours.</summary>
        public static readonly Color[] Blooms =
        {
            Palette.Butter, Palette.Coral, Palette.Lavender, Palette.White, new Color32(0xF7, 0xB7, 0xC8, 0xFF),
        };
    }
}
