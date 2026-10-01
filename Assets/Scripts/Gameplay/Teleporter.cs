using System;
using Ion.Projection;
using UnityEngine;

namespace Ion.Gameplay
{
    /// <summary>
    /// Trigger volume that invokes <see cref="OnEnter"/> when the player walks in.
    /// Uses the object's own trigger collider, or adds a box trigger of <see cref="TriggerSize"/>
    /// (bottom-centred on the transform). Physics trigger events plus a bounds check as a fallback;
    /// fires once per entry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Teleporter : MonoBehaviour
    {
        public Action OnEnter;

        public Vector3 TriggerSize = new Vector3(1.6f, 2.4f, 1.6f);
        public float Cooldown = 1f;

        Collider _trigger;
        bool _physicsOverlap;
        bool _inside;
        float _cooldownUntil;

        void Awake()
        {
            if (!TryGetComponent<Interactable>(out _)) gameObject.AddComponent<Interactable>();
        }

        void Start() => EnsureTrigger();

        void EnsureTrigger()
        {
            if (_trigger != null) return;
            var cols = GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i].isTrigger) { _trigger = cols[i]; return; }
            }

            var box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = TriggerSize;
            box.center = new Vector3(0f, TriggerSize.y * 0.5f, 0f);
            _trigger = box;
        }

        void OnDisable()
        {
            _physicsOverlap = false;
            _inside = false;
        }

        static bool IsPlayer(Collider other) =>
            other != null && other.GetComponentInParent<FirstPersonController>() != null;

        void OnTriggerEnter(Collider other)
        {
            if (IsPlayer(other)) _physicsOverlap = true;
        }

        void OnTriggerExit(Collider other)
        {
            if (IsPlayer(other)) _physicsOverlap = false;
        }

        void Update()
        {
            var player = FirstPersonController.Current;
            bool boundsInside = false;
            if (player != null && _trigger != null)
            {
                Vector3 c = player.transform.position + Vector3.up * 0.9f;
                boundsInside = _trigger.bounds.Contains(c);
            }
            if (player == null) _physicsOverlap = false;

            bool inside = boundsInside || _physicsOverlap;
            if (inside && !_inside)
            {
                _inside = true;
                Fire();
            }
            else if (!inside)
            {
                _inside = false;
            }
        }

        void Fire()
        {
            if (Time.time < _cooldownUntil) return;
            _cooldownUntil = Time.time + Cooldown;
            OnEnter?.Invoke();
        }
    }
}
