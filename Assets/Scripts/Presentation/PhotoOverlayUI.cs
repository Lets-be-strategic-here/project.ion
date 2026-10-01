using Ion.Gameplay;
using Ion.Projection;
using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// Polaroid overlay for the held photo.
    ///  * Lowered: the selected inventory photo rests small in the bottom-right corner ("in hand").
    ///  * Raised (Show): the inner image is sized to the exact screen footprint of the photo frustum
    ///    at the player camera, rotated by the roll, slightly translucent so it can be aligned
    ///    with the world behind it.
    ///  * When ProjectionSystem.Placed fires, the raised photo vanishes instantly with a soft flash
    ///    (the world now looks like the photo).
    /// </summary>
    public sealed class PhotoOverlayUI : MonoBehaviour
    {
        public static PhotoOverlayUI Instance { get; private set; }

        /// <summary>Alpha of the photo image while raised (frame stays opaque).</summary>
        public float RaisedImageAlpha = 0.86f;
        /// <summary>Show the selected photo small in the corner when nothing is raised.</summary>
        public bool ShowLoweredPreview = true;
        /// <summary>Lowered preview height as a fraction of the screen height.</summary>
        public float LoweredHeightFraction = 0.2f;
        /// <summary>Seconds for the raise / lower animation.</summary>
        public float RaiseSeconds = 0.16f;

        public bool IsShown => _raised;

        const float RefHeight = 500f;               // reference image height in holder units
        const float SideBorder = RefHeight * 0.055f;
        const float BottomBorder = RefHeight * 0.20f;
        const float LoweredTilt = -7f;

        RectTransform _root;
        CanvasGroup _group;
        RectTransform _holder;
        RawImage _image;
        Text _caption;
        Image _flash;

        PhotoData _raisedPhoto;
        PhotoData _suppressed;     // photo just placed: not re-shown until lowered
        bool _raised;
        float _targetRoll;
        float _shownRoll;

        PhotoData _display;        // photo currently on the Polaroid
        float _t;                  // 0 = lowered pose, 1 = raised pose
        float _vis;                // overall visibility
        float _flashAlpha;

        Camera _camera;
        PhotoInventory _inventory;
        InstantCamera _instantCamera;
        ProjectionSystem _projection;
        float _nextSearch;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (_projection != null)
            {
                _projection.Placed -= OnPlaced;
                _projection.Rewound -= OnRewound;
            }
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- public API

        /// <summary>Raise the photo. Idempotent; call again to update the roll.</summary>
        public void Show(PhotoData p, float roll)
        {
            if (p == null) { Hide(); return; }
            if (!_raised || p != _raisedPhoto)
            {
                _raisedPhoto = p;
                _raised = true;
                if (_display != p)
                {
                    // Different photo than the one in hand: swap now, start from the lowered pose.
                    SetDisplay(p);
                    _t = 0f;
                    _shownRoll = LoweredTilt;
                }
            }
            _targetRoll = roll;
        }

        /// <summary>Lower the photo back into the hand (or hide it if nothing is selected).</summary>
        public void Hide()
        {
            _raised = false;
            _raisedPhoto = null;
            _suppressed = null;
        }

        /// <summary>Bind to a camera explicitly (otherwise the player's camera / Camera.main is used).</summary>
        public void BindCamera(Camera cam) { _camera = cam; }

        // ---------------------------------------------------------------- events

        void OnPlaced()
        {
            // The world now matches the photo: drop the overlay instantly and flash.
            _suppressed = _raisedPhoto;
            _vis = 0f;
            _t = 0f;
            _display = null;
            _flashAlpha = 0.55f;
        }

        void OnRewound()
        {
            _flashAlpha = Mathf.Max(_flashAlpha, 0.3f);
        }

        // ---------------------------------------------------------------- update

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            FindDependencies();

            // Which photo should be on the Polaroid?
            // The lowered "in hand" photo is hidden while the instant camera is out (its viewfinder owns the screen).
            bool cameraOut = _instantCamera != null && _instantCamera.IsCameraMode;
            PhotoData desired = _raised
                ? (_raisedPhoto == _suppressed ? null : _raisedPhoto)
                : (ShowLoweredPreview && !cameraOut ? SelectedPhoto() : null);

            float speed = 1f / Mathf.Max(0.01f, RaiseSeconds);
            if (desired != _display)
            {
                // Fade the old one out quickly, then swap.
                _vis = Mathf.MoveTowards(_vis, 0f, dt * speed * 1.5f);
                if (_vis <= 0.001f)
                {
                    SetDisplay(desired);
                    _t = _raised ? _t : 0f;
                }
            }
            else
            {
                _vis = Mathf.MoveTowards(_vis, desired != null ? 1f : 0f, dt * speed);
            }

            _t = Mathf.MoveTowards(_t, _raised ? 1f : 0f, dt * speed);
            _shownRoll = Mathf.MoveTowardsAngle(_shownRoll, _raised ? _targetRoll : LoweredTilt, dt * 900f);

            // Flash.
            if (_flashAlpha > 0f)
            {
                _flashAlpha = Mathf.MoveTowards(_flashAlpha, 0f, dt * 1.8f);
                _flash.color = new Color(1f, 0.98f, 0.94f, _flashAlpha);
                if (!_flash.enabled) _flash.enabled = true;
            }
            else if (_flash.enabled)
            {
                _flash.enabled = false;
            }

            _group.alpha = _vis;
            bool visible = _vis > 0.001f && _display != null;
            if (_holder.gameObject.activeSelf != visible) _holder.gameObject.SetActive(visible);
            if (!visible) return;

            Layout();
        }

        void Layout()
        {
            Rect r = _root.rect;
            float screenW = Mathf.Max(1f, r.width);
            float screenH = Mathf.Max(1f, r.height);

            float aspect = PhotoAspect(_display);

            // Raised pose: footprint of the photo frustum at the player camera.
            float camFov = 60f;
            float camAspect = screenW / screenH;
            if (_camera != null)
            {
                camFov = _camera.fieldOfView;
                camAspect = _camera.aspect;
            }
            float photoFov = _display.FovY > 0.1f ? _display.FovY : camFov;
            float heightFrac = Mathf.Tan(photoFov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(camFov * 0.5f * Mathf.Deg2Rad);
            // width fraction (of screen width) = heightFrac * aspect / camAspect, i.e. in canvas units
            // width = heightFrac * screenH * aspect when the canvas matches the camera aspect.
            float raisedImageH = heightFrac * screenH;
            float raisedImageW = heightFrac * aspect / camAspect * screenW;
            float raisedScale = raisedImageH / RefHeight;
            // Holder is RefHeight*aspect wide, so non-uniform scale corrects any canvas/camera aspect mismatch.
            float raisedScaleX = raisedImageW / (RefHeight * aspect);

            // Lowered pose: small in the bottom-right corner.
            float lowH = LoweredHeightFraction * screenH;
            float lowScale = lowH / RefHeight;
            float lowW = lowH * aspect;
            Vector2 lowPos = new Vector2(screenW * 0.5f - lowW * 0.5f - screenW * 0.05f,
                                         -screenH * 0.5f + lowH * 0.5f + BottomBorder * lowScale + screenH * 0.06f);

            float e = _t * _t * (3f - 2f * _t); // smoothstep
            float sx = Mathf.Lerp(lowScale, raisedScaleX, e);
            float sy = Mathf.Lerp(lowScale, raisedScale, e);
            _holder.anchoredPosition = Vector2.Lerp(lowPos, Vector2.zero, e);
            _holder.localScale = new Vector3(sx, sy, 1f);
            _holder.localRotation = Quaternion.Euler(0f, 0f, _shownRoll);

            var c = _image.color;
            float a = _display.Preview != null ? Mathf.Lerp(1f, RaisedImageAlpha, e) : 1f;
            if (!Mathf.Approximately(c.a, a))
            {
                c.a = a;
                _image.color = c;
            }
        }

        // ---------------------------------------------------------------- helpers

        void FindDependencies()
        {
            if (_projection == null)
            {
                var ps = ProjectionSystem.Instance;
                if (ps != null)
                {
                    _projection = ps;
                    _projection.Placed += OnPlaced;
                    _projection.Rewound += OnRewound;
                }
            }

            if ((_camera == null || _inventory == null || _instantCamera == null) && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 0.5f;
                if (_instantCamera == null)
                    _instantCamera = Object.FindFirstObjectByType<InstantCamera>();
                if (_camera == null)
                {
                    var fpc = Object.FindFirstObjectByType<FirstPersonController>();
                    if (fpc != null) _camera = fpc.Camera;
                    if (_camera == null) _camera = Camera.main;
                }
                if (_inventory == null)
                    _inventory = Object.FindFirstObjectByType<PhotoInventory>();
            }
        }

        PhotoData SelectedPhoto()
        {
            if (_inventory == null) return null;
            var photos = _inventory.Photos;
            if (photos == null) return null;
            int i = _inventory.SelectedIndex;
            return (i >= 0 && i < photos.Count) ? photos[i] : null;
        }

        static float PhotoAspect(PhotoData p)
        {
            return (p != null && p.Aspect > 0.01f) ? p.Aspect : 4f / 3f;
        }

        void SetDisplay(PhotoData p)
        {
            _display = p;
            if (p == null) return;

            float aspect = PhotoAspect(p);
            _holder.sizeDelta = new Vector2(RefHeight * aspect, RefHeight);
            _image.texture = p.Preview;
            _image.color = p.Preview != null ? Color.white : Palette.Sky;
            _caption.text = string.IsNullOrEmpty(p.Label) ? "" : p.Label;
        }

        // ---------------------------------------------------------------- construction

        void Build()
        {
            _root = (RectTransform)transform;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = 0f;

            _holder = UIUtil.NewRect("Polaroid", _root);
            _holder.anchorMin = _holder.anchorMax = new Vector2(0.5f, 0.5f);
            _holder.pivot = new Vector2(0.5f, 0.5f);
            _holder.sizeDelta = new Vector2(RefHeight * 4f / 3f, RefHeight);

            // Drop shadow.
            var shadow = UIUtil.NewImage("Shadow", _holder, new Color(0.10f, 0.10f, 0.20f, 0.22f), UIUtil.RoundedSprite, true);
            var srt = shadow.rectTransform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(-SideBorder + 10f, -BottomBorder - 14f);
            srt.offsetMax = new Vector2(SideBorder + 10f, SideBorder - 14f);

            // White Polaroid frame (thicker bottom).
            var frame = UIUtil.NewImage("Frame", _holder, new Color32(0xFD, 0xFB, 0xF6, 0xFF), UIUtil.RoundedSprite, true);
            frame.pixelsPerUnitMultiplier = 1.5f;
            var frt = frame.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(-SideBorder, -BottomBorder);
            frt.offsetMax = new Vector2(SideBorder, SideBorder);

            // Photo (exactly the holder rect = the frustum footprint when raised).
            _image = UIUtil.NewRaw("Photo", _holder);
            UIUtil.Stretch(_image.rectTransform);

            // Thin inner edge so the photo reads against the frame.
            var edge = UIUtil.NewImage("Edge", _holder, new Color(0f, 0f, 0f, 0.06f));
            var ert = edge.rectTransform;
            ert.anchorMin = new Vector2(0f, 0f);
            ert.anchorMax = new Vector2(1f, 0f);
            ert.pivot = new Vector2(0.5f, 1f);
            ert.anchoredPosition = Vector2.zero;
            ert.sizeDelta = new Vector2(0f, 3f);

            // Hand-written-ish caption on the bottom strip.
            _caption = UIUtil.NewText("Caption", _holder, "", 38, Palette.Slate, TextAnchor.MiddleCenter, FontStyle.Italic, false);
            var crt = _caption.rectTransform;
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = new Vector2(0f, -6f);
            crt.sizeDelta = new Vector2(0f, BottomBorder - 12f);

            _holder.gameObject.SetActive(false);

            // Full-screen flash used when a photo is placed / rewound (outside the CanvasGroup fade).
            var flashRt = UIUtil.NewRect("Flash", _root.parent != null ? _root.parent : _root);
            UIUtil.Stretch(flashRt);
            _flash = flashRt.gameObject.AddComponent<Image>();
            _flash.raycastTarget = false;
            _flash.color = new Color(1f, 1f, 1f, 0f);
            _flash.enabled = false;
            // Keep the flash just above the overlay.
            flashRt.SetSiblingIndex(_root.GetSiblingIndex() + 1);
        }
    }
}
