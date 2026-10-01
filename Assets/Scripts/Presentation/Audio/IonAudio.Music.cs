using System;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels;
using UnityEngine;

namespace Ion.Presentation.Audio
{
    /// <summary>
    /// Music: two decks with equal-power crossfades (2.5 s) when the area changes, a -4 dB duck on place,
    /// rewind and teleport, the rewind tape-dip (pitch 1 -> 0.92 -> 1 over 0.6 s; checkpoint 0.85 over 1.0 s)
    /// and the limbo muffle. Only the playing loops are loaded: a deck's clip is unloaded when it fades out
    /// (UnloadAudioData), which caps decoded Web Audio memory. Each track resumes where it left off.
    ///
    /// Limbo: the bible's 600 Hz low-pass needs an AudioLowPassFilter, which WebGL does not support. On the
    /// web the muffle is a volume drop + a slight pitch sag under the limbo drone loop; elsewhere (editor,
    /// desktop) the filter sweeps 22 kHz -> 600 Hz over 0.8 s as specified.
    /// </summary>
    public sealed partial class IonAudio
    {
        public const float CrossfadeSeconds = 2.5f;
        /// <summary>Music source level before the Music slider (the loops are mastered at -20 LUFS).</summary>
        public const float MusicMix = 0.9f;
        public const float DuckDb = -4f, DuckSeconds = 0.6f;
        const float DuckAttack = 0.08f, DuckRelease = 0.4f;
        const float LimboSeconds = 0.8f, LimboMusicGain = 0.4f, LimboPitch = 0.97f, LimboCutoff = 600f;
        const float PendingTimeout = 6f;

        /// <summary>DSP filter components work everywhere except the WebGL player.</summary>
        static readonly bool FiltersSupported = Application.platform != RuntimePlatform.WebGLPlayer;

        /// <summary>Overrides the automatic area (end card, cut-scenes, tests). Null = follow the player's zone.</summary>
        public static MusicArea? ForcedArea { get; set; }

        /// <summary>Optional zone index -> area mapping (Lead C). Default: by the zone's Room class name.</summary>
        public static Func<int, MusicArea> AreaResolver { get; set; }

        /// <summary>Raised when the detected area changes.</summary>
        public static event Action<MusicArea> AreaChanged;

        /// <summary>The area the music currently follows.</summary>
        public static MusicArea CurrentArea => Instance != null ? Instance._area : MusicArea.None;

        /// <summary>The track the music is heading to (null = silence).</summary>
        public static string CurrentTrack => Instance != null ? Instance._wantTrack : null;

        /// <summary>Ducks the music by <paramref name="db"/> for <paramref name="seconds"/> (then releases).</summary>
        public static void Duck(float db = DuckDb, float seconds = DuckSeconds)
        {
            var inst = Instance;
            if (inst == null) return;
            float until = Time.unscaledTime + Mathf.Max(0f, seconds);
            if (until > inst._duckUntil) inst._duckUntil = until;
            inst._duckDepth = Mathf.Min(inst._duckDepth, Mathf.Min(0f, db));
        }

        /// <summary>Tape-dip: music pitch 1 -> <paramref name="minPitch"/> -> 1 over <paramref name="seconds"/>.</summary>
        public static void TapeDip(float minPitch, float seconds)
        {
            var inst = Instance;
            if (inst == null) return;
            inst._dipT = 0f;
            inst._dipSeconds = Mathf.Max(0.05f, seconds);
            inst._dipMin = Mathf.Clamp(minPitch, 0.5f, 1f);
        }

        /// <summary>Area for a Room class name: Tutorial*, Hub*, Gallery*/Ending*, everything else is a puzzle wing.</summary>
        public static MusicArea AreaOfRoomName(string roomTypeName)
        {
            if (string.IsNullOrEmpty(roomTypeName)) return MusicArea.Puzzle;
            if (roomTypeName.IndexOf("Tutorial", StringComparison.OrdinalIgnoreCase) >= 0) return MusicArea.Tutorial;
            if (roomTypeName.IndexOf("Hub", StringComparison.OrdinalIgnoreCase) >= 0) return MusicArea.Hub;
            if (roomTypeName.IndexOf("Gallery", StringComparison.OrdinalIgnoreCase) >= 0 ||
                roomTypeName.IndexOf("Ending", StringComparison.OrdinalIgnoreCase) >= 0) return MusicArea.Ending;
            return MusicArea.Puzzle;
        }

