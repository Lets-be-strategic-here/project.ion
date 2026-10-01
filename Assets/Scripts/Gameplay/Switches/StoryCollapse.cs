using System;
using Ion.Gameplay.State;
using Ion.Presentation.Motion;
using Ion.Projection;
using UnityEngine;

namespace Ion.Gameplay
{
    /// <summary>
    /// A slab that gives way under the player (art bible §5, §7): it looks like the deck it continues,
    /// wobbles for <see cref="Delay"/> seconds after the player steps on it (2° at 9 Hz, decaying), then drops
    /// at 20 m/s² and fades out below the zone. A one-way story event: never in the rewind history or in a
    /// checkpoint snapshot, so the tutorial beat cannot loop. An <see cref="Interactable"/> (never sliced).
    /// Detection is a distance poll against its own bounds (no physics callbacks).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StoryCollapse : MonoBehaviour
    {
        public float Delay = Feel.CollapseWarnSeconds;
        /// <summary>How far below its start it falls before it is switched off.</summary>
        public float DropDistance = 40f;

        public bool Fired { get; private set; }
        /// <summary>True once the slab has started to drop (its colliders are off).</summary>
        public bool Dropped { get; private set; }

        /// <summary>Raised when the slab starts to drop.</summary>
        public event Action Collapsed;

        /// <summary>Raised when the warning wobble starts (the crack before the fall).</summary>
        public event Action Warned;

        Vector3 _restPos;
        Quaternion _restRot;
        float _t = -1f;
        float _fallSpeed, _fallen;
        Collider[] _colliders;

        void Awake()
        {
            if (!TryGetComponent<Interactable>(out _)) gameObject.AddComponent<Interactable>();
            _restPos = transform.localPosition;
            _restRot = transform.localRotation;
        }

        /// <summary>Starts the warning wobble now (scripted beats, tests).</summary>
        public void Trigger()
        {
            if (Fired) return;
            Fired = true;
            _t = 0f;
            try { Warned?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void Update()
        {
            if (!Fired)
            {
                if (PlayerStandsOnTop()) Trigger();
                return;
            }

            float dt = Time.deltaTime;
            if (!Dropped)
            {
                _t += dt;
                float decay = Mathf.Exp(-_t * 3f);
                float wobble = Feel.CollapseWobbleDeg * decay * Mathf.Sin(_t * Feel.CollapseWobbleHz * 2f * Mathf.PI);
                transform.localRotation = _restRot * Quaternion.Euler(wobble, 0f, wobble * 0.6f);
                if (_t >= Delay) Drop();
                return;
            }

            _fallSpeed += Feel.CollapseGravity * dt;
            float step = _fallSpeed * dt;
            _fallen += step;
            transform.position += Vector3.down * step;
            transform.localRotation *= Quaternion.Euler(dt * 8f, 0f, dt * 5f);
            if (_fallen >= DropDistance) gameObject.SetActive(false);
        }

        void Drop()
        {
            Dropped = true;
            _fallSpeed = 0f;
            _fallen = 0f;
            // The player must fall freely, not ride it down.
            if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null) _colliders[i].enabled = false;
            NudgePlayerOffFaces();
            try { Collapsed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// A player falling with the slab must not scrape the camera down the island face (a flat wall filling the
        /// screen read as a camera bug): push them ~0.6 m clear of the nearest faces and ease the view 10° down.
        /// </summary>
        void NudgePlayerOffFaces()
        {
            var player = FirstPersonController.Current;
            if (player == null) return;
            Vector3 chest = player.transform.position + Vector3.up * 0.9f;
            Vector3 away = Vector3.zero;
            const float probe = 3f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int h = 0; h < 2; h++)
                {
                    Vector3 from = chest + Vector3.down * (h * 1.2f);
                    if (Physics.Raycast(from, dir, out RaycastHit hit, probe, ~(1 << 8), QueryTriggerInteraction.Ignore))
                        away -= dir * (1f - hit.distance / probe);
                }
            }
            away.y = 0f;
            // Nothing close: drift along the gap's open axis instead (away from where the player was heading, so
            // they fall clear of the face ahead).
            if (away.sqrMagnitude < 1e-4f)
            {
                Vector3 v = player.Velocity;
                v.y = 0f;
                away = v.sqrMagnitude > 1e-4f ? -v : Vector3.zero;
            }
            if (away.sqrMagnitude < 1e-4f) { player.Nudge(Vector3.zero, 0.5f, 10f); return; }
            // ~0.6 m over 0.5 s with an ease-off: v0 = d / (s * 2/3).
            const float seconds = 0.5f, distance = 0.6f;
            player.Nudge(away.normalized * (distance / (seconds * 0.667f)), seconds, 10f);
        }

        bool PlayerStandsOnTop()
        {
            var player = FirstPersonController.Current;
            if (player == null || !player.IsGrounded) return false;
            if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
            Vector3 feet = player.transform.position;
            float r = PlayerFactory.Radius * 0.5f;
            for (int i = 0; i < _colliders.Length; i++)
            {
                Collider c = _colliders[i];
                if (c == null || !c.enabled || c.isTrigger) continue;
                Bounds b = c.bounds;
                if (feet.x < b.min.x - r || feet.x > b.max.x + r || feet.z < b.min.z - r || feet.z > b.max.z + r) continue;
                if (Mathf.Abs(feet.y - b.max.y) <= 0.2f) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// A checkpoint trigger (art bible §5, §11): when the player's feet enter the box (<see cref="Size"/>,
    /// bottom-centred on the transform), the checkpoint is set if this marker is newer than the current one.
    /// The brass inlay is built by PropKit; <see cref="Dot"/> (its 0.125 Ion dot) pulses once when set.
    /// Neither Interactable nor Sliceable. Distance test, no physics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CheckpointMarker : MonoBehaviour
    {
        public string Id;
        public Vector3 Size = new Vector3(2f, 2.5f, 2f);
        /// <summary>Optional: the Ion dot that pulses once when this marker sets the checkpoint.</summary>
        public Transform Dot;

        /// <summary>Raised when this marker set the checkpoint.</summary>
        public event Action Reached;

        bool _inside;
        float _pulse = -1f;
        Vector3 _dotScale;

        void Awake()
        {
            if (Dot != null) _dotScale = Dot.localScale;
        }

        void Update()
        {
            var player = FirstPersonController.Current;
            if (player != null)
            {
                Vector3 local = transform.InverseTransformPoint(player.transform.position);
                bool inside = Mathf.Abs(local.x) <= Size.x * 0.5f && Mathf.Abs(local.z) <= Size.z * 0.5f &&
                              local.y >= -0.5f && local.y <= Size.y;
                if (inside && !_inside && player.IsGrounded)
                {
                    var history = WorldHistory.Instance;
                    if (history != null && history.TrySetMarkerCheckpoint(Id, PlayerPose.Of(player)))
                    {
                        _pulse = 0f;
                        try { Reached?.Invoke(); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
                }
                _inside = inside && (player.IsGrounded || _inside);
            }

            if (_pulse >= 0f && Dot != null)
            {
                _pulse += Time.deltaTime;
                float k = Mathf.Clamp01(_pulse / Feel.CheckpointPulseSeconds);
                float s = 1f + 0.6f * Ease.SineBump(k);
                Dot.localScale = _dotScale * s;
                if (k >= 1f) { _pulse = -1f; Dot.localScale = _dotScale; }
            }
        }
    }
}
