using System;
using System.Collections;
using Ion.Presentation.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IonAudioSettings = Ion.Presentation.Audio.AudioSettings;

namespace Ion.Tests.Audio
{
    /// <summary>IonAudio at runtime: install, every sound plays from a real clip, loops, music areas.</summary>
    public class IonAudioPlayTests
    {
        static IEnumerator EnsureInstalled()
        {
            if (IonAudio.Instance == null) new GameObject("IonAudio").AddComponent<IonAudio>();
            yield return null;
            Assert.IsNotNull(IonAudio.Instance, "IonAudio installs itself");
        }

        [UnityTest]
        public IEnumerator EverySfxPlaysFromARealClip()
        {
            yield return EnsureInstalled();
            yield return new WaitForSecondsRealtime(1.1f); // clear the retrigger guards (longest is 1 s)
            foreach (Sfx id in Enum.GetValues(typeof(Sfx)))
            {
                if (id == Sfx.None) continue;
                int before = IonAudio.PlayCount(id);
                IonAudio.Play(id, Vector3.forward * 3f);
                Assert.AreEqual(before + 1, IonAudio.PlayCount(id), id + " was not accepted");
            }
            yield return null;
            Assert.IsEmpty(SfxLibrary.Missing, "Missing clips: " + string.Join(", ", SfxLibrary.Missing));
        }

        [UnityTest]
        public IEnumerator RetriggerGuardDropsDoubles()
        {
            yield return EnsureInstalled();
            yield return new WaitForSecondsRealtime(1.1f);
            int before = IonAudio.PlayCount(Sfx.PhotoPlace);
            IonAudio.Play(Sfx.PhotoPlace);
            IonAudio.Play(Sfx.PhotoPlace); // same frame: an event and a direct call
            Assert.AreEqual(before + 1, IonAudio.PlayCount(Sfx.PhotoPlace));
        }

        [UnityTest]
        public IEnumerator LoopsStartAndStop()
        {
            yield return EnsureInstalled();
            var target = new GameObject("LoopTarget").transform;
            LoopHandle h = IonAudio.StartLoop(Sfx.MoverLoop, target);
            Assert.IsTrue(h.IsValid);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.IsTrue(IonAudio.IsLoopActive(h));
            LoopHandle copy = h;
            IonAudio.StopLoop(ref h, 0.05f);
            Assert.IsFalse(h.IsValid, "StopLoop resets the handle");
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.IsFalse(IonAudio.IsLoopActive(copy));
            UnityEngine.Object.Destroy(target.gameObject);
        }

        [UnityTest]
        public IEnumerator MusicFollowsTheArea()
        {
            yield return EnsureInstalled();
            try
            {
                IonAudio.ForcedArea = MusicArea.Puzzle;
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.AreEqual(SfxLibrary.MusicRooms, IonAudio.CurrentTrack);
                Assert.AreEqual(MusicArea.Puzzle, IonAudio.CurrentArea);

                IonAudio.ForcedArea = MusicArea.Hub;
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.AreEqual(SfxLibrary.MusicHub, IonAudio.CurrentTrack);

                IonAudio.ForcedArea = MusicArea.None;
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.IsNull(IonAudio.CurrentTrack);
            }
            finally
            {
                IonAudio.ForcedArea = null;
            }
        }

        [UnityTest]
        public IEnumerator MuteSilencesTheListener()
        {
            yield return EnsureInstalled();
            bool muted = IonAudioSettings.Muted;
            float master = IonAudioSettings.MasterVolume;
            try
            {
                IonAudioSettings.MasterVolume = 1f;
                IonAudioSettings.Muted = true;
                yield return null;
                Assert.AreEqual(0f, AudioListener.volume);
                IonAudioSettings.Muted = false;
                yield return null;
                Assert.AreEqual(1f, AudioListener.volume, 1e-4f);
            }
            finally
            {
                IonAudioSettings.Muted = muted;
                IonAudioSettings.MasterVolume = master;
                IonAudioSettings.Save();
            }
        }
    }
}
