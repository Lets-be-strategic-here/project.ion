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

        /// <summary>Render-only box.</summary>
        public static GameObject VisualBox(Transform parent, Vector3 center, Vector3 size, Color c, float yRot = 0)
        {
            return Visual("Deco", parent, CubeMesh, center, Quaternion.Euler(0f, yRot, 0f), size, c);
        }

        // ------------------------------------------------------------------ shared meshes

        static Mesh s_Cube, s_Wedge;
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
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                m.UploadMeshData(false); // keep readable: the slicer and MeshCollider read it
                return m;
            }
        }
    }
}
