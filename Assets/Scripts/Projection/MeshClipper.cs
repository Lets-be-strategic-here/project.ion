using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Projection
{
    /// <summary>
    /// Pure-geometry mesh clipping against convex volumes given as world planes whose normals
    /// point INTO the volume.
    ///
    /// All work happens in the mesh's local space (the world planes are transformed into it), so
    /// returned meshes are in the same local space as the input and can be drawn with the same
    /// transform. Every plane pass is a Sutherland–Hodgman clip of each triangle with position and
    /// normal interpolation, followed by a cap: the open boundary left on the plane is chained into
    /// loops (for a convex piece: the cross-section polygon in angular order around its centroid) and
    /// each loop is fan-triangulated from its centroid. If the boundary is degenerate, the cut points
    /// are instead welded, sorted by angle around their centroid and fanned. Caps go into submesh 0
    /// with the plane normal facing out of the kept piece. Pieces of closed convex input stay closed
    /// and convex (the levels only use convex primitives).
    ///
    /// Single-threaded: uses static scratch buffers to stay allocation-light.
    /// </summary>
    public static class MeshClipper
    {
        public enum Classification
        {
            /// <summary>The mesh lies entirely inside the convex volume.</summary>
            Inside,
            /// <summary>The mesh lies entirely outside the convex volume.</summary>
            Outside,
            /// <summary>The volume's boundary cuts through the mesh.</summary>
            Straddling,
        }

        /// <summary>Signed distance below which a vertex counts as lying on a plane (local units).</summary>
        public const float SideEpsilon = 1e-5f;
        /// <summary>
        /// Distance below which two cut points are merged by the fallback (angle-sorted) cap. Coincident
        /// cut points are bit-identical by construction (canonical interpolation order), so this only
        /// needs to absorb exact duplicates.
        /// </summary>
        public const float WeldEpsilon = 1e-6f;

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Part of <paramref name="mesh"/> inside the convex volume, in the mesh's local space.
        /// Returns null if nothing is inside, and a plain copy if everything is.
        /// </summary>
        public static Mesh ClipInside(Mesh mesh, Matrix4x4 localToWorld, Plane[] worldPlanes)
        {
            if (mesh == null) return null;
            if (worldPlanes == null || worldPlanes.Length == 0) return CopyMesh(mesh);

            Classification fast = ClassifyBounds(mesh.bounds, localToWorld, worldPlanes);
            if (fast == Classification.Outside) return null;
            if (fast == Classification.Inside) return CopyMesh(mesh);
            if (!CheckReadable(mesh)) return null;

            int planeCount = ToLocalPlanes(localToWorld, worldPlanes);
            MeshData cur = s_bufA, tmp = s_bufB;
            cur.Load(mesh);
            bool clipped = false;
            for (int i = 0; i < planeCount; i++)
            {
                Vector4 p = s_localPlanes[i];
                PassResult r = ClipPass(cur, new Vector3(p.x, p.y, p.z), p.w, tmp);
                if (r == PassResult.Empty) return null;
                if (r == PassResult.Clipped)
                {
                    (cur, tmp) = (tmp, cur);
                    clipped = true;
                }
            }
            if (!clipped) return CopyMesh(mesh);
            return cur.ToMesh(mesh.name + " (in)");
        }

        /// <summary>
        /// Convex decomposition of the part of <paramref name="mesh"/> outside the convex volume:
        /// piece_k = mesh ∩ inside(p1..p(k-1)) ∩ outside(pk); empty pieces are skipped.
        /// A mesh entirely outside yields one plain copy; a mesh entirely inside yields an empty list.
        /// </summary>
        public static List<Mesh> ClipOutside(Mesh mesh, Matrix4x4 localToWorld, Plane[] worldPlanes)
        {
            var results = new List<Mesh>();
            if (ClipOutside(mesh, localToWorld, worldPlanes, results) == Classification.Outside && mesh != null)
                results.Add(CopyMesh(mesh));
            return results;
        }

        /// <summary>
        /// Non-allocating-list variant of <see cref="ClipOutside(Mesh, Matrix4x4, Plane[])"/>.
        /// Appends the outside pieces to <paramref name="results"/> only when the result is
        /// <see cref="Classification.Straddling"/>; for Inside and Outside nothing is appended, so the
        /// caller can keep (Outside) or drop (Inside) the original object without copying it.
        /// </summary>
        public static Classification ClipOutside(Mesh mesh, Matrix4x4 localToWorld, Plane[] worldPlanes, List<Mesh> results)
        {
            if (mesh == null) return Classification.Outside;
            if (worldPlanes == null || worldPlanes.Length == 0) return Classification.Inside;

            Classification fast = ClassifyBounds(mesh.bounds, localToWorld, worldPlanes);
            if (fast != Classification.Straddling) return fast;
            if (!CheckReadable(mesh)) return Classification.Outside;

            int planeCount = ToLocalPlanes(localToWorld, worldPlanes);
            MeshData cur = s_bufA, outside = s_bufB, inside = s_bufC;
            cur.Load(mesh);
            bool cut = false;
            for (int i = 0; i < planeCount; i++)
            {
                Vector4 p = s_localPlanes[i];
                var n = new Vector3(p.x, p.y, p.z);

                PassResult r = ClipPass(cur, -n, -p.w, outside);
                if (r == PassResult.Empty) continue; // nothing of the remainder is outside this plane
                if (r == PassResult.Unchanged)
                {
                    // The whole remainder is outside this plane.
                    if (!cut) return Classification.Outside;
                    results.Add(cur.ToMesh(mesh.name + " (cut " + i + ")"));
                    return Classification.Straddling;
                }

                results.Add(outside.ToMesh(mesh.name + " (cut " + i + ")"));
                cut = true;

                PassResult r2 = ClipPass(cur, n, p.w, inside);
                if (r2 == PassResult.Empty) return Classification.Straddling;
                if (r2 == PassResult.Clipped) (cur, inside) = (inside, cur);
            }
            return cut ? Classification.Straddling : Classification.Inside;
        }

        /// <summary>
        /// Conservative classification of a local-space bounding box against the convex volume:
        /// Inside / Outside are exact claims, Straddling means "may cross the boundary".
        /// </summary>
        public static Classification ClassifyBounds(Bounds localBounds, Matrix4x4 localToWorld, Plane[] worldPlanes)
        {
            Vector3 c = localBounds.center, e = localBounds.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                s_corners[i] = localToWorld.MultiplyPoint3x4(corner);
            }

            bool allInside = true;
            for (int p = 0; p < worldPlanes.Length; p++)
            {
                Plane plane = worldPlanes[p];
                int outsideCount = 0;
                for (int i = 0; i < 8; i++)
                {
                    float d = plane.GetDistanceToPoint(s_corners[i]);
                    if (d <= SideEpsilon) outsideCount++;
                    if (d < -SideEpsilon) allInside = false;
                }
                if (outsideCount == 8) return Classification.Outside;
            }
            return allInside ? Classification.Inside : Classification.Straddling;
        }

        // ------------------------------------------------------------------ internals

        enum PassResult { Unchanged, Clipped, Empty }

        /// <summary>Indexed triangle soup with per-vertex normals and several submeshes.</summary>
        sealed class MeshData
        {
            public readonly List<Vector3> P = new List<Vector3>(256);
            public readonly List<Vector3> N = new List<Vector3>(256);
            public readonly List<List<int>> T = new List<List<int>>();
            public int SubCount;
            public bool HasNormals;

            public void Reset(int subCount)
            {
                P.Clear();
                N.Clear();
                SubCount = Mathf.Max(1, subCount);
                while (T.Count < SubCount) T.Add(new List<int>(256));
                for (int i = 0; i < T.Count; i++) T[i].Clear();
            }

            public void Load(Mesh mesh)
            {
                Reset(mesh.subMeshCount);
                mesh.GetVertices(P);
                mesh.GetNormals(N);
                HasNormals = N.Count == P.Count;
                if (!HasNormals)
                {
                    N.Clear();
                    for (int i = 0; i < P.Count; i++) N.Add(Vector3.zero);
                }
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (mesh.GetTopology(s) == MeshTopology.Triangles) mesh.GetTriangles(T[s], s, true);
                }
            }

            public bool HasTriangles()
            {
                for (int s = 0; s < SubCount; s++) if (T[s].Count > 0) return true;
                return false;
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (P.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(P);
                mesh.SetNormals(N);
                mesh.subMeshCount = SubCount;
                for (int s = 0; s < SubCount; s++) mesh.SetTriangles(T[s], s, false);
                mesh.RecalculateBounds();
                if (!HasNormals) mesh.RecalculateNormals();
                return mesh;
            }
        }

        static readonly MeshData s_bufA = new MeshData(), s_bufB = new MeshData(), s_bufC = new MeshData();
        static readonly Vector3[] s_corners = new Vector3[8];
        static Vector4[] s_localPlanes = new Vector4[8];

        // Per-pass scratch.
        static float[] s_dist = new float[256];
        static sbyte[] s_side = new sbyte[256];
        static int[] s_remap = new int[256];
        static readonly Dictionary<long, int> s_edgeCache = new Dictionary<long, int>(128);
        static readonly List<int> s_poly = new List<int>(8);
        static readonly List<bool> s_dstOnPlane = new List<bool>(256);
        // Cut points (for the angle-sort fallback cap) and directed on-plane edges of the kept surface.
        static readonly List<Vector3> s_cut = new List<Vector3>(64);
        static readonly List<Edge> s_cutEdges = new List<Edge>(64);
        static readonly Dictionary<Edge, int> s_openEdges = new Dictionary<Edge, int>(64, new EdgeComparer());
        static readonly Dictionary<Vector3, Vector3> s_next = new Dictionary<Vector3, Vector3>(64, new ExactVector3Comparer());
        static readonly List<Vector3> s_loop = new List<Vector3>(64);
        static readonly List<int> s_loopStarts = new List<int>(4);
        static Vector3[] s_capPoints = new Vector3[64];
        static float[] s_capAngles = new float[64];

        static MeshData s_src, s_dst; // the buffers of the pass in progress

        struct Edge
        {
            public Vector3 From, To;
            public Edge(Vector3 from, Vector3 to) { From = from; To = to; }
        }

        /// <summary>Exact (bitwise-value) position equality; coincident cut points are bit-identical by construction.</summary>
        sealed class ExactVector3Comparer : IEqualityComparer<Vector3>
        {
            public bool Equals(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
            public int GetHashCode(Vector3 v) => Hash(v);
            // "+ 0f" folds -0 into +0 so equal values hash equally.
            public static int Hash(Vector3 v)
            {
                unchecked { return (((v.x + 0f).GetHashCode() * 397) ^ (v.y + 0f).GetHashCode()) * 397 ^ (v.z + 0f).GetHashCode(); }
            }
        }

        sealed class EdgeComparer : IEqualityComparer<Edge>
        {
            public bool Equals(Edge a, Edge b) =>
                a.From.x == b.From.x && a.From.y == b.From.y && a.From.z == b.From.z &&
                a.To.x == b.To.x && a.To.y == b.To.y && a.To.z == b.To.z;
            public int GetHashCode(Edge e) { unchecked { return ExactVector3Comparer.Hash(e.From) * 31 + ExactVector3Comparer.Hash(e.To); } }
        }

        static bool CheckReadable(Mesh mesh)
        {
            if (mesh.isReadable) return true;
            Debug.LogWarning("MeshClipper: mesh '" + mesh.name + "' is not readable and cannot be cut.");
            return false;
        }

        static Mesh CopyMesh(Mesh mesh)
        {
            Mesh copy = Object.Instantiate(mesh);
            copy.name = mesh.name;
            return copy;
        }

        /// <summary>
        /// Transforms world planes into local space: for x_w = M x_l, n·x_w + d = (Mᵀn)·x_l + (n·t + d).
        /// The local normal Mᵀn is also exactly the local normal that renders as n in world space
        /// (normals transform by M⁻ᵀ), which keeps cap normals right under non-uniform scale.
        /// </summary>
        static int ToLocalPlanes(Matrix4x4 m, Plane[] worldPlanes)
        {
            int count = worldPlanes.Length;
            if (s_localPlanes.Length < count) s_localPlanes = new Vector4[count];
            var t = new Vector3(m.m03, m.m13, m.m23);
            for (int i = 0; i < count; i++)
            {
                Vector3 n = worldPlanes[i].normal;
                var ln = new Vector3(
                    m.m00 * n.x + m.m10 * n.y + m.m20 * n.z,
                    m.m01 * n.x + m.m11 * n.y + m.m21 * n.z,
                    m.m02 * n.x + m.m12 * n.y + m.m22 * n.z);
                float ld = Vector3.Dot(n, t) + worldPlanes[i].distance;
                float len = ln.magnitude;
                if (len < 1e-12f) { s_localPlanes[i] = new Vector4(0f, 0f, 0f, ld >= 0f ? 1f : -1f); continue; }
                float inv = 1f / len;
                s_localPlanes[i] = new Vector4(ln.x * inv, ln.y * inv, ln.z * inv, ld * inv);
            }
            return count;
        }

        static void EnsureVertexScratch(int count)
        {
            if (s_dist.Length >= count) return;
            int size = Mathf.NextPowerOfTwo(count);
            s_dist = new float[size];
            s_side = new sbyte[size];
            s_remap = new int[size];
        }

        /// <summary>
        /// Keeps the part of <paramref name="src"/> where dot(n, p) + d >= 0 and caps the cut.
        /// Writes into <paramref name="dst"/> only when the result is Clipped.
        /// </summary>
        static PassResult ClipPass(MeshData src, Vector3 n, float d, MeshData dst)
        {
            int vc = src.P.Count;
            EnsureVertexScratch(vc);
            List<Vector3> P = src.P;

            int inCount = 0, outCount = 0;
            for (int i = 0; i < vc; i++)
            {
                float dist = Vector3.Dot(n, P[i]) + d;
                sbyte side;
                if (dist > SideEpsilon) { side = 1; inCount++; }
                else if (dist < -SideEpsilon) { side = -1; outCount++; }
                else { side = 0; dist = 0f; }
                s_dist[i] = dist;
                s_side[i] = side;
            }
            if (outCount == 0) return PassResult.Unchanged;
            if (inCount == 0) return PassResult.Empty;

            s_src = src;
            s_dst = dst;
            dst.Reset(src.SubCount);
            dst.HasNormals = src.HasNormals;
            for (int i = 0; i < vc; i++) s_remap[i] = -1;
            s_edgeCache.Clear();
            s_dstOnPlane.Clear();
            s_cut.Clear();
            s_cutEdges.Clear();

            for (int s = 0; s < src.SubCount; s++)
            {
                List<int> tris = src.T[s];
                List<int> outTris = dst.T[s];
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    int sa = s_side[a], sb = s_side[b], sc = s_side[c];

                    if (sa >= 0 && sb >= 0 && sc >= 0)
                    {
                        if (sa == 0 && sb == 0 && sc == 0)
                        {
                            // Face lying on the plane: it belongs to the kept solid only if it faces away from it.
                            Vector3 fn = Vector3.Cross(P[b] - P[a], P[c] - P[a]);
                            if (Vector3.Dot(fn, n) >= 0f) continue;
                        }
                        AddTriangle(outTris, Map(a), Map(b), Map(c));
                        continue;
                    }

                    if (sa <= 0 && sb <= 0 && sc <= 0)
                    {
                        // Removed (has at least one strictly outside vertex); its on-plane vertices lie on the cut.
                        if (sa == 0) s_cut.Add(P[a]);
                        if (sb == 0) s_cut.Add(P[b]);
                        if (sc == 0) s_cut.Add(P[c]);
                        continue;
                    }

                    // Straddling: Sutherland–Hodgman against the single plane, then fan the (3- or 4-gon).
                    s_poly.Clear();
                    ClipEdge(a, b);
                    ClipEdge(b, c);
                    ClipEdge(c, a);
                    for (int k = 1; k + 1 < s_poly.Count; k++)
                        AddTriangle(outTris, s_poly[0], s_poly[k], s_poly[k + 1]);
                }
            }

            if (!BuildCapFromBoundary(-n)) BuildCapByAngle(-n);
            s_src = null;
            s_dst = null;
            return dst.HasTriangles() ? PassResult.Clipped : PassResult.Empty;
        }

        static void ClipEdge(int i0, int i1)
        {
            int s0 = s_side[i0], s1 = s_side[i1];
            if (s0 >= 0) s_poly.Add(Map(i0));
            if (s0 == 0) s_cut.Add(s_src.P[i0]);
            if (s0 * s1 < 0) s_poly.Add(Intersect(i0, i1));
        }

        static int Map(int i)
        {
            int r = s_remap[i];
            if (r >= 0) return r;
            r = s_dst.P.Count;
            s_dst.P.Add(s_src.P[i]);
            s_dst.N.Add(s_src.N[i]);
            s_dstOnPlane.Add(s_side[i] == 0);
            s_remap[i] = r;
            return r;
        }

        static int Intersect(int i0, int i1)
        {
            long key = i0 < i1 ? ((long)i0 << 32) | (uint)i1 : ((long)i1 << 32) | (uint)i0;
            if (s_edgeCache.TryGetValue(key, out int existing)) return existing;

            // Interpolate in a canonical (position-ordered) direction so that edges shared by
            // triangles with split vertices (hard normals) produce bit-identical cut points.
            int a = i0, b = i1;
            if (Less(s_src.P[i1], s_src.P[i0])) { a = i1; b = i0; }
            Vector3 pa = s_src.P[a], pb = s_src.P[b];
            float da = s_dist[a], db = s_dist[b];
            float t = da / (da - db);
            Vector3 pos = pa + (pb - pa) * t;
            Vector3 nrm = Vector3.LerpUnclamped(s_src.N[a], s_src.N[b], t);
            float len = nrm.magnitude;
            if (len > 1e-6f) nrm /= len;

            int r = s_dst.P.Count;
            s_dst.P.Add(pos);
            s_dst.N.Add(nrm);
            s_dstOnPlane.Add(true);
            s_edgeCache[key] = r;
            s_cut.Add(pos);
            return r;
        }

        static bool Less(Vector3 a, Vector3 b)
        {
            if (a.x != b.x) return a.x < b.x;
            if (a.y != b.y) return a.y < b.y;
            return a.z < b.z;
        }

        /// <summary>
        /// Adds a kept triangle unless two of its corners coincide (thin-but-valid slivers are kept on
        /// purpose: dropping them would open hairline holes) and records its on-plane edges.
        /// </summary>
        static void AddTriangle(List<int> tris, int a, int b, int c)
        {
            if (a == b || b == c || c == a) return;
            List<Vector3> P = s_dst.P;
            Vector3 pa = P[a], pb = P[b], pc = P[c];
            if ((pb - pa).sqrMagnitude == 0f || (pc - pb).sqrMagnitude == 0f || (pa - pc).sqrMagnitude == 0f) return;
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
            bool oa = s_dstOnPlane[a], ob = s_dstOnPlane[b], oc = s_dstOnPlane[c];
            if (oa && ob) s_cutEdges.Add(new Edge(pa, pb));
            if (ob && oc) s_cutEdges.Add(new Edge(pb, pc));
            if (oc && oa) s_cutEdges.Add(new Edge(pc, pa));
        }

        /// <summary>
        /// Caps the cut by closing the open boundary of the kept surface: on-plane edges whose reverse is
        /// not also kept are chained into loops, and each loop is fanned around its centroid with reversed
        /// orientation. Exact by construction (cap edges are the boundary edges), so the piece stays closed
        /// even with near-coincident cut points. Returns false if the boundary is not a set of simple loops.
        /// </summary>
        static bool BuildCapFromBoundary(Vector3 capNormal)
        {
            s_openEdges.Clear();
            for (int i = 0; i < s_cutEdges.Count; i++)
            {
                Edge e = s_cutEdges[i];
                var reverse = new Edge(e.To, e.From);
                if (s_openEdges.TryGetValue(reverse, out int rc) && rc > 0)
                {
                    if (rc == 1) s_openEdges.Remove(reverse); else s_openEdges[reverse] = rc - 1;
                    continue;
                }
                s_openEdges.TryGetValue(e, out int c);
                s_openEdges[e] = c + 1;
            }
            if (s_openEdges.Count == 0) return true; // the kept surface is already closed

            s_next.Clear();
            foreach (KeyValuePair<Edge, int> kv in s_openEdges)
            {
                if (kv.Value != 1 || s_next.ContainsKey(kv.Key.From)) return false; // non-manifold boundary
                s_next.Add(kv.Key.From, kv.Key.To);
            }

            // Chain every loop first (so a failure leaves nothing half-built), then emit the fans.
            s_loop.Clear();
            s_loopStarts.Clear();
            int guard = s_next.Count;
            while (s_next.Count > 0)
            {
                Vector3 start = default;
                foreach (KeyValuePair<Vector3, Vector3> kv in s_next) { start = kv.Key; break; }
                s_loopStarts.Add(s_loop.Count);
                Vector3 cur = start;
                while (true)
                {
                    if (!s_next.TryGetValue(cur, out Vector3 next)) return false; // open chain
                    s_next.Remove(cur);
                    s_loop.Add(cur);
                    if (s_loop.Count > guard) return false;
                    cur = next;
                    if (cur.x == start.x && cur.y == start.y && cur.z == start.z) break;
                }
            }
            s_loopStarts.Add(s_loop.Count);

            List<Vector3> P = s_dst.P, N = s_dst.N;
            List<int> tris = s_dst.T[0];
            for (int l = 0; l + 1 < s_loopStarts.Count; l++)
            {
                int begin = s_loopStarts[l], count = s_loopStarts[l + 1] - begin;
                if (count < 3) continue;

                Vector3 centroid = Vector3.zero;
                for (int i = 0; i < count; i++) centroid += s_loop[begin + i];
                centroid /= count;

                int center = P.Count;
                P.Add(centroid);
                N.Add(capNormal);
                int first = P.Count;
                for (int i = 0; i < count; i++)
                {
                    P.Add(s_loop[begin + i]);
                    N.Add(capNormal);
                }
                for (int i = 0; i < count; i++)
                {
                    // Boundary runs loop[i] -> loop[i+1]; the cap must traverse it the other way.
                    tris.Add(center);
                    tris.Add(first + (i + 1) % count);
                    tris.Add(first + i);
                }
            }
            return true;
        }

        /// <summary>
        /// Fallback cap (degenerate boundaries): weld the cut points, sort them by angle around their
        /// centroid in the plane and fan-triangulate from the centroid.
        /// </summary>
        static void BuildCapByAngle(Vector3 capNormal)
        {
            int count = 0;
            float weldSqr = WeldEpsilon * WeldEpsilon;
            for (int i = 0; i < s_cut.Count; i++)
            {
                Vector3 p = s_cut[i];
                bool dup = false;
                for (int j = 0; j < count; j++)
                {
                    if ((s_capPoints[j] - p).sqrMagnitude <= weldSqr) { dup = true; break; }
                }
                if (dup) continue;
                if (count == s_capPoints.Length)
                {
                    System.Array.Resize(ref s_capPoints, count * 2);
                    System.Array.Resize(ref s_capAngles, count * 2);
                }
                s_capPoints[count++] = p;
            }
            if (count < 3) return;

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < count; i++) centroid += s_capPoints[i];
            centroid /= count;

            Vector3 u = Vector3.Cross(capNormal, Mathf.Abs(capNormal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(capNormal, u);
            for (int i = 0; i < count; i++)
            {
                Vector3 r = s_capPoints[i] - centroid;
                s_capAngles[i] = Mathf.Atan2(Vector3.Dot(r, v), Vector3.Dot(r, u));
            }
            System.Array.Sort(s_capAngles, s_capPoints, 0, count);

            // Increasing angle runs from u towards v, and cross(u, v) = capNormal; Unity front faces
            // have normal = cross(b - a, c - a), so (center, p[i], p[i+1]) faces along capNormal.
            List<Vector3> P = s_dst.P, N = s_dst.N;
            List<int> tris = s_dst.T[0];
            int center = P.Count;
            P.Add(centroid);
            N.Add(capNormal);
            int first = P.Count;
            for (int i = 0; i < count; i++)
            {
                P.Add(s_capPoints[i]);
                N.Add(capNormal);
            }
            for (int i = 0; i < count; i++)
            {
                tris.Add(center);
                tris.Add(first + i);
                tris.Add(first + (i + 1) % count);
            }
        }
    }
}
