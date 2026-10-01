using System;
using System.Collections;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Levels;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ion.Tests.PlayMode
{
    /// <summary>
    /// Every room solved through the player's own code paths (pickups, PhotoHolder raise / rotate /
    /// place, InstantCamera, CharacterController walking), then checked physically.
    /// </summary>
    public sealed class RoomSolutionTests : IonPlayTestBase
    {
        // ------------------------------------------------------------------ expected floors (room-local z → y)

        internal static float BridgeFloor(float z) => 0f;

        internal static float DoorwayFloor(float z) => 0f;

        internal const int StairSteps = 16;
        internal const float StairRise = 0.25f, StairDepth = 0.45f, StairFront = 11f - StairSteps * StairDepth;

        internal static float StairsFloor(float z)
        {
            if (z < StairFront) return 0f;
            if (z >= 11f) return StairSteps * StairRise;
            int i = Mathf.Clamp(Mathf.FloorToInt((z - StairFront) / StairDepth), 0, StairSteps - 1);
            return (i + 1) * StairRise;
        }

        internal static float CameraFloor(float z)
        {
            if (z <= 14f) return 0f;
            if (z >= 21f) return 3f;
            return (z - 14f) / 7f * 3f;
        }

        // ------------------------------------------------------------------ room 1: bridge

        [UnityTest]
        public IEnumerator Bridge_PickupPlaceCrossAndExit()
        {
            RoomContext room = Room("bridge");
            PhotoData bridge = ShotPhoto(room);

            // Before: the chasm is empty and the photo sits on its pedestal.
            Assert.IsFalse(GroundAt(W(room, 0f, 0f, 9.5f), 3f, 40f, out _), "the chasm should start empty");
            yield return GoTo("bridge:spawn");
            var walk = new WalkResult();
            yield return WalkTo(W(room, -3f, 0f, -4f), 8f, walk, 1.2f);
            Assert.IsTrue(Inventory.Contains(bridge), "walking into the pedestal should collect the Bridge photo. " + walk + " " + State);

            // Negative control: without the photo the walk ends in the chasm.
            yield return GoTo("bridge:place");
            walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 0f, 17f), 8f, walk);
            Assert.IsFalse(walk.Arrived, "the chasm must not be crossable without the photo. " + walk);
            Assert.AreEqual(1, walk.Respawns, "falling into the chasm respawns at the checkpoint. " + walk);

            yield return GoTo("bridge:place");
            yield return RaiseRotatePlace(bridge, 0f);

            List<string> holes = new List<string>();
            foreach (float x in new[] { -1.2f, 0f, 1.2f })
                holes.AddRange(ProbeFloor(W(room, x, 0f, 2f), W(room, x, 0f, 17f), 0.5f, p => 0f, 0.3f));
            Assert.IsEmpty(holes, "bridge floor:\n" + string.Join("\n", holes));

            Assert.IsTrue(CapsuleClear(W(room, 0f, 0f, 2.5f), W(room, 0f, 0f, 17f), out RaycastHit block),
                          "something blocks the bridge: " + (block.collider != null ? block.collider.name : "") + " at " + block.point);

            walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 0f, 17f), 10f, walk);
            Assert.IsTrue(walk.Arrived, "could not cross the bridge. " + walk + " " + State);
            Assert.Greater(walk.MinY, -0.3f, "dipped while crossing. " + walk);
            Assert.IsTrue(walk.Grounded);

            walk = new WalkResult();
            yield return WalkTo(W(room, 13f, 0f, 15f), 10f, walk, 0.6f);
            Assert.AreEqual(1, Game.CurrentRoom, "the exit teleporter should lead to room 2. " + walk + " " + State);
        }

        // ------------------------------------------------------------------ room 2: doorway

        [UnityTest]
        public IEnumerator Doorway_SkyPhotoCutsAWalkableDoor()
        {
            RoomContext room = Room("doorway");
            PhotoData sky = ShotPhoto(room);
            yield return GoTo("doorway:place");

            Assert.IsFalse(CapsuleClear(W(room, 0f, 0f, 5f), W(room, 0f, 0f, 13f), out RaycastHit wall), "the wall should block before");
            Assert.AreEqual("Wall", wall.collider.name.Replace(" (cut)", ""), "blocked by " + wall.collider.name);

            Dbg.GiveAll("");
            yield return RaiseRotatePlace(sky, 0f);

            Assert.IsTrue(CapsuleClear(W(room, 0f, 0f, 5f), W(room, 0f, 0f, 13f), out RaycastHit block),
                          "the doorway is blocked by " + (block.collider != null ? block.collider.name : "") + " at " + block.point);
            List<string> holes = ProbeFloor(W(room, 0f, 0f, 4f), W(room, 0f, 0f, 14f), 0.5f, p => 0f, 0.3f);
            Assert.IsEmpty(holes, "doorway floor:\n" + string.Join("\n", holes));

            var walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 0f, 14f), 8f, walk);
            Assert.IsTrue(walk.Arrived, "could not walk through the doorway. " + walk + " " + State);
            Assert.Greater(walk.MinY, -0.3f);

            walk = new WalkResult();
            yield return WalkTo(W(room, 8.5f, 0f, 11.5f), 8f, walk, 0.6f);
            Assert.AreEqual(2, Game.CurrentRoom, "the exit teleporter should lead to room 3. " + walk + " " + State);
        }

        // ------------------------------------------------------------------ room 3: stairs

        [UnityTest]
        public IEnumerator Stairs_WrongRollIsNotClimbable([Values(0f, -90f, 180f)] float roll)
        {
            RoomContext room = Room("stairs");
            PhotoData stairs = ShotPhoto(room);
            Dbg.GiveAll("");
            yield return GoTo("stairs:place");
            yield return RaiseRotatePlace(stairs, roll);

            var walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 4f, 16f), 8f, walk);
            Assert.Less(walk.MaxY, 3f, "a wrongly rotated stairs photo (roll " + roll + ") should not get the player onto the 4 m ledge. " + walk);

            // Rewind gives the photo back for another try.
            Dbg.Rewind("");
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
            yield return WalkTo(W(room, 0f, 4f, 16f), 6f, walk);
            Assert.Less(walk.MaxY, 1f, "the ledge must not be climbable without the photo. " + walk);

            yield return GoTo("stairs:place");
            yield return RaiseRotatePlace(stairs, place.Roll);

            var fails = new List<string>();
            for (int i = 0; i < StairSteps; i++)
            {
                float z = StairFront + (i + 0.5f) * StairDepth;
                fails.AddRange(ProbeFloor(W(room, 0f, 0f, z), W(room, 0f, 0f, z), 1f, p => StairsFloor(z), 0.12f));
                fails.AddRange(ProbeFloor(W(room, 1f, 0f, z), W(room, 1f, 0f, z), 1f, p => StairsFloor(z), 0.12f));
            }
            fails.AddRange(ProbeFloor(W(room, 0f, 0f, 0f), W(room, 0f, 0f, 3.5f), 0.5f, p => 0f, 0.3f));
            fails.AddRange(ProbeFloor(W(room, 0f, 0f, 11.3f), W(room, 0f, 0f, 16f), 0.5f, p => 4f, 0.3f));
            Assert.IsEmpty(fails, "stairs:\n" + string.Join("\n", fails));

            walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 4f, 16f), 10f, walk);
            Assert.IsTrue(walk.Arrived, "could not climb the stairs. " + walk + " " + State);
            Assert.AreEqual(4f, walk.End.y, 0.15f, "should stand on the ledge");

            walk = new WalkResult();
            yield return WalkTo(W(room, 10f, 4f, 13f), 8f, walk, 0.6f);
            Assert.AreEqual(3, Game.CurrentRoom, "the exit teleporter should lead to room 4. " + walk + " " + State);
        }

        // ------------------------------------------------------------------ room 4: camera

        [UnityTest]
        public IEnumerator Camera_SnapRampAndPasteAtCliff()
        {
            RoomContext room = Room("camera");
            yield return GoTo("camera:spawn");
            Assert.IsFalse(Cam.Unlocked);

            var walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 3f, 22f), 6f, walk);
            Assert.Less(walk.MaxY, 1f, "the cliff must not be climbable without the photo. " + walk);

            yield return GoTo("camera:spawn");
            walk = new WalkResult();
            yield return WalkTo(room.SolutionFeet(Spot(room, "pickup")), 6f, walk, 0.8f);
            Assert.IsTrue(Cam.Unlocked, "walking into the floating camera should unlock it. " + walk);
            Assert.AreEqual(3, Cam.Film);

            yield return GoTo("camera:snap");
            PhotoData snap = Dbg.TrySnap();
            Assert.IsNotNull(snap, "snapshot failed. " + State);
            Assert.AreEqual(2, Cam.Film);
            Assert.Greater(snap.PieceCount, 0);
            Assert.AreEqual(InstantCamera.CaptureFovY, snap.FovY, 1e-3f, "snapshots use the photo shape");
            Assert.IsTrue(Inventory.Contains(snap));

            yield return GoTo("camera:place");
            yield return RaiseRotatePlace(snap, 0f);

            var fails = new List<string>();
            foreach (float x in new[] { -0.8f, 0f, 0.8f })
                fails.AddRange(ProbeFloor(W(room, x, 0f, 8f), W(room, x, 0f, 22f), 0.5f,
                                          p => CameraFloor(room.WorldRoot.InverseTransformPoint(p).z), 0.3f));
            Assert.IsEmpty(fails, "ramp:\n" + string.Join("\n", fails));

            walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 3f, 22f), 10f, walk);
            Assert.IsTrue(walk.Arrived, "could not climb the pasted ramp. " + walk + " " + State);
            Assert.AreEqual(3f, walk.End.y, 0.15f);

            walk = new WalkResult();
            yield return WalkTo(W(room, 10.5f, 3f, 15.5f), 8f, walk, 0.6f);
            Assert.AreEqual(4, Game.CurrentRoom, "the exit teleporter should lead to room 5. " + walk + " " + State);
        }

        // ------------------------------------------------------------------ room 5: gallery

        [UnityTest]
        public IEnumerator Gallery_FinalTeleporterIsReachable()
        {
            RoomContext room = Room("gallery");
            yield return GoTo("gallery:spawn");
            var tp = FindTeleporter(room);
            int fired = tp.FireCount;

            // The frames hang the player's photos: the three pre-made ones on the left wall.
            var curator = room.WorldRoot.GetComponent<GalleryCurator>();
            Assert.IsNotNull(curator, "the gallery has no curator");
            curator.Refresh();
            Assert.AreEqual(6, curator.Hung.Count);
            for (int i = 0; i < 3; i++)
                Assert.IsNotNull(curator.Hung[i], "frame " + i + " should hold room " + (i + 1) + "'s photo");

            var walk = new WalkResult();
            yield return WalkTo(room.SolutionFeet(Spot(room, "exit")), 12f, walk, 0.6f);
            Assert.Greater(tp.FireCount, fired, "the final teleporter never fired. " + walk + " " + State);
            Vector3 spawn = room.Spawn.position;
            Assert.Less(Vector3.Distance(Player.transform.position, spawn), 1f, "the final teleporter loops back to the gallery spawn");
            Assert.AreEqual(4, Game.CurrentRoom);
        }

        internal static Teleporter FindTeleporter(RoomContext room)
        {
            foreach (var t in room.WorldRoot.GetComponentsInChildren<Teleporter>(true))
                return t;
            Assert.Fail(room.Room.Title + " has no teleporter");
            return null;
        }
    }
}
