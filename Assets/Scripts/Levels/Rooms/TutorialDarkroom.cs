using Ion.Gameplay;
using Ion.Levels.Arch;
using Ion.Levels.Props;
using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels.Rooms
{
    using Arch = Ion.Levels.Arch.Arch;

    /// <summary>
    /// Zone 1, T2 "Darkroom" (art bible §7.2), mood Darkroom, Plaster/Ashlar with Cyanotype panels.
    ///  5. Arrival is a checkpoint (brass inlay). The roofed corridor's floor x[−1.75, 0.75] z[9, 12] is a trap
    ///     (<see cref="StoryCollapse"/>) over a sealed 2.5 m pit: the player lands grounded (not a fall). A single R
    ///     has nothing to undo since the checkpoint ("nothing to rewind" + the R R hint); R R returns to the entry.
    ///     The trap stays open; the 1 m ledge x[0.75, 1.75] by the east wall is the way round.
    ///  Then rotate and cut: the projection room ends in a solid 4 m wall (z[20, 20.5]). The photo on the easel
    ///  shows the same wall with a bracket doorway, shot rolled 90°: holding Q a quarter turn makes it upright, and placing it from
    ///  the marker cuts a walkable door. A sideways door cannot be walked through, which invites a single R.
    /// </summary>
    public sealed class TutorialDarkroom : Room
    {
        public override string Key => "t2";
        public override string Title => "Darkroom";
        public override string Intro => "Mind the floor.";
        public override ZoneMood Mood => ZoneMood.Darkroom;
        public override ArchStyle Style => ArchStyle.Darkroom;
        public override float LowestFloorY => PitFloor;

        public const float PitFloor = -2.5f;
        public const float CutWallZ = 20.25f;

        public static readonly Vector3 SpawnFeet = new Vector3(0f, 0f, 1.5f);
        public static readonly Vector3 MarkerFeet = new Vector3(0f, 0f, 15f);
        public static readonly Vector3 ExitPad = new Vector3(0f, 0f, 26f);
        public static readonly Vector3 DisplayPos = new Vector3(-4f, 0f, 16f);

        public DioramaShot DoorShot { get; private set; }
        public StoryCollapse Trap { get; private set; }

        public override void Build(Transform root, RoomContext ctx)
        {
            BuildDarkroom(root, false);
            ctx.SetSpawn(SpawnFeet, 0f);

            DoorShot = ctx.RegisterDioramaShot(MarkerFeet, 0f, 90f, "Sideways door", id: "t2.door");
            BuildDarkroom(ctx.DioramaRoot, true);
            System.Action exit = ctx.NextZone();
            ctx.DioramaTeleporter(ExitPad, Dir.NegZ, exit);

            // ---- devices (world only) ----
            ctx.Teleporter(ExitPad, Dir.NegZ, exit);
            ArchStyle st = Style;
            Trap = PropKit.CollapseSlab(root, new Vector3(-1.75f, -0.5f, 9f), new Vector3(0.75f, 0f, 12f), st.Floor);
            PropKit.Checkpoint(root, SpawnFeet, 0f, "t2.entry");
            ctx.PhotoDisplay(DisplayPos, Dir.PosX, DoorShot, DisplayMount.Easel);
            ctx.Marker(MarkerFeet, 0f);

            // ---- hints ----
            DioramaShot shot = DoorShot;
            StoryCollapse trap = Trap;
            ctx.Hint(new Vector3(-0.5f, PitFloor, 10.5f), 2.5f, "Sealed in.  [R] rewind", 4f).Height = 1.5f;
            ctx.Hint(new Vector3(0f, 0f, 7.5f), 2f, "The ledge along the wall still holds.", 4f,
                     () => trap != null && trap.Fired);
            ctx.Hint(MarkerFeet, 1.25f, "[SHIFT] hold up the photo", 4f,
                     () => RoomContext.PlayerHas(shot) && !RoomContext.PlayerRaised, false);
            ctx.Hint(MarkerFeet, 1.25f, "It's sideways.  Hold [Q] / [E] to turn it upright, then [LMB] place", 5f,
                     () => RoomContext.PlayerRaised, false);

            // ---- the tutorial script, continued ----
            AddSolution("trap", RoomSolution.Kind.Walk, new Vector3(0f, 0f, 13f)).Hazard = RoomSolution.Hazards.Drop;
            var nothing = AddSolution("rewind.nothing", RoomSolution.Kind.Rewind, new Vector3(-0.5f, PitFloor, 10.5f));
            nothing.Expect = RoomSolution.RewindExpect.Nothing;
            nothing.Tolerance = 1.5f;
            var back = AddSolution("rewind.checkpoint", RoomSolution.Kind.RewindToCheckpoint, SpawnFeet);
            back.Expect = RoomSolution.RewindExpect.ToCheckpoint;
            AddSolution("ledge", RoomSolution.Kind.Walk, new Vector3(1.25f, 0f, 12.75f)).Via = new[] { new Vector3(1.25f, 0f, 7.5f) };
            AddSolution("pickup", RoomSolution.Kind.Pickup, new Vector3(-2.75f, 0f, 16f), 270f);
            AddSolution("place", RoomSolution.Kind.Place, MarkerFeet, 0f, 0f, 0, 90f);
            AddSolution("far", RoomSolution.Kind.Goal, new Vector3(0f, 0f, 23f));
            AddSolution("exit", RoomSolution.Kind.Teleport, ExitPad).Destination = "hub";
        }

        /// <summary>The darkroom's architecture; <paramref name="withDoor"/> = the photo's version of the end wall.</summary>
        public static void BuildDarkroom(Transform p, bool withDoor)
        {
            ArchStyle st = Arch.Style ?? ArchStyle.Darkroom;

            // ---- entry terrace z[0, 6] ----
            Arch.Terrace(p, new RectXZ(-6f, 0f, 6f, 6f), 0f, 3f);
            Arch.Parapet(p, new Vector3(-5.875f, 0f, 0f), new Vector3(-5.875f, 0f, 5.75f));
            Arch.Parapet(p, new Vector3(5.875f, 0f, 0f), new Vector3(5.875f, 0f, 5.75f));
            Arch.Parapet(p, new Vector3(-6f, 0f, 0.125f), new Vector3(6f, 0f, 0.125f));
            Arch.Wall(p, new Vector3(-6f, 0f, 6f), new Vector3(6f, 0f, 6f), Arch.Storey, Arch.WallThickness,
                      WallTrim.Default, Opening.Door(6f, 3.5f, 3f));
            PropKit.Planter(p, new Vector3(-4.5f, 0f, 4.5f), Dir.PosX, PlanterStyle.Square, default, 0.625f, 201);
            PropKit.Planter(p, new Vector3(4.5f, 0f, 4.5f), Dir.NegX, PlanterStyle.Square, default, 0.625f, 202);

            // ---- the roofed corridor x[−1.75, 1.75] z[6, 14] over the trap pit ----
            Arch.Floor(p, new RectXZ(-1.75f, 6f, 1.75f, 9f), 0f, 3f, null, false);
            Arch.Floor(p, new RectXZ(0.75f, 9f, 1.75f, 12f), 0f, 3f, null, false);     // the ledge that holds
            Arch.Floor(p, new RectXZ(-1.75f, 12f, 1.75f, 14f), 0f, 3f, null, false);
            Arch.Floor(p, new RectXZ(-1.75f, 9f, 0.75f, 12f), PitFloor, 0.5f, st.Accent, false);
            // West pit wall with a window slot (z[10.25, 10.75], y[−2.25, −0.75]): the sealed pit looks out at the sky.
            Arch.Box(p, new Vector3(-2.25f, -3f, 6f), new Vector3(-1.75f, 0f, 10.25f), st.Base);
            Arch.Box(p, new Vector3(-2.25f, -3f, 10.75f), new Vector3(-1.75f, 0f, 14f), st.Base);
            Arch.Box(p, new Vector3(-2.25f, -3f, 10.25f), new Vector3(-1.75f, -2.25f, 10.75f), st.Base);
            Arch.Box(p, new Vector3(-2.25f, -0.75f, 10.25f), new Vector3(-1.75f, 0f, 10.75f), st.Base);
            // Pit articulation: a Limestone datum course and a brass reveal round the slot (Soft).
            Arch.Box(p, new Vector3(-1.75f, -1.625f, 9f), new Vector3(-1.6875f, -1.5f, 10.25f), st.Trim, ArchFlags.Soft);
            Arch.Box(p, new Vector3(-1.75f, -1.625f, 10.75f), new Vector3(-1.6875f, -1.5f, 12f), st.Trim, ArchFlags.Soft);
            Arch.Box(p, new Vector3(0.6875f, -1.625f, 9.0625f), new Vector3(0.75f, -1.5f, 11.9375f), st.Trim, ArchFlags.Soft);
            Arch.Box(p, new Vector3(-1.75f, -1.625f, 11.9375f), new Vector3(0.75f, -1.5f, 12f), st.Trim, ArchFlags.Soft);
            Arch.Box(p, new Vector3(-1.75f, -1.625f, 9f), new Vector3(0.75f, -1.5f, 9.0625f), st.Trim, ArchFlags.Soft);
            Arch.Box(p, new Vector3(-1.75f, -2.3125f, 10.1875f), new Vector3(-1.6875f, -0.6875f, 10.25f), Mat.Brass, ArchFlags.Soft);
            Arch.Box(p, new Vector3(-1.75f, -2.3125f, 10.75f), new Vector3(-1.6875f, -0.6875f, 10.8125f), Mat.Brass, ArchFlags.Soft);
            Arch.Box(p, new Vector3(-1.75f, -2.3125f, 10.25f), new Vector3(-1.6875f, -2.25f, 10.75f), Mat.Brass, ArchFlags.Soft);
            Arch.Box(p, new Vector3(-1.75f, -0.75f, 10.25f), new Vector3(-1.6875f, -0.6875f, 10.75f), Mat.Brass, ArchFlags.Soft);
            Arch.Box(p, new Vector3(1.75f, -3f, 6f), new Vector3(2.25f, 0f, 14f), st.Base);
            Arch.Box(p, new Vector3(-2.25f, -3.5f, 6f), new Vector3(2.25f, -3f, 14f), st.Base);
            Arch.Wall(p, new Vector3(-2f, 0f, 6.25f), new Vector3(-2f, 0f, 14f));
            Arch.Wall(p, new Vector3(2f, 0f, 6.25f), new Vector3(2f, 0f, 14f));
            Arch.Roof(p, new RectXZ(-2.25f, 6.25f, 2.25f, 14f), Arch.Storey + 0.5f, 0.5f, 0.5f);
            // Cyanotype accent panels and safelight sconces on the corridor's inner faces.
            for (int i = 0; i < 3; i++)
            {
                float z = 7f + i * 2.5f;
                Arch.Box(p, new Vector3(-1.75f, 0.75f, z), new Vector3(-1.6875f, 2.75f, z + 1f), st.Panel, ArchFlags.Soft);
                Arch.Box(p, new Vector3(1.6875f, 0.75f, z), new Vector3(1.75f, 2.75f, z + 1f), st.Panel, ArchFlags.Soft);
                BrassFrame(p, -1.75f, -1.625f, z);
                BrassFrame(p, 1.625f, 1.75f, z);
            }
            PropKit.Lamp(p, new Vector3(-1.75f, 2.75f, 9f), 90f, LampStyle.Sconce, 1f, Mat.Safelight);
            PropKit.Lamp(p, new Vector3(1.75f, 2.75f, 13f), 270f, LampStyle.Sconce, 1f, Mat.Safelight);

            // ---- the projection room z[14, 20], open to the sky ----
            Arch.Terrace(p, new RectXZ(-6f, 14f, 6f, 20f), 0f, 3f);
            Arch.Wall(p, new Vector3(-6f, 0f, 14f), new Vector3(-2.25f, 0f, 14f));
            Arch.Wall(p, new Vector3(2.25f, 0f, 14f), new Vector3(6f, 0f, 14f));
            Arch.Wall(p, new Vector3(-5.75f, 0f, 14.25f), new Vector3(-5.75f, 0f, 20f));
            Arch.Wall(p, new Vector3(5.75f, 0f, 14.25f), new Vector3(5.75f, 0f, 20f));
            PropKit.Bench(p, new Vector3(4.5f, 0f, 17f), Dir.NegX, 2f);
            PropKit.Lamp(p, new Vector3(-5.5f, 2.75f, 18.5f), 90f, LampStyle.Sconce, 1f, Mat.Safelight);
            PropKit.Lamp(p, new Vector3(5.5f, 2.75f, 15.5f), 270f, LampStyle.Sconce, 1f, Mat.Safelight);

            // The end wall: solid in the world; in the photo, the same wall with a bracket doorway.
            Opening[] openings = withDoor ? new[] { Opening.Door(6f) } : new Opening[0];
            Arch.Wall(p, new Vector3(-6f, 0f, CutWallZ), new Vector3(6f, 0f, CutWallZ), Arch.Storey, Arch.WallThickness,
                      WallTrim.Default, openings);

            // ---- the exit court z[20, 30] ----
            Arch.Terrace(p, new RectXZ(-6f, 20f, 6f, 30f), 0f, 3f);
            Arch.Parapet(p, new Vector3(-5.875f, 0f, 20.5f), new Vector3(-5.875f, 0f, 30f));
            Arch.Parapet(p, new Vector3(5.875f, 0f, 20.5f), new Vector3(5.875f, 0f, 30f));
            Arch.Parapet(p, new Vector3(-6f, 0f, 29.875f), new Vector3(6f, 0f, 29.875f));
            Arch.BracketFrame(p, ExitPad, Dir.NegZ, 2f, 3f, 0.5f);
            PropKit.Planter(p, new Vector3(-4.25f, 0f, 28.5f), Dir.PosZ, PlanterStyle.Square, default, 0.625f, 203);
            PropKit.Planter(p, new Vector3(4.25f, 0f, 28.5f), Dir.PosZ, PlanterStyle.Square, default, 0.625f, 204);
            PropKit.Plant(p, new Vector3(-5f, 0f, 22f), PlantKind.Strelitzia, 1f, 30f, 205);
            PropKit.Plant(p, new Vector3(5f, 0f, 22f), PlantKind.Monstera, 1f, 200f, 206);
        }

        /// <summary>A 1/16 brass frame round a Cyanotype panel on a corridor face (x0..x1 = face to 0.125 proud).</summary>
        static void BrassFrame(Transform p, float x0, float x1, float z)
        {
            const float w = 0.0625f;
            Arch.Tag(Arch.Box(p, new Vector3(x0, 2.75f, z - w), new Vector3(x1, 2.75f + w, z + 1f + w), Mat.Brass, ArchFlags.Soft), 0f);
            Arch.Tag(Arch.Box(p, new Vector3(x0, 0.75f - w, z - w), new Vector3(x1, 0.75f, z + 1f + w), Mat.Brass, ArchFlags.Soft), 0f);
            Arch.Tag(Arch.Box(p, new Vector3(x0, 0.75f, z - w), new Vector3(x1, 2.75f, z), Mat.Brass, ArchFlags.Soft), 0f);
            Arch.Tag(Arch.Box(p, new Vector3(x0, 0.75f, z + 1f), new Vector3(x1, 2.75f, z + 1f + w), Mat.Brass, ArchFlags.Soft), 0f);
        }
    }
}
