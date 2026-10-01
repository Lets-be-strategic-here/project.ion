using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Presentation
{
    /// <summary>
    /// Distant scenery so every view has depth beyond the rooms, in the "Light Table" language (art bible §1–§2):
    /// a ring of floating off-white terraces with stepped planar undersides carrying stacked slab towers (one with
    /// a spillway waterfall and mist), stepped rectilinear plateaus and soft mountains on the horizon, and a sea
    /// of cloud clumps below. Every built form is axis-aligned boxes (right angles only; curves are left to the
    /// clouds, mountains and cypresses). One procedural mesh, one renderer, one draw call; Ion/Backdrop shading
    /// (no shadows, aerial perspective into the sky colour). Deterministic.
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
        static readonly Color32 Lilac = new Color32(0x6E, 0x5E, 0x9A, 0xFF);   // shade target for forms
        static readonly Color32 MountainRock = new Color32(0xA9, 0xA4, 0xCC, 0xFF);
        static readonly Color32 MountainLight = new Color32(0xC2, 0xB9, 0xDA, 0xFF);
        static readonly Color32 MountainCap = new Color32(0xF6, 0xEC, 0xEA, 0xFF);
        static readonly Color32 MesaTop = new Color32(0xB8, 0xCF, 0x92, 0xFF);
        static readonly Color32 Water = new Color32(0xE4, 0xF4, 0xF8, 0xF0);
        static readonly Color32 Mist = new Color32(0xFF, 0xFC, 0xF6, 0xE0);
        static readonly Color32 CloudWhite = new Color32(0xFF, 0xFD, 0xF8, 0xFF);
        static readonly Color32 CloudCream = new Color32(0xFF, 0xF1, 0xE2, 0xFF);
        static readonly Color32 CloudUnder = new Color32(0xE6, 0xC6, 0xDA, 0xFF);   // pink-lilac belly

        // Light Table architecture at a distance (Paper / Limestone / Concrete, shade towards lilac).
        static readonly Color32 PaperTop = new Color32(0xF4, 0xF1, 0xEA, 0xFF);
        static readonly Color32 PaperSide = new Color32(0xEF, 0xEB, 0xE3, 0xFF);
        static readonly Color32 LimeTop = new Color32(0xE2, 0xDA, 0xCA, 0xFF);
        static readonly Color32 LimeSide = new Color32(0xD7, 0xCD, 0xBB, 0xFF);
        static readonly Color32 ConcreteSide = new Color32(0xBD, 0xB8, 0xAE, 0xFF);
        static readonly Color32 Underside = Color32.Lerp(new Color32(0xBD, 0xB8, 0xAE, 0xFF), new Color32(0x6E, 0x5E, 0x9A, 0xFF), 0.35f);
        static readonly Color32 GapShade = Color32.Lerp(new Color32(0xBD, 0xB8, 0xAE, 0xFF), new Color32(0x6E, 0x5E, 0x9A, 0xFF), 0.5f);
        static readonly Color32 Cypress = new Color32(0x6E, 0x9A, 0x5C, 0xFF);

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
            // Stepped rectilinear plateaus (three receding tiers), the base lost in the haze.
            const int count = 12;
            for (int i = 0; i < count; i++)
            {
                float ang = i * 360f / count + 15f + R(rng, -8f, 8f);
                float dist = R(rng, 470f, 560f);
                Vector3 c = Snap(Polar(ang, dist, -120f), 2f);
                float w = Mathf.Round(R(rng, 80f, 150f) / 4f) * 4f, d = Mathf.Round(R(rng, 60f, 110f) / 4f) * 4f;
                float top = Mathf.Round(R(rng, -30f, 8f) / 2f) * 2f + 120f;
                b.Plateau(c, w, d, top, rng);
            }
        }

        static void BuildIslands(Builder b, System.Random rng)
        {
            const int count = 16;
            for (int i = 0; i < count; i++)
            {
                float ang = i * 360f / count + R(rng, -7f, 7f);
                float dist = R(rng, 250f, 410f);
                // 0.5x - 2x of a 30 m terrace, log-uniform: a few big ones, many small ones.
                float size = 30f * 0.5f * Mathf.Pow(4f, R(rng, 0f, 1f));
                float top = Mathf.Round(R(rng, -28f, 42f));
                b.Terrace(Snap(Polar(ang, dist, top), 1f), size, size * R(rng, 0.6f, 1f), rng, size < 20f ? rng.Next(1, 3) : rng.Next(2, 5));
            }
            // Feature terrace ahead of the walking direction (+Z), with a spillway waterfall and mist below.
            Vector3 f = Snap(Polar(18f, 330f, 34f), 1f);
            b.Terrace(f, 64f, 48f, rng, 5);
            Vector3 lip = f + new Vector3(0f, -1.4f, -24f); // the edge facing the rooms
            b.Waterfall(lip, 6f, 95f, 180f);
            for (int k = 0; k < 4; k++)
                b.Puff(lip + new Vector3(R(rng, -9f, 9f), -95f + R(rng, -4f, 6f), R(rng, -9f, 9f)), new Vector3(R(rng, 9f, 15f), R(rng, 4f, 6f), R(rng, 9f, 15f)), rng, Mist);
        }

        static Vector3 Snap(Vector3 v, float step) =>
            new Vector3(Mathf.Round(v.x / step) * step, v.y, Mathf.Round(v.z / step) * step);

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

            /// <summary>Axis-aligned box (right angles only) with top / side / bottom colours.</summary>
            public void Box(Vector3 min, Vector3 max, Color32 top, Color32 side, Color32 bottom)
            {
                Vector3 a0 = new Vector3(min.x, min.y, min.z), a1 = new Vector3(max.x, min.y, min.z),
                        a2 = new Vector3(max.x, min.y, max.z), a3 = new Vector3(min.x, min.y, max.z),
                        b0 = new Vector3(min.x, max.y, min.z), b1 = new Vector3(max.x, max.y, min.z),
                        b2 = new Vector3(max.x, max.y, max.z), b3 = new Vector3(min.x, max.y, max.z);
                Quad(b0, b3, b2, b1, top);       // +y
                Quad(a0, a1, a2, a3, bottom);    // -y
                Quad(a0, b0, b1, a1, side);      // -z
                Quad(a2, b2, b3, a3, side);      // +z
                Quad(a3, b3, b0, a0, side);      // -x
                Quad(a1, b1, b2, a2, side);      // +x
            }

            /// <summary>
            /// A floating Light Table terrace: limestone slab with a drip line, an off-white body, a stepped planar
            /// underside (no rock spikes), and stacked slab towers (2 m-module slabs offset by quarter steps, each with
            /// a dark shadow gap) plus a few cypresses on top.
            /// </summary>
            public void Terrace(Vector3 topCenter, float width, float depth, System.Random rng, int towers)
            {
                float hw = Mathf.Round(width * 0.5f), hd = Mathf.Round(depth * 0.5f);
                Vector3 c = topCenter;
                float unit = Mathf.Max(1f, Mathf.Round(width / 24f));     // a "metre" of this terrace's grid
                // Slab + drip groove + body.
                Box(c + new Vector3(-hw, -unit, -hd), c + new Vector3(hw, 0f, hd), LimeTop, LimeSide, Underside);
                Box(c + new Vector3(-hw + unit * 0.5f, -unit * 1.5f, -hd + unit * 0.5f), c + new Vector3(hw - unit * 0.5f, -unit, hd - unit * 0.5f), GapShade, GapShade, GapShade);
                float body = Mathf.Round(R(rng, 3f, 6f)) * unit;
                Box(c + new Vector3(-hw, -unit * 1.5f - body, -hd), c + new Vector3(hw, -unit * 1.5f, hd), PaperTop, Color32.Lerp(PaperSide, Lilac, 0.08f), Underside);
                float y = -unit * 1.5f - body;
                int steps = 2 + rng.Next(0, 2);
                for (int k = 1; k <= steps; k++)
                {
                    float inset = k * 2f * unit;
                    if (hw - inset < unit * 2f || hd - inset < unit * 2f) break;
                    float h = 2f * unit;
                    Box(c + new Vector3(-hw + inset, y - h, -hd + inset), c + new Vector3(hw - inset, y, hd - inset),
                        Underside, Color32.Lerp(ConcreteSide, Lilac, 0.15f + 0.08f * k), Underside);
                    y -= h;
                }

                // Slab towers on a quarter-step grid.
                for (int t = 0; t < towers; t++)
                {
                    float fx = Mathf.Round(R(rng, -0.6f, 0.6f) * hw / unit) * unit, fz = Mathf.Round(R(rng, -0.6f, 0.6f) * hd / unit) * unit;
                    float sx = Mathf.Round(R(rng, 3f, 6f)) * unit, sz = Mathf.Round(R(rng, 3f, 6f)) * unit;
                    int slabs = 1 + rng.Next(0, 4);
                    float ty = 0f, slab = 2f * unit;
                    for (int k = 0; k < slabs; k++)
                    {
                        float ox = (rng.Next(5) - 2) * 0.25f * unit, oz = (rng.Next(5) - 2) * 0.25f * unit;
                        Vector3 o = c + new Vector3(fx + ox, ty, fz + oz);
                        bool paper = (k & 1) == 0;
                        Box(o + new Vector3(-sx * 0.5f, 0f, -sz * 0.5f), o + new Vector3(sx * 0.5f, slab - 0.125f * unit, sz * 0.5f),
                            paper ? PaperTop : LimeTop, paper ? PaperSide : ConcreteSide, Underside);
                        Box(o + new Vector3(-sx * 0.5f + 0.125f * unit, slab - 0.125f * unit, -sz * 0.5f + 0.125f * unit),
                            o + new Vector3(sx * 0.5f - 0.125f * unit, slab, sz * 0.5f - 0.125f * unit), GapShade, GapShade, GapShade);
                        ty += slab;
                    }
                }

                // A few cypresses (living things may be round).
                int trees = 1 + rng.Next(0, 3);
                for (int k = 0; k < trees; k++)
                {
                    Vector3 p = c + new Vector3(R(rng, -0.8f, 0.8f) * hw, 0f, R(rng, -0.8f, 0.8f) * hd);
                    float s = R(rng, 2.2f, 3.6f) * Mathf.Clamp(width / 30f, 0.7f, 1.4f);
                    Vector3[] tr = Ring(p + Vector3.up * s * 0.2f, s * 0.38f, 6, R(rng, 0f, 60f), null, 0f);
                    Apex(tr, p + Vector3.up * s * 2.6f, Cypress, true);
                    Cap(tr, p + Vector3.up * s * 0.2f, Cypress, false);
                }
            }

            /// <summary>Three receding rectilinear tiers (horizon plateaus), walls shaded towards lilac.</summary>
            public void Plateau(Vector3 baseCenter, float width, float depth, float height, System.Random rng)
            {
                float y0 = 0f;
                float[] tiers = { 0.72f, 0.86f, 1f };
                float inset = 0f;
                for (int k = 0; k < tiers.Length; k++)
                {
                    float y1 = height * tiers[k];
                    float hw = width * 0.5f - inset, hd = depth * 0.5f - inset;
                    Color32 wall = Color32.Lerp(k == 1 ? LimeSide : PaperSide, Lilac, 0.28f - 0.05f * k);
                    Box(baseCenter + new Vector3(-hw, y0, -hd), baseCenter + new Vector3(hw, y1, hd),
                        k == tiers.Length - 1 ? MesaTop : LimeTop, wall, Underside);
                    y0 = y1;
                    inset += Mathf.Round(R(rng, 4f, 9f));
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
