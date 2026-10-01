using System.Collections.Generic;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;

namespace Ion.Tests
{
    /// <summary>
    /// Place / Rewind integration in edit mode. Capture is not exercised here because it renders a
    /// preview (no graphics device under -nographics). Everything is built far from the origin so the
    /// frusta only touch the test's own objects.
    /// </summary>
    public class ProjectionSystemTests
    {
        static readonly Vector3 Origin = new Vector3(5000f, 0f, 5000f);

        readonly List<Object> _cleanup = new List<Object>();
        ProjectionSystem _system;
        Camera _viewer;
        Transform _root;

        [SetUp]
        public void SetUp()
        {
            _system = new GameObject("TestProjectionSystem").AddComponent<ProjectionSystem>();
            _cleanup.Add(_system.gameObject);

            var cam = new GameObject("TestViewer");
            cam.transform.SetPositionAndRotation(Origin, Quaternion.identity); // looking +z
            _viewer = cam.AddComponent<Camera>();
            _cleanup.Add(cam);

            _root = new GameObject("TestRoot").transform;
            _cleanup.Add(_root.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            while (_system != null && _system.CanRewind) _system.Rewind();
            var placed = GameObject.Find("PlacedPhotos");
            if (placed != null) Object.DestroyImmediate(placed);
            foreach (var o in _cleanup) if (o != null) Object.DestroyImmediate(o);
            _cleanup.Clear();
        }

        GameObject MakeSliceable(Vector3 center, Vector3 size)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(_root, false);
            go.transform.position = center;
            Mesh mesh = TestGeometry.Box(Vector3.zero, size);
            _cleanup.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            go.AddComponent<BoxCollider>();
            go.AddComponent<Sliceable>();
            return go;
        }

        static PhotoData EmptyPhoto() => new PhotoData { FovY = 30f, Aspect = 1f, Label = "empty" };

        List<Sliceable> SliceablesUnderRoot()
        {
            var list = new List<Sliceable>();
            _root.GetComponentsInChildren(true, list);
            return list;
        }

        [Test]
        public void Place_CutsHoleThroughWall_AndRewindRestores()
        {
            // 10 x 10 wall, 1 m thick, 10 m ahead: the 30° frustum (±2.7 m there) punches a hole through it.
            GameObject wall = MakeSliceable(Origin + new Vector3(0f, 0f, 10f), new Vector3(10f, 10f, 1f));
            int placedEvents = 0, rewoundEvents = 0;
            _system.Placed += () => placedEvents++;
            _system.Rewound += () => rewoundEvents++;

            _system.Place(EmptyPhoto(), _viewer, 0f);

            Assert.IsTrue(_system.CanRewind);
            Assert.AreEqual(1, placedEvents);
            Assert.IsFalse(wall.GetComponent<MeshRenderer>().enabled, "the original is hidden");
            Assert.IsFalse(wall.GetComponent<Collider>().enabled, "the original stops colliding");
            Assert.IsFalse(wall.GetComponent<Sliceable>().enabled);

            List<Sliceable> all = SliceablesUnderRoot();
            var pieces = all.FindAll(s => s.gameObject != wall);
            // The cut pieces of one object are merged into one object (one element per convex piece);
            // edit mode cooks every collider at once, so there is no separate "far" object either.
            Assert.AreEqual(1, pieces.Count, "the wall's cut pieces are one object");
            var elements = pieces[0].GetComponent<MeshElements>();
            Assert.IsNotNull(elements, "a merged cut keeps an element table");
            Assert.AreEqual(4, elements.Count, "a frame of four pieces around the hole");
            for (int e = 0; e < elements.Count; e++)
                Assert.Greater(TestGeometry.ElementVolume(elements, e), 1f, "every frame piece is a solid chunk");
            float volume = 0f;
            foreach (var p in pieces)
            {
                Assert.IsNotNull(p.GetComponent<MeshCollider>());
                Assert.IsFalse(p.GetComponent<MeshCollider>().convex);
                Assert.IsTrue(p.GetComponent<MeshRenderer>().enabled);
                volume += TestGeometry.SignedVolume(p.GetComponent<MeshFilter>().sharedMesh);
            }
            Assert.Less(volume, 100f - 1f, "some volume was removed");
            Assert.Greater(volume, 100f - 40f);

            _system.Rewind();

            Assert.IsFalse(_system.CanRewind);
            Assert.AreEqual(1, rewoundEvents);
            Assert.IsTrue(wall.GetComponent<MeshRenderer>().enabled);
            Assert.IsTrue(wall.GetComponent<Collider>().enabled);
            Assert.IsTrue(wall.GetComponent<Sliceable>().enabled);
            Assert.AreEqual(1, SliceablesUnderRoot().Count, "cut pieces are destroyed");
        }

