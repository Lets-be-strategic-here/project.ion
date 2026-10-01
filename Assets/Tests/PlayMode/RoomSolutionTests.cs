using System.Collections;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels;
using Ion.Levels.Rooms;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ion.Tests.PlayMode
{
    /// <summary>
    /// The wings and the gallery solved through the player's own code paths (switches, pickups, PhotoHolder raise /
    /// rotate / place, InstantCamera, CharacterController walking), then checked physically.
    /// </summary>
    public sealed class RoomSolutionTests : IonPlayTestBase
    {
        // ------------------------------------------------------------------ stairs wing

        [UnityTest]
        public IEnumerator Stairs_GateNeedsTheButton()
        {
            RoomContext room = Room("stairs");
            yield return GoTo("stairs");
            Assert.IsFalse(SwitchBoard.Get(StairsWing.GateChannel), "the gate starts closed");

            var walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 0f, 0f), 6f, walk);
            Assert.IsFalse(walk.Arrived, "the closed gate must block. " + walk);
            Assert.Less(Local(room, walk.End).z, StairsWing.GateZ, "stayed behind the gate. " + walk);

            yield return RunSteps(room, "press");
            Assert.IsTrue(SwitchBoard.Get(StairsWing.GateChannel));
            walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 0f, -5.5f), 6f, walk);
            Assert.IsTrue(walk.Arrived, "in front of the gate. " + walk + " " + State);
            walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 0f, 0f), 8f, walk);
            Assert.IsTrue(walk.Arrived, "the open gate lets the player through. " + walk + " " + State);

            // R undoes the press (anywhere): the gate closes again behind the player.
            Assert.AreEqual(RewindResult.Undid, Dbg.TryRewind());
            yield return Seconds(1f);
            Assert.IsFalse(SwitchBoard.Get(StairsWing.GateChannel));
        }

        [UnityTest]
        public IEnumerator Stairs_WrongRollIsNotClimbable([Values(0f, -90f, 180f)] float roll)
        {
            RoomContext room = Room("stairs");
            PhotoData stairs = ShotPhoto(room);
            Dbg.GiveAll("");
            yield return GoTo("stairs:place");
            yield return RaiseRotatePlace(stairs, roll);

            var walk = new WalkResult();
            yield return WalkTo(W(room, 0f, StairsWing.LedgeTop, 16f), 8f, walk);
            Assert.Less(walk.MaxY, 3f, "a wrongly rotated stairs photo (roll " + roll + ") should not get the player onto the 4 m ledge. " + walk);

            // Rewind gives the photo back for another try (recovering a fall first, if the walk ended in one).
            Assert.AreEqual(RewindResult.Undid, UndoLast());
            yield return null;
            Assert.IsTrue(Inventory.Contains(stairs));
            Assert.AreEqual(0, Projection.PlacementCount);
        }

        [UnityTest]
        public IEnumerator Stairs_UprightPhotoMakesClimbableSteps()
        {
            RoomContext room = Room("stairs");
            PhotoData stairs = ShotPhoto(room);
            RoomSolution place = Spot(room, "place");
            Assert.AreEqual(90f, place.Roll, "the intended solution is a 90° (Q) rotation");

            Dbg.GiveAll("");
            yield return GoTo("stairs:place");

            var walk = new WalkResult();
            yield return WalkTo(W(room, 0f, StairsWing.LedgeTop, 16f), 6f, walk);
            Assert.Less(walk.MaxY, 1f, "the ledge must not be climbable without the photo. " + walk);

            yield return GoTo("stairs:place");
            yield return RaiseRotatePlace(stairs, place.Roll);

            var fails = new List<string>();
            for (int i = 0; i < StairsWing.StairSteps; i++)
            {
                float z = StairsWing.StairFoot + (i + 0.5f) * StairsWing.Tread;
                float want = StairsWing.FloorAt(z);
                fails.AddRange(ProbeFloor(W(room, 0f, 0f, z), W(room, 0f, 0f, z), 1f, p => want, 0.12f));
                fails.AddRange(ProbeFloor(W(room, 0.6f, 0f, z), W(room, 0.6f, 0f, z), 1f, p => want, 0.12f));
            }
            fails.AddRange(ProbeFloor(W(room, 0f, 0f, 0.5f), W(room, 0f, 0f, 2.5f), 0.5f, p => 0f, 0.3f));
            fails.AddRange(ProbeFloor(W(room, 0f, 0f, 11.3f), W(room, 0f, 0f, 16f), 0.5f, p => StairsWing.LedgeTop, 0.3f));
            Assert.IsEmpty(fails, "stairs:\n" + string.Join("\n", fails));

            yield return RunSteps(room, "far", "exit");
            int hub = Game.IndexOfKey("hub");
            Assert.AreEqual(hub, Game.CurrentRoom, "the exit returns to the hub");
        }

        // ------------------------------------------------------------------ camera wing

        [UnityTest]
        public IEnumerator Camera_SnapRampAndPasteAtCliff()
        {
            RoomContext room = Room("camera");
            yield return GoTo("camera");
            Assert.IsFalse(Cam.Unlocked);

            var walk = new WalkResult();
            yield return WalkTo(W(room, 0f, CameraWing.CliffTop, 22f), 6f, walk);
            Assert.Less(walk.MaxY, 1f, "the cliff must not be climbable without the photo. " + walk);

            yield return GoTo("camera");
            yield return RunSteps(room, "pickup");
            Assert.IsTrue(Cam.Unlocked, "the camera stand should unlock the camera");
            Assert.AreEqual(3, Cam.Film);

            yield return RunSteps(room, "snap");
            PhotoData snap = LatestSnapshot();
            Assert.IsNotNull(snap, "snapshot. " + State);
            Assert.AreEqual(2, Cam.Film);
            Assert.AreEqual(InstantCamera.CaptureFovY, snap.FovY, 1e-3f, "snapshots use the photo shape");

            yield return RunSteps(room, "place");
            var fails = new List<string>();
            foreach (float x in new[] { -0.8f, 0f, 0.8f })
                fails.AddRange(ProbeFloor(W(room, x, 0f, 8.5f), W(room, x, 0f, 22f), 0.5f,
                                          p => CameraWing.FloorAt(Local(room, p).z), 0.3f));
            Assert.IsEmpty(fails, "ramp:\n" + string.Join("\n", fails));

            yield return RunSteps(room, "far", "exit");
            Assert.AreEqual(Game.IndexOfKey("hub"), Game.CurrentRoom, "the exit returns to the hub");
        }

        // ------------------------------------------------------------------ gallery

        [UnityTest]
        public IEnumerator Gallery_FinalTeleporterIsReachable()
        {
            RoomContext room = Room("gallery");
            yield return GoTo("gallery");
            Teleporter tp = NearestTeleporter(room.SolutionFeet(Spot(room, "exit")));
            Assert.IsNotNull(tp);
            int fired = tp.FireCount;

            // The frames hang the game's photos: T1, T2, the Stairs wing photo and the two hub keys.
            var curator = room.WorldRoot.GetComponent<GalleryCurator>();
            Assert.IsNotNull(curator, "the gallery has no curator");
            curator.Refresh();
            Assert.AreEqual(8, curator.Hung.Count);
            foreach (int i in new[] { 0, 1, 2, 4, 5 })
                Assert.IsNotNull(curator.Hung[i], "frame " + i + " should hold a photo");
            Assert.IsNull(curator.Hung[3], "no snapshot taken yet");
            Assert.IsNull(curator.Hung[6]);

            yield return RunAll(room);
            Assert.Greater(tp.FireCount, fired, "the final teleporter never fired. " + State);
            Assert.Less(Vector3.Distance(Player.transform.position, room.Spawn.position), 1f, "the final teleporter loops back to the gallery entrance");
            Assert.AreEqual(room.Index, Game.CurrentRoom);
        }
    }
}
