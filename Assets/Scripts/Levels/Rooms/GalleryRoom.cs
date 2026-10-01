using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels
{
    /// <summary>
    /// Room 5 — Gallery. A calm courtyard with six empty frames ("Coming soon" placeholders for the real
    /// portfolio projects) on two low walls, and the final teleporter at the far end.
    /// </summary>
    public sealed class GalleryRoom : Room
    {
        public override string Title => "Room 5: Gallery";
        public override string Intro => "Projects will hang here soon.";

        const float WallX = 9.5f;
        static readonly float[] FrameZ = { 2f, 8f, 14f };
        static readonly Color[] CanvasColors = { Palette.Sky, Palette.Peach, Palette.Lavender, Palette.Butter, Palette.Teal, Palette.Mint };

        public override void Build(Transform root, RoomContext ctx)
        {
            Kit.Island(root, -12f, 12f, -10f, 26f, 0f, 51);

            // Paved path.
            Geo.Box(root, new Vector3(0f, 0.02f, 8f), new Vector3(4f, 0.04f, 30f), LevelColors.PathStone).name = "Path";

            int n = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = WallX * side;
                Geo.Box(root, new Vector3(x, 2f, 8f), new Vector3(0.6f, 4f, 18f), LevelColors.Wall).name = "GalleryWall";
                Geo.Box(root, new Vector3(x, 4.1f, 8f), new Vector3(0.9f, 0.2f, 18.4f), LevelColors.Trim).name = "GalleryWallCap";

                float face = x - side * 0.3f;          // inner face of the wall
                Quaternion readRot = Quaternion.LookRotation(new Vector3(side, 0f, 0f)); // forward points into the wall
                for (int i = 0; i < FrameZ.Length; i++, n++)
                {
                    float z = FrameZ[i];
                    Geo.Box(root, new Vector3(face - side * 0.06f, 2.2f, z), new Vector3(0.12f, 2.1f, 3f), Palette.Wood).name = "Frame";
                    Geo.Box(root, new Vector3(face - side * 0.1f, 2.2f, z), new Vector3(0.08f, 1.7f, 2.6f), CanvasColors[n % CanvasColors.Length]).name = "Canvas";
                    ctx.Label(new Vector3(face - side * 0.16f, 2.45f, z), readRot, "Project " + (n + 1).ToString("00"), 40, Palette.Ink, 2.5f);
                    ctx.Label(new Vector3(face - side * 0.16f, 1.85f, z), readRot, "Coming soon", 30, Palette.Slate, 2.5f);
                    // Little plinth light under each frame.
                    Geo.Box(root, new Vector3(face - side * 0.5f, 0.25f, z), new Vector3(0.6f, 0.5f, 1.2f), Palette.Cream).name = "Plinth";
                }
            }

            // Benches.
            Geo.Box(root, new Vector3(-3.5f, 0.45f, 8f), new Vector3(0.8f, 0.1f, 3f), Palette.Wood).name = "Bench";
            Geo.Box(root, new Vector3(-3.5f, 0.2f, 7f), new Vector3(0.6f, 0.4f, 0.3f), Palette.DarkWood).name = "BenchLeg";
            Geo.Box(root, new Vector3(-3.5f, 0.2f, 9f), new Vector3(0.6f, 0.4f, 0.3f), Palette.DarkWood).name = "BenchLeg";
            Geo.Box(root, new Vector3(3.5f, 0.45f, 8f), new Vector3(0.8f, 0.1f, 3f), Palette.Wood).name = "Bench";
            Geo.Box(root, new Vector3(3.5f, 0.2f, 7f), new Vector3(0.6f, 0.4f, 0.3f), Palette.DarkWood).name = "BenchLeg";
            Geo.Box(root, new Vector3(3.5f, 0.2f, 9f), new Vector3(0.6f, 0.4f, 0.3f), Palette.DarkWood).name = "BenchLeg";

            // Greenery.
            Kit.Tree(root, new Vector3(-8f, 0f, -7f), 1.2f, 10f, true);
            Kit.Tree(root, new Vector3(8f, 0f, -6.5f), 1f, 50f);
            Kit.Tree(root, new Vector3(-8.5f, 0f, 22f), 1.3f, 30f);
            Kit.Tree(root, new Vector3(8.5f, 0f, 23f), 1.1f, 0f, true);
            Kit.Rock(root, new Vector3(-5f, 0f, -8f), 0.8f, 100f);
            Kit.Rock(root, new Vector3(5.5f, 0f, 20f), 1f, 220f);

            // Title above the exit.
            ctx.Label(new Vector3(0f, 4.6f, 21f), Quaternion.identity, "|project|ion", 90, Palette.Ink, 8f);
            ctx.Label(new Vector3(0f, 3.8f, 21f), Quaternion.identity, "more rooms coming soon", 36, Palette.Slate, 8f);

            ctx.SetSpawn(new Vector3(0f, 0f, -6f));
            ctx.CreateHint(new Vector3(0f, 0f, 8f), 3f, "These frames will hold real projects soon.", 3.5f).Once = true;

            AddSolution("exit", RoomSolution.Kind.Goal, new Vector3(0f, 0f, 21f));

            var spawn = ctx.Spawn;
            ctx.CreateTeleporter(new Vector3(0f, 0f, 21f), () =>
            {
                RoomContext.Toast("Thanks for playing |project|ion", 7f);
                var player = Ion.Gameplay.FirstPersonController.Current;
                if (player != null) player.Teleport(spawn.position, spawn.rotation.eulerAngles.y);
            });
        }
    }
}
