// [project]ion — in-shader surface patterns (art bible §3.2, §6.3), included by Ion/FlatToon.
//
// Every mesh drawn with Ion/FlatToon carries TEXCOORD0 = (pattern-space xyz in metres, PatternCode):
//   * xyz: world position at bake time (Arch.Bake) or object-local position (Arch.BakeLocal). It is data that
//     travels with the vertex through every cut, merge, capture and paste, never recomputed.
//   * w: pattern id (Ion.Presentation.Pat) + 64 on cut faces (caps). 0 and 1 mean None (1 = missing UV0 in GL).
// The face role (top / side / bottom) comes from the dominant axis of the pattern-space face normal
// cross(ddx(P), ddy(P)); a Y-dominant face is a top, or a bottom when its world normal points down.
// Every line is anti-aliased from its pixel footprint; lines thinner than 1.25 px are drawn 1.25 px wide with
// proportionally less contrast, repeating detail fades out before it reaches a few pixels per period, and all
// contrast fades to zero between _IonPatternFade.x and .y metres. Values are multipliers on albedo.
// All math is float (diorama pattern positions reach ~1000 m). No textures.
#ifndef ION_PATTERN_INCLUDED
#define ION_PATTERN_INCLUDED

// Globals, set by Ion.Presentation.Atmosphere.ApplyPatternGlobals (unset = all zero = patterns off).
float4 _IonPatternFade;   // x = fade start (m), y = fade end (m)
float  _IonPatternOn;     // 0/1
float  _IonCutHatch;      // 0..1, section hatch strength on cut faces

#define ION_PAT_TILE        2
#define ION_PAT_TILESMALL   3
#define ION_PAT_ASHLAR      4
#define ION_PAT_FORMWORK    5
#define ION_PAT_HERRINGBONE 6
#define ION_PAT_BOARDS      7
#define ION_PAT_BOARDSX     8
#define ION_PAT_BOARDSZ     9
#define ION_PAT_TERRAZZO    10
#define ION_PAT_COFFER      11
#define ION_PAT_SPROCKET    12
#define ION_PAT_FROST       13
#define ION_PAT_LATTICE     14
#define ION_PAT_LAWN        15
#define ION_PAT_CHECKER     16
#define ION_PAT_COURSES     17

#define ION_ROLE_TOP    0
#define ION_ROLE_SIDE   1
#define ION_ROLE_BOTTOM 2

#define ION_BOTTOM_SHADE float3(0.94, 0.94, 0.94)
#define ION_ONE float3(1.0, 1.0, 1.0)

float IonPatHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Repeating detail of period 'pitch' (m) is shown fully above 6 px per period and fades out by 3 px (no moiré).
float IonPatVis(float pitch, float fw)
{
    return saturate((pitch / max(fw, 1e-6) - 3.0) * (1.0 / 3.0));
}

// Coverage (0..1, already scaled for thin lines) of lines of width w (m) centred on multiples of 'pitch' along c.
float IonPatLines(float c, float pitch, float w, float fw)
{
    fw = max(fw, 1e-6);
    float d = abs(frac(c / pitch + 0.5) - 0.5) * pitch;      // distance to the nearest line centre
    float wpx = max(w, 1.25 * fw);                          // never thinner than 1.25 px ...
    float cov = saturate((0.5 * wpx - d) / fw + 0.5);
    return cov * (w / wpx) * IonPatVis(pitch, fw);           // ... with proportionally less contrast
}

float IonPatGrid(float2 uv, float pitch, float w, float2 fw)
{
    return max(IonPatLines(uv.x, pitch, w, fw.x), IonPatLines(uv.y, pitch, w, fw.y));
}

// Coverage of a joint of width w (m) given the distance e (m) to the nearest edge of a cell.
float IonPatEdge(float e, float w, float fw)
{
    fw = max(fw, 1e-6);
    float wpx = max(w, 1.25 * fw);
    return saturate((0.5 * wpx - e) / fw + 0.5) * (w / wpx);
}

// Horizontal joints every 'pitch' on a side face (strata).
float3 IonPatCourses(float2 uv, float2 fw, float pitch)
{
    return (1.0 - 0.08 * IonPatLines(uv.y, pitch, 0.012, fw.y)).xxx;
}

// ---------------------------------------------------------------------------------------------------- patterns

