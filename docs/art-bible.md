# [project]ion art bible and technical contract

Status: **binding** for every build agent · Version 1 · 2026-10-01 · Owner: lead art director
Why this theme was chosen: [`art-bible-decision.md`](art-bible-decision.md). Engine facts: [`BUILD.md`](BUILD.md). Prior contract: [`superpowers/specs/2026-09-30-vertical-slice-design.md`](superpowers/specs/2026-09-30-vertical-slice-design.md).

Words used precisely in this document:
- **must** means binding.
- **should** means expected, unless you have a written reason.
- "Lead A–E" refers to the ownership map in §10.

All dimensions are in metres, in room-local space. Floor top is y = 0 and the walking direction is +Z unless stated.

---

## 1. Theme: "Light Table"

**Statement.** [project]ion is a sunlit contact sheet built at 1:1: off-white, board-formed architecture on a strict metre grid, where every threshold, exhibit and device is framed by a pair of square brackets, the crop marks of a photograph. Colour lives in the sky, the plants and the few things you can touch, and everything you can use glows ion-blue. The hub is the light table itself, with every project laid out as a print, and placing a photograph projects it into solid space, its cut edges keeping the faint section hatch of a drawing.

How the title is carried, quietly:
- **`[ ]`** appears as crop-mark brackets around openings, exhibits, devices and standing markers.
- **"project"** is the hub, a contact sheet of portfolio projects.
- **"projection"** is the mechanic, plus the section poché on cut faces.
- **"ion"** is the single reserved interaction colour, `#9FE3FF`.

The literal title appears only on the hub entrance plaque and in the UI.

### The 10 design rules

1. **The grid is law.** The module is 1.0 m, the sub-module 0.25 m and the detail step 0.0625 m.
   - Every architectural min/max snaps to 0.25.
   - Prop details may use 0.0625.
   - Zone and diorama origins are whole metres, so in-shader tiling lands on geometry edges.
2. **Right angles only.** Architecture has no curves and no diagonal walls.
   - "Arches" are stepped corbel lintels.
   - Curves (n-gon prisms) are allowed only for living things, vessels, lamps, bulbs, poles and the teleporter disc.
   - Rotations are multiples of 90°, except Soft clutter such as chairs and plants.
3. **The bracket is the only ornament.** `[ ]` surrounds go only on thresholds, exhibits, devices and standing markers.
   - The bracket is never a literal glyph in the world, except on the hub plaque.
   - At most one bracket family is in focus per view.
4. **Three scales on every surface.** Each surface has three levels of detail, then the shader pattern on top.
   - Massing at 4 m.
   - Articulation at 0.5 m: plinths, copings, pilasters, cornices.
   - Detail at 0.0625 m: shadow gaps, drips, rivets, clips.

   No uninterrupted surface may be larger than 4 × 4 m without articulation.
5. **Thick and few.** Walls are 0.5, slabs 0.5 and parapets solid 0.25.
   - Thin metal appears only on bridge railings, lamps and frames.
   - Prefer one deep reveal to three shallow ones.
6. **Off-white structure, colour elsewhere.** Structure is Paper, Plaster, Limestone or Concrete, or exactly one zone tint (Rose, Mint).
   - Architecture is never multicolour.
   - Saturation belongs to the sky, plants, textiles and devices.
   - Plants are lush and generous: they are the softener.
7. **Shadows are tinted by the sky, not made of it.** The shade side is a value drop (a light grey, luma ≈ 0.58) carrying about a quarter of the zone mood's tint (§3.4), plus a tiny additive hue: never neutral grey, and never one saturated colour over the whole zone (a red rug stays red in shade, it does not go olive).
   - There are no outlines and no black.
   - The darkest value in the world is Graphite `#383D47`.
8. **Ion means "use me".** The `#9FE3FF` emissive is reserved for interactables: pickups, buttons, levers, teleporters, live exhibits and the raised-photo edge.
   - Nothing decorative glows ion.
   - Every intended photo spot has a **brass `[ ]` standing marker** on the floor.
9. **Patterns are quiet.** Patterns are drawn in-shader with 4–8% value contrast and one pattern per mesh element. They fade out with distance.
   - Floors are the contact-sheet tile.
   - Walls are formwork.
   - Wood is boards.
10. **Everything is sliceable except devices.** World geometry is closed convex pieces, and trims are Sliceable together with their wall.
    - Devices (`Interactable`) are never cut; photos capture or remove them whole.
    - A photo of a device pastes a working device.

---

## 2. Grid and proportions

### 2.1 Constants (`Ion.Levels.Arch.Arch`)

| Name | Value | Notes |
|---|---|---|
| `M` | 1.0 | Module. Floor tile = 1 module = 2 stair treads. |
| `Sub` | 0.25 | Sub-module. All architectural min/max snap to this. |
| `Detail` | 0.0625 | Trims, chamfers, rivets, inlays. |
| `Storey` | 4.0 | Floor to floor. The hub canopy underside is at 3.5. |
| `WallThickness` | 0.5 | Exterior and structural walls. Interior partitions are 0.25. |
| `SlabThickness` | 0.5 | Floors and roofs. |
| `HeadY` | 3.0 | **Datum.** Every door head, window head and niche head aligns here. |
| `DoorW × DoorH` | 2.0 × 3.0 | Standard bracket doorway (clear). The small door is 1.5 × 2.5. |
| `Window` | 1.0 × 2.0, sill 1.0 | Its head lands on the 3.0 datum. |
| `Niche` | 1.0 × 1.5 × 0.25 deep, sill 0.75 | |
| `Portal` | 3.0 × 4.0 | Large opening with a stepped corbel lintel (2 steps). |
| `Rise × Tread` | 0.25 × 0.5 | 26.6°. Solid step columns. The controller's `stepOffset` 0.4 handles it, so no hidden ramp collider is needed. |
| `StairWidth` | 2.0 (min 1.5; hub 3.0) | |
| `Rail` | 1.0 | Parapet and railing top height. |
| Player | eye 1.62, jump 1.15, step 0.4, slope 50° | Taken from `PlayerFactory` and `FirstPersonController`. |
| Gameplay barriers | ≥ 1.5 | Can't be jumped. Casual level changes are 0.25 or 0.5. |
| Gameplay gaps | ≥ 3.5 | Can't be jumped (the reach is about 2.8). |

### 2.2 Trim profiles as box extrusions

Each profile is a list of boxes extruded along an axis-aligned run. Two coordinates locate each box:
- `out` is measured from the face plane (+ = proud of the face, − = recessed into it).
- `y` is measured from the profile's datum: the wall base for bottom profiles, the wall top for top profiles.

All boxes are closed and convex and take the run's length. On a free end, the run is extended by the largest `out` so corners mitre as overlapping boxes. Overlap is fine; nothing is boolean-unioned.

| Profile | Datum | Boxes `(out0→out1, y0→y1)` and material | Notes |
|---|---|---|---|
| `Plinth` | base | `(0→0.0625, 0→0.5)` Limestone/Courses | Every wall foot, both sides unless the side is buried. |
| `Cornice` | top | `(0→0.0625, −0.25→−0.125)`, `(0→0.125, −0.125→0)`, both Limestone/Courses | Two steps. Tops 4 m walls. |
| `Coping` | top | `(−T/2−0.0625 → +T/2+0.0625 across the full thickness, 0→0.125)` Limestone | Sits *on* parapets and low walls (T = wall thickness). |
| `SlabEdge` | slab top | `(full, −0.125→0)` top layer; `(−0.0625 inset, −0.1875→−0.125)` groove; `(full, −0.5→−0.1875)` body | The drip groove reads as a dark line. 3 boxes per slab, not per edge. |
| `Kerb` | floor | `(0→0.125, 0→0.125)` Limestone | Bridge and terrace edges with railings. |
| `Sill` | sill | `(0→0.125, −0.0625→0)` Limestone, extending 0.125 past each jamb | This is the bottom serif of a window bracket. |
| `Pilaster` | base | `(0→0.125, 0.5→H−0.25)`, 0.5 wide | Every 4 m on walls longer than 4 m. Runs between the plinth and the cornice. |
| `ShadowGap` | any | `(−0.125, 0→0.125)`, done as an *inset body box* | Between stacked tower slabs and under terrace lips. |
| `BracketJamb` `[` / `]` | opening edge | Strip 0.25 wide × (h + 0.25) tall, proud 0.0625. Head serif 0.375 long × 0.25 tall. Foot serif 0.375 × 0.25 (windows and niches); for doors the foot serif becomes a brass floor inlay 0.375 × 0.125 × 0.01 | The head centre between the serifs stays **bare**. This open frame is the signature. |
| `StandingMarker` | floor | Two brass strips, each `[`-shaped: back 0.0625 × 0.75, returns 0.25 × 0.0625, 0.01 proud, set 0.75 apart. A 0.125 brass tick on the facing side | Soft (sliceable, no collider). |

### 2.3 Architectural elements

| Element | Composition (all boxes unless stated) |
|---|---|
| Floor slab | `SlabEdge` profile (3 boxes). Top pattern Tile. |
| Floating terrace | Slab, then a body down to `depth` (Concrete/Courses), then a **stepped planar underside**: 2 steps, each inset 0.5 and 0.5 tall. No rock spikes. |
| Wall with openings | Full-height segments between openings, a lintel box above each opening and a sill box below each window or niche. Reveals show the full 0.5 thickness. Default trims: Plinth + Cornice both sides, Pilasters if longer than 4. |
| Parapet | Body 0.25 thick × 0.875 tall + Coping. Total 1.0. |
| Railing (bridges only) | Graphite posts 0.0625² at 1.0 pitch, a top rail 0.125 w × 0.0625 h at 1.0, a mid rail 0.0625² at 0.5, and a Kerb. Only the top rail and kerb are Solid; posts and mid rail are Soft. |
| Column | Base 0.75² × 0.25, shaft 0.5² square, capital 0.625² × 0.125 plus an abacus 0.75² × 0.125. |
| Pier | Plain massive box ≥ 0.75 wide with a 0.125 ShadowGap at 0.5 from the top. |
| Bracket pier | U in plan: back 1.0 × 0.25, two returns 0.25 × 0.5. Three boxes. Used under canopies and exhibit stands. |
| Bracket frame (freestanding portal) | Two bracket jambs 0.5 thick + a lintel 0.5 deep, clear 2.0 × 3.0, with bracket surrounds front and back. The Viewfinder "standing frame". |
| Corbel arch | Opening 3.0 × 4.0. Two corbel steps, each 0.25 in and 0.25 down, under a 0.5 lintel. |
| Stair | Solid step columns: step i is a box from the floor to (i+1) × 0.25, tread 0.5, plus a 0.03 Limestone nosing strip (Soft). Cheek walls 0.25 thick rise 0.25 above the nosing line in 0.5-long stepped boxes and carry the **Sprocket** pattern on their outer face. |
| Ramp | Wedge ≤ 30° + cheek walls as for stairs. |
| Canopy | Slab 0.5 + downstand ribs 0.25 w × 0.75 deep at 2.0 pitch. Coffer pattern underneath. Rests on bracket piers. |
| Flat roof | Slab + parapet 0.75 + a scupper box (0.25³, proud 0.25) every 4 m. |
| Pergola | 0.5 piers at 4 m, Walnut beams 0.125 w × 0.25 h at 0.5 pitch. |
| Contact-sheet screen | Grid of 0.75 square openings with 0.25 mullions, 4 across per 4 m bay. Geometry: boxes. |
| Slab tower (skyline, backdrop) | Boxes 2.0 tall, footprint 2–4, each offset sideways by a multiple of 0.25 (max 0.5), with a 0.125 ShadowGap between them. |

---

## 3. Materials and patterns

### 3.1 Palette (`Ion.Presentation.Mat`)

Each `Mat` maps to **one shared `Ion/FlatToon` material** (`Palette.Get(Mat)`). The hex values are the lit albedo.

