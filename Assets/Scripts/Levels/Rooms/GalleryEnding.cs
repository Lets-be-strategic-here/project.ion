using System;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Levels.Arch;
using Ion.Levels.Props;
using Ion.Levels.Rooms;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;

namespace Ion.Levels.Rooms
{
    using Arch = Ion.Levels.Arch.Arch;

    /// <summary>
    /// Zone 5, the Gallery (art bible §7.4), mood Golden. A colonnade gallery: a Limestone/Checker floor, a nave of
    /// square columns at 4 m under a flat roof, and two long walls hung with Walnut-framed prints of every photo of
    /// the game (the coming-soon frames are paper blanks). At the far end a final teleporter opens the end card.
    /// </summary>
    public sealed class GalleryEnding : Room
    {
        public override string Key => "gallery";
        public override string Title => "Gallery";
        public override string Intro => "Your photos hang here.";
        public override ZoneMood Mood => ZoneMood.Golden;
        public override ArchStyle Style => ArchStyle.LightTable;

        public const float WallX = 7.5f;          // inner face of the long walls
        public static readonly float[] FrameZ = { 3f, 9f, 15f, 21f };
        public static readonly Vector2 ImageSize = new Vector2(1.5f, 1.125f);
        public const float FrameY = 2f;
        public static readonly Vector3 SpawnFeet = new Vector3(0f, 0f, -2f);
        public static readonly Vector3 ExitPad = new Vector3(0f, 0f, 28f);

        static readonly string[] Captions =
        {
            "[T1]  LEDGE", "[T2]  DARKROOM", "[02]  SIDEWAYS STAIRS", "[03]  YOUR SNAPSHOT",
            "[02]  STAIRS", "[03]  CAMERA", "[04]  COMING SOON", "[05]  COMING SOON",
        };

        public override void Build(Transform root, RoomContext ctx)
        {
            BuildGallery(root);
            ctx.SetSpawn(SpawnFeet, 0f);

            // Prints: left wall (x < 0) the photos used, right wall the hub keys and the reserved slots. Walnut gallery
            // frames (PropKit); their print cards are filled at run time by the curator (snapshots arrive in play).
            var curator = root.gameObject.AddComponent<GalleryCurator>();
            int n = 0;
            foreach (int side in new[] { -1, 1 })
            {
                Dir facing = side < 0 ? Dir.PosX : Dir.NegX;
                for (int i = 0; i < FrameZ.Length; i++, n++)
                {
                    float z = FrameZ[i];
                    GameObject frame = PropKit.PictureFrame(root, new Vector3(side * WallX, FrameY, z), facing, ImageSize,
                                                            (Texture2D)null, FrameStyle.Gallery);
                    curator.Add(PropKit.FramePrint(frame), n);
                    PropKit.Plaque(root, new Vector3(side * WallX, 1f, z), facing, Captions[n]);
                }
            }

            Pose spawn = ctx.Spawn;
            ctx.Teleporter(ExitPad, Dir.NegZ, () =>
            {
                var player = FirstPersonController.Current;
                if (player != null) player.Teleport(spawn.position, spawn.rotation.eulerAngles.y);
                if (EndCard.Instance != null) EndCard.Instance.Open();
                else RoomContext.Toast("Thanks for playing [project]ion", 7f);
            }, Mat.Graphite);

            ctx.Hint(new Vector3(0f, 0f, 9f), 3f, "Every photo from your trip hangs here.", 3.5f);

            AddSolution("walk", RoomSolution.Kind.Walk, new Vector3(0f, 0f, 24f));
            AddSolution("exit", RoomSolution.Kind.Teleport, ExitPad);
        }

