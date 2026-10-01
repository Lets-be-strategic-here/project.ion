using System;
using Ion.Gameplay;
using Ion.Levels;
using Ion.Presentation.Motion;
using Ion.Projection;
using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// Full-screen UI effects on the main canvas (above the HUD, below the click-to-play overlay), timed by
    /// <see cref="Feel"/> (art bible §9.1). All Images, no post-processing: a few quads for a fraction of a
    /// second.
    ///  * place: a Frost flash 30 % → 0 over 0.35 s (easeOutQuart) at the world swap, and the FOV kick;
    ///  * rewind: a Cyanotype wash 0 → 22 % → 0 with a desaturating Paper veil (sine in-out, 0.6 s);
    ///  * pose moves under a rewind: fade to Paper (0.18 s easeInQuad), hold, fade back (0.30 s easeOutCubic);
    ///  * checkpoint: two crop brackets close from the screen edges, the world is restored behind them, they
    ///    open again (a crossfade with reduced motion);
    ///  * limbo: a Cyanotype vignette closes in (0.8 s easeOutCubic);
    ///  * <see cref="Transition"/> (teleports): fade to Frost (0.40 s easeInQuad) with an FOV swell, the move
    ///    during a 0.15 s hold, fade back (0.55 s easeOutCubic) while the FOV springs home. Never to black.
    /// Rewind effects are driven by <see cref="Ion.Gameplay.State.RewindController"/>, not by ProjectionSystem events.
    /// </summary>
    public sealed class ScreenFx : MonoBehaviour
    {
        public static ScreenFx Instance { get; private set; }

        // Legacy constants (kept for callers); the real timings live in Feel.
        public const float PlaceFlashSeconds = Feel.PlaceFlashSeconds;
        public const float RewindSeconds = Feel.RewindSeconds;
        public const float TransitionIn = Feel.TeleportFadeIn, TransitionOut = Feel.TeleportFadeOut;

        RectTransform _root;

        Image _flash;
        float _flashT = -1f, _flashDuration, _flashPeak;
        Color _flashColor;

        Image _wash, _veil;
        float _washT = -1f, _washDuration, _washAlpha;

        Image _paper;
        float _paperA, _paperT;
        enum PaperState { Off, Delay, In, Held, Hold, Out }
        PaperState _paperState;
        float _paperHold;

        RectTransform _irisL, _irisR;
        Image _irisFillL, _irisFillR;
        float _irisT = -1f;

        Image _vignette;
        float _limboA;
        bool _limbo;
        float _limboT;

        Image _white;
        float _transT = -1f;
        bool _transFired;
        Action _transAction;

        ProjectionSystem _projection;

        /// <summary>True while a fade-to-Frost transition is running.</summary>
        public bool InTransition => _transT >= 0f;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (_projection != null) _projection.Placed -= OnPlaced;
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- public API

        /// <summary>A flash of <paramref name="color"/> at <paramref name="alpha"/> that fades over <paramref name="seconds"/> (easeOutQuart).</summary>
        public void Flash(Color color, float alpha, float seconds)
        {
            _flashColor = color;
            _flashPeak = Mathf.Max(alpha, _flashT >= 0f ? _flash.color.a : 0f);
            _flashDuration = Mathf.Max(0.02f, seconds);
            _flashT = 0f;
            _flash.enabled = true;
        }

        /// <summary>Legacy name for the single-rewind wash.</summary>
        public void PlayRewind() => PlayRewindWash(false);

        /// <summary>The rewind wash: Cyanotype tint + desaturating veil, sine in-out (checkpoint: longer and deeper).</summary>
        public void PlayRewindWash(bool checkpoint)
        {
            _washT = 0f;
            _washDuration = checkpoint ? Feel.CheckpointSeconds : Feel.RewindSeconds;
            _washAlpha = checkpoint ? Feel.CheckpointWashAlpha : Feel.RewindWashAlpha;
            _wash.enabled = true;
            _veil.enabled = true;
        }

        /// <summary>
        /// Starts a fade to Paper after <paramref name="delay"/> s (0.18 s, easeInQuad). It then holds at full
        /// until <see cref="ReleasePaper"/>.
        /// </summary>
        public void FadePaper(float delay)
        {
            _paper.enabled = true;
            _paperT = 0f;
            _paperState = delay > 0f ? PaperState.Delay : PaperState.In;
            _paperHold = delay;
            if (_paperState == PaperState.In) _paperA = Mathf.Max(_paperA, 0f);
        }

        /// <summary>Holds the Paper for <paramref name="hold"/> s more, then fades back (0.30 s, easeOutCubic).</summary>
        public void ReleasePaper(float hold)
        {
            if (_paperState == PaperState.Off) return;
            _paperState = PaperState.Hold;
            _paperT = 0f;
            _paperHold = Mathf.Max(0f, hold);
            _paperA = 1f;
        }

        /// <summary>Drops a Paper fade that is no longer needed (fades out quickly).</summary>
        public void CancelPaper()
        {
            if (_paperState == PaperState.Off) return;
            _paperState = PaperState.Out;
            _paperT = 0f;
        }

        /// <summary>True while the Paper fade is at full (a pose move is invisible now).</summary>
        public bool PaperCovers => _paperState == PaperState.Held || _paperState == PaperState.Hold;

        /// <summary>The checkpoint iris: brackets close (0.35 s), stay through the restore, open (0.45 s).</summary>
        public void PlayCheckpointIris()
        {
            _irisT = 0f;
            // Place the panels fully open (off screen) BEFORE they are shown: they were created closed, and one
            // frame of a full-screen Paper panel with a single "]" flashed at the start of every double rewind.
            if (Feel.ReducedMotion) SetIris(1f, 0f);
            else SetIris(0f, 1f);
            _irisL.gameObject.SetActive(true);
            _irisR.gameObject.SetActive(true);
            PlayRewindWash(true);
        }

        /// <summary>Limbo vignette in (0.8 s easeOutCubic) or out.</summary>
        public void SetLimbo(bool on)
        {
            if (on == _limbo) return;
            _limbo = on;
            _limboT = 0f;
            _vignette.enabled = true;
        }

        /// <summary>
        /// Fades to Frost, runs <paramref name="atPeak"/> at the start of the hold, fades back. If a transition is
        /// already running the action runs at that transition's peak instead (or now, if the peak has passed).
        /// </summary>
        public void Transition(Action atPeak)
        {
            if (_transT >= 0f)
            {
                if (_transFired) atPeak?.Invoke();
                else _transAction += atPeak;
                return;
            }
            _transAction = atPeak;
            _transFired = false;
            _transT = 0f;
            _white.enabled = true;
        }

        /// <summary>Static helper: runs through the transition when the UI exists, immediately otherwise.</summary>
        public static void RunTransition(Action atPeak)
        {
            if (Instance != null && Instance.isActiveAndEnabled) Instance.Transition(atPeak);
            else atPeak?.Invoke();
        }

        // ---------------------------------------------------------------- events

        void OnPlaced()
        {
            if (GameBootstrap.Restarting) return;
            Flash(UIPalette.Frost, Feel.PlaceFlashAlpha, Feel.PlaceFlashSeconds);
            var player = FirstPersonController.Current;
            if (player != null) player.KickFov(Feel.PlaceFovKickDeg, Feel.PlaceFovFreq, Feel.PlaceFovZeta);
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (_projection == null)
            {
                var ps = ProjectionSystem.Instance;
                if (ps != null)
                {
                    _projection = ps;
                    ps.Placed += OnPlaced;
                }
            }

            // Limbo follows the player's state directly (robust to missed events).
            var player = FirstPersonController.Current;
            if (player != null)
            {
                var tracker = player.GetComponent<Ion.Gameplay.State.SafePoseTracker>();
                if (tracker != null) SetLimbo(tracker.InLimbo);
            }

            // Effects run in game time (clamped), so fixed-step tests see the same frames as a player and
            // the rewind effects stay in step with RewindController.
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            UpdateFlash(udt);
            UpdateWash(dt);
            UpdatePaper(dt);
            UpdateIris(dt);
            UpdateLimbo(udt);
            UpdateTransition(dt);
        }

        void UpdateFlash(float dt)
        {
            if (_flashT < 0f) return;
            _flashT += dt;
            float k = _flashT / _flashDuration;
            if (k >= 1f)
            {
                _flashT = -1f;
                _flash.enabled = false;
                return;
            }
            Color c = _flashColor;
            c.a = _flashPeak * (1f - Ease.OutQuart(k));
            _flash.color = c;
        }

        void UpdateWash(float dt)
        {
            if (_washT < 0f) return;
            _washT += dt;
            float k = _washT / _washDuration;
            if (k >= 1f)
            {
                _washT = -1f;
                _wash.enabled = false;
                _veil.enabled = false;
                return;
            }
            // Sine in-out up to the peak and back down.
            float env = Ease.InOutSine(k < 0.5f ? k * 2f : (1f - k) * 2f);
            _wash.color = UIUtil.WithAlpha(UIPalette.Cyanotype, _washAlpha * env);
            // Desaturation approximated by a neutral Paper-grey veil (50 % "desat" ≈ 18 % veil).
            _veil.color = new Color(0.78f, 0.78f, 0.80f, Feel.RewindDesat * 0.36f * env);
        }

        void UpdatePaper(float dt)
        {
            if (_paperState == PaperState.Off) return;
            _paperT += dt;
            switch (_paperState)
            {
                case PaperState.Delay:
                    if (_paperT >= _paperHold) { _paperState = PaperState.In; _paperT = 0f; }
                    break;
                case PaperState.In:
                    _paperA = Mathf.Max(_paperA, Ease.InQuad(_paperT / Feel.RewindMoveFadeIn));
                    if (_paperT >= Feel.RewindMoveFadeIn) { _paperState = PaperState.Held; _paperA = 1f; }
                    break;
                case PaperState.Held:
                    _paperA = 1f;
                    break;
                case PaperState.Hold:
                    _paperA = 1f;
                    if (_paperT >= _paperHold) { _paperState = PaperState.Out; _paperT = 0f; }
                    break;
                case PaperState.Out:
                    _paperA = Mathf.Min(_paperA, 1f - Ease.OutCubic(_paperT / Feel.RewindMoveFadeOut));
                    if (_paperT >= Feel.RewindMoveFadeOut) { _paperState = PaperState.Off; _paperA = 0f; }
                    break;
            }
            _paper.color = UIUtil.WithAlpha(UIPalette.Paper, _paperA);
            _paper.enabled = _paperA > 0.001f || _paperState != PaperState.Off;
        }

        void UpdateIris(float dt)
        {
            if (_irisT < 0f) return;
            _irisT += dt;
            float t = _irisT;
            float closed; // 0 = open (off screen), 1 = closed (meeting at the centre)
            if (Feel.ReducedMotion)
            {
                // Crossfade instead of motion: the panels sit closed and fade.
                const float c = Feel.ReducedMotionCrossfade;
                float a = t < c ? t / c : t < Feel.CheckpointSwapAt ? 1f : 1f - (t - Feel.CheckpointSwapAt) / c;
                SetIris(1f, Mathf.Clamp01(a));
                if (t >= Feel.CheckpointSwapAt + c) EndIris();
                return;
            }
            if (t < Feel.CheckpointCloseSeconds) closed = Ease.InOutCubic(t / Feel.CheckpointCloseSeconds);
            else if (t < Feel.CheckpointSwapAt) closed = 1f;
            else closed = 1f - Ease.OutCubic((t - Feel.CheckpointSwapAt) / Feel.CheckpointOpenSeconds);
            SetIris(closed, 1f);
            if (t >= Feel.CheckpointSwapAt + Feel.CheckpointOpenSeconds) EndIris();
        }

        void EndIris()
        {
            _irisT = -1f;
            _irisL.gameObject.SetActive(false);
            _irisR.gameObject.SetActive(false);
        }

        void SetIris(float closed, float alpha)
        {
            float halfW = _root.rect.width * 0.5f;
            // Each panel is half the screen wide (plus the bracket); closed = 1 puts its edge at the centre.
            float offset = (1f - closed) * (halfW + 80f);
            _irisL.anchoredPosition = new Vector2(-offset, 0f);
            _irisR.anchoredPosition = new Vector2(offset, 0f);
            _irisL.sizeDelta = new Vector2(halfW, 0f);
            _irisR.sizeDelta = new Vector2(halfW, 0f);
            Color fill = UIUtil.WithAlpha(UIPalette.Paper, alpha);
            _irisFillL.color = fill;
            _irisFillR.color = fill;
            SetChildAlpha(_irisL, alpha);
            SetChildAlpha(_irisR, alpha);
        }

        static void SetChildAlpha(RectTransform panel, float alpha)
        {
            for (int i = 0; i < panel.childCount; i++)
            {
                var img = panel.GetChild(i).GetComponent<Image>();
                if (img == null || img.name == "Fill") continue;
                img.color = UIUtil.WithAlpha(UIPalette.Graphite, 0.85f * alpha);
            }
        }

        void UpdateLimbo(float dt)
        {
            if (!_vignette.enabled) return;
            _limboT += dt;
            if (_limbo)
                _limboA = Mathf.Max(_limboA, Ease.OutCubic(_limboT / Feel.LimboInSeconds));
            else
                _limboA = Mathf.Min(_limboA, 1f - Ease.OutCubic(_limboT / 0.4f));
            _vignette.color = UIUtil.WithAlpha(UIPalette.Cyanotype, 0.9f * _limboA);
            if (!_limbo && _limboA <= 0.001f) _vignette.enabled = false;
        }

        void UpdateTransition(float dt)
        {
            if (_transT < 0f) return;
            _transT += dt;
            var player = FirstPersonController.Current;
            float a;
            float fadeIn = Feel.TeleportFadeIn, hold = Feel.TeleportHold, fadeOut = Feel.TeleportFadeOut;
            if (_transT < fadeIn)
            {
                float k = Ease.InQuad(_transT / fadeIn);
                a = k;
                if (player != null) player.SetFovHold(Feel.TeleportFovDeg * k);
            }
            else
            {
                if (!_transFired)
                {
                    _transFired = true;
                    Action act = _transAction;
                    _transAction = null;
                    try { act?.Invoke(); }
                    catch (Exception e) { Debug.LogException(e); }
                    player = FirstPersonController.Current;
                    if (player != null) player.SetFovHold(Feel.TeleportFovDeg);
                }
                if (_transT < fadeIn + hold)
                {
                    a = 1f;
                }
                else
                {
                    if (player != null && _transT - dt < fadeIn + hold)
                        player.ReleaseFovHold(Feel.TeleportFovFreq, Feel.TeleportFovZeta);
                    float k = (_transT - fadeIn - hold) / fadeOut;
                    a = 1f - Ease.OutCubic(k);
                    if (k >= 1f)
                    {
                        _transT = -1f;
                        _white.enabled = false;
                        if (player != null) player.ReleaseFovHold(Feel.TeleportFovFreq, Feel.TeleportFovZeta);
                        return;
                    }
                }
            }
            _white.color = UIUtil.WithAlpha(UIPalette.Frost, a);
        }

        // ---------------------------------------------------------------- construction

        void Build()
        {
            _root = (RectTransform)transform;

            _vignette = UIUtil.NewImage("LimboVignette", _root, UIUtil.WithAlpha(UIPalette.Cyanotype, 0f), VignetteSprite);
            UIUtil.Stretch(_vignette.rectTransform);
            _vignette.enabled = false;

            _veil = UIUtil.NewImage("RewindVeil", _root, new Color(0.78f, 0.78f, 0.8f, 0f));
            UIUtil.Stretch(_veil.rectTransform);
            _veil.enabled = false;

            _wash = UIUtil.NewImage("RewindWash", _root, UIUtil.WithAlpha(UIPalette.Cyanotype, 0f));
            UIUtil.Stretch(_wash.rectTransform);
            _wash.enabled = false;

            _flash = UIUtil.NewImage("Flash", _root, new Color(1f, 1f, 1f, 0f));
            UIUtil.Stretch(_flash.rectTransform);
            _flash.enabled = false;

            _irisL = BuildIrisPanel("IrisLeft", true, out _irisFillL);
            _irisR = BuildIrisPanel("IrisRight", false, out _irisFillR);

            _paper = UIUtil.NewImage("PaperFade", _root, UIUtil.WithAlpha(UIPalette.Paper, 0f));
            UIUtil.Stretch(_paper.rectTransform);
            _paper.enabled = false;

            _white = UIUtil.NewImage("Transition", _root, UIUtil.WithAlpha(UIPalette.Frost, 0f));
            UIUtil.Stretch(_white.rectTransform);
            _white.enabled = false;
        }

        /// <summary>
        /// Half-screen Paper panel whose inner edge carries a crop bracket ("[" on the left panel, "]" on the
        /// right): a spine and two serifs pointing to the centre.
        /// </summary>
        RectTransform BuildIrisPanel(string name, bool left, out Image fill)
        {
            var panel = UIUtil.NewRect(name, _root);
            panel.anchorMin = new Vector2(left ? 0f : 1f, 0f);
            panel.anchorMax = new Vector2(left ? 0f : 1f, 1f);
            panel.pivot = new Vector2(left ? 0f : 1f, 0.5f);
            panel.sizeDelta = new Vector2(960f, 0f);

            fill = UIUtil.NewImage("Fill", panel, UIPalette.Paper);
            UIUtil.Stretch(fill.rectTransform);

            const float spine = 10f, serif = 64f, inset = 120f;
            float edge = left ? 1f : 0f;
            var sp = UIUtil.NewImage("Spine", panel, UIPalette.Graphite);
            sp.rectTransform.anchorMin = new Vector2(edge, 0f);
            sp.rectTransform.anchorMax = new Vector2(edge, 1f);
            sp.rectTransform.pivot = new Vector2(left ? 1f : 0f, 0.5f);
            sp.rectTransform.sizeDelta = new Vector2(spine, -inset * 2f);
            sp.rectTransform.anchoredPosition = new Vector2(left ? -24f : 24f, 0f);
            for (int i = 0; i < 2; i++)
            {
                var s = UIUtil.NewImage("Serif" + i, panel, UIPalette.Graphite);
                var rt = s.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(edge, i == 0 ? 0f : 1f);
                rt.pivot = new Vector2(left ? 1f : 0f, i == 0 ? 0f : 1f);
                rt.sizeDelta = new Vector2(serif, spine);
                // Serifs run from the spine toward the centre.
                rt.anchoredPosition = new Vector2(left ? -24f + serif - spine : 24f - serif + spine, i == 0 ? inset : -inset);
            }
            panel.gameObject.SetActive(false);
            return panel;
        }

        static Sprite s_Vignette;

        /// <summary>Radial vignette (transparent centre, opaque rim), generated once.</summary>
        static Sprite VignetteSprite
        {
            get
            {
                if (s_Vignette != null) return s_Vignette;
                const int n = 128;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
                {
                    name = "Ion_Vignette",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, r));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                s_Vignette = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
                return s_Vignette;
            }
        }
    }
}
