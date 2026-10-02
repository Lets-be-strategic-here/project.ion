using System;
using System.Collections.Generic;
using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels;
using Ion.Levels.Props;
using Ion.Presentation.Motion;
using Ion.Projection;
using UnityEngine;

namespace Ion.Presentation.Audio
{
    /// <summary>
    /// Gameplay -> audio wiring. This is the only IonAudio file that knows gameplay types, so contract changes
    /// land here. Read-only: nothing in gameplay is modified.
    ///
    /// Subscribed (gameplay code must NOT also call Play for these; if it does, the retrigger guard drops the
    /// duplicate):
    /// - ProjectionSystem.Placed                        -> photo_place + duck
    /// - WorldHistory.Rewound(result)                   -> photo_rewind + tape-dip (Undid without a glide, RecoveredFall),
    ///                                                     rewind_nothing (Nothing), rewind_checkpoint (ToCheckpoint)
    /// - RewindController glide (polled) / GlideEnded   -> rewind_tape_loop, its pitch and level following the
    ///                                                     glide's speed, music tape-dip; rewind_settle as it lands
    /// - WorldHistory.FallRecovered                     -> photo_rewind (also covers the limbo auto-recover)
    /// - WorldHistory.CheckpointReached                 -> checkpoint_set (after the teleport sound settles)
    /// - SafePoseTracker.FallingChanged / LimboChanged  -> fall_whoosh, limbo drone + music muffle
    /// - SwitchBoard.ChannelChanged (animated)          -> switch_press / switch_release at the nearest switch
    /// - Switch.PoweredChanged(true)                    -> device_wake (the T1 button waking up)
    /// - Mover.MoveStarted / MoveFinished               -> mover_loop following the mover, mover_stop
    /// - StoryCollapse.Fired (polled) / Collapsed       -> collapse_crack at the wobble, collapse_fall at the drop
    /// - ExhibitStand.Changed(Available|Solved)         -> exhibit_wake
    /// - InstantCamera.Captured, IsRaised (polled)      -> camera_shutter, camera raise / lower
    /// - PhotoHolder.RaisedChanged, RollDegrees (polled)-> photo_raise / photo_lower, photo_rotate
    /// - PhotoInventory.Changed (count up)              -> photo_pickup (not for captures, rewinds, restores)
    /// - CameraPickup.Taken (polled)                    -> photo_pickup (lower)
    /// - Teleporter.FireCount (polled)                  -> teleport_travel + duck at the fade start
    /// - FirstPersonController.Teleported               -> teleport_travel for moves without a teleporter
    /// - GameBootstrap.Restarted                        -> stop loops, quiet for a second
    /// Called directly by gameplay (no event exists): Sfx.SwitchDenied (unpowered press), Sfx.CameraEmpty
    /// (shutter with no film), Sfx.HatchRise (ending pod), Sfx.UiHover / UiClick (UI).
    /// </summary>
    public sealed partial class IonAudio
    {
        const float DiscoverInterval = 4f;

        FirstPersonController _fpc;
        PhotoInventory _inventory;
        PhotoHolder _holder;
        InstantCamera _camera;
        SafePoseTracker _tracker;
        ProjectionSystem _projection;
        WorldHistory _history;

        int _lastCount;
        float _lastRoll;
        bool _holderRaised, _cameraRaised, _falling, _inLimbo;

        float _placedTime = -10f, _rewoundTime = -10f, _capturedTime = -10f, _teleporterFireTime = -10f,
              _teleportTime = -10f, _quietUntil;
        float _lowerAt = -1f, _pickupAt = -1f, _pendingChimeAt = -1f;
        float _nextDiscover;
        bool _discoverSoon = true;

        readonly List<Teleporter> _teleporters = new List<Teleporter>();
        readonly Dictionary<Teleporter, int> _fireCounts = new Dictionary<Teleporter, int>();
        readonly List<Switch> _switches = new List<Switch>();
        readonly HashSet<Switch> _switchSubs = new HashSet<Switch>();
        readonly HashSet<Mover> _moverSubs = new HashSet<Mover>();
        readonly Dictionary<Mover, LoopHandle> _moverLoops = new Dictionary<Mover, LoopHandle>();
        readonly Dictionary<StoryCollapse, bool> _collapses = new Dictionary<StoryCollapse, bool>();
        readonly HashSet<ExhibitStand> _exhibitSubs = new HashSet<ExhibitStand>();
        readonly Dictionary<CameraPickup, bool> _cameraPickups = new Dictionary<CameraPickup, bool>();
        readonly List<Mover> _moverScratch = new List<Mover>();

