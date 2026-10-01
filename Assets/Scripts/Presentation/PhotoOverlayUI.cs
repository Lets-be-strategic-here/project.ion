using Ion.Gameplay;
using Ion.Presentation.Motion;
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
    ///    with the world behind it. Raising rides a spring (2.6 Hz, ζ 0.82: ≈ 0.34 s, a 2 % settle) with
    ///    the −7° in-hand tilt easing out; lowering is a quicker 0.26 s easeInOutCubic. Both interruptible.
    ///  * Q/E: rotation is continuous and eased in PhotoHolder; the card is drawn at exactly that roll (the
    ///    placement uses the same number). Only the raise's −7° in-hand tilt rides a spring on top, and it
    ///    has settled to 0 by the time Place is enabled.
    ///  * The Polaroid lags behind mouse look on a 1.8 Hz spring (max 12 px) and walks with the head bob; it
    ///    settles exactly on the frustum footprint when the view is still, so alignment is unaffected.
    ///  * Place (art bible §9.1): LMB presses the card in (0.10 s, scale 0.985); at the world swap the card
    ///    scales to 1.08 and fades (0.30 s easeOutQuart) while ScreenFx flashes Frost.
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
        /// <summary>Legacy (raise timing now comes from Feel's springs).</summary>
        public float RaiseSeconds = Feel.RaiseSeconds;
        /// <summary>Seconds for the place card scale-out.</summary>
        public float PlaceSeconds = Feel.PlaceCardSeconds;

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
        float _t;                  // 0 = lowered pose, 1 = raised pose (spring / ease driven)
        float _tVel;               // raise spring velocity
        float _lowerFrom = -1f;    // >= 0 while lowering: the pose value the lowering started from
        float _lowerT;
        float _rollVel;
        float _tilt, _tiltVel;     // raised: offset from the logical roll (the in-hand tilt easing out)
        float _vis;                // overall visibility
        float _placeT = -1f;       // >= 0 while the place animation runs
        float _pressT = -1f;       // >= 0 while the place press-in runs

        // Sway (look lag) and bob, in canvas units.
        Vector2 _sway, _swayVel;
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
                    _tVel = 0f;
                    _shownRoll = LoweredTilt;
                    _rollVel = 0f;
                }
                _lowerFrom = -1f;
                // Whatever the card shows now eases onto the logical roll.
                _tilt = Mathf.DeltaAngle(roll, _shownRoll);
                _tiltVel = _rollVel;
            }
            _targetRoll = roll;
        }

        /// <summary>Lower the photo back into the hand (or hide it if nothing is selected).</summary>
        public void Hide()
        {
            if (_raised)
            {
                _lowerFrom = _t;
                _lowerT = 0f;
            }
            _raised = false;
            _raisedPhoto = null;
            _suppressed = null;
            _pressT = -1f;
        }

        /// <summary>LMB on a raised photo: the card presses in (0.10 s, easeOutQuad) before the world swaps.</summary>
        public void PressIn()
        {
            if (_raised) _pressT = 0f;
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
            _pressT = -1f;
            if (_display == null || _vis < 0.05f || _t < 0.5f)
            {
                _vis = 0f;
                _t = 0f;
                _tVel = 0f;
                _display = null;
                return;
            }
            _placeT = 0f;
            _lowerFrom = -1f;
        }

        void EndPlaceAnimation()
        {
            _placeT = -1f;
            _vis = 0f;
            _t = 0f;
            _tVel = 0f;
            _lowerFrom = -1f;
            _display = null;
            _frame.color = FrameColor;
            _shadow.enabled = true;
        }

        static readonly Color FrameColor = UIPalette.Frost;

        // ---------------------------------------------------------------- update

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
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

            float speed = 1f / Feel.LowerSeconds;
            if (desired != _display)
            {
                // Fade the old one out quickly, then swap.
                _vis = Mathf.MoveTowards(_vis, 0f, dt * speed * 1.5f);
                if (_vis <= 0.001f)
                {
                    SetDisplay(desired);
                    if (!_raised) { _t = 0f; _tVel = 0f; _lowerFrom = -1f; }
                }
            }
            else
            {
                _vis = Mathf.MoveTowards(_vis, desired != null ? 1f : 0f, dt * speed);
            }

            if (_raised)
            {
                // Raise: a spring (interruptible; continues from wherever the card is).
                Spring.Step(ref _t, ref _tVel, 1f, Feel.RaiseFreq, Feel.RaiseZeta, dt);
            }
            else if (_lowerFrom >= 0f)
            {
                // Lower: exits faster than it enters (easeInOutCubic from the current pose).
                _lowerT += dt;
                float k = Ease.InOutCubic(_lowerT / Feel.LowerSeconds);
                _t = _lowerFrom * (1f - k);
                _tVel = 0f;
                if (_lowerT >= Feel.LowerSeconds) { _t = 0f; _lowerFrom = -1f; }
            }
            else
            {
                _t = Mathf.MoveTowards(_t, 0f, dt * speed);
                _tVel = 0f;
            }
            // Roll: raised, the card is drawn at the exact logical roll plus the in-hand tilt easing to 0;
            // lowered, it springs back to the −7° in-hand tilt.
            if (_raised)
            {
                Spring.Step(ref _tilt, ref _tiltVel, 0f, Feel.RotateTiltFreq, Feel.RaiseTiltZeta, dt);
                if (Mathf.Abs(_tilt) < 0.005f && Mathf.Abs(_tiltVel) < 0.05f) { _tilt = 0f; _tiltVel = 0f; }
                float prev = _shownRoll;
                _shownRoll = _targetRoll + _tilt;
                _rollVel = dt > 0f ? Mathf.DeltaAngle(prev, _shownRoll) / dt : 0f;
            }
            else
            {
                Spring.StepAngle(ref _shownRoll, ref _rollVel, LoweredTilt, Feel.RotateTiltFreq, Feel.RaiseTiltZeta, dt);
            }
            if (_pressT >= 0f) _pressT += dt;

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

        /// <summary>
        /// Look lag: the Polaroid trails the view on a 1.8 Hz spring (ζ 0.75), at most 12 px, and settles back
        /// exactly on the footprint. Off with reduced motion.
        /// </summary>
        void UpdateSway(float dt)
        {
            var fpc = FirstPersonController.Current;
            if (fpc == null || Feel.ReducedMotion) { _haveLook = false; _sway = _swayVel = Vector2.zero; return; }
            float yaw = fpc.Yaw, pitch = fpc.Pitch;
            if (_haveLook && dt > 0f)
            {
                float dYaw = Mathf.DeltaAngle(_lastYaw, yaw);
                float dPitch = pitch - _lastPitch;
                if (Mathf.Abs(dYaw) < 25f && Mathf.Abs(dPitch) < 25f) // ignore teleports / snaps
                    _swayVel += new Vector2(-dYaw, dPitch) * 36f; // look speed pushes the card the other way
            }
            _lastYaw = yaw;
            _lastPitch = pitch;
            _haveLook = true;
            Spring.Step(ref _sway, ref _swayVel, Vector2.zero, Feel.SwayFreq, Feel.SwayZeta, dt);
            if (_sway.magnitude > Feel.SwayMaxPx)
            {
                _sway = _sway.normalized * Feel.SwayMaxPx;
                _swayVel *= 0.5f;
            }
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

            // The spring value is the pose (it may overshoot a hair: the 2 % settle). A shallow dip on the way
            // makes it read as coming up from below the screen edge.
            float e = _t;
            float sx = Mathf.LerpUnclamped(lowScale, raisedScaleX, e);
            float sy = Mathf.LerpUnclamped(lowScale, raisedScale, e);
            Vector2 pos = Vector2.LerpUnclamped(lowPos, Vector2.zero, e);
            pos.y -= Mathf.Sin(Mathf.Clamp01(e) * Mathf.PI) * screenH * 0.05f;
            e = Mathf.Clamp01(e);

            // Look lag + walk bob (stronger in hand than held up).
            Vector2 bob = fpc != null ? fpc.BobSignal : Vector2.zero;
            float handK = 1f - e;
            Vector2 bobOffset = new Vector2(bob.x * Mathf.Lerp(5f, 14f, handK), -Mathf.Abs(bob.y) * Mathf.Lerp(4f, 10f, handK));
            pos += _sway * Mathf.Lerp(0.7f, 1.2f, handK) + bobOffset;
            float rollSway = handK * Mathf.Clamp(_sway.x * 0.12f, -4f, 4f);

            float roll = _shownRoll + rollSway;
            float placeAlpha = 1f;
            if (_pressT >= 0f)
            {
                // Press-in: 1 → 0.985 over 0.10 s (easeOutQuad).
                float press = Mathf.Lerp(1f, Feel.PlacePressScale, Ease.OutQuad(_pressT / Feel.PlacePressSeconds));
                sx *= press;
                sy *= press;
            }
            if (_placeT >= 0f)
            {
                // The card lets go: 0.985 → 1.08 while it fades (easeOutQuart); the world is already the photo.
                float k = Mathf.Clamp01(_placeT / PlaceSeconds);
                float q = Ease.OutQuart(k);
                float grow = Mathf.Lerp(Feel.PlacePressScale, Feel.PlaceCardScale, q);
                sx *= grow;
                sy *= grow;
                placeAlpha = 1f - q;
                roll = _shownRoll;
            }

            _holder.anchoredPosition = pos;
            _holder.localScale = new Vector3(sx, sy, 1f);
            _holder.localRotation = Quaternion.Euler(0f, 0f, roll);

            var c = _image.color;
            float a = _display.Preview != null ? Mathf.Lerp(1f, RaisedImageAlpha, e) : 1f;
            if (_placeT >= 0f) a = RaisedImageAlpha * (1f - Ease.OutQuart(_placeT / (PlaceSeconds * 0.7f)));
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
                _caption.color = UIUtil.WithAlpha(UIPalette.GraphiteSoft, placeAlpha);
            }
            else if (_caption.color.a < 1f)
            {
                _caption.color = UIPalette.GraphiteSoft;
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
                    // On the Polaroid's own border: Graphite text, Brass brackets.
                    text = text.Replace("#9FE3FFB3", "#C59A45");
                    _hint.text = text;
                    _hintPill.color = new Color(0f, 0f, 0f, 0f);
                    _hint.color = UIUtil.WithAlpha(UIPalette.Graphite, 0.85f);
                    _hint.alignment = TextAnchor.MiddleCenter;
                }
                else
                {
                    _hintPill.color = UIUtil.WithAlpha(UIPalette.Graphite, 0.72f);
                    _hint.color = UIPalette.Paper;
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
            _image.color = p.Preview != null ? Color.white : UIPalette.Frost;
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
            _shadow = UIUtil.NewImage("Shadow", _holder, UIUtil.WithAlpha(UIPalette.Cyanotype, 0.2f), UIUtil.RoundedSprite, true);
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
            _caption = UIUtil.NewText("Caption", _holder, "", 36, UIPalette.GraphiteSoft, TextAnchor.MiddleCenter, FontStyle.Italic, false);
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
            _hint = UIUtil.NewText("Text", _hintRoot, "", 18, UIUtil.WithAlpha(UIPalette.Graphite, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(_hint.rectTransform);
            _hint.rectTransform.offsetMin = new Vector2(18f, 0f);
            _hint.rectTransform.offsetMax = new Vector2(-18f, 0f);
            _hintRoot.gameObject.SetActive(false);
        }
    }
}
