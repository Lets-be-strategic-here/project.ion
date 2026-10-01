using System.Collections;
using Ion.Levels;
using Ion.Presentation;
using Ion.Presentation.Quality;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Ion.Tests.PlayMode
{
    /// <summary>
    /// The Ultra graphics tier end to end: what it turns on (2 shadow cascades, local lights capped at 8, Ultra-only
    /// detail drawn) and that every piece goes away again on High; plus the deferred far cuts of a placement.
    /// </summary>
    public sealed class UltraPlayTests : IonPlayTestBase
    {
        int _savedPref;
        GameObject _adaptive;

        [UnitySetUp]
        public IEnumerator SavePref()
        {
            _savedPref = QualityTier.Preference;
            // AdaptiveQuality self-installs with the first scene only; the tests load Main themselves.
            if (AdaptiveQuality.Instance == null) _adaptive = new GameObject("Test AdaptiveQuality", typeof(AdaptiveQuality));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestorePref()
        {
            QualityTier.Set(_savedPref);
            if (_adaptive != null) Object.Destroy(_adaptive);
            yield return null;
        }

        static UniversalRenderPipelineAsset Urp => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        [UnityTest]
        public IEnumerator Ultra_TurnsOnLightingAndDetail_HighTurnsThemOff()
        {
            yield return GoTo("hub");
            QualityTier.Set(QualityTier.Ultra);
            yield return Seconds(0.6f);
            Assert.IsTrue(UltraFx.Active, "UltraFx follows the tier");
            Assert.AreEqual(2, Urp.shadowCascadeCount, "Ultra: two shadow cascades");
            Assert.GreaterOrEqual(Urp.shadowDistance, 45f, "Ultra: a longer shadow distance");
            Assert.Greater(LocalLights.Count, 0, "lamps and teleporters carry local lights");
            Assert.Greater(UltraFx.ActiveLights, 0, "the hub's lamps light up on Ultra");
            Assert.LessOrEqual(UltraFx.ActiveLights, UltraFx.MaxLights, "local lights are capped");
            Assert.Greater(CountUltraDetail(out int drawn), 0, "the zones carry Ultra-only detail");
            Assert.Greater(drawn, 0, "Ultra-only detail draws on Ultra");
            Assert.AreEqual(UltraFx.UltraPreviewWidth, ProjectionSystem.DefaultPreviewWidth, "higher-resolution previews");

            QualityTier.Set(QualityTier.High);
            yield return Seconds(0.3f);
            Assert.IsFalse(UltraFx.Active);
            Assert.AreEqual(1, Urp.shadowCascadeCount, "High: one cascade");
            Assert.AreEqual(0, UltraFx.ActiveLights, "no local lights off Ultra");
            CountUltraDetail(out drawn);
            Assert.AreEqual(0, drawn, "Ultra-only detail costs no draw call off Ultra");
            Assert.AreEqual(ProjectionSystem.PreviewWidth, ProjectionSystem.DefaultPreviewWidth);
        }

        /// <summary>Ultra-only renderers in the current zone (and how many of them are not culled).</summary>
        int CountUltraDetail(out int drawn)
        {
            int n = 0;
            drawn = 0;
            RoomContext ctx = Game.Rooms[Game.CurrentRoom];
            foreach (Renderer r in ctx.WorldRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!Palette.IsUltra(r.sharedMaterial)) continue;
                n++;
                if (!r.forceRenderingOff && r.enabled && r.gameObject.activeInHierarchy) drawn++;
            }
            return n;
        }

        [UnityTest]
        public IEnumerator Placement_FarCutsFinishWithinFramesAndRewindCleanly()
        {
            RoomContext t1 = Room("t1");
            PhotoData stair = ShotPhoto(t1);
            yield return GoTo("t1:place");
            Dbg.GiveAll("");
            int idx = Inventory.IndexOf(stair);
            Assert.IsTrue(Dbg.TrySelect(idx.ToString()));
            Dbg.Raise("");
            yield return null;
            Assert.IsTrue(Dbg.TryPlace(), State);
            int pending = Projection.PendingCutCount;
            Assert.Greater(pending, 0, "the T1 photo reaches far past the court: some cuts are deferred");
            for (int i = 0; i < 120 && Projection.PendingCutCount > 0; i++) yield return null;
            Assert.AreEqual(0, Projection.PendingCutCount, "far cuts finish within a few frames");

            // The stair works (the near cut + paste were immediate, the far ones done).
            Physics.SyncTransforms();
            Assert.IsTrue(GroundAt(W(t1, 0f, 0f, 15.5f), 6f, 6f, out RaycastHit top) && top.point.y > 2f,
                          "the pasted stair's top step is there");

            // Rewind it, then place again and rewind at once, while far cuts are still pending: they are dropped.
            Assert.AreEqual(Ion.Gameplay.State.RewindResult.Undid, Dbg.TryRewind());
            yield return Seconds(1.5f);
            Assert.AreEqual(0, Projection.PlacementCount);
            yield return GoTo("t1:place");
            Dbg.GiveAll("");
            Assert.IsTrue(Dbg.TrySelect(Inventory.IndexOf(stair).ToString()));
            Dbg.Raise("");
            yield return null;
            Assert.IsTrue(Dbg.TryPlace(), State);
            Assert.Greater(Projection.PendingCutCount, 0);
            Assert.AreEqual(Ion.Gameplay.State.RewindResult.Undid, Dbg.TryRewind());
            Assert.AreEqual(0, Projection.PendingCutCount, "a rewind drops the undone placement's pending cuts");
            yield return Seconds(1.5f);
            Assert.AreEqual(0, Projection.PlacementCount);
            Physics.SyncTransforms();
            Assert.IsTrue(GroundAt(W(t1, 0f, 0f, 15.5f), 6f, 8f, out RaycastHit none), "the court is still there");
            Assert.Less(none.point.y, 0.5f, "no stair left behind");
        }

        [UnityTest]
        public IEnumerator Placement_LmbPressStagesCutsBehindTheCardThenSwaps()
        {
            RoomContext t1 = Room("t1");
            PhotoData stair = ShotPhoto(t1);
            yield return GoTo("t1:place");
            Dbg.GiveAll("");
            Assert.IsTrue(Dbg.TrySelect(Inventory.IndexOf(stair).ToString()));
            Dbg.Raise("");
            yield return null;

            // Press: the record is pushed and the paste is in, but the swap (event, inventory) waits for the cuts.
            int placedEvents = 0;
            System.Action onPlaced = () => placedEvents++;
            Projection.Placed += onPlaced;
            try
            {
                Assert.IsTrue(Holder.PlaceWithPress(), State);
                Assert.IsTrue(Holder.IsPlacing);
                Assert.IsTrue(Projection.IsStaging);
                Assert.Greater(Projection.PendingCutCount, 0, "every cut is staged");
                Assert.IsTrue(Inventory.Contains(stair), "the photo is still in hand during the press");
                Assert.AreEqual(0, placedEvents);
                for (int i = 0; i < 120 && Holder.IsPlacing; i++) yield return null;
                Assert.IsFalse(Holder.IsPlacing, "the press finished");
                Assert.IsFalse(Projection.IsStaging);
                Assert.AreEqual(1, placedEvents, "one swap");
                Assert.IsFalse(Inventory.Contains(stair), "consumed at the swap");
                Assert.AreEqual(1, Projection.PlacementCount);
                for (int i = 0; i < 120 && Projection.PendingCutCount > 0; i++) yield return null;
                Assert.AreEqual(0, Projection.PendingCutCount);
                Physics.SyncTransforms();
                Assert.IsTrue(GroundAt(W(t1, 0f, 0f, 15.5f), 6f, 6f, out RaycastHit top) && top.point.y > 2f,
                              "the pasted stair's top step is there, as with an immediate placement");

                // R undoes it like any placement.
                Assert.AreEqual(Ion.Gameplay.State.RewindResult.Undid, Dbg.TryRewind());
                yield return Seconds(1.5f);
                Assert.AreEqual(0, Projection.PlacementCount);
                Assert.IsTrue(Inventory.Contains(stair), "the photo is back");

                // A press cancelled mid-way (R lowers the card) leaves no trace and no history entry.
                yield return GoTo("t1:place");
                Assert.IsTrue(Dbg.TrySelect(Inventory.IndexOf(stair).ToString()));
                Dbg.Raise("");
                yield return null;
                Assert.IsTrue(Holder.PlaceWithPress(), State);
                Assert.IsTrue(Projection.IsStaging);
                Holder.Lower();
                Assert.IsFalse(Projection.IsStaging);
                Assert.AreEqual(0, Projection.PlacementCount, "the staged placement was dropped");
                Assert.AreEqual(0, Projection.PendingCutCount);
                Assert.IsTrue(Inventory.Contains(stair));
                yield return Frames(3);
                Physics.SyncTransforms();
                Assert.IsTrue(GroundAt(W(t1, 0f, 0f, 15.5f), 6f, 8f, out RaycastHit none), "the court is still there");
                Assert.Less(none.point.y, 0.5f, "no stair left behind");
            }
            finally { Projection.Placed -= onPlaced; }
        }
    }
}
