using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels
{
    /// <summary>
    /// Room 2 — Doorway. A 7 m tall wall (z 8.5 → 9.5) spans the whole island and overhangs the void,
    /// so there is no way around. The photo is "open sky": its diorama is this room's terrain WITHOUT the
    /// wall (plus a little cloud), captured level from the marker (0,0,4). Placing it from the marker
    /// cuts a doorway-sized hole (≈5 m wide, floor to ≈3.7 m at the wall) and keeps the floor intact
    /// because the diorama floor matches the room floor.
    /// </summary>
    public sealed class DoorwayRoom : Room
    {
        public override string Title => "Room 2: Doorway";
        public override string Intro => "A wall. And a photo of open sky...";

        static readonly Vector3 Marker = new Vector3(0f, 0f, 4f);

        public override void Build(Transform root, RoomContext ctx)
        {
            BuildTerrain(root);
            BuildWall(root);

            ctx.SetSpawn(new Vector3(0f, 0f, -7f));
            ctx.AddCheckpoint(new Vector3(0f, 0f, 16f));

            var shot = ctx.RegisterDioramaShot(Marker, 0f, 0f, "Open sky");
            BuildTerrain(ctx.DioramaRoot);
            Kit.Cloud(ctx.DioramaRoot, new Vector3(3f, 8.5f, 27f), 1.2f);
            Kit.Cloud(ctx.DioramaRoot, new Vector3(-5f, 11f, 40f), 1.6f);

            ctx.PlacePhotoPickup(new Vector3(-3f, 0f, -3.5f), shot);
            ctx.CreateMarker(Marker, LevelColors.MarkerPhoto,
                "Face the wall, look straight ahead and place the sky photo");
            ctx.CreateTeleporter(new Vector3(8.5f, 0f, 11.5f));

            AddSolution("place", RoomSolution.Kind.Place, Marker, 0f, 0f, 0);
            AddSolution("far", RoomSolution.Kind.Goal, new Vector3(0f, 0f, 14f));
            AddSolution("exit", RoomSolution.Kind.Goal, new Vector3(8.5f, 0f, 11.5f));
        }

        static void BuildTerrain(Transform p)
        {
            Kit.Island(p, -10f, 10f, -10f, 26f, 0f, 21);

            Kit.Tree(p, new Vector3(-7f, 0f, -7.5f), 1.1f, 0f);
            Kit.Tree(p, new Vector3(7f, 0f, -6f), 0.9f, 33f, true);
            Kit.Rock(p, new Vector3(-7.5f, 0f, 3f), 1f, 90f);

            Kit.Tree(p, new Vector3(-6.5f, 0f, 19.5f), 1.2f, 15f, true);
            Kit.Tree(p, new Vector3(5.5f, 0f, 22.5f), 1f, 60f);
            Kit.Tree(p, new Vector3(-8f, 0f, 13.5f), 0.8f, 5f);
            Kit.Rock(p, new Vector3(2.5f, 0f, 24f), 1.3f, 160f);
        }

        static void BuildWall(Transform p)
        {
            Geo.Box(p, new Vector3(0f, 3.5f, 9f), new Vector3(24f, 7f, 1f), LevelColors.Wall).name = "Wall";
            Geo.Box(p, new Vector3(0f, 7.15f, 9f), new Vector3(24.6f, 0.3f, 1.4f), LevelColors.Trim).name = "WallCap";
            Geo.Box(p, new Vector3(0f, 0.2f, 9f), new Vector3(24.2f, 0.4f, 1.2f), Palette.Stone).name = "WallSkirt";
            Geo.Box(p, new Vector3(-12.3f, 3.7f, 9f), new Vector3(0.8f, 7.4f, 1.8f), LevelColors.Pier).name = "WallPier";
            Geo.Box(p, new Vector3(12.3f, 3.7f, 9f), new Vector3(0.8f, 7.4f, 1.8f), LevelColors.Pier).name = "WallPier";
            // Collider-less pilasters on both faces break up the long plain wall (cut with it by a photo).
            for (int i = -2; i <= 2; i++)
                Kit.Pilaster(p, new Vector3(i * 4.6f, 0.4f, 9f), 6.6f, 1f, LevelColors.WallTrim);
        }
    }
}
