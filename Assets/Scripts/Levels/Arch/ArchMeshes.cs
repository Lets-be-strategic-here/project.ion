using System.Collections.Generic;
using Ion.Projection;
using UnityEngine;

namespace Ion.Levels.Arch
{
    /// <summary>Real-size meshes the kit cannot get from Geo's unit meshes (cached, readable, closed and convex).</summary>
    internal static class ArchMeshes
    {
        static readonly Dictionary<long, Mesh> s_Chamfered = new Dictionary<long, Mesh>();

        /// <summary>
        /// A box of <paramref name="size"/> centred on the origin with every edge and corner chamfered by
        /// <paramref name="chamfer"/> (6 + 12 + 8 = 26 faces). Built by clipping a box with the 20 chamfer planes,
        /// so it is closed, convex and flat-shaded like every other piece. UV0 = 0 (Bake writes the pattern space).
        /// </summary>
        public static Mesh Chamfered(Vector3 size, float chamfer)
        {
            Vector3 e = Vector3.Max(size, Vector3.one * 1e-3f) * 0.5f;
            float c = Mathf.Clamp(chamfer, 0f, Mathf.Min(e.x, Mathf.Min(e.y, e.z)) * 0.9f);
            long key = Quant(e.x) * 73856093L ^ Quant(e.y) * 19349663L ^ Quant(e.z) * 83492791L ^ Quant(c) * 2654435761L;
            if (s_Chamfered.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            Mesh box = BoxMesh(e);
            Mesh result = box;
            if (c > 1e-5f)
            {
                var planes = new List<Plane>(20);
                // 12 edges: normals (±1, ±1, 0)/√2 etc., cutting c from both faces of the edge.
                for (int a = 0; a < 3; a++)
                {
                    int b = (a + 1) % 3;
                    for (int sa = -1; sa <= 1; sa += 2)
                    for (int sb = -1; sb <= 1; sb += 2)
                    {
                        var n = Vector3.zero;
                        n[a] = sa;
                        n[b] = sb;
                        float d = e[a] + e[b] - c;                       // n·x <= d (unnormalised)
                        planes.Add(Inward(n, d));
                    }
                }
                // 8 corners: x + y + z <= ex + ey + ez - 2c (with signs).
                for (int i = 0; i < 8; i++)
                {
                    var n = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                    planes.Add(Inward(n, e.x + e.y + e.z - 2f * c));
                }
                Mesh clipped = MeshClipper.ClipInside(box, Matrix4x4.identity, planes.ToArray());
                if (clipped != null)
                {
                    Object.DestroyImmediate(box);
                    result = clipped;
                }
            }
            // Caps came out flagged as cut faces; a chamfer is not a cut: reset UV0 to "no pattern".
            Geo.SetNoPattern(result, result.vertexCount);
            result.name = "Ion_ChamferBox";
            result.RecalculateBounds();
            result.UploadMeshData(false);
            s_Chamfered[key] = result;
            return result;
        }

        /// <summary>Plane with normal pointing INTO the kept volume for the half-space n·x &lt;= d.</summary>
        static Plane Inward(Vector3 n, float d)
        {
            float len = n.magnitude;
            return new Plane(-n / len, d / len);
        }

        static long Quant(float v) => (long)Mathf.Round(v * 1024f);

        static Mesh BoxMesh(Vector3 e)
        {
            var v = new List<Vector3>(24);
            var n = new List<Vector3>(24);
            var t = new List<int>(36);
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int a = 0; a < 3; a++)
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector3 nn = axes[a] * sgn, u = axes[(a + 1) % 3], w = axes[(a + 2) % 3];
                int i0 = v.Count;
                Vector3 fc = Vector3.Scale(nn, e);
                Vector3 du = Vector3.Scale(u, e), dw = Vector3.Scale(w, e);
                v.Add(fc - du - dw); v.Add(fc - du + dw); v.Add(fc + du + dw); v.Add(fc + du - dw);
                for (int k = 0; k < 4; k++) n.Add(nn);
                if (Vector3.Dot(Vector3.Cross(v[i0 + 1] - v[i0], v[i0 + 2] - v[i0]), nn) > 0f) { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
                else { t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2); }
            }
            var colors = new List<Color32>(24);
            for (int i = 0; i < v.Count; i++) colors.Add(new Color32(255, 255, 255, 255));
            var m = new Mesh { name = "Ion_ChamferSource" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetColors(colors);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
