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
            public bool HasPose;     // where the player stood when placing (rewind fallback)
            public Vector3 Feet;
            public float Yaw;
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

        /// <summary>
        /// Automation (debug harness / tests): keeps the selected photo raised as if RMB were held.
        /// Cleared by placing, lowering or disabling input.
        /// </summary>
        public bool AutomationRaise { get; set; }

        void Update()
        {
            EnsureSubscribed();
            if (_instantCamera == null) _instantCamera = GetComponent<InstantCamera>();

            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (_fpc != null && !_fpc.InputEnabled)
            {
                AutomationRaise = false;
                SetRaised(false);
                return;
            }

            bool devices = kb != null && mouse != null;
            if (devices) HandleSelection(kb, mouse);

            SyncRoll();

            if (devices && kb.rKey.wasPressedThisFrame)
                TryRewind();

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool cameraBusy = _instantCamera != null && _instantCamera.IsCameraMode;
            bool rmb = devices && mouse.rightButton.isPressed;
            if (!rmb) _waitForRmbRelease = false;
            bool held = AutomationRaise || (locked && rmb && !_waitForRmbRelease);
            SetRaised(held && !cameraBusy && _inventory.Selected != null);

            if (!IsRaised) return;

            if (_inventory.Selected != _shown)
            {
                _shown = _inventory.Selected;
                RefreshOverlay();
            }

            if (!devices) return;
            if (kb.qKey.wasPressedThisFrame) Rotate(90f);
            if (kb.eKey.wasPressedThisFrame) Rotate(-90f);

            if (mouse.leftButton.wasPressedThisFrame)
                PlaceSelected();
        }

        /// <summary>A new selection starts upright.</summary>
        void SyncRoll()
        {
            var selected = _inventory.Selected;
            if (selected != _rollPhoto)
            {
                _rollPhoto = selected;
                RollDegrees = 0f;
            }
        }

        /// <summary>Raises the selected photo now (automation; same state as holding RMB). False if nothing to raise.</summary>
        public bool Raise()
        {
            if (_inventory.Selected == null) return false;
            if (_instantCamera != null && _instantCamera.IsCameraMode) _instantCamera.SetCameraMode(false);
            SyncRoll();
            AutomationRaise = true;
            SetRaised(true);
            return IsRaised;
        }

        /// <summary>Lowers the photo (automation; same as releasing RMB).</summary>
        public void Lower()
        {
            AutomationRaise = false;
            SetRaised(false);
        }

        /// <summary>
        /// Places the raised photo (what LMB does while holding a photo up). Returns false if no photo is
        /// raised or the placement could not be made.
        /// </summary>
        public bool Place()
        {
            if (!IsRaised || _inventory.Selected == null) return false;
            int before = _inventory.Count;
            PlaceSelected();
            return _inventory.Count < before;
        }

        /// <summary>Rewinds the last placement (what R does). Returns false if there was nothing to rewind.</summary>
        public bool Rewind()
        {
            var ps = ProjectionSystem.Instance;
            bool could = ps != null && ps.CanRewind;
            TryRewind();
            return could;
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

        /// <summary>Rotates the held photo by <paramref name="degrees"/> (Q = +90, E = −90).</summary>
        public void Rotate(float degrees)
        {
            SyncRoll();
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
            AutomationRaise = false;
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
            _consumed.Push(new Consumed
            {
                Photo = photo,
                Index = index,
                HasPose = _fpc != null,
                Feet = transform.position,
                Yaw = _fpc != null ? _fpc.Yaw : 0f,
            });
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

            // The rewound geometry may have been what the player stood on (mid-bridge, in a pasted pit)
            // or the restored world may now enclose them (standing in a cut doorway). Then put them back
            // where they placed the photo from, which was solid ground before the placement.
            if (c.HasPose && _fpc != null && !IsSafeStandingSpot(transform.position))
                _fpc.Teleport(c.Feet, c.Yaw);
        }

        const int WorldQueryMask = ~ProjectionSystem.ExcludedLayerMask;

        /// <summary>
        /// True if a standing player at <paramref name="feet"/> has ground within reach below and is
        /// neither intersecting nor enclosed by world geometry.
        /// </summary>
        static bool IsSafeStandingSpot(Vector3 feet)
        {
            Physics.SyncTransforms();
            const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;
            float r = PlayerFactory.Radius * 0.85f;

            // Ground below (generous, so a jump in progress over solid ground counts as safe).
            if (!Physics.SphereCast(feet + Vector3.up * 1f, r, Vector3.down, out _, 4f, WorldQueryMask, ignore))
                return false;

            // Intersecting surfaces (above the step offset).
            Vector3 lo = feet + Vector3.up * (PlayerFactory.Radius + 0.45f);
            Vector3 hi = feet + Vector3.up * (PlayerFactory.Height - PlayerFactory.Radius);
            if (Physics.CheckCapsule(lo, hi, r, WorldQueryMask, ignore))
                return false;

            // Enclosed: from inside a closed mesh the first surface hit in any direction is a back face.
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                Vector3 chest = feet + Vector3.up * 1f;
                Vector3[] dirs = { Vector3.up, Vector3.right, Vector3.forward };
                for (int i = 0; i < dirs.Length; i++)
                {
                    if (Physics.Raycast(chest, dirs[i], out RaycastHit hit, 60f, WorldQueryMask, ignore) &&
                        Vector3.Dot(hit.normal, dirs[i]) > 0f)
                        return false;
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = backfaces;
            }
            return true;
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