        LoopHandle _tapeLoop;
        bool _wasGliding;

        /// <summary>Event sounds stay quiet while the level builds, during a restart and right after it.</summary>
        bool Quiet => InStartupGrace || GameBootstrap.Restarting || Time.unscaledTime < _quietUntil;

        void AwakeBindings()
        {
            RewindController.GlideEnded += OnGlideEnded;
            SwitchBoard.ChannelChanged += OnChannelChanged;
            GameBootstrap.Restarted += OnRestarted;
        }

        void DestroyBindings()
        {
            RewindController.GlideEnded -= OnGlideEnded;
            SwitchBoard.ChannelChanged -= OnChannelChanged;
            GameBootstrap.Restarted -= OnRestarted;
            UnbindPlayer();
            UnbindProjection();
            UnbindHistory();
        }

        // ------------------------------------------------------------------ binding

        void UpdateBindings()
        {
            var fpc = FirstPersonController.Current;
            if (!ReferenceEquals(fpc, _fpc)) BindPlayer(fpc);
            if (fpc == null) return;

            // Both getters create their system on demand; only touch them once the player exists.
            var ps = ProjectionSystem.Instance;
            if (!ReferenceEquals(ps, _projection)) BindProjection(ps);
            var history = WorldHistory.Instance;
            if (!ReferenceEquals(history, _history)) BindHistory(history);
            if (_tracker == null) BindTracker(fpc.GetComponent<SafePoseTracker>());

            float now = Time.unscaledTime;
            if (_discoverSoon || now >= _nextDiscover) Discover();

            PollPlayerState();
            PollDevices();
            PollRewindGlide();

            if (_pendingChimeAt >= 0f && now >= _pendingChimeAt)
            {
                _pendingChimeAt = -1f;
                if (!Quiet) Play(Sfx.CheckpointSet);
            }
        }

        void LateUpdateBindings()
        {
            // A new photo that is neither our own snapshot nor a restored one (decided a little later because
            // the inventory can change before the Captured / Rewound event of the same action).
            if (_pickupAt >= 0f && Time.unscaledTime >= _pickupAt)
            {
                float t = _pickupAt;
                _pickupAt = -1f;
                bool own = Mathf.Abs(t - _capturedTime) < 0.5f || Mathf.Abs(t - _rewoundTime) < 0.7f;
                if (!own && !Quiet) Play(Sfx.PhotoPickup);
            }

            // A lower that was neither a placement nor a rewind gets its own (softer) sound.
            if (_lowerAt >= 0f && Time.unscaledTime >= _lowerAt)
            {
                float t = _lowerAt;
                _lowerAt = -1f;
                bool consumed = t - _placedTime < 0.9f || t - _rewoundTime < 0.9f;
                if (!consumed) Play(Sfx.PhotoLower);
            }
        }

        void BindPlayer(FirstPersonController fpc)
        {
            UnbindPlayer();
            _fpc = fpc;
            if (fpc == null) return;
            _inventory = fpc.GetComponent<PhotoInventory>();
            _holder = fpc.GetComponent<PhotoHolder>();
            _camera = fpc.GetComponent<InstantCamera>();
            if (_inventory != null)
            {
                _inventory.Changed += OnInventoryChanged;
                _lastCount = _inventory.Count;
            }
            if (_holder != null)
            {
                _holder.RaisedChanged += OnRaisedChanged;
                _holderRaised = _holder.IsRaised;
                _lastRoll = _holder.RollDegrees;
            }
            if (_camera != null)
            {
                _camera.Captured += OnCaptured;
                _cameraRaised = _camera.IsRaised;
            }
            fpc.Teleported += OnTeleported;
            BindTracker(fpc.GetComponent<SafePoseTracker>());
            _discoverSoon = true;
        }