        /// <summary>Track for an area (bible: tutorial, hub and ending use `hub`, the wings use `rooms`; the
        /// tutorial gets its own sparse loop when present).</summary>
        public static string TrackFor(MusicArea area)
        {
            switch (area)
            {
                case MusicArea.Tutorial:
                    return SfxLibrary.HasMusic(SfxLibrary.MusicTutorial) ? SfxLibrary.MusicTutorial : SfxLibrary.MusicHub;
                case MusicArea.Hub:
                case MusicArea.Ending:
                    return SfxLibrary.MusicHub;
                case MusicArea.Puzzle:
                    return SfxLibrary.MusicRooms;
                default:
                    return null;
            }
        }

        sealed class Deck
        {
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public string Track;
            public AudioClip Clip;
            public float Theta, ThetaTarget;   // gain = sin(theta): equal power when two decks move together
            public bool Pending;
            public float PendingSince;
        }

        readonly Deck[] _decks = new Deck[2];
        readonly Dictionary<string, float> _resumeAt = new Dictionary<string, float>();
        MusicArea _area = MusicArea.None;
        string _wantTrack;
        float _nextAreaCheck;
        float _duckGain = 1f, _duckUntil, _duckDepth;
        float _dipT = -1f, _dipSeconds = 0.6f, _dipMin = 1f;
        float _limbo, _limboTarget;

        void AwakeMusic()
        {
            for (int i = 0; i < 2; i++)
            {
                var src = NewSource("Music " + (char)('A' + i));
                src.loop = true;
                src.priority = 0;
                src.volume = 0f;
                src.ignoreListenerPause = true;
                var deck = new Deck { Source = src };
                if (FiltersSupported)
                {
                    deck.Filter = src.gameObject.AddComponent<AudioLowPassFilter>();
                    deck.Filter.cutoffFrequency = 22000f;
                    deck.Filter.lowpassResonanceQ = 1f;
                }
                _decks[i] = deck;
            }
        }

        /// <summary>Forces an immediate area re-check (teleports, restarts).</summary>
        void CheckAreaSoon() => _nextAreaCheck = 0f;

        MusicArea DetectArea()
        {
            if (ForcedArea.HasValue) return ForcedArea.Value;
            var fpc = FirstPersonController.Current;
            if (fpc == null) return MusicArea.None;
            int zone = ZoneInfo.ZoneOf(fpc.transform.position);
            if (AreaResolver != null) return AreaResolver(zone);
            var game = GameBootstrap.Instance;
            if (game == null || game.Rooms == null || game.Rooms.Count == 0) return MusicArea.Hub;
            zone = Mathf.Clamp(zone, 0, game.Rooms.Count - 1);
            var room = game.Rooms[zone].Room;
            return room != null ? AreaOfRoomName(room.GetType().Name) : MusicArea.Puzzle;
        }

