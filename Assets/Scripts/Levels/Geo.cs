using System.Collections.Generic;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;

namespace Ion.Levels
{
    /// <summary>
    /// Low-poly building blocks. Every solid shape is a closed convex mesh with flat-shaded (split)
    /// normals, a shared palette material, a static MeshCollider and a <see cref="Sliceable"/> marker so
    /// the projection system can cut and capture it. Meshes are unit-sized and shared per shape; size,
    /// position and rotation live on the transform (the slicer bakes the transform).
    /// Positions are LOCAL to <c>parent</c> (world space when parent is null).
    /// </summary>
    public static class Geo
    {
        // ------------------------------------------------------------------ contract

        /// <summary>Axis-aligned box (before yRot) centred on <paramref name="center"/>.</summary>
        public static GameObject Box(Transform parent, Vector3 center, Vector3 size, Color c, float yRot = 0)
        {
            return Solid("Box", parent, CubeMesh, center, Quaternion.Euler(0f, yRot, 0f), size, c);
        }

        /// <summary>
        /// Right-angled wedge filling the box <paramref name="size"/> around <paramref name="center"/>:
        /// flat bottom, full-height back face at local +Z, slope rising from the front-bottom edge (−Z)
        /// to the back-top edge (+Z). With yRot = 0 it is a ramp you walk up while moving toward +Z.
        /// </summary>
        public static GameObject Wedge(Transform parent, Vector3 center, Vector3 size, Color c, float yRot = 0)
        {
            return Solid("Wedge", parent, WedgeMesh, center, Quaternion.Euler(0f, yRot, 0f), size, c);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// A walkable ramp: starts at floor level <paramref name="bottomCenter"/> (centre of the low edge)
        /// and climbs <paramref name="rise"/> metres over <paramref name="run"/> metres in the direction
        /// given by <paramref name="yRot"/> (0 = toward +Z).
        /// </summary>
        public static GameObject Ramp(Transform parent, Vector3 bottomCenter, float width, float run, float rise, Color c, float yRot = 0)
        {
            Quaternion r = Quaternion.Euler(0f, yRot, 0f);
            Vector3 center = bottomCenter + r * new Vector3(0f, rise * 0.5f, run * 0.5f);
            var go = Wedge(parent, center, new Vector3(width, rise, run), c, yRot);
            go.name = "Ramp";
            return go;
        }

        /// <summary>N-sided cone/pyramid (base at the bottom of the box, apex at the top).</summary>
        public static GameObject Cone(Transform parent, Vector3 center, Vector3 size, Color c, int sides = 6, float yRot = 0)
        {
            return Solid("Cone", parent, ConeMesh(sides), center, Quaternion.Euler(0f, yRot, 0f), size, c);
        }

        /// <summary>Upside-down cone (apex pointing down) — handy for the underside of floating islands.</summary>
        public static GameObject Spike(Transform parent, Vector3 center, Vector3 size, Color c, int sides = 5, float yRot = 0)
        {
            var go = Solid("Spike", parent, ConeMesh(sides), center, Quaternion.Euler(180f, yRot, 0f), size, c);
            return go;
        }

        /// <summary>N-sided prism (low-poly cylinder) filling the box <paramref name="size"/>.</summary>
        public static GameObject Prism(Transform parent, Vector3 center, Vector3 size, Color c, int sides = 6, float yRot = 0)
        {
            return Solid("Prism", parent, PrismMesh(sides), center, Quaternion.Euler(0f, yRot, 0f), size, c);
        }

        /// <summary>Upright prism standing on <paramref name="basePos"/>.</summary>
        public static GameObject Pillar(Transform parent, Vector3 basePos, float radius, float height, Color c, int sides = 6, float yRot = 0)
        {
            var go = Prism(parent, basePos + new Vector3(0f, height * 0.5f, 0f), new Vector3(radius * 2f, height, radius * 2f), c, sides, yRot);
            go.name = "Pillar";
            return go;
        }

        /// <summary>Generic solid: MeshFilter + MeshRenderer + MeshCollider + Sliceable.</summary>
        public static GameObject Solid(string name, Transform parent, Mesh mesh, Vector3 center, Quaternion rotation, Vector3 size, Color c)
        {
            var go = Visual(name, parent, mesh, center, rotation, size, c);
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            go.AddComponent<Sliceable>();
            return go;
        }

        /// <summary>Render-only shape: no collider, not sliceable (decals, gameplay prop visuals).</summary>
        public static GameObject Visual(string name, Transform parent, Mesh mesh, Vector3 center, Quaternion rotation, Vector3 size, Color c)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = center;
            t.localRotation = rotation;
            t.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Palette.Get(c);
            return go;
        }

