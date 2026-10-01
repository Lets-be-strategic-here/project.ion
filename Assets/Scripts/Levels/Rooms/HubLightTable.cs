using System;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Levels.Arch;
using Ion.Levels.Props;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;

namespace Ion.Levels.Rooms
{
    using Arch = Ion.Levels.Arch.Arch;

    /// <summary>
    /// Zone 2, the hub "Light Table" (art bible §7.3), mood Noon. A floating plinth x/z[−11.5, 11.5] floored as a
    /// 3 × 3 contact sheet of 6 m cells (Limestone/Tile) with 1 m Terrazzo bands.
    ///  - Centre: the light table (PropKit.LightTable, 4 × 2, Frost top) under a coffered canopy: every photo of the
    ///    game as a slide-mounted print (front row, read from the arrival side), a Frost blank until known, an Ion
    ///    border once solved; the back row holds the coming-soon blanks.
    ///  - W: arrival from T2 (checkpoint "hub.arrive"), benches, rug, the only literal "[project]ion" plaque, and a
    ///    graphite pod that replays the tutorial.
    ///  - N / S: exhibits [02] Stairs and [03] Camera. Pick the key photo off the exhibit, stand on the brass
    ///    marker facing out, place it: the plain screen at the plinth edge opens into a bracket niche holding a
    ///    working teleporter to the wing (the photo copy shares its OnEnter).
    ///  - E: the ending pod, under a floor hatch until both wings are solved; it then rises and leads to the Gallery.
    ///  - Corners: [04]–[07] coming-soon stands and screens (reserved slots: a wing is added without touching the hub).
    /// </summary>
    public sealed class HubLightTable : Room
    {
        public enum Wing { Stairs, Camera }

        public override string Key => "hub";
        public override string Title => "Light Table";
        public override string Intro => "Pick a print.  Project it.";
        public override ZoneMood Mood => ZoneMood.Noon;
        public override ArchStyle Style => ArchStyle.LightTable;

        public const float Half = 11.5f;
        public const float ScreenZ = 10.75f;
        public static readonly Vector3 ArrivalFeet = new Vector3(-7f, 0f, 0f);
        public const float ArrivalYaw = 90f;
        public static readonly Vector3 NorthMarker = new Vector3(0f, 0f, 7f);
        public static readonly Vector3 SouthMarker = new Vector3(0f, 0f, -7f);
        public static readonly Vector3 NorthPad = new Vector3(0f, 0f, 12.5f);
        public static readonly Vector3 SouthPad = new Vector3(0f, 0f, -12.5f);
        public static readonly Vector3 EndingPad = new Vector3(7f, 0f, 0f);
        public static readonly Vector3 TutorialPod = new Vector3(-9.5f, 0f, 0f);

        public DioramaShot StairsKey { get; private set; }
        public DioramaShot CameraKey { get; private set; }
        public ExhibitStand StairsExhibit { get; private set; }
        public ExhibitStand CameraExhibit { get; private set; }
        public readonly List<ExhibitStand> ComingSoon = new List<ExhibitStand>();
        public HubState State { get; private set; }
        public EndingHatch Hatch { get; private set; }
        /// <summary>The contact sheet on the light table (PropKit.LightTable): slot 0–3 front row, 4–7 back row.</summary>
        public LightTableView Prints { get; private set; }