// Contact-sheet floor: 1.0 tiles (or 0.5 small tiles), per-tile jitter, 4 x 4 "sheets" with a datum band and
// one warmer "selected frame" per sheet.
float3 IonPatTile(float2 uv, float2 fw, float fwm, int role, bool big)
{
    if (role == ION_ROLE_TOP)
    {
        float pitch = big ? 1.0 : 0.5;
        float2 cell = floor(uv / pitch);
        float joint = IonPatGrid(uv, pitch, big ? 0.015 : 0.012, fw);
        float jitter = (IonPatHash(cell) * 2.0 - 1.0) * (big ? 0.03 : 0.04) * IonPatVis(pitch, fwm);
        float3 m = ((1.0 - 0.12 * joint) * (1.0 + jitter)).xxx;
        if (big)
        {
            m *= 1.0 - 0.08 * IonPatGrid(uv, 4.0, 0.04, fw);
            float2 sheet = floor(uv * 0.25);
            float2 local = cell - sheet * 4.0;
            float pick = floor(IonPatHash(sheet + 17.31) * 16.0);
            if (abs(local.x + local.y * 4.0 - pick) < 0.5)
                m *= lerp(float3(1.0, 1.0, 1.0), float3(1.0506, 1.03, 0.9991), IonPatVis(4.0, fwm));
        }
        return m;
    }
    if (role == ION_ROLE_SIDE) return IonPatCourses(uv, fw, big ? 0.5 : 0.25);
    return ION_BOTTOM_SHADE;
}

// Running bond 1.0 x 0.5, half offset per course.
float3 IonPatAshlar(float2 uv, float2 fw, float fwm, int role)
{
    if (role == ION_ROLE_SIDE)
    {
        float row = floor(uv.y * 2.0);
        float u2 = uv.x + frac(row * 0.5);                      // 0 or 0.5 offset (frac handles negatives)
        float joint = max(IonPatLines(uv.y, 0.5, 0.012, fw.y), IonPatLines(u2, 1.0, 0.012, fw.x));
        float jitter = (IonPatHash(float2(floor(u2), row)) * 2.0 - 1.0) * 0.025 * IonPatVis(0.5, fwm);
        return ((1.0 - 0.10 * joint) * (1.0 + jitter)).xxx;
    }
    return role == ION_ROLE_TOP ? ION_ONE : ION_BOTTOM_SHADE;
}

// Board-formed concrete: 1.0 x 2.0 panels, board lines every 0.125, four tie holes per panel.
float3 IonPatFormwork(float2 uv, float2 fw, float fwm, int role)
{
    if (role == ION_ROLE_SIDE)
    {
        float joint = max(IonPatLines(uv.x, 1.0, 0.01, fw.x), IonPatLines(uv.y, 2.0, 0.01, fw.y));
        float board = IonPatLines(uv.y, 0.125, 0.006, fw.y);
        float fu = frac(uv.x);
        float fv = frac(uv.y * 0.5) * 2.0;
        float2 hole = float2(fu < 0.5 ? 0.25 : 0.75, fv < 1.0 ? 0.25 : 1.75);
        float d = length(float2(fu, fv) - hole);
        float tie = saturate((0.025 - d) / max(fwm, 1e-6) + 0.5) * IonPatVis(0.1, fwm);
        return ((1.0 - 0.08 * joint) * (1.0 - 0.02 * board) * (1.0 - 0.14 * tie)).xxx;
    }
    return role == ION_ROLE_TOP ? ION_ONE : ION_BOTTOM_SHADE;
}

