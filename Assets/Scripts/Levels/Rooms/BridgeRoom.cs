using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels
{
    /// <summary>
    /// Room 1 — Bridge. Two islands separated by a 7 m chasm (z 6 → 13). The photo on the pedestal was
    /// taken in a diorama that is an exact copy of this room's terrain plus a wooden bridge, from the
    /// marker spot (0,0,2) looking level toward +Z. Standing on the marker, looking straight ahead and
    /// placing the photo replaces everything in the view cone with the same terrain + the bridge.
    /// Teaches raise (RMB), place (LMB) and rewind (R).
    ///
    /// The exit teleporter sits 45° off the view axis (outside the 50°×~64° photo cone) so placing the
    /// photo never deletes it.
    /// </summary>
    public sealed class BridgeRoom : Room
    {
        public override string Title => "Room 1: Bridge";
        public override string Intro => "Pick up the photo on the pedestal.";

        static readonly Vector3 Marker = new Vector3(0f, 0f, 2f);

        public override void Build(Transform root, RoomContext ctx)
        {
            BuildTerrain(root);

            ctx.SetSpawn(new Vector3(0f, 0f, -8f));
            ctx.AddCheckpoint(new Vector3(0f, 0f, 20f));

            var shot = ctx.RegisterDioramaShot(Marker, 0f, 0f, "Bridge");
            BuildTerrain(ctx.DioramaRoot);
            BuildBridge(ctx.DioramaRoot);

            ctx.PlacePhotoPickup(new Vector3(-3f, 0f, -4f), shot);
            ctx.CreateMarker(Marker, LevelColors.MarkerPhoto,
                "Look straight ahead, hold RMB to raise the photo, LMB to place.  R rewinds");
            ctx.CreateHint(new Vector3(0f, 0f, 5.2f), 1.5f, "Too far to jump... maybe the photo can help?", 3.5f).Once = true;
            ctx.CreateTeleporter(new Vector3(13f, 0f, 15f));
        }

        static void BuildTerrain(Transform p)
        {
            // Start island A and far island B.
            Kit.Island(p, -8f, 8f, -12f, 6f, 0f, 11);
            Kit.Island(p, -16f, 16f, 13f, 31f, 0f, 12);

            // Broken bridge anchors on both banks.
            Kit.Post(p, new Vector3(-2.1f, 0f, 5.6f));
            Kit.Post(p, new Vector3(2.1f, 0f, 5.6f));
            Kit.Post(p, new Vector3(-2.1f, 0f, 13.4f));
            Kit.Post(p, new Vector3(2.1f, 0f, 13.4f));

            // Decoration (kept off the walking line x ∈ [-3, 3]).
            Kit.Tree(p, new Vector3(-5.5f, 0f, -9f), 1.1f, 10f);
            Kit.Tree(p, new Vector3(5.8f, 0f, -10f), 0.9f, 40f, true);
            Kit.Tree(p, new Vector3(6.2f, 0f, -3.5f), 1f, 75f);
            Kit.Rock(p, new Vector3(-6f, 0f, 2.5f), 1f, 20f);
            Kit.Rock(p, new Vector3(5.5f, 0f, 3.5f), 0.7f, 200f);

            Kit.Tree(p, new Vector3(-12f, 0f, 21f), 1.2f, 0f, true);
            Kit.Tree(p, new Vector3(-6.5f, 0f, 27.5f), 1f, 50f);
            Kit.Tree(p, new Vector3(5f, 0f, 27f), 1.3f, 20f);
            Kit.Tree(p, new Vector3(12.5f, 0f, 25f), 0.9f, 80f, true);
            Kit.Rock(p, new Vector3(-9f, 0f, 16f), 1.2f, 60f);
            Kit.Rock(p, new Vector3(8f, 0f, 21f), 0.9f, 130f);
        }

        /// <summary>A 4 m wide plank bridge spanning z 5 → 14 (deck top 6 cm above the grass).</summary>
        static void BuildBridge(Transform p)
        {
            const float z0 = 5f, z1 = 14f;
            float len = z1 - z0, zc = (z0 + z1) * 0.5f;
            Geo.Box(p, new Vector3(0f, -0.09f, zc), new Vector3(4f, 0.3f, len), Palette.Wood).name = "BridgeDeck";
            // Plank seams (thin darker strips laid on the deck).
            for (float z = z0 + 1f; z < z1 - 0.5f; z += 1.5f)
                Geo.Box(p, new Vector3(0f, 0.07f, z), new Vector3(4f, 0.02f, 0.12f), Palette.DarkWood).name = "Seam";
            // Beams underneath.
            Geo.Box(p, new Vector3(-1.4f, -0.45f, zc), new Vector3(0.3f, 0.45f, len), Palette.DarkWood).name = "Beam";
            Geo.Box(p, new Vector3(1.4f, -0.45f, zc), new Vector3(0.3f, 0.45f, len), Palette.DarkWood).name = "Beam";
            // Rails.
            for (int side = -1; side <= 1; side += 2)
            {
                float x = 2.1f * side;
                Geo.Box(p, new Vector3(x, 0.95f, zc), new Vector3(0.14f, 0.12f, len), Palette.DarkWood).name = "Rail";
                for (float z = z0 + 0.6f; z <= z1 - 0.5f; z += 2.6f)
                    Geo.Box(p, new Vector3(x, 0.5f, z), new Vector3(0.18f, 0.9f, 0.18f), Palette.DarkWood).name = "RailPost";
            }
        }
    }
}
