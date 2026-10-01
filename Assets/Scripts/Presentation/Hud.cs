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
    /// Photos joining the inventory arrive as small "ghost" Polaroids that fly into their slot (from a
    /// pickup in the world, from the middle of the screen on rewind, or out of the instant camera as a
    /// print), and the slot pops when they land. The toast steps aside (left gutter) when a raised,
    /// rotated photo would cover it.
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
        const float ToastY = 100f;

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

        // Toast placement (default top centre, or the left gutter beside a tall raised photo).
        const float FilmInset = 24f + 250f;      // the film pill's right margin + width
        const float CornerBlocksBottom = 124f;   // below the strip's slots and the film pill
        float _toastAvoid = -1f;
        float _toastCovered;
        bool _toastInGutter;
        string _toastLaidOut;
        float _toastGutterWidth = -1f;

        // Arriving photos.
        RectTransform _ghostLayer;
        readonly List<Ghost> _ghosts = new List<Ghost>();
        readonly Dictionary<PhotoData, float> _pops = new Dictionary<PhotoData, float>();
        const float FlySeconds = 0.42f;
        const float PopSeconds = 0.28f;
        const float PrintSlide = 0.38f, PrintHold = 0.5f;

        sealed class Slot
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public RawImage Image;
            public Image Frame;
            public Text Number;
            public PhotoData Photo;
            public float BaseScale = 1f;
            public float BaseRotation;
        }

        sealed class Ghost
        {
            public PhotoData Photo;
            public RectTransform Root;
            public CanvasGroup Group;
            public bool Print;
            public Vector2 From;
            public float FromScale, FromRotation;
            public float T;
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
                _promptCurrent = null;
                UpdatePromptVisibility();
                return;
            }
            if (text == _promptCurrent) return;
            _promptCurrent = text;
            _promptText.text = text;
            _promptPill.sizeDelta = new Vector2(_promptText.preferredWidth + 56f, 52f);
            UpdatePromptVisibility();
        }

        /// <summary>
        /// The bottom prompt stays hidden while another surface is already instructing / animating there:
        /// the room 1 tutorial line, and an instant-camera print sliding up from the bottom edge.
        /// </summary>
        void UpdatePromptVisibility()
        {
            bool show = _promptCurrent != null && !Onboarding.IsGuiding && !PrintInFlight();
            if (_promptPill.gameObject.activeSelf != show) _promptPill.gameObject.SetActive(show);
        }

        bool PrintInFlight()
        {
            for (int i = 0; i < _ghosts.Count; i++)
                if (_ghosts[i].Print) return true;
            return false;
        }

        /// <summary>True while <paramref name="photo"/> is still flying into its inventory slot.</summary>
        public bool IsArriving(PhotoData photo) => IsIncoming(photo);

        /// <summary>Show a short message that fades out after a couple of seconds.</summary>
        public void Toast(string text)
        {
            Toast(text, ToastDefaultSeconds);
        }

        public void Toast(string text, float seconds)
        {
            if (_toastText == null || string.IsNullOrEmpty(text)) return;
            _toastText.text = text;
            _toastLaidOut = null;
            LayoutToast(true);
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

            // Toast fade. A toast that would sit on a raised photo (narrow / square windows, where it
            // cannot step aside into a gutter) is hidden while the photo is up: the photo's own border
            // already shows its controls.
            if (_toastTime > 0f)
            {
                LayoutToast(false);
                _toastTime -= dt;
                _toastCovered = Mathf.MoveTowards(_toastCovered, ToastOverlapsRaisedPhoto() ? 1f : 0f, dt * 8f);
                _toastGroup.alpha = Mathf.Clamp01(_toastTime / ToastFade) * (1f - _toastCovered);
                float age = _toastDuration - _toastTime;
                float pop = 1f + 0.08f * Mathf.Clamp01(1f - age / 0.15f);
                _toastPill.localScale = new Vector3(pop, pop, 1f);
                if (_toastTime <= 0f) _toastPill.gameObject.SetActive(false);
            }

            // Selection highlight.
            if (_inventory != null && _inventory.SelectedIndex != _shownSelected)
                ApplySelection();

            UpdateGhosts(dt);
            UpdatePops();
            UpdatePromptVisibility();

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
                var group = root.gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;

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

                _slots.Add(new Slot { Root = root, Group = group, Image = img, Frame = frame, Number = num, Photo = p });
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
                s.BaseScale = on ? 1.12f : 1f;
                s.BaseRotation = on ? 0f : ((i % 2 == 0) ? 2.5f : -2f);
                s.Root.localScale = new Vector3(s.BaseScale, s.BaseScale, 1f);
                s.Root.localRotation = Quaternion.Euler(0f, 0f, s.BaseRotation);
                s.Group.alpha = IsIncoming(s.Photo) ? 0f : 1f;
                s.Frame.color = on ? Palette.Butter : UIUtil.WithAlpha(Palette.White, 0.92f);
                if (s.Image.texture == null) s.Image.color = Palette.Sky;
                else s.Image.color = on ? Color.white : new Color(1f, 1f, 1f, 0.75f);
                s.Number.color = on ? Palette.Ink : Palette.Slate;
            }
        }

        // ---------------------------------------------------------------- arriving photos

        /// <summary>The photo flies from the middle of the screen (where it was held up) into its slot.</summary>
        public void FlyInFromCenter(PhotoData photo)
        {
            float big = Mathf.Clamp(((RectTransform)transform).rect.height * 0.5f / 90f, 3f, 7f);
            StartGhost(photo, Vector2.zero, big, 0f, false);
        }

        /// <summary>The photo pops from a world position (a pickup) into its slot.</summary>
        public void FlyInFromWorld(PhotoData photo, Vector3 worldPosition)
        {
            Vector2 from = Vector2.zero;
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(worldPosition);
                if (sp.z > 0f)
                    RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, sp, null, out from);
            }
            StartGhost(photo, from, 1.7f, -12f, false);
        }

        /// <summary>An instant-camera print slides out from the bottom edge, rests a moment, then flies into its slot.</summary>
        public void PrintOut(PhotoData photo)
        {
            // About a quarter of the screen height while it rests, so the new snapshot reads.
            float aspect = photo != null && photo.Aspect > 0.01f ? photo.Aspect : 4f / 3f;
            float h = SlotImageWidth / aspect + SlotBorder + SlotBottom;
            float screenH = ((RectTransform)transform).rect.height;
            float scale = Mathf.Clamp(screenH * 0.25f / h, 1.5f, 6f);
            StartGhost(photo, Vector2.zero, scale, 0f, true);
        }

        bool IsIncoming(PhotoData p)
        {
            if (p == null) return false;
            for (int i = 0; i < _ghosts.Count; i++)
                if (_ghosts[i].Photo == p) return true;
            return false;
        }

        void StartGhost(PhotoData photo, Vector2 from, float fromScale, float fromRotation, bool print)
        {
            if (photo == null || _ghostLayer == null) return;
            for (int i = _ghosts.Count - 1; i >= 0; i--)
                if (_ghosts[i].Photo == photo) RemoveGhost(i, false);

            float aspect = photo.Aspect > 0.01f ? photo.Aspect : 4f / 3f;
            float imgW = SlotImageWidth, imgH = imgW / aspect;
            float w = imgW + SlotBorder * 2f, h = imgH + SlotBorder + SlotBottom;

            var root = UIUtil.NewRect("Arriving " + photo.Label, _ghostLayer);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(w, h);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var shadow = UIUtil.NewImage("Shadow", root, new Color(0.1f, 0.1f, 0.2f, 0.18f), UIUtil.RoundedSprite, true);
            UIUtil.Stretch(shadow.rectTransform);
            shadow.rectTransform.offsetMin = new Vector2(2f, -5f);
            shadow.rectTransform.offsetMax = new Vector2(2f, -5f);
            shadow.pixelsPerUnitMultiplier = 4f;
            var frame = UIUtil.NewImage("Frame", root, Palette.White, UIUtil.RoundedSprite, true);
            UIUtil.Stretch(frame.rectTransform);
            frame.pixelsPerUnitMultiplier = 4f;
            var img = UIUtil.NewRaw("Photo", root);
            var irt = img.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.anchoredPosition = new Vector2(0f, -SlotBorder);
            irt.sizeDelta = new Vector2(imgW, imgH);
            img.texture = photo.Preview;
            if (img.texture == null) img.color = Palette.Sky;

            if (print)
            {
                // Starts fully below the bottom edge.
                float half = ((RectTransform)transform).rect.height * 0.5f;
                from = new Vector2(0f, -half - h * fromScale * 0.55f);
            }

            var g = new Ghost
            {
                Photo = photo, Root = root, Group = group, Print = print,
                From = from, FromScale = fromScale, FromRotation = fromRotation, T = 0f,
            };
            _ghosts.Add(g);
            ApplyGhost(g);
            // Hide the real slot until the ghost lands.
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Photo == photo) _slots[i].Group.alpha = 0f;
        }

        void RemoveGhost(int index, bool landed)
        {
            Ghost g = _ghosts[index];
            _ghosts.RemoveAt(index);
            if (g.Root != null) Destroy(g.Root.gameObject);
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Photo == g.Photo) _slots[i].Group.alpha = 1f;
            if (landed && g.Photo != null) _pops[g.Photo] = Time.unscaledTime;
        }

        void UpdateGhosts(float dt)
        {
            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                Ghost g = _ghosts[i];
                g.T += Mathf.Min(dt, 0.05f);
                float total = g.Print ? PrintSlide + PrintHold + FlySeconds : FlySeconds;
                if (g.T >= total || g.Root == null) RemoveGhost(i, true);
                else ApplyGhost(g);
            }
        }

        static float EaseOutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        static float EaseInOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        void ApplyGhost(Ghost g)
        {
            Vector2 from = g.From;
            float fromScale = g.FromScale, fromRot = g.FromRotation;
            float t = g.T;
            if (g.Print)
            {
                float h = g.Root.sizeDelta.y;
                float half = ((RectTransform)transform).rect.height * 0.5f;
                // Fully above the bottom edge, with a margin.
                Vector2 rest = new Vector2(0f, -half + h * fromScale * 0.5f + half * 0.1f);
                if (t < PrintSlide + PrintHold)
                {
                    // Ejected like a print: decelerating slide with a tiny settle wobble.
                    float e = EaseOutCubic(t / PrintSlide);
                    g.Root.anchoredPosition = Vector2.LerpUnclamped(from, rest, e);
                    g.Root.localScale = new Vector3(fromScale, fromScale, 1f);
                    float wobble = Mathf.Sin(t * 22f) * Mathf.Exp(-t * 7f) * 2.2f;
                    g.Root.localRotation = Quaternion.Euler(0f, 0f, wobble);
                    g.Group.alpha = 1f;
                    return;
                }
                from = rest;
                t -= PrintSlide + PrintHold;
            }

            float p = EaseInOutCubic(t / FlySeconds);
            Vector2 target = SlotTarget(g.Photo, out float targetRot, out float targetScale);
            Vector2 pos = Vector2.LerpUnclamped(from, target, p);
            pos.y += Mathf.Sin(p * Mathf.PI) * 50f; // a little arc
            float sc = Mathf.Lerp(fromScale, targetScale, EaseOutCubic(t / FlySeconds));
            g.Root.anchoredPosition = pos;
            g.Root.localScale = new Vector3(sc, sc, 1f);
            g.Root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(fromRot, targetRot, p));
            g.Group.alpha = g.Print ? 1f : Mathf.Clamp01(t / 0.08f);
        }

        /// <summary>Where the photo's slot is, in this HUD's local coordinates (strip start if it has none).</summary>
        Vector2 SlotTarget(PhotoData photo, out float rotation, out float scale)
        {
            var root = (RectTransform)transform;
            rotation = 0f;
            scale = 1f;
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                if (s.Photo != photo || s.Root == null) continue;
                rotation = s.BaseRotation;
                scale = s.BaseScale;
                Vector3 world = s.Root.TransformPoint(s.Root.rect.center);
                return root.InverseTransformPoint(world);
            }
            return root.InverseTransformPoint(_strip.TransformPoint(new Vector3(50f, -50f, 0f)));
        }

        void UpdatePops()
        {
            if (_pops.Count == 0) return;
            float now = Time.unscaledTime;
            List<PhotoData> done = null;
            foreach (var kv in _pops)
            {
                float k = (now - kv.Value) / PopSeconds;
                Slot s = null;
                for (int i = 0; i < _slots.Count; i++)
                    if (_slots[i].Photo == kv.Key) { s = _slots[i]; break; }
                if (k >= 1f || s == null)
                {
                    if (s != null) s.Root.localScale = new Vector3(s.BaseScale, s.BaseScale, 1f);
                    (done ?? (done = new List<PhotoData>())).Add(kv.Key);
                    continue;
                }
                float bump = 1f + 0.28f * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * (1f - k * 0.5f);
                float sc = s.BaseScale * bump;
                s.Root.localScale = new Vector3(sc, sc, 1f);
            }
            if (done != null) for (int i = 0; i < done.Count; i++) _pops.Remove(done[i]);
        }

        // ---------------------------------------------------------------- toast placement

        /// <summary>
        /// Top centre by default. When a raised photo (typically a sideways one) reaches up into the toast's
        /// row, the toast moves into the free gutter left of the photo, wrapped to fit.
        /// </summary>
        void LayoutToast(bool force)
        {
            var root = (RectTransform)transform;
            Rect canvas = root.rect;
            bool gutter = false;
            float gutterWidth = 0f, gutterCenterX = 0f;
            var overlay = PhotoOverlayUI.Instance;
            if (overlay != null && overlay.TryGetRaisedFrameRect(out Rect frame))
            {
                float toastBottom = canvas.yMax - ToastY - 30f;
                if (frame.yMax > toastBottom - 10f)
                {
                    gutterWidth = frame.xMin - canvas.xMin;
                    if (gutterWidth >= 240f)
                    {
                        gutter = true;
                        gutterCenterX = canvas.xMin + gutterWidth * 0.5f;
                    }
                }
            }

            // The top corners hold the photo strip (left) and the film pill (right): a centred toast
            // must fit between them (narrow / square windows), wrapping or dropping below if needed.
            float leftBlock = StripRight();
            float rightBlock = _filmRoot != null && _filmRoot.activeSelf ? FilmInset : 0f;
            float avoid = leftBlock * 4096f + rightBlock + canvas.width * 1e-3f;

            string text = _toastText.text;
            if (!force && gutter == _toastInGutter && text == _toastLaidOut &&
                (!gutter || Mathf.Abs(gutterWidth - _toastGutterWidth) < 1f) && Mathf.Abs(avoid - _toastAvoid) < 0.5f)
                return;
            _toastInGutter = gutter;
            _toastLaidOut = text;
            _toastGutterWidth = gutterWidth;
            _toastAvoid = avoid;

            if (!gutter)
            {
                const float gap = 18f, minWrapWidth = 360f;
                float half = canvas.width * 0.5f;
                float free = 2f * Mathf.Min(half - leftBlock - gap, half - rightBlock - gap);
                _toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
                _toastText.rectTransform.offsetMin = Vector2.zero;
                _toastText.rectTransform.offsetMax = Vector2.zero;
                float want = _toastText.preferredWidth + 64f;
                float maxWidth = canvas.width - 48f;
                if (want <= free || (leftBlock <= 0f && rightBlock <= 0f && want <= maxWidth))
                {
                    _toastPill.anchoredPosition = new Vector2(0f, -ToastY);
                    _toastPill.sizeDelta = new Vector2(want, 60f);
                    return;
                }

                // Wrap to the free width (two lines usually); if even that is too narrow, wrap to a
                // readable width and move the toast below the corner blocks.
                float w = Mathf.Min(want, Mathf.Max(free, minWrapWidth), maxWidth);
                _toastText.horizontalOverflow = HorizontalWrapMode.Wrap;
                _toastText.rectTransform.offsetMin = new Vector2(24f, 0f);
                _toastText.rectTransform.offsetMax = new Vector2(-24f, 0f);
                _toastPill.sizeDelta = new Vector2(w, 60f);
                float h = Mathf.Max(60f, _toastText.preferredHeight + 26f);
                _toastPill.sizeDelta = new Vector2(w, h);
                float topY = ToastY - 30f;                       // the single-line pill's top edge
                if (w > free + 1f) topY = Mathf.Max(topY, CornerBlocksBottom + 12f);
                _toastPill.anchoredPosition = new Vector2(0f, -(topY + h * 0.5f));
                return;
            }

            float width = Mathf.Min(_toastText.preferredWidth + 64f, gutterWidth - 48f);
            _toastText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _toastPill.sizeDelta = new Vector2(width, 60f);
            _toastText.rectTransform.offsetMin = new Vector2(22f, 0f);
            _toastText.rectTransform.offsetMax = new Vector2(-22f, 0f);
            float height = Mathf.Max(60f, _toastText.preferredHeight + 30f);
            _toastPill.sizeDelta = new Vector2(width, height);
            // Anchor is the top centre of the canvas; centre vertically in the gutter.
            _toastPill.anchoredPosition = new Vector2(gutterCenterX, -canvas.height * 0.5f);
        }

        /// <summary>True when the (centred) toast pill would overlap a raised photo's frame, with a 12 px margin.</summary>
        bool ToastOverlapsRaisedPhoto()
        {
            if (_toastInGutter) return false;
            var overlay = PhotoOverlayUI.Instance;
            if (overlay == null || !overlay.TryGetRaisedFrameRect(out Rect frame)) return false;
            Rect canvas = ((RectTransform)transform).rect;
            Vector2 size = _toastPill.sizeDelta;
            float cx = _toastPill.anchoredPosition.x;
            float cy = canvas.yMax + _toastPill.anchoredPosition.y;   // anchored to the top centre
            var toast = new Rect(cx - size.x * 0.5f, cy - size.y * 0.5f - 12f, size.x, size.y + 12f);
            return toast.Overlaps(frame);
        }

        /// <summary>Right edge of the photo strip's last slot, in canvas units from the left edge (0 if empty).</summary>
        float StripRight()
        {
            if (_slots.Count == 0 || _strip == null) return 0f;
            RectTransform last = _slots[_slots.Count - 1].Root;
            if (last == null) return 0f;
            return _strip.anchoredPosition.x + last.anchoredPosition.x + last.sizeDelta.x * 0.5f;
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
            UIUtil.Anchor(_toastPill, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -ToastY), new Vector2(300f, 60f));
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

            // Arriving photos fly above everything else on the HUD.
            _ghostLayer = UIUtil.NewRect("ArrivingPhotos", root);
            UIUtil.Stretch(_ghostLayer);
        }
    }
}
