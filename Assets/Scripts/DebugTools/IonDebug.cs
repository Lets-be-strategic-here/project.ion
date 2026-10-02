using System;
using System.Globalization;
using System.Text;
using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels;
using Ion.Projection;
using UnityEngine;

namespace Ion.DebugTools
{
    /// <summary>
    /// Debug / automation harness. Installs itself after the first scene loads as a persistent GameObject
    /// named exactly "IonDebug", so a web page can drive the game with
    /// <c>unityInstance.SendMessage("IonDebug", "GoTo", "t1:place")</c>. Every action goes through the
    /// same components a player uses (FirstPersonController, PhotoHolder, InstantCamera, Switch, WorldHistory).
    /// Each method takes one string (SendMessage contract) and is also callable from C# (PlayMode tests).
    ///
    ///   GoTo("zone:spot")   zone = 0-based index, key ("t1", "t2", "hub", "stairs", "camera", "gallery") or a
    ///                       name fragment; spot = "spawn" (default) or a <see cref="RoomSolution"/> name ("place",
    ///                       "n.pickup", "far", ...). Enters the zone like a teleporter (zone-entry checkpoint),
    ///                       then puts the feet on the spot with the spot's view.
    ///   Zone("name")        = GoTo("name") (the zone's arrival).                         alias: zone
    ///   Look("yaw,pitch")   degrees (yaw 0 = +Z, pitch positive = down).
    ///   GiveAll("")         every pre-made photo into the inventory + unlocks the camera (3 film min).
    ///   Select("i")         inventory index (0-based) or a label substring ("Ledge", "Snapshot").
    ///   Raise("") / Lower("")   hold the selected photo up / put it down.
    ///   Rotate("+1|-1")     ±90° roll steps, instant (+1 = Q, -1 = E).
    ///   RotateHold("+1|-1|0") hold Q (+1) / E (-1) through the player's eased rotation; 0 releases.
    ///   Place("")           places the held photo (raises it first if needed).
    ///   Rewind("")          single R through WorldHistory, instantly (lowers a raised photo first). alias: rewind
    ///   Rewind2("")         R R: back to the last checkpoint, instantly.                     alias: rewind2
    ///   RewindPress("")     one real press of R (RewindController.PressRewind): the glide back with its
    ///                       effects, the fall-recovery fade, or the "nothing" feedback; two presses within
    ///                       0.35 s escalate to the checkpoint (as Rewind2Press("") does).   alias: rewindpress
    ///   TimeScale("s")      Time.timeScale (0.05..4; "" = 1): slow a transition down for screenshots.
    ///   Press("")           presses the powered switch in reach (nearest, in view) like E.    alias: press
    ///   Checkpoint("id")    sets a checkpoint at the current pose (id default "debug").     alias: checkpoint
    ///   Snap("")            instant-camera photo from the current view (camera must be unlocked).
    ///   Walk("x,y,z[,s]")   walks (real movement input) to a point local to the current zone.  alias: walk
    ///   Move("x,z,seconds") holds movement input (x = strafe right, z = forward, -1..1) for some seconds.
    ///   WalkTo("x,z[,s]")   walks toward a zone-local point (or WalkTo("far"): a spot name).
    ///   State("")           logs "[IonDebug] {json}".
    ///   Quality("-1|0|1|2|3") sets the quality preference (Auto / Low / Med / High / Ultra).
    ///   Spectate("x,y,z,yaw,pitch")  freezes the player and puts the camera at a zone-local pose
    ///                       (screenshots from outside); Spectate("") hands it back.
    ///   Place logs "[IonDebug] Place took N ms" (the whole cut + paste, measured around PhotoHolder.Place).
    ///
    /// Availability: always in the Editor (Play mode, PlayMode tests). In a Web player build the harness
    /// only installs when the page URL carries <c>?debug=1</c> (e.g. http://host/?debug=1), so a public
    /// build cannot be driven (teleport, give all photos) from the browser console.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IonDebug : MonoBehaviour
    {
        public const string ObjectName = "IonDebug";

        public static IonDebug Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() => Ensure();

        /// <summary>
        /// True when the harness may run: always in the Editor and non-web players; in a Web player only
        /// with a <c>debug=1</c> (or <c>debug=true</c>) query parameter in the page URL.
        /// </summary>
        public static bool Allowed
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return UrlHasDebugFlag(Application.absoluteURL);
#else
                return true;
#endif
            }
        }

        /// <summary>True if <paramref name="url"/>'s query string has debug=1 / debug=true.</summary>
        public static bool UrlHasDebugFlag(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            int q = url.IndexOf('?');
            if (q < 0) return false;
            int hash = url.IndexOf('#', q);
            string query = hash >= 0 ? url.Substring(q + 1, hash - q - 1) : url.Substring(q + 1);
            foreach (string pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                string key = eq >= 0 ? pair.Substring(0, eq) : pair;
                string value = eq >= 0 ? pair.Substring(eq + 1) : string.Empty;
                if (!key.Equals("debug", StringComparison.OrdinalIgnoreCase)) continue;
                if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Returns the harness, creating it if needed (null when not <see cref="Allowed"/>).</summary>
        public static IonDebug Ensure()
        {
            if (Instance != null) return Instance;
            if (!Allowed) return null;
            var existing = FindFirstObjectByType<IonDebug>();
            if (existing != null) return Instance = existing;
            var go = new GameObject(ObjectName);
            Instance = go.AddComponent<IonDebug>();
            return Instance;
        }

        void Awake()
        {
            if (!Allowed)
            {
                Destroy(gameObject);
                return;
            }
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            gameObject.name = ObjectName;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ lookups

        static GameBootstrap Game => GameBootstrap.Instance;
        static FirstPersonController Player => Game != null && Game.Player != null ? Game.Player : FirstPersonController.Current;
        static T PlayerComponent<T>() where T : Component => Player != null ? Player.GetComponent<T>() : null;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static float[] ParseFloats(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) return Array.Empty<float>();
            string[] parts = arg.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, Inv, out result[i]))
                    return null;
            }
            return result;
        }

        /// <summary>
        /// Zone index (0-based) from a key ("t1", "hub"), a 0-based index ("2") or a title / type-name fragment;
        /// -1 if not found. Empty = the current zone.
        /// </summary>
        public static int FindRoom(string key)
        {
            var game = Game;
            if (game == null) return -1;
            key = (key ?? string.Empty).Trim();
            if (key.Length == 0) return game.CurrentRoom;
            for (int i = 0; i < game.Rooms.Count; i++)
                if (string.Equals(game.Rooms[i].Room.Key, key, StringComparison.OrdinalIgnoreCase)) return i;
            if (int.TryParse(key, NumberStyles.Integer, Inv, out int n))
                return n >= 0 && n < game.Rooms.Count ? n : -1;
            for (int i = 0; i < game.Rooms.Count; i++)
            {
                Room room = game.Rooms[i].Room;
                if (room.Title.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    room.GetType().Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
            return -1;
        }

        static void Log(string msg) => Debug.Log("[IonDebug] " + msg);
        static void Warn(string msg) => Debug.LogWarning("[IonDebug] " + msg);

        // ------------------------------------------------------------------ commands

        /// <summary>"room:spot" — see the class summary. Returns false if the room/spot is unknown.</summary>
        public void GoTo(string arg) => TryGoTo(arg);

        public bool TryGoTo(string arg)
        {
            var game = Game;
            var player = Player;
            if (game == null || player == null) { Warn("GoTo: game not ready"); return false; }

            string roomKey = arg ?? string.Empty, spot = "spawn";
            int colon = roomKey.IndexOf(':');
            if (colon >= 0)
            {
                spot = roomKey.Substring(colon + 1).Trim();
                roomKey = roomKey.Substring(0, colon);
            }
            int index = FindRoom(roomKey);
            if (index < 0) { Warn("GoTo: unknown room '" + roomKey + "'"); return false; }

            RoomContext ctx = game.Rooms[index];
            RoomSolution sol = null;
            if (spot.Length > 0 && !spot.Equals("spawn", StringComparison.OrdinalIgnoreCase))
            {
                sol = ctx.Room.FindSolution(spot);
                if (sol == null) { Warn("GoTo: zone " + ctx.Room.Key + " has no spot '" + spot + "'"); return false; }
            }

            game.GoToRoom(index); // current zone, mood, zone-entry checkpoint and arrival toast, like a teleporter
            if (sol != null)
            {
                player.Teleport(ctx.SolutionFeet(sol), ctx.SolutionYaw(sol));
                player.SetLook(ctx.SolutionYaw(sol), sol.Pitch);
            }
            Physics.SyncTransforms();
            Log("GoTo zone " + ctx.Room.Key + " spot '" + (sol != null ? sol.Name : "spawn") + "'");
            return true;
        }

        public void Look(string arg)
        {
            float[] v = ParseFloats(arg);
            var player = Player;
            if (player == null || v == null || v.Length < 1) { Warn("Look: expected 'yaw,pitch'"); return; }
            player.SetLook(v[0], v.Length > 1 ? v[1] : 0f);
        }

        /// <summary>Adds every captured pre-made photo to the inventory and unlocks the instant camera.</summary>
        public void GiveAll(string arg)
        {
            var game = Game;
            var inv = PlayerComponent<PhotoInventory>();
            if (game == null || inv == null) { Warn("GiveAll: game not ready"); return; }
            int added = 0;
            for (int r = 0; r < game.Rooms.Count; r++)
            {
                RoomContext ctx = game.Rooms[r];
                for (int s = 0; s < ctx.ShotCount; s++)
                {
                    PhotoData p = ctx.GetShotPhoto(s);
                    if (p == null || inv.Contains(p)) continue;
                    inv.Add(p);
                    added++;
                }
            }
            RoomContext.UnlockInstantCamera(3);
            if (inv.Count > 0) inv.SelectedIndex = 0;
            Log("GiveAll: +" + added + " photos, inventory " + inv.Count);
        }

        /// <summary>Selects by inventory index or label substring. Returns false if nothing matched.</summary>
        public void Select(string arg) => TrySelect(arg);

        public bool TrySelect(string arg)
        {
            var inv = PlayerComponent<PhotoInventory>();
            if (inv == null || inv.Count == 0) { Warn("Select: inventory empty"); return false; }
            arg = (arg ?? string.Empty).Trim();
            if (int.TryParse(arg, NumberStyles.Integer, Inv, out int i))
            {
                if (i < 0 || i >= inv.Count) { Warn("Select: index out of range"); return false; }
                inv.SelectedIndex = i;
                return true;
            }
            // Last match wins, so "Snapshot" picks the newest snapshot.
            int found = -1;
            for (int k = 0; k < inv.Count; k++)
            {
                string label = inv.Photos[k].Label ?? string.Empty;
                if (label.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0) found = k;
            }
            if (found < 0) { Warn("Select: no photo labelled '" + arg + "'"); return false; }
            inv.SelectedIndex = found;
            return true;
        }

        public void Raise(string arg)
        {
            var holder = PlayerComponent<PhotoHolder>();
            if (holder == null || !holder.Raise()) Warn("Raise: nothing to raise");
        }

        public void Lower(string arg)
        {
            var holder = PlayerComponent<PhotoHolder>();
            if (holder != null) holder.Lower();
        }

        public void Rotate(string arg)
        {
            var holder = PlayerComponent<PhotoHolder>();
            if (holder == null) return;
            float[] v = ParseFloats(arg);
            float steps = v != null && v.Length > 0 ? v[0] : 1f;
            holder.Rotate(90f * Mathf.Round(steps));
        }

        public void RotateHold(string arg)
        {
            var holder = PlayerComponent<PhotoHolder>();
            if (holder == null) return;
            float[] v = ParseFloats(arg);
            holder.AutomationRotate = v != null && v.Length > 0 ? Mathf.Clamp(v[0], -1f, 1f) : 0f;
        }

        public void Place(string arg) => TryPlace();

        public bool TryPlace()
        {
            var holder = PlayerComponent<PhotoHolder>();
            if (holder == null) return false;
            if (!holder.IsRaised && !holder.Raise()) { Warn("Place: nothing to place"); return false; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = holder.Place();
            sw.Stop();
            var ps = ProjectionSystem.Instance;
            Log("Place took " + sw.Elapsed.TotalMilliseconds.ToString("0.0", Inv) + " ms" +
                (ps != null ? " (" + ps.LastPlaceProfile + ")" : string.Empty));
            Physics.SyncTransforms();
            if (!ok) Warn("Place: failed");
            return ok;
        }

        /// <summary>
        /// Places through the player's LMB path (press-in, cuts staged behind the card over the next frames). Logs the
        /// begin frame's cost now and "Staged place done" with the frame count when the swap happens.
        /// </summary>
        public void PlacePress(string arg)
        {
            var holder = PlayerComponent<PhotoHolder>();
            if (holder == null) return;
            if (!holder.IsRaised && !holder.Raise()) { Warn("PlacePress: nothing to place"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = holder.PlaceWithPress();
            sw.Stop();
            var ps = ProjectionSystem.Instance;
            Log("PlacePress begin took " + sw.Elapsed.TotalMilliseconds.ToString("0.0", Inv) + " ms" +
                (ps != null ? " (" + ps.LastPlaceProfile + ")" : string.Empty));
            if (!ok) { Warn("PlacePress: failed"); return; }
            StartCoroutine(WatchPress(holder, Time.frameCount, Time.realtimeSinceStartup));
        }

        System.Collections.IEnumerator WatchPress(PhotoHolder holder, int frame0, float t0)
        {
            while (holder != null && holder.IsPlacing) yield return null;
            var ps = ProjectionSystem.Instance;
            Log("Staged place done after " + (Time.frameCount - frame0) + " frames, " +
                ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0", Inv) + " ms; pending cuts " +
                (ps != null ? ps.PendingCutCount : 0) + "; staged work max " +
                (ps != null ? ps.LastStageMaxFrameMs : 0f).ToString("0.0", Inv) + " ms/frame over " +
                (ps != null ? ps.LastStageFrames : 0) + " frames; swap " + (holder != null ? holder.LastCommitMs : 0f).ToString("0.0", Inv) + " ms");
        }

        // ------------------------------------------------------------------ rewind / checkpoints / switches

        /// <summary>Single R (art bible §11.2): lowers a raised photo, then WorldHistory.RewindOnce.</summary>
        public void Rewind(string arg) => TryRewind();

        public void rewind(string arg) => TryRewind();

        public RewindResult TryRewind()
        {
            var holder = PlayerComponent<PhotoHolder>();
            if (holder != null && holder.IsRaised) holder.Lower();
            var history = WorldHistory.Instance;
            if (history == null) { Warn("Rewind: no WorldHistory"); return RewindResult.Nothing; }
            RewindResult r = history.RewindOnce();
            Physics.SyncTransforms();
            Log("Rewind -> " + r);
            return r;
        }

        /// <summary>
        /// One real press of R through <see cref="RewindController.PressRewind"/>: the full visual path (the glide
        /// back with its desaturation, tape bands, vignette, tape sound and returning photo; the fall-recovery
        /// fade; the "nothing" feedback). A second press within 0.35 s escalates to the checkpoint.
        /// </summary>
        public void RewindPress(string arg)
        {
            var rc = PlayerComponent<RewindController>();
            if (rc == null) { Warn("RewindPress: no RewindController"); return; }
            rc.PressRewind();
            Log("RewindPress -> " + (rc.IsGliding ? "glide " + rc.GlideSeconds.ToString("0.00", Inv) + " s to " + rc.GlideTarget
                                                  : rc.Busy ? "transition" : rc.LastResult.ToString()));
        }

        public void rewindpress(string arg) => RewindPress(arg);

        /// <summary>R R through the real path: the checkpoint iris and restore.</summary>
        public void Rewind2Press(string arg)
        {
            var rc = PlayerComponent<RewindController>();
            if (rc == null) { Warn("Rewind2Press: no RewindController"); return; }
            rc.RewindToCheckpointNow();
            Log("Rewind2Press -> checkpoint transition");
        }

        /// <summary>Time.timeScale for screenshots of transitions ("" = 1, clamped to 0.05..4).</summary>
        public void TimeScale(string arg)
        {
            float s = 1f;
            if (!string.IsNullOrWhiteSpace(arg) && !float.TryParse(arg.Trim(), System.Globalization.NumberStyles.Float, Inv, out s)) s = 1f;
            Time.timeScale = Mathf.Clamp(s, 0.05f, 4f);
            Log("TimeScale " + Time.timeScale.ToString("0.00", Inv));
        }

        /// <summary>R R: back to the last checkpoint (world, inventory, switches, pose).</summary>
        public void Rewind2(string arg) => TryRewindToCheckpoint();

        public void rewind2(string arg) => TryRewindToCheckpoint();

        public RewindResult TryRewindToCheckpoint()
        {
            var holder = PlayerComponent<PhotoHolder>();
            if (holder != null && holder.IsRaised) holder.Lower();
            var history = WorldHistory.Instance;
            if (history == null) { Warn("Rewind2: no WorldHistory"); return RewindResult.Nothing; }
            RewindResult r = history.RewindToCheckpoint();
            Physics.SyncTransforms();
            Log("Rewind2 -> " + r);
            return r;
        }

        /// <summary>Sets a checkpoint at the current pose (id = arg, default "debug").</summary>
        public void Checkpoint(string arg)
        {
            var history = WorldHistory.Instance;
            var player = Player;
            if (history == null || player == null) { Warn("Checkpoint: not ready"); return; }
            string id = string.IsNullOrWhiteSpace(arg) ? "debug" : arg.Trim();
            history.SetCheckpoint(id, PlayerPose.Of(player));
            Log("Checkpoint '" + id + "' at " + PlayerPose.Of(player));
        }

        public void checkpoint(string arg) => Checkpoint(arg);

        /// <summary>Presses the switch the player could press with E: powered, in reach, nearest (in view first).</summary>
        public void Press(string arg) => TryPress();

        public void press(string arg) => TryPress();

        public Switch TryPress()
        {
            Switch sw = FindSwitchInReach(true);
            if (sw == null) { Warn("Press: no powered switch in reach"); return null; }
            bool ok = sw.Press();
            Physics.SyncTransforms();
            Log("Press '" + sw.Channel + "' -> " + (ok ? (sw.On ? "on" : "off") : "refused"));
            return ok ? sw : null;
        }

        /// <summary>The switch nearest the player's eye within its interact range (+0.5 m), preferring ones in view.</summary>
        public static Switch FindSwitchInReach(bool poweredOnly)
        {
            var player = Player;
            if (player == null) return null;
            Transform eye = player.Camera != null ? player.Camera.transform : player.transform;
            Switch best = null;
            float bestScore = float.MaxValue;
            foreach (Switch sw in FindObjectsByType<Switch>(FindObjectsSortMode.None))
            {
                if (sw == null || !sw.isActiveAndEnabled || (poweredOnly && !sw.Powered)) continue;
                Vector3 d = sw.FocusPoint - eye.position;
                float dist = d.magnitude;
                if (dist > sw.InteractRange + 0.5f) continue;
                bool inView = dist < 1.2f || Vector3.Dot(eye.forward, d) > 0.35f * dist;
                float score = dist + (inView ? 0f : 100f);
                if (score < bestScore) { bestScore = score; best = sw; }
            }
            return best;
        }

        public void Snap(string arg) => TrySnap();

        public PhotoData TrySnap()
        {
            var cam = PlayerComponent<InstantCamera>();
            if (cam == null || !cam.Unlocked) { Warn("Snap: the instant camera is locked"); return null; }
            var holder = PlayerComponent<PhotoHolder>();
            if (holder != null) holder.Lower();
            cam.SetCameraMode(true);
            PhotoData photo = cam.TryCapture();
            cam.SetCameraMode(false);
            if (photo == null) Warn("Snap: no photo (film " + cam.Film + ")");
            return photo;
        }

        /// <summary>Walks (real movement input) to a point local to the current zone: "x,y,z[,seconds]".</summary>
        public void Walk(string arg)
        {
            var game = Game;
            var player = Player;
            float[] v = ParseFloats(arg);
            if (game == null || player == null || v == null || v.Length < 3) { Warn("Walk: expected 'x,y,z[,seconds]'"); return; }
            RoomContext ctx = game.Rooms[game.CurrentRoom];
            Vector3 target = ctx.WorldRoot.TransformPoint(new Vector3(v[0], v[1], v[2]));
            player.ScriptedWalkTo(target, v.Length > 3 ? v[3] : 15f);
        }

        public void walk(string arg) => Walk(arg);

        /// <summary>Holds movement input "x,z,seconds" (x = strafe right, z = forward, each -1..1).</summary>
        public void Move(string arg)
        {
            float[] v = ParseFloats(arg);
            var player = Player;
            if (player == null || v == null || v.Length < 3) { Warn("Move: expected 'x,z,seconds'"); return; }
            player.ScriptedWalk(new Vector2(v[0], v[1]), v[2]);
        }

        /// <summary>Enters a zone (its arrival pose), like GoTo with no spot.</summary>
        public void Zone(string arg) => TryGoTo(arg);

        public void zone(string arg) => TryGoTo(arg);

        public void WalkTo(string arg)
        {
            var game = Game;
            var player = Player;
            if (game == null || player == null) return;
            RoomContext ctx = game.Rooms[game.CurrentRoom];
            float[] v = ParseFloats(arg);
            Vector3 target;
            float seconds = 15f;
            if (v != null && v.Length >= 2)
            {
                target = ctx.WorldRoot.TransformPoint(new Vector3(v[0], 0f, v[1]));
                if (v.Length >= 3) seconds = v[2];
            }
            else
            {
                RoomSolution sol = ctx.Room.FindSolution((arg ?? string.Empty).Trim());
                if (sol == null) { Warn("WalkTo: expected 'x,z[,seconds]' or a spot name"); return; }
                target = ctx.SolutionFeet(sol);
            }
            player.ScriptedWalkTo(target, seconds);
        }

        public void State(string arg) => Log(StateJson());

        public void Quality(string arg)
        {
            if (!int.TryParse((arg ?? string.Empty).Trim(), NumberStyles.Integer, Inv, out int tier)) { Warn("Quality: expected -1..3"); return; }
            Ion.Presentation.QualityTier.Set(tier);
            Log("Quality " + Ion.Presentation.QualityTier.Name(Ion.Presentation.QualityTier.Preference) +
                " -> tier " + Ion.Presentation.QualityTier.Name(Ion.Presentation.QualityTier.Current));
        }

        Transform _spectateCam;
        Vector3 _spectateLocalPos;
        Quaternion _spectateLocalRot;

        public void Spectate(string arg)
        {
            var player = Player;
            if (player == null || player.Camera == null) return;
            float[] v = ParseFloats(arg);
            if (v == null || v.Length < 3)
            {
                if (_spectateCam != null)
                {
                    _spectateCam.localPosition = _spectateLocalPos;
                    _spectateCam.localRotation = _spectateLocalRot;
                    _spectateCam = null;
                    player.enabled = true;
                }
                return;
            }
            Transform cam = player.Camera.transform;
            if (_spectateCam == null)
            {
                _spectateCam = cam;
                _spectateLocalPos = cam.localPosition;
                _spectateLocalRot = cam.localRotation;
            }
            player.enabled = false;
            var game = Game;
            Transform room = game != null ? game.Rooms[game.CurrentRoom].WorldRoot : null;
            Vector3 pos = room != null ? room.TransformPoint(new Vector3(v[0], v[1], v[2])) : new Vector3(v[0], v[1], v[2]);
            cam.SetPositionAndRotation(pos, Quaternion.Euler(v.Length > 4 ? v[4] : 0f, v.Length > 3 ? v[3] : 0f, 0f));
        }

        /// <summary>One-line JSON snapshot of the game state.</summary>
        public static string StateJson()
        {
            var game = Game;
            var player = Player;
            var ps = ProjectionSystem.Instance;
            var inv = PlayerComponent<PhotoInventory>();
            var holder = PlayerComponent<PhotoHolder>();
            var cam = PlayerComponent<InstantCamera>();

            var sb = new StringBuilder(256);
            sb.Append('{');
            int room = game != null ? game.CurrentRoom : -1;
            sb.Append("\"room\":").Append(room);
            if (game != null && room >= 0 && room < game.Rooms.Count)
            {
                sb.Append(",\"zone\":\"").Append(Escape(game.Rooms[room].Room.Key)).Append('"');
                sb.Append(",\"roomTitle\":\"").Append(Escape(game.Rooms[room].Room.Title)).Append('"');
            }
            if (player != null)
            {
                Vector3 p = player.transform.position;
                sb.Append(",\"pos\":[").Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z)).Append(']');
                if (game != null && room >= 0 && room < game.Rooms.Count)
                {
                    Vector3 l = game.Rooms[room].WorldRoot.InverseTransformPoint(p);
                    sb.Append(",\"local\":[").Append(F(l.x)).Append(',').Append(F(l.y)).Append(',').Append(F(l.z)).Append(']');
                }
                sb.Append(",\"yaw\":").Append(F(player.Yaw)).Append(",\"pitch\":").Append(F(player.Pitch));
                sb.Append(",\"grounded\":").Append(player.IsGrounded ? "true" : "false");
                sb.Append(",\"respawns\":").Append(player.RespawnCount);
                sb.Append(",\"gliding\":").Append(player.IsGliding ? "true" : "false");
            }
            if (inv != null)
            {
                sb.Append(",\"inventory\":").Append(inv.Count).Append(",\"selected\":").Append(inv.SelectedIndex);
                sb.Append(",\"photos\":[");
                for (int i = 0; i < inv.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(Escape(inv.Photos[i].Label)).Append('"');
                }
                sb.Append(']');
            }
            if (holder != null)
                sb.Append(",\"raised\":").Append(holder.IsRaised ? "true" : "false").Append(",\"roll\":").Append(F(holder.RollDegrees));
            if (cam != null)
                sb.Append(",\"camera\":").Append(cam.Unlocked ? "true" : "false").Append(",\"film\":").Append(cam.Film);
            if (ps != null)
                sb.Append(",\"placements\":").Append(ps.PlacementCount).Append(",\"canRewind\":").Append(ps.CanRewind ? "true" : "false");
            var history = WorldHistory.Instance;
            if (history != null)
            {
                sb.Append(",\"history\":").Append(history.Depth).Append(",\"canUndo\":").Append(history.CanUndo ? "true" : "false");
                if (history.LastCheckpoint != null)
                    sb.Append(",\"checkpoint\":\"").Append(Escape(history.LastCheckpoint.Id)).Append('"');
            }
            var tracker = PlayerComponent<SafePoseTracker>();
            if (tracker != null)
                sb.Append(",\"falling\":").Append(tracker.IsFalling ? "true" : "false").Append(",\"limbo\":").Append(tracker.InLimbo ? "true" : "false");
            var channels = SwitchBoard.Snapshot();
            if (channels.Count > 0)
            {
                sb.Append(",\"switches\":{");
                bool first = true;
                foreach (var kv in channels)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(Escape(kv.Key)).Append("\":").Append(kv.Value ? "true" : "false");
                }
                sb.Append('}');
            }
            int renderers = 0;
            foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.enabled && r.gameObject.activeInHierarchy) renderers++;
            sb.Append(",\"renderers\":").Append(renderers);
            sb.Append(",\"tier\":\"").Append(Ion.Presentation.QualityTier.Name(Ion.Presentation.QualityTier.Current)).Append('"');
            sb.Append(",\"frameMs\":").Append(F(Ion.Presentation.Quality.AdaptiveQuality.SmoothedFrameMs));
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            sb.Append(",\"hdr\":").Append(urp != null && urp.supportsHDR ? "true" : "false");
            sb.Append(",\"cascades\":").Append(urp != null ? urp.shadowCascadeCount : 0);
            sb.Append(",\"localLights\":").Append(Ion.Presentation.Quality.UltraFx.ActiveLights);
            sb.Append('}');
            return sb.ToString();
        }

        static string F(float v) => v.ToString("0.###", Inv);

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
