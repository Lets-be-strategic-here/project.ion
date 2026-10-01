using UnityEngine;

namespace Ion.Presentation
{
    /// <summary>
    /// Owns the ambience effects (created by <see cref="Ambience"/>'s self-install) and re-applies
    /// toggles / quality whenever <see cref="QualityTier.Changed"/> fires or <see cref="Ambience.Refresh"/>
    /// is called. Also resolves the view camera for camera-following effects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbienceDirector : MonoBehaviour
    {
        AmbienceClouds _clouds;
        AmbienceMotes _motes;
        AmbienceSunGlow _sunGlow;
        Camera _camera;

        void Awake()
        {
            _clouds = CreateChild<AmbienceClouds>("Clouds", 0);
            _motes = CreateChild<AmbienceMotes>("Motes", Ambience.CameraFxLayer);
            _sunGlow = CreateChild<AmbienceSunGlow>("Sun Glow", Ambience.CameraFxLayer);
        }

        void OnEnable()
        {
            QualityTier.Changed += OnTierChanged;
            ApplySettings();
        }

        void OnDisable()
        {
            QualityTier.Changed -= OnTierChanged;
        }

        void OnDestroy()
        {
            Ambience.Unregister(this);
        }

        void OnTierChanged() => Ambience.Refresh();

        T CreateChild<T>(string name, int layer) where T : Component
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        /// <summary>Applies toggles, knobs and the current quality tier to every effect.</summary>
        internal void ApplySettings()
        {
            int tier = Ambience.Tier;
            Ambience.ApplyGlobals();
            Ambience.ApplyCanopySway();

            if (_clouds != null)
            {
                int count = Ambience.CloudsEnabled
                    ? Mathf.RoundToInt(AmbienceClouds.CountForTier(tier) * Ambience.CloudDensity)
                    : 0;
                _clouds.gameObject.SetActive(count > 0);
                if (count > 0) _clouds.Configure(count, tier == QualityTier.Low ? 0 : 1);
            }

            if (_motes != null)
            {
                int count = Ambience.MotesEnabled
                    ? Mathf.RoundToInt(AmbienceMotes.CountForTier(tier) * Ambience.MotesDensity)
                    : 0;
                _motes.gameObject.SetActive(count > 0);
                if (count > 0) _motes.Configure(count, tier >= QualityTier.High);
            }

            if (_sunGlow != null)
            {
                bool on = Ambience.SunGlowEnabled && Ambience.SunGlowIntensity > 0f;
                _sunGlow.gameObject.SetActive(on);
                if (on) _sunGlow.Configure(tier >= QualityTier.High, Ambience.SunGlowIntensity);
            }
        }

        /// <summary>The camera the player sees through (Camera.main), re-resolved only when lost.</summary>
        internal Camera ViewCamera
        {
            get
            {
                if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
                return _camera;
            }
        }

        internal static AmbienceDirector Of(Component c)
        {
            return c.GetComponentInParent<AmbienceDirector>();
        }
    }
}
