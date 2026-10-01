using System;
using System.Text;
using Ion.Gameplay;
using Ion.Presentation.Motion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Ion.Presentation.Quality
{
    /// <summary>
    /// Self-installing settings overlay (own Screen Space Overlay canvas, sort order 50, above the HUD, the
    /// viewfinder and the click-to-play overlay). The bottom-right card is shown while the game is paused
    /// (pointer unlocked after having played): Quality (Low / Med / High / Ultra / Auto), mouse sensitivity, music
    /// and effects volume, Raise (Hold / Toggle), Reduced motion, Head bob and the FPS readout.
    /// F3 toggles the small top-right FPS readout at any time (persisted in "ion.showFps").
    ///
    /// The card hit-tests the raw pointer itself (no uGUI Selectables, so WASD/Space never drive it), and it
    /// registers its screen rect with <see cref="PointerLock"/> as a "block" region: a click on the card
    /// never locks the pointer (the page checks the rect inside the click itself).
    /// </summary>
    [DefaultExecutionOrder(32000)]
    [DisallowMultipleComponent]
    public sealed class SettingsPanel : MonoBehaviour
    {
        public const int SortingOrder = 50;
        public const string ShowFpsPrefKey = "ion.showFps";
        public const string MusicVolumePrefKey = "ion.musicVolume";
        public const string SfxVolumePrefKey = "ion.sfxVolume";
        /// <summary>PointerLock rect slot used by this card.</summary>
        public const int PointerRectSlot = 0;

        const int SliderSteps = 100;
        const float HandleSize = 22f;
        const float FpsRefreshSeconds = 0.5f;
        const float StatusRefreshSeconds = 0.25f;

        // Button indices: 0..4 quality tiers, then the toggles.
        const int RaiseButton = 5, MotionButton = 6, HeadBobButton = 7, FpsButton = 8, ButtonCount = 9;
        // Slider indices.
        const int SensSlider = 0, MusicSlider = 1, SfxSlider = 2, SliderCount = 3;

        static readonly int[] TierButtonValues = { QualityTier.Low, QualityTier.Medium, QualityTier.High, QualityTier.Ultra, QualityTier.Auto };

        public static SettingsPanel Instance { get; private set; }

        // ---------------------------------------------------------------- volumes (read by Audio, Lead E)

        static float s_Music = -1f, s_Sfx = -1f;

        /// <summary>Music volume 0..1 (persisted). Audio applies it on top of the master volume.</summary>
        public static float MusicVolume
        {
            get { if (s_Music < 0f) s_Music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumePrefKey, 0.8f)); return s_Music; }
            set { s_Music = Mathf.Clamp01(value); PlayerPrefs.SetFloat(MusicVolumePrefKey, s_Music); RaiseVolumes(); }
        }

        /// <summary>Sound-effects volume 0..1 (persisted).</summary>
        public static float SfxVolume
        {
            get { if (s_Sfx < 0f) s_Sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePrefKey, 1f)); return s_Sfx; }
            set { s_Sfx = Mathf.Clamp01(value); PlayerPrefs.SetFloat(SfxVolumePrefKey, s_Sfx); RaiseVolumes(); }
        }

        /// <summary>Raised when the music or effects volume changes (Audio subscribes).</summary>
        public static event Action VolumesChanged;

        static void RaiseVolumes()
        {
            try { VolumesChanged?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>True while the settings card is (becoming) visible.</summary>
        public bool IsVisible => _wantVisible;

        const float CardW = 420f, CardH = 512f, CardMargin = 28f;

        /// <summary>
        /// Canvas units taken from the right edge (card width + margin) while the card is visible, 0 otherwise.
        /// The click-to-play card moves left / shrinks to stay clear of it on small windows.
        /// </summary>
        public float ReservedRight { get; private set; }

        /// <summary>Height of the card's top edge above the bottom of the canvas (canvas units).</summary>
        public float ReservedTop { get; private set; }

        RectTransform _card;
        GameObject _cardContent;
        CanvasGroup _group;
        bool _wantVisible;
        float _vis;

        readonly RectTransform[] _buttons = new RectTransform[ButtonCount];
        readonly Image[] _buttonBg = new Image[ButtonCount];
        readonly Color[] _buttonColor = new Color[ButtonCount];
        readonly Text[] _buttonText = new Text[ButtonCount];
        readonly float[] _hoverT = new float[ButtonCount];
        readonly float[] _pressT = new float[ButtonCount];
        int _hovered = -1;
        int _shownPref = int.MinValue;

        sealed class SliderUI
        {
            public RectTransform Root, Fill, Handle;
            public Image HandleImage;
            public Text Value;
            public int Step;
        }

        readonly SliderUI[] _sliders = new SliderUI[SliderCount];
        string[] _sensLabels;
        int _dragging = -1;

        Text _status;
        int _statusTier = -99, _statusPref = -99, _statusScalePct = -1;
        bool _statusCalibrating;
        float _statusTimer;

        GameObject _fpsRoot;
        Text _fpsText;
        bool _showFps;
        float _fpsTimer;
        float _fpsAccum;
        int _fpsFrames;
        readonly StringBuilder _sb = new StringBuilder(48);

        bool _rectSent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            s_Music = s_Sfx = -1f;
            VolumesChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Instance != null || FindFirstObjectByType<SettingsPanel>() != null) return;
            EnsureEventSystem();

            var go = new GameObject("Ion Settings UI", typeof(RectTransform));
            go.layer = UIUtil.UILayer;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvas.pixelPerfect = false;

            // Same scaler as UIFactory so units match the HUD.
            IonCanvasScaler.AddTo(go); // 1920x1080, match 0.5, with a minimum scale

            // Lets EventSystem.IsPointerOverGameObject() see the card (for integration guards).
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<SettingsPanel>();
        }

        static void EnsureEventSystem()
        {
            // UIFactory normally creates it (GameBootstrap.Awake runs before AfterSceneLoad).
            var es = FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                go.AddComponent<EventSystem>();
                go.AddComponent<InputSystemUIInputModule>();
                return;
            }
            if (es.GetComponent<BaseInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _sensLabels = new string[SliderSteps + 1];
            for (int i = 0; i <= SliderSteps; i++)
                _sensLabels[i] = StepToSensitivity(i).ToString("0.000");

            Build();

            _showFps = PlayerPrefs.GetInt(ShowFpsPrefKey, 0) == 1;
            ApplyShowFps();
            ApplyToggleLabels();
            RefreshTierButtons(true);
            SyncSlidersFromSettings();

            _wantVisible = false;
            _group.alpha = 0f;
            _cardContent.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>True when <paramref name="screen"/> (pixels, origin bottom-left) is over the visible card.</summary>
        public bool ContainsScreenPoint(Vector2 screen) => _wantVisible && Contains(_card, screen);

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            var mouse = Mouse.current;
            var kb = Keyboard.current;

            if (kb != null && kb.f3Key.wasPressedThisFrame) SetShowFps(!_showFps);

            bool unlocked = !PointerLock.IsLocked;
            // Shown when paused after having played (not over the first-load title) and never over the end card.
            bool want = unlocked && PointerLock.HasEverLocked && !EndCard.IsOpen;
            if (want && !_wantVisible) SyncSlidersFromSettings();
            _wantVisible = want;

            HandlePointer(mouse, dt);

            // Fade: in 0.22 s (easeOutCubic, 8 px rise), out 0.16 s (easeInQuad).
            _vis = Mathf.MoveTowards(_vis, _wantVisible ? 1f : 0f, dt / (_wantVisible ? Feel.PanelInSeconds : Feel.PanelOutSeconds));
            float a = _wantVisible ? Ease.OutCubic(_vis) : Ease.InQuad(_vis);
            _group.alpha = a;
            bool active = _vis > 0.001f;
            if (_cardContent.activeSelf != active) _cardContent.SetActive(active);
            _group.blocksRaycasts = _wantVisible;

            LayoutCard(a);
            SyncPointerRect();

            if (active)
            {
                AnimateButtons(dt);
                _statusTimer -= dt;
                if (_statusTimer <= 0f)
                {
                    _statusTimer = StatusRefreshSeconds;
                    RefreshTierButtons(false);
                    RefreshStatus();
                }
            }

            if (_showFps) UpdateFps(dt);
        }

        /// <summary>Tells the page where the card is, so a click on it never locks the pointer.</summary>
        void SyncPointerRect()
        {
            if (_wantVisible)
            {
                PointerLock.SetRect(PointerRectSlot, PointerLock.RectKind.Block, UIUtil.ScreenRect(_card));
                _rectSent = true;
            }
            else if (_rectSent)
            {
                PointerLock.SetRect(PointerRectSlot, PointerLock.RectKind.Block, null);
                _rectSent = false;
            }
        }

        /// <summary>Fits the card on short / narrow windows (scaled about its bottom-right corner).</summary>
        void LayoutCard(float alpha)
        {
            Rect r = ((RectTransform)transform).rect;
            float scale = Mathf.Min(1f, (r.height - CardMargin * 2f) / CardH, r.width * 0.42f / CardW);
            scale = Mathf.Max(0.55f, scale);
            if (!Mathf.Approximately(_card.localScale.x, scale)) _card.localScale = new Vector3(scale, scale, 1f);
            _card.anchoredPosition = new Vector2(-CardMargin, CardMargin - (1f - alpha) * Feel.PanelRisePx);
            ReservedRight = _wantVisible ? CardW * scale + CardMargin : 0f;
            ReservedTop = _wantVisible ? CardH * scale + CardMargin : 0f;
        }

        // ------------------------------------------------------------------ pointer

        void HandlePointer(Mouse mouse, float dt)
        {
            if (mouse == null)
            {
                _dragging = -1;
                _hovered = -1;
                return;
            }

            Vector2 pos = mouse.position.ReadValue();
            bool usable = _wantVisible && _vis >= 0.5f && !PointerLock.IsLocked;

            if (usable && mouse.leftButton.wasPressedThisFrame && Contains(_card, pos))
            {
                for (int i = 0; i < ButtonCount; i++)
                {
                    if (!Contains(_buttons[i], pos)) continue;
                    _pressT[i] = Feel.UIPressSeconds;
                    if (i == FpsButton) SetShowFps(!_showFps);
                    else if (i == HeadBobButton) { FirstPersonController.HeadBobEnabled = !FirstPersonController.HeadBobEnabled; ApplyToggleLabels(); }
                    else if (i == RaiseButton) { Feel.RaiseToggle = !Feel.RaiseToggle; ApplyToggleLabels(); }
                    else if (i == MotionButton) { Feel.ReducedMotion = !Feel.ReducedMotion; ApplyToggleLabels(); }
                    else OnTierClicked(i);
                    break;
                }
                for (int s = 0; s < SliderCount; s++)
                    if (Contains(_sliders[s].Root, pos)) _dragging = s;
            }

            if (_dragging >= 0)
            {
                SliderUI sl = _sliders[_dragging];
                if (mouse.leftButton.isPressed && usable)
                {
                    SetSliderStep(_dragging, ScreenToSliderStep(sl, pos), false);
                    sl.HandleImage.color = UIPalette.Brass;
                }
                else
                {
                    // Persist once on release, not on every drag step.
                    CommitSlider(_dragging);
                    sl.HandleImage.color = UIPalette.Paper;
                    _dragging = -1;
                }
            }

            int hover = -1;
            if (usable && Contains(_card, pos))
            {
                for (int i = 0; i < ButtonCount; i++)
                {
                    if (Contains(_buttons[i], pos)) { hover = i; break; }
                }
            }
            _hovered = hover;
        }

        static bool Contains(RectTransform rt, Vector2 screen)
        {
            // Screen Space Overlay: no camera.
            return rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
        }

        /// <summary>Hover darkens over 0.12 s (easeOutQuad); a press dips to 0.97 scale for 0.08 s.</summary>
        void AnimateButtons(float dt)
        {
            for (int i = 0; i < ButtonCount; i++)
            {
                float target = i == _hovered ? 1f : 0f;
                _hoverT[i] = Mathf.MoveTowards(_hoverT[i], target, dt / Feel.UIHoverSeconds);
                float h = Ease.OutQuad(_hoverT[i]);
                Color c = _buttonColor[i];
                _buttonBg[i].color = Color.Lerp(c, new Color(c.r * 0.86f, c.g * 0.86f, c.b * 0.86f, c.a), h);
                if (_pressT[i] > 0f) _pressT[i] = Mathf.Max(0f, _pressT[i] - dt);
                float p = _pressT[i] > 0f ? Feel.UIPressScale : 1f;
                float s = Mathf.Lerp(_buttons[i].localScale.x, p, 1f - Mathf.Exp(-dt * 40f));
                _buttons[i].localScale = new Vector3(s, s, 1f);
            }
        }

        void SetButtonColor(int index, Color bg, Color text)
        {
            _buttonColor[index] = bg;
            _buttonBg[index].color = bg;
            _buttonText[index].color = text;
        }

        // ------------------------------------------------------------------ actions

        void OnTierClicked(int index)
        {
            QualityTier.Set(TierButtonValues[index]);
            RefreshTierButtons(true);
            RefreshStatus();
        }

        void SetShowFps(bool show)
        {
            if (show == _showFps) return;
            _showFps = show;
            PlayerPrefs.SetInt(ShowFpsPrefKey, show ? 1 : 0);
            PlayerPrefs.Save();
            ApplyShowFps();
        }

        void ApplyToggleLabels()
        {
            _buttonText[RaiseButton].text = Feel.RaiseToggle ? "Raise:  Toggle" : "Raise:  Hold";
            _buttonText[MotionButton].text = Feel.ReducedMotion ? "Reduced motion:  On" : "Reduced motion:  Off";
            _buttonText[HeadBobButton].text = FirstPersonController.HeadBobEnabled ? "Head bob:  On" : "Head bob:  Off";
        }

        void ApplyShowFps()
        {
            _fpsRoot.SetActive(_showFps);
            // The tier / render-scale readout is diagnostics: only with the FPS counter on.
            if (_status != null) _status.gameObject.SetActive(_showFps);
            _buttonText[FpsButton].text = _showFps ? "FPS (F3):  On" : "FPS (F3):  Off";
            _fpsTimer = 0f;
            _fpsAccum = 0f;
            _fpsFrames = 0;
        }

        // Log mapping so the useful low range is not squeezed into the first few pixels.
        static float StepToSensitivity(int step)
        {
            float t = step / (float)SliderSteps;
            float min = FirstPersonController.MinSensitivity, max = FirstPersonController.MaxSensitivity;
            return min * Mathf.Pow(max / min, t);
        }

        static int SensitivityToStep(float s)
        {
            float min = FirstPersonController.MinSensitivity, max = FirstPersonController.MaxSensitivity;
            float t = Mathf.Log(Mathf.Clamp(s, min, max) / min) / Mathf.Log(max / min);
            return Mathf.Clamp(Mathf.RoundToInt(t * SliderSteps), 0, SliderSteps);
        }

        static int ScreenToSliderStep(SliderUI sl, Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(sl.Root, screen, null, out Vector2 local))
                return sl.Step;
            Rect r = sl.Root.rect;
            float usable = Mathf.Max(1f, r.width - HandleSize);
            float t = Mathf.Clamp01((local.x - r.xMin - HandleSize * 0.5f) / usable);
            return Mathf.RoundToInt(t * SliderSteps);
        }

        void SyncSlidersFromSettings()
        {
            if (_dragging >= 0) return;
            SetSliderStep(SensSlider, SensitivityToStep(FirstPersonController.MouseSensitivity), true);
            SetSliderStep(MusicSlider, Mathf.RoundToInt(MusicVolume * SliderSteps), true);
            SetSliderStep(SfxSlider, Mathf.RoundToInt(SfxVolume * SliderSteps), true);
        }

        void SetSliderStep(int index, int step, bool force)
        {
            SliderUI sl = _sliders[index];
            step = Mathf.Clamp(step, 0, SliderSteps);
            if (!force && step == sl.Step) return;
            sl.Step = step;
            float t = step / (float)SliderSteps;

            var a = sl.Fill.anchorMax;
            a.x = t;
            sl.Fill.anchorMax = a;
            var h = sl.Handle.anchorMin;
            h.x = t;
            sl.Handle.anchorMin = h;
            h = sl.Handle.anchorMax;
            h.x = t;
            sl.Handle.anchorMax = h;

            sl.Value.text = index == SensSlider ? _sensLabels[step] : step + "%";
            // Volumes apply live while dragging (cheap; persisted on release).
            if (!force && index == MusicSlider) { s_Music = t; RaiseVolumes(); }
            if (!force && index == SfxSlider) { s_Sfx = t; RaiseVolumes(); }
        }

        void CommitSlider(int index)
        {
            float t = _sliders[index].Step / (float)SliderSteps;
            if (index == SensSlider) FirstPersonController.MouseSensitivity = StepToSensitivity(_sliders[index].Step);
            else if (index == MusicSlider) MusicVolume = t;
            else if (index == SfxSlider) SfxVolume = t;
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ refresh

        void RefreshTierButtons(bool force)
        {
            int pref = QualityTier.Preference;
            if (!force && pref == _shownPref) return;
            _shownPref = pref;
            for (int i = 0; i < TierButtonValues.Length; i++)
            {
                bool on = TierButtonValues[i] == pref;
                SetButtonColor(i, on ? UIPalette.Brass : UIUtil.WithAlpha(UIPalette.GraphiteSoft, 0.95f), on ? UIPalette.Graphite : UIPalette.Paper);
            }
        }

        void RefreshStatus()
        {
            int tier = QualityTier.Current;
            int pref = QualityTier.Preference;
            int pct = Mathf.RoundToInt(AdaptiveQuality.RenderScale * 100f);
            bool cal = AdaptiveQuality.Instance != null && AdaptiveQuality.Instance.IsCalibrating;
            if (tier == _statusTier && pref == _statusPref && pct == _statusScalePct && cal == _statusCalibrating) return;
            _statusTier = tier;
            _statusPref = pref;
            _statusScalePct = pct;
            _statusCalibrating = cal;

            _sb.Length = 0;
            if (pref == QualityTier.Auto) _sb.Append(cal ? "Auto (measuring)  " : "Auto  ");
            _sb.Append(QualityTier.Name(tier)).Append("  ").Append(pct).Append('%');
            _status.text = _sb.ToString(); // only on change
        }

        void UpdateFps(float dt)
        {
            _fpsAccum += dt;
            _fpsFrames++;
            _fpsTimer -= dt;
            if (_fpsTimer > 0f) return;
            _fpsTimer = FpsRefreshSeconds;
            if (_fpsFrames == 0 || _fpsAccum <= 0f) return;

            float avgMs = _fpsAccum * 1000f / _fpsFrames;
            int fps = Mathf.RoundToInt(_fpsFrames / _fpsAccum);
            _fpsAccum = 0f;
            _fpsFrames = 0;

            // Twice a second, not per frame.
            _sb.Length = 0;
            _sb.Append(fps).Append(" fps  ");
            AppendTenths(_sb, avgMs);
            _sb.Append(" ms  ").Append(QualityTier.Name(QualityTier.Current)).Append(' ')
               .Append(Mathf.RoundToInt(AdaptiveQuality.RenderScale * 100f)).Append('%');
            _fpsText.text = _sb.ToString();
        }

        static void AppendTenths(StringBuilder sb, float v)
        {
            int t = Mathf.Clamp(Mathf.RoundToInt(v * 10f), 0, 99999);
            sb.Append(t / 10).Append('.').Append(t % 10);
        }

        // ------------------------------------------------------------------ build

        void Build()
        {
            var root = (RectTransform)transform;

            // FPS readout: top-right. Never blocks the pointer.
            var fpsRt = UIUtil.NewRect("FpsReadout", root);
            UIUtil.Anchor(fpsRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -76f), new Vector2(380f, 26f));
            _fpsRoot = fpsRt.gameObject;
            _fpsText = UIUtil.NewText("Text", fpsRt, "-- fps", 18, UIPalette.Paper, TextAnchor.MiddleRight);
            UIUtil.Stretch(_fpsText.rectTransform);

            // Settings card: bottom-right, clear of the centred click-to-play card.
            const float w = CardW, h = CardH, pad = 22f;
            _card = UIUtil.NewRect("Settings", root);
            UIUtil.Anchor(_card, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-CardMargin, CardMargin), new Vector2(w, h));
            _group = _card.gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false; // no Selectables; input is handled in HandlePointer

            var bg = UIUtil.NewImage("Content", _card, UIUtil.WithAlpha(UIPalette.Graphite, 0.94f), UIUtil.RoundedSprite, true);
            bg.raycastTarget = true; // EventSystem.IsPointerOverGameObject() is true over the card
            UIUtil.Stretch(bg.rectTransform);
            _cardContent = bg.gameObject;
            var c = bg.rectTransform;
            float inner = w - pad * 2f;

            var title = UIUtil.NewText("Title", c, "Settings", 26, UIPalette.Paper, TextAnchor.MiddleLeft, FontStyle.Bold, false);
            TopLeft(title.rectTransform, pad, -16f, inner, 34f);

            _status = UIUtil.NewText("Status", c, "", 17, UIPalette.Ion, TextAnchor.MiddleRight, FontStyle.Normal, false);
            TopLeft(_status.rectTransform, pad, -16f, inner, 34f);

            Label(c, "Quality", pad, -58f, inner);
            const float gap = 8f;
            float bw = (inner - gap * (TierButtonValues.Length - 1)) / TierButtonValues.Length;
            for (int i = 0; i < TierButtonValues.Length; i++)
                MakeButton(i, c, QualityTier.Name(TierButtonValues[i]), pad + i * (bw + gap), -86f, bw, 40f);

            _sliders[SensSlider] = BuildSliderRow(c, "Mouse sensitivity", pad, -138f, inner);
            _sliders[MusicSlider] = BuildSliderRow(c, "Music", pad, -206f, inner);
            _sliders[SfxSlider] = BuildSliderRow(c, "Effects", pad, -274f, inner);

            float half = (inner - gap) * 0.5f;
            MakeButton(RaiseButton, c, "", pad, -350f, half, 44f);
            MakeButton(MotionButton, c, "", pad + half + gap, -350f, half, 44f);
            MakeButton(HeadBobButton, c, "", pad, -402f, half, 44f);
            MakeButton(FpsButton, c, "", pad + half + gap, -402f, half, 44f);
            for (int i = RaiseButton; i < ButtonCount; i++)
                SetButtonColor(i, UIUtil.WithAlpha(UIPalette.GraphiteSoft, 0.95f), UIPalette.Paper);

            var hint = UIUtil.NewText("Hint", c, "Shift raises the photo.  Right mouse works too.", 16, UIUtil.WithAlpha(UIPalette.Paper, 0.6f), TextAnchor.MiddleLeft, FontStyle.Normal, false);
            TopLeft(hint.rectTransform, pad, -458f, inner, 26f);
        }

        static void Label(RectTransform parent, string text, float x, float y, float width)
        {
            var t = UIUtil.NewText(text + "Label", parent, text, 19, UIPalette.Paper, TextAnchor.MiddleLeft, FontStyle.Normal, false);
            TopLeft(t.rectTransform, x, y, width, 26f);
        }

        static void TopLeft(RectTransform rt, float x, float y, float width, float height)
        {
            UIUtil.Anchor(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, height));
        }

        void MakeButton(int index, RectTransform parent, string label, float x, float y, float width, float height)
        {
            var bg = UIUtil.NewImage("Button " + index, parent, UIUtil.WithAlpha(UIPalette.GraphiteSoft, 0.95f), UIUtil.RoundedSprite, true);
            TopLeft(bg.rectTransform, x, y, width, height);
            bg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            bg.rectTransform.anchoredPosition = new Vector2(x + width * 0.5f, y - height * 0.5f);
            var text = UIUtil.NewText("Label", bg.rectTransform, label, 18, UIPalette.Paper, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(text.rectTransform);

            _buttons[index] = bg.rectTransform;
            _buttonBg[index] = bg;
            _buttonText[index] = text;
            _buttonColor[index] = bg.color;
        }

        SliderUI BuildSliderRow(RectTransform parent, string label, float x, float y, float width)
        {
            Label(parent, label, x, y, width);
            var value = UIUtil.NewText(label + "Value", parent, "", 17, UIPalette.Ion, TextAnchor.MiddleRight, FontStyle.Normal, false);
            TopLeft(value.rectTransform, x, y, width, 26f);

            var sl = new SliderUI { Value = value };
            sl.Root = UIUtil.NewRect(label + "Slider", parent);
            TopLeft(sl.Root, x, y - 28f, width, 30f);

            var track = UIUtil.NewImage("Track", sl.Root, UIUtil.WithAlpha(UIPalette.Paper, 0.2f), UIUtil.RoundedSprite, true);
            HorizontalBar(track.rectTransform, HandleSize * 0.5f, 6f);

            var fill = UIUtil.NewImage("Fill", track.rectTransform, UIPalette.Ion, UIUtil.RoundedSprite, true);
            sl.Fill = fill.rectTransform;
            sl.Fill.anchorMin = Vector2.zero;
            sl.Fill.anchorMax = new Vector2(0f, 1f);
            sl.Fill.pivot = new Vector2(0f, 0.5f);
            sl.Fill.offsetMin = Vector2.zero;
            sl.Fill.offsetMax = Vector2.zero;

            var handleArea = UIUtil.NewRect("Handle Area", sl.Root);
            UIUtil.Stretch(handleArea);
            handleArea.offsetMin = new Vector2(HandleSize * 0.5f, 0f);
            handleArea.offsetMax = new Vector2(-HandleSize * 0.5f, 0f);

            sl.HandleImage = UIUtil.NewImage("Handle", handleArea, UIPalette.Paper, UIUtil.CircleSprite);
            sl.Handle = sl.HandleImage.rectTransform;
            sl.Handle.anchorMin = new Vector2(0f, 0.5f);
            sl.Handle.anchorMax = new Vector2(0f, 0.5f);
            sl.Handle.pivot = new Vector2(0.5f, 0.5f);
            sl.Handle.sizeDelta = new Vector2(HandleSize, HandleSize);
            sl.Handle.anchoredPosition = Vector2.zero;
            return sl;
        }

        static void HorizontalBar(RectTransform rt, float inset, float height)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, -height * 0.5f);
            rt.offsetMax = new Vector2(-inset, height * 0.5f);
        }
    }
}
