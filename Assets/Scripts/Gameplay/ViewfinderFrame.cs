using Ion.Presentation;
using Ion.Presentation.Motion;
using UnityEngine;
using UnityEngine.UI;

namespace Ion.Gameplay
{
    /// <summary>
    /// Screen-space viewfinder for the instant camera: darkens the area outside the capture region (the
    /// snapshot's 50°, 4:3 frustum as seen through the player camera, so what is framed is what is taken),
    /// draws crop-mark corner ticks and a centre cross, and plays the shutter (art bible §9.1):
    ///  * raise: the corner ticks slide in from outside (0.25 s easeOutCubic) while the mask fades in;
    ///  * shutter: two Graphite blades close (0.05 s linear) and open (0.12 s easeOutQuad), then a Frost
    ///    flash fades from 25 % (0.25 s).
    /// Built entirely from code (uGUI Images, no sprites/fonts needed).
    /// </summary>
    internal sealed class ViewfinderFrame : MonoBehaviour
    {
        const float CornerInset = 28f;
        const float CornerLength = 56f;
        const float CornerThickness = 4f;
        const float TickTravel = 48f;     // the ticks slide in from this far outside
        const float HideSeconds = 0.15f;

        static readonly Color MaskColor = UIUtil.WithAlpha(UIPalette.Graphite, 0.55f);
        static readonly Color LineColor = UIUtil.WithAlpha(UIPalette.Paper, 0.92f);

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _frame;
        RectTransform _maskLeft, _maskRight, _maskTop, _maskBottom;
        readonly RectTransform[] _corners = new RectTransform[4];
        readonly Vector2[] _cornerDir = new Vector2[4];
        GameObject _frameRoot;
        Image _flash;
        RectTransform _bladeTop, _bladeBottom;

        float _aspect = 4f / 3f;
        float _fovY = 50f;
        float _lastCamFov = -1f;
        int _lastW = -1, _lastH = -1;
        float _shutterT = -1f;
        bool _visible;
        float _show;   // 0..1, time-linear show driver

