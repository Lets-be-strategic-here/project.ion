using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>Small centre dot. Hidden while the cursor is free, faint while a photo is raised.</summary>
    public sealed class Crosshair : MonoBehaviour
    {
        Image _dot;
        CanvasGroup _group;
        float _alpha;

        void Awake()
        {
            var rt = (RectTransform)transform;
            UIUtil.Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9f, 9f));

            var ring = UIUtil.NewImage("Ring", rt, UIUtil.WithAlpha(Palette.Ink, 0.35f), UIUtil.CircleSprite);
            UIUtil.Anchor(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(11f, 11f));

            _dot = UIUtil.NewImage("Dot", rt, Palette.White, UIUtil.CircleSprite);
            UIUtil.Anchor(_dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7f, 7f));

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.alpha = 0f;
        }

        void LateUpdate()
        {
            float target = 0.9f;
            if (Cursor.lockState != CursorLockMode.Locked) target = 0f;
            else if (PhotoOverlayUI.Instance != null && PhotoOverlayUI.Instance.IsShown) target = 0.25f;

            if (!Mathf.Approximately(_alpha, target))
            {
                _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime * 8f);
                _group.alpha = _alpha;
            }
        }
    }
}
