using System.Collections.Generic;
using Ion.Projection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Presentation
{
    /// <summary>
    /// Drifting low-poly clouds: flattened, jittered, flat-shaded icosphere puffs (3-5 per cloud,
    /// one mesh + one renderer per cloud) in cream / white Ion/FlatToon. Placed below the islands and
    /// in a ring around the play area (never inside it), on the Default layer, with no collider, no
    /// <see cref="Sliceable"/> and no shadow casting. A slow sine drift + bob runs in one Update loop.
    /// </summary>
    public sealed class AmbienceClouds : MonoBehaviour
    {
        const int Seed = 9157;
        const int VariantCount = 4;
        static readonly int[] k_TierCounts = { 14, 20, 28 };

        public static int CountForTier(int tier) => k_TierCounts[Mathf.Clamp(tier, 0, k_TierCounts.Length - 1)];

        Transform[] _clouds = new Transform[0];
        Vector3[] _base = new Vector3[0];
        Vector3[] _driftDir = new Vector3[0];
        float[] _driftAmp = new float[0], _driftW = new float[0], _phase = new float[0];
        float[] _bobAmp = new float[0], _bobW = new float[0];

        readonly Mesh[] _variants = new Mesh[VariantCount];
        int _variantDetail = -1;
        int _builtCount = -1, _builtDetail = -1;
        Material _white, _cream;
        bool _haveBounds;
        Bounds _world;

        /// <summary>(Re)builds <paramref name="count"/> clouds. <paramref name="detail"/>: 0 = 20-face puffs, 1 = 80-face.</summary>
        public void Configure(int count, int detail)
        {
            if (count == _builtCount && detail == _builtDetail) return;
            EnsureMaterials();
            EnsureVariants(detail);
            if (!_haveBounds) { _world = ComputeWorldBounds(); _haveBounds = true; }

            for (int i = 0; i < _clouds.Length; i++)
                if (_clouds[i] != null) Destroy(_clouds[i].gameObject);

            _clouds = new Transform[count];
            _base = new Vector3[count];
            _driftDir = new Vector3[count];
            _driftAmp = new float[count];
            _driftW = new float[count];
            _phase = new float[count];
            _bobAmp = new float[count];
            _bobW = new float[count];

            var rng = new System.Random(Seed);
            Vector3 min = _world.min, max = _world.max;
            for (int i = 0; i < count; i++)
            {
                bool below = i % 5 < 2; // ~40 % drift under the islands, the rest ring the play area
                Vector3 pos;
                float scale;
                if (below)
                {
                    pos = new Vector3(
                        Lerp(rng, min.x - 25f, max.x + 25f),
                        min.y - Lerp(rng, 4f, 24f),
                        Lerp(rng, min.z - 25f, max.z + 25f));
                    scale = Lerp(rng, 2.2f, 4.2f);
                }
                else
                {
                    pos = RingPoint(rng, min, max, Lerp(rng, 22f, 70f));
                    pos.y = Lerp(rng, min.y + 2f, max.y + 28f);
                    scale = Lerp(rng, 2.6f, 5.2f);
                }

                var go = new GameObject("Cloud") { layer = 0 };
                Transform t = go.transform;
                t.SetParent(transform, false);
                t.localPosition = pos;
                t.localRotation = Quaternion.Euler(0f, Lerp(rng, 0f, 360f), 0f);
                t.localScale = new Vector3(scale, scale * Lerp(rng, 0.85f, 1.15f), scale);

                go.AddComponent<MeshFilter>().sharedMesh = _variants[i % VariantCount];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = (i % 3 == 0) ? _cream : _white;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;

                _clouds[i] = t;
                _base[i] = pos;
                // Common gentle wind along +X with a little spread.
                float a = Lerp(rng, -0.5f, 0.5f);
                _driftDir[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                _driftAmp[i] = Lerp(rng, 2f, 6f);
                _driftW[i] = 2f * Mathf.PI / Lerp(rng, 50f, 110f);
                _phase[i] = Lerp(rng, 0f, 2f * Mathf.PI);
                _bobAmp[i] = Lerp(rng, 0.2f, 0.6f);
                _bobW[i] = 2f * Mathf.PI / Lerp(rng, 8f, 15f);
            }

            _builtCount = count;
            _builtDetail = detail;
        }

        void Update()
        {
            if (!Ambience.CloudDriftEnabled) return;
            float t = Time.time;
            for (int i = 0; i < _clouds.Length; i++)
            {
                Transform c = _clouds[i];
                if (c == null) continue;
                float ph = _phase[i];
                Vector3 p = _base[i] + _driftDir[i] * (Mathf.Sin(t * _driftW[i] + ph) * _driftAmp[i]);
                p.y += Mathf.Sin(t * _bobW[i] + ph * 1.7f) * _bobAmp[i];
                c.localPosition = p;
            }
        }

        void OnDestroy()
        {
            for (int i = 0; i < _variants.Length; i++)
                if (_variants[i] != null) Destroy(_variants[i]);
            if (_white != null) Destroy(_white);
            if (_cream != null) Destroy(_cream);
        }

        // ------------------------------------------------------------------ placement

        static float Lerp(System.Random rng, float a, float b) => a + (b - a) * (float)rng.NextDouble();

        /// <summary>A point on the rectangle <paramref name="margin"/> metres outside the XZ bounds.</summary>
        static Vector3 RingPoint(System.Random rng, Vector3 min, Vector3 max, float margin)
        {
            float x0 = min.x - margin, x1 = max.x + margin, z0 = min.z - margin, z1 = max.z + margin;
            float w = x1 - x0, d = z1 - z0;
            float u = (float)rng.NextDouble() * 2f * (w + d);
            if (u < w) return new Vector3(x0 + u, 0f, z0);
            u -= w;
            if (u < w) return new Vector3(x0 + u, 0f, z1);
            u -= w;
            if (u < d) return new Vector3(x0, 0f, z0 + u);
            u -= d;
            return new Vector3(x1, 0f, z0 + u);
        }

        /// <summary>Bounds of the playable world (Sliceables above the diorama zone), with a fallback.</summary>
        static Bounds ComputeWorldBounds()
        {
            var b = new Bounds(new Vector3(100f, -5f, 0f), new Vector3(240f, 30f, 90f));
            bool any = false;
            Sliceable[] all = Object.FindObjectsByType<Sliceable>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i].GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;
                Bounds rb = r.bounds;
                if (rb.center.y < -500f) continue; // photo dioramas live at y = -1000
                if (!any) { b = rb; any = true; }
                else b.Encapsulate(rb);
            }
            return b;
        }

        // ------------------------------------------------------------------ materials / meshes

        void EnsureMaterials()
        {
            if (_white != null) return;
            _white = CreateCloudMaterial("Ion_Cloud_White", Palette.White);
            _cream = CreateCloudMaterial("Ion_Cloud_Cream", Palette.Cream);
        }

        static Material CreateCloudMaterial(string name, Color c)
        {
            var m = new Material(Palette.ToonShader) { name = name, enableInstancing = true };
            SetColor(m, "_BaseColor", c);
            SetColor(m, "_Color", c);
            SetColor(m, "_ShadowTint", new Color(0.80f, 0.84f, 0.96f, 1f)); // pale lavender-blue undersides
            SetFloat(m, "_LitAmbient", 0.75f);
            SetFloat(m, "_LitStrength", 0.45f);
            SetFloat(m, "_MidBand", 0.6f);
            SetFloat(m, "_RampSmooth", 0.1f);
            SetFloat(m, "_RimStrength", 0.35f);
            SetColor(m, "_EmissionColor", new Color(0.05f, 0.045f, 0.04f, 1f));
            return m;
        }

        static void SetColor(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }
        static void SetFloat(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        void EnsureVariants(int detail)
        {
            if (detail == _variantDetail && _variants[0] != null) return;
            var rng = new System.Random(Seed + 17);
            for (int i = 0; i < VariantCount; i++)
            {
                if (_variants[i] != null) Destroy(_variants[i]);
                _variants[i] = BuildCloudMesh(rng, detail, "Ion_Cloud" + i);
            }
            _variantDetail = detail;
        }

        /// <summary>
        /// 3-5 flattened, jittered icosphere puffs merged into one flat-shaded mesh with a flat base.
        /// Roughly 4.5 m wide and 1.3 m tall at scale 1.
        /// </summary>
        internal static Mesh BuildCloudMesh(System.Random rng, int subdiv, string name, bool readable = false)
        {
            BuildIcosphere(subdiv, out List<Vector3> sphereV, out List<int> sphereT);

            var verts = new List<Vector3>(2048);
            var normals = new List<Vector3>(2048);
            var tris = new List<int>(2048);
            var jittered = new Vector3[sphereV.Count];

            const float floorY = -0.3f;   // flat cloud base
            const float squash = 0.62f;   // vertical flattening
            int puffs = 3 + rng.Next(3);
            for (int p = 0; p < puffs; p++)
            {
                Vector3 c = p == 0
                    ? Vector3.zero
                    : new Vector3(Lerp(rng, -1.6f, 1.6f), Lerp(rng, -0.05f, 0.3f), Lerp(rng, -0.6f, 0.6f));
                float radius = p == 0 ? 1.2f : Lerp(rng, 0.6f, 1.0f);

                // Jitter the shared vertices first so neighbouring faces stay welded.
                for (int v = 0; v < sphereV.Count; v++)
                {
                    Vector3 q = sphereV[v] * (radius * Lerp(rng, 0.88f, 1.08f));
                    q.y *= squash;
                    q += c;
                    if (q.y < floorY) q.y = floorY;
                    jittered[v] = q;
                }

                for (int t = 0; t < sphereT.Count; t += 3)
                {
                    Vector3 a = jittered[sphereT[t]], b = jittered[sphereT[t + 1]], d = jittered[sphereT[t + 2]];
                    Vector3 n = Vector3.Cross(b - a, d - a);
                    float len = n.magnitude;
                    if (len < 1e-6f) continue; // degenerate after base flattening
                    n /= len;
                    Vector3 centroid = (a + b + d) / 3f;
                    if (Vector3.Dot(n, centroid - c) < 0f)
                    {
                        Vector3 tmp = b; b = d; d = tmp;
                        n = -n;
                    }
                    int start = verts.Count;
                    verts.Add(a); verts.Add(b); verts.Add(d);
                    normals.Add(n); normals.Add(n); normals.Add(n);
                    tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            if (readable)
            {
                // World (FlatToon) materials multiply by vertex colour: plain white (alpha as in Geo meshes).
                var colors = new List<Color32>(verts.Count);
                for (int i = 0; i < verts.Count; i++) colors.Add(new Color32(255, 255, 255, 255));
                mesh.SetColors(colors);
            }
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            if (!readable) mesh.UploadMeshData(true); // never read back on the CPU (photo clouds stay readable: they get cut)
            return mesh;
        }

        /// <summary>Unit icosphere (radius 1) with shared vertices; <paramref name="subdiv"/> 0 = 20 faces, 1 = 80.</summary>
        static void BuildIcosphere(int subdiv, out List<Vector3> v, out List<int> t)
        {
            float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
            v = new List<Vector3>
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            t = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int s = 0; s < subdiv; s++)
            {
                var mid = new Dictionary<long, int>();
                var nt = new List<int>(t.Count * 4);
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2];
                    int ab = Midpoint(v, mid, a, b), bc = Midpoint(v, mid, b, c), ca = Midpoint(v, mid, c, a);
                    nt.Add(a); nt.Add(ab); nt.Add(ca);
                    nt.Add(b); nt.Add(bc); nt.Add(ab);
                    nt.Add(c); nt.Add(ca); nt.Add(bc);
                    nt.Add(ab); nt.Add(bc); nt.Add(ca);
                }
                t = nt;
            }
        }

        static int Midpoint(List<Vector3> v, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            idx = v.Count;
            v.Add(((v[a] + v[b]) * 0.5f).normalized);
            cache[key] = idx;
            return idx;
        }
    }
}
