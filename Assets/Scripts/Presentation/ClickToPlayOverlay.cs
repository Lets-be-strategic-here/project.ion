using Ion.Gameplay;
using Ion.Presentation.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// The pause / title card: wordmark, the call to action and the controls sheet, shown whenever the
    /// pointer is not locked (art bible §9.3):
    ///  * On the web the page locks the pointer synchronously inside the click (see <see cref="PointerLock"/>):
    ///    this card only arms it. On first load the page's own loading card carries "Click to play" (one click
    ///    from loader to game), so this card stays away until the loader is gone.
    ///  * After Esc it reads "Click to resume", with a thin bar that fills during the browser's ~1 s re-lock
    ///    cooldown; clicks are not offered (or sent) before that, and a refused lock simply waits for the next
    ///    click (no request loops).
    ///  * Fades in 0.22 s with an 8 px rise (easeOutCubic), out 0.16 s (easeInQuad). Never animates layout.
    /// </summary>
    public sealed class ClickToPlayOverlay : MonoBehaviour
    {
        public static ClickToPlayOverlay Instance { get; private set; }

        /// <summary>True while the overlay is (becoming) visible.</summary>
        public bool IsVisible => _wantVisible;

        /// <summary>The controls sheet (same words as the web page's loading card and the README).</summary>
        static readonly string[,] Controls =
        {
            { "W A S D", "move" },
            { "Mouse", "look" },
            { "Space", "jump" },
            { "1–9  /  Wheel", "choose photo" },
            { "hold Shift", "hold up photo  (or hold RMB)" },
            { "LMB", "place photo  /  take a photo" },
            { "Q  E", "hold to rotate the raised photo, tap to nudge" },
            { "E", "press  /  pick up" },
            { "R", "rewind, anytime" },
            { "R R", "back to the checkpoint" },
            { "C", "instant camera" },
            { "Esc", "pause" },
        };

        const float CardW = 780f, CardH = 780f;

        CanvasGroup _group;
        GameObject _content;
        Text _cta;
        RectTransform _ctaButton;
        Image _ctaBg;
        RectTransform _ctaBar;
        Image _ctaBarFill;
        RectTransform _card;
        bool _wantVisible = true;
        float _vis;              // 0..1 fade driver (linear in time)
        bool _ready;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnDisable() => PointerLock.SetArmed(false);

        void Update()
        {
            bool locked = PointerLock.IsLocked;
            bool endCard = EndCard.IsOpen; // the end card owns the free cursor
            // On first load the page's loading card is the title (it has its own "Click to play").
            bool loader = PointerLock.LoaderVisible && !PointerLock.HasEverLocked;
            _wantVisible = !locked && !endCard && !loader;

            // The page may lock on a click once the card is up and the browser's cooldown has passed.
            _ready = PointerLock.ResumeReady;
            bool arm = !locked && (_ready || !PointerLock.HasEverLocked) && (loader || _vis > 0.5f);
            if (!endCard) PointerLock.SetArmed(arm); // the end card arms its own Play again button

#if !(UNITY_WEBGL && !UNITY_EDITOR)
            // Editor / standalone: lock from script on a click (not on the settings card).
            if (_wantVisible && _ready && _vis > 0.5f)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame && !OverSettings(mouse.position.ReadValue()))
                    PointerLock.Request();
            }
