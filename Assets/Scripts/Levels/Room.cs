using System;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Levels.Arch;
using Ion.Levels.Props;
using Ion.Levels.Rooms;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;

namespace Ion.Levels
{
    /// <summary>
    /// One zone (art bible §7). <see cref="Build"/> is called once by <see cref="GameBootstrap"/> with the zone's
    /// world root (floor top at local y = 0, the player walks toward local +Z) and a context giving access to the
    /// zone's diorama root, spawn, markers, photo, device and teleporter helpers. <see cref="Arch.Arch.Style"/> is
    /// set to <see cref="Style"/> before Build runs, and the zone root and its diorama are baked
    /// (<see cref="Arch.Arch.Bake"/>) right after it returns.
    /// </summary>
    public abstract class Room
    {
        /// <summary>Short name shown when the player arrives.</summary>
        public virtual string Title => GetType().Name;

        /// <summary>One-line objective toasted after the title (optional).</summary>
        public virtual string Intro => null;

        /// <summary>Stable lookup key (IonDebug "zone t1", tests): "t1", "t2", "hub", "stairs", "camera", "gallery".</summary>
        public virtual string Key => GetType().Name.ToLowerInvariant();

        /// <summary>The zone's sky / sun / shadow tint (art bible §3.4). Applied before its diorama shots are captured.</summary>
        public virtual ZoneMood Mood => ZoneMood.Noon;

        /// <summary>Surface defaults for the zone's architecture (art bible §3.3).</summary>
        public virtual ArchStyle Style => ArchStyle.LightTable;

        /// <summary>Top of the zone's lowest walkable floor (room-local y). The void starts 6 m below it.</summary>
        public virtual float LowestFloorY => 0f;

        /// <summary>Zone bake chunk in metres for collider pieces (Arch.Bake); 0 merges the whole zone into one object per material.</summary>
        public virtual float BakeChunk => Ion.Levels.Arch.Arch.DefaultChunk;

        /// <summary>Zone bake chunk in metres for collider-less (soft / visual) pieces; 0 = one object per material.</summary>
        public virtual float BakeSoftChunk => Ion.Levels.Arch.Arch.DefaultSoftChunk;

        /// <summary>Limbo height (room-local y = world y): lowest floor − 6 (art bible §11.2).</summary>
        public float VoidY => LowestFloorY - 6f;

        public abstract void Build(Transform root, RoomContext ctx);

        /// <summary>Called after the player arrived in this zone through <see cref="GameBootstrap.GoToZone"/>.</summary>
        protected internal virtual void OnEnter(RoomContext ctx) { }

        /// <summary>"Play again": put story state (one-way beats, exhibits, hatches) back to the start.</summary>
        protected internal virtual void OnRestart(RoomContext ctx) { }

        /// <summary>
        /// The zone's full, ordered solution script (art bible §7.5): every step the player performs, in order.
        /// The tutorial zones' lists ARE the tutorial. Used by the debug harness (IonDebug.GoTo) and the PlayMode
        /// solvability tests.
        /// </summary>
        public readonly List<RoomSolution> Solutions = new List<RoomSolution>();

        /// <summary>Records a solution step (see <see cref="Solutions"/>).</summary>
        protected RoomSolution AddSolution(string name, RoomSolution.Kind action, Vector3 localFeet, float yaw = 0f,
                                           float pitch = 0f, int photoIndex = -1, float roll = 0f)
        {
            var s = new RoomSolution
            {
                Name = name,
                Action = action,
                LocalFeet = localFeet,
                Yaw = yaw,
                Pitch = pitch,
                PhotoIndex = photoIndex,
                Roll = roll,
            };
            Solutions.Add(s);
            return s;
        }

        /// <summary>The solution step called <paramref name="name"/> (case-insensitive), or null.</summary>
        public RoomSolution FindSolution(string name)
        {
            int i = IndexOfSolution(name);
            return i >= 0 ? Solutions[i] : null;
        }

