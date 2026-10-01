using System.Collections;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;

namespace Ion.Levels
{
    /// <summary>
    /// The only object in Main.unity. Builds the whole game in Awake:
    /// atmosphere → ProjectionSystem → rooms (world + dioramas) → UI → player, then captures the
    /// diorama photos one frame later (so every renderer/collider exists) and binds the HUD.
    ///
    /// Layout: room i's world root is at (i·RoomSpacing, 0, 0); each room is a set of floating islands
    /// whose walking direction is +Z. Puzzles are therefore solved looking along ±Z, so a photo's
    /// 250 m view cone never reaches the neighbouring rooms on X. Dioramas sit at
    /// (i·DioramaSpacing, DioramaY, 0) and are deactivated once captured.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public const float RoomSpacing = 50f;
        public const float DioramaY = -1000f;
        public const float DioramaSpacing = 200f;
        /// <summary>Below this (and above the dioramas) the player is put back at the current room's spawn.</summary>
        public const float ResetY = -50f;

        public static GameBootstrap Instance { get; private set; }

        readonly List<Room> _rooms = new List<Room>();
        readonly List<RoomContext> _contexts = new List<RoomContext>();
        FirstPersonController _player;
        int _currentRoom;
        Transform _worldContainer;
        Transform _dioramaContainer;

        public IReadOnlyList<RoomContext> Rooms => _contexts;
        public int CurrentRoom => _currentRoom;
        public FirstPersonController Player => _player;

        /// <summary>Camera height above the feet; diorama shots are taken from this height.</summary>
        public float EyeHeight => PlayerFactory.EyeHeight;

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

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

            // 3. Rooms.
            _rooms.Add(new BridgeRoom());
            _rooms.Add(new DoorwayRoom());
            _rooms.Add(new StairsRoom());
            _rooms.Add(new CameraRoom());
            _rooms.Add(new GalleryRoom());
            BuildRooms();

            // 4. UI.
            UIFactory.Create();

            // 5. Player at room 1.
            Pose spawn = _contexts[0].Spawn;
            _player = PlayerFactory.Create(spawn.position, spawn.rotation.eulerAngles.y);
            _player.SetCheckpoint(spawn.position, spawn.rotation.eulerAngles.y);
            _player.KillY = ResetY; // the controller respawns at its last checkpoint (room spawn or far side)
            _currentRoom = 0;

            // 6. Photos (next frame) + HUD binding.
            StartCoroutine(CaptureDioramas());
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void BuildRooms()
        {
            int merged = 0;
            _worldContainer = new GameObject("World").transform;
            for (int i = 0; i < _rooms.Count; i++)
            {
                Room room = _rooms[i];
                var root = new GameObject("Room " + (i + 1) + " (" + room.Title + ")").transform;
                root.SetParent(_worldContainer, false);
                root.localPosition = new Vector3(i * RoomSpacing, 0f, 0f);

                var ctx = new RoomContext(this, i, room, root, new Vector3(i * DioramaSpacing, DioramaY, 0f));
                _contexts.Add(ctx);
                try
                {
                    room.Build(root, ctx);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }
                try
                {
                    // Grass, flowers and paths, then one merged mesh per decor colour (world + diorama).
                    Scatter.Decorate(ctx);
                    merged += DecorCombiner.Combine(root);
                    if (ctx.HasDiorama) merged += DecorCombiner.Combine(ctx.DioramaRoot);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }
            }
            Debug.Log("[Ion] Decor: merged " + merged + " renderers into per-room meshes.");
        }

        IEnumerator CaptureDioramas()
        {
            // Let one frame pass so renderers have bounds and colliders/transforms are synced.
            yield return null;
            Physics.SyncTransforms();

            var ps = ProjectionSystem.Instance;
            for (int r = 0; r < _contexts.Count; r++)
            {
                var shots = _contexts[r].Shots;
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
            }

            var hud = Hud.Instance != null ? Hud.Instance : Object.FindFirstObjectByType<Hud>();
            if (hud != null && _player != null)
                hud.BindInventory(_player.GetComponent<PhotoInventory>());

            AnnounceRoom(0);

            // Captured photos hold their own clipped meshes; the dioramas are no longer needed once the
            // previews exist. When the render pipeline was not up yet at capture time (first frames of
            // the Web player), ProjectionSystem renders the previews in a later LateUpdate, so keep the
            // dioramas visible until it has; deactivating them earlier gave sky-only (blank) photos.
            yield return null;
            for (int i = 0; i < 600 && ps != null && ps.PendingPreviewCount > 0; i++)
                yield return null;
            if (ps != null && ps.PendingPreviewCount > 0)
                Debug.LogWarning("[GameBootstrap] Photo previews still pending; hiding the dioramas anyway.");
            if (_dioramaContainer != null) _dioramaContainer.gameObject.SetActive(false);
        }

        /// <summary>Moves the player to room <paramref name="index"/>'s spawn (no-op past the last room).</summary>
        public void GoToRoom(int index)
        {
            if (index < 0 || index >= _contexts.Count || _player == null) return;
            _currentRoom = index;
            Pose spawn = _contexts[index].Spawn;
            float yaw = spawn.rotation.eulerAngles.y;
            _player.Teleport(spawn.position, yaw);
            _player.SetCheckpoint(spawn.position, yaw);
            AnnounceRoom(index);
        }

        void AnnounceRoom(int index)
        {
            Room room = _contexts[index].Room;
            string text = string.IsNullOrEmpty(room.Intro) ? room.Title : room.Title + "  -  " + room.Intro;
            RoomContext.Toast(text, 4f);
        }

        void Update()
        {
            if (_player == null) return;
            float y = _player.transform.position.y;
            if (y < ResetY - 10f && y > -900f)
            {
                // Fallback (the controller normally respawns at its last checkpoint before this).
                Pose spawn = _contexts[_currentRoom].Spawn;
                _player.Teleport(spawn.position, spawn.rotation.eulerAngles.y);
                RoomContext.Toast("Whoops!", 2f);
            }
        }
    }
}
