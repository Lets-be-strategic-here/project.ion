using System;
using System.Collections;
using System.Collections.Generic;
using Ion.DebugTools;
using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ion.Tests.PlayMode
{
    /// <summary>
    /// Loads Main.unity fresh for every test (GameBootstrap builds the whole game), waits for the diorama
    /// photos, and runs time at a fixed 60 Hz (Time.captureDeltaTime) so controller physics is deterministic
    /// regardless of how fast the batch-mode editor renders frames.
    ///
    /// <see cref="RunStep"/> performs one <see cref="RoomSolution"/> step the way a player would (real walking
    /// along the step's waypoints, E on switches and pickups, Shift/LMB through PhotoHolder, R / R R through
    /// WorldHistory) and asserts its outcome, so a zone's Solutions list is executable as a script.
    /// </summary>
    public abstract class IonPlayTestBase
    {
        protected const string MainScene = "Main";
        protected const int PlayerLayerMask = 1 << ProjectionSystem.PlayerLayer;
        protected const int WorldMask = ~ProjectionSystem.ExcludedLayerMask;
        protected const float Step = 1f / 60f;
        protected const float WalkSpeed = 4.5f;

        protected GameBootstrap Game => GameBootstrap.Instance;
        protected FirstPersonController Player => Game.Player;
        protected PhotoInventory Inventory => Player.GetComponent<PhotoInventory>();
        protected PhotoHolder Holder => Player.GetComponent<PhotoHolder>();
        protected InstantCamera Cam => Player.GetComponent<InstantCamera>();
        protected ProjectionSystem Projection => ProjectionSystem.Instance;
        protected WorldHistory History => WorldHistory.Instance;
        protected SafePoseTracker Tracker => Player.GetComponent<SafePoseTracker>();
        protected IonDebug Dbg;

        [UnitySetUp]
        public IEnumerator LoadGame()
        {
            Time.captureDeltaTime = Step;
            // Switch channels are static: start every test with all channels off (movers build in that state).
            SwitchBoard.Restore(null);
            PointerLock.Simulate(true);
            SceneManager.LoadScene(MainScene, LoadSceneMode.Single);
            yield return null;

            for (int frame = 0; frame < 1800; frame++)
            {
                if (GameReady()) break;
                yield return null;
            }
            Assert.IsTrue(GameReady(), "Main.unity did not finish building + capturing the diorama photos.");
            // Let the bootstrap deactivate the dioramas and the UI settle.
            for (int i = 0; i < 3; i++) yield return null;
            Physics.SyncTransforms();
            Dbg = IonDebug.Ensure();
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            Time.captureDeltaTime = 0f;
            PointerLock.EndSimulation();
            SwitchBoard.Restore(null);
            yield return null;
        }

        static bool GameReady()
        {
            var game = GameBootstrap.Instance;
            if (game == null || game.Player == null || game.Rooms.Count == 0 || !game.PhotosReady) return false;
            for (int r = 0; r < game.Rooms.Count; r++)
            {
                var ctx = game.Rooms[r];
                for (int s = 0; s < ctx.ShotCount; s++)
                    if (ctx.GetShotPhoto(s) == null) return false;
            }
            var ps = ProjectionSystem.Instance;
            return ps != null && ps.PendingPreviewCount == 0;
        }

        // ------------------------------------------------------------------ zones / spots

        protected RoomContext Room(string key)
        {
            int i = IonDebug.FindRoom(key);
            Assert.GreaterOrEqual(i, 0, "unknown zone " + key);
            return Game.Rooms[i];
        }

        protected static RoomSolution Spot(RoomContext ctx, string name)
        {
            RoomSolution s = ctx.Room.FindSolution(name);
            Assert.IsNotNull(s, ctx.Room.Title + " has no solution step '" + name + "'");
            return s;
        }

        protected static Vector3 W(RoomContext ctx, float x, float y, float z) => ctx.WorldRoot.TransformPoint(new Vector3(x, y, z));

        protected static Vector3 Local(RoomContext ctx, Vector3 world) => ctx.WorldRoot.InverseTransformPoint(world);

        protected string State => IonDebug.StateJson();

        /// <summary>GoTo through the harness ("zone" or "zone:spot"), then lets the controller settle on the ground.</summary>
        protected IEnumerator GoTo(string arg, int settleFrames = 10)
        {
            Assert.IsTrue(Dbg.TryGoTo(arg), "GoTo " + arg);
            for (int i = 0; i < settleFrames; i++) yield return null;
        }

        /// <summary>Teleports to a solution spot shifted by a room-local offset and a yaw error (forgiveness tests).</summary>
        protected IEnumerator GoToOff(RoomContext ctx, string spot, Vector3 localOffset, float yawError, int settleFrames = 10)
        {
            RoomSolution s = Spot(ctx, spot);
            Assert.IsTrue(Dbg.TryGoTo(ctx.Room.Key + ":" + spot));
            Vector3 feet = ctx.WorldRoot.TransformPoint(s.LocalFeet + localOffset);
            float yaw = ctx.SolutionYaw(s) + yawError;
            Player.Teleport(feet, yaw);
            Player.SetLook(yaw, s.Pitch);
            Physics.SyncTransforms();
            for (int i = 0; i < settleFrames; i++) yield return null;
        }

        protected static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        protected static IEnumerator Seconds(float s) => Frames(Mathf.CeilToInt(s / Step));

        // ------------------------------------------------------------------ photos

        protected PhotoData ShotPhoto(RoomContext ctx, int index = 0)
        {
            PhotoData p = ctx.GetShotPhoto(index);
            Assert.IsNotNull(p, ctx.Room.Title + " shot " + index + " not captured");
            return p;
        }

        /// <summary>The newest instant-camera snapshot in the inventory (null if none).</summary>
        protected PhotoData LatestSnapshot()
        {
            for (int i = Inventory.Count - 1; i >= 0; i--)
                if (Inventory.Photos[i] != null && Inventory.Photos[i].Label == InstantCamera.SnapshotLabel) return Inventory.Photos[i];
            return null;
        }

        /// <summary>
        /// Selects <paramref name="photo"/>, raises it, applies <paramref name="roll"/> with ±90° steps
        /// (as Q/E would) and places it — the same PhotoHolder path as Shift + LMB.
        /// </summary>
        protected IEnumerator RaiseRotatePlace(PhotoData photo, float roll)
        {
            int idx = Inventory.IndexOf(photo);
            Assert.GreaterOrEqual(idx, 0, "photo '" + photo.Label + "' not in the inventory. " + State);
            Assert.IsTrue(Dbg.TrySelect(idx.ToString()));
            Dbg.Raise("");
            yield return null; // a frame with the photo held up (PhotoHolder keeps it raised)
            Assert.IsTrue(Holder.IsRaised, "photo did not stay raised. " + State);
            int steps = Mathf.RoundToInt(Mathf.DeltaAngle(0f, roll) / 90f);
            for (int i = 0; i < Mathf.Abs(steps); i++)
            {
                Dbg.Rotate(steps > 0 ? "+1" : "-1");
                yield return null;
            }
            Assert.AreEqual(Mathf.Repeat(roll, 360f), Holder.RollDegrees, 0.01f, "roll");
            Assert.IsTrue(Holder.IsRaised);
            int placements = Projection.PlacementCount;
            Assert.IsTrue(Dbg.TryPlace(), "Place failed. " + State);
            Assert.AreEqual(placements + 1, Projection.PlacementCount);
            Assert.IsFalse(Inventory.Contains(photo), "a placed photo is consumed");
            yield return null;
            // Far cuts are spread over the next frames (ProjectionSystem.DeferFarCuts): wait for them like a player would.
            for (int i = 0; i < 120 && Projection.PendingCutCount > 0; i++) yield return null;
            Assert.AreEqual(0, Projection.PendingCutCount, "far cuts did not finish");
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ physical probes

        /// <summary>Downward ray from <paramref name="fromY"/> at world XZ of <paramref name="p"/>.</summary>
        protected static bool GroundAt(Vector3 p, float fromY, float maxDistance, out RaycastHit hit)
        {
            return Physics.Raycast(new Vector3(p.x, fromY, p.z), Vector3.down, out hit, maxDistance, WorldMask,
                                   QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Every <paramref name="step"/> metres from <paramref name="a"/> to <paramref name="b"/> (world), a downward
        /// ray must hit a collider within <paramref name="tolerance"/> of <paramref name="expectedHeight"/>(p).
        /// </summary>
        protected static List<string> ProbeFloor(Vector3 a, Vector3 b, float step, Func<Vector3, float> expectedHeight, float tolerance)
        {
            var failures = new List<string>();
            float len = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                float expected = expectedHeight(p);
                bool hit = GroundAt(p, expected + 2.5f, 6f, out RaycastHit h);
                if (!hit)
                    failures.Add($"no floor at ({p.x:0.00},{p.z:0.00}) expected y={expected:0.00}");
                else if (Mathf.Abs(h.point.y - expected) > tolerance)
                    failures.Add($"floor at ({p.x:0.00},{p.z:0.00}) is y={h.point.y:0.00} ('{h.collider.name}'), expected {expected:0.00}");
            }
            return failures;
        }

        /// <summary>True if a standing-player capsule moved from feet <paramref name="from"/> to <paramref name="to"/> hits nothing.</summary>
        protected static bool CapsuleClear(Vector3 from, Vector3 to, out RaycastHit hit, float radius = 0.33f)
        {
            Vector3 lo = from + Vector3.up * (PlayerFactory.Radius + 0.45f); // above the step offset
            Vector3 hi = from + Vector3.up * (PlayerFactory.Height - PlayerFactory.Radius);
            Vector3 d = to - from;
            return !Physics.CapsuleCast(lo, hi, radius, d.normalized, out hit, d.magnitude, WorldMask, QueryTriggerInteraction.Ignore);
        }

        protected sealed class WalkResult
        {
            public Vector3 Start, End;
            public float MinY = float.MaxValue, MaxY = float.MinValue;
            public bool Arrived, Grounded, Fell;
            public int Respawns;
            public float Seconds;
            public override string ToString() =>
                $"walk {Start} -> {End}: arrived={Arrived} grounded={Grounded} fell={Fell} minY={MinY:0.00} maxY={MaxY:0.00} respawns={Respawns} t={Seconds:0.0}s";
        }

        /// <summary>
        /// Walks the player (controller input, steering toward <paramref name="worldTarget"/>) until it arrives within
        /// <paramref name="arrive"/> m horizontally, falls (SafePoseTracker), or <paramref name="maxSeconds"/> pass.
        /// </summary>
        protected IEnumerator WalkTo(Vector3 worldTarget, float maxSeconds, WalkResult result, float arrive = 0.5f,
                                     bool stopOnFall = true)
        {
            int respawns0 = Player.RespawnCount;
            result.Start = Player.transform.position;
            Player.ScriptedWalkTo(worldTarget, maxSeconds, arrive * 0.6f);
            float t = 0f;
            var tracker = Tracker;
            while (t < maxSeconds + 0.5f)
            {
                yield return null;
                t += Step;
                Vector3 p = Player.transform.position;
                result.MinY = Mathf.Min(result.MinY, p.y);
                result.MaxY = Mathf.Max(result.MaxY, p.y);
                if (tracker != null && (tracker.IsFalling || tracker.InLimbo))
                {
                    result.Fell = true;
                    if (stopOnFall) break;
                }
                if (Player.RespawnCount != respawns0) break;
                if (!Player.IsScriptedWalking) break;
            }
            Player.StopScriptedWalk();
            if (!result.Fell)
            {
                // Let the player come to rest.
                for (int i = 0; i < 20; i++)
                {
                    yield return null;
                    result.MinY = Mathf.Min(result.MinY, Player.transform.position.y);
                    if (tracker != null && (tracker.IsFalling || tracker.InLimbo)) result.Fell = true;
                }
            }
            result.Seconds = t;
            result.End = Player.transform.position;
            result.Respawns = Player.RespawnCount - respawns0;
            Vector3 d = result.End - worldTarget;
            d.y = 0f;
            result.Arrived = d.magnitude <= arrive && result.Respawns == 0 && !result.Fell;
            result.Grounded = Player.IsGrounded;
            Debug.Log("[IonTest] " + TestContext.CurrentContext.Test.Name + " " + result);
        }

        static float LegSeconds(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            return d.magnitude / WalkSpeed * 1.6f + 2f;
        }

        /// <summary>Walks a step's waypoints (each must be reached), then toward its feet; the last leg's result.</summary>
        protected IEnumerator WalkStep(RoomContext ctx, RoomSolution s, WalkResult last, float arrive, string tag)
        {
            if (s.Via != null)
            {
                foreach (Vector3 v in s.Via)
                {
                    var leg = new WalkResult();
                    Vector3 target = ctx.World(v);
                    yield return WalkTo(target, LegSeconds(Player.transform.position, target), leg, 0.6f);
                    Assert.IsTrue(leg.Arrived, tag + ": waypoint " + v + " not reached. " + leg + " " + State);
                }
            }
            Vector3 feet = ctx.SolutionFeet(s);
            yield return WalkTo(feet, LegSeconds(Player.transform.position, feet), last, arrive);
        }

        /// <summary>Puts the feet exactly on the step's spot with its view (a careful player on the marker).</summary>
        protected IEnumerator Align(RoomContext ctx, RoomSolution s, string tag)
        {
            Vector3 feet = ctx.SolutionFeet(s);
            Vector3 d = Player.transform.position - feet;
            d.y = 0f;
            Assert.Less(d.magnitude, 1f, tag + ": not near the spot before aligning. " + State);
            float yaw = ctx.SolutionYaw(s);
            Player.Teleport(feet, yaw);
            Player.SetLook(yaw, s.Pitch);
            Physics.SyncTransforms();
            yield return Frames(10);
        }

        static RewindResult Expected(RoomSolution.RewindExpect e)
        {
            switch (e)
            {
                case RoomSolution.RewindExpect.Undid: return RewindResult.Undid;
                case RoomSolution.RewindExpect.RecoveredFall: return RewindResult.RecoveredFall;
                case RoomSolution.RewindExpect.ToCheckpoint: return RewindResult.ToCheckpoint;
                default: return RewindResult.Nothing;
            }
        }

        /// <summary>Single R that undoes the last world change even if the player is falling (R first recovers the fall).</summary>
        protected RewindResult UndoLast()
        {
            RewindResult r = Dbg.TryRewind();
            if (r == RewindResult.RecoveredFall) r = Dbg.TryRewind();
            return r;
        }

        /// <summary>Runs the named steps of a zone in the given order.</summary>
        protected IEnumerator RunSteps(RoomContext ctx, params string[] names)
        {
            foreach (string n in names) yield return RunStep(ctx, Spot(ctx, n));
        }

        /// <summary>Runs every step of a zone's Solutions, in order.</summary>
        protected IEnumerator RunAll(RoomContext ctx)
        {
            foreach (RoomSolution s in ctx.Room.Solutions) yield return RunStep(ctx, s);
        }

        /// <summary>Performs one solution step through the player's own code paths and asserts its outcome.</summary>
        protected IEnumerator RunStep(RoomContext ctx, RoomSolution s)
        {
            string tag = ctx.Room.Key + ":" + s.Name;
            Debug.Log("[IonTest] step " + tag + " (" + s.Action + ")");
            Assert.AreEqual(ctx.Index, Game.CurrentRoom, tag + ": the player is not in zone " + ctx.Room.Key + ". " + State);
            Vector3 feet = ctx.SolutionFeet(s);
            switch (s.Action)
            {
                case RoomSolution.Kind.Walk:
                case RoomSolution.Kind.Goal:
                {
                    var walk = new WalkResult();
                    yield return WalkStep(ctx, s, walk, s.Tolerance, tag);
                    switch (s.Hazard)
                    {
                        case RoomSolution.Hazards.None:
                            Assert.IsTrue(walk.Arrived, tag + ": not reached. " + walk + " " + State);
                            Assert.AreEqual(feet.y, walk.End.y, 0.35f, tag + ": wrong level. " + walk);
                            break;
                        case RoomSolution.Hazards.Blocked:
                            Assert.IsFalse(walk.Arrived, tag + ": should be out of reach. " + walk);
                            Assert.IsFalse(walk.Fell, tag + ": blocked, not a fall. " + walk);
                            Assert.Less(walk.MaxY, feet.y - 1f, tag + ": climbed to the unreachable level. " + walk);
                            break;
                        case RoomSolution.Hazards.Fall:
                            Assert.IsFalse(walk.Arrived, tag + ": should have fallen. " + walk);
                            Assert.IsTrue(walk.Fell, tag + ": the walk should end in a fall (falling / limbo). " + walk + " " + State);
                            break;
                        case RoomSolution.Hazards.Drop:
                            Assert.IsFalse(walk.Arrived, tag + ": should have dropped. " + walk);
                            Assert.IsFalse(walk.Fell, tag + ": a story drop is not a fall. " + walk + " " + State);
                            Assert.IsTrue(walk.Grounded, tag + ": should land grounded. " + walk);
                            Assert.Less(walk.End.y, walk.Start.y - 1f, tag + ": should be lower. " + walk);
                            break;
                    }
                    break;
                }

                case RoomSolution.Kind.Press:
                {
                    var walk = new WalkResult();
                    yield return WalkStep(ctx, s, walk, s.Tolerance, tag);
                    Assert.IsTrue(walk.Arrived, tag + ": press spot not reached. " + walk + " " + State);
                    Player.SetLook(ctx.SolutionYaw(s), s.Pitch);
                    Switch sw = null;
                    for (int i = 0; i < 180 && sw == null; i++)
                    {
                        sw = IonDebug.FindSwitchInReach(true);
                        if (sw == null) yield return null;
                    }
                    Assert.IsNotNull(sw, tag + ": no powered switch in reach. " + State);
                    string channel = sw.Channel;
                    if (!string.IsNullOrEmpty(s.Channel)) Assert.AreEqual(s.Channel, channel, tag + ": wrong switch");
                    bool before = SwitchBoard.Get(channel);
                    int depth = History.Depth;
                    Assert.IsNotNull(Dbg.TryPress(), tag + ": press refused. " + State);
                    Assert.AreNotEqual(before, SwitchBoard.Get(channel), tag + ": channel did not toggle");
                    Assert.AreEqual(depth + 1, History.Depth, tag + ": a press is a world change (history)");
                    // Let the movers on the channel finish.
                    for (int i = 0; i < 360; i++)
                    {
                        bool moving = false;
                        foreach (Mover m in UnityEngine.Object.FindObjectsByType<Mover>(FindObjectsSortMode.None))
                            if (m.Channel == channel && m.IsMoving) moving = true;
                        if (!moving && i > 5) break;
                        yield return null;
                    }
                    Physics.SyncTransforms();
                    break;
                }

                case RoomSolution.Kind.Pickup:
                {
                    int photos = Inventory.Count;
                    bool unlocked = Cam.Unlocked;
                    var walk = new WalkResult();
                    yield return WalkStep(ctx, s, walk, s.Tolerance + 0.3f, tag);
                    Player.SetLook(ctx.SolutionYaw(s), s.Pitch);
                    yield return Frames(10);
                    if (Inventory.Count == photos && Cam.Unlocked == unlocked)
                    {
                        // E: the nearest uncollected pickup in reach.
                        PhotoPickup best = null;
                        float bestD = PhotoPickup.InteractRange;
                        foreach (PhotoPickup p in UnityEngine.Object.FindObjectsByType<PhotoPickup>(FindObjectsSortMode.None))
                        {
                            if (p.Collected || !p.isActiveAndEnabled) continue;
                            float d = Vector3.Distance(p.FocusPoint, Player.Camera.transform.position);
                            if (d < bestD) { bestD = d; best = p; }
                        }
                        if (best != null) best.Collect(Inventory);
                        else
                        {
                            foreach (Ion.Gameplay.CameraPickup c in UnityEngine.Object.FindObjectsByType<Ion.Gameplay.CameraPickup>(FindObjectsSortMode.None))
                                if (!c.Taken && Vector3.Distance(c.transform.position, Player.transform.position) < 2.5f) c.Take(Player);
                        }
                        yield return null;
                    }
                    Assert.IsTrue(Inventory.Count > photos || (Cam.Unlocked && !unlocked), tag + ": nothing was picked up. " + walk + " " + State);
                    break;
                }

                case RoomSolution.Kind.Place:
                {
                    var walk = new WalkResult();
                    yield return WalkStep(ctx, s, walk, s.Tolerance, tag);
                    Assert.IsTrue(walk.Arrived, tag + ": marker not reached. " + walk + " " + State);
                    yield return Align(ctx, s, tag);
                    PhotoData photo = s.PhotoIndex >= 0 ? ShotPhoto(ctx, s.PhotoIndex) : LatestSnapshot();
                    Assert.IsNotNull(photo, tag + ": no photo to place. " + State);
                    Assert.IsTrue(Inventory.Contains(photo), tag + ": '" + photo.Label + "' is not in the inventory. " + State);
                    int depth = History.Depth;
                    yield return RaiseRotatePlace(photo, s.Roll);
                    Assert.AreEqual(depth + 1, History.Depth, tag + ": a placement is a world change (history)");
                    break;
                }

                case RoomSolution.Kind.Snap:
                {
                    var walk = new WalkResult();
                    yield return WalkStep(ctx, s, walk, s.Tolerance, tag);
                    Assert.IsTrue(walk.Arrived, tag + ": marker not reached. " + walk + " " + State);
                    yield return Align(ctx, s, tag);
                    int film = Cam.Film;
                    PhotoData snap = Dbg.TrySnap();
                    Assert.IsNotNull(snap, tag + ": snapshot failed. " + State);
                    Assert.AreEqual(film - 1, Cam.Film, tag + ": film");
                    Assert.Greater(snap.PieceCount, 0, tag + ": empty snapshot");
                    yield return null;
                    break;
                }

                case RoomSolution.Kind.Rewind:
                {
                    Vector3 before = Player.transform.position;
                    RewindResult r = Dbg.TryRewind();
                    Assert.AreEqual(Expected(s.Expect), r, tag + ": rewind result. " + State);
                    yield return Frames(40);
                    Vector3 p = Player.transform.position;
                    if (s.Expect == RoomSolution.RewindExpect.Nothing)
                    {
                        Assert.Less(Vector3.Distance(p, before), 0.15f, tag + ": nothing to rewind must not move the player. " + State);
                    }
                    else
                    {
                        Assert.IsTrue(Player.IsGrounded, tag + ": not standing after the rewind. " + State);
                        Vector3 d = p - feet;
                        Assert.AreEqual(feet.y, p.y, 0.4f, tag + ": wrong level after the rewind. " + State);
                        d.y = 0f;
                        Assert.LessOrEqual(d.magnitude, s.Tolerance, tag + ": restored too far from " + s.LocalFeet + ". " + State);
                    }
                    if (Tracker != null) Assert.IsFalse(Tracker.IsFalling || Tracker.InLimbo, tag + ": still falling. " + State);
                    break;
                }

                case RoomSolution.Kind.RewindToCheckpoint:
                {
                    RewindResult r = Dbg.TryRewindToCheckpoint();
                    Assert.AreEqual(RewindResult.ToCheckpoint, r, tag + ": rewind-to-checkpoint result. " + State);
                    yield return Frames(40);
                    Vector3 p = Player.transform.position;
                    Assert.IsTrue(Player.IsGrounded, tag + ": not standing after R R. " + State);
                    Vector3 d = p - feet;
                    Assert.AreEqual(feet.y, p.y, 0.3f, tag + ": wrong level after R R. " + State);
                    d.y = 0f;
                    Assert.LessOrEqual(d.magnitude, s.Tolerance, tag + ": not back at the checkpoint " + s.LocalFeet + ". " + State);
                    break;
                }

                case RoomSolution.Kind.Teleport:
                {
                    Teleporter tp = NearestTeleporter(feet);
                    Assert.IsNotNull(tp, tag + ": no active teleporter at " + s.LocalFeet + ". " + State);
                    int fired = tp.FireCount;
                    int zone = Game.CurrentRoom;
                    var walk = new WalkResult();
                    yield return WalkStep(ctx, s, walk, 0.4f, tag);
                    int dest = string.IsNullOrEmpty(s.Destination) ? -1 : Game.IndexOfKey(s.Destination);
                    for (int i = 0; i < 240; i++)
                    {
                        if (dest >= 0 ? Game.CurrentRoom == dest : tp.FireCount > fired) break;
                        yield return null;
                    }
                    Assert.Greater(tp.FireCount, fired, tag + ": the teleporter never fired. " + walk + " " + State);
                    if (dest >= 0)
                        Assert.AreEqual(dest, Game.CurrentRoom, tag + ": should lead to '" + s.Destination + "'. " + State);
                    else
                        Assert.AreEqual(zone, Game.CurrentRoom, tag + ": should stay in the zone. " + State);
                    yield return Frames(30);
                    Physics.SyncTransforms();
                    break;
                }
            }
        }

        /// <summary>The active teleporter (original or photo copy) whose pad is nearest <paramref name="world"/> (within 1.5 m).</summary>
        protected static Teleporter NearestTeleporter(Vector3 world)
        {
            Teleporter best = null;
            float bestD = 1.5f;
            foreach (Teleporter t in UnityEngine.Object.FindObjectsByType<Teleporter>(FindObjectsSortMode.None))
            {
                if (!t.isActiveAndEnabled) continue;
                Vector3 d = t.transform.position - world;
                d.y *= 0.5f;
                float m = d.magnitude;
                if (m < bestD) { bestD = m; best = t; }
            }
            return best;
        }

        protected static int ActiveNamed(string suffix)
        {
            int n = 0;
            foreach (var mf in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (mf.gameObject.activeInHierarchy && mf.gameObject.name.EndsWith(suffix, StringComparison.Ordinal)) n++;
            return n;
        }

        protected static int LiveSliceables()
        {
            int n = 0;
            foreach (var s in UnityEngine.Object.FindObjectsByType<Sliceable>(FindObjectsSortMode.None))
            {
                if (!s.enabled || !s.gameObject.activeInHierarchy) continue;
                var r = s.GetComponent<MeshRenderer>();
                var c = s.GetComponent<Collider>();
                if (r != null && r.enabled && c != null && c.enabled) n++;
            }
            return n;
        }
    }
}