        /// <summary>
        /// Sliceable decoration without a collider (grass tufts, flowers, stepping stones, contact
        /// shadows): cut and captured like terrain, but the player walks through it.
        /// </summary>
        public static GameObject Soft(string name, Transform parent, Mesh mesh, Vector3 center, Quaternion rotation, Vector3 size, Color c)
        {
            var go = Visual(name, parent, mesh, center, rotation, size, c);
            go.AddComponent<Sliceable>();
            return go;
        }

        /// <summary>Render-only box.</summary>
        public static GameObject VisualBox(Transform parent, Vector3 center, Vector3 size, Color c, float yRot = 0)
        {
            return Visual("Deco", parent, CubeMesh, center, Quaternion.Euler(0f, yRot, 0f), size, c);
        }

        // ------------------------------------------------------------------ shared meshes

        static Mesh s_Cube, s_Wedge, s_Disc;
        static readonly Dictionary<int, Mesh> s_Rocks = new Dictionary<int, Mesh>();
        static readonly Dictionary<int, Mesh> s_Cones = new Dictionary<int, Mesh>();
        static readonly Dictionary<int, Mesh> s_Prisms = new Dictionary<int, Mesh>();

        /// <summary>Unit cube centred on the origin (24 vertices, 12 triangles).</summary>
        public static Mesh CubeMesh
        {
            get
            {
                if (s_Cube != null) return s_Cube;
                var b = new MeshBuilder();
                Vector3 c = Vector3.zero;
                Vector3 a0 = new Vector3(-.5f, -.5f, -.5f), a1 = new Vector3(.5f, -.5f, -.5f),
                        a2 = new Vector3(.5f, -.5f, .5f), a3 = new Vector3(-.5f, -.5f, .5f),
                        b0 = new Vector3(-.5f, .5f, -.5f), b1 = new Vector3(.5f, .5f, -.5f),
                        b2 = new Vector3(.5f, .5f, .5f), b3 = new Vector3(-.5f, .5f, .5f);
                b.Face(c, a0, a1, a2, a3); // bottom
                b.Face(c, b0, b1, b2, b3); // top
                b.Face(c, a0, a1, b1, b0); // front (-Z)
                b.Face(c, a3, a2, b2, b3); // back (+Z)
                b.Face(c, a0, a3, b3, b0); // left (-X)
                b.Face(c, a1, a2, b2, b1); // right (+X)
                s_Cube = b.Build("Ion_UnitCube");
                return s_Cube;
            }
        }

        /// <summary>Unit wedge: slope rises toward +Z (18 vertices, 8 triangles).</summary>
        public static Mesh WedgeMesh
        {
            get
            {
                if (s_Wedge != null) return s_Wedge;
                var b = new MeshBuilder();
                Vector3 a0 = new Vector3(-.5f, -.5f, -.5f), a1 = new Vector3(.5f, -.5f, -.5f),
                        a2 = new Vector3(.5f, -.5f, .5f), a3 = new Vector3(-.5f, -.5f, .5f),
                        t2 = new Vector3(.5f, .5f, .5f), t3 = new Vector3(-.5f, .5f, .5f);
                Vector3 c = (a0 + a1 + a2 + a3 + t2 + t3) / 6f;
                b.Face(c, a0, a1, a2, a3);  // bottom
                b.Face(c, a3, a2, t2, t3);  // back
                b.Face(c, a0, a1, t2, t3);  // slope
                b.Face(c, a0, a3, t3);      // left
                b.Face(c, a1, a2, t2);      // right
                s_Wedge = b.Build("Ion_UnitWedge");
                return s_Wedge;
            }
        }