        public static void BuildGallery(Transform p)
        {
            var checker = new Surf(Mat.Limestone, Pat.Checker);

            Arch.Terrace(p, new RectXZ(-8f, -4f, 8f, 32f), 0f, 3f, Arch.Underside.Stepped, checker);

            // Long walls (x[7.5, 8]) with plinth, cornice and pilasters; the prints hang on their inner faces.
            foreach (int side in new[] { -1, 1 })
            {
                float cx = side * (WallX + 0.25f);
                Arch.Wall(p, new Vector3(cx, 0f, 0f), new Vector3(cx, 0f, 24f), Arch.Storey, Arch.WallThickness,
                          WallTrim.Default | WallTrim.Pilasters);
            }

            // The nave: square columns every 4 m under a flat roof.
            for (int i = 0; i <= 6; i++)
            {
                Arch.Column(p, new Vector3(-4f, 0f, i * 4f), Arch.Storey);
                Arch.Column(p, new Vector3(4f, 0f, i * 4f), Arch.Storey);
            }
            Arch.Roof(p, new RectXZ(-4.5f, -0.5f, 4.5f, 24.5f), Arch.Storey + 0.5f, 0.5f, 0.75f);
            // A coffered ceiling: Limestone downstand beams on the column grid (across every 4 m, along at x = ±1.5),
            // 0.375 deep under the roof slab (underside at 4.0), so the nave ceiling is not one flat slab (rule 4).
            var beam = new Surf(Mat.Limestone, Pat.Courses);
            for (int i = 0; i <= 6; i++)
            {
                float z = i * 4f;
                Arch.Box(p, new Vector3(-4.25f, 3.625f, z - 0.25f), new Vector3(4.25f, 4f, z + 0.25f), beam, ArchFlags.Soft);
            }
            foreach (float x in new[] { -4f, -1.5f, 1.5f, 4f })
                Arch.Box(p, new Vector3(x - 0.125f, 3.75f, -0.25f), new Vector3(x + 0.125f, 4f, 24.25f), beam, ArchFlags.Soft);
            // A Warm pendant in the middle of every other coffer.
            for (int i = 0; i < 6; i += 2)
                PropKit.Lamp(p, new Vector3(0f, 4f, i * 4f + 2f), 0f, LampStyle.Pendant, 1.25f, Mat.Warm);

            // Benches facing the walls, planting at the ends, parapets round the open court.
            foreach (float z in new[] { 6f, 18f })
            {
                PropKit.Bench(p, new Vector3(-2.25f, 0f, z), Dir.NegX, 2f);
                PropKit.Bench(p, new Vector3(2.25f, 0f, z), Dir.PosX, 2f);
            }
            PropKit.Planter(p, new Vector3(-5.75f, 0f, 28f), Dir.PosX, PlanterStyle.Bowl, default, 0.5f, 601);
            PropKit.Planter(p, new Vector3(5.75f, 0f, 28f), Dir.NegX, PlanterStyle.Bowl, default, 0.5f, 602);
            PropKit.Planter(p, new Vector3(-5.75f, 0f, -2.5f), Dir.PosX, PlanterStyle.Bowl, default, 0.5f, 603);
            PropKit.Planter(p, new Vector3(5.75f, 0f, -2.5f), Dir.NegX, PlanterStyle.Bowl, default, 0.5f, 604);
            Arch.Parapet(p, new Vector3(-7.875f, 0f, 24.5f), new Vector3(-7.875f, 0f, 32f));
            Arch.Parapet(p, new Vector3(7.875f, 0f, 24.5f), new Vector3(7.875f, 0f, 32f));
            Arch.Parapet(p, new Vector3(-8f, 0f, 31.875f), new Vector3(8f, 0f, 31.875f));
            Arch.Parapet(p, new Vector3(-8f, 0f, -3.875f), new Vector3(8f, 0f, -3.875f));
            // Café sets in the open court before the exit (the visit ends with a seat in the sun).
            foreach (int side in new[] { -1, 1 })
            {
                float cx = side * 4.5f;
                PropKit.Table(p, new Vector3(cx, 0f, 30f), Dir.NegZ, new Vector2(1f, 0.75f), 0.75f);
                PropKit.Chair(p, new Vector3(cx - 0.875f, 0f, 30f), 95f, ChairStyle.Cafe, side < 0 ? Mat.Teal : Mat.Mustard);
                PropKit.Chair(p, new Vector3(cx + 0.875f, 0f, 30.125f), -95f, ChairStyle.Cafe, side < 0 ? Mat.Mustard : Mat.Teal);
                PropKit.Chair(p, new Vector3(cx, 0f, 29.25f), 5f, ChairStyle.Cafe, Mat.Teal);
            }
            Arch.BracketFrame(p, ExitPad, Dir.NegZ, 2f, 3f, 0.5f);
        }
    }
}

