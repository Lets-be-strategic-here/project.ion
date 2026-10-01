# Art bible decision: which theme, and what we took from the others

Date: 2026-10-01 · Decided by: lead art director · Binding document: [`art-bible.md`](art-bible.md)

## The three candidates

| | Angle A, "Light Table" | Angle B, "Villa Proiezione" | Angle C, "Drawing Set" |
|---|---|---|---|
| One line | The world is photographs laid out on a light table. The `[ ]` appears as a "bracket return", backed by photographic motifs (contact sheet, sprocket, slide mount, cyanotype). | A sunlit Mediterranean villa run as a museum of projections: lime plaster, terracotta, limestone, cobalt. `[ ]` surrounds frame every opening. | Every room is a sheet from an architect's drawing set, built at 1:1. Photos arrive as cyanotypes and are "built" in colour once placed. |

## Scores

Each criterion is scored 1–10 and weighted. Weights follow the user's feedback: "doesn't have taste" makes fit and subtlety count, and parallel agents building in code makes buildability count.

| Criterion (weight) | A Light Table | B Villa Proiezione | C Drawing Set |
|---|---|---|---|
| Fit to the title `[project]ion` (20%) | **9**. Photography *is* the mechanic. The hub becomes a contact sheet of *projects*, and the brackets read as crop marks. | 6. The brackets and the ion light carry it. A villa says little about photos or projects. | **9**. It carries three meanings: project (commission), projection (orthographic drawings), and `[ ]` as crop marks. |
| Subtlety (15%) | **8**. One motif per surface and 4–8% pattern contrast. | **8**. The title appears literally only on the hub plaque and in the UI. | 6. North arrows, section markers, hatching and the blueprint palette slide easily into gimmick. |
| Viewfinder-like right-angle rigour (15%) | **9**. No curves at all, a 1 m / 0.25 m grid, and stepped lintels. | **9**. Strictly orthogonal, with corbel "arches". | **9**. The same rigour plus a clear three-scale rule. |
| Buildability in code (20%) | **8**. Box kit only. Its pattern-ID-in-vertex-alpha idea collides with the existing sway weight in alpha (see below). | **8**. Box kit only. A per-placement MaterialPropertyBlock pattern frame needs a ProjectionSystem hook, and per-material pattern keywords multiply materials. | 6. Object-space patterns break once cut pieces and photo pieces are merged into shared meshes, which the slicer already does. Cyanotype-to-colour needs per-piece state. |
| Readability for puzzles (15%) | 8. Light-table glow marks the pedestals, but there is no reserved "interactive" colour. | **9**. Ion light `#9FE3FF` is reserved for interactive things, and cobalt threshold inlays mark key spots. | 7. A pale study model risks low contrast. Colour-on-solve is good feedback. |
| Portfolio appeal (15%) | **9**. The light-table hub *is* a portfolio and scales naturally to future projects. | 7. Warm and pretty, but reads as a Viewfinder chapter-1 homage. | 8. Strong for an architecture-flavoured portfolio. |
| **Weighted total** | **8.50** | 7.75 | 7.50 |

## Decision

**Angle A, "Light Table", is the base.** It ties the theme to the mechanic (photographs), to the title (`[ ]` as crop marks, "project" as the portfolio) and to the user's portfolio goal (the hub is a contact sheet of projects). It does all of this with the strictest geometry.

## What we graft from the others

From **B (Villa Proiezione)**:
- The **ion-light rule**. `#9FE3FF` emissive is reserved for things the player can use: pickups, buttons, teleporters, the raised-photo edge and live exhibits. This is the single biggest readability gain.
- **Threshold inlays** as *standing markers*. A brass `[ ]` floor inlay marks every intended photo spot. It doubles as the solvability anchor for `Room.Solutions`.
- The **open-headed bracket surround**. A `[` jamb and a `]` jamb each have short serifs, and the lintel centre stays bare. It replaces A's inward-stepping corners, which narrowed the clear opening.
- The warm material set (limestone, terracotta, walnut) and the sunlit, hard-shadow mood.
- Gameplay heights: barriers ≥ 1.5 m, gaps ≥ 3.5 m (re-derived from the controller's 1.15 m jump).
- The slicer rule that **trims are Sliceable with their wall**, so a cut wall never leaves trim floating.
- The UI echo: `[SHIFT] hold up photo`, `[01]`.

From **C (Drawing Set)**:
- The **three-scale rule**: massing 4 m, articulation 0.5 m, detail 0.0625 m, then the shader pattern. This is how "low poly but detailed" is defined.
- **Section poché on cut faces**. Cap faces carry a faint 45° hatch, a quiet nod to "projection" drawings. This is made possible by a cap flag the clipper writes.
- The contact-sheet "datum" joint every 4 m, which also appears in A.
- The HUD reticle as 4 corner ticks, and the title-block style for plaques.
- Square columns only, the stair geometry (rise 0.25, tread 0.5, solid step columns) and the slab shadow-reveal.

From the **Viewfinder research**:
- Sky-tinted shadows, never grey. This is the biggest "taste" lever.
- No outlines.
- Off-white structure, with saturation only in sky, plants, textiles and devices.
- One mood per zone.
- Lush plants as the softener.
- Planar, stepped undersides on floating terraces instead of rock spikes.
- Stacked offset-slab towers for skyline silhouettes.

## The technical decision none of the three got right

All three proposals tied the tiling pattern to a *space*: world space (A's fallback), a per-placement frame (B) or object space (C). The slicer breaks each of them:
- Cut pieces and photo pieces are **merged** into shared meshes, so object space is lost.
- Pasted pieces are rigidly moved, so world space slides.
- MaterialPropertyBlocks don't survive merging pieces of different placements.

**Binding choice:** every vertex carries its own pattern-space position and pattern code in **`TEXCOORD0` (float4: xyz = pattern position in metres, w = pattern code)**.
- Architecture writes UV0 at bake time from its world position. The rooms and the dioramas are grid-aligned, so the grout lands on the geometry's edges.
- The clipper interpolates UV0 linearly. This is exact, because the attribute is affine in position.
- Caps copy UV0 from their boundary and set a cap flag.
- Mergers copy UV0 unchanged.

What follows:
- Patterns survive any number of cuts, merges and pastes.
- A pasted photo carries its own tiling. A rolled photo turns floor tiles into a wall, which gives the Viewfinder "this was projected" read for free.
- The projection *semantics* do not change. The change is additive attribute plumbing in `MeshClipper`, `MeshElements`, `PieceMerger` and the merge paths of `ProjectionSystem`.

The shader picks floor or wall treatment from the **pattern-space face normal**, computed per pixel as `cross(ddx(P), ddy(P))`. It needs no extra attribute, and it is exact for flat-shaded low-poly faces.