| `Mat` | Hex | Role | Default `Pat` |
|---|---|---|---|
| `Paper` | `#EFEBE3` | Main walls, frames of structure | Formwork |
| `Plaster` | `#E4DDD0` | Interior and secondary walls, ceilings | None / Coffer |
| `Limestone` | `#D7CDBB` | Floors, paving, trims, copings, stair treads | Tile / Courses |
| `Concrete` | `#BDB8AE` | Terrace bodies, undersides, piers, slab towers | Courses |
| `Terracotta` | `#C47755` | Accent floors in alcoves and niches, planters | Herringbone / TileSmall |
| `Rose` | `#E8CBC4` | Zone tint: Camera wing structure | Formwork |
| `Mint` | `#CADDD3` | Zone tint: Stairs wing structure | Formwork |
| `Cyanotype` | `#2F5B88` | Tutorial "darkroom" accent walls, print backs, rewind wash | None |
| `Graphite` | `#383D47` | Metal: rails, posts, lamp stems, frame bezels, device trims. The darkest value in the game | None |
| `Brass` | `#C59A45` | Standing markers, plaques, rivets, crop clips, footprint plates | None |
| `Walnut` | `#7A4E33` | Bench seats, tables, gallery frames, pergola beams | Boards |
| `Oak` | `#C8A273` | Decks, easels, sliding bridges | Boards |
| `Foliage` | `#5C8A48` | Plant mass | None (sway) |
| `FoliageLight` | `#94B866` | Plant tips, young leaves | None (sway) |
| `FoliageDark` | `#3D6437` | Cypress, ivy, undergrowth | None (sway) |
| `Lilac` | `#9E86B4` | Camera wing foliage accent | None (sway) |
| `Lawn` | `#84AC5C` | Inset lawns and planter soil cover | Lawn |
| `TextileRed` | `#B24A34` | Rugs, cushions, lever bases | None |
| `Mustard` | `#D6A43B` | Instant camera body, cushions, monobloc chairs | None |
| `Teal` | `#3C8C88` | Café chairs, a few monoblocs | None |
| `Frost` | `#F5F8F8`, emissive 0.35 | Light-table tops, "coming soon" slides, projector screens | Frost |
| `Ion` | `#9FE3FF`, emissive 0.8 | **Interactables only** | None |
| `Safelight` | `#E9853B`, emissive 0.6 | Darkroom lamps (tutorial T2 only) | None |
| `Warm` | `#FFD9A0`, emissive 0.7 | Pendant and sconce bulbs, lanterns | None |

The current `Palette.Get(Color)` stays (legacy, used by old Geo code) until Lead C has migrated every room. After that, new code must use `Palette.Get(Mat)` only.

### 3.2 Patterns (`Ion.Presentation.Pat`): in-shader, from per-vertex pattern space

The pattern code is written into `TEXCOORD0.w` (§6).
- Code **0 and 1 mean None**. Code 1 is reserved because a mesh *missing* UV0 reads `(0,0,0,1)` in GL.
- The **face role** (top, side or bottom) comes from the dominant axis of the pattern-space face normal.
- On top/bottom faces, `uv = P.xz`.
- On side faces, `uv = (|n.x| > |n.z| ? P.z : P.x, P.y)`.

Every grout or line value below is a **multiplier on albedo**: −8% means × 0.92.

| `Pat` (code) | Top faces | Side faces | Bottom faces |
|---|---|---|---|
| `None` (0) | plain | plain | −6% |
| `Tile` (2), the contact sheet | 1.0 square tiles, joint 0.015 at −12%. Per-tile jitter ±3% (`hash(floor(uv))`). Every 4th joint is a 0.04 **datum band** at −8% (the 4 × 4 "sheet"). In one tile per sheet, chosen by hash, the "selected frame" is +3% and warmer (×(1.02, 1.0, 0.97)) | Courses at 0.5 | −6% |
| `TileSmall` (3) | 0.5 square, joint 0.012 at −12%, jitter ±4% | Courses at 0.25 | −6% |
| `Ashlar` (4) | plain | Running bond 1.0 × 0.5, offset 0.5 per course, joint 0.012 at −10%, jitter ±2.5% | −6% |
| `Formwork` (5) | plain | Panels 1.0 w × 2.0 h, joint 0.01 at −8%. Board lines every 0.125 vertically at −2%. Tie holes: radius 0.025 at −14%, at (0.25, 0.25) and (0.75, 0.25) inside each panel's top and bottom 0.5 | −6% |
| `Herringbone` (6) | Bricks 0.5 × 0.125 in 45° herringbone, joint 0.01 at −12%, jitter ±5% | Courses at 0.125 | −6% |
| `Boards` (7) | Resolved at bake to `BoardsX` or `BoardsZ` along the element's longest horizontal extent | | |
| `BoardsX` (8) / `BoardsZ` (9) | Boards 0.125 wide running along X (or Z). Butt joints every 2.0, staggered per board by `hash(boardIndex)`. Joint 0.008 at −14%, jitter ±5% | Same boards along the face's horizontal axis | −6% |
| `Terrazzo` (10) | Chips: cell 0.05; 15% of cells (by hash) ±10%. Brass divider line 0.01 on a 2.0 grid at +6% warm | plain | −6% |
| `Coffer` (11) | plain | plain | 1.0 grid, rebate band 0.0625 at −10% |
| `Sprocket` (12) | plain | A 0.25-high band at −10%, centred on the face's local `v mod 0.5`. Perforations: rounded rects 0.11 × 0.08 at 0.25 pitch at +8%. **Only** on stair cheeks, the light-table rim and slide-mount frames | −6% |
| `Frost` (13) | Emissive; faint 0.125 grid at −3% | plain | plain |
| `Lattice` (14) | plain | Breeze block on a 0.25 module: a diamond "hole" at −35% inside each cell with a 0.03 frame. This fakes depth; use it only on non-walkable screens | plain |
| `Lawn` (15) | 1.0 mowing stripes along X at ±3%, plus the existing `_GroundVar` facets | plain | plain |
| `Checker` (16) | 0.5 checker at ±6%. **Accent only** (Gallery floor); never in main architecture | plain | −6% |
| `Courses` (17) | plain | Horizontal joints every 0.5 at −8% (strata on terraces, trims, piers) | −6% |

**Cut faces (poché).** Vertices with the cap flag (code + 64) get a 45° hatch:
- in face uv, pitch 0.1, line width 0.012, at −5%;
- strength set by the global `_IonCutHatch` (default 1; 0 on the Low tier);
- the cut face otherwise shows its pattern's *side* treatment.

**Anti-aliasing and distance.** Every line is drawn with `fwidth`-based coverage.
- A line narrower than 1.25 px is drawn 1.25 px wide with its contrast scaled down in proportion.
- All pattern contrast fades linearly to 0 between `_IonPatternFade.x` = 25 m and `.y` = 45 m (Low tier: 15 / 30).
- There must be no moiré at the 1.5 DPR cap.

### 3.3 Material use by surface

These are the defaults that `ArchStyle.LightTable` encodes. Zones change only the fields named in their mood.

| Surface | `Surf` |
|---|---|
| Floor tops | Limestone / Tile |
| Alcove, niche and teleporter-bay floors | Terracotta / Herringbone |
| Walls | Paper / Formwork. In the Camera wing: Rose / Formwork. In the Stairs wing: Mint / Formwork |
| Interior walls (T2 darkroom) | Plaster / Ashlar, with Cyanotype accent panels |
| Trims, copings, plinths, stair treads | Limestone / Courses |
| Terrace bodies, piers, towers | Concrete / Courses |
| Ceilings, canopy undersides | Plaster / Coffer |
| Decks, sliding bridges, easels | Oak / Boards |
| Seats, frames, tables | Walnut / Boards |
| Metal | Graphite / None |

### 3.4 Zone moods (`Ion.Presentation.ZoneMood`)

Sky, sun, ambient and **shadow tint** are globals, so materials stay shared across zones.

| Zone | Sky top → horizon | Sun | Shadow tint | Fog | Notes |
|---|---|---|---|---|---|
| T1 "Dawn" | `#8FB8E6` → `#F3E1D6` | `#FFE2C2`, low from the east | `#6E7FB8` | warm haze | First impression: soft, hopeful. |
| T2 "Darkroom" | `#3E4E86` → `#E7A98F` | `#FFC9A0`, low | `#625E74` (a dim grey-violet: a darkroom, not a lavender room) | dense | Safelight sconces, Cyanotype accents. |
| Hub "Noon" | `#1FA6D2` → `#D6F0F5` | `#FFF4E2`, high | `#4D8FC4` | light | The most saturated sky, the most lush planting. |
| Stairs wing "Mint" | `#45B3C4` → `#CFEDE6` (natural mint-cyan) | `#FFF1DA` | `#5E9EA0` | light | Mint structure tint. |
| Camera wing "Rose" | `#A9BEE3` → `#FBE6E6` (blue over a rose horizon) | `#FFE6D8` | `#9A7C98` | medium | Rose structure, Lilac plants. |
| Gallery "Golden" | `#F2B66B` → `#FCE9C8` | `#FFD9A6`, low from the west | `#8A82A8` (violet-blue, not magenta) | medium | The ending. |

Two **binding** rules:
- `Atmosphere.ApplyMood` must be applied **before** a zone's diorama shots are captured, so every photo is exposed in its zone's light.
- When the player enters a zone, its mood blends in over 2.0 s.

---

## 4. Architecture kit API: `Ion.Levels.Arch`

Files: `Assets/Scripts/Levels/Arch/**` (Lead A).

Every method has these properties:
- It returns the **root GameObject of a group** whose children are closed convex boxes, wedges or prisms built on `Geo`'s shared unit meshes.
- Each child carries an `ArchPiece` (its `Surf` and `ArchFlags`).
- Children are **Solid by default**: `MeshCollider` + `Sliceable`.
- Positions are local to `p`, and all min/max values snap to 0.25.
- Nothing is visible to the slicer as "merged" until `Bake`.

> **C# name lookup trap (binding):** the static class `Arch` lives in the namespace `Ion.Levels.Arch`. Code whose namespace is `Ion.Levels` itself will resolve `Arch` to the *namespace*.
> - New rooms live in `namespace Ion.Levels.Rooms` and PropKit lives in `namespace Ion.Levels.Props`.
> - Both declare `using Arch = Ion.Levels.Arch.Arch;` **inside** the namespace block, or use `using static Ion.Levels.Arch.Arch;`.