// 45° herringbone of 0.5 x 0.125 bricks (ratio n = 4). The tiling is the lattice a(1,1) + b(n,-n) (in brick
// widths, in the 45°-rotated frame) applied to one horizontal brick H = [0,n)x[0,1) and one vertical brick
// V = [n,n+1)x[1-n,1); H u V spans x - y in (-1, 2n), so at most two lattice columns need testing.
float3 IonPatHerringbone(float2 uv, float2 fw, float fwm, int role)
{
    if (role == ION_ROLE_TOP)
    {
        const float n = 4.0, w = 0.125;
        float2 q = float2(uv.x + uv.y, uv.y - uv.x) * (0.70710678 / w);
        float b = floor((q.x - q.y + 1.0) / (2.0 * n));
        float2 r = q - b * float2(n, -n);
        float a = floor(r.y);
        float2 l = r - a;
        float2 size = float2(n, 1.0);
        float id = 0.0;
        if (l.x < 0.0 || l.x >= n)
        {
            // Not in H(a): it is in V of this column, or (x - y < 0) in V of the previous one.
            float2 rv = r;
            if (r.x - r.y < 0.0 && !(l.x >= 0.0 && l.x < n)) { rv = r + float2(n, -n); b -= 1.0; }
            a = floor(rv.x) - n;
            l = rv - float2(a + n, a + 1.0 - n);
            size = float2(1.0, n);
            id = 1.0;
        }
        float e = min(min(l.x, size.x - l.x), min(l.y, size.y - l.y)) * w;
        float joint = IonPatEdge(e, 0.01, fwm) * IonPatVis(0.125, fwm);
        float jitter = (IonPatHash(float2(a, b * 2.0 + id)) * 2.0 - 1.0) * 0.05 * IonPatVis(0.25, fwm);
        return ((1.0 - 0.12 * joint) * (1.0 + jitter)).xxx;
    }
    if (role == ION_ROLE_SIDE) return IonPatCourses(uv, fw, 0.125);
    return ION_BOTTOM_SHADE;
}

// Boards 0.125 wide along X (alongX) or Z, butt joints every 2.0 staggered per board. Side faces: the same
// boards along the face's horizontal axis.
float3 IonPatBoards(float2 uv, float2 fw, float fwm, int role, bool alongX)
{
    if (role == ION_ROLE_BOTTOM) return ION_BOTTOM_SHADE;
    float along, across, fwAlong, fwAcross;
    if (role == ION_ROLE_SIDE || alongX) { along = uv.x; across = uv.y; fwAlong = fw.x; fwAcross = fw.y; }
    else { along = uv.y; across = uv.x; fwAlong = fw.y; fwAcross = fw.x; }
    float board = floor(across * 8.0);
    float shifted = along + IonPatHash(float2(board, 3.7)) * 2.0;
    float joint = max(IonPatLines(across, 0.125, 0.008, fwAcross), IonPatLines(shifted, 2.0, 0.008, fwAlong));
    float jitter = (IonPatHash(float2(board, floor(shifted * 0.5))) * 2.0 - 1.0) * 0.05 * IonPatVis(0.125, fwm);
    return ((1.0 - 0.14 * joint) * (1.0 + jitter)).xxx;
}

// Terrazzo: small round chips (0.035 cells; 35% of cells hold one, jittered, ±7% with a warm / cool cast) and
// brass divider lines on a 2.0 grid. Chips fade out before they reach a few pixels (no blocky squares up close).
float3 IonPatTerrazzo(float2 uv, float2 fw, float fwm, int role)
{
    if (role == ION_ROLE_TOP)
    {
        const float cellSize = 0.035;
        float2 q = uv / cellSize;
        float2 cell = floor(q);
        float h0 = IonPatHash(cell);
        float3 m = ION_ONE;
        if (h0 < 0.35)
        {
            float h1 = IonPatHash(cell + 11.1), h2 = IonPatHash(cell + 23.7);
            float2 c = float2(0.3 + 0.4 * h1, 0.3 + 0.4 * h2);
            float r = 0.18 + 0.14 * IonPatHash(cell + 5.3);
            float d = length(frac(q) - c) - r;                                  // in cells
            float cov = saturate(0.5 - d * cellSize / max(fwm, 1e-6)) * IonPatVis(cellSize, fwm);
            float v = (h1 * 2.0 - 1.0) * 0.07;
            float3 tint = h2 < 0.5 ? float3(1.0 + v, 1.0 + v * 0.9, 1.0 + v * 0.7) : float3(1.0 + v * 0.8, 1.0 + v, 1.0 + v * 1.1);
            m = lerp(ION_ONE, tint, cov);
        }
        float brass = IonPatGrid(uv, 2.0, 0.01, fw);
        return m * lerp(ION_ONE, float3(1.09, 1.06, 1.03), brass);
    }
    return role == ION_ROLE_SIDE ? ION_ONE : ION_BOTTOM_SHADE;
}

