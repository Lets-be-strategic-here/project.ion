using System.Collections;
using Ion.Gameplay;
using Ion.Projection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Presentation.Audio
{
    /// <summary>
    /// Self-installing procedural sound: every clip is synthesised at startup by <see cref="AudioSynth"/>
    /// (22050 Hz mono, one clip, or half a second of wind, per frame) - there are no audio assets.
    ///
    /// Hooks (read-only, nothing in gameplay code is changed):
    /// - ProjectionSystem.Placed / Rewound            -> whoosh + chime / reverse whoosh
    /// - PhotoHolder.RaisedChanged                    -> paper rustle (raise, softer on lower)
    /// - InstantCamera.Captured                       -> shutter click + film whirr
    /// - PhotoInventory.Changed (count goes up)       -> pickup chime (not for rewinds / own snapshots)
    /// - FirstPersonController grounded + transform   -> footsteps by distance walked, landing thud
    /// - player jumps more than a few metres in a frame (room teleport / respawn) -> shimmer
    /// Ambience: one looping wind source and occasional positional bird chirps (Medium/High only).
    ///
    /// Master volume / mute are global (AudioListener.volume), persisted, and M toggles mute.
    /// Cost knobs: <see cref="QualityTier"/> picks the one-shot voice count and whether birds play;
    /// Low also uses a shorter wind loop. The web player only starts audio after the first click,
    /// which the click-to-play overlay provides.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class ProceduralAudio : MonoBehaviour
    {
        public const string MasterVolumePrefKey = "ion.masterVolume";
        public const string MutedPrefKey = "ion.muted";

        // Mix (kept gentle; the wind sits well under everything).
        const float WindVolume = 0.075f;
        const float BirdVolume = 0.07f;
        const float RustleVolume = 0.28f;
        const float LowerRustleVolume = 0.12f;
        const float ShutterVolume = 0.42f;
        const float PlaceVolume = 0.42f;
        const float RewindVolume = 0.38f;
        const float PickupVolume = 0.34f;
        const float StepVolume = 0.16f;
        const float TeleportVolume = 0.32f;

        const int FootstepVariants = 4;
        const int BirdVariants = 4;
        const int MaxVoices = 8;
        const float TeleportJump = 4f;      // horizontal metres in one frame => teleport
        const float StartupGrace = 1.5f;    // no pickup/teleport sounds while the level builds

        public static ProceduralAudio Instance { get; private set; }

        static float s_Master = -1f;
        static int s_Muted = -1;

        /// <summary>Master volume 0..1 (persisted).</summary>
        public static float MasterVolume
        {
            get
            {
                if (s_Master < 0f) s_Master = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePrefKey, 1f));
                return s_Master;
            }
            set
            {
                s_Master = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(MasterVolumePrefKey, s_Master);
                PlayerPrefs.Save();
                ApplyListenerVolume();
            }
        }

        /// <summary>Global mute (persisted). Toggled with M.</summary>
        public static bool Muted
        {
            get
            {
                if (s_Muted < 0) s_Muted = PlayerPrefs.GetInt(MutedPrefKey, 0) != 0 ? 1 : 0;
                return s_Muted == 1;
            }
            set
            {
                s_Muted = value ? 1 : 0;
                PlayerPrefs.SetInt(MutedPrefKey, s_Muted);
                PlayerPrefs.Save();
                ApplyListenerVolume();
            }
        }

        static void ApplyListenerVolume() => AudioListener.volume = Muted ? 0f : MasterVolume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            s_Master = -1f;
            s_Muted = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Instance != null) return;
            new GameObject("ProceduralAudio").AddComponent<ProceduralAudio>();
        }

        // Clips.
        AudioClip _wind, _rustle, _shutter, _place, _rewind, _pickup, _teleport;
        readonly AudioClip[] _steps = new AudioClip[FootstepVariants];
        readonly AudioClip[] _birds = new AudioClip[BirdVariants];
        bool _ready;

        // Sources.
        AudioSource _windSource;
        AudioSource _birdSource;
        readonly AudioSource[] _voices = new AudioSource[MaxVoices];
        int _voiceCount;
        int _nextVoice;

        // Quality.
        bool _birdsEnabled = true;
        int _tier = -1;

        // Bindings.
        FirstPersonController _fpc;
        PhotoInventory _inventory;
        PhotoHolder _holder;
        InstantCamera _camera;
        ProjectionSystem _projection;
        int _lastCount;

        // Event bookkeeping (frames, so same-frame events can suppress each other).
        int _rewoundFrame = -10, _placedFrame = -10, _capturedFrame = -10;
        int _pickupFrame = -1, _lowerFrame = -1;

        // Movement.
        Vector3 _lastPos;
        bool _hasLastPos;
        float _stepDistance;
        float _lastGroundedTime;
        float _airMinVy;
        bool _wasGrounded = true;
        int _lastStepVariant = -1;

        float _nextBirdTime;
        float _windFade;
        bool _windStarted;
        float _nextListenerCheck;
        float _startTime;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _startTime = Time.time;
            ApplyListenerVolume();

            _windSource = NewSource("Wind", 0f);
            _windSource.loop = true;
            _windSource.priority = 64;

            _birdSource = NewSource("Birds", 1f);
            _birdSource.rolloffMode = AudioRolloffMode.Linear;
            _birdSource.minDistance = 8f;
            _birdSource.maxDistance = 70f;
            _birdSource.dopplerLevel = 0f;
            _birdSource.priority = 200;

            ApplyQuality(CurrentTier());
            QualityTier.Changed += OnQualityChanged;
            _nextBirdTime = Time.time + 6f;

            StartCoroutine(Generate());
        }

        void OnDestroy()
        {
            QualityTier.Changed -= OnQualityChanged;
            UnbindPlayer();
            UnbindProjection();
            if (Instance == this) Instance = null;
        }

        AudioSource NewSource(string name, float spatialBlend)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = spatialBlend;
            src.dopplerLevel = 0f;
            return src;
        }

        // ------------------------------------------------------------------ quality

        static int CurrentTier() => Mathf.Clamp(QualityTier.Current, 0, 2);

        void OnQualityChanged() => ApplyQuality(CurrentTier());

        void ApplyQuality(int tier)
        {
            if (tier == _tier) return;
            _tier = tier;
            int voices = tier == 0 ? 4 : tier == 1 ? 6 : MaxVoices;
            // Grow the pool on demand; when shrinking, extra voices just stop being used.
            for (int i = 0; i < voices; i++)
                if (_voices[i] == null) _voices[i] = NewSource("Voice " + i, 0f);
            for (int i = voices; i < MaxVoices; i++)
                if (_voices[i] != null) _voices[i].Stop();
            _voiceCount = voices;
            if (_nextVoice >= _voiceCount) _nextVoice = 0;

            _birdsEnabled = tier > 0;
            if (!_birdsEnabled && _birdSource != null) _birdSource.Stop();
        }

        // ------------------------------------------------------------------ generation

        static AudioClip MakeClip(string name, float[] data)
        {
            // stream must be false on the web; SetData on a Create()d PCM clip is fine.
            var clip = AudioClip.Create(name, data.Length, 1, AudioSynth.SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        IEnumerator Generate()
        {
            // One clip per frame so startup never hitches; the important cues first.
            for (int i = 0; i < FootstepVariants; i++)
            {
                _steps[i] = MakeClip("Ion_Step" + i, AudioSynth.Footstep(101u + (uint)i * 7919u));
            }
            yield return null;
            _rustle = MakeClip("Ion_Rustle", AudioSynth.PaperRustle(23u));
            yield return null;
            _place = MakeClip("Ion_Place", AudioSynth.PlaceWhooshChime(31u));
            yield return null;
            _rewind = MakeClip("Ion_Rewind", AudioSynth.RewindReverse(37u));
            yield return null;
            _pickup = MakeClip("Ion_Pickup", AudioSynth.PickupChime());
            yield return null;
            _shutter = MakeClip("Ion_Shutter", AudioSynth.ShutterWhirr(41u));
            yield return null;
            _teleport = MakeClip("Ion_Teleport", AudioSynth.TeleportShimmer(43u));
            yield return null;
            // Wind is the biggest clip: ~0.5 s of audio per frame.
            float windSeconds = CurrentTier() == 0 ? 6f : 10f;
            var wind = new AudioSynth.WindBuilder(windSeconds, 11u);
            while (!wind.Step(AudioSynth.SampleRate / 2)) yield return null;
            _wind = MakeClip("Ion_Wind", wind.Result);
            _windSource.clip = _wind;
            yield return null;
            for (int i = 0; i < BirdVariants; i++)
            {
                _birds[i] = MakeClip("Ion_Bird" + i, AudioSynth.BirdChirp(211u + (uint)i * 104729u));
                yield return null;
            }
            _ready = true;
        }

        // ------------------------------------------------------------------ playback

        AudioSource Play(AudioClip clip, float volume, float pitch = 1f)
        {
            if (clip == null || _voiceCount == 0) return null;
            // Prefer a free voice; otherwise steal round-robin.
            AudioSource src = null;
            for (int k = 0; k < _voiceCount; k++)
            {
                int i = (_nextVoice + k) % _voiceCount;
                if (!_voices[i].isPlaying) { src = _voices[i]; _nextVoice = (i + 1) % _voiceCount; break; }
            }
            if (src == null)
            {
                src = _voices[_nextVoice];
                _nextVoice = (_nextVoice + 1) % _voiceCount;
            }
            src.Stop();
            src.clip = clip;
            src.volume = volume;
            src.pitch = Mathf.Max(0.05f, pitch); // the web only supports positive pitch
            src.Play();
            return src;
        }

        static float Jitter(float amount) => 1f + Random.Range(-amount, amount);

        // ------------------------------------------------------------------ binding

        void BindPlayer(FirstPersonController fpc)
        {
            UnbindPlayer();
            _fpc = fpc;
            _hasLastPos = false;
            if (fpc == null) return;

            _inventory = fpc.GetComponent<PhotoInventory>();
            _holder = fpc.GetComponent<PhotoHolder>();
            _camera = fpc.GetComponent<InstantCamera>();
            if (_inventory != null)
            {
                _inventory.Changed += OnInventoryChanged;
                _lastCount = _inventory.Count;
            }
            if (_holder != null) _holder.RaisedChanged += OnRaisedChanged;
            if (_camera != null) _camera.Captured += OnCaptured;
        }

        void UnbindPlayer()
        {
            if (!ReferenceEquals(_inventory, null)) _inventory.Changed -= OnInventoryChanged;
            if (!ReferenceEquals(_holder, null)) _holder.RaisedChanged -= OnRaisedChanged;
            if (!ReferenceEquals(_camera, null)) _camera.Captured -= OnCaptured;
            _inventory = null;
            _holder = null;
            _camera = null;
            _fpc = null;
        }

        void BindProjection(ProjectionSystem ps)
        {
            UnbindProjection();
            if (ps == null) return;
            ps.Placed += OnPlaced;
            ps.Rewound += OnRewound;
            _projection = ps;
        }

        void UnbindProjection()
        {
            if (ReferenceEquals(_projection, null)) return;
            _projection.Placed -= OnPlaced;
            _projection.Rewound -= OnRewound;
            _projection = null;
        }

        void EnsureListener()
        {
            if (Time.unscaledTime < _nextListenerCheck) return;
            _nextListenerCheck = Time.unscaledTime + 2f;
            if (Object.FindFirstObjectByType<AudioListener>() != null) return;
            var cam = Camera.main;
            if (cam != null) cam.gameObject.AddComponent<AudioListener>();
        }

        // ------------------------------------------------------------------ events

        void OnPlaced()
        {
            _placedFrame = Time.frameCount;
            Play(_place, PlaceVolume, Jitter(0.03f));
        }

        void OnRewound()
        {
            _rewoundFrame = Time.frameCount;
            Play(_rewind, RewindVolume, Jitter(0.03f));
        }

        void OnCaptured(PhotoData photo)
        {
            _capturedFrame = Time.frameCount;
            Play(_shutter, ShutterVolume, Jitter(0.02f));
        }

        void OnRaisedChanged()
        {
            if (_holder == null) return;
            if (_holder.IsRaised) Play(_rustle, RustleVolume, Jitter(0.08f));
            else _lowerFrame = Time.frameCount; // decided in LateUpdate (placing/rewinding also lowers)
        }

        void OnInventoryChanged()
        {
            if (_inventory == null) return;
            int count = _inventory.Count;
            if (count > _lastCount) _pickupFrame = Time.frameCount; // decided in LateUpdate
            _lastCount = count;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.mKey.wasPressedThisFrame)
            {
                Muted = !Muted;
                GameplayUI.Toast(Muted ? "Sound off (M)" : "Sound on (M)");
            }

            var fpc = FirstPersonController.Current;
            if (!ReferenceEquals(fpc, _fpc)) BindPlayer(fpc);
            if (fpc == null) return;

            var ps = ProjectionSystem.Instance;
            if (!ReferenceEquals(ps, _projection)) BindProjection(ps);

            EnsureListener();
            UpdateWind(fpc);
            UpdateMovement(fpc);
            UpdateBirds(fpc);
        }

        void LateUpdate()
        {
            int frame = Time.frameCount;
            if (_lowerFrame >= 0 && frame - _lowerFrame >= 1)
            {
                // Lowering was neither a placement nor a rewind.
                bool consumed = Mathf.Abs(_lowerFrame - _placedFrame) <= 1 ||
                                Mathf.Abs(_lowerFrame - _rewoundFrame) <= 1;
                if (!consumed) Play(_rustle, LowerRustleVolume, Jitter(0.06f) * 1.15f);
                _lowerFrame = -1;
            }
            if (_pickupFrame >= 0 && frame - _pickupFrame >= 1)
            {
                bool fromRewind = Mathf.Abs(_pickupFrame - _rewoundFrame) <= 1;
                bool fromSnapshot = Mathf.Abs(_pickupFrame - _capturedFrame) <= 1;
                if (!fromRewind && !fromSnapshot && Time.time - _startTime > StartupGrace)
                    Play(_pickup, PickupVolume, Jitter(0.02f));
                _pickupFrame = -1;
            }
        }

        void UpdateWind(FirstPersonController fpc)
        {
            if (_wind == null) return;
            if (!_windStarted)
            {
                // Started once (isPlaying is unreliable on the web before the first user gesture).
                // Random start so a reload doesn't always open on the same gust.
                _windStarted = true;
                _windSource.volume = 0f;
                _windFade = 0f;
                _windSource.Play();
                _windSource.timeSamples = Random.Range(0, _wind.samples);
            }
            _windFade = Mathf.MoveTowards(_windFade, 1f, Time.unscaledDeltaTime / 4f);
            // A touch windier higher up.
            float height = Mathf.Clamp01((fpc.transform.position.y + 5f) / 30f);
            _windSource.volume = WindVolume * _windFade * (0.85f + 0.4f * height);
        }

        void UpdateMovement(FirstPersonController fpc)
        {
            Vector3 pos = fpc.transform.position;
            if (!_hasLastPos)
            {
                _lastPos = pos;
                _hasLastPos = true;
                _wasGrounded = fpc.IsGrounded;
                _stepDistance = 0f;
                return;
            }

            Vector3 delta = pos - _lastPos;
            _lastPos = pos;
            float horiz = new Vector2(delta.x, delta.z).magnitude;

            // Teleport / respawn: a jump no walking could produce.
            if (horiz > TeleportJump || Mathf.Abs(delta.y) > 25f)
            {
                if (Time.time - _startTime > StartupGrace)
                    Play(_teleport, TeleportVolume, Jitter(0.03f));
                _stepDistance = 0f;
                _wasGrounded = fpc.IsGrounded;
                _airMinVy = 0f;
                return;
            }

            bool grounded = fpc.IsGrounded;
            float now = Time.time;
            if (grounded) _lastGroundedTime = now;
            bool groundedish = now - _lastGroundedTime < 0.1f; // isGrounded flickers on steps

            if (!grounded) _airMinVy = Mathf.Min(_airMinVy, fpc.Velocity.y);
            else if (!_wasGrounded)
            {
                // Landing thud scaled by impact speed (skip tiny step-offs).
                float impact = -_airMinVy;
                if (impact > 5f)
                    PlayStep(Mathf.Clamp01(impact / 14f) * 1.6f + 0.6f, 0.82f);
                _airMinVy = 0f;
                _stepDistance = 0f;
            }
            _wasGrounded = grounded;

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float speed = horiz / dt;
            if (!groundedish || speed < 0.4f)
            {
                // Standing still: the next step lands soon after starting to move.
                if (groundedish) _stepDistance = Mathf.Min(_stepDistance, 0.9f);
                return;
            }

            float stride = 1.6f + 0.12f * speed; // ~2 steps/s walking, ~2.8 sprinting
            _stepDistance += horiz;
            if (_stepDistance >= stride)
            {
                _stepDistance -= stride;
                PlayStep(Mathf.Lerp(0.7f, 1.1f, Mathf.InverseLerp(2f, 7f, speed)), 1f);
            }
        }

        void PlayStep(float volumeScale, float pitchScale)
        {
            // Never repeat the same variant twice in a row.
            int v = Random.Range(0, FootstepVariants);
            if (v == _lastStepVariant) v = (v + 1) % FootstepVariants;
            _lastStepVariant = v;
            Play(_steps[v], StepVolume * volumeScale, Jitter(0.08f) * pitchScale);
        }

        void UpdateBirds(FirstPersonController fpc)
        {
            if (!_ready || !_birdsEnabled) return;
            float now = Time.time;
            if (now < _nextBirdTime) return;
            _nextBirdTime = now + Random.Range(6f, 16f);
            if (_birdSource.isPlaying) return;

            // Somewhere off to the side and a little above the player (birds are positional; the
            // web player has no stereo pan).
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(12f, 30f);
            Vector3 p = fpc.transform.position + new Vector3(Mathf.Cos(ang) * dist, Random.Range(4f, 12f), Mathf.Sin(ang) * dist);
            _birdSource.transform.position = p;
            _birdSource.clip = _birds[Random.Range(0, BirdVariants)];
            _birdSource.volume = BirdVolume * Random.Range(0.7f, 1.1f);
            _birdSource.pitch = Random.Range(0.9f, 1.12f);
            _birdSource.Play();

            // Sometimes a quick answer from another bird.
            if (Random.value < 0.35f) _nextBirdTime = now + Random.Range(0.8f, 1.6f);
        }
    }
}