```csharp
// ---------------------------------------------------------------- Assets/Scripts/Presentation/Surface.cs (Lead A)
namespace Ion.Presentation
{
    public enum Mat : byte
    {
        Paper, Plaster, Limestone, Concrete, Terracotta, Rose, Mint, Cyanotype,
        Graphite, Brass, Walnut, Oak,
        Foliage, FoliageLight, FoliageDark, Lilac, Lawn,
        TextileRed, Mustard, Teal,
        Frost, Ion, Safelight, Warm,
    }

    public enum Pat : byte
    {
        None = 0, /* 1 = reserved (missing-attribute default) */
        Tile = 2, TileSmall = 3, Ashlar = 4, Formwork = 5, Herringbone = 6,
        Boards = 7, BoardsX = 8, BoardsZ = 9, Terrazzo = 10, Coffer = 11,
        Sprocket = 12, Frost = 13, Lattice = 14, Lawn = 15, Checker = 16, Courses = 17,
    }

    /// <summary>Material role + pattern. Implicit from Mat (Pat.None).</summary>
    public readonly struct Surf
    {
        public readonly Mat Mat;
        public readonly Pat Pat;
        public Surf(Mat mat, Pat pat = Pat.None) { Mat = mat; Pat = pat; }
        public static implicit operator Surf(Mat mat) => new Surf(mat);
    }

    /// <summary>TEXCOORD0.w encoding shared by Arch, DecorCombiner, MeshClipper and the shader.</summary>
    public static class PatternCode
    {
        public const int CapFlag = 64;
        public static float Encode(Pat pat, bool cap = false);   // (int)pat + (cap ? 64 : 0)
        public static Pat Decode(float w, out bool cap);          // round(w); codes 0/1 -> None
        public static float SetCap(float w);                      // idempotent
    }
}

// ---------------------------------------------------------------- Assets/Scripts/Levels/Arch/ArchTypes.cs (Lead A)
namespace Ion.Levels.Arch
{
    [System.Flags]
    public enum ArchFlags
    {
        None = 0,
        Collider = 1,        // static MeshCollider (merged per chunk at Bake)
        Sliceable = 2,       // Sliceable marker (cut + captured)
        Dynamic = 4,         // never merged into the room batch; BakeLocal into its own object (movers, devices)
        NoShadows = 8,
        Solid = Collider | Sliceable,
        Soft = Sliceable,    // walk-through detail: nosings, inlays, posts, plants
        Visual = 0,          // render-only. ONLY allowed under an Interactable
    }

    public enum Dir { PosX, NegX, PosZ, NegZ }          // plan directions; facing / rise / outward

    public readonly struct RectXZ
    {
        public readonly float X0, Z0, X1, Z1;           // X0 < X1, Z0 < Z1
        public RectXZ(float x0, float z0, float x1, float z1);
        public static RectXZ Centered(float cx, float cz, float width, float depth);
        public float Width { get; }
        public float Depth { get; }
        public Vector3 Center(float y);
        public RectXZ Inset(float d);
    }

    public enum OpeningKind { Door, Window, Niche, Portal, Hole }

    public struct Opening
    {
        public OpeningKind Kind;
        public float At;          // centre, metres along the wall from 'from'
        public float Width, Height, Sill, Depth;
        public bool Bracket;      // bracket surround (default true except Hole)
        public static Opening Door(float at, float width = 2f, float height = 3f);
        public static Opening Window(float at, float width = 1f, float height = 2f, float sill = 1f);
        public static Opening Niche(float at, float width = 1f, float height = 1.5f, float sill = 0.75f, float depth = 0.25f);
        public static Opening Portal(float at, float width = 3f, float height = 4f);   // corbel lintel
        public static Opening Hole(float at, float width, float height, float sill = 0f);
    }

    [System.Flags]
    public enum WallTrim { None = 0, Plinth = 1, Cornice = 2, Coping = 4, Pilasters = 8, BothSides = 16,
                           Default = Plinth | Cornice | BothSides }

    public struct ProfileBox { public float Out0, Out1, Y0, Y1; public bool UseOverride; public Surf Override; }

    public sealed class TrimProfile
    {
        public readonly ProfileBox[] Boxes;
        public readonly bool FromTop;                   // datum = top of the host
        public TrimProfile(bool fromTop, params ProfileBox[] boxes);
        public static readonly TrimProfile Plinth, Cornice, Coping, Kerb, Sill, ShadowGap;   // §2.2 values
    }

    /// <summary>Per-zone surface choices. Arch methods use Arch.Style when no Surf is passed.</summary>
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
        public static ArchStyle LightTable { get; }    // T1, Hub, Gallery
        public static ArchStyle Darkroom { get; }      // T2: Plaster/Ashlar walls, Cyanotype accents
        public static ArchStyle Mint { get; }          // Stairs wing
        public static ArchStyle Rose { get; }          // Camera wing
    }

    /// <summary>Build-time tag on every Arch child. Read by Bake.</summary>
    [DisallowMultipleComponent]
    public sealed class ArchPiece : MonoBehaviour { public Surf Surf; public ArchFlags Flags; }

    public struct StairResult { public GameObject Root; public Vector3 TopLanding; public int Steps; }
}

// ---------------------------------------------------------------- Assets/Scripts/Levels/Arch/Arch.cs (Lead A)
namespace Ion.Levels.Arch
{
    public static class Arch
    {
        public const float M = 1f, Sub = 0.25f, Detail = 0.0625f;
        public const float Storey = 4f, WallThickness = 0.5f, SlabThickness = 0.5f, HeadY = 3f;
        public const float DoorWidth = 2f, DoorHeight = 3f, Rise = 0.25f, Tread = 0.5f, RailHeight = 1f;

        /// <summary>Surfaces used when a method gets no explicit Surf. Set at the start of Room.Build.</summary>
        public static ArchStyle Style { get; set; }

        // ---- primitives (min/max in p-local space; Surf + flags) -------------------------------------
        public static GameObject Group(Transform p, string name, Vector3 localPos = default, Dir facing = Dir.PosZ);
        public static GameObject Box(Transform p, Vector3 min, Vector3 max, Surf s, ArchFlags f = ArchFlags.Solid);
        public static GameObject Wedge(Transform p, Vector3 min, Vector3 max, Dir rise, Surf s, ArchFlags f = ArchFlags.Solid);
        public static GameObject ChamferBox(Transform p, Vector3 min, Vector3 max, float chamfer, Surf s,
                                            ArchFlags f = ArchFlags.Solid);           // convex 26-face hull; props only
        public static GameObject Prism(Transform p, Vector3 baseCenter, float radius, float height, int sides,
                                       Surf s, ArchFlags f = ArchFlags.Solid, float yaw = 0f);  // vessels, poles, discs

        // ---- floors, terraces, platforms ---------------------------------------------------------------
        public static GameObject Floor(Transform p, RectXZ r, float topY = 0f, float thickness = SlabThickness,
                                       Surf? top = null, bool drip = true);
        public enum Underside { Flat, Stepped, Piers }
        public static GameObject Terrace(Transform p, RectXZ r, float topY, float depth,
                                         Underside under = Underside.Stepped, Surf? top = null);
        public static GameObject Plinth(Transform p, RectXZ r, float baseY, float height = 0.25f, Surf? s = null);
        public static GameObject Steps(Transform p, RectXZ r, float fromY, float toY, Dir up, Surf? s = null); // wide, no cheeks

        // ---- walls, parapets, screens, trims --------------------------------------------------------------
        /// <summary>Axis-aligned wall on the centre line from->to (same x or same z; from.y = base).</summary>
        public static GameObject Wall(Transform p, Vector3 from, Vector3 to, float height = Storey,
                                      float thickness = WallThickness, WallTrim trim = WallTrim.Default,
                                      params Opening[] openings);
        public static GameObject Parapet(Transform p, Vector3 from, Vector3 to, float height = RailHeight,
                                         float thickness = 0.25f);
        public static GameObject Railing(Transform p, Vector3 from, Vector3 to, float height = RailHeight);
        public enum ScreenKind { ContactSheet, Lattice }
        public static GameObject Screen(Transform p, Vector3 from, Vector3 to, float height, ScreenKind kind);
        public static GameObject Trim(Transform p, Vector3 from, Vector3 to, Dir outward, TrimProfile profile,
                                      float datumY, Surf? s = null);

        // ---- columns and piers -----------------------------------------------------------------------
        public static GameObject Column(Transform p, Vector3 basePos, float height = Storey, float shaft = 0.5f);
        public static GameObject Pier(Transform p, Vector3 basePos, Vector2 size, float height);
        public static GameObject BracketPier(Transform p, Vector3 basePos, float height, Dir open);
        public static GameObject Pilaster(Transform p, Vector3 faceBase, float height, Dir outward);
        public static GameObject Colonnade(Transform p, Vector3 from, Vector3 to, float height = Storey,
                                           float bay = 4f, bool roof = true);

        // ---- portals and arches ------------------------------------------------------------------------
        public static GameObject BracketFrame(Transform p, Vector3 sillCenter, Dir facing,
                                              float width = DoorWidth, float height = DoorHeight, float depth = 0.5f);
        public static GameObject CorbelArch(Transform p, Vector3 sillCenter, Dir facing, float width = 3f,
                                            float height = 4f, int steps = 2, float depth = WallThickness);

        // ---- stairs and ramps ------------------------------------------------------------------------
        /// <summary>Solid step columns, rise 0.25 / tread 0.5. 'rise' must be a multiple of 0.25.</summary>
        public static StairResult Stair(Transform p, Vector3 bottomCenter, Dir up, float rise,
                                        float width = 2f, bool cheeks = true);
        public static GameObject Ramp(Transform p, Vector3 bottomCenter, Dir up, float width, float run, float rise,
                                      bool cheeks = true);   // <= 30 degrees

        // ---- roofs, canopies, pergolas ----------------------------------------------------------------------
        public static GameObject Canopy(Transform p, RectXZ r, float undersideY, float thickness = 0.5f,
                                        float ribPitch = 2f, float ribDepth = 0.75f, Dir ribsAlong = Dir.PosX);
        public static GameObject Roof(Transform p, RectXZ r, float topY, float thickness = SlabThickness,
                                      float parapet = 0.75f);
        public static GameObject Pergola(Transform p, RectXZ r, float height = 3f, float beamPitch = 0.5f,
                                         Dir beamsAlong = Dir.PosX);

        // ---- skyline -------------------------------------------------------------------------------------
        public static GameObject SlabTower(Transform p, Vector3 baseCenter, Vector2 footprint, int slabs, int seed,
                                           float slabHeight = 2f);

        // ---- baking (call once per zone root and once per diorama root, after all building) ------------
        /// <summary>
        /// Merges every non-Dynamic ArchPiece (and every Decor) under root into one Sliceable per (Mat, flags,
        /// chunk) with MeshElements (one convex element per piece), a merged MeshCollider for Collider pieces,
        /// and TEXCOORD0 = (world position, PatternCode). Destroys the source children. Returns draw objects created.
        /// </summary>
        public static int Bake(Transform root, float chunk = 8f);
        /// <summary>Same merge, but UV0 in go-local space (the pattern moves with the object). Movers, devices.</summary>
        public static void BakeLocal(GameObject go);
        /// <summary>Editor/test check: off-grid min/max, open meshes, Sliceables without UV0, Visual outside Interactable.</summary>
        public static bool Validate(Transform root, System.Collections.Generic.List<string> errors);
    }
}
```

Behaviour that must hold:
- **Bake timing.** `Bake` runs after a zone (or diorama) is fully built and positioned, so the UV0 world positions are final. Zone roots and diorama roots never move after baking.
- **Chunks.** Chunking by 8 m means a photo cut re-cooks only the touched chunk's collider.
- **Draw-call budget.** Target ≤ 120 batches per visible zone with the SRP Batcher.
- **`Dynamic` pieces** are skipped by `Bake`. Their owner calls `BakeLocal`. Every moving or device object must be wrapped in an `Interactable` (§5, §11).
- **Snapping.** Snap is checked, not silently fixed: `Validate` reports any min/max that is off the 0.0625 grid (and off 0.25 for architecture).
- **Walls are axis-aligned only.** Rotated architecture is not supported.
- **Sliceable props in the batch** get world-space patterns, so their yaw must be a multiple of 90° (rule 2). Arbitrary yaw is allowed only for Soft clutter with `Pat.None`.

---

## 5. Prop family API: `Ion.Levels.Props`

Files: `Assets/Scripts/Levels/Props/**` (Lead B). Props are built only from `Arch` primitives with the shared profiles: 0.0625 chamfers, the 0.0625 coping lip and brass clips at bracket joints.

- **Sliceable props** go into the zone bake. Their facing is a `Dir`.
- **Interactable props** call `Arch.BakeLocal` on themselves and carry `Interactable`. They are never sliced and are captured or removed whole by their pivot.
- **Soft clutter** (chairs, plants) can take any float `yaw`.