        public static ViewfinderFrame Create(float aspect, float fovY)
        {
            var go = new GameObject("ViewfinderCanvas", typeof(RectTransform));
            go.layer = 5; // UI
            var vf = go.AddComponent<ViewfinderFrame>();
            vf._aspect = aspect;
            vf._fovY = fovY;
            vf.Build();
            return vf;
        }

        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                if (value && _frameRoot != null) _frameRoot.SetActive(true);
                UpdateCanvasEnabled();
            }
        }

        /// <summary>The shutter: blades close and open, then the Frost flash.</summary>
        public void Flash()
        {
            _shutterT = 0f;
            _flash.enabled = true;
            _bladeTop.gameObject.SetActive(true);
            _bladeBottom.gameObject.SetActive(true);
            UpdateCanvasEnabled();
        }

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40;

            var rootRect = (RectTransform)transform;

            _frameRoot = new GameObject("Viewfinder", typeof(RectTransform));
            _frameRoot.layer = 5;
            var frameRootRect = (RectTransform)_frameRoot.transform;
            frameRootRect.SetParent(rootRect, false);
            Stretch(frameRootRect);
            _group = _frameRoot.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = 0f;

            _maskLeft = MakeImage("MaskL", frameRootRect, MaskColor).rectTransform;
            _maskLeft.anchorMin = new Vector2(0f, 0f);
            _maskLeft.anchorMax = new Vector2(0f, 1f);
            _maskLeft.pivot = new Vector2(0f, 0.5f);
            _maskLeft.anchoredPosition = Vector2.zero;

            _maskRight = MakeImage("MaskR", frameRootRect, MaskColor).rectTransform;
            _maskRight.anchorMin = new Vector2(1f, 0f);
            _maskRight.anchorMax = new Vector2(1f, 1f);
            _maskRight.pivot = new Vector2(1f, 0.5f);
            _maskRight.anchoredPosition = Vector2.zero;

            _maskTop = MakeImage("MaskT", frameRootRect, MaskColor).rectTransform;
            _maskTop.anchorMin = new Vector2(0.5f, 1f);
            _maskTop.anchorMax = new Vector2(0.5f, 1f);
            _maskTop.pivot = new Vector2(0.5f, 1f);
            _maskTop.anchoredPosition = Vector2.zero;

            _maskBottom = MakeImage("MaskB", frameRootRect, MaskColor).rectTransform;
            _maskBottom.anchorMin = new Vector2(0.5f, 0f);
            _maskBottom.anchorMax = new Vector2(0.5f, 0f);
            _maskBottom.pivot = new Vector2(0.5f, 0f);
            _maskBottom.anchoredPosition = Vector2.zero;

            var frameGo = new GameObject("Frame", typeof(RectTransform));
            frameGo.layer = 5;
            _frame = (RectTransform)frameGo.transform;
            _frame.SetParent(frameRootRect, false);
            _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f);
            _frame.pivot = new Vector2(0.5f, 0.5f);

            // Corner ticks (crop marks): one horizontal + one vertical bar per corner, grouped so each
            // corner can slide in as a unit.
            int k = 0;
            for (int cx = 0; cx < 2; cx++)
            for (int cy = 0; cy < 2; cy++)
            {
                var corner = new Vector2(cx, cy);
                var dir = new Vector2(cx == 0 ? 1f : -1f, cy == 0 ? 1f : -1f);
                var inset = new Vector2(dir.x * CornerInset, dir.y * CornerInset);

                var group = new GameObject("Corner", typeof(RectTransform));
                group.layer = 5;
                var g = (RectTransform)group.transform;
                g.SetParent(_frame, false);
                g.anchorMin = g.anchorMax = corner;
                g.pivot = corner;
                g.sizeDelta = new Vector2(CornerLength, CornerLength);
                g.anchoredPosition = inset;
                _corners[k] = g;
                _cornerDir[k] = dir;
                k++;

                var h = MakeImage("CornerH", g, LineColor).rectTransform;
                h.anchorMin = h.anchorMax = corner;
                h.pivot = corner;
                h.sizeDelta = new Vector2(CornerLength, CornerThickness);
                h.anchoredPosition = Vector2.zero;

                var v = MakeImage("CornerV", g, LineColor).rectTransform;
                v.anchorMin = v.anchorMax = corner;
                v.pivot = corner;
                v.sizeDelta = new Vector2(CornerThickness, CornerLength);
                v.anchoredPosition = Vector2.zero;
            }

            // Centre cross with an Ion dot (the shutter is the thing you use).
            var ch = MakeImage("CrossH", _frame, LineColor).rectTransform;
            ch.anchorMin = ch.anchorMax = ch.pivot = new Vector2(0.5f, 0.5f);
            ch.sizeDelta = new Vector2(22f, 2f);
            var cv = MakeImage("CrossV", _frame, LineColor).rectTransform;
            cv.anchorMin = cv.anchorMax = cv.pivot = new Vector2(0.5f, 0.5f);
            cv.sizeDelta = new Vector2(2f, 22f);
            var dot = MakeImage("Dot", _frame, UIPalette.Ion).rectTransform;
            dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(0.5f, 0.5f);
            dot.sizeDelta = new Vector2(4f, 4f);

            // Shutter blades: close from the top and bottom of the frame.
            _bladeTop = MakeImage("BladeTop", _frame, UIPalette.Graphite).rectTransform;
            _bladeTop.anchorMin = new Vector2(0f, 1f);
            _bladeTop.anchorMax = new Vector2(1f, 1f);
            _bladeTop.pivot = new Vector2(0.5f, 1f);
            _bladeBottom = MakeImage("BladeBottom", _frame, UIPalette.Graphite).rectTransform;
            _bladeBottom.anchorMin = new Vector2(0f, 0f);
            _bladeBottom.anchorMax = new Vector2(1f, 0f);
            _bladeBottom.pivot = new Vector2(0.5f, 0f);
            _bladeTop.gameObject.SetActive(false);
            _bladeBottom.gameObject.SetActive(false);

            _flash = MakeImage("Flash", rootRect, UIUtil.WithAlpha(UIPalette.Frost, 0f));
            Stretch(_flash.rectTransform);
            _flash.enabled = false;

            _frameRoot.SetActive(false);
            _visible = false;
            UpdateCanvasEnabled();
            Layout();
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static Image MakeImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        void UpdateCanvasEnabled()
        {
            if (_canvas != null) _canvas.enabled = _visible || _show > 0f || _shutterT >= 0f;
        }

        void Layout()
        {
            int w = Screen.width, h = Screen.height;
            var fpc = FirstPersonController.Current;
            float camFov = fpc != null ? fpc.BaseFieldOfView : 70f;
            if (camFov < 1f) camFov = 70f;
            if (w == _lastW && h == _lastH && Mathf.Approximately(camFov, _lastCamFov)) return;
            _lastW = w;
            _lastH = h;
            _lastCamFov = camFov;

            // Canvas units equal screen pixels (overlay canvas without a scaler).
            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            float sw = w / scale, sh = h / scale;

            // Footprint of the capture frustum (fovY, aspect) on the player camera's (camFov) image.
            float frac = Mathf.Tan(_fovY * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(camFov * 0.5f * Mathf.Deg2Rad);
            float frameH = Mathf.Min(sh, sh * frac);
            float frameW = Mathf.Min(sw, sh * frac * _aspect);

            _frame.sizeDelta = new Vector2(frameW, frameH);
            float side = Mathf.Max(0f, (sw - frameW) * 0.5f);
            float band = Mathf.Max(0f, (sh - frameH) * 0.5f);
            _maskLeft.sizeDelta = new Vector2(side, 0f);
            _maskRight.sizeDelta = new Vector2(side, 0f);
            _maskTop.sizeDelta = new Vector2(frameW, band);
            _maskBottom.sizeDelta = new Vector2(frameW, band);
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.04f); // a capture hitch must not skip the blink

            // Show / hide: ticks slide in (easeOutCubic), mask fades.
            float target = _visible ? 1f : 0f;
            if (!Mathf.Approximately(_show, target))
            {
                _show = Mathf.MoveTowards(_show, target, dt / (_visible ? Feel.ViewfinderTicksSeconds : HideSeconds));
                if (_show <= 0f && !_visible) _frameRoot.SetActive(false);
                UpdateCanvasEnabled();
            }
            if (_show > 0f)
            {
                Layout();
                float e = _visible ? Ease.OutCubic(_show) : _show;
                _group.alpha = e;
                float travel = (1f - e) * TickTravel;
                for (int i = 0; i < 4; i++)
                    _corners[i].anchoredPosition = _cornerDir[i] * CornerInset - _cornerDir[i] * travel;
            }

            if (_shutterT >= 0f)
            {
                _shutterT += dt;
                float half = _frame.rect.height * 0.5f + 2f;
                float closed;
                if (_shutterT < Feel.ShutterCloseSeconds) closed = _shutterT / Feel.ShutterCloseSeconds;
                else closed = 1f - Ease.OutQuad((_shutterT - Feel.ShutterCloseSeconds) / Feel.ShutterOpenSeconds);
                closed = Mathf.Clamp01(closed);
                _bladeTop.sizeDelta = new Vector2(0f, half * closed);
                _bladeBottom.sizeDelta = new Vector2(0f, half * closed);

                float flashStart = Feel.ShutterCloseSeconds + Feel.ShutterOpenSeconds * 0.5f;
                float fk = (_shutterT - flashStart) / Feel.ShutterFlashSeconds;
                float a = _shutterT < flashStart ? 0f : Feel.ShutterFlashAlpha * (1f - Ease.OutQuad(fk));
                _flash.color = UIUtil.WithAlpha(UIPalette.Frost, Mathf.Max(0f, a));

                if (_shutterT >= Feel.ShutterSeconds)
                {
                    _shutterT = -1f;
                    _flash.enabled = false;
                    _bladeTop.gameObject.SetActive(false);
                    _bladeBottom.gameObject.SetActive(false);
                    UpdateCanvasEnabled();
                }
            }
        }
    }
}
