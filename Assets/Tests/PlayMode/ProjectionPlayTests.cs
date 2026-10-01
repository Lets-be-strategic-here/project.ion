using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ion.DebugTools;
using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ion.Tests.PlayMode
{
    /// <summary>Harness sanity, solution-script integrity, rewind, player exclusion and placement forgiveness.</summary>
    public sealed class ProjectionPlayTests : IonPlayTestBase
    {
        // ------------------------------------------------------------------ harness

        static bool StandsOnRealGround(RoomSolution s) =>
            s.Hazard == RoomSolution.Hazards.None && !s.AfterPlace &&
            s.Action != RoomSolution.Kind.Teleport && s.Action != RoomSolution.Kind.Rewind &&
            s.Action != RoomSolution.Kind.RewindToCheckpoint;

        [UnityTest]
        public IEnumerator Harness_EverySolutionSpotIsSolidGround()
        {
            var problems = new List<string>();
            for (int r = 0; r < Game.Rooms.Count; r++)
            {
                RoomContext ctx = Game.Rooms[r];
                Assert.IsNotEmpty(ctx.Room.Solutions, ctx.Room.Title + " exposes no solution steps");
                foreach (RoomSolution s in ctx.Room.Solutions)
                {
                    if (!StandsOnRealGround(s)) continue;
                    int respawns = Player.RespawnCount;
                    yield return GoTo(ctx.Room.Key + ":" + s.Name, 30);
                    Vector3 want = ctx.SolutionFeet(s), got = Player.transform.position;
                    Vector2 dxz = new Vector2(got.x - want.x, got.z - want.z);
                    if (Game.CurrentRoom != r) problems.Add($"{ctx.Room.Key}:{s.Name} current zone {Game.CurrentRoom}");
                    if (dxz.magnitude > 0.15f || Mathf.Abs(got.y - want.y) > 0.15f || !Player.IsGrounded || Player.RespawnCount != respawns)
                        problems.Add($"{ctx.Room.Key}:{s.Name} want {want} got {got} grounded={Player.IsGrounded}");
                    if (Mathf.Abs(Mathf.DeltaAngle(Player.Yaw, ctx.SolutionYaw(s))) > 0.01f)
                        problems.Add($"{ctx.Room.Key}:{s.Name} yaw {Player.Yaw}");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>Art bible §7.5.2: one brass standing marker per Place / Snap step, at its feet and yaw (1:1).</summary>
        [UnityTest]
        public IEnumerator Harness_EveryMarkerHasASolution()
        {
            yield return null;
            var problems = new List<string>();
            foreach (RoomContext ctx in Game.Rooms)
            {
                var steps = ctx.Room.Solutions.Where(s => s.NeedsMarker).ToList();
                if (steps.Count != ctx.Markers.Count)
                    problems.Add($"{ctx.Room.Key}: {steps.Count} Place/Snap steps but {ctx.Markers.Count} markers");
                var used = new bool[ctx.Markers.Count];
                foreach (RoomSolution s in steps)
                {
                    int found = -1;
                    for (int i = 0; i < ctx.Markers.Count && found < 0; i++)
                    {
                        MarkerSpot m = ctx.Markers[i];
                        if (!used[i] && Vector3.Distance(m.LocalFeet, s.LocalFeet) < 0.05f &&
                            Mathf.Abs(Mathf.DeltaAngle(m.Yaw, s.Yaw)) < 0.5f)
                            found = i;
                    }
                    if (found < 0) problems.Add($"{ctx.Room.Key}:{s.Name} has no marker at {s.LocalFeet} yaw {s.Yaw}");
                    else used[found] = true;
                }
                for (int i = 0; i < used.Length; i++)
                    if (!used[i]) problems.Add($"{ctx.Room.Key}: marker at {ctx.Markers[i].LocalFeet} matches no step");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>
        /// Art bible §7.3: no photo placed (or snapshot taken) from any solution spot, at any roll, can reach another
        /// zone. Every Sliceable / Interactable of every other zone must lie outside the 250 m photo frustum.
        /// </summary>
        [UnityTest]
        public IEnumerator Solutions_FrustaTouchOnlyTheirZone()
        {
            yield return null;
            var problems = new List<string>();
            var planes = new Plane[PhotoFrustum.PlaneCount];
            var corners = new Vector3[8];
            foreach (RoomContext ctx in Game.Rooms)
            {
                foreach (RoomSolution s in ctx.Room.Solutions.Where(x => x.NeedsMarker))
                {
                    Vector3 eye = ctx.SolutionFeet(s) + Vector3.up * Game.EyeHeight;
                    Quaternion view = ctx.WorldRoot.rotation * Quaternion.Euler(s.Pitch, s.Yaw, 0f);
                    foreach (float roll in new[] { 0f, 90f, 180f, 270f })
                    {
                        var f = new PhotoFrustum
                        {
                            Pose = new Pose(eye, PhotoFrustum.RolledRotation(view, roll)),
                            FovY = RoomContext.PhotoFovY,
                            Aspect = RoomContext.PhotoAspect,
                            Near = ProjectionSystem.HoldNear,
                            Far = ProjectionSystem.MaxFar,
                        };
                        f.GetPlanes(planes);
                        foreach (RoomContext other in Game.Rooms)
                        {
                            if (other == ctx) continue;
                            foreach (Sliceable sl in other.WorldRoot.GetComponentsInChildren<Sliceable>(true))
                            {
                                var rend = sl.GetComponent<Renderer>();
                                if (rend == null) continue;
                                if (Touches(planes, rend.bounds, corners))
                                    problems.Add($"{ctx.Room.Key}:{s.Name} roll {roll} reaches {other.Room.Key} '{sl.name}' {rend.bounds}");
                            }
                            foreach (Interactable it in other.WorldRoot.GetComponentsInChildren<Interactable>(true))
                                if (f.Contains(it.transform.position))
                                    problems.Add($"{ctx.Room.Key}:{s.Name} roll {roll} reaches {other.Room.Key} device '{it.name}'");
                        }
                    }
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems.Take(30)));
        }

        /// <summary>Conservative AABB vs frustum (planes face inward): false only if one plane separates them.</summary>
        static bool Touches(Plane[] planes, Bounds b, Vector3[] c)
        {
            Vector3 mn = b.min, mx = b.max;
            c[0] = new Vector3(mn.x, mn.y, mn.z); c[1] = new Vector3(mx.x, mn.y, mn.z);
            c[2] = new Vector3(mn.x, mx.y, mn.z); c[3] = new Vector3(mx.x, mx.y, mn.z);
            c[4] = new Vector3(mn.x, mn.y, mx.z); c[5] = new Vector3(mx.x, mn.y, mx.z);
            c[6] = new Vector3(mn.x, mx.y, mx.z); c[7] = new Vector3(mx.x, mx.y, mx.z);
            foreach (Plane p in planes)
            {
                bool allOutside = true;
                for (int i = 0; i < 8 && allOutside; i++)
                    if (p.GetDistanceToPoint(c[i]) >= -1e-3f) allOutside = false;
                if (allOutside) return false;
            }
            return true;
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
            Assert.IsTrue(Dbg.TrySelect("Sideways stairs"));
            Assert.AreEqual("Sideways stairs", Inventory.Selected.Label);
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

            Dbg.Move("0,1,0.5");
            Vector3 p0 = Player.transform.position;
            for (int i = 0; i < 40; i++) yield return null;
            Assert.Greater(Player.transform.position.x - p0.x, 0.8f, "Move should walk the player forward (+X at yaw 90)");

            string json = IonDebug.StateJson();
            StringAssert.StartsWith("{\"room\":0", json);
            StringAssert.Contains("\"zone\":\"t1\"", json);
            StringAssert.Contains("\"inventory\":" + presets, json);
            StringAssert.Contains("\"placements\":0", json);
            StringAssert.Contains("\"canRewind\":false", json);
            StringAssert.Contains("\"grounded\":true", json);
            LogAssert.Expect(LogType.Log, "[IonDebug] " + json);
            Dbg.State("");
        }

        [UnityTest]
        public IEnumerator Harness_ZoneLookupAndWalk()
        {
            string[] keys = { "t1", "t2", "hub", "stairs", "camera", "gallery" };
            for (int i = 0; i < keys.Length; i++)
                Assert.AreEqual(i, IonDebug.FindRoom(keys[i]), "zone key " + keys[i]);
            Dbg.Zone("hub");
            yield return Frames(10);
            Assert.AreEqual(Game.IndexOfKey("hub"), Game.CurrentRoom);
            Dbg.Walk("-7,0,-1.5,4");
            yield return Seconds(1.5f);
            Vector3 local = Local(Room("hub"), Player.transform.position);
            Assert.Less(Vector2.Distance(new Vector2(local.x, local.z), new Vector2(-7f, -1.5f)), 0.6f, "walk x,y,z. " + State);
        }

        /// <summary>
        /// Each pre-made photo's preview must show its diorama, not just sky: a sky is a smooth gradient, while
        /// architecture gives hard edges. At least 0.5% of sampled pixels must sit on a strong edge.
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
                    int w = tex.width, h = tex.height, edges = 0, samples = 0;
                    for (int y = 1; y < h; y += 2)
                        for (int x = 1; x < w; x += 2)
                        {
                            Color32 c = px[y * w + x], l = px[y * w + x - 1], d = px[(y - 1) * w + x];
                            int g = Mathf.Abs(c.r - l.r) + Mathf.Abs(c.g - l.g) + Mathf.Abs(c.b - l.b) +
                                    Mathf.Abs(c.r - d.r) + Mathf.Abs(c.g - d.g) + Mathf.Abs(c.b - d.b);
                            samples++;
                            if (g > 60) edges++;
                        }
                    float share = edges / (float)Mathf.Max(1, samples);
                    if (share < 0.005f) problems.Add($"{p.Label}: only {share:P2} edge pixels (blank / sky-only preview?)");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        // ------------------------------------------------------------------ rewind (projection level)

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
            RoomContext room = Room("t1");
            PhotoData stair = ShotPhoto(room);
            PhotoData door = ShotPhoto(Room("t2"));
            Dbg.GiveAll("");
            yield return GoTo("t1:place");

            List<string> before = WorldSignature();
            int interactables = UnityEngine.Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None).Length;
            Assert.AreEqual(0, ActiveNamed(" (cut)") + ActiveNamed(" (photo)"));
            Vector3 midStair = W(room, 0f, 0f, 13.25f);
            Assert.IsTrue(GroundAt(midStair, 4f, 6f, out RaycastHit h0) && h0.point.y < 0.1f, "no stair before");

            yield return RaiseRotatePlace(stair, 0f);
            Assert.IsTrue(GroundAt(midStair, 4f, 6f, out RaycastHit h1) && h1.point.y > 1f, "stair placed");
            Assert.Greater(ActiveNamed(" (photo)"), 0);

            // A second placement on top, from the spawn (the sideways door photo, unrotated).
            yield return GoTo("t1");
            yield return RaiseRotatePlace(door, 0f);
            Assert.AreEqual(2, Projection.PlacementCount);

            Assert.AreEqual(RewindResult.Undid, Dbg.TryRewind());
            yield return null;
            Assert.AreEqual(1, Projection.PlacementCount);
            Assert.IsTrue(Inventory.Contains(door), "rewind returns the photo");

            yield return GoTo("t1:place");
            Assert.AreEqual(RewindResult.Nothing, Dbg.TryRewind(), "entering the zone again is a checkpoint: the stair is sealed");
            Assert.AreEqual(1, Projection.PlacementCount);
            Assert.AreEqual(RewindResult.ToCheckpoint, Dbg.TryRewindToCheckpoint());
            Assert.AreEqual(1, Projection.PlacementCount, "R R never crosses its own checkpoint");

            // A fresh checkpoint below the stair: unwind everything through the history instead.
            History.UnwindAll();
            yield return null;
            Physics.SyncTransforms();
            Assert.AreEqual(0, Projection.PlacementCount);
            Assert.IsFalse(Projection.CanRewind);
            Assert.IsTrue(Inventory.Contains(stair));
            Assert.IsTrue(GroundAt(midStair, 4f, 6f, out RaycastHit h2) && h2.point.y < 0.1f, "the stair should be gone, hit y " + h2.point.y);
            Assert.AreEqual(0, ActiveNamed(" (cut)") + ActiveNamed(" (photo)"), "no pasted / cut pieces remain");
            Assert.AreEqual(interactables, UnityEngine.Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None).Length);
            CollectionAssert.AreEqual(before, WorldSignature(), "world after rewind differs from before");

            // The returned photo still works.
            yield return GoTo("t1:place");
            yield return RaiseRotatePlace(stair, 0f);
            var walk = new WalkResult();
            yield return WalkTo(W(room, 0f, 3f, 18.5f), 10f, walk);
            Assert.IsTrue(walk.Arrived, walk.ToString());
            Assert.AreEqual(3f, walk.End.y, 0.15f);
        }

        /// <summary>
        /// Rewinding while standing on what the placement created (the pasted stair), or where the restored world
        /// would enclose the player (in the cut doorway), puts the player back where they placed from. Standing on
        /// ground that exists either way (the court) keeps them where they are.
        /// </summary>
        [UnityTest]
        public IEnumerator Rewind_LeavesThePlayerSomewhereSafe([Values("t1:onstairs", "t2:indoor", "t1:court")] string scenario)
        {
            string key = scenario.Split(':')[0];
            RoomContext room = Room(key);
            RoomSolution place = Spot(room, "place");
            Dbg.GiveAll("");
            yield return GoTo(key + ":place");
            Vector3 placedFrom = Player.transform.position;
            yield return RaiseRotatePlace(ShotPhoto(room), place.Roll);

            Vector3 standAt = scenario == "t1:onstairs" ? W(room, 0f, 0f, 13.25f)
                            : scenario == "t2:indoor" ? W(room, 0f, 0f, Ion.Levels.Rooms.TutorialDarkroom.CutWallZ)
                            : W(room, 0f, 0f, 8f);
            var walk = new WalkResult();
            yield return WalkTo(standAt, 8f, walk, 0.3f);
            Assert.IsTrue(walk.Arrived, walk.ToString());

            int respawns = Player.RespawnCount;
            Assert.AreEqual(RewindResult.Undid, Dbg.TryRewind());
            for (int i = 0; i < 90; i++) yield return null;

            Vector3 p = Player.transform.position;
            Assert.AreEqual(respawns, Player.RespawnCount, "fell out of the world after rewinding. " + State);
            Assert.IsTrue(Player.IsGrounded, "not standing after rewind. " + State);
            if (scenario == "t1:court")
                Assert.Less(Vector3.Distance(p, walk.End), 0.3f, "standing on real ground: rewinding should not move the player. " + State);
            else
                Assert.Less(Vector3.Distance(p, placedFrom), 0.4f, "should be back at the placement spot. " + State);
        }

        // ------------------------------------------------------------------ the player is never part of a photo

        [UnityTest]
        public IEnumerator Player_IsNeverCapturedOrCut()
        {
            RoomContext room = Room("t1");
            Dbg.GiveAll("");
            yield return GoTo("t1:place");
            Vector3 eye = Player.Camera.transform.position;

            // A capture aimed straight at the player from 4 m in front.
            Vector3 from = eye + Player.transform.forward * 4f;
            PhotoData shot = Projection.Capture(new Pose(from, Quaternion.LookRotation(eye - from)), 50f, 4f / 3f, "test");
            Assert.Greater(shot.PieceCount, 0, "the capture should contain the court");
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

        // ------------------------------------------------------------------ forgiveness (±0.3 m, ±3°)

        static readonly (Vector3 offset, float yaw)[] Poses =
        {
            (new Vector3(0.3f, 0f, 0f), 3f),
            (new Vector3(-0.3f, 0f, 0f), -3f),
            (new Vector3(0f, 0f, 0.3f), -3f),
            (new Vector3(0f, 0f, -0.3f), 3f),
            (new Vector3(0.21f, 0f, -0.21f), -3f),
        };

        /// <summary>
        /// Placing from up to 0.3 m / 3° away from the marker still yields a solution: the paste follows the viewer,
        /// so the player walks the pasted path in the viewer's frame (as a human would, following what they see).
        /// Cases: "zone" uses its "place"/"far" steps; "hub:n" uses "n.place"/"n.far".
        /// </summary>
        [UnityTest]
        public IEnumerator Forgiveness_SlightlyOffPoseStillSolves([Values("t1", "t2", "stairs", "camera", "hub:n", "hub:s")] string which)
        {
            string key = which.Split(':')[0];
            string prefix = which.Contains(":") ? which.Split(':')[1] + "." : "";
            RoomContext room = Room(key);
            RoomSolution place = Spot(room, prefix + "place");
            RoomSolution far = Spot(room, prefix + "far");
            Dbg.GiveAll("");

            PhotoData photo;
            if (place.PhotoIndex >= 0)
            {
                photo = ShotPhoto(room, place.PhotoIndex);
            }
            else
            {
                yield return GoTo(key + ":snap");
                photo = Dbg.TrySnap();
                Assert.IsNotNull(photo);
            }

            var failures = new List<string>();
            foreach (var pose in Poses)
            {
                Vector3 offset = Quaternion.Euler(0f, place.Yaw, 0f) * pose.offset;
                yield return GoToOff(room, prefix + "place", offset, pose.yaw);
                yield return RaiseRotatePlace(photo, place.Roll);

                Quaternion r = Quaternion.Euler(0f, pose.yaw, 0f);
                Vector3 target = room.WorldRoot.TransformPoint(place.LocalFeet + offset + r * (far.LocalFeet - place.LocalFeet));
                var walk = new WalkResult();
                yield return WalkTo(target, 12f, walk);
                bool ok = walk.Arrived && Mathf.Abs(walk.End.y - target.y) < 0.2f && walk.MinY > Mathf.Min(walk.Start.y, target.y) - 0.3f;
                if (!ok) failures.Add($"offset {pose.offset} yaw {pose.yaw:+0;-0}: {walk}");

                Assert.AreEqual(RewindResult.Undid, Dbg.TryRewind(), "rewind. " + State);
                yield return null;
                Assert.IsTrue(Inventory.Contains(photo), "rewind returns the photo");
            }
            Assert.IsEmpty(failures, room.Room.Title + " is not forgiving:\n" + string.Join("\n", failures));
        }
    }
}
