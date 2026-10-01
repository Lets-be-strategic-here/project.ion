using System;
using Ion.Presentation.Motion;
using Ion.Projection;
using UnityEngine;

namespace Ion.Gameplay.State
{
    /// <summary>
    /// Remembers the last pose where the player stood safely, and decides when they are falling or in limbo
    /// (art bible §11.2):
    ///  * safe samples every 0.2 s while grounded on a floor (normal.y &gt; 0.7) that is not a Mover or a
    ///    StoryCollapse;
    ///  * falling = not grounded and below LastSafe − 3 m, or below the zone's void height;
    ///  * limbo = below the void height: gravity eases to 15 % and the fall slows, the screen vignettes, and
    ///    R (or 4 s without input, see <see cref="RewindController"/>) brings the player back.
    ///  * below <see cref="ZoneInfo.ResetY"/> the recovery happens at once (last resort).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FirstPersonController))]
    public sealed class SafePoseTracker : MonoBehaviour
    {
        public const float SampleInterval = 0.2f;
        public const float FallDrop = 3f;
        public const float MinFloorNormalY = 0.7f;
        /// <summary>Fall speed limbo eases toward (m/s, down).</summary>
        public const float LimboFallSpeed = 1.5f;

        const int WorldMask = ~ProjectionSystem.ExcludedLayerMask;

        FirstPersonController _fpc;
        PlayerPose _lastSafe;
        bool _hasSafe;
        float _nextSample;
        float _limboTime;

        public PlayerPose LastSafe => _hasSafe ? _lastSafe : PlayerPose.Of(_fpc);
        public bool IsFalling { get; private set; }
        public bool InLimbo { get; private set; }
        /// <summary>Seconds spent in limbo (0 when not in limbo).</summary>
        public float LimboSeconds => InLimbo ? _limboTime : 0f;

        /// <summary>Raised when the player enters (true) or leaves (false) limbo.</summary>
        public event Action<bool> LimboChanged;
        /// <summary>Raised when the falling state starts (true) or ends (false).</summary>
        public event Action<bool> FallingChanged;

        void Awake()
        {
            _fpc = GetComponent<FirstPersonController>();
        }

        void OnEnable()
        {
            if (_fpc != null) _fpc.Teleported += OnTeleported;
        }

        void OnDisable()
        {
            if (_fpc != null) _fpc.Teleported -= OnTeleported;
        }

        /// <summary>A teleport (zone entry, debug move) starts a fresh safe pose where the player lands.</summary>
        void OnTeleported(Vector3 from, Vector3 to) => Reset(PlayerPose.Of(_fpc));

        void Start()
        {
            if (!_hasSafe) Reset(PlayerPose.Of(_fpc));
        }

        /// <summary>The current pose when standing safely right now, otherwise <see cref="LastSafe"/>.</summary>
        public PlayerPose CurrentSafe()
        {
            if (_fpc != null && !IsFalling && IsSafeGround(out _))
                return PlayerPose.Of(_fpc);
            return LastSafe;
        }

        /// <summary>Starts over from <paramref name="pose"/> (after a rewind, a teleport or a restart).</summary>
        public void Reset(PlayerPose pose)
        {
            _lastSafe = pose;
            _hasSafe = true;
            _nextSample = Time.time + SampleInterval;
            SetFalling(false);
            SetLimbo(false);
        }

        void Update()
        {
            if (_fpc == null) return;
            Vector3 feet = transform.position;
            int zone = ZoneInfo.ZoneOf(feet);
            bool grounded = _fpc.IsGrounded;

            if (grounded && Time.time >= _nextSample)
            {
                _nextSample = Time.time + SampleInterval;
                if (IsSafeGround(out _))
                {
                    _lastSafe = PlayerPose.Of(_fpc);
                    _hasSafe = true;
                }
            }

            float voidY = ZoneInfo.VoidY(zone);
            bool below = _hasSafe && feet.y < _lastSafe.Feet.y - FallDrop;
            bool falling = !grounded && (below || feet.y < voidY);
            if (grounded) falling = false;
            SetFalling(falling);
            SetLimbo(!grounded && feet.y < voidY);

            if (InLimbo)
            {
                _limboTime += Time.deltaTime;
                // Gravity eases to 15 %, and the fall itself slows to a drift.
                float k = Ease.OutCubic(_limboTime / Feel.LimboInSeconds);
                _fpc.GravityScale = Mathf.Lerp(1f, Feel.LimboGravityScale, k);
                _fpc.LimitFallSpeed(Mathf.Lerp(_fpc.MaxFallSpeed, LimboFallSpeed, k));
            }

            if (feet.y < ZoneInfo.ResetY)
            {
                // Last resort: recover at once, whatever the presentation is doing.
                var history = WorldHistory.Instance;
                if (history != null) history.RewindOnce();
            }
        }

        void SetFalling(bool on)
        {
            if (on == IsFalling) return;
            IsFalling = on;
            try { FallingChanged?.Invoke(on); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void SetLimbo(bool on)
        {
            if (on == InLimbo) return;
            InLimbo = on;
            _limboTime = 0f;
            if (!on && _fpc != null)
            {
                _fpc.GravityScale = 1f;
                _fpc.LimitFallSpeed(_fpc.MaxFallSpeed);
            }
            try { LimboChanged?.Invoke(on); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Grounded on a walkable floor that is neither a Mover nor a StoryCollapse.</summary>
        bool IsSafeGround(out Collider ground)
        {
            ground = null;
            if (_fpc == null || !_fpc.IsGrounded) return false;
            float r = PlayerFactory.Radius * 0.9f;
            Vector3 origin = transform.position + Vector3.up * (r + 0.05f);
            if (!Physics.SphereCast(origin, r, Vector3.down, out RaycastHit hit, 0.3f, WorldMask, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.normal.y <= MinFloorNormalY || !IsStable(hit.collider)) return false;
            // The feet themselves must be over a stable floor (not just the capsule's rim on a ledge lip,
            // nor a slab that is about to go).
            if (!Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit under, 0.45f, WorldMask, QueryTriggerInteraction.Ignore))
                return false;
            if (under.normal.y <= MinFloorNormalY || !IsStable(under.collider)) return false;
            ground = under.collider;
            return true;
        }

        static bool IsStable(Collider c)
        {
            if (c == null) return false;
            if (c.GetComponentInParent<Mover>() != null) return false;
            if (c.GetComponentInParent<StoryCollapse>() != null) return false;
            return true;
        }
    }
}
