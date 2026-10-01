using System.Collections.Generic;
using Ion.DebugTools;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;

namespace Ion.Tests
{
    /// <summary>Debug-flag URL parsing and the element-wise cut of merged decor meshes.</summary>
    public class PolishTests
    {
        [TestCase("https://vasiniks.github.io/project.ion/?debug=1", true)]
        [TestCase("http://127.0.0.1:8765/?dpr=2&debug=1", true)]
        [TestCase("http://127.0.0.1:8765/?debug=true#x", true)]
        [TestCase("http://127.0.0.1:8765/?debug=0", false)]
        [TestCase("http://127.0.0.1:8765/?nodebug=1", false)]
        [TestCase("http://127.0.0.1:8765/#debug=1", false)]
        [TestCase("http://127.0.0.1:8765/", false)]
        [TestCase("", false)]
        public void DebugFlag_IsReadFromTheQueryString(string url, bool expected)
        {
            Assert.AreEqual(expected, IonDebug.UrlHasDebugFlag(url));
        }

        [Test]
        public void MergedMesh_ElementsOutsideTheFrustumSurviveWhole()
        {
            var origin = new Vector3(-5000f, 0f, 5000f);
            var cleanup = new List<Object>();
            var system = new GameObject("PolishProjection").AddComponent<ProjectionSystem>();
            cleanup.Add(system.gameObject);
            var cam = new GameObject("PolishViewer");
            cam.transform.SetPositionAndRotation(origin, Quaternion.identity);
            var viewer = cam.AddComponent<Camera>();
            cleanup.Add(cam);
            try
            {
                // Three 1 m cubes merged into one mesh: one dead ahead (inside), one straddling the frustum
                // edge, one far off to the side (untouched).
                Mesh a = TestGeometry.Box(new Vector3(0f, 0f, 10f), Vector3.one * 0.5f);
                Mesh b = TestGeometry.Box(new Vector3(2.7f, 0f, 10f), Vector3.one);
                Mesh c = TestGeometry.Box(new Vector3(20f, 0f, 10f), Vector3.one);
                var parts = new[] { a, b, c };
                var p = new List<Vector3>();
                var t = new List<int>();
                var v0 = new int[3]; var vn = new int[3]; var t0 = new int[3]; var tn = new int[3];
                var bounds = new Bounds[3];
                for (int i = 0; i < 3; i++)
                {
                    v0[i] = p.Count; t0[i] = t.Count;
                    int baseV = p.Count;
                    p.AddRange(parts[i].vertices);
                    foreach (int idx in parts[i].triangles) t.Add(idx + baseV);
                    vn[i] = p.Count - v0[i]; tn[i] = t.Count - t0[i];
                    bounds[i] = parts[i].bounds;
                    cleanup.Add(parts[i]);
                }
                var merged = new Mesh { name = "Merged" };
                merged.SetVertices(p);
                merged.SetTriangles(t, 0);
                merged.RecalculateNormals();
                merged.RecalculateBounds();
                cleanup.Add(merged);

                var go = new GameObject("Decor merged");
                go.transform.position = origin;
                go.AddComponent<MeshFilter>().sharedMesh = merged;
                go.AddComponent<MeshRenderer>();
                go.AddComponent<MeshCollider>().sharedMesh = merged;
                go.AddComponent<Sliceable>();
                go.AddComponent<MeshElements>().Set(merged, v0, vn, t0, tn, bounds);
                cleanup.Add(go);

                system.Place(new PhotoData { FovY = 30f, Aspect = 1f, Label = "empty" }, viewer, 0f);

                Assert.IsFalse(go.GetComponent<MeshRenderer>().enabled, "the merged original is hidden");
                var pieces = new List<GameObject>();
                foreach (var s in Object.FindObjectsByType<Sliceable>(FindObjectsSortMode.None))
                    if (s.gameObject != go && s.name.StartsWith("Decor merged")) pieces.Add(s.gameObject);
                // Kept elements and the clipped remains of the straddling cube are merged into one object.
                Assert.AreEqual(1, pieces.Count, "one object replaces the merged original");

                MeshElements kept = pieces[0].GetComponent<MeshElements>();
                Assert.IsNotNull(kept, "the result keeps an element table");
                Assert.AreEqual(2, kept.Count, "the far cube (whole) plus the outside part of the straddling cube");
                // The far cube is kept verbatim: same vertex range, same bounds, its full volume.
                Assert.AreEqual(v0[2], kept.VertexStart[0]);
                Assert.AreEqual(vn[2], kept.VertexCount[0]);
                Assert.AreEqual(bounds[2], kept.Bounds[0]);
                Assert.AreEqual(1f, TestGeometry.ElementVolume(kept, 0), 1e-3f);
                Assert.IsNotNull(kept.GetComponent<MeshCollider>(), "edit mode cooks every collider immediately");

                float total = TestGeometry.SignedVolume(kept.Mesh);
                Assert.Less(total, 2f - 0.05f, "part of the straddling cube was cut away");
                Assert.Greater(total, 1.05f, "the outside part of the straddling cube remains");

                system.Rewind();
                Assert.IsTrue(go.GetComponent<MeshRenderer>().enabled);
            }
            finally
            {
                while (system != null && system.CanRewind) system.Rewind();
                var placed = GameObject.Find("PlacedPhotos");
                if (placed != null) Object.DestroyImmediate(placed);
                foreach (var o in cleanup) if (o != null) Object.DestroyImmediate(o);
            }
        }
    }
}
