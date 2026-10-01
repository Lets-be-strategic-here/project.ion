using System;
using Ion.Gameplay.State;
using Ion.Presentation.Motion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Gameplay
{
    /// <summary>
    /// CharacterController-based first-person movement: WASD/arrows, Space to jump, mouse look while the
    /// pointer is locked (see <see cref="PointerLock"/> / <see cref="IonInput"/>). There is no sprint: Shift
    /// raises the photo. Mouse delta is scaled by sensitivity only, never by deltaTime.
    /// Falling is handled by <see cref="SafePoseTracker"/> + <see cref="RewindController"/> (R recovers; limbo
    /// recovers by itself); below <see cref="KillY"/> the recovery happens at once.
    /// View feel (art bible §9.1): head bob 0.015 m / 2.3 m stride, a landing-dip spring and FOV springs
    /// (place kick, teleport swell). Reduced motion turns all of them off.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        public const string SensitivityPrefKey = "ion.mouseSensitivity";
        public const float DefaultSensitivity = 0.12f; // degrees per pixel of mouse movement
        public const float MinSensitivity = 0.01f;
        public const float MaxSensitivity = 1f;

        /// <summary>The active player, if any.</summary>
        public static FirstPersonController Current { get; private set; }

        static float s_Sensitivity = -1f;

        /// <summary>Mouse look sensitivity in degrees per pixel. Persisted in PlayerPrefs.</summary>
        public static float MouseSensitivity
        {
            get
            {
                if (s_Sensitivity < 0f)
                    s_Sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityPrefKey, DefaultSensitivity),
                                                MinSensitivity, MaxSensitivity);
                return s_Sensitivity;
            }
            set
            {
                s_Sensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
                PlayerPrefs.SetFloat(SensitivityPrefKey, s_Sensitivity);
                PlayerPrefs.Save();
            }
        }

        public const string HeadBobPrefKey = "ion.headBob";
        static int s_HeadBob = -1;

        /// <summary>Subtle walking head bob and landing dip (on by default). Persisted in PlayerPrefs.</summary>
        public static bool HeadBobEnabled
        {
            get
            {
                if (s_HeadBob < 0) s_HeadBob = PlayerPrefs.GetInt(HeadBobPrefKey, 1) != 0 ? 1 : 0;
                return s_HeadBob == 1;
            }
            set
            {
                s_HeadBob = value ? 1 : 0;
                PlayerPrefs.SetInt(HeadBobPrefKey, s_HeadBob);
                PlayerPrefs.Save();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
            s_Sensitivity = -1f;
            s_HeadBob = -1;
        }

        [Header("Movement")]
        public float WalkSpeed = 4.5f;
        public float GroundAcceleration = 45f;
        public float AirAcceleration = 12f;
        public float Gravity = 24f;
        public float JumpHeight = 1.15f;
        public float CoyoteTime = 0.12f;
        public float JumpBufferTime = 0.12f;
        public float MaxFallSpeed = 50f;

        [Header("Look")]
        public float MaxPitch = 85f;

        [Header("Safety")]
        /// <summary>Below this height the fall recovery happens at once (no limbo wait).</summary>
        public float KillY = -40f;

        [Header("Feel")]
        /// <summary>Vertical bob amplitude (m) at walking speed; sideways sway is about half.</summary>
        public float BobHeight = Feel.HeadBobAmplitude;
        /// <summary>Metres walked per full bob cycle (two footsteps).</summary>
        public float BobStride = Feel.HeadBobStride;

        Camera _camera;
        CharacterController _cc;
        bool _inputEnabled = true;

        float _yaw, _pitch;
        Vector3 _horizontalVelocity;
        float _verticalVelocity;
        float _lastGroundedTime = -10f;
        float _jumpPressedTime = -10f;
        float _fallLimit = -1f;

        Vector3 _checkpointPosition;
        float _checkpointYaw;
        float _nextCheckpointScan;

        // View feel: head bob, landing dip and FOV springs (camera-local; never moves the body).
        Vector3 _eyeLocal = new Vector3(0f, 1.62f, 0f);
        float _baseFov = 70f;
        float _bobPhase, _bobAmount;
        float _dip, _dipVelocity;
        float _fovKick, _fovKickVelocity;
        float _fovFreq = Feel.PlaceFovFreq, _fovZeta = Feel.PlaceFovZeta;
        float _fovHold = float.NaN;
        bool _wasGrounded = true;
        float _fallSpeed;

        // Automation (debug harness / tests): movement input injected instead of the keyboard.
        Vector2 _scriptedInput;
        float _scriptedUntil = -1f;
        bool _scriptedHasTarget;
        Vector3 _scriptedTarget;
        float _scriptedArriveRadius;

        public Camera Camera => _camera;

        /// <summary>Field of view without the transient FOV springs.</summary>
        public float BaseFieldOfView => _baseFov;

        /// <summary>Current camera offset from head bob + landing dip (camera-local metres).</summary>
        public Vector3 ViewBobOffset { get; private set; }

        /// <summary>Normalised walking bob (x = sideways, y = vertical, each -1..1, scaled by how much it is active).</summary>
        public Vector2 BobSignal { get; private set; }

        /// <summary>When false, movement/look/actions are ignored (gravity still applies).</summary>
        public bool InputEnabled
        {
            get => _inputEnabled;
            set
            {
                _inputEnabled = value;
                if (!value) _horizontalVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Rewind in progress: no walking and no gravity (the body hangs exactly where it is), look still
        /// works. Set by <see cref="RewindController"/>.
        /// </summary>
        public bool Frozen
        {
            get => _frozen;
            set
            {
                _frozen = value;
                if (value)
                {
                    _horizontalVelocity = Vector3.zero;
                    _verticalVelocity = 0f;
                }
            }
        }
        bool _frozen;

        /// <summary>Holds the view and the body still (the 0.10 s place press-in). Gravity still applies.</summary>
        public bool HoldStill { get; set; }

        /// <summary>Gravity multiplier (limbo eases it to 15 %).</summary>
        public float GravityScale { get; set; } = 1f;

        public CharacterController Controller => _cc;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public bool IsGrounded => _cc != null && _cc.isGrounded;
        public Vector3 Velocity => _horizontalVelocity + Vector3.up * _verticalVelocity;
        public Vector3 CheckpointPosition => _checkpointPosition;

        /// <summary>Fired after the player was recovered from a fall (R, limbo timeout, last resort).</summary>
        public event Action Respawned;

        /// <summary>Fired after <see cref="Teleport"/> (from, to feet positions); not for silent rewind moves.</summary>
        public event Action<Vector3, Vector3> Teleported;

        /// <summary>Number of times the player was recovered from a fall.</summary>
        public int RespawnCount { get; private set; }

        /// <summary>True while a <see cref="ScriptedWalk"/> / <see cref="ScriptedWalkTo"/> is in progress.</summary>
        public bool IsScriptedWalking => Time.time < _scriptedUntil;

        /// <summary>
        /// Automation: holds movement input (x = strafe right, y = forward, each in [-1, 1]) for
        /// <paramref name="seconds"/>, exactly as if the keys were held. Ignores InputEnabled.
        /// </summary>
        public void ScriptedWalk(Vector2 input, float seconds)
        {
            _scriptedInput = Vector2.ClampMagnitude(input, 1f);
            _scriptedHasTarget = false;
            _scriptedUntil = Time.time + Mathf.Max(0f, seconds);
        }

        /// <summary>
        /// Automation: walks forward toward the world point <paramref name="worldTarget"/> (XZ only),
        /// turning the view toward it every frame, until within <paramref name="arriveRadius"/> or
        /// <paramref name="maxSeconds"/> elapse.
        /// </summary>
        public void ScriptedWalkTo(Vector3 worldTarget, float maxSeconds, float arriveRadius = 0.3f)
        {
            _scriptedTarget = worldTarget;
            _scriptedHasTarget = true;
            _scriptedArriveRadius = Mathf.Max(0.05f, arriveRadius);
            _scriptedInput = new Vector2(0f, 1f);
            _scriptedUntil = Time.time + Mathf.Max(0f, maxSeconds);
        }

        public void StopScriptedWalk()
        {
            _scriptedUntil = -1f;
            _scriptedHasTarget = false;
        }

        /// <summary>Sets the view (yaw in degrees, pitch in degrees, positive = looking down).</summary>
        public void SetLook(float yaw, float pitch)
        {
            _nudgeT = _nudgeSeconds;
            _yaw = Mathf.Repeat(yaw, 360f);
            _pitch = Mathf.Clamp(pitch, -MaxPitch, MaxPitch);
            ApplyRotation();
        }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (_camera == null) _camera = GetComponentInChildren<Camera>(true);
            CaptureCameraBase();
            _yaw = transform.eulerAngles.y;
            _pitch = 0f;
            _checkpointPosition = transform.position;
            _checkpointYaw = _yaw;
            ApplyRotation();
        }

        void OnEnable()
        {
            Current = this;
            PointerLock.FocusLost += OnFocusLost;
        }

        void OnDisable()
        {
            PointerLock.FocusLost -= OnFocusLost;
            if (Current == this) Current = null;
        }

        void OnFocusLost()
        {
            _horizontalVelocity = Vector3.zero;
            _jumpPressedTime = -10f;
        }

        /// <summary>Used by <see cref="PlayerFactory"/>; normally the camera is found in Awake.</summary>
        internal void AttachCamera(Camera cam)
        {
            _camera = cam;
            CaptureCameraBase();
            ApplyRotation();
        }

        void CaptureCameraBase()
        {
            if (_camera == null) return;
            _eyeLocal = _camera.transform.localPosition;
            _baseFov = _camera.fieldOfView;
        }

        // ---------------------------------------------------------------- FOV springs

        /// <summary>Legacy: a place-style FOV kick (degrees, positive = wider) that springs back.</summary>
        public void PunchFov(float degrees) => KickFov(degrees, Feel.PlaceFovFreq, Feel.PlaceFovZeta);

        /// <summary>
        /// An FOV impulse whose peak is about <paramref name="peakDegrees"/>, absorbed by a spring
        /// (<paramref name="freqHz"/>, <paramref name="zeta"/>). Off with reduced motion.
        /// </summary>
        public void KickFov(float peakDegrees, float freqHz, float zeta)
        {
            if (Feel.ReducedMotion || !float.IsNaN(_fovHold)) return;
            _fovFreq = freqHz;
            _fovZeta = zeta;
            _fovKickVelocity += ImpulseForPeak(peakDegrees, freqHz, zeta);
        }

        /// <summary>Holds the FOV offset at <paramref name="degrees"/> (the teleport swell, driven per frame).</summary>
        public void SetFovHold(float degrees)
        {
            if (Feel.ReducedMotion) degrees = 0f;
            _fovHold = degrees;
            _fovKick = degrees;
            _fovKickVelocity = 0f;
        }

        /// <summary>Lets go of <see cref="SetFovHold"/>: the offset springs back to 0.</summary>
        public void ReleaseFovHold(float freqHz, float zeta)
        {
            _fovHold = float.NaN;
            _fovFreq = freqHz;
            _fovZeta = zeta;
        }

        /// <summary>Initial velocity that makes a spring at rest peak at <paramref name="peak"/>.</summary>
        static float ImpulseForPeak(float peak, float freqHz, float zeta)
        {
            float w = 2f * Mathf.PI * Mathf.Max(0.01f, freqHz);
            if (zeta >= 0.999f) return peak * w * Mathf.Exp(1f);      // critical: peak = v0 / (w e)
            float s = Mathf.Sqrt(1f - zeta * zeta);
            float phase = Mathf.Atan2(s, zeta);
            float k = Mathf.Exp(-zeta / s * phase);                  // peak = v0 / w * k
            return peak * w / Mathf.Max(1e-3f, k);
        }

        /// <summary>
        /// Puts the camera back at its exact eye position and base FOV right now (no bob, dip or springs).
        /// Called before anything uses the camera pose for gameplay (placing, capturing).
        /// </summary>
        public void ResetViewEffects()
        {
            _bobAmount = 0f;
            _dip = _dipVelocity = 0f;
            if (float.IsNaN(_fovHold)) _fovKick = _fovKickVelocity = 0f;
            ApplyViewEffects();
        }

        void UpdateViewEffects(float dt)
        {
            if (dt <= 0f) return;
            bool on = HeadBobEnabled && !Feel.ReducedMotion;
            bool grounded = _cc != null && _cc.isGrounded;

            // Bob: phase follows the distance walked; amplitude follows speed (and fades in the air).
            Vector3 hv = _horizontalVelocity;
            hv.y = 0f;
            float speed = hv.magnitude;
            float target = on && grounded ? Mathf.Clamp01(speed / WalkSpeed) : 0f;
            _bobAmount = Mathf.MoveTowards(_bobAmount, target, dt * (target > _bobAmount ? 3f : 5f));
            _bobPhase = Mathf.Repeat(_bobPhase + speed * dt * (2f * Mathf.PI / Mathf.Max(0.5f, BobStride)), 2f * Mathf.PI);
            if (_bobAmount < 1e-3f) _bobPhase = Mathf.MoveTowards(_bobPhase, _bobPhase < Mathf.PI ? 0f : 2f * Mathf.PI, dt * 4f);

            // Landing dip: depth 0.03 m per m/s above 4 m/s (max 0.09), on a soft spring.
            if (grounded && !_wasGrounded && on && _fallSpeed > Feel.LandingMinSpeed)
            {
                float depth = Mathf.Min((_fallSpeed - Feel.LandingMinSpeed) * Feel.LandingDepthPerMps, Feel.LandingMaxDepth);
                _dipVelocity += ImpulseForPeak(depth, Feel.LandingFreq, Feel.LandingZeta);
            }
            _wasGrounded = grounded;
            Spring.Step(ref _dip, ref _dipVelocity, 0f, Feel.LandingFreq, Feel.LandingZeta, dt);
            _dip = Mathf.Clamp(_dip, -0.04f, Feel.LandingMaxDepth + 0.02f);

            // FOV spring (held during a teleport swell).
            if (float.IsNaN(_fovHold))
            {
                Spring.Step(ref _fovKick, ref _fovKickVelocity, 0f, _fovFreq, _fovZeta, dt);
                _fovKick = Mathf.Clamp(_fovKick, -8f, 8f);
            }

            ApplyViewEffects();
        }

        void ApplyViewEffects()
        {
            float sx = Mathf.Sin(_bobPhase), sy = Mathf.Sin(_bobPhase * 2f);
            BobSignal = new Vector2(sx, sy) * _bobAmount;
            var offset = new Vector3(sx * BobHeight * 0.55f * _bobAmount, sy * BobHeight * _bobAmount - _dip, 0f);
            ViewBobOffset = offset;
            if (_camera == null) return;
            _camera.transform.localPosition = _eyeLocal + offset;
            float fov = _baseFov + _fovKick;
            if (!Mathf.Approximately(_camera.fieldOfView, fov)) _camera.fieldOfView = fov;
        }

        // ---------------------------------------------------------------- frame

        void Update()
        {
            var kb = Keyboard.current;

            HandleLook();
            HandleMove(kb);
            UpdateViewEffects(Time.deltaTime);
            HandleCheckpoints();

            // Hard floor: recover at once (the limbo wait is for falls above this).
            if (transform.position.y < KillY && !_frozen)
            {
                var tracker = GetComponent<SafePoseTracker>();
                var history = WorldHistory.Instance;
                if (tracker != null && history != null)
                {
                    if (tracker.IsFalling || tracker.InLimbo) history.RewindOnce();
                }
                else
                {
                    Respawn();
                }
            }
        }

        void HandleLook()
        {
            if (!_inputEnabled || HoldStill) return;
            Vector2 px = IonInput.LookDelta;
            if (px.sqrMagnitude < 1e-8f) return;
            Vector2 delta = px * MouseSensitivity; // NOT * deltaTime
            _yaw = Mathf.Repeat(_yaw + delta.x, 360f);
            _pitch = Mathf.Clamp(_pitch - delta.y, -MaxPitch, MaxPitch);
            ApplyRotation();
        }

        void ApplyRotation()
        {
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (_camera != null)
                _camera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        Vector3 _nudgeVelocity;
        float _nudgeT = 1f, _nudgeSeconds = 1f, _nudgePitch, _nudgePitchDone;
        bool _noAirControl;

        /// <summary>
        /// A gentle shove that eases off over <paramref name="seconds"/> (e.g. away from a wall when the floor gives
        /// way), optionally easing the view <paramref name="pitchDownDegrees"/> down over the same time.
        /// </summary>
        public void Nudge(Vector3 horizontalVelocity, float seconds, float pitchDownDegrees = 0f)
        {
            horizontalVelocity.y = 0f;
            _nudgeVelocity = horizontalVelocity;
            _nudgeSeconds = Mathf.Max(0.05f, seconds);
            _nudgeT = 0f;
            _nudgePitch = pitchDownDegrees;
            _nudgePitchDone = 0f;
            _noAirControl = true;
            _horizontalVelocity = Vector3.zero;
        }

        /// <summary>Caps the downward speed (limbo drift); pass <see cref="MaxFallSpeed"/> to lift the cap.</summary>
        public void LimitFallSpeed(float metresPerSecond)
        {
            _fallLimit = metresPerSecond >= MaxFallSpeed ? -1f : Mathf.Max(0f, metresPerSecond);
        }

        void HandleMove(Keyboard kb)
        {
            if (_cc == null || !_cc.enabled) return;
            if (_frozen)
            {
                _horizontalVelocity = Vector3.zero;
                _verticalVelocity = 0f;
                _nudgeT = _nudgeSeconds;   // a rewind / teleport cancels any shove
                _noAirControl = false;
                return;
            }

            float dt = Time.deltaTime;
            float now = Time.time;

            Vector2 input = Vector2.zero;
            if (_inputEnabled && !HoldStill && kb != null && IonInput.Active)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
                if (kb.spaceKey.wasPressedThisFrame) _jumpPressedTime = now;
            }
            if (Time.time < _scriptedUntil)
            {
                input = _scriptedInput;
                if (_scriptedHasTarget)
                {
                    Vector3 to = _scriptedTarget - transform.position;
                    to.y = 0f;
                    if (to.magnitude <= _scriptedArriveRadius)
                    {
                        StopScriptedWalk();
                        input = Vector2.zero;
                    }
                    else
                    {
                        _yaw = Mathf.Repeat(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 360f);
                        ApplyRotation();
                    }
                }
            }
            if (input.sqrMagnitude > 1f) input.Normalize();

            Vector3 wish = (transform.forward * input.y + transform.right * input.x) * WalkSpeed;

            bool grounded = _cc.isGrounded;
            // After the floor gave way (Nudge): no air control until the player lands, so a held W does not drag
            // the camera down the island face (a flat wall filling the screen read as a camera bug).
            if (_noAirControl)
            {
                if (grounded && _nudgeT >= _nudgeSeconds) _noAirControl = false;
                else wish = Vector3.zero;
            }
            if (grounded)
            {
                _lastGroundedTime = now;
                if (_verticalVelocity < 0f) _verticalVelocity = -2f; // keep snapped to the ground
            }

            if (now - _jumpPressedTime <= JumpBufferTime && now - _lastGroundedTime <= CoyoteTime)
            {
                _verticalVelocity = Mathf.Sqrt(2f * Gravity * JumpHeight);
                _jumpPressedTime = -10f;
                _lastGroundedTime = -10f;
            }

            float maxFall = _fallLimit >= 0f ? _fallLimit : MaxFallSpeed;
            float v = _verticalVelocity - Gravity * GravityScale * dt;
            if (v < -maxFall)
            {
                // Ease into a lowered cap instead of snapping (limbo slows the fall gently).
                v = _fallLimit >= 0f ? Mathf.MoveTowards(_verticalVelocity, -maxFall, Gravity * dt * 1.5f) : -maxFall;
            }
            _verticalVelocity = v;
            if (!grounded) _fallSpeed = Mathf.Max(0f, -_verticalVelocity);

            float accel = grounded ? GroundAcceleration : AirAcceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, wish, accel * dt);

            Vector3 nudge = Vector3.zero;
            if (_nudgeT < _nudgeSeconds)
            {
                _nudgeT += dt;
                float k = Mathf.Clamp01(_nudgeT / _nudgeSeconds);
                nudge = _nudgeVelocity * (1f - k * k);   // eases off
                if (_nudgePitch != 0f)
                {
                    // Ease the view down a little while the nudge lasts (a fall reads as a fall, not a wall).
                    float target = Mathf.SmoothStep(0f, _nudgePitch, k);
                    _pitch = Mathf.Clamp(_pitch + (target - _nudgePitchDone), -MaxPitch, MaxPitch);
                    _nudgePitchDone = target;
                    ApplyRotation();
                }
            }
            CollisionFlags flags = _cc.Move((_horizontalVelocity + nudge + Vector3.up * _verticalVelocity) * dt);
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f)
                _verticalVelocity = 0f;
        }

        void HandleCheckpoints()
        {
            if (Time.time < _nextCheckpointScan) return;
            _nextCheckpointScan = Time.time + 0.25f;
            if (!_cc.isGrounded) return;

            Vector3 p = transform.position;
            var spawns = PlayerSpawn.All;
            for (int i = 0; i < spawns.Count; i++)
            {
                var s = spawns[i];
                if (s == null) continue;
                Vector3 sp = s.transform.position;
                float r = s.CheckpointRadius;
                if ((sp - p).sqrMagnitude <= r * r)
                {
                    _checkpointPosition = sp;
                    _checkpointYaw = s.transform.eulerAngles.y;
                    break;
                }
            }
        }

        /// <summary>
        /// Placement/capture assist: if the view is within <paramref name="toleranceDegrees"/> of level,
        /// snap it exactly level (visibly, so what the player sees matches what is placed).
        /// </summary>
        public void SnapPitchLevel(float toleranceDegrees)
        {
            if (_pitch == 0f || Mathf.Abs(_pitch) > toleranceDegrees) return;
            _pitch = 0f;
            ApplyRotation();
        }

        /// <summary>Legacy respawn point (a <see cref="PlayerSpawn"/> nearby); used only as a last fallback.</summary>
        public void SetCheckpoint(Vector3 position, float yaw)
        {
            _checkpointPosition = position;
            _checkpointYaw = yaw;
        }

        /// <summary>Last-resort move back to the legacy spawn checkpoint (when there is no rewind history).</summary>
        public void Respawn()
        {
            Teleport(_checkpointPosition, _checkpointYaw);
            NotifyFallRecovered();
        }

        /// <summary>
        /// Called after a fall recovery (and checkpoint restores, with <paramref name="countsAsRespawn"/> false):
        /// lifts limbo gravity, counts the respawn and raises <see cref="Respawned"/>.
        /// </summary>
        public void NotifyFallRecovered(bool countsAsRespawn = true)
        {
            GravityScale = 1f;
            _fallLimit = -1f;
            _verticalVelocity = 0f;
            _fallSpeed = 0f;
            if (!countsAsRespawn) return;
            RespawnCount++;
            try { Respawned?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Instantly moves the player (feet position) and sets the view yaw; resets velocity and pitch.</summary>
        public void Teleport(Vector3 position, float yaw) => Teleport(position, yaw, false);

        /// <summary>
        /// Instantly moves the player. <paramref name="silent"/>: a rewind's own pose move (no
        /// <see cref="Teleported"/>, so it is not mistaken for a zone entry).
        /// </summary>
        public void Teleport(Vector3 position, float yaw, bool silent)
        {
            _nudgeT = _nudgeSeconds;
            _noAirControl = false;
            Vector3 from = transform.position;
            // A CharacterController overrides transform writes while enabled.
            if (_cc != null) _cc.enabled = false;
            transform.position = position;
            _yaw = Mathf.Repeat(yaw, 360f);
            _pitch = 0f;
            ApplyRotation();
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            StopScriptedWalk();
            if (_cc != null) _cc.enabled = true;
            _wasGrounded = true;
            _fallSpeed = 0f;
            ResetViewEffects();
            if (silent) return;
            try { Teleported?.Invoke(from, position); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
