using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels.Props
{
    using Ion.Levels.Arch;
    using Arch = Ion.Levels.Arch.Arch;   // art bible §4: the name-lookup trap
    using B = PropBuild;

    /// <summary>
    /// The prop family of the "Light Table" art bible (§5): every stand, seat, table, planter, lamp, plant and
    /// device, built only from Arch primitives with the shared profiles (0.0625 chamfers, 0.0625 coping lips,
    /// brass clips and rivets at bracket joints).
    ///
    /// Frames: every prop is built in its own frame whose +Z is its front (<c>facing</c>, or <c>yaw</c> for soft
    /// clutter), +Y up, origin at <c>basePos</c>; all positions are local to <c>p</c>.
    /// - Sliceable props (Solid / Soft Arch pieces) are merged with their zone by Arch.Bake.
    /// - Soft clutter (chairs, lamps, plants) takes any float yaw and has no collider.
    /// - Interactable devices bake themselves (Arch.BakeLocal), carry <see cref="Ion.Projection.Interactable"/>
    ///   and are never sliced; photos capture or remove them whole by their pivot.
    /// </summary>
    public static partial class PropKit
    {
        public const float Chamfer = 0.0625f;

        /// <summary>
        /// Optional (addition): the root the caller will pass to Arch.Bake / DecorCombiner. Plant sway and contact
        /// shade heights are measured in its space. When null, PropKit uses the zone / diorama root (the ancestor
        /// one level under a top-level container), which matches GameBootstrap's hierarchy.
        /// </summary>
        public static Transform BakeRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => BakeRoot = null;

        static readonly Surf TrimSurf = new Surf(Mat.Limestone, Pat.Courses);
        static readonly Surf PaperForm = new Surf(Mat.Paper, Pat.Formwork);
        static readonly Surf ConcreteSurf = new Surf(Mat.Concrete, Pat.Courses);
        static readonly Surf WalnutBoards = new Surf(Mat.Walnut, Pat.Boards);
        static readonly Surf OakBoards = new Surf(Mat.Oak, Pat.Boards);

        // ====================================================================== pedestal

        /// <summary>Top height of a pedestal size: 0.5 / 1.0 / 1.25.</summary>
        public static float PedestalTop(PedestalSize size) =>
            size == PedestalSize.Low ? 0.5f : size == PedestalSize.Tall ? 1.25f : 1f;

        /// <summary>
        /// Bracket-pier pedestal: Limestone base plinth 0.75² × 0.125, Paper/Formwork shaft 0.5² (a bracket pier: back
        /// and two returns around a 0.0625 front reveal), a 0.0625 shadow gap, Limestone cap 0.625² × 0.0625 with
        /// brass "[ ]" corner clips on its front edge. Solid. The front is +Z of <paramref name="facing"/>.
        /// </summary>
        public static GameObject Pedestal(Transform p, Vector3 basePos, Dir facing, PedestalSize size = PedestalSize.Standard)
        {
            Transform g = B.Frame(p, "Pedestal", basePos, B.Yaw(facing));
            BuildPedestal(g, PedestalTop(size), B.Solid);
            return g.gameObject;
        }

        internal static void BuildPedestal(Transform g, float top, ArchFlags body)
        {
            ArchFlags detail = body == B.Device ? B.Device : B.Soft;
            B.Chamfer(g, -0.375f, 0f, -0.375f, 0.375f, 0.125f, 0.375f, TrimSurf, body);
            // Shaft 0.5² as a bracket pier: a full back and two returns around a 0.0625 front reveal ("[ ]" in plan).
            B.Box(g, -0.25f, 0.125f, -0.25f, 0.25f, top - 0.125f, 0.1875f, PaperForm, body);
            B.Box(g, -0.25f, 0.125f, 0.1875f, -0.125f, top - 0.125f, 0.25f, PaperForm, body);
            B.Box(g, 0.125f, 0.125f, 0.1875f, 0.25f, top - 0.125f, 0.25f, PaperForm, body);
            B.Box(g, -0.1875f, top - 0.125f, -0.1875f, 0.1875f, top - 0.0625f, 0.1875f, ConcreteSurf, body);   // shadow gap
            B.Chamfer(g, -0.3125f, top - 0.0625f, -0.3125f, 0.3125f, top, 0.3125f, TrimSurf, body, 0.015625f);
            CornerClips(g, 0.3125f, 0.3125f, top - 0.0625f, top, detail);
        }

        /// <summary>Brass "L" clips wrapping the two front corners of a w × d top (reads "[ ]" from the front).</summary>
        static void CornerClips(Transform g, float halfW, float halfD, float y0, float y1, ArchFlags f)
        {
            const float arm = 0.125f, t = 0.015625f;
            for (int s = -1; s <= 1; s += 2)
            {
                float xo = s * (halfW + t), xc = s * halfW, xi = s * (halfW - arm);
                B.Box(g, xi, y0, halfD, xo, y1, halfD + t, Mat.Brass, f);                // along the front
                B.Box(g, xc, y0, halfD - arm, xo, y1, halfD + t, Mat.Brass, f);          // along the side
            }
        }

        // ====================================================================== bench

        /// <summary>
        /// Bench, a bracket in section: two Concrete end blocks 0.375 × 0.4 × 0.4 (the returns), a Walnut/Boards seat
        /// 0.4 deep × 0.0625 at 0.45, brass rivets at the joints; optional back (0.0625 Walnut board on two Graphite
        /// posts). Length runs along local X.
        /// </summary>
        public static GameObject Bench(Transform p, Vector3 basePos, Dir facing, float length = 2f)
        {
            return Bench(p, basePos, facing, length, false);
        }

        /// <summary>Bench with an optional back (addition).</summary>
        public static GameObject Bench(Transform p, Vector3 basePos, Dir facing, float length, bool back)
        {
            Transform g = B.Frame(p, "Bench", basePos, B.Yaw(facing));
            float hl = Mathf.Max(0.75f, length) * 0.5f;
            const float hd = 0.2f, seat0 = 0.4f, seat1 = 0.4625f;
            float blockX = hl - 0.1875f;
            for (int s = -1; s <= 1; s += 2)
            {
                float xc = s * blockX;
                // A brass shoe under each Concrete end block (a shadow-gap foot), then the block.
                B.Box(g, xc - 0.171875f, 0f, -hd + 0.015625f, xc + 0.171875f, 0.0625f, hd - 0.015625f, Mat.Brass, B.Soft);
                B.Chamfer(g, xc - 0.1875f, 0.0625f, -hd, xc + 0.1875f, seat0 - 0.0625f, hd, ConcreteSurf, B.Solid);
                // A Graphite bearer under the slats.
                B.Box(g, xc - 0.1875f, seat0 - 0.0625f, -hd - 0.03125f, xc + 0.1875f, seat0, hd + 0.03125f, Mat.Graphite, B.Solid);
                B.Rivet(g, new Vector3(xc - s * 0.0625f, seat0 - 0.03125f, hd + 0.046875f), B.Soft);
                B.Rivet(g, new Vector3(xc + s * 0.0625f, seat0 - 0.03125f, hd + 0.046875f), B.Soft);
            }
            // Three Walnut slats with two 1/16 gaps (reads as a bench, not a plank on two blocks).
            float slat = (2f * hd + 0.0625f - 2f * 0.0625f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float z0 = -hd - 0.03125f + i * (slat + 0.0625f);
                B.Chamfer(g, -hl, seat0, z0, hl, seat1, z0 + slat, WalnutBoards, B.Solid, 0.015625f);
            }
            if (back)
            {
                for (int s = -1; s <= 1; s += 2)
                    B.Box(g, s * blockX - 0.03125f, seat1, -hd, s * blockX + 0.03125f, 0.9375f, -hd + 0.0625f, Mat.Graphite, B.Soft);
                B.Chamfer(g, -hl + 0.0625f, 0.625f, -hd + 0.0625f, hl - 0.0625f, 0.75f, -hd + 0.125f, WalnutBoards, B.Soft, 0.015625f);
                B.Chamfer(g, -hl + 0.0625f, 0.8125f, -hd + 0.0625f, hl - 0.0625f, 0.9375f, -hd + 0.125f, WalnutBoards, B.Soft, 0.015625f);
            }
            B.Exempt(g);   // slats sit on the 1/32 step
            return g.gameObject;
        }

        // ====================================================================== table

        /// <summary>Walnut/Boards top 0.0625 thick on two Graphite U-frames (3 boxes each, 0.0625 sections). Top default 1.0 × 0.75.</summary>
        public static GameObject Table(Transform p, Vector3 basePos, Dir facing, Vector2 top = default, float height = 0.75f)
        {
            if (top.x <= 0f || top.y <= 0f) top = new Vector2(1f, 0.75f);
            Transform g = B.Frame(p, "Table", basePos, B.Yaw(facing));
            float hw = top.x * 0.5f, hd = top.y * 0.5f;
            const float t = 0.0625f;
            B.Chamfer(g, -hw, height - t, -hd, hw, height, hd, WalnutBoards, B.Solid, 0.015625f);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (hw - 0.125f);
                float z = hd - 0.0625f;
                B.Box(g, x - t * 0.5f, 0f, -z - t * 0.5f, x + t * 0.5f, height - t, -z + t * 0.5f, Mat.Graphite, B.Soft);
                B.Box(g, x - t * 0.5f, 0f, z - t * 0.5f, x + t * 0.5f, height - t, z + t * 0.5f, Mat.Graphite, B.Soft);
                B.Box(g, x - t * 0.5f, height - 2f * t, -z - t * 0.5f, x + t * 0.5f, height - t, z + t * 0.5f, Mat.Graphite, B.Soft);
            }
            return g.gameObject;
        }

        // ====================================================================== planter

        /// <summary>
        /// Planter, always filled with at least three seeded plants. Trough (default 1.5 × 0.5): Concrete, walls
        /// 0.125, a 0.0625 coping lip overhanging 0.0625, Lawn soil inset 0.125, a 0.0625 shadow-gap foot.
        /// Square: the same at 0.75². Bowl: a 12-gon Terracotta prism 0.8 Ø × 0.5. Solid.
        /// </summary>
        public static GameObject Planter(Transform p, Vector3 basePos, Dir facing, PlanterStyle style = PlanterStyle.Trough,
                                         Vector2 size = default, float height = 0.625f, int seed = 0)
        {
            Transform g = B.Frame(p, "Planter " + style, basePos, B.Yaw(facing));
            var rng = new B.Rng(seed * 131 + (int)style * 17 + 3);
            if (style == PlanterStyle.Bowl)
            {
                float d = size.x > 0f ? size.x : 0.8f;
                float h = height > 0f && height < 0.6f ? height : 0.5f;
                float r = d * 0.5f;
                B.Prism(g, Vector3.zero, r - 0.0625f, 0.0625f, 12, Mat.Terracotta, B.Solid);                        // foot (shadow gap)
                B.Prism(g, new Vector3(0f, 0.0625f, 0f), r, h - 0.0625f, 12, new Surf(Mat.Terracotta, Pat.TileSmall), B.Solid);
                B.Prism(g, new Vector3(0f, h, 0f), r - 0.0625f, 0.015625f, 12, new Surf(Mat.Lawn, Pat.Lawn), B.Soft);
                float soil = h + 0.015625f;
                FillPlanter(g, rng, style, new Vector2(r - 0.125f, r - 0.125f), soil);
                return g.gameObject;
            }

            Vector2 s = size;
            if (s.x <= 0f || s.y <= 0f) s = style == PlanterStyle.Square ? new Vector2(0.75f, 0.75f) : new Vector2(1.5f, 0.5f);
            float hx = s.x * 0.5f, hz = s.y * 0.5f;
            const float wall = 0.125f, lip = 0.0625f, gap = 0.0625f;
            float top = Mathf.Max(0.375f, height);
            float soilY = top - 0.125f;
            // foot (inset shadow gap), body up to the soil, Lawn soil cover
            B.Box(g, -hx + gap, 0f, -hz + gap, hx - gap, gap, hz - gap, ConcreteSurf, B.Solid);
            B.Box(g, -hx, gap, -hz, hx, soilY - 0.0625f, hz, ConcreteSurf, B.Solid);
            B.Box(g, -hx + wall, soilY - 0.0625f, -hz + wall, hx - wall, soilY, hz - wall, new Surf(Mat.Lawn, Pat.Lawn), B.Solid);
            // walls up to the coping, then the coping lip overhanging 0.0625 outside
            B.Box(g, -hx, soilY - 0.0625f, -hz, hx, top - lip, -hz + wall, ConcreteSurf, B.Solid);
            B.Box(g, -hx, soilY - 0.0625f, hz - wall, hx, top - lip, hz, ConcreteSurf, B.Solid);
            B.Box(g, -hx, soilY - 0.0625f, -hz + wall, -hx + wall, top - lip, hz - wall, ConcreteSurf, B.Solid);
            B.Box(g, hx - wall, soilY - 0.0625f, -hz + wall, hx, top - lip, hz - wall, ConcreteSurf, B.Solid);
            B.Chamfer(g, -hx - lip, top - lip, -hz - lip, hx + lip, top, -hz + wall, TrimSurf, B.Solid, 0.015625f);
            B.Chamfer(g, -hx - lip, top - lip, hz - wall, hx + lip, top, hz + lip, TrimSurf, B.Solid, 0.015625f);
            B.Chamfer(g, -hx - lip, top - lip, -hz + wall, -hx + wall, top, hz - wall, TrimSurf, B.Solid, 0.015625f);
            B.Chamfer(g, hx - wall, top - lip, -hz + wall, hx + lip, top, hz - wall, TrimSurf, B.Solid, 0.015625f);
            FillPlanter(g, rng, style, new Vector2(hx - wall - 0.0625f, hz - wall - 0.0625f), soilY);
            return g.gameObject;
        }

        static void FillPlanter(Transform g, B.Rng rng, PlanterStyle style, Vector2 half, float soilY)
        {
            int seed = rng.Range(0, 100000);
            if (style == PlanterStyle.Trough)
            {
                int n = Mathf.Max(3, Mathf.RoundToInt(half.x * 2f / 0.42f));
                for (int i = 0; i < n; i++)
                {
                    float x = Mathf.Lerp(-half.x, half.x, (i + 0.5f) / n) + rng.Range(-0.06f, 0.06f);
                    float z = rng.Range(-half.y, half.y) * 0.5f;
                    float pick = rng.Value;
                    PlantKind k = pick < 0.35f ? PlantKind.Grass : pick < 0.6f ? PlantKind.Shrub : pick < 0.8f ? PlantKind.Strelitzia : PlantKind.Monstera;
                    float sc = k == PlantKind.Grass ? rng.Range(0.8f, 1.1f) : rng.Range(0.55f, 0.75f);
                    Plant(g, new Vector3(x, soilY, z), k, sc, rng.Range(0f, 360f), seed + i * 7);
                }
                UltraUnderplanting(g, rng, half, soilY, seed);
                return;
            }
            // Square and Bowl: one hero plant and grass at its feet.
            float hero = rng.Value;
            PlantKind main = style == PlanterStyle.Bowl
                ? (hero < 0.5f ? PlantKind.Monstera : PlantKind.Shrub)
                : (hero < 0.3f ? PlantKind.Palm : hero < 0.55f ? PlantKind.Cypress : hero < 0.8f ? PlantKind.Strelitzia : PlantKind.Monstera);
            float mainScale = main == PlantKind.Palm ? 0.7f : main == PlantKind.Cypress ? 0.75f : 0.85f;
            Plant(g, new Vector3(0f, soilY, 0f), main, mainScale, rng.Range(0f, 360f), seed);
            for (int i = 0; i < 2; i++)
            {
                float a = (i * 180f + rng.Range(-40f, 40f) + 45f) * Mathf.Deg2Rad;
                Plant(g, new Vector3(Mathf.Cos(a) * half.x * 0.7f, soilY, Mathf.Sin(a) * half.y * 0.7f), PlantKind.Grass,
                      rng.Range(0.6f, 0.85f), rng.Range(0f, 360f), seed + 11 + i);
            }
            UltraUnderplanting(g, rng, half, soilY, seed);
        }

        /// <summary>Ultra detail: denser planting, three more grass / shrub tufts round the edge of the soil.</summary>
        static void UltraUnderplanting(Transform g, B.Rng rng, Vector2 half, float soilY, int seed)
        {
            using (Arch.Ultra())
            {
                for (int i = 0; i < 3; i++)
                {
                    float a = (i * 120f + 75f + rng.Range(-25f, 25f)) * Mathf.Deg2Rad;
                    PlantKind k = i == 1 ? PlantKind.Shrub : PlantKind.Grass;
                    float sc = k == PlantKind.Shrub ? rng.Range(0.35f, 0.45f) : rng.Range(0.55f, 0.8f);
                    Plant(g, new Vector3(Mathf.Cos(a) * half.x * 0.82f, soilY, Mathf.Sin(a) * half.y * 0.82f), k, sc,
                          rng.Range(0f, 360f), seed + 101 + i * 13);
                }
            }
        }

        // ====================================================================== easel

        /// <summary>
        /// Oak/Boards A-frame easel: two splayed front legs and a back leg (0.0625² sticks), a 0.0625 ledge at 0.9,
        /// holding a print 0.8 × 0.6 (an Ion/PhotoDisplay print card under its own Interactable; null image = Frost
        /// blank). Soft (sliceable, no collider). Returns the easel; the print is <see cref="EaselPrint"/> of it.
        /// </summary>
        public static GameObject Easel(Transform p, Vector3 basePos, Dir facing, Texture2D image = null)
        {
            Transform g = BuildEasel(p, basePos, facing, out Vector3 printBottom, out Quaternion lean);
            Vector2 outer = new Vector2(0.8f, 0.6f);
            const float border = 0.04f;
            Vector2 img = new Vector2(outer.x - 2f * border, outer.y - 2f * border);
            Vector3 center = printBottom + lean * new Vector3(0f, outer.y * 0.5f, PrintBuilder.MountThickness * 0.5f);
            PrintCard card = PrintBuilder.Build(p, "Easel Print", g.localPosition + g.localRotation * center, g.localRotation * lean, img,
                                                border, false, true, true);
            card.Image = image;
            var link = g.gameObject.AddComponent<PropLink>();
            link.Print = card;
            return g.gameObject;
        }

        /// <summary>The print card standing on an easel made by <see cref="Easel"/> (addition).</summary>
        public static PrintCard EaselPrint(GameObject easel) =>
            easel != null && easel.TryGetComponent(out PropLink l) ? l.Print : null;

        const float EaselLeanDeg = 10f, EaselLedgeY = 0.9f;

        /// <summary>Builds the easel's sticks; returns the frame plus the print's bottom-centre (frame-local) and lean.</summary>
        internal static Transform BuildEasel(Transform p, Vector3 basePos, Dir facing, out Vector3 printBottom, out Quaternion lean)
        {
            Transform g = B.Frame(p, "Easel", basePos, B.Yaw(facing));
            const float s = 0.0625f, top = 1.75f;
            float tan = Mathf.Tan(EaselLeanDeg * Mathf.Deg2Rad);
            // Front legs: splayed (A) and leaning back with the print.
            Vector3 topPt = new Vector3(0f, top, -top * tan * 0.5f);
            B.Beam(g, new Vector3(0.375f, 0f, top * tan * 0.5f), topPt + new Vector3(0.0625f, 0f, 0f), s, OakBoards, B.Soft);
            B.Beam(g, new Vector3(-0.375f, 0f, top * tan * 0.5f), topPt - new Vector3(0.0625f, 0f, 0f), s, OakBoards, B.Soft);
            // Back leg.
            B.Beam(g, new Vector3(0f, 0f, -0.625f), topPt + new Vector3(0f, -0.125f, -0.03125f), s, OakBoards, B.Soft);
            // Ledge across the front legs at 0.9 and a top clamp.
            float zl = top * tan * 0.5f - EaselLedgeY * tan;
            B.Box(g, -0.4375f, EaselLedgeY - 0.0625f, zl - 0.03125f, 0.4375f, EaselLedgeY, zl + 0.125f, OakBoards, B.Soft);
            B.Box(g, -0.4375f, EaselLedgeY, zl + 0.09375f, 0.4375f, EaselLedgeY + 0.03125f, zl + 0.125f, OakBoards, B.Soft);   // lip
            B.Box(g, -0.09375f, top - 0.1875f, topPt.z - 0.03125f, 0.09375f, top - 0.0625f, topPt.z + 0.0625f, OakBoards, B.Soft);
            B.Rivet(g, new Vector3(0f, top - 0.125f, topPt.z + 0.078125f), B.Soft);
            lean = Quaternion.Euler(-EaselLeanDeg, 0f, 0f);
            printBottom = new Vector3(0f, EaselLedgeY, zl + 0.03125f);
            return g;
        }

        // ====================================================================== rug

        /// <summary>Kilim rug 0.01 high: 3–5 TextileRed / Mustard bands with Paper fringe ends, along its long side. Soft.</summary>
        public static GameObject Rug(Transform p, RectXZ r, int seed = 0)
        {
            Transform g = B.Frame(p, "Rug", Vector3.zero, 0f);
            bool alongX = r.Width >= r.Depth;
            float len = alongX ? r.Width : r.Depth;
            int bands = 3 + Mathf.Abs(seed) % 3;
            // Relative widths: fringe | red | (mustard | red)* | fringe, the middle red is widest.
            const float fringe = 0.0625f;
            float inner = len - 2f * fringe;
            var widths = new System.Collections.Generic.List<float>();
            var mats = new System.Collections.Generic.List<Mat>();
            if (bands == 3) { widths.AddRange(new[] { 0.25f, 0.5f, 0.25f }); mats.AddRange(new[] { Mat.TextileRed, Mat.Mustard, Mat.TextileRed }); }
            else if (bands == 4) { widths.AddRange(new[] { 0.2f, 0.1f, 0.5f, 0.2f }); mats.AddRange(new[] { Mat.TextileRed, Mat.Mustard, Mat.TextileRed, Mat.Mustard }); }
            else { widths.AddRange(new[] { 0.22f, 0.08f, 0.4f, 0.08f, 0.22f }); mats.AddRange(new[] { Mat.TextileRed, Mat.Mustard, Mat.TextileRed, Mat.Mustard, Mat.TextileRed }); }
            float a = (alongX ? r.X0 : r.Z0);
            Band(g, r, alongX, a, a + fringe, Mat.Paper);
            a += fringe;
            for (int i = 0; i < widths.Count; i++)
            {
                float w = widths[i] * inner;
                Band(g, r, alongX, a, a + w, mats[i]);
                a += w;
            }
            Band(g, r, alongX, a, a + fringe, Mat.Paper);
            B.Exempt(g);
            return g.gameObject;
        }

        static void Band(Transform g, RectXZ r, bool alongX, float a0, float a1, Mat m)
        {
            if (alongX) B.Box(g, a0, 0f, r.Z0, a1, 0.01f, r.Z1, m, B.Soft);
            else B.Box(g, r.X0, 0f, a0, r.X1, 0.01f, a1, m, B.Soft);
        }

        // ====================================================================== chair (soft clutter)

        /// <summary>
        /// Chair (soft clutter, any yaw). Café: Teal seat 0.45² at 0.45, Graphite legs 0.04², back 0.4 × 0.35.
        /// Monobloc: chunkier, one colour, raked back and arms. Stool: 0.35² seat at 0.65 on Graphite legs with a foot ring.
        /// </summary>
        public static GameObject Chair(Transform p, Vector3 basePos, float yaw, ChairStyle style = ChairStyle.Cafe, Mat color = Mat.Teal)
        {
            Transform g = B.Frame(p, "Chair " + style, basePos, yaw);
            const ArchFlags f = ArchFlags.Soft;
            switch (style)
            {
                case ChairStyle.Monobloc:
                {
                    const float hs = 0.24f, leg = 0.07f;
                    B.Chamfer(g, -hs, 0.37f, -hs, hs, 0.45f, hs, color, f, 0.02f);
                    for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        B.Box(g, sx * (hs - 0.01f) - leg * 0.5f, 0f, sz * (hs - 0.01f) - leg * 0.5f,
                                 sx * (hs - 0.01f) + leg * 0.5f, 0.37f, sz * (hs - 0.01f) + leg * 0.5f, color, f);
                    Transform back = B.Frame(g, "Back", new Vector3(0f, 0.45f, -hs), Quaternion.Euler(-8f, 0f, 0f));
                    B.Chamfer(back, -0.22f, 0.04f, -0.06f, 0.22f, 0.44f, 0f, color, f, 0.02f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        B.Box(g, s * hs - 0.03f, 0.62f, -hs, s * hs + 0.03f, 0.68f, hs - 0.04f, color, f);   // arm
                        B.Box(g, s * hs - 0.03f, 0.45f, hs - 0.1f, s * hs + 0.03f, 0.62f, hs - 0.04f, color, f); // arm post
                    }
                    break;
                }
                case ChairStyle.Stool:
                {
                    const float hs = 0.175f, leg = 0.04f, seat = 0.65f;
                    B.Chamfer(g, -hs, seat - 0.0625f, -hs, hs, seat, hs, color, f, 0.015625f);
                    float lx = hs - 0.035f;
                    for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        B.Box(g, sx * lx - leg * 0.5f, 0f, sz * lx - leg * 0.5f, sx * lx + leg * 0.5f, seat - 0.0625f, sz * lx + leg * 0.5f, Mat.Graphite, f);
                    const float ry0 = 0.24f, ry1 = 0.27f;
                    B.Box(g, -lx, ry0, -lx - 0.015f, lx, ry1, -lx + 0.015f, Mat.Graphite, f);
                    B.Box(g, -lx, ry0, lx - 0.015f, lx, ry1, lx + 0.015f, Mat.Graphite, f);
                    B.Box(g, -lx - 0.015f, ry0, -lx, -lx + 0.015f, ry1, lx, Mat.Graphite, f);
                    B.Box(g, lx - 0.015f, ry0, -lx, lx + 0.015f, ry1, lx, Mat.Graphite, f);
                    break;
                }
                default:
                {
                    const float hs = 0.225f, leg = 0.04f, seat = 0.45f;
                    B.Chamfer(g, -hs, seat - 0.0625f, -hs, hs, seat, hs, color, f, 0.015625f);
                    float lx = hs - 0.03f;
                    for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        float top = sz < 0 ? 0.9f : seat - 0.0625f;   // back legs run up as the back posts
                        B.Box(g, sx * lx - leg * 0.5f, 0f, sz * lx - leg * 0.5f, sx * lx + leg * 0.5f, top, sz * lx + leg * 0.5f, Mat.Graphite, f);
                    }
                    B.Chamfer(g, -0.2f, 0.55f, -lx - 0.05f, 0.2f, 0.9f, -lx - 0.01f, color, f, 0.015625f);
                    B.Box(g, -lx, 0.18f, -lx - 0.01f, lx, 0.21f, -lx + 0.01f, Mat.Graphite, f);   // stretcher
                    break;
                }
            }
            B.Exempt(g);   // soft clutter: any yaw, any position
            return g.gameObject;
        }

        // ====================================================================== lamp (soft clutter)

        /// <summary>
        /// Lamp (soft clutter, any yaw). Pendant: <paramref name="pos"/> is the ceiling anchor (Graphite rose, 0.02
        /// cord, 6-gon shade and a Warm 0.12 bulb, 1.0 below). Standing: a 1.6 Graphite pole and a 0.25 Paper shade.
        /// Sconce: <paramref name="pos"/> is on the wall face, +Z (yaw) out of the wall: a bracket-shaped Graphite
        /// mount around a Warm box. Lantern: a 0.25 Graphite cage around a Warm core. Bollard: a 0.25² × 0.75
        /// Concrete post with a Warm slot.
        /// </summary>
        public static GameObject Lamp(Transform p, Vector3 pos, float yaw, LampStyle style)
        {
            return Lamp(p, pos, yaw, style, 1f, Mat.Warm);
        }

        /// <summary>Lamp with a pendant drop and glow role (Warm, or Safelight in the T2 darkroom). Addition.</summary>
        public static GameObject Lamp(Transform p, Vector3 pos, float yaw, LampStyle style, float drop, Mat glow)
        {
            Transform g = B.Frame(p, "Lamp " + style, pos, yaw);
            const ArchFlags f = ArchFlags.Soft;
            switch (style)
            {
                case LampStyle.Pendant:
                {
                    float d = Mathf.Max(0.3f, drop);
                    B.Box(g, -0.0625f, -0.03125f, -0.0625f, 0.0625f, 0f, 0.0625f, Mat.Graphite, f);
                    B.Box(g, -0.01f, -d + 0.125f, -0.01f, 0.01f, -0.03125f, 0.01f, Mat.Graphite, f);
                    B.Prism(g, new Vector3(0f, -d + 0.0625f, 0f), 0.125f, 0.0625f, 6, Mat.Graphite, f);
                    B.Prism(g, new Vector3(0f, -d + 0.125f, 0f), 0.0625f, 0.03125f, 6, Mat.Graphite, f);
                    B.Prism(g, new Vector3(0f, -d - 0.0625f, 0f), 0.06f, 0.125f, 6, glow, f);
                    break;
                }
                case LampStyle.Standing:
                {
                    // A weighted Brass foot, a slim Graphite pole and a small 6-gon Paper drum (0.25 Ø) whose warm core
                    // glows out of its open bottom: a reading lamp, not a sign on a post.
                    B.Prism(g, Vector3.zero, 0.15f, 0.04f, 8, Mat.Brass, f);
                    B.Box(g, -0.015625f, 0.04f, -0.015625f, 0.015625f, 1.47f, 0.015625f, Mat.Graphite, f);
                    B.Prism(g, new Vector3(0f, 1.40f, 0f), 0.085f, 0.09f, 6, glow, f);
                    B.Prism(g, new Vector3(0f, 1.44f, 0f), 0.125f, 0.22f, 6, Mat.Paper, f);
                    B.Prism(g, new Vector3(0f, 1.66f, 0f), 0.05f, 0.02f, 6, Mat.Brass, f);
                    break;
                }
                case LampStyle.Sconce:
                {
                    B.Box(g, -0.09375f, -0.1875f, 0f, 0.09375f, 0.1875f, 0.03125f, Mat.Graphite, f);                 // back plate
                    B.Box(g, -0.09375f, 0.125f, 0.03125f, 0.09375f, 0.1875f, 0.21875f, Mat.Graphite, f);             // top arm
                    B.Box(g, -0.09375f, -0.1875f, 0.03125f, 0.09375f, -0.125f, 0.21875f, Mat.Graphite, f);           // bottom arm
                    B.Box(g, -0.0625f, -0.125f, 0.0625f, 0.0625f, 0.125f, 0.1875f, glow, f);
                    B.Rivet(g, new Vector3(0f, 0.15625f, 0.234375f), f);
                    B.Rivet(g, new Vector3(0f, -0.15625f, 0.234375f), f);
                    break;
                }
                case LampStyle.Lantern:
                {
                    const float h = 0.125f, post = 0.03125f;
                    B.Box(g, -h, 0f, -h, h, 0.03125f, h, Mat.Graphite, f);
                    B.Chamfer(g, -h - 0.015625f, 0.3125f, -h - 0.015625f, h + 0.015625f, 0.375f, h + 0.015625f, Mat.Graphite, f, 0.015625f);
                    for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        B.Box(g, sx * h - (sx > 0 ? post : 0f), 0.03125f, sz * h - (sz > 0 ? post : 0f),
                                 sx * h + (sx < 0 ? post : 0f), 0.3125f, sz * h + (sz < 0 ? post : 0f), Mat.Graphite, f);
                    B.Box(g, -0.078125f, 0.0625f, -0.078125f, 0.078125f, 0.28125f, 0.078125f, glow, f);
                    B.Box(g, -0.015625f, 0.375f, -0.0625f, 0.015625f, 0.4375f, 0.0625f, Mat.Graphite, f);   // handle
                    break;
                }
                default: // Bollard
                {
                    // A slim path light: a 0.1875² Concrete post on a brass shoe, a warm band and a Graphite hood that
                    // overhangs it (light falls down onto the path; nothing reads as a bin).
                    const float h = 0.09375f, post = 0.03125f;
                    B.Box(g, -h - 0.015625f, 0f, -h - 0.015625f, h + 0.015625f, 0.0625f, h + 0.015625f, Mat.Brass, f);
                    B.Chamfer(g, -h, 0.0625f, -h, h, 0.75f, h, ConcreteSurf, f, 0.015625f);
                    B.Box(g, -0.0625f, 0.75f, -0.0625f, 0.0625f, 0.84375f, 0.0625f, glow, f);
                    for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        B.Box(g, sx * h - (sx > 0 ? post : 0f), 0.75f, sz * h - (sz > 0 ? post : 0f),
                                 sx * h + (sx < 0 ? post : 0f), 0.84375f, sz * h + (sz < 0 ? post : 0f), Mat.Graphite, f);
                    B.Chamfer(g, -h - 0.03125f, 0.84375f, -h - 0.03125f, h + 0.03125f, 0.90625f, h + 0.03125f, Mat.Graphite, f, 0.015625f);
                    break;
                }
            }
            B.Exempt(g);   // soft clutter: any yaw, any position
            LampLight(g, style, drop, glow);
            return g.gameObject;
        }

        /// <summary>The Ultra tier's local light at a lamp's glowing core (disabled elsewhere; see UltraFx).</summary>
        static void LampLight(Transform g, LampStyle style, float drop, Mat glow)
        {
            Vector3 at;
            float range;
            switch (style)
            {
                case LampStyle.Pendant: at = new Vector3(0f, -Mathf.Max(0.3f, drop) - 0.1f, 0f); range = 5.5f; break;
                case LampStyle.Standing: at = new Vector3(0f, 1.38f, 0f); range = 4.5f; break;
                case LampStyle.Sconce: at = new Vector3(0f, 0f, 0.3f); range = 4.5f; break;
                case LampStyle.Lantern: at = new Vector3(0f, 0.2f, 0f); range = 3.5f; break;
                default: at = new Vector3(0f, 0.8f, 0f); range = 3f; break;
            }
            Ion.Presentation.Quality.LocalLights.Add(g, at, LightColor(glow), range, glow == Mat.Safelight ? 2.2f : 1.6f);
        }

        /// <summary>Light colour of a glow role (sRGB).</summary>
        internal static Color LightColor(Mat glow)
        {
            switch (glow)
            {
                case Mat.Safelight: return new Color(1f, 0.52f, 0.22f);
                case Mat.Ion: return new Color(0.62f, 0.89f, 1f);
                default: return new Color(1f, 0.8f, 0.56f);
            }
        }
    }

    /// <summary>Links a sliceable prop group to its non-sliceable parts (e.g. an easel's print card).</summary>
    [DisallowMultipleComponent]
    public sealed class PropLink : MonoBehaviour
    {
        public PrintCard Print;
    }
}
