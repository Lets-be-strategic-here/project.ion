using System.Collections.Generic;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Levels.Props
{
    using Ion.Levels.Arch;
    using Arch = Ion.Levels.Arch.Arch;   // art bible §4: the name-lookup trap

    /// <summary>
    /// Shared construction helpers for <see cref="PropKit"/>. Every Arch call PropKit makes goes through here, so
    /// an Arch signature change is fixed in one place.
    ///
    /// Conventions (all props):
    /// - A prop is built in its own frame: +Z is the front (the side a viewer stands on, = facing), +Y up,
    ///   origin at the base. <see cref="Frame"/> creates that frame under the caller's parent.
    /// - Sliceable pieces are plain Arch pieces (Solid or Soft) baked later with the zone.
    /// - Device pieces use <see cref="Device"/> flags (render only, Dynamic) and are merged by
    ///   <see cref="BakeDevice"/> (Arch.BakeLocal) under their Interactable; colliders on devices are primitive
    ///   colliders added by PropKit, never merged mesh colliders, so movers stay cheap.
    /// - Arch pieces never get children: device nodes, labels and print cards are siblings of the pieces.
    /// </summary>
    internal static class PropBuild
    {
        public const float D = 0.0625f;              // detail step
        public const ArchFlags Solid = ArchFlags.Solid;
        public const ArchFlags Soft = ArchFlags.Soft;
        /// <summary>Render-only device piece, merged by BakeLocal (never by the zone Bake).</summary>
        public const ArchFlags Device = ArchFlags.Dynamic;

        // ------------------------------------------------------------------ frames and directions

        /// <summary>Yaw (degrees) that turns +Z to <paramref name="d"/>.</summary>
        public static float Yaw(Dir d)
        {
            switch (d)
            {
                case Dir.PosX: return 90f;
                case Dir.NegZ: return 180f;
                case Dir.NegX: return 270f;
                default: return 0f;
            }
        }

        /// <summary>Unit vector of a plan direction (p-local).</summary>
        public static Vector3 Vec(Dir d)
        {
            switch (d)
            {
                case Dir.PosX: return Vector3.right;
                case Dir.NegX: return Vector3.left;
                case Dir.NegZ: return Vector3.back;
                default: return Vector3.forward;
            }
        }

        /// <summary>A plain group (no ArchPiece) at <paramref name="localPos"/>, turned by <paramref name="yaw"/>.</summary>
        public static Transform Frame(Transform p, string name, Vector3 localPos, float yaw)
        {
            return Frame(p, name, localPos, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>
        /// A plain group frame. It carries <see cref="ArchGroup"/>, so a zone Bake removes it once everything under
        /// it has been merged (frames holding other components, e.g. <see cref="PropLink"/>, stay).
        /// </summary>
        public static Transform Frame(Transform p, string name, Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(p, false);
            t.localPosition = localPos;
            t.localRotation = localRot;
            go.AddComponent<ArchGroup>();
            return t;
        }

        /// <summary>
        /// Pose of a point of frame <paramref name="g"/> expressed under <paramref name="parent"/> (for siblings of a
        /// prop's sliceable group: device nodes, labels, print cards).
        /// </summary>
        public static void PoseIn(Transform parent, Transform g, Vector3 localPos, Quaternion localRot, out Vector3 pos, out Quaternion rot)
        {
            pos = parent.InverseTransformPoint(g.TransformPoint(localPos));
            rot = Quaternion.Inverse(parent.rotation) * g.rotation * localRot;
        }

        /// <summary>
        /// A device root carrying <see cref="Interactable"/>. Order of work for every device:
        /// 1. build its visuals and <see cref="BakeDevice"/> them while it is ACTIVE (Arch.BakeLocal skips inactive
        ///    pieces, so <paramref name="p"/> must be active too);
        /// 2. <see cref="Deactivate"/> it, add and configure the gameplay components (their Awake / OnEnable then see
        ///    the configured fields);
        /// 3. <see cref="Activate"/> it (pickups stay inactive until they have their photo).
        /// </summary>
        public static GameObject DeviceRoot(Transform p, string name, Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(p, false);
            t.localPosition = localPos;
            t.localRotation = localRot;
            go.AddComponent<Interactable>();
            return go;
        }

        public static void Activate(GameObject go)
        {
            if (go != null && !go.activeSelf) go.SetActive(true);
        }

        public static void Deactivate(GameObject go)
        {
            if (go != null && go.activeSelf) go.SetActive(false);
        }

        // ------------------------------------------------------------------ grid (Arch.Validate)

        static bool OnDetail(float v) => Mathf.Abs(v / D - Mathf.Round(v / D)) * D < 1e-3f;

        static bool OnDetail(Vector3 a, Vector3 b) =>
            OnDetail(a.x) && OnDetail(a.y) && OnDetail(a.z) && OnDetail(b.x) && OnDetail(b.y) && OnDetail(b.z);

        /// <summary>
        /// Pieces whose min/max are not on the 0.0625 detail grid (bible dimensions such as 0.04 chair legs, 0.01
        /// inlays, 0.03 plaques) are marked exempt from Arch.Validate's grid check (ArchPiece.Snap = 0).
        /// </summary>
        static GameObject Checked(GameObject piece, Vector3 min, Vector3 max)
        {
            if (piece != null && !OnDetail(min, max) && piece.TryGetComponent(out ArchPiece ap)) ap.Snap = 0f;
            return piece;
        }

        /// <summary>Exempts every Arch piece under <paramref name="g"/> from the grid check (inlays, rugs, soft clutter).</summary>
        public static void Exempt(Transform g)
        {
            if (g == null) return;
            foreach (ArchPiece ap in g.GetComponentsInChildren<ArchPiece>(true)) ap.Snap = 0f;
        }

        // ------------------------------------------------------------------ primitives (thin Arch wrappers)

        public static GameObject Box(Transform g, Vector3 min, Vector3 max, Surf s, ArchFlags f)
        {
            Vector3 lo = Vector3.Min(min, max), hi = Vector3.Max(min, max);
            return Checked(Arch.Box(g, lo, hi, s, f), lo, hi);
        }

        public static GameObject Box(Transform g, float x0, float y0, float z0, float x1, float y1, float z1, Surf s, ArchFlags f)
        {
            return Box(g, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), s, f);
        }

        /// <summary>Box with the shared 0.0625 chamfer on every edge (smaller for small parts; plain box if tiny).</summary>
        public static GameObject Chamfer(Transform g, Vector3 min, Vector3 max, Surf s, ArchFlags f, float chamfer = PropKit.Chamfer)
        {
            Vector3 lo = Vector3.Min(min, max), hi = Vector3.Max(min, max);
            Vector3 size = hi - lo;
            float c = Mathf.Min(chamfer, 0.3f * Mathf.Min(size.x, Mathf.Min(size.y, size.z)));
            if (c < 0.008f) return Checked(Arch.Box(g, lo, hi, s, f), lo, hi);
            return Checked(Arch.ChamferBox(g, lo, hi, c, s, f), lo, hi);
        }

        public static GameObject Chamfer(Transform g, float x0, float y0, float z0, float x1, float y1, float z1, Surf s, ArchFlags f,
                                         float chamfer = PropKit.Chamfer)
        {
            return Chamfer(g, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), s, f, chamfer);
        }

        /// <summary>N-gon prism standing on <paramref name="baseCenter"/> (flats facing ±X/±Z for 4·k sides).</summary>
        public static GameObject Prism(Transform g, Vector3 baseCenter, float radius, float height, int sides, Surf s, ArchFlags f, float yaw = 0f)
        {
            return Arch.Prism(g, baseCenter, radius, height, sides, s, f, yaw);
        }

        public static GameObject Wedge(Transform g, Vector3 min, Vector3 max, Dir rise, Surf s, ArchFlags f)
        {
            Vector3 lo = Vector3.Min(min, max), hi = Vector3.Max(min, max);
            return Checked(Arch.Wedge(g, lo, hi, rise, s, f), lo, hi);
        }

        /// <summary>
        /// A square-section stick from <paramref name="a"/> to <paramref name="b"/> (easel legs, tilted struts):
        /// a box in its own rotated sub-frame.
        /// </summary>
        public static GameObject Beam(Transform g, Vector3 a, Vector3 b, float section, Surf s, ArchFlags f)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return null;
            Quaternion r = Quaternion.LookRotation(d / len, Mathf.Abs(Vector3.Dot(d / len, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
            Transform sub = Frame(g, "Beam", (a + b) * 0.5f, r);
            float h = section * 0.5f;
            return Box(sub, new Vector3(-h, -h, -len * 0.5f), new Vector3(h, h, len * 0.5f), s, f);
        }

        /// <summary>Brass rivet 0.0625³ centred on <paramref name="c"/>.</summary>
        public static GameObject Rivet(Transform g, Vector3 c, ArchFlags f)
        {
            const float h = D * 0.5f;
            return Box(g, c - new Vector3(h, h, h), c + new Vector3(h, h, h), Mat.Brass, f);
        }

        /// <summary>
        /// A brass "[" (or "]" when <paramref name="mirror"/>) clip on a face whose outward normal is +Z, at
        /// z = <paramref name="faceZ"/>: a vertical bar <paramref name="height"/> tall plus top and bottom returns.
        /// The bracket opens toward −X ("[" read by a viewer standing at +Z) unless mirrored.
        /// </summary>
        public static void BracketClip(Transform g, float x, float yBottom, float height, float faceZ, float reach, bool mirror, ArchFlags f,
                                       Mat mat = Mat.Brass, float bar = D, float proud = 0.015625f)
        {
            float s = mirror ? -1f : 1f;      // viewer's left is +X in a +Z-facing frame
            float x0 = x, x1 = x + s * bar;
            float r1 = x + s * bar - s * reach; // returns run toward the opening
            Box(g, x0, yBottom, faceZ, x1, yBottom + height, faceZ + proud, mat, f);
            Box(g, x1, yBottom + height - bar, faceZ, r1, yBottom + height, faceZ + proud, mat, f);
            Box(g, x1, yBottom, faceZ, r1, yBottom + bar, faceZ + proud, mat, f);
        }

        /// <summary>
        /// Bracket surround (§2.2 BracketJamb) on one face of an opening <paramref name="width"/> × <paramref name="height"/>
        /// whose sill centre is the frame origin; the face plane is z = <paramref name="faceZ"/>, outward along
        /// <paramref name="outward"/> (+1 = +Z, −1 = −Z). Jambs 0.25 wide × (h + 0.25), proud 0.0625; head serifs
        /// 0.375 × 0.25 with a bare centre; for doors a brass floor inlay 0.375 × 0.125 × 0.01 at each foot.
        /// </summary>
        public static void BracketSurround(Transform g, float width, float height, float faceZ, float outward, bool door, Surf trim, ArchFlags f)
        {
            const float jamb = 0.25f, proud = D, serifLen = 0.375f, serifH = 0.25f;
            float hw = width * 0.5f;
            float z0 = faceZ, z1 = faceZ + outward * proud;
            for (int side = -1; side <= 1; side += 2)
            {
                float xo = side * (hw + jamb), xi = side * hw;              // outer / inner edge of the jamb
                Box(g, new Vector3(xo, 0f, z0), new Vector3(xi, height + jamb, z1), trim, f);
                // head serif (top of the "[")
                Box(g, new Vector3(xo, height, z0), new Vector3(xo - side * serifLen, height + serifH, z1), trim, f);
                if (door)
                {
                    float zi = faceZ + outward * 0.125f;
                    Box(g, new Vector3(xo, 0f, z0), new Vector3(xo - side * serifLen, 0.01f, zi), Mat.Brass, Soft);
                }
                else
                {
                    Box(g, new Vector3(xo, -serifH, z0), new Vector3(xo - side * serifLen, 0f, z1), trim, f);
                }
            }
        }

        // ------------------------------------------------------------------ devices

        /// <summary>Merges the Arch pieces under <paramref name="go"/> in its local pattern space (Arch.BakeLocal).</summary>
        public static void BakeDevice(GameObject go)
        {
            if (go == null) return;
            Arch.BakeLocal(go);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                // Devices are small: their shadows are not worth a caster each.
                if (r.shadowCastingMode == ShadowCastingMode.On && r.bounds.size.sqrMagnitude < 0.05f)
                    r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>Replaces every material slot of every renderer under <paramref name="go"/>.</summary>
        public static void SetMaterial(GameObject go, Material m)
        {
            if (go == null || m == null) return;
            var rs = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                var mats = rs[i].sharedMaterials;
                bool changed = false;
                for (int k = 0; k < mats.Length; k++)
                {
                    if (mats[k] == m) continue;
                    mats[k] = m;
                    changed = true;
                }
                if (changed) rs[i].sharedMaterials = mats;
            }
        }

        static Material s_BrassGlow;

        /// <summary>"Brass glow" (solved exhibit lamp, switched-on button): Brass with a warm emission.</summary>
        public static Material BrassGlow
        {
            get
            {
                if (s_BrassGlow == null) s_BrassGlow = Palette.GetEmissive(Palette.ColorOf(Mat.Brass), 0.55f);
                return s_BrassGlow;
            }
        }

        public static Material IonMaterial => Palette.Get(Mat.Ion);
        public static Material GraphiteMaterial => Palette.Get(Mat.Graphite);

        /// <summary>Box collider on its own child (devices: cheap primitive colliders that move with their part).</summary>
        public static BoxCollider Collider(Transform parent, Vector3 min, Vector3 max, string name = "Collider")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<BoxCollider>();
            c.center = (min + max) * 0.5f;
            c.size = Vector3.Max(max - min, new Vector3(0.01f, 0.01f, 0.01f));
            return c;
        }

        // ------------------------------------------------------------------ decor (plants)

        /// <summary>
        /// The transform whose space Decor heights (sway / contact shade) are measured in: the root later passed
        /// to Arch.Bake / DecorCombiner. <see cref="PropKit.BakeRoot"/> wins when set; otherwise the zone or
        /// diorama root, which GameBootstrap parents one level under a top-level container ("World", "Dioramas").
        /// A top-level <paramref name="p"/> is its own root.
        /// </summary>
        public static Transform DecorRoot(Transform p)
        {
            if (p == null) return null;
            Transform o = PropKit.BakeRoot;
            if (o != null && (p == o || p.IsChildOf(o))) return o;
            Transform t = p;
            while (t.parent != null && t.parent.parent != null) t = t.parent;
            return t;
        }

        /// <summary>Per-plant decor bake info (heights in <see cref="DecorRoot"/> space).</summary>
        public struct DecorInfo
        {
            public float BaseY, Height, AoMin, Tint;
        }

        /// <summary>
        /// A sliceable, collider-less decor piece (closed convex mesh) tagged for DecorCombiner / Arch.Bake with
        /// the plant's base and height, so contact shade and sway weight (vertex alpha) run from its base.
        /// </summary>
        public static GameObject DecorPiece(Transform parent, string name, Mesh mesh, Vector3 center, Quaternion rot, Vector3 size,
                                            Mat mat, DecorInfo info, bool sway, bool shadows = true)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = center;
            t.localRotation = rot;
            t.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Ion.Levels.Arch.Arch.UltraOnly ? Palette.GetUltra(mat) : Palette.Get(mat);
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.AddComponent<Sliceable>();
            var d = go.AddComponent<Decor>();
            d.BaseY = info.BaseY;
            d.Height = Mathf.Max(0.05f, info.Height);
            d.AoMin = info.AoMin;
            d.Tint = info.Tint;
            d.SwayWeight = sway;
            return go;
        }

        // ------------------------------------------------------------------ text

        /// <summary>
        /// Title-block world text: legacy uGUI on a world canvas (via <see cref="WorldText"/>), rendered at 400 px/m
        /// so small plaque type stays crisp. <paramref name="localRot"/> is the label's rotation; the text reads
        /// correctly for a viewer looking along the label's +Z.
        /// </summary>
        public static GameObject Label(Transform parent, Vector3 localPos, Quaternion localRot, string text, float capHeight,
                                       float width, Color color)
        {
            const float ppm = 400f;
            int fontSize = Mathf.Max(8, Mathf.RoundToInt(capHeight * 1.4f * ppm));
            GameObject go = WorldText.Create(parent, localPos, localRot, text, fontSize, color, width);
            var rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one / ppm;
            rt.sizeDelta = new Vector2(width * ppm, fontSize * 1.6f);
            return go;
        }

        // ------------------------------------------------------------------ seeded random

        public sealed class Rng
        {
            readonly System.Random _r;
            public Rng(int seed) { _r = new System.Random(seed * 7919 + 17); }
            public float Value => (float)_r.NextDouble();
            public float Range(float a, float b) => a + (b - a) * (float)_r.NextDouble();
            public int Range(int a, int bExclusive) => _r.Next(a, bExclusive);
        }

        /// <summary>A stable seed from a position (so a zone and its diorama copy draw the same plant).</summary>
        public static int SeedOf(Vector3 v, int salt)
        {
            unchecked
            {
                int h = salt * 486187739;
                h = h * 31 + Mathf.RoundToInt(v.x * 16f);
                h = h * 31 + Mathf.RoundToInt(v.y * 16f);
                h = h * 31 + Mathf.RoundToInt(v.z * 16f);
                return h & 0x7fffffff;
            }
        }

        internal static readonly List<Renderer> s_Renderers = new List<Renderer>(16);
    }
}
