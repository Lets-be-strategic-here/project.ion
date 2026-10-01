using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ion.DebugTools;
using Ion.Gameplay;
using Ion.Levels;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ion.Tests.PlayMode
{
    /// <summary>Harness sanity, rewind, player exclusion and placement forgiveness.</summary>
    public sealed class ProjectionPlayTests : IonPlayTestBase
    {
        // ------------------------------------------------------------------ harness

        [UnityTest]
        public IEnumerator Harness_EverySolutionSpotIsSolidGround()
        {
            var problems = new List<string>();
            for (int r = 0; r < Game.Rooms.Count; r++)
            {
                RoomContext ctx = Game.Rooms[r];
                Assert.IsNotEmpty(ctx.Room.Solutions, ctx.Room.Title + " exposes no solution spots");
                foreach (RoomSolution s in ctx.Room.Solutions)
                {
                    if (s.Name == "exit") continue; // standing there teleports
                    int respawns = Player.RespawnCount;
                    yield return GoTo((r + 1) + ":" + s.Name, 30);
                    Vector3 want = ctx.SolutionFeet(s), got = Player.transform.position;
                    Vector2 dxz = new Vector2(got.x - want.x, got.z - want.z);
                    if (Game.CurrentRoom != r) problems.Add($"{ctx.Room.Title}:{s.Name} current room {Game.CurrentRoom}");
                    if (dxz.magnitude > 0.15f || Mathf.Abs(got.y - want.y) > 0.15f || !Player.IsGrounded || Player.RespawnCount != respawns)
                        problems.Add($"{ctx.Room.Title}:{s.Name} want {want} got {got} grounded={Player.IsGrounded}");
                    if (Mathf.Abs(Mathf.DeltaAngle(Player.Yaw, ctx.SolutionYaw(s))) > 0.01f)
                        problems.Add($"{ctx.Room.Title}:{s.Name} yaw {Player.Yaw}");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [UnityTest]
        public IEnumerator Harness_GiveAllSelectAndState()
        {
            Assert.AreEqual(IonDebug.ObjectName, Dbg.gameObject.name);
            Dbg.GiveAll("");
            yield return null;
            int presets = Game.Rooms.Sum(r => r.ShotCount);
            Assert.AreEqual(presets, Inventory.Count);
            Assert.IsTrue(Cam.Unlocked);
            Assert.IsTrue(Dbg.TrySelect("Stairs"));
            Assert.AreEqual("Stairs (sideways)", Inventory.Selected.Label);
            Dbg.Raise("");
            yield return null;
            Assert.IsTrue(Holder.IsRaised);
            Dbg.Rotate("+1");
            yield return null;
            Assert.AreEqual(90f, Holder.RollDegrees, 0.01f);
            Dbg.Lower("");
            yield return null;
            Assert.IsFalse(Holder.IsRaised);

            Dbg.Look("90,10");
            Assert.AreEqual(90f, Player.Yaw, 0.01f);
            Assert.AreEqual(10f, Player.Pitch, 0.01f);

            Dbg.Walk("0,1,0.5");
            Vector3 p0 = Player.transform.position;
            for (int i = 0; i < 40; i++) yield return null;
            Assert.Greater(Player.transform.position.x - p0.x, 0.8f, "Walk should move the player forward (+X at yaw 90)");

            string json = IonDebug.StateJson();
            StringAssert.StartsWith("{\"room\":1", json);
            StringAssert.Contains("\"inventory\":" + presets, json);
            StringAssert.Contains("\"placements\":0", json);
            StringAssert.Contains("\"canRewind\":false", json);
            StringAssert.Contains("\"grounded\":true", json);
            LogAssert.Expect(LogType.Log, "[IonDebug] " + json);
            Dbg.State("");
        }

        /// <summary>
        /// Each pre-made photo's preview must show its diorama (terrain), not just sky: a good share of
        /// the lower half is grass green, and the image is not a flat gradient.
        /// </summary>
        [UnityTest]
        public IEnumerator Photos_PresetPreviewsShowTheScene()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device (-nographics): previews are placeholders.");
            yield return null;
            var problems = new List<string>();
            foreach (RoomContext ctx in Game.Rooms)
            {
                for (int s = 0; s < ctx.ShotCount; s++)
                {
                    PhotoData p = ctx.GetShotPhoto(s);
                    Texture2D tex = p.Preview;
                    if (tex == null) { problems.Add(p.Label + ": no preview"); continue; }
                    Color32[] px = tex.GetPixels32();
                    int w = tex.width, h = tex.height, green = 0, lower = 0;
                    for (int y = 0; y < h / 2; y++)
                        for (int x = 0; x < w; x++)
                        {
                            Color32 c = px[y * w + x];
                            lower++;
                            if (c.g > c.r + 15 && c.g > c.b + 15) green++;
                        }
                    float share = green / (float)lower;
                    if (share < 0.1f) problems.Add($"{p.Label}: only {share:P0} grass in the lower half (blank / sky-only preview?)");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        // ------------------------------------------------------------------ rewind

        static List<string> WorldSignature()
        {
            var list = new List<string>();
            foreach (var s in UnityEngine.Object.FindObjectsByType<Sliceable>(FindObjectsSortMode.None))
            {
                if (!s.enabled || !s.gameObject.activeInHierarchy) continue;
                var r = s.GetComponent<MeshRenderer>();
                var c = s.GetComponent<Collider>();
                if (r == null || !r.enabled || c == null || !c.enabled) continue;
                Bounds b = r.bounds;
                list.Add($"{s.name}|{b.center.x:0.00},{b.center.y:0.00},{b.center.z:0.00}|{b.size.x:0.00},{b.size.y:0.00},{b.size.z:0.00}");
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        [UnityTest]
        public IEnumerator Rewind_RestoresWorldAndPhotos()
        {
            RoomContext room = Room("bridge");
            PhotoData bridge = ShotPhoto(room);
            PhotoData sky = ShotPhoto(Room("doorway"));
            Dbg.GiveAll("");
            yield return GoTo("bridge:place");

            List<string> before = WorldSignature();
            int interactables = UnityEngine.Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None).Length;
            Assert.AreEqual(0, ActiveNamed(" (cut)") + ActiveNamed(" (photo)"));

            yield return RaiseRotatePlace(bridge, 0f);
            Assert.IsTrue(GroundAt(W(room, 0f, 0f, 9.5f), 3f, 6f, out _), "bridge placed");
            Assert.Greater(ActiveNamed(" (photo)"), 0);

            // A second placement on top (the sky photo from further back cuts the bridge and the far island).
            yield return GoTo("bridge:spawn");
            yield return RaiseRotatePlace(sky, 0f);
            Assert.AreEqual(2, Projection.PlacementCount);

            Dbg.Rewind("");
            yield return null;
            Assert.AreEqual(1, Projection.PlacementCount);
            Assert.IsTrue(Inventory.Contains(sky), "rewind returns the photo");
            Assert.AreSame(sky, Inventory.Selected, "the returned photo is selected");
            Assert.IsTrue(GroundAt(W(room, 0f, 0f, 9.5f), 3f, 6f, out _), "first placement still in place");

            Dbg.Rewind("");
            yield return null;
            Physics.SyncTransforms();
            Assert.AreEqual(0, Projection.PlacementCount);
            Assert.IsFalse(Projection.CanRewind);
            Assert.IsTrue(Inventory.Contains(bridge));
            Assert.IsFalse(GroundAt(W(room, 0f, 0f, 9.5f), 3f, 40f, out RaycastHit h), "chasm should be empty again, hit " + (h.collider ? h.collider.name : ""));
            Assert.AreEqual(0, ActiveNamed(" (cut)") + ActiveNamed(" (photo)"), "no pasted / cut pieces remain");
            Assert.AreEqual(interactables, UnityEngine.Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None).Length);
            List<string> after = WorldSignature();
            CollectionAssert.AreEqual(before, after, "world after rewind differs from before");

            // The returned photo still works.
            yield return GoTo("bridge:place");
            yield return RaiseRotatePlace(bridge, 0f);
            var walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 0f, 17f), 10f, walk);
            Assert.IsTrue(walk.Arrived, walk.ToString());
        }

        /// <summary>
        /// Rewinding while standing on what the placement created (mid-bridge), or where the restored world
        /// would enclose the player (in the cut doorway), puts the player back where they placed from.
        /// Standing on ground that exists either way (the far island) keeps them where they are.
        /// </summary>
        [UnityTest]
        public IEnumerator Rewind_LeavesThePlayerSomewhereSafe([Values("bridge:mid", "doorway:inwall", "bridge:far")] string scenario)
        {
            string roomKey = scenario.Split(':')[0];
            RoomContext room = Room(roomKey);
            Dbg.GiveAll("");
            yield return GoTo(roomKey + ":place");
            Vector3 placedFrom = Player.transform.position;
            yield return RaiseRotatePlace(ShotPhoto(room), 0f);

            Vector3 standAt = scenario == "bridge:mid" ? W(room, 0f, 0f, 9.5f)
                            : scenario == "doorway:inwall" ? W(room, 0f, 0f, 9f)
                            : W(room, 0f, 0f, 17f);
            var walk = new WalkResult();
            yield return WalkTo(standAt, 8f, walk, 0.3f);
            Assert.IsTrue(walk.Arrived, walk.ToString());

            int respawns = Player.RespawnCount;
            Dbg.Rewind("");
            for (int i = 0; i < 90; i++) yield return null;

            Vector3 p = Player.transform.position;
            Assert.AreEqual(respawns, Player.RespawnCount, "fell out of the world after rewinding. " + State);
            Assert.IsTrue(Player.IsGrounded, "not standing after rewind. " + State);
            if (scenario == "bridge:far")
                Assert.Less(Vector3.Distance(p, walk.End), 0.3f, "standing on real ground: rewinding should not move the player. " + State);
            else
                Assert.Less(Vector3.Distance(p, placedFrom), 0.3f, "should be back at the placement spot. " + State);
        }

        // ------------------------------------------------------------------ the player is never part of a photo

        [UnityTest]
        public IEnumerator Player_IsNeverCapturedOrCut()
        {
            RoomContext room = Room("bridge");
            Dbg.GiveAll("");
            yield return GoTo("bridge:place");
            Vector3 eye = Player.Camera.transform.position;

            // A capture aimed straight at the player from 4 m in front.
            Vector3 from = eye + Player.transform.forward * 4f;
            PhotoData shot = Projection.Capture(new Pose(from, Quaternion.LookRotation(eye - from)), 50f, 4f / 3f, "test");
            Assert.Greater(shot.PieceCount, 0, "the capture should contain terrain");
            foreach (PhotoPiece p in shot.Pieces)
            {
                Assert.AreEqual(0, ProjectionSystem.ExcludedLayerMask & (1 << p.Layer), "captured piece on an excluded layer: " + p.Name);
                StringAssert.DoesNotStartWith("Player", p.Name);
            }
            foreach (PhotoEntity e in shot.Entities)
                Assert.IsNull(e.Template.GetComponentInChildren<FirstPersonController>(true), "captured the player");

            // A snapshot by the player's own camera.
            PhotoData snap = Dbg.TrySnap();
            Assert.IsNotNull(snap);
            foreach (PhotoPiece p in snap.Pieces)
                Assert.AreEqual(0, ProjectionSystem.ExcludedLayerMask & (1 << p.Layer), "snap captured " + p.Name);

            // Placing never touches the player rig.
            Vector3 pos = Player.transform.position;
            int childCount = Player.GetComponentsInChildren<Transform>(true).Length;
            yield return RaiseRotatePlace(ShotPhoto(room), 0f);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None).Length);
            Assert.IsTrue(Player.gameObject.activeInHierarchy && Player.Controller.enabled);
            Assert.Less(Vector3.Distance(pos, Player.transform.position), 0.05f, "the player moved during placement");
            Assert.AreEqual(childCount, Player.GetComponentsInChildren<Transform>(true).Length);
            foreach (var t in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (t.name.EndsWith(" (photo)") || t.name.EndsWith(" (cut)"))
                    Assert.AreEqual(0, ProjectionSystem.ExcludedLayerMask & (1 << t.gameObject.layer), t.name);
        }

        // ------------------------------------------------------------------ forgiveness

        static readonly (Vector3 offset, float yaw)[] Poses =
        {
            (new Vector3(0.3f, 0f, 0f), 3f),
            (new Vector3(-0.3f, 0f, 0f), -3f),
            (new Vector3(0f, 0f, 0.3f), -3f),
            (new Vector3(0f, 0f, -0.3f), 3f),
            (new Vector3(0.21f, 0f, -0.21f), -3f),
        };

        /// <summary>
        /// Placing from up to 0.3 m / 3° away from the marker still yields a solution: the paste follows the
        /// viewer, so the player walks the pasted path in the viewer's frame (as a human would, following
        /// what they see).
        /// </summary>
        [UnityTest]
        public IEnumerator Forgiveness_SlightlyOffPoseStillSolves([Values("bridge", "doorway", "stairs", "camera")] string roomKey)
        {
            RoomContext room = Room(roomKey);
            RoomSolution place = Spot(room, "place");
            RoomSolution far = Spot(room, "far");
            Dbg.GiveAll("");

            PhotoData photo;
            if (place.PhotoIndex >= 0)
            {
                photo = ShotPhoto(room, place.PhotoIndex);
            }
            else
            {
                yield return GoTo((room.Index + 1) + ":snap");
                photo = Dbg.TrySnap();
                Assert.IsNotNull(photo);
            }

            var failures = new List<string>();
            foreach (var pose in Poses)
            {
                yield return GoToOff(room, "place", pose.offset, pose.yaw);
                yield return RaiseRotatePlace(photo, place.Roll);

                Quaternion r = Quaternion.Euler(0f, pose.yaw, 0f);
                Vector3 target = room.WorldRoot.TransformPoint(place.LocalFeet + pose.offset + r * (far.LocalFeet - place.LocalFeet));
                var walk = new WalkResult();
                yield return WalkTo(target, 12f, walk);
                bool ok = walk.Arrived && Mathf.Abs(walk.End.y - target.y) < 0.2f && walk.MinY > Mathf.Min(walk.Start.y, target.y) - 0.3f;
                if (!ok) failures.Add($"offset {pose.offset} yaw {pose.yaw:+0;-0}: {walk}");

                Dbg.Rewind("");
                yield return null;
                Assert.IsTrue(Inventory.Contains(photo), "rewind returns the photo");
            }
            Assert.IsEmpty(failures, room.Room.Title + " is not forgiving:\n" + string.Join("\n", failures));
        }
    }
}
