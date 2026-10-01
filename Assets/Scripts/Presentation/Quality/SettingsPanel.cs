using System.Text;
using Ion.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Ion.Presentation.Quality
{
    /// <summary>
    /// Self-installing settings overlay (own Screen Space Overlay canvas, sort order 50, above the HUD,
    /// the viewfinder and the click-to-play overlay). The bottom-right card is shown while the cursor is
    /// unlocked: Quality (Auto / Low / Med / High), mouse sensitivity and an FPS readout toggle.
    /// F3 toggles the small top-right FPS readout at any time (persisted in "ion.showFps").
    ///
    /// Why the card does its own hit-testing instead of using uGUI Button/Slider:
    /// FirstPersonController and ClickToPlayOverlay lock the cursor on ANY left press while it is
    /// unlocked, and the Input System UI module drops pointer presses once the cursor is locked
    /// (so a Button could miss its press, depending on update order). This component runs last in the
    /// frame, reads the raw press position, handles the card itself and undoes that lock when the
    /// press landed on the card. The card background is still a raycast target, so
    /// EventSystem.current.IsPointerOverGameObject() is true over it. A guard on their side is the
    /// cleaner fix. Having no Selectables also means WASD/Space can never drive these controls.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    [DisallowMultipleComponent]
    public sealed class SettingsPanel : MonoBehaviour
    {
        public const int SortingOrder = 50;
        public const string ShowFpsPrefKey = "ion.showFps";

        const int SliderSteps = 100;
        const float HandleSize = 22f;
        const float FpsRefreshSeconds = 0.5f;
        const float StatusRefreshSeconds = 0.25f;
        const int FpsButton = 4; // index in _buttons after the four tier buttons

        static readonly int[] TierButtonValues = { QualityTier.Auto, QualityTier.Low, QualityTier.Medium, QualityTier.High };

        public static SettingsPanel Instance { get; private set; }

        /// <summary>True while the settings card is (becoming) visible.</summary>
        public bool IsVisible => _wantVisible;

        RectTransform _card;
        GameObject _cardContent;
        CanvasGroup _group;
        bool _wantVisible;

        readonly RectTransform[] _buttons = new RectTransform[5];
        readonly Image[] _buttonBg = new Image[5];
        readonly Color[] _buttonColor = new Color[5];
        readonly Text[] _buttonText = new Text[5];
        int _hovered = -1;
        int _shownPref = int.MinValue;

        RectTransform _slider;
        RectTransform _sliderFill;
        RectTransform _sliderHandle;
        Image _sliderHandleImage;
        Text _sensValue;
        string[] _sensLabels;
        int _sensStep;
        bool _dragging;

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

        bool _wasUnlocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

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
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

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
            RefreshTierButtons(true);
            SyncSensitivityFromController();

            _wasUnlocked = Cursor.lockState != CursorLockMode.Locked;
            _wantVisible = _wasUnlocked;
            _group.alpha = _wantVisible ? 1f : 0f;
            _cardContent.SetActive(_wantVisible);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var mouse = Mouse.current;
            var kb = Keyboard.current;

            if (kb != null && kb.f3Key.wasPressedThisFrame) SetShowFps(!_showFps);

            HandlePointer(mouse);

            bool unlocked = Cursor.lockState != CursorLockMode.Locked;
            if (unlocked && !_wantVisible) SyncSensitivityFromController();
            _wantVisible = unlocked;

            // Fade.
            float target = _wantVisible ? 1f : 0f;
            if (!Mathf.Approximately(_group.alpha, target))
                _group.alpha = Mathf.MoveTowards(_group.alpha, target, dt * 8f);
            bool active = _group.alpha > 0.001f;
            if (_cardContent.activeSelf != active) _cardContent.SetActive(active);
            _group.blocksRaycasts = _wantVisible;

            if (active)
            {
                _statusTimer -= dt;
                if (_statusTimer <= 0f)
                {
                    _statusTimer = StatusRefreshSeconds;
                    RefreshTierButtons(false);
                    RefreshStatus();
                }
            }

            if (_showFps) UpdateFps(dt);

            _wasUnlocked = Cursor.lockState != CursorLockMode.Locked;
        }

        // ------------------------------------------------------------------ pointer

        void HandlePointer(Mouse mouse)
        {
            if (mouse == null)
            {
                _dragging = false;
                SetHovered(-1);
                return;
            }

            Vector2 pos = mouse.position.ReadValue();
            bool usable = _wasUnlocked && _group.alpha >= 0.5f;

            if (usable && mouse.leftButton.wasPressedThisFrame && Contains(_card, pos))
            {
                // FirstPersonController / ClickToPlayOverlay (earlier this frame) locked on this press: undo it.
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                for (int i = 0; i < _buttons.Length; i++)
                {
                    if (!Contains(_buttons[i], pos)) continue;
                    if (i == FpsButton) SetShowFps(!_showFps);
                    else OnTierClicked(i);
                    break;
                }

                if (Contains(_slider, pos)) _dragging = true;
            }

            if (_dragging)
            {
                if (mouse.leftButton.isPressed && Cursor.lockState != CursorLockMode.Locked)
                {
                    SetSensitivityStep(ScreenToSliderStep(pos), false);
                }
                else
                {
                    // Write (PlayerPrefs.Save) once on release, not on every drag step.
                    _dragging = false;
                    FirstPersonController.MouseSensitivity = StepToSensitivity(_sensStep);
                    _sliderHandleImage.color = Palette.Cream;
                }
            }

            int hover = -1;
            if (usable && Cursor.lockState != CursorLockMode.Locked && Contains(_card, pos))
            {
                for (int i = 0; i < _buttons.Length; i++)
                {
                    if (Contains(_buttons[i], pos))
                    {
                        hover = i;
                        break;
                    }
                }
            }
            SetHovered(hover);
            if (_dragging) _sliderHandleImage.color = Palette.Butter;
        }

        static bool Contains(RectTransform rt, Vector2 screen)
        {
            // Screen Space Overlay: no camera.
            return rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
        }

        void SetHovered(int index)
        {
            if (index == _hovered) return;
            if (_hovered >= 0) _buttonBg[_hovered].color = _buttonColor[_hovered];
            _hovered = index;
            if (_hovered >= 0)
            {
                Color c = _buttonColor[_hovered];
                _buttonBg[_hovered].color = new Color(c.r * 0.88f, c.g * 0.88f, c.b * 0.88f, c.a);
            }
        }

        void SetButtonColor(int index, Color bg, Color text)
        {
            _buttonColor[index] = bg;
            _buttonBg[index].color = bg;
            _buttonText[index].color = text;
            if (index == _hovered) _hovered = -1; // re-tinted on the next hover check
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

        void ApplyShowFps()
        {
            _fpsRoot.SetActive(_showFps);
            _buttonText[FpsButton].text = _showFps ? "FPS counter (F3):  On" : "FPS counter (F3):  Off";
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

        int ScreenToSliderStep(Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_slider, screen, null, out Vector2 local))
                return _sensStep;
            Rect r = _slider.rect;
            float usable = Mathf.Max(1f, r.width - HandleSize);
            float t = Mathf.Clamp01((local.x - r.xMin - HandleSize * 0.5f) / usable);
            return Mathf.RoundToInt(t * SliderSteps);
        }

        void SyncSensitivityFromController()
        {
            if (_dragging) return;
            SetSensitivityStep(SensitivityToStep(FirstPersonController.MouseSensitivity), true);
        }

        void SetSensitivityStep(int step, bool force)
        {
            step = Mathf.Clamp(step, 0, SliderSteps);
            if (!force && step == _sensStep) return;
            _sensStep = step;
            float t = step / (float)SliderSteps;

            var a = _sliderFill.anchorMax;
            a.x = t;
            _sliderFill.anchorMax = a;

            var h = _sliderHandle.anchorMin;
            h.x = t;
            _sliderHandle.anchorMin = h;
            h = _sliderHandle.anchorMax;
            h.x = t;
            _sliderHandle.anchorMax = h;

            _sensValue.text = _sensLabels[step];
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
                SetButtonColor(i, on ? Palette.Butter : UIUtil.WithAlpha(Palette.Slate, 0.95f), on ? Palette.Ink : Palette.Cream);
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

            // FPS readout: top-right, under the HUD film counter. Never blocks the pointer.
            var fpsRt = UIUtil.NewRect("FpsReadout", root);
            UIUtil.Anchor(fpsRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -76f), new Vector2(380f, 26f));
            _fpsRoot = fpsRt.gameObject;
            _fpsText = UIUtil.NewText("Text", fpsRt, "-- fps", 18, Palette.Cream, TextAnchor.MiddleRight);
            UIUtil.Stretch(_fpsText.rectTransform);

            // Settings card: bottom-right, clear of the centred click-to-play card.
            const float w = 400f, h = 292f, pad = 20f;
            _card = UIUtil.NewRect("Settings", root);
            UIUtil.Anchor(_card, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 28f), new Vector2(w, h));
            _group = _card.gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false; // no Selectables; input is handled in HandlePointer

            var bg = UIUtil.NewImage("Content", _card, UIUtil.WithAlpha(Palette.Ink, 0.92f), UIUtil.RoundedSprite, true);
            bg.raycastTarget = true; // EventSystem.IsPointerOverGameObject() is true over the card
            UIUtil.Stretch(bg.rectTransform);
            _cardContent = bg.gameObject;
            var c = bg.rectTransform;
            float inner = w - pad * 2f;

            var title = UIUtil.NewText("Title", c, "Settings", 26, Palette.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            TopLeft(title.rectTransform, pad, -16f, inner, 34f);

            _status = UIUtil.NewText("Status", c, "", 17, Palette.Sky, TextAnchor.MiddleRight);
            TopLeft(_status.rectTransform, pad, -16f, inner, 34f);

            var qLabel = UIUtil.NewText("QualityLabel", c, "Quality", 19, Palette.Cream, TextAnchor.MiddleLeft);
            TopLeft(qLabel.rectTransform, pad, -58f, inner, 26f);

            const float gap = 8f;
            float bw = (inner - gap * 3f) / 4f;
            for (int i = 0; i < TierButtonValues.Length; i++)
                MakeButton(i, c, QualityTier.Name(TierButtonValues[i]), pad + i * (bw + gap), -88f, bw, 40f);

            var sLabel = UIUtil.NewText("SensitivityLabel", c, "Mouse sensitivity", 19, Palette.Cream, TextAnchor.MiddleLeft);
            TopLeft(sLabel.rectTransform, pad, -144f, inner, 26f);
            _sensValue = UIUtil.NewText("SensitivityValue", c, "", 17, Palette.Sky, TextAnchor.MiddleRight);
            TopLeft(_sensValue.rectTransform, pad, -144f, inner, 26f);

            BuildSlider(c, pad, -172f, inner, 30f);

            MakeButton(FpsButton, c, "", pad, -226f, inner, 44f);
            SetButtonColor(FpsButton, UIUtil.WithAlpha(Palette.Slate, 0.95f), Palette.Cream);
        }

        static void TopLeft(RectTransform rt, float x, float y, float width, float height)
        {
            UIUtil.Anchor(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, height));
        }

        void MakeButton(int index, RectTransform parent, string label, float x, float y, float width, float height)
        {
            var bg = UIUtil.NewImage("Button " + index, parent, UIUtil.WithAlpha(Palette.Slate, 0.95f), UIUtil.RoundedSprite, true);
            TopLeft(bg.rectTransform, x, y, width, height);
            var text = UIUtil.NewText("Label", bg.rectTransform, label, 19, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(text.rectTransform);

            _buttons[index] = bg.rectTransform;
            _buttonBg[index] = bg;
            _buttonText[index] = text;
            _buttonColor[index] = bg.color;
        }

        void BuildSlider(RectTransform parent, float x, float y, float width, float height)
        {
            _slider = UIUtil.NewRect("Slider", parent);
            TopLeft(_slider, x, y, width, height);

            var track = UIUtil.NewImage("Track", _slider, UIUtil.WithAlpha(Palette.Cream, 0.2f), UIUtil.RoundedSprite, true);
            HorizontalBar(track.rectTransform, HandleSize * 0.5f, 8f);

            var fill = UIUtil.NewImage("Fill", track.rectTransform, Palette.Butter, UIUtil.RoundedSprite, true);
            _sliderFill = fill.rectTransform;
            _sliderFill.anchorMin = Vector2.zero;
            _sliderFill.anchorMax = new Vector2(0f, 1f);
            _sliderFill.pivot = new Vector2(0f, 0.5f);
            _sliderFill.offsetMin = Vector2.zero;
            _sliderFill.offsetMax = Vector2.zero;

            var handleArea = UIUtil.NewRect("Handle Area", _slider);
            UIUtil.Stretch(handleArea);
            handleArea.offsetMin = new Vector2(HandleSize * 0.5f, 0f);
            handleArea.offsetMax = new Vector2(-HandleSize * 0.5f, 0f);

            _sliderHandleImage = UIUtil.NewImage("Handle", handleArea, Palette.Cream, UIUtil.CircleSprite);
            _sliderHandle = _sliderHandleImage.rectTransform;
            _sliderHandle.anchorMin = new Vector2(0f, 0.5f);
            _sliderHandle.anchorMax = new Vector2(0f, 0.5f);
            _sliderHandle.pivot = new Vector2(0.5f, 0.5f);
            _sliderHandle.sizeDelta = new Vector2(HandleSize, HandleSize);
            _sliderHandle.anchoredPosition = Vector2.zero;
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
