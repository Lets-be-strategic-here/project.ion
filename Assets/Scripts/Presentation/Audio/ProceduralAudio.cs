namespace Ion.Presentation.Audio
{
    /// <summary>
    /// Legacy facade kept for its public API (art bible §8): master volume and mute, now backed by
    /// <see cref="AudioSettings"/>. The procedural synthesis it used to do is gone; sounds are offline-rendered
    /// clips played by <see cref="IonAudio"/>, with <see cref="AudioSynth"/> only as a missing-clip fallback.
    /// New code should use <see cref="AudioSettings"/> (which also has the Music and SFX sliders).
    /// </summary>
    public static class ProceduralAudio
    {
        public const string MasterVolumePrefKey = AudioSettings.MasterPrefKey;
        public const string MutedPrefKey = AudioSettings.MutedPrefKey;

        /// <summary>Master volume slider 0..1 (persisted).</summary>
        public static float MasterVolume
        {
            get => AudioSettings.MasterVolume;
            set
            {
                AudioSettings.MasterVolume = value;
                AudioSettings.Save();
            }
        }

        /// <summary>Global mute (persisted). Toggled with M.</summary>
        public static bool Muted
        {
            get => AudioSettings.Muted;
            set
            {
                AudioSettings.Muted = value;
                AudioSettings.Save();
            }
        }
    }
}
