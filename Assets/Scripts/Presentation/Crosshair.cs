using Ion.Gameplay;
using Ion.Presentation.Motion;
using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// The reticle: four tiny crop-mark corner ticks around a point (the title's bracket, at 14 px). Hidden
    /// while the pointer is free, faint while a photo is raised (the photo's own frame is the reticle then),
    /// and the ticks draw in a little when something usable is in reach.
    /// </summary>
    public sealed class Crosshair : MonoBehaviour
    {
        const float Size = 14f, Tick = 5f, Thick = 2f;

        CanvasGroup _group;
        readonly RectTransform[] _ticks = new RectTransform[4];
        readonly Vector2[] _dir = new Vector2[4];
        float _alpha;
        float _focus, _focusVel;
        PlayerInteractor _interactor;
        float _nextFind;

        void Awake()
        {
            var rt = (RectTransform)transform;
            UIUtil.Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Size, Size));

            var dot = UIUtil.NewImage("Dot", rt, UIUtil.WithAlpha(UIPalette.Paper, 0.95f), UIUtil.CircleSprite);
            UIUtil.Anchor(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, 3f));

            int k = 0;
            for (int cx = 0; cx < 2; cx++)
            for (int cy = 0; cy < 2; cy++)
            {
                var corner = new Vector2(cx, cy);
                var g = UIUtil.NewRect("Tick", rt);
                g.anchorMin = g.anchorMax = corner;
                g.pivot = corner;
                g.sizeDelta = new Vector2(Tick, Tick);
                var h = UIUtil.NewImage("H", g, UIUtil.WithAlpha(UIPalette.Paper, 0.9f));
                h.rectTransform.anchorMin = h.rectTransform.anchorMax = h.rectTransform.pivot = corner;
                h.rectTransform.sizeDelta = new Vector2(Tick, Thick);
                var v = UIUtil.NewImage("V", g, UIUtil.WithAlpha(UIPalette.Paper, 0.9f));
                v.rectTransform.anchorMin = v.rectTransform.anchorMax = v.rectTransform.pivot = corner;
                v.rectTransform.sizeDelta = new Vector2(Thick, Tick);
                // A soft Graphite shadow so the ticks read on Paper walls.
                var sh = g.gameObject.AddComponent<Shadow>();
                sh.effectColor = UIUtil.WithAlpha(UIPalette.Graphite, 0.35f);
                sh.effectDistance = new Vector2(1f, -1f);
                _ticks[k] = g;
                _dir[k] = new Vector2(cx == 0 ? -1f : 1f, cy == 0 ? -1f : 1f);
                k++;
            }

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.alpha = 0f;
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float target = 0.9f;
            if (!PointerLock.IsLocked) target = 0f;
            else if (PhotoOverlayUI.Instance != null && PhotoOverlayUI.Instance.IsShown) target = 0.2f;

            if (!Mathf.Approximately(_alpha, target))
            {
                _alpha = Mathf.MoveTowards(_alpha, target, dt / Feel.UIHoverSeconds);
                _group.alpha = Ease.OutQuad(_alpha);
            }

            // Something usable in reach: the ticks close in (a spring, so it settles softly).
            if (_interactor == null && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 0.5f;
                var fpc = FirstPersonController.Current;
                if (fpc != null) _interactor = fpc.GetComponent<PlayerInteractor>();
            }
            bool focus = _interactor != null && (_interactor.Focused != null || _interactor.FocusedSwitch != null);
            Spring.Step(ref _focus, ref _focusVel, focus ? 1f : 0f, Feel.CardFreq, Feel.CardZeta, dt);
            float spread = Mathf.Lerp(2f, -1.5f, _focus);
            for (int i = 0; i < 4; i++)
                _ticks[i].anchoredPosition = _dir[i] * spread;
        }
    }
}