        public override void Build(Transform root, RoomContext ctx)
        {
            BuildHub(root, false);
            ctx.SetSpawn(ArrivalFeet, ArrivalYaw);

            // The key photos: the plinth edge with each screen opened into a niche holding the wing's teleporter.
            StairsKey = ctx.RegisterDioramaShot(NorthMarker, 0f, 0f, "[02] Stairs", id: "hub.stairs");
            CameraKey = ctx.RegisterDioramaShot(SouthMarker, 180f, 0f, "[03] Camera", id: "hub.camera");
            BuildHub(ctx.DioramaRoot, true);
            GameBootstrap game = ctx.Game;
            Action toStairs = () => { if (game != null) game.GoToRoom(game.IndexOf<StairsWing>()); };
            Action toCamera = () => { if (game != null) game.GoToRoom(game.IndexOf<CameraWing>()); };
            ctx.DioramaTeleporter(NorthPad, Dir.NegZ, toStairs, Mat.Mint);
            ctx.DioramaTeleporter(SouthPad, Dir.PosZ, toCamera, Mat.Rose);

            // ---- exhibits ----
            StairsExhibit = PropKit.Exhibit(root, new Vector3(0f, 0f, 5f), Dir.NegZ,
                new ExhibitSpec { Number = "02", Title = "Stairs", Key = StairsKey, State = ExhibitState.Available });
            CameraExhibit = PropKit.Exhibit(root, new Vector3(0f, 0f, -5f), Dir.PosZ,
                new ExhibitSpec { Number = "03", Title = "Camera", Key = CameraKey, State = ExhibitState.Available });
            int n = 4;
            foreach (int sz in new[] { 1, -1 })
            foreach (int sx in new[] { -1, 1 })
            {
                ComingSoon.Add(PropKit.Exhibit(root, new Vector3(7f * sx, 0f, 5f * sz), sz > 0 ? Dir.NegZ : Dir.PosZ,
                    new ExhibitSpec { Number = n.ToString("00"), Title = "coming soon", Key = null, State = ExhibitState.ComingSoon }));
                n++;
            }
            ctx.Marker(NorthMarker, 0f);
            ctx.Marker(SouthMarker, 180f);

            // ---- W cell: arrival ----
            PropKit.Checkpoint(root, ArrivalFeet, ArrivalYaw, "hub.arrive");
            ctx.Teleporter(TutorialPod, Dir.PosX, () => { if (game != null) game.GoToRoom(0); }, Mat.Graphite);
            PropKit.Pedestal(root, new Vector3(-5f, 0f, -2.25f), Dir.NegX, PedestalSize.Tall);
            PropKit.Plaque(root, new Vector3(-5.25f, 0.875f, -2.25f), Dir.NegX, "[project]ion");
            PropKit.Pedestal(root, new Vector3(-9.5f, 0f, 1.75f), Dir.PosX, PedestalSize.Low);
            PropKit.Plaque(root, new Vector3(-9.25f, 0.3125f, 1.75f), Dir.PosX, "[01]  TUTORIAL");

            // ---- centre: the light table and its contact sheet, read from the arrival side (W) ----
            Prints = PropKit.LightTable(root, Vector3.zero, Dir.NegX, 4, 2);

            // ---- E cell: the ending pod under its hatch ----
            Action toGallery = () => { if (game != null) game.GoToRoom(game.IndexOf<GalleryEnding>()); };
            Hatch = EndingHatch.Create(root, EndingPad, toGallery);

            State = root.gameObject.AddComponent<HubState>();
            State.Init(this, ctx);

            // ---- routes (each exhibit is its own route; see the wings for what follows) ----
            AddSolution("arrive", RoomSolution.Kind.Walk, ArrivalFeet, ArrivalYaw);
            // Routes go round the 6 × 3 light table (z ±3.125) and the pergola columns at the centre cell corners.
            AddSolution("n.pickup", RoomSolution.Kind.Pickup, new Vector3(0f, 0f, 3.75f), 0f).Via =
                new[] { new Vector3(-2.5f, 0f, 2.75f), new Vector3(-2.25f, 0f, 3.75f) };
            AddSolution("n.place", RoomSolution.Kind.Place, NorthMarker, 0f, 0f, 0).Via =
                new[] { new Vector3(1.75f, 0f, 3.75f), new Vector3(1.75f, 0f, 6.5f) };
            var nFar = AddSolution("n.far", RoomSolution.Kind.Goal, new Vector3(0f, 0f, ScreenZ));
            nFar.AfterPlace = true;
            AddSolution("n.enter", RoomSolution.Kind.Teleport, NorthPad).Destination = "stairs";
            AddSolution("s.pickup", RoomSolution.Kind.Pickup, new Vector3(0f, 0f, -3.75f), 180f).Via =
                new[] { new Vector3(-2.5f, 0f, -2.75f), new Vector3(-2.25f, 0f, -3.75f) };
            AddSolution("s.place", RoomSolution.Kind.Place, SouthMarker, 180f, 0f, 1).Via =
                new[] { new Vector3(1.75f, 0f, -3.75f), new Vector3(1.75f, 0f, -6.5f) };
            var sFar = AddSolution("s.far", RoomSolution.Kind.Goal, new Vector3(0f, 0f, -ScreenZ), 180f);
            sFar.AfterPlace = true;
            AddSolution("s.enter", RoomSolution.Kind.Teleport, SouthPad, 180f).Destination = "camera";
            var ending = AddSolution("ending", RoomSolution.Kind.Teleport, EndingPad, 90f);
            ending.Destination = "gallery";
            ending.Via = new[] { new Vector3(-2.5f, 0f, 2.75f), new Vector3(-2.25f, 0f, 3.75f), new Vector3(2.25f, 0f, 3.75f),
                                 new Vector3(2.5f, 0f, 2.75f) };
        }

