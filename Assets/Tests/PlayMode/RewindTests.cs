using System.Collections;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels;
using Ion.Presentation.Motion;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ion.Tests.PlayMode
{
    /// <summary>
    /// A small self-contained world for the rewind / checkpoint / switch tests (Lead D). It does not load
    /// Main.unity, so it is independent of the rooms being rebuilt:
    ///  * floor A: x[-4, 4] z[0, 10], top y = 0 (the player starts at (0, 0, 2) facing +Z);
    ///  * a 4 m gap z[10, 14] over the void;
    ///  * floor B: x[-4, 4] z[14, 24];
    ///  * a "bridge" diorama at x = 200 (one floor z[0, 24]): its photo, placed from (0, 0, 2) facing +Z,
    ///    pastes a floor across the gap.
    /// The pointer lock and the raise / click devices are simulated (<see cref="PointerLock.Simulate"/>,
    /// <see cref="IonInput.UseSimulatedDevices"/>). Time runs at a fixed 60 Hz.
    /// </summary>
    public abstract class StateTestFixture
    {
        protected const float Step = 1f / 60f;
        protected static readonly Vector3 StartFeet = new Vector3(0f, 0f, 2f);

        static int s_SceneSerial;
        Scene _scene;

        protected FirstPersonController Player;
        protected PhotoInventory Inventory;
        protected PhotoHolder Holder;
        protected InstantCamera InstantCam;
        protected SafePoseTracker Tracker;
        protected RewindController Rewinder;
        protected Transform World;
        protected PhotoData BridgePhoto;

        protected WorldHistory History => WorldHistory.Instance;
        protected ProjectionSystem Projection => ProjectionSystem.Instance;

        [UnitySetUp]
        public IEnumerator BuildWorld()
        {
            Time.captureDeltaTime = Step;
            _scene = SceneManager.CreateScene("ion-state-test-" + (++s_SceneSerial));
            SceneManager.SetActiveScene(_scene);
            var others = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s != _scene && s.isLoaded) others.Add(s);
            }
            for (int i = 0; i < others.Count; i++)
                yield return SceneManager.UnloadSceneAsync(others[i]);

            SwitchBoard.Restore(null);
            PointerLock.Simulate(true);
            IonInput.UseSimulatedDevices = true;
            IonInput.SimulatedRaise = false;
            IonInput.SimulatedPrimaryDown = false;
            IonInput.SimulatedLook = Vector2.zero;

            World = new GameObject("TestWorld").transform;
            Geo.Box(World, new Vector3(0f, -0.25f, 5f), new Vector3(8f, 0.5f, 10f), Color.gray).name = "FloorA";
            Geo.Box(World, new Vector3(0f, -0.25f, 19f), new Vector3(8f, 0.5f, 10f), Color.gray).name = "FloorB";

            // Bridge diorama, captured from the same relative pose as the placement.
            var diorama = new GameObject("BridgeDiorama").transform;
            diorama.position = new Vector3(200f, 0f, 0f);
            Geo.Box(diorama, new Vector3(0f, -0.25f, 12f), new Vector3(8f, 0.5f, 24f), Color.gray).name = "DioramaFloor";
            Physics.SyncTransforms();
            BridgePhoto = Projection.Capture(new Pose(new Vector3(200f, PlayerFactory.EyeHeight, 2f), Quaternion.identity),
                                             50f, 4f / 3f, "bridge");
            Assert.IsNotNull(BridgePhoto, "bridge photo");
            diorama.gameObject.SetActive(false);

            Player = PlayerFactory.Create(StartFeet, 0f);
            Inventory = Player.GetComponent<PhotoInventory>();
            Holder = Player.GetComponent<PhotoHolder>();
            InstantCam = Player.GetComponent<InstantCamera>();
            Tracker = Player.GetComponent<SafePoseTracker>();
            Rewinder = Player.GetComponent<RewindController>();
            yield return Frames(20);
            Assert.IsTrue(Player.IsGrounded, "player settles on floor A");
            Assert.IsNotNull(History.LastCheckpoint, "a start checkpoint is taken once grounded");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            PointerLock.EndSimulation();
            IonInput.UseSimulatedDevices = false;
            IonInput.SimulatedRaise = false;
            IonInput.SimulatedPrimaryDown = false;
            SwitchBoard.Restore(null);
            if (_scene.IsValid() && _scene.isLoaded)
                foreach (var go in _scene.GetRootGameObjects()) Object.Destroy(go);
            yield return null;
        }

        // ------------------------------------------------------------------ helpers

        protected static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        protected static IEnumerator Seconds(float s) => Frames(Mathf.CeilToInt(s / Step));

        protected string State =>
            $"feet {Player.transform.position} grounded {Player.IsGrounded} depth {History.Depth} " +
            $"placements {Projection.PlacementCount} photos {Inventory.Count} cp {History.LastCheckpoint}";

        /// <summary>Walks (scripted, like held keys) to a feet position on the floor and waits to arrive.</summary>
        protected IEnumerator WalkTo(Vector3 feet, float maxSeconds = 8f)
        {
            Player.ScriptedWalkTo(feet, maxSeconds, 0.2f);
            int guard = Mathf.CeilToInt(maxSeconds / Step) + 5;
            while (Player.IsScriptedWalking && guard-- > 0) yield return null;
            yield return Frames(15); // settle + a safe-pose sample
        }

        /// <summary>Places the bridge photo from the start spot (Holder.Place: the automation path).</summary>
        protected IEnumerator PlaceBridge()
        {
            Player.Teleport(StartFeet, 0f);
            Player.SetLook(0f, 0f);
            yield return Frames(15);
            if (!Inventory.Contains(BridgePhoto)) Inventory.Add(BridgePhoto);
            Inventory.SelectedIndex = Inventory.IndexOf(BridgePhoto);
            Assert.IsTrue(Holder.Raise(), "raise");
            yield return null;
            int before = Projection.PlacementCount;
            Assert.IsTrue(Holder.Place(), "place. " + State);
            Assert.AreEqual(before + 1, Projection.PlacementCount);
            yield return Frames(3);
            Physics.SyncTransforms();
        }

        protected static bool FloorAt(Vector3 p) =>
            Physics.Raycast(new Vector3(p.x, 2f, p.z), Vector3.down, 3f, ~ProjectionSystem.ExcludedLayerMask, QueryTriggerInteraction.Ignore);

        /// <summary>A pedestal button (Switch) at <paramref name="feet"/> on channel <paramref name="channel"/>, with a cap.</summary>
        protected Switch MakeButton(Vector3 feet, string channel)
        {
            var go = new GameObject("Button " + channel);
            go.transform.SetParent(World, false);
            go.transform.position = feet;
            var body = Geo.Box(go.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 1f, 0.5f), Color.white);
            body.name = "Pedestal";
            var cap = Geo.Box(go.transform, new Vector3(0f, 1.03f, 0f), new Vector3(0.25f, 0.0625f, 0.25f), Color.cyan);
            cap.name = "Cap";
            var sw = go.AddComponent<Switch>();
            sw.Channel = channel;
            sw.Cap = cap.transform;
            return sw;
        }

        /// <summary>A sliding bridge (Mover) that fills the gap z[10, 14] when its channel is on.</summary>
        protected Mover MakeGapBridge(string channel)
        {
            var go = new GameObject("GapBridge");
            go.transform.SetParent(World, false);
            Geo.Box(go.transform, new Vector3(0f, -0.125f, 0f), new Vector3(2f, 0.25f, 4f), Color.yellow).name = "Deck";
            var mover = go.AddComponent<Mover>();
            mover.Channel = channel;
            mover.OffLocal = new Vector3(-6f, 0f, 12f);  // stowed beside the gap
            mover.OnLocal = new Vector3(0f, 0f, 12f);    // spanning it
            go.transform.localPosition = mover.OffLocal;
            return mover;
        }
    }

    /// <summary>Art bible §11.3 (Lead D): rewind anywhere, nothing-to-rewind, falls, checkpoints, double R.</summary>
    public sealed class RewindTests : StateTestFixture
    {
        [UnityTest]
        public IEnumerator Rewind_AnywhereUndoesLast_Placement()
        {
            yield return PlaceBridge();
            Assert.IsFalse(Inventory.Contains(BridgePhoto), "the photo is consumed");
            Assert.IsTrue(FloorAt(new Vector3(0f, 0f, 12f)), "the bridge spans the gap");

            // Walk across and stand on floor B, long after the placement.
            yield return WalkTo(new Vector3(0f, 0f, 18f));
            Assert.Greater(Player.transform.position.z, 17f, "crossed on the pasted bridge. " + State);
            yield return Seconds(1f);

            RewindResult r = History.RewindOnce();
            yield return Frames(5);
            Assert.AreEqual(RewindResult.Undid, r);
            Assert.AreEqual(0, Projection.PlacementCount, "the placement is undone");
            Assert.IsTrue(Inventory.Contains(BridgePhoto), "the photo is back in the hand");
            Assert.IsFalse(FloorAt(new Vector3(0f, 0f, 12f)), "the gap is open again");
            Assert.Greater(Player.transform.position.z, 17f, "still supported on floor B: the player stays. " + State);
            Assert.IsTrue(Player.IsGrounded);
        }

        [UnityTest]
        public IEnumerator Rewind_AnywhereUndoesLast_Placement_MovesUnsupportedPlayerToSafety()
        {
            yield return PlaceBridge();
            yield return WalkTo(new Vector3(0f, 0f, 12f)); // standing on the pasted part, over the gap
            Assert.AreEqual(12f, Player.transform.position.z, 0.5f, State);

            // The full player path: R, with its transition.
            Rewinder.PressRewind();
            yield return Seconds(1.2f);
            Assert.IsFalse(Rewinder.Busy);
            Assert.AreEqual(RewindResult.Undid, Rewinder.LastResult);
            Assert.AreEqual(0, Projection.PlacementCount);
            Vector3 feet = Player.transform.position;
            Assert.Less(feet.z, 10f, "moved off the vanished bridge to where the photo was placed from. " + State);
            Assert.IsTrue(Player.IsGrounded, "on solid ground. " + State);
            Assert.AreEqual(0, Player.RespawnCount, "no fall");
        }

        [UnityTest]
        public IEnumerator Rewind_AnywhereUndoesLast_Capture()
        {
            InstantCam.SetUnlockedSilently(true);
            InstantCam.Film = 3;
            Player.SetLook(0f, 0f);
            yield return null;
            PhotoData snap = InstantCam.TryCapture();
            Assert.IsNotNull(snap);
            Assert.AreEqual(2, InstantCam.Film);
            Assert.IsTrue(Inventory.Contains(snap));

            yield return WalkTo(new Vector3(2f, 0f, 7f));
            yield return Seconds(0.5f);
            Assert.AreEqual(RewindResult.Undid, History.RewindOnce());
            Assert.AreEqual(3, InstantCam.Film, "the film comes back");
            Assert.IsFalse(Inventory.Contains(snap), "the snapshot is gone");
            Assert.AreEqual(2f, Player.transform.position.x, 0.4f, "supported: stays where it was rewound from");
        }

        [UnityTest]
        public IEnumerator Rewind_AnywhereUndoesLast_Switch()
        {
            Switch button = MakeButton(new Vector3(-3f, 0f, 4f), "test.span");
            Mover bridge = MakeGapBridge("test.span");
            yield return Frames(2);
            Assert.IsTrue(button.Press());
            Assert.IsTrue(SwitchBoard.Get("test.span"));
            yield return Seconds(Feel.MoverSeconds(6f) + 0.3f); // 6 m of travel: 1.6 s per 4 m
            Assert.AreEqual(bridge.OnLocal.x, bridge.transform.localPosition.x, 0.01f, "the bridge slid in");

            yield return WalkTo(new Vector3(2f, 0f, 6f));
            Assert.AreEqual(RewindResult.Undid, History.RewindOnce());
            Assert.IsFalse(SwitchBoard.Get("test.span"), "the channel is off again");
            yield return Seconds(0.6f); // rewind speed: 0.35 s
            Assert.AreEqual(bridge.OffLocal.x, bridge.transform.localPosition.x, 0.01f, "the bridge slid back");
        }

        [UnityTest]
        public IEnumerator Rewind_AnywhereUndoesLast_Pickup()
        {
            var go = new GameObject("Pickup");
            go.transform.SetParent(World, false);
            go.transform.position = new Vector3(2f, 0f, 6f);
            var pickup = go.AddComponent<PhotoPickup>();
            pickup.Photo = BridgePhoto;
            yield return Frames(2);

            yield return WalkTo(new Vector3(2f, 0f, 6f));
            Assert.IsTrue(pickup.Collected, "walked into it. " + State);
            Assert.IsTrue(Inventory.Contains(BridgePhoto));

            yield return WalkTo(new Vector3(-2f, 0f, 3f));
            Assert.AreEqual(RewindResult.Undid, History.RewindOnce());
            yield return null;
            Assert.IsFalse(pickup.Collected);
            Assert.IsTrue(pickup.gameObject.activeSelf, "the photo lies where it was");
            Assert.IsFalse(Inventory.Contains(BridgePhoto));
        }

        [UnityTest]
        public IEnumerator Rewind_NothingToRewind_NoChangeAndFeedback()
        {
            var results = new List<RewindResult>();
            History.Rewound += results.Add;
            Vector3 before = Player.transform.position;
            float yaw = Player.Yaw;

            Assert.IsFalse(History.CanUndo);
            Rewinder.PressRewind();
            yield return Seconds(0.5f);

            History.Rewound -= results.Add;
            Assert.AreEqual(RewindResult.Nothing, Rewinder.LastResult);
            CollectionAssert.Contains(results, RewindResult.Nothing, "Rewound(Nothing) is raised for the gentle feedback");
            Assert.IsFalse(Rewinder.Busy, "no transition plays");
            Assert.Less(Vector3.Distance(before, Player.transform.position), 0.05f, "no camera / body motion");
            Assert.AreEqual(yaw, Player.Yaw, 0.01f);
            Assert.AreEqual(0, History.Depth);
        }

        [UnityTest]
        public IEnumerator Rewind_RecoversFromFall_NoWorldChange()
        {
            yield return PlaceBridge();
            int placements = Projection.PlacementCount;

            // Walk off the side of floor A into the void.
            Player.ScriptedWalk(new Vector2(1f, 0f), 3f);
            int guard = 400;
            while (!Tracker.IsFalling && guard-- > 0) yield return null;
            Assert.IsTrue(Tracker.IsFalling, "falling. " + State);
            Assert.AreEqual(RewindResult.RecoveredFall, History.PeekRewind());

            bool recovered = false;
            History.FallRecovered += () => recovered = true;
            Assert.AreEqual(RewindResult.RecoveredFall, History.RewindOnce());
            yield return Frames(10);
            Assert.IsTrue(recovered, "FallRecovered fires");
            Assert.AreEqual(placements, Projection.PlacementCount, "a fall recovery never undoes the world");
            Assert.AreEqual(1, History.Depth);
            Assert.IsTrue(Player.IsGrounded, "back on safe ground. " + State);
            Assert.LessOrEqual(Mathf.Abs(Player.transform.position.x), 4.05f, State);
            Assert.IsFalse(Tracker.IsFalling);
        }

        [UnityTest]
        public IEnumerator Rewind_LimboRecoversByItself()
        {
            Player.ScriptedWalk(new Vector2(1f, 0f), 3f);
            int guard = 900;
            while (!Tracker.InLimbo && guard-- > 0) yield return null;
            Assert.IsTrue(Tracker.InLimbo, "reached limbo. " + State);
            // 4 s without input, then the recovery sequence (wash, swap, Paper hold) and a settle.
            yield return Seconds(Feel.LimboAutoRecoverSeconds + Feel.RewindSeconds + 0.4f);
            Assert.IsFalse(Tracker.InLimbo, State);
            Assert.IsTrue(Player.IsGrounded, State);
            Assert.AreEqual(1, Player.RespawnCount);
        }

        [UnityTest]
        public IEnumerator Rewind_NeverCrossesCheckpoint()
        {
            Switch button = MakeButton(new Vector3(-3f, 0f, 4f), "test.cp");
            yield return null;
            Assert.IsTrue(button.Press());
            Assert.IsTrue(History.CanUndo);

            History.SetCheckpoint("marker", PlayerPose.Of(Player));
            Assert.IsFalse(History.CanUndo, "records below the checkpoint are sealed");
            Assert.AreEqual(RewindResult.Nothing, History.RewindOnce());
            Assert.IsTrue(SwitchBoard.Get("test.cp"), "the sealed change stays");
            Assert.AreEqual(1, History.Depth);
        }

        [UnityTest]
        public IEnumerator DoubleRewind_RestoresCheckpointSnapshot()
        {
            // Checkpoint state: one photo (the bridge), camera unlocked with 3 film, channel off.
            InstantCam.SetUnlockedSilently(true);
            InstantCam.Film = 3;
            Inventory.Add(BridgePhoto);
            Switch button = MakeButton(new Vector3(-3f, 0f, 4f), "test.mix");
            Mover bridge = MakeGapBridge("test.mix");
            var pickupGo = new GameObject("PickupB");
            pickupGo.transform.SetParent(World, false);
            pickupGo.transform.position = new Vector3(3.5f, 0f, 3.5f); // outside the bridge placement's frustum
            var pickup = pickupGo.AddComponent<PhotoPickup>();
            var photoB = new PhotoData { FovY = 50f, Aspect = 4f / 3f, Label = "B" };
            pickup.Photo = photoB;
            yield return Frames(2);
            PlayerPose cpPose = PlayerPose.Of(Player);
            History.SetCheckpoint("cp", cpPose);
            var snapshot = new List<PhotoData>(Inventory.Photos);
            int selected = Inventory.SelectedIndex;

            // Several mixed changes.
            yield return PlaceBridge();                                   // placement (photo consumed)
            Player.SetLook(90f, 0f);
            yield return null;
            PhotoData snap = InstantCam.TryCapture();                         // capture
            Assert.IsNotNull(snap);
            Assert.IsTrue(button.Press());                                // switch
            yield return WalkTo(new Vector3(3.5f, 0f, 3.5f));             // pickup
            Assert.IsTrue(pickup.Collected, State);
            yield return WalkTo(new Vector3(0f, 0f, 18f));                // elsewhere (over the pasted bridge)
            Assert.AreEqual(4, History.Depth - History.LastCheckpoint.HistoryDepth);

            Assert.AreEqual(RewindResult.ToCheckpoint, History.RewindToCheckpoint());
            yield return Seconds(0.2f);

            Assert.AreEqual(0, Projection.PlacementCount, "world");
            CollectionAssert.AreEqual(snapshot, new List<PhotoData>(Inventory.Photos), "inventory: same photos, same order");
            Assert.AreEqual(selected, Inventory.SelectedIndex, "selection");
            Assert.AreEqual(3, InstantCam.Film, "film");
            Assert.IsTrue(InstantCam.Unlocked, "camera unlock");
            Assert.IsFalse(SwitchBoard.Get("test.mix"), "switch state");
            Assert.AreEqual(bridge.OffLocal, bridge.transform.localPosition, "instant mover restore");
            Assert.IsFalse(pickup.Collected, "pickup back");
            Assert.Less(Vector3.Distance(cpPose.Feet, Player.transform.position), 0.3f, "pose. " + State);
            Assert.AreEqual(cpPose.Yaw, Player.Yaw, 0.5f);
            Assert.AreEqual(History.LastCheckpoint.HistoryDepth, History.Depth);
        }

        [UnityTest]
        public IEnumerator DoubleRewind_WorksWithNoChanges()
        {
            PlayerPose cpPose = History.LastCheckpoint.Pose;
            yield return WalkTo(new Vector3(-2f, 0f, 8f));
            Assert.IsFalse(History.CanUndo);
            Rewinder.PressRewind();               // nothing to rewind...
            yield return Seconds(0.15f);
            Rewinder.PressRewind();               // ...the second press escalates
            yield return Seconds(1.3f);
            Assert.AreEqual(RewindResult.ToCheckpoint, Rewinder.LastResult);
            Assert.Less(Vector3.Distance(cpPose.Feet, Player.transform.position), 0.3f, State);
        }

        [UnityTest]
        public IEnumerator DoubleRewind_SlowSecondPressIsTwoSingles()
        {
            Switch a = MakeButton(new Vector3(-3f, 0f, 4f), "test.a");
            Switch b = MakeButton(new Vector3(3f, 0f, 4f), "test.b");
            yield return null;
            Assert.IsTrue(a.Press());
            Assert.IsTrue(b.Press());
            yield return WalkTo(new Vector3(-1.5f, 0f, 8f));
            Vector3 there = Player.transform.position;

            Rewinder.PressRewind();
            yield return Seconds(0.5f);           // outside the 0.35 s window
            Rewinder.PressRewind();
            yield return Seconds(1.2f);

            Assert.AreEqual(RewindResult.Undid, Rewinder.LastResult, "the second press is a single rewind");
            Assert.AreEqual(2, Rewinder.RewindCount);
            Assert.IsFalse(SwitchBoard.Get("test.a"));
            Assert.IsFalse(SwitchBoard.Get("test.b"));
            Assert.Less(Vector3.Distance(there, Player.transform.position), 0.3f, "not sent to the checkpoint. " + State);
        }

        [UnityTest]
        public IEnumerator DoubleRewind_FastSecondPressEscalates()
        {
            Switch a = MakeButton(new Vector3(-3f, 0f, 4f), "test.f1");
            Switch b = MakeButton(new Vector3(3f, 0f, 4f), "test.f2");
            yield return null;
            PlayerPose cpPose = History.LastCheckpoint.Pose;
            Assert.IsTrue(a.Press());
            Assert.IsTrue(b.Press());
            yield return WalkTo(new Vector3(-1.5f, 0f, 8f));

            Rewinder.PressRewind();
            yield return Seconds(0.2f);           // inside the window
            Rewinder.PressRewind();
            yield return Seconds(1.3f);

            Assert.AreEqual(RewindResult.ToCheckpoint, Rewinder.LastResult);
            Assert.IsFalse(SwitchBoard.Get("test.a") || SwitchBoard.Get("test.f1") || SwitchBoard.Get("test.f2"));
            Assert.Less(Vector3.Distance(cpPose.Feet, Player.transform.position), 0.3f, State);
        }

        [UnityTest]
        public IEnumerator PointerLock_FirstClickDoesNotPlace()
        {
            PointerLock.Simulate(false);
            Inventory.Add(BridgePhoto);
            Player.SetLook(0f, 0f);
            yield return Frames(3);

            // The click that acquires the lock: button down on the same frame the lock arrives, Shift held.
            IonInput.SimulatedRaise = true;
            IonInput.SimulatedPrimaryDown = true;
            PointerLock.Simulate(true);
            yield return Seconds(0.5f);           // fully raised, button still held
            Assert.IsTrue(Holder.IsRaised, "Shift raises at once");
            Assert.IsTrue(IonInput.MouseGated, "mouse buttons are ignored until released");
            Assert.AreEqual(0, Projection.PlacementCount, "the locking click never places");

            IonInput.SimulatedPrimaryDown = false;
            yield return Frames(IonInput.GateFrames + 1);
            Assert.IsFalse(IonInput.MouseGated);
            Assert.AreEqual(0, Projection.PlacementCount);

            // A real click now places (after the 0.10 s press-in).
            IonInput.SimulatedPrimaryDown = true;
            yield return Seconds(0.25f);
            IonInput.SimulatedPrimaryDown = false;
            Assert.AreEqual(1, Projection.PlacementCount, "a later click places. " + State);
            Assert.AreEqual(1, History.Depth);
        }

        [UnityTest]
        public IEnumerator FocusLoss_ForgetsHeldShift()
        {
            Inventory.Add(BridgePhoto);
            IonInput.SimulatedRaise = true;
            yield return Seconds(0.3f);
            Assert.IsTrue(Holder.IsRaised);

            PointerLock.SimulateBlur();            // alt-tab with Shift held
            yield return Frames(3);
            Assert.IsFalse(Holder.IsRaised, "lowered on focus loss");
            PointerLock.Simulate(true);            // click to resume, Shift still reported down
            yield return Frames(5);
            Assert.IsFalse(Holder.IsRaised, "a Shift held across the alt-tab must be released first");
            IonInput.SimulatedRaise = false;
            yield return Frames(2);
            IonInput.SimulatedRaise = true;
            yield return Frames(3);
            Assert.IsTrue(Holder.IsRaised, "a fresh press raises");
        }
    }
}
