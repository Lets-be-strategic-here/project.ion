using UnityEngine;

namespace Ion.Presentation
{
    /// <summary>
    /// Material roles of the "Light Table" art bible (§3.1). Each maps to ONE shared Ion/FlatToon material
    /// (<see cref="Palette.Get(Mat)"/>). Append only: the numeric values are used as dictionary keys and in tests.
    /// </summary>
    public enum Mat : byte
    {
        Paper, Plaster, Limestone, Concrete, Terracotta, Rose, Mint, Cyanotype,
        Graphite, Brass, Walnut, Oak,
        Foliage, FoliageLight, FoliageDark, Lilac, Lawn,
        TextileRed, Mustard, Teal,
        Frost, Ion, Safelight, Warm,
    }

    /// <summary>
    /// In-shader patterns (§3.2), drawn by Assets/Shaders/IonPattern.hlsl from TEXCOORD0 (xyz = pattern space,
    /// w = <see cref="PatternCode"/>). Code 1 is reserved: a mesh without UV0 reads (0,0,0,1) in GL.
    /// </summary>
    public enum Pat : byte
    {
        None = 0, /* 1 = reserved (missing-attribute default) */
        Tile = 2, TileSmall = 3, Ashlar = 4, Formwork = 5, Herringbone = 6,
        Boards = 7, BoardsX = 8, BoardsZ = 9, Terrazzo = 10, Coffer = 11,
        Sprocket = 12, Frost = 13, Lattice = 14, Lawn = 15, Checker = 16, Courses = 17,
    }

    /// <summary>Material role + pattern. Implicit from Mat (Pat.None).</summary>
    public readonly struct Surf : System.IEquatable<Surf>
    {
        public readonly Mat Mat;
        public readonly Pat Pat;
        public Surf(Mat mat, Pat pat = Pat.None) { Mat = mat; Pat = pat; }
        public static implicit operator Surf(Mat mat) => new Surf(mat);

        public bool Equals(Surf other) => Mat == other.Mat && Pat == other.Pat;
        public override bool Equals(object obj) => obj is Surf s && Equals(s);
        public override int GetHashCode() => ((int)Mat << 8) | (int)Pat;
        public static bool operator ==(Surf a, Surf b) => a.Equals(b);
        public static bool operator !=(Surf a, Surf b) => !a.Equals(b);
        public override string ToString() => Pat == Pat.None ? Mat.ToString() : Mat + "/" + Pat;
    }

    /// <summary>TEXCOORD0.w encoding shared by Arch, DecorCombiner, MeshClipper and the shader.</summary>
    public static class PatternCode
    {
        /// <summary>Added to the pattern id on cut faces (caps): they get the section hatch (poché).</summary>
        public const int CapFlag = 64;

        /// <summary>(int)pat + (cap ? 64 : 0).</summary>
        public static float Encode(Pat pat, bool cap = false) => (int)pat + (cap ? CapFlag : 0);

        /// <summary>round(w); codes 0/1 (and anything unknown) decode to None.</summary>
        public static Pat Decode(float w, out bool cap)
        {
            int code = Mathf.RoundToInt(w);
            cap = code >= CapFlag;
            if (cap) code -= CapFlag;
            if (code < (int)Pat.Tile || code > (int)Pat.Courses) return Pat.None;
            return (Pat)code;
        }

        /// <summary>Sets the cap flag. Idempotent: an already flagged code is returned unchanged.</summary>
        public static float SetCap(float w)
        {
            int code = Mathf.RoundToInt(w);
            return code >= CapFlag ? code : code + CapFlag;
        }

        /// <summary>True if the code carries the cap flag.</summary>
        public static bool IsCap(float w) => Mathf.RoundToInt(w) >= CapFlag;
    }
}
