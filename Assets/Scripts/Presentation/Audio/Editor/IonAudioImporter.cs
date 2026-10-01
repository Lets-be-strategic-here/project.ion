using UnityEditor;
using UnityEngine;

namespace Ion.Presentation.Audio.EditorTools
{
    /// <summary>
    /// Import settings for everything under Assets/Resources/Audio (art bible §8), applied on (re)import so the
    /// clips need no hand-edited .meta files. Bump <see cref="Version"/> after changing a value to reimport.
    ///
    /// Music (Audio/Music/*): stereo, 32 kHz, not preloaded (IonAudio loads the current area's loop with
    /// LoadAudioData and unloads the other), loaded in the background, compressed in memory.
    /// SFX (Audio/Sfx/**): mono, preloaded, decompressed on load (short, played often).
    /// Web: AAC at the qualities below. The whole Resources/Audio folder ships, so these numbers set the
    /// download size; the budget is 3.5 MB (art bible §10). Check the clip inspector's "Imported" size or
    /// the build report's Sounds line and lower the qualities if needed.
    /// </summary>
    public sealed class IonAudioImporter : AssetPostprocessor
    {
        const uint Version = 1;
        const string Root = "Assets/Resources/Audio/";
        const string MusicRoot = Root + "Music/";
        const string WebPlatform = "WebGL";

        public const float MusicQuality = 0.5f, SfxQuality = 0.6f;          // editor / desktop (Vorbis)
        public const float MusicWebQuality = 0.3f, SfxWebQuality = 0.45f;   // web (AAC)
        public const uint MusicSampleRate = 32000;

        public override uint GetVersion() => Version;

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root, System.StringComparison.Ordinal)) return;
            var importer = assetImporter as AudioImporter;
            if (importer == null) return;
            bool music = assetPath.StartsWith(MusicRoot, System.StringComparison.Ordinal);

            importer.forceToMono = !music;
            importer.loadInBackground = music;
            importer.ambisonic = false;

            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            s.loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? MusicQuality : SfxQuality;
            s.sampleRateSetting = music ? AudioSampleRateSetting.OverrideSampleRate : AudioSampleRateSetting.PreserveSampleRate;
            s.sampleRateOverride = MusicSampleRate;
            s.preloadAudioData = !music;
            importer.defaultSampleSettings = s;

            AudioImporterSampleSettings web = s;
            web.compressionFormat = AudioCompressionFormat.AAC;
            web.quality = music ? MusicWebQuality : SfxWebQuality;
            if (!importer.SetOverrideSampleSettings(WebPlatform, web))
            {
                web.compressionFormat = AudioCompressionFormat.Vorbis;
                if (!importer.SetOverrideSampleSettings(WebPlatform, web))
                    Debug.LogWarning("[IonAudioImporter] Could not set the Web override for " + assetPath);
            }
        }
    }
}
