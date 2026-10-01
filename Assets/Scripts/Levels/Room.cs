using System;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;

namespace Ion.Levels
{
    /// <summary>
    /// One level. <see cref="Build"/> is called once by <see cref="GameBootstrap"/> with the room's
    /// world root (floor top at local y = 0, the player walks toward local +Z) and a context giving
    /// access to the room's diorama root, spawn, photo, pickup and teleporter helpers.
    /// </summary>
    public abstract class Room
    {
        /// <summary>Short name shown when the player arrives.</summary>
        public virtual string Title => GetType().Name;

        /// <summary>One-line objective toasted after the title (optional).</summary>
        public virtual string Intro => null;

        public abstract void Build(Transform root, RoomContext ctx);

        /// <summary>
        /// Where the intended solution is performed (room-local feet positions + view), filled by
        /// <see cref="Build"/>. Used by the debug harness (IonDebug.GoTo) and the PlayMode solvability tests.
        /// </summary>
        public readonly List<RoomSolution> Solutions = new List<RoomSolution>();

        /// <summary>Records a solution spot (see <see cref="Solutions"/>).</summary>
        protected void AddSolution(string name, RoomSolution.Kind action, Vector3 localFeet, float yaw = 0f,
                                   float pitch = 0f, int photoIndex = -1, float roll = 0f)
        {
            Solutions.Add(new RoomSolution
            {
                Name = name,
                Action = action,
                LocalFeet = localFeet,
                Yaw = yaw,
                Pitch = pitch,
                PhotoIndex = photoIndex,
                Roll = roll,
            });
        }

