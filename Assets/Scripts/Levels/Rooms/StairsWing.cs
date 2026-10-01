using Ion.Gameplay;
using Ion.Levels.Arch;
using Ion.Levels.Props;
using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels.Rooms
{
    using Arch = Ion.Levels.Arch.Arch;

    /// <summary>
    /// Zone 3, the Stairs wing (art bible §7.4), mood Mint, Mint/Formwork structure.
    ///  - Arrival behind a gate wall: a button (channel <see cref="GateChannel"/>) sinks the gate, so switches recur
    ///    after the tutorial.
    ///  - The existing puzzle: a 4 m ledge (front at z = 11) and a photo of a 16-step stair shot rolled 90° from the
    ///    marker (0, 0, −0.5). Placed unrotated it pastes the stair on its side; holding Q a quarter turn makes it upright and climbable.
    ///  - The exit teleporter on the ledge returns to hub exhibit [02] and marks it solved.
    /// </summary>
    public sealed class StairsWing : Room
    {
        public override string Key => "stairs";
        public override string Title => "Stairs";
        public override string Intro => "Too high to climb.  That photo looks sideways...";
        public override ZoneMood Mood => ZoneMood.Mint;
        public override ArchStyle Style => ArchStyle.Mint;

        public const string GateChannel = "stairs.gate";
        public const int StairSteps = 16;
        public const float Rise = 0.25f, Tread = 0.5f;
        public const float LedgeFront = 11f, LedgeTop = StairSteps * Rise;       // 4 m
        public const float StairFoot = LedgeFront - StairSteps * Tread;          // z = 3
        public const float GateZ = -4f;

        public static readonly Vector3 SpawnFeet = new Vector3(0f, 0f, -9f);
        public static readonly Vector3 MarkerFeet = new Vector3(0f, 0f, -0.5f);
        public static readonly Vector3 ButtonPos = new Vector3(2.5f, 0f, -6.5f);
        public static readonly Vector3 DisplayPos = new Vector3(-4f, 0f, -1.5f);
        public static readonly Vector3 ExitPad = new Vector3(0f, LedgeTop, 23f);

        public DioramaShot StairShot { get; private set; }
        public Mover Gate { get; private set; }
        public Switch Button { get; private set; }

        public override void Build(Transform root, RoomContext ctx)
        {
            BuildWing(root, false);
            ctx.SetSpawn(SpawnFeet, 0f);

            StairShot = ctx.RegisterDioramaShot(MarkerFeet, 0f, 90f, "Sideways stairs", id: "stairs.stair");
            BuildWing(ctx.DioramaRoot, true);
            System.Action exit = () => HubLightTable.ReturnFromWing(HubLightTable.Wing.Stairs);
            ctx.DioramaTeleporter(ExitPad, Dir.NegZ, exit, Mat.Mint);

            // ---- devices ----
            ctx.Teleporter(ExitPad, Dir.NegZ, exit, Mat.Mint);
            Gate = PropKit.Gate(root, new Vector3(0f, 0f, GateZ), Dir.NegZ, Arch.DoorWidth, Arch.DoorHeight, GateChannel, true);
            // A floor lever here (the device vocabulary: T1 teaches the button, the wing adds the lever).
            Button = PropKit.Lever(root, ButtonPos, Dir.NegZ, GateChannel);
            PropKit.Checkpoint(root, SpawnFeet, 0f, "stairs.arrive");
            ctx.PhotoDisplay(DisplayPos, Dir.PosX, StairShot);
            ctx.Marker(MarkerFeet, 0f);

            DioramaShot shot = StairShot;
            ctx.Hint(new Vector3(2.5f, 0f, -7.75f), 2f, "[E] pull the lever", 3f, () => !SwitchBoard.Get(GateChannel), false);
            ctx.Hint(MarkerFeet, 1.25f, "Sideways again.  [SHIFT] raise, hold [Q] / [E] to turn, [LMB] place", 5f,
                     () => RoomContext.PlayerHas(shot), false);

            var press = AddSolution("press", RoomSolution.Kind.Press, new Vector3(2.5f, 0f, -7.75f), 0f, 20f);
            press.Channel = GateChannel;
            press.Via = new[] { new Vector3(1f, 0f, -8.5f) };
            AddSolution("pickup", RoomSolution.Kind.Pickup, new Vector3(-2.75f, 0f, -1.5f), 270f).Via =
                new[] { new Vector3(0f, 0f, -5.5f), new Vector3(0f, 0f, -2.75f) };
            AddSolution("place", RoomSolution.Kind.Place, MarkerFeet, 0f, 0f, 0, 90f);
            AddSolution("far", RoomSolution.Kind.Goal, new Vector3(0f, LedgeTop, 16f));
            AddSolution("exit", RoomSolution.Kind.Teleport, ExitPad).Destination = "hub";
        }

        /// <summary>Expected floor height along the centre line after the upright paste (tests).</summary>
        public static float FloorAt(float z)
        {
            if (z < StairFoot) return 0f;
            if (z >= LedgeFront) return LedgeTop;
            int i = Mathf.Clamp(Mathf.FloorToInt((z - StairFoot) / Tread), 0, StairSteps - 1);
            return (i + 1) * Rise;
        }

        public static void BuildWing(Transform p, bool withStairs)
        {
            ArchStyle st = Arch.Style ?? ArchStyle.Mint;

            // ---- arrival terrace behind the gate wall ----
            Arch.Terrace(p, new RectXZ(-6f, -12f, 6f, GateZ), 0f, 3f);
            Arch.Parapet(p, new Vector3(-5.875f, 0f, -12f), new Vector3(-5.875f, 0f, GateZ - 0.25f));
            Arch.Parapet(p, new Vector3(5.875f, 0f, -12f), new Vector3(5.875f, 0f, GateZ - 0.25f));
            Arch.Parapet(p, new Vector3(-6f, 0f, -11.875f), new Vector3(6f, 0f, -11.875f));
            // The gate wall spans the whole court (x[−10, 10]) so there is no way round its ends.
            Arch.Wall(p, new Vector3(-10f, 0f, GateZ), new Vector3(10f, 0f, GateZ), Arch.Storey, Arch.WallThickness,
                      WallTrim.Default | WallTrim.Pilasters, Opening.Door(10f));
            PropKit.Bench(p, new Vector3(-4f, 0f, -9f), Dir.PosX, 2f);
            PropKit.Planter(p, new Vector3(-4.75f, 0f, -11f), Dir.PosZ, PlanterStyle.Square, default, 0.625f, 401);
            PropKit.Planter(p, new Vector3(4.75f, 0f, -11f), Dir.PosZ, PlanterStyle.Square, default, 0.625f, 402);

            // ---- the court and the ground under the ledge ----
            Arch.Terrace(p, new RectXZ(-10f, GateZ, 10f, 27f), 0f, 4f);
            Arch.Parapet(p, new Vector3(-9.875f, 0f, GateZ + 0.25f), new Vector3(-9.875f, 0f, LedgeFront));
            Arch.Parapet(p, new Vector3(9.875f, 0f, GateZ + 0.25f), new Vector3(9.875f, 0f, LedgeFront));
            PropKit.Planter(p, new Vector3(-8.5f, 0f, 9.5f), Dir.PosX, PlanterStyle.Trough, default, 0.625f, 403);
            PropKit.Planter(p, new Vector3(8.5f, 0f, 9.5f), Dir.NegX, PlanterStyle.Trough, default, 0.625f, 404);
            PropKit.Lamp(p, new Vector3(-6f, 0f, 1f), 0f, LampStyle.Bollard);
            PropKit.Lamp(p, new Vector3(6f, 0f, 1f), 0f, LampStyle.Bollard);
            PropKit.Plant(p, new Vector3(-9f, 0f, -2.5f), PlantKind.Cypress, 1f, 0f, 405);
            PropKit.Plant(p, new Vector3(9f, 0f, -2.5f), PlantKind.Cypress, 1.1f, 0f, 406);

            // ---- a two-storey house on a west annex beyond the court parapet (rule 4; outside the photo's cone) ----
            Arch.Terrace(p, new RectXZ(-17f, GateZ, -10f, 6f), 0f, 3f);
            Arch.House(p, new RectXZ(-16.25f, -3.25f, -10.75f, 5.25f), 0f, 2, Dir.PosX);

            // ---- the 4 m ledge: Mint formwork mass, slab, plinth and pilasters every 4 m ----
            Arch.Box(p, new Vector3(-10f, 0f, LedgeFront), new Vector3(10f, LedgeTop - 0.5f, 27f), st.Wall);
            Arch.Floor(p, new RectXZ(-10f, LedgeFront, 10f, 27f), LedgeTop, 0.5f);
            Arch.Trim(p, new Vector3(-10f, 0f, LedgeFront), new Vector3(10f, 0f, LedgeFront), Dir.NegZ, TrimProfile.Plinth, 0f);
            for (int i = -2; i <= 2; i++)
                if (i != 0) Arch.Pilaster(p, new Vector3(i * 4f - Mathf.Sign(i) * 2f, 0f, LedgeFront), LedgeTop - 0.5f, Dir.NegZ);
            Arch.Parapet(p, new Vector3(-9.875f, LedgeTop, LedgeFront), new Vector3(-9.875f, LedgeTop, 27f));
            Arch.Parapet(p, new Vector3(9.875f, LedgeTop, LedgeFront), new Vector3(9.875f, LedgeTop, 27f));
            Arch.Parapet(p, new Vector3(-10f, LedgeTop, 26.875f), new Vector3(10f, LedgeTop, 26.875f));
            Arch.BracketFrame(p, ExitPad, Dir.NegZ, 2f, 3f, 0.5f);
            // Pergola on the ledge top (Pergola builds from its parent's y = 0, so lift a group to the ledge).
            Transform top = Arch.Group(p, "LedgeTop", new Vector3(0f, LedgeTop, 0f)).transform;
            Arch.Pergola(top, new RectXZ(-8f, 18f, -4f, 24f), 3f, 0.5f, Dir.PosX);
            PropKit.Bench(p, new Vector3(-6f, LedgeTop, 21f), Dir.PosX, 2f);
            PropKit.Planter(p, new Vector3(6f, LedgeTop, 20f), Dir.NegX, PlanterStyle.Square, default, 0.625f, 407);
            PropKit.Planter(p, new Vector3(6f, LedgeTop, 24f), Dir.NegX, PlanterStyle.Square, default, 0.625f, 408);

            // ---- the photo's delta: 16 steps (rise 0.25, tread 0.5) from z = 3 to the ledge ----
            if (withStairs) Arch.Stair(p, new Vector3(0f, 0f, StairFoot), Dir.PosZ, LedgeTop, 2f, true);
        }
    }
}