// Coffered ceiling: 1.0 grid of 0.0625 rebate bands on undersides.
float3 IonPatCoffer(float2 uv, float2 fw, int role)
{
    if (role == ION_ROLE_BOTTOM) return (1.0 - 0.10 * IonPatGrid(uv, 1.0, 0.0625, fw)).xxx;
    return ION_ONE;
}

// Film sprocket band: 0.25 high, centred in every 0.5 of height (-6%), perforated every 0.25. The perforations
// are holes, so they read a little darker (-9%) than the band: quiet, never like a row of lit slots.
float3 IonPatSprocket(float2 uv, float2 fw, float fwm, int role)
{
    if (role == ION_ROLE_SIDE)
    {
        float vm = frac(uv.y * 2.0) * 0.5;                     // v mod 0.5
        float dv = vm - 0.25;
        float band = saturate((0.125 - abs(dv)) / max(fw.y, 1e-6) + 0.5) * IonPatVis(0.5, fwm);
        float pu = (frac(uv.x * 4.0) - 0.5) * 0.25;
        float2 pd = abs(float2(pu, dv)) - float2(0.055 - 0.02, 0.04 - 0.02);
        float sd = length(max(pd, 0.0)) + min(max(pd.x, pd.y), 0.0) - 0.02;
        float perf = saturate(-sd / max(fwm, 1e-6) + 0.5) * IonPatVis(0.25, fwm);
        return lerp(1.0 - 0.06 * band, 0.91, perf).xxx;
    }
    return role == ION_ROLE_TOP ? ION_ONE : ION_BOTTOM_SHADE;
}

// Breeze-block screen: a diamond "hole" in every 0.25 cell with a 0.03 frame (fakes depth; non-walkable only).
float3 IonPatLattice(float2 uv, float2 fw, float fwm, int role)
{
    if (role == ION_ROLE_SIDE)
    {
        float2 c = (frac(uv * 4.0) - 0.5) * 0.25;
        float l1 = abs(c.x) + abs(c.y);
        float hole = saturate((0.095 - l1) / max(fwm * 1.41421, 1e-6) + 0.5) * IonPatVis(0.25, fwm);
        return (1.0 - 0.35 * hole).xxx;
    }
    return ION_ONE;
}

// Mowing stripes 1.0 wide along X, +-3% (the material's _GroundVar adds the facets).
float3 IonPatLawn(float2 uv, float2 fw, int role)
{
    if (role == ION_ROLE_TOP)
    {
        float tri = abs(frac(uv.y * 0.5) - 0.5) * 2.0;           // 0..1 triangle wave, period 2
        float s = clamp((tri - 0.5) / max(fw.y, 1e-4), -1.0, 1.0) * IonPatVis(2.0, fw.y);
        return (1.0 + 0.03 * s).xxx;
    }
    return ION_ONE;
}

// Box-filtered 0.5 checker, +-6% (accent floors only).
float3 IonPatChecker(float2 uv, float2 fw, int role)
{
    if (role == ION_ROLE_TOP)
    {
        float2 p = uv * 2.0;
        float2 w = max(fw * 2.0, 1e-4);
        float2 i = 2.0 * (abs(frac((p - 0.5 * w) * 0.5) - 0.5) - abs(frac((p + 0.5 * w) * 0.5) - 0.5)) / w;
        float chk = 0.5 - 0.5 * i.x * i.y;
        return (1.0 + 0.06 * (2.0 * chk - 1.0)).xxx;
    }
    return role == ION_ROLE_SIDE ? ION_ONE : ION_BOTTOM_SHADE;
}

// ---------------------------------------------------------------------------------------------------- entry

