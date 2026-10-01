using Ion.Gameplay;
using UnityEngine;

namespace Ion.Levels.Props
{
    /// <summary>Pedestal heights (art bible §5): top at 0.5 / 1.0 / 1.25.</summary>
    public enum PedestalSize { Low, Standard, Tall }

    public enum ChairStyle { Cafe, Monobloc, Stool }

    public enum LampStyle { Pendant, Standing, Sconce, Lantern, Bollard }

    public enum PlanterStyle { Trough, Square, Bowl }

    public enum PlantKind { Cypress, Palm, Monstera, Strelitzia, Grass, Ivy, Shrub }

    public enum FrameStyle { SlideMount, Gallery, Polaroid }

    public enum ButtonStyle { Pedestal, Wall }

    public enum ExhibitState { ComingSoon, Locked, Available, Solved }

    /// <summary>What a <see cref="PropKit.PhotoDisplay(Transform, Vector3, Ion.Levels.Arch.Dir, DioramaShot, DisplayMount)"/> stands on (addition).</summary>
    public enum DisplayMount { Pedestal, Easel }

    /// <summary>One hub exhibit (art bible §5, §7.3).</summary>
    public sealed class ExhibitSpec
    {
        /// <summary>"02" gives the plaque "[02]  STAIRS".</summary>
        public string Number;
        /// <summary>"Stairs".</summary>
        public string Title;
        /// <summary>The wing's key photo; null = coming soon.</summary>
        public DioramaShot Key;
        public ExhibitState State = ExhibitState.Available;
    }

    /// <summary>
    /// Exhibit state view: the state lamp (Graphite / Ion / brass glow), the print (desaturated while locked,
    /// colour once available, Ion border once solved), the plaque tick and the key photo's pickup. The state
    /// itself is set by the hub logic (Lead C); this component only shows it.
    /// It sits on its own (non-Interactable) object, so photo copies of the exhibit never carry a second stand.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExhibitStand : MonoBehaviour
    {
        [SerializeField] ExhibitState _state = ExhibitState.Available;
        [SerializeField] PrintCard _print;
        [SerializeField] GameObject _border;
        [SerializeField] Transform _lamp;
        [SerializeField] GameObject _tick;
        [SerializeField] PhotoPickup _pickup;
        [SerializeField] string _number, _title;

        float _lampScale = 1f, _lampVel;
        float _wake = 1f;          // 0 -> 1 over Feel.ExhibitSeconds when the print wakes up
        float _wakeFrom, _wakeTo;  // saturation range of the running wake
        bool _built;

        /// <summary>Current state. Setting it updates the lamp, print, border, tick and pickup (animated).</summary>
        public ExhibitState State
        {
            get => _state;
            set
            {
                if (_built && value == _state) return;
                ExhibitState old = _state;
                _state = value;
                Apply(old, value, !_built);
                Changed?.Invoke(value);
            }
        }

        /// <summary>Raised after <see cref="State"/> changes.</summary>
        public event System.Action<ExhibitState> Changed;

        /// <summary>The key photo's pickup on the exhibit ledge (null for coming-soon stands). Addition.</summary>
        public PhotoPickup Pickup => _pickup;

        /// <summary>The big print in the slide-mount frame. Addition.</summary>
        public PrintCard Print => _print;

        /// <summary>"02". Addition.</summary>
        public string Number => _number;

        /// <summary>"Stairs". Addition.</summary>
        public string Title => _title;

        internal void Init(ExhibitSpec spec, PrintCard print, GameObject border, Transform lamp, GameObject tick, PhotoPickup pickup)
        {
            _number = spec.Number;
            _title = spec.Title;
            _print = print;
            _border = border;
            _lamp = lamp;
            _tick = tick;
            _pickup = pickup;
            _state = spec.Key == null ? ExhibitState.ComingSoon : spec.State;
            Apply(_state, _state, true);
            _built = true;
        }

        void Apply(ExhibitState from, ExhibitState to, bool instant)
        {
            Ion.Presentation.Mat lampMat = to == ExhibitState.Available ? Ion.Presentation.Mat.Ion : Ion.Presentation.Mat.Graphite;
            if (_lamp != null)
            {
                Material m = to == ExhibitState.Solved ? PropBuild.BrassGlow : Ion.Presentation.Palette.Get(lampMat);
                PropBuild.SetMaterial(_lamp.gameObject, m);
                if (!instant && from != to)
                {
                    // Lamp pop: spring from 0.6 back to 1 (f = 3 Hz, zeta = 0.7).
                    _lampScale = 0.6f;
                    _lampVel = 0f;
                }
            }
            if (_border != null) _border.SetActive(to == ExhibitState.Solved);
            if (_tick != null) _tick.SetActive(to == ExhibitState.Solved);

            float sat = to == ExhibitState.Locked ? 0f : PrintCard.DefaultSaturation;
            if (_print != null)
            {
                bool wakes = !instant && from == ExhibitState.Locked && (to == ExhibitState.Available || to == ExhibitState.Solved);
                if (wakes)
                {
                    _wakeFrom = 0f;
                    _wakeTo = sat;
                    _wake = 0f;
                }
                else
                {
                    _wake = 1f;
                    if (to != ExhibitState.ComingSoon) _print.Saturation = sat;
                }
            }

            RefreshPickup();
        }

        /// <summary>
        /// Shows the key pickup while the exhibit is Available or Solved and the photo exists and is not collected;
        /// hides it otherwise. Never resurrects a collected pickup (a rewind resets it through PhotoPickup itself).
        /// </summary>
        internal void RefreshPickup()
        {
            if (_pickup == null) return;
            bool show = _state == ExhibitState.Available || _state == ExhibitState.Solved;
            if (!show) _pickup.gameObject.SetActive(false);
            else if (!_pickup.Collected && _pickup.Photo != null) _pickup.gameObject.SetActive(true);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_lamp != null && (Mathf.Abs(_lampScale - 1f) > 1e-4f || Mathf.Abs(_lampVel) > 1e-3f))
            {
                Ion.Presentation.Motion.Spring.Step(ref _lampScale, ref _lampVel, 1f,
                    Ion.Presentation.Motion.Feel.ExhibitLampFreq, Ion.Presentation.Motion.Feel.ExhibitLampZeta, dt);
                _lamp.localScale = Vector3.one * _lampScale;
            }
            if (_wake < 1f && _print != null)
            {
                _wake = Mathf.Min(1f, _wake + dt / Ion.Presentation.Motion.Feel.ExhibitSeconds);
                _print.Saturation = Mathf.Lerp(_wakeFrom, _wakeTo, Ion.Presentation.Motion.Ease.InOutSine(_wake));
            }
        }
    }
}
