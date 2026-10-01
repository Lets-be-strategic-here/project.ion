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
        /// <summary>Default preview width (pre-made photos). Snapshots pass a larger width (they fill more of the screen).</summary>
        public const int PreviewWidth = 768;

        /// <summary>Preview width used when a capture names none (the Ultra tier raises it; see UltraFx).</summary>
        public static int DefaultPreviewWidth = PreviewWidth;
        /// <summary>
        /// Far plane of the preview render. Only cuts and captures stop at <see cref="MaxFar"/>; the photo
        /// image also shows what lies beyond (the distant backdrop), like the player's own view.
        /// </summary>
        public const float PreviewFar = 1200f;

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

        /// <summary>
        /// Collision cooking for pasted / cut pieces: no "faster simulation" pre-processing (pieces are
        /// static, only the character controller sweeps against them), which roughly halves the cooking
        /// time that dominates a placement on slow CPUs. Photo pieces are baked with the same options
        /// when the photo is captured, so pasting them assigns pre-cooked data.
        /// </summary>
        public const MeshColliderCookingOptions CookingOptions =
            MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices |
            MeshColliderCookingOptions.UseFastMidphase;

        /// <summary>Pieces whose world-space bounds diagonal is below this get no collider (crumbs, decor).</summary>
        internal const float MinColliderDiagonal = 0.25f;

        /// <summary>
        /// Cut pieces farther than this from the viewer's eye get their collider on the next frame
        /// (play mode only), spreading the cooking cost of a placement over two frames. Everything the
        /// player stands on (eye height 1.62 m) or can reach within a frame (~0.1 m) is well inside it.
        /// </summary>
        public const float ImmediateColliderRadius = 3.5f;

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
        readonly List<GameObject> _deferredColliders = new List<GameObject>(64);
        readonly List<Renderer> _lastPasted = new List<Renderer>(64);
        int _deferredFrame;
        readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();

        /// <summary>Renderers of the photo pieces pasted by the most recent placement (presentation effects).</summary>
        public IReadOnlyList<Renderer> LastPastedRenderers => _lastPasted;

        /// <summary>Cut pieces still waiting for their (deferred) collider.</summary>
        public int DeferredColliderCount => _deferredColliders.Count;

        /// <summary>Timing breakdown of the most recent placement (debug harness).</summary>
        public string LastPlaceProfile { get; private set; } = string.Empty;
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
        public PhotoData Capture(Pose pose, float fovY, float aspect, string label, int previewWidth = 0)
        {
            if (previewWidth <= 0) previewWidth = DefaultPreviewWidth;
            FlushPendingCuts();
            var frustum = new PhotoFrustum { Pose = pose, FovY = fovY, Aspect = aspect, Near = HoldNear, Far = MaxFar };
            frustum.GetPlanes(_planes);

            FlushDeferredColliders();
            var photo = new PhotoData { FovY = fovY, Aspect = aspect, Label = label };
            Matrix4x4 captureInverse = Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one).inverse;

            List<Sliceable> sliceables = GatherSliceables();
            for (int i = 0; i < sliceables.Count; i++)
            {
                Sliceable s = sliceables[i];
                if (!TryGetCuttable(s, out MeshFilter mf, out MeshRenderer mr)) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, mr.bounds)) continue;
                if (IsInsideInteractable(s)) continue;

                Matrix4x4 localToWorld = mf.transform.localToWorldMatrix;
                Mesh source = mf.sharedMesh;
                PieceElements insideElements = null;
                Mesh inside;
                // Merged meshes (Arch.Bake chunks, decor) are clipped element by element: each element is a closed
                // convex piece, so its cap is convex; clipping the whole mesh could join the cut outlines of touching
                // elements (a wall and its lintel) into one non-convex cap.
                if (s.TryGetComponent(out MeshElements elements) && elements.Mesh == source && elements.Count > 1 &&
                    source.subMeshCount == 1 && elements.EnsureData())
                    inside = ClipInsideByElements(elements, localToWorld, pose.position, out insideElements);
                else
                    inside = MeshClipper.ClipInside(source, localToWorld, _planes);
                if (inside == null) continue;
                if (IsSliver(inside, localToWorld)) { DestroyObject(inside); continue; }

                bool collide = mf.TryGetComponent(out Collider _) && WantsCollider(inside, localToWorld);
                photo.Pieces.Add(new PhotoPiece
                {
                    Name = mf.gameObject.name,
                    Mesh = inside,
                    Materials = mr.sharedMaterials,
                    Relative = captureInverse * localToWorld,
                    Layer = mf.gameObject.layer,
                    ShadowCasting = mr.shadowCastingMode,
                    ReceiveShadows = mr.receiveShadows,
                    Collide = collide,
                    Elements = insideElements,
                });
            }
            sliceables.Clear();

            // Fewer, bigger pieces: one pasted object per material instead of one per captured object.
            MergePhotoPieces(photo);
            // Cook the collision data now (load time / behind the shutter) so pasting is cheap.
            for (int i = 0; i < photo.Pieces.Count; i++)
                if (photo.Pieces[i].Collide) Physics.BakeMesh(photo.Pieces[i].Mesh.GetEntityId(), false, CookingOptions);

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

            photo.Preview = RenderPreview(pose, fovY, aspect, label, previewWidth);
            return photo;
        }

        // ------------------------------------------------------------------ place / rewind

        /// <summary>
        /// Cuts the world outside-of-frustum (removing everything inside the viewer's photo frustum)
        /// and pastes the photo's contents, then pushes an undo record.
        /// </summary>
        public void Place(PhotoData photo, Camera viewer, float rollDegrees)
        {
            CommitStagedPlace();
            PlaceCore(photo, viewer, rollDegrees, true, false);
        }

        // ------------------------------------------------------------------ staged placement (the LMB path)

        bool _staged;

        /// <summary>
        /// Per-frame budget (ms, as measured in the player) of a staged placement's cuts. The press-in holds the view
        /// still with the photo covering exactly the frustum, so the world swap is spread over those frames instead of
        /// landing in one (WebGL is single-threaded: one-frame placements were 2-3x over the 25 ms hitch budget at a
        /// 6x CPU throttle).
        /// </summary>
        public const float StageBudgetMs = 6f;

        /// <summary>True between <see cref="BeginStagedPlace"/> and its commit / cancel.</summary>
        public bool IsStaging => _staged;

        /// <summary>True while a staged placement still has cuts to make.</summary>
        public bool StagingBusy => _staged && (_pendingCuts.Count > 0 || _pendingPastes.Count > 0 || _deferredColliders.Count > 0 || _handover.Count > 0);

        // While staging, a cut keeps its original's collider on and cooks the cut piece's collider in a later frame
        // (cutting and cooking the floor under the player in one frame was the largest single hitch); the originals
        // listed here are switched off once every staged collider is cooked.
        readonly List<Collider> _handover = new List<Collider>(16);
        bool _stagingNow;

        /// <summary>Longest per-frame placement work (ms) of the latest staged placement, and its frame count (debug / perf).</summary>
        public float LastStageMaxFrameMs { get; private set; }
        public int LastStageFrames { get; private set; }

        void ReleaseHandover()
        {
            for (int i = 0; i < _handover.Count; i++)
                if (_handover[i] != null) _handover[i].enabled = false;
            _handover.Clear();
        }

        /// <summary>
        /// Starts a placement whose cuts run over the next frames (<see cref="StageBudgetMs"/> each, nearest first) while
        /// the caller keeps the raised photo over the frustum. Pastes and pushes the undo record at once (so the result is
        /// that of <see cref="Place"/>), but raises <see cref="Placed"/> only at <see cref="CommitStagedPlace"/>.
        /// <see cref="CancelStagedPlace"/> undoes it silently. False if nothing could be placed.
        /// </summary>
        public bool BeginStagedPlace(PhotoData photo, Camera viewer, float rollDegrees)
        {
            CommitStagedPlace();
            int before = _undo.Count;
            PlaceCore(photo, viewer, rollDegrees, false, Application.isPlaying);
            if (_undo.Count <= before) return false;
            _staged = true;
            LastStageMaxFrameMs = 0f;
            LastStageFrames = 0;
            return true;
        }

        /// <summary>Ends a staged placement (the world swap the player sees). Cuts still pending finish as deferred cuts.</summary>
        public void CommitStagedPlace()
        {
            if (!_staged) return;
            if (_handover.Count > 0)
            {
                // Capped press (a very slow frame budget): finish the world swap now.
                _stagingNow = true;
                FlushPendingCuts();
                _stagingNow = false;
                FlushDeferredColliders();
                ReleaseHandover();
            }
            _staged = false;
            Placed?.Invoke();
        }

        /// <summary>Undoes a staged placement without events (the press was cancelled: R, lowering, restart).</summary>
        public void CancelStagedPlace()
        {
            if (!_staged) return;
            _staged = false;
            _handover.Clear();   // the record re-enables nothing it did not switch off; these never went off
            RewindCore(false);
        }

        void PlaceCore(PhotoData photo, Camera viewer, float rollDegrees, bool raiseEvents, bool stageAll)
        {
            if (photo == null || viewer == null) return;
            FlushPendingCuts();
            FlushDeferredColliders();
            _watch.Restart();
            int cooked = 0, deferred = 0, pendingAdded = 0;
            Vector3 eye = viewer.transform.position;
            bool canDefer = Application.isPlaying;

            PhotoFrustum frustum = PhotoFrustum.FromCamera(viewer, photo.FovY, photo.Aspect, rollDegrees, HoldNear, MaxFar);
            frustum.GetPlanes(_planes);
            var record = new PlacementRecord();

            // 1. Replace every cut Sliceable by its outside pieces (merged into one object, or two when
            //    the far pieces' collider can wait a frame).
            List<Sliceable> sliceables = GatherSliceables();
            double findMs = _watch.Elapsed.TotalMilliseconds, clipMs = 0, colliderMs = 0;
            int clipped = 0;
            double slowMs = 0;
            string slowName = null;
            bool deferFar = canDefer && DeferFarCuts;
            for (int i = 0; i < sliceables.Count; i++)
            {
                Sliceable s = sliceables[i];
                if (!TryGetCuttable(s, out MeshFilter mf, out MeshRenderer mr)) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, mr.bounds)) continue;
                if (IsInsideInteractable(s)) continue;
                // Far pieces are cut over the next frames (a few ms per frame), under the placement's develop
                // flash: the click itself only pays for what is near the player (what they stand on and touch).
                float sqrDist = mr.bounds.SqrDistance(eye);
                if (stageAll || (deferFar && sqrDist > DeferCutRadius * DeferCutRadius))
                {
                    _pendingCuts.Add(new PendingCut { Target = s, Record = record, SqrDistance = sqrDist });
                    pendingAdded++;
                    continue;
                }
                CutOne(s, mf, mr, record, eye, canDefer, ref cooked, ref deferred, ref colliderMs, ref clipMs, ref clipped, ref slowMs, ref slowName);
            }
            if (pendingAdded > 0)
            {
                // Nearest first: what the player stands on is done in the first staged frame.
                if (stageAll) _pendingCuts.Sort((a, b) => a.SqrDistance.CompareTo(b.SqrDistance));
                _pendingEye = eye;
                System.Array.Copy(_planes, _pendingPlanes, _planes.Length);
            }
            sliceables.Clear();
            double cutMs = _watch.Elapsed.TotalMilliseconds;

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
            _lastPasted.Clear();
            for (int i = 0; i < photo.Pieces.Count; i++)
            {
                if (stageAll)
                {
                    // Staged: spawned over the press frames too (behind the card), after the cuts.
                    _pendingPastes.Add(new PendingPaste { Piece = photo.Pieces[i], ViewerFrame = viewerFrame, Record = record });
                    continue;
                }
                PasteOne(photo.Pieces[i], viewerFrame, root, record);
            }
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
            if (deferred > 0) _deferredFrame = Time.frameCount;
            _watch.Stop();
            LastPlaceProfile = "find " + findMs.ToString("0.0") + " ms, clip " + clipMs.ToString("0.0") + " ms (" + clipped + " objects, slowest " + slowName + " " + slowMs.ToString("0.0") + " ms), cook " +
                               colliderMs.ToString("0.0") + " ms, cut total " + cutMs.ToString("0.0") + " ms, paste " +
                               (_watch.Elapsed.TotalMilliseconds - cutMs).ToString("0.0") + " ms, " +
                               photo.Pieces.Count + " pasted, cut colliders " + cooked + " now / " + deferred + " next frame, " +
                               pendingAdded + (stageAll ? " cuts staged over the press" : " far cuts over the next frames");
            if (raiseEvents) Placed?.Invoke();
        }

        void PasteOne(PhotoPiece piece, Matrix4x4 viewerFrame, Transform root, PlacementRecord record)
        {
            GameObject pasted = SpawnPhotoPiece(piece, viewerFrame, root);
            record.Spawned.Add(pasted);
            if (pasted.TryGetComponent(out MeshRenderer pr)) _lastPasted.Add(pr);
        }

        /// <summary>One Sliceable's share of a placement cut (with the frustum in _planes).</summary>
        void CutOne(Sliceable s, MeshFilter mf, MeshRenderer mr, PlacementRecord record, Vector3 eye, bool canDefer,
                    ref int cooked, ref int deferred, ref double colliderMs, ref double clipMs, ref int clipped,
                    ref double slowMs, ref string slowName)
        {
                _pieces.Clear();
                Matrix4x4 localToWorld = mf.transform.localToWorldMatrix;
                Mesh mesh = mf.sharedMesh;
                double c0 = _watch.Elapsed.TotalMilliseconds;

                // Merged meshes: only the elements that straddle the frustum are clipped.
                if (s.TryGetComponent(out MeshElements elements) && elements.Mesh == mesh &&
                    elements.Count > 0 && mesh.subMeshCount == 1 && elements.EnsureData())
                {
                    bool touched = CutByElements(s, mf, mr, elements, localToWorld, record, eye, canDefer,
                                                 ref cooked, ref deferred, ref colliderMs);
                    double tookE = _watch.Elapsed.TotalMilliseconds - c0;
                    clipMs += tookE;
                    if (tookE > slowMs) { slowMs = tookE; slowName = mf.name + "/" + elements.Count + " el"; }
                    if (touched) clipped++;
                    return;
                }

                if (CanMerge(mesh))
                {
                    bool collide = s.TryGetComponent(out Collider _);
                    _near.Reset();
                    _far.Reset();
                    _sink.Begin(_near, collide && canDefer ? _far : _near, localToWorld, eye);
                    MeshClipper.Classification merged = MeshClipper.ClipOutsideInto(mesh, localToWorld, _planes, _sink);
                    if (merged != MeshClipper.Classification.Outside)
                    {
                        clipped++;
                        Hide(s, mr, record);
                        SpawnMerged(mf, mr, _near, collide, false, record, ref cooked, ref deferred, ref colliderMs);
                        SpawnMerged(mf, mr, _far, collide, true, record, ref cooked, ref deferred, ref colliderMs);
                    }
                    double tookM = _watch.Elapsed.TotalMilliseconds - c0;
                    clipMs += tookM;
                    if (tookM > slowMs) { slowMs = tookM; slowName = mf.name + "/" + mesh.GetIndexCount(0) / 3; }
                    return;
                }

                MeshClipper.Classification result =
                    MeshClipper.ClipOutside(mesh, localToWorld, _planes, _pieces);
                double took = _watch.Elapsed.TotalMilliseconds - c0;
                clipMs += took;
                if (took > slowMs) { slowMs = took; slowName = mf.name + "/" + mesh.GetIndexCount(0) / 3; }
                if (result == MeshClipper.Classification.Outside) return;
                clipped++;

                Hide(s, mr, record);
                for (int p = 0; p < _pieces.Count; p++)
                {
                    Mesh piece = _pieces[p];
                    if (IsSliver(piece, localToWorld)) { DestroyObject(piece); continue; }
                    record.OwnedMeshes.Add(piece);
                    GameObject cut = SpawnCutPiece(mf, mr, piece, out bool wantsCollider);
                    record.Spawned.Add(cut);
                    if (!wantsCollider) continue;
                    if (canDefer && (_stagingNow || !IsNear(piece, cut.transform.localToWorldMatrix, eye)))
                    {
                        _deferredColliders.Add(cut);
                        deferred++;
                    }
                    else
                    {
                        double k0 = _watch.Elapsed.TotalMilliseconds;
                        AddCollider(cut, piece);
                        colliderMs += _watch.Elapsed.TotalMilliseconds - k0;
                        cooked++;
                    }
                }
                _pieces.Clear();
        }

        // ------------------------------------------------------------------ deferred far cuts

        struct PendingCut
        {
            public Sliceable Target;
            public PlacementRecord Record;
            public float SqrDistance;
        }

        /// <summary>
        /// Placement cuts farther than <see cref="DeferCutRadius"/> from the eye are applied over the following frames
        /// (<see cref="DeferCutBudgetMs"/> per frame) instead of in the click's frame (WebGL is single-threaded; the
        /// whole cut was 1.5–3× over the 25 ms hitch budget at 6× CPU throttle). Pending cuts are flushed before any
        /// other placement, rewind or capture, so the semantics are those of an immediate cut. Off in edit mode.
        /// </summary>
        public static bool DeferFarCuts = true;
        public const float DeferCutRadius = 9f;
        public const float DeferCutBudgetMs = 3f;

        readonly List<PendingCut> _pendingCuts = new List<PendingCut>(64);

        struct PendingPaste
        {
            public PhotoPiece Piece;
            public Matrix4x4 ViewerFrame;
            public PlacementRecord Record;
        }

        readonly List<PendingPaste> _pendingPastes = new List<PendingPaste>(64);

        // Running cost estimates (ms per mesh vertex for a cut, ms per pasted piece) so a budgeted frame does not
        // start an item it cannot finish in time (each frame still does at least one).
        double _cutMsPerVertex = 0.002, _pasteMs = 0.3;
        readonly Plane[] _pendingPlanes = new Plane[PhotoFrustum.PlaneCount];
        Vector3 _pendingEye;

        /// <summary>Far cuts of the latest placement still waiting (they finish within a few frames).</summary>
        public int PendingCutCount => _pendingCuts.Count + _pendingPastes.Count;

        /// <summary>Applies every pending far cut now (before a new placement, a rewind or a capture).</summary>
        public void FlushPendingCuts() => ProcessPendingCuts(double.MaxValue);

        void ProcessPendingCuts(double budgetMs)
        {
            if (_pendingCuts.Count == 0 && _pendingPastes.Count == 0) return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int cooked = 0, deferredCount = 0, clipped = 0;
            double colliderMs = 0, clipMs = 0, slowMs = 0;
            string slowName = null;
            int done = 0, work = 0;
            if (_pendingCuts.Count > 0) System.Array.Copy(_pendingPlanes, _planes, _planes.Length);
            while (done < _pendingCuts.Count)
            {
                PendingCut pc = _pendingCuts[done];
                Sliceable s = pc.Target;
                if (s == null || pc.Record == null || !_undo.Contains(pc.Record) || !TryGetCuttable(s, out MeshFilter mf, out MeshRenderer mr))
                {
                    done++;
                    continue;
                }
                int verts = mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
                double t0 = watch.Elapsed.TotalMilliseconds;
                if (work > 0 && t0 + verts * _cutMsPerVertex > budgetMs) break;
                done++;
                work++;
                CutOne(s, mf, mr, pc.Record, _pendingEye, true, ref cooked, ref deferredCount, ref colliderMs, ref clipMs, ref clipped,
                       ref slowMs, ref slowName);
                double took = watch.Elapsed.TotalMilliseconds - t0;
                if (verts > 0) _cutMsPerVertex = 0.7 * _cutMsPerVertex + 0.3 * (took / verts);
                if (watch.Elapsed.TotalMilliseconds >= budgetMs) break;
            }
            _pendingCuts.RemoveRange(0, done);
            if (deferredCount > 0) _deferredFrame = Time.frameCount;
            if (_pendingCuts.Count > 0) return;

            // Then the staged paste.
            done = 0;
            Transform root = _pendingPastes.Count > 0 ? EnsurePlacedRoot() : null;
            while (done < _pendingPastes.Count)
            {
                PendingPaste pp = _pendingPastes[done];
                if (pp.Record == null || !_undo.Contains(pp.Record)) { done++; continue; }
                double t0 = watch.Elapsed.TotalMilliseconds;
                if (work > 0 && t0 + _pasteMs > budgetMs) break;
                done++;
                work++;
                PasteOne(pp.Piece, pp.ViewerFrame, root, pp.Record);
                _pasteMs = 0.7 * _pasteMs + 0.3 * (watch.Elapsed.TotalMilliseconds - t0);
                if (watch.Elapsed.TotalMilliseconds >= budgetMs) break;
            }
            _pendingPastes.RemoveRange(0, done);
        }

        /// <summary>Cooks deferred cut colliders until <paramref name="budgetMs"/> is spent (at least one).</summary>
        void CookDeferredColliders(double budgetMs)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int done = 0;
            while (done < _deferredColliders.Count)
            {
                GameObject go = _deferredColliders[done++];
                if (go == null || !go.activeSelf || !go.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) continue;
                if (!go.TryGetComponent(out MeshCollider _)) AddCollider(go, mf.sharedMesh);
                if (watch.Elapsed.TotalMilliseconds >= budgetMs) break;
            }
            _deferredColliders.RemoveRange(0, done);
        }

        /// <summary>Undoes the most recent placement.</summary>
        public void Rewind()
        {
            // A placement still being staged never happened as far as the history knows: drop it first.
            CancelStagedPlace();
            RewindCore(true);
        }

        void RewindCore(bool raiseEvents)
        {
            if (_undo.Count == 0) return;
            // Far cuts still pending for the record being undone never happened: drop them; older ones finish first.
            PlacementRecord top = _undo[_undo.Count - 1];
            _pendingCuts.RemoveAll(pc => pc.Record == top);
            _pendingPastes.RemoveAll(pp => pp.Record == top);
            FlushPendingCuts();
            PlacementRecord record = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _lastPasted.Clear();

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

            if (raiseEvents) Rewound?.Invoke();
        }

        /// <summary>
        /// Runs the whole place / rewind path once on throwaway geometry 3 km below the world, silently (no
        /// events, no undo entry left). Call at load time: the first real placement then does not pay the
        /// one-time costs of the cut / merge / cook / paste code paths (~10 ms of the first click on a slow
        /// laptop in the Web player).
        /// </summary>
        public void WarmUp()
        {
            if (!Application.isPlaying) return;
            var root = new GameObject("ProjectionWarmUp").transform;
            root.position = new Vector3(0f, -3000f, 0f);
            var meshes = new List<Mesh>();
            // Grow the scratch buffers to their working size now (merged decor runs to a few thousand
            // vertices): growing the heap during the first click was a few ms of it.
            _near.Reserve(16384, 32768);
            _far.Reserve(8192, 16384);
            _photoMerger.Reserve(16384, 32768);
            try
            {
                // A plain box (whole-mesh path) and a two-element merged mesh (element path), 6 m ahead;
                // the frustum cuts through both.
                Mesh box = WarmUpBox(Vector3.zero, Vector3.one * 2f);
                meshes.Add(box);
                var boxGo = new GameObject("WarmUpBox");
                boxGo.transform.SetParent(root, false);
                boxGo.transform.localPosition = new Vector3(0.9f, 0f, 6f);
                AddGeometry(boxGo, box, new Material[0], ShadowCastingMode.Off, false);
                boxGo.AddComponent<MeshCollider>().sharedMesh = box;

                _photoMerger.Reset();
                Mesh a = WarmUpBox(new Vector3(-1.4f, 0f, 6f), Vector3.one), b = WarmUpBox(new Vector3(-6f, 0f, 6f), Vector3.one);
                meshes.Add(a);
                meshes.Add(b);
                _photoMerger.AddTransformed(a.vertices, a.normals, a.colors32, a.GetIndices(0), Matrix4x4.identity);
                _photoMerger.AddTransformed(b.vertices, b.normals, b.colors32, b.GetIndices(0), Matrix4x4.identity);
                Mesh merged = _photoMerger.Build("WarmUpMerged", out Vector3[] mp, out Vector3[] mn, out Color32[] mc, out int[] mt);
                meshes.Add(merged);
                var mergedGo = new GameObject("WarmUpMerged");
                mergedGo.transform.SetParent(root, false);
                AddGeometry(mergedGo, merged, new Material[0], ShadowCastingMode.Off, false);
                mergedGo.AddComponent<MeshCollider>().sharedMesh = merged;
                _photoMerger.ApplyTo(mergedGo.AddComponent<MeshElements>(), merged, mp, mn, mc, mt);

                // A photo with one merged piece (paste path, pre-baked collider).
                _photoMerger.Reset();
                _photoMerger.AddTransformed(a.vertices, a.normals, a.colors32, a.GetIndices(0), Matrix4x4.identity);
                Mesh pasteMesh = _photoMerger.Build("WarmUpPhoto", out Vector3[] pp, out Vector3[] pn, out Color32[] pc, out int[] pt);
                meshes.Add(pasteMesh);
                _photoMerger.GetTable(out int[] v0, out int[] vn, out int[] t0, out int[] tn, out Bounds[] eb);
                _photoMerger.Reset();
                Physics.BakeMesh(pasteMesh.GetEntityId(), false, CookingOptions);
                var photo = new PhotoData { FovY = 30f, Aspect = 1f, Label = "warm-up" };
                photo.Pieces.Add(new PhotoPiece
                {
                    Name = "WarmUp", Mesh = pasteMesh, Materials = new Material[0], Relative = Matrix4x4.identity,
                    Layer = 0, ShadowCasting = ShadowCastingMode.Off, Collide = true,
                    Elements = new PieceElements
                    {
                        VertexStart = v0, VertexCount = vn, IndexStart = t0, IndexCount = tn, Bounds = eb,
                        Positions = pp, Normals = pn, Colors = pc, Indices = pt,
                    },
                });

                var camGo = new GameObject("WarmUpViewer");
                camGo.transform.SetParent(root, false);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                int before = _undo.Count;
                PlaceCore(photo, cam, 0f, false, false);
                FlushDeferredColliders();
                if (_undo.Count > before) RewindCore(false);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("ProjectionSystem: warm-up failed (" + e.Message + ").");
            }
            finally
            {
                _lastPasted.Clear();
                LastPlaceProfile = string.Empty;
                DestroyObject(root.gameObject);
                for (int i = 0; i < meshes.Count; i++) if (meshes[i] != null) DestroyObject(meshes[i]);
            }
        }

        /// <summary>A closed box mesh with normals and white vertex colours (warm-up geometry).</summary>
        static Mesh WarmUpBox(Vector3 c, Vector3 size)
        {
            var p = new List<Vector3>(24);
            var n = new List<Vector3>(24);
            var t = new List<int>(36);
            Vector3 e = size * 0.5f;
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int a = 0; a < 3; a++)
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector3 nn = axes[a] * sgn, u = axes[(a + 1) % 3], v = axes[(a + 2) % 3];
                if (sgn < 0) (u, v) = (v, u);
                int i0 = p.Count;
                Vector3 fc = c + Vector3.Scale(nn, e);
                Vector3 du = Vector3.Scale(u, e), dv = Vector3.Scale(v, e);
                p.Add(fc - du - dv); p.Add(fc - du + dv); p.Add(fc + du + dv); p.Add(fc + du - dv);
                for (int k = 0; k < 4; k++) n.Add(nn);
                // Front face normal = cross(b - a, c - a) must be nn.
                if (Vector3.Dot(Vector3.Cross(p[i0 + 1] - p[i0], p[i0 + 2] - p[i0]), nn) > 0f) { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
                else { t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2); }
            }
            var colors = new List<Color32>(24);
            for (int i = 0; i < p.Count; i++) colors.Add(new Color32(255, 255, 255, 255));
            var m = new Mesh { name = "WarmUpBox" };
            m.SetVertices(p);
            m.SetNormals(n);
            m.SetColors(colors);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Forgets all placements without undoing them (e.g. when a level is rebuilt).</summary>
        public void ClearHistory()
        {
            _staged = false;
            _pendingCuts.Clear();
            _pendingPastes.Clear();
            _handover.Clear();
            _undo.Clear();
        }

        /// <summary>The world-space frustum a placement would use right now (for UI / debug).</summary>
        public static PhotoFrustum PlacementFrustum(PhotoData photo, Camera viewer, float rollDegrees)
        {
            return PhotoFrustum.FromCamera(viewer, photo.FovY, photo.Aspect, rollDegrees, HoldNear, MaxFar);
        }

        // ------------------------------------------------------------------ merged cuts

        readonly PieceMerger _near = new PieceMerger(), _far = new PieceMerger(), _photoMerger = new PieceMerger();
        readonly CutSink _sink = new CutSink();
        readonly List<int> _keep = new List<int>(256), _straddle = new List<int>(64);
        readonly List<Sliceable> _sliceables = new List<Sliceable>(4096);

        /// <summary>
        /// Routes the outside pieces of a cut into a merger: drops crumbs and slivers, and sends pieces
        /// beyond <see cref="ImmediateColliderRadius"/> to the "far" merger (collider next frame).
        /// </summary>
        sealed class CutSink : MeshClipper.IPieceSink
        {
            PieceMerger _near, _far;
            Matrix4x4 _localToWorld;
            Vector3 _eye;
            public bool AnyNear;

            public void Begin(PieceMerger near, PieceMerger far, Matrix4x4 localToWorld, Vector3 eye)
            {
                _near = near;
                _far = far;
                _localToWorld = localToWorld;
                _eye = eye;
                AnyNear = false;
            }

            public void AddPiece(List<Vector3> p, List<Vector3> n, List<Color32> c, List<Vector4> u, List<int> t)
            {
                int count = p.Count;
                if (count < 3 || t.Count < 3) return;
                Vector3 min = p[0], max = p[0];
                for (int i = 1; i < count; i++)
                {
                    Vector3 v = p[i];
                    if (v.x < min.x) min.x = v.x; else if (v.x > max.x) max.x = v.x;
                    if (v.y < min.y) min.y = v.y; else if (v.y > max.y) max.y = v.y;
                    if (v.z < min.z) min.z = v.z; else if (v.z > max.z) max.z = v.z;
                }
                Vector3 size = max - min;
                if (size.x < MinPieceExtent && size.y < MinPieceExtent && size.z < MinPieceExtent) return;
                if (WorldThickness(p, t, _localToWorld) < MinPieceThickness) return;
                var b = new Bounds();
                b.SetMinMax(min, max);
                bool near = WorldBounds(b, _localToWorld).SqrDistance(_eye) <= ImmediateColliderRadius * ImmediateColliderRadius;
                if (near) AnyNear = true;
                (near || _far == _near ? _near : _far).AddPiece(p, n, c, u, t, b); // UV0 copied unchanged
            }
        }

        /// <summary>Meshes the merged cut path handles: readable, one triangle submesh, with normals.</summary>
        static bool CanMerge(Mesh mesh) =>
            mesh.isReadable && mesh.subMeshCount == 1 && mesh.GetTopology(0) == MeshTopology.Triangles &&
            mesh.HasVertexAttribute(VertexAttribute.Normal);

        /// <summary>
        /// Spawns the merged pieces of <paramref name="merger"/> as one cut object (with an element table).
        /// <paramref name="far"/>: its collider is cooked next frame (play mode).
        /// </summary>
        void SpawnMerged(MeshFilter mf, MeshRenderer mr, PieceMerger merger, bool collide, bool far, PlacementRecord record,
                         ref int cooked, ref int deferred, ref double colliderMs)
        {
            if (merger.ElementCount == 0) return;
            Mesh mesh = merger.Build(mf.sharedMesh.name + " (cut)", out Vector3[] p, out Vector3[] n, out Color32[] c, out Vector4[] u, out int[] t);
            if (mesh == null) return;
            record.OwnedMeshes.Add(mesh);
            GameObject go = SpawnCutPiece(mf, mr, mesh, out bool wantsCollider);
            merger.ApplyTo(go.AddComponent<MeshElements>(), mesh, p, n, c, u, t);
            record.Spawned.Add(go);
            if (!collide || !wantsCollider) return;
            if (far || _stagingNow)
            {
                _deferredColliders.Add(go);
                deferred++;
            }
            else
            {
                double k0 = _watch.Elapsed.TotalMilliseconds;
                AddCollider(go, mesh);
                colliderMs += _watch.Elapsed.TotalMilliseconds - k0;
                cooked++;
            }
        }

        /// <summary>
        /// Cuts a merged mesh element by element: elements whose bounds miss the frustum are kept as they
        /// are (same vertex buffer, their indices copied), elements inside are dropped, and only the
        /// straddling ones go through <see cref="MeshClipper"/>, their outside pieces appended as new
        /// elements. Everything left becomes one object. Same result as clipping the whole mesh (elements
        /// are closed and independent), a fraction of the work. Returns false if the object was not touched.
        /// </summary>
        bool CutByElements(Sliceable s, MeshFilter mf, MeshRenderer mr, MeshElements el, Matrix4x4 localToWorld,
                           PlacementRecord record, Vector3 eye, bool canDefer, ref int cooked, ref int deferred, ref double colliderMs)
        {
            int planeCount = MeshClipper.PrepareLocalPlanes(localToWorld, _planes);
            _keep.Clear();
            _straddle.Clear();
            bool removed = false;
            Bounds[] bounds = el.Bounds;
            for (int i = 0; i < bounds.Length; i++)
            {
                switch (MeshClipper.ClassifyLocalBounds(bounds[i], planeCount))
                {
                    case MeshClipper.Classification.Outside: _keep.Add(i); break;
                    case MeshClipper.Classification.Inside: removed = true; break;
                    default: _straddle.Add(i); break;
                }
            }
            if (!removed && _straddle.Count == 0) return false;

            bool collide = s.TryGetComponent(out Collider _);
            Vector3[] P = el.Positions, N = el.Normals;
            Color32[] C = el.Colors;
            Vector4[] U = el.Uvs;
            int[] T = el.Indices;
            _near.BeginShared(P, N, C, U);
            _sink.Begin(_near, _near, localToWorld, eye);
            bool near = !canDefer;
            for (int k = 0; k < _keep.Count; k++)
            {
                int e = _keep[k];
                _near.AddExistingElement(T, el.VertexStart[e], el.VertexCount[e], el.IndexStart[e], el.IndexCount[e], bounds[e]);
                if (collide && !near)
                    near = WorldBounds(bounds[e], localToWorld).SqrDistance(eye) <= ImmediateColliderRadius * ImmediateColliderRadius;
            }
            bool changed = removed;
            for (int k = 0; k < _straddle.Count; k++)
            {
                int e = _straddle[k];
                MeshClipper.Classification r = MeshClipper.ClipOutsideRange(P, N, C, T, el.VertexStart[e], el.VertexCount[e],
                    el.IndexStart[e], el.IndexCount[e], planeCount, _sink, U);
                if (r == MeshClipper.Classification.Outside)
                {
                    // Conservative bounds: nothing actually inside, keep it whole.
                    _near.AddExistingElement(T, el.VertexStart[e], el.VertexCount[e], el.IndexStart[e], el.IndexCount[e], bounds[e]);
                    if (collide && !near)
                        near = WorldBounds(bounds[e], localToWorld).SqrDistance(eye) <= ImmediateColliderRadius * ImmediateColliderRadius;
                }
                else
                {
                    changed = true;
                }
            }
            if (!changed)
            {
                _near.Reset();
                return false;
            }
            near |= _sink.AnyNear;

            Hide(s, mr, record);
            SpawnMerged(mf, mr, _near, collide, !near, record, ref cooked, ref deferred, ref colliderMs);
            _near.Reset();
            return true;
        }

        /// <summary>
        /// The part of a merged mesh inside the capture frustum (_planes), element by element: elements outside are
        /// skipped, elements inside are copied whole, straddling ones are clipped (convex caps). The result keeps one
        /// element per piece (<paramref name="table"/>), so the pasted copy is cut element-wise too. Null if empty.
        /// </summary>
        Mesh ClipInsideByElements(MeshElements el, Matrix4x4 localToWorld, Vector3 eye, out PieceElements table)
        {
            table = null;
            int planeCount = MeshClipper.PrepareLocalPlanes(localToWorld, _planes);
            Vector3[] P = el.Positions, N = el.Normals;
            Color32[] C = el.Colors;
            Vector4[] U = el.Uvs;
            int[] T = el.Indices;
            _near.Reset();
            _sink.Begin(_near, _near, localToWorld, eye); // drops crumbs and slivers like a cut
            Bounds[] bounds = el.Bounds;
            for (int e = 0; e < bounds.Length; e++)
            {
                MeshClipper.Classification c = MeshClipper.ClassifyLocalBounds(bounds[e], planeCount);
                if (c == MeshClipper.Classification.Outside) continue;
                int v0 = el.VertexStart[e], vn = el.VertexCount[e], t0 = el.IndexStart[e], tn = el.IndexCount[e];
                if (c == MeshClipper.Classification.Straddling)
                {
                    c = MeshClipper.ClipInsideRange(P, N, C, T, v0, vn, t0, tn, planeCount, _sink, U);
                    if (c != MeshClipper.Classification.Inside) continue; // emitted (or empty)
                }
                _near.AddTransformedRange(P, N, C, U, T, v0, vn, t0, tn, Matrix4x4.identity);
            }
            if (_near.ElementCount == 0) { _near.Reset(); return null; }
            Mesh mesh = _near.Build(el.Mesh.name + " (in)", out Vector3[] mp, out Vector3[] mn, out Color32[] mc, out int[] mt);
            if (mesh != null)
            {
                _near.GetTable(out int[] tv0, out int[] tvn, out int[] tt0, out int[] ttn, out Bounds[] tb);
                table = new PieceElements
                {
                    VertexStart = tv0, VertexCount = tvn, IndexStart = tt0, IndexCount = ttn, Bounds = tb,
                    Positions = mp, Normals = mn, Colors = mc, Indices = mt,
                };
            }
            _near.Reset();
            return mesh;
        }

        /// <summary>
        /// Merges a capture's pieces that draw alike (one material, layer, shadow settings, collider or not)
        /// into one piece each, in capture space, with an element table. Pieces with several submeshes or
        /// object-space foliage sway stay separate.
        /// </summary>
        void MergePhotoPieces(PhotoData photo)
        {
            List<PhotoPiece> pieces = photo.Pieces;
            if (pieces.Count < 2) return;
            var groups = new List<List<PhotoPiece>>();
            var result = new List<PhotoPiece>(pieces.Count);
            for (int i = 0; i < pieces.Count; i++)
            {
                PhotoPiece p = pieces[i];
                if (!Mergeable(p)) { result.Add(p); continue; }
                List<PhotoPiece> g = null;
                for (int k = 0; k < groups.Count && g == null; k++)
                    if (SameLook(groups[k][0], p)) g = groups[k];
                if (g == null) groups.Add(g = new List<PhotoPiece>(4));
                g.Add(p);
            }

            for (int k = 0; k < groups.Count; k++)
            {
                List<PhotoPiece> g = groups[k];
                if (g.Count == 1) { result.Add(g[0]); continue; }
                _photoMerger.Reset();
                for (int i = 0; i < g.Count; i++)
                {
                    Mesh m = g[i].Mesh;
                    PieceElements pe = g[i].Elements;
                    // UV0 (pattern space + code) travels unchanged: positions are transformed, UV0 never is.
                    Vector4[] uv = ReadUV0(m);
                    if (pe != null && pe.Positions != null && pe.Indices != null && pe.Positions.Length == m.vertexCount)
                    {
                        // Keep the piece's own elements (one convex element each), so pasted copies cut cleanly.
                        for (int e = 0; e < pe.VertexStart.Length; e++)
                            _photoMerger.AddTransformedRange(pe.Positions, pe.Normals, pe.Colors, uv, pe.Indices,
                                pe.VertexStart[e], pe.VertexCount[e], pe.IndexStart[e], pe.IndexCount[e], g[i].Relative);
                        continue;
                    }
                    Vector3[] n = m.normals;
                    Color32[] c = m.colors32;
                    _photoMerger.AddTransformed(m.vertices, n.Length > 0 ? n : null, c.Length > 0 ? c : null, uv,
                                                m.GetIndices(0), g[i].Relative);
                }
                Mesh merged = _photoMerger.Build("Photo " + g[0].Materials[0].name, out Vector3[] mp, out Vector3[] mn, out Color32[] mc, out int[] mt);
                if (merged == null) { result.AddRange(g); continue; }
                _photoMerger.GetTable(out int[] v0, out int[] vn, out int[] t0, out int[] tn, out Bounds[] b);
                PhotoPiece first = g[0];
                result.Add(new PhotoPiece
                {
                    Name = "Photo " + first.Materials[0].name,
                    Mesh = merged,
                    Materials = first.Materials,
                    Relative = Matrix4x4.identity,
                    Layer = first.Layer,
                    ShadowCasting = first.ShadowCasting,
                    ReceiveShadows = first.ReceiveShadows,
                    Collide = first.Collide,
                    Elements = new PieceElements
                    {
                        VertexStart = v0, VertexCount = vn, IndexStart = t0, IndexCount = tn, Bounds = b,
                        Positions = mp, Normals = mn, Colors = mc, Indices = mt,
                    },
                });
                for (int i = 0; i < g.Count; i++) DestroyObject(g[i].Mesh); // copied into the merged mesh
            }
            _photoMerger.Reset();
            pieces.Clear();
            pieces.AddRange(result);
        }

        static readonly List<Vector4> s_uv0 = new List<Vector4>(256);

        /// <summary>The mesh's UV0 as float4 (pattern space + PatternCode), or null when it has none.</summary>
        static Vector4[] ReadUV0(Mesh m)
        {
            if (!m.HasVertexAttribute(VertexAttribute.TexCoord0)) return null;
            m.GetUVs(0, s_uv0);
            Vector4[] result = s_uv0.Count == m.vertexCount ? s_uv0.ToArray() : null;
            s_uv0.Clear();
            return result;
        }

        static readonly int SwayId = Shader.PropertyToID("_Sway"), SwayFromColorId = Shader.PropertyToID("_SwayFromColor");

        static bool Mergeable(PhotoPiece p)
        {
            if (p.Mesh == null || p.Materials == null || p.Materials.Length != 1 || p.Materials[0] == null) return false;
            if (p.Mesh.subMeshCount != 1 || !p.Mesh.isReadable || p.Mesh.GetTopology(0) != MeshTopology.Triangles) return false;
            Material m = p.Materials[0];
            // Foliage that sways by object-space height would sway differently once merged into capture space.
            if (m.HasProperty(SwayId) && m.GetFloat(SwayId) > 0f && !(m.HasProperty(SwayFromColorId) && m.GetFloat(SwayFromColorId) > 0.5f))
                return false;
            return true;
        }

        static bool SameLook(PhotoPiece a, PhotoPiece b) =>
            a.Materials[0] == b.Materials[0] && a.Layer == b.Layer && a.ShadowCasting == b.ShadowCasting &&
            a.ReceiveShadows == b.ReceiveShadows && a.Collide == b.Collide;

        /// <summary>The Sliceables to consider: the live registry while playing, a scene search in edit mode.</summary>
        List<Sliceable> GatherSliceables()
        {
            if (Sliceable.RegistryActive) Sliceable.CopyLive(_sliceables);
            else
            {
                _sliceables.Clear();
                _sliceables.AddRange(FindObjectsByType<Sliceable>(FindObjectsSortMode.None));
            }
            return _sliceables;
        }

        static Bounds WorldBounds(Bounds b, Matrix4x4 localToWorld)
        {
            Vector3 c = b.center, e = b.extents;
            var result = new Bounds(localToWorld.MultiplyPoint3x4(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
                result.Encapsulate(localToWorld.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return result;
        }

        /// <summary>
        /// <see cref="WorldThickness(Mesh, Matrix4x4)"/> for one piece given as buffers (one triangle list).
        /// </summary>
        internal static float WorldThickness(List<Vector3> p, List<int> t, Matrix4x4 localToWorld)
        {
            if (p.Count == 0) return 0f;
            Vector3 r = p[0];
            double volume = 0, area = 0;
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                Vector3 a = p[t[i]] - r, b = p[t[i + 1]] - r, c = p[t[i + 2]] - r;
                volume += Vector3.Dot(a, Vector3.Cross(b, c));
                Vector3 e1 = localToWorld.MultiplyVector(b - a), e2 = localToWorld.MultiplyVector(c - a);
                area += Vector3.Cross(e1, e2).magnitude;
            }
            return ThicknessFrom(volume, area, localToWorld);
        }

        static float ThicknessFrom(double volume, double area, Matrix4x4 m)
        {
            if (area <= 1e-12) return 0f;
            double det = m.m00 * ((double)m.m11 * m.m22 - (double)m.m12 * m.m21)
                       - m.m01 * ((double)m.m10 * m.m22 - (double)m.m12 * m.m20)
                       + m.m02 * ((double)m.m10 * m.m21 - (double)m.m11 * m.m20);
            double worldVolume = System.Math.Abs(volume / 6.0 * det);
            return (float)(2.0 * worldVolume / (0.5 * area));
        }

        // ------------------------------------------------------------------ helpers

        static bool IsExcludedLayer(int layer) => (ExcludedLayerMask & (1 << layer)) != 0;

        // Explicit instead of isActiveAndEnabled, which is unreliable in edit mode (tests).
        static bool IsLive(Behaviour b) => b.enabled && b.gameObject.activeInHierarchy;

        /// <summary>
        /// Cheap checks (component cache): live, not on an excluded layer, has a mesh and an enabled
        /// renderer. The "inside an Interactable" check is separate (<see cref="IsInsideInteractable"/>),
        /// so it only runs for objects the frustum touches.
        /// </summary>
        static bool TryGetCuttable(Sliceable s, out MeshFilter mf, out MeshRenderer mr)
        {
            mf = null;
            mr = null;
            if (s == null || !IsLive(s)) return false;
            GameObject go = s.gameObject;
            if (IsExcludedLayer(go.layer)) return false;
            mf = s.CachedFilter;
            if (mf == null)
            {
                if (!go.TryGetComponent(out mf)) return false;
                s.CachedFilter = mf;
            }
            if (mf.sharedMesh == null) return false;
            mr = s.CachedRenderer;
            if (mr == null)
            {
                if (!go.TryGetComponent(out mr)) return false;
                s.CachedRenderer = mr;
            }
            return mr.enabled;
        }

        /// <summary>Interactables are captured / removed whole, never cut (cached per Sliceable).</summary>
        static bool IsInsideInteractable(Sliceable s)
        {
            if (s.InInteractable < 0) s.InInteractable = (sbyte)(s.GetComponentInParent<Interactable>(true) != null ? 1 : 0);
            return s.InInteractable > 0;
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
            return ThicknessFrom(volume, area, localToWorld);
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
                record.DisabledColliders.Add(c);
                if (_stagingNow) { _handover.Add(c); continue; }   // off once the cut piece's collider is cooked
                c.enabled = false;
            }
            _colliderScratch.Clear();
        }

        static GameObject SpawnCutPiece(MeshFilter source, MeshRenderer sourceRenderer, Mesh mesh, out bool wantsCollider)
        {
            GameObject src = source.gameObject;
            var go = new GameObject(src.name + " (cut)") { layer = src.layer };
            Transform st = src.transform, t = go.transform;
            t.SetParent(st.parent, false);
            t.localPosition = st.localPosition;
            t.localRotation = st.localRotation;
            t.localScale = st.localScale;
            AddGeometry(go, mesh, sourceRenderer.sharedMaterials, sourceRenderer.shadowCastingMode, sourceRenderer.receiveShadows);
            wantsCollider = src.TryGetComponent(out Collider _) && WantsCollider(mesh, t.localToWorldMatrix);
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
            AddGeometry(go, piece.Mesh, piece.Materials, piece.ShadowCasting, piece.ReceiveShadows);
            if (piece.Elements != null) piece.Elements.ApplyTo(go.AddComponent<MeshElements>(), piece.Mesh);
            // Pre-baked at capture (see Capture), so this does not cook.
            if (piece.Collide) AddCollider(go, piece.Mesh);
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

        static void AddGeometry(GameObject go, Mesh mesh, Material[] materials, ShadowCastingMode shadows, bool receiveShadows)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = shadows;
            r.receiveShadows = receiveShadows;
            go.AddComponent<Sliceable>();
        }

        static void AddCollider(GameObject go, Mesh mesh)
        {
            var col = go.AddComponent<MeshCollider>();
            col.convex = false;
            col.cookingOptions = CookingOptions; // before sharedMesh, which cooks (or picks up baked data)
            col.sharedMesh = mesh;
        }

        /// <summary>World-space AABB of a mesh under a matrix (8 transformed corners).</summary>
        static Bounds WorldBounds(Mesh mesh, Matrix4x4 localToWorld) => WorldBounds(mesh.bounds, localToWorld);

        /// <summary>False for crumbs too small to matter for walking (no collider is cooked for them).</summary>
        internal static bool WantsCollider(Mesh mesh, Matrix4x4 localToWorld)
        {
            return WorldBounds(mesh, localToWorld).size.sqrMagnitude >= MinColliderDiagonal * MinColliderDiagonal;
        }

        static bool IsNear(Mesh mesh, Matrix4x4 localToWorld, Vector3 eye)
        {
            Bounds b = WorldBounds(mesh, localToWorld);
            return b.SqrDistance(eye) <= ImmediateColliderRadius * ImmediateColliderRadius;
        }

        void Update()
        {
            if (_staged)
            {
                // The press: cuts, then the paste, then far colliders, all inside one frame budget.
                var watch = System.Diagnostics.Stopwatch.StartNew();
                _stagingNow = true;
                if (_pendingCuts.Count > 0 || _pendingPastes.Count > 0) ProcessPendingCuts(StageBudgetMs);
                _stagingNow = false;
                double left = StageBudgetMs - watch.Elapsed.TotalMilliseconds;
                if (_deferredColliders.Count > 0 && Time.frameCount > _deferredFrame && left > 1.0) CookDeferredColliders(left);
                if (_pendingCuts.Count == 0 && _deferredColliders.Count == 0 && _handover.Count > 0) ReleaseHandover();
                LastStageFrames++;
                LastStageMaxFrameMs = Mathf.Max(LastStageMaxFrameMs, (float)watch.Elapsed.TotalMilliseconds);
                return;
            }
            if (_pendingCuts.Count > 0 || _pendingPastes.Count > 0) ProcessPendingCuts(DeferCutBudgetMs);
            if (_deferredColliders.Count > 0 && Time.frameCount > _deferredFrame) FlushDeferredColliders();
        }

        /// <summary>Cooks the colliders deferred by the last placement now.</summary>
        public void FlushDeferredColliders()
        {
            for (int i = 0; i < _deferredColliders.Count; i++)
            {
                GameObject go = _deferredColliders[i];
                if (go == null || !go.activeSelf || !go.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) continue;
                if (!go.TryGetComponent(out MeshCollider _)) AddCollider(go, mf.sharedMesh);
            }
            _deferredColliders.Clear();
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
        Texture2D RenderPreview(Pose pose, float fovY, float aspect, string label, int width)
        {
            width = Mathf.Clamp(width, 16, 2048);
            int height = Mathf.Clamp(Mathf.RoundToInt(width / Mathf.Max(0.05f, aspect)), 1, 2048);
            // Mipmapped: the same texture is drawn full size (raised) and as an 84 px inventory thumbnail.
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true, false)
            {
                name = "Photo " + label,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 1,
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
            tex.Apply(true, false);
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
            cam.farClipPlane = PreviewFar;
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
            target.Apply(true, false);
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
