using Ion.Projection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Gameplay
{
    /// <summary>
    /// Owns the HUD prompt line (single writer, so prompts never fight) and the E-to-pick-up interaction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        const string PromptHolding = "LMB  place photo      Q / E  rotate      R  rewind";
        const string PromptCameraMode = "Hold RMB  aim camera      C  put camera away";
        const string PromptNoFilm = "Out of film      C  put camera away";
        const string PromptPickup = "E  pick up photo";
        const string PromptRaiseHint = "Hold RMB  hold up a photo";
        static readonly string[] s_ShootPrompts =
        {
            PromptNoFilm,
            "LMB  take photo  (1 film left)",
            "LMB  take photo  (2 film left)",
            "LMB  take photo  (3 film left)",
            "LMB  take photo  (4 film left)",
            "LMB  take photo  (5 film left)",
        };

        FirstPersonController _fpc;
        PhotoInventory _inventory;
        PhotoHolder _holder;
        InstantCamera _camera;

        /// <summary>The pickup currently in reach (E collects it), or null.</summary>
        public PhotoPickup Focused { get; private set; }

        void Awake()
        {
            _fpc = GetComponent<FirstPersonController>();
            _inventory = GetComponent<PhotoInventory>();
            _holder = GetComponent<PhotoHolder>();
            _camera = GetComponent<InstantCamera>();
        }

        void OnDisable()
        {
            Focused = null;
            GameplayUI.SetPrompt(string.Empty);
        }

        void Update()
        {
            bool inputOn = _fpc == null || _fpc.InputEnabled;
            bool holderRaised = _holder != null && _holder.IsRaised;
            bool cameraMode = _camera != null && _camera.IsCameraMode;

            Focused = inputOn && !holderRaised && !cameraMode ? FindFocusedPickup() : null;

            var kb = Keyboard.current;
            if (Focused != null && kb != null && kb.eKey.wasPressedThisFrame)
            {
                Focused.Collect(_inventory);
                Focused = null;
            }

            GameplayUI.SetPrompt(inputOn ? ChoosePrompt(holderRaised, cameraMode) : string.Empty);
        }

        string ChoosePrompt(bool holderRaised, bool cameraMode)
        {
            if (holderRaised) return PromptHolding;
            if (cameraMode)
            {
                if (!_camera.IsRaised) return PromptCameraMode;
                int film = _camera.Film;
                return film < s_ShootPrompts.Length ? s_ShootPrompts[film] : "LMB  take photo";
            }
            if (Focused != null) return PromptPickup;
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
    }
}
