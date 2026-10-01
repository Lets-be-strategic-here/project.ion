using System.Collections;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels.Rooms;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;

namespace Ion.Levels
{
    // `Arch` inside namespace Ion.Levels resolves to the namespace Ion.Levels.Arch (art bible §4 name trap).
    using ArchKit = Ion.Levels.Arch.Arch;

    /// <summary>
    /// The only object in Main.unity. Builds the whole game in Awake:
    /// atmosphere → ProjectionSystem → zones (world + dioramas, each baked) → UI → player, then captures the
    /// diorama photos zone by zone (each in its own mood) a frame later and binds the HUD.
    ///
    /// Layout (art bible §7): zone i's world root is at (i·RoomSpacing, 0, 0); every photo placement aims along
    /// ±Z, so a photo's 250 m view cone never reaches the neighbouring zones on X. Dioramas sit at
    /// (i·DioramaSpacing, DioramaY, 0) and are deactivated once captured.
    ///
    /// Zones: 0 T1 Ledge, 1 T2 Darkroom, 2 Hub (light table), 3 Stairs wing, 4 Camera wing, 5 Gallery.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public const float RoomSpacing = 50f;
        public const float DioramaY = -1000f;
        public const float DioramaSpacing = 200f;
        /// <summary>Last resort: below this (and above the dioramas) the player is put back at the zone's spawn.</summary>
        public const float ResetY = -50f;
        /// <summary>Seconds a zone's mood takes to blend in on arrival (art bible §3.4).</summary>
        public const float MoodBlendSeconds = 2f;

        public static GameBootstrap Instance { get; private set; }

        readonly List<Room> _rooms = new List<Room>();
        readonly List<RoomContext> _contexts = new List<RoomContext>();
        FirstPersonController _player;
        int _currentRoom;
        Transform _worldContainer;
        Transform _dioramaContainer;
        bool _photosReady;
        WorldHistory _history;

        public IReadOnlyList<RoomContext> Rooms => _contexts;

        /// <summary>The zone the player is in (index into <see cref="Rooms"/>).</summary>
        public int CurrentRoom => _currentRoom;

        /// <summary>Same as <see cref="CurrentRoom"/> (bible wording).</summary>
        public int CurrentZone => _currentRoom;

        public FirstPersonController Player => _player;

        /// <summary>True once every diorama photo is captured and the dioramas are hidden.</summary>
        public bool PhotosReady => _photosReady;

        /// <summary>Camera height above the feet; diorama shots are taken from this height (art bible §7.5.1).</summary>
        public float EyeHeight => PlayerFactory.EyeHeight;

        /// <summary>Raised when the current zone changes (index).</summary>
        public event System.Action<int> ZoneChanged;

        internal Transform DioramaContainer
        {
            get
            {
                if (_dioramaContainer == null)
                {
                    _dioramaContainer = new GameObject("Dioramas").transform;
                    _dioramaContainer.position = new Vector3(0f, DioramaY, 0f);
                }
                return _dioramaContainer;
            }
        }

        /// <summary>True while <see cref="Restart"/> is undoing the run (effects stay quiet).</summary>
        public static bool Restarting { get; private set; }

