using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels.Props
{
    using Ion.Levels.Arch;
    using B = PropBuild;

    public static partial class PropKit
    {
        // ====================================================================== picture frame

        /// <summary>
        /// A framed print on a wall. <paramref name="center"/> is the image centre on the wall face; the frame
        /// stands proud along <paramref name="facing"/>. SlideMount: 0.125 Paper/Sprocket border with brass L crop
        /// clips at the corners. Gallery: a 0.0625 Walnut frame around a 0.125 Plaster mat. Polaroid: Paper, 0.06
        /// sides, 0.2 bottom. The frame is Soft (sliceable); the image is a print card under its own Interactable
        /// (null image = Frost blank, the "[ ]" coming-soon slide). Returns the frame; the card is
        /// <see cref="FramePrint"/> of it.
        /// </summary>
        public static GameObject PictureFrame(Transform p, Vector3 center, Dir facing, Vector2 imageSize, Texture2D image,
                                              FrameStyle style = FrameStyle.SlideMount)
        {
            GameObject frame = BuildFrame(p, center, facing, imageSize, style, out PrintCard card);
            card.Image = image;
            return frame;
        }

        /// <summary>Picture frame showing a diorama shot once it is captured (addition).</summary>
        public static GameObject PictureFrame(Transform p, Vector3 center, Dir facing, Vector2 imageSize, DioramaShot shot,
                                              FrameStyle style = FrameStyle.SlideMount)
        {
            GameObject frame = BuildFrame(p, center, facing, imageSize, style, out PrintCard card);
            card.Bind(shot);
            return frame;
        }

        /// <summary>The print card of a frame made by <see cref="PictureFrame(Transform, Vector3, Dir, Vector2, Texture2D, FrameStyle)"/> (addition).</summary>
        public static PrintCard FramePrint(GameObject frame) =>
            frame != null && frame.TryGetComponent(out PropLink l) ? l.Print : null;

        static GameObject BuildFrame(Transform p, Vector3 center, Dir facing, Vector2 imageSize, FrameStyle style, out PrintCard card)
        {
            Transform g = B.Frame(p, "Frame " + style, center, B.Yaw(facing));
            const ArchFlags f = ArchFlags.Soft;
            float hw = Mathf.Max(0.1f, imageSize.x) * 0.5f, hh = Mathf.Max(0.1f, imageSize.y) * 0.5f;
            float imageZ;
            switch (style)
            {
                case FrameStyle.Gallery:
                {
                    const float mat = 0.125f, rail = 0.0625f;
                    float mx = hw + mat, my = hh + mat;
                    B.Box(g, -mx, -my, 0f, mx, my, 0.0625f, Mat.Plaster, f);
                    var walnut = new Surf(Mat.Walnut, Pat.Boards);
                    B.Chamfer(g, mx, -my - rail, 0f, mx + rail, my + rail, 0.125f, walnut, f, 0.015625f);
                    B.Chamfer(g, -mx - rail, -my - rail, 0f, -mx, my + rail, 0.125f, walnut, f, 0.015625f);
                    B.Chamfer(g, -mx, my, 0f, mx, my + rail, 0.125f, walnut, f, 0.015625f);
                    B.Chamfer(g, -mx, -my - rail, 0f, mx, -my, 0.125f, walnut, f, 0.015625f);
                    imageZ = 0.0625f + 0.005f;
                    break;
                }
                case FrameStyle.Polaroid:
                {
                    const float side = 0.06f, bottom = 0.2f;
                    B.Box(g, -hw - side, -hh - bottom, 0f, hw + side, hh + side, 0.03125f, Mat.Paper, f);
                    imageZ = 0.03125f + 0.005f;
                    break;
                }
                default:
                {
                    const float b = 0.125f, depth = 0.0625f;
                    var sprocket = new Surf(Mat.Paper, Pat.Sprocket);
                    B.Box(g, -hw, -hh, 0f, hw, hh, 0.03125f, new Surf(Mat.Paper, Pat.Formwork), f);              // print back
                    B.Box(g, hw, -hh - b, 0f, hw + b, hh + b, depth, sprocket, f);
                    B.Box(g, -hw - b, -hh - b, 0f, -hw, hh + b, depth, sprocket, f);
                    B.Box(g, -hw, hh, 0f, hw, hh + b, depth, sprocket, f);
                    B.Box(g, -hw, -hh - b, 0f, hw, -hh, depth, sprocket, f);
                    // brass L crop clips on the four outer corners
                    const float arm = 0.125f, w = 0.0625f, z1 = depth + 0.015625f;
                    float ox = hw + b, oy = hh + b;
                    for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        B.Box(g, new Vector3(sx * ox, sy * oy, depth), new Vector3(sx * (ox - arm), sy * (oy - w), z1), Mat.Brass, f);
                        B.Box(g, new Vector3(sx * ox, sy * (oy - w), depth), new Vector3(sx * (ox - w), sy * (oy - arm), z1), Mat.Brass, f);
                    }
                    imageZ = 0.03125f + 0.005f;
                    break;
                }
            }
            card = PrintBuilder.Build(p, "Frame Print", g.localPosition + g.localRotation * new Vector3(0f, 0f, imageZ), g.localRotation,
                                      new Vector2(hw * 2f, hh * 2f), 0f, false, false, true);
            g.gameObject.AddComponent<PropLink>().Print = card;
            return g.gameObject;
        }

        // ====================================================================== plaque

        /// <summary>
        /// Title-block plaque: brass 0.5 × 0.25 × 0.03 on a 0.0625 Graphite back, <paramref name="center"/> on the
        /// wall face, standing proud along <paramref name="facing"/>. Text such as "[02]  STAIRS" in Graphite. The
        /// plates are Soft; the text is world-canvas type under its own Interactable (captured / removed whole).
        /// </summary>
        public static GameObject Plaque(Transform p, Vector3 center, Dir facing, string text)
        {
            Transform g = B.Frame(p, "Plaque", center, B.Yaw(facing));
            BuildPlaque(p, g, text, ArchFlags.Soft);
            return g.gameObject;
        }

        /// <summary>Plaque plates in frame <paramref name="g"/> (wall face at z = 0) plus its label (parented to <paramref name="labelParent"/>).</summary>
        internal static GameObject BuildPlaque(Transform labelParent, Transform g, string text, ArchFlags f)
        {
            B.Box(g, -0.28125f, -0.15625f, 0f, 0.28125f, 0.15625f, 0.0625f, Mat.Graphite, f);
            B.Chamfer(g, -0.25f, -0.125f, 0.0625f, 0.25f, 0.125f, 0.09375f, Mat.Brass, f, 0.0078125f);
            B.Rivet(g, new Vector3(0.21875f, 0.09375f, 0.09375f), f);
            B.Rivet(g, new Vector3(-0.21875f, 0.09375f, 0.09375f), f);
            B.Rivet(g, new Vector3(0.21875f, -0.09375f, 0.09375f), f);
            B.Rivet(g, new Vector3(-0.21875f, -0.09375f, 0.09375f), f);
            B.Exempt(g); // small hardware hung at prop heights (e.g. 0.9 in an exhibit niche): not plan architecture
            if (string.IsNullOrEmpty(text)) return null;

            // Label: its own Interactable so photos treat the type like the plaque's other unsliceable parts.
            Vector3 pos = new Vector3(0f, 0f, 0.0975f);
            // Placed through world space: g may sit deeper than one level under labelParent.
            Transform node = B.Frame(labelParent, "PlaqueText", labelParent.InverseTransformPoint(g.TransformPoint(pos)),
                                     Quaternion.Inverse(labelParent.rotation) * g.rotation);
            node.gameObject.AddComponent<Ion.Projection.Interactable>();
            // WorldText reads correctly for a viewer looking along the label's +Z: turn it to face the front.
            B.Label(node, Vector3.zero, Quaternion.Euler(0f, 180f, 0f), text, 0.055f, 0.46f, Palette.ColorOf(Mat.Graphite));
            return node.gameObject;
        }

        /// <summary>Plaque type in title-block style: "[02]  STAIRS".</summary>
        public static string PlaqueText(string number, string title)
        {
            string t = string.IsNullOrEmpty(title) ? "" : title.ToUpperInvariant();
            return string.IsNullOrEmpty(number) ? t : "[" + number + "]  " + t;
        }

        // ====================================================================== standing marker

        /// <summary>
        /// Brass "[ ]" floor inlay (§2.2) where the player stands for an intended photo: two bracket strips 0.75
        /// apart (back 0.0625 × 0.75, returns 0.25 × 0.0625), 0.01 proud, and a 0.125 tick on the facing side.
        /// <paramref name="yaw"/> is the solution's view direction (0 = +Z). Soft.
        /// </summary>
        public static GameObject StandingMarker(Transform p, Vector3 feet, float yaw)
        {
            Transform g = B.Frame(p, "StandingMarker", feet, yaw);
            BracketInlay(g, 0.375f, 0.375f, 0.25f, Mat.Brass, ArchFlags.Soft);
            B.Box(g, -0.03125f, 0f, 0.4375f, 0.03125f, 0.01f, 0.5625f, Mat.Brass, ArchFlags.Soft);
            B.Exempt(g);   // an inlay: placed at the solution's exact feet / yaw, not on the grid
            return g.gameObject;
        }

        /// <summary>A floor "[ ]": brackets at x = ±<paramref name="halfGap"/>, each <paramref name="halfLen"/>·2 long in z.</summary>
        static void BracketInlay(Transform g, float halfGap, float halfLen, float returnLen, Mat m, ArchFlags f)
        {
            const float w = 0.0625f, h = 0.01f;
            for (int s = -1; s <= 1; s += 2)
            {
                float xo = s * (halfGap + w * 0.5f), xi = s * (halfGap - w * 0.5f);
                float xr = s * (halfGap + w * 0.5f - returnLen);
                B.Box(g, xo, 0f, -halfLen, xi, h, halfLen, m, f);
                B.Box(g, xo, 0f, halfLen - w, xr, h, halfLen, m, f);
                B.Box(g, xo, 0f, -halfLen, xr, h, -halfLen + w, m, f);
            }
        }
    }
}
