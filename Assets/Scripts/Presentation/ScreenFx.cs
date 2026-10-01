using System;
using Ion.Gameplay;
using Ion.Levels;
using Ion.Projection;
using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// Full-screen UI effects on the main canvas (above the HUD, below the click-to-play overlay):
    ///  * place: a short warm-white flash (0.12 s) and a small FOV punch,
    ///  * rewind: a 0.3 s "tape rewind" look (a grey wash that drains the colour, plus a few horizontal
    ///    bands that wobble and roll down the screen),
    ///  * <see cref="Transition"/>: fade to white, run an action at the peak, fade back (teleporters).
    /// All Images, no post-processing, so the cost is a few quads for a fraction of a second.
    /// </summary>
    public sealed class ScreenFx : MonoBehaviour
    {
        public static ScreenFx Instance { get; private set; }

        public const float PlaceFlashSeconds = 0.12f;
        public const float RewindSeconds = 0.3f;
        public const float TransitionIn = 0.2f, TransitionOut = 0.25f;

        const int BandCount = 4;

        RectTransform _root;
        Image _flash;
        float _flashT = -1f, _flashDuration, _flashPeak;
        Color _flashColor;

        Image _wash;
        readonly Image[] _bands = new Image[BandCount];
        float _rewindT = -1f;

        Image _white;
        float _transT = -1f;
        bool _transFired;
        Action _transAction;

        ProjectionSystem _projection;

        /// <summary>True while a fade-to-white transition is running.</summary>
        public bool InTransition => _transT >= 0f;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (_projection != null)
            {
                _projection.Placed -= OnPlaced;
                _projection.Rewound -= OnRewound;
            }
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- public API

        /// <summary>A flash of <paramref name="color"/> at <paramref name="alpha"/> that fades over <paramref name="seconds"/>.</summary>
        public void Flash(Color color, float alpha, float seconds)
        {
            _flashColor = color;
            _flashPeak = Mathf.Max(alpha, _flashT >= 0f ? _flash.color.a : 0f);
            _flashDuration = Mathf.Max(0.02f, seconds);
            _flashT = 0f;
            _flash.enabled = true;
        }

        /// <summary>The rewind screen effect (0.3 s).</summary>
        public void PlayRewind()
        {
            _rewindT = 0f;
            _wash.enabled = true;
            for (int i = 0; i < _bands.Length; i++) _bands[i].enabled = true;
        }

        /// <summary>
        /// Fades to white, runs <paramref name="atPeak"/>, fades back. If a transition is already running the
        /// action runs at that transition's peak instead (or now, if the peak has passed).
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
            Flash(new Color(1f, 0.99f, 0.95f), 0.42f, PlaceFlashSeconds); // soft: the frame dissolve carries the moment
            var player = FirstPersonController.Current;
            if (player != null) player.PunchFov(2.2f);
        }

        void OnRewound()
        {
            if (GameBootstrap.Restarting) return;
            PlayRewind();
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
                    ps.Rewound += OnRewound;
                }
            }

            // Effects are UI-time; the transition's action timing follows game time so automated runs
            // with a fixed frame step (tests) see the same number of frames as a player.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            UpdateFlash(dt);
            UpdateRewind(dt);
            UpdateTransition(Mathf.Min(Time.deltaTime, 0.05f));
        }

        void UpdateFlash(float dt)
        {
            if (_flashT < 0f) return;
            _flashT += dt;
            float k = 1f - _flashT / _flashDuration;
            if (k <= 0f)
            {
                _flashT = -1f;
                _flash.enabled = false;
                return;
            }
            Color c = _flashColor;
            c.a = _flashPeak * k * k;
            _flash.color = c;
        }

        void UpdateRewind(float dt)
        {
            if (_rewindT < 0f) return;
            _rewindT += dt;
            float t = _rewindT / RewindSeconds;
            if (t >= 1f)
            {
                _rewindT = -1f;
                _wash.enabled = false;
                for (int i = 0; i < _bands.Length; i++) _bands[i].enabled = false;
                return;
            }

            // Quick in, smooth out.
            float env = t < 0.15f ? t / 0.15f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.85f);
            _wash.color = new Color(0.62f, 0.62f, 0.66f, 0.42f * env);

            Rect r = _root.rect;
            float h = r.height;
            for (int i = 0; i < _bands.Length; i++)
            {
                RectTransform b = _bands[i].rectTransform;
                // Bands roll downward (tape running backwards) and wobble sideways.
                float phase = Mathf.Repeat(t * 1.6f + i / (float)_bands.Length, 1f);
                float y = h * 0.5f - phase * h;
                float wobble = Mathf.Sin((t * 38f) + i * 1.9f) * 14f;
                b.anchoredPosition = new Vector2(wobble, y);
                float thickness = (i % 2 == 0 ? 18f : 7f) + 10f * Mathf.Sin(t * 20f + i);
                b.sizeDelta = new Vector2(r.width + 60f, Mathf.Max(3f, thickness));
                _bands[i].color = new Color(1f, 1f, 1f, (i % 2 == 0 ? 0.16f : 0.26f) * env);
            }
            // The whole wash shivers a little, like a tracking error.
            _wash.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(t * 50f) * 6f * env, 0f);
        }

        void UpdateTransition(float dt)
        {
            if (_transT < 0f) return;
            _transT += dt;
            float a;
            if (_transT < TransitionIn)
            {
                a = Mathf.SmoothStep(0f, 1f, _transT / TransitionIn);
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
                }
                float k = (_transT - TransitionIn) / TransitionOut;
                a = 1f - Mathf.SmoothStep(0f, 1f, k);
                if (k >= 1f)
                {
                    _transT = -1f;
                    _white.enabled = false;
                    return;
                }
            }
            _white.color = new Color(1f, 0.985f, 0.95f, a);
        }

        // ---------------------------------------------------------------- construction

        void Build()
        {
            _root = (RectTransform)transform;

            _wash = UIUtil.NewImage("RewindWash", _root, new Color(0.6f, 0.6f, 0.65f, 0f));
            UIUtil.Stretch(_wash.rectTransform);
            _wash.rectTransform.offsetMin = new Vector2(-20f, 0f);
            _wash.rectTransform.offsetMax = new Vector2(20f, 0f);
            _wash.enabled = false;

            for (int i = 0; i < _bands.Length; i++)
            {
                var band = UIUtil.NewImage("RewindBand" + i, _root, new Color(1f, 1f, 1f, 0f));
                var rt = band.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                band.enabled = false;
                _bands[i] = band;
            }

            _flash = UIUtil.NewImage("Flash", _root, new Color(1f, 1f, 1f, 0f));
            UIUtil.Stretch(_flash.rectTransform);
            _flash.enabled = false;

            _white = UIUtil.NewImage("Transition", _root, new Color(1f, 1f, 1f, 0f));
            UIUtil.Stretch(_white.rectTransform);
            _white.enabled = false;
        }
    }
}
