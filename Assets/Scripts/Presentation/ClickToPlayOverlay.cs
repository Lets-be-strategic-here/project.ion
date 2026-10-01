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

        static readonly string[,] Controls =
        {
            { "W A S D", "Move" },
            { "Mouse", "Look" },
            { "Space", "Jump" },
            { "E", "Interact / pick up" },
            { "1 - 5  /  Scroll", "Select photo" },
            { "Hold Right Mouse", "Raise photo" },
            { "Left Mouse", "Place photo" },
            { "Q  /  E", "Rotate raised photo" },
            { "R", "Rewind" },
            { "C", "Instant camera" },
            { "Esc", "Release cursor" },
        };

        CanvasGroup _group;
        Text _cta;
        bool _wantVisible = true;
        bool _hasPlayed;

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
            _wantVisible = !locked;
            if (locked) _hasPlayed = true;

            // Click anywhere to (re)lock the cursor.
            if (!locked)
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
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, dt * 6f);
            _group.blocksRaycasts = _wantVisible;

            bool active = _group.alpha > 0.001f;
            var content = transform.GetChild(0).gameObject;
            if (content.activeSelf != active) content.SetActive(active);

            if (active)
            {
                string label = _hasPlayed ? "Click to resume" : "Click to play";
                if (_cta.text != label) _cta.text = label;
                float pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 3.2f);
                var c = _cta.color;
                c.a = pulse;
                _cta.color = c;
            }
        }

        void Build()
        {
            var root = (RectTransform)transform;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.alpha = 1f;

            // Dim panel (first child = content toggled by Update).
            var dim = UIUtil.NewImage("Dim", root, UIUtil.WithAlpha(Palette.Ink, 0.74f));
            UIUtil.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            var card = UIUtil.NewRect("Card", dim.rectTransform);
            UIUtil.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 760f));

            var title = UIUtil.NewText("Title", card, "|project|ion", 84, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIUtil.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(760f, 100f));

            var sub = UIUtil.NewText("Subtitle", card, "a little photo-projection puzzle", 24, Palette.Sky, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIUtil.Anchor(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(760f, 36f));

            _cta = UIUtil.NewText("CallToAction", card, "Click to play", 40, Palette.Butter, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIUtil.Anchor(_cta.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(760f, 56f));

            // Divider.
            var div = UIUtil.NewImage("Divider", card, UIUtil.WithAlpha(Palette.Cream, 0.25f));
            UIUtil.Anchor(div.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -248f), new Vector2(420f, 2f));

            // Controls list: keys right-aligned on the left, actions left-aligned on the right.
            const float rowH = 36f;
            float y = -276f;
            int rows = Controls.GetLength(0);
            for (int i = 0; i < rows; i++)
            {
                var key = UIUtil.NewText("Key" + i, card, Controls[i, 0], 22, Palette.Coral, TextAnchor.MiddleRight, FontStyle.Bold);
                UIUtil.Anchor(key.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-16f, y), new Vector2(320f, rowH));

                var act = UIUtil.NewText("Action" + i, card, Controls[i, 1], 22, Palette.Cream, TextAnchor.MiddleLeft);
                UIUtil.Anchor(act.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(16f, y), new Vector2(320f, rowH));
                y -= rowH;
            }
        }
    }
}
