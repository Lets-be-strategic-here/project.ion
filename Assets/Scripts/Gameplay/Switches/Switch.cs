using System;
using System.Collections.Generic;
using Ion.Gameplay.State;
using Ion.Presentation.Motion;
using Ion.Projection;
using UnityEngine;

namespace Ion.Gameplay
{
    /// <summary>
    /// A button or lever that toggles a <see cref="SwitchBoard"/> channel (art bible §5, §11). An
    /// <see cref="Interactable"/>: never sliced; a photo captures or removes it whole, and a pasted copy works
    /// (it shares the channel). Pressing (E, via <see cref="PlayerInteractor"/>) records a rewindable change.
    ///
    /// Visuals are built by PropKit (Lead B) and wired through the public fields: <see cref="Cap"/> travels
    /// 0.03 m on a press (button), <see cref="Handle"/> rotates ±35° (lever), <see cref="Indicators"/> show
    /// Ion when powered and off, Brass when on, Graphite when unpowered, and <see cref="RiseRoot"/> rises
    /// 0.25 m when the switch powers up as a story beat. Every field is optional.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Switch : MonoBehaviour
    {
        public enum SwitchKind { Button, Lever }

        static readonly List<Switch> s_Active = new List<Switch>();
        internal static List<Switch> Active => s_Active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Active.Clear();

        public string Channel;
        public bool Powered = true;
        public SwitchKind Kind = SwitchKind.Button;

        [Header("Interaction")]
        /// <summary>E reaches the switch within this distance of <see cref="FocusPoint"/>.</summary>
        public float InteractRange = 2.25f;
        /// <summary>Where the prompt / range check aims, relative to the transform (the cap of a pedestal button).</summary>
        public Vector3 FocusOffset = new Vector3(0f, 1.05f, 0f);

        [Header("Visual hooks (optional)")]
        public Transform Cap;
        public Vector3 CapTravelLocal = new Vector3(0f, -Feel.ButtonTravel, 0f);
        public Transform Handle;
        public Vector3 HandleAxisLocal = Vector3.right;
        public float HandleAngle = Feel.LeverAngleDeg;
        public Renderer[] Indicators;
        public Material UnpoweredMaterial, OffMaterial, OnMaterial;
        public Transform RiseRoot;
        public float RiseHeight = Feel.PowerUpRise;

        /// <summary>The channel's state (shared by every copy).</summary>
        public bool On => SwitchBoard.Get(Channel);

        /// <summary>Raised when the channel changes (any source: press, rewind, checkpoint, another copy).</summary>
        public event Action<bool> Changed;
        /// <summary>Raised when the switch gains or loses power.</summary>
        public event Action<bool> PoweredChanged;

        /// <summary>World point for range checks and the prompt.</summary>
        public Vector3 FocusPoint => transform.TransformPoint(FocusOffset);

        // Animation state.
        Vector3 _capRest;
        Quaternion _handleRest;
        float _capX, _capV;          // 0 = rest, 1 = fully pressed
        float _pressT = -1f;         // >= 0 while the press-in runs
        float _handleDeg, _handleV;
        Vector3 _riseRest;
        float _riseT = -1f;
        bool _shownOn, _shownPowered;
        bool _visualInit;

        void Awake()
        {
            if (!TryGetComponent<Interactable>(out _)) gameObject.AddComponent<Interactable>();
            if (Cap != null) _capRest = Cap.localPosition;
            if (Handle != null) _handleRest = Handle.localRotation;
            if (RiseRoot != null)
            {
                _riseRest = RiseRoot.localPosition;
                if (!Powered) RiseRoot.localPosition = _riseRest - Vector3.up * RiseHeight;
            }
        }

        void OnEnable()
        {
            if (!s_Active.Contains(this)) s_Active.Add(this);
            SwitchBoard.ChannelChanged += OnChannelChanged;
            // A photo copy (or a re-enabled original) shows the channel's current state at once.
            _handleDeg = On ? HandleAngle : -HandleAngle;
            _handleV = 0f;
            ApplyVisuals(true);
        }

        void OnDisable()
        {
            s_Active.Remove(this);
            SwitchBoard.ChannelChanged -= OnChannelChanged;
        }

        /// <summary>
        /// Toggles the channel (E). False when unpowered. Records a rewindable <c>SwitchChange</c>.
        /// </summary>
        public bool Press()
        {
            if (!Powered || string.IsNullOrEmpty(Channel)) return false;
            bool before = On;
            var history = WorldHistory.Instance;
            PlayerPose safe = WorldHistory.SafePoseNow();
            SwitchBoard.Set(Channel, !before);
            if (history != null)
                history.Push(new SwitchChange { Channel = Channel, Before = before, SafePose = safe });
            _pressT = 0f;
            return true;
        }

        /// <summary>Powers the switch up or down. <paramref name="animate"/>: the T1 wake-up (rise + light, 0.6 s).</summary>
        public void SetPowered(bool powered, bool animate = true)
        {
            if (powered == Powered) return;
            Powered = powered;
            if (RiseRoot != null)
            {
                if (powered && animate) _riseT = 0f;
                else RiseRoot.localPosition = powered ? _riseRest : _riseRest - Vector3.up * RiseHeight;
            }
            ApplyVisuals(true);
            try { PoweredChanged?.Invoke(powered); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void OnChannelChanged(string channel, bool on, bool instant)
        {
            if (channel != Channel) return;
            if (instant) { _handleDeg = on ? HandleAngle : -HandleAngle; _handleV = 0f; }
            ApplyVisuals(true);
            try { Changed?.Invoke(on); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void ApplyVisuals(bool force)
        {
            bool on = On;
            if (!force && _visualInit && on == _shownOn && Powered == _shownPowered) return;
            _visualInit = true;
            _shownOn = on;
            _shownPowered = Powered;
            if (Indicators == null) return;
            Material m = !Powered ? UnpoweredMaterial : on ? OnMaterial : OffMaterial;
            if (m == null) return;
            for (int i = 0; i < Indicators.Length; i++)
                if (Indicators[i] != null && Indicators[i].sharedMaterial != m) Indicators[i].sharedMaterial = m;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Button cap: press-in 0.10 s easeOutQuad, then a spring back.
            if (Cap != null)
            {
                if (_pressT >= 0f)
                {
                    _pressT += dt;
                    _capX = Ease.OutQuad(_pressT / Feel.ButtonPressSeconds);
                    _capV = 0f;
                    if (_pressT >= Feel.ButtonPressSeconds) _pressT = -1f;
                }
                else if (_capX != 0f || _capV != 0f)
                {
                    Spring.Step(ref _capX, ref _capV, 0f, Feel.ButtonReleaseFreq, Feel.ButtonReleaseZeta, dt);
                    if (Spring.Settled(_capX, _capV, 0f, 1e-4f)) { _capX = 0f; _capV = 0f; }
                }
                Cap.localPosition = _capRest + CapTravelLocal * _capX;
            }

            // Lever handle: springs to ±HandleAngle.
            if (Handle != null)
            {
                float target = On ? HandleAngle : -HandleAngle;
                Spring.Step(ref _handleDeg, ref _handleV, target, Feel.ButtonReleaseFreq, Feel.ButtonReleaseZeta, dt);
                Handle.localRotation = _handleRest * Quaternion.AngleAxis(_handleDeg, HandleAxisLocal);
            }

            // Power-up rise (story beat).
            if (RiseRoot != null && _riseT >= 0f)
            {
                _riseT += dt;
                float k = Ease.OutCubic(_riseT / Feel.PowerUpSeconds);
                RiseRoot.localPosition = _riseRest - Vector3.up * (RiseHeight * (1f - k));
                if (_riseT >= Feel.PowerUpSeconds) _riseT = -1f;
            }
        }
    }
}
