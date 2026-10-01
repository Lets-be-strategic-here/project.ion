using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;
using UnityEngine.UI;

namespace Ion.Levels
{
    /// <summary>
    /// Room 5 — Gallery. A calm courtyard where the player's own photos hang: the left wall holds the
    /// three pre-made photos (Bridge, Open sky, Stairs), the right wall the instant-camera snapshots from
    /// room 4, each captioned with its room. The final teleporter waits at the far end.
    /// </summary>
    public sealed class GalleryRoom : Room
    {
        public override string Title => "Room 5: Gallery";
        public override string Intro => "Your photos hang here.";

        const float WallX = 9.5f;
        // Off-white paper mat and a slightly warmer print area: lit like the walls (a bare frame no longer
        // shows an unlit, stark white image).
        static readonly Color PaperMat = new Color32(0xF5, 0xEF, 0xE4, 0xFF);
        static readonly Color PaperPrint = new Color32(0xEA, 0xE1, 0xD2, 0xFF);
        // An empty snapshot frame holds an "undeveloped" instant print: dark slate with a soft sheen band.
        static readonly Color UndevelopedPrint = new Color32(0x3C, 0x42, 0x52, 0xFF);
        static readonly Color UndevelopedSheen = new Color32(0x5A, 0x62, 0x76, 0xFF);
        static readonly float[] FrameZ = { 2f, 8f, 14f };

