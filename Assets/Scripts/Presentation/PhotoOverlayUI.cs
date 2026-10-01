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
    ///    with the world behind it. Raising slides it up from the bottom with an ease-out; lowering
    ///    plays the same motion backwards.
    ///  * The Polaroid lags a little behind mouse look and walks with the head bob (it settles exactly on
    ///    the frustum footprint when the view is still, so alignment is unaffected).
    ///  * When ProjectionSystem.Placed fires, the frame scales out past the screen edges while the image
    ///    fades: the photo "becomes" the world (the flash itself is <see cref="ScreenFx"/>).
    ///  * The controls hint stays upright whatever the roll: on the bottom border when the photo is
    ///    upright, otherwise under / beside the rotated frame in a small pill.
    /// </summary>
    public sealed class PhotoOverlayUI : MonoBehaviour
    {
        public static PhotoOverlayUI Instance { get; private set; }

        /// <summary>Alpha of the photo image while raised (frame stays opaque).</summary>
        public float RaisedImageAlpha = 0.9f;
        /// <summary>Show the selected photo small in the corner when nothing is raised.</summary>
        public bool ShowLoweredPreview = true;
        /// <summary>Lowered preview height as a fraction of the screen height.</summary>
        public float LoweredHeightFraction = 0.2f;
        /// <summary>Seconds for the raise / lower animation.</summary>
        public float RaiseSeconds = 0.2f;
        /// <summary>Seconds for the place "scale out" animation.</summary>
        public float PlaceSeconds = 0.24f;

        public bool IsShown => _raised;

        const float RefHeight = 500f;               // reference image height in holder units
        const float SideBorder = RefHeight * 0.055f;
        const float BottomBorder = RefHeight * 0.20f;
        const float LoweredTilt = -7f;

        RectTransform _root;
        CanvasGroup _group;
        RectTransform _holder;
        Image _frame;
        Image _shadow;
        RawImage _image;
        Text _caption;

        RectTransform _hintRoot;
        Image _hintPill;
        Text _hint;
        string _hintText;
        int _hintMode = -1;

        PhotoData _raisedPhoto;
        PhotoData _suppressed;     // photo just placed: not re-shown until lowered
        bool _raised;
        float _targetRoll;
        float _shownRoll;

        PhotoData _display;        // photo currently on the Polaroid
        float _t;                  // 0 = lowered pose, 1 = raised pose (linear in time)
        float _vis;                // overall visibility
        float _placeT = -1f;       // >= 0 while the place animation runs

        // Sway (look lag) and bob, in canvas units.
        Vector2 _sway;
        float _lastYaw, _lastPitch;
        bool _haveLook;

        Rect _frameRect;
        bool _frameRectValid;

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
                _projection.Placed -= OnPlaced;
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
                if (_placeT >= 0f) EndPlaceAnimation();
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

        /// <summary>Controls line shown with the raised Polaroid (null hides it).</summary>
        public void SetHint(string text)
        {
            if (text == _hintText || _hint == null) return;
            _hintText = text;
            _hintMode = -1; // re-layout
        }

        /// <summary>Bind to a camera explicitly (otherwise the player's camera / Camera.main is used).</summary>
        public void BindCamera(Camera cam) { _camera = cam; }

        /// <summary>
        /// The raised Polaroid's axis-aligned bounds (frame included) in canvas units, relative to the
        /// canvas centre. False while no photo is (mostly) raised.
        /// </summary>
        public bool TryGetRaisedFrameRect(out Rect rect)
        {
            rect = _frameRect;
            return _frameRectValid;
        }

        // ---------------------------------------------------------------- events

        void OnPlaced()
        {
            // The world now matches the photo: the frame scales out past the screen edges and fades.
            if (_display == null || _vis < 0.05f || _t < 0.5f)
            {
                _vis = 0f;
                _t = 0f;
                _display = null;
                return;
            }
            _placeT = 0f;
        }

        void EndPlaceAnimation()
        {
            _placeT = -1f;
            _vis = 0f;
            _t = 0f;
            _display = null;
            _frame.color = FrameColor;
            _shadow.enabled = true;
        }

        static readonly Color FrameColor = new Color32(0xFD, 0xFB, 0xF6, 0xFF);

        // ---------------------------------------------------------------- update

        static float EaseOutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            FindDependencies();
            UpdateSway(dt);

            if (_placeT >= 0f)
            {
                _placeT += Mathf.Min(dt, 0.05f);
                if (_placeT >= PlaceSeconds)
                {
                    EndPlaceAnimation();
                }
                else
                {
                    _group.alpha = 1f;
                    Layout();
                    _frameRectValid = false;
                    return;
                }
            }

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

            _group.alpha = _vis;
            bool visible = _vis > 0.001f && _display != null;
            if (_holder.gameObject.activeSelf != visible) _holder.gameObject.SetActive(visible);
            if (!visible)
            {
                _frameRectValid = false;
                if (_hintRoot.gameObject.activeSelf) _hintRoot.gameObject.SetActive(false);
                return;
            }

            Layout();
        }

        /// <summary>Look lag: the Polaroid trails the view a little and springs back.</summary>
        void UpdateSway(float dt)
        {
            var fpc = FirstPersonController.Current;
            if (fpc == null) { _haveLook = false; return; }
            float yaw = fpc.Yaw, pitch = fpc.Pitch;
            if (_haveLook)
            {
                float dYaw = Mathf.DeltaAngle(_lastYaw, yaw);
                float dPitch = pitch - _lastPitch;
                if (Mathf.Abs(dYaw) < 25f && Mathf.Abs(dPitch) < 25f) // ignore teleports / snaps
                    _sway += new Vector2(-dYaw, dPitch) * 2.4f;
            }
            _lastYaw = yaw;
            _lastPitch = pitch;
            _haveLook = true;
            _sway = Vector2.ClampMagnitude(_sway, 42f);
            _sway *= Mathf.Exp(-dt * 10f);
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
            var fpc = FirstPersonController.Current;
            if (_camera != null)
            {
                camFov = fpc != null && fpc.Camera == _camera ? fpc.BaseFieldOfView : _camera.fieldOfView;
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

            // Ease-out raise; lowering plays the same curve backwards. A shallow dip on the way makes it
            // read as coming up from below the screen edge.
            float e = EaseOutCubic(_t);
            float sx = Mathf.Lerp(lowScale, raisedScaleX, e);
            float sy = Mathf.Lerp(lowScale, raisedScale, e);
            Vector2 pos = Vector2.Lerp(lowPos, Vector2.zero, e);
            pos.y -= Mathf.Sin(e * Mathf.PI) * screenH * 0.07f;

            // Look lag + walk bob (stronger in hand than held up).
            Vector2 bob = fpc != null ? fpc.BobSignal : Vector2.zero;
            float handK = 1f - e;
            Vector2 bobOffset = new Vector2(bob.x * Mathf.Lerp(5f, 14f, handK), -Mathf.Abs(bob.y) * Mathf.Lerp(4f, 10f, handK));
            pos += _sway * Mathf.Lerp(0.7f, 1.2f, handK) + bobOffset;
            float rollSway = handK * Mathf.Clamp(_sway.x * 0.12f, -4f, 4f);

            float roll = _shownRoll + rollSway;
            float placeAlpha = 1f;
            if (_placeT >= 0f)
            {
                // Scale out to fill the screen while the image fades into the (now identical) world.
                float k = Mathf.Clamp01(_placeT / PlaceSeconds);
                float grow = 1f + 0.85f * EaseOutCubic(k);
                sx *= grow;
                sy *= grow;
                placeAlpha = 1f - k * k;
                roll = _shownRoll;
            }

            _holder.anchoredPosition = pos;
            _holder.localScale = new Vector3(sx, sy, 1f);
            _holder.localRotation = Quaternion.Euler(0f, 0f, roll);

            var c = _image.color;
            float a = _display.Preview != null ? Mathf.Lerp(1f, RaisedImageAlpha, e) : 1f;
            if (_placeT >= 0f) a = RaisedImageAlpha * Mathf.Clamp01(1f - _placeT / (PlaceSeconds * 0.6f));
            if (!Mathf.Approximately(c.a, a))
            {
                c.a = a;
                _image.color = c;
            }
            if (_placeT >= 0f)
            {
                Color fc = FrameColor;
                fc.a = placeAlpha;
                _frame.color = fc;
                _shadow.enabled = false;
                _caption.color = UIUtil.WithAlpha(Palette.Slate, placeAlpha);
            }
            else if (_caption.color.a < 1f)
            {
                _caption.color = Palette.Slate;
            }

            // Bounds of the whole frame (border included), for the hint and the HUD toast.
            float hw = RefHeight * aspect * 0.5f;
            float hh = RefHeight * 0.5f;
            Quaternion rot = Quaternion.Euler(0f, 0f, roll);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                float lx = (i & 1) == 0 ? -hw - SideBorder : hw + SideBorder;
                float ly = (i & 2) == 0 ? -hh - BottomBorder : hh + SideBorder;
                Vector3 p = rot * new Vector3(lx * sx, ly * sy, 0f);
                Vector2 q = new Vector2(p.x, p.y) + pos;
                min = Vector2.Min(min, q);
                max = Vector2.Max(max, q);
            }
            _frameRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            _frameRectValid = _raised && _placeT < 0f && e > 0.6f && _vis > 0.5f;

            LayoutHint(r, pos, sy, e);
        }

        /// <summary>
        /// Upright controls hint. Mode 0: on the bottom border of an upright photo. Mode 1: centred under
        /// the frame. Mode 2: stacked in the gutter right of the frame. Mode 3: bottom of the screen.
        /// </summary>
        void LayoutHint(Rect screen, Vector2 holderPos, float sy, float e)
        {
            bool show = !string.IsNullOrEmpty(_hintText) && _raised && _placeT < 0f && e > 0.5f;
            if (_hintRoot.gameObject.activeSelf != show) _hintRoot.gameObject.SetActive(show);
            if (!show) return;

            int mode;
            bool upright = Mathf.Abs(Mathf.DeltaAngle(_shownRoll, 0f)) < 1f;
            float below = _frameRect.yMin - screen.yMin;
            float right = screen.xMax - _frameRect.xMax;
            if (upright) mode = 0;
            else if (below >= 54f) mode = 1;
            else if (right >= 250f) mode = 2;
            else mode = 3;

            if (mode != _hintMode)
            {
                _hintMode = mode;
                string text = _hintText ?? string.Empty;
                if (mode == 2) text = text.Replace("      ", "\n");
                _hint.text = text;
                if (mode == 0)
                {
                    _hintPill.color = new Color(0f, 0f, 0f, 0f);
                    _hint.color = UIUtil.WithAlpha(Palette.Slate, 0.85f);
                    _hint.alignment = TextAnchor.MiddleCenter;
                }
                else
                {
                    _hintPill.color = UIUtil.WithAlpha(Palette.Ink, 0.62f);
                    _hint.color = Palette.Cream;
                    _hint.alignment = mode == 2 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                }
                _hint.fontSize = mode == 0 ? 18 : 19;
                _hint.lineSpacing = 1.25f;
            }
            if (mode == 0) _hint.fontSize = Mathf.Clamp(Mathf.RoundToInt(15f * sy), 13, 26);

            Vector2 size = new Vector2(_hint.preferredWidth + 36f, _hint.preferredHeight + 18f);
            _hintRoot.sizeDelta = size;
            Vector2 at;
            switch (mode)
            {
                case 0:
                    // Under the caption on the thick bottom border.
                    at = holderPos + new Vector2(0f, (-RefHeight * 0.5f - 4f - BottomBorder * 0.75f) * sy);
                    break;
                case 1:
                    at = new Vector2((_frameRect.xMin + _frameRect.xMax) * 0.5f, _frameRect.yMin - 10f - size.y * 0.5f);
                    break;
                case 2:
                    at = new Vector2(_frameRect.xMax + 24f + size.x * 0.5f, _frameRect.yMin + 24f + size.y * 0.5f);
                    break;
                default:
                    at = new Vector2(0f, screen.yMin + 18f + size.y * 0.5f);
                    break;
            }
            _hintRoot.anchoredPosition = at;
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
            PhotoData p = (i >= 0 && i < photos.Count) ? photos[i] : null;
            // A photo still flying into the strip (new print, pickup, rewind) reaches the hand when it lands.
            if (p != null && Hud.Instance != null && Hud.Instance.IsArriving(p)) return null;
            return p;
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
            _shadow = UIUtil.NewImage("Shadow", _holder, new Color(0.10f, 0.10f, 0.20f, 0.22f), UIUtil.RoundedSprite, true);
            var srt = _shadow.rectTransform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(-SideBorder + 10f, -BottomBorder - 14f);
            srt.offsetMax = new Vector2(SideBorder + 10f, SideBorder - 14f);

            // White Polaroid frame (thicker bottom).
            _frame = UIUtil.NewImage("Frame", _holder, FrameColor, UIUtil.RoundedSprite, true);
            _frame.pixelsPerUnitMultiplier = 1.5f;
            var frt = _frame.rectTransform;
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
            _caption = UIUtil.NewText("Caption", _holder, "", 36, Palette.Slate, TextAnchor.MiddleCenter, FontStyle.Italic, false);
            var crt = _caption.rectTransform;
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = new Vector2(0f, -4f);
            crt.sizeDelta = new Vector2(0f, BottomBorder * 0.6f);

            _holder.gameObject.SetActive(false);

            // Controls hint: a sibling of the Polaroid (never rotated with it).
            _hintPill = UIUtil.NewImage("Hint", _root, new Color(0f, 0f, 0f, 0f), UIUtil.RoundedSprite, true);
            _hintRoot = _hintPill.rectTransform;
            _hintRoot.anchorMin = _hintRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _hintRoot.pivot = new Vector2(0.5f, 0.5f);
            _hint = UIUtil.NewText("Text", _hintRoot, "", 18, UIUtil.WithAlpha(Palette.Slate, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(_hint.rectTransform);
            _hint.rectTransform.offsetMin = new Vector2(18f, 0f);
            _hint.rectTransform.offsetMax = new Vector2(-18f, 0f);
            _hintRoot.gameObject.SetActive(false);
        }
    }
}
