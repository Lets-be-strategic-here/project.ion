using System.Collections.Generic;
using Ion.Projection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Levels
{
    /// <summary>
    /// Marks a decoration renderer (tree, rock, tuft, flower, island underside...) for
    /// <see cref="DecorCombiner"/>. Describes how its vertex colours are baked: a contact-shade
    /// gradient from <see cref="AoMin"/> at <see cref="BaseY"/> (root-local height) to 1 at
    /// BaseY + <see cref="Height"/>, a small per-instance tint jitter and, for swaying canopies, the
    /// sway weight in alpha (0 at the base, 1 at the top).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Decor : MonoBehaviour
    {
        public float BaseY;
        public float Height = 1f;
        public float AoMin = 1f;
        public float Tint = 0.04f;
        public bool SwayWeight;
        /// <summary>Invert the gradient (darker at the top), e.g. for downward island spikes seen from above.</summary>
        public bool AoFromTop;
    }

    /// <summary>
    /// Merges every <see cref="Decor"/> renderer under a room root into one mesh per
    /// (material, collider, shadow mode), baking per-instance tint, contact shading and sway weight into
    /// vertex colours. Keeps draw calls per room at roughly the number of decor colours, and the result
    /// is still a closed, readable <see cref="Sliceable"/> (several convex parts), so photos cut and
    /// capture it like any other terrain. Deterministic: a world root and its diorama copy combine
    /// identically.
    /// </summary>
    public static class DecorCombiner
    {
        struct Key
        {
            public Material Material;
            public bool Collider;
            public ShadowCastingMode Shadows;
        }

        sealed class Group
        {
            public readonly List<Vector3> P = new List<Vector3>(1024);
            public readonly List<Vector3> N = new List<Vector3>(1024);
            public readonly List<Color32> C = new List<Color32>(1024);
            /// <summary>UV0 (pattern space + PatternCode): copied unchanged, or the world position at combine time for sources without pattern space.</summary>
            public readonly List<Vector4> U = new List<Vector4>(1024);
            public readonly List<int> T = new List<int>(2048);
            public string Name;
            // One entry per merged source renderer (see MeshElements).
            public readonly List<int> ElemV0 = new List<int>(), ElemVN = new List<int>(), ElemT0 = new List<int>(), ElemTN = new List<int>();
            public readonly List<Bounds> ElemBounds = new List<Bounds>();
        }

        static readonly List<Vector3> s_v = new List<Vector3>(256);
        static readonly List<Vector3> s_n = new List<Vector3>(256);
        static readonly List<int> s_t = new List<int>(512);
        static readonly List<Color32> s_c = new List<Color32>(256);
        static readonly List<Vector4> s_u = new List<Vector4>(256);

        /// <summary>Combines the decor under <paramref name="root"/>. Returns the number of merged source renderers.</summary>
        public static int Combine(Transform root)
        {
            if (root == null) return 0;
            Decor[] decor = root.GetComponentsInChildren<Decor>(true);
            if (decor.Length == 0) return 0;

            var groups = new Dictionary<Key, Group>();
            var order = new List<Key>();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            int merged = 0;

            for (int d = 0; d < decor.Length; d++)
            {
                Decor info = decor[d];
                var mf = info.GetComponent<MeshFilter>();
                var mr = info.GetComponent<MeshRenderer>();
                if (mf == null || mr == null || mf.sharedMesh == null || mr.sharedMaterial == null) continue;

                var key = new Key
                {
                    Material = mr.sharedMaterial,
                    Collider = info.GetComponent<Collider>() != null,
                    Shadows = mr.shadowCastingMode,
                };
                if (!groups.TryGetValue(key, out Group g))
                {
                    g = new Group { Name = "Decor " + mr.sharedMaterial.name + (key.Collider ? "" : " (soft)") };
                    groups.Add(key, g);
                    order.Add(key);
                }

                Matrix4x4 m = toRoot * info.transform.localToWorldMatrix;
                Matrix4x4 nm = m.inverse.transpose;
                Mesh mesh = mf.sharedMesh;
                mesh.GetVertices(s_v);
                mesh.GetNormals(s_n);
                mesh.GetTriangles(s_t, 0);
                bool hasN = s_n.Count == s_v.Count;
                mesh.GetColors(s_c);
                bool hasC = s_c.Count == s_v.Count;
                mesh.GetUVs(0, s_u);
                bool hasU = s_u.Count == s_v.Count && s_v.Count > 0;
                // A source with a real pattern space keeps it (UV0 is data); one without (Geo's unit meshes carry
                // zeros) gets its world position at combine time, so every merged Sliceable has pattern space.
                bool srcPattern = false;
                if (hasU) for (int i = 0; i < s_u.Count && !srcPattern; i++) srcPattern = s_u[i].x != 0f || s_u[i].y != 0f || s_u[i].z != 0f;
                Matrix4x4 toWorld = info.transform.localToWorldMatrix;

                // Per-instance tint from the object's root-local position (identical for world and diorama).
                DecorTint tint = TintFor(info, toRoot.MultiplyPoint3x4(info.transform.position));
                bool mirrored = m.determinant < 0f;

                int start = g.P.Count;
                int tStart = g.T.Count;
                Vector3 bMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), bMax = -bMin;
                for (int i = 0; i < s_v.Count; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(s_v[i]);
                    g.P.Add(p);
                    bMin = Vector3.Min(bMin, p);
                    bMax = Vector3.Max(bMax, p);
                    g.N.Add(hasN ? nm.MultiplyVector(s_n[i]).normalized : Vector3.up);
                    g.C.Add(Shade(info, tint, p.y, hasC ? s_c[i] : new Color32(255, 255, 255, 255)));
                    if (srcPattern) g.U.Add(s_u[i]);
                    else
                    {
                        Vector3 w = toWorld.MultiplyPoint3x4(s_v[i]);
                        g.U.Add(new Vector4(w.x, w.y, w.z, hasU ? s_u[i].w : 0f));
                    }
                }
                for (int i = 0; i + 2 < s_t.Count; i += 3)
                {
                    if (mirrored)
                    {
                        g.T.Add(start + s_t[i]);
                        g.T.Add(start + s_t[i + 2]);
                        g.T.Add(start + s_t[i + 1]);
                    }
                    else
                    {
                        g.T.Add(start + s_t[i]);
                        g.T.Add(start + s_t[i + 1]);
                        g.T.Add(start + s_t[i + 2]);
                    }
                }

                if (g.P.Count > start)
                {
                    g.ElemV0.Add(start);
                    g.ElemVN.Add(g.P.Count - start);
                    g.ElemT0.Add(tStart);
                    g.ElemTN.Add(g.T.Count - tStart);
                    var eb = new Bounds();
                    eb.SetMinMax(bMin, bMax);
                    g.ElemBounds.Add(eb);
                }

                // Gone for good: hidden now (so later queries this frame skip it), destroyed at frame end.
                info.gameObject.SetActive(false);
                if (Application.isPlaying) Object.Destroy(info.gameObject);
                else Object.DestroyImmediate(info.gameObject);
                merged++;
            }

            for (int i = 0; i < order.Count; i++)
            {
                Key key = order[i];
                Group g = groups[key];
                var mesh = new Mesh { name = g.Name };
                if (g.P.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(g.P);
                mesh.SetNormals(g.N);
                mesh.SetColors(g.C);
                bool groupUV = g.U.Count == g.P.Count;
                if (groupUV) mesh.SetUVs(0, g.U);
                mesh.SetTriangles(g.T, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false); // readable: the slicer and MeshCollider read it

                var go = new GameObject(g.Name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = key.Material;
                r.shadowCastingMode = key.Shadows;
                r.receiveShadows = true;
                if (key.Collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.AddComponent<Sliceable>();
                var el = go.AddComponent<MeshElements>();
                el.Set(mesh, g.ElemV0.ToArray(), g.ElemVN.ToArray(), g.ElemT0.ToArray(), g.ElemTN.ToArray(), g.ElemBounds.ToArray());
                // CPU copies for the slicer (no GPU readback on the first cut).
                el.SetData(g.P.ToArray(), g.N.ToArray(), g.C.ToArray(), groupUV ? g.U.ToArray() : null, g.T.ToArray());
            }
            s_v.Clear();
            s_n.Clear();
            s_t.Clear();
            s_c.Clear();
            s_u.Clear();
            return merged;
        }

        /// <summary>Per-instance tint of a decor object (shared with Arch.Bake, which merges Decor too).</summary>
        internal struct DecorTint
        {
            public float Value, Hue, Base;
        }

        /// <summary>Tint from the object's root-local position (identical for a world root and its diorama copy).</summary>
        internal static DecorTint TintFor(Decor info, Vector3 rootLocalObjectPosition)
        {
            Vector3 op = rootLocalObjectPosition;
            float h1 = Hash(op.x * 0.731f + op.z * 1.379f + op.y * 0.417f);
            float h2 = Hash(op.x * 1.913f - op.z * 0.613f + 7.1f);
            return new DecorTint
            {
                Value = (h1 * 2f - 1f) * info.Tint,
                Hue = (h2 * 2f - 1f) * info.Tint * 0.6f,
                Base = 1f - 1.6f * info.Tint,
            };
        }

        /// <summary>
        /// Vertex colour of a decor vertex at root-local height <paramref name="y"/>: contact shade gradient, tint
        /// jitter, the source colour multiplied in, and the sway weight (0 at the base, 1 at the top) in alpha.
        /// </summary>
        internal static Color32 Shade(Decor info, DecorTint k, float y, Color32 source)
        {
            float t = Mathf.Clamp01((y - info.BaseY) / Mathf.Max(0.01f, info.Height));
            float ao = Mathf.Lerp(info.AoMin, 1f, Mathf.Sqrt(info.AoFromTop ? 1f - t : t));
            // Colours are multipliers (<= 1): centre the jitter just below white.
            float r = ao * (k.Base + k.Value + k.Hue), g = ao * (k.Base + k.Value), b = ao * (k.Base + k.Value - k.Hue);
            // The source mesh's own colours (e.g. the contact disc's radial gradient) multiply in.
            r *= source.r / 255f; g *= source.g / 255f; b *= source.b / 255f;
            return new Color32(ToByte(r), ToByte(g), ToByte(b), info.SwayWeight ? ToByte(t) : (byte)255);
        }

        static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        static float Hash(float x)
        {
            float s = Mathf.Sin(x * 12.9898f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }
    }
}
