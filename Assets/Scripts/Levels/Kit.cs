using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels
{
    /// <summary>
    /// Composite low-poly pieces (floating islands, ledges, trees, rocks, clouds) built from <see cref="Geo"/>.
    /// All positions are local to <c>parent</c>. Deterministic for a given seed, so a room's terrain can be
    /// built twice (world + photo diorama) and come out identical.
    /// </summary>
    public static class Kit
    {
        const float GrassDepth = 0.5f;

        /// <summary>
        /// A floating island whose walkable top is the rectangle [x0,x1]×[z0,z1] at height <paramref name="top"/>:
        /// grass slab, dirt body, a rocky core and a few downward spikes.
        /// </summary>
        public static void Island(Transform p, float x0, float x1, float z0, float z1, float top = 0f, int seed = 0, float depth = 3.5f)
        {
            float w = x1 - x0, d = z1 - z0;
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            Geo.Box(p, new Vector3(cx, top - GrassDepth * 0.5f, cz), new Vector3(w, GrassDepth, d), LevelColors.Grass).name = "Grass";
            float dirtH = depth - GrassDepth;
            Geo.Box(p, new Vector3(cx, top - GrassDepth - dirtH * 0.5f, cz), new Vector3(w, dirtH, d), LevelColors.Dirt).name = "Dirt";

            // Rocky core, inset.
            float coreW = Mathf.Max(1f, w - 2.4f), coreD = Mathf.Max(1f, d - 2.4f);
            float coreTop = top - depth;
            Geo.Box(p, new Vector3(cx, coreTop - 1.25f, cz), new Vector3(coreW, 2.5f, coreD), LevelColors.Rock).name = "Core";

            // Spikes hanging below.
            var rng = new System.Random(seed * 31 + 7);
            int count = Mathf.Clamp(Mathf.RoundToInt(w * d / 70f), 2, 9);
            float spikeTop = coreTop - 2.5f;
            for (int i = 0; i < count; i++)
            {
                float sx = Mathf.Lerp(x0 + 2f, x1 - 2f, (float)rng.NextDouble());
                float sz = Mathf.Lerp(z0 + 2f, z1 - 2f, (float)rng.NextDouble());
                float r = Mathf.Lerp(2.5f, 5f, (float)rng.NextDouble());
                r = Mathf.Min(r, Mathf.Min(coreW, coreD));
                float h = Mathf.Lerp(3f, 8f, (float)rng.NextDouble());
                Geo.Spike(p, new Vector3(sx, spikeTop - h * 0.5f + 0.3f, sz), new Vector3(r, h, r),
                          i % 2 == 0 ? LevelColors.Rock : LevelColors.Dirt, 5, (float)rng.NextDouble() * 360f);
            }
        }

        /// <summary>
        /// A raised block (ledge/mesa) covering [x0,x1]×[z0,z1] from <paramref name="bottom"/> up to
        /// <paramref name="top"/>, with a grass cap.
        /// </summary>
        public static void Block(Transform p, float x0, float x1, float z0, float z1, float top, float bottom)
        {
            float w = x1 - x0, d = z1 - z0;
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            Geo.Box(p, new Vector3(cx, top - GrassDepth * 0.5f, cz), new Vector3(w, GrassDepth, d), LevelColors.Grass).name = "LedgeGrass";
            float bodyH = top - GrassDepth - bottom;
            Geo.Box(p, new Vector3(cx, bottom + bodyH * 0.5f, cz), new Vector3(w, bodyH, d), LevelColors.Dirt).name = "LedgeBody";
        }

        /// <summary>Low-poly tree: box trunk + two stacked cones (~40 triangles).</summary>
        public static void Tree(Transform p, Vector3 basePos, float scale = 1f, float yRot = 0f, bool dark = false)
        {
            float s = scale;
            Geo.Box(p, basePos + new Vector3(0f, 0.6f * s, 0f), new Vector3(0.35f * s, 1.2f * s, 0.35f * s), LevelColors.Trunk, yRot).name = "Trunk";
            Color leaves = dark ? LevelColors.LeavesDark : LevelColors.Leaves;
            Geo.Cone(p, basePos + new Vector3(0f, 2.1f * s, 0f), new Vector3(2.2f * s, 2.2f * s, 2.2f * s), leaves, 6, yRot).name = "Leaves";
            Geo.Cone(p, basePos + new Vector3(0f, 3.3f * s, 0f), new Vector3(1.5f * s, 1.7f * s, 1.5f * s), leaves, 6, yRot + 30f).name = "Leaves";
        }

        /// <summary>Rock: a squat wedge plus a small pyramid.</summary>
        public static void Rock(Transform p, Vector3 basePos, float scale = 1f, float yRot = 0f)
        {
            float s = scale;
            Geo.Wedge(p, basePos + new Vector3(0f, 0.35f * s, 0f), new Vector3(1.4f * s, 0.7f * s, 1.1f * s), LevelColors.Rock, yRot).name = "Rock";
            Geo.Cone(p, basePos + new Vector3(0.5f * s, 0.25f * s, -0.4f * s), new Vector3(0.8f * s, 0.5f * s, 0.8f * s), Palette.Stone, 4, yRot + 20f).name = "Pebble";
        }

        /// <summary>Puffy cloud of three boxes.</summary>
        public static void Cloud(Transform p, Vector3 center, float scale = 1f)
        {
            float s = scale;
            Geo.Box(p, center, new Vector3(4f * s, 1.2f * s, 2.2f * s), LevelColors.Cloud).name = "Cloud";
            Geo.Box(p, center + new Vector3(-0.9f * s, 0.7f * s, 0.1f * s), new Vector3(1.8f * s, 1.2f * s, 1.6f * s), LevelColors.Cloud).name = "Cloud";
            Geo.Box(p, center + new Vector3(0.8f * s, 0.5f * s, -0.1f * s), new Vector3(1.4f * s, 1f * s, 1.4f * s), LevelColors.Cloud).name = "Cloud";
        }

        /// <summary>Short wooden post (bridge anchors, fences).</summary>
        public static void Post(Transform p, Vector3 basePos, float height = 1.1f)
        {
            Geo.Box(p, basePos + new Vector3(0f, height * 0.5f, 0f), new Vector3(0.25f, height, 0.25f), Palette.DarkWood).name = "Post";
        }
    }
}
