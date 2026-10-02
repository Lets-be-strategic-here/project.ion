using Ion.Presentation.Motion;
using NUnit.Framework;

namespace Ion.Tests
{
    /// <summary>The rewind glide's timing curve (art bible §9.1): duration from distance, smootherstep, effect envelope.</summary>
    public sealed class RewindGlideTests
    {
        [Test]
        public void GlideDuration_GrowsWithDistance_AndIsCapped()
        {
            Assert.AreEqual(0.8f, Feel.RewindGlideSeconds(0f), 1e-5f, "base");
            Assert.AreEqual(0.8f, Feel.RewindGlideSeconds(-3f), 1e-5f, "no negative distances");
            Assert.AreEqual(1.0f, Feel.RewindGlideSeconds(5f), 1e-5f);
            Assert.AreEqual(1.2f, Feel.RewindGlideSeconds(10f), 1e-5f);
            Assert.AreEqual(1.6f, Feel.RewindGlideSeconds(20f), 1e-5f, "reaches the cap at 20 m");
            Assert.AreEqual(1.6f, Feel.RewindGlideSeconds(500f), 1e-5f, "capped");
            float prev = 0f;
            for (float d = 0f; d <= 40f; d += 0.5f)
            {
                float s = Feel.RewindGlideSeconds(d);
                Assert.GreaterOrEqual(s, prev, "monotonic at " + d);
                Assert.That(s, Is.InRange(Feel.RewindGlideBaseSeconds, Feel.RewindGlideMaxSeconds));
                prev = s;
            }
            Assert.Greater(Feel.RewindGlideBaseSeconds, Feel.RewindGlideUndoAt, "the undo happens inside every glide");
        }

        [Test]
        public void GlideEase_IsSmootherstep_SlowFastGentle()
        {
            Assert.AreEqual(0f, Feel.RewindGlideEase(0f), 1e-6f);
            Assert.AreEqual(0.5f, Feel.RewindGlideEase(0.5f), 1e-6f);
            Assert.AreEqual(1f, Feel.RewindGlideEase(1f), 1e-6f);
            Assert.AreEqual(1f, Feel.RewindGlideEase(1.5f), 1e-6f, "clamped");
            const float h = 1e-3f;
            float startSpeed = (Feel.RewindGlideEase(h) - Feel.RewindGlideEase(0f)) / h;
            float midSpeed = (Feel.RewindGlideEase(0.5f + h) - Feel.RewindGlideEase(0.5f - h)) / (2f * h);
            float endSpeed = (Feel.RewindGlideEase(1f) - Feel.RewindGlideEase(1f - h)) / h;
            Assert.Less(startSpeed, 0.01f, "slow start");
            Assert.AreEqual(1.875f, midSpeed, 0.01f, "fast middle (15/8)");
            Assert.Less(endSpeed, 0.01f, "gentle settle");
            for (int i = 1; i <= 100; i++)
                Assert.Greater(Feel.RewindGlideEase(i / 100f), Feel.RewindGlideEase((i - 1) / 100f), "strictly rising");
        }

        [Test]
        public void EffectEnvelope_FollowsTheGlideSpeed()
        {
            Assert.AreEqual(0f, Feel.RewindFxEnvelope(0f), 1e-6f);
            Assert.AreEqual(1f, Feel.RewindFxEnvelope(0.5f), 1e-5f);
            Assert.AreEqual(0f, Feel.RewindFxEnvelope(1f), 1e-6f);
            Assert.AreEqual(1f, Ease.SmootherstepSpeed(0.5f), 1e-5f, "speed normalised to 1 at its peak");
            for (int i = 0; i <= 50; i++)
            {
                float k = i / 100f;
                Assert.AreEqual(Feel.RewindFxEnvelope(k), Feel.RewindFxEnvelope(1f - k), 1e-5f, "symmetric");
                Assert.GreaterOrEqual(Feel.RewindFxEnvelope(k), Ease.SmootherstepSpeed(k) - 1e-6f, "a fuller shoulder than the raw speed");
            }
        }
    }
}
