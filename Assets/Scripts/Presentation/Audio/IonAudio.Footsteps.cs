using System;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Presentation.Motion;
using UnityEngine;

namespace Ion.Presentation.Audio
{
    /// <summary>
    /// Footsteps and landings. A step plays every half head-bob stride walked while grounded (so steps and
    /// bob dips share a rate). The set comes from what is underfoot (art bible §8):
    /// <list type="number">
    /// <item><see cref="SurfaceResolver"/> if a level wants to decide;</item>
    /// <item>the ground renderer's <see cref="Mat"/> via <see cref="Palette.TryGetMat"/> (stone-like, wood, grass);</item>
    /// <item>for stone-like materials, the pattern id in UV0.w of the hit triangle refines tile vs. stone
    /// (contact-sheet floors click, formwork and courses thud);</item>
    /// <item>legacy colour materials: a hue guess.</item>
    /// </list>
    /// One short raycast per step; per-mesh pattern lookups are cached.
    /// </summary>
    public sealed partial class IonAudio
    {
        /// <summary>Optional hook: decide the footstep set for a ground hit (return null to fall through).</summary>
        public static Func<RaycastHit, FootSurface?> SurfaceResolver { get; set; }

        const int PlayerLayer = 8, PhotoUiLayer = 9;
        const int GroundMask = ~((1 << PlayerLayer) | (1 << PhotoUiLayer));
        const float TeleportJump = 4f;

        // ------------------------------------------------------------------ mapping (public for tests)

        /// <summary>Footstep set for a material role (bible §8 table; Limestone/Terracotta default to tile).</summary>
        public static FootSurface SurfaceForMat(Mat mat)
        {
            switch (mat)
            {
                case Mat.Oak:
                case Mat.Walnut:
                    return FootSurface.Wood;
                case Mat.Lawn:
                case Mat.Foliage:
                case Mat.FoliageLight:
                case Mat.FoliageDark:
                case Mat.Lilac:
                    return FootSurface.Grass;
                case Mat.Paper:
                case Mat.Plaster:
                case Mat.Concrete:
                case Mat.Rose:
                case Mat.Mint:
                case Mat.TextileRed:
                case Mat.Mustard:
                case Mat.Teal:
                    return FootSurface.Stone;
                default: // Limestone, Terracotta, Cyanotype, metals, Frost, emissives
                    return FootSurface.Tile;
            }
        }

        /// <summary>Footstep set implied by a floor pattern, or null when the pattern says nothing.</summary>
        public static FootSurface? SurfaceForPattern(Pat pat)
        {
            switch (pat)
            {
                case Pat.Boards:
                case Pat.BoardsX:
                case Pat.BoardsZ:
                    return FootSurface.Wood;
                case Pat.Lawn:
                    return FootSurface.Grass;
                case Pat.Tile:
                case Pat.TileSmall:
                case Pat.Herringbone:
                case Pat.Terrazzo:
                case Pat.Checker:
                case Pat.Frost:
                    return FootSurface.Tile;
                case Pat.Courses:
                case Pat.Formwork:
                case Pat.Ashlar:
                case Pat.Coffer:
                case Pat.Sprocket:
                case Pat.Lattice:
                    return FootSurface.Stone;
                default:
                    return null;
            }
        }

        public static Sfx StepSfx(FootSurface s)
        {
            switch (s)
            {
                case FootSurface.Wood: return Sfx.FootstepWood;
                case FootSurface.Grass: return Sfx.FootstepGrass;
                case FootSurface.Stone: return Sfx.FootstepStone;
                default: return Sfx.FootstepTile;
            }
        }

        /// <summary>What is underfoot at <paramref name="feet"/> (falls back to the last surface, then tile).</summary>
        public static FootSurface ProbeSurface(Vector3 feet)
        {
            var inst = Instance;
            FootSurface fallback = inst != null ? inst._lastSurface : FootSurface.Tile;
            if (!Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 1.0f, GroundMask,
                                 QueryTriggerInteraction.Ignore))
                return fallback;
            FootSurface s = SurfaceOfHit(hit);
            if (inst != null) inst._lastSurface = s;
            return s;
        }