        /// <summary>Index of the step called <paramref name="name"/> in <see cref="Solutions"/>, or -1.</summary>
        public int IndexOfSolution(string name)
        {
            for (int i = 0; i < Solutions.Count; i++)
                if (string.Equals(Solutions[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }
    }

    /// <summary>
    /// One step of a zone's intended solution: stand at <see cref="LocalFeet"/> (room-local), look along
    /// <see cref="Yaw"/>/<see cref="Pitch"/> (degrees, 0 = room +Z, level; pitch positive = down) and do
    /// <see cref="Action"/>. A runner walks <see cref="Via"/> first (room-local waypoints), then the feet.
    /// </summary>
    public sealed class RoomSolution
    {
        public enum Kind
        {
            /// <summary>Raise photo <see cref="PhotoIndex"/>, rotate it by <see cref="Roll"/> and place it.</summary>
            Place,
            /// <summary>Take an instant-camera snapshot.</summary>
            Snap,
            /// <summary>A place the player must be able to reach (far side, ledge top).</summary>
            Goal,
            /// <summary>Walk to <see cref="LocalFeet"/>; it may trigger a story event (see <see cref="Hazard"/>).</summary>
            Walk,
            /// <summary>Press the switch nearest <see cref="LocalFeet"/> in view (channel <see cref="Channel"/>).</summary>
            Press,
            /// <summary>Collect the pickup (photo or camera) nearest <see cref="LocalFeet"/>.</summary>
            Pickup,
            /// <summary>Single R. <see cref="Expect"/> is the expected result; LocalFeet the expected pose.</summary>
            Rewind,
            /// <summary>R R: back to the last checkpoint (LocalFeet = the checkpoint pose).</summary>
            RewindToCheckpoint,
            /// <summary>Enter the teleporter at <see cref="LocalFeet"/> (leads to zone <see cref="Destination"/>).</summary>
            Teleport,
        }

        /// <summary>Expected <c>RewindResult</c> of a <see cref="Kind.Rewind"/> / <see cref="Kind.RewindToCheckpoint"/> step.</summary>
        public enum RewindExpect { Undid, RecoveredFall, Nothing, ToCheckpoint }

        /// <summary>What deliberately stops a Walk / Goal step short (addition).</summary>
        public enum Hazards
        {
            None,
            /// <summary>The goal cannot be reached (a wall, a ledge): the player stays on its level.</summary>
            Blocked,
            /// <summary>The walk ends in a fall into the void (falling / limbo).</summary>
            Fall,
            /// <summary>A story drop: the player lands, grounded, at least 1 m lower (the T2 trap pit).</summary>
            Drop,
        }

        public string Name;
        public Kind Action;
        public Vector3 LocalFeet;
        public float Yaw, Pitch;
        /// <summary>Index into the zone's own pre-made photos (diorama shots); -1 = the latest instant-camera snapshot.</summary>
        public int PhotoIndex = -1;
        /// <summary>Roll the raised photo must have when placed (multiples of 90; positive = Q).</summary>
        public float Roll;
        /// <summary>Rewind steps: the expected result.</summary>
        public RewindExpect Expect;

        // ---- additions (art bible §7.5 "you may add") ----------------------------------------------------------
        /// <summary>Walk / Goal: the step is expected to be stopped by this (negative beats of a tutorial).</summary>
        public Hazards Hazard;
        /// <summary>Room-local waypoints walked (in order) before <see cref="LocalFeet"/>.</summary>
        public Vector3[] Via;
        /// <summary>Arrival tolerance (m) for walks and expected rewind poses.</summary>
        public float Tolerance = 0.5f;
        /// <summary>The spot only has ground after the preceding Place (skipped by the "solid ground" harness check).</summary>
        public bool AfterPlace;
        /// <summary>Press: the switch channel expected to toggle.</summary>
        public string Channel;
        /// <summary>Teleport: key of the zone the teleporter leads to (null = stays, e.g. the end card).</summary>
        public string Destination;

        /// <summary>Place and Snap steps have a brass standing marker at their feet (art bible §7.5.2).</summary>
        public bool NeedsMarker => Action == Kind.Place || Action == Kind.Snap;

        public override string ToString() => Name + " (" + Action + ")";
    }

    /// <summary>
    /// A photo captured from a diorama once the whole world exists (GameBootstrap does it a frame after building,
    /// with the zone's mood applied). The capture pose is defined in diorama-local coordinates by the feet
    /// position of an imaginary player; the camera sits at <see cref="RoomContext.EyeHeight"/> above it.
    /// </summary>
    public sealed class DioramaShot
    {
        public Transform Diorama;
        public Vector3 LocalFeet;
        public float Yaw, Pitch, Roll;
        public float FovY, Aspect;
        public string Label;
        /// <summary>Stable id ("t1.stair", "hub.stairs" ...). Addition.</summary>
        public string Id;

        /// <summary>Set once captured.</summary>
        public PhotoData Photo { get; internal set; }

        /// <summary>Raised once, right after capture.</summary>
        public event Action<PhotoData> Captured;

        /// <summary>World pose of the capture camera for the given eye height.</summary>
        public Pose WorldPose(float eyeHeight)
        {
            Vector3 p = Diorama.TransformPoint(LocalFeet + Vector3.up * eyeHeight);
            // Euler(pitch, yaw, roll) = yaw * pitch * roll: the camera is yawed, pitched, then rolled about its
            // own forward axis.
            Quaternion r = Diorama.rotation * Quaternion.Euler(Pitch, Yaw, Roll);
            return new Pose(p, r);
        }

        internal void Resolve(PhotoData photo)
        {
            Photo = photo;
            Action<PhotoData> handlers = Captured;
            Captured = null;
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action<PhotoData>)d)(photo); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }

    /// <summary>A brass standing marker recorded by <see cref="RoomContext.Marker"/> (harness: 1:1 with Place/Snap steps).</summary>
    public struct MarkerSpot
    {
        public Vector3 LocalFeet;
        public float Yaw;
    }

    /// <summary>Build-time services handed to each <see cref="Room"/>.</summary>
    public sealed class RoomContext
    {
        /// <summary>Fallback eye height when there is no bootstrap (the player's is <c>PlayerFactory.EyeHeight</c> = 1.62).</summary>
        public const float DefaultEyeHeight = 1.62f;

        /// <summary>Standard photo shape for the pre-made photos (same as the instant camera).</summary>
        public const float PhotoFovY = 50f, PhotoAspect = 4f / 3f;

        readonly GameBootstrap _game;
        readonly Vector3 _dioramaOrigin;
        Transform _diorama;
        Pose _spawn;
        bool _hasSpawn;

        internal readonly List<DioramaShot> Shots = new List<DioramaShot>();
        readonly List<MarkerSpot> _markers = new List<MarkerSpot>();

        /// <summary>Room-local discs (x, z, radius in xyz) that scattered decor must stay out of (legacy Scatter).</summary>
        internal readonly List<Vector3> KeepClear = new List<Vector3>();

        /// <summary>Keeps scattered decor out of a disc around <paramref name="localPos"/> (legacy).</summary>
        public void KeepClearAt(Vector3 localPos, float radius)
        {
            KeepClear.Add(new Vector3(localPos.x, localPos.z, radius));
        }

        internal RoomContext(GameBootstrap game, int index, Room room, Transform worldRoot, Vector3 dioramaOrigin)
        {
            _game = game;
            Index = index;
            Room = room;
            WorldRoot = worldRoot;
            _dioramaOrigin = dioramaOrigin;
            Random = new System.Random(1234 + index * 977);
        }

        public int Index { get; }
        public Room Room { get; }
        public GameBootstrap Game => _game;

        /// <summary>The zone's world root (floor top at local y = 0).</summary>
        public Transform WorldRoot { get; }

        /// <summary>Deterministic per-zone random source.</summary>
        public System.Random Random { get; }

        /// <summary>Camera height above the feet used for diorama captures (<c>PlayerFactory.EyeHeight</c>).</summary>
        public float EyeHeight => _game != null ? _game.EyeHeight : DefaultEyeHeight;

        /// <summary>This zone's photo diorama root, far below the world (created on first use).</summary>
        public Transform DioramaRoot
        {
            get
            {
                if (_diorama == null)
                {
                    var go = new GameObject("Diorama " + Index + " (" + Room.Title + ")");
                    _diorama = go.transform;
                    if (_game != null) _diorama.SetParent(_game.DioramaContainer, false);
                    _diorama.position = _dioramaOrigin;
                    _diorama.rotation = Quaternion.identity;
                }
                return _diorama;
            }
        }

        public bool HasDiorama => _diorama != null;

        /// <summary>The zone's pre-made photos in registration order (null until captured).</summary>
        public PhotoData GetShotPhoto(int index) => index >= 0 && index < Shots.Count ? Shots[index].Photo : null;

        public DioramaShot GetShot(int index) => index >= 0 && index < Shots.Count ? Shots[index] : null;

        public int ShotCount => Shots.Count;

        /// <summary>Standing markers placed with <see cref="Marker"/> (room-local).</summary>
        public IReadOnlyList<MarkerSpot> Markers => _markers;

        /// <summary>World-space feet position of a solution step.</summary>
        public Vector3 SolutionFeet(RoomSolution s) => WorldRoot.TransformPoint(s.LocalFeet);

        /// <summary>World yaw (degrees) of a solution step.</summary>
        public float SolutionYaw(RoomSolution s) => (WorldRoot.rotation * Quaternion.Euler(0f, s.Yaw, 0f)).eulerAngles.y;

        /// <summary>World point of a room-local position.</summary>
        public Vector3 World(Vector3 local) => WorldRoot.TransformPoint(local);

        /// <summary>World yaw of a room-local yaw.</summary>
        public float WorldYaw(float localYaw) => (WorldRoot.rotation * Quaternion.Euler(0f, localYaw, 0f)).eulerAngles.y;

        /// <summary>World-space spawn (feet position; rotation = facing).</summary>
        public Pose Spawn => _hasSpawn ? _spawn : new Pose(WorldRoot.position, WorldRoot.rotation);

        public float SpawnYaw => Spawn.rotation.eulerAngles.y;

        /// <summary>Sets the zone's arrival pose (local feet position + yaw).</summary>
        public void SetSpawn(Vector3 localFeet, float yaw = 0f)
        {
            _spawn = new Pose(WorldRoot.TransformPoint(localFeet), WorldRoot.rotation * Quaternion.Euler(0f, yaw, 0f));
            _hasSpawn = true;
        }

        // ------------------------------------------------------------------ photos

        /// <summary>Captures a photo right now (the world must already be built).</summary>
        public PhotoData CapturePhoto(Pose pose, float fovY, float aspect, string label)
        {
            var ps = ProjectionSystem.Instance;
            if (ps == null)
            {
                Debug.LogError("[Levels] No ProjectionSystem; cannot capture '" + label + "'.");
                return null;
            }
            return ps.Capture(pose, fovY, aspect, label);
        }

        /// <summary>
        /// Registers a diorama photo, captured after everything is built (in this zone's mood). <paramref name="localFeet"/>
        /// is in diorama-local coordinates (diorama floor top at y = 0, like the zone). The camera looks along
        /// <paramref name="yaw"/> (0 = +Z), pitched by <paramref name="pitch"/> (positive = down), rolled by
        /// <paramref name="roll"/> degrees. Frame it from the same spot as the zone's standing marker.
        /// </summary>
        public DioramaShot RegisterDioramaShot(Vector3 localFeet, float yaw, float roll, string label,
                                               float fovY = PhotoFovY, float aspect = PhotoAspect, float pitch = 0f,
                                               string id = null)
        {
            var shot = new DioramaShot
            {
                Diorama = DioramaRoot,
                LocalFeet = localFeet,
                Yaw = yaw,
                Pitch = pitch,
                Roll = roll,
                FovY = fovY,
                Aspect = aspect,
                Label = label,
                Id = id ?? label,
            };
            Shots.Add(shot);
            return shot;
        }

        // ------------------------------------------------------------------ kit wrappers

        /// <summary>A brass <c>[ ]</c> standing marker (PropKit) at a Place / Snap spot, recorded for the harness.</summary>
        public GameObject Marker(Vector3 localFeet, float yaw)
        {
            _markers.Add(new MarkerSpot { LocalFeet = localFeet, Yaw = yaw });
            return PropKit.StandingMarker(WorldRoot, localFeet, yaw);
        }

        /// <summary>A photo display (pedestal or easel) holding <paramref name="shot"/>'s photo once captured.</summary>
        public PhotoPickup PhotoDisplay(Vector3 localPos, Dir facing, DioramaShot shot, DisplayMount mount = DisplayMount.Pedestal)
        {
            return PropKit.PhotoDisplay(WorldRoot, localPos, facing, shot, mount);
        }

        /// <summary>The default "next zone" action (zone index + 1).</summary>
        public Action NextZone()
        {
            var game = _game;
            int next = Index + 1;
            return () => { if (game != null) game.GoToRoom(next); };
        }

        /// <summary>A teleporter in the zone (front faces <paramref name="facing"/>). Null action = next zone.</summary>
        public Teleporter Teleporter(Vector3 localPos, Dir facing, Action onEnter = null, Mat tint = Mat.Graphite)
        {
            return PropKit.Teleporter(WorldRoot, localPos, facing, onEnter ?? NextZone(), tint);
        }

        /// <summary>
        /// The same teleporter in the diorama (art bible §7.5.3): a placement whose frustum holds the zone's teleporter
        /// replaces it with this copy, which shares <paramref name="onEnter"/>, so the way forward survives.
        /// </summary>
        public Teleporter DioramaTeleporter(Vector3 localPos, Dir facing, Action onEnter, Mat tint = Mat.Graphite)
        {
            return PropKit.Teleporter(DioramaRoot, localPos, facing, onEnter ?? NextZone(), tint);
        }

        /// <summary>A hint area (toasts once per entry while <paramref name="when"/> holds; see <see cref="ZoneHint"/>).</summary>
        public ZoneHint Hint(Vector3 localPos, float radius, string text, float seconds = 4f, Func<bool> when = null,
                             bool once = true)
        {
            var go = new GameObject("Hint");
            go.transform.SetParent(WorldRoot, false);
            go.transform.localPosition = localPos;
            var zone = go.AddComponent<ZoneHint>();
            zone.Text = text;
            zone.Radius = radius;
            zone.Seconds = seconds;
            zone.When = when;
            zone.Once = once;
            return zone;
        }

        /// <summary>The local player's photo holder / inventory / camera (null before the player exists).</summary>
        public static PhotoHolder PlayerHolder => FirstPersonController.Current != null ? FirstPersonController.Current.GetComponent<PhotoHolder>() : null;
        public static PhotoInventory PlayerInventory => FirstPersonController.Current != null ? FirstPersonController.Current.GetComponent<PhotoInventory>() : null;
        public static InstantCamera PlayerCamera => FirstPersonController.Current != null ? FirstPersonController.Current.GetComponent<InstantCamera>() : null;

        /// <summary>True if the player holds <paramref name="shot"/>'s photo.</summary>
        public static bool PlayerHas(DioramaShot shot)
        {
            var inv = PlayerInventory;
            return inv != null && shot != null && shot.Photo != null && inv.Contains(shot.Photo);
        }

        /// <summary>True while the player holds a photo up.</summary>
        public static bool PlayerRaised
        {
            get
            {
                var h = PlayerHolder;
                return h != null && h.IsRaised;
            }
        }

        /// <summary>Unlocks the player's instant camera and sets its film.</summary>
        public static void UnlockInstantCamera(int film = 3)
        {
            var cam = FirstPersonController.Current != null
                ? FirstPersonController.Current.GetComponentInChildren<InstantCamera>(true)
                : null;
            if (cam == null) cam = UnityEngine.Object.FindFirstObjectByType<InstantCamera>(FindObjectsInactive.Include);
            if (cam == null)
            {
                Debug.LogWarning("[Levels] No InstantCamera found to unlock.");
                return;
            }
            cam.Unlocked = true;
            cam.Film = Mathf.Max(cam.Film, film);
        }

        /// <summary>Toasts on the HUD if it exists.</summary>
        public static void Toast(string text, float seconds = 3.5f)
        {
            var hud = Hud.Instance != null ? Hud.Instance : UnityEngine.Object.FindFirstObjectByType<Hud>();
            if (hud != null) hud.Toast(text, seconds);
        }
    }
}