        /// <summary>
        /// Unit 12-sided disc (radius 0.5, y ∈ [−0.5, 0.5]) whose top is fanned from a centre vertex with a
        /// darker vertex colour: multiplied into the albedo it gives a soft radial contact shade with an
        /// invisible rim. Closed (bottom fan + side quads) so it can be cut like any solid.
        /// </summary>
        public static Mesh DiscMesh
        {
            get
            {
                if (s_Disc != null) return s_Disc;
                const int n = 12;
                var v = new List<Vector3>();
                var nr = new List<Vector3>();
                var c = new List<Color32>();
                var t = new List<int>();
                var white = new Color32(255, 255, 255, 255);
                var dark = new Color32(196, 200, 210, 255); // a touch cooler as well as darker
                Vector3[] ring = Ring(n, 0f);
                // Top fan (normal up), centre dark.
                int top = v.Count;
                v.Add(new Vector3(0f, 0.5f, 0f)); nr.Add(Vector3.up); c.Add(dark);
                for (int i = 0; i < n; i++) { v.Add(new Vector3(ring[i].x, 0.5f, ring[i].z)); nr.Add(Vector3.up); c.Add(white); }
                for (int i = 0; i < n; i++) { t.Add(top); t.Add(top + 1 + (i + 1) % n); t.Add(top + 1 + i); }
                // Bottom fan (normal down).
                int bot = v.Count;
                v.Add(new Vector3(0f, -0.5f, 0f)); nr.Add(Vector3.down); c.Add(white);
                for (int i = 0; i < n; i++) { v.Add(new Vector3(ring[i].x, -0.5f, ring[i].z)); nr.Add(Vector3.down); c.Add(white); }
                for (int i = 0; i < n; i++) { t.Add(bot); t.Add(bot + 1 + i); t.Add(bot + 1 + (i + 1) % n); }
                // Sides.
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector3 a0 = new Vector3(ring[i].x, -0.5f, ring[i].z), a1 = new Vector3(ring[j].x, -0.5f, ring[j].z);
                    Vector3 b0 = new Vector3(ring[i].x, 0.5f, ring[i].z), b1 = new Vector3(ring[j].x, 0.5f, ring[j].z);
                    Vector3 sn = new Vector3((a0.x + a1.x) * 0.5f, 0f, (a0.z + a1.z) * 0.5f).normalized;
                    int s0 = v.Count;
                    v.Add(a0); v.Add(a1); v.Add(b1); v.Add(b0);
                    // The rim is only ~1 cm tall: shade it like the grass top (normal up) so it never
                    // shows as a dark outline at grazing angles.
                    for (int k = 0; k < 4; k++) { nr.Add(Vector3.up); c.Add(white); }
                    // Front face: normal = cross(b - a, c - a) must point along sn.
                    if (Vector3.Dot(Vector3.Cross(a1 - a0, b1 - a0), sn) > 0f) { t.Add(s0); t.Add(s0 + 1); t.Add(s0 + 2); t.Add(s0); t.Add(s0 + 2); t.Add(s0 + 3); }
                    else { t.Add(s0); t.Add(s0 + 2); t.Add(s0 + 1); t.Add(s0); t.Add(s0 + 3); t.Add(s0 + 2); }
                }
                var m = new Mesh { name = "Ion_ContactDisc" };
                m.SetVertices(v);
                m.SetNormals(nr);
                m.SetColors(c);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                m.UploadMeshData(false);
                s_Disc = m;
                return m;
            }
        }

        /// <summary>Unit cone: n-gon base at y = −0.5 (radius 0.5), apex at y = +0.5.</summary>
        public static Mesh ConeMesh(int sides)
        {
            sides = Mathf.Clamp(sides, 3, 32);
            if (s_Cones.TryGetValue(sides, out var m) && m != null) return m;
            var b = new MeshBuilder();
            var ring = Ring(sides, -0.5f);
            Vector3 apex = new Vector3(0f, 0.5f, 0f);
            Vector3 c = new Vector3(0f, -0.25f, 0f);
            b.Face(c, ring);
            for (int i = 0; i < sides; i++)
                b.Face(c, ring[i], ring[(i + 1) % sides], apex);
            m = b.Build("Ion_UnitCone" + sides);
            s_Cones[sides] = m;
            return m;
        }