        public override void Build(Transform root, RoomContext ctx)
        {
            Kit.Island(root, -12f, 12f, -10f, 26f, 0f, 51);

            // Paved path.
            Geo.Box(root, new Vector3(0f, 0.02f, 8f), new Vector3(4f, 0.04f, 30f), LevelColors.PathStone).name = "Path";

            int n = 0;
            var curator = root.gameObject.AddComponent<GalleryCurator>();
            for (int side = -1; side <= 1; side += 2)
            {
                float x = WallX * side;
                Geo.Box(root, new Vector3(x, 2f, 8f), new Vector3(0.6f, 4f, 18f), LevelColors.Wall).name = "GalleryWall";
                Geo.Box(root, new Vector3(x, 4.1f, 8f), new Vector3(0.9f, 0.2f, 18.4f), LevelColors.Trim).name = "GalleryWallCap";
                Kit.ContactStrip(root, new Vector3(x, 0f, 8f), 2.8f, 20f, 0f, LevelColors.Contact);

                float face = x - side * 0.3f;          // inner face of the wall
                Quaternion readRot = Quaternion.LookRotation(new Vector3(side, 0f, 0f)); // forward points into the wall
                for (int i = 0; i < FrameZ.Length; i++, n++)
                {
                    float z = FrameZ[i];
                    Geo.Box(root, new Vector3(face - side * 0.06f, 2.2f, z), new Vector3(0.12f, 2.1f, 3f), Palette.Wood).name = "Frame";
                    Geo.Box(root, new Vector3(face - side * 0.1f, 2.2f, z), new Vector3(0.08f, 1.7f, 2.6f), PaperMat).name = "Canvas";
                    // The print area behind the photo (what an empty frame shows): the snapshot wall
                    // (n >= 3) shows a dark undeveloped instant print with a faint sheen until filled.
                    bool snapshotFrame = n >= GalleryCurator.PreMade;
                    Geo.Soft("Print", root, Geo.CubeMesh, new Vector3(face - side * 0.143f, 2.36f, z), Quaternion.identity,
                             new Vector3(0.008f, 1.215f, 1.62f), snapshotFrame ? UndevelopedPrint : PaperPrint);
                    if (snapshotFrame)
                    {
                        Geo.Soft("Sheen", root, Geo.CubeMesh, new Vector3(face - side * 0.147f, 2.68f, z), Quaternion.identity,
                                 new Vector3(0.004f, 0.16f, 1.62f), UndevelopedSheen);
                        Geo.Soft("Sheen", root, Geo.CubeMesh, new Vector3(face - side * 0.147f, 2.52f, z), Quaternion.identity,
                                 new Vector3(0.004f, 0.05f, 1.62f), UndevelopedSheen);
                    }
                    // The photo (filled in by GalleryCurator) and its caption on the mat.
                    var photo = GalleryCurator.CreatePhoto(root, new Vector3(face - side * 0.152f, 2.36f, z), readRot, 1.62f, 1.215f);
                    var caption = ctx.Label(new Vector3(face - side * 0.16f, 1.55f, z), readRot, "", 28, Palette.Ink, 2.6f);
                    curator.Add(photo, caption.GetComponentInChildren<Text>());
                    // Little plinth light under each frame.
                    Geo.Box(root, new Vector3(face - side * 0.5f, 0.25f, z), new Vector3(0.6f, 0.5f, 1.2f), Palette.Cream).name = "Plinth";
                    Kit.Contact(root, new Vector3(face - side * 0.5f, 0f, z), 1.3f, 1.9f, 0f, LevelColors.Contact);
                }
            }

            // Benches.
            Geo.Box(root, new Vector3(-3.5f, 0.45f, 8f), new Vector3(0.8f, 0.1f, 3f), Palette.Wood).name = "Bench";
            Geo.Box(root, new Vector3(-3.5f, 0.2f, 7f), new Vector3(0.6f, 0.4f, 0.3f), Palette.DarkWood).name = "BenchLeg";
            Geo.Box(root, new Vector3(-3.5f, 0.2f, 9f), new Vector3(0.6f, 0.4f, 0.3f), Palette.DarkWood).name = "BenchLeg";
            Geo.Box(root, new Vector3(3.5f, 0.45f, 8f), new Vector3(0.8f, 0.1f, 3f), Palette.Wood).name = "Bench";
            Geo.Box(root, new Vector3(3.5f, 0.2f, 7f), new Vector3(0.6f, 0.4f, 0.3f), Palette.DarkWood).name = "BenchLeg";
            Geo.Box(root, new Vector3(3.5f, 0.2f, 9f), new Vector3(0.6f, 0.4f, 0.3f), Palette.DarkWood).name = "BenchLeg";
            Kit.Contact(root, new Vector3(-3.5f, 0f, 8f), 1.5f, 3.8f, 0f, LevelColors.Contact);
            Kit.Contact(root, new Vector3(3.5f, 0f, 8f), 1.5f, 3.8f, 0f, LevelColors.Contact);

            // Greenery.
            Kit.Tree(root, new Vector3(-8f, 0f, -7f), 1.2f, 10f, true);
            Kit.Tree(root, new Vector3(8f, 0f, -6.5f), 1f, 50f);
            Kit.Tree(root, new Vector3(-8.5f, 0f, 22f), 1.3f, 30f);
            Kit.Tree(root, new Vector3(8.5f, 0f, 23f), 1.1f, 0f, true);
            Kit.Rock(root, new Vector3(-5f, 0f, -8f), 0.8f, 100f);
            Kit.Rock(root, new Vector3(5.5f, 0f, 20f), 1f, 220f);

            // Title above the exit.
            ctx.Label(new Vector3(0f, 4.5f, 21f), Quaternion.identity, "[project]ion", 110, Palette.Ink, 8f);
            ctx.Label(new Vector3(0f, 3.65f, 21f), Quaternion.identity, "step through to finish", 40, Palette.Slate, 8f);

            ctx.SetSpawn(new Vector3(0f, 0f, -6f));
            ctx.CreateHint(new Vector3(0f, 0f, 8f), 3f, "Every photo from your trip hangs here.", 3.5f).Once = true;

            AddSolution("exit", RoomSolution.Kind.Goal, new Vector3(0f, 0f, 21f));

            var spawn = ctx.Spawn;
            // The last teleporter brings the player back to the courtyard entrance and opens the end card
            // (Play again / View projects) behind the fade-to-white.
            // The exit pad stands on the paved path: a path-coloured contact shade on top of it.
            Kit.Contact(root, new Vector3(0f, 0.04f, 21f), 3.4f, 3.4f, 0f, LevelColors.PathStone);
            ctx.CreateTeleporter(new Vector3(0f, 0f, 21f), () =>
            {
                var player = Ion.Gameplay.FirstPersonController.Current;
                if (player != null) player.Teleport(spawn.position, spawn.rotation.eulerAngles.y);
                if (EndCard.Instance != null) EndCard.Instance.Open();
                else RoomContext.Toast("Thanks for playing [project]ion", 7f);
            });
        }
    }