        void UnbindPlayer()
        {
            if (!ReferenceEquals(_inventory, null)) _inventory.Changed -= OnInventoryChanged;
            if (!ReferenceEquals(_holder, null)) _holder.RaisedChanged -= OnRaisedChanged;
            if (!ReferenceEquals(_camera, null)) _camera.Captured -= OnCaptured;
            if (!ReferenceEquals(_fpc, null)) _fpc.Teleported -= OnTeleported;
            BindTracker(null);
            _inventory = null;
            _holder = null;
            _camera = null;
            _fpc = null;
        }

        void BindTracker(SafePoseTracker tracker)
        {
            if (ReferenceEquals(tracker, _tracker)) return;
            if (!ReferenceEquals(_tracker, null))
            {
                _tracker.FallingChanged -= OnFallingChanged;
                _tracker.LimboChanged -= OnLimboChanged;
            }
            _tracker = tracker;
            _falling = false;
            SetLimboState(false);
            if (tracker == null) return;
            tracker.FallingChanged += OnFallingChanged;
            tracker.LimboChanged += OnLimboChanged;
            _falling = tracker.IsFalling;
            SetLimboState(tracker.InLimbo);
        }

        void BindProjection(ProjectionSystem ps)
        {
            UnbindProjection();
            _projection = ps;
            if (ps != null) ps.Placed += OnPlaced;
        }

        void UnbindProjection()
        {
            if (!ReferenceEquals(_projection, null)) _projection.Placed -= OnPlaced;
            _projection = null;
        }

        void BindHistory(WorldHistory h)
        {
            UnbindHistory();
            _history = h;
            if (h == null) return;
            h.Rewound += OnRewound;
            h.FallRecovered += OnFallRecovered;
            h.CheckpointReached += OnCheckpointReached;
        }

        void UnbindHistory()
        {
            if (!ReferenceEquals(_history, null))
            {
                _history.Rewound -= OnRewound;
                _history.FallRecovered -= OnFallRecovered;
                _history.CheckpointReached -= OnCheckpointReached;
            }
            _history = null;
        }

        // ------------------------------------------------------------------ discovery of devices

        void Discover()
        {
            _discoverSoon = false;
            _nextDiscover = Time.unscaledTime + DiscoverInterval;

            _teleporters.Clear();
            foreach (var t in FindObjectsByType<Teleporter>(FindObjectsSortMode.None))
            {
                _teleporters.Add(t);
                if (!_fireCounts.ContainsKey(t)) _fireCounts[t] = t.FireCount;
            }

            _switches.Clear();
            foreach (var s in FindObjectsByType<Switch>(FindObjectsSortMode.None))
            {
                _switches.Add(s);
                if (_switchSubs.Add(s))
                {
                    Switch captured = s;
                    s.PoweredChanged += on => OnSwitchPowered(captured, on);
                }
            }

            foreach (var m in FindObjectsByType<Mover>(FindObjectsSortMode.None))
            {
                if (!_moverSubs.Add(m)) continue;
                Mover captured = m;
                m.MoveStarted += on => OnMoverStarted(captured);
                m.MoveFinished += on => OnMoverFinished(captured);
            }

            foreach (var c in FindObjectsByType<StoryCollapse>(FindObjectsSortMode.None))
            {
                if (_collapses.ContainsKey(c)) continue;
                _collapses[c] = c.Fired;
                StoryCollapse captured = c;
                c.Collapsed += () => OnCollapsed(captured);
            }

            foreach (var e in FindObjectsByType<ExhibitStand>(FindObjectsSortMode.None))
            {
                if (!_exhibitSubs.Add(e)) continue;
                ExhibitStand captured = e;
                e.Changed += state => OnExhibitChanged(captured, state);
            }

            foreach (var p in FindObjectsByType<CameraPickup>(FindObjectsSortMode.None))
                if (!_cameraPickups.ContainsKey(p)) _cameraPickups[p] = p.Taken;

            Prune();
        }

        void Prune()
        {
            _switchSubs.RemoveWhere(s => s == null);
            _moverSubs.RemoveWhere(m => m == null);
            _exhibitSubs.RemoveWhere(e => e == null);
            RemoveDead(_fireCounts);
            RemoveDead(_collapses);
            RemoveDead(_cameraPickups);
        }

        static readonly List<UnityEngine.Object> s_Dead = new List<UnityEngine.Object>();

