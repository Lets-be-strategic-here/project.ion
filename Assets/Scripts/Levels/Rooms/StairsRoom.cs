using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels
{
    /// <summary>
    /// Room 3 — Stairs. A 4 m ledge (front face at z = 11) blocks the way. The diorama is this room's
    /// terrain plus a staircase (16 steps × 0.25 m, z 3.8 → 11) climbing to the ledge top.
    ///
    /// How the "sideways" photo works: the diorama camera stands on the marker spot (0,0,0) looking level
    /// toward +Z but ROLLED 90° about its forward axis. The preview therefore shows the stairs lying on
    /// their side. Placement uses worldPose = viewerRolled · capture⁻¹ · piece, so if the player places it
    /// unrotated the whole scene is pasted rolled 90° (stairs on their side, unclimbable). Rotating the
    /// raised photo by 90° (Q or E — one of the two directions; the other gives upside-down stairs) makes
    /// viewerRolled match the capture roll, and the stairs and ledge paste upright and aligned.
    /// The tall-and-narrow cone (25° horizontal half-angle once rotated) still covers the 3 m stairs.
    /// </summary>
    public sealed class StairsRoom : Room
    {
        public override string Title => "Room 3: Stairs";
        public override string Intro => "Too high to climb. That photo looks sideways...";

        static readonly Vector3 Marker = new Vector3(0f, 0f, 0f);

        const int StepCount = 16;
        const float StepRise = 0.25f, StepDepth = 0.45f;
        const float LedgeFront = 11f, LedgeTop = StepCount * StepRise; // 4 m

        public override void Build(Transform root, RoomContext ctx)
        {
            BuildTerrain(root);

            ctx.SetSpawn(new Vector3(0f, 0f, -8f));
            ctx.AddCheckpoint(new Vector3(0f, LedgeTop, 20f));

            var shot = ctx.RegisterDioramaShot(Marker, 0f, 90f, "Stairs");
            BuildTerrain(ctx.DioramaRoot);
            BuildStairs(ctx.DioramaRoot);

            ctx.PlacePhotoPickup(new Vector3(-3.5f, 0f, -5.5f), shot);
            ctx.CreateMarker(Marker, LevelColors.MarkerPhoto,
                "This photo is sideways.  Raise it, turn it upright with Q / E, place");
            ctx.CreateTeleporter(new Vector3(10f, LedgeTop, 13f));

            AddSolution("place", RoomSolution.Kind.Place, Marker, 0f, 0f, 0, 90f);
            AddSolution("far", RoomSolution.Kind.Goal, new Vector3(0f, LedgeTop, 16f));
            AddSolution("exit", RoomSolution.Kind.Goal, new Vector3(10f, LedgeTop, 13f));
        }

        static void BuildTerrain(Transform p)
        {
            Kit.Island(p, -9f, 9f, -11f, LedgeFront, 0f, 31);
            // The ledge: a tall block with its own island underneath.
            Kit.Block(p, -12f, 12f, LedgeFront, 31f, LedgeTop, -3.5f);
            Kit.Island(p, -12f, 12f, LedgeFront, 31f, -3.5f, 32, 2.5f);

            Kit.Tree(p, new Vector3(-6.5f, 0f, -8.5f), 1f, 20f);
            Kit.Tree(p, new Vector3(6.5f, 0f, -9f), 1.2f, 70f, true);
            Kit.Rock(p, new Vector3(6.8f, 0f, 6f), 1.1f, 250f);
            Kit.Rock(p, new Vector3(-7f, 0f, 8f), 0.8f, 10f);

            Kit.Tree(p, new Vector3(-8f, LedgeTop, 17f), 1.1f, 0f, true);
            Kit.Tree(p, new Vector3(-3f, LedgeTop, 26f), 1.3f, 45f);
            Kit.Tree(p, new Vector3(7f, LedgeTop, 24f), 1f, 10f);
            Kit.Rock(p, new Vector3(3.5f, LedgeTop, 19f), 1f, 300f);
        }

        /// <summary>Solid step columns, top step flush with the ledge.</summary>
        static void BuildStairs(Transform p)
        {
            float z0 = LedgeFront - StepCount * StepDepth; // 3.8
            for (int i = 0; i < StepCount; i++)
            {
                float h = (i + 1) * StepRise;
                float z = z0 + (i + 0.5f) * StepDepth;
                Geo.Box(p, new Vector3(0f, (h - 0.1f) * 0.5f, z), new Vector3(3f, h + 0.1f, StepDepth), LevelColors.Stairs).name = "Step";
            }
            // Newel posts at the foot of the stairs.
            for (int side = -1; side <= 1; side += 2)
                Geo.Box(p, new Vector3(1.65f * side, 0.55f, z0 + 0.15f), new Vector3(0.3f, 1.1f, 0.3f), Palette.Cream).name = "Newel";
        }
    }
}