// Albedo multiplier for a fragment. P / code: the interpolated TEXCOORD0; normalWS: the world normal;
// positionWS: for the distance fade; strength: the material's _PatternStrength.
// Returns exactly (1,1,1) when patterns are off, for codes 0/1 and where the mesh has no pattern space.
float3 IonPatternMultiplier(float3 P, float code, float3 normalWS, float3 positionWS, float strength)
{
    // Derivatives first, in uniform control flow (the branches below are per face).
    float3 nP = cross(ddx(P), ddy(P));
    float3 an = abs(nP);
    bool yDom = an.y >= an.x && an.y >= an.z;
    bool xDom = !yDom && an.x > an.z;
    float2 uv = yDom ? P.xz : (xDom ? P.zy : P.xy);
    float2 fw = fwidth(uv);
    float2 uvW = positionWS.xz;
    float2 fwW = fwidth(uvW);

    const float3 one = float3(1.0, 1.0, 1.0);
    if (_IonPatternOn < 0.5) return one;
    float c = round(code);
    bool cap = c > 63.5;
    int id = (int)(cap ? c - 64.0 : c);
    if (id <= 1 && !cap) return one;
    if (max(an.x, max(an.y, an.z)) < 1e-14) return one;      // constant pattern space: no pattern

    float fadeLen = max(_IonPatternFade.y - _IonPatternFade.x, 1e-3);
    float dist = distance(positionWS, GetCameraPositionWS());
    float k = (1.0 - saturate((dist - _IonPatternFade.x) / fadeLen)) * strength;
    if (k <= 0.0) return one;

    // Cut faces show their pattern's side treatment (plus the hatch below).
    int role = cap ? ION_ROLE_SIDE : (yDom ? (normalWS.y < -0.7 ? ION_ROLE_BOTTOM : ION_ROLE_TOP) : ION_ROLE_SIDE);

    // Floor patterns on faces that point up in the world are anchored to the world grid, not to their pattern
    // space: a floor pasted from a photo (taken from another diorama origin, maybe rolled) then continues the
    // live floor's joints, sheets and per-tile jitter exactly, with no lighter "pasted rectangle". Baked zone
    // architecture never moves, so its pattern space already is world space: nothing changes for it.
    bool floorPat = id == ION_PAT_TILE || id == ION_PAT_TILESMALL || id == ION_PAT_TERRAZZO || id == ION_PAT_HERRINGBONE ||
                    id == ION_PAT_CHECKER || id == ION_PAT_LAWN;
    if (floorPat && !cap && normalWS.y > 0.9)
    {
        uv = uvW;
        fw = fwW;
        role = ION_ROLE_TOP;
    }
    float fwm = max(fw.x, fw.y);

    float3 m = one;
    [branch] switch (id)
    {
        case ION_PAT_TILE:        m = IonPatTile(uv, fw, fwm, role, true); break;
        case ION_PAT_TILESMALL:   m = IonPatTile(uv, fw, fwm, role, false); break;
        case ION_PAT_ASHLAR:      m = IonPatAshlar(uv, fw, fwm, role); break;
        case ION_PAT_FORMWORK:    m = IonPatFormwork(uv, fw, fwm, role); break;
        case ION_PAT_HERRINGBONE: m = IonPatHerringbone(uv, fw, fwm, role); break;
        case ION_PAT_BOARDS:
        case ION_PAT_BOARDSX:     m = IonPatBoards(uv, fw, fwm, role, true); break;
        case ION_PAT_BOARDSZ:     m = IonPatBoards(uv, fw, fwm, role, false); break;
        case ION_PAT_TERRAZZO:    m = IonPatTerrazzo(uv, fw, fwm, role); break;
        case ION_PAT_COFFER:      m = IonPatCoffer(uv, fw, role); break;
        case ION_PAT_SPROCKET:    m = IonPatSprocket(uv, fw, fwm, role); break;
        case ION_PAT_FROST:       if (role == ION_ROLE_TOP) m = (1.0 - 0.03 * IonPatGrid(uv, 0.125, 0.006, fw)).xxx; break;
        case ION_PAT_LATTICE:     m = IonPatLattice(uv, fw, fwm, role); break;
        case ION_PAT_LAWN:        m = IonPatLawn(uv, fw, role); break;
        case ION_PAT_CHECKER:     m = IonPatChecker(uv, fw, role); break;
        case ION_PAT_COURSES:
            if (role == ION_ROLE_SIDE) m = IonPatCourses(uv, fw, 0.5);
            else if (role == ION_ROLE_BOTTOM) m = ION_BOTTOM_SHADE;
            break;
        default: break;
    }

    if (cap && _IonCutHatch > 0.0)
    {
        // Section poché: 45° hatch, pitch 0.1, line 0.012, -5%.
        float h = (uv.x + uv.y) * 0.70710678;
        float fwh = (fw.x + fw.y) * 0.70710678;
        m *= 1.0 - 0.05 * _IonCutHatch * IonPatLines(h, 0.1, 0.012, fwh);
    }
    return lerp(one, m, k);
}

#endif
