using System;
using Ion.Projection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Gameplay
{
    /// <summary>
    /// The instant camera. Once <see cref="Unlocked"/>, C toggles camera mode; in camera mode hold RMB to
    /// raise the viewfinder and press LMB to take a photo (costs one film). The photo goes to the inventory.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InstantCamera : MonoBehaviour
    {
        public const float CaptureAspect = 4f / 3f;
        /// <summary>
        /// Vertical FOV of a snapshot: the same shape as the pre-made photos (50°, 4:3), so a raised snapshot
        /// fits inside its Polaroid frame like the others instead of filling the whole 70° view.
        /// </summary>
        public const float CaptureFovY = 50f;
        /// <summary>Snapshot preview width (about screen resolution for the raised frame).</summary>
        public const int CapturePreviewWidth = 1024;
        public const string SnapshotLabel = "Snapshot";

        FirstPersonController _fpc;
        PhotoInventory _inventory;
        ViewfinderFrame _viewfinder;

        bool _unlocked;
        int _film;

        /// <summary>Whether the player owns the camera (C does nothing until then).</summary>
        public bool Unlocked
        {
            get => _unlocked;
            set
            {
                if (value == _unlocked) return;
                _unlocked = value;
                if (!value) SetCameraMode(false);
                else if (Time.timeSinceLevelLoad > 1f) GameplayUI.Toast("Got the instant camera - press C");
                Changed?.Invoke();
            }
        }

        /// <summary>Remaining shots.</summary>
        public int Film
        {
            get => _film;
            set
            {
                value = Mathf.Max(0, value);
                if (value == _film) return;
                _film = value;
                Changed?.Invoke();
            }
        }

        /// <summary>Camera is out (photo holding is disabled meanwhile).</summary>
        public bool IsCameraMode { get; private set; }

        /// <summary>Viewfinder is up (RMB held in camera mode).</summary>
        public bool IsRaised { get; private set; }

        /// <summary>Raised when Unlocked, Film, camera mode or raise state changes.</summary>
        public event Action Changed;

        /// <summary>Raised after a successful capture, with the new photo.</summary>
        public event Action<PhotoData> Captured;

        void Awake()
        {
            _fpc = GetComponent<FirstPersonController>();
            _inventory = GetComponent<PhotoInventory>();
        }

        void OnDisable()
        {
            SetRaised(false);
        }

        void OnDestroy()
        {
            if (_viewfinder != null) Destroy(_viewfinder.gameObject);
        }

        ViewfinderFrame Viewfinder
        {
            get
            {
                if (_viewfinder == null) _viewfinder = ViewfinderFrame.Create(CaptureAspect, CaptureFovY);
                return _viewfinder;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if ((_fpc != null && !_fpc.InputEnabled) || kb == null || mouse == null)
            {
                SetRaised(false);
                return;
            }

            if (_unlocked && kb.cKey.wasPressedThisFrame)
                SetCameraMode(!IsCameraMode);

            if (!IsCameraMode)
            {
                SetRaised(false);
                return;
            }

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            SetRaised(locked && mouse.rightButton.isPressed);

            if (IsRaised && mouse.leftButton.wasPressedThisFrame)
                TryCapture();
        }

        /// <summary>Takes the camera out / puts it away.</summary>
        public void SetCameraMode(bool on)
        {
            if (on && !_unlocked) on = false;
            if (on == IsCameraMode) return;
            IsCameraMode = on;
            if (!on) SetRaised(false);
            Changed?.Invoke();
        }

        void SetRaised(bool raised)
        {
            if (raised == IsRaised) return;
            IsRaised = raised;
            if (raised || _viewfinder != null) Viewfinder.Visible = raised;
            Changed?.Invoke();
        }

        /// <summary>
        /// Takes a photo from the player camera right now (what LMB does with the viewfinder up):
        /// costs one film, adds the photo to the inventory. Returns null if out of film / not possible.
        /// </summary>
        public PhotoData TryCapture()
        {
            if (_film <= 0)
            {
                GameplayUI.Toast("Out of film");
                return null;
            }

            var ps = ProjectionSystem.Instance;
            var cam = _fpc != null ? _fpc.Camera : null;
            if (ps == null || cam == null) return null;

            // Same level-view assist as placing, so a snapshot and its paste line up.
            _fpc.SnapPitchLevel(PhotoHolder.LevelSnapDegrees);
            _fpc.ResetViewEffects(); // exact eye pose and base FOV (no head bob / FOV punch in the photo)
            var t = cam.transform;
            var photo = ps.Capture(new Pose(t.position, t.rotation), CaptureFovY, CaptureAspect, SnapshotLabel, CapturePreviewWidth);
            if (photo == null) return null;

            Film = _film - 1;
            Viewfinder.Flash();
            if (_inventory != null) _inventory.Add(photo);
            GameplayUI.PhotoPrinted(photo);
            GameplayUI.Toast(_film > 0 ? "Click! Photo added (C to put the camera away)" : "Click! That was the last of the film");
            Captured?.Invoke(photo);
            return photo;
        }
    }
}