        [Test]
        public void Place_LeavesObjectsOutsideUntouched_AndHidesObjectsFullyInside()
        {
            GameObject outside = MakeSliceable(Origin + new Vector3(30f, 0f, 10f), Vector3.one);
            GameObject inside = MakeSliceable(Origin + new Vector3(0f, 0f, 10f), Vector3.one * 0.5f);

            _system.Place(EmptyPhoto(), _viewer, 0f);

            Assert.IsTrue(outside.GetComponent<MeshRenderer>().enabled);
            Assert.IsTrue(outside.GetComponent<Sliceable>().enabled);
            Assert.IsFalse(inside.GetComponent<MeshRenderer>().enabled);
            Assert.AreEqual(2, SliceablesUnderRoot().Count, "no pieces spawned");
        }

        [Test]
        public void Place_DeactivatesInteractablesInside_AndRewindReactivates()
        {
            var pickup = new GameObject("Pickup");
            pickup.transform.SetParent(_root, false);
            pickup.transform.position = Origin + new Vector3(0f, 0f, 5f);
            pickup.AddComponent<Interactable>();
            var far = new GameObject("FarPickup");
            far.transform.SetParent(_root, false);
            far.transform.position = Origin + new Vector3(0f, 20f, 5f);
            far.AddComponent<Interactable>();

            _system.Place(EmptyPhoto(), _viewer, 0f);
            Assert.IsFalse(pickup.activeSelf);
            Assert.IsTrue(far.activeSelf);

            _system.Rewind();
            Assert.IsTrue(pickup.activeSelf);
        }

        [Test]
        public void Place_PastesPhotoPiecesRelativeToRolledViewer()
        {
            Mesh mesh = TestGeometry.Box(Vector3.zero, Vector3.one);
            _cleanup.Add(mesh);
            var photo = EmptyPhoto();
            photo.Pieces.Add(new PhotoPiece
            {
                Name = "Bridge",
                Mesh = mesh,
                Materials = new Material[0],
                Relative = Matrix4x4.TRS(new Vector3(1f, 0f, 5f), Quaternion.identity, new Vector3(2f, 1f, 1f)),
                Layer = 0,
            });

            _system.Place(photo, _viewer, 90f);

            var placed = GameObject.Find("PlacedPhotos");
            Assert.IsNotNull(placed);
            Assert.AreEqual(1, placed.transform.childCount);
            Transform piece = placed.transform.GetChild(0);
            // Rolling 90° maps the photo's +x onto the viewer's +y.
            Vector3 expected = Origin + new Vector3(0f, 1f, 5f);
            Assert.Less((piece.position - expected).magnitude, 1e-4f, $"got {piece.position}");
            Assert.Less((piece.lossyScale - new Vector3(2f, 1f, 1f)).magnitude, 1e-4f);
            Assert.AreSame(mesh, piece.GetComponent<MeshFilter>().sharedMesh);
            Assert.AreSame(mesh, piece.GetComponent<MeshCollider>().sharedMesh);
            Assert.IsNotNull(piece.GetComponent<Sliceable>());

            _system.Rewind();
            Assert.AreEqual(0, placed.transform.childCount);
            Assert.IsTrue(mesh != null, "photo meshes are shared and must survive a rewind");
        }

