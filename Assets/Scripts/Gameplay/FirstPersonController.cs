using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Gameplay
{
    /// <summary>
    /// CharacterController-based first-person movement: WASD/arrows, Shift to sprint, Space to jump,
    /// mouse look (pointer locked). Mouse delta is scaled by sensitivity only, never by deltaTime.
    /// Falling below <see cref="KillY"/> respawns at the last checkpoint (nearest <see cref="PlayerSpawn"/> reached).
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
            s_Sensitivity = -1f;
        }

        [Header("Movement")]
        public float WalkSpeed = 4.5f;
        public float SprintSpeed = 6.75f;
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
        public float KillY = -40f;

        Camera _camera;
        CharacterController _cc;
        bool _inputEnabled = true;

        float _yaw, _pitch;
        Vector3 _horizontalVelocity;
        float _verticalVelocity;
        float _lastGroundedTime = -10f;
        float _jumpPressedTime = -10f;

        bool _wasLocked;
        int _skipLookFrames;

        Vector3 _checkpointPosition;
        float _checkpointYaw;
        float _nextCheckpointScan;

        // Automation (debug harness / tests): movement input injected instead of the keyboard.
        Vector2 _scriptedInput;
        float _scriptedUntil = -1f;
        bool _scriptedHasTarget;
        Vector3 _scriptedTarget;
        float _scriptedArriveRadius;

        public Camera Camera => _camera;

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

        public CharacterController Controller => _cc;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public bool IsGrounded => _cc != null && _cc.isGrounded;
        public Vector3 Velocity => _horizontalVelocity + Vector3.up * _verticalVelocity;
        public Vector3 CheckpointPosition => _checkpointPosition;

        /// <summary>Fired after the player falls out of the world and is put back at the checkpoint.</summary>
        public event System.Action Respawned;

        /// <summary>Number of times the player fell out of the world and was respawned.</summary>
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
            _yaw = Mathf.Repeat(yaw, 360f);
            _pitch = Mathf.Clamp(pitch, -MaxPitch, MaxPitch);
            ApplyRotation();
        }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (_camera == null) _camera = GetComponentInChildren<Camera>(true);
            _yaw = transform.eulerAngles.y;
            _pitch = 0f;
            _checkpointPosition = transform.position;
            _checkpointYaw = _yaw;
            ApplyRotation();
        }

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        /// <summary>Used by <see cref="PlayerFactory"/>; normally the camera is found in Awake.</summary>
        internal void AttachCamera(Camera cam)
        {
            _camera = cam;
            ApplyRotation();
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            HandleCursor(kb, mouse);
            HandleLook(mouse);
            HandleMove(kb);
            HandleCheckpoints();

            if (transform.position.y < KillY)
                Respawn();
        }

        void HandleCursor(Keyboard kb, Mouse mouse)
        {
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (_inputEnabled && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                     Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (locked != _wasLocked)
            {
                // Browsers often report a large bogus delta right after pointer lock changes.
                _skipLookFrames = 2;
                _wasLocked = locked;
            }
        }

        void HandleLook(Mouse mouse)
        {
            if (!_inputEnabled || mouse == null || Cursor.lockState != CursorLockMode.Locked)
                return;

            if (_skipLookFrames > 0)
            {
                _skipLookFrames--;
                return;
            }

            Vector2 delta = mouse.delta.ReadValue() * MouseSensitivity; // NOT * deltaTime
            if (delta.sqrMagnitude < 1e-8f) return;

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

        void HandleMove(Keyboard kb)
        {
            if (_cc == null || !_cc.enabled) return;

            float dt = Time.deltaTime;
            float now = Time.time;

            Vector2 input = Vector2.zero;
            bool sprint = false;
            if (_inputEnabled && kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
                sprint = kb.leftShiftKey.isPressed;
                if (kb.spaceKey.wasPressedThisFrame) _jumpPressedTime = now;
            }
            if (Time.time < _scriptedUntil)
            {
                input = _scriptedInput;
                sprint = false;
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

            float speed = sprint ? SprintSpeed : WalkSpeed;
            Vector3 wish = (transform.forward * input.y + transform.right * input.x) * speed;

            bool grounded = _cc.isGrounded;
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

            _verticalVelocity = Mathf.Max(_verticalVelocity - Gravity * dt, -MaxFallSpeed);

            float accel = grounded ? GroundAcceleration : AirAcceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, wish, accel * dt);

            CollisionFlags flags = _cc.Move((_horizontalVelocity + Vector3.up * _verticalVelocity) * dt);
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

        /// <summary>Overrides the respawn point used when the player falls out of the world.</summary>
        public void SetCheckpoint(Vector3 position, float yaw)
        {
            _checkpointPosition = position;
            _checkpointYaw = yaw;
        }

        /// <summary>Moves the player back to the last checkpoint.</summary>
        public void Respawn()
        {
            Teleport(_checkpointPosition, _checkpointYaw);
            RespawnCount++;
            GameplayUI.Toast("Whoops - back to the last checkpoint");
            Respawned?.Invoke();
        }

        /// <summary>Instantly moves the player (feet position) and sets the view yaw; resets velocity and pitch.</summary>
        public void Teleport(Vector3 position, float yaw)
        {
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
        }
    }
}