        /// <summary>The solution spot called <paramref name="name"/> (case-insensitive), or null.</summary>
        public RoomSolution FindSolution(string name)
        {
            for (int i = 0; i < Solutions.Count; i++)
                if (string.Equals(Solutions[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return Solutions[i];
            return null;
        }
    }

    /// <summary>
    /// One step of a room's intended solution: stand at <see cref="LocalFeet"/> (room-local), look along
    /// <see cref="Yaw"/>/<see cref="Pitch"/> (degrees, 0 = room +Z, level) and do <see cref="Action"/>.
    /// </summary>
    public sealed class RoomSolution
    {
        public enum Kind
        {
            /// <summary>Raise photo <see cref="PhotoIndex"/>, rotate it by <see cref="Roll"/> and place it.</summary>
            Place,
            /// <summary>Take an instant-camera snapshot.</summary>
            Snap,
            /// <summary>A place the player must be able to reach (far side, exit teleporter, pickup).</summary>
            Goal,
        }

        public string Name;
        public Kind Action;
        public Vector3 LocalFeet;
        public float Yaw, Pitch;
        /// <summary>Index into the room's own pre-made photos (diorama shots); -1 = the latest instant-camera snapshot.</summary>
        public int PhotoIndex = -1;
        /// <summary>Roll the raised photo must have when placed (multiples of 90; positive = Q).</summary>
        public float Roll;
    }

    /// <summary>
    /// A photo that is captured from a diorama once the whole world exists (GameBootstrap does it a
    /// frame after building). The capture pose is defined in diorama-local coordinates by the feet
    /// position of an imaginary player; the camera sits at <see cref="RoomContext.EyeHeight"/> above it.
    /// </summary>
    public sealed class DioramaShot
    {
        public Transform Diorama;
        public Vector3 LocalFeet;
        public float Yaw, Pitch, Roll;
        public float FovY, Aspect;
        public string Label;

        /// <summary>Set once captured.</summary>
        public PhotoData Photo { get; internal set; }

        /// <summary>Raised once, right after capture.</summary>
        public event Action<PhotoData> Captured;

        /// <summary>World pose of the capture camera for the given eye height.</summary>
        public Pose WorldPose(float eyeHeight)
        {
            Vector3 p = Diorama.TransformPoint(LocalFeet + Vector3.up * eyeHeight);
            // Euler(pitch, yaw, roll) = yaw * pitch * roll: the camera is yawed, pitched, then rolled
            // about its own forward axis.
            Quaternion r = Diorama.rotation * Quaternion.Euler(Pitch, Yaw, Roll);
            return new Pose(p, r);
        }

        internal void Resolve(PhotoData photo)
        {
            Photo = photo;
            Captured?.Invoke(photo);
            Captured = null;
        }
    }

    /// <summary>Build-time services handed to each <see cref="Room"/>.</summary>
    public sealed class RoomContext
    {
        /// <summary>Default standing eye height (camera above feet) assumed for diorama captures.</summary>
        public const float DefaultEyeHeight = 1.6f;

        /// <summary>Standard photo shape for the pre-made photos.</summary>
        public const float PhotoFovY = 50f, PhotoAspect = 4f / 3f;

        readonly GameBootstrap _game;
        readonly Vector3 _dioramaOrigin;
        Transform _diorama;
        Pose _spawn;
        bool _hasSpawn;

        internal readonly List<DioramaShot> Shots = new List<DioramaShot>();

        /// <summary>Room-local discs (x, z, radius in xyz) that scattered decor must stay out of.</summary>
        internal readonly List<Vector3> KeepClear = new List<Vector3>();

        /// <summary>Keeps scattered grass / flowers out of a disc around <paramref name="localPos"/>.</summary>
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

        /// <summary>The room's world root (floor top at local y = 0).</summary>
        public Transform WorldRoot { get; }

        /// <summary>Deterministic per-room random source (for decoration placement).</summary>
        public System.Random Random { get; }

        /// <summary>Camera height above the feet used for diorama captures (measured from the player).</summary>
        public float EyeHeight => _game != null ? _game.EyeHeight : DefaultEyeHeight;

        /// <summary>This room's photo diorama root, far below the world (created on first use).</summary>
        public Transform DioramaRoot
        {
            get
            {
                if (_diorama == null)
                {
                    var go = new GameObject("Diorama " + (Index + 1) + " (" + Room.Title + ")");
                    _diorama = go.transform;
                    _diorama.position = _dioramaOrigin;
                    if (_game != null) _diorama.SetParent(_game.DioramaContainer, true);
                }
                return _diorama;
            }
        }

        public bool HasDiorama => _diorama != null;

        /// <summary>The room's pre-made photos in registration order (null until captured).</summary>
        public PhotoData GetShotPhoto(int index) =>
            index >= 0 && index < Shots.Count ? Shots[index].Photo : null;

        public int ShotCount => Shots.Count;

        /// <summary>World-space feet position and view rotation (yaw/pitch) of a solution spot.</summary>
        public Vector3 SolutionFeet(RoomSolution s) => WorldRoot.TransformPoint(s.LocalFeet);

        /// <summary>World yaw (degrees) of a solution spot.</summary>
        public float SolutionYaw(RoomSolution s) => (WorldRoot.rotation * Quaternion.Euler(0f, s.Yaw, 0f)).eulerAngles.y;

        /// <summary>World-space spawn (feet position; rotation = facing).</summary>
        public Pose Spawn => _hasSpawn ? _spawn : new Pose(WorldRoot.position, WorldRoot.rotation);

        public float SpawnYaw => Spawn.rotation.eulerAngles.y;

        /// <summary>Sets the room's spawn (local feet position + yaw) and drops a checkpoint there.</summary>
        public void SetSpawn(Vector3 localFeet, float yaw = 0f)
        {
            _spawn = new Pose(WorldRoot.TransformPoint(localFeet), WorldRoot.rotation * Quaternion.Euler(0f, yaw, 0f));
            _hasSpawn = true;

            var go = new GameObject("PlayerSpawn");
            go.transform.SetParent(WorldRoot, false);
            go.transform.SetPositionAndRotation(_spawn.position, _spawn.rotation);
            go.AddComponent<PlayerSpawn>();
        }

        /// <summary>Drops an extra checkpoint (e.g. on the far side of a puzzle).</summary>
        public void AddCheckpoint(Vector3 localFeet, float yaw = 0f, float radius = 3f)
        {
            var go = new GameObject("Checkpoint");
            go.transform.SetParent(WorldRoot, false);
            go.transform.localPosition = localFeet;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<PlayerSpawn>().CheckpointRadius = radius;
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
        /// Registers a diorama photo, captured after everything is built. <paramref name="localFeet"/>
        /// is in diorama-local coordinates (diorama floor top at y = 0, like the room). The camera looks
        /// along <paramref name="yaw"/> (0 = +Z), level, rolled by <paramref name="roll"/> degrees.
        /// To make the paste line up, frame it from the same spot as the room's marker.
        /// </summary>
        public DioramaShot RegisterDioramaShot(Vector3 localFeet, float yaw, float roll, string label,
                                               float fovY = PhotoFovY, float aspect = PhotoAspect, float pitch = 0f)
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
            };
            Shots.Add(shot);
            return shot;
        }

        /// <summary>
        /// Builds a pedestal at <paramref name="localPos"/> (floor level) and, once the shot is captured,
        /// a <see cref="PhotoPickup"/> on top of it holding the photo.
        /// </summary>
        public GameObject PlacePhotoPickup(Vector3 localPos, DioramaShot shot)
        {
            const float pedestalHeight = 0.9f;
            KeepClearAt(localPos, 1.0f);
            var pedestal = new GameObject("PhotoPedestal");
            pedestal.transform.SetParent(WorldRoot, false);
            pedestal.transform.localPosition = localPos;
            Geo.Prism(pedestal.transform, new Vector3(0f, 0.1f, 0f), new Vector3(1.1f, 0.2f, 1.1f), Palette.Stone, 6);
            Geo.Prism(pedestal.transform, new Vector3(0f, pedestalHeight * 0.5f, 0f), new Vector3(0.55f, pedestalHeight, 0.55f), Palette.Cream, 6);
            Geo.Prism(pedestal.transform, new Vector3(0f, pedestalHeight + 0.05f, 0f), new Vector3(0.8f, 0.1f, 0.8f), Palette.Coral, 6);

            Vector3 top = pedestal.transform.TransformPoint(new Vector3(0f, pedestalHeight + 0.1f, 0f));
            Transform parent = WorldRoot;
            shot.Captured += photo =>
            {
                // Created after capture so the pickup's own visual shows the photo's preview.
                var go = new GameObject("PhotoPickup " + shot.Label);
                go.transform.SetParent(parent, false);
                go.transform.position = top;
                go.AddComponent<Interactable>();
                var pickup = go.AddComponent<PhotoPickup>();
                pickup.Photo = photo;
            };
            return pedestal;
        }

        /// <summary>
        /// A teleporter pad. With no <paramref name="onEnter"/> it moves the player to the next room's spawn.
        /// </summary>
        public Teleporter CreateTeleporter(Vector3 localPos, Action onEnter = null)
        {
            KeepClearAt(localPos, 1.7f);
            var go = new GameObject("Teleporter");
            go.transform.SetParent(WorldRoot, false);
            go.transform.localPosition = localPos;

            // Visual: base, glowing pad, four posts and a floating ring of slabs. No colliders.
            var t = go.transform;
            Geo.Visual("Base", t, Geo.PrismMesh(8), new Vector3(0f, 0.08f, 0f), Quaternion.identity, new Vector3(2.4f, 0.16f, 2.4f), Palette.Stone);
            var pad = Geo.Visual("Pad", t, Geo.PrismMesh(8), new Vector3(0f, 0.18f, 0f), Quaternion.identity, new Vector3(1.8f, 0.06f, 1.8f), Palette.Teal);
            pad.GetComponent<MeshRenderer>().sharedMaterial = Palette.GetEmissive(Palette.Teal, 0.8f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                Vector3 p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 1.3f, 1.05f);
                Geo.Visual("Post", t, Geo.CubeMesh, p, Quaternion.Euler(0f, a, 0f), new Vector3(0.18f, 2.6f, 0.18f), Palette.Cream);
            }
            var ring = new GameObject("Ring").transform;
            ring.SetParent(t, false);
            ring.localPosition = new Vector3(0f, 2.75f, 0f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                Vector3 p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 1.05f);
                var seg = Geo.Visual("Seg", ring, Geo.CubeMesh, p, Quaternion.Euler(0f, a, 0f), new Vector3(0.85f, 0.14f, 0.14f), Palette.Teal);
                seg.GetComponent<MeshRenderer>().sharedMaterial = Palette.GetEmissive(Palette.Teal, 0.5f);
            }
            ring.gameObject.AddComponent<Spin>().DegreesPerSecond = 35f;
            go.AddComponent<TeleporterFx>();

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.6f, 2.4f, 1.6f);
            box.center = new Vector3(0f, 1.2f, 0f);

