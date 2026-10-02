using System;
using System.Collections.Generic;
using Ion.Projection;
using UnityEngine;

namespace Ion.Gameplay.State
{
    /// <summary>
    /// The rewind stack (art bible §11): every undoable world change (photo placement, camera shot, switch
    /// press, pickup) is pushed here by the code that makes it, and checkpoints snapshot the rest of the
    /// state (inventory, film, camera unlock, switch channels, pose).
    ///
    /// <see cref="RewindOnce"/> and <see cref="RewindToCheckpoint"/> are synchronous and instant: the
    /// presentation (fades, iris, timing, the double tap) lives in <see cref="RewindController"/>, which
    /// calls them at the swap moment. Tests and the debug harness may call them directly.
    /// Only this class calls <see cref="ProjectionSystem.Rewind"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldHistory : MonoBehaviour
    {
        static WorldHistory s_Instance;
        static bool s_Quitting;

        readonly List<WorldChange> _changes = new List<WorldChange>(32);
        readonly HashSet<string> _visitedMarkers = new HashSet<string>();
        Checkpoint _checkpoint;
        FirstPersonController _subscribedPlayer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Instance = null;
            s_Quitting = false;
            IsUndoing = false;
            Application.quitting -= OnQuit;
            Application.quitting += OnQuit;
        }

        static void OnQuit() => s_Quitting = true;

