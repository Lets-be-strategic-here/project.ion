using System.Collections.Generic;
using UnityEngine;

namespace Ion.Levels
{
    /// <summary>
    /// Dresses a built room with collider-less detail: stepping stones from the spawn to each photo /
    /// camera marker, grass tufts and flower clusters on open grass. Candidates are drawn once from a
    /// per-room seed and then tested against each root (world and diorama) with raycasts, so wherever
    /// the two share terrain they get exactly the same dressing, and a photo pasted over the world
    /// lines up. Everything is tagged <see cref="Decor"/> and merged by <see cref="DecorCombiner"/>.
    /// </summary>
    public static class Scatter
    {
        struct Candidate
        {
            public float X, Z, Scale, Yaw;
            public int Kind;   // 0 = dark tuft, 1 = light tuft, 2 = flowers
            public int Bloom;
        }

        const float EdgeMargin = 0.45f;

        public static void Decorate(RoomContext ctx)
        {
            if (ctx == null) return;
            Physics.SyncTransforms();
            Transform world = ctx.WorldRoot;
            if (!GrassBounds(world, out Vector2 min, out Vector2 max)) return;

            var rng = new System.Random(5150 + ctx.Index * 97);
            float R() => (float)rng.NextDouble();

            // 1. Stepping stones from the spawn to every place / snap spot on the same level.
            var stones = new List<Vector4>(); // x, z, radius, yaw
            Vector3 spawn = world.InverseTransformPoint(ctx.Spawn.position);
            foreach (RoomSolution s in ctx.Room.Solutions)
            {
                if (s.Action == RoomSolution.Kind.Goal || Mathf.Abs(s.LocalFeet.y - spawn.y) > 0.1f) continue;
                Vector2 a = new Vector2(spawn.x, spawn.z), b = new Vector2(s.LocalFeet.x, s.LocalFeet.z);
                float len = Vector2.Distance(a, b);
                if (len < 3f) continue;
                Vector2 dir = (b - a) / len, side = new Vector2(dir.y, -dir.x);
                int k = 0;
                for (float d = 1.2f; d < len - 1.6f; d += 0.78f, k++)
                {
                    float lateral = ((k % 2 == 0) ? 0.16f : -0.16f) + (R() - 0.5f) * 0.1f;
                    Vector2 p = a + dir * d + side * lateral;
                    stones.Add(new Vector4(p.x, p.y, 0.19f + 0.08f * R(), R() * 360f));
                }
            }

            // 2. Tufts and flower clusters.
            float area = (max.x - min.x) * (max.y - min.y);
            int tufts = Mathf.Clamp(Mathf.RoundToInt(area * 0.06f), 24, 200);
            int flowers = Mathf.Clamp(Mathf.RoundToInt(area * 0.014f), 6, 44);
            var cands = new List<Candidate>(tufts + flowers);
            for (int i = 0; i < tufts + flowers; i++)
            {
                bool flower = i >= tufts;
                cands.Add(new Candidate
                {
                    X = Mathf.Lerp(min.x, max.x, R()),
                    Z = Mathf.Lerp(min.y, max.y, R()),
                    Scale = flower ? 0.9f + 0.35f * R() : 0.75f + 0.55f * R(),
                    Yaw = R() * 360f,
                    Kind = flower ? 2 : (R() < 0.45f ? 1 : 0),
                    Bloom = rng.Next(LevelColors.Blooms.Length),
                });
            }

            Apply(ctx, world, stones, cands);
            if (ctx.HasDiorama) Apply(ctx, ctx.DioramaRoot, stones, cands);
        }

        static void Apply(RoomContext ctx, Transform root, List<Vector4> stones, List<Candidate> cands)
        {
            var clear = new List<Vector3>(ctx.KeepClear);
            for (int i = 0; i < stones.Count; i++)
            {
                Vector4 s = stones[i];
                if (InClear(clear, s.x, s.y, 0f) || !Ground(root, s.x, s.y, 0.2f, out float y)) continue;
                Kit.SteppingStone(root, new Vector3(s.x, y, s.y), s.z, s.w);
            }
            // Keep the path itself free of grass.
            for (int i = 0; i < stones.Count; i++) clear.Add(new Vector3(stones[i].x, stones[i].y, 0.55f));

            for (int i = 0; i < cands.Count; i++)
            {
                Candidate c = cands[i];
                if (InClear(clear, c.X, c.Z, 0.2f) || !Ground(root, c.X, c.Z, EdgeMargin, out float y)) continue;
                var pos = new Vector3(c.X, y, c.Z);
                if (c.Kind == 2) Kit.Flowers(root, pos, c.Scale, c.Yaw, LevelColors.Blooms[c.Bloom]);
                else Kit.Tuft(root, pos, c.Scale, c.Yaw, c.Kind == 1);
                clear.Add(new Vector3(c.X, c.Z, 0.35f)); // no stacking
            }
        }

        static bool InClear(List<Vector3> clear, float x, float z, float pad)
        {
            for (int i = 0; i < clear.Count; i++)
            {
                Vector3 c = clear[i];
                float dx = x - c.x, dz = z - c.y, r = c.z + pad;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        static bool IsGrass(Collider c) => c != null && (c.name == "Grass" || c.name == "LedgeGrass");

        /// <summary>
        /// Open, flat grass at room-local (x, z), at least <paramref name="margin"/> from any edge and with
        /// nothing standing on it. Returns the room-local ground height.
        /// </summary>
        static bool Ground(Transform root, float x, float z, float margin, out float y)
        {
            y = 0f;
            if (!Probe(root, x, z, out RaycastHit hit)) return false;
            y = root.InverseTransformPoint(hit.point).y;
            if (margin > 0f)
            {
                for (int i = 0; i < 4; i++)
                {
                    float ox = i == 0 ? margin : i == 1 ? -margin : 0f;
                    float oz = i == 2 ? margin : i == 3 ? -margin : 0f;
                    if (!Probe(root, x + ox, z + oz, out RaycastHit h2)) return false;
                    if (Mathf.Abs(root.InverseTransformPoint(h2.point).y - y) > 0.01f) return false;
                }
            }
            // Nothing (trees, rocks, posts, walls, teleporter triggers) in the way.
            return !Physics.CheckSphere(hit.point + Vector3.up * 0.5f, 0.42f, ~0, QueryTriggerInteraction.Collide);
        }

        static bool Probe(Transform root, float x, float z, out RaycastHit hit)
        {
            Vector3 o = root.TransformPoint(new Vector3(x, 40f, z));
            if (!Physics.Raycast(o, Vector3.down, out hit, 90f, ~0, QueryTriggerInteraction.Ignore)) return false;
            return IsGrass(hit.collider) && hit.normal.y > 0.99f;
        }

        static bool GrassBounds(Transform root, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (Collider c in root.GetComponentsInChildren<Collider>())
            {
                if (!IsGrass(c)) continue;
                Bounds b = c.bounds;
                Vector3 lo = root.InverseTransformPoint(b.min), hi = root.InverseTransformPoint(b.max);
                min = Vector2.Min(min, new Vector2(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.z, hi.z)));
                max = Vector2.Max(max, new Vector2(Mathf.Max(lo.x, hi.x), Mathf.Max(lo.z, hi.z)));
                any = true;
            }
            return any;
        }
    }
}
