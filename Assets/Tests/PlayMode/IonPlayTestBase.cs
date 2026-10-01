using System;
using System.Collections;
using System.Collections.Generic;
using Ion.DebugTools;
using Ion.Gameplay;
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
    /// photos, and runs time at a fixed 60 Hz (Time.captureDeltaTime) so controller physics is
    /// deterministic regardless of how fast the batch-mode editor renders frames.
    /// </summary>
    public abstract class IonPlayTestBase
    {
        protected const string MainScene = "Main";
        protected const int PlayerLayerMask = 1 << ProjectionSystem.PlayerLayer;
        protected const int WorldMask = ~ProjectionSystem.ExcludedLayerMask;
        protected const float Step = 1f / 60f;

        protected GameBootstrap Game => GameBootstrap.Instance;
        protected FirstPersonController Player => Game.Player;
        protected PhotoInventory Inventory => Player.GetComponent<PhotoInventory>();
        protected PhotoHolder Holder => Player.GetComponent<PhotoHolder>();
        protected InstantCamera Cam => Player.GetComponent<InstantCamera>();
        protected ProjectionSystem Projection => ProjectionSystem.Instance;
        protected IonDebug Dbg;

        [UnitySetUp]
        public IEnumerator LoadGame()
        {
            Time.captureDeltaTime = Step;
            SceneManager.LoadScene(MainScene, LoadSceneMode.Single);
            yield return null;

            for (int frame = 0; frame < 600; frame++)
            {
                if (GameReady()) break;
                yield return null;
            }
            Assert.IsTrue(GameReady(), "Main.unity did not finish building + capturing the diorama photos.");
            // Let the bootstrap deactivate the dioramas and the UI settle.
            yield return null;
            yield return null;
            Physics.SyncTransforms();
            Dbg = IonDebug.Ensure();
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            Time.captureDeltaTime = 0f;
            yield return null;
        }

        static bool GameReady()
        {
            var game = GameBootstrap.Instance;
            if (game == null || game.Player == null || game.Rooms.Count == 0) return false;
            for (int r = 0; r < game.Rooms.Count; r++)
            {
                var ctx = game.Rooms[r];
                for (int s = 0; s < ctx.ShotCount; s++)
                    if (ctx.GetShotPhoto(s) == null) return false;
            }
            var ps = ProjectionSystem.Instance;
            return ps != null && ps.PendingPreviewCount == 0;
        }

        // ------------------------------------------------------------------ rooms / spots

        protected RoomContext Room(string key)
        {
            int i = IonDebug.FindRoom(key);
            Assert.GreaterOrEqual(i, 0, "unknown room " + key);
            return Game.Rooms[i];
        }

        protected static RoomSolution Spot(RoomContext ctx, string name)
        {
            RoomSolution s = ctx.Room.FindSolution(name);
            Assert.IsNotNull(s, ctx.Room.Title + " has no solution spot '" + name + "'");
            return s;
        }

        protected static Vector3 W(RoomContext ctx, float x, float y, float z) => ctx.WorldRoot.TransformPoint(new Vector3(x, y, z));

        protected string State => IonDebug.StateJson();

        /// <summary>GoTo through the harness, then lets the controller settle on the ground.</summary>
        protected IEnumerator GoTo(string arg, int settleFrames = 10)
        {
            Assert.IsTrue(Dbg.TryGoTo(arg), "GoTo " + arg);
            for (int i = 0; i < settleFrames; i++) yield return null;
        }

        /// <summary>Teleports to a solution spot shifted by a room-local offset and a yaw error (forgiveness tests).</summary>
        protected IEnumerator GoToOff(RoomContext ctx, string spot, Vector3 localOffset, float yawError, int settleFrames = 10)
        {
            RoomSolution s = Spot(ctx, spot);
            Assert.IsTrue(Dbg.TryGoTo((ctx.Index + 1) + ":" + spot));
            Vector3 feet = ctx.WorldRoot.TransformPoint(s.LocalFeet + localOffset);
            float yaw = ctx.SolutionYaw(s) + yawError;
            Player.Teleport(feet, yaw);
            Player.SetLook(yaw, s.Pitch);
            Physics.SyncTransforms();
            for (int i = 0; i < settleFrames; i++) yield return null;
        }

        // ------------------------------------------------------------------ photos

        protected PhotoData ShotPhoto(RoomContext ctx, int index = 0)
        {
            PhotoData p = ctx.GetShotPhoto(index);
            Assert.IsNotNull(p, ctx.Room.Title + " shot " + index + " not captured");
            return p;
        }

        /// <summary>
        /// Selects <paramref name="photo"/>, raises it, applies <paramref name="roll"/> with ±90° steps
        /// (as Q/E would) and places it — the same PhotoHolder path as the mouse.
        /// </summary>
        protected IEnumerator RaiseRotatePlace(PhotoData photo, float roll)
        {
            int idx = Inventory.IndexOf(photo);
            Assert.GreaterOrEqual(idx, 0, "photo '" + photo.Label + "' not in the inventory. " + State);
            Assert.IsTrue(Dbg.TrySelect(idx.ToString()));
            Dbg.Raise("");
            yield return null; // a frame with the photo held up (PhotoHolder.Update keeps it raised)
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
        /// Every <paramref name="step"/> metres from <paramref name="a"/> to <paramref name="b"/> (world),
        /// a downward ray must hit a collider within <paramref name="tolerance"/> of
        /// <paramref name="expectedHeight"/>(t) (t in 0..1 along the segment). Returns failures.
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
            public bool Arrived, Grounded;
            public int Respawns;
            public float Seconds;
            public override string ToString() =>
                $"walk {Start} -> {End}: arrived={Arrived} grounded={Grounded} minY={MinY:0.00} maxY={MaxY:0.00} respawns={Respawns} t={Seconds:0.0}s";
        }

        /// <summary>
        /// Walks the player (controller input, steering toward <paramref name="worldTarget"/>) until it
        /// arrives within <paramref name="arrive"/> m horizontally or <paramref name="maxSeconds"/> pass.
        /// </summary>
        protected IEnumerator WalkTo(Vector3 worldTarget, float maxSeconds, WalkResult result, float arrive = 0.5f)
        {
            int respawns0 = Player.RespawnCount;
            result.Start = Player.transform.position;
            Player.ScriptedWalkTo(worldTarget, maxSeconds, arrive * 0.6f);
            float t = 0f;
            while (t < maxSeconds + 0.5f)
            {
                yield return null;
                t += Step;
                Vector3 p = Player.transform.position;
                result.MinY = Mathf.Min(result.MinY, p.y);
                result.MaxY = Mathf.Max(result.MaxY, p.y);
                if (Player.RespawnCount != respawns0) break;
                if (!Player.IsScriptedWalking) break;
            }
            Player.StopScriptedWalk();
            // Let the player come to rest.
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                result.MinY = Mathf.Min(result.MinY, Player.transform.position.y);
            }
            result.Seconds = t;
            result.End = Player.transform.position;
            result.Respawns = Player.RespawnCount - respawns0;
            Vector3 d = result.End - worldTarget;
            d.y = 0f;
            result.Arrived = d.magnitude <= arrive && result.Respawns == 0;
            result.Grounded = Player.IsGrounded;
            Debug.Log("[IonTest] " + TestContext.CurrentContext.Test.Name + " " + result);
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
