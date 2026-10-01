using System;
using Ion.Gameplay.State;
using Ion.Presentation;
using Ion.Presentation.Motion;
using Ion.Projection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Gameplay
{
    /// <summary>
    /// Holding and placing photos (art bible §9). 1–9 / wheel select, hold Shift (or RMB, or toggle in
    /// Settings) to raise, LMB places once the raise is ≥ 85 % up: a 0.10 s press-in, then the world swaps.
    /// The photo is consumed and the placement is recorded in <see cref="WorldHistory"/> (R, anytime, gives it
    /// back).
    ///
    /// Rotation is continuous (art bible §9.1 "Rotate Q/E"): holding Q (+, counter-clockwise) or E (−) turns the
    /// raised photo at <see cref="Feel.RotateDegPerSec"/> with an eased start and an eased stop; a tap turns
    /// it a few degrees with the same easing. Within <see cref="Feel.RotateAssistDegrees"/> of a multiple of
    /// 90° a gentle magnetic assist eases the roll onto it once the key is released (never a visible snap).
    /// <see cref="RollDegrees"/> is the single roll value: the overlay draws it and the placement uses it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PhotoInventory))]
    public sealed class PhotoHolder : MonoBehaviour
    {
        public float ScrollCooldown = 0.08f;
        /// <summary>Views within this many degrees of level are snapped level when placing.</summary>
        public const float LevelSnapDegrees = 5f;

        FirstPersonController _fpc;
        PhotoInventory _inventory;
        InstantCamera _instantCamera;

        PhotoData _shown;        // photo currently shown in the overlay while raised
        PhotoData _rollPhoto;    // photo the current roll belongs to
        float _nextScrollTime;

        SpringFloat _raise = new SpringFloat(0f, Feel.RaiseFreq, Feel.RaiseZeta);
        float _pressT = -1f;     // >= 0 while the place press-in runs
        PhotoData _pressPhoto;
        int _pressIndex;
        float _pressRoll;
        bool _pressStaged;       // the world swap is being staged behind the card (ProjectionSystem.BeginStagedPlace)
        int _pressDepthBefore;
        PlayerPose _pressSafe;

        // Continuous rotation (degrees / second, signed) and the tap nudge.
        float _rollVel;
        int _rotDir;             // direction of the current press (+1 Q, −1 E, 0 none)
        float _rotTravel;        // degrees turned since that press started

        public bool IsRaised { get; private set; }
        public float RollDegrees { get; private set; }

        /// <summary>0 = lowered, 1 = fully up (the raise spring; may overshoot slightly).</summary>
        public float RaiseProgress => _raise.Value;

        /// <summary>True while a placement's 0.10 s press-in runs (the world swaps at its end).</summary>
        public bool IsPlacing => _pressT >= 0f;

        /// <summary>Cost (ms) of the latest staged placement's swap frame work: commit, events, history (debug / perf).</summary>
        public float LastCommitMs { get; private set; }

        /// <summary>The photo currently held up (null when lowered).</summary>
        public PhotoData RaisedPhoto => IsRaised ? _shown : null;

        /// <summary>True once the player has raised any photo (used to stop showing the hint).</summary>
        public bool HasRaisedOnce { get; private set; }

        public event Action RaisedChanged;

        /// <summary>Raised when a placement has been made by this holder (after the world swap).</summary>
        public event Action<PhotoData> PlacedPhoto;

        void Awake()
        {
            _fpc = GetComponent<FirstPersonController>();
            _inventory = GetComponent<PhotoInventory>();
            _instantCamera = GetComponent<InstantCamera>();
        }

        void OnDisable()
        {
            CancelPress();
            SetRaised(false);
        }

        /// <summary>
        /// Automation (debug harness / tests): keeps the selected photo raised as if Shift were held.
        /// Cleared by placing, lowering or disabling input.
        /// </summary>
        public bool AutomationRaise { get; set; }

        void Update()
        {
            if (_instantCamera == null) _instantCamera = GetComponent<InstantCamera>();
            float dt = Time.deltaTime;

            if (_pressT >= 0f)
            {
                _pressT += dt;
                // The swap waits for the staged cuts (a frame or two; longer only on a very slow machine, capped).
                var stagePs = ProjectionSystem.Instance;
                bool busy = _pressStaged && stagePs != null && stagePs.StagingBusy;
                if (_pressT >= Feel.PlaceSwapAt && (!busy || _pressT >= Feel.PlaceStageMaxSeconds)) FinishPress();
                StepRaise(dt);
                return;
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (_fpc != null && (!_fpc.InputEnabled || _fpc.Frozen))
            {
                AutomationRaise = false;
                SetRaised(false);
                StepRaise(dt);
                return;
            }

            bool active = IonInput.Active;
            if (active && kb != null && mouse != null) HandleSelection(kb, mouse);

            SyncRoll();

            bool cameraBusy = _instantCamera != null && _instantCamera.IsCameraMode;
            bool held = AutomationRaise || IonInput.RaiseHeld;
            SetRaised(held && !cameraBusy && _inventory.Selected != null);
            StepRaise(dt);

            if (!IsRaised) return;

            if (_inventory.Selected != _shown)
            {
                _shown = _inventory.Selected;
                RefreshOverlay();
            }

            StepRotation(dt, active ? IonInput.RotateAxis + AutomationRotate : AutomationRotate);
            if (!active) return;

            if (IonInput.PrimaryPressedThisFrame && _raise.Value >= Feel.PlaceEnableProgress)
                BeginPress();
        }

        void StepRaise(float dt)
        {
            _raise.Target = IsRaised ? 1f : 0f;
            if (IsRaised) _raise.Step(dt);
            else
            {
                // Lowering is a fixed easeInOutCubic (exits faster than it enters); track it linearly here.
                _raise.Velocity = 0f;
                _raise.Value = Mathf.MoveTowards(_raise.Value, 0f, dt / Feel.LowerSeconds);
            }
        }

        /// <summary>A new selection starts upright.</summary>
        void SyncRoll()
        {
            var selected = _inventory.Selected;
            if (selected != _rollPhoto)
            {
                _rollPhoto = selected;
                RollDegrees = 0f;
                _rollVel = 0f;
                _rotDir = 0;
                _rotTravel = 0f;
            }
        }

        /// <summary>Raises the selected photo now (automation; same state as holding Shift). False if nothing to raise.</summary>
        public bool Raise()
        {
            if (_inventory.Selected == null) return false;
            if (_instantCamera != null && _instantCamera.IsCameraMode) _instantCamera.SetCameraMode(false);
            SyncRoll();
            AutomationRaise = true;
            SetRaised(true);
            _raise.Snap(1f);
            return IsRaised;
        }

        /// <summary>Game restart: lowers the photo and forgets the hint state.</summary>
        public void ResetForRestart()
        {
            AutomationRaise = false;
            CancelPress();
            SetRaised(false);
            _raise.Snap(0f);
            HasRaisedOnce = false;
        }

        /// <summary>Lowers the photo (automation; same as releasing Shift; R does it too).</summary>
        public void Lower()
        {
            AutomationRaise = false;
            AutomationRotate = 0f;
            CancelPress();
            SetRaised(false);
            IonInput.ConsumeRaise();
        }

        /// <summary>
        /// Places the raised photo right now, without the press-in (automation; the player's LMB path waits
        /// 0.10 s first). Returns false if no photo is raised or the placement could not be made.
        /// </summary>
        public bool Place()
        {
            if (!IsRaised || _inventory.Selected == null) return false;
            CancelPress();
            return PlaceNow(_inventory.Selected, _inventory.SelectedIndex, RollDegrees);
        }

        /// <summary>
        /// Places the raised photo through the player's LMB path (press-in with the world swap staged behind the card).
        /// Automation; false if nothing is raised. Watch <see cref="IsPlacing"/> for the swap.
        /// </summary>
        public bool PlaceWithPress()
        {
            if (!IsRaised || _inventory.Selected == null || IsPlacing) return false;
            BeginPress();
            return IsPlacing;
        }

        /// <summary>Single rewind through the history (legacy automation entry; R does the same with its transition).</summary>
        public bool Rewind()
        {
            var history = WorldHistory.Instance;
            if (history == null) return false;
            if (IsRaised) Lower();
            return history.RewindOnce() != RewindResult.Nothing;
        }

        void HandleSelection(Keyboard kb, Mouse mouse)
        {
            int count = _inventory.Count;
            if (count == 0) return;

            int digit = -1;
            if (kb.digit1Key.wasPressedThisFrame) digit = 0;
            else if (kb.digit2Key.wasPressedThisFrame) digit = 1;
            else if (kb.digit3Key.wasPressedThisFrame) digit = 2;
            else if (kb.digit4Key.wasPressedThisFrame) digit = 3;
            else if (kb.digit5Key.wasPressedThisFrame) digit = 4;
            else if (kb.digit6Key.wasPressedThisFrame) digit = 5;
            else if (kb.digit7Key.wasPressedThisFrame) digit = 6;
            else if (kb.digit8Key.wasPressedThisFrame) digit = 7;
            else if (kb.digit9Key.wasPressedThisFrame) digit = 8;
            if (digit >= 0 && digit < count) _inventory.SelectedIndex = digit;

            // Scroll magnitudes differ wildly between browsers/OSes; only the sign matters.
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && Time.unscaledTime >= _nextScrollTime && count > 1)
            {
                _nextScrollTime = Time.unscaledTime + ScrollCooldown;
                int step = scroll > 0f ? -1 : 1;
                int idx = _inventory.SelectedIndex < 0 ? 0 : _inventory.SelectedIndex;
                _inventory.SelectedIndex = (idx + step + count) % count;
            }
        }

        /// <summary>
        /// Automation (debug harness / tests): a held rotate key, +1 = Q, −1 = E, 0 = released. Goes through the
        /// same eased rotation as the keyboard. Cleared when the photo is lowered.
        /// </summary>
        public float AutomationRotate { get; set; }

        /// <summary>Current rotation speed in degrees per second (signed; 0 when settled).</summary>
        public float RollVelocity => _rollVel;

        /// <summary>True while the photo is turning at more than a crawl (audio: no "landed" click mid-turn).</summary>
        public bool IsRotatingFast => Mathf.Abs(_rollVel) > Feel.RotateAssistMaxSpeed;

        /// <summary>True while the roll is still moving (key held, easing out, or the 90° assist settling).</summary>
        public bool IsRotating => _rotDir != 0 || Mathf.Abs(_rollVel) > 0.01f || AssistPending();

        /// <summary>
        /// Instant roll change (debug harness: ±90 steps; legacy automation). The player's path is
        /// <see cref="StepRotation"/>; this skips the easing.
        /// </summary>
        public void Rotate(float degrees)
        {
            SyncRoll();
            SetRoll(RollDegrees + degrees);
            _rollVel = 0f;
            _rotDir = 0;
            _rotTravel = 0f;
        }

        void SetRoll(float degrees)
        {
            float r = Mathf.Repeat(degrees, 360f);
            if (r > 359.995f) r = 0f;
            if (Mathf.Approximately(r, RollDegrees)) return;
            RollDegrees = r;
            RefreshOverlay();
        }

        /// <summary>
        /// One frame of continuous rotation. <paramref name="axis"/>: +1 Q held, −1 E held, 0 none (both cancel).
        /// Velocity eases toward ±<see cref="Feel.RotateDegPerSec"/> (ease-in) and back to 0 (ease-out); a press
        /// shorter than the nudge keeps driving until it has turned <see cref="Feel.RotateTapDegrees"/>. Once
        /// released and nearly still, a roll within <see cref="Feel.RotateAssistDegrees"/> of a multiple of 90°
        /// eases onto it.
        /// </summary>
        void StepRotation(float dt, float axis)
        {
            if (dt <= 0f) return;
            int dir = axis > 0.5f ? 1 : axis < -0.5f ? -1 : 0;
            if (dir != 0 && dir != _rotDir)
            {
                // A new press (or a reversal): the tap nudge starts counting from here.
                _rotDir = dir;
                _rotTravel = 0f;
            }

            bool held = dir != 0;
            bool nudging = !held && _rotDir != 0 && _rotTravel < Feel.RotateTapDegrees;
            float target = held ? dir * Feel.RotateDegPerSec : nudging ? _rotDir * Feel.RotateDegPerSec : 0f;
            float tau = Mathf.Abs(target) > Mathf.Abs(_rollVel) ? Feel.RotateEaseIn : Feel.RotateEaseOut;
            _rollVel += (target - _rollVel) * (1f - Mathf.Exp(-dt / tau));
            if (!held && !nudging)
            {
                _rotDir = 0;
                if (Mathf.Abs(_rollVel) < 0.05f) _rollVel = 0f;
            }

            float step = _rollVel * dt;
            if (step != 0f)
            {
                _rotTravel += Mathf.Abs(step);
                SetRoll(RollDegrees + step);
            }

            // Magnetic assist: only after release and once the ease-out has nearly stopped.
            if (!held && !nudging && Mathf.Abs(_rollVel) < Feel.RotateAssistMaxSpeed)
            {
                float nearest = Mathf.Round(RollDegrees / 90f) * 90f;
                float off = Mathf.DeltaAngle(RollDegrees, nearest);
                if (Mathf.Abs(off) <= Feel.RotateAssistDegrees && Mathf.Abs(off) > 0f)
                {
                    float k = 1f - Mathf.Exp(-dt / Feel.RotateAssistSeconds);
                    float move = off * k;
                    if (Mathf.Abs(off - move) < 0.01f) move = off; // land exactly
                    SetRoll(RollDegrees + move);
                }
            }
        }

        bool AssistPending()
        {
            float nearest = Mathf.Round(RollDegrees / 90f) * 90f;
            float off = Mathf.Abs(Mathf.DeltaAngle(RollDegrees, nearest));
            return off > 0f && off <= Feel.RotateAssistDegrees;
        }

        void SetRaised(bool raised)
        {
            if (raised == IsRaised) return;
            IsRaised = raised;

            if (raised)
            {
                HasRaisedOnce = true;
                _shown = _inventory.Selected;
                RefreshOverlay();
            }
            else
            {
                _shown = null;
                _rollVel = 0f;
                _rotDir = 0;
                AutomationRotate = 0f;
                var overlay = GameplayUI.Overlay;
                if (overlay != null) overlay.Hide();
            }

            RaisedChanged?.Invoke();
        }

        void RefreshOverlay()
        {
            if (!IsRaised || _shown == null) return;
            var overlay = GameplayUI.Overlay;
            if (overlay != null) overlay.Show(_shown, RollDegrees);
        }

        // ---------------------------------------------------------------- placing

        /// <summary>LMB: the card presses in (0.10 s) with the view held still, then the world swaps.</summary>
        void BeginPress()
        {
            _pressPhoto = _inventory.Selected;
            _pressIndex = _inventory.SelectedIndex;
            _pressRoll = RollDegrees;
            _rollVel = 0f;      // the roll freezes where the card is: placed exactly as shown
            _rotDir = 0;
            if (_pressPhoto == null) return;
            _pressT = 0f;
            if (_fpc != null) _fpc.HoldStill = true;

            // Stage the world swap now, behind the card (it covers exactly the frustum while the view is held): the
            // cuts run over the press-in frames instead of one long frame at the swap.
            _pressStaged = false;
            var ps = ProjectionSystem.Instance;
            var cam = _fpc != null ? _fpc.Camera : null;
            if (ps != null && cam != null)
            {
                _fpc.SnapPitchLevel(LevelSnapDegrees);
                _fpc.ResetViewEffects();
                _pressSafe = WorldHistory.SafePoseNow();
                _pressDepthBefore = ps.PlacementCount;
                _pressStaged = ps.BeginStagedPlace(_pressPhoto, cam, _pressRoll);
                if (!_pressStaged)
                {
                    CancelPress();
                    GameplayUI.Toast("The photo won't take here");
                    return;
                }
            }

            var overlay = GameplayUI.Overlay;
            if (overlay != null) overlay.PressIn();
        }

        void FinishPress()
        {
            PhotoData photo = _pressPhoto;
            int index = _pressIndex;
            float roll = _pressRoll;
            bool staged = _pressStaged;
            _pressStaged = false; // committed below, not cancelled
            CancelPress();
            if (!staged)
            {
                if (photo != null && _inventory.Contains(photo)) PlaceNow(photo, index, roll);
                return;
            }
            var ps = ProjectionSystem.Instance;
            if (ps == null) return;
            if (photo == null || !_inventory.Contains(photo))
            {
                ps.CancelStagedPlace();
                return;
            }
            var commitWatch = System.Diagnostics.Stopwatch.StartNew();
            AutomationRaise = false;
            SetRaised(false);
            IonInput.ConsumeRaise();
            ps.CommitStagedPlace();
            AfterPlaced(photo, index, _pressDepthBefore, _pressSafe);
            LastCommitMs = (float)commitWatch.Elapsed.TotalMilliseconds;
        }

        void CancelPress()
        {
            if (_pressT >= 0f && _fpc != null) _fpc.HoldStill = false;
            if (_pressStaged)
            {
                _pressStaged = false;
                var ps = ProjectionSystem.Instance;
                if (ps != null) ps.CancelStagedPlace();
            }
            _pressT = -1f;
            _pressPhoto = null;
        }

        bool PlaceNow(PhotoData photo, int index, float roll)
        {
            var ps = ProjectionSystem.Instance;
            var cam = _fpc != null ? _fpc.Camera : null;
            if (ps == null || photo == null || cam == null)
            {
                GameplayUI.Toast("The photo won't take here");
                return false;
            }

            // The pre-made photos are taken level; forgive a slightly tilted view.
            _fpc.SnapPitchLevel(LevelSnapDegrees);
            _fpc.ResetViewEffects(); // the cut uses the exact eye pose, never a head-bob offset
            PlayerPose safe = WorldHistory.SafePoseNow();
            int depthBefore = ps.PlacementCount;

            AutomationRaise = false;
            SetRaised(false);
            IonInput.ConsumeRaise();

            ps.Place(photo, cam, roll);
            if (ps.PlacementCount <= depthBefore)
            {
                GameplayUI.Toast("The photo won't take here");
                return false;
            }

            AfterPlaced(photo, index, depthBefore, safe);
            return true;
        }

        /// <summary>Bookkeeping once a placement is in the world: consume the photo, record the change, notify.</summary>
        void AfterPlaced(PhotoData photo, int index, int depthBefore, PlayerPose safe)
        {
            _inventory.Remove(photo);
            var history = WorldHistory.Instance;
            if (history != null)
            {
                history.Push(new PlacementChange
                {
                    ProjectionDepthBefore = depthBefore,
                    Photo = photo,
                    Index = index,
                    Inventory = _inventory,
                    SafePose = safe,
                });
            }
            try { PlacedPhoto?.Invoke(photo); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
