using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Projection;
using Ion.Levels;
using Ion.Presentation.Motion;
using Ion.Web;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// The end-of-game card shown by the Gallery's final teleporter: title, "thanks for playing", a
    /// one-line credit and two buttons (Play again / View projects). Opening releases the pointer on
    /// purpose (art bible §9.3); the player's input is off and the click-to-play overlay stays away.
    /// "Play again" re-locks inside its own click: the card registers the button as the page's only
    /// "allow" region, so the browser's lock happens synchronously in that gesture. Buttons are hit-tested
    /// here from the raw pointer (no uGUI Selectables).
    /// </summary>
    [DefaultExecutionOrder(31000)]
    public sealed class EndCard : MonoBehaviour
    {
        public const string ProjectsUrl = "https://github.com/Vasiniks";

        public static EndCard Instance { get; private set; }

        /// <summary>True while the card is open (or opening).</summary>
        public static bool IsOpen => Instance != null && Instance._open;

        RectTransform _card;
        CanvasGroup _group;
        GameObject _content;
        RectTransform _playButton, _projectsButton;
        Image _playBg, _projectsBg;
        RawImage _photo;
        Text _photoCaption;
        bool _open;
        bool _sawUnlocked;
        float _t;
        float _hoverPlay, _hoverProjects;
        /// <summary>PointerLock rect slot used for the Play again button.</summary>
        const int PointerRectSlot = 1;
        readonly List<GameObject> _hidden = new List<GameObject>();

        /// <summary>HUD layers (siblings under the UI canvas) hidden while the card is up.</summary>
        static readonly string[] HudLayers = { "PhotoOverlay", "Crosshair", "Hud", "Onboarding" };

        static readonly Color PlayColor = UIPalette.Ion;          // the thing to use
        static readonly Color ProjectsColor = UIPalette.Plaster;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Opens the card: frees the cursor and pauses the player's input.</summary>
        public void Open()
        {
            if (_open) return;
            _open = true;
            _t = 0f;
            _content.SetActive(true);
            ChoosePhoto();
            SetHudVisible(false);
            WebLinks.SetEndCardOpen(true);
            var player = FirstPersonController.Current;
            if (player != null) player.InputEnabled = false;
            _sawUnlocked = false;
            PointerLock.Release();
            IonInput.ResetHeld();
        }

        public void Close()
        {
            _open = false;
            SetHudVisible(true);
            WebLinks.SetEndCardOpen(false);
            PointerLock.SetRect(PointerRectSlot, PointerLock.RectKind.Allow, null);
            PointerLock.SetArmed(false);
        }

        /// <summary>Hides / restores the HUD layers (inventory, prompts, toasts, held photo, tutorial).</summary>
        void SetHudVisible(bool visible)
        {
            if (!visible)
            {
                _hidden.Clear();
                Transform canvas = transform.parent;
                if (canvas == null) return;
                for (int i = 0; i < HudLayers.Length; i++)
                {
                    Transform t = canvas.Find(HudLayers[i]);
                    if (t == null || !t.gameObject.activeSelf) continue;
                    t.gameObject.SetActive(false);
                    _hidden.Add(t.gameObject);
                }
                return;
            }
            for (int i = 0; i < _hidden.Count; i++)
                if (_hidden[i] != null) _hidden[i].SetActive(true);
            _hidden.Clear();
        }

        /// <summary>
        /// The card's Polaroid shows one of the player's own photos: the newest snapshot if any was hung in
        /// the gallery, else the Bridge photo.
        /// </summary>
        void ChoosePhoto()
        {
            PhotoData pick = null;
            string caption = "";
            var curator = Object.FindFirstObjectByType<GalleryCurator>();
            if (curator != null)
            {
                curator.Refresh();
                for (int i = curator.Hung.Count - 1; i >= 0 && pick == null; i--)
                    if (curator.Hung[i] != null && curator.Hung[i].Preview != null && curator.Hung[i].Label == InstantCamera.SnapshotLabel)
                    {
                        pick = curator.Hung[i];
                        caption = "your snapshot";
                    }
            }
            var game = GameBootstrap.Instance;
            if (pick == null && game != null && game.Rooms.Count > 0)
            {
                pick = game.Rooms[0].GetShotPhoto(0);
                caption = pick != null ? pick.Label : "";
            }
            _photo.texture = pick != null ? pick.Preview : null;
            _photo.color = _photo.texture != null ? Color.white : UIPalette.Frost;
            if (_photo.texture != null)
            {
                // Cover-crop the photo into the frame.
                float frameAspect = _photo.rectTransform.rect.width / Mathf.Max(1f, _photo.rectTransform.rect.height);
                float photoAspect = pick.Aspect > 0.01f ? pick.Aspect : 4f / 3f;
                if (photoAspect > frameAspect)
                {
                    float w = frameAspect / photoAspect;
                    _photo.uvRect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
                }
                else
                {
                    float h = photoAspect / frameAspect;
                    _photo.uvRect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
                }
            }
            _photoCaption.text = caption;
        }

        void PlayAgain()
        {
            Close();
            ScreenFx.RunTransition(() =>
            {
                var game = GameBootstrap.Instance;
                if (game != null) game.Restart();
            });
            // On the web the page has already locked inside this click (the button is the allow region);
            // elsewhere lock from script.
            PointerLock.Request();
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            // In 2× the panel time (it is the ending), out like any panel.
            _t = Mathf.MoveTowards(_t, _open ? 1f : 0f, dt / (_open ? Feel.PanelInSeconds * 2f : Feel.PanelOutSeconds));
            float e = _open ? Ease.OutCubic(_t) : Ease.InQuad(_t);
            _group.alpha = e;
            Rect screen = ((RectTransform)transform).rect;
            float fit = Mathf.Min(1f, (screen.width - 32f) / Mathf.Max(1f, _card.sizeDelta.x), (screen.height - 32f) / Mathf.Max(1f, _card.sizeDelta.y));
            float s = Mathf.Lerp(0.97f, 1f, e) * Mathf.Max(0.3f, fit);
            _card.localScale = new Vector3(s, s, 1f);
            _card.anchoredPosition = new Vector2(0f, -(1f - e) * Feel.PanelRisePx);
            bool active = _t > 0.001f;
            if (_content.activeSelf != active) _content.SetActive(active);
            if (!_open) return;

            // Keep the player paused while the card is up.
            var player = FirstPersonController.Current;
            if (player != null && player.InputEnabled) player.InputEnabled = false;

            // The only way to get locked while the card is open is a click on Play again (the page's allow
            // region), once the deliberate release has gone through.
            if (!PointerLock.IsLocked) _sawUnlocked = true;
            else if (_sawUnlocked) { PlayAgain(); return; }
            if (_t >= 0.6f)
            {
                PointerLock.SetRect(PointerRectSlot, PointerLock.RectKind.Allow, UIUtil.ScreenRect(_playButton));
                PointerLock.SetArmed(_sawUnlocked);
            }

            var mouse = Mouse.current;
            if (mouse == null || _t < 0.6f) return;
            Vector2 pos = mouse.position.ReadValue();
            bool overPlay = Contains(_playButton, pos), overProjects = Contains(_projectsButton, pos);
            _hoverPlay = Mathf.MoveTowards(_hoverPlay, overPlay ? 1f : 0f, dt / Feel.UIHoverSeconds);
            _hoverProjects = Mathf.MoveTowards(_hoverProjects, overProjects ? 1f : 0f, dt / Feel.UIHoverSeconds);
            _playBg.color = Color.Lerp(PlayColor, Darker(PlayColor), Ease.OutQuad(_hoverPlay));
            _projectsBg.color = Color.Lerp(ProjectsColor, Darker(ProjectsColor), Ease.OutQuad(_hoverProjects));

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (overPlay) PlayAgain();
                else if (overProjects) WebLinks.Open(ProjectsUrl);
            }
        }

        static Color Darker(Color c) => new Color(c.r * 0.88f, c.g * 0.88f, c.b * 0.88f, c.a);

        static bool Contains(RectTransform rt, Vector2 screen) =>
            rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

        void Build()
        {
            var root = (RectTransform)transform;
            UIUtil.Stretch(root);

            var dim = UIUtil.NewImage("Dim", root, UIUtil.WithAlpha(UIPalette.Graphite, 0.74f));
            UIUtil.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            _content = dim.gameObject;
            _group = _content.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.alpha = 0f;

            // A wide cream card: a tilted Polaroid of the player's own photo on the left, title, thanks and
            // the two buttons on the right.
            const float w = 820f, h = 440f;
            _card = UIUtil.NewRect("Card", dim.rectTransform);
            UIUtil.Anchor(_card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            var shadow = UIUtil.NewImage("Shadow", _card, UIUtil.WithAlpha(UIPalette.Cyanotype, 0.3f), UIUtil.RoundedSprite, true);
            UIUtil.Stretch(shadow.rectTransform);
            shadow.rectTransform.offsetMin = new Vector2(6f, -16f);
            shadow.rectTransform.offsetMax = new Vector2(6f, -16f);
            shadow.pixelsPerUnitMultiplier = 0.8f;
            var face = UIUtil.NewImage("Face", _card, UIPalette.Paper, UIUtil.RoundedSprite, true);
            UIUtil.Stretch(face.rectTransform);
            face.pixelsPerUnitMultiplier = 0.8f;
            face.raycastTarget = true;

            // Polaroid (white frame, thick bottom border with a handwritten-ish caption).
            const float photoW = 300f, photoH = 225f, border = 16f, bottom = 64f;
            var polaroid = UIUtil.NewRect("Polaroid", _card);
            UIUtil.Anchor(polaroid, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46f + (photoW + border * 2f) * 0.5f, 6f),
                          new Vector2(photoW + border * 2f, photoH + border + bottom));
            polaroid.localRotation = Quaternion.Euler(0f, 0f, 3.5f);
            var pShadow = UIUtil.NewImage("Shadow", polaroid, UIUtil.WithAlpha(UIPalette.Cyanotype, 0.22f), UIUtil.RoundedSprite, true);
            UIUtil.Stretch(pShadow.rectTransform);
            pShadow.rectTransform.offsetMin = new Vector2(5f, -9f);
            pShadow.rectTransform.offsetMax = new Vector2(5f, -9f);
            pShadow.pixelsPerUnitMultiplier = 4f;
            var pFrame = UIUtil.NewImage("Frame", polaroid, UIPalette.Frost, UIUtil.RoundedSprite, true);
            UIUtil.Stretch(pFrame.rectTransform);
            pFrame.pixelsPerUnitMultiplier = 4f;
            _photo = UIUtil.NewRaw("Photo", polaroid);
            UIUtil.Anchor(_photo.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -border), new Vector2(photoW, photoH));
            _photoCaption = UIUtil.NewText("Caption", polaroid, "", 26, UIPalette.GraphiteSoft, TextAnchor.MiddleCenter, FontStyle.Italic, false);
            UIUtil.Anchor(_photoCaption.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(photoW, bottom - 12f));

            // Right column.
            const float colX = 46f + photoW + border * 2f + 40f;
            float colW = w - colX - 40f;
            float colCenter = colX + colW * 0.5f - w * 0.5f;
            var title = UIUtil.NewText("Title", _card, "<color=#C59A45>[</color>project<color=#C59A45>]</color>ion", 72, UIPalette.Graphite, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(colCenter, -48f), new Vector2(colW, 96f));

            var thanks = UIUtil.NewText("Thanks", _card, "thanks for playing", 34, UIPalette.Graphite, TextAnchor.MiddleCenter, FontStyle.Italic, false);
            UIUtil.Anchor(thanks.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(colCenter, -150f), new Vector2(colW, 48f));

            var credit = UIUtil.NewText("Credit", _card, "a Viewfinder-inspired prototype", 22, UIPalette.GraphiteSoft, TextAnchor.MiddleCenter, FontStyle.Normal, false);
            UIUtil.Anchor(credit.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(colCenter, -194f), new Vector2(colW, 32f));

            _playBg = MakeButton("PlayAgain", "Play again", new Vector2(colCenter, 136f), PlayColor, out _playButton);
            _projectsBg = MakeButton("ViewProjects", "View projects", new Vector2(colCenter, 62f), ProjectsColor, out _projectsButton);

            _content.SetActive(false);
        }

        Image MakeButton(string name, string label, Vector2 pos, Color color, out RectTransform rect)
        {
            var bg = UIUtil.NewImage(name, _card, color, UIUtil.RoundedSprite, true);
            rect = bg.rectTransform;
            UIUtil.Anchor(rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), pos, new Vector2(300f, 60f));
            bg.raycastTarget = true;
            var text = UIUtil.NewText("Label", rect, label, 26, UIPalette.Graphite, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(text.rectTransform);
            return bg;
        }
    }
}
