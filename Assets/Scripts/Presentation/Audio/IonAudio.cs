using System;
using System.Collections;
using System.Collections.Generic;
using Ion.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Presentation.Audio
{
    /// <summary>
    /// The game's mixer (art bible §8). Self-installs once per play session and owns every AudioSource:
    /// <list type="bullet">
    /// <item>a voice pool for one-shots (<see cref="Play(Sfx, Vector3?)"/>), with a per-sound retrigger guard
    /// so an event subscription and a direct call can never double up;</item>
    /// <item>fading loops with handles (<see cref="StartLoop"/>): movers, teleporter hum, wind, limbo drone;</item>
    /// <item>two music decks with equal-power crossfades per area (tutorial / hub / puzzle / ending), ducking,
    /// the rewind tape-dip and the limbo muffle (IonAudio.Music.cs);</item>
    /// <item>footsteps per ground material and landings (IonAudio.Footsteps.cs);</item>
    /// <item>wind, birds and the nearest teleporter's hum (IonAudio.Ambience.cs);</item>
    /// <item>gameplay event bindings (IonAudio.Bindings.cs): the only file that knows gameplay types.</item>
    /// </list>
    /// Buses: master = AudioListener.volume, music and SFX gains multiply into their sources
    /// (<see cref="AudioSettings"/>). WebGL notes: no AudioMixer, no DSP filters, pitch must stay positive,
    /// spatialBlend is 0 or 1; isPlaying is unreliable before the first click, so voices track their own
    /// end times.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed partial class IonAudio : MonoBehaviour
    {
        const int MaxVoices = 16;
        const float StartupGrace = 1.5f;
        const float SettingsFlushDelay = 0.75f;

        public static IonAudio Instance { get; private set; }

        /// <summary>Set false before the first scene loads to keep IonAudio from installing (tests).</summary>
        public static bool AutoInstall = true;

        /// <summary>Raised for every accepted <see cref="Play(Sfx, Vector3?)"/> (debug overlay, tests).</summary>
        public static event Action<Sfx, Vector3?> Played;

        static readonly Dictionary<Sfx, int> s_PlayCounts = new Dictionary<Sfx, int>();
        static readonly Dictionary<Sfx, float> s_LastPlay = new Dictionary<Sfx, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            AutoInstall = true;
            Played = null;
            s_PlayCounts.Clear();
            s_LastPlay.Clear();
            ForcedArea = null;
            AreaResolver = null;
            SurfaceResolver = null;
            AreaChanged = null;
            s_NextLoopId = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!AutoInstall || Instance != null) return;
            new GameObject("IonAudio").AddComponent<IonAudio>();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Plays a sound. With <paramref name="at"/> a positional sound is placed there (2D otherwise).</summary>
        public static void Play(Sfx id, Vector3? at = null) => Play(id, at, 1f, 1f);

        /// <summary>Plays a sound with a volume and pitch scale on top of its table values.</summary>
        public static void Play(Sfx id, Vector3? at, float volumeScale, float pitchScale = 1f)
        {
            if (id == Sfx.None || !SfxLibrary.TryGet(id, out SfxDef def)) return;
            float now = Time.unscaledTime;
            if (s_LastPlay.TryGetValue(id, out float last) && now - last < def.MinInterval && now >= last) return;
            s_LastPlay[id] = now;
            s_PlayCounts.TryGetValue(id, out int count);
            s_PlayCounts[id] = count + 1;
            try { Played?.Invoke(id, at); }
            catch (Exception e) { Debug.LogException(e); }

            var inst = Instance;
            if (inst != null) inst.PlayVoice(id, def, at, volumeScale, pitchScale);
        }

        /// <summary>UI hover tick (buttons, sliders). Call from pointer-enter.</summary>
        public static void UiHover() => Play(Sfx.UiHover);

        /// <summary>UI click (buttons, toggles, slider release).</summary>
        public static void UiClick() => Play(Sfx.UiClick);

        /// <summary>How many times <paramref name="id"/> was played this session (tests, debug).</summary>
        public static int PlayCount(Sfx id) => s_PlayCounts.TryGetValue(id, out int c) ? c : 0;

        /// <summary>
        /// Starts a looping sound that fades in, optionally following <paramref name="follow"/> (positional) or
        /// fixed at <paramref name="at"/>. Returns a handle for <see cref="StopLoop"/> (invalid if it could not start).
        /// </summary>
        public static LoopHandle StartLoop(Sfx id, Transform follow = null, Vector3? at = null, float volume = 1f,
                                           float fadeIn = 0.15f)
        {
            var inst = Instance;
            if (inst == null || !SfxLibrary.TryGet(id, out SfxDef def)) return default;
            return inst.StartLoopInternal(id, def, follow, at, volume, fadeIn);
        }

        /// <summary>Fades a loop out and releases it; the handle is reset.</summary>
        public static void StopLoop(ref LoopHandle handle, float fadeOut = 0.25f)
        {
            var inst = Instance;
            if (inst != null && handle.IsValid) inst.StopLoopInternal(handle.Id, fadeOut);
            handle = default;
        }

        /// <summary>Changes a running loop's volume scale (smoothed).</summary>
        public static void SetLoopVolume(LoopHandle handle, float volume)
        {
            var inst = Instance;
            if (inst == null || !handle.IsValid) return;
            Loop l = inst.FindLoop(handle.Id);
            if (l != null) l.Volume = Mathf.Max(0f, volume);
        }

        /// <summary>Changes a running loop's pitch (playback rate: pitch and speed together; the rewind tape).</summary>
        public static void SetLoopPitch(LoopHandle handle, float pitch)
        {
            var inst = Instance;
            if (inst == null || !handle.IsValid) return;
            Loop l = inst.FindLoop(handle.Id);
            if (l != null) l.Source.pitch = Mathf.Max(0.05f, l.Def.Pitch * pitch); // the web only supports positive pitch
        }

        public static bool IsLoopActive(LoopHandle handle)
        {
            var inst = Instance;
            return inst != null && handle.IsValid && inst.FindLoop(handle.Id) != null;
        }

        // ------------------------------------------------------------------ voices

        sealed class Voice
        {
            public AudioSource Source;
            public float BusyUntil;
            public int Priority;
        }

        readonly Voice[] _voices = new Voice[MaxVoices];
        int _voiceCount, _nextVoice, _tier = -1;
        readonly Dictionary<Sfx, int> _lastVariant = new Dictionary<Sfx, int>();
        float _startTime, _nextListenerCheck;

        AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.dopplerLevel = 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.reverbZoneMix = 0f;
            return src;
        }

        void PlayVoice(Sfx id, in SfxDef def, Vector3? at, float volumeScale, float pitchScale)
        {
            int variant = 0;
            if (def.Variants > 1)
            {
                variant = UnityEngine.Random.Range(0, def.Variants);
                if (_lastVariant.TryGetValue(id, out int lastV) && lastV == variant) variant = (variant + 1) % def.Variants;
                _lastVariant[id] = variant;
            }
            AudioClip clip = SfxLibrary.Clip(id, variant);
            if (clip == null) return;
            Voice v = AcquireVoice(def.Priority);
            if (v == null) return;

            AudioSource src = v.Source;
            bool spatial = def.Spatial && at.HasValue;
            src.Stop();
            src.spatialBlend = spatial ? 1f : 0f;
            if (spatial)
            {
                src.transform.position = at.Value;
                src.minDistance = def.MinDistance;
                src.maxDistance = def.MaxDistance;
            }
            float pitch = def.Pitch * pitchScale;
            if (def.PitchJitter > 0f) pitch *= 1f + UnityEngine.Random.Range(-def.PitchJitter, def.PitchJitter);
            pitch = Mathf.Max(0.05f, pitch); // the web only supports positive pitch
            src.clip = clip;
            src.loop = false;
            src.pitch = pitch;
            src.priority = def.Priority;
            src.volume = Mathf.Clamp01(def.Volume * volumeScale * AudioSettings.SfxGain);
            src.Play();
            v.Priority = def.Priority;
            v.BusyUntil = Time.unscaledTime + clip.length / pitch + 0.05f;
        }

        Voice AcquireVoice(int priority)
        {
            float now = Time.unscaledTime;
            for (int k = 0; k < _voiceCount; k++)
            {
                int i = (_nextVoice + k) % _voiceCount;
                if (_voices[i].BusyUntil <= now)
                {
                    _nextVoice = (i + 1) % _voiceCount;
                    return _voices[i];
                }
            }
            // All busy: steal the least important (then the one that ends soonest), never a more important one.
            Voice worst = null;
            for (int i = 0; i < _voiceCount; i++)
            {
                Voice v = _voices[i];
                if (worst == null || v.Priority > worst.Priority ||
                    (v.Priority == worst.Priority && v.BusyUntil < worst.BusyUntil))
                    worst = v;
            }
            return worst != null && worst.Priority >= priority ? worst : null;
        }

        void ApplyQuality(int tier)
        {
            if (tier == _tier) return;
            _tier = tier;
            int voices = tier <= 0 ? 8 : tier == 1 ? 12 : MaxVoices;
            for (int i = 0; i < voices; i++)
                if (_voices[i] == null) _voices[i] = new Voice { Source = NewSource("Voice " + i) };
            for (int i = voices; i < MaxVoices; i++)
                if (_voices[i] != null)
                {
                    _voices[i].Source.Stop();
                    _voices[i].BusyUntil = 0f;
                }
            _voiceCount = voices;
            if (_nextVoice >= _voiceCount) _nextVoice = 0;
            _birdsEnabled = tier > 0;
        }

        static int CurrentTier() => Mathf.Clamp(QualityTier.Current, 0, 2);

        void OnQualityChanged() => ApplyQuality(CurrentTier());

        // ------------------------------------------------------------------ loops

        sealed class Loop
        {
            public int Id;
            public Sfx Sfx;
            public SfxDef Def;
            public AudioSource Source;
            public Transform Follow;
            public bool HasFollow;
            public float Volume = 1f;
            public float Fade, FadeTarget, FadeSpeed;
        }

        static int s_NextLoopId;
        readonly List<Loop> _loops = new List<Loop>(8);
        readonly Stack<AudioSource> _freeLoopSources = new Stack<AudioSource>();

        Loop FindLoop(int id)
        {
            for (int i = 0; i < _loops.Count; i++)
                if (_loops[i].Id == id && _loops[i].FadeTarget > 0f) return _loops[i];
            return null;
        }

        LoopHandle StartLoopInternal(Sfx id, in SfxDef def, Transform follow, Vector3? at, float volume, float fadeIn)
        {
            AudioClip clip = SfxLibrary.Clip(id, 0);
            if (clip == null) return default;
            AudioSource src = _freeLoopSources.Count > 0 ? _freeLoopSources.Pop() : NewSource("Loop");
            src.name = "Loop " + id;
            bool spatial = def.Spatial && (follow != null || at.HasValue);
            src.spatialBlend = spatial ? 1f : 0f;
            src.minDistance = def.MinDistance;
            src.maxDistance = def.MaxDistance;
            src.priority = def.Priority;
            src.clip = clip;
            src.loop = true;
            src.pitch = Mathf.Max(0.05f, def.Pitch);
            src.volume = 0f;
            if (follow != null) src.transform.position = follow.position;
            else if (at.HasValue) src.transform.position = at.Value;
            src.Play();
            // Start somewhere random so two loops of the same sound never phase.
            if (clip.samples > 1) src.timeSamples = UnityEngine.Random.Range(0, clip.samples - 1);

            var l = new Loop
            {
                Id = ++s_NextLoopId,
                Sfx = id,
                Def = def,
                Source = src,
                Follow = follow,
                HasFollow = follow != null,
                Volume = Mathf.Max(0f, volume),
                Fade = 0f,
                FadeTarget = 1f,
                FadeSpeed = fadeIn > 0f ? 1f / fadeIn : 1000f,
            };
            _loops.Add(l);
            return new LoopHandle(l.Id);
        }

        void StopLoopInternal(int id, float fadeOut)
        {
            for (int i = 0; i < _loops.Count; i++)
            {
                if (_loops[i].Id != id) continue;
                _loops[i].FadeTarget = 0f;
                _loops[i].FadeSpeed = fadeOut > 0f ? 1f / fadeOut : 1000f;
            }
        }

        void StopAllLoops(bool keepAmbience)
        {
            for (int i = 0; i < _loops.Count; i++)
            {
                Loop l = _loops[i];
                if (keepAmbience && l.Sfx == Sfx.AmbWindLoop) continue;
                l.FadeTarget = 0f;
                l.FadeSpeed = 1f / 0.2f;
            }
        }

        void UpdateLoops(float dt)
        {
            float bus = AudioSettings.SfxGain;
            for (int i = _loops.Count - 1; i >= 0; i--)
            {
                Loop l = _loops[i];
                l.Fade = Mathf.MoveTowards(l.Fade, l.FadeTarget, l.FadeSpeed * dt);
                if (l.HasFollow)
                {
                    if (l.Follow != null) l.Source.transform.position = l.Follow.position;
                    else l.FadeTarget = 0f; // the object is gone
                }
                if (l.FadeTarget <= 0f && l.Fade <= 0f)
                {
                    l.Source.Stop();
                    l.Source.clip = null;
                    _freeLoopSources.Push(l.Source);
                    _loops.RemoveAt(i);
                    continue;
                }
                l.Source.volume = Mathf.Clamp01(l.Def.Volume * l.Volume * l.Fade * bus);
            }
        }

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _startTime = Time.time;
            AudioSettings.ApplyListener();
            ApplyQuality(CurrentTier());
            QualityTier.Changed += OnQualityChanged;
            AwakeMusic();
            AwakeAmbience();
            AwakeBindings();
            StartCoroutine(PreloadSfx());
        }

        /// <summary>Loads every SFX clip a few per frame after the level build, so no first play hitches.</summary>
        IEnumerator PreloadSfx()
        {
            yield return new WaitForSecondsRealtime(StartupGrace);
            int n = 0;
            foreach (Sfx id in SfxLibrary.All)
            {
                if (!SfxLibrary.TryGet(id, out SfxDef def)) continue;
                for (int v = 0; v < def.Variants; v++)
                {
                    SfxLibrary.Clip(id, v);
                    if (++n % 4 == 0) yield return null;
                }
            }
        }

        void OnDestroy()
        {
            QualityTier.Changed -= OnQualityChanged;
            DestroyBindings();
            AudioSettings.Save();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit() => AudioSettings.Save();

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            HandleMuteKey();
            if (AudioSettings.Dirty && Time.unscaledTime - AudioSettings.LastChangeTime > SettingsFlushDelay)
                AudioSettings.Save();

            EnsureListener();
            UpdateBindings();
            UpdateMusic(dt);
            UpdateFootsteps();
            UpdateAmbience(dt);
        }

        void LateUpdate()
        {
            LateUpdateBindings();
            UpdateLoops(Time.unscaledDeltaTime);
        }

        void HandleMuteKey()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.mKey.wasPressedThisFrame) return;
            AudioSettings.Muted = !AudioSettings.Muted;
            GameplayUI.Toast(AudioSettings.Muted ? "Sound off  [M]" : "Sound on  [M]");
        }

        void EnsureListener()
        {
            if (Time.unscaledTime < _nextListenerCheck) return;
            _nextListenerCheck = Time.unscaledTime + 2f;
            if (FindFirstObjectByType<AudioListener>() != null) return;
            var cam = Camera.main;
            if (cam != null) cam.gameObject.AddComponent<AudioListener>();
        }

        /// <summary>True during the first moments of the session (level build), when event sounds stay quiet.</summary>
        bool InStartupGrace => Time.time - _startTime < StartupGrace;
    }
}
