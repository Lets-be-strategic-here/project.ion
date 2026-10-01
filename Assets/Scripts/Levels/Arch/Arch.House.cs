using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels.Arch
{
    public static partial class Arch
    {
        /// <summary>
        /// A closed building mass (art bible rule 4: every zone has one building, not just trays with parapets):
        /// Paper/Formwork walls on <paramref name="r"/> with Limestone quoins, a plinth and a cornice, windows every
        /// <paramref name="bay"/> whose heads sit on the 3.0 datum of each storey (recessed, pale Frost glass with a
        /// Limestone mullion; Ultra adds a transom), a Walnut door under an Oak awning in the middle of the
        /// <paramref name="front"/> wall, and a flat roof (0.25 overhang, parapet, scuppers) with a stair-house.
        /// Solid walls (it is meant to stand outside the walkable court); glazing and hardware are Soft. All sliceable.
        /// </summary>
        public static GameObject House(Transform p, RectXZ r, float baseY, int storeys, Dir front, float bay = 2f, bool door = true)
        {
            GameObject g = Group(p, "House", new Vector3(0f, baseY, 0f));
            Transform t = g.transform;
            storeys = Mathf.Clamp(storeys, 1, 3);
            float h = Storey * storeys;
            const float th = WallThickness, half = th * 0.5f;
            float x0 = r.X0, z0 = r.Z0, x1 = r.X1, z1 = r.Z1;

            // Walls counter-clockwise from above, so every front face (right-hand side) faces out.
            HouseWall(t, new Vector3(x0, 0f, z0 + half), new Vector3(x1, 0f, z0 + half), h, storeys, bay, door && front == Dir.NegZ);
            HouseWall(t, new Vector3(x1 - half, 0f, z0 + th), new Vector3(x1 - half, 0f, z1 - th), h, storeys, bay, door && front == Dir.PosX);
            HouseWall(t, new Vector3(x1, 0f, z1 - half), new Vector3(x0, 0f, z1 - half), h, storeys, bay, door && front == Dir.PosZ);
            HouseWall(t, new Vector3(x0 + half, 0f, z1 - th), new Vector3(x0 + half, 0f, z0 + th), h, storeys, bay, door && front == Dir.NegX);

            // Quoins: 0.75 Limestone corner blocks, 0.125 proud of both faces, plinth to cornice.
            foreach (float cx in new[] { x0, x1 })
            foreach (float cz in new[] { z0, z1 })
            {
                float sx = cx == x0 ? 1f : -1f, sz = cz == z0 ? 1f : -1f;
                Vector3 a = new Vector3(cx - sx * 0.125f, 0f, cz - sz * 0.125f);
                Vector3 b = new Vector3(cx + sx * 0.625f, h - 0.25f, cz + sz * 0.625f);
                Tag(Box(t, a, b, Style.Trim), Detail);
            }

            // Roof: slab on the walls with a 0.25 eave, parapet, scuppers (Roof), and a stair-house at the back corner.
            Roof(t, r.Inset(-0.25f), h + 0.5f, 0.5f, 0.75f);
            float bx = front == Dir.NegX ? x1 - 2.75f : x0 + 0.75f;
            float bz = front == Dir.NegZ ? z1 - 2.75f : z0 + 0.75f;
            Tag(Box(t, new Vector3(bx, h + 0.5f, bz), new Vector3(bx + 2f, h + 2f, bz + 2f), Style.Wall), Detail);
            Tag(Box(t, new Vector3(bx - 0.125f, h + 2f, bz - 0.125f), new Vector3(bx + 2.125f, h + 2.25f, bz + 2.125f), Style.Trim), Detail);
            return g;
        }

        /// <summary>One wall of a <see cref="House"/>: windows per storey (heads on that storey's datum) and the door.</summary>
        static void HouseWall(Transform t, Vector3 from, Vector3 to, float h, int storeys, float bay, bool withDoor)
        {
            Frame fr = Frame.Along(from, to, out float length);
            if (length < 1.5f) return;
            const float half = WallThickness * 0.5f, depth = 0.25f;
            var openings = new System.Collections.Generic.List<Opening>();
            float doorAt = Mathf.Round(length * 0.5f / Sub) * Sub;
            const float doorW = 1.5f;
            if (withDoor) openings.Add(Opening.Door(doorAt, doorW, HeadY));

            bay = Mathf.Max(bay, 1.5f);
            int n = Mathf.FloorToInt((length - 1.5f) / bay) + 1;
            float first = (length - (n - 1) * bay) * 0.5f;
            for (int s = 0; s < storeys; s++)
            {
                float sill = s * Storey + 1f;
                for (int i = 0; i < n; i++)
                {
                    float at = Mathf.Round((first + i * bay) / Detail) * Detail;
                    if (at < 1f || at > length - 1f) continue;
                    if (s == 0 && withDoor && Mathf.Abs(at - doorAt) < doorW * 0.5f + 1f) continue;
                    openings.Add(Opening.Niche(at, 1f, 2f, sill, depth));
                }
            }
            Wall(t, from, to, h, WallThickness, WallTrim.Plinth | WallTrim.Cornice, openings.ToArray());

            // Glazing in each window niche: Frost glass on the niche back, a Limestone mullion (and on Ultra a
            // transom) 1/16 in front of it.
            float back = half - depth;
            var glass = new Surf(Mat.Frost, Pat.Frost);   // pale sky glass (self-lit, capped below white)
            foreach (Opening o in openings)
            {
                if (o.Kind != OpeningKind.Niche) continue;
                Tag(fr.Box(t, o.Start, o.End, o.Sill, o.Top, back, back + Detail, glass, ArchFlags.Soft), 0f);
                Tag(fr.Box(t, o.At - 0.03125f, o.At + 0.03125f, o.Sill, o.Top, back + Detail, back + 0.125f, Style.Trim, ArchFlags.Soft), 0f);
                using (Ultra())
                    Tag(fr.Box(t, o.Start, o.End, o.Sill + 1.25f, o.Sill + 1.3125f, back + Detail, back + 0.125f, Style.Trim, ArchFlags.Soft), 0f);
            }

            if (!withDoor) return;
            // Door leaf (Walnut boards) 0.25 back from the face, a brass pull, and an Oak awning on two brackets.
            float d0 = doorAt - doorW * 0.5f, d1 = doorAt + doorW * 0.5f;
            Tag(fr.Box(t, d0, d1, 0f, HeadY, -half + 0.125f, half - 0.25f, new Surf(Mat.Walnut, Pat.Boards)), Detail);
            Tag(fr.Box(t, d1 - 0.3125f, d1 - 0.25f, 0.875f, 1.25f, half - 0.25f, half - 0.1875f, Mat.Brass, ArchFlags.Soft), 0f);
            Tag(fr.Box(t, d0 - 0.5f, d1 + 0.5f, HeadY + 0.25f, HeadY + 0.375f, half, half + 1f, new Surf(Mat.Oak, Pat.Boards), ArchFlags.Soft), Detail);
            foreach (float u in new[] { d0 - 0.375f, d1 + 0.25f })
                Tag(fr.Box(t, u, u + 0.125f, HeadY - 0.125f, HeadY + 0.25f, half, half + 0.75f, Style.Trim, ArchFlags.Soft), Detail);
        }
    }
}
