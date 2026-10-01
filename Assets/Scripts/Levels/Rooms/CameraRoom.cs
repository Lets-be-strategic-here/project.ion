using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels
{
    /// <summary>
    /// Room 4 — Camera. The instant camera (3 film) floats near the spawn. Ahead, a 3 m cliff
    /// (front face z = 14) blocks the way. Behind the spawn is a back terrace (a dead end, also 3 m tall,
    /// front face z = −17, as wide as the cliff) with a ramp climbing a channel cut into it toward −Z.
    ///
    /// Solution: stand on the lavender marker (0,0,−11) facing −Z and snap the ramp (the terrace face is
    /// 6 m ahead). Then stand on the coral marker (0,0,8) facing +Z — also 6 m from the cliff — and place
    /// the snapshot: the terrace face lands on the cliff face, both 3 m tall, so the pasted terrace merges
    /// with the real ledge and the ramp channel becomes the way up. Standing elsewhere still works (the
    /// whole view cone is replaced consistently), it just shifts the ramp.
    /// Both shots look along ±Z, away from the neighbouring rooms laid out on X. The instant camera
    /// uses the player's 70° FOV (≈43° horizontal half-angle), so the teleporter sits ~54° off the cliff
    /// marker's view axis and a paste from the marker never removes it.
    /// </summary>
    public sealed class CameraRoom : Room
    {
        public override string Title => "Room 4: Camera";
        public override string Intro => "Grab the instant camera.";

        const float CliffFront = 14f, CliffTop = 3f;
        const float TerraceFront = -17f;   // terrace face; the ramp channel runs from here to z = -24
        const float RampRun = 7f, RampWidth = 3f;

        public override void Build(Transform root, RoomContext ctx)
        {
            Kit.Island(root, -12f, 12f, TerraceFront, CliffFront, 0f, 41);
            // Front cliff + its upper level.
            Kit.Block(root, -12f, 12f, CliffFront, 32f, CliffTop, -3.5f);
            Kit.Island(root, -12f, 12f, CliffFront, 32f, -3.5f, 42, 2.5f);
            // Back terrace (dead end) with a ramp climbing a 3 m wide channel cut into its front.
            const float half = RampWidth * 0.5f;
            float rampTop = TerraceFront - RampRun;
            Kit.Block(root, -12f, -half, -42f, TerraceFront, CliffTop, -3.5f);
            Kit.Block(root, half, 12f, -42f, TerraceFront, CliffTop, -3.5f);
            Kit.Block(root, -half, half, -42f, rampTop, CliffTop, -3.5f);
            Kit.Island(root, -12f, 12f, -42f, TerraceFront, -3.5f, 43, 2.5f);
            Geo.Ramp(root, new Vector3(0f, 0f, TerraceFront), RampWidth, RampRun, CliffTop, Palette.Coral, 180f);
            Geo.Box(root, new Vector3(0f, -1.75f, TerraceFront - RampRun * 0.5f), new Vector3(RampWidth, 3.5f, RampRun), LevelColors.Dirt).name = "RampFill";

            // Decoration.
            Kit.Tree(root, new Vector3(-8f, 0f, 6f), 1f, 0f);
            Kit.Tree(root, new Vector3(8.5f, 0f, -6f), 1.2f, 30f, true);
            Kit.Rock(root, new Vector3(7f, 0f, 9f), 1f, 120f);
            Kit.Tree(root, new Vector3(-7f, CliffTop, 26f), 1.1f, 15f, true);
            Kit.Tree(root, new Vector3(4f, CliffTop, 28f), 1.3f, 70f);
            Kit.Tree(root, new Vector3(-6f, CliffTop, -34f), 1.2f, 5f);
            Kit.Tree(root, new Vector3(7f, CliffTop, -38f), 0.9f, 40f, true);
            Kit.Rock(root, new Vector3(3f, CliffTop, -30f), 1f, 210f);

            ctx.SetSpawn(new Vector3(0f, 0f, 0f));
            ctx.AddCheckpoint(new Vector3(0f, CliffTop, 24f));

            ctx.CreateCameraPickup(new Vector3(-3.5f, 0f, 3f), 3);
            ctx.CreateMarker(new Vector3(0f, 0f, -11f), 180f, LevelColors.MarkerCamera,
                "Snap the ramp from here:  C for the camera, hold RMB, LMB to shoot");
            ctx.CreateMarker(new Vector3(0f, 0f, 8f), LevelColors.MarkerPhoto,
                "Hold up your ramp snapshot here (RMB) and place it (LMB)");
            ctx.CreateHint(new Vector3(0f, 0f, 12f), 2f, "Too high. There was a ramp behind you...", 3.5f).Once = true;
            ctx.CreateTeleporter(new Vector3(10.5f, CliffTop, 15.5f));
        }
    }
}
