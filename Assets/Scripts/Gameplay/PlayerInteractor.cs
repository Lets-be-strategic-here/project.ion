using Ion.Gameplay.State;
using Ion.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Gameplay
{
    /// <summary>
    /// Owns the HUD prompt line (single writer, so prompts never fight) and E: pick up a photo, press a
    /// switch, take the instant camera. Prompts use the bracket key style: "[SHIFT] hold up photo".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        static readonly string K_E = UIUtil.Key("E");
        static readonly string PromptHolding = UIUtil.Key("LMB") + " place      hold " + UIUtil.Key("Q") + " " + UIUtil.Key("E") + " rotate      " + UIUtil.Key("R") + " rewind";
        static readonly string PromptCameraMode = UIUtil.Key("SHIFT") + " aim      " + UIUtil.Key("C") + " put the camera away";
        static readonly string PromptNoFilm = "Out of film      " + UIUtil.Key("C") + " put the camera away";
        static readonly string PromptPickup = K_E + " pick up";
        static readonly string PromptPress = K_E + " press";
        static readonly string PromptPull = K_E + " pull";
        static readonly string PromptNoPower = "no power";
        static readonly string PromptRaiseHint = UIUtil.Key("SHIFT") + " hold up photo";
        static readonly string PromptFalling = Hud.UrgentRewindPrompt;   // large, centred, ion (Hud)
        static readonly string[] s_ShootPrompts =
        {
            PromptNoFilm,
            UIUtil.Key("LMB") + " take photo   ·   1 film left",
            UIUtil.Key("LMB") + " take photo   ·   2 film left",
            UIUtil.Key("LMB") + " take photo   ·   3 film left",
            UIUtil.Key("LMB") + " take photo   ·   4 film left",
            UIUtil.Key("LMB") + " take photo   ·   5 film left",
        };

        FirstPersonController _fpc;
        PhotoInventory _inventory;
        PhotoHolder _holder;
        InstantCamera _camera;
        SafePoseTracker _tracker;

        /// <summary>The pickup currently in reach (E collects it), or null.</summary>
        public PhotoPickup Focused { get; private set; }

        /// <summary>The switch currently in reach (E presses it), or null.</summary>
        public Switch FocusedSwitch { get; private set; }

        void Awake()
        {
            _fpc = GetComponent<FirstPersonController>();
            _inventory = GetComponent<PhotoInventory>();
            _holder = GetComponent<PhotoHolder>();
            _camera = GetComponent<InstantCamera>();
            _tracker = GetComponent<SafePoseTracker>();
        }

        void OnDisable()
        {
            Focused = null;
            FocusedSwitch = null;
            GameplayUI.SetPrompt(string.Empty);
        }

        void Update()
        {
            if (_tracker == null) _tracker = GetComponent<SafePoseTracker>();
            bool inputOn = (_fpc == null || (_fpc.InputEnabled && !_fpc.Frozen));
            bool holderRaised = _holder != null && _holder.IsRaised;
            bool cameraMode = _camera != null && _camera.IsCameraMode;
            bool free = inputOn && !holderRaised && !cameraMode;

            Focused = free ? FindFocusedPickup() : null;
            FocusedSwitch = free && Focused == null ? FindFocusedSwitch() : null;

            var kb = Keyboard.current;
            if (kb != null && IonInput.Active && kb.eKey.wasPressedThisFrame && free)
            {
                if (Focused != null)
                {
                    Focused.Collect(_inventory);
                    Focused = null;
                }
                else if (FocusedSwitch != null)
                {
                    FocusedSwitch.Press();
                }
            }

            GameplayUI.SetPrompt(inputOn ? ChoosePrompt(holderRaised, cameraMode) : string.Empty);
        }

        /// <summary>
        /// Presses the switch in reach and in view (what E does; also the debug harness's "press").
        /// False if none is in reach or it is unpowered.
        /// </summary>
        public bool PressFocused()
        {
            Switch s = FocusedSwitch != null ? FocusedSwitch : FindFocusedSwitch();
            return s != null && s.Press();
        }

        string ChoosePrompt(bool holderRaised, bool cameraMode)
        {
            if (_tracker != null && _tracker.IsFalling) return PromptFalling;
            if (holderRaised) return PromptHolding;
            if (cameraMode)
            {
                if (!_camera.IsRaised) return _camera.Film > 0 ? PromptCameraMode : PromptNoFilm;
                int film = _camera.Film;
                return film < s_ShootPrompts.Length ? s_ShootPrompts[film] : UIUtil.Key("LMB") + " take photo";
            }
            if (Focused != null) return PromptPickup;
            if (FocusedSwitch != null)
                return !FocusedSwitch.Powered ? PromptNoPower
                    : FocusedSwitch.Kind == Switch.SwitchKind.Lever ? PromptPull : PromptPress;
            if (_inventory != null && _inventory.Count > 0 && _holder != null && !_holder.HasRaisedOnce)
                return PromptRaiseHint;
            return string.Empty;
        }

        PhotoPickup FindFocusedPickup()
        {
            var cam = _fpc != null ? _fpc.Camera : null;
            if (cam == null) return null;

            Vector3 eye = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            const float rangeSq = PhotoPickup.InteractRange * PhotoPickup.InteractRange;

            PhotoPickup best = null;
            float bestSq = float.MaxValue;
            var list = PhotoPickup.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null || p.Collected || !p.isActiveAndEnabled) continue;
                Vector3 d = p.FocusPoint - eye;
                float sq = d.sqrMagnitude;
                if (sq > rangeSq || sq >= bestSq) continue;
                // Must be roughly in view unless very close.
                if (sq > 1.44f && Vector3.Dot(fwd, d) < 0.35f * Mathf.Sqrt(sq)) continue;
                best = p;
                bestSq = sq;
            }
            return best;
        }

        Switch FindFocusedSwitch()
        {
            var cam = _fpc != null ? _fpc.Camera : null;
            if (cam == null) return null;

            Vector3 eye = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            Switch best = null;
            float bestScore = float.MaxValue;
            var list = Switch.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s == null || !s.isActiveAndEnabled) continue;
                Vector3 d = s.FocusPoint - eye;
                float sq = d.sqrMagnitude;
                float range = s.InteractRange;
                if (sq > range * range) continue;
                float dist = Mathf.Sqrt(sq);
                float facing = dist > 1e-3f ? Vector3.Dot(fwd, d) / dist : 1f;
                if (dist > 1.1f && facing < 0.55f) continue; // roughly in view unless very close
                float score = dist * (2f - facing);
                if (score >= bestScore) continue;
                best = s;
                bestScore = score;
            }
            return best;
        }
    }
}