        void UpdateMusic(float dt)
        {
            float now = Time.unscaledTime;
            if (now >= _nextAreaCheck)
            {
                _nextAreaCheck = now + 0.25f;
                MusicArea area = MusicArea.None;
                try { area = DetectArea(); }
                catch (Exception e) { Debug.LogException(e); }
                if (area != _area)
                {
                    _area = area;
                    try { AreaChanged?.Invoke(area); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                string track = TrackFor(area);
                if (track != _wantTrack) RequestTrack(track);
            }

            // Start decks whose clip finished loading, then fade everything else out.
            for (int i = 0; i < 2; i++)
            {
                Deck d = _decks[i];
                if (!d.Pending) continue;
                AudioDataLoadState state = d.Clip != null ? d.Clip.loadState : AudioDataLoadState.Failed;
                bool timedOut = now - d.PendingSince > PendingTimeout;
                if (state == AudioDataLoadState.Loading && !timedOut) continue;
                d.Pending = false;
                if (state == AudioDataLoadState.Failed && !timedOut)
                {
                    Debug.LogWarning("[IonAudio] Music '" + d.Track + "' failed to load.");
                    d.Track = null;
                    continue;
                }
                d.Source.clip = d.Clip;
                d.Source.volume = 0f;
                d.Source.Play();
                if (_resumeAt.TryGetValue(d.Track, out float t) && d.Clip != null && t > 0f && t < d.Clip.length - 0.1f)
                    d.Source.time = t;
                d.ThetaTarget = Mathf.PI * 0.5f;
                Deck other = _decks[1 - i];
                other.ThetaTarget = 0f;
            }

            float speed = Mathf.PI * 0.5f / CrossfadeSeconds;
            for (int i = 0; i < 2; i++)
            {
                Deck d = _decks[i];
                d.Theta = Mathf.MoveTowards(d.Theta, d.ThetaTarget, speed * dt);
                if (d.Theta <= 0f && d.ThetaTarget <= 0f && !d.Pending && d.Track != null) RetireDeck(d);
            }

            // Envelopes.
            float duckTarget = now < _duckUntil ? Mathf.Pow(10f, _duckDepth / 20f) : 1f;
            float duckRate = (1f - Mathf.Pow(10f, DuckDb / 20f)) / (duckTarget < _duckGain ? DuckAttack : DuckRelease);
            _duckGain = Mathf.MoveTowards(_duckGain, duckTarget, duckRate * dt);
            if (now >= _duckUntil) _duckDepth = 0f;

            float pitch = 1f;
            if (_dipT >= 0f)
            {
                _dipT += dt;
                float u = Mathf.Clamp01(_dipT / _dipSeconds);
                pitch = 1f - (1f - _dipMin) * Mathf.Sin(u * Mathf.PI);
                if (u >= 1f) _dipT = -1f;
            }
            _limbo = Mathf.MoveTowards(_limbo, _limboTarget, dt / LimboSeconds);
            float limboEase = 1f - (1f - _limbo) * (1f - _limbo); // easeOutQuad
            pitch *= Mathf.Lerp(1f, LimboPitch, limboEase);
            float limboGain = Mathf.Lerp(1f, FiltersSupported ? 0.75f : LimboMusicGain, limboEase);
            float cutoff = 22000f * Mathf.Pow(LimboCutoff / 22000f, limboEase);

            float bus = AudioSettings.MusicGain * MusicMix * _duckGain * limboGain;
            for (int i = 0; i < 2; i++)
            {
                Deck d = _decks[i];
                if (d.Track == null || d.Pending) continue;
                d.Source.volume = Mathf.Clamp01(Mathf.Sin(d.Theta) * bus);
                d.Source.pitch = Mathf.Max(0.05f, pitch);
                if (d.Filter != null) d.Filter.cutoffFrequency = cutoff;
            }
        }

        void RequestTrack(string track)
        {
            _wantTrack = track;
            if (track == null)
            {
                for (int i = 0; i < 2; i++)
                {
                    _decks[i].ThetaTarget = 0f;
                    if (_decks[i].Pending) { _decks[i].Pending = false; _decks[i].Track = null; }
                }
                return;
            }

            // Already on a deck (e.g. coming back mid-fade): bring it back up, fade the other.
            for (int i = 0; i < 2; i++)
            {
                Deck d = _decks[i];
                if (d.Track != track) continue;
                if (!d.Pending) d.ThetaTarget = Mathf.PI * 0.5f;
                Deck other = _decks[1 - i];
                if (other.Pending) { other.Pending = false; other.Track = null; }
                else if (!d.Pending) other.ThetaTarget = 0f;
                return;
            }

            AudioClip clip = SfxLibrary.Music(track);
            if (clip == null) return; // missing: keep whatever plays (one warning was logged)

            // Use the quieter deck; whatever it still plays is cut (it is the one fading out anyway).
            Deck target = _decks[0].Theta <= _decks[1].Theta ? _decks[0] : _decks[1];
            if (target.Track != null && !target.Pending) RetireDeck(target);
            target.Track = track;
            target.Clip = clip;
            target.Theta = 0f;
            target.ThetaTarget = 0f;
            target.Pending = true;
            target.PendingSince = Time.unscaledTime;
            if (clip.loadState != AudioDataLoadState.Loaded && clip.loadState != AudioDataLoadState.Loading)
                clip.LoadAudioData();
            // The other deck keeps playing until this one is ready, then the two cross (equal power).
        }

        void RetireDeck(Deck d)
        {
            if (d.Track != null && d.Clip != null && d.Source.clip == d.Clip)
                _resumeAt[d.Track] = d.Source.time;
            d.Source.Stop();
            d.Source.clip = null;
            AudioClip clip = d.Clip;
            d.Track = null;
            d.Clip = null;
            d.Theta = d.ThetaTarget = 0f;
            Deck other = _decks[0] == d ? _decks[1] : _decks[0];
            if (clip != null && other.Clip != clip && clip.loadState == AudioDataLoadState.Loaded)
                clip.UnloadAudioData();
        }

        void SetLimbo(bool on)
        {
            _limboTarget = on ? 1f : 0f;
        }
    }
}