        /// <summary>Unit prism: n-gon (radius 0.5) extruded from y = −0.5 to +0.5.</summary>
        public static Mesh PrismMesh(int sides)
        {
            sides = Mathf.Clamp(sides, 3, 32);
            if (s_Prisms.TryGetValue(sides, out var m) && m != null) return m;
            var b = new MeshBuilder();
            var lo = Ring(sides, -0.5f);
            var hi = Ring(sides, 0.5f);
            Vector3 c = Vector3.zero;
            b.Face(c, lo);
            b.Face(c, hi);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                b.Face(c, lo[i], lo[j], hi[j], hi[i]);
            }
            m = b.Build("Ion_UnitPrism" + sides);
            s_Prisms[sides] = m;
            return m;
        }

        /// <summary>
        /// Unit faceted boulder (fits the unit box, base at y = −0.5): a jittered 7-gon base, a wider
        /// shoulder ring rotated half a step and a small tilted top facet, all triangles. Convex for the
        /// small jitter used, so it cuts cleanly. <paramref name="variant"/> picks one of a few shapes.
        /// </summary>
        public static Mesh RockMesh(int variant)
        {
            variant = Mathf.Abs(variant) % 4;
            if (s_Rocks.TryGetValue(variant, out var m) && m != null) return m;
            var rng = new System.Random(9173 + variant * 131);
            float J() => (float)rng.NextDouble();
            const int n = 7;
            var lo = new Vector3[n];
            var mid = new Vector3[n];
            var hi = new Vector3[4];
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.15f * J()) * Mathf.PI * 2f / n;
                float r = 0.42f + 0.05f * J();
                lo[i] = new Vector3(Mathf.Cos(a) * r, -0.5f, Mathf.Sin(a) * r);
                float b = (i + 0.5f) * Mathf.PI * 2f / n;
                float rm = 0.48f + 0.02f * J();
                mid[i] = new Vector3(Mathf.Cos(b) * rm, -0.12f + 0.12f * J(), Mathf.Sin(b) * rm);
            }
            float tilt = 0.08f * (J() - 0.5f);
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.3f) * Mathf.PI * 0.5f;
                float r = 0.2f + 0.05f * J();
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                hi[i] = new Vector3(x + 0.04f, 0.5f - 0.04f + tilt * x * 4f, z);
            }
            var bld = new MeshBuilder();
            Vector3 c = new Vector3(0f, -0.1f, 0f);
            bld.Face(c, lo);
            // Base ring -> shoulder ring (antiprism band).
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                bld.Face(c, lo[i], lo[j], mid[i]);
                bld.Face(c, mid[i], lo[j], mid[j]);
            }
            // Shoulder ring -> top facet: a closed triangle strip, merged by angle around the Y axis.
            StripBetween(bld, c, mid, hi);
            bld.Face(c, hi);
            m = bld.Build("Ion_Rock" + variant);
            s_Rocks[variant] = m;
            return m;
        }

        /// <summary>Triangulates the band between two convex rings (any vertex counts) around the Y axis.</summary>
        static void StripBetween(MeshBuilder bld, Vector3 interior, Vector3[] ringA, Vector3[] ringB)
        {
            float[] angA = Unwrapped(ringA, out int startA);
            float[] angB = Unwrapped(ringB, out int startB);
            float baseAng = Mathf.Min(angA[0], angB[0]);
            int na = ringA.Length, nb = ringB.Length, i = 0, k = 0;
            while (i < na || k < nb)
            {
                Vector3 a = ringA[(startA + i) % na], b = ringB[(startB + k) % nb];
                float nextA = i < na ? angA[i + 1] - baseAng : float.MaxValue;
                float nextB = k < nb ? angB[k + 1] - baseAng : float.MaxValue;
                if (nextA <= nextB) { bld.Face(interior, a, ringA[(startA + i + 1) % na], b); i++; }
                else { bld.Face(interior, a, ringB[(startB + k + 1) % nb], b); k++; }
            }
        }

        /// <summary>Angles (radians) of a ring's vertices starting at the smallest one, increasing, with the first repeated + 2π at the end.</summary>
        static float[] Unwrapped(Vector3[] ring, out int start)
        {
            int n = ring.Length;
            var raw = new float[n];
            start = 0;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Atan2(ring[i].z, ring[i].x);
                if (a < 0f) a += Mathf.PI * 2f;
                raw[i] = a;
                if (a < raw[start]) start = i;
            }
            var result = new float[n + 1];
            float prev = raw[start];
            for (int i = 0; i <= n; i++)
            {
                float a = raw[(start + i) % n];
                while (a < prev - 1e-5f) a += Mathf.PI * 2f;
                if (i == n) a = raw[start] + Mathf.PI * 2f;
                result[i] = a;
                prev = a;
            }
            return result;
        }

        static Vector3[] Ring(int sides, float y)
        {
            var pts = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / sides;
                pts[i] = new Vector3(Mathf.Cos(a) * 0.5f, y, Mathf.Sin(a) * 0.5f);
            }
            return pts;
        }

        /// <summary>Accumulates flat-shaded convex polygons (each face gets its own vertices).</summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<Color32> _c = new List<Color32>();
            readonly List<int> _t = new List<int>();

            /// <summary>
            /// Adds a planar convex polygon (points in perimeter order, either direction). It is
            /// oriented so its normal points away from <paramref name="interior"/> and fan-triangulated
            /// with Unity's clockwise front-face winding.
            /// </summary>
            public void Face(Vector3 interior, params Vector3[] pts)
            {
                Vector3 n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]).normalized;
                Vector3 centroid = Vector3.zero;
                for (int i = 0; i < pts.Length; i++) centroid += pts[i];
                centroid /= pts.Length;
                bool flip = Vector3.Dot(n, centroid - interior) < 0f;
                if (flip) n = -n;

                int start = _v.Count;
                for (int i = 0; i < pts.Length; i++)
                {
                    _v.Add(flip ? pts[pts.Length - 1 - i] : pts[i]);
                    _n.Add(n);
                    _c.Add(new Color32(255, 255, 255, 255)); // multiplier: decor bakes tint / contact shade here
                }
                for (int i = 1; i < pts.Length - 1; i++)
                {
                    _t.Add(start);
                    _t.Add(start + i);
                    _t.Add(start + i + 1);
                }
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_v);
                m.SetNormals(_n);
                m.SetColors(_c);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                m.UploadMeshData(false); // keep readable: the slicer and MeshCollider read it
                return m;
            }
        }
    }
}
