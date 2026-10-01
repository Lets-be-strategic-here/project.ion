using System.Collections.Generic;
using Ion.Levels.Arch;
using Ion.Presentation;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using ArchKit = Ion.Levels.Arch.Arch;

namespace Ion.Tests
{
    /// <summary>
    /// The Light Table kit (art bible §2–§4, §6): Arch geometry and grid, Bake / BakeLocal (merge per material and
    /// chunk, pattern space, Boards resolution), Validate, Palette roles and zone moods, and the pattern space
    /// surviving a real placement through ProjectionSystem.
    /// </summary>
    public class ArchKitTests
    {
        static readonly Vector3 Far = new Vector3(1000f, 0f, -1000f);

        readonly List<Object> _cleanup = new List<Object>();
        Transform _root;
        ArchStyle _previousStyle;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ArchTestRoot").transform;
            _cleanup.Add(_root.gameObject);
            _previousStyle = ArchKit.Style;
            ArchKit.Style = ArchStyle.LightTable;
        }

        [TearDown]
        public void TearDown()
        {
            ArchKit.Style = _previousStyle;
            var placed = GameObject.Find("PlacedPhotos");
            if (placed != null) Object.DestroyImmediate(placed);
            foreach (var o in _cleanup) if (o != null) Object.DestroyImmediate(o);
            _cleanup.Clear();
        }

        static List<string> Errors(Transform root)
        {
            var errors = new List<string>();
            ArchKit.Validate(root, errors);
            return errors;
        }

