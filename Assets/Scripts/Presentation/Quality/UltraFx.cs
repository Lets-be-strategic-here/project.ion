using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ion.Presentation.Quality
{
    /// <summary>
    /// The Ultra tier's extra lighting (everything here is off on Low / Med / High, so they pay nothing):
    /// <list type="bullet">
    /// <item>Local lights: every lamp, lantern, sconce and teleporter registers a point light (<see cref="LocalLights"/>);
    ///   on Ultra the <see cref="MaxLights"/> nearest within <see cref="LightCullDistance"/> of the camera are enabled
    ///   (per-pixel, no shadows; Ion/FlatToon adds them as a soft wrapped term).</item>
    /// <item>Bloom: post-processing on the player camera with one global Volume (Bloom only, threshold 1.3 in HDR, so
    ///   only the boosted emissive ion / lamp / safelight colours and the sun disc bloom). No half-float
    ///   targets (LDR): no bloom, since it would catch every sunlit white wall.</item>
    /// <item>Ambient occlusion: a cheap substitute (the baked contact shade under props deepens, Ambience). URP's SSAO
    ///   was measured and rejected on WebGL2: its depth prepass doubled the draw calls and the result was noisy.</item>
    /// <item>Shader globals <c>_IonUltraFx</c> / <c>_IonUltraFxAtmo</c>: a softer shade with a warm ground bounce, HDR
    ///   glow gain for bloom, sun in-scattering in the fog, and Ultra-only detail geometry (Palette.GetUltra).</item>
    /// </list>
    /// Self-installs after the scene loads and follows <see cref="QualityTier.Changed"/>. WebGL2-safe: every piece
    /// degrades to "off" (no HDR targets: LDR bloom; no SSAO shader: the feature simply does nothing).
    /// </summary>
    [DefaultExecutionOrder(-850)]
    [DisallowMultipleComponent]
    public sealed class UltraFx : MonoBehaviour
    {
        public const int MaxLights = 8;
        /// <summary>Photo preview width on Ultra (768 elsewhere).</summary>
        public const int UltraPreviewWidth = 1280;
        public const float LightCullDistance = 26f;
        const float LightRefreshSeconds = 0.2f;
        const float CameraCheckSeconds = 1f;

        // _IonUltraFx: x on, y fog scatter, z bounce strength, w HDR glow gain.
        static readonly Vector4 k_On = new Vector4(1f, 0.55f, 0.12f, 0.5f);
        static readonly int UltraFxId = Shader.PropertyToID("_IonUltraFx");
        static readonly int UltraFxAtmoId = Shader.PropertyToID("_IonUltraFxAtmo");

        public static UltraFx Instance { get; private set; }

        /// <summary>True while the Ultra effects are applied.</summary>
        public static bool Active { get; private set; }

        /// <summary>True while Ultra bloom runs (needs HDR targets).</summary>
        public static bool BloomActive { get; private set; }

        /// <summary>Local lights enabled right now (Ultra only).</summary>
        public static int ActiveLights { get; private set; }

        Volume _volume;
        VolumeProfile _profile;
        Bloom _bloom;
        Camera _camera;
        float _lightTimer, _cameraTimer;
        static readonly List<KeyValuePair<float, Light>> s_Sorted = new List<KeyValuePair<float, Light>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Active = false;
            BloomActive = false;
            ActiveLights = 0;
            Ion.Projection.ProjectionSystem.DefaultPreviewWidth = Ion.Projection.ProjectionSystem.PreviewWidth;
            Shader.SetGlobalVector(UltraFxId, Vector4.zero);
            Shader.SetGlobalVector(UltraFxAtmoId, Vector4.zero);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Instance != null || FindFirstObjectByType<UltraFx>() != null) return;
            new GameObject("Ion UltraFx").AddComponent<UltraFx>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            QualityTier.Changed += Apply;
            Apply();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            QualityTier.Changed -= Apply;
            Shader.SetGlobalVector(UltraFxId, Vector4.zero);
            Shader.SetGlobalVector(UltraFxAtmoId, Vector4.zero);
            if (_profile != null) Destroy(_profile);
            Active = false;
        }

        /// <summary>Applies (or removes) every Ultra effect for the current tier.</summary>
        public void Apply()
        {
            bool on = QualityTier.IsUltra;
            Active = on;
            Shader.SetGlobalVector(UltraFxId, on ? k_On : Vector4.zero);
            Ion.Projection.ProjectionSystem.DefaultPreviewWidth = on ? UltraPreviewWidth : Ion.Projection.ProjectionSystem.PreviewWidth;
            Shader.SetGlobalVector(UltraFxAtmoId, on ? new Vector4(0f, k_On.y, 0f, 0f) : Vector4.zero);
            EnsureVolume();
            // Bloom needs HDR targets (Ultra turns HDR on when the GPU has half-float targets): only the boosted
            // emissives and the sun pass 1.0 there. Without them it would bloom every sunlit white wall: off.
            BloomActive = on && AdaptiveQuality.SupportsHdrTargets();
            if (_volume != null) _volume.enabled = BloomActive;
            ApplyCamera(BloomActive);
            if (!on)
            {
                LocalLights.DisableAll();
                ActiveLights = 0;
            }
            else RefreshLights();
        }

        void Update()
        {
            if (!Active) return;
            float dt = Time.unscaledDeltaTime;
            _cameraTimer -= dt;
            if (_cameraTimer <= 0f)
            {
                _cameraTimer = CameraCheckSeconds;
                ApplyCamera(BloomActive);
            }
            _lightTimer -= dt;
            if (_lightTimer <= 0f)
            {
                _lightTimer = LightRefreshSeconds;
                RefreshLights();
            }
        }

        void ApplyCamera(bool on)
        {
            Camera cam = Camera.main;
            if (cam != _camera && _camera != null && _camera.TryGetComponent(out UniversalAdditionalCameraData old))
                old.renderPostProcessing = false;
            _camera = cam;
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            if (data.renderPostProcessing != on) data.renderPostProcessing = on;
            if (on)
            {
                data.antialiasing = AntialiasingMode.None;   // MSAA does the edges
                data.dithering = false;
            }
        }

        void EnsureVolume()
        {
            if (_volume != null) return;
            var go = new GameObject("Ultra Bloom Volume");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Ion Ultra";
            _bloom = _profile.Add<Bloom>(true);
            // URP's soft knee starts at half the threshold: 1.3 keeps sunlit white walls (<= 1.0) almost out of it,
            // while the boosted emissives (up to 1.5) and the sun disc (up to ~1.75) bloom.
            _bloom.threshold.Override(1.3f);
            _bloom.intensity.Override(0.35f);
            _bloom.scatter.Override(0.5f);
            _bloom.clamp.Override(8f);
            _bloom.tint.Override(new Color(1f, 0.95f, 0.86f));
            _bloom.highQualityFiltering.Override(false);
            _bloom.maxIterations.Override(5);
            var tone = _profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.None);
            _volume.sharedProfile = _profile;
            _volume.enabled = false;
        }

        void RefreshLights()
        {
            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null)
            {
                LocalLights.DisableAll();
                ActiveLights = 0;
                return;
            }
            Vector3 eye = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            s_Sorted.Clear();
            var all = LocalLights.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Light l = all[i];
                if (l == null) { all.RemoveAt(i); continue; }
                if (!l.gameObject.activeInHierarchy) { l.enabled = false; continue; }
                Vector3 to = l.transform.position - eye;
                float d = to.magnitude;
                if (d > LightCullDistance + l.range) { if (l.enabled) l.enabled = false; continue; }
                // Lights behind the camera matter less (but still light what is in front of them).
                float behind = Vector3.Dot(to, fwd) < -l.range ? 2f : 1f;
                s_Sorted.Add(new KeyValuePair<float, Light>(d * behind, l));
            }
            s_Sorted.Sort((a, b) => a.Key.CompareTo(b.Key));
            int n = 0;
            for (int i = 0; i < s_Sorted.Count; i++)
            {
                bool want = i < MaxLights;
                Light l = s_Sorted[i].Value;
                if (l.enabled != want) l.enabled = want;
                if (want) n++;
            }
            ActiveLights = n;
        }
    }

    /// <summary>
    /// Registry of the local point lights props carry for the Ultra tier (lamps, lanterns, sconces, teleporters).
    /// Lights are created disabled; <see cref="UltraFx"/> enables the nearest few on Ultra.
    /// </summary>
    public static class LocalLights
    {
        public const string ObjectName = "Ultra Light";
        static readonly List<Light> s_All = new List<Light>();

        internal static List<Light> All => s_All;

        public static int Count => s_All.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();

        /// <summary>
        /// Adds a (disabled) point light under <paramref name="parent"/> at <paramref name="localPos"/>.
        /// Colour sRGB; range in metres; intensity is URP's (linear) point intensity.
        /// </summary>
        public static Light Add(Transform parent, Vector3 localPos, Color color, float range, float intensity)
        {
            var go = new GameObject(ObjectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            l.cullingMask = ~(1 << 9);   // never the photo UI layer
            l.enabled = false;
            go.AddComponent<UltraLight>();   // registers itself (photo copies of a device register too)
            return l;
        }

        internal static void Register(Light l)
        {
            if (l != null && !s_All.Contains(l)) s_All.Add(l);
        }

        internal static void Unregister(Light l) => s_All.Remove(l);

        internal static void DisableAll()
        {
            for (int i = s_All.Count - 1; i >= 0; i--)
            {
                if (s_All[i] == null) { s_All.RemoveAt(i); continue; }
                if (s_All[i].enabled) s_All[i].enabled = false;
            }
        }
    }
}

namespace Ion.Presentation.Quality
{
    /// <summary>Marks a local light for <see cref="LocalLights"/> (self-registering, so cloned devices keep theirs).</summary>
    [DisallowMultipleComponent]
    public sealed class UltraLight : MonoBehaviour
    {
        Light _light;

        void Awake()
        {
            _light = GetComponent<Light>();
            if (_light != null) _light.enabled = false;
            LocalLights.Register(_light);
        }

        void OnDestroy() => LocalLights.Unregister(_light);
    }
}
