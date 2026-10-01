using System.Collections.Generic;
using Ion.Presentation;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Tests
{
    /// <summary>
    /// UV0 (pattern space + PatternCode, art bible §6.2) through the slicer: affine interpolation on edge splits,
    /// cap flags, repeated cuts, merges, and meshes without UV0.
    /// </summary>
    public class PatternUVTests
    {
        const int Code = 5; // Pat.Formwork
        const float Tol = 1e-4f;

        readonly List<Object> _cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup) if (o != null) Object.DestroyImmediate(o);
            _cleanup.Clear();
        }

        Mesh Track(Mesh m) { if (m != null) _cleanup.Add(m); return m; }

        // An arbitrary affine pattern map (rotation, non-uniform scale, offset): UV0.xyz = A·pos + o.
        static readonly Matrix4x4 A = Matrix4x4.TRS(new Vector3(3.5f, -1.25f, 7f), Quaternion.Euler(20f, 35f, -10f), new Vector3(1.3f, 0.7f, 2f));

        static Vector3 Pattern(Vector3 p) => A.MultiplyPoint3x4(p);

        /// <summary>Box with UV0 = (A·pos, Code) on every vertex.</summary>
        Mesh PatternBox(Vector3 center, Vector3 size)
        {
            Mesh m = Track(TestGeometry.Box(center, size));
            var uv = new List<Vector4>();
            foreach (Vector3 v in m.vertices)
            {
                Vector3 p = Pattern(v);
                uv.Add(new Vector4(p.x, p.y, p.z, Code));
            }
            m.SetUVs(0, uv);
            return m;
        }

        static List<Vector4> UVs(Mesh m)
        {
            var list = new List<Vector4>();
            m.GetUVs(0, list);
            return list;
        }

        static void AssertAffine(Mesh m, string label)
        {
            Vector3[] v = m.vertices;
            List<Vector4> uv = UVs(m);
            Assert.AreEqual(v.Length, uv.Count, label + ": one UV0 per vertex");
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 expected = Pattern(v[i]);
                Vector3 got = uv[i];
                Assert.Less((expected - got).magnitude, Tol * 10f, label + ": vertex " + i + " " + v[i] + " UV0 " + got + " expected " + expected);
            }
        }

        static void AssertCodes(Mesh m, string label)
        {
            foreach (Vector4 u in UVs(m))
            {
                int w = Mathf.RoundToInt(u.w);
                Assert.IsTrue(w == Code || w == Code + PatternCode.CapFlag, label + ": unexpected code " + u.w);
                Assert.AreEqual(w, u.w, 1e-4f, label + ": the code stays an exact integer");
            }
        }

        static Plane RandomPlane(System.Random rng)
        {
            var n = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
            if (n.sqrMagnitude < 1e-3f) n = Vector3.up;
            n.Normalize();
            float d = ((float)rng.NextDouble() * 2f - 1f) * 0.6f;
            return new Plane(n, d);
        }

        // ------------------------------------------------------------------ PatternCode

        [Test]
        public void PatternCode_EncodeDecodeAndCapFlag()
        {
            Assert.AreEqual(5f, PatternCode.Encode(Pat.Formwork));
            Assert.AreEqual(69f, PatternCode.Encode(Pat.Formwork, true));
            Assert.AreEqual(Pat.Formwork, PatternCode.Decode(69f, out bool cap));
            Assert.IsTrue(cap);
            Assert.AreEqual(Pat.Tile, PatternCode.Decode(2.0001f, out cap));
            Assert.IsFalse(cap);
            Assert.AreEqual(Pat.None, PatternCode.Decode(0f, out _), "0 = None");
            Assert.AreEqual(Pat.None, PatternCode.Decode(1f, out _), "1 = missing UV0 = None");
            Assert.AreEqual(Pat.None, PatternCode.Decode(65f, out cap), "a cut face of a mesh without pattern");
            Assert.IsTrue(cap);
            Assert.AreEqual(69f, PatternCode.SetCap(5f));
            Assert.AreEqual(69f, PatternCode.SetCap(PatternCode.SetCap(5f)), "idempotent");
            Assert.AreEqual(MeshClipper.CapFlag, PatternCode.CapFlag, "the clipper uses the same flag");
        }

        // ------------------------------------------------------------------ clipper (bible §6.2 tests)

        [Test]
        public void Clipper_UV0InterpolatesAffinely()
        {
            var rng = new System.Random(1234);
            for (int iter = 0; iter < 40; iter++)
            {
                Mesh box = PatternBox(new Vector3(0.1f, -0.2f, 0.3f), new Vector3(2f, 1.5f, 1f));
                int planeCount = 1 + iter % 4;
                var planes = new Plane[planeCount];
                for (int k = 0; k < planeCount; k++) planes[k] = RandomPlane(rng);

                Mesh inside = Track(MeshClipper.ClipInside(box, Matrix4x4.identity, planes));
                if (inside != null)
                {
                    AssertAffine(inside, "inside " + iter);
                    AssertCodes(inside, "inside " + iter);
                    TestGeometry.AssertWatertight(inside);
                }
                foreach (Mesh piece in MeshClipper.ClipOutside(box, Matrix4x4.identity, planes))
                {
                    Track(piece);
                    AssertAffine(piece, "outside " + iter);
                    AssertCodes(piece, "outside " + iter);
                }
            }
        }

        [Test]
        public void Clipper_UV0InterpolatesAffinely_UnderTransform()
        {
            // Planes are given in world space; UV0 is data and is never transformed.
            Mesh box = PatternBox(Vector3.zero, Vector3.one * 2f);
            Matrix4x4 l2w = Matrix4x4.TRS(new Vector3(100f, -20f, 40f), Quaternion.Euler(0f, 90f, 0f), new Vector3(2f, 1f, 0.5f));
            var planes = new[] { new Plane(new Vector3(1f, 0.3f, 0.2f).normalized, new Vector3(100.1f, -20f, 40f)) }; // through the transformed box
            Mesh inside = Track(MeshClipper.ClipInside(box, l2w, planes));
            Assert.IsNotNull(inside);
            AssertAffine(inside, "transformed");
        }

        [Test]
        public void Clipper_CapsFlagged()
        {
            Mesh box = PatternBox(Vector3.zero, Vector3.one * 2f);
            var planes = new[] { new Plane(Vector3.right, 0f) }; // keep x >= 0
            Mesh inside = Track(MeshClipper.ClipInside(box, Matrix4x4.identity, planes));
            Assert.IsNotNull(inside);

            Vector3[] v = inside.vertices, n = inside.normals;
            List<Vector4> uv = UVs(inside);
            int caps = 0;
            for (int i = 0; i < v.Length; i++)
            {
                bool isCap = Vector3.Dot(n[i], Vector3.left) > 0.999f && Mathf.Abs(v[i].x) < 1e-5f;
                int w = Mathf.RoundToInt(uv[i].w);
                if (isCap)
                {
                    caps++;
                    Assert.AreEqual(Code + PatternCode.CapFlag, w, "cap vertex " + v[i] + " carries the cap flag");
                }
                else
                {
                    Assert.AreEqual(Code, w, "surface vertex " + v[i] + " keeps its code");
                }
            }
            Assert.GreaterOrEqual(caps, 4, "the cap has its loop and the fan centre");
            AssertAffine(inside, "capped");

            // The outside half's cap faces +x and is flagged too.
            foreach (Mesh piece in MeshClipper.ClipOutside(box, Matrix4x4.identity, planes))
            {
                Track(piece);
                Vector3[] pv = piece.vertices, pn = piece.normals;
                List<Vector4> pu = UVs(piece);
                for (int i = 0; i < pv.Length; i++)
                    if (Vector3.Dot(pn[i], Vector3.right) > 0.999f && Mathf.Abs(pv[i].x) < 1e-5f)
                        Assert.AreEqual(Code + PatternCode.CapFlag, Mathf.RoundToInt(pu[i].w));
            }
        }

        [Test]
        public void Clipper_CutTwice_FlagsAndUVsStable()
        {
            Mesh box = PatternBox(Vector3.zero, Vector3.one * 2f);
            Mesh once = Track(MeshClipper.ClipInside(box, Matrix4x4.identity, new[] { new Plane(Vector3.right, 0f) }));
            // Second cut crosses the first cap (x = 0 face) and the original faces.
            var second = new[] { new Plane(new Vector3(0.3f, 0.2f, 1f).normalized, -0.1f) };
            Mesh twice = Track(MeshClipper.ClipInside(once, Matrix4x4.identity, second));
            Assert.IsNotNull(twice);
            AssertCodes(twice, "twice");
            AssertAffine(twice, "twice");
            TestGeometry.AssertWatertight(twice);

            Vector3[] v = twice.vertices, n = twice.normals;
            List<Vector4> uv = UVs(twice);
            int firstCap = 0, secondCap = 0;
            Vector3 secondNormal = -second[0].normal; // the cap faces out of the kept piece
            for (int i = 0; i < v.Length; i++)
            {
                int w = Mathf.RoundToInt(uv[i].w);
                if (Vector3.Dot(n[i], Vector3.left) > 0.999f) { firstCap++; Assert.AreEqual(Code + 64, w, "the first cap stays flagged once"); }
                if (Vector3.Dot(n[i], secondNormal) > 0.999f) { secondCap++; Assert.AreEqual(Code + 64, w, "the second cap is flagged once (not +128)"); }
            }
            Assert.Greater(firstCap, 0);
            Assert.Greater(secondCap, 0);

            // Outside pieces of a re-cut keep the same invariants.
            foreach (Mesh piece in MeshClipper.ClipOutside(once, Matrix4x4.identity, second))
            {
                Track(piece);
                AssertCodes(piece, "twice outside");
                AssertAffine(piece, "twice outside");
            }
        }

        [Test]
        public void Clipper_RangePath_CarriesUV0()
        {
            // The element path (merged meshes) loads UV0 from the CPU arrays.
            Mesh box = PatternBox(new Vector3(0f, 0f, 0f), Vector3.one * 2f);
            Vector3[] p = box.vertices, n = box.normals;
            int[] t = box.triangles;
            Vector4[] u = UVs(box).ToArray();
            int planeCount = MeshClipper.PrepareLocalPlanes(Matrix4x4.identity, new[] { new Plane(new Vector3(1f, 1f, 0f).normalized, 0.2f) });
            var sink = new CollectSink();
            MeshClipper.Classification r = MeshClipper.ClipOutsideRange(p, n, null, t, 0, p.Length, 0, t.Length, planeCount, sink, u);
            Assert.AreEqual(MeshClipper.Classification.Straddling, r);
            Assert.Greater(sink.Pieces.Count, 0);
            foreach (KeyValuePair<List<Vector3>, List<Vector4>> piece in sink.Pieces)
            {
                Assert.IsNotNull(piece.Value, "UV0 handed to the sink");
                Assert.AreEqual(piece.Key.Count, piece.Value.Count);
                for (int i = 0; i < piece.Key.Count; i++)
                {
                    Assert.Less((Pattern(piece.Key[i]) - (Vector3)piece.Value[i]).magnitude, Tol * 10f);
                    int w = Mathf.RoundToInt(piece.Value[i].w);
                    Assert.IsTrue(w == Code || w == Code + 64);
                }
            }
        }

        sealed class CollectSink : MeshClipper.IPieceSink
        {
            public readonly List<KeyValuePair<List<Vector3>, List<Vector4>>> Pieces = new List<KeyValuePair<List<Vector3>, List<Vector4>>>();

            public void AddPiece(List<Vector3> positions, List<Vector3> normals, List<Color32> colors, List<Vector4> uvs, List<int> indices)
            {
                Pieces.Add(new KeyValuePair<List<Vector3>, List<Vector4>>(new List<Vector3>(positions), uvs != null ? new List<Vector4>(uvs) : null));
            }
        }

        [Test]
        public void MissingUV0_StaysAbsent()
        {
            Mesh box = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            var planes = new[] { new Plane(new Vector3(1f, 0.5f, 0.2f).normalized, 0.1f) };
            Mesh inside = Track(MeshClipper.ClipInside(box, Matrix4x4.identity, planes));
            Assert.IsNotNull(inside);
            Assert.IsFalse(inside.HasVertexAttribute(VertexAttribute.TexCoord0), "no UV0 in, no UV0 out");
            foreach (Mesh piece in MeshClipper.ClipOutside(box, Matrix4x4.identity, planes))
            {
                Track(piece);
                Assert.IsFalse(piece.HasVertexAttribute(VertexAttribute.TexCoord0));
            }

            var merger = new PieceMerger();
            merger.AddTransformed(box.vertices, box.normals, null, box.triangles, Matrix4x4.Translate(Vector3.one));
            Mesh merged = Track(merger.Build("NoUV", out _, out _, out _, out Vector4[] uvs, out _));
            Assert.IsNull(uvs);
            Assert.IsFalse(merged.HasVertexAttribute(VertexAttribute.TexCoord0));
        }

        // ------------------------------------------------------------------ merger

        [Test]
        public void Merger_KeepsUV0()
        {
            Mesh a = PatternBox(Vector3.zero, Vector3.one);
            Mesh b = Track(TestGeometry.Box(new Vector3(3f, 0f, 0f), Vector3.one)); // no UV0
            Vector4[] ua = UVs(a).ToArray();
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(10f, 2f, -4f), Quaternion.Euler(0f, 90f, 0f), Vector3.one * 2f);

            var merger = new PieceMerger();
            merger.AddTransformed(a.vertices, a.normals, null, ua, a.triangles, m);
            merger.AddTransformed(b.vertices, b.normals, null, null, b.triangles, m);
            Mesh merged = Track(merger.Build("Merged", out Vector3[] p, out _, out _, out Vector4[] uvs, out _));
            Assert.IsNotNull(uvs);
            Assert.AreEqual(p.Length, uvs.Length);
            for (int i = 0; i < ua.Length; i++)
            {
                Assert.AreEqual(ua[i], uvs[i], "UV0 is copied unchanged (never transformed)");
                Assert.Less((p[i] - m.MultiplyPoint3x4(a.vertices[i])).magnitude, 1e-4f, "positions are transformed");
            }
            for (int i = ua.Length; i < uvs.Length; i++) Assert.AreEqual(Vector4.zero, uvs[i], "a piece without UV0 reads None");
            List<Vector4> meshUv = UVs(merged);
            Assert.AreEqual(uvs.Length, meshUv.Count);

            // A piece without UV0 first, then one with: the earlier vertices are back-filled with zero.
            merger.Reset();
            merger.AddTransformed(b.vertices, b.normals, null, null, b.triangles, Matrix4x4.identity);
            merger.AddTransformed(a.vertices, a.normals, null, ua, a.triangles, Matrix4x4.identity);
            Track(merger.Build("Merged2", out _, out _, out _, out Vector4[] uvs2, out _));
            for (int i = 0; i < b.vertexCount; i++) Assert.AreEqual(Vector4.zero, uvs2[i]);
            for (int i = 0; i < ua.Length; i++) Assert.AreEqual(ua[i], uvs2[b.vertexCount + i]);

            // Shared path (a kept element) + an appended cut piece.
            merger.Reset();
            merger.BeginShared(a.vertices, a.normals, null, ua);
            merger.AddExistingElement(a.triangles, 0, a.vertexCount, 0, a.triangles.Length, a.bounds);
            merger.AddPiece(new List<Vector3>(b.vertices), new List<Vector3>(b.normals), null,
                            new List<Vector4>(new Vector4[b.vertexCount]), new List<int>(b.triangles), b.bounds);
            Track(merger.Build("Shared", out Vector3[] sp, out _, out _, out Vector4[] su, out _));
            Assert.AreEqual(sp.Length, su.Length);
            for (int i = 0; i < ua.Length; i++) Assert.AreEqual(ua[i], su[i]);
        }

        [Test]
        public void MeshElements_ReadsUV0Lazily()
        {
            Mesh a = PatternBox(Vector3.zero, Vector3.one);
            var go = new GameObject("ElementsUV");
            _cleanup.Add(go);
            var el = go.AddComponent<MeshElements>();
            el.Set(a, new[] { 0 }, new[] { a.vertexCount }, new[] { 0 }, new[] { a.triangles.Length }, new[] { a.bounds });
            el.SetData(a.vertices, a.normals, null, a.triangles); // no UV0 given: read from the mesh
            Assert.IsTrue(el.EnsureData());
            Assert.IsNotNull(el.Uvs);
            Assert.AreEqual(a.vertexCount, el.Uvs.Length);
            Assert.AreEqual(UVs(a)[3], el.Uvs[3]);
        }
    }
}
