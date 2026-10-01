using System.Collections.Generic;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;

namespace Ion.Tests
{
    public class MeshClipperTests
    {
        const float VolumeTolerance = 1e-3f;

        readonly List<Object> _cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup) if (o != null) Object.DestroyImmediate(o);
            _cleanup.Clear();
        }

        Mesh Track(Mesh m) { if (m != null) _cleanup.Add(m); return m; }
        List<Mesh> Track(List<Mesh> list) { foreach (var m in list) Track(m); return list; }

        static PhotoFrustum NarrowFrustum(float near = 4f, float far = 10f)
        {
            // Apex at z = -5 looking +z; the cube [-1,1]^3 spans distances 4..6 from the apex.
            return new PhotoFrustum
            {
                Pose = new Pose(new Vector3(0f, 0f, -5f), Quaternion.identity),
                FovY = 15f,
                Aspect = 1f,
                Near = near,
                Far = far,
            };
        }

        static float TruncatedPyramidVolume(PhotoFrustum f, float z1, float z2)
        {
            // Cross-section at distance z is (2 z tanX) x (2 z tanY).
            float tanY = Mathf.Tan(0.5f * f.FovY * Mathf.Deg2Rad);
            float tanX = tanY * f.Aspect;
            return 4f * tanX * tanY * (z2 * z2 * z2 - z1 * z1 * z1) / 3f;
        }

        // ------------------------------------------------------------------ sanity of the helpers

        [Test]
        public void TestMeshes_AreClosedWithExpectedVolume()
        {
            Mesh box = Track(TestGeometry.Box(Vector3.zero, new Vector3(2f, 2f, 2f)));
            Assert.AreEqual(8f, TestGeometry.SignedVolume(box), VolumeTolerance);
            TestGeometry.AssertWatertight(box);

            Mesh wedge = Track(TestGeometry.Wedge(new Vector3(1f, 2f, 3f), new Vector3(2f, 1f, 4f)));
            Assert.AreEqual(4f, TestGeometry.SignedVolume(wedge), VolumeTolerance);
            TestGeometry.AssertWatertight(wedge);
        }

        // ------------------------------------------------------------------ single plane

        [Test]
        public void CubeVsSinglePlane_InsideHalf()
        {
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            var planes = new[] { new Plane(Vector3.right, 0f) }; // keep x >= 0

            Mesh inside = Track(MeshClipper.ClipInside(cube, Matrix4x4.identity, planes));

            Assert.IsNotNull(inside);
            Assert.AreEqual(4f, TestGeometry.SignedVolume(inside), VolumeTolerance);
            TestGeometry.AssertWatertight(inside);
            TestGeometry.AssertInsideAll(inside, Matrix4x4.identity, planes);
            Assert.AreEqual(0f, inside.bounds.min.x, 1e-4f);
            Assert.AreEqual(1f, inside.bounds.max.x, 1e-4f);
        }

        [Test]
        public void CubeVsSinglePlane_CapNormalFacesOutOfKeptPiece()
        {
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            var planes = new[] { new Plane(Vector3.right, 0f) };

            Mesh inside = Track(MeshClipper.ClipInside(cube, Matrix4x4.identity, planes));

            Vector3[] v = inside.vertices;
            Vector3[] n = inside.normals;
            int[] t = inside.GetTriangles(0);
            int capTriangles = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (Mathf.Abs(a.x) > 1e-5f || Mathf.Abs(b.x) > 1e-5f || Mathf.Abs(c.x) > 1e-5f) continue;
                capTriangles++;
                Vector3 face = Vector3.Cross(b - a, c - a).normalized;
                Assert.Greater(Vector3.Dot(face, Vector3.left), 0.999f, "cap winding must face -x");
                Assert.Greater(Vector3.Dot(n[t[i]], Vector3.left), 0.999f, "cap vertex normal must be -x");
            }
            Assert.Greater(capTriangles, 0, "expected a cap on x = 0");
        }

        [Test]
        public void CubeVsSinglePlane_OutsideHalf()
        {
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            var planes = new[] { new Plane(Vector3.right, -0.5f) }; // inside: x >= 0.5

            List<Mesh> outside = Track(MeshClipper.ClipOutside(cube, Matrix4x4.identity, planes));

            Assert.AreEqual(1, outside.Count);
            Assert.AreEqual(1.5f * 4f, TestGeometry.SignedVolume(outside[0]), VolumeTolerance);
            TestGeometry.AssertWatertight(outside[0]);
            Assert.AreEqual(0.5f, outside[0].bounds.max.x, 1e-4f);
        }

        [Test]
        public void ObliquePlane_ConservesVolume()
        {
            Mesh cube = Track(TestGeometry.Box(new Vector3(0.3f, -0.2f, 0.1f), new Vector3(2f, 1.5f, 3f)));
            Vector3 normal = new Vector3(0.4f, 0.7f, -0.3f).normalized;
            var planes = new[] { new Plane(normal, new Vector3(0.1f, 0.05f, 0.2f)) };

            Mesh inside = Track(MeshClipper.ClipInside(cube, Matrix4x4.identity, planes));
            List<Mesh> outside = Track(MeshClipper.ClipOutside(cube, Matrix4x4.identity, planes));

            Assert.IsNotNull(inside);
            Assert.AreEqual(1, outside.Count);
            TestGeometry.AssertWatertight(inside);
            TestGeometry.AssertWatertight(outside[0]);
            float total = TestGeometry.SignedVolume(inside) + TestGeometry.SignedVolume(outside);
            Assert.AreEqual(2f * 1.5f * 3f, total, VolumeTolerance);
        }

        [Test]
        public void PlaneThroughVertices_IsHandled()
        {
            // The diagonal plane x = y passes exactly through 4 cube vertices.
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            var planes = new[] { new Plane(new Vector3(1f, -1f, 0f).normalized, 0f) };

            Mesh inside = Track(MeshClipper.ClipInside(cube, Matrix4x4.identity, planes));
            List<Mesh> outside = Track(MeshClipper.ClipOutside(cube, Matrix4x4.identity, planes));

            Assert.IsNotNull(inside);
            Assert.AreEqual(1, outside.Count);
            Assert.AreEqual(4f, TestGeometry.SignedVolume(inside), VolumeTolerance);
            Assert.AreEqual(4f, TestGeometry.SignedVolume(outside[0]), VolumeTolerance);
            TestGeometry.AssertWatertight(inside);
            TestGeometry.AssertWatertight(outside[0]);
        }

        [Test]
        public void PlaneOnFace_DoesNotCutOrCap()
        {
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            var planes = new[] { new Plane(Vector3.right, 1f) }; // x >= -1: the -x face lies on the plane

            Mesh inside = Track(MeshClipper.ClipInside(cube, Matrix4x4.identity, planes));
            List<Mesh> outside = Track(MeshClipper.ClipOutside(cube, Matrix4x4.identity, planes));

            Assert.IsNotNull(inside);
            Assert.AreEqual(8f, TestGeometry.SignedVolume(inside), VolumeTolerance);
            Assert.AreEqual(cube.triangles.Length, inside.triangles.Length, "no cap must be added");
            Assert.AreEqual(0, outside.Count);
        }

        // ------------------------------------------------------------------ frustum

        [Test]
        public void CubeVsFrustum_InsideMatchesAnalyticVolume()
        {
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            PhotoFrustum f = NarrowFrustum();
            Plane[] planes = f.Planes();

            Mesh inside = Track(MeshClipper.ClipInside(cube, Matrix4x4.identity, planes));

            Assert.IsNotNull(inside);
            TestGeometry.AssertWatertight(inside);
            TestGeometry.AssertInsideAll(inside, Matrix4x4.identity, planes);
            Assert.AreEqual(TruncatedPyramidVolume(f, 4f, 6f), TestGeometry.SignedVolume(inside), VolumeTolerance);
        }

        [Test]
        public void CubeVsFrustum_OutsidePiecesAreClosedAndConserveVolume()
        {
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            PhotoFrustum f = NarrowFrustum();
            Plane[] planes = f.Planes();

            Mesh inside = Track(MeshClipper.ClipInside(cube, Matrix4x4.identity, planes));
            List<Mesh> outside = Track(MeshClipper.ClipOutside(cube, Matrix4x4.identity, planes));

            // The frustum tunnels straight through the cube: the four side planes each cut off a piece;
            // near/far lie beyond the cube.
            Assert.AreEqual(4, outside.Count);
            foreach (var piece in outside)
            {
                TestGeometry.AssertWatertight(piece);
                Assert.Greater(TestGeometry.SignedVolume(piece), 0f);
            }
            float total = TestGeometry.SignedVolume(inside) + TestGeometry.SignedVolume(outside);
            Assert.AreEqual(8f, total, VolumeTolerance);
        }

        [Test]
        public void CubeVsFrustum_NearAndFarCutsConserveVolume()
        {
            // near/far now slice through the cube too (distances 4.5 and 5.5 from the apex).
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            PhotoFrustum f = NarrowFrustum(4.5f, 5.5f);
            Plane[] planes = f.Planes();

            Mesh inside = Track(MeshClipper.ClipInside(cube, Matrix4x4.identity, planes));
            List<Mesh> outside = Track(MeshClipper.ClipOutside(cube, Matrix4x4.identity, planes));

            Assert.AreEqual(TruncatedPyramidVolume(f, 4.5f, 5.5f), TestGeometry.SignedVolume(inside), VolumeTolerance);
            Assert.AreEqual(6, outside.Count);
            TestGeometry.AssertWatertight(inside);
            foreach (var piece in outside) TestGeometry.AssertWatertight(piece);
            Assert.AreEqual(8f, TestGeometry.SignedVolume(inside) + TestGeometry.SignedVolume(outside), VolumeTolerance);
        }

        [Test]
        public void RotatedFrustumThroughWedge_ConservesVolume()
        {
            Mesh wedge = Track(TestGeometry.Wedge(Vector3.zero, new Vector3(4f, 2f, 6f)));
            var f = new PhotoFrustum
            {
                Pose = new Pose(new Vector3(-6f, 3f, -1f), Quaternion.LookRotation(new Vector3(1f, -0.45f, 0.2f)) * Quaternion.AngleAxis(25f, Vector3.forward)),
                FovY = 25f,
                Aspect = 1.5f,
                Near = 0.6f,
                Far = 250f,
            };
            Plane[] planes = f.Planes();

            Mesh inside = Track(MeshClipper.ClipInside(wedge, Matrix4x4.identity, planes));
            List<Mesh> outside = Track(MeshClipper.ClipOutside(wedge, Matrix4x4.identity, planes));

            Assert.IsNotNull(inside, "test setup: the frustum should hit the wedge");
            Assert.Greater(outside.Count, 0, "test setup: the frustum should not swallow the wedge");
            TestGeometry.AssertWatertight(inside);
            foreach (var piece in outside) TestGeometry.AssertWatertight(piece);
            float total = TestGeometry.SignedVolume(inside) + TestGeometry.SignedVolume(outside);
            Assert.AreEqual(0.5f * 4f * 2f * 6f, total, VolumeTolerance);
        }

        // ------------------------------------------------------------------ transforms

        [Test]
        public void NonUniformTransform_WorldPlanesAreAppliedInLocalSpace()
        {
            // Unit cube scaled (2,1,3) and turned 90° about Y: world extents x:3, y:1, z:2 around (5,0,0).
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one));
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(5f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector3(2f, 1f, 3f));
            var planes = new[] { new Plane(Vector3.right, new Vector3(5.5f, 0f, 0f)) }; // keep world x >= 5.5

            Mesh inside = Track(MeshClipper.ClipInside(cube, m, planes));

            Assert.IsNotNull(inside);
            float worldVolume = TestGeometry.SignedVolume(inside) * 6f; // |det| = 2*1*3
            Assert.AreEqual(1f * 1f * 2f, worldVolume, VolumeTolerance);
            TestGeometry.AssertWatertight(inside);
            TestGeometry.AssertInsideAll(inside, m, planes);

            // Cap normals, rendered through the inverse-transpose, must face world -x.
            Matrix4x4 normalMatrix = m.inverse.transpose;
            Vector3[] v = inside.vertices, n = inside.normals;
            bool foundCap = false;
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.Abs(m.MultiplyPoint3x4(v[i]).x - 5.5f) > 1e-4f) continue;
                Vector3 worldN = normalMatrix.MultiplyVector(n[i]).normalized;
                if (Vector3.Dot(worldN, Vector3.left) > 0.999f) foundCap = true;
            }
            Assert.IsTrue(foundCap, "expected cap vertices whose world normal is -x");
        }

        [Test]
        public void ClipInside_ResultIsInLocalSpaceOfInput()
        {
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            Matrix4x4 m = Matrix4x4.Translate(new Vector3(100f, 0f, 0f));
            var planes = new[] { new Plane(Vector3.right, new Vector3(100f, 0f, 0f)) }; // world x >= 100

            Mesh inside = Track(MeshClipper.ClipInside(cube, m, planes));

            Assert.AreEqual(0f, inside.bounds.min.x, 1e-4f);
            Assert.AreEqual(1f, inside.bounds.max.x, 1e-4f);
        }

        // ------------------------------------------------------------------ fast paths / empties

        [Test]
        public void FullyInside_ReturnsWholeCopy_AndNoOutsidePieces()
        {
            Mesh cube = Track(TestGeometry.Box(new Vector3(0f, 0f, 20f), Vector3.one * 0.5f));
            Plane[] planes = NarrowFrustum(0.6f, 250f).Planes();

            Mesh inside = Track(MeshClipper.ClipInside(cube, Matrix4x4.identity, planes));
            List<Mesh> outside = Track(MeshClipper.ClipOutside(cube, Matrix4x4.identity, planes));

            Assert.IsNotNull(inside);
            Assert.AreNotSame(cube, inside, "must return a copy, never the input");
            Assert.AreEqual(TestGeometry.SignedVolume(cube), TestGeometry.SignedVolume(inside), VolumeTolerance);
            Assert.AreEqual(0, outside.Count);

            var buffer = new List<Mesh>();
            Assert.AreEqual(MeshClipper.Classification.Inside, MeshClipper.ClipOutside(cube, Matrix4x4.identity, planes, buffer));
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void FullyOutside_ReturnsNull_AndOneWholePiece()
        {
            Mesh cube = Track(TestGeometry.Box(new Vector3(30f, 0f, 0f), Vector3.one * 2f));
            Plane[] planes = NarrowFrustum().Planes();

            Mesh inside = MeshClipper.ClipInside(cube, Matrix4x4.identity, planes);
            List<Mesh> outside = Track(MeshClipper.ClipOutside(cube, Matrix4x4.identity, planes));

            Assert.IsNull(inside);
            Assert.AreEqual(1, outside.Count);
            Assert.AreNotSame(cube, outside[0]);
            Assert.AreEqual(8f, TestGeometry.SignedVolume(outside[0]), VolumeTolerance);

            var buffer = new List<Mesh>();
            Assert.AreEqual(MeshClipper.Classification.Outside, MeshClipper.ClipOutside(cube, Matrix4x4.identity, planes, buffer));
            Assert.AreEqual(0, buffer.Count, "the buffer overload must not copy untouched meshes");
        }

        [Test]
        public void BoundsStraddleButVerticesOutside_ReturnsNull()
        {
            // A thin wedge whose bounding box crosses the plane x >= 0 while its triangles do not:
            // only the (x >= 0) corner region of its bounds is empty space.
            var a = new Vector3(-2f, 0f, -1f);
            var b = new Vector3(1f, 0f, -1f);
            var c = new Vector3(-2f, 0f, 2f);
            var up = new Vector3(0f, 0.5f, 0f);
            Mesh prism = Track(TestGeometry.Convex("Prism", new[]
            {
                new[] { a, b, c },
                new[] { a + up, b + up, c + up },
                new[] { a, b, b + up, a + up },
                new[] { b, c, c + up, b + up },
                new[] { c, a, a + up, c + up },
            }));
            var planes = new[] { new Plane(new Vector3(1f, 0f, 1f).normalized, -0.5f) }; // x + z >= 0.707

            Mesh inside = MeshClipper.ClipInside(prism, Matrix4x4.identity, planes);
            var buffer = new List<Mesh>();
            MeshClipper.Classification cls = MeshClipper.ClipOutside(prism, Matrix4x4.identity, planes, buffer);

            Assert.IsNull(inside);
            Assert.AreEqual(MeshClipper.Classification.Outside, cls);
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void NullAndEmptyInputs_AreHandled()
        {
            Assert.IsNull(MeshClipper.ClipInside(null, Matrix4x4.identity, new[] { new Plane(Vector3.up, 0f) }));
            Assert.AreEqual(0, MeshClipper.ClipOutside(null, Matrix4x4.identity, new[] { new Plane(Vector3.up, 0f) }).Count);

            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one));
            Mesh copy = Track(MeshClipper.ClipInside(cube, Matrix4x4.identity, new Plane[0]));
            Assert.IsNotNull(copy);
            Assert.AreEqual(0, MeshClipper.ClipOutside(cube, Matrix4x4.identity, new Plane[0]).Count);
        }

        [Test]
        public void ClippedPieceCanBeClippedAgain()
        {
            // Pieces produced by a placement are Sliceables and get cut by later placements.
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            List<Mesh> first = Track(MeshClipper.ClipOutside(cube, Matrix4x4.identity, NarrowFrustum().Planes()));

            var second = new PhotoFrustum
            {
                Pose = new Pose(new Vector3(-5f, 0.3f, 0.2f), Quaternion.LookRotation(Vector3.right)),
                FovY = 20f,
                Aspect = 1.3f,
                Near = 0.6f,
                Far = 250f,
            };
            Plane[] planes2 = second.Planes();
            float before = TestGeometry.SignedVolume(first);
            float after = 0f;
            foreach (var piece in first)
            {
                Mesh inside = Track(MeshClipper.ClipInside(piece, Matrix4x4.identity, planes2));
                List<Mesh> outside = Track(MeshClipper.ClipOutside(piece, Matrix4x4.identity, planes2));
                if (inside != null) { TestGeometry.AssertWatertight(inside); after += TestGeometry.SignedVolume(inside); }
                foreach (var o in outside) { TestGeometry.AssertWatertight(o); after += TestGeometry.SignedVolume(o); }
            }
            Assert.AreEqual(before, after, VolumeTolerance);
        }

        [Test]
        public void RandomFrusta_ConserveVolume_AndStayWatertight()
        {
            var rng = new System.Random(20260930);
            System.Func<float, float, float> R = (min, max) => min + (float)rng.NextDouble() * (max - min);
            int straddling = 0;
            for (int iter = 0; iter < 200; iter++)
            {
                Vector3 size = new Vector3(R(0.5f, 6f), R(0.5f, 6f), R(0.5f, 6f));
                Mesh mesh = Track(iter % 2 == 0 ? TestGeometry.Box(Vector3.zero, size) : TestGeometry.Wedge(Vector3.zero, size));
                Matrix4x4 m = Matrix4x4.TRS(
                    new Vector3(R(-2f, 2f), R(-2f, 2f), R(-2f, 2f)),
                    Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f)),
                    new Vector3(R(0.5f, 2f), R(0.5f, 2f), R(0.5f, 2f)));
                float det = Mathf.Abs(m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20) + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20));

                Vector3 apex = new Vector3(R(-8f, 8f), R(-8f, 8f), R(-8f, 8f));
                Vector3 target = new Vector3(R(-1.5f, 1.5f), R(-1.5f, 1.5f), R(-1.5f, 1.5f));
                var f = new PhotoFrustum
                {
                    Pose = new Pose(apex, Quaternion.LookRotation(target - apex) * Quaternion.AngleAxis(R(0f, 360f), Vector3.forward)),
                    FovY = R(10f, 70f),
                    Aspect = R(0.6f, 2f),
                    Near = R(0.3f, 6f),
                    Far = R(6f, 14f),
                };
                Plane[] planes = f.Planes();

                Mesh inside = Track(MeshClipper.ClipInside(mesh, m, planes));
                List<Mesh> outside = Track(MeshClipper.ClipOutside(mesh, m, planes));

                float total = 0f;
                if (inside != null)
                {
                    TestGeometry.AssertWatertight(inside);
                    TestGeometry.AssertInsideAll(inside, m, planes);
                    total += TestGeometry.SignedVolume(inside);
                }
                foreach (var piece in outside)
                {
                    TestGeometry.AssertWatertight(piece);
                    total += TestGeometry.SignedVolume(piece);
                }
                if (inside != null && outside.Count > 0) straddling++;
                float expected = TestGeometry.SignedVolume(mesh);
                Assert.AreEqual(expected, total, 1e-4f * Mathf.Max(1f, expected) * Mathf.Max(1f, 1f / det) + 1e-3f, $"iteration {iter}");
            }
            Assert.Greater(straddling, 60, "test setup: most cases should actually cut");
        }

        [Test]
        public void MultipleSubmeshes_ArePreserved_CapGoesToFirst()
        {
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one * 2f));
            // Move the +y face (last 6 indices of the 4th face) into submesh 1.
            int[] all = cube.triangles;
            Vector3[] cv = cube.vertices;
            var sub0 = new List<int>();
            var sub1 = new List<int>();
            for (int i = 0; i < all.Length; i += 3)
            {
                bool top = cv[all[i]].y > 0.99f && cv[all[i + 1]].y > 0.99f && cv[all[i + 2]].y > 0.99f;
                var target = top ? sub1 : sub0;
                target.Add(all[i]); target.Add(all[i + 1]); target.Add(all[i + 2]);
            }
            cube.subMeshCount = 2;
            cube.SetTriangles(sub0, 0);
            cube.SetTriangles(sub1, 1);

            Mesh inside = Track(MeshClipper.ClipInside(cube, Matrix4x4.identity, new[] { new Plane(Vector3.right, 0f) }));

            Assert.AreEqual(2, inside.subMeshCount);
            Vector3[] v = inside.vertices;
            int[] top1 = inside.GetTriangles(1);
            float topArea = 0f;
            for (int i = 0; i < top1.Length; i += 3)
            {
                Assert.Greater(v[top1[i]].y, 0.99f);
                Assert.Greater(v[top1[i + 1]].y, 0.99f);
                Assert.Greater(v[top1[i + 2]].y, 0.99f);
                topArea += 0.5f * Vector3.Cross(v[top1[i + 1]] - v[top1[i]], v[top1[i + 2]] - v[top1[i]]).magnitude;
            }
            Assert.AreEqual(2f, topArea, 1e-4f, "submesh 1 keeps exactly the clipped half of the top face");
            // The cap (on x = 0) goes into submesh 0.
            bool capInFirst = false;
            int[] t0 = inside.GetTriangles(0);
            for (int i = 0; i < t0.Length; i += 3)
                if (Mathf.Abs(v[t0[i]].x) < 1e-5f && Mathf.Abs(v[t0[i + 1]].x) < 1e-5f && Mathf.Abs(v[t0[i + 2]].x) < 1e-5f) capInFirst = true;
            Assert.IsTrue(capInFirst);
            Assert.AreEqual(4f, TestGeometry.SignedVolume(inside), VolumeTolerance);
            TestGeometry.AssertWatertight(inside);
        }

        // ------------------------------------------------------------------ exact coincidences

        [Test]
        public void RecutWithIdenticalFrustum_LeavesPiecesUntouched()
        {
            // Placing a second photo from exactly the same pose re-cuts the first placement's pieces, whose
            // cap faces lie on the cutting planes. Nothing may change: outside pieces stay whole, the inside
            // stays whole, and no slivers appear (here far from the origin, rotated and non-uniformly scaled).
            Mesh cube = Track(TestGeometry.Box(Vector3.zero, Vector3.one));
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(150f, 3f, 12f), Quaternion.Euler(0f, 23f, 0f), new Vector3(20f, 16f, 1.5f));
            var f = new PhotoFrustum
            {
                Pose = new Pose(new Vector3(149f, 4.6f, 2f), Quaternion.Euler(3f, 4f, 0f) * Quaternion.AngleAxis(90f, Vector3.forward)),
                FovY = 50f,
                Aspect = 4f / 3f,
                Near = 0.6f,
                Far = 250f,
            };
            Plane[] planes = f.Planes();

            Mesh inside = Track(MeshClipper.ClipInside(cube, m, planes));
            List<Mesh> outside = Track(MeshClipper.ClipOutside(cube, m, planes));
            Assert.IsNotNull(inside, "test setup: the frustum should hit the box");
            Assert.AreEqual(4, outside.Count, "test setup: the frustum should cut the box");

            var buffer = new List<Mesh>();
            foreach (Mesh piece in outside)
            {
                Assert.IsNull(Track(MeshClipper.ClipInside(piece, m, planes)), "an outside piece has nothing inside");
                buffer.Clear();
                Assert.AreEqual(MeshClipper.Classification.Outside, MeshClipper.ClipOutside(piece, m, planes, buffer));
                Assert.AreEqual(0, buffer.Count);
            }
            buffer.Clear();
            Assert.AreEqual(MeshClipper.Classification.Inside, MeshClipper.ClipOutside(inside, m, planes, Track(buffer)));
            Mesh again = Track(MeshClipper.ClipInside(inside, m, planes));
            Assert.AreEqual(TestGeometry.SignedVolume(inside), TestGeometry.SignedVolume(again), 1e-5f);
        }

        [Test]
        public void FrustumPlanesThroughBoxCornersAndFaces_ConserveVolume()
        {
            // 90° x 90° frustum: its side planes are x = ±z and y = ±z, so they pass exactly through box
            // corners and edges, and near/far coincide with box faces.
            var f = new PhotoFrustum { Pose = new Pose(Vector3.zero, Quaternion.identity), FovY = 90f, Aspect = 1f, Near = 1f, Far = 4f };
            Plane[] planes = f.Planes();
            var meshes = new[]
            {
                TestGeometry.Box(new Vector3(2f, 0f, 3f), new Vector3(2f, 2f, 2f)),
                TestGeometry.Box(new Vector3(1f, 1f, 2f), new Vector3(2f, 2f, 2f)),
                TestGeometry.Box(new Vector3(0f, 0f, 2.5f), new Vector3(5f, 5f, 3f)),
                TestGeometry.Box(new Vector3(0f, 0f, 2.5f), new Vector3(2f, 2f, 3f)),
                TestGeometry.Wedge(new Vector3(0f, -1f, 2f), new Vector3(2f, 2f, 2f)),
            };
            foreach (Mesh mesh in meshes)
            {
                Track(mesh);
                Mesh inside = Track(MeshClipper.ClipInside(mesh, Matrix4x4.identity, planes));
                List<Mesh> outside = Track(MeshClipper.ClipOutside(mesh, Matrix4x4.identity, planes));
                float total = 0f;
                if (inside != null)
                {
                    TestGeometry.AssertWatertight(inside);
                    TestGeometry.AssertInsideAll(inside, Matrix4x4.identity, planes);
                    total += TestGeometry.SignedVolume(inside);
                }
                foreach (Mesh piece in outside)
                {
                    TestGeometry.AssertWatertight(piece);
                    Assert.Greater(TestGeometry.SignedVolume(piece), 0f);
                    total += TestGeometry.SignedVolume(piece);
                }
                Assert.AreEqual(TestGeometry.SignedVolume(mesh), total, 1e-3f, mesh.name);
            }
        }
    }
}