        /// <summary>Raised after <see cref="Restart"/> has reset the game.</summary>
        public static event System.Action Restarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Restarting = false;
            Restarted = null;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameBootstrap] Duplicate bootstrap destroyed.");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 1. Sun, sky, ambient, fog.
            Atmosphere.Apply();

            // 2. Projection system.
            if (Object.FindFirstObjectByType<ProjectionSystem>() == null)
                new GameObject("ProjectionSystem").AddComponent<ProjectionSystem>();

            // 3. Zones (order = index = position on the X line).
            _rooms.Add(new TutorialLedge());
            _rooms.Add(new TutorialDarkroom());
            _rooms.Add(new HubLightTable());
            _rooms.Add(new StairsWing());
            _rooms.Add(new CameraWing());
            _rooms.Add(new GalleryEnding());
            BuildRooms();

            // The rewind system's zone hooks (Lead D): which zone a point is in, and where its void starts.
            ZoneInfo.ZoneOfProvider = ZoneAt;
            ZoneInfo.VoidYProvider = VoidYOf;

            // 3b. Distant scenery around the zones (not sliceable; drawn shifted for diorama captures).
            float middle = (_rooms.Count - 1) * RoomSpacing * 0.5f;
            Backdrop.Build(new Vector3(middle, 0f, -5f));
            Backdrop.SetDioramaMapping(DioramaY * 0.5f, DioramaSpacing, DioramaSpacing - RoomSpacing, DioramaY);

            // 4. UI.
            UIFactory.Create();
            // The Ultra tier's effects (self-installs at startup; a scene reload, e.g. in tests, needs it again).
            if (Ion.Presentation.Quality.UltraFx.Instance == null)
                new GameObject("Ion UltraFx").AddComponent<Ion.Presentation.Quality.UltraFx>();

            // 5. Player at T1.
            _currentRoom = 0;
            Pose spawn = _contexts[0].Spawn;
            _player = PlayerFactory.Create(spawn.position, spawn.rotation.eulerAngles.y);
            _player.SetCheckpoint(spawn.position, spawn.rotation.eulerAngles.y);
            _player.KillY = ResetY; // last resort (the SafePoseTracker recovers falls long before this)
            _history = WorldHistory.Instance; // created on demand; the first checkpoint is the T1 spawn
            if (_history != null) _history.CheckpointRestored += OnCheckpointRestored;
            Atmosphere.ApplyMood(_rooms[0].Mood);

            // 6. Photos (next frame, zone by zone) + HUD binding.
            StartCoroutine(CaptureDioramas());
            QualityTier.Changed += OnQualityChanged;
        }

        // Ultra-only detail batches (Palette.GetUltra) are skipped by the culling below on the other tiers.
        void OnQualityChanged()
        {
            if (_photosReady) ApplyZoneCulling();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            QualityTier.Changed -= OnQualityChanged;
            if (_history != null) _history.CheckpointRestored -= OnCheckpointRestored;
            if (ZoneInfo.ZoneOfProvider == (System.Func<Vector3, int>)ZoneAt) ZoneInfo.ZoneOfProvider = null;
            if (ZoneInfo.VoidYProvider == (System.Func<int, float>)VoidYOf) ZoneInfo.VoidYProvider = null;
        }

        void BuildRooms()
        {
            int draws = 0;
            _worldContainer = new GameObject("World").transform;
            for (int i = 0; i < _rooms.Count; i++)
            {
                Room room = _rooms[i];
                var root = new GameObject("Zone " + i + " (" + room.Title + ")").transform;
                root.SetParent(_worldContainer, false);
                root.localPosition = new Vector3(i * RoomSpacing, 0f, 0f);

                var ctx = new RoomContext(this, i, room, root, new Vector3(i * DioramaSpacing, DioramaY, 0f));
                _contexts.Add(ctx);
                try
                {
                    ArchKit.Style = room.Style;
                    room.Build(root, ctx);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }
                try
                {
                    // One merged, chunked Sliceable per material for the zone and for its diorama (art bible §4).
                    draws += ArchKit.Bake(root, room.BakeChunk, room.BakeSoftChunk);
                    if (ctx.HasDiorama) draws += ArchKit.Bake(ctx.DioramaRoot, room.BakeChunk, room.BakeSoftChunk);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }
            }
            ArchKit.Style = Ion.Levels.Arch.ArchStyle.LightTable;
            Debug.Log("[Ion] Zones built and baked into " + draws + " draw objects.");
        }

        IEnumerator CaptureDioramas()
        {
            // Let one frame pass so renderers have bounds and colliders/transforms are synced.
            yield return null;
            Physics.SyncTransforms();

            var ps = ProjectionSystem.Instance;
            int budget = 900; // frames we are willing to wait for deferred previews, in total
            for (int r = 0; r < _contexts.Count; r++)
            {
                var shots = _contexts[r].Shots;
                if (shots.Count == 0) continue;
                // Binding (§3.4): every photo is exposed in its zone's light.
                Atmosphere.ApplyMood(_rooms[r].Mood);
                for (int s = 0; s < shots.Count; s++)
                {
                    DioramaShot shot = shots[s];
                    PhotoData photo = null;
                    if (ps != null)
                    {
                        try
                        {
                            photo = ps.Capture(shot.WorldPose(EyeHeight), shot.FovY, shot.Aspect, shot.Label);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                    if (photo == null) Debug.LogError("[GameBootstrap] Capture failed for '" + shot.Label + "'.");
                    shot.Resolve(photo);
                }
                // A preview deferred to a later LateUpdate (render pipeline not up yet) must still render in this
                // zone's mood, so wait for it before switching the light.
                while (ps != null && ps.PendingPreviewCount > 0 && budget > 0)
                {
                    budget--;
                    yield return null;
                }
            }
            Atmosphere.ApplyMood(_rooms[_currentRoom].Mood);

            var hud = Hud.Instance != null ? Hud.Instance : Object.FindFirstObjectByType<Hud>();
            if (hud != null && _player != null)
                hud.BindInventory(_player.GetComponent<PhotoInventory>());

            AnnounceRoom(_currentRoom);

            // Exercise the placement path once now, so the first real click is not the slow one.
            if (ps != null) ps.WarmUp();

            yield return null;
            for (int i = 0; i < 600 && ps != null && ps.PendingPreviewCount > 0; i++)
                yield return null;
            if (ps != null && ps.PendingPreviewCount > 0)
                Debug.LogWarning("[GameBootstrap] Photo previews still pending; hiding the dioramas anyway.");
            if (_dioramaContainer != null) _dioramaContainer.gameObject.SetActive(false);
            _photosReady = true;
            ApplyZoneCulling();
        }

        // ------------------------------------------------------------------ zones

        /// <summary>Index of the first zone of type <typeparamref name="T"/>, or -1.</summary>
        public int IndexOf<T>() where T : Room
        {
            for (int i = 0; i < _rooms.Count; i++)
                if (_rooms[i] is T) return i;
            return -1;
        }

        /// <summary>Index of the zone whose <see cref="Room.Key"/> is <paramref name="key"/>, or -1.</summary>
        public int IndexOfKey(string key)
        {
            for (int i = 0; i < _rooms.Count; i++)
                if (string.Equals(_rooms[i].Key, key, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The zone context of type <typeparamref name="T"/> (null if absent).</summary>
        public RoomContext Context<T>() where T : Room
        {
            int i = IndexOf<T>();
            return i >= 0 ? _contexts[i] : null;
        }

        /// <summary>Zone index of a world position (nearest zone root on the X line; dioramas map to the current zone).</summary>
        public int ZoneAt(Vector3 world)
        {
            if (_contexts.Count == 0) return 0;
            if (world.y < DioramaY * 0.5f) return _currentRoom;
            return Mathf.Clamp(Mathf.RoundToInt(world.x / RoomSpacing), 0, _contexts.Count - 1);
        }

        /// <summary>World y of a zone's void (its lowest floor − 6; art bible §11.2).</summary>
        public float VoidYOf(int zone)
        {
            if (zone < 0 || zone >= _rooms.Count) return ZoneInfo.DefaultVoidY;
            return _contexts[zone].WorldRoot.position.y + _rooms[zone].VoidY;
        }

        /// <summary>Moves the player to zone <paramref name="index"/>'s spawn (no-op past the last zone).</summary>
        public void GoToRoom(int index)
        {
            if (index < 0 || index >= _contexts.Count) return;
            RoomContext ctx = _contexts[index];
            Pose spawn = ctx.Spawn;
            GoToZoneWorld(index, spawn.position, spawn.rotation.eulerAngles.y);
        }

        /// <summary>
        /// Moves the player into zone <paramref name="index"/> at a room-local feet position and yaw: current zone,
        /// mood blend, a zone-entry checkpoint (art bible §11.2) and the arrival toast. Used by teleporters.
        /// </summary>
        public void GoToZone(int index, Vector3 localFeet, float localYaw)
        {
            if (index < 0 || index >= _contexts.Count) return;
            RoomContext ctx = _contexts[index];
            GoToZoneWorld(index, ctx.World(localFeet), ctx.WorldYaw(localYaw));
        }

        void GoToZoneWorld(int index, Vector3 feet, float yaw)
        {
            if (_player == null) return;
            SetCurrentZone(index);
            _player.Teleport(feet, yaw);
            _player.SetCheckpoint(feet, yaw);
            Physics.SyncTransforms();
            var history = WorldHistory.Instance;
            if (history != null && !Restarting)
                history.SetCheckpoint("zone." + _rooms[index].Key, new PlayerPose(feet, yaw, 0f, index));
            try { _rooms[index].OnEnter(_contexts[index]); }
            catch (System.Exception e) { Debug.LogException(e); }
            AnnounceRoom(index);
        }

        /// <summary>Makes <paramref name="index"/> the current zone and blends its mood in (no teleport).</summary>
        public void SetCurrentZone(int index)
        {
            if (index < 0 || index >= _contexts.Count || index == _currentRoom) return;
            _currentRoom = index;
            if (_photosReady)
            {
                Atmosphere.BlendTo(_rooms[index].Mood, MoodBlendSeconds);
                ApplyZoneCulling();
            }
            try { ZoneChanged?.Invoke(index); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// Draw-call budget across zones (they sit 50 m apart on one line, so a view down the line sees them all):
        /// the current zone draws everything, its neighbours draw only their baked architecture (direct children
        /// with a collider: the silhouettes), farther zones draw nothing. Uses Renderer.forceRenderingOff, not
        /// enabled, so gameplay code that shows / hides renderers is unaffected; colliders are untouched.
        /// </summary>
        void ApplyZoneCulling()
        {
            bool ultra = QualityTier.IsUltra;
            for (int i = 0; i < _contexts.Count; i++)
            {
                Transform root = _contexts[i].WorldRoot;
                if (root == null) continue;
                int d = Mathf.Abs(i - _currentRoom);
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    bool show = d == 0 || (d == 1 && r.transform.parent == root && r.GetComponent<Collider>() != null);
                    if (show && !ultra && Palette.IsUltra(r.sharedMaterial)) show = false;   // no draw call off Ultra
                    r.forceRenderingOff = !show;
                }
            }
        }

        void OnCheckpointRestored(Checkpoint cp)
        {
            if (cp != null) SetCurrentZone(Mathf.Clamp(cp.Zone, 0, _contexts.Count - 1));
        }

        /// <summary>
        /// "Play again": undoes every world change (WorldHistory.UnwindAll), clears switches, empties the
        /// inventory, puts the photos back, re-locks the instant camera, resets one-way story beats and exhibits,
        /// re-arms hints and returns to T1. A soft reset (no scene reload).
        /// </summary>
        public void Restart()
        {
            if (_player == null) return;
            Restarting = true;
            try
            {
                var history = WorldHistory.Instance;
                if (history != null) history.UnwindAll();
                var ps = ProjectionSystem.Instance;
                if (ps != null && ps.CanRewind)
                    Debug.LogWarning("[GameBootstrap] Placements remain after UnwindAll (" + ps.PlacementCount + ").");
                SwitchBoard.Restore(new Dictionary<string, bool>());

                var holder = _player.GetComponent<PhotoHolder>();
                if (holder != null) holder.ResetForRestart();
                var inventory = _player.GetComponent<PhotoInventory>();
                if (inventory != null) inventory.Clear();
                var cam = _player.GetComponent<InstantCamera>();
                if (cam != null)
                {
                    cam.SetCameraMode(false);
                    cam.Unlocked = false;
                    cam.Film = 0;
                }

                foreach (var p in Object.FindObjectsByType<PhotoPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (p.GetComponentInParent<ProjectionSystem>(true) == null) p.ResetPickup();
                foreach (var c in Object.FindObjectsByType<Ion.Gameplay.CameraPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (c.GetComponentInParent<ProjectionSystem>(true) == null) c.ResetPickup();
                foreach (var h in Object.FindObjectsByType<ZoneHint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    h.ResetZone();
                for (int i = 0; i < _rooms.Count; i++)
                {
                    try { _rooms[i].OnRestart(_contexts[i]); }
                    catch (System.Exception e) { Debug.LogException(e); }
                }

                _player.InputEnabled = true;
                if (Hud.Instance != null) Hud.Instance.ClearToast();
                GoToRoom(0);
                if (history != null)
                {
                    history.ClearAll();
                    Pose spawn = _contexts[0].Spawn;
                    history.SetCheckpoint("zone." + _rooms[0].Key,
                                          new PlayerPose(spawn.position, spawn.rotation.eulerAngles.y, 0f, 0));
                }
            }
            finally
            {
                Restarting = false;
            }
            Restarted?.Invoke();
        }

        void AnnounceRoom(int index)
        {
            if (index == 0 && (Onboarding.IsGuiding || Restarting)) return;
            if (!_photosReady && index != 0) return;
            Room room = _contexts[index].Room;
            string text = string.IsNullOrEmpty(room.Intro) ? room.Title : room.Title + "  -  " + room.Intro;
            RoomContext.Toast(text, 4f);
        }

        void Update()
        {
            if (_player == null) return;
            Vector3 p = _player.transform.position;
            if (p.y < ResetY - 10f && p.y > -900f)
            {
                // Fallback (the SafePoseTracker / controller recover long before this).
                Pose spawn = _contexts[_currentRoom].Spawn;
                _player.Teleport(spawn.position, spawn.rotation.eulerAngles.y);
                RoomContext.Toast("Whoops!", 2f);
                return;
            }
            // Keep the current zone in step with where the player actually is (debug teleports, rewinds).
            if (_photosReady && _player.IsGrounded)
            {
                int zone = ZoneAt(p);
                if (zone != _currentRoom) SetCurrentZone(zone);
            }
        }
    }
}