        [Test]
        public void Place_PastesRotatedNonUniformPiecesExactly()
        {
            // A captured piece that was rotated and non-uniformly scaled, pasted with a rolled, yawed and
            // pitched viewer: every mesh vertex must land at viewerRolled * Relative * v.
            Mesh mesh = TestGeometry.Wedge(Vector3.zero, Vector3.one);
            _cleanup.Add(mesh);
            var relative = Matrix4x4.TRS(new Vector3(-1.5f, 0.4f, 7f), Quaternion.Euler(10f, 35f, -20f), new Vector3(3f, 0.5f, 1.7f));
            var photo = EmptyPhoto();
            photo.Pieces.Add(new PhotoPiece { Name = "Ramp", Mesh = mesh, Materials = new Material[0], Relative = relative });

            _viewer.transform.rotation = Quaternion.Euler(-8f, 50f, 0f);
            _system.Place(photo, _viewer, 270f);

            Transform piece = GameObject.Find("PlacedPhotos").transform.GetChild(0);
            Matrix4x4 viewerRolled = Matrix4x4.TRS(_viewer.transform.position,
                PhotoFrustum.RolledRotation(_viewer.transform.rotation, 270f), Vector3.one);
            Matrix4x4 expected = viewerRolled * relative;
            Matrix4x4 actual = piece.localToWorldMatrix;
            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 e = expected.MultiplyPoint3x4(v), a = actual.MultiplyPoint3x4(v);
                Assert.Less((e - a).magnitude, 2e-3f, $"vertex {v}: expected {e}, got {a}");
            }
        }

