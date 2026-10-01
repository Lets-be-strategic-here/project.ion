using System;
using UnityEngine;

namespace Ion.Presentation.Audio
{
    /// <summary>
    /// Player audio preferences: Master, Music and SFX volume sliders (0..1) plus mute. Persisted in
    /// PlayerPrefs. This is the API the Settings panel binds to (Lead D owns the panel layout):
    /// <code>
    /// using IonAudioSettings = Ion.Presentation.Audio.AudioSettings;   // avoids UnityEngine.AudioSettings
    /// slider.value = IonAudioSettings.MusicVolume;  slider.onValueChanged.AddListener(v => IonAudioSettings.MusicVolume = v);
    /// label.text = IonAudioSettings.Percent(IonAudioSettings.MusicVolume);
    /// IonAudioSettings.Changed += Refresh;      // e.g. M toggles mute from anywhere
    /// </code>
    /// Setters apply immediately and are cheap enough for every drag step; the PlayerPrefs write is
    /// batched (IonAudio flushes it ~0.75 s after the last change, or call <see cref="Save"/> on release).
    /// Slider values are perceptual: the gain is the slider squared (about -12 dB at half way).
    /// </summary>
    public static class AudioSettings
    {
        /// <summary>Same key as the old ProceduralAudio.MasterVolume, so existing players keep their level.</summary>
        public const string MasterPrefKey = "ion.masterVolume";
        public const string MusicPrefKey = "ion.musicVolume";
        public const string SfxPrefKey = "ion.sfxVolume";
        public const string MutedPrefKey = "ion.muted";

        public const float DefaultMaster = 1f, DefaultMusic = 0.8f, DefaultSfx = 1f;

        static float s_Master = -1f, s_Music = -1f, s_Sfx = -1f;
        static int s_Muted = -1;

        /// <summary>Raised on the main thread after any value changes (also mute).</summary>
        public static event Action Changed;

        /// <summary>True while there are unsaved changes (IonAudio flushes them).</summary>
        public static bool Dirty { get; private set; }
        /// <summary>Unscaled time of the last change.</summary>
        public static float LastChangeTime { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Master = s_Music = s_Sfx = -1f;
            s_Muted = -1;
            Dirty = false;
            Changed = null;
        }

        public static float MasterVolume
        {
            get => Load(ref s_Master, MasterPrefKey, DefaultMaster);
            set => Store(ref s_Master, MasterPrefKey, value);
        }

        public static float MusicVolume
        {
            get => Load(ref s_Music, MusicPrefKey, DefaultMusic);
            set => Store(ref s_Music, MusicPrefKey, value);
        }

        public static float SfxVolume
        {
            get => Load(ref s_Sfx, SfxPrefKey, DefaultSfx);
            set => Store(ref s_Sfx, SfxPrefKey, value);
        }

        public static bool Muted
        {
            get
            {
                if (s_Muted < 0) s_Muted = PlayerPrefs.GetInt(MutedPrefKey, 0) != 0 ? 1 : 0;
                return s_Muted == 1;
            }
            set
            {
                int v = value ? 1 : 0;
                if (v == s_Muted) return;
                s_Muted = v;
                PlayerPrefs.SetInt(MutedPrefKey, v);
                MarkChanged();
            }
        }

        /// <summary>AudioListener.volume: 0 when muted, otherwise the master gain.</summary>
        public static float ListenerGain => Muted ? 0f : ToGain(MasterVolume);
        /// <summary>Music bus gain (multiplied into the music sources).</summary>
        public static float MusicGain => ToGain(MusicVolume);
        /// <summary>SFX bus gain (multiplied into every effect, footsteps, ambience and UI).</summary>
        public static float SfxGain => ToGain(SfxVolume);

        /// <summary>Slider position (0..1) to linear gain.</summary>
        public static float ToGain(float slider)
        {
            slider = Mathf.Clamp01(slider);
            return slider * slider;
        }

        /// <summary>"80%" style label for a slider value.</summary>
        public static string Percent(float slider) => Mathf.RoundToInt(Mathf.Clamp01(slider) * 100f) + "%";

        /// <summary>Writes pending changes to disk now (IndexedDB on the web). Cheap when nothing changed.</summary>
        public static void Save()
        {
            if (!Dirty) return;
            Dirty = false;
            PlayerPrefs.Save();
        }

        /// <summary>Restores the defaults (a "Reset" button).</summary>
        public static void ResetToDefaults()
        {
            MasterVolume = DefaultMaster;
            MusicVolume = DefaultMusic;
            SfxVolume = DefaultSfx;
            Muted = false;
        }

        /// <summary>Applies the master volume / mute to the listener (IonAudio calls this; safe anywhere).</summary>
        public static void ApplyListener() => AudioListener.volume = ListenerGain;

        static float Load(ref float cache, string key, float def)
        {
            if (cache < 0f) cache = Mathf.Clamp01(PlayerPrefs.GetFloat(key, def));
            return cache;
        }

        static void Store(ref float cache, string key, float value)
        {
            value = Mathf.Clamp01(value);
            if (cache >= 0f && Mathf.Approximately(cache, value)) return;
            cache = value;
            PlayerPrefs.SetFloat(key, value);
            MarkChanged();
        }

        static void MarkChanged()
        {
            Dirty = true;
            LastChangeTime = Time.unscaledTime;
            ApplyListener();
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