    /// <summary>
    /// Hangs the player's photos in the gallery frames: slots 0-2 get the pre-made photos of rooms 1-3,
    /// slots 3-5 the instant-camera snapshots taken this run (newest last), captioned with their room.
    /// Refreshed twice a second (cheap: texture assignments only on change); forgets the snapshots on
    /// "Play again".
    /// </summary>
    public sealed class GalleryCurator : MonoBehaviour
    {
        public const int PreMade = 3;

        readonly List<RawImage> _photos = new List<RawImage>();
        readonly List<Text> _captions = new List<Text>();
        readonly List<PhotoData> _snapshots = new List<PhotoData>();
        InstantCamera _camera;
        float _next;

        /// <summary>The photo currently hung in each frame (null = empty frame).</summary>
        public readonly List<PhotoData> Hung = new List<PhotoData>();

        public void Add(RawImage photo, Text caption)
        {
            _photos.Add(photo);
            _captions.Add(caption);
            Hung.Add(null);
            Apply(_photos.Count - 1, null, "");
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

        /// <summary>Re-assigns every frame now (tests / debug).</summary>
        public void Refresh()
        {
            if (_camera == null)
            {
                var player = FirstPersonController.Current;
                _camera = player != null ? player.GetComponent<InstantCamera>() : null;
                if (_camera != null) _camera.Captured += OnCaptured;
            }

            var game = GameBootstrap.Instance;
            for (int i = 0; i < _photos.Count; i++)
            {
                PhotoData photo = null;
                string caption;
                if (i < PreMade)
                {
                    RoomContext ctx = game != null && i < game.Rooms.Count ? game.Rooms[i] : null;
                    photo = ctx != null ? ctx.GetShotPhoto(0) : null;
                    caption = ctx != null ? ctx.Room.Title : "";
                }
                else
                {
                    int k = i - PreMade;
                    // The newest snapshots, oldest of them first.
                    int first = Mathf.Max(0, _snapshots.Count - (_photos.Count - PreMade));
                    photo = first + k < _snapshots.Count ? _snapshots[first + k] : null;
                    caption = photo != null ? "Your snapshot" : "Unused film";
                }
                Apply(i, photo, caption);
            }
        }

        void Apply(int i, PhotoData photo, string caption)
        {
            if (_captions[i] != null && _captions[i].text != caption) _captions[i].text = caption;
            // An empty frame shows the lit paper print area behind the image (no unlit white rectangle).
            bool show = photo != null && photo.Preview != null;
            if (_photos[i].enabled != show) _photos[i].enabled = show;
            if (Hung[i] == photo && _photos[i].texture == (photo != null ? photo.Preview : null)) return;
            Hung[i] = photo;
            var img = _photos[i];
            img.texture = photo != null ? photo.Preview : null;
            img.color = Color.white;
        }

        /// <summary>A world-space photo (unlit RawImage on its own canvas), <paramref name="w"/> x <paramref name="h"/> metres.</summary>
        public static RawImage CreatePhoto(Transform parent, Vector3 localPos, Quaternion localRotation, float w, float h)
        {
            const float pixelsPerMetre = 100f;
            var go = new GameObject("GalleryPhoto", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.localPosition = localPos;
            rt.localRotation = localRotation;
            rt.localScale = Vector3.one / pixelsPerMetre;
            rt.sizeDelta = new Vector2(w * pixelsPerMetre, h * pixelsPerMetre);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var imgGo = new GameObject("Image", typeof(RectTransform));
            var irt = (RectTransform)imgGo.transform;
            irt.SetParent(rt, false);
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
            var img = imgGo.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }
    }
}
