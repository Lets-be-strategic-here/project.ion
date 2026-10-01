using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels.Arch
{
    /// <summary>What a built piece is: collision, slicing, merging and shadow behaviour (art bible §4).</summary>
    [System.Flags]
    public enum ArchFlags
    {
        None = 0,
        /// <summary>Static MeshCollider (merged per chunk at Bake).</summary>
        Collider = 1,
        /// <summary>Sliceable marker (cut + captured).</summary>
        Sliceable = 2,
        /// <summary>Never merged into the room batch; BakeLocal into its own object (movers, devices).</summary>
        Dynamic = 4,
        NoShadows = 8,
        Solid = Collider | Sliceable,
        /// <summary>Walk-through detail: nosings, inlays, posts, plants.</summary>
        Soft = Sliceable,
        /// <summary>Render-only. ONLY allowed under an Interactable.</summary>
        Visual = 0,
    }

    /// <summary>Plan directions: facing / rise / outward.</summary>
    public enum Dir { PosX, NegX, PosZ, NegZ }

    /// <summary>Helpers for <see cref="Dir"/> (additive to the bible contract).</summary>
    public static class DirUtil
    {
        /// <summary>Unit vector of the direction (y = 0).</summary>
        public static Vector3 ToVector(this Dir d)
        {
            switch (d)
            {
                case Dir.PosX: return Vector3.right;
                case Dir.NegX: return Vector3.left;
                case Dir.NegZ: return Vector3.back;
                default: return Vector3.forward;
            }
        }

        /// <summary>Yaw in degrees that turns +Z into the direction (PosZ 0, PosX 90, NegZ 180, NegX 270).</summary>
        public static float Yaw(this Dir d)
        {
            switch (d)
            {
                case Dir.PosX: return 90f;
                case Dir.NegZ: return 180f;
                case Dir.NegX: return 270f;
                default: return 0f;
            }
        }

        public static Quaternion Rotation(this Dir d) => Quaternion.Euler(0f, d.Yaw(), 0f);

        public static Dir Opposite(this Dir d)
        {
            switch (d)
            {
                case Dir.PosX: return Dir.NegX;
                case Dir.NegX: return Dir.PosX;
                case Dir.PosZ: return Dir.NegZ;
                default: return Dir.PosZ;
            }
        }

        /// <summary>The direction 90° clockwise seen from above (to the right when facing <paramref name="d"/>).</summary>
        public static Dir Right(this Dir d)
        {
            switch (d)
            {
                case Dir.PosZ: return Dir.PosX;
                case Dir.PosX: return Dir.NegZ;
                case Dir.NegZ: return Dir.NegX;
                default: return Dir.PosZ;
            }
        }

        public static bool IsX(this Dir d) => d == Dir.PosX || d == Dir.NegX;

        /// <summary>The plan direction closest to a vector (y ignored).</summary>
        public static Dir FromVector(Vector3 v)
        {
            if (Mathf.Abs(v.x) >= Mathf.Abs(v.z)) return v.x >= 0f ? Dir.PosX : Dir.NegX;
            return v.z >= 0f ? Dir.PosZ : Dir.NegZ;
        }
    }

    /// <summary>Axis-aligned rectangle in plan (X0 &lt; X1, Z0 &lt; Z1).</summary>
    public readonly struct RectXZ
    {
        public readonly float X0, Z0, X1, Z1;

        public RectXZ(float x0, float z0, float x1, float z1)
        {
            X0 = Mathf.Min(x0, x1);
            X1 = Mathf.Max(x0, x1);
            Z0 = Mathf.Min(z0, z1);
            Z1 = Mathf.Max(z0, z1);
        }

        public static RectXZ Centered(float cx, float cz, float width, float depth) =>
            new RectXZ(cx - width * 0.5f, cz - depth * 0.5f, cx + width * 0.5f, cz + depth * 0.5f);

        public float Width => X1 - X0;
        public float Depth => Z1 - Z0;
        public float CenterX => (X0 + X1) * 0.5f;
        public float CenterZ => (Z0 + Z1) * 0.5f;
        public Vector3 Center(float y) => new Vector3(CenterX, y, CenterZ);

        /// <summary>Shrinks every side by <paramref name="d"/> (negative grows).</summary>
        public RectXZ Inset(float d) => new RectXZ(X0 + d, Z0 + d, X1 - d, Z1 - d);

        public bool Contains(float x, float z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;

        public Vector3 Min(float y) => new Vector3(X0, y, Z0);
        public Vector3 Max(float y) => new Vector3(X1, y, Z1);

        public override string ToString() => "x[" + X0 + ", " + X1 + "] z[" + Z0 + ", " + Z1 + "]";
    }

    public enum OpeningKind { Door, Window, Niche, Portal, Hole }

    /// <summary>An opening in a <see cref="Arch.Wall"/>, located by its centre along the wall.</summary>
    public struct Opening
    {
        public OpeningKind Kind;
        /// <summary>Centre, metres along the wall from 'from'.</summary>
        public float At;
        public float Width, Height, Sill, Depth;
        /// <summary>Bracket surround (default true except Hole).</summary>
        public bool Bracket;

        public static Opening Door(float at, float width = 2f, float height = 3f) =>
            new Opening { Kind = OpeningKind.Door, At = at, Width = width, Height = height, Sill = 0f, Bracket = true };

        public static Opening Window(float at, float width = 1f, float height = 2f, float sill = 1f) =>
            new Opening { Kind = OpeningKind.Window, At = at, Width = width, Height = height, Sill = sill, Bracket = true };

        /// <summary>A recess (not through): <paramref name="depth"/> deep from the wall's front face.</summary>
        public static Opening Niche(float at, float width = 1f, float height = 1.5f, float sill = 0.75f, float depth = 0.25f) =>
            new Opening { Kind = OpeningKind.Niche, At = at, Width = width, Height = height, Sill = sill, Depth = depth, Bracket = true };

        /// <summary>Large opening with a stepped corbel lintel (2 steps).</summary>
        public static Opening Portal(float at, float width = 3f, float height = 4f) =>
            new Opening { Kind = OpeningKind.Portal, At = at, Width = width, Height = height, Sill = 0f, Bracket = true };

        public static Opening Hole(float at, float width, float height, float sill = 0f) =>
            new Opening { Kind = OpeningKind.Hole, At = at, Width = width, Height = height, Sill = sill, Bracket = false };

        /// <summary>Copy without the bracket surround (additive helper).</summary>
        public Opening NoBracket() { Opening o = this; o.Bracket = false; return o; }

        internal float Start => At - Width * 0.5f;
        internal float End => At + Width * 0.5f;
        internal float Top => Sill + Height;
        /// <summary>Openings that reach the floor break the plinth.</summary>
        internal bool ReachesFloor => Sill <= 1e-4f && Kind != OpeningKind.Niche;
    }

    [System.Flags]
    public enum WallTrim
    {
        None = 0, Plinth = 1, Cornice = 2, Coping = 4, Pilasters = 8, BothSides = 16,
        Default = Plinth | Cornice | BothSides,
    }

    /// <summary>
    /// One box of a trim profile: <c>Out</c> from the face plane (+ proud, − recessed), <c>Y</c> from the profile's
    /// datum (wall base for bottom profiles, wall top for top profiles).
    /// </summary>
    public struct ProfileBox
    {
        public float Out0, Out1, Y0, Y1;
        public bool UseOverride;
        public Surf Override;

        public ProfileBox(float out0, float out1, float y0, float y1)
        {
            Out0 = out0; Out1 = out1; Y0 = y0; Y1 = y1;
            UseOverride = false;
            Override = default;
        }

        public ProfileBox(float out0, float out1, float y0, float y1, Surf surf)
        {
            Out0 = out0; Out1 = out1; Y0 = y0; Y1 = y1;
            UseOverride = true;
            Override = surf;
        }
    }

    /// <summary>A list of boxes extruded along an axis-aligned run (§2.2).</summary>
    public sealed class TrimProfile
    {
        public readonly ProfileBox[] Boxes;
        /// <summary>Datum = top of the host.</summary>
        public readonly bool FromTop;

        public TrimProfile(bool fromTop, params ProfileBox[] boxes)
        {
            FromTop = fromTop;
            Boxes = boxes ?? new ProfileBox[0];
        }

        /// <summary>Largest projection from the face (free ends are extended by it so corners mitre).</summary>
        public float MaxOut
        {
            get
            {
                float m = 0f;
                for (int i = 0; i < Boxes.Length; i++) m = Mathf.Max(m, Boxes[i].Out1);
                return m;
            }
        }

        public static readonly TrimProfile Plinth = new TrimProfile(false, new ProfileBox(0f, 0.0625f, 0f, 0.5f));
        public static readonly TrimProfile Cornice = new TrimProfile(true,
            new ProfileBox(0f, 0.0625f, -0.25f, -0.125f), new ProfileBox(0f, 0.125f, -0.125f, 0f));
        /// <summary>
        /// Sits ON parapets and low walls. Out is measured from the host's centre line in this profile only: the
        /// box spans the full thickness plus 0.0625 each side (use <see cref="Arch.Coping"/>, which knows T).
        /// </summary>
        public static readonly TrimProfile Coping = new TrimProfile(true, new ProfileBox(-0.0625f, 0.0625f, 0f, 0.125f));
        public static readonly TrimProfile Kerb = new TrimProfile(false, new ProfileBox(0f, 0.125f, 0f, 0.125f));
        /// <summary>Window sill (bottom serif of a window bracket); datum = sill height.</summary>
        public static readonly TrimProfile Sill = new TrimProfile(false, new ProfileBox(0f, 0.125f, -0.0625f, 0f));
        /// <summary>Recessed body box (inset 0.125, 0.125 tall) between stacked slabs and under terrace lips.</summary>
        public static readonly TrimProfile ShadowGap = new TrimProfile(false, new ProfileBox(-0.125f, 0f, 0f, 0.125f));
        /// <summary>Wall pilaster (0.5 wide); its top is relative to the base: use <see cref="Arch.Pilaster"/>.</summary>
        public static readonly TrimProfile PilasterProfile = new TrimProfile(false, new ProfileBox(0f, 0.125f, 0.5f, 3.75f));
    }

    /// <summary>Per-zone surface choices. Arch methods use <see cref="Arch.Style"/> when no Surf is passed.</summary>
    public sealed class ArchStyle
    {
        public Surf Floor = new Surf(Mat.Limestone, Pat.Tile);
        public Surf Wall = new Surf(Mat.Paper, Pat.Formwork);
        public Surf Trim = new Surf(Mat.Limestone, Pat.Courses);
        public Surf Base = new Surf(Mat.Concrete, Pat.Courses);
        public Surf Accent = new Surf(Mat.Terracotta, Pat.Herringbone);
        public Surf Ceiling = new Surf(Mat.Plaster, Pat.Coffer);
        public Surf Wood = new Surf(Mat.Oak, Pat.Boards);
        public Surf Metal = Mat.Graphite;
        /// <summary>Additive: bracket surrounds, sills and inlays (Limestone/Courses by default).</summary>
        public Surf Bracket = new Surf(Mat.Limestone, Pat.Courses);
        /// <summary>Additive: brass inlays (door foot serifs, markers).</summary>
        public Surf Inlay = Mat.Brass;
        /// <summary>Additive: secondary wall surface (accent panels in T2: Cyanotype).</summary>
        public Surf Panel = new Surf(Mat.Plaster, Pat.None);

        /// <summary>A copy (the presets below return fresh instances, so editing one never leaks into another zone).</summary>
        public ArchStyle Clone() => (ArchStyle)MemberwiseClone();

        /// <summary>T1, Hub, Gallery.</summary>
        public static ArchStyle LightTable => new ArchStyle();

        /// <summary>T2: Plaster/Ashlar walls, Cyanotype accents.</summary>
        public static ArchStyle Darkroom => new ArchStyle
        {
            Wall = new Surf(Mat.Plaster, Pat.Ashlar),
            Panel = Mat.Cyanotype,
            Accent = new Surf(Mat.Terracotta, Pat.Herringbone),
        };

        /// <summary>Stairs wing.</summary>
        public static ArchStyle Mint => new ArchStyle { Wall = new Surf(Ion.Presentation.Mat.Mint, Pat.Formwork) };

        /// <summary>Camera wing.</summary>
        public static ArchStyle Rose => new ArchStyle { Wall = new Surf(Ion.Presentation.Mat.Rose, Pat.Formwork) };
    }

    /// <summary>Build-time tag on every Arch child. Read by Bake / BakeLocal / Validate, destroyed by the bake.</summary>
    [DisallowMultipleComponent]
    public sealed class ArchPiece : MonoBehaviour
    {
        public Surf Surf;
        public ArchFlags Flags;

        /// <summary>
        /// Additive: the grid its min/max must sit on for <see cref="Arch.Validate"/> (0.25 architecture, 0.0625
        /// detail and props, 0 = exempt: nosings, inlays, rotated clutter).
        /// </summary>
        public float Snap = Arch.Detail;
    }

    public struct StairResult
    {
        public GameObject Root;
        /// <summary>Centre of the top tread's far edge at the top height (where the stair lands).</summary>
        public Vector3 TopLanding;
        public int Steps;
    }

    /// <summary>Marks the empty grouping objects Arch creates (removed by Bake once they hold nothing).</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class ArchGroup : MonoBehaviour { }
}
