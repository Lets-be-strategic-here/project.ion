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
    ///
    /// UV0 (art bible §6.2): when the source has a 4-component TEXCOORD0 (xyz = pattern-space position,
    /// w = PatternCode), it travels with the vertices. Edge splits interpolate it linearly (exact: xyz is affine
    /// in the position within a flat face), kept vertices copy it, and cap vertices copy the boundary vertex they
    /// duplicate with the cap flag set (w + 64, idempotent); a fan centroid takes the mean of its loop. Meshes
    /// without UV0 stay without it.
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
            s_bufA.Load(mesh);
            return ClipOutsideCore(planeCount, mesh.name, results, null);
        }

        /// <summary>
        /// Receives the outside pieces of a cut as raw buffers (valid only during the call: copy them).
        /// Normals / colours are null when the source has none. One submesh only.
        /// </summary>
        internal interface IPieceSink
        {
            /// <summary><paramref name="uvs"/>: UV0 (pattern space + code), null when the source has none.</summary>
            void AddPiece(List<Vector3> positions, List<Vector3> normals, List<Color32> colors, List<Vector4> uvs, List<int> indices);
        }

        /// <summary>
        /// Like <see cref="ClipOutside(Mesh, Matrix4x4, Plane[], List{Mesh})"/>, but hands the outside pieces
        /// to <paramref name="sink"/> instead of creating meshes. The mesh must have one triangle submesh and normals.
        /// </summary>
        internal static Classification ClipOutsideInto(Mesh mesh, Matrix4x4 localToWorld, Plane[] worldPlanes, IPieceSink sink)
        {
            if (mesh == null) return Classification.Outside;
            if (worldPlanes == null || worldPlanes.Length == 0) return Classification.Inside;

            Classification fast = ClassifyBounds(mesh.bounds, localToWorld, worldPlanes);
            if (fast != Classification.Straddling) return fast;
            if (!CheckReadable(mesh)) return Classification.Outside;

            int planeCount = ToLocalPlanes(localToWorld, worldPlanes);
            s_bufA.Load(mesh);
            return ClipOutsideCore(planeCount, null, null, sink);
        }

        /// <summary>
        /// Clips one element of a merged mesh, given as ranges of CPU arrays (normals / colours may be null),
        /// against the planes prepared by <see cref="PrepareLocalPlanes"/>. Outside pieces go to <paramref name="sink"/>.
        /// </summary>
        internal static Classification ClipOutsideRange(Vector3[] positions, Vector3[] normals, Color32[] colors, int[] indices,
                                                        int v0, int vn, int t0, int tn, int planeCount, IPieceSink sink,
                                                        Vector4[] uvs = null)
        {
            s_bufA.LoadRange(positions, normals, colors, indices, v0, vn, t0, tn, uvs);
            return ClipOutsideCore(planeCount, null, null, sink);
        }

        /// <summary>
        /// Clips one element of a merged mesh (CPU array ranges, as <see cref="ClipOutsideRange"/>) to the INSIDE of
        /// the planes prepared by <see cref="PrepareLocalPlanes"/>. Inside: nothing was cut (the caller copies the
        /// element whole, nothing is emitted); Outside: nothing is inside; Straddling: the clipped part went to the sink.
        /// Element-wise clipping keeps every cap convex: elements are closed convex pieces, while a whole merged mesh
        /// may hold touching pieces whose cut outlines would join into one non-convex cap.
        /// </summary>
        internal static Classification ClipInsideRange(Vector3[] positions, Vector3[] normals, Color32[] colors, int[] indices,
                                                       int v0, int vn, int t0, int tn, int planeCount, IPieceSink sink,
                                                       Vector4[] uvs = null)
        {
            s_bufA.LoadRange(positions, normals, colors, indices, v0, vn, t0, tn, uvs);
            MeshData cur = s_bufA, tmp = s_bufB;
            bool clipped = false;
            for (int i = 0; i < planeCount; i++)
            {
                Vector4 p = s_localPlanes[i];
                PassResult r = ClipPass(cur, new Vector3(p.x, p.y, p.z), p.w, tmp);
                if (r == PassResult.Empty) return Classification.Outside;
                if (r == PassResult.Clipped)
                {
                    (cur, tmp) = (tmp, cur);
                    clipped = true;
                }
            }
            if (!clipped) return Classification.Inside;
            Emit(cur, null, 0, null, sink);
            return Classification.Straddling;
        }

        /// <summary>
        /// Shared by the overloads: s_bufA holds the mesh; the local planes are ready. Piece_k = mesh ∩
        /// inside(p1..p(k-1)) ∩ outside(pk).
        /// </summary>
        static Classification ClipOutsideCore(int planeCount, string name, List<Mesh> results, IPieceSink sink)
        {
            MeshData cur = s_bufA, outside = s_bufB, inside = s_bufC;
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
                    Emit(cur, name, i, results, sink);
                    return Classification.Straddling;
                }

                Emit(outside, name, i, results, sink);
                cut = true;

                PassResult r2 = ClipPass(cur, n, p.w, inside);
                if (r2 == PassResult.Empty) return Classification.Straddling;
                if (r2 == PassResult.Clipped) (cur, inside) = (inside, cur);
            }
            return cut ? Classification.Straddling : Classification.Inside;
        }

        static void Emit(MeshData d, string name, int plane, List<Mesh> results, IPieceSink sink)
        {
            if (sink != null) sink.AddPiece(d.P, d.HasNormals ? d.N : null, d.HasColors ? d.C : null, d.HasUV ? d.U : null, d.T[0]);
            else results.Add(d.ToMesh(name + " (cut " + plane + ")"));
        }

        /// <summary>
        /// Prepares the world planes in the local space of <paramref name="localToWorld"/> for
        /// <see cref="ClassifyLocalBounds"/> and <see cref="ClipOutsideRange"/>. Returns the plane count.
        /// </summary>
        internal static int PrepareLocalPlanes(Matrix4x4 localToWorld, Plane[] worldPlanes) => ToLocalPlanes(localToWorld, worldPlanes);

        /// <summary>
        /// Fast conservative classification of a local box against the planes prepared by
        /// <see cref="PrepareLocalPlanes"/> (one centre/extent test per plane instead of eight transformed corners).
        /// </summary>
        internal static Classification ClassifyLocalBounds(Bounds b, int planeCount)
        {
            Vector3 c = b.center, e = b.extents;
            bool allInside = true;
            for (int i = 0; i < planeCount; i++)
            {
                Vector4 p = s_localPlanes[i];
                float d = p.x * c.x + p.y * c.y + p.z * c.z + p.w;
                float r = Mathf.Abs(p.x) * e.x + Mathf.Abs(p.y) * e.y + Mathf.Abs(p.z) * e.z;
                if (d + r <= SideEpsilon) return Classification.Outside;
                if (d - r < -SideEpsilon) allInside = false;
            }
            return allInside ? Classification.Inside : Classification.Straddling;
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
            /// <summary>Vertex colours (only when <see cref="HasColors"/>): interpolated like normals, caps get the boundary average.</summary>
            public readonly List<Color32> C = new List<Color32>(256);
            /// <summary>UV0 (only when <see cref="HasUV"/>): pattern-space xyz + PatternCode w.</summary>
            public readonly List<Vector4> U = new List<Vector4>(256);
            public readonly List<List<int>> T = new List<List<int>>();
            public int SubCount;
            public bool HasNormals;
            public bool HasColors;
            public bool HasUV;

            public void Reset(int subCount)
            {
                P.Clear();
                N.Clear();
                C.Clear();
                U.Clear();
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
                mesh.GetColors(C);
                HasColors = C.Count == P.Count && P.Count > 0;
                if (!HasColors) C.Clear();
                mesh.GetUVs(0, U);
                HasUV = U.Count == P.Count && P.Count > 0;
                if (!HasUV) U.Clear();
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

            /// <summary>Loads one element (vertex range + index range) of CPU arrays; indices are rebased to 0.</summary>
            public void LoadRange(Vector3[] positions, Vector3[] normals, Color32[] colors, int[] indices, int v0, int vn, int t0, int tn,
                                  Vector4[] uvs = null)
            {
                Reset(1);
                HasNormals = normals != null;
                HasColors = colors != null && vn > 0;
                HasUV = uvs != null && vn > 0 && uvs.Length >= v0 + vn;
                if (HasUV) for (int i = 0; i < vn; i++) U.Add(uvs[v0 + i]);
                for (int i = 0; i < vn; i++)
                {
                    P.Add(positions[v0 + i]);
                    N.Add(HasNormals ? normals[v0 + i] : Vector3.zero);
                }
                if (HasColors) for (int i = 0; i < vn; i++) C.Add(colors[v0 + i]);
                List<int> t = T[0];
                for (int i = 0; i < tn; i++) t.Add(indices[t0 + i] - v0);
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
                if (HasColors && C.Count == P.Count) mesh.SetColors(C);
                if (HasUV && U.Count == P.Count) mesh.SetUVs(0, U);
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
        // UV0 beside the cut points (fallback cap) and per on-plane kept position (boundary cap).
        static readonly List<Vector4> s_cutU = new List<Vector4>(64);
        static Vector4[] s_capU = new Vector4[64];
        static int[] s_capOrder = new int[64];
        static readonly Dictionary<Vector3, int> s_planeVertex = new Dictionary<Vector3, int>(64, new ExactVector3Comparer());

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
            dst.HasColors = src.HasColors;
            dst.HasUV = src.HasUV;
            for (int i = 0; i < vc; i++) s_remap[i] = -1;
            s_edgeCache.Clear();
            s_dstOnPlane.Clear();
            s_cut.Clear();
            s_cutU.Clear();
            s_cutEdges.Clear();
            s_planeVertex.Clear();

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
                        if (sa == 0) AddCut(P[a], a);
                        if (sb == 0) AddCut(P[b], b);
                        if (sc == 0) AddCut(P[c], c);
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
            if (s0 == 0) AddCut(s_src.P[i0], i0);
            if (s0 * s1 < 0) s_poly.Add(Intersect(i0, i1));
        }

        /// <summary>Records a cut point of the source (index <paramref name="srcIndex"/>) for the fallback cap.</summary>
        static void AddCut(Vector3 p, int srcIndex)
        {
            s_cut.Add(p);
            if (s_src.HasUV) s_cutU.Add(s_src.U[srcIndex]);
        }

        static int Map(int i)
        {
            int r = s_remap[i];
            if (r >= 0) return r;
            r = s_dst.P.Count;
            s_dst.P.Add(s_src.P[i]);
            s_dst.N.Add(s_src.N[i]);
            if (s_src.HasColors) s_dst.C.Add(s_src.C[i]);
            if (s_src.HasUV) s_dst.U.Add(s_src.U[i]);
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
            if (s_src.HasColors) s_dst.C.Add(Color32.Lerp(s_src.C[a], s_src.C[b], t));
            Vector4 uv = default;
            if (s_src.HasUV)
            {
                // Same canonical direction and t as the position: exact for the affine pattern space.
                uv = Vector4.LerpUnclamped(s_src.U[a], s_src.U[b], t);
                s_dst.U.Add(uv);
            }
            s_dstOnPlane.Add(true);
            s_edgeCache[key] = r;
            s_cut.Add(pos);
            if (s_src.HasUV) s_cutU.Add(uv);
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
            if (s_dst.HasUV)
            {
                // First kept vertex at each on-plane position: the cap copies its UV0 (cap flag set).
                if (oa && !s_planeVertex.ContainsKey(pa)) s_planeVertex.Add(pa, a);
                if (ob && !s_planeVertex.ContainsKey(pb)) s_planeVertex.Add(pb, b);
                if (oc && !s_planeVertex.ContainsKey(pc)) s_planeVertex.Add(pc, c);
            }
            if (oa && ob) s_cutEdges.Add(new Edge(pa, pb));
            if (ob && oc) s_cutEdges.Add(new Edge(pb, pc));
            if (oc && oa) s_cutEdges.Add(new Edge(pc, pa));
        }

        /// <summary>Average colour of the kept vertices lying on the cutting plane (the cap's colour).</summary>
        static Color32 CapColor()
        {
            List<Color32> c = s_dst.C;
            int r = 0, g = 0, b = 0, a = 0, n = 0;
            int count = Mathf.Min(c.Count, s_dstOnPlane.Count);
            for (int i = 0; i < count; i++)
            {
                if (!s_dstOnPlane[i]) continue;
                Color32 v = c[i];
                r += v.r; g += v.g; b += v.b; a += v.a; n++;
            }
            if (n == 0) return c.Count > 0 ? c[0] : new Color32(255, 255, 255, 255);
            return new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
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
            List<Vector4> U = s_dst.U;
            List<int> tris = s_dst.T[0];
            bool colors = s_dst.HasColors;
            bool uvs = s_dst.HasUV;
            Color32 capColor = colors ? CapColor() : default;
            for (int l = 0; l + 1 < s_loopStarts.Count; l++)
            {
                int begin = s_loopStarts[l], count = s_loopStarts[l + 1] - begin;
                if (count < 3) continue;

                Vector3 centroid = Vector3.zero;
                Vector4 uvSum = Vector4.zero;
                for (int i = 0; i < count; i++)
                {
                    centroid += s_loop[begin + i];
                    if (uvs) uvSum += PlaneVertexUV(s_loop[begin + i]);
                }
                centroid /= count;

                int center = P.Count;
                P.Add(centroid);
                N.Add(capNormal);
                if (colors) s_dst.C.Add(capColor);
                if (uvs) U.Add(CapCentreUV(uvSum / count, PlaneVertexUV(s_loop[begin])));
                int first = P.Count;
                for (int i = 0; i < count; i++)
                {
                    P.Add(s_loop[begin + i]);
                    N.Add(capNormal);
                    if (colors) s_dst.C.Add(capColor);
                    if (uvs) U.Add(CapUV(PlaneVertexUV(s_loop[begin + i])));
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

        /// <summary>UV0 of the kept vertex at an on-plane position (the boundary vertex a cap vertex duplicates).</summary>
        static Vector4 PlaneVertexUV(Vector3 p)
        {
            return s_planeVertex.TryGetValue(p, out int i) ? s_dst.U[i] : Vector4.zero;
        }

        /// <summary>Cap vertex UV0: the same pattern-space position, the code with the cap flag (idempotent).</summary>
        static Vector4 CapUV(Vector4 uv)
        {
            // Coincident boundary vertices may come from a surface (code) or an earlier cap (code + flag):
            // strip the flag before setting it so every cap vertex carries exactly code + flag.
            int code = Mathf.RoundToInt(uv.w);
            uv.w = (code & (CapFlag - 1)) + CapFlag;
            return uv;
        }

        /// <summary>Fan centre UV0: the averaged (affine, so exact) pattern position with one loop vertex's code,
        /// never an average of codes (a loop can mix surface and earlier-cap vertices).</summary>
        static Vector4 CapCentreUV(Vector4 average, Vector4 codeSource)
        {
            average.w = codeSource.w;
            return CapUV(average);
        }

        /// <summary>PatternCode cap flag (Ion.Presentation.PatternCode.CapFlag; kept here so Projection has no Presentation dependency).</summary>
        internal const int CapFlag = 64;

        /// <summary>
        /// Fallback cap (degenerate boundaries): weld the cut points, sort them by angle around their
        /// centroid in the plane and fan-triangulate from the centroid.
        /// </summary>
        static void BuildCapByAngle(Vector3 capNormal)
        {
            int count = 0;
            float weldSqr = WeldEpsilon * WeldEpsilon;
            bool uvs = s_dst.HasUV && s_cutU.Count == s_cut.Count;
            for (int i = 0; i < s_cut.Count; i++)
            {
                Vector3 p = s_cut[i];
                bool dup = false;
                for (int j = 0; j < count; j++)
                {
                    if ((s_capPoints[j] - p).sqrMagnitude <= weldSqr) { dup = true; break; } // a weld keeps the first point's UV0
                }
                if (dup) continue;
                if (count == s_capPoints.Length)
                {
                    System.Array.Resize(ref s_capPoints, count * 2);
                    System.Array.Resize(ref s_capAngles, count * 2);
                    System.Array.Resize(ref s_capU, count * 2);
                    System.Array.Resize(ref s_capOrder, count * 2);
                }
                s_capPoints[count] = p;
                s_capU[count] = uvs ? s_cutU[i] : default;
                count++;
            }
            if (count < 3) return;

            Vector3 centroid = Vector3.zero;
            Vector4 uvSum = Vector4.zero;
            for (int i = 0; i < count; i++)
            {
                centroid += s_capPoints[i];
                uvSum += s_capU[i];
            }
            centroid /= count;

            Vector3 u = Vector3.Cross(capNormal, Mathf.Abs(capNormal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(capNormal, u);
            for (int i = 0; i < count; i++)
            {
                Vector3 r = s_capPoints[i] - centroid;
                s_capAngles[i] = Mathf.Atan2(Vector3.Dot(r, v), Vector3.Dot(r, u));
                s_capOrder[i] = i;
            }
            // Sort an index permutation so the UV0 beside each point follows it.
            System.Array.Sort(s_capAngles, s_capOrder, 0, count);

            // Increasing angle runs from u towards v, and cross(u, v) = capNormal; Unity front faces
            // have normal = cross(b - a, c - a), so (center, p[i], p[i+1]) faces along capNormal.
            List<Vector3> P = s_dst.P, N = s_dst.N;
            List<int> tris = s_dst.T[0];
            bool colors = s_dst.HasColors;
            bool dstUV = s_dst.HasUV;
            Color32 capColor = colors ? CapColor() : default;
            int center = P.Count;
            P.Add(centroid);
            N.Add(capNormal);
            if (colors) s_dst.C.Add(capColor);
            if (dstUV) s_dst.U.Add(CapCentreUV(uvSum / count, s_capU[0]));
            int first = P.Count;
            for (int i = 0; i < count; i++)
            {
                int k = s_capOrder[i];
                P.Add(s_capPoints[k]);
                N.Add(capNormal);
                if (colors) s_dst.C.Add(capColor);
                if (dstUV) s_dst.U.Add(CapUV(s_capU[k]));
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
