using System.Collections;
using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels;
using Ion.Levels.Props;
using Ion.Levels.Rooms;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ion.Tests.PlayMode
{
    /// <summary>The hub "Light Table" (art bible §7.3): the contact sheet, the exhibit routes, the wings' return and the ending.</summary>
    public sealed class HubTests : IonPlayTestBase
    {
        RoomContext HubCtx => Room("hub");
        HubLightTable Hub => (HubLightTable)HubCtx.Room;

        [UnityTest]
        public IEnumerator Hub_AllPhotosOnLightTable()
        {
            HubLightTable hub = Hub;
            LightTableView prints = hub.Prints;
            Assert.IsNotNull(prints, "the light table has no prints");
            Assert.AreEqual(8, prints.Count, "a 4 × 2 contact sheet");

            // Every photo of the game has its slot; the rest are reserved (coming soon).
            Assert.AreSame(ShotPhoto(Room("t1")), hub.State.SlotPhoto(0), "T1 stair");
            Assert.AreSame(ShotPhoto(Room("t2")), hub.State.SlotPhoto(1), "T2 door");
            Assert.AreSame(ShotPhoto(HubCtx, 0), hub.State.SlotPhoto(2), "Stairs key");
            Assert.AreSame(ShotPhoto(HubCtx, 1), hub.State.SlotPhoto(3), "Camera key");
            for (int i = 4; i < 8; i++) Assert.IsNull(hub.State.SlotPhoto(i), "slot " + i + " is coming soon");

            // A fresh game: nothing known yet, every print is a blank.
            hub.State.Refresh();
            for (int i = 0; i < 8; i++)
            {
                Assert.IsFalse(!prints[i].IsBlank, "print " + i + " should be blank before it is known");
                Assert.IsFalse(prints[i].Border, "print " + i + " is not solved");
            }

            // Entering the hub: the tutorial photos were used, so they are known and solved.
            yield return GoTo("hub");
            hub.State.Refresh();
            for (int i = 0; i < 2; i++)
            {
                Assert.IsTrue(!prints[i].IsBlank, "tutorial print " + i);
                Assert.IsTrue(prints[i].Border, "tutorial print " + i + " solved");
            }
            Assert.IsTrue(prints[2].IsBlank);

            // Holding the keys makes them known (not solved).
            Dbg.GiveAll("");
            yield return Seconds(0.5f);
            Assert.IsTrue(!prints[2].IsBlank && !prints[3].IsBlank, "keys known once held");
            Assert.IsFalse(prints[2].Border || prints[3].Border);
            for (int i = 4; i < 8; i++) Assert.IsFalse(!prints[i].IsBlank, "coming soon stays blank");

            // Solving a wing borders its print and turns its exhibit to Solved.
            hub.State.SetSolved(HubLightTable.Wing.Stairs);
            Assert.IsTrue(prints[2].Border);
            Assert.AreEqual(ExhibitState.Solved, hub.StairsExhibit.State);
            Assert.AreEqual(ExhibitState.Available, hub.CameraExhibit.State);
            foreach (ExhibitStand s in hub.ComingSoon) Assert.AreEqual(ExhibitState.ComingSoon, s.State);
        }

        /// <summary>Each exhibit's key photo, placed from its marker, pastes a working teleporter to its wing.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator Hub_EveryExhibitPhotoOpensItsWing([Values("n", "s")] string exhibit)
        {
            RoomContext hub = HubCtx;
            string wing = exhibit == "n" ? "stairs" : "camera";
            yield return GoTo("hub");

            // Before: the screen is solid.
            RoomSolution far = Spot(hub, exhibit + ".far");
            RoomSolution place = Spot(hub, exhibit + ".place");
            Assert.IsFalse(CapsuleClear(hub.SolutionFeet(place), hub.SolutionFeet(far), out _), "the screen blocks before the photo");
            Assert.IsNull(NearestTeleporter(hub.SolutionFeet(Spot(hub, exhibit + ".enter"))), "no teleporter beyond the screen yet");

            yield return RunSteps(hub, exhibit + ".pickup", exhibit + ".place", exhibit + ".far", exhibit + ".enter");
            Assert.AreEqual(Game.IndexOfKey(wing), Game.CurrentRoom, "the pasted teleporter leads to the " + wing + " wing");
            Assert.AreEqual(Game.CurrentRoom, History.LastCheckpoint.Zone, "zone entry is a checkpoint");
        }

        /// <summary>Each wing, solved from its arrival, returns to its exhibit's marker facing the centre and marks it solved.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator Wings_SolvedReturnToHubAndMarkExhibit([Values("stairs", "camera")] string key)
        {
            RoomContext wing = Room(key);
            yield return GoTo(key);
            Assert.AreEqual(ExhibitState.Available, Hub.ExhibitOf(key == "stairs" ? HubLightTable.Wing.Stairs : HubLightTable.Wing.Camera).State);

            yield return RunAll(wing);

            int hub = Game.IndexOfKey("hub");
            Assert.AreEqual(hub, Game.CurrentRoom, "the wing's exit returns to the hub. " + State);
            bool north = key == "stairs";
            HubLightTable.Wing w = north ? HubLightTable.Wing.Stairs : HubLightTable.Wing.Camera;
            Assert.AreEqual(ExhibitState.Solved, Hub.ExhibitOf(w).State, "the exhibit is marked solved");
            Assert.IsTrue(Hub.State.IsSolved(w));
            Vector3 marker = north ? HubLightTable.NorthMarker : HubLightTable.SouthMarker;
            Vector3 local = Local(HubCtx, Player.transform.position);
            Assert.Less(Vector2.Distance(new Vector2(local.x, local.z), new Vector2(marker.x, marker.z)), 0.5f, "back on the exhibit marker. " + State);
            float facing = HubCtx.WorldYaw(north ? 180f : 0f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(Player.Yaw, facing)), 1f, "facing the hub centre");
            Assert.AreEqual(hub, History.LastCheckpoint.Zone, "hub entry is a checkpoint");
            Assert.IsFalse(History.CanUndo, "the wing's changes are sealed below the hub checkpoint");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Ending_UnlocksAfterBothWings()
        {
            RoomContext hubCtx = HubCtx;
            HubLightTable hub = Hub;
            EndingHatch hatch = hub.Hatch;
            Assert.IsFalse(hatch.IsOpen);
            Assert.IsFalse(hatch.Pod.gameObject.activeSelf, "the ending pod is parked under the hatch");

            // The closed hatch is floor: walking onto it neither falls nor teleports.
            yield return GoTo("hub");
            RoomSolution ending = Spot(hubCtx, "ending");
            var walkTo = new WalkResult();
            foreach (Vector3 v in ending.Via)
            {
                walkTo = new WalkResult();
                yield return WalkTo(hubCtx.World(v), 8f, walkTo, 0.6f);
            }
            walkTo = new WalkResult();
            yield return WalkTo(hubCtx.SolutionFeet(ending), 6f, walkTo, 0.4f);
            Assert.IsTrue(walkTo.Arrived && walkTo.Grounded, "the hatch holds the player. " + walkTo);
            Assert.AreEqual(hubCtx.Index, Game.CurrentRoom);

            hub.State.SetSolved(HubLightTable.Wing.Stairs);
            yield return Seconds(1f);
            Assert.IsFalse(hatch.IsOpen, "one wing is not enough");

            yield return GoTo("hub");
            hub.State.SetSolved(HubLightTable.Wing.Camera);
            Assert.IsTrue(hatch.IsOpen, "both wings solved: the hatch opens");
            yield return Seconds(EndingHatch.LeafSeconds + EndingHatch.RiseSeconds + 0.5f);
            Assert.IsTrue(hatch.Risen);
            Assert.AreEqual(0f, Local(hubCtx, hatch.Pod.transform.position).y, 0.01f, "the pod stands on the floor");

            yield return RunSteps(hubCtx, "ending");
            RoomContext gallery = Room("gallery");
            Assert.AreEqual(gallery.Index, Game.CurrentRoom);
            yield return RunAll(gallery);
        }
    }
}