        [Test]
        public void DecomposeTRS_RoundTrips_IncludingMirroredScale()
        {
            var cases = new[]
            {
                Matrix4x4.TRS(new Vector3(1f, -2f, 3f), Quaternion.Euler(30f, -70f, 110f), new Vector3(2f, 0.3f, 5f)),
                Matrix4x4.TRS(new Vector3(0f, -1000f, 400f), Quaternion.Euler(180f, 12f, 0f), new Vector3(0.7f, 2f, 0.7f)),
                Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(5f, 6f, 7f), new Vector3(1f, -1.5f, 2f)), // mirrored
            };
            foreach (Matrix4x4 m in cases)
            {
                ProjectionSystem.DecomposeTRS(m, out Vector3 p, out Quaternion r, out Vector3 s);
                Matrix4x4 back = Matrix4x4.TRS(p, r, s);
                for (int i = 0; i < 16; i++)
                    Assert.AreEqual(m[i], back[i], 1e-3f + 1e-5f * Mathf.Abs(m[i]), $"element {i} of {m}");
            }
        }

        [Test]
        public void WorldThickness_MeasuresSlabsAndCubes()
        {
            Mesh cube = TestGeometry.Box(Vector3.zero, Vector3.one);
            _cleanup.Add(cube);
            // A cube of side s measures s/3, wherever it is.
            Assert.AreEqual(1f, ProjectionSystem.WorldThickness(cube, Matrix4x4.TRS(new Vector3(0f, -1000f, 800f), Quaternion.identity, Vector3.one * 3f)), 1e-4f);
            // A 10 x 10 x 0.2 mm slab (scaled and rotated) is a sliver; a 10 x 10 x 2 cm board is not.
            Matrix4x4 slab = Matrix4x4.TRS(new Vector3(200f, 3f, 5f), Quaternion.Euler(20f, 40f, 0f), new Vector3(10f, 10f, 2e-4f));
            Matrix4x4 board = Matrix4x4.TRS(new Vector3(200f, 3f, 5f), Quaternion.Euler(20f, 40f, 0f), new Vector3(10f, 10f, 0.02f));
            Assert.IsTrue(ProjectionSystem.IsSliver(cube, slab));
            Assert.IsFalse(ProjectionSystem.IsSliver(cube, board));
            Assert.IsFalse(ProjectionSystem.IsSliver(cube, Matrix4x4.identity));
        }

        [Test]
        public void Place_TwiceFromAlmostTheSamePose_SpawnsNoSlivers()
        {
            // Placing a second photo after a tiny mouse twitch (0.0015° yaw = 0.27 mm at the wall) used to
            // leave 0.27 mm slabs of the first frame as new Sliceables with near-degenerate MeshColliders.
            var o = new Vector3(0f, 600f, 0f); // close to the origin: sub-millimetre float precision
            _viewer.transform.SetPositionAndRotation(o, Quaternion.identity);
            MakeSliceable(o + new Vector3(0.3f, -0.2f, 10f), new Vector3(10f, 10f, 1f));

            _system.Place(EmptyPhoto(), _viewer, 0f);
            _viewer.transform.rotation = Quaternion.Euler(0f, 0.0015f, 0f);
            _system.Place(EmptyPhoto(), _viewer, 0f);

            // Cut pieces are merged per object (one element each), so count and measure elements.
            int live = 0;
            foreach (Sliceable s in SliceablesUnderRoot())
            {
                if (!s.enabled) continue;
                Matrix4x4 m = s.transform.localToWorldMatrix;
                var el = s.GetComponent<MeshElements>();
                if (el == null)
                {
                    live++;
                    float thickness = ProjectionSystem.WorldThickness(s.GetComponent<MeshFilter>().sharedMesh, m);
                    Assert.GreaterOrEqual(thickness, ProjectionSystem.MinPieceThickness, $"{s.name} is a {thickness * 1000f} mm sliver");
                    continue;
                }
                Assert.IsTrue(el.EnsureData());
                for (int e = 0; e < el.Count; e++)
                {
                    live++;
                    var p = new List<Vector3>();
                    var t = new List<int>();
                    for (int i = 0; i < el.VertexCount[e]; i++) p.Add(el.Positions[el.VertexStart[e] + i]);
                    for (int i = 0; i < el.IndexCount[e]; i++) t.Add(el.Indices[el.IndexStart[e] + i] - el.VertexStart[e]);
                    float thickness = ProjectionSystem.WorldThickness(p, t, m);
                    Assert.GreaterOrEqual(thickness, ProjectionSystem.MinPieceThickness, $"{s.name}[{e}] is a {thickness * 1000f} mm sliver");
                }
            }
            Assert.AreEqual(4, live, "the frame around the hole is still four pieces");
        }

        [Test]
        public void Place_PastesCapturedInteractables_AndRewindRemovesThem()
        {
            var template = new GameObject("PickupTemplate");
            template.AddComponent<Interactable>();
            template.SetActive(false);
            _cleanup.Add(template);
            var photo = EmptyPhoto();
            photo.Entities.Add(new PhotoEntity
            {
                Template = template,
                RelativePosition = new Vector3(1f, 0f, 4f),
                RelativeRotation = Quaternion.Euler(0f, 45f, 0f),
                WorldScale = Vector3.one * 2f,
            });

            _system.Place(photo, _viewer, 90f);

            Transform placed = GameObject.Find("PlacedPhotos").transform;
            Assert.AreEqual(1, placed.childCount);
            Transform clone = placed.GetChild(0);
            Assert.AreEqual(template.name, clone.name);
            Assert.Less((clone.position - (Origin + new Vector3(0f, 1f, 4f))).magnitude, 1e-3f, "roll 90° maps photo +x onto viewer +y");
            Assert.Less((clone.lossyScale - Vector3.one * 2f).magnitude, 1e-4f);
            Assert.IsNotNull(clone.GetComponent<Interactable>());

            _system.Rewind();
            Assert.AreEqual(0, placed.childCount);
            Assert.IsTrue(template != null, "templates belong to the photo and survive a rewind");
        }

        [Test]
        public void Rewind_UndoesPlacementsInReverseOrder()
        {
            GameObject wall = MakeSliceable(Origin + new Vector3(0f, 0f, 10f), new Vector3(10f, 10f, 1f));

            _system.Place(EmptyPhoto(), _viewer, 0f);
            int afterFirst = SliceablesUnderRoot().Count;
            _viewer.transform.rotation = Quaternion.Euler(0f, 10f, 0f);
            _system.Place(EmptyPhoto(), _viewer, 45f);
            Assert.AreEqual(2, _system.PlacementCount);

            _system.Rewind();
            Assert.AreEqual(afterFirst, SliceablesUnderRoot().Count);
            foreach (var s in SliceablesUnderRoot())
                if (s.gameObject != wall) Assert.IsTrue(s.enabled && s.GetComponent<MeshRenderer>().enabled, "first placement's pieces are visible again");

            _system.Rewind();
            Assert.AreEqual(1, SliceablesUnderRoot().Count);
            Assert.IsTrue(wall.GetComponent<MeshRenderer>().enabled);
            Assert.IsFalse(_system.CanRewind);
        }
    }
}