        static void RemoveDead<TKey, TValue>(Dictionary<TKey, TValue> map) where TKey : UnityEngine.Object
        {
            s_Dead.Clear();
            foreach (var kv in map) if (kv.Key == null) s_Dead.Add(kv.Key);
            for (int i = 0; i < s_Dead.Count; i++) map.Remove((TKey)s_Dead[i]);
            s_Dead.Clear();
        }

        // ------------------------------------------------------------------ polling

        void PollPlayerState()
        {
            if (_holder != null)
            {
                // Continuous rotation: a soft dial detent every 15° crossed, a firmer one on each 90° landing.
                float roll = _holder.RollDegrees;
                if (_holder.IsRaised && _holderRaised)
                {
                    float d = Mathf.DeltaAngle(_lastRoll, roll);
                    if (Mathf.Abs(d) > 0.001f && Mathf.Abs(d) < 45f)
                    {
                        float nearest = Mathf.Round(roll / 90f) * 90f;
                        bool landed = Mathf.Abs(Mathf.DeltaAngle(roll, nearest)) < 0.02f
                                      && Mathf.Abs(Mathf.DeltaAngle(_lastRoll, nearest)) >= 0.02f;
                        float from = _lastRoll, to = _lastRoll + d;
                        bool detent = Mathf.FloorToInt(from / 15f) != Mathf.FloorToInt(to / 15f);
                        if (landed && !_holder.IsRotatingFast) Play(Sfx.PhotoRotate, null, 0.8f, 1f);
                        else if (detent) Play(Sfx.PhotoRotate, null, 0.3f, 1.15f);
                    }
                }
                _lastRoll = roll;
                _holderRaised = _holder.IsRaised;
            }
            if (_camera != null)
            {
                bool raised = _camera.IsRaised;
                if (raised != _cameraRaised)
                {
                    if (raised) Play(Sfx.CameraRaise);
                    else if (Time.unscaledTime - _capturedTime > 0.6f) Play(Sfx.PhotoLower, null, 0.8f, 0.9f);
                    _cameraRaised = raised;
                }
            }
        }

        void PollDevices()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _teleporters.Count; i++)
            {
                Teleporter t = _teleporters[i];
                if (t == null) continue;
                int fc = t.FireCount;
                if (_fireCounts.TryGetValue(t, out int known) && fc > known && !Quiet)
                {
                    _teleporterFireTime = now;
                    Play(Sfx.TeleportTravel);
                    Duck(DuckDb, Feel.TeleportSeconds);
                }
                _fireCounts[t] = fc;
            }

            // Collapses: the crack at the start of the warning wobble (Fired), the fall on Collapsed.
            // Fired can go back to false (a restart re-arms the slab), so the known state follows it.
            if (_collapses.Count > 0)
            {
                StoryCollapse changed = null;
                foreach (var kv in _collapses)
                    if (kv.Key != null && kv.Key.Fired != kv.Value) { changed = kv.Key; break; }
                if (changed != null)
                {
                    bool fired = changed.Fired;
                    _collapses[changed] = fired;
                    if (fired && !Quiet) Play(Sfx.CollapseCrack, changed.transform.position);
                }
            }

            if (_cameraPickups.Count > 0)
            {
                CameraPickup taken = null;
                bool takenState = false;
                foreach (var kv in _cameraPickups)
                    if (kv.Key != null && kv.Key.Taken != kv.Value) { taken = kv.Key; takenState = kv.Key.Taken; break; }
                if (taken != null)
                {
                    _cameraPickups[taken] = takenState;
                    if (takenState && !Quiet && now - _rewoundTime > 0.7f) Play(Sfx.PhotoPickup, null, 1f, 0.84f);
                }
            }

