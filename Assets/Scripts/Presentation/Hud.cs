using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Projection;
using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// In-game HUD: bottom-centre interaction prompt, fading toast, photo inventory strip (top-left),
    /// film counter (top-right) and a rewind hint (bottom-left). Built by UIFactory.Create().
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public static Hud Instance { get; private set; }

        const float ToastDefaultSeconds = 2.6f;
        const float ToastFade = 0.35f;
        const float SlotImageWidth = 84f;
        const float SlotBorder = 6f;
        const float SlotBottom = 18f;
        const float SlotSpacing = 12f;

        RectTransform _promptPill;
        Text _promptText;
        string _promptCurrent;

        RectTransform _toastPill;
        Text _toastText;
        CanvasGroup _toastGroup;
        float _toastTime;      // time remaining
        float _toastDuration;

        RectTransform _strip;
        readonly List<Slot> _slots = new List<Slot>();
        PhotoInventory _inventory;
        int _shownSelected = int.MinValue;

        GameObject _filmRoot;
        Text _filmText;
        InstantCamera _camera;
        int _shownFilm = int.MinValue;
        bool _shownUnlocked;

        GameObject _rewindRoot;

        float _nextSearch;

        sealed class Slot
        {
            public RectTransform Root;
            public RawImage Image;
            public Image Frame;
            public Text Number;
        }

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (_inventory != null) _inventory.Changed -= OnInventoryChanged;
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- public API

        /// <summary>Show a bottom-centre prompt. Null or empty hides it. Cheap to call every frame.</summary>
        public void Prompt(string text)
        {
            if (_promptText == null) return;
            // While a photo is held up it covers the bottom of the screen: the controls go onto the
            // Polaroid's own bottom border instead of a pill on top of its caption.
            var overlay = PhotoOverlayUI.Instance;
            if (overlay != null && overlay.IsShown)
            {
                overlay.SetHint(text);
                text = null;
            }
            else if (overlay != null)
            {
                overlay.SetHint(null);
            }
            if (string.IsNullOrEmpty(text))
            {
                if (_promptCurrent != null)
                {
                    _promptCurrent = null;
                    _promptPill.gameObject.SetActive(false);
                }
                return;
            }
            if (text == _promptCurrent) return;
            _promptCurrent = text;
            _promptText.text = text;
            _promptPill.sizeDelta = new Vector2(_promptText.preferredWidth + 56f, 52f);
            _promptPill.gameObject.SetActive(true);
        }

        /// <summary>Show a short message that fades out after a couple of seconds.</summary>
        public void Toast(string text)
        {
            Toast(text, ToastDefaultSeconds);
        }

        public void Toast(string text, float seconds)
        {
            if (_toastText == null || string.IsNullOrEmpty(text)) return;
            _toastText.text = text;
            _toastPill.sizeDelta = new Vector2(_toastText.preferredWidth + 64f, 60f);
            _toastDuration = Mathf.Max(0.2f, seconds);
            _toastTime = _toastDuration;
            _toastGroup.alpha = 1f;
            _toastPill.gameObject.SetActive(true);
        }

        /// <summary>Bind the photo strip to an inventory (also found automatically).</summary>
        public void BindInventory(PhotoInventory inv)
        {
            if (_inventory == inv) return;
            if (_inventory != null) _inventory.Changed -= OnInventoryChanged;
            _inventory = inv;
            if (_inventory != null) _inventory.Changed += OnInventoryChanged;
            RebuildStrip();
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // Lazy lookup of gameplay objects (they may be created after the UI).
            if ((_inventory == null || _camera == null) && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 0.5f;
                if (_inventory == null)
                {
                    var inv = Object.FindFirstObjectByType<PhotoInventory>();
                    if (inv != null) BindInventory(inv);
                }
                if (_camera == null)
                    _camera = Object.FindFirstObjectByType<InstantCamera>();
            }

            // Toast fade.
            if (_toastTime > 0f)
            {
                _toastTime -= dt;
                _toastGroup.alpha = Mathf.Clamp01(_toastTime / ToastFade);
                float age = _toastDuration - _toastTime;
                float pop = 1f + 0.08f * Mathf.Clamp01(1f - age / 0.15f);
                _toastPill.localScale = new Vector3(pop, pop, 1f);
                if (_toastTime <= 0f) _toastPill.gameObject.SetActive(false);
            }

            // Selection highlight.
            if (_inventory != null && _inventory.SelectedIndex != _shownSelected)
                ApplySelection();

            // Film counter.
            bool unlocked = _camera != null && _camera.Unlocked;
            int film = _camera != null ? _camera.Film : 0;
            if (unlocked != _shownUnlocked || (unlocked && film != _shownFilm))
            {
                _shownUnlocked = unlocked;
                _shownFilm = film;
                _filmRoot.SetActive(unlocked);
                if (unlocked) _filmText.text = "FILM  " + film + "     [C] camera";
            }

            // Rewind hint.
            var ps = ProjectionSystem.Instance;
            bool canRewind = ps != null && ps.CanRewind;
            if (_rewindRoot.activeSelf != canRewind) _rewindRoot.SetActive(canRewind);
        }

        // ---------------------------------------------------------------- inventory strip

        void OnInventoryChanged()
        {
            RebuildStrip();
        }

        void RebuildStrip()
        {
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Root != null) Destroy(_slots[i].Root.gameObject);
            _slots.Clear();
            _shownSelected = int.MinValue;

            if (_inventory == null || _inventory.Photos == null) return;

            var photos = _inventory.Photos;
            float x = 0f;
            for (int i = 0; i < photos.Count; i++)
            {
                PhotoData p = photos[i];
                float aspect = (p != null && p.Aspect > 0.01f) ? p.Aspect : 4f / 3f;
                float imgW = SlotImageWidth;
                float imgH = imgW / aspect;
                float w = imgW + SlotBorder * 2f;
                float h = imgH + SlotBorder + SlotBottom;

                var root = UIUtil.NewRect("Slot" + (i + 1), _strip);
                UIUtil.Anchor(root, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(x + w * 0.5f, 0f), new Vector2(w, h));

                var frame = UIUtil.NewImage("Frame", root, Palette.White, UIUtil.RoundedSprite, true);
                UIUtil.Stretch(frame.rectTransform);
                frame.pixelsPerUnitMultiplier = 4f; // small corner radius

                var img = UIUtil.NewRaw("Photo", root);
                var irt = img.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = new Vector2(0f, -SlotBorder);
                irt.sizeDelta = new Vector2(imgW, imgH);
                img.texture = p != null ? p.Preview : null;
                if (img.texture == null) img.color = Palette.Sky;

                var num = UIUtil.NewText("Num", root, (i + 1).ToString(), 15, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold, false);
                var nrt = num.rectTransform;
                nrt.anchorMin = new Vector2(0f, 0f);
                nrt.anchorMax = new Vector2(1f, 0f);
                nrt.pivot = new Vector2(0.5f, 0f);
                nrt.anchoredPosition = new Vector2(0f, 0f);
                nrt.sizeDelta = new Vector2(0f, SlotBottom);

                _slots.Add(new Slot { Root = root, Image = img, Frame = frame, Number = num });
                x += w + SlotSpacing;
            }
            ApplySelection();
        }

        void ApplySelection()
        {
            int sel = _inventory != null ? _inventory.SelectedIndex : -1;
            _shownSelected = sel;
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                bool on = i == sel;
                s.Root.localScale = on ? new Vector3(1.12f, 1.12f, 1f) : Vector3.one;
                s.Root.localRotation = Quaternion.Euler(0f, 0f, on ? 0f : ((i % 2 == 0) ? 2.5f : -2f));
                s.Frame.color = on ? Palette.Butter : UIUtil.WithAlpha(Palette.White, 0.92f);
                if (s.Image.texture == null) s.Image.color = Palette.Sky;
                else s.Image.color = on ? Color.white : new Color(1f, 1f, 1f, 0.75f);
                s.Number.color = on ? Palette.Ink : Palette.Slate;
            }
        }

        // ---------------------------------------------------------------- construction

        void Build()
        {
            var root = (RectTransform)transform;

            // Prompt (bottom centre).
            _promptPill = UIUtil.NewImage("PromptPill", root, UIUtil.WithAlpha(Palette.Ink, 0.62f), UIUtil.RoundedSprite, true).rectTransform;
            UIUtil.Anchor(_promptPill, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(300f, 52f));
            _promptText = UIUtil.NewText("Text", _promptPill, "", 24, Palette.Cream);
            UIUtil.Stretch(_promptText.rectTransform);
            _promptPill.gameObject.SetActive(false);

            // Toast (upper centre).
            _toastPill = UIUtil.NewImage("ToastPill", root, UIUtil.WithAlpha(Palette.Cream, 0.92f), UIUtil.RoundedSprite, true).rectTransform;
            // High enough to clear a raised photo's top border.
            UIUtil.Anchor(_toastPill, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -92f), new Vector2(300f, 60f));
            _toastGroup = _toastPill.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastGroup.interactable = false;
            _toastText = UIUtil.NewText("Text", _toastPill, "", 28, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(_toastText.rectTransform);
            _toastPill.gameObject.SetActive(false);

            // Photo strip (top-left).
            _strip = UIUtil.NewRect("PhotoStrip", root);
            UIUtil.Anchor(_strip, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -26f), new Vector2(600f, 120f));

            // Film counter (top-right).
            var film = UIUtil.NewImage("Film", root, UIUtil.WithAlpha(Palette.Ink, 0.55f), UIUtil.RoundedSprite, true).rectTransform;
            // Below the page's "View projects" corner link (HTML, top-right).
            UIUtil.Anchor(film, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -78f), new Vector2(250f, 44f));
            _filmText = UIUtil.NewText("Text", film, "", 20, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(_filmText.rectTransform);
            _filmRoot = film.gameObject;
            _filmRoot.SetActive(false);

            // Rewind hint (bottom-left).
            var rw = UIUtil.NewImage("Rewind", root, UIUtil.WithAlpha(Palette.Ink, 0.45f), UIUtil.RoundedSprite, true).rectTransform;
            UIUtil.Anchor(rw, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 26f), new Vector2(150f, 40f));
            var rwText = UIUtil.NewText("Text", rw, "[R]  Rewind", 19, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Normal, false);
            UIUtil.Stretch(rwText.rectTransform);
            _rewindRoot = rw.gameObject;
            _rewindRoot.SetActive(false);
        }
    }
}