#endif

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _vis = Mathf.MoveTowards(_vis, _wantVisible ? 1f : 0f, dt / (_wantVisible ? Feel.PanelInSeconds : Feel.PanelOutSeconds));
            float a = _wantVisible ? Ease.OutCubic(_vis) : Ease.InQuad(_vis);
            _group.alpha = a;
            _group.blocksRaycasts = _wantVisible;

            bool active = _vis > 0.001f;
            if (_content.activeSelf != active) _content.SetActive(active);
            if (!active) return;

            string label = PointerLock.HasEverLocked ? "Click to resume" : "Click to play";
            if (_cta.text != label)
            {
                _cta.text = label;
                _ctaButton.sizeDelta = new Vector2(_cta.preferredWidth + 96f, 72f);
            }
            float progress = PointerLock.ResumeProgress;
            bool waiting = !_ready && PointerLock.HasEverLocked;
            _ctaBg.color = waiting ? UIUtil.WithAlpha(UIPalette.Paper, 0.55f) : UIPalette.Paper;
            _ctaBar.gameObject.SetActive(waiting);
            if (waiting) _ctaBarFill.rectTransform.anchorMax = new Vector2(progress, 1f);

            LayoutCard(a);
        }

        static bool OverSettings(Vector2 screen)
        {
            var settings = Quality.SettingsPanel.Instance;
            return settings != null && settings.ContainsScreenPoint(screen);
        }

        /// <summary>Fits the card on short / narrow screens and keeps it clear of the settings card.</summary>
        void LayoutCard(float alpha)
        {
            Rect r = ((RectTransform)transform).rect;
            float fit = Mathf.Min(1f, (r.height - 40f) / CardH, (r.width - 40f) / CardW);
            float x = 0f;
            var settings = Quality.SettingsPanel.Instance;
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
            if (fit <= 0f) return;
            _card.localScale = new Vector3(fit, fit, 1f);
            // Rise in (8 px), no layout animation.
            _card.anchoredPosition = new Vector2(x, -(1f - alpha) * Feel.PanelRisePx);
        }

        void Build()
        {
            var root = (RectTransform)transform;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.alpha = 0f;

            // Dim (first child = content toggled by Update): Graphite, never black.
            var dim = UIUtil.NewImage("Dim", root, UIUtil.WithAlpha(UIPalette.Graphite, 0.78f));
            UIUtil.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            _content = dim.gameObject;

            var card = UIUtil.NewRect("Card", dim.rectTransform);
            UIUtil.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW, CardH));
            _card = card;

            // Wordmark: the title's brackets in Ion, as crop marks.
            var title = UIUtil.NewText("Title", card, "<color=#9FE3FF>[</color>project<color=#9FE3FF>]</color>ion", 92, UIPalette.Paper, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(CardW, 110f));

            var sub = UIUtil.NewText("Subtitle", card, "a light table of photographs you can step into", 25, UIUtil.WithAlpha(UIPalette.Paper, 0.72f), TextAnchor.MiddleCenter, FontStyle.Normal, false);
            UIUtil.Anchor(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -102f), new Vector2(CardW, 36f));

            // Call to action: a solid Paper key cap with Graphite text.
            _ctaBg = UIUtil.NewImage("CallToAction", card, UIPalette.Paper, UIUtil.RoundedSprite, true);
            _ctaButton = _ctaBg.rectTransform;
            UIUtil.Anchor(_ctaButton, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -196f), new Vector2(320f, 72f));
            _ctaBg.pixelsPerUnitMultiplier = 0.6f;
            _cta = UIUtil.NewText("Label", _ctaButton, "Click to play", 34, UIPalette.Graphite, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(_cta.rectTransform);
            _ctaButton.sizeDelta = new Vector2(_cta.preferredWidth + 96f, 72f);

            // Re-lock cooldown bar under the label (fills in ~1 s after Esc).
            _ctaBar = UIUtil.NewRect("Cooldown", _ctaButton);
            _ctaBar.anchorMin = new Vector2(0f, 0f);
            _ctaBar.anchorMax = new Vector2(1f, 0f);
            _ctaBar.pivot = new Vector2(0.5f, 0f);
            _ctaBar.offsetMin = new Vector2(24f, 10f);
            _ctaBar.offsetMax = new Vector2(-24f, 13f);
            var track = UIUtil.NewImage("Track", _ctaBar, UIUtil.WithAlpha(UIPalette.Graphite, 0.18f));
            UIUtil.Stretch(track.rectTransform);
            _ctaBarFill = UIUtil.NewImage("Fill", _ctaBar, UIPalette.Graphite);
            var frt = _ctaBarFill.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0f, 1f);
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            _ctaBar.gameObject.SetActive(false);

            // Controls sheet on its own Graphite card (bright world text behind the dim never collides).
            const float rowH = 36f;
            int rows = Controls.GetLength(0);
            float listH = rows * rowH + 56f;
            var listCard = UIUtil.NewImage("ControlsCard", card, UIUtil.WithAlpha(UIPalette.Graphite, 0.9f), UIUtil.RoundedSprite, true);
            UIUtil.Anchor(listCard.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -256f), new Vector2(620f, listH));
            listCard.pixelsPerUnitMultiplier = 0.8f;
            listCard.raycastTarget = false;

            // Keys right-aligned in bracket style on the left, actions left-aligned on the right.
            float y = -284f;
            for (int i = 0; i < rows; i++)
            {
                string keys = Controls[i, 0];
                var key = UIUtil.NewText("Key" + i, card, Bracketed(keys), 22, UIPalette.Paper, TextAnchor.MiddleRight, FontStyle.Bold, false);
                UIUtil.Anchor(key.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-40f, y), new Vector2(300f, rowH));

                var act = UIUtil.NewText("Action" + i, card, Controls[i, 1], 22, UIUtil.WithAlpha(UIPalette.Paper, 0.82f), TextAnchor.MiddleLeft, FontStyle.Normal, false);
                UIUtil.Anchor(act.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(-8f, y), new Vector2(340f, rowH));
                y -= rowH;
            }
        }

        /// <summary>"hold Shift" → "hold [Shift]"; "Q  E" → "[Q] [E]"; "1–9  /  Wheel" → "[1–9] / [Wheel]".</summary>
        static string Bracketed(string keys)
        {
            if (keys.StartsWith("hold ")) return "hold " + UIUtil.Key(keys.Substring(5));
            if (keys == "Q  E") return UIUtil.Key("Q") + " " + UIUtil.Key("E");
            if (keys == "R R") return UIUtil.Key("R") + " " + UIUtil.Key("R");
            if (keys == "W A S D") return UIUtil.Key("W A S D");
            int slash = keys.IndexOf('/');
            if (slash > 0) return UIUtil.Key(keys.Substring(0, slash).Trim()) + " / " + UIUtil.Key(keys.Substring(slash + 1).Trim());
            return UIUtil.Key(keys);
        }
    }
}