namespace Ion.Levels
{
    using Ion.Levels.Props;
    using Ion.Levels.Rooms;

    /// <summary>
    /// Hangs the photos in the gallery frames' print cards (slot order: T1 stair, T2 door, the Stairs wing photo, the latest
    /// instant-camera snapshot, the Stairs key, the Camera key, then two coming-soon blanks). Refreshed twice a
    /// second (assignments only on change); forgets the snapshots on "Play again". Kept in Ion.Levels (EndCard).
    /// </summary>
    public sealed class GalleryCurator : MonoBehaviour
    {
        /// <summary>Slots filled from pre-made photos (legacy constant).</summary>
        public const int PreMade = 3;

        readonly List<PrintCard> _prints = new List<PrintCard>();
        readonly List<int> _slots = new List<int>();
        readonly List<PhotoData> _snapshots = new List<PhotoData>();
        InstantCamera _camera;
        float _next;

        /// <summary>The photo currently hung in each frame (null = blank).</summary>
        public readonly List<PhotoData> Hung = new List<PhotoData>();

        public int Count => _prints.Count;

        internal void Add(PrintCard print, int slot)
        {
            _prints.Add(print);
            _slots.Add(slot);
            Hung.Add(null);
        }

        void OnEnable() => GameBootstrap.Restarted += OnRestarted;

        void OnDisable() => GameBootstrap.Restarted -= OnRestarted;

        void OnDestroy()
        {
            if (_camera != null) _camera.Captured -= OnCaptured;
        }

        void OnRestarted() => _snapshots.Clear();

        void OnCaptured(PhotoData p)
        {
            if (p != null && !_snapshots.Contains(p)) _snapshots.Add(p);
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            Refresh();
        }

        /// <summary>The photo for gallery slot <paramref name="slot"/> (null = blank).</summary>
        public PhotoData SlotPhoto(int slot)
        {
            var game = GameBootstrap.Instance;
            if (game == null) return null;
            switch (slot)
            {
                case 0: { var c = game.Context<TutorialLedge>(); return c != null ? c.GetShotPhoto(0) : null; }
                case 1: { var c = game.Context<TutorialDarkroom>(); return c != null ? c.GetShotPhoto(0) : null; }
                case 2: { var c = game.Context<StairsWing>(); return c != null ? c.GetShotPhoto(0) : null; }
                case 3: return _snapshots.Count > 0 ? _snapshots[_snapshots.Count - 1] : null;
                case 4: { var c = game.Context<HubLightTable>(); return c != null ? c.GetShotPhoto(0) : null; }
                case 5: { var c = game.Context<HubLightTable>(); return c != null ? c.GetShotPhoto(1) : null; }
                default: return null;
            }
        }

        /// <summary>Re-assigns every frame now (tests / end card).</summary>
        public void Refresh()
        {
            if (_camera == null)
            {
                var player = FirstPersonController.Current;
                _camera = player != null ? player.GetComponent<InstantCamera>() : null;
                if (_camera != null) _camera.Captured += OnCaptured;
            }
            for (int i = 0; i < _prints.Count; i++)
            {
                PhotoData photo = SlotPhoto(_slots[i]);
                Texture2D image = photo != null ? photo.Preview : null;
                PrintCard card = _prints[i];
                if (card != null && card.Image != image) card.Image = image;
                Hung[i] = image != null ? photo : null;
            }
        }
    }
}
