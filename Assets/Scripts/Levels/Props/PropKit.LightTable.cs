using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels.Props
{
    using Ion.Levels.Arch;
    using Arch = Ion.Levels.Arch.Arch;   // art bible §4: the name-lookup trap
    using B = PropBuild;

    public static partial class PropKit
    {
        // ====================================================================== light table (addition)

        /// <summary>
        /// The hub light table (§7.3, addition to §5): 6.0 × 3.0, top at 0.9, a Frost/Frost glass top set flush in a
        /// Paper/Sprocket rim, an Oak/Boards apron, six Oak legs on Graphite shoes. On it, a contact sheet of
        /// <paramref name="columns"/> × <paramref name="rows"/> prints (1.2 × 0.9 Paper cards, image 1.12 × 0.84) lying
        /// flush on the glass, all under one Interactable "ContactSheet" whose <see cref="LightTableView"/> is returned.
        /// Slot 0 is the front-left print as seen from the front; slots run left to right, then front to back.
        /// </summary>
        public static LightTableView LightTable(Transform p, Vector3 center, Dir facing, int columns = 4, int rows = 2)
        {
            Transform g = B.Frame(p, "LightTable", center, B.Yaw(facing));
            const float hw = 3f, hd = 1.5f, top = 0.875f, rim = 0.125f;
            var frost = new Surf(Mat.Frost, Pat.Frost);
            var sprocket = new Surf(Mat.Paper, Pat.Sprocket);
            B.Box(g, -hw, top - 0.0625f, -hd, hw, top, hd, frost, B.Solid);
            // Rim flush with the glass (no lip: the prints read as lying on a lit surface, not sunk in a tray).
            B.Chamfer(g, -hw - rim, top - 0.25f, hd, hw + rim, top, hd + rim, sprocket, B.Solid, 0.015625f);
            B.Chamfer(g, -hw - rim, top - 0.25f, -hd - rim, hw + rim, top, -hd, sprocket, B.Solid, 0.015625f);
            B.Chamfer(g, -hw - rim, top - 0.25f, -hd, -hw, top, hd, sprocket, B.Solid, 0.015625f);
            B.Chamfer(g, hw, top - 0.25f, -hd, hw + rim, top, hd, sprocket, B.Solid, 0.015625f);
            B.Box(g, -hw + 0.0625f, top - 0.4375f, -hd + 0.0625f, hw - 0.0625f, top - 0.0625f, hd - 0.0625f, OakBoards, B.Solid);
            for (int ix = -1; ix <= 1; ix++)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float x = ix * (hw - 0.375f), z = sz * (hd - 0.375f);
                B.Chamfer(g, x - 0.09375f, 0.0625f, z - 0.09375f, x + 0.09375f, top - 0.4375f, z + 0.09375f, OakBoards, B.Solid, 0.015625f);
                B.Box(g, x - 0.125f, 0f, z - 0.125f, x + 0.125f, 0.0625f, z + 0.125f, Mat.Graphite, B.Solid);
            }
            // A Graphite stretcher between the leg pairs, and brass rivets where the rim meets (the bracket joints).
            B.Box(g, -hw + 0.375f, 0.25f, -0.03125f, hw - 0.375f, 0.3125f, 0.03125f, Mat.Graphite, B.Soft);
            for (int ix = -1; ix <= 1; ix++)
                B.Box(g, ix * (hw - 0.375f) - 0.03125f, 0.25f, -hd + 0.375f, ix * (hw - 0.375f) + 0.03125f, 0.3125f, hd - 0.375f, Mat.Graphite, B.Soft);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                B.Rivet(g, new Vector3(sx * (hw + 0.0625f), top - 0.125f, sz * (hd + rim + 0.015625f)), B.Soft);

            // The contact sheet (one Interactable for every print).
            var sheet = new GameObject("ContactSheet");
            sheet.transform.SetParent(g, false);
            sheet.transform.localPosition = new Vector3(0f, top, 0f);
            sheet.AddComponent<Ion.Projection.Interactable>();
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            const float pw = 1.2f, ph = 0.9f, margin = 0.04f, card = 0.00390625f;
            float gapX = columns > 1 ? Mathf.Min(0.2f, (2f * hw - 0.4f - columns * pw) / (columns - 1)) : 0f;
            float gapZ = rows > 1 ? Mathf.Min(0.25f, (2f * hd - 0.4f - rows * ph) / (rows - 1)) : 0f;
            float sw = columns * pw + (columns - 1) * gapX, sd = rows * ph + (rows - 1) * gapZ;
            var slots = new PrintCard[columns * rows];
            Quaternion flat = Quaternion.Euler(-90f, 0f, 0f);   // image +Z -> up, image top -> away from the front
            Transform cards = B.Frame(sheet.transform, "Cards", Vector3.zero, Quaternion.identity);
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                // Viewer at the front (+Z) sees +X on their left: slot 0 is the left-front print.
                float x = sw * 0.5f - pw * 0.5f - c * (pw + gapX);
                float z = sd * 0.5f - ph * 0.5f - r * (ph + gapZ);
                // The print's white Paper card, a hair above the glass; the image lies on it.
                B.Box(cards, x - pw * 0.5f, 0f, z - ph * 0.5f, x + pw * 0.5f, card, z + ph * 0.5f, Mat.Paper, B.Device);
                slots[r * columns + c] = PrintBuilder.Build(sheet.transform, "Print " + (r * columns + c),
                    new Vector3(x, card + 0.001f, z), flat,
                    new Vector2(pw - 2f * margin, ph - 2f * margin), 0f, true, false, false);
            }
            B.Exempt(cards);
            B.BakeDevice(cards.gameObject);
            var view = sheet.AddComponent<LightTableView>();
            view.Init(slots);
            return view;
        }
    }
}
