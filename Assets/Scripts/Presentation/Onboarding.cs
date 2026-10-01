using Ion.Gameplay;
using Ion.Levels;
using Ion.Projection;
using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// First-room tutorial: one short line at the top of the screen with five progress dots that advances
    /// as the player does each step (pick up → hold RMB → line it up → click → R to undo). Only shown in
    /// room 1 while playing; it finishes for good once the player rewinds, crosses or leaves the room.
    /// </summary>
    public sealed class Onboarding : MonoBehaviour
    {
        public static Onboarding Instance { get; private set; }

        const int Steps = 5;
        const float FadeOut = 0.14f, FadeIn = 0.22f;
        const float AlignDistance = 1.1f, AlignYaw = 10f, AlignPitch = 8f;

        static readonly string[] Lines =
        {
            "Pick up the photo on the pedestal",
            "Hold the right mouse button to raise it",
            "Stand on the marker and line it up with the view",
            "Click to place it",
            "Press R to undo. Rewinding gives the photo back",
        };
        const string DoneRewound = "That's the trick. Place it again and cross";
        const string DoneCrossed = "Nicely done";

        RectTransform _pill;
        CanvasGroup _group;
        Text _text;
        readonly Image[] _dots = new Image[Steps];

        int _step = -1;          // shown step (0..4), 5 = finishing message
        string _shown;           // text currently displayed
        string _pending;         // text to display after the fade-out
        float _fade;             // 0..1 alpha driver
        float _slide;
        bool _swapping;
        bool _placed, _pickedUp, _finished;
        float _finishTimer;
        ProjectionSystem _projection;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnEnable() => GameBootstrap.Restarted += ResetTutorial;

        void OnDisable() => GameBootstrap.Restarted -= ResetTutorial;

        void OnDestroy()
        {
            if (_projection != null)
            {
                _projection.Placed -= OnPlaced;
                _projection.Rewound -= OnRewound;
            }
            if (Instance == this) Instance = null;
        }

        /// <summary>Starts the tutorial over (game restart).</summary>
        public void ResetTutorial()
        {
            _step = -1;
            _shown = _pending = null;
            _fade = 0f;
            _swapping = false;
            _placed = _pickedUp = _finished = false;
            _finishTimer = 0f;
            _group.alpha = 0f;
        }

        /// <summary>
        /// True while the room 1 tutorial is running (not finished): the tutorial line is then the only
        /// instruction on screen, so the HUD's bottom prompt, the marker hint toasts and the room 1 title
        /// toast stay quiet.
        /// </summary>
        public static bool IsGuiding => Instance != null && Instance.isActiveAndEnabled && !Instance._finished && InFirstRoom();

        /// <summary>Current step (0-based) or -1 when hidden / finished (tests, debug).</summary>
        public int Step => _finished && _finishTimer <= 0f ? -1 : _step;

        void OnPlaced()
        {
            if (GameBootstrap.Restarting) return;
            if (InFirstRoom()) _placed = true;
        }

        void OnRewound()
        {
            if (GameBootstrap.Restarting || _finished || !_placed || !InFirstRoom()) return;
            Finish(DoneRewound, 4f);
        }

        static bool InFirstRoom()
        {
            var game = GameBootstrap.Instance;
            return game != null && game.CurrentRoom == 0;
        }

        void Finish(string message, float seconds)
        {
            _finished = true;
            _finishTimer = seconds;
            Show(Steps, message);
        }

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

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            var game = GameBootstrap.Instance;
            var player = FirstPersonController.Current;
            bool playing = Cursor.lockState == CursorLockMode.Locked || Application.isBatchMode;
            bool visible = game != null && player != null && player.InputEnabled && playing;

            if (game != null && game.CurrentRoom != 0 && !_finished)
            {
                // Left room 1 (teleporter / debug): the tutorial is over.
                _finished = true;
                _finishTimer = 0f;
            }

            if (_finished)
            {
                _finishTimer -= dt;
                if (_finishTimer <= 0f) visible = false;
            }
            else if (visible)
            {
                DecideStep(game, player);
            }

            Animate(dt, visible);
        }

        void DecideStep(GameBootstrap game, FirstPersonController player)
        {
            var inventory = player.GetComponent<PhotoInventory>();
            var holder = player.GetComponent<PhotoHolder>();
            if (inventory != null && inventory.Count > 0) _pickedUp = true;

            RoomContext ctx = game.Rooms.Count > 0 ? game.Rooms[0] : null;
            if (_placed)
            {
                // Crossed the chasm without rewinding: wrap up.
                if (ctx != null && ctx.WorldRoot.InverseTransformPoint(player.transform.position).z > 13.5f)
                {
                    Finish(DoneCrossed, 2.5f);
                    return;
                }
                Show(4, Lines[4]);
                return;
            }
            if (!_pickedUp) { Show(0, Lines[0]); return; }
            bool raised = holder != null && holder.IsRaised;
            if (!raised) { Show(1, Lines[1]); return; }
            Show(Aligned(ctx, player) ? 3 : 2, Aligned(ctx, player) ? Lines[3] : Lines[2]);
        }

        static bool Aligned(RoomContext ctx, FirstPersonController player)
        {
            if (ctx == null) return true;
            RoomSolution sol = ctx.Room.FindSolution("place");
            if (sol == null) return true;
            Vector3 d = player.transform.position - ctx.SolutionFeet(sol);
            d.y = 0f;
            return d.magnitude <= AlignDistance &&
                   Mathf.Abs(Mathf.DeltaAngle(player.Yaw, ctx.SolutionYaw(sol))) <= AlignYaw &&
                   Mathf.Abs(player.Pitch - sol.Pitch) <= AlignPitch;
        }

        void Show(int step, string text)
        {
            if (text == _shown && !_swapping) { _step = step; return; }
            if (text == _pending && _swapping) return;
            _pending = text;
            _swapping = true;
            _step = step;
        }

        void Animate(float dt, bool visible)
        {
            if (_swapping && (_fade <= 0.001f || _shown == null))
            {
                _shown = _pending;
                _swapping = false;
                _text.text = _shown;
                _slide = 1f;
                LayoutPill();
            }
            float target = visible && !_swapping && _shown != null ? 1f : 0f;
            _fade = Mathf.MoveTowards(_fade, target, dt / (target > _fade ? FadeIn : FadeOut));
            _slide = Mathf.MoveTowards(_slide, 0f, dt / 0.3f);

            _group.alpha = _fade;
            float e = _slide * _slide;
            _pill.anchoredPosition = new Vector2(0f, -18f + e * 10f);
            bool active = _fade > 0.001f;
            if (_pill.gameObject.activeSelf != active) _pill.gameObject.SetActive(active);
        }

        void LayoutPill()
        {
            for (int i = 0; i < Steps; i++)
            {
                bool done = i < _step || _step >= Steps;
                bool current = i == _step;
                _dots[i].color = current ? Palette.Butter
                    : done ? UIUtil.WithAlpha(Palette.Butter, 0.75f)
                    : UIUtil.WithAlpha(Palette.Cream, 0.3f);
                _dots[i].rectTransform.sizeDelta = current ? new Vector2(11f, 11f) : new Vector2(8f, 8f);
            }
            float dotsW = Steps * 16f;
            float textW = _text.preferredWidth;
            float w = 26f + dotsW + 14f + textW + 28f;
            _pill.sizeDelta = new Vector2(w, 44f);
            for (int i = 0; i < Steps; i++)
                _dots[i].rectTransform.anchoredPosition = new Vector2(26f + i * 16f + 4f, 0f);
            _text.rectTransform.anchoredPosition = new Vector2(26f + dotsW + 14f, 0f);
            _text.rectTransform.sizeDelta = new Vector2(textW + 4f, 44f);
        }

        void Build()
        {
            var root = (RectTransform)transform;
            UIUtil.Stretch(root);

            var bg = UIUtil.NewImage("Tutorial", root, UIUtil.WithAlpha(Palette.Ink, 0.66f), UIUtil.RoundedSprite, true);
            _pill = bg.rectTransform;
            UIUtil.Anchor(_pill, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(400f, 44f));
            _group = bg.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = 0f;

            for (int i = 0; i < Steps; i++)
            {
                var dot = UIUtil.NewImage("Dot" + i, _pill, UIUtil.WithAlpha(Palette.Cream, 0.3f), UIUtil.CircleSprite);
                var rt = dot.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(8f, 8f);
                _dots[i] = dot;
            }

            _text = UIUtil.NewText("Text", _pill, "", 21, Palette.White, TextAnchor.MiddleLeft, FontStyle.Bold, false);
            var trt = _text.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 0.5f);
            trt.pivot = new Vector2(0f, 0.5f);
            _pill.gameObject.SetActive(false);
        }
    }
}