```csharp
namespace Ion.Levels.Props
{
    using Arch = Ion.Levels.Arch.Arch;   // see the name-lookup trap in §4

    public enum PedestalSize { Low, Standard, Tall }                       // top at 0.5 / 1.0 / 1.25
    public enum ChairStyle { Cafe, Monobloc, Stool }
    public enum LampStyle { Pendant, Standing, Sconce, Lantern, Bollard }
    public enum PlanterStyle { Trough, Square, Bowl }
    public enum PlantKind { Cypress, Palm, Monstera, Strelitzia, Grass, Ivy, Shrub }
    public enum FrameStyle { SlideMount, Gallery, Polaroid }
    public enum ButtonStyle { Pedestal, Wall }
    public enum ExhibitState { ComingSoon, Locked, Available, Solved }

    public sealed class ExhibitSpec
    {
        public string Number;            // "02" -> plaque "[02]"
        public string Title;             // "Stairs"
        public Ion.Levels.DioramaShot Key;  // the wing's key photo; null = coming soon
        public ExhibitState State = ExhibitState.Available;
    }

    /// <summary>Exhibit state view (lamp, print colour, plaque tick). State is set by the hub logic (Lead C).</summary>
    public sealed class ExhibitStand : MonoBehaviour { public ExhibitState State { get; set; } public event System.Action<ExhibitState> Changed; }

    public static class PropKit
    {
        public const float Chamfer = 0.0625f;

        // ---- Sliceable (baked with the zone) -------------------------------------------------------------
        public static GameObject Pedestal(Transform p, Vector3 basePos, Dir facing, PedestalSize size = PedestalSize.Standard);
        public static GameObject Bench(Transform p, Vector3 basePos, Dir facing, float length = 2f);
        public static GameObject Table(Transform p, Vector3 basePos, Dir facing, Vector2 top = default /*1.0×0.75*/, float height = 0.75f);
        public static GameObject Planter(Transform p, Vector3 basePos, Dir facing, PlanterStyle style = PlanterStyle.Trough,
                                         Vector2 size = default /*1.5×0.5*/, float height = 0.625f, int seed = 0);
        public static GameObject Easel(Transform p, Vector3 basePos, Dir facing, Texture2D image = null);
        public static GameObject PictureFrame(Transform p, Vector3 center, Dir facing, Vector2 imageSize, Texture2D image,
                                              FrameStyle style = FrameStyle.SlideMount);
        public static GameObject Plaque(Transform p, Vector3 center, Dir facing, string text);
        public static GameObject StandingMarker(Transform p, Vector3 feet, float yaw);       // brass [ ] inlay (Soft)
        public static GameObject Rug(Transform p, RectXZ r, int seed = 0);                     // Soft, kilim stripes via boxes

        // ---- Soft clutter (any yaw) --------------------------------------------------------------------------
        public static GameObject Chair(Transform p, Vector3 basePos, float yaw, ChairStyle style = ChairStyle.Cafe, Mat color = Mat.Teal);
        public static GameObject Lamp(Transform p, Vector3 pos, float yaw, LampStyle style);
        public static GameObject Plant(Transform p, Vector3 basePos, PlantKind kind, float scale = 1f, float yaw = 0f, int seed = 0);

        // ---- Interactable devices (never sliced; BakeLocal; ion = usable) -----------------------------------
        public static Ion.Gameplay.Teleporter Teleporter(Transform p, Vector3 basePos, Dir facing, System.Action onEnter,
                                                         Mat tint = Mat.Graphite);
        public static Ion.Gameplay.PhotoPickup PhotoDisplay(Transform p, Vector3 basePos, Dir facing, Ion.Levels.DioramaShot shot);
        public static Ion.Gameplay.PhotoPickup PhotoDisplay(Transform p, Vector3 basePos, Dir facing, Ion.Projection.PhotoData photo);
        public static Ion.Gameplay.CameraPickup CameraStand(Transform p, Vector3 basePos, Dir facing, int film = 3);
        public static ExhibitStand Exhibit(Transform p, Vector3 basePos, Dir facing, ExhibitSpec spec);
        public static Ion.Gameplay.Switch Button(Transform p, Vector3 basePos, Dir facing, string channel,
                                                 ButtonStyle style = ButtonStyle.Pedestal, bool powered = true);
        public static Ion.Gameplay.Switch Lever(Transform p, Vector3 basePos, Dir facing, string channel);
        public static Ion.Gameplay.Mover SlidingBridge(Transform p, Vector3 stowedMin, Vector3 stowedMax, Vector3 travel,
                                                       string channel);           // Oak deck, Graphite kerbs
        public static Ion.Gameplay.Mover Gate(Transform p, Vector3 sillCenter, Dir facing, float width, float height,
                                              string channel, bool openWhenOn = true); // Paper slab sinks into a floor slot
        public static Ion.Gameplay.StoryCollapse CollapseSlab(Transform p, Vector3 min, Vector3 max, Surf s);
        public static Ion.Gameplay.CheckpointMarker Checkpoint(Transform p, Vector3 feet, float yaw, string id); // trigger + brass inlay
    }
}
```

### Design and dimensions

Unless noted, all props use 0.0625 chamfers on outward edges and brass rivets 0.0625³ at bracket joints.

| Prop | Kind | Description and dimensions |
|---|---|---|
| Pedestal | Sliceable | Bracket-pier body. Base plinth 0.75² × 0.125 (Limestone). Shaft 0.5² (Paper/Formwork). Cap 0.625² × 0.0625 (Limestone). Two brass `[` clips on the cap edge. Top at 0.5 / 1.0 / 1.25. |
| Bench | Sliceable | Bracket in section: two Concrete end blocks 0.375 w × 0.4 h × 0.4 d (the returns), a Walnut seat 0.4 d × 0.0625 at 0.45 (Boards gives the slats). The back is optional: a 0.0625 Walnut board on two Graphite 0.0625 posts. |
| Table | Sliceable | Walnut top 0.0625 thick on two Graphite U-frames (3 boxes each, 0.0625 sections). 0.75 high. |
| Planter | Sliceable | Trough: Concrete box, walls 0.125, a 0.0625 coping lip overhanging 0.0625, Lawn soil top inset 0.125. Square: 0.75². Bowl: a 12-gon Terracotta prism 0.8 Ø × 0.5. Always filled with ≥ 3 plants (seeded). |
| Plant | Soft | Low-poly and seeded. Merged by `DecorCombiner` with sway weight in vertex alpha. Cypress: stacked 6-gon cones, 2.5–4 m. Palm: a 0.15 6-gon trunk with 7 flat 2-tri fronds. Monstera: 5–7 split-leaf quads tilted 30–60°. Strelitzia: upright blade leaves. Grass: 6–10 thin wedges. Ivy: a curtain of 0.25 leaf quads down a wall face. Shrub: 3 merged 8-gon puffs. |
| Easel | Sliceable | Oak A-frame of 3 legs (0.0625² boxes), a 0.0625 ledge at 0.9, holding a print 0.8 × 0.6 (PhotoDisplay shader quad: render-only, child of the easel). |
| Picture frame | Sliceable | SlideMount: border 0.125 Paper/Sprocket, brass L crop clips 0.125 × 0.0625 at the 4 corners. Gallery: Walnut 0.0625 frame with a 0.125 Plaster mat. Polaroid: Paper, 0.06 sides, 0.2 bottom. Image quad inset 0.005. |
| Plaque | Sliceable | Brass 0.5 × 0.25 × 0.03 on a 0.0625 Graphite back. WorldText in title-block style: `[02]  STAIRS`. |
| Standing marker | Soft | §2.2. Brass, 0.01 proud. **One at every `RoomSolution` with kind `Place` or `Snap`**, at its `LocalFeet`, yaw = solution yaw. |
| Rug | Soft | 0.01 high. Kilim bands as 3–5 boxes in TextileRed / Mustard / Paper. |
| Chair | Soft | Café: Teal seat 0.45² at 0.45, Graphite legs 0.04², back 0.4 × 0.35. Monobloc: chunkier, 1 colour. Stool: 0.35² seat at 0.65. |
| Lamp | Soft | Pendant: Graphite cord 0.02, a 6-gon Warm bulb 0.12. Standing: a 1.6 Graphite pole + a 0.25 Paper shade box. Sconce: a bracket-shaped Graphite mount + a Warm box (Safelight in T2). Lantern: a 0.25 Graphite cage + Warm core. Bollard: a 0.25² × 0.75 Concrete post with a Warm slot. |
| Teleporter | **Interactable** | Floor disc: 8-gon prism 1.5 Ø × 0.25 with two brass footprint plates. Pod back: 1.0 w × 2.25 h × 0.5 d in the tint colour, with a square "porthole" screen 0.5 × 0.375 in **Ion** framed by a Graphite bracket surround. Two side rails shaped as `[ ]`, 3 boxes each, 0.0625. Existing `Teleporter` + `TeleporterFx`. |
| Photo display | **Interactable** (the pickup) | Pedestal (Standard, sliceable) + a slide-mount stand holding the print tilted 15°, with an Ion edge glow while available. The `PhotoPickup` is the print object. |
| Camera stand | **Interactable** | Lectern: Pedestal + a 15° wedge top. The instant camera is Mustard, 0.25 × 0.15 × 0.18, with a 12-gon Graphite lens and an Ion shutter button. |
| Exhibit stand | Sliceable body + **Interactable** pickup | Bracket pier base 1.0 × 0.5. A slide-mount frame 1.75 × 1.375 outer, image 1.6 × 1.2 (4:3), top at 2.5. Plaque at 0.9. State lamp: a 0.125 cube at the top-right that is Graphite (Locked/ComingSoon), Ion (Available) or Brass glow (Solved). Coming soon: a Frost slide with a `[ ]` plaque. |
| Button | **Interactable** | Standard pedestal + a cap: a 0.25² × 0.0625 button in a Graphite bracket bezel. **Ion when powered and off, Brass when on, Graphite when unpowered**. Travel 0.03. |
| Lever | **Interactable** | TextileRed base 0.5 × 0.375 × 0.25, a Graphite handle 0.05² × 0.5 with a brass knob. Rotates ±35°. |
| Sliding bridge | **Interactable** (Mover) | Oak deck 0.25 thick with Graphite kerbs 0.125. Stows inside a terrace body and slides along `travel`. |
| Gate | **Interactable** (Mover) | A Paper/Formwork slab 0.25 thick that sinks into a floor slot (`height` down) when open, framed by a bracket surround on the fixed wall (sliceable). |
| Collapse slab | **Interactable** (StoryCollapse) | Looks like the deck or floor it continues (same `Surf`). Wobbles, then drops (§9). One-way story event. |
| Checkpoint marker | **Not** Interactable, not Sliceable | A trigger volume (`CheckpointMarker`, distance test, no physics) + a sliceable brass inlay: a `[ ]` with a 0.125 Ion dot that pulses once when the checkpoint is set. |

---

## 6. Shader and material contract

### 6.1 Vertex layout for every mesh drawn with `Ion/FlatToon`

| Attribute | Type | Meaning | Who writes it |
|---|---|---|---|
| `POSITION` | float3 | object space | everyone |
| `NORMAL` | float3 | flat (split) normals | everyone |
| `COLOR` | Color32 | rgb = per-instance tint × contact AO (white = none). **a = sway weight** (decor) | Geo meshes (white), DecorCombiner, Arch.Bake |
| **`TEXCOORD0`** | **float4** | **xyz = pattern-space position in metres. w = `PatternCode` (pattern id + 64 if a cut face)** | `Arch.Bake` / `BakeLocal`, `DecorCombiner`, Geo shared meshes (0,0,0,0), MeshClipper, PieceMerger |

Pattern space is defined as follows:
- For baked architecture it is the vertex **world position at bake time**.
- For `BakeLocal` objects it is **object-local**.
- It is never recomputed after that. It is data that travels with the vertex through every cut, merge, capture and paste.

### 6.2 MeshClipper and merger rules (Lead A, additive; projection semantics unchanged)

1. **Load.** `MeshData` gains `List<Vector4> U` and `bool HasUV`. On load, `mesh.GetUVs(0, U)`, and `HasUV = U.Count == P.Count`. `LoadRange` takes an optional `Vector4[] uvs`.
2. **Interpolate.** On an edge split: `U = Vector4.Lerp(U[a], U[b], t)`. This is exact, because UV0.xyz is affine in position within a flat face. Kept vertices copy `U`.
3. **Caps.**
   - Each cap vertex copies `U` from the boundary vertex it duplicates, with `w = PatternCode.SetCap(w)`.
   - The fan centroid takes the **mean** of the loop's `U`, which is exact because the centroid is the mean of the positions.
   - The angle-sort fallback carries `U` beside each welded point; a weld keeps the first point's `U`.