        static FootSurface SurfaceOfHit(in RaycastHit hit)
        {
            if (SurfaceResolver != null)
            {
                FootSurface? r = null;
                try { r = SurfaceResolver(hit); }
                catch (Exception e) { Debug.LogException(e); }
                if (r.HasValue) return r.Value;
            }

            Collider col = hit.collider;
            FootSurface? fromMat = null;
            Material material = MaterialAt(col, hit.triangleIndex);
            if (material != null)
            {
                if (Palette.TryGetMat(material, out Mat m)) fromMat = SurfaceForMat(m);
                else fromMat = LegacyGuess(material);
            }
            if (fromMat == FootSurface.Wood || fromMat == FootSurface.Grass) return fromMat.Value;

            if (TryPatternAt(col, hit.triangleIndex, out Pat pat))
            {
                FootSurface? p = SurfaceForPattern(pat);
                if (p.HasValue && (fromMat == null || p.Value == FootSurface.Tile || p.Value == FootSurface.Stone))
                    return p.Value;
            }
            return fromMat ?? FootSurface.Tile;
        }

        static readonly List<Material> s_Mats = new List<Material>(4);

        static Material MaterialAt(Collider col, int triangleIndex)
        {
            if (col == null || !col.TryGetComponent(out Renderer rend)) return null;
            rend.GetSharedMaterials(s_Mats);
            if (s_Mats.Count == 0) return null;
            if (s_Mats.Count == 1 || triangleIndex < 0) return s_Mats[0];
            // Several sub-meshes: find the one holding the hit triangle (same mesh as the collider only).
            if (col is MeshCollider mc && !mc.convex && col.TryGetComponent(out MeshFilter mf) &&
                mf.sharedMesh != null && mf.sharedMesh == mc.sharedMesh)
            {
                Mesh mesh = mf.sharedMesh;
                int index = triangleIndex * 3;
                for (int s = 0; s < mesh.subMeshCount && s < s_Mats.Count; s++)
                {
                    var sm = mesh.GetSubMesh(s);
                    if (index >= sm.indexStart && index < sm.indexStart + sm.indexCount) return s_Mats[s];
                }
            }
            return s_Mats[0];
        }

        // Per-mesh pattern id of every triangle (from UV0.w of its first vertex). Small, built once per mesh.
        sealed class TriPatterns
        {
            public int VertexCount;
            public byte[] Codes; // null = mesh has no pattern space
        }

        static readonly Dictionary<Mesh, TriPatterns> s_TriPatterns = new Dictionary<Mesh, TriPatterns>();
        static readonly List<Vector4> s_Uv = new List<Vector4>();
        const int MaxPatternMeshes = 64;

        static bool TryPatternAt(Collider col, int triangleIndex, out Pat pat)
        {
            pat = Pat.None;
            if (triangleIndex < 0 || !(col is MeshCollider mc) || mc.convex) return false;
            Mesh mesh = mc.sharedMesh;
            if (mesh == null || !mesh.isReadable) return false;

            if (!s_TriPatterns.TryGetValue(mesh, out TriPatterns tp) || tp.VertexCount != mesh.vertexCount)
            {
                if (s_TriPatterns.Count >= MaxPatternMeshes) s_TriPatterns.Clear(); // cut meshes come and go
                tp = new TriPatterns { VertexCount = mesh.vertexCount };
                mesh.GetUVs(0, s_Uv);
                if (s_Uv.Count == mesh.vertexCount && s_Uv.Count > 0)
                {
                    int[] tris = mesh.triangles;
                    tp.Codes = new byte[tris.Length / 3];
                    for (int t = 0; t < tp.Codes.Length; t++)
                        tp.Codes[t] = (byte)PatternCode.Decode(s_Uv[tris[t * 3]].w, out _);
                }
                s_Uv.Clear();
                s_TriPatterns[mesh] = tp;
            }
            if (tp.Codes == null || triangleIndex >= tp.Codes.Length) return false;
            pat = (Pat)tp.Codes[triangleIndex];
            return pat != Pat.None;
        }

