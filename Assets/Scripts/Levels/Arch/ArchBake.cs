using System.Collections.Generic;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Levels.Arch
{
    public static partial class Arch
    {
        // ================================================================================== baking

        /// <summary>
        /// Merges every non-Dynamic ArchPiece (and every Decor) under root into one Sliceable per (Mat, flags,
        /// chunk) with MeshElements (one convex element per piece), a merged MeshCollider for Collider pieces,
        /// and TEXCOORD0 = (world position, PatternCode). Destroys the source children. Returns draw objects created.
        /// Pieces under an Interactable, inactive pieces and Dynamic pieces are left alone (their owner calls
        /// <see cref="BakeLocal"/>). Call once per zone / diorama root, after it is fully built and positioned.
        /// </summary>
        public static int Bake(Transform root, float chunk = DefaultChunk, float softChunk = DefaultSoftChunk)
        {
            if (root == null) return 0;
            var sources = new List<Source>();
            Collect(root, root, false, sources);
            return Merge(root, sources, chunk, false, softChunk);
        }

        /// <summary>
        /// Zone bake chunk (m) for pieces with a collider. A placement re-cooks the MeshCollider of every chunk it
        /// cuts (the dominant placement cost on slow CPUs), so these stay small.
        /// </summary>
        public const float DefaultChunk = 8f;

        /// <summary>
        /// Chunk (m) for collider-less (soft / visual) pieces: 0 = one object per material for the whole zone. They
        /// cook nothing when cut, so merging them all keeps a zone inside ≤ 120 batches (art bible §10).
        /// </summary>
        public const float DefaultSoftChunk = 0f;

        /// <summary>
        /// Same merge for one object, but UV0 in go-local space (the pattern moves with the object): movers, devices.
        /// Includes Dynamic pieces; skips pieces of nested Interactables (they bake themselves).
        /// </summary>
        public static void BakeLocal(GameObject go)
        {
            if (go == null) return;
            var sources = new List<Source>();
            Collect(go.transform, go.transform, true, sources);
            Merge(go.transform, sources, 0f, true);
        }

        /// <summary>
        /// Editor / test check under root. Reports: off-grid pieces (plan min/max off the piece's snap: 0.25 for
        /// architecture, 0.0625 for detail; heights off 0.0625), open meshes, Sliceables without pattern space (no
        /// float4 UV0, or all-zero pattern space) and Visual pieces outside an Interactable. True if none were found.
        /// </summary>
        public static bool Validate(Transform root, List<string> errors)
        {
            if (errors == null) errors = new List<string>();
            int before = errors.Count;
            if (root == null) { errors.Add("Validate: root is null"); return false; }
            Matrix4x4 toRoot = root.worldToLocalMatrix;

            foreach (ArchPiece piece in root.GetComponentsInChildren<ArchPiece>(true))
            {
                string path = PathOf(piece.transform, root);
                if (!piece.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null)
                {
                    errors.Add("No mesh: " + path);
                    continue;
                }
                Mesh mesh = mf.sharedMesh;
                if (!IsClosed(mesh)) errors.Add("Open mesh '" + mesh.name + "': " + path);
                bool underInteractable = piece.GetComponentInParent<Interactable>(true) != null;
                if ((piece.Flags & (ArchFlags.Collider | ArchFlags.Sliceable)) == 0 && !underInteractable)
                    errors.Add("Visual piece outside an Interactable: " + path);

                if (piece.Snap > 0f && IsAxisAligned(toRoot * piece.transform.localToWorldMatrix))
                {
                    Bounds b = Transformed(mesh.bounds, toRoot * piece.transform.localToWorldMatrix);
                    Vector3 min = b.min, max = b.max;
                    float s = piece.Snap;
                    if (!OnGrid(min.x, s) || !OnGrid(max.x, s) || !OnGrid(min.z, s) || !OnGrid(max.z, s) ||
                        !OnGrid(min.y, Detail) || !OnGrid(max.y, Detail))
                        errors.Add("Off grid (" + s + "): " + path + " min " + F(min) + " max " + F(max));
                }
            }

            foreach (Sliceable sl in root.GetComponentsInChildren<Sliceable>(true))
            {
                if (sl.GetComponent<ArchPiece>() != null) continue;              // not baked yet: Bake writes it
                if (sl.GetComponent<Decor>() != null) continue;                  // unbaked decor (plants): Bake writes it too
                if (sl.GetComponentInParent<Interactable>(true) != null) continue; // devices are never cut
                string path = PathOf(sl.transform, root);
                if (!sl.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                if (!HasPatternSpace(mesh)) errors.Add("Sliceable without pattern space (UV0): " + path);
                if (sl.TryGetComponent(out MeshElements el) && el.Mesh == mesh && el.Count > 0)
                {
                    if (!ElementsClosed(el)) errors.Add("Open element in merged mesh '" + mesh.name + "': " + path);
                }
                else if (!IsClosed(mesh)) errors.Add("Open mesh '" + mesh.name + "': " + path);
            }
            return errors.Count == before;
        }

        /// <summary>Counts for the art budget tests (§10: ≤ 120 batches and ≤ 60k triangles per visible zone).</summary>
        public struct ArtAudit
        {
            /// <summary>Active, enabled MeshRenderers.</summary>
            public int Renderers;
            /// <summary>Draw calls before shadows: one per renderer and submesh (SRP Batcher batches state, not draws).</summary>
            public int Batches;
            public int Triangles;
            /// <summary>Distinct materials (each Mat is one shared material).</summary>
            public int Materials;
            /// <summary>Live Sliceables outside Interactables, and how many of them lack pattern space.</summary>
            public int Sliceables, SliceablesWithoutPattern;
            public override string ToString() =>
                Renderers + " renderers, " + Batches + " batches, " + Triangles + " tris, " + Materials + " materials, " +
                Sliceables + " sliceables (" + SliceablesWithoutPattern + " without pattern space)";
        }

        /// <summary>Measures a built zone (or any root) for the art budget tests. Additive helper.</summary>
        public static ArtAudit Audit(Transform root)
        {
            var a = new ArtAudit();
            if (root == null) return a;
            var mats = new HashSet<Material>();
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || !r.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) continue;
                a.Renderers++;
                Mesh m = mf.sharedMesh;
                a.Batches += Mathf.Max(1, m.subMeshCount);
                for (int sm = 0; sm < m.subMeshCount; sm++)
                    if (m.GetTopology(sm) == MeshTopology.Triangles) a.Triangles += (int)(m.GetIndexCount(sm) / 3);
                foreach (Material mat in r.sharedMaterials) if (mat != null) mats.Add(mat);
            }
            a.Materials = mats.Count;
            foreach (Sliceable s in root.GetComponentsInChildren<Sliceable>())
            {
                if (!s.enabled || s.GetComponentInParent<Interactable>(true) != null) continue;
                if (!s.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) continue;
                a.Sliceables++;
                if (!HasPatternSpace(mf.sharedMesh)) a.SliceablesWithoutPattern++;
            }
            return a;
        }

        /// <summary>True if the mesh has a float4 UV0 whose pattern space is not all zero (art test helper).</summary>
        public static bool HasPatternSpace(Mesh mesh)
        {
            if (mesh == null || !mesh.HasVertexAttribute(VertexAttribute.TexCoord0)) return false;
            if (mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0) < 4) return false;
            if (!mesh.isReadable) return true;
            mesh.GetUVs(0, s_uvScratch);
            bool any = false;
            for (int i = 0; i < s_uvScratch.Count && !any; i++)
            {
                Vector4 u = s_uvScratch[i];
                any = u.x != 0f || u.y != 0f || u.z != 0f;
            }
            s_uvScratch.Clear();
            return any;
        }

        // ---------------------------------------------------------------------------------- internals

        struct Source
        {
            public GameObject Go;
            public Mesh Mesh;
            public Material Material;
            public ArchPiece Piece;
            public Decor Decor;
            public bool Collider, Sliceable;
            public ShadowCastingMode Shadows;
            public int Layer;
            public Matrix4x4 LocalToWorld;
        }

        struct GroupKey : System.IEquatable<GroupKey>
        {
            public Material Material;
            public bool Collider, Sliceable;
            public ShadowCastingMode Shadows;
            public int Layer, Cx, Cz;

            public bool Equals(GroupKey o) => Material == o.Material && Collider == o.Collider && Sliceable == o.Sliceable &&
                                              Shadows == o.Shadows && Layer == o.Layer && Cx == o.Cx && Cz == o.Cz;
            public override bool Equals(object obj) => obj is GroupKey k && Equals(k);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Material != null ? Material.GetInstanceID() : 0;
                    h = h * 31 + (Collider ? 1 : 0) + (Sliceable ? 2 : 0) + ((int)Shadows << 2);
                    h = h * 31 + Layer;
                    h = h * 31 + Cx;
                    return h * 31 + Cz;
                }
            }
        }

        sealed class Batch
        {
            public readonly List<Vector3> P = new List<Vector3>(1024);
            public readonly List<Vector3> N = new List<Vector3>(1024);
            public readonly List<Color32> C = new List<Color32>(1024);
            public readonly List<Vector4> U = new List<Vector4>(1024);
            public readonly List<int> T = new List<int>(2048);
            public readonly List<int> V0 = new List<int>(), VN = new List<int>(), T0 = new List<int>(), TN = new List<int>();
            public readonly List<Bounds> B = new List<Bounds>();
            public readonly List<GameObject> Sources = new List<GameObject>();
            public GroupKey Key;
        }

        static readonly List<Vector3> s_v = new List<Vector3>(256), s_n = new List<Vector3>(256);
        static readonly List<Color32> s_c = new List<Color32>(256);
        static readonly List<int> s_t = new List<int>(512);
        static readonly List<Vector4> s_uvScratch = new List<Vector4>(256);
        static readonly List<ArchPiece> s_pieces = new List<ArchPiece>(512);
        static readonly List<Decor> s_decor = new List<Decor>(256);

        static void Collect(Transform scope, Transform owner, bool local, List<Source> into)
        {
            s_pieces.Clear();
            s_decor.Clear();
            scope.GetComponentsInChildren(true, s_pieces);
            scope.GetComponentsInChildren(true, s_decor);
            var seen = new HashSet<GameObject>();
            foreach (ArchPiece piece in s_pieces) Add(piece.gameObject, piece, piece.GetComponent<Decor>(), owner, local, into, seen);
            foreach (Decor d in s_decor) Add(d.gameObject, d.GetComponent<ArchPiece>(), d, owner, local, into, seen);
            s_pieces.Clear();
            s_decor.Clear();
        }

        static void Add(GameObject go, ArchPiece piece, Decor decor, Transform owner, bool local, List<Source> into, HashSet<GameObject> seen)
        {
            if (!seen.Add(go)) return;
            if (!go.activeInHierarchy) return;
            if (!go.TryGetComponent(out MeshFilter mf) || !go.TryGetComponent(out MeshRenderer mr)) return;
            if (mf.sharedMesh == null || mr.sharedMaterial == null || !mf.sharedMesh.isReadable) return;
            if (mf.sharedMesh.subMeshCount != 1 || mf.sharedMesh.GetTopology(0) != MeshTopology.Triangles) return;

            // Devices: a zone bake never takes pieces under an Interactable; BakeLocal takes its own, not nested ones.
            Interactable it = go.GetComponentInParent<Interactable>(true);
            if (!local && it != null) return;
            if (local && it != null && it.transform != owner && it.transform.IsChildOf(owner)) return;
            if (!local && piece != null && (piece.Flags & ArchFlags.Dynamic) != 0) return;

            into.Add(new Source
            {
                Go = go,
                Mesh = mf.sharedMesh,
                Material = mr.sharedMaterial,
                Piece = piece,
                Decor = decor,
                Collider = piece != null ? (piece.Flags & ArchFlags.Collider) != 0 : go.GetComponent<Collider>() != null,
                Sliceable = piece != null ? (piece.Flags & ArchFlags.Sliceable) != 0 : go.GetComponent<Sliceable>() != null,
                Shadows = mr.shadowCastingMode,
                Layer = go.layer,
                LocalToWorld = go.transform.localToWorldMatrix,
            });
        }

        static int Merge(Transform owner, List<Source> sources, float chunk, bool local, float softChunk = 0f)
        {
            if (sources.Count == 0) return 0;
            Matrix4x4 toOwner = owner.worldToLocalMatrix;
            var batches = new Dictionary<GroupKey, Batch>();
            var order = new List<Batch>();

            foreach (Source src in sources)
            {
                Matrix4x4 toMesh = toOwner * src.LocalToWorld;                     // vertex -> output mesh space
                Matrix4x4 toPattern = local ? toMesh : src.LocalToWorld;          // vertex -> pattern space
                Bounds wb = Transformed(src.Mesh.bounds, src.LocalToWorld);
                float c = src.Collider ? chunk : softChunk;
                var key = new GroupKey
                {
                    Material = src.Material,
                    Collider = src.Collider,
                    Sliceable = src.Sliceable,
                    Shadows = src.Shadows,
                    Layer = src.Layer,
                    // Chunks are centred on the owner (zone root): with 8 m they match the hub's 3 x 3 cell grid,
                    // so an exhibit placement re-cooks one chunk per material instead of four.
                    Cx = c > 0f ? Mathf.FloorToInt((wb.center.x - owner.position.x) / c + 0.5f) : 0,
                    Cz = c > 0f ? Mathf.FloorToInt((wb.center.z - owner.position.z) / c + 0.5f) : 0,
                };
                if (!batches.TryGetValue(key, out Batch batch))
                {
                    batch = new Batch { Key = key };
                    batches.Add(key, batch);
                    order.Add(batch);
                }
                AppendSource(batch, src, toMesh, toPattern, toOwner);
            }

            int created = 0;
            foreach (Batch batch in order)
            {
                if (batch.T.Count == 0) continue;
                CreateMerged(owner, batch);
                created++;
            }

            // Remove the sources (and the Arch groups left empty).
            var doomed = new HashSet<GameObject>();
            foreach (Batch batch in order)
                foreach (GameObject go in batch.Sources)
                {
                    if (go.transform.childCount == 0) doomed.Add(go);
                    else StripGeometry(go);
                }
            foreach (ArchGroup g in owner.GetComponentsInChildren<ArchGroup>(true))
                MarkEmptyGroups(g.transform, doomed);
            foreach (GameObject go in doomed)
            {
                if (go == null || go == owner.gameObject) continue;
                Transform parent = go.transform.parent;
                if (parent != null && doomed.Contains(parent.gameObject)) continue; // goes with its parent
                go.SetActive(false);
                DestroyObject(go);
            }
            return created;
        }

        static void AppendSource(Batch batch, Source src, Matrix4x4 toMesh, Matrix4x4 toPattern, Matrix4x4 toOwner)
        {
            Mesh mesh = src.Mesh;
            mesh.GetVertices(s_v);
            mesh.GetNormals(s_n);
            mesh.GetColors(s_c);
            mesh.GetTriangles(s_t, 0);
            bool hasN = s_n.Count == s_v.Count, hasC = s_c.Count == s_v.Count;
            Matrix4x4 nm = toMesh.inverse.transpose;
            bool mirrored = toMesh.determinant < 0f;

            // Pattern: Boards resolve along the element's longest horizontal extent in pattern space.
            Pat pat = src.Piece != null ? src.Piece.Surf.Pat : Pat.None;
            if (pat == Pat.Boards)
            {
                Bounds pb = Transformed(mesh.bounds, toPattern);
                pat = pb.size.x >= pb.size.z ? Pat.BoardsX : Pat.BoardsZ;
            }
            float code = PatternCode.Encode(pat);

            // Vertex colour: Decor contact shade / tint / sway weight; plants without Decor sway by height in the piece.
            DecorCombiner.DecorTint tint = default;
            if (src.Decor != null) tint = DecorCombiner.TintFor(src.Decor, toOwner.MultiplyPoint3x4(src.Go.transform.position));
            bool foliage = src.Decor == null && src.Piece != null && Palette.IsFoliage(src.Piece.Surf.Mat);
            Bounds mb = Transformed(mesh.bounds, toMesh);
            float y0 = mb.min.y, invH = 1f / Mathf.Max(0.01f, mb.size.y);

            int start = batch.P.Count, tStart = batch.T.Count;
            Vector3 bMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), bMax = -bMin;
            for (int i = 0; i < s_v.Count; i++)
            {
                Vector3 p = toMesh.MultiplyPoint3x4(s_v[i]);
                batch.P.Add(p);
                bMin = Vector3.Min(bMin, p);
                bMax = Vector3.Max(bMax, p);
                batch.N.Add(hasN ? nm.MultiplyVector(s_n[i]).normalized : Vector3.up);
                Color32 c = hasC ? s_c[i] : new Color32(255, 255, 255, 255);
                if (src.Decor != null) c = DecorCombiner.Shade(src.Decor, tint, p.y, c);
                else if (foliage) c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01((p.y - y0) * invH) * 255f), 0, 255);
                batch.C.Add(c);
                Vector3 pp = toPattern.MultiplyPoint3x4(s_v[i]);
                batch.U.Add(new Vector4(pp.x, pp.y, pp.z, code));
            }
            for (int i = 0; i + 2 < s_t.Count; i += 3)
            {
                batch.T.Add(start + s_t[i]);
                batch.T.Add(start + s_t[mirrored ? i + 2 : i + 1]);
                batch.T.Add(start + s_t[mirrored ? i + 1 : i + 2]);
            }
            if (batch.P.Count > start)
            {
                batch.V0.Add(start);
                batch.VN.Add(batch.P.Count - start);
                batch.T0.Add(tStart);
                batch.TN.Add(batch.T.Count - tStart);
                var eb = new Bounds();
                eb.SetMinMax(bMin, bMax);
                batch.B.Add(eb);
            }
            batch.Sources.Add(src.Go);
            s_v.Clear();
            s_n.Clear();
            s_c.Clear();
            s_t.Clear();
        }

        static void CreateMerged(Transform owner, Batch batch)
        {
            GroupKey k = batch.Key;
            string matName = Palette.TryGetMat(k.Material, out Mat role) ? role.ToString() : k.Material.name;
            string name = "Arch " + matName + (k.Collider ? "" : k.Sliceable ? " (soft)" : " (visual)") + " [" + k.Cx + "," + k.Cz + "]";
            var mesh = new Mesh { name = name };
            if (batch.P.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(batch.P);
            mesh.SetNormals(batch.N);
            mesh.SetColors(batch.C);
            mesh.SetUVs(0, batch.U);
            mesh.SetTriangles(batch.T, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false); // readable: the slicer and MeshCollider read it

            var go = new GameObject(name) { layer = k.Layer };
            go.transform.SetParent(owner, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = k.Material;
            r.shadowCastingMode = k.Shadows;
            r.receiveShadows = true;
            if (k.Collider)
            {
                var col = go.AddComponent<MeshCollider>();
                col.cookingOptions = ProjectionSystem.CookingOptions;
                col.sharedMesh = mesh;
            }
            if (k.Sliceable) go.AddComponent<Sliceable>();
            var el = go.AddComponent<MeshElements>();
            el.Set(mesh, batch.V0.ToArray(), batch.VN.ToArray(), batch.T0.ToArray(), batch.TN.ToArray(), batch.B.ToArray());
            // CPU copies for the slicer (no readback on the first cut), UV0 included.
            el.SetData(batch.P.ToArray(), batch.N.ToArray(), batch.C.ToArray(), batch.U.ToArray(), batch.T.ToArray());
        }

        /// <summary>A source that still has children (something was parented under a piece): keep the object, drop its geometry.</summary>
        static void StripGeometry(GameObject go)
        {
            foreach (Collider c in go.GetComponents<Collider>()) DestroyObject(c);
            if (go.TryGetComponent(out Sliceable s)) DestroyObject(s);
            if (go.TryGetComponent(out MeshElements el)) DestroyObject(el);
            if (go.TryGetComponent(out MeshRenderer r)) DestroyObject(r);
            if (go.TryGetComponent(out MeshFilter f)) DestroyObject(f);
            if (go.TryGetComponent(out ArchPiece p)) DestroyObject(p);
            if (go.TryGetComponent(out Decor d)) DestroyObject(d);
        }

        /// <summary>Marks an Arch group as doomed when everything under it is doomed (recursively). Returns whether it is.</summary>
        static bool MarkEmptyGroups(Transform t, HashSet<GameObject> doomed)
        {
            if (doomed.Contains(t.gameObject)) return true;
            if (t.GetComponent<ArchGroup>() == null) return false;
            // Only plain groups: Transform + ArchGroup.
            if (t.GetComponents<Component>().Length > 2) return false;
            for (int i = 0; i < t.childCount; i++)
                if (!MarkEmptyGroups(t.GetChild(i), doomed)) return false;
            doomed.Add(t.gameObject);
            return true;
        }

        static void DestroyObject(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        // ---------------------------------------------------------------------------------- validation helpers

        static readonly Dictionary<Mesh, bool> s_closed = new Dictionary<Mesh, bool>();

        static bool OnGrid(float v, float step)
        {
            float k = v / step;
            return Mathf.Abs(k - Mathf.Round(k)) * step < 1e-3f;
        }

        static string F(Vector3 v) => "(" + v.x.ToString("0.###") + ", " + v.y.ToString("0.###") + ", " + v.z.ToString("0.###") + ")";

        static string PathOf(Transform t, Transform root)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// <summary>True when the matrix maps axes onto axes (rotations by multiples of 90°, any scale).</summary>
        static bool IsAxisAligned(Matrix4x4 m)
        {
            for (int c = 0; c < 3; c++)
            {
                Vector3 col = m.GetColumn(c);
                float a = Mathf.Abs(col.x), b = Mathf.Abs(col.y), d = Mathf.Abs(col.z);
                float big = Mathf.Max(a, Mathf.Max(b, d));
                if (big < 1e-8f) return false;
                int nonZero = (a > big * 1e-4f ? 1 : 0) + (b > big * 1e-4f ? 1 : 0) + (d > big * 1e-4f ? 1 : 0);
                if (nonZero != 1) return false;
            }
            return true;
        }

        static Bounds Transformed(Bounds b, Matrix4x4 m)
        {
            Vector3 c = b.center, e = b.extents;
            var result = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
                result.Encapsulate(m.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return result;
        }

        /// <summary>Closed, consistently oriented surface (positions welded exactly): every directed edge once, its reverse once.</summary>
        internal static bool IsClosed(Mesh mesh)
        {
            if (mesh == null) return false;
            if (s_closed.TryGetValue(mesh, out bool known)) return known;
            bool ok = mesh.isReadable && mesh.subMeshCount >= 1;
            if (ok)
            {
                Vector3[] v = mesh.vertices;
                int[] t = mesh.triangles;
                ok = RangeClosed(v, t, 0, t.Length);
            }
            s_closed[mesh] = ok;
            return ok;
        }

        static bool ElementsClosed(MeshElements el)
        {
            if (!el.Mesh.isReadable) return true;
            Vector3[] v = el.Mesh.vertices;
            int[] t = el.Mesh.GetIndices(0);
            for (int e = 0; e < el.Count; e++)
                if (!RangeClosed(v, t, el.IndexStart[e], el.IndexCount[e])) return false;
            return true;
        }

        static bool RangeClosed(Vector3[] v, int[] t, int t0, int tn)
        {
            if (tn < 12) return false; // a closed solid has at least 4 triangles
            var ids = new Dictionary<Vector3, int>();
            var edges = new Dictionary<long, int>();
            int Id(int i)
            {
                if (!ids.TryGetValue(v[i], out int id)) { id = ids.Count; ids.Add(v[i], id); }
                return id;
            }
            for (int i = t0; i + 2 < t0 + tn; i += 3)
            {
                int a = Id(t[i]), b = Id(t[i + 1]), c = Id(t[i + 2]);
                if (a == b || b == c || c == a) continue;
                Count(edges, a, b);
                Count(edges, b, c);
                Count(edges, c, a);
            }
            foreach (KeyValuePair<long, int> kv in edges)
            {
                if (kv.Value != 1) return false;
                long a = kv.Key >> 32, b = kv.Key & 0xffffffffL;
                if (!edges.TryGetValue((b << 32) | a, out int r) || r != 1) return false;
            }
            return true;
        }

        static void Count(Dictionary<long, int> d, int a, int b)
        {
            long key = ((long)a << 32) | (uint)b;
            d.TryGetValue(key, out int n);
            d[key] = n + 1;
        }
    }
}