        /// <summary>The scene's history (created on demand while playing).</summary>
        public static WorldHistory Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = FindFirstObjectByType<WorldHistory>();
                    if (s_Instance == null && Application.isPlaying && !s_Quitting)
                        s_Instance = new GameObject("WorldHistory").AddComponent<WorldHistory>();
                }
                return s_Instance;
            }
        }

        /// <summary>True while records are being undone (movers then run at rewind speed).</summary>
        public static bool IsUndoing { get; private set; }

        public int Depth => _changes.Count;
        public IReadOnlyList<WorldChange> Changes => _changes;
        public Checkpoint LastCheckpoint => _checkpoint;
        /// <summary>Something above the checkpoint floor can be undone.</summary>
        public bool CanUndo => _changes.Count > (_checkpoint != null ? _checkpoint.HistoryDepth : 0);

        /// <summary>Raised after every rewind (also Nothing, so audio / HUD can give gentle feedback).</summary>
        public event Action<RewindResult> Rewound;
        public event Action<Checkpoint> CheckpointReached, CheckpointRestored;
        /// <summary>The player was recovered from a fall (story beats hang off this: T1 powers its button).</summary>
        public event Action FallRecovered;
        /// <summary>A change was pushed (HUD [R] chip, tutorial).</summary>
        public event Action<WorldChange> Pushed;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Debug.LogWarning("[WorldHistory] a second instance was created; destroying it.");
                Destroy(this);
                return;
            }
            s_Instance = this;
        }

        void OnDestroy()
        {
            if (_subscribedPlayer != null) _subscribedPlayer.Teleported -= OnPlayerTeleported;
            if (s_Instance == this) s_Instance = null;
        }

        void Update()
        {
            var player = FirstPersonController.Current;
            if (player != _subscribedPlayer)
            {
                if (_subscribedPlayer != null) _subscribedPlayer.Teleported -= OnPlayerTeleported;
                _subscribedPlayer = player;
                if (player != null) player.Teleported += OnPlayerTeleported;
            }
            // The first checkpoint: wherever the player starts.
            if (_checkpoint == null && player != null && player.IsGrounded)
                SetCheckpoint("start", PlayerPose.Of(player));
        }

        /// <summary>Zone entry (teleport arrival into another zone, hub entry) is a checkpoint.</summary>
        void OnPlayerTeleported(Vector3 from, Vector3 to)
        {
            if (Restoring) return;
            var player = FirstPersonController.Current;
            if (player == null) return;
            int zone = ZoneInfo.ZoneOf(to);
            if (_checkpoint == null || zone != ZoneInfo.ZoneOf(from) || zone != _checkpoint.Zone)
                SetCheckpoint("zone:" + zone, PlayerPose.Of(player));
        }

        /// <summary>True during a rewind's own pose restore (not a zone entry).</summary>
        internal static bool Restoring { get; private set; }

        // ---------------------------------------------------------------- push

        /// <summary>Records a world change (call right after making it).</summary>
        public void Push(WorldChange change)
        {
            if (change == null) return;
            if (change.Time <= 0f) change.Time = UnityEngine.Time.time;
            if (!change.HasPose && FirstPersonController.Current != null) change.At(PoseNow());
            _changes.Add(change);
            try { Pushed?.Invoke(change); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>The safe pose to store with a change being made now (the player's current safe pose).</summary>
        public static PlayerPose SafePoseNow()
        {
            var fpc = FirstPersonController.Current;
            if (fpc == null) return default;
            var tracker = fpc.GetComponent<SafePoseTracker>();
            return tracker != null ? tracker.CurrentSafe() : PlayerPose.Of(fpc);
        }

        /// <summary>The player's exact pose now (feet, yaw, pitch): what a change being made now stores as its <see cref="WorldChange.Pose"/>.</summary>
        public static PlayerPose PoseNow() => PlayerPose.Of(FirstPersonController.Current);

        // ---------------------------------------------------------------- rewind

        /// <summary>What a single R would do right now (no side effects).</summary>
        public RewindResult PeekRewind()
        {
            var tracker = Tracker();
            if (tracker != null && (tracker.IsFalling || tracker.InLimbo)) return RewindResult.RecoveredFall;
            return CanUndo ? RewindResult.Undid : RewindResult.Nothing;
        }

        /// <summary>
        /// Single R, instantly. Priority: fall recovery &gt; undo the top record &gt; nothing. An undo always puts
        /// the player back at the pose the change was made from (<see cref="WorldChange.Pose"/>, or its safe
        /// fallback), wherever they are now: crossing on a placement and rewinding never leaves them across.
        /// <see cref="RewindController"/> plays the same undo as a glide (<see cref="UndoTop"/>).
        /// </summary>
        public RewindResult RewindOnce()
        {
            var fpc = FirstPersonController.Current;
            var tracker = Tracker();

            if (tracker != null && (tracker.IsFalling || tracker.InLimbo))
            {
                PlayerPose pose = tracker.LastSafe;
                ApplyPose(fpc, pose);
                tracker.Reset(pose);
                if (fpc != null) fpc.NotifyFallRecovered();
                Raise(FallRecovered);
                return Finish(RewindResult.RecoveredFall);
            }

            if (CanUndo)
            {
                PlayerPose result = UndoTopCore();
                if (fpc != null) ApplyPose(fpc, result);
                if (tracker != null) tracker.Reset(result);
                return Finish(RewindResult.Undid);
            }

            return Finish(RewindResult.Nothing);
        }

        /// <summary>The record a single R would undo now (null when nothing can be undone above the checkpoint).</summary>
        public WorldChange Top => CanUndo ? _changes[_changes.Count - 1] : null;

        /// <summary>
        /// Where undoing the top record would return the player, judged against the current world (the glide
        /// heads here before the undo; <see cref="UndoTop"/> confirms it against the restored world).
        /// </summary>
        public bool PeekReturnPose(out PlayerPose pose)
        {
            WorldChange top = Top;
            pose = top != null ? PreferredPose(top) : PlayerPose.Of(FirstPersonController.Current);
            return top != null;
        }

        /// <summary>
        /// Undoes the top record without moving the player and reports where they belong
        /// (<paramref name="returnPose"/>): the caller moves them there (the rewind glide). Raises
        /// <see cref="Rewound"/>(Undid). Nothing happens (and Nothing is returned) when there is nothing to undo.
        /// </summary>
        public RewindResult UndoTop(out PlayerPose returnPose)
        {
            returnPose = PlayerPose.Of(FirstPersonController.Current);
            if (!CanUndo) return RewindResult.Nothing;
            returnPose = UndoTopCore();
            return Finish(RewindResult.Undid);
        }

        PlayerPose UndoTopCore()
        {
            WorldChange top = _changes[_changes.Count - 1];
            _changes.RemoveAt(_changes.Count - 1);
            UndoRecord(top, false);
            return ResolveReturnPose(top);
        }

        /// <summary>The pose a change wants to return to, before any check: its exact pose, else its safe pose.</summary>
        static PlayerPose PreferredPose(WorldChange c)
        {
            if (c.HasPose) return c.Pose;
            if (c.SafePose.Feet != Vector3.zero) return c.SafePose;
            return PlayerPose.Of(FirstPersonController.Current);
        }

        /// <summary>
        /// Where the player goes after <paramref name="c"/> was undone (evaluate in the restored world): the exact
        /// pose of the change when a player can stand there; otherwise the safe pose sampled then (looking the
        /// same way as at the change); as a last resort the exact pose anyway.
        /// </summary>
        public static PlayerPose ResolveReturnPose(WorldChange c)
        {
            if (c == null) return PlayerPose.Of(FirstPersonController.Current);
            if (c.HasPose && StandingCheck.IsSupported(c.Pose.Feet)) return c.Pose;
            bool hasSafe = c.SafePose.Feet != Vector3.zero;
            if (hasSafe && StandingCheck.IsSupported(c.SafePose.Feet))
            {
                PlayerPose p = c.SafePose;
                if (c.HasPose)
                {
                    p.Yaw = c.Pose.Yaw;
                    p.Pitch = c.Pose.Pitch;
                }
                return p;
            }
            return PreferredPose(c);
        }

        /// <summary>
        /// Double R: unwinds every record down to the checkpoint floor (instantly, LIFO), restores the switch
        /// channels, the inventory (same photos, same order, same selection), film, camera unlock and pose.
        /// Works with no changes at all (the T2 lesson).
        /// </summary>
        public RewindResult RewindToCheckpoint()
        {
            Checkpoint cp = _checkpoint;
            if (cp == null) return Finish(RewindResult.Nothing);
            var fpc = FirstPersonController.Current;

            while (_changes.Count > cp.HistoryDepth)
            {
                WorldChange top = _changes[_changes.Count - 1];
                _changes.RemoveAt(_changes.Count - 1);
                UndoRecord(top, true);
            }

            SwitchBoard.Restore(cp.Channels);

            if (fpc != null)
            {
                var inventory = fpc.GetComponent<PhotoInventory>();
                if (inventory != null) inventory.SetAll(cp.Inventory, cp.SelectedIndex);
                var camera = fpc.GetComponent<InstantCamera>();
                if (camera != null)
                {
                    camera.SetUnlockedSilently(cp.CameraUnlocked);
                    camera.Film = cp.Film;
                }
                var holder = fpc.GetComponent<PhotoHolder>();
                if (holder != null) holder.Lower();
            }

            ApplyPose(fpc, cp.Pose);
            var tracker = Tracker();
            if (tracker != null) tracker.Reset(cp.Pose);
            if (fpc != null) fpc.NotifyFallRecovered(false);

            Raise(CheckpointRestored, cp);
            return Finish(RewindResult.ToCheckpoint);
        }

        // ---------------------------------------------------------------- checkpoints

        /// <summary>Takes a checkpoint snapshot now (zone entry, hub entry, markers).</summary>
        public void SetCheckpoint(string id, PlayerPose pose)
        {
            var fpc = FirstPersonController.Current;
            var cp = new Checkpoint
            {
                Id = id,
                Zone = pose.Zone,
                HistoryDepth = _changes.Count,
                Pose = pose,
                Inventory = new List<PhotoData>(8),
                SelectedIndex = -1,
                Channels = SwitchBoard.Snapshot(),
                Time = UnityEngine.Time.time,
            };
            if (fpc != null)
            {
                var inventory = fpc.GetComponent<PhotoInventory>();
                if (inventory != null)
                {
                    for (int i = 0; i < inventory.Count; i++) cp.Inventory.Add(inventory.Photos[i]);
                    cp.SelectedIndex = inventory.SelectedIndex;
                }
                var camera = fpc.GetComponent<InstantCamera>();
                if (camera != null)
                {
                    cp.Film = camera.Film;
                    cp.CameraUnlocked = camera.Unlocked;
                }
            }
            _checkpoint = cp;
            Raise(CheckpointReached, cp);
        }

        /// <summary>
        /// A CheckpointMarker was reached: sets the checkpoint only when the marker is newer than the current
        /// one (not the current checkpoint and not visited before since the last restart).
        /// </summary>
        public bool TrySetMarkerCheckpoint(string id, PlayerPose pose)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (_checkpoint != null && _checkpoint.Id == id) return false;
            if (!_visitedMarkers.Add(id)) return false;
            SetCheckpoint(id, pose);
            return true;
        }

        /// <summary>Forgets every record and checkpoint (GameBootstrap.Restart). Does not touch the world.</summary>
        public void ClearAll()
        {
            _changes.Clear();
            _visitedMarkers.Clear();
            _checkpoint = null;
        }

        /// <summary>Undoes every record (instantly, LIFO, across checkpoints), then <see cref="ClearAll"/>.</summary>
        public void UnwindAll()
        {
            while (_changes.Count > 0)
            {
                WorldChange top = _changes[_changes.Count - 1];
                _changes.RemoveAt(_changes.Count - 1);
                UndoRecord(top, true);
            }
            ClearAll();
        }

        // ---------------------------------------------------------------- helpers

        static SafePoseTracker Tracker()
        {
            var fpc = FirstPersonController.Current;
            return fpc != null ? fpc.GetComponent<SafePoseTracker>() : null;
        }

        static void UndoRecord(WorldChange change, bool instant)
        {
            IsUndoing = true;
            try { change.Undo(instant); }
            catch (Exception e) { Debug.LogException(e); }
            finally { IsUndoing = false; }
        }

        static void ApplyPose(FirstPersonController fpc, PlayerPose pose)
        {
            if (fpc == null) return;
            Restoring = true;
            try
            {
                fpc.Teleport(pose.Feet, pose.Yaw);
                fpc.SetLook(pose.Yaw, pose.Pitch);
            }
            finally { Restoring = false; }
            Physics.SyncTransforms();
        }

        RewindResult Finish(RewindResult result)
        {
            try { Rewound?.Invoke(result); }
            catch (Exception e) { Debug.LogException(e); }
            return result;
        }

        static void Raise(Action a)
        {
            try { a?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void Raise(Action<Checkpoint> a, Checkpoint cp)
        {
            try { a?.Invoke(cp); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>Is a standing player at a feet position supported and free (art bible §11.2)?</summary>
    public static class StandingCheck
    {
        const int WorldMask = ~ProjectionSystem.ExcludedLayerMask;
        const QueryTriggerInteraction Ignore = QueryTriggerInteraction.Ignore;

        /// <summary>
        /// Ground within 0.35 m below the feet, the capsule (above the step offset) overlaps nothing, and the
        /// chest is not enclosed by a closed mesh.
        /// </summary>
        public static bool IsSupported(Vector3 feet)
        {
            Physics.SyncTransforms();
            float r = PlayerFactory.Radius * 0.85f;

            // Ground probe: 0.35 m below the feet. Ground that is about to leave (a mover the undo just set
            // moving, a slab that has given way) does not count.
            if (!Physics.SphereCast(feet + Vector3.up * (r + 0.05f), r, Vector3.down, out RaycastHit ground, 0.05f + 0.35f, WorldMask, Ignore))
                return false;
            var mover = ground.collider != null ? ground.collider.GetComponentInParent<Mover>() : null;
            if (mover != null && mover.IsMoving) return false;
            var collapse = ground.collider != null ? ground.collider.GetComponentInParent<StoryCollapse>() : null;
            if (collapse != null && collapse.Fired) return false;

            Vector3 lo = feet + Vector3.up * (PlayerFactory.Radius + 0.45f);
            Vector3 hi = feet + Vector3.up * (PlayerFactory.Height - PlayerFactory.Radius);
            if (Physics.CheckCapsule(lo, hi, r, WorldMask, Ignore)) return false;

            // Enclosed: from inside a closed mesh the first surface hit in any direction is a back face.
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                Vector3 chest = feet + Vector3.up * 1f;
                if (Enclosed(chest, Vector3.up) || Enclosed(chest, Vector3.right) || Enclosed(chest, Vector3.forward))
                    return false;
            }
            finally
            {
                Physics.queriesHitBackfaces = backfaces;
            }
            return true;
        }

        static bool Enclosed(Vector3 from, Vector3 dir) =>
            Physics.Raycast(from, dir, out RaycastHit hit, 60f, WorldMask, Ignore) && Vector3.Dot(hit.normal, dir) > 0f;
    }
}