        protected internal override void OnEnter(RoomContext ctx)
        {
            if (State != null) State.MarkTutorialDone();
        }

        protected internal override void OnRestart(RoomContext ctx)
        {
            if (State != null) State.ResetState();
        }

        /// <summary>
        /// A wing's exit teleporter: marks the wing solved (exhibit lamp brass, print bordered Ion) and returns the
        /// player to that exhibit's marker, facing the hub centre. Hub entry is a checkpoint.
        /// </summary>
        public static void ReturnFromWing(Wing wing)
        {
            var game = GameBootstrap.Instance;
            if (game == null) return;
            int hub = game.IndexOf<HubLightTable>();
            if (hub < 0) return;
            var room = (HubLightTable)game.Rooms[hub].Room;
            if (room.State != null) room.State.SetSolved(wing, true);
            bool north = wing == Wing.Stairs;
            game.GoToZone(hub, north ? NorthMarker : SouthMarker, north ? 180f : 0f);
        }

        /// <summary>Exhibit of a wing.</summary>
        public ExhibitStand ExhibitOf(Wing wing) => wing == Wing.Stairs ? StairsExhibit : CameraExhibit;

        // ==================================================================== architecture

        static readonly float[] CellCentres = { -7f, 0f, 7f };

        /// <summary>
        /// The hub's architecture. The diorama version (<paramref name="diorama"/>) opens the N and S screens into
        /// teleporter niches and has no ending well; everything a key photo can see is identical in both.
        /// </summary>
        public static void BuildHub(Transform p, bool diorama)
        {
            ArchStyle st = Arch.Style ?? ArchStyle.LightTable;
            Surf cell = st.Floor;
            var band = new Surf(Mat.Limestone, Pat.Terrazzo);

            // ---- the contact-sheet floor: 3 × 3 cells of 6 m, 1 m Terrazzo bands, 1.5 m outer band ----
            // Every cell is framed like a frame of a contact sheet: a 0.25 Concrete kerb flush with the floor and a
            // brass inlay on its inner edge, so the 3 × 3 sheet reads at standing height.
            var kerb = new Surf(Mat.Concrete, Pat.Courses);
            foreach (float cx in CellCentres)
            foreach (float cz in CellCentres)
            {
                RectXZ c = RectXZ.Centered(cx, cz, 6f, 6f);
                Arch.Box(p, new Vector3(c.X0, -0.5f, c.Z0), new Vector3(c.X1, 0f, c.Z0 + 0.25f), kerb);
                Arch.Box(p, new Vector3(c.X0, -0.5f, c.Z1 - 0.25f), new Vector3(c.X1, 0f, c.Z1), kerb);
                Arch.Box(p, new Vector3(c.X0, -0.5f, c.Z0 + 0.25f), new Vector3(c.X0 + 0.25f, 0f, c.Z1 - 0.25f), kerb);
                Arch.Box(p, new Vector3(c.X1 - 0.25f, -0.5f, c.Z0 + 0.25f), new Vector3(c.X1, 0f, c.Z1 - 0.25f), kerb);
                RectXZ i = c.Inset(0.25f);
                CellInlay(p, i);
                if (!diorama && cx > 0f && cz == 0f)
                {
                    // E cell around the ending well x[6, 8] z[−1, 1].
                    Arch.Floor(p, new RectXZ(i.X0, i.Z0, i.X1, -1f), 0f, 0.5f, cell, false);
                    Arch.Floor(p, new RectXZ(i.X0, 1f, i.X1, i.Z1), 0f, 0.5f, cell, false);
                    Arch.Floor(p, new RectXZ(i.X0, -1f, 6f, 1f), 0f, 0.5f, cell, false);
                    Arch.Floor(p, new RectXZ(8f, -1f, i.X1, 1f), 0f, 0.5f, cell, false);
                    continue;
                }
                Arch.Floor(p, i, 0f, 0.5f, cell, false);
            }
            float[,] strips = { { -Half, -10f }, { -4f, -3f }, { 3f, 4f }, { 10f, Half } };
            float[,] spans = { { -10f, -4f }, { -3f, 3f }, { 4f, 10f } };
            for (int i = 0; i < 4; i++)
                Arch.Floor(p, new RectXZ(-Half, strips[i, 0], Half, strips[i, 1]), 0f, 0.5f, band, false);
            for (int i = 0; i < 4; i++)
            for (int j = 0; j < 3; j++)
                Arch.Floor(p, new RectXZ(strips[i, 0], spans[j, 0], strips[i, 1], spans[j, 1]), 0f, 0.5f, band, false);

            // ---- plinth body (with the ending well in the world) and its stepped planar underside ----
            if (diorama)
            {
                Arch.Box(p, new Vector3(-Half, -3f, -Half), new Vector3(Half, -0.5f, Half), st.Base);
            }
            else
            {
                Arch.Box(p, new Vector3(-Half, -3f, -Half), new Vector3(6f, -0.5f, Half), st.Base);
                Arch.Box(p, new Vector3(8f, -3f, -Half), new Vector3(Half, -0.5f, Half), st.Base);
                Arch.Box(p, new Vector3(6f, -3f, -Half), new Vector3(8f, -0.5f, -1f), st.Base);
                Arch.Box(p, new Vector3(6f, -3f, 1f), new Vector3(8f, -0.5f, Half), st.Base);
            }
            // The stepped underside: coffered soffits (in-shader pattern) so it is not one flat slab from below.
            var soffit = new Surf(st.Base.Mat, Pat.Coffer);
            Arch.Box(p, new Vector3(-11f, -3.5f, -11f), new Vector3(11f, -3f, 11f), soffit);
            Arch.Box(p, new Vector3(-10.5f, -4f, -10.5f), new Vector3(10.5f, -3.5f, 10.5f), soffit);
            Arch.Trim(p, new Vector3(-Half, -0.5f, -Half), new Vector3(Half, -0.5f, -Half), Dir.NegZ, TrimProfile.ShadowGap, -0.5f);
            Arch.Trim(p, new Vector3(-Half, -0.5f, Half), new Vector3(Half, -0.5f, Half), Dir.PosZ, TrimProfile.ShadowGap, -0.5f);
            Arch.Trim(p, new Vector3(-Half, -0.5f, -Half), new Vector3(-Half, -0.5f, Half), Dir.NegX, TrimProfile.ShadowGap, -0.5f);
            Arch.Trim(p, new Vector3(Half, -0.5f, -Half), new Vector3(Half, -0.5f, Half), Dir.PosX, TrimProfile.ShadowGap, -0.5f);

            // ---- parapets on the E and W edges (N and S open onto the screens) ----
            Arch.Parapet(p, new Vector3(11.375f, 0f, -Half), new Vector3(11.375f, 0f, Half));
            Arch.Parapet(p, new Vector3(-11.375f, 0f, -Half), new Vector3(-11.375f, 0f, Half));

            // ---- centre cell: a high, open pergola over the light table (the table itself is a PropKit prop) ----
            // Four columns on the band crossings, two Oak girders at 4.0–4.5 and 0.125 slats on top at 0.5 pitch:
            // the sun draws a striped shadow across the table instead of throwing the whole cell into shade.
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                Arch.Column(p, new Vector3(3.5f * sx, 0f, 3.5f * sz), 4f, 0.5f);
            var walnut = new Surf(Mat.Oak, Pat.Boards);   // light Oak: the open pergola must not read as a dark lid
            foreach (int sz in new[] { -1, 1 })
                Arch.Box(p, new Vector3(-4f, 4f, 3.5f * sz - 0.1875f), new Vector3(4f, 4.5f, 3.5f * sz + 0.1875f), walnut, ArchFlags.Soft);
            for (float x = -3.25f; x <= 3.25f + 1e-3f; x += 0.5f)
                Arch.Box(p, new Vector3(x - 0.0625f, 4.5f, -4f), new Vector3(x + 0.0625f, 4.75f, 4f), walnut, ArchFlags.Soft);
            PropKit.Lamp(p, new Vector3(0f, 4.5f, -1.5f), 0f, LampStyle.Pendant, 2.25f, Mat.Warm);
            PropKit.Lamp(p, new Vector3(0f, 4.5f, 1.5f), 0f, LampStyle.Pendant, 2.25f, Mat.Warm);

            // ---- screens at the outer band: N / S exhibits (niches in the photo), corners coming soon ----
            foreach (float cx in new[] { -7f, 0f, 7f })
            foreach (int sz in new[] { 1, -1 })
            {
                bool niche = diorama && cx == 0f;
                BuildScreen(p, st, cx, sz, niche);
            }

            // ---- W cell: arrival furniture (a bench, a café set, the rug, a lamp) ----
            PropKit.Rug(p, new RectXZ(-8.5f, -1.5f, -5.5f, 1.5f), 301);
            PropKit.Bench(p, new Vector3(-7f, 0f, -2.5f), Dir.PosZ, 2f, true);
            PropKit.Table(p, new Vector3(-7f, 0f, 2.25f), Dir.NegZ, new Vector2(1f, 0.75f), 0.75f);
            PropKit.Chair(p, new Vector3(-7.875f, 0f, 2.25f), 95f, ChairStyle.Cafe, Mat.Teal);
            PropKit.Chair(p, new Vector3(-6.125f, 0f, 2.375f), -100f, ChairStyle.Cafe, Mat.Teal);
            PropKit.Chair(p, new Vector3(-7f, 0f, 2.875f), 185f, ChairStyle.Cafe, Mat.Mustard);
            PropKit.Planter(p, new Vector3(-9.25f, 0f, -2.25f), Dir.PosX, PlanterStyle.Square, default, 0.625f, 302);
            PropKit.Lamp(p, new Vector3(-4.75f, 0f, 2.5f), 0f, LampStyle.Standing);

            // ---- planting: bowls with palms at the plinth corners and the E/W band ends (the most lush zone) ----
            int seed = 310;
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                PropKit.Planter(p, new Vector3(10.75f * sx, 0f, 10.5f * sz), Dir.PosZ, PlanterStyle.Bowl, default, 0.5f, seed++);
            PropKit.Planter(p, new Vector3(10.5f, 0f, 3.5f), Dir.NegX, PlanterStyle.Bowl, default, 0.5f, seed++);
            PropKit.Planter(p, new Vector3(10.5f, 0f, -3.5f), Dir.NegX, PlanterStyle.Bowl, default, 0.5f, seed++);
            PropKit.Planter(p, new Vector3(-10.5f, 0f, 3.5f), Dir.PosX, PlanterStyle.Bowl, default, 0.5f, seed++);
            PropKit.Planter(p, new Vector3(-10.5f, 0f, -3.5f), Dir.PosX, PlanterStyle.Bowl, default, 0.5f, seed++);
            PropKit.Planter(p, new Vector3(-3.5f, 0f, 3.5f), Dir.PosZ, PlanterStyle.Square, default, 0.625f, seed++);
            PropKit.Planter(p, new Vector3(3.5f, 0f, -3.5f), Dir.NegZ, PlanterStyle.Square, default, 0.625f, seed);
        }