4. **Output.** `ToMesh` calls `SetUVs(0, U)` when `HasUV`. `IPieceSink.AddPiece(...)` gains a `List<Vector4> uvs` parameter (null when absent).
5. **MeshElements** gains `internal Vector4[] Uvs` (`SetData` overload; `EnsureData` reads `GetUVs(0)`).
6. **PieceMerger, `MergePhotoPieces`, `SpawnMerged`, `CutByElements` and `DecorCombiner`** copy UV0 **unchanged**. Positions get transformed; UV0 never does.
7. **Edge cases.**
   - Meshes without UV0 stay without it, as they behave today.
   - Mixing meshes with and without UV0 in one merge fills the missing ones with `(0,0,0,0)` (None).
8. **New EditMode tests** (Lead A):
   - `Clipper_UV0InterpolatesAffinely`: for random cuts, every output vertex satisfies `U.xyz == M·pos` within 1e-4.
   - `Clipper_CapsFlagged`.
   - `Clipper_CutTwice_FlagsAndUVsStable`.
   - `Merger_KeepsUV0`.
   - `MissingUV0_StaysAbsent`.

### 6.3 `Ion/FlatToon` additions (Lead A)

- Lighting is untouched.
- **Pattern code:** the new include `Assets/Shaders/IonPattern.hlsl` holds all pattern code.
- **Interpolators:** add `float3 patternPos : TEXCOORD5` and `float patternCode : TEXCOORD6`. Use plain interpolation, not `nointerpolation`: the code is constant per face. `round()` it in the fragment.
- **Precision:** pattern math must use `float`, not `half`, because diorama positions reach about 1000 m.
- **Face role:** `float3 nP = cross(ddx(patternPos), ddy(patternPos))`. The role is the largest of `|nP.x|, |nP.y|, |nP.z|`, and its sign is ignored. A Y-dominant face uses the *top* treatment, or the *bottom* treatment when the world normal points down (`normalWS.y < −0.7`).
- **Branching:** branch on the decoded id with `[branch] switch`. Each pattern costs ≤ 30 ALU, with no textures and no UVs beyond TEXCOORD0.
- **Per-material properties (`UnityPerMaterial`, SRP-batcher-compatible):** `_PatternStrength` (Range 0–1.5, default 1).
- **Globals** (set by `Atmosphere` / `AdaptiveQuality`):

  | Global | Meaning |
  |---|---|
  | `_IonPatternFade` | float4: x = start, y = end |
  | `_IonPatternOn` | 0/1 |
  | `_IonCutHatch` | 0–1 |
  | `_IonShadowTint` | color; the zone shadow tint |
  | `_IonShadowTintMix` | 0–1; how much the global replaces the material's `_ShadowTint`. Default 1 |
- **Passes:** ShadowCaster and DepthOnly ignore patterns, and must still compile with TEXCOORD0 present.
- **Fallback:** if `_IonPatternOn == 0`, or the code is 0/1, output is bit-identical to today.

### 6.3.1 Graphics tiers and the Ultra tier

Settings offer **Low / Med / High / Ultra / Auto** (`QualityTier`: 0–3, −1 Auto). Auto starts at High, steps down above
22/20 ms, and steps up to **Ultra only when the sustained frame time stays under 8 ms for 20 s**; it leaves Ultra above
13 ms and does not return in that session. Everything Ultra adds is WebGL2-safe and degrades to "off":

