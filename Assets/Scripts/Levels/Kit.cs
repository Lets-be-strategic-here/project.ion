using Ion.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Levels
{
    /// <summary>
    /// Composite low-poly pieces (floating islands, ledges, trees, rocks, tufts, flowers, clouds) built from
    /// <see cref="Geo"/>. All positions are local to <c>parent</c>. Deterministic for a given seed, so a room's
    /// terrain can be built twice (world + photo diorama) and come out identical.
    /// Pieces tagged with <see cref="Decor"/> are merged per room by <see cref="DecorCombiner"/>.
    ///
    /// DEPRECATED (art bible §10): legacy island terrain for the pre-redesign rooms. Trees and tufts are now
    /// <see cref="Props.PropKit.Plant(Transform, Vector3, Props.PlantKind, float, float, int)"/>; new zones use the
    /// Arch kit and PropKit only.
    /// </summary>
    [System.Obsolete("Legacy island kit: build zones with Ion.Levels.Arch.Arch and Ion.Levels.Props.PropKit.")]
    public static class Kit
    {
        const float GrassDepth = 0.5f;

        /// <summary>
        /// A floating island whose walkable top is the rectangle [x0,x1]×[z0,z1] at height <paramref name="top"/>:
        /// grass slab, dirt body, a rocky core and downward spikes. The overhanging grass lip, the darker
        /// dirt band and the spikes are collider-less decor.
        /// </summary>
        public static void Island(Transform p, float x0, float x1, float z0, float z1, float top = 0f, int seed = 0, float depth = 3.5f)
        {
            float w = x1 - x0, d = z1 - z0;
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            Geo.Box(p, new Vector3(cx, top - GrassDepth * 0.5f, cz), new Vector3(w, GrassDepth, d), LevelColors.Grass).name = "Grass";
            float dirtH = depth - GrassDepth;
            Geo.Box(p, new Vector3(cx, top - GrassDepth - dirtH * 0.5f, cz), new Vector3(w, dirtH, d), LevelColors.Dirt).name = "Dirt";
            Skirt(p, cx, cz, w, d, top, top - depth);

            // Rocky core, inset.
            float coreW = Mathf.Max(1f, w - 2.4f), coreD = Mathf.Max(1f, d - 2.4f);
            float coreTop = top - depth;
            Geo.Box(p, new Vector3(cx, coreTop - 1.25f, cz), new Vector3(coreW, 2.5f, coreD), LevelColors.Rock).name = "Core";

            // Underside: one broad inverted rock cone plus a few smaller spikes. No colliders, so a
            // falling player drops past them to the respawn height.
            var rng = new System.Random(seed * 31 + 7);
            float spikeTop = coreTop - 2.5f;
            float mainR = Mathf.Min(coreW, coreD) * 1.05f;
            float mainH = 4f + Mathf.Min(w, d) * 0.45f;
            DecorSpike(p, new Vector3(cx, spikeTop - mainH * 0.5f + 0.4f, cz), new Vector3(mainR, mainH, mainR * 0.9f),
                       LevelColors.Rock, 7, (float)rng.NextDouble() * 360f);
            int count = Mathf.Clamp(Mathf.RoundToInt(w * d / 70f), 2, 9);
            for (int i = 0; i < count; i++)
            {
                float sx = Mathf.Lerp(x0 + 2f, x1 - 2f, (float)rng.NextDouble());
                float sz = Mathf.Lerp(z0 + 2f, z1 - 2f, (float)rng.NextDouble());
                float r = Mathf.Lerp(2.5f, 5f, (float)rng.NextDouble());
                r = Mathf.Min(r, Mathf.Min(coreW, coreD));
                float h = Mathf.Lerp(3f, 8f, (float)rng.NextDouble());
                DecorSpike(p, new Vector3(sx, spikeTop - h * 0.5f + 0.3f, sz), new Vector3(r, h, r),
                           i % 2 == 0 ? LevelColors.RockDark : LevelColors.Rock, 5, (float)rng.NextDouble() * 360f);
            }
        }

        static void DecorSpike(Transform p, Vector3 center, Vector3 size, Color c, int sides, float yRot)
        {
            var go = Geo.Soft("Spike", p, Geo.ConeMesh(sides), center, Quaternion.Euler(180f, yRot, 0f), size, c);
            // Darker toward the tip (the lowest point).
            Tag(go, center.y - size.y * 0.5f, size.y, 0.7f, 0.03f);
        }

        /// <summary>
        /// Collider-less dressing around a grass/dirt block: a grass lip hanging slightly over the dirt
        /// and a darker dirt band lower down (reads as strata).
        /// </summary>
        static void Skirt(Transform p, float cx, float cz, float w, float d, float top, float bottom)
        {
            const float lip = 0.12f, lipH = 0.34f;
            var g = Geo.Soft("GrassLip", p, Geo.CubeMesh, new Vector3(cx, top - 0.03f - lipH * 0.5f, cz), Quaternion.identity,
                             new Vector3(w + lip * 2f, lipH, d + lip * 2f), LevelColors.GrassLip);
            Tag(g, top - 0.03f - lipH, lipH, 0.88f, 0f);
            // Strata: darker bands every ~1.7 m down the face, alternating thickness.
            int k = 0;
            for (float bandTop = top - 1.15f; ; bandTop -= 1.7f, k++)
            {
                float bandH = k % 2 == 0 ? 0.34f : 0.22f;
                if (bandTop - bandH < bottom + 0.2f) break;
                var b = Geo.Soft("DirtBand", p, Geo.CubeMesh, new Vector3(cx, bandTop - bandH * 0.5f, cz), Quaternion.identity,
                                 new Vector3(w + 0.06f, bandH, d + 0.06f), LevelColors.DirtBand);
                Tag(b, bandTop - bandH, bandH, 1f, 0f);
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
            Skirt(p, cx, cz, w, d, top, bottom);
        }

        /// <summary>Tree (migrated): a PropKit cypress; <paramref name="dark"/> keeps the dark foliage, otherwise a lighter one.</summary>
        public static void Tree(Transform p, Vector3 basePos, float scale = 1f, float yRot = 0f, bool dark = false)
        {
            Props.PropKit.Plant(p, basePos, Props.PlantKind.Cypress, scale, yRot, Props.PropBuild.SeedOf(basePos, 3),
                                dark ? (Mat?)null : Mat.Foliage);
        }

        /// <summary>Rock: a faceted boulder plus a small pebble, on a soft contact shadow.</summary>
        public static void Rock(Transform p, Vector3 basePos, float scale = 1f, float yRot = 0f)
        {
            float s = scale;
            int variant = Mathf.Abs(Mathf.RoundToInt(yRot)) % 4;
            Quaternion r = Quaternion.Euler(0f, yRot, 0f);
            var size = new Vector3(1.5f * s, 0.95f * s, 1.2f * s);
            var rock = Geo.Solid("Rock", p, Geo.RockMesh(variant), basePos + new Vector3(0f, size.y * 0.5f - 0.04f * s, 0f), r, size, LevelColors.Boulder);
            Tag(rock, basePos.y, size.y, 0.68f, 0.05f);
            var pebbleSize = new Vector3(0.6f * s, 0.38f * s, 0.5f * s);
            Vector3 off = r * new Vector3(0.85f * s, 0f, -0.45f * s);
            var pebble = Geo.Solid("Pebble", p, Geo.RockMesh(variant + 1), basePos + off + new Vector3(0f, pebbleSize.y * 0.5f - 0.03f * s, 0f),
                                   Quaternion.Euler(0f, yRot + 70f, 0f), pebbleSize, LevelColors.BoulderDark);
            Tag(pebble, basePos.y, pebbleSize.y, 0.7f, 0.05f);
            Contact(p, basePos + off * 0.35f, 1.0f * s);
        }

        /// <summary>
        /// Contact shading under a prop: a grass-coloured disc darkening toward its centre (radial
        /// gradient in the disc mesh's vertex colours, so its rim is invisible). Works with shadows off,
        /// and deepens on the Low tier (Palette.GetContact).
        /// </summary>
        public static void Contact(Transform p, Vector3 basePos, float radius)
        {
            Contact(p, basePos, radius * 2f, radius * 2f, 0f, LevelColors.Contact);
        }

        /// <summary>
        /// Elliptical contact shade (<paramref name="sizeX"/> x <paramref name="sizeZ"/> metres, turned by
        /// <paramref name="yRot"/>) on a floor of colour <paramref name="ground"/>, e.g. along a wall base
        /// or under a bench on the paved path. <paramref name="p"/> must be the room root (Decor contract).
        /// </summary>
        public static void Contact(Transform p, Vector3 basePos, float sizeX, float sizeZ, float yRot, Color ground)
        {
            var go = Geo.Soft("Contact", p, Geo.DiscMesh, basePos + new Vector3(0f, 0.006f, 0f), Quaternion.Euler(0f, yRot, 0f),
                              new Vector3(sizeX, 0.012f, sizeZ), ground);
            go.GetComponent<MeshRenderer>().sharedMaterial = Palette.GetContact(ground);
            Tag(go, basePos.y, 0.01f, 1f, 0f);
            NoShadow(go);
        }

        /// <summary>
        /// Contact shade along a long prop (wall base): <paramref name="width"/> across, <paramref name="length"/>
        /// along local z turned by <paramref name="yRot"/>; darkest on the centre line, soft ends.
        /// </summary>
        public static void ContactStrip(Transform p, Vector3 center, float width, float length, float yRot, Color ground)
        {
            var go = Geo.Soft("Contact", p, Geo.ContactStripMesh, center + new Vector3(0f, 0.006f, 0f), Quaternion.Euler(0f, yRot, 0f),
                              new Vector3(width, 0.012f, length), ground);
            go.GetComponent<MeshRenderer>().sharedMaterial = Palette.GetContact(ground);
            Tag(go, center.y, 0.01f, 1f, 0f);
            NoShadow(go);
        }

        /// <summary>Grass tuft (migrated): a small PropKit grass clump.</summary>
        public static void Tuft(Transform p, Vector3 basePos, float scale, float yRot, bool light)
        {
            Props.PropKit.Plant(p, basePos, Props.PlantKind.Grass, scale * 0.7f, yRot, Props.PropBuild.SeedOf(basePos, 5),
                                light ? Mat.FoliageLight : Mat.Foliage);
        }

        /// <summary>A small cluster of low-poly flowers (short stem, pentagon bloom, butter centre), collider-less.</summary>
        public static void Flowers(Transform p, Vector3 basePos, float scale, float yRot, Color bloom)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector3 off = Quaternion.Euler(0f, yRot + i * 120f, 0f) * new Vector3(0f, 0f, 0.13f * scale * (1f + 0.35f * i));
                float h = scale * (0.17f + 0.05f * ((i + 1) % 3));
                Vector3 b = basePos + off;
                var stem = Geo.Soft("Stem", p, Geo.PrismMesh(3), b + new Vector3(0f, h * 0.5f - 0.02f, 0f), Quaternion.Euler(0f, yRot + i * 40f, 0f),
                                    new Vector3(0.035f, h, 0.035f), LevelColors.TuftDark);
                Tag(stem, basePos.y, h, 0.75f, 0f);
                NoShadow(stem);
                Quaternion tilt = Quaternion.Euler(0f, yRot + i * 47f, 0f) * Quaternion.Euler(12f, 0f, 0f);
                var cap = Geo.Soft("Bloom", p, Geo.PrismMesh(5), b + new Vector3(0f, h, 0f), tilt,
                                   new Vector3(0.22f * scale, 0.035f, 0.22f * scale), bloom);
                Tag(cap, basePos.y, h, 1f, 0.05f);
                NoShadow(cap);
                var eye = Geo.Soft("BloomEye", p, Geo.PrismMesh(5), b + new Vector3(0f, h + 0.025f, 0f), tilt,
                                   new Vector3(0.07f * scale, 0.03f, 0.07f * scale), Palette.Butter);
                Tag(eye, basePos.y, h, 1f, 0f);
                NoShadow(eye);
            }
        }

        /// <summary>A flat pilaster on both faces of a wall centred at <paramref name="basePos"/> (thickness = wall depth).</summary>
        public static void Pilaster(Transform p, Vector3 basePos, float height, float wallDepth, Color c)
        {
            var go = Geo.Soft("Pilaster", p, Geo.CubeMesh, basePos + new Vector3(0f, height * 0.5f, 0f), Quaternion.identity,
                              new Vector3(0.7f, height, wallDepth + 0.16f), c);
            Tag(go, basePos.y, height, 0.85f, 0.02f);
        }

        /// <summary>A flat, slightly sunk stepping stone (collider-less: its top is only 5 cm proud).</summary>
        public static void SteppingStone(Transform p, Vector3 basePos, float radius, float yRot)
        {
            var go = Geo.Soft("Stone", p, Geo.PrismMesh(7), basePos + new Vector3(0f, 0.012f, 0f), Quaternion.Euler(0f, yRot, 0f),
                              new Vector3(radius * 2f, 0.06f, radius * 1.65f), LevelColors.PathStone);
            Tag(go, basePos.y - 0.02f, 0.07f, 0.9f, 0.05f);
            NoShadow(go);
        }

        /// <summary>Puffy cloud of three boxes.</summary>
        static readonly System.Collections.Generic.List<Mesh> s_CloudMeshes = new System.Collections.Generic.List<Mesh>();

        /// <summary>
        /// A puffy low-poly cloud: the same flattened-icosphere mesh as the sky's ambient clouds (so clouds
        /// pasted from a photo match the world's), sliceable, no collider.
        /// </summary>
        public static void Cloud(Transform p, Vector3 center, float scale = 1f)
        {
            if (s_CloudMeshes.Count == 0 || s_CloudMeshes[0] == null)
            {
                s_CloudMeshes.Clear();
                var rng = new System.Random(4242);
                for (int i = 0; i < 3; i++)
                    s_CloudMeshes.Add(Ion.Presentation.AmbienceClouds.BuildCloudMesh(rng, 1, "Ion_PhotoCloud" + i, true));
            }
            int k = Mathf.Abs(Mathf.RoundToInt(center.x * 7f + center.y * 3f + center.z)) % s_CloudMeshes.Count;
            var go = Geo.Soft("Cloud", p, s_CloudMeshes[k], center, Quaternion.Euler(0f, (center.x * 37f) % 360f, 0f), Vector3.one * scale, LevelColors.Cloud);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Ion.Presentation.AmbienceClouds.SharedCloudMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Short wooden post (bridge anchors, fences).</summary>
        public static void Post(Transform p, Vector3 basePos, float height = 1.1f)
        {
            var post = Geo.Box(p, basePos + new Vector3(0f, height * 0.5f, 0f), new Vector3(0.25f, height, 0.25f), Palette.DarkWood);
            post.name = "Post";
            Tag(post, basePos.y, height, 0.72f, 0.04f);
            Contact(p, basePos, 0.4f);
        }

        /// <summary>Marks <paramref name="go"/> for DecorCombiner. <paramref name="baseY"/> is local to its parent (the room root).</summary>
        static void Tag(GameObject go, float baseY, float height, float aoMin, float tint, bool sway = false)
        {
            var d = go.AddComponent<Decor>();
            d.BaseY = baseY;
            d.Height = height;
            d.AoMin = aoMin;
            d.Tint = tint;
            d.SwayWeight = sway;
        }

        static void NoShadow(GameObject go)
        {
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
