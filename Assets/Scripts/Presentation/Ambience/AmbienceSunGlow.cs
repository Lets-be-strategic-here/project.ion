using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Presentation
{
    /// <summary>
    /// Soft fake "sun glow": camera-facing additive quads placed far along the direction to the sun
    /// (80 % of the camera's far plane, so world geometry occludes it through the depth buffer).
    /// No fog (Ion/AmbienceSoft), on the Player layer so it never shows up in photo previews.
    /// Cost: 1 large transparent quad (+1 faint outer halo on High) — only fill-rate when looking at the sun.
    /// </summary>
    public sealed class AmbienceSunGlow : MonoBehaviour
    {
        const float GlowAngle = 26f;   // full angular size of the glow (degrees)
        const float HaloAngle = 64f;   // outer halo (High only)
        // Golden (Atmosphere.SunHaloColor): additive on a bright horizon, so it is kept saturated and a
        // little weaker, or the sum reads as cool white.
        static readonly Color k_GlowColor = new Color(1f, 0.78f, 0.5f, 0.42f);
        static readonly Color k_HaloColor = new Color(1f, 0.76f, 0.52f, 0.15f);

        static Mesh s_Quad;

        AmbienceDirector _director;
        Material _glowMat, _haloMat;
        Renderer _glow, _halo;
        float _intensity = -1f;
        bool _haloOn;

        void Awake()
        {
            _director = AmbienceDirector.Of(this);
            _glowMat = Ambience.CreateSoftMaterial("Ion_SunGlow", k_GlowColor, true, 2.2f, 0.5f);
            _haloMat = Ambience.CreateSoftMaterial("Ion_SunHalo", k_HaloColor, true, 1.6f, 0f);
            if (_glowMat == null) { enabled = false; return; }
            _glow = CreateQuad("Glow", _glowMat, GlowAngle);
            if (_haloMat != null) _halo = CreateQuad("Halo", _haloMat, HaloAngle);
        }

        Renderer CreateQuad(string name, Material mat, float angleDeg)
        {
            var go = new GameObject(name) { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            // Child scale = tan(half angle) * 2; the parent is scaled by the distance.
            float s = 2f * Mathf.Tan(angleDeg * 0.5f * Mathf.Deg2Rad);
            go.transform.localScale = new Vector3(s, s, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>Halo on/off (High tier) and overall intensity multiplier.</summary>
        public void Configure(bool halo, float intensity)
        {
            _haloOn = halo;
            if (_halo != null) _halo.enabled = halo;
            if (!Mathf.Approximately(intensity, _intensity))
            {
                _intensity = intensity;
                if (_glowMat != null) _glowMat.SetColor("_BaseColor", Scale(k_GlowColor, intensity));
                if (_haloMat != null) _haloMat.SetColor("_BaseColor", Scale(k_HaloColor, intensity));
            }
        }

        static Color Scale(Color c, float k) => new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a * k));

        void LateUpdate()
        {
            Camera cam = _director != null ? _director.ViewCamera : Camera.main;
            Light sun = RenderSettings.sun;
            bool visible = cam != null && sun != null && sun.isActiveAndEnabled;
            Vector3 toSun = visible ? -sun.transform.forward : Vector3.up;
            if (visible && toSun.y < -0.05f) visible = false; // sun below the horizon

            if (_glow != null && _glow.enabled != visible) _glow.enabled = visible;
            if (_halo != null)
            {
                bool h = visible && _haloOn;
                if (_halo.enabled != h) _halo.enabled = h;
            }
            if (!visible) return;

            Transform ct = cam.transform;
            float d = cam.farClipPlane * 0.8f;
            transform.SetPositionAndRotation(ct.position + toSun * d, Quaternion.LookRotation(toSun));
            transform.localScale = new Vector3(d, d, d);
        }

        static Mesh Quad()
        {
            if (s_Quad != null) return s_Quad;
            var m = new Mesh { name = "Ion_GlowQuad" };
            m.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            });
            m.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            m.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            m.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateBounds();
            m.UploadMeshData(true);
            s_Quad = m;
            return m;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Quad = null;

        void OnDestroy()
        {
            if (_glowMat != null) Destroy(_glowMat);
            if (_haloMat != null) Destroy(_haloMat);
        }
    }
}
