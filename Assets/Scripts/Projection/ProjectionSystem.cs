using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Projection
{
    /// <summary>
    /// Captures photos of the world and places them back: the world inside the photo frustum is
    /// cut away and replaced by the photo's contents, which become real geometry. Placements can
    /// be rewound (undo stack).
    ///
    /// Preview rendering uses RenderPipeline.SubmitRenderRequest with a StandardRequest (the
    /// supported way to render a camera on demand in URP, Unity 6) and falls back to
    /// Camera.Render() when no render pipeline accepts the request (e.g. built-in RP in tests).
    /// If Capture runs before URP has instantiated its pipeline (Awake on the first frame), the
    /// Preview texture is returned immediately (neutral grey) and filled on the next LateUpdate.
    /// Placement work is done synchronously in one call (no spreading across frames).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class ProjectionSystem : MonoBehaviour
    {
        public const float HoldNear = 0.6f, MaxFar = 250f;
        public const int PlayerLayer = 8, PhotoUILayer = 9;
        /// <summary>Layers that are never cut, captured or rendered into previews.</summary>
        public const int ExcludedLayerMask = (1 << PlayerLayer) | (1 << PhotoUILayer);
        public const int PreviewWidth = 512;

        // Pieces whose bounds are smaller than this in every dimension (local units) are dropped.
        const float MinPieceExtent = 1e-3f;

        /// <summary>
        /// Cut / captured pieces thinner than this (world metres, measured as 2·volume / surface area,
        /// which is the thickness of a slab) are dropped. Re-placing a photo from almost the same pose
        /// otherwise leaves sub-millimetre slabs along the old cut faces: invisible, but each one would
        /// become a Sliceable with a near-degenerate MeshCollider (PhysX cooking warnings) and keep
        /// spawning ever thinner debris on later cuts. Assumes closed meshes (Sliceable contract).
        /// </summary>
        internal const float MinPieceThickness = 5e-4f;

        static ProjectionSystem s_instance;
        static bool s_quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_instance = null;
            s_quitting = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        static void OnQuitting() => s_quitting = true;

        /// <summary>The active system. Found in the scene, or created on demand while playing.</summary>
        public static ProjectionSystem Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindFirstObjectByType<ProjectionSystem>();
                    if (s_instance == null && Application.isPlaying && !s_quitting)
                        s_instance = new GameObject("ProjectionSystem").AddComponent<ProjectionSystem>();
                }
                return s_instance;
            }
        }

        public event System.Action Placed, Rewound;

        public bool CanRewind => _undo.Count > 0;

        /// <summary>Number of placements on the undo stack.</summary>
        public int PlacementCount => _undo.Count;

        sealed class PlacementRecord
        {
            public readonly List<GameObject> Spawned = new List<GameObject>();
            public readonly List<Mesh> OwnedMeshes = new List<Mesh>();
            public readonly List<Behaviour> DisabledSliceables = new List<Behaviour>();
            public readonly List<Renderer> DisabledRenderers = new List<Renderer>();
            public readonly List<Collider> DisabledColliders = new List<Collider>();
            public readonly List<GameObject> Deactivated = new List<GameObject>();
        }

        readonly List<PlacementRecord> _undo = new List<PlacementRecord>();
        readonly Plane[] _planes = new Plane[PhotoFrustum.PlaneCount];
        readonly List<Mesh> _pieces = new List<Mesh>(8);
        readonly List<Collider> _colliderScratch = new List<Collider>(4);
        Transform _templateHolder;
        Transform _placedRoot;
        Camera _previewCamera;

        void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Debug.LogWarning("ProjectionSystem: a second instance was created; destroying it.");
                Destroy(this);
                return;
            }
            s_instance = this;
            EnsureTemplateHolder();
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        // ------------------------------------------------------------------ capture

        /// <summary>
        /// Copies everything inside the frustum at <paramref name="pose"/> (near = HoldNear,
        /// far = MaxFar) into a new photo and renders its preview. The world is not modified.
        /// </summary>
        public PhotoData Capture(Pose pose, float fovY, float aspect, string label)
        {
            var frustum = new PhotoFrustum { Pose = pose, FovY = fovY, Aspect = aspect, Near = HoldNear, Far = MaxFar };
            frustum.GetPlanes(_planes);

            var photo = new PhotoData { FovY = fovY, Aspect = aspect, Label = label };
            Matrix4x4 captureInverse = Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one).inverse;

            Sliceable[] sliceables = FindObjectsByType<Sliceable>(FindObjectsSortMode.None);
            for (int i = 0; i < sliceables.Length; i++)
            {
                if (!TryGetCuttable(sliceables[i], out MeshFilter mf, out MeshRenderer mr)) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, mr.bounds)) continue;

                Matrix4x4 localToWorld = mf.transform.localToWorldMatrix;
                Mesh inside = MeshClipper.ClipInside(mf.sharedMesh, localToWorld, _planes);
                if (inside == null) continue;
                if (IsSliver(inside, localToWorld)) { DestroyObject(inside); continue; }

                photo.Pieces.Add(new PhotoPiece
                {
                    Name = mf.gameObject.name,
                    Mesh = inside,
                    Materials = mr.sharedMaterials,
                    Relative = captureInverse * localToWorld,
                    Layer = mf.gameObject.layer,
                    ShadowCasting = mr.shadowCastingMode,
                    ReceiveShadows = mr.receiveShadows,
                    Collide = mf.TryGetComponent(out Collider _),
                });
            }

            Interactable[] interactables = FindObjectsByType<Interactable>(FindObjectsSortMode.None);
            for (int i = 0; i < interactables.Length; i++)
            {
                Interactable it = interactables[i];
                if (!IsLive(it) || IsExcludedLayer(it.gameObject.layer)) continue;
                // Only the outermost Interactable of a hierarchy is cloned.
                Transform parent = it.transform.parent;
                if (parent != null && parent.GetComponentInParent<Interactable>() != null) continue;
                Transform t = it.transform;
                if (!frustum.Contains(t.position)) continue;

                GameObject template = Instantiate(it.gameObject, EnsureTemplateHolder(), false);
                template.name = it.gameObject.name;
                photo.Entities.Add(new PhotoEntity
                {
                    Template = template,
                    RelativePosition = captureInverse.MultiplyPoint3x4(t.position),
                    RelativeRotation = Quaternion.Inverse(pose.rotation) * t.rotation,
                    WorldScale = t.lossyScale,
                });
            }

            photo.Preview = RenderPreview(pose, fovY, aspect, label);
            return photo;
        }

        // ------------------------------------------------------------------ place / rewind

        /// <summary>
        /// Cuts the world outside-of-frustum (removing everything inside the viewer's photo frustum)
        /// and pastes the photo's contents, then pushes an undo record.
        /// </summary>
        public void Place(PhotoData photo, Camera viewer, float rollDegrees)
        {
            if (photo == null || viewer == null) return;

            PhotoFrustum frustum = PhotoFrustum.FromCamera(viewer, photo.FovY, photo.Aspect, rollDegrees, HoldNear, MaxFar);
            frustum.GetPlanes(_planes);
            var record = new PlacementRecord();

            // 1. Replace every cut Sliceable by its outside pieces.
            Sliceable[] sliceables = FindObjectsByType<Sliceable>(FindObjectsSortMode.None);
            for (int i = 0; i < sliceables.Length; i++)
            {
                Sliceable s = sliceables[i];
                if (!TryGetCuttable(s, out MeshFilter mf, out MeshRenderer mr)) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, mr.bounds)) continue;

                _pieces.Clear();
                Matrix4x4 localToWorld = mf.transform.localToWorldMatrix;
                MeshClipper.Classification result =
                    MeshClipper.ClipOutside(mf.sharedMesh, localToWorld, _planes, _pieces);
                if (result == MeshClipper.Classification.Outside) continue;

                Hide(s, mr, record);
                for (int p = 0; p < _pieces.Count; p++)
                {
                    Mesh piece = _pieces[p];
                    if (IsSliver(piece, localToWorld)) { DestroyObject(piece); continue; }
                    record.OwnedMeshes.Add(piece);
                    record.Spawned.Add(SpawnCutPiece(mf, mr, piece));
                }
                _pieces.Clear();
            }

            // 2. Remove Interactables inside the frustum.
            Interactable[] interactables = FindObjectsByType<Interactable>(FindObjectsSortMode.None);
            for (int i = 0; i < interactables.Length; i++)
            {
                Interactable it = interactables[i];
                if (!IsLive(it) || IsExcludedLayer(it.gameObject.layer)) continue;
                if (!frustum.Contains(it.transform.position)) continue;
                it.gameObject.SetActive(false);
                record.Deactivated.Add(it.gameObject);
            }

            // 3. Paste the photo: worldPose = viewerPoseRolled * capturePose⁻¹ * pieceWorldPose.
            Matrix4x4 viewerFrame = Matrix4x4.TRS(frustum.Pose.position, frustum.Pose.rotation, Vector3.one);
            Transform root = EnsurePlacedRoot();
            for (int i = 0; i < photo.Pieces.Count; i++)
                record.Spawned.Add(SpawnPhotoPiece(photo.Pieces[i], viewerFrame, root));
            for (int i = 0; i < photo.Entities.Count; i++)
            {
                PhotoEntity e = photo.Entities[i];
                if (e.Template == null) continue;
                Vector3 pos = viewerFrame.MultiplyPoint3x4(e.RelativePosition);
                Quaternion rot = frustum.Pose.rotation * e.RelativeRotation;
                GameObject go = Instantiate(e.Template, pos, rot, root);
                go.name = e.Template.name;
                go.transform.localScale = e.WorldScale;
                record.Spawned.Add(go);
            }

            // 4. Push undo.
            _undo.Add(record);
            Placed?.Invoke();
        }

        /// <summary>Undoes the most recent placement.</summary>
        public void Rewind()
        {
            if (_undo.Count == 0) return;
            PlacementRecord record = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            for (int i = 0; i < record.Spawned.Count; i++)
            {
                GameObject go = record.Spawned[i];
                if (go == null) continue;
                go.SetActive(false); // invisible to Find* immediately, even though Destroy is deferred
                DestroyObject(go);
            }
            for (int i = 0; i < record.OwnedMeshes.Count; i++)
                if (record.OwnedMeshes[i] != null) DestroyObject(record.OwnedMeshes[i]);

            for (int i = record.Deactivated.Count - 1; i >= 0; i--)
                if (record.Deactivated[i] != null) record.Deactivated[i].SetActive(true);
            for (int i = 0; i < record.DisabledRenderers.Count; i++)
                if (record.DisabledRenderers[i] != null) record.DisabledRenderers[i].enabled = true;
            for (int i = 0; i < record.DisabledColliders.Count; i++)
                if (record.DisabledColliders[i] != null) record.DisabledColliders[i].enabled = true;
            for (int i = 0; i < record.DisabledSliceables.Count; i++)
                if (record.DisabledSliceables[i] != null) record.DisabledSliceables[i].enabled = true;

            Rewound?.Invoke();
        }

        /// <summary>Forgets all placements without undoing them (e.g. when a level is rebuilt).</summary>
        public void ClearHistory()
        {
            _undo.Clear();
        }

        /// <summary>The world-space frustum a placement would use right now (for UI / debug).</summary>
        public static PhotoFrustum PlacementFrustum(PhotoData photo, Camera viewer, float rollDegrees)
        {
            return PhotoFrustum.FromCamera(viewer, photo.FovY, photo.Aspect, rollDegrees, HoldNear, MaxFar);
        }

        // ------------------------------------------------------------------ helpers

        static bool IsExcludedLayer(int layer) => (ExcludedLayerMask & (1 << layer)) != 0;

        // Explicit instead of isActiveAndEnabled, which is unreliable in edit mode (tests).
        static bool IsLive(Behaviour b) => b.enabled && b.gameObject.activeInHierarchy;

        static bool TryGetCuttable(Sliceable s, out MeshFilter mf, out MeshRenderer mr)
        {
            mf = null;
            mr = null;
            if (s == null || !IsLive(s)) return false;
            GameObject go = s.gameObject;
            if (IsExcludedLayer(go.layer)) return false;
            if (!go.TryGetComponent(out mf) || mf.sharedMesh == null) return false;
            if (!go.TryGetComponent(out mr) || !mr.enabled) return false;
            // Interactables are captured / removed whole, never cut.
            if (s.GetComponentInParent<Interactable>(true) != null) return false;
            return true;
        }

        static readonly List<Vector3> s_measureVerts = new List<Vector3>(256);
        static readonly List<int> s_measureTris = new List<int>(256);

        /// <summary>
        /// True for pieces not worth spawning: (almost) no vertices, tiny in every dimension, or thinner
        /// than <see cref="MinPieceThickness"/> in world space.
        /// </summary>
        internal static bool IsSliver(Mesh mesh, Matrix4x4 localToWorld)
        {
            if (mesh == null || mesh.vertexCount < 3) return true;
            Vector3 size = mesh.bounds.size;
            if (size.x < MinPieceExtent && size.y < MinPieceExtent && size.z < MinPieceExtent) return true;
            return WorldThickness(mesh, localToWorld) < MinPieceThickness;
        }

        /// <summary>
        /// 2·volume / surface area of a closed mesh in world space (the thickness of a slab; a third of
        /// the side of a cube). Volume is summed relative to the first vertex in double precision, so
        /// meshes far from the origin (dioramas at y = -1000) measure accurately.
        /// </summary>
        internal static float WorldThickness(Mesh mesh, Matrix4x4 localToWorld)
        {
            mesh.GetVertices(s_measureVerts);
            if (s_measureVerts.Count == 0) return 0f;
            Vector3 r = s_measureVerts[0];
            double volume = 0, area = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                mesh.GetTriangles(s_measureTris, s);
                for (int t = 0; t + 2 < s_measureTris.Count; t += 3)
                {
                    Vector3 a = s_measureVerts[s_measureTris[t]] - r;
                    Vector3 b = s_measureVerts[s_measureTris[t + 1]] - r;
                    Vector3 c = s_measureVerts[s_measureTris[t + 2]] - r;
                    volume += Vector3.Dot(a, Vector3.Cross(b, c));
                    Vector3 e1 = localToWorld.MultiplyVector(b - a), e2 = localToWorld.MultiplyVector(c - a);
                    area += Vector3.Cross(e1, e2).magnitude;
                }
            }
            s_measureTris.Clear();
            s_measureVerts.Clear();
            if (area <= 1e-12) return 0f;
            Matrix4x4 m = localToWorld;
            double det = m.m00 * ((double)m.m11 * m.m22 - (double)m.m12 * m.m21)
                       - m.m01 * ((double)m.m10 * m.m22 - (double)m.m12 * m.m20)
                       + m.m02 * ((double)m.m10 * m.m21 - (double)m.m11 * m.m20);
            double worldVolume = System.Math.Abs(volume / 6.0 * det);
            return (float)(2.0 * worldVolume / (0.5 * area));
        }

        void Hide(Sliceable s, MeshRenderer mr, PlacementRecord record)
        {
            s.enabled = false;
            record.DisabledSliceables.Add(s);
            mr.enabled = false;
            record.DisabledRenderers.Add(mr);
            _colliderScratch.Clear();
            s.GetComponents(_colliderScratch);
            for (int i = 0; i < _colliderScratch.Count; i++)
            {
                Collider c = _colliderScratch[i];
                if (!c.enabled) continue;
                c.enabled = false;
                record.DisabledColliders.Add(c);
            }
            _colliderScratch.Clear();
        }

        static GameObject SpawnCutPiece(MeshFilter source, MeshRenderer sourceRenderer, Mesh mesh)
        {
            GameObject src = source.gameObject;
            var go = new GameObject(src.name + " (cut)") { layer = src.layer };
            Transform st = src.transform, t = go.transform;
            t.SetParent(st.parent, false);
            t.localPosition = st.localPosition;
            t.localRotation = st.localRotation;
            t.localScale = st.localScale;
            AddGeometry(go, mesh, sourceRenderer.sharedMaterials, sourceRenderer.shadowCastingMode, sourceRenderer.receiveShadows,
                        src.TryGetComponent(out Collider _));
            return go;
        }

        static GameObject SpawnPhotoPiece(PhotoPiece piece, Matrix4x4 viewerFrame, Transform root)
        {
            Matrix4x4 world = viewerFrame * piece.Relative;
            var go = new GameObject(piece.Name + " (photo)") { layer = piece.Layer };
            Transform t = go.transform;
            t.SetParent(root, false);
            DecomposeTRS(world, out Vector3 position, out Quaternion rotation, out Vector3 scale);
            t.SetPositionAndRotation(position, rotation);
            t.localScale = scale; // root is never moved or scaled, so local == world
            AddGeometry(go, piece.Mesh, piece.Materials, piece.ShadowCasting, piece.ReceiveShadows, piece.Collide);
            return go;
        }

        /// <summary>
        /// Exact decomposition of a shear-free TRS matrix (what Transform hierarchies without a scaled,
        /// rotated parent produce): scale = column lengths (x negated for mirrored matrices), rotation
        /// from the scale-free z and y columns. Unlike Matrix4x4.rotation / lossyScale this does not
        /// depend on how the engine treats non-uniform scale.
        /// </summary>
        internal static void DecomposeTRS(Matrix4x4 m, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = new Vector3(m.m03, m.m13, m.m23);
            var c0 = new Vector3(m.m00, m.m10, m.m20);
            var c1 = new Vector3(m.m01, m.m11, m.m21);
            var c2 = new Vector3(m.m02, m.m12, m.m22);
            float sx = c0.magnitude, sy = c1.magnitude, sz = c2.magnitude;
            if (Vector3.Dot(Vector3.Cross(c0, c1), c2) < 0f) sx = -sx;
            scale = new Vector3(sx, sy, sz);
            rotation = sy > 1e-12f && sz > 1e-12f
                ? Quaternion.LookRotation(c2 / sz, c1 / sy)
                : Quaternion.identity;
        }

        static void AddGeometry(GameObject go, Mesh mesh, Material[] materials, ShadowCastingMode shadows, bool receiveShadows, bool collide)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = shadows;
            r.receiveShadows = receiveShadows;
            if (collide)
            {
                var col = go.AddComponent<MeshCollider>();
                col.convex = false;
                col.sharedMesh = mesh;
            }
            go.AddComponent<Sliceable>();
        }

        Transform EnsureTemplateHolder()
        {
            if (_templateHolder != null) return _templateHolder;
            var holder = new GameObject("PhotoTemplates");
            holder.SetActive(false); // templates never wake up
            holder.transform.SetParent(transform, false);
            _templateHolder = holder.transform;
            return _templateHolder;
        }

        Transform EnsurePlacedRoot()
        {
            if (_placedRoot != null) return _placedRoot;
            _placedRoot = new GameObject("PlacedPhotos").transform;
            return _placedRoot;
        }

        Camera EnsurePreviewCamera()
        {
            if (_previewCamera != null) return _previewCamera;
            var go = new GameObject("PhotoPreviewCamera");
            go.transform.SetParent(transform, false);
            _previewCamera = go.AddComponent<Camera>();
            _previewCamera.enabled = false; // only renders on request
            _previewCamera.clearFlags = CameraClearFlags.Skybox;
            _previewCamera.allowMSAA = false;
            _previewCamera.allowHDR = false;
            return _previewCamera;
        }

        struct PendingPreview
        {
            public Pose Pose;
            public float FovY, Aspect;
            public Texture2D Target;
        }

        readonly List<PendingPreview> _pendingPreviews = new List<PendingPreview>();

        /// <summary>
        /// Previews captured before the render pipeline was up, still waiting for LateUpdate. Their scene
        /// (e.g. a diorama) must stay visible until this drops to zero.
        /// </summary>
        public int PendingPreviewCount => _pendingPreviews.Count;

        /// <summary>
        /// False while a scriptable pipeline is configured but has not been instantiated yet (before the
        /// first rendered frame; in batch mode the editor never renders a frame by itself). Previews are
        /// then rendered immediately (a render request instantiates the pipeline) and once more from
        /// LateUpdate when the pipeline is up.
        /// </summary>
        static bool CanRenderNow =>
            GraphicsSettings.currentRenderPipeline == null || RenderPipelineManager.currentPipeline != null;

        void LateUpdate()
        {
            if (_pendingPreviews.Count == 0 || !CanRenderNow) return;
            for (int i = 0; i < _pendingPreviews.Count; i++)
            {
                PendingPreview p = _pendingPreviews[i];
                if (p.Target != null) RenderInto(p.Target, p.Pose, p.FovY, p.Aspect);
            }
            _pendingPreviews.Clear();
        }

        /// <summary>
        /// Creates the photo's preview texture (PreviewWidth wide, height from the aspect) and renders it
        /// now, or on the next LateUpdate if the render pipeline is not up yet.
        /// </summary>
        Texture2D RenderPreview(Pose pose, float fovY, float aspect, string label)
        {
            int width = PreviewWidth;
            int height = Mathf.Clamp(Mathf.RoundToInt(width / Mathf.Max(0.05f, aspect)), 1, 2048);
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "Photo " + label,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                // No GPU (-nographics batch mode / headless server): keep a neutral placeholder.
                FillPlaceholder(tex);
            }
            else if (CanRenderNow)
            {
                RenderInto(tex, pose, fovY, aspect);
            }
            else
            {
                // The pipeline object does not exist yet (no frame rendered: batch mode, first frames of the
                // Web player). A render request instantiates it on demand, so render right away; but also
                // render again once the pipeline is confirmed up, in case this early request was not served.
                FillPlaceholder(tex);
                try
                {
                    RenderInto(tex, pose, fovY, aspect);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("ProjectionSystem: early preview render failed (" + e.Message + "); retrying later.");
                }
                _pendingPreviews.Add(new PendingPreview { Pose = pose, FovY = fovY, Aspect = aspect, Target = tex });
            }
            return tex;
        }

        static void FillPlaceholder(Texture2D tex)
        {
            var pixels = new Color32[tex.width * tex.height];
            var grey = new Color32(200, 196, 188, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = grey;
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
        }

        /// <summary>
        /// Renders the view at <paramref name="pose"/> into <paramref name="target"/>, excluding the Player
        /// and PhotoUI layers. Uses RenderPipeline.SubmitRenderRequest (StandardRequest) under URP and
        /// Camera.Render() otherwise, then reads the pixels back synchronously (fine on WebGL).
        /// </summary>
        void RenderInto(Texture2D target, Pose pose, float fovY, float aspect)
        {
            int width = target.width, height = target.height;

            Camera cam = EnsurePreviewCamera();
            cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
            cam.fieldOfView = fovY;
            cam.aspect = aspect;
            cam.nearClipPlane = HoldNear;
            cam.farClipPlane = MaxFar;
            cam.cullingMask = ~ExcludedLayerMask;

            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24)
            {
                sRGB = true,
                msaaSamples = 1,
            };
            RenderTexture rt = RenderTexture.GetTemporary(desc);
            cam.targetTexture = rt;

            var request = new RenderPipeline.StandardRequest();
            if (GraphicsSettings.currentRenderPipeline != null && RenderPipeline.SupportsRenderRequest(cam, request))
            {
                request.destination = rt;
                RenderPipeline.SubmitRenderRequest(cam, request);
            }
            else
            {
                cam.Render();
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            target.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            target.Apply(false, false);
            RenderTexture.active = previous;

            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
        }

        static void DestroyObject(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