            // Movers whose loop outlived them (destroyed mid-move).
            if (_moverLoops.Count > 0)
            {
                _moverScratch.Clear();
                foreach (var kv in _moverLoops) if (kv.Key == null) _moverScratch.Add(kv.Key);
                for (int i = 0; i < _moverScratch.Count; i++)
                {
                    LoopHandle h = _moverLoops[_moverScratch[i]];
                    StopLoop(ref h, 0.1f);
                    _moverLoops.Remove(_moverScratch[i]);
                }
            }
        }

        // ------------------------------------------------------------------ handlers

        void OnPlaced()
        {
            _placedTime = Time.unscaledTime;
            Play(Sfx.PhotoPlace);
            Duck();
            _discoverSoon = true; // pasted devices (teleporters, switches, movers) need binding
        }

        /// <summary>
        /// The rewind glide's tape: a loop whose playback rate (pitch and chatter together) and level follow the
        /// glide's speed, slow -> fast -> settling, so the sound rides the same curve as the body and the screen.
        /// Back-to-back glides (a buffered press) keep the same tape running.
        /// </summary>
        void PollRewindGlide()
        {
            var rc = RewindController.Instance;
            bool gliding = rc != null && rc.IsGliding;
            if (gliding && !_wasGliding)
            {
                _rewoundTime = Time.unscaledTime;
                if (!Quiet && !_tapeLoop.IsValid)
                {
                    _tapeLoop = StartLoop(Sfx.RewindTapeLoop, null, null, 0f, 0.04f);
                    TapeDip(Feel.RewindMusicDip, rc.GlideSeconds);
                    Duck(DuckDb, rc.GlideSeconds);
                }
            }
            if (gliding && _tapeLoop.IsValid)
            {
                SetLoopPitch(_tapeLoop, Mathf.Lerp(Feel.RewindTapePitchMin, Feel.RewindTapePitchMax, rc.GlideSpeed01));
                SetLoopVolume(_tapeLoop, Feel.RewindTapeVolume * rc.GlideFx);
            }
            if (!gliding && _tapeLoop.IsValid) StopLoop(ref _tapeLoop, 0.08f);
            _wasGliding = gliding;
        }

        void OnGlideEnded()
        {
            _rewoundTime = Time.unscaledTime;
            if (!Quiet) Play(Sfx.RewindSettle);
        }

        void OnRewound(RewindResult result)
        {
            _rewoundTime = Time.unscaledTime;
            _discoverSoon = true;
            var rewind = RewindController.Instance;
            if (result == RewindResult.Undid && rewind != null && rewind.IsGliding)
                return; // the glide's tape and settle carry it (PollRewindGlide, OnGlideEnded)
            switch (result)
            {
                case RewindResult.Undid:
                case RewindResult.RecoveredFall:
                    Play(Sfx.PhotoRewind);
                    TapeDip(0.92f, Feel.RewindSeconds);
                    Duck();
                    if (result == RewindResult.RecoveredFall) SetLimboState(false);
                    break;
                case RewindResult.Nothing:
                    Play(Sfx.RewindNothing);
                    break;
                case RewindResult.ToCheckpoint:
                    Play(Sfx.RewindCheckpoint);
                    TapeDip(0.85f, Feel.CheckpointSeconds);
                    Duck(DuckDb, Feel.CheckpointSeconds);
                    SetLimboState(false);
                    break;
            }
        }

        void OnFallRecovered()
        {
            // RewindOnce raises this just before Rewound(RecoveredFall); the retrigger guard merges the two.
            // On its own (the limbo auto-recover), it still sounds like a rewind.
            _rewoundTime = Time.unscaledTime;
            Play(Sfx.PhotoRewind);
            TapeDip(0.92f, Feel.RewindSeconds);
            Duck();
            SetLimboState(false);
        }

        void OnCheckpointReached(Checkpoint cp)
        {
            if (Quiet) return;
            float now = Time.unscaledTime;
            if (now - _rewoundTime < 1f) return;
            // Zone entries come right after a teleport: let the arrival sound breathe first.
            if (now - _teleportTime < 1.5f || now - _teleporterFireTime < 1.5f)
                _pendingChimeAt = Mathf.Max(_teleportTime, _teleporterFireTime) + 1.3f;
            else
                Play(Sfx.CheckpointSet);
        }

        void OnCaptured(PhotoData photo)
        {
            _capturedTime = Time.unscaledTime;
            Play(Sfx.CameraShutter);
        }

        void OnRaisedChanged()
        {
            if (_holder == null) return;
            if (_holder.IsRaised)
            {
                Play(Sfx.PhotoRaise);
                _lowerAt = -1f;
            }
            else
            {
                // Decided a little later: a placement or rewind that lowers the photo has its own sound.
                _lowerAt = Time.unscaledTime + 0.05f;
            }
            _holderRaised = _holder.IsRaised;
        }

        void OnInventoryChanged()
        {
            if (_inventory == null) return;
            int count = _inventory.Count;
            float now = Time.unscaledTime;
            bool up = count > _lastCount;
            _lastCount = count;
            if (!up || Quiet) return;
            _pickupAt = now + 0.05f; // decided in LateUpdateBindings
        }

        void OnFallingChanged(bool falling)
        {
            _falling = falling;
            if (falling && !_inLimbo && !Quiet) Play(Sfx.FallWhoosh);
        }

        void OnLimboChanged(bool inLimbo) => SetLimboState(inLimbo);

        void SetLimboState(bool on)
        {
            _inLimbo = on;
            SetLimbo(on);
        }

        void OnTeleported(Vector3 from, Vector3 to)
        {
            float now = Time.unscaledTime;
            _teleportTime = now;
            _discoverSoon = true;
            CheckAreaSoon();
            // A teleporter already played its sound at the fade start; rewinds move the player silently.
            if (Quiet || now - _teleporterFireTime < 1.5f || now - _rewoundTime < 1f) return;
            Play(Sfx.TeleportTravel);
            Duck(DuckDb, Feel.TeleportSeconds);
        }

        void OnChannelChanged(string channel, bool on, bool instant)
        {
            if (instant || Quiet || string.IsNullOrEmpty(channel)) return;
            _discoverSoon = true;
            Switch nearest = NearestSwitch(channel);
            if (nearest == null) return;
            // An undo pops the button back a little more softly than a press.
            float vol = WorldHistory.IsUndoing || Time.unscaledTime - _rewoundTime < 0.1f ? 0.7f : 1f;
            Play(on ? Sfx.SwitchPress : Sfx.SwitchRelease, nearest.FocusPoint, vol, 1f);
        }

        Switch NearestSwitch(string channel)
        {
            Vector3 p = _fpc != null ? _fpc.transform.position : Vector3.zero;
            Switch best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _switches.Count; i++)
            {
                Switch s = _switches[i];
                if (s == null || !s.isActiveAndEnabled || s.Channel != channel) continue;
                float d = (s.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best == null)
            {
                // Not discovered yet (just pasted?): one direct search.
                foreach (var s in FindObjectsByType<Switch>(FindObjectsSortMode.None))
                {
                    if (s.Channel != channel) continue;
                    float d = (s.transform.position - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = s; }
                }
            }
            return best;
        }

        void OnSwitchPowered(Switch s, bool powered)
        {
            if (s == null || !powered || Quiet) return;
            Play(Sfx.DeviceWake, s.FocusPoint);
        }

        void OnMoverStarted(Mover m)
        {
            if (m == null || Quiet) return;
            if (_moverLoops.TryGetValue(m, out LoopHandle existing) && IsLoopActive(existing)) return;
            if (_moverLoops.Count >= 3) return;
            float vol = WorldHistory.IsUndoing ? 0.6f : 1f;
            _moverLoops[m] = StartLoop(Sfx.MoverLoop, m.transform, null, vol, 0.12f);
        }

        void OnMoverFinished(Mover m)
        {
            if (m == null) return;
            if (_moverLoops.TryGetValue(m, out LoopHandle h))
            {
                StopLoop(ref h, 0.08f);
                _moverLoops.Remove(m);
                if (!Quiet) Play(Sfx.MoverStop, m.transform.position, WorldHistory.IsUndoing ? 0.6f : 1f, 1f);
            }
        }

        void OnCollapsed(StoryCollapse c)
        {
            if (c == null || Quiet) return;
            Play(Sfx.CollapseFall, c.transform.position);
        }

        void OnExhibitChanged(ExhibitStand e, ExhibitState state)
        {
            if (e == null || Quiet) return;
            if (state == ExhibitState.Available || state == ExhibitState.Solved)
                Play(Sfx.ExhibitWake, e.transform.position + Vector3.up * 2f);
        }

        void OnRestarted()
        {
            _quietUntil = Time.unscaledTime + 1f;
            foreach (var kv in _moverLoops)
            {
                LoopHandle h = kv.Value;
                StopLoop(ref h, 0.1f);
            }
            _moverLoops.Clear();
            StopAmbienceForRestart();
            SetLimboState(false);
            _falling = false;
            _pendingChimeAt = -1f;
            _lowerAt = -1f;
            _pickupAt = -1f;
            _discoverSoon = true;
            CheckAreaSoon();
        }
    }
}