        static readonly Dictionary<Material, FootSurface> s_Legacy = new Dictionary<Material, FootSurface>();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Legacy colour materials (old Geo rooms): green reads as grass, warm brown as wood.</summary>
        static FootSurface LegacyGuess(Material m)
        {
            if (s_Legacy.TryGetValue(m, out FootSurface s)) return s;
            Color c = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            if (sat > 0.2f && h > 0.17f && h < 0.45f) s = FootSurface.Grass;
            else if (sat > 0.25f && h > 0.03f && h < 0.12f && v < 0.85f) s = FootSurface.Wood;
            else s = FootSurface.Tile;
            s_Legacy[m] = s;
            return s;
        }

        // ------------------------------------------------------------------ stepping

        FirstPersonController _stepFpc;
        Vector3 _lastFeet;
        bool _hasLastFeet, _wasGrounded = true;
        float _stepDistance, _lastGroundedTime, _airMinVy;
        FootSurface _lastSurface = FootSurface.Tile;

        void UpdateFootsteps()
        {
            var fpc = FirstPersonController.Current;
            if (!ReferenceEquals(fpc, _stepFpc))
            {
                _stepFpc = fpc;
                _hasLastFeet = false;
            }
            if (fpc == null) return;

            Vector3 pos = fpc.transform.position;
            if (!_hasLastFeet)
            {
                _lastFeet = pos;
                _hasLastFeet = true;
                _wasGrounded = fpc.IsGrounded;
                _stepDistance = 0f;
                return;
            }
            Vector3 delta = pos - _lastFeet;
            _lastFeet = pos;
            float horiz = new Vector2(delta.x, delta.z).magnitude;

            // Teleports, rewinds and respawns: a move no walking produces.
            if (horiz > TeleportJump || Mathf.Abs(delta.y) > 6f)
            {
                _stepDistance = 0f;
                _wasGrounded = fpc.IsGrounded;
                _airMinVy = 0f;
                return;
            }

            bool grounded = fpc.IsGrounded;
            float now = Time.time;
            if (grounded) _lastGroundedTime = now;
            bool groundedish = now - _lastGroundedTime < 0.1f; // isGrounded flickers on steps

            if (!grounded)
            {
                _airMinVy = Mathf.Min(_airMinVy, fpc.Velocity.y);
            }
            else if (!_wasGrounded)
            {
                float impact = -_airMinVy;
                if (impact > Feel.LandingMinSpeed && !_inLimbo)
                {
                    float k = Mathf.Clamp01((impact - Feel.LandingMinSpeed) / 10f);
                    Play(Sfx.Land, null, 0.5f + 0.7f * k, 1f);
                    PlayStep(fpc, 0.9f + 0.6f * k, 0.88f);
                }
                _airMinVy = 0f;
                _stepDistance = 0f;
            }
            _wasGrounded = grounded;

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float speed = horiz / dt;
            if (!groundedish || speed < 0.4f || _falling || _inLimbo)
            {
                // Standing still: the next step lands soon after moving off.
                if (groundedish) _stepDistance = Mathf.Min(_stepDistance, StepLength(fpc) * 0.75f);
                return;
            }

            _stepDistance += horiz;
            float stride = StepLength(fpc);
            if (_stepDistance >= stride)
            {
                _stepDistance -= stride;
                PlayStep(fpc, Mathf.Lerp(0.75f, 1.05f, Mathf.InverseLerp(1.5f, 4.5f, speed)), 1f);
            }
        }

        static float StepLength(FirstPersonController fpc) => Mathf.Max(0.6f, fpc.BobStride * 0.5f);

        void PlayStep(FirstPersonController fpc, float volume, float pitch)
        {
            FootSurface s = ProbeSurface(fpc.transform.position);
            Play(StepSfx(s), null, volume, pitch);
        }
    }
}