            go.AddComponent<Interactable>();
            var tp = go.AddComponent<Teleporter>();
            int next = Index + 1;
            var game = _game;
            tp.OnEnter = onEnter ?? (() => { if (game != null) game.GoToRoom(next); });
            return tp;
        }

        /// <summary>
        /// A coloured floor marker (flat hexagon) that toasts <paramref name="hint"/> when the player steps on it.
        /// </summary>
        public GameObject CreateMarker(Vector3 localPos, Color color, string hint, float radius = 1.1f)
        {
            return CreateMarker(localPos, 0f, color, hint, radius);
        }

        /// <summary>Marker whose arrow points along <paramref name="yaw"/> (0 = room +Z).</summary>
        public GameObject CreateMarker(Vector3 localPos, float yaw, Color color, string hint, float radius = 1.1f)
        {
            KeepClearAt(localPos, radius + 0.35f);
            var go = new GameObject("Marker");
            go.transform.SetParent(WorldRoot, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Geo.Visual("Pad", go.transform, Geo.PrismMesh(6), new Vector3(0f, 0.03f, 0f), Quaternion.identity,
                       new Vector3(radius * 2f, 0.06f, radius * 2f), color);
            // A flat triangle pointing where the player should look (triangular prism, tip rotated to +Z).
            Geo.Visual("Arrow", go.transform, Geo.PrismMesh(3), new Vector3(0f, 0.07f, radius * 0.35f), Quaternion.Euler(0f, 90f, 0f),
                       new Vector3(radius * 0.8f, 0.04f, radius * 0.8f), Palette.Cream);
            if (!string.IsNullOrEmpty(hint))
            {
                var zone = go.AddComponent<HintZone>();
                zone.Text = hint;
                zone.Radius = radius + 0.3f;
            }
            // Standing on a marker makes it the checkpoint: a fall respawns here, facing the puzzle.
            go.AddComponent<PlayerSpawn>().CheckpointRadius = radius;
            return go;
        }

        /// <summary>An invisible hint area (toasts once per entry).</summary>
        public HintZone CreateHint(Vector3 localPos, float radius, string hint, float seconds = 4.5f)
        {
            var go = new GameObject("Hint");
            go.transform.SetParent(WorldRoot, false);
            go.transform.localPosition = localPos;
            var zone = go.AddComponent<HintZone>();
            zone.Text = hint;
            zone.Radius = radius;
            zone.Seconds = seconds;
            return zone;
        }

        /// <summary>A floating instant camera; walking into it unlocks the camera with <paramref name="film"/> shots.</summary>
        public CameraPickup CreateCameraPickup(Vector3 localPos, int film = 3)
        {
            KeepClearAt(localPos, 0.9f);
            var go = new GameObject("CameraPickup");
            go.transform.SetParent(WorldRoot, false);
            go.transform.localPosition = localPos;
            var t = go.transform;

            // Pedestal is world geometry (sliceable), so it is a sibling, not a child of the Interactable.
            Geo.Prism(WorldRoot, localPos + new Vector3(0f, 0.45f, 0f), new Vector3(0.7f, 0.9f, 0.7f), Palette.Cream, 6);
            var vis = new GameObject("Visual").transform;
            vis.SetParent(t, false);
            vis.localPosition = new Vector3(0f, 1.3f, 0f);
            Geo.Visual("Body", vis, Geo.CubeMesh, Vector3.zero, Quaternion.identity, new Vector3(0.5f, 0.36f, 0.3f), Palette.Coral);
            Geo.Visual("Lens", vis, Geo.PrismMesh(8), new Vector3(0f, -0.02f, -0.18f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.22f, 0.1f, 0.22f), Palette.Ink);
            Geo.Visual("Slot", vis, Geo.CubeMesh, new Vector3(0f, 0.2f, 0.02f), Quaternion.identity, new Vector3(0.36f, 0.05f, 0.2f), Palette.Cream);
            Geo.Visual("Flash", vis, Geo.CubeMesh, new Vector3(0.16f, 0.12f, -0.16f), Quaternion.identity, new Vector3(0.1f, 0.07f, 0.02f), Palette.Butter);
            vis.localScale = Vector3.one * 1.3f;
            var spin = vis.gameObject.AddComponent<Spin>();
            spin.DegreesPerSecond = 60f;
            spin.BobHeight = 0.08f;

            go.AddComponent<Interactable>();
            var pickup = go.AddComponent<CameraPickup>();
            pickup.Film = film;
            pickup.Visual = vis.gameObject;
            return pickup;
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

        /// <summary>World-space text (legacy uGUI Text on a world canvas). Faces −forward of <paramref name="localRotation"/>.</summary>
        public GameObject Label(Vector3 localPos, Quaternion localRotation, string text, int fontSize = 48, Color? color = null, float width = 3f)
        {
            return WorldText.Create(WorldRoot, localPos, localRotation, text, fontSize, color ?? Palette.Ink, width);
        }

        /// <summary>Toasts on the HUD if it exists.</summary>
        public static void Toast(string text, float seconds = 3.5f)
        {
            var hud = Hud.Instance != null ? Hud.Instance : UnityEngine.Object.FindFirstObjectByType<Hud>();
            if (hud != null) hud.Toast(text, seconds);
        }
    }
}
