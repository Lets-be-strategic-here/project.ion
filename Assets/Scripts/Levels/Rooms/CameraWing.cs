using Ion.Gameplay;
using Ion.Levels.Arch;
using Ion.Levels.Props;
using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels.Rooms
{
    using Arch = Ion.Levels.Arch.Arch;

    /// <summary>
    /// Zone 4, the Camera wing (art bible §7.4), mood Rose, Rose/Formwork structure. The existing puzzle, rebuilt:
    /// the instant camera (3 film) waits on a lectern near the arrival. Ahead, a 3 m cliff (front z = 14). Behind,
    /// a dead-end terrace (front z = −17, also 3 m) with a ramp climbing a 3 m channel toward −Z.
    /// Snap the ramp from the marker (0, 0, −11) facing −Z (6 m to the terrace face), then place the snapshot from
    /// the marker (0, 0, 8) facing +Z (6 m to the cliff): the terrace face lands on the cliff face and the ramp
    /// becomes the way up. The exit returns to hub exhibit [03].
    /// </summary>
    public sealed class CameraWing : Room
    {
        public override string Key => "camera";
        public override string Title => "Camera";
        public override string Intro => "Grab the instant camera.";
        public override ZoneMood Mood => ZoneMood.Rose;
        public override ArchStyle Style => ArchStyle.Rose;

        public const float CliffFront = 14f, CliffTop = 3f;
        public const float TerraceFront = -17f, RampRun = 7f, RampHalf = 1.5f;
        public const float Back = -30f, Far = 32f, Half = 12f;

        public static readonly Vector3 SpawnFeet = new Vector3(0f, 0f, 0f);
        public static readonly Vector3 CameraStandPos = new Vector3(-2.5f, 0f, 4.5f);
        public static readonly Vector3 SnapFeet = new Vector3(0f, 0f, -11f);
        public static readonly Vector3 PlaceFeet = new Vector3(0f, 0f, 8f);
        public static readonly Vector3 ExitPad = new Vector3(10.5f, CliffTop, 16.5f);

        /// <summary>Expected floor height along the centre line once the ramp is pasted (tests).</summary>
        public static float FloorAt(float z)
        {
            if (z <= CliffFront) return 0f;
            if (z >= CliffFront + RampRun) return CliffTop;
            return (z - CliffFront) / RampRun * CliffTop;
        }

        public override void Build(Transform root, RoomContext ctx)
        {
            BuildWing(root);
            ctx.SetSpawn(SpawnFeet, 0f);

            PropKit.CameraStand(root, CameraStandPos, Dir.PosX, 3);
            PropKit.Checkpoint(root, SpawnFeet, 0f, "camera.arrive");
            ctx.Teleporter(ExitPad, Dir.NegX, () => HubLightTable.ReturnFromWing(HubLightTable.Wing.Camera), Mat.Rose);
            ctx.Marker(SnapFeet, 180f);
            ctx.Marker(PlaceFeet, 0f);

            ctx.Hint(new Vector3(0f, 0f, 12f), 2f, "Too high.  There was a ramp behind you...", 3.5f);
            ctx.Hint(SnapFeet, 1.25f, "[C] camera   [SHIFT] aim   [LMB] shoot", 4f,
                     () => { var c = RoomContext.PlayerCamera; return c != null && c.Unlocked; }, false);
            ctx.Hint(PlaceFeet, 1.25f, "[SHIFT] hold up your snapshot   [LMB] place", 4f,
                     () => { var inv = RoomContext.PlayerInventory; return inv != null && inv.Count > 0; }, false);

            AddSolution("pickup", RoomSolution.Kind.Pickup, new Vector3(-1.6f, 0f, 4.5f), 270f);
            AddSolution("snap", RoomSolution.Kind.Snap, SnapFeet, 180f);
            AddSolution("place", RoomSolution.Kind.Place, PlaceFeet, 0f, 0f, -1);
            AddSolution("far", RoomSolution.Kind.Goal, new Vector3(0f, CliffTop, 22f));
            var exit = AddSolution("exit", RoomSolution.Kind.Teleport, ExitPad, 90f);
            exit.Destination = "hub";
            exit.Via = new[] { new Vector3(0f, CliffTop, 24f) };   // clear of the pasted channel parapets
        }

        static void Mass(Transform p, ArchStyle st, float x0, float z0, float x1, float z1)
        {
            Arch.Box(p, new Vector3(x0, 0f, z0), new Vector3(x1, CliffTop - 0.5f, z1), st.Wall);
            Arch.Floor(p, new RectXZ(x0, z0, x1, z1), CliffTop, 0.5f);
        }

        public static void BuildWing(Transform p)
        {
            ArchStyle st = Arch.Style ?? ArchStyle.Rose;

            // ---- one floating base: court z[−17, 14] between the cliff and the back terrace ----
            Arch.Terrace(p, new RectXZ(-Half, Back, Half, Far), 0f, 4f);

            // ---- the cliff (front z = 14, top 3) ----
            Mass(p, st, -Half, CliffFront, Half, Far);
            Arch.Trim(p, new Vector3(-Half, 0f, CliffFront), new Vector3(Half, 0f, CliffFront), Dir.NegZ, TrimProfile.Plinth, 0f);
            foreach (float x in new[] { -8f, -4f, 4f, 8f })
                Arch.Pilaster(p, new Vector3(x, 0f, CliffFront), CliffTop - 0.5f, Dir.NegZ);
            Arch.Parapet(p, new Vector3(-Half, CliffTop, CliffFront + 0.125f), new Vector3(Half, CliffTop, CliffFront + 0.125f));
            Arch.Parapet(p, new Vector3(-11.875f, CliffTop, CliffFront), new Vector3(-11.875f, CliffTop, Far));
            Arch.Parapet(p, new Vector3(11.875f, CliffTop, CliffFront), new Vector3(11.875f, CliffTop, Far));
            Arch.Parapet(p, new Vector3(-Half, CliffTop, Far - 0.125f), new Vector3(Half, CliffTop, Far - 0.125f));
            Transform top = Arch.Group(p, "CliffTop", new Vector3(0f, CliffTop, 0f)).transform;
            Arch.Pergola(top, new RectXZ(-8f, 22f, -2f, 28f), 3f, 0.5f, Dir.PosZ);
            PropKit.Planter(p, new Vector3(6f, CliffTop, 26f), Dir.NegX, PlanterStyle.Bowl, default, 0.5f, 501);
            PropKit.Planter(p, new Vector3(9f, CliffTop, 26f), Dir.NegX, PlanterStyle.Bowl, default, 0.5f, 502);

            // ---- the back terrace (front z = −17, top 3) with a 3 m channel holding the ramp ----
            Mass(p, st, -Half, Back, -RampHalf, TerraceFront);
            Mass(p, st, RampHalf, Back, Half, TerraceFront);
            Mass(p, st, -RampHalf, Back, RampHalf, TerraceFront - RampRun);
            Arch.Trim(p, new Vector3(-Half, 0f, TerraceFront), new Vector3(-RampHalf, 0f, TerraceFront), Dir.PosZ, TrimProfile.Plinth, 0f);
            Arch.Trim(p, new Vector3(RampHalf, 0f, TerraceFront), new Vector3(Half, 0f, TerraceFront), Dir.PosZ, TrimProfile.Plinth, 0f);
            foreach (float x in new[] { -8f, -4f, 4f, 8f })
                Arch.Pilaster(p, new Vector3(x, 0f, TerraceFront), CliffTop - 0.5f, Dir.PosZ);
            Arch.Ramp(p, new Vector3(0f, 0f, TerraceFront), Dir.NegZ, 2f * RampHalf, RampRun, CliffTop, false);
            Arch.Parapet(p, new Vector3(-Half, CliffTop, TerraceFront - 0.125f), new Vector3(-RampHalf, CliffTop, TerraceFront - 0.125f));
            Arch.Parapet(p, new Vector3(RampHalf, CliffTop, TerraceFront - 0.125f), new Vector3(Half, CliffTop, TerraceFront - 0.125f));
            Arch.Parapet(p, new Vector3(-1.625f, CliffTop, TerraceFront - RampRun), new Vector3(-1.625f, CliffTop, TerraceFront));
            Arch.Parapet(p, new Vector3(1.625f, CliffTop, TerraceFront - RampRun), new Vector3(1.625f, CliffTop, TerraceFront));
            Arch.Parapet(p, new Vector3(-Half, CliffTop, Back + 0.125f), new Vector3(Half, CliffTop, Back + 0.125f));
            Arch.Parapet(p, new Vector3(-11.875f, CliffTop, Back), new Vector3(-11.875f, CliffTop, TerraceFront));
            Arch.Parapet(p, new Vector3(11.875f, CliffTop, Back), new Vector3(11.875f, CliffTop, TerraceFront));
            PropKit.Planter(p, new Vector3(-6f, CliffTop, -27f), Dir.PosZ, PlanterStyle.Trough, default, 0.625f, 503);
            PropKit.Planter(p, new Vector3(6f, CliffTop, -27f), Dir.PosZ, PlanterStyle.Trough, default, 0.625f, 504);

            // ---- a house on an east annex beyond the court parapet (rule 4; behind both markers, out of both cones) ----
            Arch.Terrace(p, new RectXZ(Half, -8f, Half + 7f, 4f), 0f, 3f);
            Arch.House(p, new RectXZ(Half + 0.75f, -7.25f, Half + 6.25f, 3.25f), 0f, 1, Dir.NegX);

            // ---- court edges and furniture (kept out of both photo cones) ----
            Arch.Parapet(p, new Vector3(-11.875f, 0f, TerraceFront), new Vector3(-11.875f, 0f, CliffFront));
            Arch.Parapet(p, new Vector3(11.875f, 0f, TerraceFront), new Vector3(11.875f, 0f, CliffFront));
            PropKit.Bench(p, new Vector3(-8f, 0f, 2f), Dir.PosX, 2f);
            PropKit.Bench(p, new Vector3(8f, 0f, -6f), Dir.NegX, 2f);
            PropKit.Planter(p, new Vector3(-9.5f, 0f, -9f), Dir.PosX, PlanterStyle.Square, default, 0.625f, 505);
            PropKit.Planter(p, new Vector3(9.5f, 0f, 9.5f), Dir.NegX, PlanterStyle.Square, default, 0.625f, 506);
            PropKit.Plant(p, new Vector3(-10.5f, 0f, 11.5f), PlantKind.Cypress, 1.1f, 0f, 507);
            PropKit.Plant(p, new Vector3(10.5f, 0f, -14.5f), PlantKind.Cypress, 1f, 0f, 508);
            PropKit.Lamp(p, new Vector3(-5f, 0f, -8f), 0f, LampStyle.Bollard);
            PropKit.Lamp(p, new Vector3(5f, 0f, 4f), 0f, LampStyle.Bollard);
        }
    }
}