        /// <summary>
        /// A brass inlay framing a cell just inside its kerb (Soft, 0.0625 wide, 1/64 proud; off the 0.0625 grid in y,
        /// so exempt from the grid check like the other inlays).
        /// </summary>
        static void CellInlay(Transform p, RectXZ i)
        {
            const float w = 0.0625f, h = 0.015625f;
            Arch.Tag(Arch.Box(p, new Vector3(i.X0, 0f, i.Z0), new Vector3(i.X1, h, i.Z0 + w), Mat.Brass, ArchFlags.Soft), 0f);
            Arch.Tag(Arch.Box(p, new Vector3(i.X0, 0f, i.Z1 - w), new Vector3(i.X1, h, i.Z1), Mat.Brass, ArchFlags.Soft), 0f);
            Arch.Tag(Arch.Box(p, new Vector3(i.X0, 0f, i.Z0 + w), new Vector3(i.X0 + w, h, i.Z1 - w), Mat.Brass, ArchFlags.Soft), 0f);
            Arch.Tag(Arch.Box(p, new Vector3(i.X1 - w, 0f, i.Z0 + w), new Vector3(i.X1, h, i.Z1 - w), Mat.Brass, ArchFlags.Soft), 0f);
        }

        /// <summary>
        /// The screens on the outer band. N / S (the exhibit screens, x = 0): a solid 5 × 4 Paper wall with end
        /// pilasters, a cornice and a Frost "projection" rectangle on the face toward the hub; the photo version opens
        /// a bracket niche there instead. Corners (coming soon): a contact-sheet screen (0.75 openings, 0.25 mullions)
        /// on a plinth, with a coping.
        /// </summary>
        static void BuildScreen(Transform p, ArchStyle st, float cx, int sz, bool niche)
        {
            float z = ScreenZ * sz;
            Dir toHub = sz > 0 ? Dir.NegZ : Dir.PosZ, outward = sz > 0 ? Dir.PosZ : Dir.NegZ;
            if (cx != 0f)
            {
                Arch.Box(p, new Vector3(cx - 2.5f, 0f, z - 0.25f), new Vector3(cx + 2.5f, 0.5f, z + 0.25f), st.Trim);
                Arch.Screen(p, new Vector3(cx - 2.5f, 0.5f, z), new Vector3(cx + 2.5f, 0.5f, z), 3.5f, Arch.ScreenKind.ContactSheet);
                Arch.Box(p, new Vector3(cx - 2.625f, 4f, z - 0.25f), new Vector3(cx + 2.625f, 4.25f, z + 0.25f), st.Trim);
                return;
            }
            var trim = WallTrim.Plinth | WallTrim.Cornice | WallTrim.BothSides;
            Opening[] openings = niche ? new[] { Opening.Door(2.5f, 2.5f, 3f) } : new Opening[0];
            Arch.Wall(p, new Vector3(cx - 2.5f, 0f, z), new Vector3(cx + 2.5f, 0f, z), Arch.Storey, Arch.WallThickness, trim, openings);
            foreach (float ex in new[] { cx - 2.25f, cx + 2.25f })
            {
                Arch.Pilaster(p, new Vector3(ex, 0f, z - 0.25f * sz), Arch.Storey, toHub);
                Arch.Pilaster(p, new Vector3(ex, 0f, z + 0.25f * sz), Arch.Storey, outward);
            }
            if (!niche)
            {
                // The projection rectangle the key photo opens into a niche (faint Frost, a 1/16 proud panel).
                float face = z - sz * 0.25f;
                Arch.Box(p, new Vector3(cx - 1.25f, 0.75f, Mathf.Min(face, face - sz * 0.0625f)),
                         new Vector3(cx + 1.25f, 3f, Mathf.Max(face, face - sz * 0.0625f)),
                         new Surf(Mat.Frost, Pat.Frost), ArchFlags.Soft);
                // Read as a contact strip, not a blank wall: three frames split by Paper mullions, with Sprocket
                // bands above and below (1/8 proud of the face).
                float za = Mathf.Min(face, face - sz * 0.125f), zb = Mathf.Max(face, face - sz * 0.125f);
                var sprocketBand = new Surf(Mat.Paper, Pat.Sprocket);
                foreach (float mx in new[] { cx - 0.4375f, cx + 0.4375f })
                    Arch.Tag(Arch.Box(p, new Vector3(mx - 0.0625f, 0.75f, za), new Vector3(mx + 0.0625f, 3f, zb), Mat.Paper, ArchFlags.Soft), 0f);
                Arch.Box(p, new Vector3(cx - 1.5f, 3f, za), new Vector3(cx + 1.5f, 3.25f, zb), sprocketBand, ArchFlags.Soft);
                Arch.Box(p, new Vector3(cx - 1.5f, 0.5f, za), new Vector3(cx + 1.5f, 0.75f, zb), sprocketBand, ArchFlags.Soft);
                Arch.Tag(Arch.Box(p, new Vector3(cx - 1.5f, 0.75f, za), new Vector3(cx - 1.25f, 3f, zb), sprocketBand, ArchFlags.Soft), 0f);
                Arch.Tag(Arch.Box(p, new Vector3(cx + 1.25f, 0.75f, za), new Vector3(cx + 1.5f, 3f, zb), sprocketBand, ArchFlags.Soft), 0f);
            }
            if (!niche) return;

            // The niche: a Terracotta herringbone alcove beyond the plinth edge, to |z| = 14 (clear x[−1.25, 1.25]).
            float z0 = Half * sz, z1 = 14f * sz;
            Arch.Floor(p, new RectXZ(-1.75f, Mathf.Min(z0, z1), 1.75f, Mathf.Max(z0, z1)), 0f, 1.5f, st.Accent);
            float w0 = 11f * sz, w1 = 14f * sz;
            Arch.Wall(p, new Vector3(-1.5f, 0f, w0), new Vector3(-1.5f, 0f, 13.75f * sz), 3.5f, 0.5f, WallTrim.None);
            Arch.Wall(p, new Vector3(1.5f, 0f, w0), new Vector3(1.5f, 0f, 13.75f * sz), 3.5f, 0.5f, WallTrim.None);
            Arch.Wall(p, new Vector3(-1.75f, 0f, 13.75f * sz), new Vector3(1.75f, 0f, 13.75f * sz), 3.5f, 0.5f, WallTrim.None);
            Arch.Box(p, new Vector3(-1.75f, 3.5f, Mathf.Min(w0, w1)), new Vector3(1.75f, 4f, Mathf.Max(w0, w1)), st.Wall);
        }
    }

