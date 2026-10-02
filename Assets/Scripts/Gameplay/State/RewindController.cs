using System;
using System.Collections.Generic;
using Ion.Presentation;
using Ion.Presentation.Motion;
using Ion.Projection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Gameplay.State
{
    /// <summary>
    /// R input and the rewind transitions (art bible §9.1, §9.2, §11):
    ///  * R works anytime (a raised photo is lowered first, in the same press).
    ///  * Single R that undoes a change: the player GLIDES back to the exact pose the change was made from
    ///    (<see cref="WorldChange.Pose"/>), wherever they stand now. Position follows a smootherstep (slow start,
    ///    fast middle, gentle settle) over 0.8 s + 0.04 s/m (at most 1.6 s); the look slerps on the same curve;
    ///    the CharacterController is off (no collisions, no gravity, no input) and the path lifts into a slight
    ///    arc when the straight line would pass through geometry. The world undo happens at 0.24 s, after the
    ///    pasted pieces have un-developed, so the change visibly dissolves while the player glides back. The
    ///    desaturation, tape bands, vignette (ScreenFx) and the tape sound (IonAudio) follow the glide's speed;
    ///    a returned photo flies back to its slot and lands as the glide settles.
    ///  * Successive single R presses walk back through the poses in order. A press during a glide is buffered
    ///    (<see cref="Feel.RewindQueueMax"/>) and plays when the glide settles; within 0.35 s of the previous
    ///    press it escalates to the checkpoint instead.
    ///  * Falling / limbo: the recovery to the last safe pose keeps its Paper fade (the move happens under it).
    ///  * A second R within 0.35 s escalates to the checkpoint: brackets close from the screen edges,
    ///    everything (pose included) is restored at 0.40 s, they open again.
    ///  * Nothing to rewind: the HUD [R] chip shakes and a gentle toast says so (with the R R hint the first
    ///    3 times). No camera motion.
    ///  * Limbo: 4 s without input recovers automatically.
    /// The player is frozen (no walking, no gravity) while a rewind plays. Timing follows game time, so
    /// fixed-step tests see the same frames as a player.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FirstPersonController))]
    public sealed class RewindController : MonoBehaviour
    {
        public const float DoubleTapWindow = 0.35f;

        const int WorldMask = ~ProjectionSystem.ExcludedLayerMask;
        const QueryTriggerInteraction IgnoreTriggers = QueryTriggerInteraction.Ignore;

        public static RewindController Instance { get; private set; }

        /// <summary>
        /// A rewind transition starts (single: Undid = a glide, RecoveredFall = the Paper fade; double:
        /// ToCheckpoint). The world swaps <see cref="Feel.RewindGlideUndoAt"/> / <see cref="Feel.RewindSwapAt"/> /
        /// <see cref="Feel.CheckpointSwapAt"/> later. "Nothing" is not a transition: listen to
        /// <see cref="WorldHistory.Rewound"/> for it.
        /// </summary>
        public static event Action<RewindResult> Started;

        /// <summary>A glide settled (the player stands at the restored pose again).</summary>
        public static event Action GlideEnded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Started = null;
            GlideEnded = null;
        }

        static void RaiseStarted(RewindResult r)
        {
            try { Started?.Invoke(r); }
            catch (Exception e) { Debug.LogException(e); }
        }

        enum Phase { Idle, Single, Glide, Checkpoint }

        FirstPersonController _fpc;
        PhotoHolder _holder;
        InstantCamera _camera;
        SafePoseTracker _tracker;

        Phase _phase;
        float _t;                  // seconds into the current sequence
        bool _swapped;             // the world swap / undo of the current sequence has happened
        float _movedAt = -1f;      // single (fall): when the pose move happened (sequence time)
        float _lastPressTime = -10f;
        bool _lastPressEscalates;  // the previous press may escalate into a checkpoint restore
        int _queued;               // presses buffered during a transition
        float _idleInLimbo;
        int _nothingHints;

        // Glide.
        PlayerPose _from, _to;
        Quaternion _lookFrom, _lookTo;
        float _glideSeconds;
        float _arc, _arcTarget;
        float _cover;
        readonly List<Renderer> _undevelop = new List<Renderer>(32);

        /// <summary>True while a rewind transition plays.</summary>
        public bool Busy => _phase != Phase.Idle;

        /// <summary>True while the player glides back to a change's pose.</summary>
        public bool IsGliding => _phase == Phase.Glide;

        /// <summary>Normalised glide time 0..1 (0 when not gliding).</summary>
        public float GlideProgress => IsGliding ? Mathf.Clamp01(_t / _glideSeconds) : 0f;

        /// <summary>Position along the glide path 0..1 (the smootherstep of <see cref="GlideProgress"/>).</summary>
        public float GlideEased => IsGliding ? Feel.RewindGlideEase(GlideProgress) : 0f;

        /// <summary>Glide speed, normalised to 1 at its peak (0 when not gliding). The tape sound follows it.</summary>
        public float GlideSpeed01 => IsGliding ? Ease.SmootherstepSpeed(GlideProgress) : 0f;

        /// <summary>Rewind effect strength 0..1 (<see cref="Feel.RewindFxEnvelope"/>; 0 when not gliding).</summary>
        public float GlideFx => IsGliding ? Feel.RewindFxEnvelope(GlideProgress) : 0f;

        /// <summary>Duration of the current glide (s).</summary>
        public float GlideSeconds => IsGliding ? _glideSeconds : 0f;

        /// <summary>Seconds until the current glide settles (0 when not gliding).</summary>
        public float GlideRemaining => IsGliding ? Mathf.Max(0f, _glideSeconds - _t) : 0f;

        /// <summary>0..1 Paper veil while the eye passes through a surface (the arc could not avoid it).</summary>
        public float GlideCover => IsGliding ? _cover : 0f;

        /// <summary>Where the current glide ends.</summary>
        public PlayerPose GlideTarget => _to;

        /// <summary>Lift of the current glide's arc at its middle (m; 0 = straight).</summary>
        public float GlideArc => _arcTarget;

        /// <summary>Presses buffered while a transition plays (debug / tests).</summary>
        public int QueuedPresses => _queued;

        /// <summary>Result of the most recent rewind (debug / tests).</summary>
        public RewindResult LastResult { get; private set; } = RewindResult.Nothing;

        /// <summary>Number of rewinds completed (debug / tests).</summary>
        public int RewindCount { get; private set; }

        void Awake()
        {
            _fpc = GetComponent<FirstPersonController>();
            _holder = GetComponent<PhotoHolder>();
            _camera = GetComponent<InstantCamera>();
            _tracker = GetComponent<SafePoseTracker>();
        }

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            _queued = 0;
            EndSequence();
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (_holder == null) _holder = GetComponent<PhotoHolder>();
            if (_tracker == null) _tracker = GetComponent<SafePoseTracker>();

            var kb = Keyboard.current;
            bool inputOn = _fpc == null || _fpc.InputEnabled;
            if (inputOn && IonInput.Active && kb != null && kb.rKey.wasPressedThisFrame)
                PressRewind();

            // Limbo: recover by itself after a while without input.
            if (_tracker != null && _tracker.InLimbo && _phase == Phase.Idle)
            {
                bool anyInput = kb != null && kb.anyKey.isPressed;
                _idleInLimbo = anyInput ? 0f : _idleInLimbo + Time.deltaTime;
                if (_idleInLimbo >= Feel.LimboAutoRecoverSeconds)
                {
                    _idleInLimbo = 0f;
                    StartSingle(RewindResult.RecoveredFall);
                }
            }
            else
            {
                _idleInLimbo = 0f;
            }

            Advance(Time.deltaTime);
        }

        /// <summary>
        /// One press of R (the player's path; also used by the debug harness). The first press acts at once;
        /// a second press within <see cref="DoubleTapWindow"/> escalates to the last checkpoint. A press while a
        /// rewind plays is buffered and plays when it settles.
        /// </summary>
        public void PressRewind()
        {
            float now = Time.time;
            bool escalate = _lastPressEscalates && now - _lastPressTime <= DoubleTapWindow;
            _lastPressTime = now;

            // A raised photo / viewfinder is lowered first, in the same press.
            if (_holder != null && _holder.IsRaised) _holder.Lower();
            if (_camera != null && _camera.IsRaised) IonInput.ConsumeRaise(); // viewfinder down, camera stays out

            if (escalate)
            {
                _lastPressEscalates = false;
                StartCheckpoint();
                return;
            }

            if (_phase == Phase.Checkpoint) return;   // already restoring
            _lastPressEscalates = true;
            if (_phase != Phase.Idle)
            {
                // A glide or a fall recovery is playing: never cut it short; play this press when it settles.
                if (_queued < Feel.RewindQueueMax) _queued++;
                return;
            }
            StartFromIdle();
        }

        /// <summary>Starts a full single rewind (with its transition) right now, or after the one playing.</summary>
        public void RewindNow()
        {
            _lastPressEscalates = false;
            if (_phase == Phase.Idle) StartFromIdle();
            else if (_phase != Phase.Checkpoint && _queued < Feel.RewindQueueMax) _queued++;
        }

        /// <summary>Starts a full checkpoint restore (with its transition) right now.</summary>
        public void RewindToCheckpointNow() => StartCheckpoint();

        void StartFromIdle()
        {
            var history = WorldHistory.Instance;
            RewindResult peek = history != null ? history.PeekRewind() : RewindResult.Nothing;
            switch (peek)
            {
                case RewindResult.RecoveredFall:
                    StartSingle(peek);
                    break;
                case RewindResult.Undid:
                    StartGlide();
                    break;
                default:
                    if (history != null) history.RewindOnce(); // raises Rewound(Nothing) for audio / HUD
                    NothingFeedback();
                    LastResult = RewindResult.Nothing;
                    break;
            }
        }

        void NothingFeedback()
        {
            var hud = Hud.Instance;
            if (hud == null) return;
            hud.ShakeRewindChip();
            bool hint = _nothingHints < Feel.NothingHintTimes;
            if (hint) _nothingHints++;
            var history = WorldHistory.Instance;
            bool haveCheckpoint = history != null && history.LastCheckpoint != null;
            hud.Toast(hint && haveCheckpoint
                ? "Nothing to rewind.   " + UIUtil.Key("R") + " " + UIUtil.Key("R") + "  back to the checkpoint"
                : "Nothing to rewind", hint ? 2.6f : Feel.NothingToastSeconds);
        }

        // ---------------------------------------------------------------- sequences

        /// <summary>Fall recovery: the move to the last safe pose happens under a Paper fade.</summary>
        void StartSingle(RewindResult expected)
        {
            _phase = Phase.Single;
            _t = 0f;
            _swapped = false;
            _movedAt = -1f;
            Freeze(true);
            RaiseStarted(expected);
            var fx = ScreenFx.Instance;
            if (fx != null)
            {
                fx.PlayRewindWash(false);
                fx.FadePaper(Feel.RewindSwapAt - Feel.RewindMoveFadeIn);
            }
        }

        /// <summary>Undo: glide back to the top change's pose; the undo itself happens a moment in.</summary>
        void StartGlide()
        {
            var history = WorldHistory.Instance;
            if (history == null || _fpc == null) return;
            WorldChange top = history.Top;
            history.PeekReturnPose(out PlayerPose target);

            _phase = Phase.Glide;
            _t = 0f;
            _swapped = false;
            _from = PlayerPose.Of(_fpc);
            _to = target;
            _lookFrom = Look(_from.Yaw, _from.Pitch);
            _lookTo = Look(_to.Yaw, _to.Pitch);
            _glideSeconds = Feel.RewindGlideSeconds(Vector3.Distance(_from.Feet, _to.Feet));
            _arcTarget = ChooseArc(_from.Feet, _to.Feet, 0f);
            _arc = _arcTarget;   // the arc term is 0 at the start, so it can start at its full height
            _cover = 0f;

            // The pasted pieces un-develop (a reverse FreshPulse) before the undo removes them.
            _undevelop.Clear();
            if (top != null && top.Kind == ChangeKind.Placement)
            {
                var ps = ProjectionSystem.Instance;
                if (ps != null) ps.GetTopPastedRenderers(_undevelop);
                var pulse = FreshPulse.Instance;
                if (pulse != null && _undevelop.Count > 0) pulse.Undevelop(_undevelop, Feel.RewindUndevelopSeconds);
            }

            Freeze(true);
            _fpc.BeginGlide();
            RaiseStarted(RewindResult.Undid);
        }

        void StartCheckpoint()
        {
            // A rewind still in flight is covered by the checkpoint restore: a glide stops where it is (the
            // checkpoint pose wins), a pending Paper fade is dropped, buffered presses are forgotten.
            var fx0 = ScreenFx.Instance;
            if (fx0 != null && _phase == Phase.Single) fx0.CancelPaper();
            if (_phase == Phase.Glide) StopGlideHere();
            _queued = 0;
            _phase = Phase.Checkpoint;
            _t = 0f;
            _swapped = false;
            Freeze(true);
            RaiseStarted(RewindResult.ToCheckpoint);
            var fx = ScreenFx.Instance;
            if (fx != null) fx.PlayCheckpointIris();
        }

        void Advance(float dt)
        {
            if (_phase == Phase.Idle) return;
            dt = Mathf.Min(dt, 0.05f);
            _t += dt;
            var history = WorldHistory.Instance;

            switch (_phase)
            {
                case Phase.Single:
                {
                    if (!_swapped && _t >= Feel.RewindSwapAt)
                    {
                        // Under the Paper fade (already at its peak).
                        _swapped = true;
                        LastResult = history != null ? history.RewindOnce() : RewindResult.Nothing;
                        RewindCount++;
                        _movedAt = _t;
                        var fx = ScreenFx.Instance;
                        if (fx != null) fx.ReleasePaper(Feel.RewindMoveHold);
                    }
                    // The sequence ends once the wash is over and the move has happened (plus the Paper hold).
                    float end = Feel.RewindSeconds;
                    if (_movedAt >= 0f) end = Mathf.Max(end, _movedAt + Feel.RewindMoveHold + 0.05f);
                    if (_swapped && _t >= end) FinishSequence();
                    break;
                }
                case Phase.Glide:
                    AdvanceGlide(history, dt);
                    break;
                case Phase.Checkpoint:
                    if (!_swapped && _t >= Feel.CheckpointSwapAt)
                    {
                        _swapped = true;
                        LastResult = history != null ? history.RewindToCheckpoint() : RewindResult.Nothing;
                        RewindCount++;
                    }
                    if (_swapped && _t >= Feel.CheckpointSeconds) FinishSequence();
                    break;
            }
        }

        // ---------------------------------------------------------------- glide

        void AdvanceGlide(WorldHistory history, float dt)
        {
            if (_fpc == null) { FinishSequence(); return; }
            if (!_fpc.IsGliding)
            {
                // Something else moved the player mid-glide (a restart, a debug teleport): it wins.
                _queued = 0;
                EndSequence();
                return;
            }

            if (!_swapped && _t >= Feel.RewindGlideUndoAt)
            {
                _swapped = true;
                PlayerPose resolved = _to;
                RewindResult r = history != null ? history.UndoTop(out resolved) : RewindResult.Nothing;
                LastResult = r;
                RewindCount++;
                if (r == RewindResult.Undid)
                {
                    Retarget(resolved);
                    // The restored world may block the rest of the way (a wall the placement had cut).
                    Physics.SyncTransforms();
                    _arcTarget = ChooseArc(CurrentFeet(), _to.Feet, _arcTarget);
                }
            }

            float k = Mathf.Clamp01(_t / _glideSeconds);
            _arc = Mathf.MoveTowards(_arc, _arcTarget, Feel.RewindGlideArcSpeed * dt);
            Vector3 feet = FeetAt(k);
            Quaternion look = Quaternion.Slerp(_lookFrom, _lookTo, Feel.RewindGlideEase(k));
            YawPitch(look, out float yaw, out float pitch);
            _fpc.SetGlidePose(feet, yaw, pitch);
            UpdateCover(k, dt);

            if (k >= 1f && _swapped) FinishGlide();
        }

        Vector3 FeetAt(float k)
        {
            float s = Feel.RewindGlideEase(k);
            return Vector3.LerpUnclamped(_from.Feet, _to.Feet, s) + Vector3.up * (_arc * 4f * s * (1f - s));
        }

        Vector3 CurrentFeet() => FeetAt(Mathf.Clamp01(_t / _glideSeconds));

        /// <summary>
        /// The undo confirmed (or corrected) where the glide ends. Re-base the start so the body continues from
        /// exactly where it is now (no jump), then head to the new target on the same curve.
        /// </summary>
        void Retarget(PlayerPose resolved)
        {
            bool moved = (resolved.Feet - _to.Feet).sqrMagnitude > 1e-6f;
            bool turned = Mathf.Abs(Mathf.DeltaAngle(resolved.Yaw, _to.Yaw)) > 0.01f || Mathf.Abs(resolved.Pitch - _to.Pitch) > 0.01f;
            if (!moved && !turned)
            {
                _to = resolved;
                return;
            }
            float s0 = Feel.RewindGlideEase(Mathf.Clamp01(_t / _glideSeconds));
            if (moved && s0 < 0.98f)
            {
                Vector3 now = Vector3.LerpUnclamped(_from.Feet, _to.Feet, s0);
                _from.Feet = (now - resolved.Feet * s0) / (1f - s0);
            }
            if (turned && s0 < 0.98f)
            {
                // Same idea for the look: start from the view shown now, slerping on the rest of the curve.
                Quaternion now = Quaternion.Slerp(_lookFrom, _lookTo, s0);
                _lookTo = Look(resolved.Yaw, resolved.Pitch);
                _lookFrom = Quaternion.SlerpUnclamped(_lookTo, now, 1f / (1f - s0));
            }
            else if (turned)
            {
                _lookTo = Look(resolved.Yaw, resolved.Pitch);
            }
            _to = resolved;
        }

        /// <summary>
        /// The arc lift (m at the middle of the glide) for which the eye's path from <paramref name="from"/> to
        /// <paramref name="to"/> stays clear of geometry; <paramref name="fallback"/> if none is clear (then the
        /// glide passes through, under the Paper veil). A handful of sphere casts, once or twice per glide.
        /// </summary>
        static float ChooseArc(Vector3 from, Vector3 to, float fallback)
        {
            if ((to - from).sqrMagnitude < 0.01f) return 0f;
            for (int i = 0; i < ArcLifts.Length; i++)
            {
                float lift = ArcLifts[i] * Feel.RewindGlideArcMax;
                if (PathClear(from, to, lift)) return lift;
            }
            return fallback;
        }

        static readonly float[] ArcLifts = { 0f, 0.33f, 0.66f, 1f };

        static bool PathClear(Vector3 from, Vector3 to, float lift)
        {
            const int Segments = 10;
            const float Radius = 0.15f;
            Vector3 eye = Vector3.up * PlayerFactory.EyeHeight;
            Vector3 prev = from + eye;
            for (int i = 1; i <= Segments; i++)
            {
                float u = i / (float)Segments;
                Vector3 p = Vector3.Lerp(from, to, u) + Vector3.up * (lift * 4f * u * (1f - u)) + eye;
                Vector3 d = p - prev;
                float len = d.magnitude;
                if (len > 1e-4f && Physics.SphereCast(prev, Radius, d / len, out _, len, WorldMask, IgnoreTriggers))
                    return false;
                prev = p;
            }
            return true;
        }

        /// <summary>A soft Paper veil while the eye is about to cross / has just crossed a surface.</summary>
        void UpdateCover(float k, float dt)
        {
            float span = 0.1f / Mathf.Max(0.1f, _glideSeconds);
            Vector3 eye = Vector3.up * PlayerFactory.EyeHeight;
            Vector3 behind = FeetAt(Mathf.Clamp01(k - span)) + eye;
            Vector3 ahead = FeetAt(Mathf.Clamp01(k + span)) + eye;
            bool crossing = (ahead - behind).sqrMagnitude > 1e-6f &&
                            (Physics.Linecast(behind, ahead, WorldMask, IgnoreTriggers) ||
                             Physics.Linecast(ahead, behind, WorldMask, IgnoreTriggers));
            _cover = Mathf.MoveTowards(_cover, crossing ? 1f : 0f, dt / Feel.RewindGlideCoverSeconds);
        }

        void FinishGlide()
        {
            PlayerPose end = _to;
            _fpc.EndGlide(end.Feet, end.Yaw, end.Pitch);
            Physics.SyncTransforms();
            if (_tracker != null) _tracker.Reset(end);
            _cover = 0f;
            FinishSequence();
            try { GlideEnded?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>A glide interrupted (checkpoint escalation, disable): the body stays where it is, solid again.</summary>
        void StopGlideHere()
        {
            if (_fpc != null && _fpc.IsGliding)
                _fpc.EndGlide(_fpc.transform.position, _fpc.Yaw, _fpc.Pitch);
            _cover = 0f;
        }

        static Quaternion Look(float yaw, float pitch) => Quaternion.Euler(pitch, yaw, 0f);

        static void YawPitch(Quaternion q, out float yaw, out float pitch)
        {
            Vector3 f = q * Vector3.forward;
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        // ---------------------------------------------------------------- end

        /// <summary>The transition is over: unfreeze, then play a buffered press, if any.</summary>
        void FinishSequence()
        {
            EndSequence();
            if (_queued > 0)
            {
                _queued--;
                StartFromIdle();
            }
        }

        void EndSequence()
        {
            if (_phase == Phase.Glide) StopGlideHere();
            _phase = Phase.Idle;
            _t = 0f;
            _swapped = false;
            _movedAt = -1f;
            Freeze(false);
        }

        void Freeze(bool on)
        {
            if (_fpc != null) _fpc.Frozen = on;
        }
    }
}
