using System;
using System.IO;
using Ion.Presentation;
using Ion.Presentation.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using IonAudioSettings = Ion.Presentation.Audio.AudioSettings;

namespace Ion.Tests.Audio
{
    /// <summary>Assets, import settings and the pure mapping logic of the audio system (Lead E).</summary>
    public class AudioTests
    {
        [Test]
        public void EverySfxIdHasADefinition()
        {
            foreach (Sfx id in Enum.GetValues(typeof(Sfx)))
            {
                if (id == Sfx.None) continue;
                Assert.IsTrue(SfxLibrary.TryGet(id, out _), "No SfxDef for " + id);
            }
        }

        [Test]
        public void EverySfxClipExistsInResources()
        {
            foreach (string path in SfxLibrary.AllResourcePaths())
                Assert.IsNotNull(Resources.Load<AudioClip>(path), "Missing Resources/" + path + " (run tools/audio/ionsfx.py)");
        }

        [TestCase(SfxLibrary.MusicRooms, 150f)]
        [TestCase(SfxLibrary.MusicHub, 53.333f)]
        [TestCase(SfxLibrary.MusicTutorial, 60f)]
        public void MusicLoopsExistWithExactLengths(string track, float seconds)
        {
            var clip = Resources.Load<AudioClip>(SfxLibrary.MusicRoot + track);
            Assert.IsNotNull(clip, "Missing music " + track + " (run tools/audio/ionmusic.py)");
            Assert.AreEqual(seconds, clip.length, 0.06f, track + " length");
            Assert.AreEqual(2, clip.channels, track + " should be stereo");
        }

        [Test]
        public void ImportSettingsAreApplied()
        {
            var music = AssetImporter.GetAtPath("Assets/Resources/Audio/Music/" + SfxLibrary.MusicRooms + ".ogg") as AudioImporter;
            Assert.IsNotNull(music, "rooms.ogg importer");
            Assert.IsFalse(music.forceToMono, "music stays stereo");
            Assert.IsFalse(music.defaultSampleSettings.preloadAudioData, "music loads on demand");

            var sfx = AssetImporter.GetAtPath("Assets/Resources/Audio/Sfx/photo_place.ogg") as AudioImporter;
            Assert.IsNotNull(sfx, "photo_place.ogg importer");
            Assert.IsTrue(sfx.forceToMono, "sfx are mono");
            Assert.IsTrue(sfx.defaultSampleSettings.preloadAudioData, "sfx preload");
        }

        [Test]
        public void CreditsListTheCc0Sources()
        {
            var credits = Resources.Load<TextAsset>(SfxLibrary.CreditsPath);
            Assert.IsNotNull(credits, "Resources/Audio/CREDITS.txt");
            StringAssert.Contains("CC0", credits.text);
            StringAssert.Contains("kenney.nl", credits.text);
        }

        [Test]
        public void SourceAudioStaysModest()
        {
            long bytes = 0;
            foreach (string f in Directory.GetFiles("Assets/Resources/Audio", "*.ogg", SearchOption.AllDirectories))
                bytes += new FileInfo(f).Length;
            // Source Vorbis files; the web build re-encodes (see IonAudioImporter) for the 3.5 MB budget.
            Assert.Less(bytes, 6L * 1024 * 1024, "Assets/Resources/Audio is " + bytes / 1024 + " KB");
        }

        [Test]
        public void FootstepSetsFollowTheBibleTable()
        {
            foreach (Mat m in new[] { Mat.Limestone, Mat.Paper, Mat.Plaster, Mat.Concrete, Mat.Terracotta, Mat.Rose, Mat.Mint })
            {
                FootSurface s = IonAudio.SurfaceForMat(m);
                Assert.IsTrue(s == FootSurface.Tile || s == FootSurface.Stone, m + " -> " + s);
            }
            Assert.AreEqual(FootSurface.Wood, IonAudio.SurfaceForMat(Mat.Oak));
            Assert.AreEqual(FootSurface.Wood, IonAudio.SurfaceForMat(Mat.Walnut));
            Assert.AreEqual(FootSurface.Grass, IonAudio.SurfaceForMat(Mat.Lawn));
            Assert.AreEqual(FootSurface.Grass, IonAudio.SurfaceForMat(Mat.Foliage));
            Assert.AreEqual(FootSurface.Grass, IonAudio.SurfaceForMat(Mat.FoliageDark));
            Assert.AreEqual(FootSurface.Tile, IonAudio.SurfaceForMat(Mat.Limestone));
        }

