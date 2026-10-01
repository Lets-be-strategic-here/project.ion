using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Presentation
{
    /// <summary>
    /// Distant scenery so every view has depth beyond the rooms: a ring of low-poly floating islands
    /// (one with a waterfall and mist), soft mountains and mesas on the horizon and a sea of cloud clumps
    /// below. One procedural mesh, one renderer, one draw call; Ion/Backdrop shading (no shadows, aerial
    /// perspective into the sky colour). Deterministic.
    ///
    /// Depth layers: every vertex carries its layer's haze in uv0.x (near islands and clouds ~40 %, mesas
    /// ~60 %, mountains ~80 %), so the three bands separate instead of reading as one flat cut-out.
    /// Island undersides and mesa walls are shaded towards a darker lilac so their forms read.
    ///
    /// Not <c>Sliceable</c>: photos never capture or cut it. It is world-anchored around the rooms; when a
    /// photo is captured from a diorama (1000 m below), the shader shifts it by the room-to-diorama offset
    /// (<see cref="SetDioramaMapping"/>), so the photo shows it exactly where it appears from the
    /// matching spot in the world. Its mesh bounds cover both places, so it is never culled for either.
    /// </summary>
    public static class Backdrop
    {
        public const string ShaderName = "Ion/Backdrop";
        public const string ObjectName = "Ion Backdrop";
        const int Seed = 7311;

        static readonly int BackdropMapId = Shader.PropertyToID("_IonBackdropMap");

        // Per-layer haze (uv0.x), see Ion/Backdrop.
        const float HazeIslands = 0.36f, HazeClouds = 0.4f, HazeMesas = 0.56f, HazeMountains = 0.8f;

        // Vertex colours (sRGB albedo; alpha = 1 - extra haze).
        static readonly Color32 Grass = new Color32(0xA6, 0xD0, 0x86, 0xFF);
        static readonly Color32 GrassLip = new Color32(0x94, 0xC4, 0x78, 0xFF);
        static readonly Color32 Dirt = new Color32(0xEE, 0xD2, 0xAE, 0xFF);
        static readonly Color32 DirtDark = new Color32(0xDC, 0xB8, 0x98, 0xFF);
        static readonly Color32 Rock = new Color32(0xB0, 0x9C, 0xB2, 0xFF);
        static readonly Color32 Lilac = new Color32(0x6E, 0x5E, 0x9A, 0xFF);   // shade target for forms
        static readonly Color32 RockUnder = Color32.Lerp(new Color32(0xB0, 0x9C, 0xB2, 0xFF), new Color32(0x6E, 0x5E, 0x9A, 0xFF), 0.35f);
        static readonly Color32 Leaves = new Color32(0x8C, 0xBC, 0x86, 0xFF);
        static readonly Color32 MountainRock = new Color32(0xA9, 0xA4, 0xCC, 0xFF);
        static readonly Color32 MountainLight = new Color32(0xC2, 0xB9, 0xDA, 0xFF);
        static readonly Color32 MountainCap = new Color32(0xF6, 0xEC, 0xEA, 0xFF);
        static readonly Color32 MesaRock = new Color32(0xEC, 0xC2, 0xA6, 0xFF);
        static readonly Color32 MesaBand = new Color32(0xDE, 0xAA, 0x94, 0xFF);
        static readonly Color32 MesaTop = new Color32(0xB8, 0xCF, 0x92, 0xFF);
        static readonly Color32 Water = new Color32(0xE4, 0xF4, 0xF8, 0xF0);
        static readonly Color32 Mist = new Color32(0xFF, 0xFC, 0xF6, 0xE0);
        static readonly Color32 CloudWhite = new Color32(0xFF, 0xFD, 0xF8, 0xFF);
        static readonly Color32 CloudCream = new Color32(0xFF, 0xF1, 0xE2, 0xFF);
        static readonly Color32 CloudUnder = new Color32(0xE6, 0xC6, 0xDA, 0xFF);   // pink-lilac belly

        /// <summary>
        /// Builds the backdrop around <paramref name="center"/> (the middle of the rooms, floor level).
        /// Returns null if the shader is missing from the build.
        /// </summary>
        public static GameObject Build(Vector3 center)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("[Backdrop] Shader '" + ShaderName + "' not found / unsupported; no distant scenery.");
                return null;
            }

            var b = new Builder();
            var rng = new System.Random(Seed);
            b.Layer = HazeMountains;
            BuildMountains(b, rng);
            b.Layer = HazeMesas;
            BuildMesas(b, rng);
            b.Layer = HazeIslands;
            BuildIslands(b, rng);
            b.Layer = HazeClouds;
            BuildCloudSea(b, rng);

            Mesh mesh = b.ToMesh("Ion_Backdrop");
            // Never culled: covers the ring around the rooms and its copies around the dioramas (x up to
            // +600 m, 1000 m down) that the shader draws for diorama captures.
            mesh.bounds = new Bounds(new Vector3(300f, -480f, 0f), new Vector3(2400f, 1900f, 2000f));

            var go = new GameObject(ObjectName) { layer = 0 };
            go.transform.position = center;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(shader) { name = "Ion_Backdrop" };
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return go;
        }

        /// <summary>
        /// Cameras below <paramref name="belowY"/> see the backdrop shifted by (i · <paramref name="xPerIndex"/>,
        /// <paramref name="dioramaY"/>, 0), i = round(camera x / <paramref name="dioramaSpacing"/>).
        /// </summary>
        public static void SetDioramaMapping(float belowY, float dioramaSpacing, float xPerIndex, float dioramaY)
        {
            Shader.SetGlobalVector(BackdropMapId, new Vector4(belowY, dioramaSpacing, xPerIndex, dioramaY));
        }

        // ------------------------------------------------------------------ pieces

        static float R(System.Random rng, float a, float b) => a + (b - a) * (float)rng.NextDouble();

        static Vector3 Polar(float angleDeg, float radius, float y)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius);
        }

        static void BuildMountains(Builder b, System.Random rng)
        {
            const int count = 24;
            for (int i = 0; i < count; i++)
            {
                float ang = i * 360f / count + R(rng, -5f, 5f);
                float dist = R(rng, 640f, 800f);
                float baseY = -140f;
                // Broad, soft shoulders: most of the body sits in the horizon haze, peaks 0-90 m up.
                float h = R(rng, 140f, 200f) * (i % 3 == 0 ? 1.12f : 1f);
                float radius = R(rng, 170f, 250f);
                b.Mountain(Polar(ang, dist, baseY), radius, h, R(rng, 0f, 360f), 7 + (i % 3), rng, i % 2 == 0 ? MountainRock : MountainLight);
            }
        }

        static void BuildMesas(Builder b, System.Random rng)
        {
            const int count = 12;
            for (int i = 0; i < count; i++)
            {
                float ang = i * 360f / count + 15f + R(rng, -8f, 8f);
                float dist = R(rng, 470f, 560f);
                // Broad, flat-topped buttes (wider than the visible height), the base lost in the haze.
                float radius = R(rng, 48f, 80f);
                float top = R(rng, -30f, 8f);
                b.Mesa(Polar(ang, dist, -120f), radius, top + 120f, R(rng, 0f, 360f), 8, rng);
            }
        }

        static void BuildIslands(Builder b, System.Random rng)
        {
            const int count = 16;
            for (int i = 0; i < count; i++)
            {
                float ang = i * 360f / count + R(rng, -7f, 7f);
                float dist = R(rng, 250f, 410f);
                // 0.5x - 2x of a 15 m island, log-uniform: a few big ones, many small ones.
                float radius = 15f * 0.5f * Mathf.Pow(4f, R(rng, 0f, 1f));
                float top = R(rng, -28f, 42f);
                b.Island(Polar(ang, dist, top), radius, R(rng, 0f, 360f), rng, radius < 10f ? rng.Next(0, 2) : rng.Next(1, 5));
            }
            // Feature island ahead of the walking direction (+Z), with a waterfall and mist below.
            Vector3 f = Polar(18f, 330f, 34f);
            b.Island(f, 32f, 20f, rng, 5);
            Vector3 lip = f + Polar(198f, 27f, -1.4f); // edge facing the rooms
            b.Waterfall(lip, 6f, 95f, 198f);
            for (int k = 0; k < 4; k++)
                b.Puff(lip + new Vector3(R(rng, -9f, 9f), -95f + R(rng, -4f, 6f), R(rng, -9f, 9f)), new Vector3(R(rng, 9f, 15f), R(rng, 4f, 6f), R(rng, 9f, 15f)), rng, Mist);
        }

        static void BuildCloudSea(Builder b, System.Random rng)
        {
            // A few big, round cumulus clumps low in the haze: smooth-shaded spheres, white crowns, a
            // pink-lilac belly that fades into the haze at the base (many small flat puffs read as shards).
            const int count = 14;
            for (int i = 0; i < count; i++)
            {
                float ang = i * 360f / count + R(rng, -9f, 9f);
                float dist = R(rng, 280f, 600f);
                Vector3 c = Polar(ang, dist, R(rng, -115f, -90f));
                Vector3 along = Polar(ang + 90f, 1f, 0f);
                Vector3 outward = Polar(ang, 1f, 0f);
                float size = R(rng, 0.8f, 1.35f);
                int puffs = 4 + (i % 3);
                for (int k = 0; k < puffs; k++)
                {
                    // A central tall puff with smaller ones stepping down to each side.
                    float side = k == 0 ? 0f : ((k % 2 == 1) ? 1f : -1f) * (0.45f + 0.3f * ((k - 1) / 2));
                    float s = size * (k == 0 ? R(rng, 30f, 36f) : R(rng, 18f, 26f) * (1f - 0.25f * Mathf.Abs(side)));
                    Vector3 o = along * (side * 46f * size) + outward * R(rng, -10f, 10f) + Vector3.up * (k == 0 ? 5f : R(rng, -5f, 0f));
                    b.CloudPuff(c + o, new Vector3(s * R(rng, 1.15f, 1.35f), s * R(rng, 0.66f, 0.76f), s), rng,
                                (i + k) % 3 == 0 ? CloudCream : CloudWhite, CloudUnder, c.y - 6f, c.y + 30f * size);
                }
            }
        }

        // ------------------------------------------------------------------ mesh builder

        /// <summary>Flat-shaded triangle soup with vertex colours.</summary>
        sealed class Builder
        {
            readonly List<Vector3> _p = new List<Vector3>(16384);
            readonly List<Vector3> _n = new List<Vector3>(16384);
            readonly List<Color32> _c = new List<Color32>(16384);
            readonly List<int> _t = new List<int>(16384);
            readonly List<Vector2> _uv = new List<Vector2>(16384);

            /// <summary>Haze of the depth layer being built (written to uv0.x).</summary>
            public float Layer;

            /// <summary>1 for soft-lit vertices (clouds: wrapped light, no hard terminator), written to uv0.y.</summary>
            public float Soft;

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Color32 col)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                float len = n.magnitude;
                if (len < 1e-6f) return;
                n /= len;
                TriSmooth(a, b, c, n, n, n, col, col, col);
            }

            /// <summary>Triangle with per-vertex normals and colours (smooth-shaded clouds).</summary>
            public void TriSmooth(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Color32 ca, Color32 cb, Color32 cc)
            {
                int i = _p.Count;
                _p.Add(a); _p.Add(b); _p.Add(c);
                _n.Add(na); _n.Add(nb); _n.Add(nc);
                _c.Add(ca); _c.Add(cb); _c.Add(cc);
                var uv = new Vector2(Layer, Soft);
                _uv.Add(uv); _uv.Add(uv); _uv.Add(uv);
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            }

            /// <summary>Quad a-b-c-d (front face = clockwise seen from outside, Unity convention).</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col)
            {
                Tri(a, b, c, col);
                Tri(a, c, d, col);
            }

            static Vector3[] Ring(Vector3 c, float radius, int n, float rot, System.Random rng, float jitter)
            {
                var r = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    float a = (rot + i * 360f / n) * Mathf.Deg2Rad;
                    float rr = radius * (1f + (rng != null ? R(rng, -jitter, jitter) : 0f));
                    r[i] = c + new Vector3(Mathf.Sin(a) * rr, 0f, Mathf.Cos(a) * rr);
                }
                return r;
            }

            /// <summary>Side band between two rings (lower <paramref name="lo"/>, upper <paramref name="hi"/>), outward faces.</summary>
            void Band(Vector3[] lo, Vector3[] hi, Color32 col)
            {
                int n = lo.Length;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    // Unity front faces: normal = cross(b - a, c - a); with the rings' sin/cos order
                    // lo[i] -> lo[j] -> hi[j] faces outwards.
                    Quad(lo[i], lo[j], hi[j], hi[i], col);
                }
            }

            void Cap(Vector3[] ring, Vector3 center, Color32 col, bool up)
            {
                int n = ring.Length;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    if (up) Tri(center, ring[i], ring[j], col);
                    else Tri(center, ring[j], ring[i], col);
                }
            }

            void Apex(Vector3[] ring, Vector3 apex, Color32 col, bool up)
            {
                int n = ring.Length;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    if (up) Tri(ring[j], apex, ring[i], col);
                    else Tri(ring[i], apex, ring[j], col);
                }
            }

            public void Mountain(Vector3 baseCenter, float radius, float height, float rot, int sides, System.Random rng, Color32 col)
            {
                Vector3[] r0 = Ring(baseCenter, radius, sides, rot, rng, 0.18f);
                Vector3 midC = baseCenter + new Vector3(R(rng, -0.1f, 0.1f) * radius, height * R(rng, 0.5f, 0.6f), R(rng, -0.1f, 0.1f) * radius);
                Vector3[] r1 = Ring(midC, radius * R(rng, 0.42f, 0.55f), sides, rot + 360f / sides * 0.5f, rng, 0.2f);
                Vector3 hiC = baseCenter + new Vector3(R(rng, -0.12f, 0.12f) * radius, height * R(rng, 0.8f, 0.86f), R(rng, -0.12f, 0.12f) * radius);
                Vector3[] r2 = Ring(hiC, radius * R(rng, 0.14f, 0.2f), sides, rot, rng, 0.25f);
                Vector3 apex = baseCenter + new Vector3(R(rng, -0.1f, 0.1f) * radius, height, R(rng, -0.1f, 0.1f) * radius);
                Band(r0, r1, col);
                Band(r1, r2, Color32.Lerp(col, MountainCap, 0.35f));
                Apex(r2, apex, MountainCap, true);
            }

            public void Mesa(Vector3 baseCenter, float radius, float height, float rot, int sides, System.Random rng)
            {
                Vector3[] r0 = Ring(baseCenter, radius, sides, rot, rng, 0.12f);
                float y1 = height * 0.72f, y2 = height * 0.8f;
                Vector3[] r1 = Ring(baseCenter + Vector3.up * y1, radius * 0.86f, sides, rot, rng, 0.06f);
                Vector3[] r2 = Ring(baseCenter + Vector3.up * y2, radius * 0.83f, sides, rot, rng, 0.04f);
                Vector3[] r3 = Ring(baseCenter + Vector3.up * height, radius * 0.74f, sides, rot, rng, 0.04f);
                // Walls shaded towards lilac (the base a little more), so the flat-topped form reads.
                Band(r0, r1, Color32.Lerp(MesaRock, Lilac, 0.28f));
                Band(r1, r2, Color32.Lerp(MesaBand, Lilac, 0.22f));
                Band(r2, r3, Color32.Lerp(MesaRock, Lilac, 0.18f));
                Cap(r3, baseCenter + Vector3.up * height, MesaTop, true);
            }

            public void Island(Vector3 topCenter, float radius, float rot, System.Random rng, int trees)
            {
                int sides = 7;
                Vector3[] top = Ring(topCenter, radius, sides, rot, rng, 0.14f);
                Vector3[] lip = new Vector3[sides];
                Vector3[] body = new Vector3[sides];
                Vector3[] band = new Vector3[sides];
                float lipH = radius * 0.06f + 0.6f;
                float bodyH = radius * R(rng, 0.28f, 0.4f);
                for (int i = 0; i < sides; i++)
                {
                    Vector3 o = top[i] - topCenter;
                    lip[i] = topCenter + o * 1.0f + Vector3.down * lipH;
                    band[i] = topCenter + o * 0.95f + Vector3.down * (lipH + bodyH * 0.45f);
                    body[i] = topCenter + o * 0.82f + Vector3.down * (lipH + bodyH);
                }
                Cap(top, topCenter, Grass, true);
                Band(lip, top, GrassLip);
                Band(band, lip, Dirt);
                Band(body, band, DirtDark);

                // Ragged rocky underside: a jagged mid ring (each vertex at its own depth and radius),
                // the main spike, and a few smaller hanging spikes, all shaded towards lilac.
                float depth = radius * R(rng, 0.9f, 1.5f);
                float bodyY = lipH + bodyH;
                Vector3[] mid = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    Vector3 o = top[i] - topCenter;
                    mid[i] = topCenter + o * R(rng, 0.42f, 0.66f) + Vector3.down * (bodyY + depth * R(rng, 0.22f, 0.5f));
                }
                Vector3 apex = topCenter + new Vector3(R(rng, -0.15f, 0.15f) * radius, -(bodyY + depth), R(rng, -0.15f, 0.15f) * radius);
                Band(mid, body, Color32.Lerp(Rock, Lilac, 0.18f));
                Apex(mid, apex, RockUnder, false);
                int spikes = 1 + rng.Next(0, 3);
                for (int k = 0; k < spikes; k++)
                {
                    int i = rng.Next(0, sides);
                    Vector3 basePt = Vector3.Lerp(Vector3.Lerp(body[i], body[(i + 1) % sides], 0.5f), mid[i], 0.35f);
                    float sr = radius * R(rng, 0.14f, 0.22f);
                    Vector3[] sring = Ring(basePt + Vector3.up * sr * 0.4f, sr, 4, R(rng, 0f, 90f), rng, 0.15f);
                    Apex(sring, basePt + Vector3.down * depth * R(rng, 0.35f, 0.6f), RockUnder, false);
                }

                for (int k = 0; k < trees; k++)
                {
                    float a = R(rng, 0f, 360f) * Mathf.Deg2Rad;
                    float d = R(rng, 0.1f, 0.65f) * radius;
                    Vector3 p = topCenter + new Vector3(Mathf.Sin(a) * d, 0f, Mathf.Cos(a) * d);
                    float s = R(rng, 2.2f, 4.2f) * Mathf.Clamp(radius / 18f, 0.7f, 1.4f);
                    Vector3[] tr = Ring(p + Vector3.up * s * 0.35f, s * 0.55f, 6, R(rng, 0f, 60f), null, 0f);
                    Apex(tr, p + Vector3.up * s * 2.1f, Leaves, true);
                    Cap(tr, p + Vector3.up * s * 0.35f, Leaves, false);
                }
            }

            public void Waterfall(Vector3 top, float width, float drop, float facingDeg)
            {
                // A thin sheet bending outwards a little, hazier towards the bottom (alpha = 1 - extra haze).
                Vector3 outward = Polar(facingDeg, 1f, 0f);
                Vector3 side = Vector3.Cross(Vector3.up, outward).normalized * (width * 0.5f);
                const int steps = 5;
                for (int i = 0; i < steps; i++)
                {
                    float t0 = i / (float)steps, t1 = (i + 1) / (float)steps;
                    Vector3 c0 = top + outward * (2.2f * t0 * t0 + 0.6f) + Vector3.down * (drop * t0);
                    Vector3 c1 = top + outward * (2.2f * t1 * t1 + 0.6f) + Vector3.down * (drop * t1);
                    var col = Water;
                    col.a = (byte)Mathf.RoundToInt(Mathf.Lerp(240f, 120f, t1));
                    float w0 = 1f + t0 * 0.5f, w1 = 1f + t1 * 0.5f;
                    // Front (towards the rooms) and back faces.
                    Quad(c0 - side * w0, c0 + side * w0, c1 + side * w1, c1 - side * w1, col);
                    Quad(c0 + side * w0, c0 - side * w0, c1 - side * w1, c1 + side * w1, col);
                }
            }

            static readonly Vector3[] s_Ico = BuildIco();
            static readonly int[] s_IcoTris =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            static Vector3[] BuildIco()
            {
                float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
                var v = new[]
                {
                    new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                    new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                    new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
                };
                for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized;
                return v;
            }

            /// <summary>A flattened, jittered 20-face puff (cloud / mist), flat bottom-ish.</summary>
            public void Puff(Vector3 c, Vector3 size, System.Random rng, Color32 col)
            {
                var v = new Vector3[s_Ico.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 p = s_Ico[i] * (1f + R(rng, -0.12f, 0.12f));
                    if (p.y < -0.2f) p.y = -0.2f + (p.y + 0.2f) * 0.35f; // flatter underside
                    v[i] = c + Vector3.Scale(p, size);
                }
                for (int i = 0; i < s_IcoTris.Length; i += 3)
                    Tri(v[s_IcoTris[i]], v[s_IcoTris[i + 1]], v[s_IcoTris[i + 2]], col);
            }

            static Vector3[] s_Sphere;
            static int[] s_SphereTris;

            /// <summary>Icosphere subdivided once (42 vertices, 80 faces), unit radius.</summary>
            static void EnsureSphere()
            {
                if (s_Sphere != null) return;
                var verts = new List<Vector3>(s_Ico);
                var tris = new List<int>();
                var cache = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int m)) return m;
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                    cache[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
                for (int i = 0; i < s_IcoTris.Length; i += 3)
                {
                    int a = s_IcoTris[i], b = s_IcoTris[i + 1], c = s_IcoTris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                s_Sphere = verts.ToArray();
                s_SphereTris = tris.ToArray();
            }

            /// <summary>
            /// A round, smooth-shaded cloud puff: <paramref name="top"/> colour on the crown, shading to
            /// <paramref name="under"/> below, with extra haze (vertex alpha) towards <paramref name="baseY"/>
            /// so the clump melts into the haze; <paramref name="crownY"/> is the clump's top.
            /// </summary>
            public void CloudPuff(Vector3 c, Vector3 size, System.Random rng, Color32 top, Color32 under, float baseY, float crownY)
            {
                EnsureSphere();
                float soft = Soft;
                Soft = 1f;
                var v = new Vector3[s_Sphere.Length];
                var nrm = new Vector3[s_Sphere.Length];
                var col = new Color32[s_Sphere.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 p = s_Sphere[i] * (1f + R(rng, -0.06f, 0.06f));
                    if (p.y < -0.35f) p.y = -0.35f + (p.y + 0.35f) * 0.4f; // softly flattened base
                    v[i] = c + Vector3.Scale(p, size);
                    nrm[i] = Vector3.Scale(s_Sphere[i], new Vector3(1f / size.x, 1f / size.y, 1f / size.z)).normalized;
                    float h = Mathf.InverseLerp(baseY, crownY, v[i].y);
                    Color32 cc = Color32.Lerp(under, top, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.65f, h)));
                    cc.a = (byte)Mathf.RoundToInt(Mathf.Lerp(60f, 255f, Mathf.InverseLerp(0.05f, 0.55f, h)));
                    col[i] = cc;
                }
                for (int i = 0; i < s_SphereTris.Length; i += 3)
                {
                    int a = s_SphereTris[i], b = s_SphereTris[i + 1], d = s_SphereTris[i + 2];
                    TriSmooth(v[a], v[b], v[d], nrm[a], nrm[b], nrm[d], col[a], col[b], col[d]);
                }
                Soft = soft;
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (_p.Count > 65535) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(_p);
                m.SetNormals(_n);
                m.SetColors(_c);
                m.SetUVs(0, _uv);
                m.SetTriangles(_t, 0);
                m.UploadMeshData(true);
                return m;
            }
        }
    }
}
