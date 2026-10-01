using System;
using Ion.Gameplay.State;
using Ion.Presentation.Motion;
using Ion.Projection;
using UnityEngine;

namespace Ion.Gameplay
{
    /// <summary>
    /// Something that follows a <see cref="SwitchBoard"/> channel. Applies the channel's current state
    /// instantly whenever it is enabled (so a photo copy, or an object a rewind brought back, matches the
    /// world), then animates on changes.
    /// </summary>
    public abstract class SwitchTarget : MonoBehaviour
    {
        public string Channel;
        /// <summary>Active when the channel is OFF instead of on.</summary>
        public bool Invert;

        /// <summary>The channel state as this target reads it (after <see cref="Invert"/>).</summary>
        public bool Active => SwitchBoard.Get(Channel) != Invert;

        protected virtual void OnEnable()
        {
            SwitchBoard.ChannelChanged += OnChannelChanged;
            Apply(Active, true);
        }

        protected virtual void OnDisable()
        {
            SwitchBoard.ChannelChanged -= OnChannelChanged;
        }

        void OnChannelChanged(string channel, bool on, bool instant)
        {
            if (channel != Channel) return;
            Apply(on != Invert, instant);
        }

        /// <summary>Show / become <paramref name="active"/>. <paramref name="instant"/>: no animation (restores).</summary>
        protected abstract void Apply(bool active, bool instant);
    }

    /// <summary>
    /// Moves between two local positions with its channel: sliding bridges, gates sinking into a floor slot,
    /// the ending hatch. 1.6 s per ≤ 4 m (easeInOutCubic + a 0.06 s settle); 0.35 s while a rewind undoes the
    /// switch; instant on checkpoint restores. It never moves into the player: it waits until the way is clear.
    /// An <see cref="Interactable"/> (never sliced; a photo copy follows its channel).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Mover : SwitchTarget
    {
        public Vector3 OffLocal, OnLocal;
        /// <summary>Seconds per ≤ 4 m of travel (longer travels scale up).</summary>
        public float Duration = Feel.MoverSecondsPer4m;

        public bool IsMoving => _t >= 0f;
        /// <summary>True while the move is paused because it would enter the player's capsule.</summary>
        public bool IsBlocked { get; private set; }

        /// <summary>Raised when a move starts (true = toward OnLocal) and when it ends.</summary>
        public event Action<bool> MoveStarted, MoveFinished;

        Vector3 _from, _to;
        float _t = -1f, _seconds, _settle = -1f;
        bool _towardOn;
        Collider[] _colliders;

        void Awake()
        {
            if (!TryGetComponent<Interactable>(out _)) gameObject.AddComponent<Interactable>();
        }

        protected override void Apply(bool active, bool instant)
        {
            if (OffLocal == OnLocal) return; // not configured yet (fields are set after AddComponent)
            Vector3 target = active ? OnLocal : OffLocal;
            _towardOn = active;
            if (instant || !isActiveAndEnabled)
            {
                _t = -1f;
                _settle = -1f;
                IsBlocked = false;
                transform.localPosition = target;
                return;
            }
            if ((transform.localPosition - target).sqrMagnitude < 1e-8f && _t < 0f) return;
            _from = transform.localPosition;
            _to = target;
            float dist = (transform.parent != null ? transform.parent.TransformVector(_to - _from) : _to - _from).magnitude;
            _seconds = WorldHistory.IsUndoing ? Feel.MoverRewindSeconds : Duration * Mathf.Max(1f, dist / 4f);
            _t = 0f;
            _settle = -1f;
            try { MoveStarted?.Invoke(active); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_t >= 0f)
            {
                float next = Mathf.Min(1f, _t + dt / Mathf.Max(0.01f, _seconds));
                Vector3 nextPos = Vector3.LerpUnclamped(_from, _to, Ease.InOutCubic(next));
                if (WouldHitPlayer(nextPos))
                {
                    IsBlocked = true;
                    return;
                }
                IsBlocked = false;
                _t = next;
                transform.localPosition = nextPos;
                if (_t >= 1f)
                {
                    _t = -1f;
                    _settle = 0f;
                }
            }
            if (_settle >= 0f)
            {
                // A last tiny give (4 mm past the stop and back) so heavy things land instead of halting.
                _settle += dt;
                float k = Mathf.Clamp01(_settle / Feel.MoverSettleSeconds);
                Vector3 dir = (_to - _from).normalized;
                transform.localPosition = _to + dir * (0.004f * Mathf.Sin(k * Mathf.PI));
                if (k >= 1f)
                {
                    _settle = -1f;
                    transform.localPosition = _to;
                    try { MoveFinished?.Invoke(_towardOn); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }

        /// <summary>Would this mover's colliders overlap the player's capsule at <paramref name="localPos"/>?</summary>
        bool WouldHitPlayer(Vector3 localPos)
        {
            var player = FirstPersonController.Current;
            if (player == null || player.Controller == null) return false;
            if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
            Vector3 world = transform.parent != null ? transform.parent.TransformPoint(localPos) : localPos;
            Vector3 delta = world - transform.position;
            if (delta.sqrMagnitude < 1e-10f) return false;

            Bounds pb = player.Controller.bounds;
            pb.Expand(-0.08f);
            for (int i = 0; i < _colliders.Length; i++)
            {
                Collider c = _colliders[i];
                if (c == null || !c.enabled || c.isTrigger) continue;
                Bounds b = c.bounds;
                b.center += delta;
                if (!b.Intersects(pb)) continue;
                // Already overlapping before the step (player walked into it): do not lock up forever.
                Bounds now = c.bounds;
                if (now.Intersects(pb)) continue;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Powers what sits on the same object (and its children) with its channel: Teleporters, Switches and
    /// anything listening to <see cref="PowerChanged"/> (exhibit stands).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PowerTarget : SwitchTarget
    {
        public bool Powered { get; private set; }
        public event Action<bool> PowerChanged;

        protected override void Apply(bool active, bool instant)
        {
            Powered = active;
            foreach (var t in GetComponentsInChildren<Teleporter>(true)) t.Powered = active;
            foreach (var s in GetComponentsInChildren<Switch>(true)) s.SetPowered(active, !instant);
            try { PowerChanged?.Invoke(active); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
