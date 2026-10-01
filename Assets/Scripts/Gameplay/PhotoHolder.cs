using System;
using System.Collections.Generic;
using Ion.Projection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Gameplay
{
    /// <summary>
    /// Holding and placing photos. 1-5 / scroll select, hold RMB to raise, Q/E rotate in 90° steps,
    /// LMB places (the photo is consumed), R rewinds the last placement (the photo comes back).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PhotoInventory))]
    public sealed class PhotoHolder : MonoBehaviour
    {
        struct Consumed
        {
            public PhotoData Photo; // null = a placement made by someone else (nothing to give back)
            public int Index;
        }

        public float ScrollCooldown = 0.08f;
        /// <summary>Views within this many degrees of level are snapped level when placing.</summary>
        public const float LevelSnapDegrees = 5f;

        FirstPersonController _fpc;
        PhotoInventory _inventory;
        InstantCamera _instantCamera;

        PhotoData _shown;        // photo currently shown in the overlay while raised
        PhotoData _rollPhoto;    // photo the current roll belongs to
        float _nextScrollTime;
        bool _waitForRmbRelease;  // after placing, RMB must be released before raising again

        readonly Stack<Consumed> _consumed = new Stack<Consumed>(8);
        ProjectionSystem _subscribed;
        bool _placingActive;
        bool _placedHandled;
        PhotoData _placingPhoto;
        int _placingIndex;
        int _latePlacedSkips;

        public bool IsRaised { get; private set; }
        public float RollDegrees { get; private set; }

        /// <summary>The photo currently held up (null when lowered).</summary>
        public PhotoData RaisedPhoto => IsRaised ? _shown : null;

        /// <summary>True once the player has raised any photo (used to stop showing the hint).</summary>
        public bool HasRaisedOnce { get; private set; }

        public event Action RaisedChanged;

        void Awake()
        {
            _fpc = GetComponent<FirstPersonController>();
            _inventory = GetComponent<PhotoInventory>();
            _instantCamera = GetComponent<InstantCamera>();
        }

        void OnDisable()
        {
            SetRaised(false);
            Unsubscribe();
        }

        void OnDestroy() => Unsubscribe();

        void EnsureSubscribed()
        {
            var ps = ProjectionSystem.Instance;
            if (ReferenceEquals(ps, _subscribed)) return;
            Unsubscribe();
            if (ps != null)
            {
                ps.Placed += OnPlaced;
                ps.Rewound += OnRewound;
                _subscribed = ps;
            }
        }

        void Unsubscribe()
        {
            if (ReferenceEquals(_subscribed, null)) return;
            _subscribed.Placed -= OnPlaced;
            _subscribed.Rewound -= OnRewound;
            _subscribed = null;
        }

        void Update()
        {
            EnsureSubscribed();
            if (_instantCamera == null) _instantCamera = GetComponent<InstantCamera>();

            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if ((_fpc != null && !_fpc.InputEnabled) || kb == null || mouse == null)
            {
                SetRaised(false);
                return;
            }

            HandleSelection(kb, mouse);

            // A new selection starts upright.
            var selected = _inventory.Selected;
            if (selected != _rollPhoto)
            {
                _rollPhoto = selected;
                RollDegrees = 0f;
            }

            if (kb.rKey.wasPressedThisFrame)
                TryRewind();

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool cameraBusy = _instantCamera != null && _instantCamera.IsCameraMode;
            bool rmb = mouse.rightButton.isPressed;
            if (!rmb) _waitForRmbRelease = false;
            bool wantRaise = locked && !cameraBusy && rmb && !_waitForRmbRelease && _inventory.Selected != null;
            SetRaised(wantRaise);

            if (!IsRaised) return;

            if (_inventory.Selected != _shown)
            {
                _shown = _inventory.Selected;
                RefreshOverlay();
            }

            if (kb.qKey.wasPressedThisFrame) Rotate(90f);
            if (kb.eKey.wasPressedThisFrame) Rotate(-90f);

            if (mouse.leftButton.wasPressedThisFrame)
                PlaceSelected();
        }

        void HandleSelection(Keyboard kb, Mouse mouse)
        {
            int count = _inventory.Count;
            if (count == 0) return;

            if (kb.digit1Key.wasPressedThisFrame) _inventory.SelectedIndex = 0;
            else if (kb.digit2Key.wasPressedThisFrame && count > 1) _inventory.SelectedIndex = 1;
            else if (kb.digit3Key.wasPressedThisFrame && count > 2) _inventory.SelectedIndex = 2;
            else if (kb.digit4Key.wasPressedThisFrame && count > 3) _inventory.SelectedIndex = 3;
            else if (kb.digit5Key.wasPressedThisFrame && count > 4) _inventory.SelectedIndex = 4;

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

        void Rotate(float degrees)
        {
            RollDegrees = Mathf.Repeat(RollDegrees + degrees, 360f);
            if (RollDegrees > 359.5f) RollDegrees = 0f;
            RefreshOverlay();
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

        void PlaceSelected()
        {
            var ps = ProjectionSystem.Instance;
            var photo = _inventory.Selected;
            var cam = _fpc != null ? _fpc.Camera : null;
            if (ps == null || photo == null || cam == null)
            {
                GameplayUI.Toast("The photo won't take here");
                return;
            }

            int index = _inventory.SelectedIndex;
            float roll = RollDegrees;
            // The pre-made photos are taken level; forgive a slightly tilted view.
            _fpc.SnapPitchLevel(LevelSnapDegrees);
            SetRaised(false);
            _waitForRmbRelease = true;

            EnsureSubscribed();
            _placingActive = true;
            _placedHandled = false;
            _placingPhoto = photo;
            _placingIndex = index;

            ps.Place(photo, cam, roll);

            _placingActive = false;
            if (!_placedHandled)
            {
                // Placed did not fire synchronously; consume now and ignore the late event.
                _latePlacedSkips++;
                Consume(photo, index);
            }
            _placingPhoto = null;
        }

        void Consume(PhotoData photo, int index)
        {
            _consumed.Push(new Consumed { Photo = photo, Index = index });
            _inventory.Remove(photo);
        }

        void OnPlaced()
        {
            if (_placingActive)
            {
                if (_placedHandled) return;
                _placedHandled = true;
                Consume(_placingPhoto, _placingIndex);
            }
            else if (_latePlacedSkips > 0)
            {
                _latePlacedSkips--;
            }
            else
            {
                // Placement made by another system: keep the undo stacks aligned.
                _consumed.Push(new Consumed { Photo = null, Index = -1 });
            }
        }

        void OnRewound()
        {
            if (_consumed.Count == 0) return;
            var c = _consumed.Pop();
            if (c.Photo != null && !_inventory.Contains(c.Photo))
                _inventory.Insert(c.Index, c.Photo);
        }

        void TryRewind()
        {
            var ps = ProjectionSystem.Instance;
            if (ps != null && ps.CanRewind)
            {
                SetRaised(false);
                ps.Rewind();
                GameplayUI.Toast("Rewound");
            }
            else
            {
                GameplayUI.Toast("Nothing to rewind");
            }
        }
    }
}
