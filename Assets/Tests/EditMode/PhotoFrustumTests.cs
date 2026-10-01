using Ion.Projection;
using NUnit.Framework;
using UnityEngine;

namespace Ion.Tests
{
    public class PhotoFrustumTests
    {
        static PhotoFrustum Make(Vector3 pos, Quaternion rot, float fov = 60f, float aspect = 1.5f, float near = 0.6f, float far = 250f)
        {
            return new PhotoFrustum { Pose = new Pose(pos, rot), FovY = fov, Aspect = aspect, Near = near, Far = far };
        }

        [Test]
        public void Contains_AxisCases()
        {
            PhotoFrustum f = Make(Vector3.zero, Quaternion.identity, 90f, 1f, 1f, 10f); // tan(45°) = 1

            Assert.IsTrue(f.Contains(new Vector3(0f, 0f, 5f)), "center");
            Assert.IsTrue(f.Contains(new Vector3(4.9f, -4.9f, 5f)), "near the corner");
            Assert.IsFalse(f.Contains(new Vector3(5.2f, 0f, 5f)), "beyond the right side");
            Assert.IsFalse(f.Contains(new Vector3(0f, 5.2f, 5f)), "above the top");
            Assert.IsFalse(f.Contains(new Vector3(0f, 0f, 0.5f)), "before near");
            Assert.IsFalse(f.Contains(new Vector3(0f, 0f, 10.5f)), "beyond far");
            Assert.IsFalse(f.Contains(new Vector3(0f, 0f, -5f)), "behind the apex");
            Assert.IsTrue(f.Contains(new Vector3(0f, 0f, 1f)), "on near");
            Assert.IsTrue(f.Contains(new Vector3(0f, 0f, 10f)), "on far");
        }

        [Test]
        public void Contains_RespectsAspect()
        {
            PhotoFrustum f = Make(Vector3.zero, Quaternion.identity, 90f, 2f, 0.1f, 100f); // tanY = 1, tanX = 2
            Assert.IsTrue(f.Contains(new Vector3(9f, 0f, 5f)));
            Assert.IsFalse(f.Contains(new Vector3(0f, 6f, 5f)));
        }

        [Test]
        public void Contains_UsesPose()
        {
            PhotoFrustum f = Make(new Vector3(10f, 5f, -3f), Quaternion.LookRotation(Vector3.left), 40f, 1f);
            Assert.IsTrue(f.Contains(new Vector3(0f, 5f, -3f)));
            Assert.IsFalse(f.Contains(new Vector3(20f, 5f, -3f)));
        }

        [Test]
        public void Planes_AreSixUnitNormalsPointingInward()
        {
            PhotoFrustum f = Make(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 40f, 5f));
            Plane[] planes = f.Planes();
            Assert.AreEqual(6, planes.Length);
            Vector3 center = f.Pose.position + f.Pose.rotation * Vector3.forward * ((f.Near + f.Far) * 0.5f);
            foreach (var p in planes)
            {
                Assert.AreEqual(1f, p.normal.magnitude, 1e-4f);
                Assert.Greater(p.GetDistanceToPoint(center), 0f);
            }
        }

        [Test]
        public void Planes_AgreeWithContains()
        {
            PhotoFrustum f = Make(new Vector3(-2f, 1f, 4f), Quaternion.Euler(-20f, 130f, 30f), 50f, 1.33f, 0.6f, 20f);
            Plane[] planes = f.Planes();
            var rng = new System.Random(1234);
            int inside = 0;
            for (int i = 0; i < 5000; i++)
            {
                Vector3 local = new Vector3(
                    (float)(rng.NextDouble() * 40 - 20),
                    (float)(rng.NextDouble() * 40 - 20),
                    (float)(rng.NextDouble() * 25 - 2));
                Vector3 p = f.Pose.position + f.Pose.rotation * local;
                bool byPlanes = true;
                float minDist = float.MaxValue;
                foreach (var pl in planes)
                {
                    float d = pl.GetDistanceToPoint(p);
                    minDist = Mathf.Min(minDist, Mathf.Abs(d));
                    if (d < 0f) byPlanes = false;
                }
                if (minDist < 1e-3f) continue; // ignore points on the boundary
                Assert.AreEqual(byPlanes, f.Contains(p), $"point {p}");
                if (byPlanes) inside++;
            }
            Assert.Greater(inside, 50, "test setup: sample enough points inside");
        }

        [Test]
        public void Planes_MatchUnityCameraFrustumPlanes()
        {
            var go = new GameObject("FrustumTestCamera");
            try
            {
                go.transform.SetPositionAndRotation(new Vector3(3f, -1f, 7f), Quaternion.Euler(15f, -70f, 0f));
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 55f;
                cam.aspect = 1.6f;
                cam.nearClipPlane = 0.6f;
                cam.farClipPlane = 250f;

                Plane[] unity = GeometryUtility.CalculateFrustumPlanes(cam);
                Plane[] ours = PhotoFrustum.FromCamera(cam, 55f, 1.6f, 0f, 0.6f, 250f).Planes();

                for (int i = 0; i < 6; i++)
                {
                    Assert.Greater(Vector3.Dot(unity[i].normal, ours[i].normal), 0.9999f, $"plane {i} normal");
                    float tolerance = 0.02f + 1e-3f * Mathf.Abs(unity[i].distance);
                    Assert.AreEqual(unity[i].distance, ours[i].distance, tolerance, $"plane {i} distance");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FromCamera_RollRotatesAboutForward()
        {
            var go = new GameObject("RollTestCamera");
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var cam = go.AddComponent<Camera>();
                // Wide photo: tanY = tan(30°) ≈ 0.577, tanX ≈ 1.155.
                PhotoFrustum level = PhotoFrustum.FromCamera(cam, 60f, 2f, 0f, 0.6f, 250f);
                PhotoFrustum rolled = PhotoFrustum.FromCamera(cam, 60f, 2f, 90f, 0.6f, 250f);

                var wide = new Vector3(9f, 0f, 10f); // within tanX, outside tanY
                var tall = new Vector3(0f, 9f, 10f);
                Assert.IsTrue(level.Contains(wide));
                Assert.IsFalse(level.Contains(tall));
                Assert.IsFalse(rolled.Contains(wide));
                Assert.IsTrue(rolled.Contains(tall));

                Assert.AreEqual(Vector3.zero, rolled.Pose.position);
                Assert.Greater(Vector3.Dot(rolled.Pose.rotation * Vector3.forward, Vector3.forward), 0.9999f, "roll keeps forward");
                // Positive roll: the photo's up axis turns towards the viewer's left (counter-clockwise on screen).
                Assert.Greater(Vector3.Dot(rolled.Pose.rotation * Vector3.up, Vector3.left), 0.9999f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
