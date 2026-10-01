using System.Collections.Generic;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Levels.Arch
{
    /// <summary>
    /// The "Light Table" architecture kit (art bible §2, §4). Every method returns the root of a group whose
    /// children are closed convex boxes, wedges or prisms built on Geo's shared unit meshes, each tagged with an
    /// <see cref="ArchPiece"/> (its <see cref="Surf"/> and <see cref="ArchFlags"/>). Children are Solid by default
    /// (MeshCollider + Sliceable), so an unbaked build already plays. Positions are local to <c>p</c>; min/max are
    /// given, never centre + size, and architecture snaps to 0.25 (checked by <see cref="Validate"/>, never fixed).
    /// Call <see cref="Bake"/> once per zone / diorama root after building (merges per material and chunk and
    /// writes the pattern space), and <see cref="BakeLocal"/> on movers and devices.
    ///
    /// Conventions: walls run along their centre line from → to; their FRONT face is on the right-hand side when
    /// walking from → to (Cross(up, to - from)). Niches are recessed from the front face.
    /// </summary>
    public static partial class Arch
    {
        public const float M = 1f, Sub = 0.25f, Detail = 0.0625f;
        public const float Storey = 4f, WallThickness = 0.5f, SlabThickness = 0.5f, HeadY = 3f;
        public const float DoorWidth = 2f, DoorHeight = 3f, Rise = 0.25f, Tread = 0.5f, RailHeight = 1f;

        /// <summary>Additive: the T2 small door (1.5 x 2.5) and a few §2.1 constants.</summary>
        public const float SmallDoorWidth = 1.5f, SmallDoorHeight = 2.5f, PartitionThickness = 0.25f, BarrierHeight = 1.5f;

        static ArchStyle s_Style;

        /// <summary>Surfaces used when a method gets no explicit Surf. Set at the start of Room.Build (null = LightTable).</summary>
        public static ArchStyle Style
        {
            get => s_Style ?? (s_Style = ArchStyle.LightTable);
            set => s_Style = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Style = null;
            s_UltraDepth = 0;
        }

        static int s_UltraDepth;

        /// <summary>
        /// True inside an <see cref="Ultra"/> scope: pieces made now take the Ultra-only twin of their material
        /// (Palette.GetUltra), so they bake into their own per-material batches and only draw on the Ultra tier.
        /// </summary>
        public static bool UltraOnly => s_UltraDepth > 0;

        /// <summary>
        /// Ultra-only detail scope: <c>using (Arch.Ultra()) { ... }</c>. Use it only for walk-through Soft detail
        /// (no colliders): trims, bevels, mullions, hardware, extra plants. Pieces stay sliceable.
        /// </summary>
        public static UltraScope Ultra() => new UltraScope(true);

        public readonly struct UltraScope : System.IDisposable
        {
            readonly bool _on;
            internal UltraScope(bool on) { _on = on; if (on) s_UltraDepth++; }
            public void Dispose() { if (_on && s_UltraDepth > 0) s_UltraDepth--; }
        }

        // ================================================================================== primitives

        /// <summary>Empty grouping transform (removed by Bake when it ends up empty).</summary>
        public static GameObject Group(Transform p, string name, Vector3 localPos = default, Dir facing = Dir.PosZ)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? "Group" : name);
            Transform t = go.transform;
            t.SetParent(p, false);
            t.localPosition = localPos;
            t.localRotation = facing.Rotation();
            go.AddComponent<ArchGroup>();
            return go;
        }

        /// <summary>Axis-aligned box from <paramref name="min"/> to <paramref name="max"/>.</summary>
        public static GameObject Box(Transform p, Vector3 min, Vector3 max, Surf s, ArchFlags f = ArchFlags.Solid)
        {
            Order(ref min, ref max);
            return Piece(p, "Box", Geo.CubeMesh, (min + max) * 0.5f, Quaternion.identity, max - min, s, f, Detail);
        }

        /// <summary>Wedge filling the box min..max: flat bottom, full-height back face on the <paramref name="rise"/> side.</summary>
        public static GameObject Wedge(Transform p, Vector3 min, Vector3 max, Dir rise, Surf s, ArchFlags f = ArchFlags.Solid)
        {
            Order(ref min, ref max);
            Vector3 size = max - min;
            // Geo's unit wedge rises toward local +Z; the yaw turns it to 'rise', so local x/z swap for X directions.
            Vector3 local = rise.IsX() ? new Vector3(size.z, size.y, size.x) : size;
            return Piece(p, "Wedge", Geo.WedgeMesh, (min + max) * 0.5f, rise.Rotation(), local, s, f, Detail);
        }

        /// <summary>Box with every edge and corner chamfered (convex 26-face hull). Props only.</summary>
        public static GameObject ChamferBox(Transform p, Vector3 min, Vector3 max, float chamfer, Surf s, ArchFlags f = ArchFlags.Solid)
        {
            Order(ref min, ref max);
            Vector3 size = max - min;
            Mesh mesh = ArchMeshes.Chamfered(size, chamfer);
            return Piece(p, "ChamferBox", mesh, (min + max) * 0.5f, Quaternion.identity, Vector3.one, s, f, Detail);
        }

        /// <summary>Upright n-gon prism standing on <paramref name="baseCenter"/> (vessels, poles, discs).</summary>
        public static GameObject Prism(Transform p, Vector3 baseCenter, float radius, float height, int sides, Surf s,
                                       ArchFlags f = ArchFlags.Solid, float yaw = 0f)
        {
            var go = Piece(p, "Prism", Geo.PrismMesh(sides), baseCenter + new Vector3(0f, height * 0.5f, 0f),
                           Quaternion.Euler(0f, yaw, 0f), new Vector3(radius * 2f, Mathf.Max(height, 1e-4f), radius * 2f), s, f, 0f);
            return go;
        }

        // ================================================================================== floors, terraces

        /// <summary>
        /// Floor slab with the SlabEdge profile: a 0.125 top layer (<paramref name="top"/>, default Style.Floor),
        /// a 0.0625-inset drip groove and the body (Style.Trim) down to topY - thickness.
        /// </summary>
        public static GameObject Floor(Transform p, RectXZ r, float topY = 0f, float thickness = SlabThickness,
                                       Surf? top = null, bool drip = true)
        {
            GameObject g = Group(p, "Floor");
            Transform t = g.transform;
            Surf ts = top ?? Style.Floor, body = Style.Trim;
            thickness = Mathf.Max(thickness, Detail);
            if (thickness <= 0.25f + 1e-4f || !drip)
            {
                float layer = Mathf.Min(0.125f, thickness);
                Tag(Box(t, r.Min(topY - layer), r.Max(topY), ts), Sub);
                if (thickness > layer + 1e-4f) Tag(Box(t, r.Min(topY - thickness), r.Max(topY - layer), body), Sub);
                return g;
            }
            Tag(Box(t, r.Min(topY - 0.125f), r.Max(topY), ts), Sub);
            RectXZ groove = r.Inset(Detail);
            Box(t, groove.Min(topY - 0.1875f), groove.Max(topY - 0.125f), body);
            Tag(Box(t, r.Min(topY - thickness), r.Max(topY - 0.1875f), body), Sub);
            return g;
        }

        public enum Underside { Flat, Stepped, Piers }

        /// <summary>
        /// Floating terrace: slab (as <see cref="Floor"/>), a Concrete/Courses body, then the underside: Stepped =
        /// two planar steps each inset 0.5 and 0.5 tall (within <paramref name="depth"/>), Flat, or Piers.
        /// </summary>
        public static GameObject Terrace(Transform p, RectXZ r, float topY, float depth, Underside under = Underside.Stepped,
                                         Surf? top = null)
        {
            GameObject g = Group(p, "Terrace");
            Transform t = g.transform;
            depth = Mathf.Max(depth, SlabThickness);
            Floor(t, r, topY, SlabThickness, top);
            float bottom = topY - depth;
            float bodyTop = topY - SlabThickness;
            Surf b = Style.Base;
            switch (under)
            {
                case Underside.Stepped:
                {
                    int steps = 0;
                    while (steps < 2 && bodyTop - bottom - (steps + 1) * 0.5f >= 0.5f - 1e-4f &&
                           r.Width > (steps + 1) * 1f + 0.5f && r.Depth > (steps + 1) * 1f + 0.5f) steps++;
                    float bodyBottom = bottom + steps * 0.5f;
                    if (bodyTop - bodyBottom > 1e-4f) Tag(Box(t, r.Min(bodyBottom), r.Max(bodyTop), b), Sub);
                    for (int i = 1; i <= steps; i++)
                    {
                        RectXZ ri = r.Inset(0.5f * i);
                        float y1 = bodyBottom - (i - 1) * 0.5f;
                        Tag(Box(t, ri.Min(y1 - 0.5f), ri.Max(y1), b), Sub);
                    }
                    break;
                }
                case Underside.Piers:
                {
                    float bodyBottom = Mathf.Max(bottom, bodyTop - 1f);
                    if (bodyTop - bodyBottom > 1e-4f) Tag(Box(t, r.Min(bodyBottom), r.Max(bodyTop), b), Sub);
                    if (bodyBottom - bottom > 1e-4f)
                    {
                        foreach (Vector2 c in PierPositions(r, 4f))
                            Tag(Box(t, new Vector3(c.x - 0.375f, bottom, c.y - 0.375f), new Vector3(c.x + 0.375f, bodyBottom, c.y + 0.375f), b), 0f);
                    }
                    break;
                }
                default:
                    if (bodyTop - bottom > 1e-4f) Tag(Box(t, r.Min(bottom), r.Max(bodyTop), b), Sub);
                    break;
            }
            return g;
        }

        /// <summary>A low plinth block (default 0.25 tall, Style.Trim).</summary>
        public static GameObject Plinth(Transform p, RectXZ r, float baseY, float height = 0.25f, Surf? s = null)
        {
            GameObject go = Box(p, r.Min(baseY), r.Max(baseY + height), s ?? Style.Trim);
            go.name = "Plinth";
            return Tag(go, Sub);
        }

        /// <summary>Wide steps without cheeks filling <paramref name="r"/>, climbing from fromY to toY toward <paramref name="up"/>.</summary>
        public static GameObject Steps(Transform p, RectXZ r, float fromY, float toY, Dir up, Surf? s = null)
        {
            GameObject g = Group(p, "Steps");
            Transform t = g.transform;
            float rise = toY - fromY;
            int count = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(rise) / Rise));
            float run = up.IsX() ? r.Width : r.Depth;
            float tread = run / count;
            Surf surf = s ?? Style.Trim;
            for (int i = 0; i < count; i++)
            {
                float a0 = i * tread, a1 = (i + 1) * tread;
                float y1 = fromY + (i + 1) * (rise / count);
                Vector3 min, max;
                switch (up)
                {
                    case Dir.PosX: min = new Vector3(r.X0 + a0, fromY, r.Z0); max = new Vector3(r.X0 + a1, y1, r.Z1); break;
                    case Dir.NegX: min = new Vector3(r.X1 - a1, fromY, r.Z0); max = new Vector3(r.X1 - a0, y1, r.Z1); break;
                    case Dir.NegZ: min = new Vector3(r.X0, fromY, r.Z1 - a1); max = new Vector3(r.X1, y1, r.Z1 - a0); break;
                    default: min = new Vector3(r.X0, fromY, r.Z0 + a0); max = new Vector3(r.X1, y1, r.Z0 + a1); break;
                }
                Tag(Box(t, min, max, surf), Sub);
            }
            return g;
        }

        // ================================================================================== walls, parapets, trims

        /// <summary>
        /// Axis-aligned wall on the centre line from → to (same x or same z; from.y = base): full-height segments,
        /// a lintel above each opening, a sill box below windows / niches (niches keep a back), bracket surrounds,
        /// and the trims (plinth, cornice, coping, pilasters every 4 m) on the front face or both faces.
        /// </summary>
        public static GameObject Wall(Transform p, Vector3 from, Vector3 to, float height = Storey,
                                      float thickness = WallThickness, WallTrim trim = WallTrim.Default,
                                      params Opening[] openings)
        {
            GameObject g = Group(p, "Wall");
            var fr = Frame.Along(from, to, out float length);
            if (length < 1e-4f) return g;
            Transform t = g.transform;
            float h = height, half = thickness * 0.5f;
            Surf wall = Style.Wall;
            bool both = (trim & WallTrim.BothSides) != 0;

            var list = new List<Opening>();
            if (openings != null)
                foreach (Opening o in openings)
                {
                    Opening c = o;
                    if (c.Width <= 0f || c.Height <= 0f) continue;
                    if (c.Start < 0f || c.End > length) Debug.LogWarning("[Arch] Wall opening at " + c.At + " runs past the wall (" + length + " m).");
                    list.Add(c);
                }
            list.Sort((a, b) => a.Start.CompareTo(b.Start));

            // Massing.
            float cur = 0f;
            foreach (Opening o in list)
            {
                float s0 = Mathf.Clamp(o.Start, 0f, length), s1 = Mathf.Clamp(o.End, 0f, length);
                if (s0 > cur + 1e-4f) Tag(fr.Box(t, cur, s0, 0f, h, -half, half, wall), Sub);
                float top = Mathf.Min(o.Top, h);
                if (h - top > 1e-4f) Tag(fr.Box(t, s0, s1, top, h, -half, half, wall), Sub);
                if (o.Sill > 1e-4f)
                {
                    Tag(fr.Box(t, s0, s1, 0f, Mathf.Min(o.Sill, h), -half, half, wall), Sub);
                }
                if (o.Kind == OpeningKind.Niche)
                {
                    float back = Mathf.Clamp(half - Mathf.Max(o.Depth, Detail), -half + Detail, half);
                    Tag(fr.Box(t, s0, s1, Mathf.Min(o.Sill, h), top, -half, back, wall), Sub);
                }
                if (o.Kind == OpeningKind.Portal) Corbels(t, fr, s0, s1, top, 2, -half, half, wall);
                cur = Mathf.Max(cur, s1);
            }
            if (length - cur > 1e-4f) Tag(fr.Box(t, cur, length, 0f, h, -half, half, wall), Sub);

            // Bracket surrounds and sills.
            foreach (Opening o in list)
            {
                if (o.Kind == OpeningKind.Window) Tag(fr.Box(t, o.Start - 0.125f, o.End + 0.125f, o.Sill - Detail, o.Sill, half, half + 0.125f, Style.Trim), Detail);
                if (!o.Bracket || o.Kind == OpeningKind.Hole) continue;
                bool foot = o.Kind == OpeningKind.Window || o.Kind == OpeningKind.Niche;
                BracketSurround(t, fr, o.Start, o.End, o.Sill, Mathf.Min(o.Top, h), h, half, +1f, foot);
                if (both && o.Kind != OpeningKind.Niche) BracketSurround(t, fr, o.Start, o.End, o.Sill, Mathf.Min(o.Top, h), h, half, -1f, foot);
            }

            // Trims.
            Surf trimS = Style.Trim;
            int sides = both ? 2 : 1;
            for (int side = 0; side < sides; side++)
            {
                float sign = side == 0 ? 1f : -1f;
                if ((trim & WallTrim.Plinth) != 0)
                    RunTrim(t, fr, length, list, sign, half, TrimProfile.Plinth, 0f, h, trimS, o => o.ReachesFloor, both);
                if ((trim & WallTrim.Cornice) != 0 && h > 0.75f)
                    RunTrim(t, fr, length, list, sign, half, TrimProfile.Cornice, h, h, trimS, o => o.Top >= h - 0.25f, both);
                if ((trim & WallTrim.Pilasters) != 0 && length > 4f + 1e-4f && h > 1f)
                {
                    for (float u = 4f; u < length - 0.5f; u += 4f)
                    {
                        bool blocked = false;
                        foreach (Opening o in list) if (u + 0.25f > o.Start - 0.25f && u - 0.25f < o.End + 0.25f) blocked = true;
                        if (blocked) continue;
                        Tag(fr.Box(t, u - 0.25f, u + 0.25f, 0.5f, h - 0.25f, sign * half, sign * (half + 0.125f), trimS), Detail);
                    }
                }
            }
            if ((trim & WallTrim.Coping) != 0)
                Tag(fr.Box(t, -Detail, length + Detail, h, h + 0.125f, -half - Detail, half + Detail, trimS), Detail);

            // A string course on the 3.0 datum (a 0.0625 band, 0.0625 proud) between the openings that reach it, on
            // every trimmed face: walls articulate at the datum (rule 4). Same Trim material: no extra draw call.
            // Ultra adds a 1/32 drip under it.
            if (h >= HeadY + 0.5f && length >= 1f)
                {
                    for (int side = 0; side < sides; side++)
                    {
                        float sign = side == 0 ? 1f : -1f;
                        float a = 0f;
                        foreach (Opening o in list)
                        {
                            if (o.Top < HeadY - 0.01f || o.Sill > HeadY) continue;
                            if (o.Start - a > 0.25f) StringCourse(t, fr, a, o.Start - 0.125f, sign, half);
                            a = Mathf.Max(a, o.End + 0.125f);
                        }
                        if (length - a > 0.25f) StringCourse(t, fr, a, length, sign, half);
                    }
                }
            return g;
        }

        static void StringCourse(Transform t, Frame fr, float u0, float u1, float sign, float half)
        {
            float i0 = sign * half, o0 = sign * (half + Detail), d0 = sign * (half + Detail * 0.5f);
            Tag(fr.Box(t, u0, u1, HeadY - Detail, HeadY, Mathf.Min(i0, o0), Mathf.Max(i0, o0), Style.Trim, ArchFlags.Soft), 0f);
            using (Ultra())
                Tag(fr.Box(t, u0, u1, HeadY - Detail - 0.03125f, HeadY - Detail, Mathf.Min(i0, d0), Mathf.Max(i0, d0), Style.Trim, ArchFlags.Soft), 0f);
        }

        /// <summary>Solid parapet: body (thickness × height − 0.125) and a coping. Total height = <paramref name="height"/>.</summary>
        public static GameObject Parapet(Transform p, Vector3 from, Vector3 to, float height = RailHeight, float thickness = 0.25f)
        {
            GameObject g = Group(p, "Parapet");
            var fr = Frame.Along(from, to, out float length);
            if (length < 1e-4f) return g;
            Transform t = g.transform;
            float half = thickness * 0.5f;
            float body = Mathf.Max(Detail, height - 0.125f);
            // 0.25-thick bodies centred on a grid line sit on the 0.125 half-step: checked on the detail grid.
            Tag(fr.Box(t, 0f, length, 0f, body, -half, half, Style.Wall), Detail);
            Tag(fr.Box(t, -Detail, length + Detail, body, height, -half - Detail, half + Detail, Style.Trim), Detail);
            // Ultra detail: a drip under the coping overhang and a skirting fillet at the foot, both faces.
            using (Ultra())
            {
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    float a = sgn * half, b = sgn * (half + 0.03125f);
                    Tag(fr.Box(t, 0f, length, body - 0.03125f, body, Mathf.Min(a, b), Mathf.Max(a, b), Style.Trim, ArchFlags.Soft), 0f);
                    Tag(fr.Box(t, 0f, length, 0f, Detail, Mathf.Min(a, b), Mathf.Max(a, b), Style.Base, ArchFlags.Soft), 0f);
                }
            }
            return g;
        }

        /// <summary>Bridge railing: kerb + top rail (Solid), Graphite posts at 1.0 and a mid rail (Soft).</summary>
        public static GameObject Railing(Transform p, Vector3 from, Vector3 to, float height = RailHeight)
        {
            GameObject g = Group(p, "Railing");
            var fr = Frame.Along(from, to, out float length);
            if (length < 1e-4f) return g;
            Transform t = g.transform;
            Surf metal = Style.Metal;
            Tag(fr.Box(t, 0f, length, 0f, 0.125f, -0.125f, 0.125f, Style.Trim), Detail);
            int posts = Mathf.Max(1, Mathf.RoundToInt(length / M));
            for (int i = 0; i <= posts; i++)
            {
                float u = Mathf.Clamp(i * (length / posts), Detail * 0.5f, length - Detail * 0.5f);
                Tag(fr.Box(t, u - Detail * 0.5f, u + Detail * 0.5f, 0.125f, height - Detail, -Detail * 0.5f, Detail * 0.5f, metal, ArchFlags.Soft), 0f);
            }
            Tag(fr.Box(t, 0f, length, 0.5f - Detail * 0.5f, 0.5f + Detail * 0.5f, -Detail * 0.5f, Detail * 0.5f, metal, ArchFlags.Soft), 0f);
            Tag(fr.Box(t, 0f, length, height - Detail, height, -Detail, Detail, metal), Detail);
            // Ultra detail: brass collars where the posts meet the rails (hardware).
            using (Ultra())
            {
                for (int i = 0; i <= posts; i++)
                {
                    float u = Mathf.Clamp(i * (length / posts), Detail, length - Detail);
                    Tag(fr.Box(t, u - Detail * 0.75f, u + Detail * 0.75f, 0.5f - Detail, 0.5f + Detail, -Detail * 0.75f, Detail * 0.75f, Mat.Brass, ArchFlags.Soft), 0f);
                    Tag(fr.Box(t, u - Detail * 0.75f, u + Detail * 0.75f, 0.125f, 0.125f + Detail, -Detail * 0.75f, Detail * 0.75f, Mat.Brass, ArchFlags.Soft), 0f);
                }
            }
            return g;
        }

        public enum ScreenKind { ContactSheet, Lattice }

        /// <summary>Contact-sheet grid (0.75 openings, 0.25 mullions on a 1.0 pitch) or a breeze-block Lattice slab. 0.25 thick.</summary>
        public static GameObject Screen(Transform p, Vector3 from, Vector3 to, float height, ScreenKind kind)
        {
            GameObject g = Group(p, kind == ScreenKind.Lattice ? "Lattice" : "ContactSheet");
            var fr = Frame.Along(from, to, out float length);
            if (length < 1e-4f) return g;
            Transform t = g.transform;
            Surf wall = Style.Wall;
            if (kind == ScreenKind.Lattice)
            {
                Tag(fr.Box(t, 0f, length, 0f, height, -0.125f, 0.125f, new Surf(wall.Mat, Pat.Lattice)), Detail);
                return g;
            }
            int cols = Mathf.Max(1, Mathf.FloorToInt(length + 1e-4f));
            for (int i = 0; i <= cols; i++)
            {
                float u0 = Mathf.Min(i * 1f, length - Sub);
                Tag(fr.Box(t, u0, u0 + Sub, 0f, height, -0.125f, 0.125f, wall), Detail);
            }
            int rows = Mathf.Max(1, Mathf.FloorToInt(height + 1e-4f));
            for (int j = 0; j <= rows; j++)
            {
                float y0 = Mathf.Min(j * 1f, height - Sub);
                Tag(fr.Box(t, 0f, length, y0, y0 + Sub, -0.125f, 0.125f, wall), Detail);
            }
            // Ultra detail: a slim Graphite mullion and transom in every opening (the contact sheet's frame lines).
            using (Ultra())
            {
                for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                {
                    float u0 = i * 1f + Sub, u1 = Mathf.Min((i + 1) * 1f, length - Sub);
                    float y0 = j * 1f + Sub, y1 = Mathf.Min((j + 1) * 1f, height - Sub);
                    if (u1 - u0 < 0.25f || y1 - y0 < 0.25f) continue;
                    float um = (u0 + u1) * 0.5f, ym = (y0 + y1) * 0.5f;
                    Tag(fr.Box(t, um - 0.015625f, um + 0.015625f, y0, y1, -0.03125f, 0.03125f, Mat.Graphite, ArchFlags.Soft), 0f);
                    Tag(fr.Box(t, u0, u1, ym - 0.015625f, ym + 0.015625f, -0.03125f, 0.03125f, Mat.Graphite, ArchFlags.Soft), 0f);
                }
            }
            return g;
        }

        /// <summary>
        /// A trim profile along the run from → to on a face at <paramref name="from"/> looking <paramref name="outward"/>,
        /// with its datum at <paramref name="datumY"/>. Both ends are free (extended by the profile's largest Out).
        /// </summary>
        public static GameObject Trim(Transform p, Vector3 from, Vector3 to, Dir outward, TrimProfile profile, float datumY, Surf? s = null)
        {
            GameObject g = Group(p, "Trim");
            if (profile == null) return g;
            Transform t = g.transform;
            var fr = Frame.OnFace(from, to, outward, out float length);
            float ext = profile.MaxOut;
            foreach (ProfileBox b in profile.Boxes)
            {
                Surf surf = b.UseOverride ? b.Override : (s ?? Style.Trim);
                Tag(fr.Box(t, -ext, length + ext, datumY - from.y + b.Y0, datumY - from.y + b.Y1, b.Out0, b.Out1, surf), Detail);
            }
            return g;
        }

        /// <summary>Additive: coping on a host of <paramref name="thickness"/> centred on from → to, sitting on topY.</summary>
        public static GameObject Coping(Transform p, Vector3 from, Vector3 to, float topY, float thickness)
        {
            GameObject g = Group(p, "Coping");
            var fr = Frame.Along(from, to, out float length);
            float half = thickness * 0.5f;
            Tag(fr.Box(g.transform, -Detail, length + Detail, topY - from.y, topY - from.y + 0.125f, -half - Detail, half + Detail, Style.Trim), Detail);
            return g;
        }

        // ================================================================================== columns and piers

        /// <summary>Column: base 0.75² × 0.25, square shaft, capital 0.625² × 0.125 and abacus 0.75² × 0.125.</summary>
        public static GameObject Column(Transform p, Vector3 basePos, float height = Storey, float shaft = 0.5f)
        {
            GameObject g = Group(p, "Column");
            Transform t = g.transform;
            Vector3 b = basePos;
            Tag(Box(t, b + new Vector3(-0.375f, 0f, -0.375f), b + new Vector3(0.375f, 0.25f, 0.375f), Style.Trim), Detail);
            float hs = shaft * 0.5f;
            Tag(Box(t, b + new Vector3(-hs, 0.25f, -hs), b + new Vector3(hs, height - 0.25f, hs), Style.Wall), Detail);
            Tag(Box(t, b + new Vector3(-0.3125f, height - 0.25f, -0.3125f), b + new Vector3(0.3125f, height - 0.125f, 0.3125f), Style.Trim), Detail);
            Tag(Box(t, b + new Vector3(-0.375f, height - 0.125f, -0.375f), b + new Vector3(0.375f, height, 0.375f), Style.Trim), Detail);
            // Ultra detail: astragal rings at the foot and neck of the shaft.
            using (Ultra())
            {
                float r = hs + 0.03125f;
                Tag(Box(t, b + new Vector3(-r, 0.25f, -r), b + new Vector3(r, 0.3125f, r), Style.Trim, ArchFlags.Soft), 0f);
                Tag(Box(t, b + new Vector3(-r, height - 0.375f, -r), b + new Vector3(r, height - 0.3125f, r), Style.Trim, ArchFlags.Soft), 0f);
            }
            return g;
        }

        /// <summary>Plain massive pier (size ≥ 0.75) with a 0.125 shadow gap 0.5 below the top.</summary>
        public static GameObject Pier(Transform p, Vector3 basePos, Vector2 size, float height)
        {
            GameObject g = Group(p, "Pier");
            Transform t = g.transform;
            Vector3 h = new Vector3(size.x * 0.5f, 0f, size.y * 0.5f);
            Surf s = Style.Base;
            if (height <= 0.75f)
            {
                Tag(Box(t, basePos - h, basePos + h + Vector3.up * height, s), Detail);
                return g;
            }
            Vector3 gi = h - new Vector3(0.125f, 0f, 0.125f);
            Tag(Box(t, basePos - h, basePos + h + Vector3.up * (height - 0.625f), s), Detail);
            Tag(Box(t, basePos - gi + Vector3.up * (height - 0.625f), basePos + gi + Vector3.up * (height - 0.5f), s), Detail);
            Tag(Box(t, basePos - h + Vector3.up * (height - 0.5f), basePos + h + Vector3.up * height, s), Detail);
            return g;
        }

        /// <summary>U-shaped pier in plan (back 1.0 × 0.25, two returns 0.25 × 0.5), open toward <paramref name="open"/>.</summary>
        public static GameObject BracketPier(Transform p, Vector3 basePos, float height, Dir open)
        {
            GameObject g = Group(p, "BracketPier");
            Transform t = g.transform;
            var fr = new Frame(basePos, open.Right(), open);
            Surf s = Style.Wall;
            Tag(fr.Box(t, -0.5f, 0.5f, 0f, height, -0.25f, 0f, s), Detail);
            Tag(fr.Box(t, -0.5f, -0.25f, 0f, height, -0.25f, 0.25f, s), Detail);
            Tag(fr.Box(t, 0.25f, 0.5f, 0f, height, -0.25f, 0.25f, s), Detail);
            return g;
        }

        /// <summary>Pilaster 0.5 wide on a face (point at the face's base), 0.125 proud, from 0.5 to height − 0.25.</summary>
        public static GameObject Pilaster(Transform p, Vector3 faceBase, float height, Dir outward)
        {
            var fr = new Frame(faceBase, outward.Right(), outward);
            float top = Mathf.Max(0.5f + Detail, height - 0.25f);
            GameObject go = fr.Box(p, -0.25f, 0.25f, 0.5f, top, 0f, 0.125f, Style.Trim);
            go.name = "Pilaster";
            // Ultra detail: a capital band and a base fillet, 1/16 proud of the pilaster.
            using (Ultra())
            {
                Tag(fr.Box(p, -0.3125f, 0.3125f, top - 0.125f, top, 0f, 0.1875f, Style.Trim, ArchFlags.Soft), 0f);
                Tag(fr.Box(p, -0.28125f, 0.28125f, 0.5f, 0.5f + Detail, 0f, 0.15625f, Style.Trim, ArchFlags.Soft), 0f);
            }
            return Tag(go, Detail);
        }

        /// <summary>Columns every <paramref name="bay"/> along from → to, plus (roof) an architrave beam on top.</summary>
        public static GameObject Colonnade(Transform p, Vector3 from, Vector3 to, float height = Storey, float bay = 4f, bool roof = true)
        {
            GameObject g = Group(p, "Colonnade");
            var fr = Frame.Along(from, to, out float length);
            Transform t = g.transform;
            int n = Mathf.Max(1, Mathf.RoundToInt(length / Mathf.Max(bay, 0.5f)));
            for (int i = 0; i <= n; i++) Column(t, fr.Point(i * (length / n), 0f, 0f), height);
            if (roof) Tag(fr.Box(t, -0.375f, length + 0.375f, height, height + 0.5f, -0.375f, 0.375f, Style.Trim), Detail);
            return g;
        }

        // ================================================================================== portals and arches

        /// <summary>Freestanding bracket portal: two 0.5 jambs + a 0.5 lintel around a clear width × height, bracket surrounds front and back.</summary>
        public static GameObject BracketFrame(Transform p, Vector3 sillCenter, Dir facing,
                                              float width = DoorWidth, float height = DoorHeight, float depth = 0.5f)
        {
            GameObject g = Group(p, "BracketFrame");
            Transform t = g.transform;
            var fr = new Frame(sillCenter, facing.Right(), facing);
            float hw = width * 0.5f, hd = depth * 0.5f;
            Surf s = Style.Wall;
            Tag(fr.Box(t, -hw - 0.5f, -hw, 0f, height, -hd, hd, s), Sub);
            Tag(fr.Box(t, hw, hw + 0.5f, 0f, height, -hd, hd, s), Sub);
            Tag(fr.Box(t, -hw - 0.5f, hw + 0.5f, height, height + 0.5f, -hd, hd, s), Sub);
            BracketSurround(t, fr, -hw, hw, 0f, height, height + 0.5f, hd, +1f, false);
            BracketSurround(t, fr, -hw, hw, 0f, height, height + 0.5f, hd, -1f, false);
            return g;
        }

        /// <summary>Corbel arch: jambs, a 0.5 lintel and <paramref name="steps"/> corbel steps (0.25 in, 0.25 down each).</summary>
        public static GameObject CorbelArch(Transform p, Vector3 sillCenter, Dir facing, float width = 3f,
                                            float height = 4f, int steps = 2, float depth = WallThickness)
        {
            GameObject g = Group(p, "CorbelArch");
            Transform t = g.transform;
            var fr = new Frame(sillCenter, facing.Right(), facing);
            float hw = width * 0.5f, hd = depth * 0.5f;
            Surf s = Style.Wall;
            Tag(fr.Box(t, -hw - 0.5f, -hw, 0f, height, -hd, hd, s), Sub);
            Tag(fr.Box(t, hw, hw + 0.5f, 0f, height, -hd, hd, s), Sub);
            Tag(fr.Box(t, -hw - 0.5f, hw + 0.5f, height, height + 0.5f, -hd, hd, s), Sub);
            Corbels(t, fr, -hw, hw, height, steps, -hd, hd, s);
            return g;
        }

        // ================================================================================== stairs and ramps

        /// <summary>
        /// Solid step columns (rise 0.25, tread 0.5) from <paramref name="bottomCenter"/> (centre of the first riser's
        /// foot) climbing toward <paramref name="up"/>, with Soft Limestone nosings and stepped Sprocket cheek walls.
        /// </summary>
        public static StairResult Stair(Transform p, Vector3 bottomCenter, Dir up, float rise, float width = 2f, bool cheeks = true)
        {
            GameObject g = Group(p, "Stair");
            Transform t = g.transform;
            int n = Mathf.Max(1, Mathf.RoundToInt(rise / Rise));
            if (Mathf.Abs(n * Rise - rise) > 1e-3f) Debug.LogWarning("[Arch] Stair rise " + rise + " is not a multiple of 0.25 (built " + n * Rise + ").");
            var fr = new Frame(bottomCenter, up.Right(), up);
            float hw = width * 0.5f;
            Surf tread = Style.Trim;
            var cheek = new Surf(Style.Wall.Mat, Pat.Sprocket);
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Tread, a1 = (i + 1) * Tread, top = (i + 1) * Rise;
                Tag(fr.Box(t, -hw, hw, 0f, top, a0, a1, tread), Sub);
                Tag(fr.Box(t, -hw, hw, top - 0.03f, top, a0 - 0.03f, a0 + Detail, new Surf(Mat.Limestone), ArchFlags.Soft), 0f);
                if (cheeks)
                {
                    Tag(fr.Box(t, -hw - 0.25f, -hw, 0f, top + 0.25f, a0, a1, cheek), Sub);
                    Tag(fr.Box(t, hw, hw + 0.25f, 0f, top + 0.25f, a0, a1, cheek), Sub);
                }
            }
            return new StairResult
            {
                Root = g,
                Steps = n,
                TopLanding = fr.Point(0f, n * Rise, n * Tread),
            };
        }

        /// <summary>Walkable wedge ramp (≤ 30°) from <paramref name="bottomCenter"/> toward <paramref name="up"/>, with cheeks as for stairs.</summary>
        public static GameObject Ramp(Transform p, Vector3 bottomCenter, Dir up, float width, float run, float rise, bool cheeks = true)
        {
            GameObject g = Group(p, "Ramp");
            Transform t = g.transform;
            if (Mathf.Atan2(rise, run) * Mathf.Rad2Deg > 30.5f) Debug.LogWarning("[Arch] Ramp steeper than 30° (" + rise + " over " + run + ").");
            var fr = new Frame(bottomCenter, up.Right(), up);
            float hw = width * 0.5f;
            fr.MinMax(-hw, hw, 0f, rise, 0f, run, out Vector3 min, out Vector3 max);
            Tag(Wedge(t, min, max, up, Style.Trim), Sub);
            if (cheeks)
            {
                var cheek = new Surf(Style.Wall.Mat, Pat.Sprocket);
                foreach (float sgn in new[] { -1f, 1f })
                {
                    float c0 = sgn < 0 ? -hw - 0.25f : hw, c1 = sgn < 0 ? -hw : hw + 0.25f;
                    Tag(fr.Box(t, c0, c1, 0f, 0.25f, 0f, run, cheek), Sub);
                    fr.MinMax(c0, c1, 0.25f, rise + 0.25f, 0f, run, out Vector3 wmin, out Vector3 wmax);
                    Tag(Wedge(t, wmin, wmax, up, cheek), Sub);
                }
            }
            return g;
        }

        // ================================================================================== roofs, canopies, pergolas

        /// <summary>
        /// Ribbed canopy over <paramref name="r"/>: the rib bottoms are at <paramref name="undersideY"/> (the headroom), the
        /// slab (Plaster/Coffer underneath) sits on the ribs. Ribs run along <paramref name="ribsAlong"/> every <paramref name="ribPitch"/>.
        /// </summary>
        public static GameObject Canopy(Transform p, RectXZ r, float undersideY, float thickness = 0.5f,
                                        float ribPitch = 2f, float ribDepth = 0.75f, Dir ribsAlong = Dir.PosX)
        {
            GameObject g = Group(p, "Canopy");
            Transform t = g.transform;
            Surf ceil = Style.Ceiling;
            float slab0 = undersideY + ribDepth;
            Tag(Box(t, r.Min(slab0), r.Max(slab0 + thickness), ceil), Sub);
            // Fascia: wraps the slab edge, 0.0625 proud on every side and above (no coplanar faces with the slab).
            RectXZ fascia = r.Inset(-Detail);
            Tag(Box(t, fascia.Min(slab0 + thickness - 0.1875f), fascia.Max(slab0 + thickness + Detail), Style.Trim, ArchFlags.Soft), Detail);
            bool alongX = ribsAlong.IsX();
            float a0 = alongX ? r.Z0 : r.X0, a1 = alongX ? r.Z1 : r.X1;
            int n = Mathf.Max(1, Mathf.RoundToInt((a1 - a0) / Mathf.Max(ribPitch, 0.5f)));
            for (int i = 0; i <= n; i++)
            {
                float c = Mathf.Clamp(a0 + i * (a1 - a0) / n, a0 + 0.125f, a1 - 0.125f);
                Vector3 min = alongX ? new Vector3(r.X0, undersideY, c - 0.125f) : new Vector3(c - 0.125f, undersideY, r.Z0);
                Vector3 max = alongX ? new Vector3(r.X1, slab0, c + 0.125f) : new Vector3(c + 0.125f, slab0, r.Z1);
                Tag(Box(t, min, max, ceil, ArchFlags.Soft), Detail);
            }
            return g;
        }

        /// <summary>Flat roof: slab + 0.25 parapet around the edge (height <paramref name="parapet"/>) + a scupper every 4 m.</summary>
        public static GameObject Roof(Transform p, RectXZ r, float topY, float thickness = SlabThickness, float parapet = 0.75f)
        {
            GameObject g = Group(p, "Roof");
            Transform t = g.transform;
            Floor(t, r, topY, thickness, Style.Base);
            if (parapet > 1e-4f)
            {
                float x0 = r.X0 + 0.125f, x1 = r.X1 - 0.125f, z0 = r.Z0 + 0.125f, z1 = r.Z1 - 0.125f;
                Parapet(t, new Vector3(r.X0, topY, z0), new Vector3(r.X1, topY, z0), parapet);
                Parapet(t, new Vector3(r.X0, topY, z1), new Vector3(r.X1, topY, z1), parapet);
                Parapet(t, new Vector3(x0, topY, r.Z0 + 0.25f), new Vector3(x0, topY, r.Z1 - 0.25f), parapet);
                Parapet(t, new Vector3(x1, topY, r.Z0 + 0.25f), new Vector3(x1, topY, r.Z1 - 0.25f), parapet);
            }
            for (float x = r.X0 + 2f; x < r.X1 - 1f; x += 4f)
            {
                Tag(Box(t, new Vector3(x - 0.125f, topY, r.Z0 - 0.25f), new Vector3(x + 0.125f, topY + 0.25f, r.Z0), Style.Trim), Detail);
                Tag(Box(t, new Vector3(x - 0.125f, topY, r.Z1), new Vector3(x + 0.125f, topY + 0.25f, r.Z1 + 0.25f), Style.Trim), Detail);
            }
            return g;
        }

        /// <summary>Pergola: 0.5 piers every 4 m under two Walnut girders, Walnut beams 0.125 × 0.25 every <paramref name="beamPitch"/> along <paramref name="beamsAlong"/>.</summary>
        public static GameObject Pergola(Transform p, RectXZ r, float height = 3f, float beamPitch = 0.5f, Dir beamsAlong = Dir.PosX)
        {
            GameObject g = Group(p, "Pergola");
            Transform t = g.transform;
            bool alongX = beamsAlong.IsX();
            var wood = new Surf(Mat.Walnut, Pat.Boards);
            float girderY = height - 0.375f;
            // Girders run across the beams at both beam ends, on piers every 4 m.
            float e0 = alongX ? r.X0 + 0.25f : r.Z0 + 0.25f, e1 = alongX ? r.X1 - 0.25f : r.Z1 - 0.25f;
            float s0 = alongX ? r.Z0 : r.X0, s1 = alongX ? r.Z1 : r.X1;
            foreach (float e in new[] { e0, e1 })
            {
                int n = Mathf.Max(1, Mathf.RoundToInt((s1 - s0 - 0.5f) / 4f));
                for (int i = 0; i <= n; i++)
                {
                    float s = s0 + 0.25f + i * (s1 - s0 - 0.5f) / n;
                    Vector3 c = alongX ? new Vector3(e, 0f, s) : new Vector3(s, 0f, e);
                    Tag(Box(t, c + new Vector3(-0.25f, 0f, -0.25f), c + new Vector3(0.25f, girderY, 0.25f), Style.Wall), Detail);
                }
                Vector3 gmin = alongX ? new Vector3(e - 0.125f, girderY, s0) : new Vector3(s0, girderY, e - 0.125f);
                Vector3 gmax = alongX ? new Vector3(e + 0.125f, height, s1) : new Vector3(s1, height, e + 0.125f);
                Tag(Box(t, gmin, gmax, wood, ArchFlags.Soft), Detail);
            }
            float step = Mathf.Max(beamPitch, 0.25f);
            for (float s = s0 + step * 0.5f; s < s1 - 1e-4f; s += step)
            {
                Vector3 bmin = alongX ? new Vector3(r.X0, height, s - Detail) : new Vector3(s - Detail, height, r.Z0);
                Vector3 bmax = alongX ? new Vector3(r.X1, height + 0.25f, s + Detail) : new Vector3(s + Detail, height + 0.25f, r.Z1);
                Tag(Box(t, bmin, bmax, wood, ArchFlags.Soft), Detail);
            }
            return g;
        }

        // ================================================================================== skyline

        /// <summary>Stacked 2.0 slabs, each shifted by a multiple of 0.25 (≤ 0.5), with a 0.125 shadow gap between them. Soft.</summary>
        public static GameObject SlabTower(Transform p, Vector3 baseCenter, Vector2 footprint, int slabs, int seed, float slabHeight = 2f)
        {
            GameObject g = Group(p, "SlabTower");
            Transform t = g.transform;
            var rng = new System.Random(seed);
            float y = 0f;
            for (int i = 0; i < Mathf.Max(1, slabs); i++)
            {
                float ox = (rng.Next(5) - 2) * 0.25f, oz = (rng.Next(5) - 2) * 0.25f;
                float wx = Mathf.Max(1f, footprint.x + (rng.Next(3) - 1) * 0.5f), wz = Mathf.Max(1f, footprint.y + (rng.Next(3) - 1) * 0.5f);
                Vector3 c = baseCenter + new Vector3(ox, 0f, oz);
                Vector3 h = new Vector3(wx * 0.5f, 0f, wz * 0.5f);
                Surf s = (i & 1) == 0 ? Style.Wall : Style.Base;
                Tag(Box(t, c - h + Vector3.up * y, c + h + Vector3.up * (y + slabHeight - 0.125f), s, ArchFlags.Soft), Detail);
                Vector3 gi = h - new Vector3(0.125f, 0f, 0.125f);
                Tag(Box(t, c - gi + Vector3.up * (y + slabHeight - 0.125f), c + gi + Vector3.up * (y + slabHeight), Style.Base, ArchFlags.Soft), Detail);
                y += slabHeight;
            }
            return g;
        }

        // ================================================================================== internals

        /// <summary>Creates one tagged piece (child of p) on a shared unit mesh.</summary>
        internal static GameObject Piece(Transform p, string name, Mesh mesh, Vector3 center, Quaternion rotation, Vector3 scale,
                                         Surf s, ArchFlags f, float snap)
        {
            if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
            {
                Debug.LogWarning("[Arch] Degenerate " + name + " (size " + scale + ") under '" + (p != null ? p.name : "<root>") + "'.");
                scale = Vector3.Max(scale, Vector3.one * 1e-4f);
            }
            var go = new GameObject(name);
            Transform t = go.transform;
            t.SetParent(p, false);
            t.localPosition = center;
            t.localRotation = rotation;
            t.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = UltraOnly ? Palette.GetUltra(s.Mat) : Palette.Get(s.Mat);
            if (UltraOnly) f &= ~ArchFlags.Collider;   // Ultra detail never changes collision
            r.shadowCastingMode = (f & ArchFlags.NoShadows) != 0 ? ShadowCastingMode.Off : ShadowCastingMode.On;
            r.receiveShadows = true;
            if ((f & ArchFlags.Collider) != 0)
            {
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = mesh;
            }
            if ((f & ArchFlags.Sliceable) != 0) go.AddComponent<Sliceable>();
            var piece = go.AddComponent<ArchPiece>();
            piece.Surf = s;
            piece.Flags = f;
            piece.Snap = snap;
            return go;
        }

        /// <summary>Sets the grid a piece is validated against (0 = exempt). Returns the object.</summary>
        internal static GameObject Tag(GameObject go, float snap)
        {
            if (go != null && go.TryGetComponent(out ArchPiece piece)) piece.Snap = snap;
            return go;
        }

        static void Order(ref Vector3 min, ref Vector3 max)
        {
            Vector3 a = Vector3.Min(min, max), b = Vector3.Max(min, max);
            min = a;
            max = b;
        }

        static IEnumerable<Vector2> PierPositions(RectXZ r, float spacing)
        {
            int nx = Mathf.Max(1, Mathf.RoundToInt((r.Width - 1f) / spacing));
            int nz = Mathf.Max(1, Mathf.RoundToInt((r.Depth - 1f) / spacing));
            for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
            {
                if (i != 0 && i != nx && j != 0 && j != nz) continue; // perimeter only
                yield return new Vector2(r.X0 + 0.5f + i * (r.Width - 1f) / nx, r.Z0 + 0.5f + j * (r.Depth - 1f) / nz);
            }
        }

        /// <summary>Corbel steps hanging under a lintel at <paramref name="top"/>: step k protrudes k × 0.25 from each jamb.</summary>
        static void Corbels(Transform t, Frame fr, float s0, float s1, float top, int steps, float n0, float n1, Surf s)
        {
            for (int k = 1; k <= steps; k++)
            {
                float y1 = top - (steps - k) * 0.25f, y0 = y1 - 0.25f;
                float w = k * 0.25f;
                if (s1 - s0 <= 2f * w + 0.25f) break;
                Tag(fr.Box(t, s0, s0 + w, y0, y1, n0, n1, s), Sub);
                Tag(fr.Box(t, s1 - w, s1, y0, y1, n0, n1, s), Sub);
            }
        }

        /// <summary>
        /// The [ ] crop-mark surround on one face (sign +1 front / −1 back) of an opening u ∈ [s0, s1], y ∈ [sill, top]:
        /// jamb strips 0.25 wide up to top + 0.25, head serifs 0.375 long, foot serifs (windows, niches) or brass floor
        /// inlays (doors). Proud 0.0625. The head centre between the serifs stays bare.
        /// </summary>
        static void BracketSurround(Transform t, Frame fr, float s0, float s1, float sill, float top, float hostTop, float half,
                                    float sign, bool footSerif)
        {
            Surf b = Style.Bracket;
            float n0 = sign * half, n1 = sign * (half + Detail);
            float head = Mathf.Min(top + 0.25f, hostTop);
            bool serif = top + 0.25f <= hostTop + 1e-4f;
            float bottom = footSerif ? sill - 0.25f : sill;
            Tag(fr.Box(t, s0 - 0.25f, s0, bottom, head, n0, n1, b), Detail);
            Tag(fr.Box(t, s1, s1 + 0.25f, bottom, head, n0, n1, b), Detail);
            if (serif)
            {
                Tag(fr.Box(t, s0 - 0.25f, s0 + 0.125f, top, top + 0.25f, n0, n1, b), Detail);
                Tag(fr.Box(t, s1 - 0.125f, s1 + 0.25f, top, top + 0.25f, n0, n1, b), Detail);
            }
            if (footSerif)
            {
                Tag(fr.Box(t, s0 - 0.25f, s0 + 0.125f, sill - 0.25f, sill, n0, n1, b), Detail);
                Tag(fr.Box(t, s1 - 0.125f, s1 + 0.25f, sill - 0.25f, sill, n0, n1, b), Detail);
            }
            else if (sill <= 1e-4f)
            {
                // Door: the foot serif becomes a brass floor inlay 0.375 × 0.125 × 0.01 in front of the face.
                float i0 = sign * half, i1 = sign * (half + 0.125f);
                Tag(fr.Box(t, s0 - 0.25f, s0 + 0.125f, 0f, 0.01f, i0, i1, Style.Inlay, ArchFlags.Soft | ArchFlags.NoShadows), 0f);
                Tag(fr.Box(t, s1 - 0.125f, s1 + 0.25f, 0f, 0.01f, i0, i1, Style.Inlay, ArchFlags.Soft | ArchFlags.NoShadows), 0f);
            }
        }

        /// <summary>
        /// A trim profile along the solid runs of a wall face (sign ±1), broken where <paramref name="breaks"/> says an
        /// opening interrupts it. Free wall ends are extended by the profile's largest Out and wrapped around the end.
        /// </summary>
        static void RunTrim(Transform t, Frame fr, float length, List<Opening> openings, float sign, float half, TrimProfile profile,
                            float datum, float hostTop, Surf s, System.Func<Opening, bool> breaks, bool wrapBothSides)
        {
            float ext = profile.MaxOut;
            float cur = 0f;
            bool startFree = true;
            var runs = new List<Vector3>(); // (u0, u1, flags: 1 = start free, 2 = end free)
            foreach (Opening o in openings)
            {
                if (!breaks(o)) continue;
                // A bracket jamb runs down to the floor beside its opening: the trim stops at the bracket.
                float pad = o.Bracket && o.Kind != OpeningKind.Hole ? 0.25f : 0f;
                float s0 = Mathf.Clamp(o.Start - pad, 0f, length);
                if (s0 > cur + 1e-4f) runs.Add(new Vector3(cur, s0, startFree ? 1f : 0f));
                cur = Mathf.Max(cur, Mathf.Clamp(o.End + pad, 0f, length));
                startFree = false;
            }
            if (length > cur + 1e-4f) runs.Add(new Vector3(cur, length, (startFree ? 1f : 0f) + 2f));

            foreach (Vector3 run in runs)
            {
                bool f0 = ((int)run.z & 1) != 0, f1 = ((int)run.z & 2) != 0;
                float u0 = run.x - (f0 ? ext : 0f), u1 = run.y + (f1 ? ext : 0f);
                foreach (ProfileBox b in profile.Boxes)
                {
                    float y0 = (profile.FromTop ? hostTop : datum) + b.Y0, y1 = (profile.FromTop ? hostTop : datum) + b.Y1;
                    Surf surf = b.UseOverride ? b.Override : s;
                    float n0 = sign * (half + b.Out0), n1 = sign * (half + b.Out1);
                    Tag(fr.Box(t, u0, u1, y0, y1, n0, n1, surf), Detail);
                    // Wrap the free ends (front pass only, across the whole end when both faces are trimmed).
                    if (sign > 0f)
                    {
                        float w0 = wrapBothSides ? -half - b.Out1 : -half, w1 = half + b.Out1;
                        if (f0) Tag(fr.Box(t, run.x - b.Out1, run.x - b.Out0, y0, y1, w0, w1, surf), Detail);
                        if (f1) Tag(fr.Box(t, run.y + b.Out0, run.y + b.Out1, y0, y1, w0, w1, surf), Detail);
                    }
                }
            }
        }

        /// <summary>
        /// A local frame in plan: u along <see cref="U"/>, n along <see cref="N"/> (both axis-aligned and perpendicular),
        /// y up. Converts (u, y, n) boxes to p-local min/max.
        /// </summary>
        internal readonly struct Frame
        {
            public readonly Vector3 Origin, U, N;

            public Frame(Vector3 origin, Dir u, Dir n)
            {
                Origin = origin;
                U = u.ToVector();
                N = n.ToVector();
            }

            /// <summary>Frame along from → to (snapped to the dominant plan axis); n = the front (right-hand) side.</summary>
            public static Frame Along(Vector3 from, Vector3 to, out float length)
            {
                Vector3 d = to - from;
                d.y = 0f;
                Dir u = DirUtil.FromVector(d);
                length = Mathf.Abs(u.IsX() ? d.x : d.z);
                if (Mathf.Abs(u.IsX() ? d.z : d.x) > 1e-3f)
                    Debug.LogWarning("[Arch] Run " + from + " → " + to + " is not axis-aligned; using its " + u + " component.");
                return new Frame(from, u, u.Right());
            }

            /// <summary>Frame on a face: u along from → to, n = outward.</summary>
            public static Frame OnFace(Vector3 from, Vector3 to, Dir outward, out float length)
            {
                Vector3 d = to - from;
                d.y = 0f;
                Dir u = DirUtil.FromVector(d);
                if (u.IsX() == outward.IsX()) u = outward.Right();
                length = Mathf.Abs(Vector3.Dot(d, u.ToVector()));
                return new Frame(from, u, outward);
            }

            public Vector3 Point(float u, float y, float n) => Origin + U * u + Vector3.up * y + N * n;

            public void MinMax(float u0, float u1, float y0, float y1, float n0, float n1, out Vector3 min, out Vector3 max)
            {
                Vector3 a = Point(u0, y0, n0), b = Point(u1, y1, n1);
                min = Vector3.Min(a, b);
                max = Vector3.Max(a, b);
            }

            public GameObject Box(Transform t, float u0, float u1, float y0, float y1, float n0, float n1, Surf s,
                                  ArchFlags f = ArchFlags.Solid)
            {
                MinMax(u0, u1, y0, y1, n0, n1, out Vector3 min, out Vector3 max);
                return Arch.Box(t, min, max, s, f);
            }
        }
    }
}