| Ultra feature | How | Fallback |
|---|---|---|
| Render scale | 1.0 of a canvas already at min(devicePixelRatio, 1.5) (template) | dynamic scale as on other tiers |
| Sun shadows | 4096 map, 2 cascades (split 40 %), 48 m, soft High | 2048 map on devices without 8k textures |
| Local lights | every lamp / lantern / sconce / teleporter carries a point light (`LocalLights`); the 8 nearest within 26 m are on, per-pixel, no shadows; FlatToon adds them as a soft wrapped term | off on every other tier (no keyword, no cost) |
| Ambient | softer shade (more sky ambient) plus a warm ground bounce on side / down faces (`_IonUltraFx.z`) | — |
| Ambient occlusion | the baked contact shade at 45 % (cheap substitute). URP SSAO was measured and rejected on WebGL2: its depth prepass doubled the draw calls and the downsampled 4-sample result was noisy on flat toon faces | — |
| Bloom | post-processing on the player camera only, Bloom only (threshold 1.3, HDR on; URP's soft knee starts at half of it); Ion / Warm / Safelight self-lit colours and the sun disc are pushed past 1 (`_GlowBoost`, `_IonUltraFx.w`) so white walls never bloom | no half-float targets: no bloom |
| Sun glow / fog | sun-glow billboard ×1.35; fog in-scatters towards the sun (`_IonUltraFxAtmo.y`) | — |
| Detail | `Arch.Ultra()` scopes make Soft, sliceable pieces in each role's Ultra twin material (`Palette.GetUltra`, `_UltraOnly = 1`): string-course drips, pilaster capitals, column astragals, parapet drips and skirtings, screen mullions, railing collars, three more plants per planter. Merged per material by the zone bake; culled (no draw call) and collapsed in the vertex shader off Ultra | — |
| Photos / particles | 1280-wide previews, 320 motes, 34 clouds | 768 / tier counts |

There are no outlines on any tier (rule 7).

### 6.4 Palette and Atmosphere (Lead A)

```csharp
namespace Ion.Presentation
{
    public static partial class Palette
    {
        public static Material Get(Mat mat);                 // shared, cached; emissive for Frost/Ion/Safelight/Warm; _GroundVar for Lawn
        public static Color ColorOf(Mat mat);
        public static bool TryGetMat(Material material, out Mat mat);   // footsteps, debug, tests
        // existing: Get(Color), GetEmissive(Color, float), GetContact(Color), Hex(string), ToonShader
    }

    public struct ZoneMood
    {
        public Color SkyTop, SkyHorizon, SkyBottom, SunColor, ShadowTint, AmbientSky, AmbientEquator, AmbientGround;
        public Vector3 ToSun; public float SunIntensity, FogDensity, FogStart;
        public static ZoneMood Dawn, Darkroom, Noon, Mint, Rose, Golden;     // §3.4
    }

    public static partial class Atmosphere
    {
        public static void ApplyMood(ZoneMood mood);                       // immediate (captures, tests)
        public static void BlendTo(ZoneMood mood, float seconds = 2f);      // play time
        public static ZoneMood Current { get; }
    }
}
```

---

## 7. Level structure

Zones sit on the existing X line at `i × GameBootstrap.RoomSpacing` (50 m). Every photo placement aims along ±Z. The dioramas sit at `(i × 200, −1000, 0)`.

| Index | Zone (class, namespace `Ion.Levels.Rooms`) | Mood | Purpose |
|---|---|---|---|
| 0 | `TutorialLedge` (T1) | Dawn | Rewind tutorial steps 1–4 |
| 1 | `TutorialDarkroom` (T2) | Darkroom | Step 5 (double-R) + rotate + cut |
| 2 | `HubLightTable` | Noon | The contact-sheet hub |
| 3 | `StairsWing` | Mint | The existing Stairs puzzle, rebuilt with the kits |
| 4 | `CameraWing` | Rose | The existing Camera puzzle, rebuilt with the kits |
| 5 | `GalleryEnding` | Golden | Ending and end card |

`BridgeRoom` and `DoorwayRoom` are retired: their lessons now live in T1 and T2. Lead C deletes them in the same change that updates their tests.

### 7.1 T1 "Ledge": tutorial steps 1–4

Footprint: x[−18, 4] z[0, 24].

- **Court:** Floor x[−4, 4] z[0, 16], y = 0, Limestone/Tile, with Parapets on the east side.
  - Spawn at (0, 0, 1.5), facing +Z.
  - A bench and planters by the spawn give scale.
- **Step 1, unreachable goal:** the Ledge terrace x[−4, 4] z[16, 24], top y = 3.0.
  - Its face at z = 16 is a sheer Formwork wall (3.0 ≥ 1.5 barrier).
  - On top: a BracketFrame around the exit Teleporter at (0, 3, 21.5), which goes to T2.
- **Step 2, forced fall:**
  - **Bridge:** a plank bridge (Oak/Boards, Railing on one side only) runs west from the court edge, x[−11, −4] z[11, 13].
  - **Island E:** x[−17, −11] z[8, 16], holding the photo display.
  - **Collapse:** the bridge's middle x[−9.5, −6] is a `CollapseSlab`. It wobbles 0.35 s after the player steps on it, then drops.
  - **The fall:** the player falls into the void. The fall state starts and a limbo prompt appears: `[R] rewind`.
  - **R:** recovers the player (pose only) to the last safe pose on the bridge head (about x = −4.5). The gap is now 3.5 m and can't be jumped.
- **Step 3, button:**
  - A `Button` (Pedestal) on the court at (−3, 0, 9), facing −X, channel `t1.span`, **unpowered** until the event `FallRecovered` fires in T1. Its pedestal rises 0.25 and lights Ion (0.6 s). A hint zone shows `[E] press`.
  - It drives `SlidingBridge`, stowed in island E's body and travelling +X 3.5 m to fill x[−9.5, −6].
- **Step 4, photo:**
  - **Pickup:** island E holds a `PhotoDisplay` at (−14, 0, 12) with photo `t1.stair`: the ledge with a 12-step stair (rise 3.0, run 6.0, from z = 10 to z = 16), shot upright.
  - **Standing marker:** at (0, 0, 6.5), yaw 0, pitch about −8° (Lead C tunes and proves it).
  - **Prompts:** `[SHIFT] hold up photo` → `[LMB] place`.
  - **Result:** the stairs paste and the player climbs to the teleporter.
  - **Diorama rule:** the T1 diorama is built by the same `BuildLedge(root, withStair: true)` function and **includes a teleporter copy** (shared `OnEnter`), because the exit teleporter lies inside the placement frustum.

### 7.2 T2 "Darkroom": step 5, rotate and cut

Footprint: x[−6, 6] z[0, 30]. The style is Darkroom: Plaster/Ashlar, Cyanotype panels, Safelight sconces, and a roofed corridor with Coffer ceilings.

- **Entry:** terrace z[0, 6]. Arrival at (0, 0, 1.5). This is a **checkpoint** (zone entry) and the brass checkpoint inlay pulses.
- **Step 5, double rewind:**
  - **Corridor:** x[−1.75, 1.75] z[6, 14].
  - **Trap:** the floor x[−1.75, 0.75] z[9, 12] is a `CollapseSlab` (trap variant) over a 2.5 m deep shaft.
  - **The trap:** the player drops and lands **grounded** in a sealed pit. This is not a "fall": the drop is 2.5 < 3.0.
  - **Single R:** history since the checkpoint is empty, so it gives the "nothing to rewind" feedback and the hint `R R — back to the checkpoint`.
  - **Double R:** returns the player to the entry.
  - **Afterwards:** the trap stays open (a one-way story event). The 1.0 m ledge x[0.75, 1.75] along the east wall is now the obvious way round.
- **Rotate and cut:**
  - **Room:** the projection room z[14, 20] x[−6, 6]. A solid wall z[20, 20.5] spans the full width, 4.0 high.
  - **Photo:** the `PhotoDisplay` (on an Easel) at (−4, 0, 16) holds photo `t2.door`: the same wall with a bracket doorway (2.0 × 3.0), **shot rolled 90°**.
  - **Standing marker:** at (0, 0, 15), yaw 0. Solution: Roll +90 (one Q).
  - **Wrong placement:** a sideways doorway can't be walked through, which invites a single R. This is organic reinforcement.
  - **Exit:** beyond the wall, x[−6, 6] z[20.5, 30], with the exit Teleporter at (0, 0, 26) going to the hub W cell. The diorama **includes a teleporter copy** for the same frustum reason as in T1.

### 7.3 Hub "Light Table" (zone 2)

A floating plinth x[−11.5, 11.5] z[−11.5, 11.5].
- **Planar stepped underside:** 3 m.
- **Parapets:** on the E and W edges.
- **Floor:** a **3 × 3 grid of 6 × 6 m cells** (Limestone/Tile, the contact-sheet floor).
- **Cells:** at x, z ∈ {[−10, −4], [−3, 3], [4, 10]}.
- **Bands:** 1 m walkways between and around the cells, Limestone/Terrazzo with brass dividers.

| Cell | Content |
|---|---|
| Centre (0, 0) | **The Light Table.** A table 4.0 × 2.0, top at 0.875, with a Frost/Frost top, Oak/Boards apron and Sprocket rim. On it, **every photo of the game as an 0.8 × 0.6 print in a 4 × 2 contact sheet**: T1 stair, T2 door, the Stairs key, the Camera key, and 4 coming-soon blanks. Prints show their preview when known, become Frost blanks when unknown, and get an Ion border when solved. Over it, a Canopy 7 × 7 at underside 3.5 on two bracket piers, with pendant lamps. |
| W (−7, 0) | **Arrival** from T2: Teleporter pod + CheckpointMarker `hub.arrive`, benches, planters and a rug. The hub plaque `[project]ion` sits here. This is the only literal title in the world. |
| E (7, 0) | **Ending pod.** Hidden under a floor hatch until the Stairs and Camera wings are both Solved. It then rises (1.6 s), and its Teleporter goes to the Gallery. |
| N (0, 7) | **Exhibit `[02] Stairs`** (see "Exhibit cells" below). It faces +Z, the marker sits at (0, 0, 7), and the screen sits at the north edge. |
| S (0, −7) | **Exhibit `[03] Camera`**, the mirror of N. It faces −Z, the marker sits at (0, 0, −7), and the screen sits at the south edge. |
| NW, NE, SW, SE | **Coming soon** `[04]`–`[07]`: exhibit stands with Frost slides, plaque `[ coming soon ]`, and a screen with a faint Frost projection rectangle at 15%. They are reserved slots for future portfolio projects. Each later becomes an exhibit cell identical to N and S, so a wing can be added without changing the hub. |

**Exhibit cells:**
- **Layout:** the Exhibit stand at (0, 0, ±5) faces the hub centre, with the key `PhotoDisplay` ledge in front of it. The standing marker is at (0, 0, ±7) facing outward.
- **Screen:** at the outer band, |z| ∈ [10.5, 11], a freestanding 5 × 4 Paper wall with a bracket surround.
- **Key photo:** shows the same screen opened into a bracket niche (2.5 × 3.0, Terracotta/Herringbone floor) holding the wing's **Teleporter**, with an alcove floor reaching |z| = 13 beyond the hub edge.
- **Placing it:** pastes a working teleporter. The photo copy shares `OnEnter`, which travels to the wing.
- **Returning:** the wing's exit teleporter returns the player to the exhibit cell's marker, facing the centre. It sets the exhibit to Solved (lamp Brass, print border Ion), and the hub entry is a checkpoint.

**Frustum safety:**
- Placements from N and S markers aim at ±Z.
- The cone reaches |x| = 50 (the neighbouring zones) only beyond |z| ≈ 85, and no zone extends past z = 40.
- The test `Solutions_FrustaTouchOnlyTheirZone` enforces this for every room.

### 7.4 Wings and ending

- **Stairs wing** (zone 3, Mint):
  - The existing puzzle: a ledge 4.0 m high and a staircase photo shot sideways, so roll is required. Same `Room.Solutions` semantics.
  - Rebuilt with Arch + PropKit, plus one Button-driven Gate beat at the start (channel `stairs.gate`), so switches recur after the tutorial.
  - The exit Teleporter returns to hub N.
- **Camera wing** (zone 4, Rose):
  - The existing puzzle: unlock the instant camera (3 film), photograph a ramp from a side terrace, and paste it at the cliff.
  - Rebuilt with the kits. The exit returns to hub S.
- **Gallery ending** (zone 5, Golden):
  - A colonnade gallery 8 × 24 (Columns at 4 m, Roof, Limestone/Checker floor).
  - Every photo hangs as a Gallery-framed print, with coming-soon frames as `[ ]` blanks.
  - At the far end a final Teleporter opens the `EndCard`.

### 7.5 How solvability is preserved (binding, Lead C)

1. **Capture poses use `ctx.EyeHeight`**, which is `PlayerFactory.EyeHeight` = **1.62**. Never use the 1.6 constant. The diorama `LocalFeet` / Yaw / Pitch / Roll match the matching `RoomSolution`.
2. **Every `Place` and `Snap` solution has a `PropKit.StandingMarker`** at its `LocalFeet` and yaw. The test `Harness_EveryMarkerHasASolution` checks the 1:1 match.
3. **A diorama is built by the same builder function as its zone**, plus the solution delta. It includes copies of any Interactable that lies inside the placement frustum (teleporters), so a placement never deletes the way forward.
4. **`RoomSolution.Kind` gains these kinds**, while keeping `Place`, `Snap` and `Goal`:
   - `Walk`: walk to `LocalFeet`; it may trigger a story event.
   - `Press`: press the Switch nearest `LocalFeet` in view.
   - `Pickup`: collect the pickup nearest `LocalFeet`.
   - `Rewind`: single R. `Expect` is filled in.
   - `RewindToCheckpoint`.
   - `Teleport`: enter the teleporter at `LocalFeet`.

   Add `public RewindExpect Expect` with values `Undid`, `RecoveredFall`, `Nothing` and `ToCheckpoint`. **`Room.Solutions` lists the full ordered sequence**, so the T1 and T2 lists *are* the tutorial script.
5. **IonDebug** gains `press`, `rewind`, `rewind2`, `checkpoint`, `walk <x,y,z>` and `zone <name>`, all routed through the same gameplay APIs as the player.
6. **Forgiveness:** every Place solution still works at ±0.3 m and ±3° (the existing `Forgiveness_*` test, extended to the new rooms).

---

## 8. Audio direction and the chosen approach

**Direction.** A warm lo-fi chamber sound in **D lydian**, where the raised 4th (G#) is the colour note shared by the music and every musical sound effect.
- Instruments: soft pads, felt piano, kalimba.
- Texture: tape hiss at −52 dB, sparse crackle, a slow 1.6 ms tape wobble.
- Space: a long 3.6 s room reverb.
- Mechanical sounds (shutter, tape, switches) are tactile and close. Place, rewind and pickup land musically in key.

**Chosen approach (binding, Lead E): hybrid offline rendering.**
- **Music** is rendered offline by the committed generator `tools/audio/ionmusic.py`:
  - `rooms`: 150 s at 64 BPM, Dmaj9 → E/D → Bm11 → Gmaj7#11.
  - `hub`: 53 s at 72 BPM, brighter, with kalimba.
  - Both are seamless exact loops at about −20 LUFS, and the room loop gets a brighter mix: raise the 1700/2600 Hz pad and piano filter numbers until the energy above 2 kHz is ≥ 1%.
  - Ship the **exact-loop** files only and loop them with `AudioSource.loop` (sample-accurate in Web Audio). The `_xf` variants are not shipped.
  - Format: 32 kHz stereo.
  - Only the current zone's loop is loaded; the other is unloaded (`UnloadAudioData`). This caps decoded Web Audio memory at about 40 MB.
- **SFX** are rendered offline by `tools/audio/ionsfx.py`, mono, 44.1 kHz, with reverb baked in:
  - photo_raise, photo_lower, photo_place, photo_rewind
  - camera_shutter, photo_pickup
  - teleporter_hum_loop, teleport_travel
  - ui_hover, ui_click
  - amb_wind_loop, amb_bird_0..3
  - **new for this redesign:** switch_press, switch_release, mover_loop (seamless), mover_stop, collapse_crack, collapse_fall, fall_whoosh, limbo_drone_loop, rewind_nothing (soft muted tick + falling minor second), rewind_checkpoint (0.9 s long tape rewind + low D bell), checkpoint_set (kalimba D–A), exhibit_wake (G# glass shimmer), hatch_rise.
  - **rewind glide:** rewind_tape_loop (an exact 1 s loop of tape chattering past the heads; IonAudio drives its pitch / playback rate 0.55 → 1.55 → 0.55 and its level from the glide's speed) and rewind_settle (the tape stops with a soft clunk, D5 + A4 kalimba and F#4 felt piano).
- **Footsteps** come from **Kenney Impact Sounds (CC0)**: concrete, grass and wood, 5 variants each.
  - They are converted to mono, trimmed and filtered by `ionsfx.py`.
  - The "tile" set is the concrete set high-passed.
  - The surface is picked from the ground renderer's `Mat` via `Palette.TryGetMat`:

    | Ground `Mat` | Footstep set |
    |---|---|
    | Limestone, Paper, Plaster, Concrete, Terracotta, Rose, Mint | stone / tile |
    | Oak, Walnut | wood |
    | Lawn, Foliage* | grass |

  - Credits go in `Assets/Resources/Audio/CREDITS.txt`.
- **Location.** Clips live in `Assets/Resources/Audio/{Music,Sfx}/` and load via `Resources.Load`: there are no scenes or prefabs to reference them. Budget ≤ 3.5 MB of audio in the build.
- **Runtime.** `Ion.Presentation.Audio.IonAudio` plays the clips. The existing `ProceduralAudio` public API stays as the facade (MasterVolume and mute prefs, used by the settings panel), and settings gain Music and SFX sliders. `AudioSynth` remains only as a silent-safe fallback when a clip is missing (with one warning).
- **Mix:**
  - Music at −20 LUFS. SFX peaks at −6 dBFS.
  - The music ducks −4 dB for 0.6 s on place, rewind and teleport.
  - Rewind applies a music tape-dip: pitch 1 → 0.92 → 1 over 0.6 s (checkpoint: 0.85 over 1.0 s; an undo glide: 0.9 over the glide).
  - Limbo low-passes the music to 600 Hz over 0.8 s.
  - Zone change crossfades over 2.5 s with equal power. Tutorial, hub and ending use `hub`; the wings use `rooms`.
- **Event wiring.** Audio subscribes to gameplay events (`ProjectionSystem.Placed`, `WorldHistory.Rewound`, `CheckpointReached`, `Switch.Changed`, `InstantCamera.Captured`, `PhotoInventory.Changed`, teleports). Gameplay code calls only `IonAudio.Play(Sfx id, Vector3? at = null)`. Nobody else creates AudioSources.

---

## 9. Feel: animation, input and pointer lock (Lead D)

All values live in one static class, `Ion.Presentation.Motion.Feel`, so tuning happens in one place. Springs are semi-implicit damped springs `Spring.Step(ref x, ref v, target, freqHz, zeta, dt)`. Durations below are the time to settle within 2%.

### 9.1 Motion table

| Motion | Duration | Curve / spring | Notes |
|---|---|---|---|
| Photo raise | ≈ 0.34 s | position/scale spring f = 2.6 Hz, ζ = 0.82 (≈ 2% overshoot); tilt −7° → 0 at ζ = 0.9 | Interruptible both ways. Place is enabled at ≥ 85% progress. |
| Photo lower | 0.26 s | easeInOutCubic | Exits faster than it enters. |
| Held-photo sway | continuous | lag spring f = 1.8 Hz, ζ = 0.75 on mouse delta, max 12 px | |
| Rotate Q/E | continuous | Hold: 60°/s, velocity eases in (τ 0.14 s) and out on release (τ 0.07 s, ≈ 4° coast). Tap: at least 3° with the same easing. Within 2.5° of a multiple of 90°, an eased magnetic settle (τ 0.12 s) after release | No 90° snapping. The overlay draws the exact logical roll (`PhotoHolder.RollDegrees`) and placement uses the same number. Audio: a soft detent every 15°, a firmer one on landing square. |
| Place | 0.70 s | 0–0.10 s: press-in, scale 1 → 0.985, easeOutQuad. At 0.10 s: world swap. Frost flash 30% → 0 over 0.35 s, easeOutQuart. Photo card scales 1 → 1.08 and fades over 0.30 s, easeOutQuart. Pasted pieces "develop" from a warm desaturated tone over 0.6 s (`FreshPulse`) | FOV +2.5° kick, spring back f = 2 Hz, ζ = 0.8. |
| Rewind (single, undo) | 0.80 s + 0.04 s/m, ≤ 1.60 s | **Glide** back to the change's recorded pose: position on smootherstep (slow start, fast middle, gentle settle), look slerped on the same curve, a slight upward arc (≤ 1.8 m) when the straight line would pass through geometry. CharacterController off, no gravity, no input, velocity zeroed at the end. Pasted pieces un-develop (reverse `FreshPulse`, warm glow 0 → 0.62, sine in-out) over 0.24 s, then the world undo at 0.24 s. Effects on the glide's speed curve (√ of the normalised smootherstep speed): world desaturation ≤ 62% toward a cool grey, two tape / scanline bands rolling with the glide's position, Cyanotype vignette ≤ 30%, tape loop pitch 0.55 → 1.55 → 0.55 with its level; `rewind_settle` as it lands. The returned photo flies to its slot on smootherstep and lands as the glide settles | A press during a glide is buffered (one) and plays when it settles; within 0.35 s it escalates to R R. If the eye must cross a surface anyway, a soft Paper veil (0.12 s) covers the crossing. |
| Rewind (fall recovery) | 0.60 s | Cyanotype wash 0 → 22% → 0 and desaturation 0 → 50% → 0, sine in-out. Fade to Paper 0.18 s easeInQuad, the move at 0.22 s, hold 0.06 s, fade in 0.30 s easeOutCubic | Pose only, no world change. |
| Rewind to checkpoint | 1.00 s | Two `[` `]` brackets close from the screen edges, 0.35 s easeInOutCubic. Swap at 0.40 s. They open in 0.45 s easeOutCubic. Wash 30% | |
| Nothing to rewind | 0.30 s | HUD `[R]` chip shakes on x, 2 cycles, 5 px, decaying (ζ = 0.3). Toast "Nothing to rewind" for 1.4 s | No camera motion. The hint `R R — back to the checkpoint` is shown the first 3 times. |
| Fall → limbo | 0.8 s | Vignette and fog in, easeOutCubic. Gravity eases to 15% | Prompt `[R] rewind`. Auto-recover after 4.0 s without input. |
| Pickup | 0.70 s | Lift 0.2 m over 0.30 s, easeOutBack (s = 1.3). Fly to the HUD slot over 0.40 s, easeInOutCubic, scale 1 → 0.4. HUD card spring f = 2.8 Hz, ζ = 0.78 | |
| Teleport | 1.10 s | Fade to Frost over 0.40 s easeInQuad, with FOV +6°. Hold 0.15 s (the move). Fade in 0.55 s easeOutCubic, FOV spring f = 1.8 Hz, ζ = 0.9 | Not to black: the light table is white. |
| Camera raise | as photo raise | viewfinder corner ticks slide in over 0.25 s, easeOutCubic | |
| Shutter | 0.45 s | Blades close 0.05 s linear, open 0.12 s easeOutQuad. Flash 25% → 0 over 0.25 s. Print ejects over 0.5 s easeOutCubic | |
| Button | — | press 0.10 s easeOutQuad (0.03 m); release spring f = 4 Hz, ζ = 0.7 | |
| Mover (gate/bridge) | 1.6 s per ≤ 4 m | easeInOutCubic, then a 0.06 s settle | During rewind: 0.35 s. During checkpoint restore: instant. It never moves into the player's capsule (it waits). |
| Collapse slab | 0.35 s warning | 2° wobble at 9 Hz, decaying, then a drop at 20 m/s² and fade below the zone | |
| Exhibit wake / solve | 0.8 s | print desaturated → colour, sine in-out; lamp spring f = 3 Hz, ζ = 0.7 | |
| Ending hatch rise | 1.6 s | easeInOutCubic | |
| Landing dip | — | spring f = 4.5 Hz, ζ = 0.65. Depth 0.03 m per m/s above 4 m/s, max 0.09 | |
| Head bob | — | amplitude 0.015 m (was 0.022), stride 2.3 | Respects the HeadBob setting. |
| UI hover | 0.12 s | easeOutQuad. Press scale 0.97 over 0.08 s | |
| Panels | 0.22 s in, 0.16 s out | In: fade + 8 px rise, easeOutCubic. Out: easeInQuad | Never animate layout. |
| Toasts | in, hold 2.4 s, out 0.25 s | In: spring f = 3 Hz, ζ = 0.85. Out: easeInQuad | |
| Prompt change | 0.15 s | crossfade | |
| Reduced motion (setting) | — | turns off sway, bob, FOV kicks and shakes; the bracket iris becomes a 0.3 s crossfade | |

### 9.2 Input (decided)

| Action | Binding |
|---|---|
| Move / look / jump | WASD / mouse / Space |
| **Raise photo or camera** | **Shift (left or right), hold**. A Settings option "Raise: Hold / Toggle" exists, default Hold. **RMB hold stays as an alternative.** It is not advertised in prompts but is listed in the controls sheet, for mouse players and for existing tests. |
| Place / shoot | LMB while raised |
| Rotate | **Hold** Q / E while raised (smooth, about 60°/s; tap to nudge). E means interact only when nothing is raised |
| Interact / press / pick up | E |
| Select photo | 1–9, wheel |
| **Rewind** | **R, anytime.** If a photo is raised, it is lowered first in the same press. |
| **Rewind to checkpoint** | **R R within 0.35 s.** The first press acts immediately; the second press escalates to a checkpoint restore. |
| Camera mode | C |
| Pause / release cursor | Esc |

- **Sprint is removed.** Shift is reassigned, rooms are small, and walk speed stays at 4.5 m/s.
- **Known risk:** Windows Sticky Keys triggers on 5 fast Shift taps. Hold-to-raise rarely does this, and RMB remains available.
- **Prompt style:** prompts render key glyphs as brackets: `[SHIFT] hold up photo`, `[R] rewind`, `[E] press`.

### 9.3 Pointer lock on load (requirement)

The template and jslib own the lock; Unity reads the state.

1. **No lock during loading.** No lock request is made before the game is ready (first frame rendered). The loader overlay swallows clicks.
2. **Lock synchronously on click.** The click on "Click to play" calls `canvas.requestPointerLock({ unadjustedMovement: true })` **synchronously inside the JS `click` handler**, through a jslib-registered listener, not on Unity's next frame. If the promise rejects with `NotSupportedError`, it retries without options.
3. **Unity follows the browser.** Unity's lock state comes from `pointerlockchange` (jslib → `SendMessage`). `Cursor.lockState` is not the source of truth on WebGL.
4. **The click is consumed.** After the lock is acquired, mouse buttons are ignored until they are released and for at least 2 frames. That click must never place, shoot or press.
5. **No camera jump.** Mouse delta is discarded for the first 2 frames after a lock. Per-frame delta is clamped to 300 px, which filters Chrome's spurious large deltas.
6. **Re-lock cooldown.** After Esc, Chrome refuses a re-lock for about 1 s. The overlay shows "Click to resume" and enables the click after 1.0 s. On `pointerlockerror`, the next click retries, and the game never loops requests.
7. **Focus and scrolling.** On lock, `canvas.focus()`. Space, arrow keys, Tab and Shift never scroll the page (`preventDefault` while the game is focused).
8. **Clean release on blur.** On `blur` / `visibilitychange`, the game releases cleanly, shows the overlay and **resets all held input** (a Shift held across an alt-tab must not leave the photo raised).
9. **Deliberate releases.** The end card and the settings panel release the lock on purpose; closing them re-locks only on a click.

Acceptance:
- A manual checklist in Chrome, Edge and Firefox on macOS and Windows: fresh load, Esc and resume within 1 s, alt-tab while holding Shift, end card.
- A PlayMode test of the input gating with simulated lock events.

---

## 10. File ownership map (5 build leads)

Each path has exactly one owner. Others may *read* it, and request changes from its owner. The **rewind and checkpoint system belongs to Lead D, "Feel & State"**, end to end.

| Lead | Mission | Owns (exclusive) |
|---|---|---|
| **A: Materials & Architecture** | Palette and patterns, zone moods, the Arch kit, UV0 plumbing through the slicer | `Assets/Shaders/**`; `Assets/Scripts/Presentation/{Palette,Surface,Atmosphere,ZoneMood,Backdrop}.cs`; `Assets/Scripts/Presentation/Ambience/**`; `Assets/Scripts/Presentation/Quality/**`; `Assets/Scripts/Levels/Arch/**`; `Assets/Scripts/Levels/{Geo,DecorCombiner}.cs`; `Assets/Scripts/Projection/{MeshClipper,MeshElements,PieceMerger}.cs` and **the UV0 merge/spawn plumbing in `ProjectionSystem.cs` (the only editor of that file this phase)**; `Assets/Tests/EditMode/**`; `Assets/Editor/**` |
| **B: PropKit** | Every prop, plant and device *visual*, in the shared language | `Assets/Scripts/Levels/Props/**`; `Assets/Scripts/Levels/{LevelProps,Kit,Scatter}.cs` (Kit and Scatter are migrated to PropKit plants, then deprecated) |
| **C: Levels & Solvability** | T1, T2, Hub, the wings and Gallery, rebuilt with the kits; zone topology; solutions; harness; tutorial and hub tests | `Assets/Scripts/Levels/Rooms/**`; `Assets/Scripts/Levels/{Room,GameBootstrap}.cs`; `Assets/Scripts/DebugTools/**`; `Assets/Tests/PlayMode/**` **except** the two files owned by D |
| **D: Feel & State (owner of REWIND and CHECKPOINTS)** | Input (Shift), animation, UI polish, pointer lock; **WorldHistory, checkpoints, fall recovery, switches and movers**, end to end | `Assets/Scripts/Gameplay/**` (including the new `Gameplay/State/**` and `Gameplay/Switches/**`, and `CameraPickup` moved here from LevelProps); `Assets/Scripts/Presentation/{Hud,PhotoOverlayUI,Crosshair,ClickToPlayOverlay,Onboarding,ScreenFx,FreshPulse,EndCard,UIFactory,UIUtil,IonCanvasScaler}.cs`; `Assets/Scripts/Presentation/Motion/**`; `Assets/Scripts/Presentation/Quality/SettingsPanel.cs` (an exception to A's Quality folder); `Assets/WebGLTemplates/**`; `Assets/Plugins/WebGL/**`; `Assets/Tests/PlayMode/{RewindTests,SwitchTests}.cs` |
| **E: Audio** | Music, SFX, mixing, footsteps | `Assets/Scripts/Presentation/Audio/**`; `Assets/Resources/Audio/**`; `tools/audio/**` |

**Shared contracts.** These are frozen at kickoff. Changes go through this document, and the owner lands **compiling stubs on day 1** so everyone else can build against them.

1. **`Mat` / `Pat` / `Surf` / `PatternCode` + `Palette.Get(Mat)`.** Owner: A. Used by B, C, E (`TryGetMat`).
2. **The vertex layout** (§6.1): `TEXCOORD0 = (pattern xyz, code)`, with the cap flag at +64. Owner: A. B and C never write UV0 by hand; `Arch.Bake`/`BakeLocal` do it.
3. **The `Arch` API** (§4). Owner: A. Day-1 stubs build plain Geo boxes with the right flags.
4. **The `PropKit` API** (§5). Owner: B. Day-1 stubs build placeholder boxes plus the real gameplay components.
5. **The gameplay state API** (§11): `WorldHistory`, `WorldChange`, `Checkpoint`, `PlayerPose`, `RewindResult`, `Switch`, `SwitchBoard`, `SwitchTarget`, `Mover`, `StoryCollapse`, `CheckpointMarker`, `CameraPickup`. Owner: D.
6. **`RoomSolution.Kind` additions + `RewindExpect`** (§7.5). Owner: C.
7. **`ZoneMood` + `Atmosphere.ApplyMood` / `BlendTo`.** Owner: A. Used by C (per zone and per capture).
8. **`IonAudio.Play(Sfx, Vector3?)` and the `Sfx` enum.** Owner: E. D calls it. E subscribes to the events listed in §8.
9. **`Feel` constants** (§9.1). Owner: D. Used by B (`Mover` timings), C (none) and E (rewind timing for the tape-dip).
10. **The projection public API is unchanged**: `Capture`, `Place`, `Rewind`, `CanRewind`, `PlacementCount`, `Placed`, `Rewound`. **Only `WorldHistory` (D) may call `ProjectionSystem.Rewind()`.** IonDebug and tests go through `WorldHistory`.

**Integration order:**
1. Day-1 stubs (all leads).
2. A: clipper UV0 + shader + `Bake`.
3. D: `WorldHistory` + switches.
4. B: props on Arch.
5. C: zones on kits + tests.
6. E: in parallel throughout.

Lead C's tutorial test is the final integration gate.

**Budgets that every lead respects:**
- ≤ 120 batches per visible zone.
- ≤ 60k triangles per zone.
- A placement takes ≤ 25 ms on the reference slow laptop (the existing `LastPlaceProfile`). On the player's LMB path the work is staged behind the pressed card, at most about 6 ms of placement work per frame (`ProjectionSystem.StageBudgetMs`). It is measured at a 6x CPU throttle with `IonDebug.PlacePress`.
- Pattern shader ≤ 30 ALU.
- Audio ≤ 3.5 MB.
- No new textures except photo previews.

---

## 11. Rewind and checkpoint contract (Lead D)

### 11.1 Data model

```csharp
namespace Ion.Gameplay.State
{
    public struct PlayerPose { public Vector3 Feet; public float Yaw, Pitch; public int Zone; }

    public enum ChangeKind { Placement, Capture, Switch, Pickup, CameraPickup }

    /// <summary>One undoable world change. Pushed by the code that makes the change.</summary>
    public abstract class WorldChange
    {
        public PlayerPose Pose;          // exact pose at the moment of the change: where a single R returns the player
        public bool HasPose;
        public PlayerPose SafePose;      // safe pose at the moment of the change (fallback when Pose is not standable)
        public float Time;
        public abstract ChangeKind Kind { get; }
        internal abstract void Undo(bool instant);
    }
    // Concrete records (internal):
    //  PlacementChange    { int ProjectionDepthBefore; PhotoData Photo; }        Undo: ProjectionSystem.Rewind() (asserts depth)
    //  CaptureChange      { PhotoData Photo; int InventoryIndex; int FilmBefore; } Undo: inventory.Remove, camera.Film = FilmBefore
    //  SwitchChange       { string Channel; bool Before; }                         Undo: SwitchBoard.Set(Channel, Before)
    //  PickupChange       { PhotoPickup Pickup; PhotoData Photo; int Index; }    Undo: inventory.Remove, Pickup.ResetPickup()
    //  CameraPickupChange { CameraPickup Pickup; bool UnlockedBefore; int FilmBefore; }

    /// <summary>Snapshot taken at room entry, hub entry and explicit CheckpointMarkers.</summary>
    public sealed class Checkpoint
    {
        public string Id;
        public int Zone;
        public int HistoryDepth;                                   // the "floor" single R cannot cross
        public PlayerPose Pose;
        public List<PhotoData> Inventory; public int SelectedIndex;
        public int Film; public bool CameraUnlocked;
        public Dictionary<string, bool> Channels;                  // SwitchBoard snapshot
    }

    public enum RewindResult { Undid, RecoveredFall, Nothing, ToCheckpoint }

    public sealed class WorldHistory : MonoBehaviour
    {
        public static WorldHistory Instance { get; }
        public int Depth { get; }
        public IReadOnlyList<WorldChange> Changes { get; }
        public Checkpoint LastCheckpoint { get; }
        public bool CanUndo { get; }                               // Depth > LastCheckpoint.HistoryDepth
        public void Push(WorldChange change);
        public RewindResult RewindOnce();                          // priority: fall > undo > nothing
        public RewindResult RewindToCheckpoint();
        public void SetCheckpoint(string id, PlayerPose pose);
        public void ClearAll();                                    // GameBootstrap.Restart
        public event System.Action<RewindResult> Rewound;
        public event System.Action<Checkpoint> CheckpointReached, CheckpointRestored;
        public event System.Action FallRecovered;
    }

    public sealed class SafePoseTracker : MonoBehaviour
    {
        public PlayerPose LastSafe { get; }
        public bool IsFalling { get; }
        public bool InLimbo { get; }
        public PlayerPose CurrentSafe();                           // current pose if grounded+safe, else LastSafe
        public void Reset(PlayerPose pose);
    }

    public sealed class RewindController : MonoBehaviour   // R input + transitions (the undo glide, the fall fade, the R R iris)
    {
        public const float DoubleTapWindow = 0.35f;
        public bool IsGliding { get; }  public float GlideFx { get; }  public float GlideSpeed01 { get; }  public float GlideRemaining { get; }
        public static event System.Action<RewindResult> Started;  public static event System.Action GlideEnded;
    }
}

namespace Ion.Gameplay
{
    public sealed class Switch : MonoBehaviour   // + Interactable; never sliced
    {
        public string Channel; public bool Powered = true;
        public bool On { get; }                    // SwitchBoard.Get(Channel)
        public bool Press();                       // false if unpowered; pushes SwitchChange
        public event System.Action<bool> Changed;
    }
    public static class SwitchBoard
    {
        public static bool Get(string channel);
        public static void Set(string channel, bool on, bool instant = false);   // no history (undo/restore use it)
        public static Dictionary<string, bool> Snapshot();
        public static void Restore(Dictionary<string, bool> snapshot);
        public static event System.Action<string, bool, bool> ChannelChanged;    // channel, on, instant
    }
    public abstract class SwitchTarget : MonoBehaviour { public string Channel; public bool Invert; protected abstract void Apply(bool active, bool instant); }
    public sealed class Mover : SwitchTarget { public Vector3 OffLocal, OnLocal; public float Duration = 1.6f; public bool IsMoving { get; } } // + Interactable
    public sealed class PowerTarget : SwitchTarget { }   // powers a Teleporter / Switch / exhibit
    public sealed class StoryCollapse : MonoBehaviour { public float Delay = 0.35f; public bool Fired { get; } public event System.Action Collapsed; } // + Interactable
    public sealed class CheckpointMarker : MonoBehaviour { public string Id; public Vector3 Size = new Vector3(2f, 2.5f, 2f); }
    public sealed class CameraPickup : MonoBehaviour { public int Film = 3; }   // moved from Levels/LevelProps
}
```

### 11.2 Rules

- **Who pushes records:**
  - `PhotoHolder.Place` pushes a Placement.
  - `InstantCamera.TryCapture` pushes a Capture.
  - `PhotoPickup.Collect` pushes a Pickup.
  - `CameraPickup` pushes a CameraPickup.
  - `Switch.Press` pushes a Switch record.

  The old rewind code in `PhotoHolder` (its R handling and stored pose) is deleted.
- **Single R** (`RewindOnce`), in priority order:
  1. **Falling or in limbo:** restore `LastSafe` (pose only) and raise `FallRecovered`.
  2. **`CanUndo`:** undo the top record and **always** return the player to the pose the change was made from (`record.Pose`: feet, yaw, pitch, recorded by `WorldHistory.Push` at the moment of the change), even when they stand on solid ground elsewhere. Placing a bridge, crossing and pressing R puts the player back on the near side with the photo in hand. If that exact pose is not standable in the restored world (a press made mid-jump, on a moving deck), they go to `record.SafePose` with the recorded look. `RewindController` plays it as the §9 glide (`WorldHistory.UndoTop` at 0.24 s, the move along the curve); `RewindOnce` does it instantly (tests, debug, last resort). `SafePoseTracker.Reset` is then called with the result. Successive presses walk back through the recorded poses in order. A walk-in pickup that is put back by a rewind collects again only after the player steps away from it.
  3. **Otherwise:** return `Nothing`, with the gentle feedback (§9).
- **Checkpoint floor.** Single R never crosses the last checkpoint. Records below it are sealed.
- **Double R** (`RewindToCheckpoint`) happens when a second R arrives within 0.35 s:
  1. Unwind every record down to `HistoryDepth`, instantly and in LIFO order.
  2. `SwitchBoard.Restore`.
  3. Restore the inventory (same `PhotoData` references and order), selected index, film and camera unlock.
  4. Restore the pose.

  It works even with no changes, which is the T2 lesson.
- **Checkpoints** are set at every zone entry (teleport arrival), every hub entry and every `CheckpointMarker`. A marker sets the checkpoint only when it is newer than the current one.
- **Fall definition.**
  - **Falling:** not grounded, and either feet < `LastSafe.y − 3.0`, or feet < the zone's `VoidY` (lowest floor − 6).
  - **Limbo:** feet < `VoidY`.
  - **Safe-pose sampling:** every 0.2 s while grounded on a normal with y > 0.7, and not on a `Mover` or `StoryCollapse`.
  - **Last resort:** `ResetY` (−50) stays.
- **Story events** (`StoryCollapse`, tutorial reveals such as an unpowered button powering up) are **one-way**. They are not in the history or in checkpoint snapshots. This is deliberate, so a tutorial beat can't loop.
- **Photo copies of devices.**
  - A pasted Switch shares `Channel`, so it works.
  - A pasted Mover follows its channel.
  - A pasted Teleporter shares `OnEnter`.
  - Capture clones the switch's visual state; the channel state is global.
- **Restart.** `GameBootstrap.Restart` calls `WorldHistory.ClearAll()` and `SwitchBoard.Restore(empty)`.

### 11.3 Required PlayMode tests

**Lead D** (`RewindTests.cs`, `SwitchTests.cs`):
- `Rewind_AnywhereUndoesLast_[Placement|Capture|Switch|Pickup]`: R long after the change, from elsewhere in the zone, restores the world and returns the player to the pose the change was made from (within 0.05 m; for the placement: after crossing the pasted bridge, back on the near side, grounded, no respawn).
- `Rewind_SuccessivePresses_WalkBackThroughPoses`, `Rewind_PressDuringGlide_IsBufferedNeverBroken`, `Rewind_GlideFollowsTheCurve`, `DoubleRewind_DuringGlide_RestoresCheckpointPose` (+ EditMode `RewindGlideTests`).
- `Rewind_NothingToRewind_NoChangeAndFeedback`.
- `Rewind_RecoversFromFall_NoWorldChange`.
- `Rewind_NeverCrossesCheckpoint`.
- `DoubleRewind_RestoresCheckpointSnapshot`: world, inventory, film, switch states and pose after several mixed changes.
- `DoubleRewind_SlowSecondPressIsTwoSingles`: two presses 0.5 s apart behave as two single rewinds.
- `Switch_ToggleMovesTarget_AndRewindRestores`.
- `Switch_PhotoCopyOfButtonWorks`.
- `Switch_IsNeverSliced`.
- `Mover_NeverCrushesPlayer`.
- `PointerLock_FirstClickDoesNotPlace`.

**Lead C:**
- `Tutorial_FullSequenceSolvable`: runs T1 and T2 `Room.Solutions` in order, including the forced fall → R, the button, the place, and the trap → R (Nothing) → R R. It ends in the hub.
- `Hub_EveryExhibitPhotoOpensItsWing`.
- `Hub_AllPhotosOnLightTable`.
- `Wings_SolvedReturnToHubAndMarkExhibit`.
- `Ending_UnlocksAfterBothWings`.
- `Harness_EveryMarkerHasASolution`.
- `Solutions_FrustaTouchOnlyTheirZone`.
- The existing Stairs, Camera and Forgiveness tests, ported.

**Lead A** (EditMode / PlayMode):
- The clipper UV0 tests from §6.2.
- `Art_EverySliceableHasPatternSpace`.
- `Art_ArchitectureOnGrid` (`Arch.Validate` on every zone and diorama).
- `Art_BatchBudgetPerZone`.
