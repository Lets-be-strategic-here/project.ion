using Ion.Gameplay;
using Ion.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Ion.Tests
{
    /// <summary>Regression tests for the critic pass: photo copies of teleporters, UI scale floor, snapshot shape.</summary>
    public class CriticFixTests
    {
        [Test]
        public void Teleporter_CloneKeepsItsAction()
        {
            var go = new GameObject("TeleporterSource");
            GameObject copy = null;
            try
            {
                var tp = go.AddComponent<Teleporter>();
                int fired = 0;
                tp.OnEnter = () => fired++;

                // A photo stores an Instantiate()d template and pastes another clone of it.
                copy = Object.Instantiate(go);
                var clone = copy.GetComponent<Teleporter>();
                Assert.IsNotNull(clone.OnEnter, "a pasted teleporter must keep its destination");
                clone.OnEnter();
                Assert.AreEqual(1, fired);
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (copy != null) Object.DestroyImmediate(copy);
            }
        }

        [Test]
        public void CanvasScale_HasAFloorForSmallWindows()
        {
            var reference = new Vector2(1920f, 1080f);
            Assert.AreEqual(1f, IonCanvasScaler.ScaleFor(reference, reference, 0.5f), 1e-4f);
            Assert.AreEqual(IonCanvasScaler.MinScale, IonCanvasScaler.ScaleFor(new Vector2(800f, 500f), reference, 0.5f), 1e-4f);
            Assert.Greater(IonCanvasScaler.ScaleFor(new Vector2(2560f, 1440f), reference, 0.5f), 1f);
        }

        [Test]
        public void Snapshot_HasThePhotoShape()
        {
            // Same frustum as the pre-made photos, so a raised snapshot fits in its frame.
            Assert.AreEqual(Ion.Levels.RoomContext.PhotoFovY, InstantCamera.CaptureFovY);
            Assert.AreEqual(Ion.Levels.RoomContext.PhotoAspect, InstantCamera.CaptureAspect, 1e-5f);
        }
    }
}
