using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels;
using Ion.Presentation.Motion;
using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// The tutorial line (art bible §7.1, §7.2): one short instruction at the top of the screen in the two
    /// tutorial zones, chosen from the live game state (not a script), so it is right whatever order the
    /// player does things in:
    ///  T1 (zone 0): the ledge is out of reach → falling: [R] rewind → find the button → fetch the photo →
    ///  [SHIFT] hold it up → stand on the brass marker, line it up, [LMB] place → up the stairs.
    ///  T2 (zone 1): after a "nothing to rewind": [R] [R] back to the checkpoint → raised: hold [Q] [E] to rotate.
    /// It never blocks other prompts; HintZones stay quiet while it guides (<see cref="IsGuiding"/>).
    /// </summary>
    public sealed class Onboarding : MonoBehaviour
    {
        public static Onboarding Instance { get; private set; }

        public const int LedgeZone = 0, DarkroomZone = 1;

        static readonly string L_Ledge = "The ledge is out of reach. Look around";
        static readonly string L_Fall = UIUtil.Key("R") + "  rewind. It works anytime, anywhere";
        static readonly string L_Button = "That bridge won't hold. Find another way across.   " + UIUtil.Key("E") + " presses buttons";
        static readonly string L_Fetch = "Fetch the photo on the island";
        static readonly string L_Raise = UIUtil.Key("SHIFT") + "  hold up the photo";
        static readonly string L_Align = "Stand on the brass " + UIUtil.Key(" ") + " marker, line it up, then " + UIUtil.Key("LMB") + " place";
        static readonly string L_Placed = "Up you go.   " + UIUtil.Key("R") + " undoes anything, anytime";
        static readonly string L_Double = UIUtil.Key("R") + " " + UIUtil.Key("R") + "  quickly: back to the last checkpoint";
        static readonly string L_DoubleDone = "Checkpoints are set as you enter each place";
        static readonly string L_Rotate = "hold " + UIUtil.Key("Q") + " " + UIUtil.Key("E") + "  to rotate the photo";

        RectTransform _pill;
        CanvasGroup _group;
        Text _text;

        string _shown;           // text currently displayed
        string _pending;         // text to display after the fade-out
        float _fade;             // 0..1 alpha driver
        float _rise;
        bool _swapping;

        // Progress (from gameplay events).
        bool _fallRecovered, _pressedSwitch, _placed, _nothingInT2, _checkpointRestored, _rotated;
        float _doneTimer = -1f;
        string _doneLine;
        WorldHistory _history;
        PhotoHolder _holder;
        float _lastRoll;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnEnable() => GameBootstrap.Restarted += ResetTutorial;

        void OnDisable() => GameBootstrap.Restarted -= ResetTutorial;

        void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        /// <summary>Starts the tutorial over (game restart).</summary>
        public void ResetTutorial()
        {
            _shown = _pending = null;
            _fade = 0f;
            _swapping = false;
            _fallRecovered = _pressedSwitch = _placed = _nothingInT2 = _checkpointRestored = _rotated = false;
            _doneTimer = -1f;
            _doneLine = null;
            _group.alpha = 0f;
        }

        /// <summary>
        /// True while a tutorial line is guiding (zones 0 and 1, not finished): HintZones and room title toasts
        /// stay quiet so the line is the only instruction.
        /// </summary>
        public static bool IsGuiding => Instance != null && Instance.isActiveAndEnabled && Instance._shown != null && Instance._fade > 0.01f;

        /// <summary>Current line (tests, debug); null when hidden.</summary>
        public string Line => _fade > 0.01f ? _shown : null;

        // ---------------------------------------------------------------- events

        void Subscribe()
        {
            var h = WorldHistory.Instance;
            if (h == _history) return;
            Unsubscribe();
            _history = h;
            if (h == null) return;
            h.FallRecovered += OnFallRecovered;
            h.Pushed += OnPushed;
            h.Rewound += OnRewound;
            h.CheckpointRestored += OnCheckpointRestored;
        }

        void Unsubscribe()
        {
            if (_history == null) return;
            _history.FallRecovered -= OnFallRecovered;
            _history.Pushed -= OnPushed;
            _history.Rewound -= OnRewound;
            _history.CheckpointRestored -= OnCheckpointRestored;
            _history = null;
        }

        static int Zone()
        {
            var game = GameBootstrap.Instance;
            if (game != null) return game.CurrentRoom;
            var p = FirstPersonController.Current;
            return p != null ? ZoneInfo.ZoneOf(p.transform.position) : -1;
        }

        void OnFallRecovered()
        {
            if (Zone() == LedgeZone) _fallRecovered = true;
        }

        void OnPushed(WorldChange c)
        {
            if (GameBootstrap.Restarting) return;
            if (Zone() != LedgeZone) return;
            if (c.Kind == ChangeKind.Switch) _pressedSwitch = true;
            if (c.Kind == ChangeKind.Placement && !_placed)
            {
                _placed = true;
                _doneTimer = 5f;
                _doneLine = L_Placed;
            }
        }

        void OnRewound(RewindResult r)
        {
            if (r == RewindResult.Nothing && Zone() == DarkroomZone) _nothingInT2 = true;
        }

        void OnCheckpointRestored(Checkpoint cp)
        {
            if (Zone() != DarkroomZone || _checkpointRestored) return;
            _checkpointRestored = true;
            _doneTimer = 3.5f;
            _doneLine = L_DoubleDone;
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            Subscribe();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            var player = FirstPersonController.Current;
            bool playing = PointerLock.IsLocked || PointerLock.IsSimulated || Application.isBatchMode;
            string line = null;
            if (player != null && player.InputEnabled && playing)
                line = Decide(player, dt);

            if (line != _shown || _swapping)
            {
                if (line != _pending || !_swapping)
                {
                    _pending = line;
                    _swapping = line != _shown;
                }
            }
            Animate(dt);
        }

        string Decide(FirstPersonController player, float dt)
        {
            int zone = Zone();
            if (zone != LedgeZone && zone != DarkroomZone) return null;

            if (_doneTimer > 0f)
            {
                _doneTimer -= dt;
                return _doneLine;
            }

            var tracker = player.GetComponent<SafePoseTracker>();
            if (tracker != null && tracker.IsFalling) return L_Fall;

            if (_holder == null) _holder = player.GetComponent<PhotoHolder>();
            var inventory = player.GetComponent<PhotoInventory>();
            bool raised = _holder != null && _holder.IsRaised;

            if (zone == LedgeZone)
            {
                if (_placed) return null;
                if (raised) return L_Align;
                if (inventory != null && inventory.Count > 0) return L_Raise;
                if (_pressedSwitch) return L_Fetch;
                if (_fallRecovered) return L_Button;
                return L_Ledge;
            }

            // Darkroom.
            if (_nothingInT2 && !_checkpointRestored) return L_Double;
            if (raised && !_rotated)
            {
                if (_holder.RollDegrees != _lastRoll) _rotated = true;
                _lastRoll = _holder.RollDegrees;
                return _rotated ? null : L_Rotate;
            }
            if (_holder != null) _lastRoll = _holder.RollDegrees;
            return null;
        }

        void Animate(float dt)
        {
            float half = Feel.PromptCrossfadeSeconds;
            if (_swapping && (_fade <= 0.001f || _shown == null))
            {
                _shown = _pending;
                _swapping = false;
                if (_shown != null)
                {
                    _text.text = _shown;
                    LayoutPill();
                    _rise = 1f;
                }
            }
            float target = !_swapping && _shown != null ? 1f : 0f;
            float speed = target > _fade ? Feel.PanelInSeconds : half;
            _fade = Mathf.MoveTowards(_fade, target, dt / speed);
            _rise = Mathf.MoveTowards(_rise, 0f, dt / Feel.PanelInSeconds);

            _group.alpha = target > 0f ? Ease.OutCubic(_fade) : Ease.InQuad(_fade);
            _pill.anchoredPosition = new Vector2(0f, -22f + Ease.InQuad(_rise) * Feel.PanelRisePx);
            bool active = _fade > 0.001f;
            if (_pill.gameObject.activeSelf != active) _pill.gameObject.SetActive(active);
        }

        void LayoutPill()
        {
            float textW = _text.preferredWidth;
            _pill.sizeDelta = new Vector2(textW + 56f, 48f);
        }

        void Build()
        {
            var root = (RectTransform)transform;
            UIUtil.Stretch(root);

            var bg = UIUtil.NewImage("Tutorial", root, UIUtil.WithAlpha(UIPalette.Graphite, 0.78f), UIUtil.RoundedSprite, true);
            _pill = bg.rectTransform;
            UIUtil.Anchor(_pill, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(400f, 48f));
            _group = bg.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = 0f;

            // A thin Ion rule on the left: "this is the thing to do".
            var rule = UIUtil.NewImage("Rule", _pill, UIPalette.Ion);
            var rrt = rule.rectTransform;
            rrt.anchorMin = new Vector2(0f, 0.5f);
            rrt.anchorMax = new Vector2(0f, 0.5f);
            rrt.pivot = new Vector2(0f, 0.5f);
            rrt.anchoredPosition = new Vector2(16f, 0f);
            rrt.sizeDelta = new Vector2(3f, 22f);

            _text = UIUtil.NewText("Text", _pill, "", 21, UIPalette.Paper, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UIUtil.Stretch(_text.rectTransform);
            _text.rectTransform.offsetMin = new Vector2(12f, 0f);
            _pill.gameObject.SetActive(false);
        }
    }
}
