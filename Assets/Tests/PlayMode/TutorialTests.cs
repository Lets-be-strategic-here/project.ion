using System.Collections;
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
    /// The tutorial (art bible §7.1, §7.2), run as its own script: T1's and T2's Room.Solutions in order. The final
    /// integration gate: an unreachable ledge, the forced fall and R, the button that wakes up, the photo placed from
    /// the marker, the trap, R (nothing to rewind), R R (back to the checkpoint), the rolled door, then the hub.
    /// </summary>
    public sealed class TutorialTests : IonPlayTestBase
    {
        [UnityTest, Timeout(900000)]
        public IEnumerator Tutorial_FullSequenceSolvable()
        {
            RoomContext t1 = Room("t1"), t2 = Room("t2");
            var ledge = (TutorialLedge)t1.Room;
            var darkroom = (TutorialDarkroom)t2.Room;
            Assert.AreEqual(0, Game.CurrentRoom, "the game starts in T1");
            Assert.IsNotNull(History.LastCheckpoint, "the start is a checkpoint");
            Assert.IsFalse(ledge.Button.Powered, "the T1 button sleeps until the fall");
            Assert.AreEqual(0, Inventory.Count);

            foreach (RoomSolution s in t1.Room.Solutions)
            {
                yield return RunStep(t1, s);
                switch (s.Name)
                {
                    case "bridge":
                        Assert.IsTrue(ledge.Collapse.Fired, "the bridge gave way");
                        Assert.IsFalse(ledge.Button.Powered, "still asleep while falling");
                        break;
                    case "rewind.fall":
                        yield return Seconds(0.8f);
                        Assert.IsTrue(ledge.Button.Powered, "the rewind out of the fall wakes the button. " + State);
                        Assert.AreEqual(0, Projection.PlacementCount, "a fall recovery changes no world state");
                        break;
                    case "press":
                        Assert.IsTrue(SwitchBoard.Get(TutorialLedge.SpanChannel), "the span is out");
                        Assert.IsFalse(ledge.Span.IsMoving);
                        break;
                    case "pickup":
                        Assert.IsTrue(Inventory.Contains(ledge.StairShot.Photo), "holding the stair photo");
                        break;
                    case "place":
                        Assert.IsTrue(GroundAt(W(t1, 0f, 0f, 13.25f), 4f, 6f, out RaycastHit h) && h.point.y > 1f, "the stair is pasted");
                        break;
                }
            }

            Assert.AreEqual(t2.Index, Game.CurrentRoom, "T1's exit leads to T2. " + State);
            Assert.AreEqual(t2.Index, History.LastCheckpoint.Zone, "arriving in T2 is a checkpoint");
            int depthAtEntry = History.LastCheckpoint.HistoryDepth;
            yield return Frames(20);

            foreach (RoomSolution s in t2.Room.Solutions)
            {
                yield return RunStep(t2, s);
                switch (s.Name)
                {
                    case "trap":
                        Assert.IsTrue(darkroom.Trap.Fired, "the trap fired");
                        Assert.IsFalse(History.CanUndo, "nothing happened since the checkpoint");
                        break;
                    case "rewind.checkpoint":
                        Assert.IsTrue(darkroom.Trap.Fired, "the trap stays open (one-way story)");
                        Assert.AreEqual(depthAtEntry, History.Depth);
                        break;
                }
            }

            int hub = Game.IndexOfKey("hub");
            Assert.AreEqual(hub, Game.CurrentRoom, "the tutorial ends in the hub. " + State);
            Vector3 local = Local(Game.Rooms[hub], Player.transform.position);
            Assert.Less(Vector2.Distance(new Vector2(local.x, local.z), new Vector2(HubLightTable.ArrivalFeet.x, HubLightTable.ArrivalFeet.z)), 1f,
                        "arrives in the W cell. " + State);
            Assert.AreEqual(hub, History.LastCheckpoint.Zone, "hub entry is a checkpoint");
        }

        /// <summary>Step 3's precondition: the button can't be pressed before the fall; after it, it can.</summary>
        [UnityTest]
        public IEnumerator Tutorial_ButtonSleepsUntilTheFallIsRewound()
        {
            RoomContext t1 = Room("t1");
            var ledge = (TutorialLedge)t1.Room;
            yield return GoTo("t1:press");
            Assert.IsNull(Ion.DebugTools.IonDebug.FindSwitchInReach(true), "no powered switch before the fall");
            Assert.IsNotNull(Ion.DebugTools.IonDebug.FindSwitchInReach(false), "but the button is there");

            yield return GoTo("t1");
            yield return RunSteps(t1, "bridge", "rewind.fall");
            yield return Seconds(0.8f);
            Assert.IsTrue(ledge.Button.Powered);
            yield return RunSteps(t1, "press");

            // The press is a world change: R anywhere undoes it and the span slides back.
            yield return GoTo("t1:wall");
            Assert.AreEqual(RewindResult.Nothing, Dbg.TryRewind(), "GoTo re-entered T1: a fresh checkpoint");
            yield return GoTo("t1:press");
            Assert.IsNotNull(Dbg.TryPress());
            Assert.IsFalse(SwitchBoard.Get(TutorialLedge.SpanChannel), "pressing again retracts the span");
            var walk = new WalkResult();
            yield return WalkTo(W(t1, 0f, 0f, 14f), 6f, walk);
            Assert.AreEqual(RewindResult.Undid, Dbg.TryRewind(), "R from elsewhere undoes the last press");
            yield return Seconds(1f);
            Assert.IsTrue(SwitchBoard.Get(TutorialLedge.SpanChannel), "the span is out again");
            Assert.IsTrue(ledge.Button.Powered, "the wake-up is a one-way story beat");
        }

        /// <summary>The sideways door photo placed at the wrong roll cannot be walked through; R gives it back.</summary>
        [UnityTest]
        public IEnumerator Darkroom_WrongRollDoorIsNotWalkable([Values(0f, -90f, 180f)] float roll)
        {
            RoomContext t2 = Room("t2");
            PhotoData door = ShotPhoto(t2);
            Dbg.GiveAll("");
            yield return GoTo("t2:place");
            yield return RaiseRotatePlace(door, roll);

            var walk = new WalkResult();
            yield return WalkTo(W(t2, 0f, 0f, 23f), 8f, walk);
            bool through = walk.Arrived && Mathf.Abs(walk.End.y) < 0.3f;
            Assert.IsFalse(through, "a door pasted at roll " + roll + " should not lead through the wall. " + walk);

            Assert.AreEqual(RewindResult.Undid, UndoLast(), "R anywhere (after recovering a fall, if any) undoes it. " + State);
            yield return Frames(30);
            Assert.AreEqual(0, Projection.PlacementCount);
            Assert.IsTrue(Inventory.Contains(door));
        }

        [UnityTest]
        public IEnumerator Darkroom_RolledDoorPhotoCutsAWalkableDoor()
        {
            RoomContext t2 = Room("t2");
            PhotoData door = ShotPhoto(t2);
            RoomSolution place = Spot(t2, "place");
            yield return GoTo("t2:place");

            Assert.IsFalse(CapsuleClear(W(t2, 0f, 0f, 16f), W(t2, 0f, 0f, 23f), out RaycastHit wall), "the wall should block before");

            Dbg.GiveAll("");
            yield return RaiseRotatePlace(door, place.Roll);

            Assert.IsTrue(CapsuleClear(W(t2, 0f, 0f, 16f), W(t2, 0f, 0f, 23f), out RaycastHit block),
                          "the doorway is blocked by " + (block.collider != null ? block.collider.name : "") + " at " + block.point);
            var holes = ProbeFloor(W(t2, 0f, 0f, 16f), W(t2, 0f, 0f, 25f), 0.5f, p => 0f, 0.3f);
            Assert.IsEmpty(holes, "doorway floor:\n" + string.Join("\n", holes));

            yield return RunSteps(t2, "far", "exit");
            Assert.AreEqual(Game.IndexOfKey("hub"), Game.CurrentRoom);
        }
    }
}
