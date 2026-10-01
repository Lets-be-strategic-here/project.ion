using System.Collections;
using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels;
using Ion.Projection;
using Ion.Presentation.Motion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ion.Tests.PlayMode
{
    /// <summary>Art bible §11.3 (Lead D): switches, movers, photo copies of buttons, never sliced, never crushing.</summary>
    public sealed class SwitchTests : StateTestFixture
    {
        [UnityTest]
        public IEnumerator Switch_ToggleMovesTarget_AndRewindRestores()
        {
            Switch button = MakeButton(new Vector3(-3f, 0f, 4f), "sw.span");
            Mover bridge = MakeGapBridge("sw.span");
            yield return Frames(2);
            Assert.IsFalse(button.On);
            Assert.AreEqual(bridge.OffLocal, bridge.transform.localPosition);

            // E on the button (the PlayerInteractor path): stand in front of it, looking at it.
            Player.Teleport(new Vector3(-1.8f, 0f, 4f), 270f);
            Player.SetLook(270f, 20f);
            yield return Frames(5);
            var interactor = Player.GetComponent<PlayerInteractor>();
            Assert.AreEqual(button, interactor.FocusedSwitch, "the button is in reach and in view");
            Assert.IsTrue(interactor.PressFocused());
            Assert.IsTrue(button.On);
            Assert.IsTrue(bridge.IsMoving, "the bridge starts to slide");

            yield return Seconds(Feel.MoverSeconds(6f) + 0.3f); // 6 m of travel: 1.6 s per 4 m
            Assert.IsFalse(bridge.IsMoving);
            Assert.AreEqual(bridge.OnLocal.x, bridge.transform.localPosition.x, 0.01f, "the bridge spans the gap");
            Assert.IsTrue(FloorAt(new Vector3(0f, 0f, 12f)));

            Assert.AreEqual(RewindResult.Undid, History.RewindOnce());
            Assert.IsFalse(button.On, "rewind restores the switch state");
            yield return Seconds(0.6f);
            Assert.AreEqual(bridge.OffLocal.x, bridge.transform.localPosition.x, 0.01f, "and its target");
            Assert.IsFalse(FloorAt(new Vector3(0f, 0f, 12f)));
        }

        [UnityTest]
        public IEnumerator Switch_UnpoweredDoesNothing_ThenPowersUp()
        {
            Switch button = MakeButton(new Vector3(-3f, 0f, 4f), "sw.power");
            button.SetPowered(false, false);
            yield return null;
            Assert.IsFalse(button.Press(), "an unpowered switch does not press");
            Assert.AreEqual(0, History.Depth);
            button.SetPowered(true);
            yield return Seconds(0.7f);
            Assert.IsTrue(button.Press());
            Assert.AreEqual(1, History.Depth);
        }

        [UnityTest]
        public IEnumerator Switch_PhotoCopyOfButtonWorks()
        {
            // The button sits in view on floor A; the thing it drives is far off to the side (outside both
            // the capture and the placement frustum).
            Switch original = MakeButton(new Vector3(0f, 0f, 6f), "sw.copy");
            var far = new GameObject("FarMover");
            far.transform.SetParent(World, false);
            Geo.Box(far.transform, Vector3.zero, new Vector3(1f, 1f, 1f), Color.yellow);
            Mover bridge = far.AddComponent<Mover>();
            bridge.Channel = "sw.copy";
            bridge.OffLocal = new Vector3(30f, 0f, 0f);
            bridge.OnLocal = new Vector3(30f, 0f, 3f);
            far.transform.localPosition = bridge.OffLocal;
            yield return Frames(2);

            Player.Teleport(StartFeet, 0f);
            Player.SetLook(0f, 0f);
            yield return Frames(5);
            var cam = Player.Camera.transform;
            PhotoData photo = Projection.Capture(new Pose(cam.position, cam.rotation), 50f, 4f / 3f, "button");
            Assert.AreEqual(1, photo.Entities.Count, "the button is captured whole, as a device");

            // Paste it on floor B, looking west (the original stays outside this frustum).
            Player.Teleport(new Vector3(2f, 0f, 18f), 270f);
            Player.SetLook(270f, 0f);
            yield return Frames(5);
            Inventory.Add(photo);
            Inventory.SelectedIndex = Inventory.IndexOf(photo);
            Assert.IsTrue(Holder.Raise());
            yield return null;
            Assert.IsTrue(Holder.Place(), State);
            yield return Frames(3);
            Assert.IsTrue(original.isActiveAndEnabled, "the original button was not in the placement");

            Switch copy = null;
            foreach (var s in Object.FindObjectsByType<Switch>(FindObjectsSortMode.None))
                if (s != original && s.transform.root.name == "PlacedPhotos") copy = s;
            Assert.IsNotNull(copy, "a working copy was pasted");
            Assert.AreEqual("sw.copy", copy.Channel, "the copy shares the channel");

            Assert.IsTrue(copy.Press());
            Assert.IsTrue(original.On, "the channel is global: the original reads on");
            yield return Seconds(2f);
            Assert.AreEqual(bridge.OnLocal.z, bridge.transform.localPosition.z, 0.01f, "the copy drives the real mover");

            Assert.AreEqual(RewindResult.Undid, History.RewindOnce(), "undo the copy's press");
            Assert.IsFalse(original.On);
            Assert.AreEqual(RewindResult.Undid, History.RewindOnce(), "undo the placement");
            yield return null;
            Assert.IsFalse(copy != null && copy.isActiveAndEnabled, "the copy goes with its placement");
        }

        [UnityTest]
        public IEnumerator Switch_IsNeverSliced()
        {
            // A button half inside the placement frustum (its pivot outside): it must not be cut.
            Switch button = MakeButton(new Vector3(2.9f, 0f, 6f), "sw.slice");
            button.transform.localScale = new Vector3(2.4f, 1f, 2.4f);
            yield return Frames(2);
            var filters = button.GetComponentsInChildren<MeshFilter>();
            var meshes = new Mesh[filters.Length];
            for (int i = 0; i < filters.Length; i++) meshes[i] = filters[i].sharedMesh;

            // Capture through it: devices never become cut pieces.
            Player.Teleport(StartFeet, 0f);
            Player.SetLook(0f, 0f);
            yield return Frames(5);
            var cam = Player.Camera.transform;
            PhotoData photo = Projection.Capture(new Pose(cam.position, cam.rotation), 50f, 4f / 3f, "edge");
            foreach (var piece in photo.Pieces)
                StringAssert.DoesNotContain("Pedestal", piece.Name, "no piece of the button in the photo");

            yield return PlaceBridge();
            for (int i = 0; i < filters.Length; i++)
            {
                Assert.AreSame(meshes[i], filters[i].sharedMesh, "the button's meshes are untouched");
                Assert.IsTrue(filters[i].GetComponent<MeshRenderer>().enabled, "and still drawn");
            }
            Assert.IsTrue(button.gameObject.activeInHierarchy, "pivot outside the frustum: the button stays");
        }

        [UnityTest]
        public IEnumerator Mover_NeverCrushesPlayer()
        {
            // A gate that rises out of the floor into the spot where the player stands.
            var go = new GameObject("Gate");
            go.transform.SetParent(World, false);
            Geo.Box(go.transform, new Vector3(0f, 1.5f, 0f), new Vector3(3f, 3f, 0.5f), Color.white).name = "Slab";
            var gate = go.AddComponent<Mover>();
            gate.Channel = "sw.gate";
            gate.OffLocal = new Vector3(0f, -3.5f, 6f);  // sunk into its slot (open)
            gate.OnLocal = new Vector3(0f, 0f, 6f);      // closed
            go.transform.localPosition = gate.OffLocal;

            Player.Teleport(new Vector3(0f, 0f, 6f), 0f);
            yield return Frames(10);
            SwitchBoard.Set("sw.gate", true);
            yield return Seconds(2.5f);
            Assert.IsTrue(gate.IsBlocked, "it waits instead of entering the player's capsule");
            Assert.Less(go.transform.localPosition.y, -1.6f, "stopped below the player's feet. " + go.transform.localPosition);
            Assert.AreEqual(6f, Player.transform.position.z, 0.2f, "the player was not pushed");

            yield return WalkTo(new Vector3(0f, 0f, 3f));
            yield return Seconds(2f);
            Assert.IsFalse(gate.IsMoving);
            Assert.AreEqual(0f, go.transform.localPosition.y, 0.01f, "it finishes once the way is clear");
        }

        [UnityTest]
        public IEnumerator Collapse_DropsUnderThePlayer_AndIsNotRewound()
        {
            // The far end of floor A becomes a slab that gives way.
            var go = new GameObject("Collapse");
            go.transform.SetParent(World, false);
            go.transform.position = new Vector3(0f, 0f, 12f);
            Geo.Box(go.transform, new Vector3(0f, -0.25f, 0f), new Vector3(3f, 0.5f, 4f), Color.white).name = "Slab";
            var collapse = go.AddComponent<StoryCollapse>();
            bool collapsed = false;
            collapse.Collapsed += () => collapsed = true;
            yield return Frames(2);

            yield return WalkTo(new Vector3(0f, 0f, 9.6f));
            Player.ScriptedWalkTo(new Vector3(0f, 0f, 12f), 2f, 0.2f);
            int guard = 300;
            while (!collapse.Fired && guard-- > 0) yield return null;
            Assert.IsTrue(collapse.Fired, "stepping on it starts the wobble. " + State);
            yield return Seconds(0.6f);
            Assert.IsTrue(collapsed && collapse.Dropped, "then it drops");
            guard = 300;
            while (!Tracker.IsFalling && guard-- > 0) yield return null;
            Assert.IsTrue(Tracker.IsFalling, "the player falls with it. " + State);

            Assert.AreEqual(RewindResult.RecoveredFall, History.RewindOnce());
            yield return Frames(10);
            Assert.IsTrue(Player.IsGrounded, State);
            Assert.Less(Player.transform.position.z, 10.2f, "recovered to the last safe pose (not on the slab)");
            Assert.IsTrue(collapse.Dropped, "a story event is one-way");
            Assert.AreEqual(0, History.Depth, "and never in the history");
        }

        [UnityTest]
        public IEnumerator CheckpointMarker_SetsOnlyWhenNewer()
        {
            var go = new GameObject("Marker");
            go.transform.SetParent(World, false);
            go.transform.position = new Vector3(0f, 0f, 8f);
            var marker = go.AddComponent<CheckpointMarker>();
            marker.Id = "test.marker";
            yield return null;

            yield return WalkTo(new Vector3(0f, 0f, 8f));
            Assert.AreEqual("test.marker", History.LastCheckpoint.Id, "entering the marker sets the checkpoint");
            History.SetCheckpoint("later", PlayerPose.Of(Player));
            yield return WalkTo(new Vector3(0f, 0f, 4f));
            yield return WalkTo(new Vector3(0f, 0f, 8f));
            Assert.AreEqual("later", History.LastCheckpoint.Id, "an older marker never moves the checkpoint back");
        }
    }
}