    // ======================================================================== hub state

    /// <summary>
    /// The hub's logic (Lead C): which wings are solved (exhibit state), which photos the player has seen (light
    /// table prints), and the ending hatch (opens once both wings are solved). Not part of the rewind history:
    /// a solved wing stays solved (hub entry is a checkpoint).
    /// </summary>
    public sealed class HubState : MonoBehaviour
    {
        HubLightTable _hub;
        RoomContext _ctx;
        readonly HashSet<PhotoData> _known = new HashSet<PhotoData>();
        bool _stairs, _camera, _tutorialDone;
        float _next;

        public bool StairsSolved => _stairs;
        public bool CameraSolved => _camera;
        public bool TutorialDone => _tutorialDone;
        public bool EndingUnlocked => _stairs && _camera;

        internal void Init(HubLightTable hub, RoomContext ctx)
        {
            _hub = hub;
            _ctx = ctx;
        }

        public bool IsSolved(HubLightTable.Wing wing) => wing == HubLightTable.Wing.Stairs ? _stairs : _camera;

        /// <summary>Marks a wing solved (or not): exhibit state, light-table border, and the ending hatch.</summary>
        public void SetSolved(HubLightTable.Wing wing, bool solved = true)
        {
            if (wing == HubLightTable.Wing.Stairs) _stairs = solved;
            else _camera = solved;
            ExhibitStand stand = _hub != null ? _hub.ExhibitOf(wing) : null;
            if (stand != null) stand.State = solved ? ExhibitState.Solved : ExhibitState.Available;
            if (EndingUnlocked && _hub != null && _hub.Hatch != null && !_hub.Hatch.IsOpen) _hub.Hatch.Open();
            Refresh();
        }

