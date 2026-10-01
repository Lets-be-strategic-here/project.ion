using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels.Props
{
    using B = PropBuild;

    public static partial class PropKit
    {
        // ====================================================================== plants (soft clutter)

        /// <summary>
        /// A seeded low-poly plant (soft clutter: sliceable, no collider, any yaw), lush and generous — the
        /// softener of the architecture. Every part is a closed convex mesh merged by DecorCombiner / Arch.Bake
        /// with its sway weight (0 at the base, 1 at the top) in vertex alpha.
        /// - Cypress: stacked 6-gon cones, 2.5–4 m.
        /// - Palm: a 0.15 6-gon trunk with 7 fronds.
        /// - Monstera: 5–7 split leaves tilted 30–60°.
        /// - Strelitzia: upright blade leaves (and one bird-of-paradise flower).
        /// - Grass: 6–10 thin blades.
        /// - Ivy: a curtain of 0.25 leaves hanging down a wall face (<paramref name="basePos"/> = top of the
        ///   curtain on the face, +Z of <paramref name="yaw"/> = out of the wall).
        /// - Shrub: 3 merged 8-gon puffs.
        /// </summary>
        public static GameObject Plant(Transform p, Vector3 basePos, PlantKind kind, float scale = 1f, float yaw = 0f, int seed = 0)
        {
            return Plant(p, basePos, kind, scale, yaw, seed, null);
        }

        /// <summary>
        /// Plant with an accent foliage role replacing its light tips (e.g. <see cref="Mat.Lilac"/> in the Camera wing,
        /// <see cref="Mat.Foliage"/> for a lighter cypress). Addition.
        /// </summary>
        public static GameObject Plant(Transform p, Vector3 basePos, PlantKind kind, float scale, float yaw, int seed, Mat? accent)
        {
            float s = Mathf.Max(0.1f, scale);
            Transform g = B.Frame(p, "Plant " + kind, basePos, yaw);
            var rng = new B.Rng(seed * 92821 + (int)kind * 613 + 1);
            Transform root = B.DecorRoot(p);
            float baseY = root != null ? root.InverseTransformPoint(g.position).y : basePos.y;
            Mat light = accent ?? Mat.FoliageLight;

            switch (kind)
            {
                case PlantKind.Cypress: Cypress(g, rng, s, baseY, accent ?? Mat.FoliageDark); break;
                case PlantKind.Palm: Palm(g, rng, s, baseY, light); break;
                case PlantKind.Monstera: Monstera(g, rng, s, baseY, light); break;
                case PlantKind.Strelitzia: Strelitzia(g, rng, s, baseY, light); break;
                case PlantKind.Grass: Grass(g, rng, s, baseY, light); break;
                case PlantKind.Ivy: Ivy(g, rng, s, baseY, light); break;
                default: Shrub(g, rng, s, baseY, light); break;
            }
            return g.gameObject;
        }

        static B.DecorInfo Info(float baseY, float height, float aoMin = 0.72f, float tint = 0.05f) =>
            new B.DecorInfo { BaseY = baseY, Height = height, AoMin = aoMin, Tint = tint };

        static void Cypress(Transform g, B.Rng rng, float s, float baseY, Mat leaf)
        {
            float h = rng.Range(2.5f, 4f) * s;
            float trunkH = 0.35f * s;
            var info = Info(baseY, h, 0.7f);
            B.DecorPiece(g, "Trunk", Geo.PrismMesh(6), new Vector3(0f, trunkH * 0.5f, 0f), Quaternion.identity,
                         new Vector3(0.16f * s, trunkH, 0.16f * s), Mat.Walnut, info, false);
            // Three stacked, overlapping 6-gon cones, narrowing upward (a column, not a Christmas tree).
            float w = Mathf.Lerp(0.75f, 1.05f, rng.Value) * s * Mathf.Sqrt(h / (3.2f * s));
            float[] y0 = { trunkH * 0.6f, 0.3f * h, 0.55f * h };
            float[] y1 = { 0.62f * h, 0.86f * h, h };
            float[] wk = { 1f, 0.78f, 0.52f };
            for (int i = 0; i < 3; i++)
            {
                float ch = y1[i] - y0[i];
                float cw = w * wk[i];
                Mat m = i == 1 ? Mat.Foliage : leaf;
                B.DecorPiece(g, "Cone", Geo.ConeMesh(6), new Vector3(0f, y0[i] + ch * 0.5f, 0f), Quaternion.Euler(0f, rng.Range(0f, 60f), 0f),
                             new Vector3(cw, ch, cw), m, info, true);
            }
        }

        static void Palm(Transform g, B.Rng rng, float s, float baseY, Mat light)
        {
            float h = rng.Range(2.2f, 3f) * s;
            var info = Info(baseY, h + 0.6f * s, 0.75f);
            // Trunk: three slightly offset 6-gon segments (a gentle lean), 0.15 thick.
            Vector3 lean = new Vector3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)).normalized * 0.12f * s;
            Vector3 prev = Vector3.zero;
            const int segs = 3;
            for (int i = 0; i < segs; i++)
            {
                float t0 = i / (float)segs, t1 = (i + 1) / (float)segs;
                Vector3 a = lean * (t0 * t0) + Vector3.up * (h * t0);
                Vector3 b = lean * (t1 * t1) + Vector3.up * (h * t1);
                Vector3 mid = (a + b) * 0.5f;
                Quaternion r = Quaternion.FromToRotation(Vector3.up, (b - a).normalized) * Quaternion.Euler(0f, i * 20f, 0f);
                float th = Mathf.Lerp(0.17f, 0.13f, t0) * s;
                B.DecorPiece(g, "Trunk", Geo.PrismMesh(6), mid, r, new Vector3(th, (b - a).magnitude + 0.04f * s, th), Mat.Oak, info, false);
                prev = b;
            }
            Vector3 crown = prev;
            B.DecorPiece(g, "Crown", PropMeshes.Puff, crown + new Vector3(0f, 0.04f * s, 0f), Quaternion.identity,
                         new Vector3(0.32f * s, 0.24f * s, 0.32f * s), Mat.FoliageDark, info, true);
            // Seven fronds radiating and drooping.
            float start = rng.Range(0f, 360f);
            for (int i = 0; i < 7; i++)
            {
                float az = start + i * (360f / 7f) + rng.Range(-12f, 12f);
                float len = rng.Range(1.15f, 1.45f) * s;
                float droop = rng.Range(60f, 80f);           // from vertical
                Quaternion r = Quaternion.Euler(0f, az, 0f) * Quaternion.Euler(droop, 0f, 0f);
                Vector3 dir = r * Vector3.up;
                Vector3 c = crown + dir * (len * 0.5f) + Vector3.down * (0.08f * s);
                Mat m = (i & 1) == 0 ? Mat.Foliage : light;
                B.DecorPiece(g, "Frond", PropMeshes.Leaf, c, r * Quaternion.Euler(0f, 0f, 0f),
                             new Vector3(0.34f * s, len, 0.035f * s), m, info, true);
            }
        }

        static void Monstera(Transform g, B.Rng rng, float s, float baseY, Mat light)
        {
            int n = rng.Range(5, 8);
            float h = 0.95f * s;
            var info = Info(baseY, h, 0.7f);
            float start = rng.Range(0f, 360f);
            for (int i = 0; i < n; i++)
            {
                float az = start + i * (360f / n) + rng.Range(-15f, 15f);
                float tilt = rng.Range(30f, 60f);
                float stemLen = rng.Range(0.45f, 0.7f) * s;
                Quaternion sr = Quaternion.Euler(0f, az, 0f) * Quaternion.Euler(tilt * 0.6f, 0f, 0f);
                Vector3 stemTop = sr * Vector3.up * stemLen;
                B.DecorPiece(g, "Stem", Geo.PrismMesh(4), stemTop * 0.5f, sr, new Vector3(0.025f * s, stemLen, 0.025f * s),
                             Mat.FoliageDark, info, true, false);
                // Split leaf: two halves either side of the midrib, with a gap that reads as the split.
                float leafLen = rng.Range(0.4f, 0.55f) * s;
                Quaternion lr = Quaternion.Euler(0f, az, 0f) * Quaternion.Euler(tilt + 20f, 0f, 0f);
                Vector3 side = Quaternion.Euler(0f, az, 0f) * Vector3.right;
                Vector3 center = stemTop + (lr * Vector3.up) * (leafLen * 0.45f);
                Mat m = (i % 3 == 0) ? light : Mat.Foliage;
                for (int k = -1; k <= 1; k += 2)
                {
                    Quaternion hr = lr * Quaternion.Euler(0f, 0f, k * -8f);
                    B.DecorPiece(g, "Leaf", PropMeshes.Leaf, center + side * (k * 0.105f * s), hr,
                                 new Vector3(0.19f * s, leafLen, 0.03f * s), m, info, true);
                }
            }
        }

        static void Strelitzia(Transform g, B.Rng rng, float s, float baseY, Mat light)
        {
            int n = rng.Range(5, 9);
            float h = rng.Range(1.2f, 1.6f) * s;
            var info = Info(baseY, h, 0.7f);
            float start = rng.Range(0f, 360f);
            for (int i = 0; i < n; i++)
            {
                float az = start + i * (360f / n) + rng.Range(-10f, 10f);
                float tilt = rng.Range(8f, 24f);
                float len = h * rng.Range(0.7f, 1f);
                Quaternion r = Quaternion.Euler(0f, az, 0f) * Quaternion.Euler(tilt, 0f, 0f);
                Mat m = (i & 1) == 0 ? Mat.Foliage : (i % 3 == 0 ? light : Mat.FoliageDark);
                // A short petiole and the blade above it.
                B.DecorPiece(g, "Petiole", Geo.PrismMesh(4), r * Vector3.up * (len * 0.2f), r,
                             new Vector3(0.03f * s, len * 0.4f, 0.03f * s), Mat.FoliageDark, info, true, false);
                B.DecorPiece(g, "Blade", PropMeshes.Leaf, r * Vector3.up * (len * 0.68f), r,
                             new Vector3(0.2f * s, len * 0.65f, 0.03f * s), m, info, true);
            }
            // One bird-of-paradise flower: a Mustard crest over a TextileRed beak, held sideways.
            float fa = start + rng.Range(0f, 360f);
            Quaternion fr = Quaternion.Euler(0f, fa, 0f);
            Vector3 fp = fr * new Vector3(0f, h * 0.72f, 0.1f * s);
            B.DecorPiece(g, "Spathe", PropMeshes.Leaf, fp, fr * Quaternion.Euler(80f, 0f, 0f),
                         new Vector3(0.07f * s, 0.26f * s, 0.05f * s), Mat.TextileRed, info, true, false);
            B.DecorPiece(g, "Crest", Geo.ConeMesh(3), fp + Vector3.up * (0.08f * s), fr * Quaternion.Euler(-15f, 0f, 0f),
                         new Vector3(0.12f * s, 0.16f * s, 0.06f * s), Mat.Mustard, info, true, false);
        }

        static void Grass(Transform g, B.Rng rng, float s, float baseY, Mat light)
        {
            int n = rng.Range(6, 11);
            float hMax = 0.6f * s;
            var info = Info(baseY, hMax, 0.78f, 0.06f);
            for (int i = 0; i < n; i++)
            {
                float az = i * (360f / n) + rng.Range(-20f, 20f);
                float h = rng.Range(0.35f, 0.6f) * s;
                float tilt = rng.Range(10f, 30f);
                Quaternion r = Quaternion.Euler(0f, az, 0f) * Quaternion.Euler(tilt, 0f, 0f);
                Vector3 foot = Quaternion.Euler(0f, az, 0f) * new Vector3(0f, 0f, rng.Range(0.02f, 0.08f) * s);
                Mat m = (i % 3 == 0) ? Mat.Foliage : light;
                B.DecorPiece(g, "Blade", PropMeshes.Blade, foot + r * Vector3.up * (h * 0.5f) - Vector3.up * 0.01f, r,
                             new Vector3(0.07f * s, h, 0.03f * s), m, info, true, false);
            }
        }

        static void Ivy(Transform g, B.Rng rng, float s, float baseY, Mat light)
        {
            // Columns of 0.25 leaves hanging from the top edge, lengths seeded; leaves sit just off the wall face.
            int cols = Mathf.Max(2, Mathf.RoundToInt(4f * s));
            float maxLen = rng.Range(1.5f, 2.5f) * s;
            var info = Info(baseY - maxLen, maxLen, 0.8f, 0.06f);
            const float leaf = 0.25f;
            float width = cols * leaf * 0.8f;
            for (int c = 0; c < cols; c++)
            {
                float x = -width * 0.5f + (c + 0.5f) * (width / cols);
                float len = maxLen * rng.Range(0.45f, 1f);
                int count = Mathf.Max(1, Mathf.RoundToInt(len / (leaf * 0.7f)));
                for (int k = 0; k < count; k++)
                {
                    float y = -k * leaf * 0.7f - rng.Range(0f, 0.05f);
                    float jx = rng.Range(-0.05f, 0.05f);
                    Quaternion r = Quaternion.Euler(rng.Range(-20f, 5f), 0f, rng.Range(-35f, 35f));
                    Mat m = ((c + k) % 4 == 0) ? light : ((c + k) % 3 == 0 ? Mat.Foliage : Mat.FoliageDark);
                    float sz = leaf * rng.Range(0.8f, 1.05f) * Mathf.Clamp(s, 0.6f, 1.3f);
                    B.DecorPiece(g, "IvyLeaf", PropMeshes.Leaf, new Vector3(x + jx, y - sz * 0.4f, 0.03f), r,
                                 new Vector3(sz, sz, 0.03f), m, info, false, false);
                }
            }
        }

        static void Shrub(Transform g, B.Rng rng, float s, float baseY, Mat light)
        {
            float h = 0.95f * s;
            var info = Info(baseY, h, 0.68f);
            Vector3[] pos =
            {
                new Vector3(0f, 0.36f, 0f),
                new Vector3(rng.Range(0.18f, 0.28f), 0.26f, rng.Range(-0.12f, 0.12f)),
                new Vector3(rng.Range(-0.28f, -0.18f), 0.3f, rng.Range(-0.12f, 0.12f)),
            };
            Vector3[] size =
            {
                new Vector3(0.82f, 0.72f, 0.78f),
                new Vector3(0.56f, 0.5f, 0.55f),
                new Vector3(0.6f, 0.56f, 0.58f),
            };
            Mat[] mats = { Mat.Foliage, light, Mat.FoliageDark };
            for (int i = 0; i < 3; i++)
            {
                float k = rng.Range(0.9f, 1.1f);
                B.DecorPiece(g, "Puff", PropMeshes.Puff, pos[i] * s, Quaternion.Euler(0f, rng.Range(0f, 45f), 0f),
                             size[i] * (s * k), mats[i], info, true);
            }
        }
    }
}
