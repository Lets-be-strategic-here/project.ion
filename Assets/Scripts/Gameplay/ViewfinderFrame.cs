using UnityEngine;
using UnityEngine.UI;

namespace Ion.Gameplay
{
    /// <summary>
    /// Screen-space viewfinder for the instant camera: darkens the area outside the capture region (the
    /// snapshot's 50°, 4:3 frustum as seen through the player camera, so what is framed is what is taken),
    /// draws corner brackets and a centre cross, and plays the shutter on capture: a short black blink
    /// (the shutter closing) followed by a white flash that fades.
    /// Built entirely from code (uGUI Images, no sprites/fonts needed).
    /// </summary>
    internal sealed class ViewfinderFrame : MonoBehaviour
    {
        const float CornerInset = 28f;
        const float CornerLength = 56f;
        const float CornerThickness = 4f;
        const float BlinkDuration = 0.07f;
        const float FlashDuration = 0.32f;

        static readonly Color MaskColor = new Color(0f, 0f, 0f, 0.5f);
        static readonly Color LineColor = new Color(1f, 1f, 1f, 0.9f);

        Canvas _canvas;
        RectTransform _frame;
        RectTransform _maskLeft, _maskRight, _maskTop, _maskBottom;
        GameObject _frameRoot;
        Image _flash;

        float _aspect = 4f / 3f;
        float _fovY = 50f;
        float _lastCamFov = -1f;
        int _lastW = -1, _lastH = -1;
        float _flashT = -1f;
        bool _visible;

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
                if (_frameRoot != null) _frameRoot.SetActive(value);
                UpdateCanvasEnabled();
            }
        }

        public void Flash()
        {
            _flashT = 0f;
            if (_flash != null) _flash.enabled = true;
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

            // Corner brackets: one horizontal + one vertical bar per corner.
            for (int cx = 0; cx < 2; cx++)
            for (int cy = 0; cy < 2; cy++)
            {
                var corner = new Vector2(cx, cy);
                var dir = new Vector2(cx == 0 ? 1f : -1f, cy == 0 ? 1f : -1f);
                var inset = new Vector2(dir.x * CornerInset, dir.y * CornerInset);

                var h = MakeImage("CornerH", _frame, LineColor).rectTransform;
                h.anchorMin = h.anchorMax = corner;
                h.pivot = corner;
                h.sizeDelta = new Vector2(CornerLength, CornerThickness);
                h.anchoredPosition = inset;

                var v = MakeImage("CornerV", _frame, LineColor).rectTransform;
                v.anchorMin = v.anchorMax = corner;
                v.pivot = corner;
                v.sizeDelta = new Vector2(CornerThickness, CornerLength);
                v.anchoredPosition = inset;
            }

            // Centre cross.
            var ch = MakeImage("CrossH", _frame, LineColor).rectTransform;
            ch.anchorMin = ch.anchorMax = ch.pivot = new Vector2(0.5f, 0.5f);
            ch.sizeDelta = new Vector2(22f, 2f);
            var cv = MakeImage("CrossV", _frame, LineColor).rectTransform;
            cv.anchorMin = cv.anchorMax = cv.pivot = new Vector2(0.5f, 0.5f);
            cv.sizeDelta = new Vector2(2f, 22f);

            _flash = MakeImage("Flash", rootRect, new Color(1f, 1f, 1f, 0f));
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
            if (_canvas != null) _canvas.enabled = _visible || _flashT >= 0f;
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
            if (_visible) Layout();

            if (_flashT >= 0f)
            {
                _flashT += Mathf.Min(Time.unscaledDeltaTime, 0.04f); // a capture hitch must not skip the blink
                if (_flashT < BlinkDuration)
                {
                    _flash.color = new Color(0.04f, 0.04f, 0.05f, 0.94f);
                    return;
                }
                float a = 1f - (_flashT - BlinkDuration) / FlashDuration;
                if (a <= 0f)
                {
                    _flashT = -1f;
                    _flash.enabled = false;
                    UpdateCanvasEnabled();
                }
                else
                {
                    _flash.color = new Color(1f, 0.99f, 0.96f, a * a * 0.9f);
                }
            }
        }
    }
}
