using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ion.Presentation.Quality
{
    /// <summary>
    /// Keeps the game smooth on weak laptops. Self-installs after the scene loads.
    ///
    /// Auto (QualityTier.Preference == -1): starts at High, waits 2 s, measures 3 s of unscaled frame
    /// time and steps a tier down while that average is above 22 ms (re-measuring after each step).
    /// Then it monitors 5 s windows: above 20 ms average steps down; below 11 ms for three windows
    /// (15 s) steps up, unless the target tier was already dropped from more than once. Inside a tier
    /// it tunes the URP render scale in 0.05 steps between 0.6 and the tier's base scale (1.5 s windows,
    /// with back-off when an increase does not hold).
    /// Fixed tier: applies that tier's table at its base scale and does not adapt.
    ///
    /// Tier tables (render scale / MSAA / shadow distance / sun shadows / shadow map / soft filter):
    ///   High 1.0 / 4x / 40 m / Soft / asset value (2048) / Medium;  Med 0.85 / 2x / 30 m / Soft / 1024 / Low;
    ///   Low 0.75 / 1x / 0 / None. Low relies on the baked contact shading (Kit.Contact, decor AO).
    ///
    /// Only public URP 17.3 setters are used: renderScale, msaaSampleCount, shadowDistance,
    /// mainLightShadowmapResolution. supportsMainLightShadows and supportsSoftShadows have internal
    /// setters, so shadows are switched on the sun light instead (Light.shadows), and the soft filter
    /// quality on its UniversalAdditionalLightData. The URP asset has Soft Shadows enabled.
    /// In the Editor the asset's original values are restored when play stops.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public sealed class AdaptiveQuality : MonoBehaviour
    {
        struct TierSpec
        {
            public float Scale;
            public int Msaa;
            public float ShadowDistance;
            public LightShadows Shadows;
            public int ShadowRes; // 0 = keep the asset's own value
            public SoftShadowQuality Soft;
        }

        static readonly TierSpec[] k_Tiers =
        {
            new TierSpec { Scale = 0.75f, Msaa = 1, ShadowDistance = 0f, Shadows = LightShadows.None, ShadowRes = 1024, Soft = SoftShadowQuality.Low },
            new TierSpec { Scale = 0.85f, Msaa = 2, ShadowDistance = 30f, Shadows = LightShadows.Soft, ShadowRes = 1024, Soft = SoftShadowQuality.Low },
            new TierSpec { Scale = 1f, Msaa = 4, ShadowDistance = 40f, Shadows = LightShadows.Soft, ShadowRes = 0, Soft = SoftShadowQuality.Medium },
        };

        public const float MinRenderScale = 0.6f;
        public const float MaxRenderScale = 1f;
        public const float ScaleStep = 0.05f;

        const float WarmupSeconds = 2f;
        const float ResettleSeconds = 1f;      // ignore frames right after a change (target realloc, variant compile)
        const float CalibrateSeconds = 3f;
        const float CalibrateDownMs = 22f;
        const float MonitorSeconds = 5f;
        const float MonitorDownMs = 20f;
        const float TierUpMs = 11f;
        const int TierUpWindows = 3;           // 3 x 5 s = 15 s
        const int MaxDropsBeforeNoReturn = 1;  // may return to a tier dropped from at most once

        const float ScaleWindowSeconds = 1.5f;
        const float ScaleDownMs = 18.5f;
        const float ScaleBigDownMs = 25f;
        const float ScaleUpAvgMs = 17.8f;
        const float SlowFrameMs = 20f;
        const float ScaleUpMaxSlowFraction = 0.03f;
        const int ScaleUpWindowsMin = 2;
        const int ScaleUpWindowsMax = 16;

        const float MaxCountedDt = 0.1f;       // a 1 s hitch counts as 100 ms, so one stall cannot force a drop
        const float SunCheckInterval = 1f;

        public static AdaptiveQuality Instance { get; private set; }

        /// <summary>Exponentially smoothed frame time in ms (about 0.5 s time constant), unscaled.</summary>
        public static float SmoothedFrameMs { get; private set; } = 16.7f;

        /// <summary>The render scale currently applied.</summary>
        public static float RenderScale { get; private set; } = 1f;

        /// <summary>Cost knob: false keeps each tier at its base render scale.</summary>
        public static bool DynamicResolutionEnabled = true;

        enum Phase { Settle, Calibrate, Monitor, Manual }

        Phase _phase;
        Phase _afterSettle;
        float _settleLeft;

        float _winTime;
        int _winFrames;
        int _upStreak;
        readonly int[] _drops = new int[3];

        float _sTime;
        int _sFrames;
        int _sSlow;
        int _goodStreak;
        int _upNeeded = ScaleUpWindowsMin;
        int _windowsSinceScaleUp = 99;

        float _scale = 1f;
        Light _sun;
        float _sunTimer;

        UniversalRenderPipelineAsset _urp;
        float _origScale = 1f;
        int _origMsaa = 4;
        float _origShadowDistance = 60f;
        int _origShadowRes = 2048;

        /// <summary>True while Auto is still measuring the first tier.</summary>
        public bool IsCalibrating => _phase == Phase.Calibrate || (_phase == Phase.Settle && _afterSettle == Phase.Calibrate);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            SmoothedFrameMs = 16.7f;
            RenderScale = 1f;
            DynamicResolutionEnabled = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Instance != null || FindFirstObjectByType<AdaptiveQuality>() != null) return;
            new GameObject("Ion AdaptiveQuality").AddComponent<AdaptiveQuality>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            // Web: the browser drives frames with requestAnimationFrame; never throttle on our side.
            Application.targetFrameRate = -1;

            QualityTier.PreferenceChanged += OnPreferenceChanged;

            int tier = QualityTier.Current;
            ApplyTier(tier, k_Tiers[tier].Scale);
            if (QualityTier.IsAuto) BeginSettle(WarmupSeconds, Phase.Calibrate);
            else _phase = Phase.Manual;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            QualityTier.PreferenceChanged -= OnPreferenceChanged;
#if UNITY_EDITOR
            // Runtime edits to a pipeline asset persist in the Editor; put the asset back.
            RestoreAssetValues();
#endif
        }

        /// <summary>Puts the URP asset's renderScale / MSAA / shadow distance / shadow map size back to their startup values.</summary>
        public void RestoreAssetValues()
        {
            if (_urp == null) return;
            _urp.renderScale = _origScale;
            _urp.msaaSampleCount = _origMsaa;
            _urp.shadowDistance = _origShadowDistance;
            _urp.mainLightShadowmapResolution = _origShadowRes;
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused) Resettle();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) Resettle();
        }

        void Resettle()
        {
            if (_phase == Phase.Calibrate || _phase == Phase.Monitor)
                BeginSettle(ResettleSeconds, _phase);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
            {
                float ms = Mathf.Min(dt, 0.25f) * 1000f;
                SmoothedFrameMs += (ms - SmoothedFrameMs) * Mathf.Clamp01(dt / 0.5f);
            }

            _sunTimer -= dt;
            if (_sunTimer <= 0f)
            {
                _sunTimer = SunCheckInterval;
                EnsureSunShadows();
            }

            if (_phase == Phase.Manual) return;

            if (_phase == Phase.Settle)
            {
                _settleLeft -= dt;
                if (_settleLeft <= 0f)
                {
                    _phase = _afterSettle;
                    ResetWindows();
                }
                return;
            }

            float cdt = Mathf.Min(dt, MaxCountedDt);
            _winTime += cdt;
            _winFrames++;

            if (_phase == Phase.Calibrate)
            {
                if (_winTime >= CalibrateSeconds)
                {
                    float avg = _winTime * 1000f / _winFrames;
                    int tier = QualityTier.Current;
                    if (avg > CalibrateDownMs && tier > QualityTier.Low)
                    {
                        _drops[tier]++;
                        StepTo(tier - 1, Phase.Calibrate);
                    }
                    else
                    {
                        _phase = Phase.Monitor;
                        ResetWindows();
                    }
                }
                return;
            }

            // Monitor.
            if (DynamicResolutionEnabled)
            {
                _sTime += cdt;
                _sFrames++;
                if (cdt * 1000f > SlowFrameMs) _sSlow++;
                if (_sTime >= ScaleWindowSeconds) EvaluateScale();
            }

            if (_winTime >= MonitorSeconds) EvaluateTier();
        }

        void EvaluateTier()
        {
            float avg = _winTime * 1000f / Mathf.Max(1, _winFrames);
            _winTime = 0f;
            _winFrames = 0;

            int tier = QualityTier.Current;
            if (avg > MonitorDownMs && tier > QualityTier.Low)
            {
                _drops[tier]++;
                StepTo(tier - 1, Phase.Monitor);
                return;
            }

            bool scaleMaxed = !DynamicResolutionEnabled || _scale >= k_Tiers[tier].Scale - 0.001f;
            if (avg < TierUpMs && tier < QualityTier.High && scaleMaxed && _drops[tier + 1] <= MaxDropsBeforeNoReturn)
            {
                if (++_upStreak >= TierUpWindows) StepTo(tier + 1, Phase.Monitor);
            }
            else
            {
                _upStreak = 0;
            }
        }

        void EvaluateScale()
        {
            float avg = _sTime * 1000f / Mathf.Max(1, _sFrames);
            float slowFraction = _sSlow / (float)Mathf.Max(1, _sFrames);
            _sTime = 0f;
            _sFrames = 0;
            _sSlow = 0;
            if (_windowsSinceScaleUp < 1000) _windowsSinceScaleUp++;

            float max = k_Tiers[QualityTier.Current].Scale;
            if (avg > ScaleDownMs && _scale > MinRenderScale + 0.001f)
            {
                // The last increase did not hold: wait longer before trying again.
                if (_windowsSinceScaleUp <= 2) _upNeeded = Mathf.Min(_upNeeded * 2, ScaleUpWindowsMax);
                float step = avg > ScaleBigDownMs ? ScaleStep * 2f : ScaleStep;
                SetScale(Mathf.Max(MinRenderScale, _scale - step));
                _goodStreak = 0;
            }
            else if (_scale < max - 0.001f &&
                     ((avg < ScaleUpAvgMs && slowFraction < ScaleUpMaxSlowFraction) || avg < TierUpMs))
            {
                if (++_goodStreak >= _upNeeded)
                {
                    SetScale(Mathf.Min(max, _scale + ScaleStep));
                    _goodStreak = 0;
                    _windowsSinceScaleUp = 0;
                }
            }
            else
            {
                _goodStreak = 0;
            }
        }

        void StepTo(int tier, Phase next)
        {
            int cur = QualityTier.Current;
            float baseScale = k_Tiers[tier].Scale;
            // Going down: features get cheaper, so allow a little more resolution back but never above the base.
            float scale = tier < cur ? Mathf.Min(baseScale, Mathf.Max(MinRenderScale, _scale + 0.1f)) : baseScale;
            if (!DynamicResolutionEnabled) scale = baseScale;

            ApplyTier(tier, scale);
            QualityTier.SetAutoTier(tier);
            _upStreak = 0;
            _goodStreak = 0;
            _upNeeded = ScaleUpWindowsMin;
            BeginSettle(ResettleSeconds, next);
        }

        void OnPreferenceChanged()
        {
            for (int i = 0; i < _drops.Length; i++) _drops[i] = 0;
            _upStreak = 0;
            _goodStreak = 0;
            _upNeeded = ScaleUpWindowsMin;

            int tier = QualityTier.Current;
            ApplyTier(tier, k_Tiers[tier].Scale);
            if (QualityTier.IsAuto) BeginSettle(ResettleSeconds, Phase.Monitor);
            else _phase = Phase.Manual;
        }

        void BeginSettle(float seconds, Phase next)
        {
            _phase = Phase.Settle;
            _afterSettle = next;
            _settleLeft = seconds;
            ResetWindows();
        }

        void ResetWindows()
        {
            _winTime = 0f;
            _winFrames = 0;
            _sTime = 0f;
            _sFrames = 0;
            _sSlow = 0;
        }

        // ------------------------------------------------------------------ apply

        void ApplyTier(int tier, float scale)
        {
            TierSpec t = k_Tiers[Mathf.Clamp(tier, 0, k_Tiers.Length - 1)];
            var urp = ResolveUrp();
            if (urp != null)
            {
                if (urp.msaaSampleCount != t.Msaa) urp.msaaSampleCount = t.Msaa;
                if (!Mathf.Approximately(urp.shadowDistance, t.ShadowDistance)) urp.shadowDistance = t.ShadowDistance;
                int res = t.ShadowRes <= 0 ? _origShadowRes : Mathf.Min(t.ShadowRes, _origShadowRes);
                if (urp.mainLightShadowmapResolution != res) urp.mainLightShadowmapResolution = res;
            }
            SetScale(scale);
            EnsureSunShadows(tier);
        }

        void SetScale(float scale)
        {
            scale = Mathf.Clamp(scale, MinRenderScale, MaxRenderScale);
            scale = Mathf.Round(scale / ScaleStep) * ScaleStep; // keep on the 0.05 grid (no float drift)
            _scale = scale;
            RenderScale = scale;
            var urp = ResolveUrp();
            if (urp != null && !Mathf.Approximately(urp.renderScale, scale)) urp.renderScale = scale;
        }

        UniversalRenderPipelineAsset ResolveUrp()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != _urp && urp != null)
            {
                _urp = urp;
                _origScale = urp.renderScale;
                _origMsaa = urp.msaaSampleCount;
                _origShadowDistance = urp.shadowDistance;
                _origShadowRes = urp.mainLightShadowmapResolution;
            }
            return urp;
        }

        void EnsureSunShadows() => EnsureSunShadows(QualityTier.Current);

        void EnsureSunShadows(int tier)
        {
            if (_sun == null || !_sun.isActiveAndEnabled)
            {
                _sun = RenderSettings.sun;
                if (_sun == null)
                {
                    var l = FindFirstObjectByType<Light>();
                    if (l != null && l.type == LightType.Directional) _sun = l;
                }
                if (_sun == null) return;
            }
            // Re-applied every second: Atmosphere.Apply() resets the sun to Soft.
            TierSpec spec = k_Tiers[Mathf.Clamp(tier, 0, k_Tiers.Length - 1)];
            if (_sun.shadows != spec.Shadows) _sun.shadows = spec.Shadows;
            if (!_sun.TryGetComponent(out UniversalAdditionalLightData data))
                data = _sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            if (data.softShadowQuality != spec.Soft) data.softShadowQuality = spec.Soft;
        }
    }
}
