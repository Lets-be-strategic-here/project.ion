using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// Full-screen dimmed panel with the title, "Click to play" and the controls list.
    /// Visible whenever the cursor is not locked; clicking locks the cursor (browsers only allow
    /// pointer lock from a user gesture, which a click is).
    /// </summary>
    public sealed class ClickToPlayOverlay : MonoBehaviour
    {
        public static ClickToPlayOverlay Instance { get; private set; }

        /// <summary>True while the overlay is (becoming) visible.</summary>
        public bool IsVisible => _wantVisible;

        /// <summary>Same list, same words as the web page's loading card (index.html).</summary>
        static readonly string[,] Controls =
        {
            { "W A S D", "move" },
            { "Mouse", "look" },
            { "Space", "jump" },
            { "1\u20135  /  Wheel", "choose photo" },
            { "hold RMB", "raise photo" },
            { "LMB", "place photo" },
            { "Q  E", "rotate raised photo" },
            { "R", "rewind" },
            { "C", "instant camera" },
            { "Esc", "release cursor" },
        };

        const float CardW = 760f, CardH = 700f;

        CanvasGroup _group;
        Text _cta;
        RectTransform _ctaButton;
        Image _ctaBg;
        RectTransform _card;
        bool _wantVisible = true;
        bool _hasPlayed;
        float _shownAt = -1f;

        /// <summary>
        /// Seconds the overlay waits for the web page's loading card before showing anyway (the page
        /// normally hides it within half a second of the game starting).
        /// </summary>
        const float LoaderTimeout = 6f;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool endCard = EndCard.IsOpen; // the end card owns the free cursor
            // On the web, wait until the page's loading card (with its own title + controls) is gone,
            // so the two never cross-fade into a double exposure.
            bool loader = !_hasPlayed && Time.realtimeSinceStartup < LoaderTimeout && Ion.Web.WebLinks.LoaderVisible;
            _wantVisible = !locked && !endCard && !loader;
            if (locked) _hasPlayed = true;

            // Click anywhere to (re)lock the cursor.
            if (!locked && !endCard)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            float dt = Time.unscaledDeltaTime;
            float target = _wantVisible ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, dt * (loader ? 100f : 6f));
            _group.blocksRaycasts = _wantVisible;

            bool active = _group.alpha > 0.001f;
            var content = transform.GetChild(0).gameObject;
            if (content.activeSelf != active) content.SetActive(active);

            if (active)
            {
                if (_shownAt < 0f) _shownAt = Time.unscaledTime;
                string label = _hasPlayed ? "Click to resume" : "Click to play";
                if (_cta.text != label)
                {
                    _cta.text = label;
                    _ctaButton.sizeDelta = new Vector2(_cta.preferredWidth + 88f, 72f);
                }
                // A solid button that breathes gently (never fades out).
                float pulse = 1f + 0.025f * Mathf.Sin((Time.unscaledTime - _shownAt) * 3.2f);
                _ctaButton.localScale = new Vector3(pulse, pulse, 1f);

                // Fit the card on short / narrow screens, and keep it clear of the settings card
                // (bottom-right, shown together after Esc): move left and shrink when they would overlap.
                Rect r = ((RectTransform)transform).rect;
                float fit = Mathf.Min(1f, (r.height - 40f) / CardH, (r.width - 40f) / CardW);
                float x = 0f;
                var settings = Ion.Presentation.Quality.SettingsPanel.Instance;
                float reserve = settings != null ? settings.ReservedRight : 0f;
                if (reserve > 0f && fit > 0f)
                {
                    const float gap = 20f;
                    bool overlapX = r.width * 0.5f + CardW * fit * 0.5f > r.width - reserve - gap;
                    bool overlapY = r.height * 0.5f - CardH * fit * 0.5f < settings.ReservedTop;
                    if (overlapX && overlapY)
                    {
                        float avail = r.width - reserve - gap * 2f;
                        fit = Mathf.Min(fit, avail / CardW);
                        x = gap + avail * 0.5f - r.width * 0.5f;
                    }
                }
                if (fit > 0f)
                {
                    _card.localScale = new Vector3(fit, fit, 1f);
                    _card.anchoredPosition = new Vector2(x, 0f);
                }
            }
            else
            {
                _shownAt = -1f;
            }
        }

        void Build()
        {
            var root = (RectTransform)transform;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.alpha = Ion.Web.WebLinks.LoaderVisible ? 0f : 1f;

            // Dim panel (first child = content toggled by Update).
            var dim = UIUtil.NewImage("Dim", root, UIUtil.WithAlpha(Palette.Ink, 0.8f));
            UIUtil.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            var card = UIUtil.NewRect("Card", dim.rectTransform);
            UIUtil.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW, CardH));
            _card = card;

            // Wordmark: heavy rounded sans like the page's loader, thin pipes.
            var title = UIUtil.NewText("Title", card, "<color=#FFF4E073>[</color>project<color=#FFF4E073>]</color>ion", 92, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIUtil.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(CardW, 110f));

            var sub = UIUtil.NewText("Subtitle", card, "a little photo-projection puzzle", 26, Palette.Sky, TextAnchor.MiddleCenter, FontStyle.Normal);
            UIUtil.Anchor(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(CardW, 36f));

            // Call to action: a solid cream button with dark text.
            _ctaBg = UIUtil.NewImage("CallToAction", card, Palette.Cream, UIUtil.RoundedSprite, true);
            _ctaButton = _ctaBg.rectTransform;
            UIUtil.Anchor(_ctaButton, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -196f), new Vector2(320f, 72f));
            _ctaBg.pixelsPerUnitMultiplier = 0.6f;
            _cta = UIUtil.NewText("Label", _ctaButton, "Click to play", 34, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(_cta.rectTransform);
            _ctaButton.sizeDelta = new Vector2(_cta.preferredWidth + 88f, 72f);

            // The controls sit on their own dark card (like the Settings card), so bright world text or
            // signs behind the dim never collide with them.
            var listCard = UIUtil.NewImage("ControlsCard", card, UIUtil.WithAlpha(Palette.Ink, 0.88f), UIUtil.RoundedSprite, true);
            UIUtil.Anchor(listCard.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -248f), new Vector2(560f, 452f));
            listCard.pixelsPerUnitMultiplier = 0.8f;
            listCard.raycastTarget = false;

            // Divider.
            var div = UIUtil.NewImage("Divider", card, UIUtil.WithAlpha(Palette.Cream, 0.25f));
            UIUtil.Anchor(div.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -266f), new Vector2(420f, 2f));

            // Controls list: keys right-aligned on the left, actions left-aligned on the right.
            const float rowH = 38f;
            float y = -290f;
            int rows = Controls.GetLength(0);
            for (int i = 0; i < rows; i++)
            {
                var key = UIUtil.NewText("Key" + i, card, Controls[i, 0], 23, Palette.Butter, TextAnchor.MiddleRight, FontStyle.Bold);
                UIUtil.Anchor(key.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-16f, y), new Vector2(320f, rowH));

                var act = UIUtil.NewText("Action" + i, card, Controls[i, 1], 23, Palette.Cream, TextAnchor.MiddleLeft);
                UIUtil.Anchor(act.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(16f, y), new Vector2(320f, rowH));
                y -= rowH;
            }
        }
    }
}