        /// <summary>The player reached the hub: both tutorial photos have been used.</summary>
        public void MarkTutorialDone()
        {
            _tutorialDone = true;
            Refresh();
        }

        /// <summary>True once the player has held <paramref name="photo"/> (or its wing is solved).</summary>
        public bool IsKnown(PhotoData photo) => photo != null && _known.Contains(photo);

        /// <summary>"Play again".</summary>
        public void ResetState()
        {
            _known.Clear();
            _stairs = _camera = _tutorialDone = false;
            if (_hub != null)
            {
                if (_hub.StairsExhibit != null) _hub.StairsExhibit.State = ExhibitState.Available;
                if (_hub.CameraExhibit != null) _hub.CameraExhibit.State = ExhibitState.Available;
                if (_hub.Hatch != null) _hub.Hatch.Close();
            }
            Refresh();
        }

        void Update()
        {
            // Game time (not unscaled): fixed-step tests and the player see the same refresh cadence.
            if (Time.time < _next) return;
            _next = Time.time + 0.25f;
            var inv = RoomContext.PlayerInventory;
            if (inv != null)
                for (int i = 0; i < inv.Count; i++)
                    if (inv.Photos[i] != null) _known.Add(inv.Photos[i]);
            Refresh();
        }

        /// <summary>The photo of light-table slot <paramref name="slot"/> (front row: 0 T1 stair, 1 T2 door, 2 Stairs key, 3 Camera key; back row 4–7 coming soon).</summary>
        public PhotoData SlotPhoto(int slot)
        {
            var game = GameBootstrap.Instance;
            if (game == null) return null;
            switch (slot)
            {
                case 0: { var c = game.Context<TutorialLedge>(); return c != null ? c.GetShotPhoto(0) : null; }
                case 1: { var c = game.Context<TutorialDarkroom>(); return c != null ? c.GetShotPhoto(0) : null; }
                case 2: return _hub != null && _hub.StairsKey != null ? _hub.StairsKey.Photo : null;
                case 3: return _hub != null && _hub.CameraKey != null ? _hub.CameraKey.Photo : null;
                default: return null;
            }
        }