        static void AssertPatternSpaceIsWorld(MeshFilter mf, float tol, bool allowCaps)
        {
            Mesh m = mf.sharedMesh;
            Vector3[] v = m.vertices;
            var uv = new List<Vector4>();
            m.GetUVs(0, uv);
            Assert.AreEqual(v.Length, uv.Count, mf.name + ": UV0 on every vertex");
            Matrix4x4 l2w = mf.transform.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = l2w.MultiplyPoint3x4(v[i]);
                Assert.Less((w - (Vector3)uv[i]).magnitude, tol, mf.name + ": UV0.xyz is the world position at bake (" + w + " vs " + (Vector3)uv[i] + ")");
                if (!allowCaps) Assert.Less(uv[i].w, 63.5f, mf.name + ": no cap flag before any cut");
            }
        }

        // ------------------------------------------------------------------ palette, moods

        [Test]
        public void Palette_EveryMatHasOneSharedMaterial_AndRoundTrips()
        {
            foreach (Mat mat in System.Enum.GetValues(typeof(Mat)))
            {
                Material a = Palette.Get(mat), b = Palette.Get(mat);
                Assert.IsNotNull(a, mat.ToString());
                Assert.AreSame(a, b, mat + ": cached");
                Assert.IsTrue(Palette.TryGetMat(a, out Mat back), mat + ": TryGetMat");
                Assert.AreEqual(mat, back);
                Assert.AreSame(a, Palette.Get(new Surf(mat, Pat.Tile)), "the pattern does not change the material");
            }
            Assert.AreEqual(new Color32(0xEF, 0xEB, 0xE3, 0xFF), (Color32)Palette.ColorOf(Mat.Paper));
            Assert.AreEqual(new Color32(0x9F, 0xE3, 0xFF, 0xFF), (Color32)Palette.ColorOf(Mat.Ion));
            Assert.Greater(Palette.EmissionOf(Mat.Ion), 0f);
            Assert.AreEqual(0f, Palette.EmissionOf(Mat.Paper));
            Assert.IsFalse(Palette.TryGetMat(Palette.Get(Palette.Coral), out _), "legacy colour materials have no role");

            var clone = new Material(Palette.Get(Mat.Foliage)) { name = Palette.MatPrefix + "Foliage_Sway" };
            _cleanup.Add(clone);
            Assert.IsTrue(Palette.TryGetMat(clone, out Mat cm));
            Assert.AreEqual(Mat.Foliage, cm, "clones are recognised by name");
        }

        [Test]
        public void ZoneMood_ShadeTintKeepsHueAndValue()
        {
            Color shade = Atmosphere.ShadeMultiplier(ZoneMood.Dawn.ShadowTint);
            float luma = 0.2126f * shade.r + 0.7152f * shade.g + 0.0722f * shade.b;
            Assert.AreEqual(Atmosphere.ShadowTintLuma, luma, 0.02f);
            Assert.Greater(shade.b, shade.r, "Dawn's shade stays blue");

            ZoneMood a = ZoneMood.Noon, b = ZoneMood.Golden;
            Assert.Less(Diff(a.SkyTop, ZoneMood.Lerp(a, b, 0f).SkyTop), 1e-5f);
            Assert.Less(Diff(b.SkyTop, ZoneMood.Lerp(a, b, 1f).SkyTop), 1e-5f);
            Assert.AreEqual(1f, ZoneMood.Lerp(a, b, 0.5f).ToSun.magnitude, 1e-4f);
            Assert.Less(Diff(new Color32(0x1F, 0xA6, 0xD2, 0xFF), ZoneMood.Noon.SkyTop), 1e-5f);
        }

        static float Diff(Color a, Color b) => ((Vector4)a - (Vector4)b).magnitude;

        [Test]
        public void PatternGlobals_FollowTheTier()
        {
            Atmosphere.ApplyPatternGlobals();
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_IonPatternOn"));
            Vector4 fade = Shader.GetGlobalVector("_IonPatternFade");
            Assert.Less(fade.x, fade.y);
            Atmosphere.PatternsEnabled = false;
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_IonPatternOn"));
            Atmosphere.PatternsEnabled = true;
        }

        // ------------------------------------------------------------------ geometry and grid

        [Test]
        public void Kit_BuildsOnGrid_AndValidates()
        {
            ArchKit.Floor(_root, new RectXZ(-8f, 0f, 8f, 12f));
            ArchKit.Wall(_root, new Vector3(-8f, 0f, 12f), new Vector3(8f, 0f, 12f), 4f, 0.5f,
                         WallTrim.Default | WallTrim.Pilasters,
                         Opening.Door(4f), Opening.Window(8.5f), Opening.Niche(12f), Opening.Portal(14f, 2f, 3f));
            ArchKit.Terrace(_root, new RectXZ(-4f, 14f, 4f, 20f), 3f, 4f);
            ArchKit.Stair(_root, new Vector3(0f, 0f, 2f), Dir.PosZ, 3f);
            ArchKit.Parapet(_root, new Vector3(-8f, 0f, 0.125f), new Vector3(8f, 0f, 0.125f));
            ArchKit.Railing(_root, new Vector3(9f, 0f, 0f), new Vector3(9f, 0f, 6f));
            ArchKit.BracketFrame(_root, new Vector3(-4f, 0f, 6f), Dir.PosZ);
            ArchKit.CorbelArch(_root, new Vector3(4f, 0f, 6f), Dir.PosX);
            ArchKit.Column(_root, new Vector3(-6f, 0f, 4f));
            ArchKit.Pier(_root, new Vector3(-6f, 0f, 8f), new Vector2(1f, 1f), 3f);
            ArchKit.BracketPier(_root, new Vector3(6f, 0f, 8f), 3.5f, Dir.NegX);
            ArchKit.Canopy(_root, new RectXZ(-3f, -3f, 3f, 3f), 3.5f);
            ArchKit.Roof(_root, new RectXZ(10f, 0f, 18f, 8f), 4f);
            ArchKit.Pergola(_root, new RectXZ(-16f, 0f, -10f, 6f));
            ArchKit.Screen(_root, new Vector3(-8f, 0f, -2f), new Vector3(-4f, 0f, -2f), 3f, ArchKit.ScreenKind.ContactSheet);
            ArchKit.Screen(_root, new Vector3(-3f, 0f, -2f), new Vector3(1f, 0f, -2f), 3f, ArchKit.ScreenKind.Lattice);
            ArchKit.Ramp(_root, new Vector3(12f, 0f, 10f), Dir.PosZ, 2f, 4f, 2f);
            ArchKit.SlabTower(_root, new Vector3(20f, 0f, 20f), new Vector2(3f, 3f), 3, 7);
            ArchKit.ChamferBox(_root, new Vector3(0f, 0f, -6f), new Vector3(1f, 0.5f, -5f), 0.0625f, Mat.Walnut);
            ArchKit.Prism(_root, new Vector3(2f, 0f, -6f), 0.4f, 0.5f, 12, Mat.Terracotta);

            List<string> before = Errors(_root);
            Assert.IsEmpty(before, "before bake:\n" + string.Join("\n", before));

            int created = ArchKit.Bake(_root);
            Assert.Greater(created, 0);
            Assert.AreEqual(0, _root.GetComponentsInChildren<ArchPiece>(true).Length, "sources are gone");
            List<string> after = Errors(_root);
            Assert.IsEmpty(after, "after bake:\n" + string.Join("\n", after));
        }

        [Test]
        public void Validate_FlagsOffGridOpenAndVisualPieces()
        {
            ArchKit.Tag(ArchKit.Box(_root, new Vector3(0.1f, 0f, 0f), new Vector3(1f, 1f, 1f), Mat.Paper), ArchKit.Sub);
            ArchKit.Box(_root, new Vector3(2f, 0f, 0f), new Vector3(3f, 1f, 1f), Mat.Paper, ArchFlags.Visual);
            var legacy = new GameObject("LegacySliceable");
            legacy.transform.SetParent(_root, false);
            legacy.AddComponent<MeshFilter>().sharedMesh = Ion.Levels.Geo.CubeMesh;
            legacy.AddComponent<MeshRenderer>();
            legacy.AddComponent<Sliceable>();

            List<string> errors = Errors(_root);
            Assert.IsTrue(errors.Exists(e => e.StartsWith("Off grid")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.StartsWith("Visual piece outside")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.StartsWith("Sliceable without pattern space")), string.Join("\n", errors));
        }

        [Test]
        public void ChamferBox_IsClosedConvexAndFitsItsBox()
        {
            GameObject go = ArchKit.ChamferBox(_root, new Vector3(-0.5f, 0f, -0.25f), new Vector3(0.5f, 0.75f, 0.25f), 0.0625f, Mat.Walnut);
            Mesh m = go.GetComponent<MeshFilter>().sharedMesh;
            TestGeometry.AssertWatertight(m);
            Bounds b = TestGeometry_World(go);
            Assert.AreEqual(-0.5f, b.min.x, 1e-4f);
            Assert.AreEqual(0.75f, b.max.y, 1e-4f);
            float full = 1f * 0.75f * 0.5f;
            float vol = TestGeometry.SignedVolume(m);
            Assert.Less(vol, full, "corners are cut");
            Assert.Greater(vol, full * 0.9f);
            var normals = new HashSet<Vector3>();
            foreach (Vector3 n in m.normals) normals.Add(new Vector3(Mathf.Round(n.x * 100f), Mathf.Round(n.y * 100f), Mathf.Round(n.z * 100f)));
            Assert.AreEqual(26, normals.Count, "6 faces + 12 edge bevels + 8 corners");
        }

        static Bounds TestGeometry_World(GameObject go)
        {
            Mesh m = go.GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 l2w = go.transform.localToWorldMatrix;
            var b = new Bounds(l2w.MultiplyPoint3x4(m.vertices[0]), Vector3.zero);
            foreach (Vector3 v in m.vertices) b.Encapsulate(l2w.MultiplyPoint3x4(v));
            return b;
        }

        [Test]
        public void Wall_DoorAndWindowAreClear()
        {
            ArchKit.Wall(_root, new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), 4f, 0.5f, WallTrim.Default,
                         Opening.Door(3f), Opening.Window(7f));
            var door = new Bounds(new Vector3(3f, 1.5f, 0f), new Vector3(2f - 0.02f, 3f - 0.02f, 2f));
            var window = new Bounds(new Vector3(7f, 2f, 0f), new Vector3(1f - 0.02f, 2f - 0.02f, 2f));
            foreach (ArchPiece p in _root.GetComponentsInChildren<ArchPiece>())
            {
                Bounds b = TestGeometry_World(p.gameObject);
                b.Expand(-0.001f);
                Assert.IsFalse(b.Intersects(door), "nothing blocks the door: " + p.name + " " + b);
                Assert.IsFalse(b.Intersects(window), "nothing blocks the window: " + p.name + " " + b);
            }
            // The wall is closed above the door (lintel) and below the window (sill box).
            bool lintel = false, sill = false;
            foreach (ArchPiece p in _root.GetComponentsInChildren<ArchPiece>())
            {
                Bounds b = TestGeometry_World(p.gameObject);
                if (b.min.y <= 3f + 1e-3f && b.max.y >= 3.9f && b.min.x <= 2.1f && b.max.x >= 3.9f) lintel = true;
                if (b.min.y <= 0.01f && b.max.y >= 0.99f && b.min.x <= 6.6f && b.max.x >= 7.4f && p.Surf.Mat == Mat.Paper) sill = true;
            }
            Assert.IsTrue(lintel);
            Assert.IsTrue(sill);
        }

        [Test]
        public void Stair_StepsAndLanding()
        {
            StairResult s = ArchKit.Stair(_root, new Vector3(1f, 0f, 10f), Dir.PosZ, 3f);
            Assert.AreEqual(12, s.Steps);
            Assert.Less((s.TopLanding - new Vector3(1f, 3f, 16f)).magnitude, 1e-4f);
            StairResult x = ArchKit.Stair(_root, new Vector3(0f, 0f, 0f), Dir.NegX, 1f, 1.5f, false);
            Assert.Less((x.TopLanding - new Vector3(-2f, 1f, 0f)).magnitude, 1e-4f);
        }

        // ------------------------------------------------------------------ bake

        [Test]
        public void Bake_MergesPerMaterialAndChunk_WithWorldPatternSpace()
        {
            _root.position = new Vector3(50f, 0f, 0f); // zone origins are whole metres
            ArchKit.Floor(_root, new RectXZ(0f, 0f, 4f, 4f));
            ArchKit.Floor(_root, new RectXZ(20f, 0f, 24f, 4f));                                   // another chunk
            ArchKit.Box(_root, new Vector3(1f, 0f, 1f), new Vector3(1.5f, 0.5f, 3f), ArchStyle.LightTable.Wood); // long in Z
            ArchKit.Box(_root, new Vector3(2f, 0f, 1f), new Vector3(3f, 0.25f, 1.25f), Mat.Brass, ArchFlags.Soft);

            int created = ArchKit.Bake(_root);
            var merged = new List<MeshFilter>(_root.GetComponentsInChildren<MeshFilter>());
            Assert.AreEqual(created, merged.Count);

            int limestoneChunks = 0;
            foreach (MeshFilter mf in merged)
            {
                AssertPatternSpaceIsWorld(mf, 1e-3f, false);
                var mr = mf.GetComponent<MeshRenderer>();
                Assert.IsTrue(Palette.TryGetMat(mr.sharedMaterial, out Mat mat));
                var el = mf.GetComponent<MeshElements>();
                Assert.IsNotNull(el);
                Assert.AreSame(mf.sharedMesh, el.Mesh);
                Assert.IsNotNull(el.Uvs, "CPU UV0 attached for the slicer");
                Assert.IsNotNull(mf.GetComponent<Sliceable>());
                if (mat == Mat.Limestone) limestoneChunks++;
                if (mat == Mat.Brass) Assert.IsNull(mf.GetComponent<Collider>(), "Soft pieces get no collider");
                if (mat == Mat.Oak)
                {
                    var uv = new List<Vector4>();
                    mf.sharedMesh.GetUVs(0, uv);
                    foreach (Vector4 u in uv) Assert.AreEqual((float)Pat.BoardsZ, u.w, "Boards resolve along the longest extent");
                    Assert.IsNotNull(mf.GetComponent<MeshCollider>());
                }
            }
            Assert.AreEqual(2, limestoneChunks, "one Limestone object per chunk");
            Assert.AreEqual(0, _root.childCount - merged.Count, "empty Arch groups are removed");
        }

        [Test]
        public void BakeLocal_UsesObjectSpace_AndSkipsNestedDevices()
        {
            var device = new GameObject("Device");
            device.transform.SetParent(_root, false);
            device.transform.localPosition = new Vector3(10f, 2f, 5f);
            device.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            device.AddComponent<Interactable>();
            ArchKit.Box(device.transform, new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 1f, 0.5f), new Surf(Mat.Paper, Pat.Formwork),
                        ArchFlags.Solid | ArchFlags.Dynamic);
            var nested = new GameObject("Nested");
            nested.transform.SetParent(device.transform, false);
            nested.AddComponent<Interactable>();
            ArchKit.Box(nested.transform, Vector3.zero, Vector3.one * 0.25f, Mat.Ion, ArchFlags.Visual);

            // A zone bake leaves devices alone.
            ArchKit.Bake(_root);
            Assert.AreEqual(2, device.GetComponentsInChildren<ArchPiece>().Length);

            ArchKit.BakeLocal(device);
            Assert.AreEqual(1, device.GetComponentsInChildren<ArchPiece>().Length, "the nested device keeps its piece");
            MeshFilter mf = null;
            foreach (Transform c in device.transform)
                if (c.GetComponent<MeshElements>() != null) mf = c.GetComponent<MeshFilter>();
            Assert.IsNotNull(mf);
            var uv = new List<Vector4>();
            mf.sharedMesh.GetUVs(0, uv);
            Vector3[] v = mf.sharedMesh.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                Assert.Less((v[i] - (Vector3)uv[i]).magnitude, 1e-4f, "BakeLocal: pattern space = object-local");
                Assert.AreEqual((float)Pat.Formwork, uv[i].w);
            }
        }

        // ------------------------------------------------------------------ through the slicer

        [Test]
        public void Placement_KeepsPatternSpace_AndFlagsCutFaces()
        {
            var system = new GameObject("ArchTestProjection").AddComponent<ProjectionSystem>();
            _cleanup.Add(system.gameObject);
            var cam = new GameObject("ArchTestViewer");
            cam.transform.SetPositionAndRotation(Far + new Vector3(0f, 1.6f, 0f), Quaternion.identity);
            Camera viewer = cam.AddComponent<Camera>();
            _cleanup.Add(cam);
            try
            {
                _root.position = Far;
                ArchKit.Floor(_root, new RectXZ(-6f, -2f, 6f, 20f));
                ArchKit.Wall(_root, new Vector3(-6f, 0f, 10f), new Vector3(6f, 0f, 10f), 6f);
                ArchKit.Bake(_root);

                system.Place(new PhotoData { FovY = 30f, Aspect = 1.3f, Label = "empty" }, viewer, 0f);
                Assert.IsTrue(system.CanRewind);

                int capVertices = 0, checkedMeshes = 0;
                foreach (Sliceable s in _root.GetComponentsInChildren<Sliceable>())
                {
                    if (!s.enabled || !s.GetComponent<MeshRenderer>().enabled) continue;
                    MeshFilter mf = s.GetComponent<MeshFilter>();
                    AssertPatternSpaceIsWorld(mf, 2e-3f, true);
                    var uv = new List<Vector4>();
                    mf.sharedMesh.GetUVs(0, uv);
                    foreach (Vector4 u in uv) if (u.w > 63.5f) capVertices++;
                    checkedMeshes++;
                    var el = s.GetComponent<MeshElements>();
                    if (el != null && el.Mesh == mf.sharedMesh && el.Uvs != null) Assert.AreEqual(mf.sharedMesh.vertexCount, el.Uvs.Length);
                }
                Assert.Greater(checkedMeshes, 0);
                Assert.Greater(capVertices, 0, "the hole through the wall has flagged cut faces");

                // A second placement from a slightly different pose re-cuts the cut pieces (element path + UV0).
                cam.transform.position += new Vector3(0.7f, 0.2f, 0.5f);
                system.Place(new PhotoData { FovY = 25f, Aspect = 1f, Label = "empty" }, viewer, 90f);
                foreach (Sliceable s in _root.GetComponentsInChildren<Sliceable>())
                {
                    if (!s.enabled || !s.GetComponent<MeshRenderer>().enabled) continue;
                    AssertPatternSpaceIsWorld(s.GetComponent<MeshFilter>(), 2e-3f, true);
                }
            }
            finally
            {
                while (system != null && system.CanRewind) system.Rewind();
            }
        }

        [Test]
        public void CaptureAndPaste_ElementWise_KeepsPatternSpace()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                Assert.Ignore("Capture renders a preview; run with -nographics (BUILD.md) to exercise it in edit mode.");
            var system = new GameObject("ArchCaptureProjection").AddComponent<ProjectionSystem>();
            _cleanup.Add(system.gameObject);
            var cam = new GameObject("ArchCaptureViewer");
            Camera viewer = cam.AddComponent<Camera>();
            _cleanup.Add(cam);
            Vector3 origin = new Vector3(-1000f, 0f, 1000f);
            try
            {
                _root.position = origin;
                ArchKit.Floor(_root, new RectXZ(-6f, -2f, 6f, 20f));
                ArchKit.Wall(_root, new Vector3(-6f, 0f, 10f), new Vector3(6f, 0f, 10f), 4f, 0.5f, WallTrim.Default, Opening.Door(6f));
                ArchKit.Bake(_root);

                var pose = new Pose(origin + new Vector3(0f, 1.6f, 0f), Quaternion.identity);
                PhotoData photo = system.Capture(pose, 30f, 1.3f, "arch");
                Assert.Greater(photo.PieceCount, 0);
                foreach (PhotoPiece piece in photo.Pieces)
                {
                    var uv = new List<Vector4>();
                    piece.Mesh.GetUVs(0, uv);
                    Vector3[] v = piece.Mesh.vertices;
                    Assert.AreEqual(v.Length, uv.Count, piece.Name + ": captured pieces keep UV0");
                    Matrix4x4 world = Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one) * piece.Relative;
                    for (int i = 0; i < v.Length; i++)
                        Assert.Less((world.MultiplyPoint3x4(v[i]) - (Vector3)uv[i]).magnitude, 2e-3f, piece.Name + ": UV0 = world position at bake");
                    Assert.IsNotNull(piece.Elements, piece.Name + ": a merged capture keeps one element per convex piece");
                    for (int e = 0; e < piece.Elements.VertexStart.Length; e++)
                        Assert.Greater(piece.Elements.IndexCount[e], 0);
                }

                // Paste it 30 m to the side: UV0 never moves with the paste (it is data).
                Vector3 shift = new Vector3(30f, 0f, 0f);
                cam.transform.SetPositionAndRotation(pose.position + shift, pose.rotation);
                system.Place(photo, viewer, 0f);
                int checkedPieces = 0;
                foreach (Renderer r in system.LastPastedRenderers)
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    var uv = new List<Vector4>();
                    mf.sharedMesh.GetUVs(0, uv);
                    Vector3[] v = mf.sharedMesh.vertices;
                    Matrix4x4 l2w = mf.transform.localToWorldMatrix;
                    for (int i = 0; i < v.Length; i++)
                        Assert.Less((l2w.MultiplyPoint3x4(v[i]) - shift - (Vector3)uv[i]).magnitude, 3e-3f);
                    checkedPieces++;
                }
                Assert.Greater(checkedPieces, 0);
            }
            finally
            {
                while (system != null && system.CanRewind) system.Rewind();
            }
        }
    }
}
