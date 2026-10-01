using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Presentation.Motion;
using Ion.Projection;
using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// In-game HUD (art bible §9): bottom-centre prompt (bracket key style, 0.15 s crossfade on change), a
    /// toast that springs in and eases out, the photo strip (top-left, Paper cards, the selected one in
    /// Brass), the film pill (top-right) and the [R] rewind chip (bottom-left; it shakes when there is
    /// nothing to rewind). Built by UIFactory.Create().
    /// Photos joining the inventory arrive as small "ghost" prints that fly into their slot: from a pickup in
    /// the world (lift 0.2 m with easeOutBack, then 0.40 s easeInOutCubic), from the middle of the screen
    /// on rewind, or out of the instant camera as a print (0.5 s eject). The slot lands on a spring.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public static Hud Instance { get; private set; }

        const float SlotImageWidth = 84f;
        const float SlotBorder = 6f;
        const float SlotBottom = 18f;
        const float SlotSpacing = 12f;
        const float ToastY = 100f;

        // Prompt.
        RectTransform _promptPill;
        Image _promptPillImage;
        CanvasGroup _promptGroup;
        Text _promptText;
        string _promptCurrent;   // text on screen
        string _promptPending;   // text waiting for the crossfade (null = hide)
        bool _promptSwap;
        float _promptAlpha;

        // Toast.
        RectTransform _toastPill;
        Text _toastText;
        CanvasGroup _toastGroup;
        float _toastTime;      // time remaining in the hold
        float _toastOut = -1f; // >= 0 while fading out
        SpringFloat _toastIn = new SpringFloat(0f, Feel.ToastFreq, Feel.ToastZeta);

        // Strip.
        RectTransform _strip;
        readonly List<Slot> _slots = new List<Slot>();
        PhotoInventory _inventory;
        int _shownSelected = int.MinValue;

        // Film.
        GameObject _filmRoot;
        Text _filmText;
        InstantCamera _camera;
        int _shownFilm = int.MinValue;
        bool _shownUnlocked;

        // Rewind chip.
        RectTransform _rewindChip;
        CanvasGroup _rewindGroup;
        float _rewindAlpha;
        float _shakeT = -1f;
        float _chipForcedUntil = -1f;

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
            public SpringFloat Scale = new SpringFloat(1f, Feel.CardFreq, Feel.CardZeta);
            public SpringFloat Rotation = new SpringFloat(0f, Feel.CardFreq, Feel.CardZeta);
        }

        enum GhostKind { World, Center, Print }

        sealed class Ghost
        {
            public PhotoData Photo;
            public RectTransform Root;
            public CanvasGroup Group;
            public GhostKind Kind;
            public Vector3 World;          // pickups: where the print lifted from
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

        /// <summary>
        /// The falling / limbo prompt: shown large, centred below the crosshair, in ion on a Paper-edged pill, so a
        /// fall reads as "press R" at once instead of a camera bug.
        /// </summary>
        public static readonly string UrgentRewindPrompt = UIUtil.Key("R") + "  rewind";

        void StylePrompt(bool urgent)
        {
            int size = urgent ? 34 : 23;
            float h = urgent ? 76f : 52f;
            _promptText.fontSize = size;
            _promptText.color = urgent ? UIPalette.Graphite : UIPalette.Paper;
            _promptPillImage.color = urgent ? UIUtil.WithAlpha(UIPalette.Ion, 0.95f) : UIUtil.WithAlpha(UIPalette.Graphite, 0.72f);
            Vector2 anchor = urgent ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0f);
            _promptPill.anchorMin = anchor;
            _promptPill.anchorMax = anchor;
            _promptPill.anchoredPosition = urgent ? new Vector2(0f, -120f) : new Vector2(0f, 110f);
            _promptPill.sizeDelta = new Vector2(_promptText.preferredWidth + (urgent ? 80f : 56f), h);
        }

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
            if (string.IsNullOrEmpty(text)) text = null;
            if (text == _promptPending && (_promptSwap || text == _promptCurrent)) return;
            if (!_promptSwap && text == _promptCurrent) return;
            _promptPending = text;
            _promptSwap = true;
        }

        /// <summary>True while <paramref name="photo"/> is still flying into its inventory slot.</summary>
        public bool IsArriving(PhotoData photo) => IsIncoming(photo);

        /// <summary>Show a short message (springs in, holds 2.4 s, eases out).</summary>
        public void Toast(string text) => Toast(text, Feel.ToastHoldSeconds);

        public void Toast(string text, float seconds)
        {
            if (_toastText == null || string.IsNullOrEmpty(text)) return;
            bool wasShowing = _toastPill.gameObject.activeSelf && _toastOut < 0f;
            _toastText.text = text;
            _toastLaidOut = null;
            LayoutToast(true);
            _toastTime = Mathf.Max(0.2f, seconds);
            _toastOut = -1f;
            _toastPill.gameObject.SetActive(true);
            if (!wasShowing) _toastIn.Snap(0f);
            else _toastIn.Kick(-1.5f); // a new line on a visible toast: a small nudge, not a restart
            _toastIn.Target = 1f;
        }

        /// <summary>Hides any toast at once (game restart: a toast paused under the end card must not reappear).</summary>
        public void ClearToast()
        {
            if (_toastPill == null) return;
            _toastPill.gameObject.SetActive(false);
            _toastOut = -1f;
            _toastTime = 0f;
        }

        /// <summary>"Nothing to rewind": the [R] chip shows and shakes (2 cycles, 5 px, decaying).</summary>
        public void ShakeRewindChip()
        {
            _shakeT = 0f;
            _chipForcedUntil = Time.unscaledTime + Feel.NothingToastSeconds;
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
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

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

            UpdatePrompt(dt);
            UpdateToast(dt);

            if (_inventory != null && _inventory.SelectedIndex != _shownSelected)
                ApplySelection(false);

            UpdateGhosts(dt);
            UpdateSlots(dt);

            // Film counter.
            bool unlocked = _camera != null && _camera.Unlocked;
            int film = _camera != null ? _camera.Film : 0;
            if (unlocked != _shownUnlocked || (unlocked && film != _shownFilm))
            {
                _shownUnlocked = unlocked;
                _shownFilm = film;
                _filmRoot.SetActive(unlocked);
                if (unlocked) _filmText.text = "FILM  " + film + "      " + UIUtil.Key("C") + " camera";
            }

            UpdateRewindChip(dt);
        }

        // ---------------------------------------------------------------- prompt

        void UpdatePrompt(float dt)
        {
            bool blocked = PrintInFlight();
            float half = Feel.PromptCrossfadeSeconds * 0.5f;
            if (_promptSwap)
            {
                // Out (half the crossfade), swap at zero, then in.
                _promptAlpha = Mathf.MoveTowards(_promptAlpha, 0f, dt / half);
                if (_promptAlpha <= 0.001f || _promptCurrent == null)
                {
                    _promptSwap = false;
                    _promptCurrent = _promptPending;
                    if (_promptCurrent != null)
                    {
                        _promptText.text = _promptCurrent;
                        StylePrompt(_promptCurrent == UrgentRewindPrompt);
                    }
                }
            }
            else
            {
                float target = _promptCurrent != null && !blocked ? 1f : 0f;
                _promptAlpha = Mathf.MoveTowards(_promptAlpha, target, dt / half);
            }
            _promptGroup.alpha = _promptAlpha;
            bool show = _promptAlpha > 0.001f;
            if (_promptPill.gameObject.activeSelf != show) _promptPill.gameObject.SetActive(show);
        }

        bool PrintInFlight()
        {
            for (int i = 0; i < _ghosts.Count; i++)
                if (_ghosts[i].Kind == GhostKind.Print) return true;
            return false;
        }

        // ---------------------------------------------------------------- toast

        void UpdateToast(float dt)
        {
            if (!_toastPill.gameObject.activeSelf) return;
            LayoutToast(false);
            _toastCovered = Mathf.MoveTowards(_toastCovered, ToastOverlapsRaisedPhoto() ? 1f : 0f, dt * 8f);

            float a;
            if (_toastOut < 0f)
            {
                _toastIn.Step(dt);
                _toastTime -= dt;
                a = Mathf.Clamp01(_toastIn.Value);
                if (_toastTime <= 0f) _toastOut = 0f;
            }
            else
            {
                _toastOut += dt;
                float k = _toastOut / Feel.ToastOutSeconds;
                a = Mathf.Clamp01(_toastIn.Value) * (1f - Ease.InQuad(k));
                if (k >= 1f)
                {
                    _toastPill.gameObject.SetActive(false);
                    _toastOut = -1f;
                    return;
                }
            }
            _toastGroup.alpha = a * (1f - _toastCovered);
            // Springs in from slightly small and 10 px high (the overshoot reads as a soft settle).
            float s = Mathf.LerpUnclamped(0.92f, 1f, _toastIn.Value);
            _toastPill.localScale = new Vector3(s, s, 1f);
            var p = _toastPill.anchoredPosition;
            _toastPill.anchoredPosition = new Vector2(p.x, _toastBaseY + (1f - Mathf.Clamp01(_toastIn.Value)) * 10f);
        }

        float _toastBaseY = -ToastY;

        // ---------------------------------------------------------------- rewind chip

        void UpdateRewindChip(float dt)
        {
            var history = WorldHistory.Instance;
            bool can = history != null && history.CanUndo;
            bool show = can || Time.unscaledTime < _chipForcedUntil;
            _rewindAlpha = Mathf.MoveTowards(_rewindAlpha, show ? 1f : 0f, dt / 0.15f);
            _rewindGroup.alpha = Ease.OutQuad(_rewindAlpha) * (can ? 1f : 0.75f);
            bool active = _rewindAlpha > 0.001f;
            if (_rewindChip.gameObject.activeSelf != active) _rewindChip.gameObject.SetActive(active);

            float x = 0f;
            if (_shakeT >= 0f)
            {
                _shakeT += dt;
                float k = _shakeT / Feel.NothingSeconds;
                if (k >= 1f) _shakeT = -1f;
                else if (!Feel.ReducedMotion)
                    x = Feel.NothingShakePx * Ease.Shake(k, Feel.NothingShakeCycles, Feel.NothingShakeZeta);
            }
            _rewindChip.anchoredPosition = new Vector2(28f + x, 26f);
        }

        // ---------------------------------------------------------------- inventory strip

        void OnInventoryChanged()
        {
            RebuildStrip();
        }

        void RebuildStrip()
        {
            // Keep the springs of photos that stay, so a rebuild never pops.
            var keep = new Dictionary<PhotoData, Slot>();
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Photo != null && !keep.ContainsKey(_slots[i].Photo)) keep[_slots[i].Photo] = _slots[i];
                if (_slots[i].Root != null) Destroy(_slots[i].Root.gameObject);
            }
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

                var frame = UIUtil.NewImage("Frame", root, UIPalette.Paper, UIUtil.RoundedSprite, true);
                UIUtil.Stretch(frame.rectTransform);
                frame.pixelsPerUnitMultiplier = 4f; // small corner radius

                var img = UIUtil.NewRaw("Photo", root);
                var irt = img.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = new Vector2(0f, -SlotBorder);
                irt.sizeDelta = new Vector2(imgW, imgH);
                img.texture = p != null ? p.Preview : null;
                if (img.texture == null) img.color = UIPalette.Frost;

                var num = UIUtil.NewText("Num", root, (i + 1).ToString("00"), 14, UIPalette.Graphite, TextAnchor.MiddleCenter, FontStyle.Bold, false);
                var nrt = num.rectTransform;
                nrt.anchorMin = new Vector2(0f, 0f);
                nrt.anchorMax = new Vector2(1f, 0f);
                nrt.pivot = new Vector2(0.5f, 0f);
                nrt.anchoredPosition = new Vector2(0f, 0f);
                nrt.sizeDelta = new Vector2(0f, SlotBottom);

                var slot = new Slot { Root = root, Group = group, Image = img, Frame = frame, Number = num, Photo = p };
                if (p != null && keep.TryGetValue(p, out Slot old))
                {
                    slot.Scale = old.Scale;
                    slot.Rotation = old.Rotation;
                }
                _slots.Add(slot);
                x += w + SlotSpacing;
            }
            ApplySelection(true);
        }

        void ApplySelection(bool snapNew)
        {
            int sel = _inventory != null ? _inventory.SelectedIndex : -1;
            _shownSelected = sel;
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                bool on = i == sel;
                s.BaseScale = on ? 1.12f : 1f;
                s.BaseRotation = on ? 0f : ((i % 2 == 0) ? 2.5f : -2f);
                s.Scale.Target = s.BaseScale;
                s.Rotation.Target = s.BaseRotation;
                if (snapNew && Mathf.Approximately(s.Scale.Value, 1f) && Mathf.Approximately(s.Rotation.Value, 0f) && s.Scale.Velocity == 0f)
                {
                    s.Scale.Snap(s.BaseScale);
                    s.Rotation.Snap(s.BaseRotation);
                }
                s.Group.alpha = IsIncoming(s.Photo) ? 0f : 1f;
                s.Frame.color = on ? UIPalette.Brass : UIUtil.WithAlpha(UIPalette.Paper, 0.94f);
                if (s.Image.texture == null) s.Image.color = UIPalette.Frost;
                else s.Image.color = on ? Color.white : new Color(1f, 1f, 1f, 0.78f);
                s.Number.color = on ? UIPalette.Graphite : UIPalette.GraphiteSoft;
            }
        }

        /// <summary>Selection changes and landings settle on the card spring (2.8 Hz, ζ 0.78).</summary>
        void UpdateSlots(float dt)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s.Root == null) continue;
                float sc = s.Scale.Step(dt);
                float rot = s.Rotation.Step(dt);
                s.Root.localScale = new Vector3(sc, sc, 1f);
                s.Root.localRotation = Quaternion.Euler(0f, 0f, rot);
            }
        }

        // ---------------------------------------------------------------- arriving photos

        /// <summary>The photo flies from the middle of the screen (where it was held up) into its slot.</summary>
        public void FlyInFromCenter(PhotoData photo)
        {
            float big = Mathf.Clamp(((RectTransform)transform).rect.height * 0.5f / 90f, 3f, 7f);
            StartGhost(photo, GhostKind.Center, Vector2.zero, big, 0f, Vector3.zero);
        }

        /// <summary>The photo lifts from a world position (a pickup), then flies into its slot.</summary>
        public void FlyInFromWorld(PhotoData photo, Vector3 worldPosition)
        {
            Vector2 from = Vector2.zero;
            float scale = 1.7f;
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(worldPosition);
                if (sp.z > 0f)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, sp, null, out from);
                    // Start at the print's on-screen size (a 0.34 m print).
                    Vector3 sp2 = cam.WorldToScreenPoint(worldPosition + cam.transform.right * 0.34f);
                    RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, sp2, null, out Vector2 to2);
                    float w = Mathf.Abs(to2.x - from.x);
                    scale = Mathf.Clamp(w / (SlotImageWidth + SlotBorder * 2f), 0.6f, 4f);
                }
            }
            StartGhost(photo, GhostKind.World, from, scale, -10f, worldPosition);
        }

        /// <summary>An instant-camera print ejects from the bottom edge, rests a moment, then flies into its slot.</summary>
        public void PrintOut(PhotoData photo)
        {
            // About a quarter of the screen height while it rests, so the new snapshot reads.
            float aspect = photo != null && photo.Aspect > 0.01f ? photo.Aspect : 4f / 3f;
            float h = SlotImageWidth / aspect + SlotBorder + SlotBottom;
            float screenH = ((RectTransform)transform).rect.height;
            float scale = Mathf.Clamp(screenH * 0.25f / h, 1.5f, 6f);
            StartGhost(photo, GhostKind.Print, Vector2.zero, scale, 0f, Vector3.zero);
        }

        const float PrintHold = 0.5f;

        bool IsIncoming(PhotoData p)
        {
            if (p == null) return false;
            for (int i = 0; i < _ghosts.Count; i++)
                if (_ghosts[i].Photo == p) return true;
            return false;
        }

        void StartGhost(PhotoData photo, GhostKind kind, Vector2 from, float fromScale, float fromRotation, Vector3 world)
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

            var shadow = UIUtil.NewImage("Shadow", root, UIUtil.WithAlpha(UIPalette.Cyanotype, 0.18f), UIUtil.RoundedSprite, true);
            UIUtil.Stretch(shadow.rectTransform);
            shadow.rectTransform.offsetMin = new Vector2(2f, -5f);
            shadow.rectTransform.offsetMax = new Vector2(2f, -5f);
            shadow.pixelsPerUnitMultiplier = 4f;
            var frame = UIUtil.NewImage("Frame", root, UIPalette.Paper, UIUtil.RoundedSprite, true);
            UIUtil.Stretch(frame.rectTransform);
            frame.pixelsPerUnitMultiplier = 4f;
            var img = UIUtil.NewRaw("Photo", root);
            var irt = img.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.anchoredPosition = new Vector2(0f, -SlotBorder);
            irt.sizeDelta = new Vector2(imgW, imgH);
            img.texture = photo.Preview;
            if (img.texture == null) img.color = UIPalette.Frost;

            if (kind == GhostKind.Print)
            {
                // Starts fully below the bottom edge.
                float half = ((RectTransform)transform).rect.height * 0.5f;
                from = new Vector2(0f, -half - h * fromScale * 0.55f);
            }

            var g = new Ghost
            {
                Photo = photo, Root = root, Group = group, Kind = kind, World = world,
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
            {
                if (_slots[i].Photo != g.Photo) continue;
                _slots[i].Group.alpha = 1f;
                // Land on the card spring: a little overshoot instead of a pop.
                if (landed && !Feel.ReducedMotion) _slots[i].Scale.Kick(4f);
            }
        }

        float GhostDuration(Ghost g)
        {
            switch (g.Kind)
            {
                case GhostKind.World: return Feel.PickupLiftSeconds + Feel.PickupFlySeconds;
                case GhostKind.Print: return Feel.PrintEjectSeconds + PrintHold + Feel.PickupFlySeconds;
                default: return Feel.PickupFlySeconds;
            }
        }

        void UpdateGhosts(float dt)
        {
            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                Ghost g = _ghosts[i];
                g.T += dt;
                if (g.T >= GhostDuration(g) || g.Root == null) RemoveGhost(i, true);
                else ApplyGhost(g);
            }
        }

        void ApplyGhost(Ghost g)
        {
            Vector2 from = g.From;
            float fromScale = g.FromScale, fromRot = g.FromRotation;
            float t = g.T;
            var root = (RectTransform)transform;

            if (g.Kind == GhostKind.Print)
            {
                float h = g.Root.sizeDelta.y;
                float half = root.rect.height * 0.5f;
                Vector2 rest = new Vector2(0f, -half + h * fromScale * 0.5f + half * 0.1f);
                if (t < Feel.PrintEjectSeconds + PrintHold)
                {
                    // Ejected like a print: decelerating slide, then a tiny settle.
                    float e = Ease.OutCubic(t / Feel.PrintEjectSeconds);
                    g.Root.anchoredPosition = Vector2.LerpUnclamped(from, rest, e);
                    g.Root.localScale = new Vector3(fromScale, fromScale, 1f);
                    float wobble = Feel.ReducedMotion ? 0f : Mathf.Sin(t * 18f) * Mathf.Exp(-t * 6f) * 1.6f;
                    g.Root.localRotation = Quaternion.Euler(0f, 0f, wobble);
                    g.Group.alpha = 1f;
                    return;
                }
                from = rest;
                t -= Feel.PrintEjectSeconds + PrintHold;
            }
            else if (g.Kind == GhostKind.World)
            {
                // Lift 0.2 m above the pickup (easeOutBack), tracked on screen.
                Camera cam = Camera.main;
                float lift = Ease.OutBack(Mathf.Min(t, Feel.PickupLiftSeconds) / Feel.PickupLiftSeconds, Feel.PickupLiftBack);
                if (cam != null)
                {
                    Vector3 sp = cam.WorldToScreenPoint(g.World + Vector3.up * (Feel.PickupLift * lift));
                    if (sp.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out Vector2 p))
                        from = p;
                }
                if (t < Feel.PickupLiftSeconds)
                {
                    g.Root.anchoredPosition = from;
                    g.Root.localScale = new Vector3(fromScale, fromScale, 1f);
                    g.Root.localRotation = Quaternion.Euler(0f, 0f, fromRot * (1f - lift));
                    g.Group.alpha = Mathf.Clamp01(t / 0.08f);
                    return;
                }
                fromRot = 0f;
                t -= Feel.PickupLiftSeconds;
            }

            float p01 = Ease.InOutCubic(t / Feel.PickupFlySeconds);
            Vector2 target = SlotTarget(g.Photo, out float targetRot, out float targetScale);
            Vector2 pos = Vector2.LerpUnclamped(from, target, p01);
            if (!Feel.ReducedMotion) pos.y += Mathf.Sin(p01 * Mathf.PI) * 40f; // a little arc
            float sc = Mathf.Lerp(fromScale, targetScale, p01);
            g.Root.anchoredPosition = pos;
            g.Root.localScale = new Vector3(sc, sc, 1f);
            g.Root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(fromRot, targetRot, p01));
            g.Group.alpha = g.Kind == GhostKind.Center ? Mathf.Clamp01(t / 0.08f) : 1f;
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
                    SetToastPos(0f, -ToastY);
                    _toastPill.sizeDelta = new Vector2(want, 56f);
                    return;
                }

                // Wrap to the free width (two lines usually); if even that is too narrow, wrap to a
                // readable width and move the toast below the corner blocks.
                float w = Mathf.Min(want, Mathf.Max(free, minWrapWidth), maxWidth);
                _toastText.horizontalOverflow = HorizontalWrapMode.Wrap;
                _toastText.rectTransform.offsetMin = new Vector2(24f, 0f);
                _toastText.rectTransform.offsetMax = new Vector2(-24f, 0f);
                _toastPill.sizeDelta = new Vector2(w, 56f);
                float h = Mathf.Max(56f, _toastText.preferredHeight + 26f);
                _toastPill.sizeDelta = new Vector2(w, h);
                float topY = ToastY - 28f;                       // the single-line pill's top edge
                if (w > free + 1f) topY = Mathf.Max(topY, CornerBlocksBottom + 12f);
                SetToastPos(0f, -(topY + h * 0.5f));
                return;
            }

            float width = Mathf.Min(_toastText.preferredWidth + 64f, gutterWidth - 48f);
            _toastText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _toastPill.sizeDelta = new Vector2(width, 56f);
            _toastText.rectTransform.offsetMin = new Vector2(22f, 0f);
            _toastText.rectTransform.offsetMax = new Vector2(-22f, 0f);
            float height = Mathf.Max(56f, _toastText.preferredHeight + 30f);
            _toastPill.sizeDelta = new Vector2(width, height);
            // Anchor is the top centre of the canvas; centre vertically in the gutter.
            SetToastPos(gutterCenterX, -canvas.height * 0.5f);
        }

        void SetToastPos(float x, float y)
        {
            _toastBaseY = y;
            _toastPill.anchoredPosition = new Vector2(x, y);
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
            float cy = canvas.yMax + _toastBaseY;   // anchored to the top centre
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

            // Prompt (bottom centre): a Graphite key-cap pill with Paper text.
            _promptPillImage = UIUtil.NewImage("PromptPill", root, UIUtil.WithAlpha(UIPalette.Graphite, 0.72f), UIUtil.RoundedSprite, true);
            _promptPill = _promptPillImage.rectTransform;
            UIUtil.Anchor(_promptPill, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(300f, 52f));
            _promptGroup = _promptPill.gameObject.AddComponent<CanvasGroup>();
            _promptGroup.blocksRaycasts = false;
            _promptGroup.interactable = false;
            _promptText = UIUtil.NewText("Text", _promptPill, "", 23, UIPalette.Paper, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(_promptText.rectTransform);
            _promptPill.gameObject.SetActive(false);

            // Toast (upper centre): a Paper card with Graphite text.
            _toastPill = UIUtil.NewImage("ToastPill", root, UIUtil.WithAlpha(UIPalette.Paper, 0.95f), UIUtil.RoundedSprite, true).rectTransform;
            // High enough to clear a raised photo's top border.
            UIUtil.Anchor(_toastPill, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -ToastY), new Vector2(300f, 56f));
            _toastGroup = _toastPill.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastGroup.interactable = false;
            _toastText = UIUtil.NewText("Text", _toastPill, "", 26, UIPalette.Graphite, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(_toastText.rectTransform);
            _toastPill.gameObject.SetActive(false);

            // Photo strip (top-left).
            _strip = UIUtil.NewRect("PhotoStrip", root);
            UIUtil.Anchor(_strip, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -26f), new Vector2(600f, 120f));

            // Film counter (top-right), below the page's "View projects" corner link (HTML).
            var film = UIUtil.NewImage("Film", root, UIUtil.WithAlpha(UIPalette.Graphite, 0.62f), UIUtil.RoundedSprite, true).rectTransform;
            UIUtil.Anchor(film, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -78f), new Vector2(250f, 44f));
            _filmText = UIUtil.NewText("Text", film, "", 19, UIPalette.Paper, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(_filmText.rectTransform);
            _filmRoot = film.gameObject;
            _filmRoot.SetActive(false);

            // Rewind chip (bottom-left).
            var rw = UIUtil.NewImage("Rewind", root, UIUtil.WithAlpha(UIPalette.Graphite, 0.6f), UIUtil.RoundedSprite, true).rectTransform;
            UIUtil.Anchor(rw, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 26f), new Vector2(156f, 40f));
            _rewindGroup = rw.gameObject.AddComponent<CanvasGroup>();
            _rewindGroup.blocksRaycasts = false;
            var rwText = UIUtil.NewText("Text", rw, UIUtil.Key("R") + "  rewind", 19, UIPalette.Paper, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(rwText.rectTransform);
            _rewindChip = rw;
            _rewindChip.gameObject.SetActive(false);

            // Arriving photos fly above everything else on the HUD.
            _ghostLayer = UIUtil.NewRect("ArrivingPhotos", root);
            UIUtil.Stretch(_ghostLayer);
        }
    }
}