        public bool SlotSolved(int slot)
        {
            switch (slot)
            {
                case 0:
                case 1: return _tutorialDone;
                case 2: return _stairs;
                case 3: return _camera;
                default: return false;
            }
        }

        /// <summary>Re-applies every print (cheap: assignments only change on change).</summary>
        public void Refresh()
        {
            LightTableView prints = _hub != null ? _hub.Prints : null;
            if (prints == null) return;
            for (int i = 0; i < prints.Count; i++)
            {
                PhotoData photo = SlotPhoto(i);
                bool solved = SlotSolved(i);
                bool known = photo != null && (solved || _known.Contains(photo));
                Texture2D image = known ? photo.Preview : null;
                PrintCard card = prints[i];
                if (card == null) continue;
                if (card.Image != image) card.Image = image;
                if (card.Border != solved) card.Border = solved;
            }
        }
    }

    // ======================================================================== ending hatch

    /// <summary>
    /// The E-cell ending pod (art bible §7.3): a teleporter parked 2.75 m down a well under a two-leaf Limestone
    /// hatch. <see cref="Open"/>: the leaves drop and slide under the floor, then the pod rises 1.6 s
    /// (easeInOutCubic). Story state (not rewound); reset by "Play again".
    /// </summary>
    public sealed class EndingHatch : MonoBehaviour
    {
        public const float Depth = 2.75f, LeafSeconds = 0.4f, RiseSeconds = 1.6f;

