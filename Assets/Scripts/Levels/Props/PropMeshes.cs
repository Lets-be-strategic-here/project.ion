using System.Collections.Generic;
using UnityEngine;

namespace Ion.Levels.Props
{
    /// <summary>
    /// Shared unit meshes for props that the Arch primitives do not cover: plant parts (closed and convex, so the
    /// slicer caps them like any solid) and the image quad of a print. Flat (split) normals, white vertex colours,
    /// TEXCOORD0 = (0,0,0,0) (Pat.None) like Geo's shared meshes. Created once and shared.
    /// </summary>
    internal static class PropMeshes
    {
        static Mesh s_Leaf, s_Puff, s_Quad, s_Blade;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Leaf = s_Puff = s_Quad = s_Blade = null;
        }

        /// <summary>
        /// Unit leaf: a squashed octahedron along +Y (y ∈ [−0.5, 0.5], tips at both ends), widest at y = −0.1,
        /// x ∈ [−0.5, 0.5], z ∈ [−0.5, 0.5]. Scale z small for a flat blade. 8 faces, convex and closed.
        /// </summary>
        public static Mesh Leaf
        {
            get
            {
                if (s_Leaf != null) return s_Leaf;
                var b = new ConvexBuilder();
                Vector3 bot = new Vector3(0f, -0.5f, 0f), top = new Vector3(0f, 0.5f, 0f);
                Vector3 l = new Vector3(-0.5f, -0.1f, 0f), r = new Vector3(0.5f, -0.1f, 0f);
                Vector3 f = new Vector3(0f, -0.1f, 0.5f), k = new Vector3(0f, -0.1f, -0.5f);
                Vector3 c = new Vector3(0f, -0.1f, 0f);
                b.Face(c, top, l, f); b.Face(c, top, f, r); b.Face(c, top, r, k); b.Face(c, top, k, l);
                b.Face(c, bot, l, f); b.Face(c, bot, f, r); b.Face(c, bot, r, k); b.Face(c, bot, k, l);
                s_Leaf = b.Build("Ion_PropLeaf");
                return s_Leaf;
            }
        }

        /// <summary>
        /// Unit "puff" (shrub mass): an 8-gon bun, y ∈ [−0.5, 0.5], radius 0.5 at the shoulder. The radius profile
        /// is concave in y (0.32, 0.5, 0.44, 0.2), so the solid is convex.
        /// </summary>
        public static Mesh Puff
        {
            get
            {
                if (s_Puff != null) return s_Puff;
                const int n = 8;
                float[] ys = { -0.5f, -0.15f, 0.2f, 0.5f };
                float[] rs = { 0.32f, 0.5f, 0.44f, 0.2f };
                var rings = new Vector3[ys.Length][];
                for (int k = 0; k < ys.Length; k++)
                {
                    rings[k] = new Vector3[n];
                    float off = (k & 1) == 0 ? 0f : 0.5f;   // alternate rings half a step: faceted, not a lathe
                    for (int i = 0; i < n; i++)
                    {
                        float a = (i + off) * Mathf.PI * 2f / n;
                        rings[k][i] = new Vector3(Mathf.Cos(a) * rs[k], ys[k], Mathf.Sin(a) * rs[k]);
                    }
                }
                var b = new ConvexBuilder();
                Vector3 c = new Vector3(0f, -0.05f, 0f);
                b.Face(c, rings[0]);
                b.Face(c, rings[ys.Length - 1]);
                for (int k = 0; k + 1 < ys.Length; k++)
                {
                    Vector3[] lo = rings[k], hi = rings[k + 1];
                    bool loLeads = (k & 1) == 0;   // the ring with offset 0 leads
                    for (int i = 0; i < n; i++)
                    {
                        int j = (i + 1) % n;
                        if (loLeads)
                        {
                            b.Face(c, lo[i], lo[j], hi[i]);
                            b.Face(c, hi[i], lo[j], hi[j]);
                        }
                        else
                        {
                            // lo[i] sits between hi[i] and hi[j]; hi[j] between lo[i] and lo[j].
                            b.Face(c, hi[i], hi[j], lo[i]);
                            b.Face(c, lo[i], hi[j], lo[j]);
                        }
                    }
                }
                s_Puff = b.Build("Ion_PropPuff");
                return s_Puff;
            }
        }

        /// <summary>Unit grass blade: a thin triangular pyramid (base y = −0.5, tip y = 0.5).</summary>
        public static Mesh Blade
        {
            get
            {
                if (s_Blade != null) return s_Blade;
                var b = new ConvexBuilder();
                Vector3 a0 = new Vector3(-0.5f, -0.5f, -0.25f), a1 = new Vector3(0.5f, -0.5f, -0.25f), a2 = new Vector3(0f, -0.5f, 0.5f);
                Vector3 tip = new Vector3(0f, 0.5f, 0.05f);
                Vector3 c = (a0 + a1 + a2 + tip) * 0.25f;
                b.Face(c, a0, a1, a2);
                b.Face(c, a0, a1, tip);
                b.Face(c, a1, a2, tip);
                b.Face(c, a2, a0, tip);
                s_Blade = b.Build("Ion_PropBlade");
                return s_Blade;
            }
        }

        /// <summary>
        /// Unit image quad in the XY plane (x, y ∈ [−0.5, 0.5]), facing +Z, UV0 = (u, v) such that the image reads
        /// correctly for a viewer at +Z looking along −Z (u grows toward −X). Drawn with Ion/PhotoDisplay (Cull Off).
        /// </summary>
        public static Mesh ImageQuad
        {
            get
            {
                if (s_Quad != null) return s_Quad;
                var m = new Mesh { name = "Ion_PrintQuad" };
                m.SetVertices(new List<Vector3>
                {
                    new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
                });
                m.SetNormals(new List<Vector3> { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
                m.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                var white = new Color32(255, 255, 255, 255);
                m.SetColors(new List<Color32> { white, white, white, white });
                // Clockwise seen from +Z (Unity front face).
                m.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0);
                m.RecalculateBounds();
                m.UploadMeshData(false);
                s_Quad = m;
                return m;
            }
        }

        /// <summary>Accumulates flat-shaded convex polygons (each face its own vertices), oriented away from an interior point.</summary>
        sealed class ConvexBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<int> _t = new List<int>();

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
                // Same winding as Geo's builder: the front face is the side cross(p1 - p0, p2 - p0) points to.
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
                var c = new List<Color32>(_v.Count);
                var uv = new List<Vector4>(_v.Count);
                for (int i = 0; i < _v.Count; i++)
                {
                    c.Add(new Color32(255, 255, 255, 255));
                    uv.Add(Vector4.zero);
                }
                m.SetColors(c);
                m.SetUVs(0, uv);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                m.UploadMeshData(false); // readable: the slicer and DecorCombiner read it
                return m;
            }
        }
    }
}