        [Test]
        public void PatternsRefineFootsteps()
        {
            Assert.AreEqual(FootSurface.Tile, IonAudio.SurfaceForPattern(Pat.Tile));
            Assert.AreEqual(FootSurface.Tile, IonAudio.SurfaceForPattern(Pat.Herringbone));
            Assert.AreEqual(FootSurface.Stone, IonAudio.SurfaceForPattern(Pat.Courses));
            Assert.AreEqual(FootSurface.Stone, IonAudio.SurfaceForPattern(Pat.Formwork));
            Assert.AreEqual(FootSurface.Wood, IonAudio.SurfaceForPattern(Pat.BoardsX));
            Assert.AreEqual(FootSurface.Wood, IonAudio.SurfaceForPattern(Pat.BoardsZ));
            Assert.AreEqual(FootSurface.Grass, IonAudio.SurfaceForPattern(Pat.Lawn));
            Assert.IsNull(IonAudio.SurfaceForPattern(Pat.None));
            foreach (FootSurface s in Enum.GetValues(typeof(FootSurface)))
                Assert.IsTrue(SfxLibrary.TryGet(IonAudio.StepSfx(s), out _), "step sfx for " + s);
        }

        [TestCase("TutorialLedge", MusicArea.Tutorial)]
        [TestCase("TutorialDarkroom", MusicArea.Tutorial)]
        [TestCase("HubLightTable", MusicArea.Hub)]
        [TestCase("StairsWing", MusicArea.Puzzle)]
        [TestCase("CameraWing", MusicArea.Puzzle)]
        [TestCase("GalleryEnding", MusicArea.Ending)]
        [TestCase("BridgeRoom", MusicArea.Puzzle)]
        public void ZonesMapToMusicAreas(string roomClass, MusicArea expected)
        {
            Assert.AreEqual(expected, IonAudio.AreaOfRoomName(roomClass));
        }

        [Test]
        public void AreasMapToTracks()
        {
            Assert.AreEqual(SfxLibrary.MusicRooms, IonAudio.TrackFor(MusicArea.Puzzle));
            Assert.AreEqual(SfxLibrary.MusicHub, IonAudio.TrackFor(MusicArea.Hub));
            Assert.AreEqual(SfxLibrary.MusicHub, IonAudio.TrackFor(MusicArea.Ending));
            Assert.AreEqual(SfxLibrary.MusicTutorial, IonAudio.TrackFor(MusicArea.Tutorial));
            Assert.IsNull(IonAudio.TrackFor(MusicArea.None));
        }

        [Test]
        public void SettingsClampPersistAndUseAPerceptualCurve()
        {
            float master = IonAudioSettings.MasterVolume, music = IonAudioSettings.MusicVolume, sfx = IonAudioSettings.SfxVolume;
            bool muted = IonAudioSettings.Muted;
            try
            {
                IonAudioSettings.MusicVolume = 1.7f;
                Assert.AreEqual(1f, IonAudioSettings.MusicVolume);
                IonAudioSettings.MusicVolume = 0.5f;
                Assert.AreEqual(0.25f, IonAudioSettings.MusicGain, 1e-5f);
                Assert.AreEqual(0.5f, PlayerPrefs.GetFloat(IonAudioSettings.MusicPrefKey), 1e-5f);

                IonAudioSettings.SfxVolume = -1f;
                Assert.AreEqual(0f, IonAudioSettings.SfxGain);

                IonAudioSettings.Muted = false;
                IonAudioSettings.MasterVolume = 0.5f;
                Assert.AreEqual(0.25f, AudioListener.volume, 1e-5f);
                IonAudioSettings.Muted = true;
                Assert.AreEqual(0f, AudioListener.volume);
                Assert.AreEqual("50%", IonAudioSettings.Percent(0.5f));

                int changes = 0;
                Action count = () => changes++;
                IonAudioSettings.Changed += count;
                IonAudioSettings.MusicVolume = 0.3f;
                IonAudioSettings.MusicVolume = 0.3f; // no change, no event
                IonAudioSettings.Changed -= count;
                Assert.AreEqual(1, changes);
            }
            finally
            {
                IonAudioSettings.MasterVolume = master;
                IonAudioSettings.MusicVolume = music;
                IonAudioSettings.SfxVolume = sfx;
                IonAudioSettings.Muted = muted;
                IonAudioSettings.Save();
            }
        }

        [Test]
        public void ProceduralAudioFacadeForwardsToSettings()
        {
            float master = IonAudioSettings.MasterVolume;
            try
            {
                ProceduralAudio.MasterVolume = 0.4f;
                Assert.AreEqual(0.4f, IonAudioSettings.MasterVolume, 1e-5f);
                Assert.AreEqual(IonAudioSettings.MasterPrefKey, ProceduralAudio.MasterVolumePrefKey);
            }
            finally
            {
                ProceduralAudio.MasterVolume = master;
            }
        }
    }
}
