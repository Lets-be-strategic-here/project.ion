using System;
using Ion.Presentation;
using Ion.Presentation.Motion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Gameplay.State
{
    /// <summary>
    /// R input and the rewind transitions (art bible §9.1, §9.2, §11):
    ///  * R works anytime (a raised photo is lowered first, in the same press).
    ///  * Single R: Cyanotype wash; the world swaps at 0.22 s. When the player has to move (fall recovery,
    ///    or the undo left them unsupported), the screen fades to Paper and the move happens under it:
    ///    the camera never slides through geometry.
    ///  * A second R within 0.35 s escalates to the checkpoint: brackets close from the screen edges,
    ///    everything is restored at 0.40 s, they open again.
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

        public static RewindController Instance { get; private set; }

        /// <summary>
        /// A rewind transition starts (single: Undid / RecoveredFall; double: ToCheckpoint). Audio's tape-dip
        /// starts here; the world swaps <see cref="Feel.RewindSwapAt"/> / <see cref="Feel.CheckpointSwapAt"/> later.
        /// "Nothing" is not a transition: listen to <see cref="WorldHistory.Rewound"/> for it.
        /// </summary>
        public static event Action<RewindResult> Started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Started = null;
        }

        static void RaiseStarted(RewindResult r)
        {
            try { Started?.Invoke(r); }
            catch (Exception e) { Debug.LogException(e); }
        }

        enum Phase { Idle, Single, Checkpoint }

        FirstPersonController _fpc;
        PhotoHolder _holder;
        InstantCamera _camera;
        SafePoseTracker _tracker;

        Phase _phase;
        float _t;                  // seconds into the current sequence
        bool _swapped;             // the world swap of the current sequence has happened
        bool _moveFade;            // single: the pose move happens under a Paper fade
        float _moveAt = -1f;       // single: when the pose move happens (sequence time)
        float _movedAt = -1f;      // single: when the pose move happened (sequence time), -1 = no move yet
        RewindResult _expected;
        float _lastPressTime = -10f;
        bool _lastPressEscalates;  // the previous press may escalate into a checkpoint restore
        float _idleInLimbo;
        int _nothingHints;

        /// <summary>True while a rewind transition plays.</summary>
        public bool Busy => _phase != Phase.Idle;

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
        /// a second press within <see cref="DoubleTapWindow"/> escalates to the last checkpoint.
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
            if (_phase == Phase.Single && (!_swapped || _moveAt >= 0f))
            {
                // The previous press is still mid-swap: let it finish (a quick second press escalates).
                _lastPressEscalates = true;
                return;
            }

            _lastPressEscalates = true;
            var history = WorldHistory.Instance;
            RewindResult peek = history != null ? history.PeekRewind() : RewindResult.Nothing;
            if (peek == RewindResult.Nothing)
            {
                if (history != null) history.RewindOnce(); // raises Rewound(Nothing) for audio / HUD
                NothingFeedback();
                LastResult = RewindResult.Nothing;
                return;
            }
            if (_phase != Phase.Idle) EndSequence();
            StartSingle(peek);
        }

        /// <summary>Starts a full single rewind (with its transition) right now.</summary>
        public void RewindNow() { _lastPressEscalates = false; StartSingleOrNothing(); }

        /// <summary>Starts a full checkpoint restore (with its transition) right now.</summary>
        public void RewindToCheckpointNow() => StartCheckpoint();

        void StartSingleOrNothing()
        {
            var history = WorldHistory.Instance;
            RewindResult peek = history != null ? history.PeekRewind() : RewindResult.Nothing;
            if (peek == RewindResult.Nothing)
            {
                if (history != null) history.RewindOnce();
                NothingFeedback();
                return;
            }
            if (_phase != Phase.Idle) EndSequence();
            StartSingle(peek);
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

        void StartSingle(RewindResult expected)
        {
            _phase = Phase.Single;
            _expected = expected;
            _t = 0f;
            _swapped = false;
            _moveAt = -1f;
            _movedAt = -1f;
            // A fall recovery always moves the player: the Paper fade peaks at the swap.
            _moveFade = expected == RewindResult.RecoveredFall;
            Freeze(true);
            RaiseStarted(expected);
            var fx = ScreenFx.Instance;
            if (fx != null)
            {
                fx.PlayRewindWash(false);
                if (_moveFade) fx.FadePaper(Feel.RewindSwapAt - Feel.RewindMoveFadeIn);
            }
        }

        void StartCheckpoint()
        {
            // A single rewind still in flight is covered by the checkpoint restore (its pending pose move
            // and Paper fade are dropped: the checkpoint pose wins).
            var fx0 = ScreenFx.Instance;
            if (fx0 != null && _phase == Phase.Single) fx0.CancelPaper();
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
            _t += Mathf.Min(dt, 0.05f);
            var history = WorldHistory.Instance;

            if (_phase == Phase.Single)
            {
                if (!_swapped && _t >= Feel.RewindSwapAt)
                {
                    _swapped = true;
                    if (_expected == RewindResult.RecoveredFall || _moveFade)
                    {
                        // Under the Paper fade (already at its peak).
                        LastResult = history != null ? history.RewindOnce() : RewindResult.Nothing;
                        RewindCount++;
                        _movedAt = _t;
                        var fx = ScreenFx.Instance;
                        if (fx != null) fx.ReleasePaper(Feel.RewindMoveHold);
                    }
                    else
                    {
                        // Undo in place; if the player then has to move, fade to Paper first.
                        LastResult = UndoKeepingPose(history, out bool mustMove, out PlayerPose target);
                        RewindCount++;
                        if (mustMove)
                        {
                            _moveFade = true;
                            _moveAt = _t + Feel.RewindMoveFadeIn;
                            _pendingPose = target;
                            var fx = ScreenFx.Instance;
                            if (fx != null) fx.FadePaper(0f);
                        }
                    }
                }
                if (_moveAt >= 0f && _t >= _moveAt)
                {
                    _moveAt = -1f;
                    _movedAt = _t;
                    MoveTo(_pendingPose);
                    var fx = ScreenFx.Instance;
                    if (fx != null) fx.ReleasePaper(Feel.RewindMoveHold);
                }
                // The sequence ends once the wash is over and any pose move has happened (plus the Paper hold).
                // (The end must not be measured from the current time, or it would never be reached.)
                float end = Feel.RewindSeconds;
                if (_movedAt >= 0f) end = Mathf.Max(end, _movedAt + Feel.RewindMoveHold + 0.05f);
                if (_swapped && _moveAt < 0f && _t >= end) EndSequence();
            }
            else if (_phase == Phase.Checkpoint)
            {
                if (!_swapped && _t >= Feel.CheckpointSwapAt)
                {
                    _swapped = true;
                    LastResult = history != null ? history.RewindToCheckpoint() : RewindResult.Nothing;
                    RewindCount++;
                }
                if (_swapped && _t >= Feel.CheckpointSeconds) EndSequence();
            }
        }

        PlayerPose _pendingPose;

        /// <summary>
        /// Undoes the top record without moving the player yet: reports where they must go (if anywhere),
        /// so the move can happen under the Paper fade.
        /// </summary>
        RewindResult UndoKeepingPose(WorldHistory history, out bool mustMove, out PlayerPose target)
        {
            mustMove = false;
            target = PlayerPose.Of(_fpc);
            if (history == null) return RewindResult.Nothing;
            Vector3 feet = transform.position;
            float yaw = _fpc != null ? _fpc.Yaw : 0f, pitch = _fpc != null ? _fpc.Pitch : 0f;
            RewindResult r = history.RewindOnce();
            if (r != RewindResult.Undid || _fpc == null) return r;
            // RewindOnce moves the player itself when unsupported; put them back for the fade, then move.
            Vector3 moved = transform.position;
            if ((moved - feet).sqrMagnitude > 1e-4f)
            {
                mustMove = true;
                target = PlayerPose.Of(_fpc);
                _fpc.Teleport(feet, yaw, silent: true);
                _fpc.SetLook(yaw, pitch);
            }
            return r;
        }

        void MoveTo(PlayerPose pose)
        {
            if (_fpc == null) return;
            _fpc.Teleport(pose.Feet, pose.Yaw, silent: true);
            _fpc.SetLook(pose.Yaw, pose.Pitch);
            Physics.SyncTransforms();
            if (_tracker != null) _tracker.Reset(pose);
        }

        void EndSequence()
        {
            _phase = Phase.Idle;
            _t = 0f;
            _swapped = false;
            _moveFade = false;
            _moveAt = -1f;
            _movedAt = -1f;
            Freeze(false);
        }

        void Freeze(bool on)
        {
            if (_fpc != null) _fpc.Frozen = on;
        }
    }
}
