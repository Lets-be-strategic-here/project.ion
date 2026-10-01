using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Ion.Tests
{
    /// <summary>Mesh builders and geometric measurements shared by the projection tests.</summary>
    public static class TestGeometry
    {
        /// <summary>Axis-aligned box with 24 split vertices (hard normals), like Unity's cube.</summary>
        public static Mesh Box(Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = center + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
            return Convex("Box", new[]
            {
                new[] { c[0], c[2], c[6], c[4] }, // -x
                new[] { c[1], c[3], c[7], c[5] }, // +x
                new[] { c[0], c[1], c[5], c[4] }, // -y
                new[] { c[2], c[3], c[7], c[6] }, // +y
                new[] { c[0], c[1], c[3], c[2] }, // -z
                new[] { c[4], c[5], c[7], c[6] }, // +z
            });
        }

        /// <summary>Triangular prism (ramp): the slope rises along +z.</summary>
        public static Mesh Wedge(Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3 a0 = center + new Vector3(-h.x, -h.y, -h.z), a1 = center + new Vector3(h.x, -h.y, -h.z);
            Vector3 b0 = center + new Vector3(-h.x, -h.y, h.z), b1 = center + new Vector3(h.x, -h.y, h.z);
            Vector3 t0 = center + new Vector3(-h.x, h.y, h.z), t1 = center + new Vector3(h.x, h.y, h.z);
            return Convex("Wedge", new[]
            {
                new[] { a0, a1, b1, b0 }, // bottom
                new[] { b0, b1, t1, t0 }, // back
                new[] { a0, a1, t1, t0 }, // slope
                new[] { a0, b0, t0 },     // left
                new[] { a1, b1, t1 },     // right
            });
        }

        /// <summary>
        /// Builds a closed convex mesh from planar convex face polygons (any winding); each face gets its
        /// own vertices with a flat outward normal and Unity front-face winding.
        /// </summary>
        public static Mesh Convex(string name, Vector3[][] faces)
        {
            Vector3 solidCenter = Vector3.zero;
            int n = 0;
            foreach (var f in faces) foreach (var p in f) { solidCenter += p; n++; }
            solidCenter /= n;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            foreach (var f in faces)
            {
                Vector3 fc = Vector3.zero;
                foreach (var p in f) fc += p;
                fc /= f.Length;
                Vector3 normal = Vector3.Cross(f[1] - f[0], f[2] - f[0]).normalized;
                bool flip = Vector3.Dot(normal, fc - solidCenter) < 0f;
                if (flip) normal = -normal;
                int baseIndex = verts.Count;
                foreach (var p in f) { verts.Add(p); normals.Add(normal); }
                for (int k = 1; k + 1 < f.Length; k++)
                {
                    tris.Add(baseIndex);
                    if (flip) { tris.Add(baseIndex + k + 1); tris.Add(baseIndex + k); }
                    else { tris.Add(baseIndex + k); tris.Add(baseIndex + k + 1); }
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Signed volume via the divergence theorem (sum of origin tetrahedra), in mesh space.</summary>
        public static float SignedVolume(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            double sum = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] t = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    sum += Vector3.Dot(a, Vector3.Cross(b, c));
                }
            }
            return (float)(sum / 6.0);
        }

        public static float SignedVolume(IEnumerable<Mesh> meshes)
        {
            float sum = 0f;
            foreach (var m in meshes) sum += SignedVolume(m);
            return sum;
        }

        /// <summary>
        /// Asserts the mesh is a closed, consistently oriented 2-manifold after welding positions:
        /// every undirected edge is used by exactly two triangles, once in each direction.
        /// The default weld is exact: the clipper produces bit-identical positions for coincident
        /// points, while legitimately distinct sliver points may lie closer than any tolerance.
        /// </summary>
        /// <summary>Signed volume of one element of a merged mesh (see Ion.Projection.MeshElements).</summary>
        public static float ElementVolume(Ion.Projection.MeshElements el, int e)
        {
            Vector3[] p = el.Mesh.vertices;
            int[] t = el.Mesh.GetIndices(0);
            double v = 0;
            int t0 = el.IndexStart[e], tn = el.IndexCount[e];
            for (int i = t0; i + 2 < t0 + tn; i += 3)
                v += Vector3.Dot(p[t[i]], Vector3.Cross(p[t[i + 1]], p[t[i + 2]]));
            return (float)(v / 6.0);
        }

        public static void AssertWatertight(Mesh mesh, float weld = 0f)
        {
            Vector3[] v = mesh.vertices;
            var ids = new int[v.Length];
            var unique = new List<Vector3>();
            for (int i = 0; i < v.Length; i++)
            {
                int found = -1;
                for (int j = 0; j < unique.Count; j++)
                    if ((unique[j] - v[i]).sqrMagnitude <= weld * weld) { found = j; break; }
                if (found < 0) { found = unique.Count; unique.Add(v[i]); }
                ids[i] = found;
            }

            var directed = new Dictionary<long, int>();
            int triangles = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] t = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    triangles++;
                    int a = ids[t[i]], b = ids[t[i + 1]], c = ids[t[i + 2]];
                    Assert.IsTrue(a != b && b != c && c != a, $"{mesh.name}: degenerate triangle after welding");
                    Count(directed, a, b);
                    Count(directed, b, c);
                    Count(directed, c, a);
                }
            }
            Assert.Greater(triangles, 0, $"{mesh.name}: no triangles");
            foreach (var kv in directed)
            {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
                Assert.AreEqual(1, kv.Value, $"{mesh.name}: directed edge {unique[a]}->{unique[b]} used {kv.Value} times");
                long reverse = ((long)b << 32) | (uint)a;
                Assert.IsTrue(directed.TryGetValue(reverse, out int r) && r == 1,
                    $"{mesh.name}: edge {unique[a]}->{unique[b]} has no matching reverse edge (open boundary)");
            }
        }

        static void Count(Dictionary<long, int> d, int a, int b)
        {
            long key = ((long)a << 32) | (uint)b;
            d.TryGetValue(key, out int n);
            d[key] = n + 1;
        }

        /// <summary>Asserts every vertex lies inside (or on) all planes, given in mesh-local = world space.</summary>
        public static void AssertInsideAll(Mesh mesh, Matrix4x4 localToWorld, Plane[] planes, float eps = 1e-3f)
        {
            foreach (var p in mesh.vertices)
            {
                Vector3 w = localToWorld.MultiplyPoint3x4(p);
                foreach (var pl in planes)
                    Assert.GreaterOrEqual(pl.GetDistanceToPoint(w), -eps, $"{mesh.name}: vertex {w} outside a plane");
            }
        }

        public static void DestroyAll(IEnumerable<Mesh> meshes)
        {
            foreach (var m in meshes) if (m != null) Object.DestroyImmediate(m);
        }
    }
}