        Transform _leafA, _leafB;
        Vector3 _restA, _restB, _podUp, _podDown;
        Teleporter _pod;
        float _t = -1f;
        bool _open;

        public bool IsOpen => _open;
        /// <summary>True once the pod has fully risen.</summary>
        public bool Risen => _open && _t < 0f;
        public Teleporter Pod => _pod;

        public static EndingHatch Create(Transform root, Vector3 padLocal, Action onEnter)
        {
            var go = new GameObject("EndingHatch");
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.zero;
            var hatch = go.AddComponent<EndingHatch>();
            hatch._leafA = Leaf(root, "HatchLeafA", new Vector3(padLocal.x, 0f, padLocal.z - 1f));
            hatch._leafB = Leaf(root, "HatchLeafB", new Vector3(padLocal.x, 0f, padLocal.z));
            hatch._restA = hatch._leafA.localPosition;
            hatch._restB = hatch._leafB.localPosition;

            hatch._pod = PropKit.Teleporter(root, padLocal, Dir.NegX, onEnter, Mat.Graphite);
            hatch._pod.name = "EndingPod";
            hatch._podUp = hatch._pod.transform.localPosition;
            hatch._podDown = hatch._podUp - Vector3.up * Depth;
            hatch.Close();
            return hatch;
        }

        /// <summary>One hatch leaf: a 2 × 1 Limestone/Tile slab (0.25 thick, top flush with the floor), walkable.</summary>
        static Transform Leaf(Transform root, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPos;
            go.AddComponent<Interactable>();
            Arch.Box(go.transform, new Vector3(-1f, -0.25f, 0f), new Vector3(1f, 0f, 1f), new Surf(Mat.Limestone, Pat.Tile), ArchFlags.Dynamic);
            Arch.BakeLocal(go);
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.125f, 0.5f);
            box.size = new Vector3(2f, 0.25f, 1f);
            return go.transform;
        }

        /// <summary>Opens the hatch and raises the pod (animated unless <paramref name="instant"/>).</summary>
        public void Open(bool instant = false)
        {
            _open = true;
            if (_pod != null) _pod.gameObject.SetActive(true);
            if (instant)
            {
                _t = -1f;
                Apply(LeafSeconds + RiseSeconds);
            }
            else
            {
                _t = 0f;
                Apply(0f);
            }
        }

        /// <summary>Closes the hatch at once with the pod parked below (start, "Play again").</summary>
        public void Close()
        {
            _open = false;
            _t = -1f;
            if (_leafA != null) _leafA.localPosition = _restA;
            if (_leafB != null) _leafB.localPosition = _restB;
            if (_pod != null)
            {
                _pod.transform.localPosition = _podDown;
                _pod.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            if (_t < 0f) return;
            _t += Time.deltaTime;
            Apply(_t);
            if (_t >= LeafSeconds + RiseSeconds) _t = -1f;
        }

        void Apply(float t)
        {
            float k = LevelEase.OutCubic(t / LeafSeconds);
            Vector3 down = Vector3.down * (0.3f * Mathf.Clamp01(k * 2f));
            if (_leafA != null) _leafA.localPosition = _restA + down + Vector3.back * k;
            if (_leafB != null) _leafB.localPosition = _restB + down + Vector3.forward * k;
            float r = LevelEase.InOutCubic((t - LeafSeconds * 0.75f) / RiseSeconds);
            if (_pod != null) _pod.transform.localPosition = Vector3.Lerp(_podDown, _podUp, r);
        }
    }
}
