using System;
using System.Collections.Generic;
using Ion.Projection;
using UnityEngine;

namespace Ion.Gameplay
{
    /// <summary>
    /// Trigger volume that invokes <see cref="OnEnter"/> when the player walks in.
    /// Uses the object's own trigger collider, or adds a box trigger of <see cref="TriggerSize"/>
    /// (bottom-centred on the transform). Physics trigger events plus a bounds check as a fallback;
    /// fires once per entry. The move itself happens at the peak of a short fade-to-white
    /// (<see cref="Ion.Presentation.ScreenFx.Transition"/>).
    ///
    /// A teleporter is an Interactable, so a photo of it carries a whole copy (a clone of the object). A
    /// delegate does not survive cloning, so <see cref="OnEnter"/> is kept in a registry under a serialized
    /// id that the clone inherits: a pasted copy of a teleporter works exactly like the original.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Teleporter : MonoBehaviour
    {
        static readonly Dictionary<int, Action> s_Actions = new Dictionary<int, Action>();
        static int s_NextId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Actions.Clear();
            s_NextId = 0;
        }

        [SerializeField, HideInInspector] int _actionId;

        /// <summary>What entering does (shared with every photo copy of this teleporter).</summary>
        public Action OnEnter
        {
            get => _actionId != 0 && s_Actions.TryGetValue(_actionId, out Action a) ? a : null;
            set
            {
                if (_actionId == 0) _actionId = ++s_NextId;
                s_Actions[_actionId] = value;
            }
        }

        /// <summary>True for a copy pasted from a photo (debug / tests).</summary>
        public bool IsPhotoCopy => transform.root.name == "PlacedPhotos";

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

        /// <summary>How many times the teleporter has fired (debug / tests).</summary>
        public int FireCount { get; private set; }

        void Fire()
        {
            if (Time.time < _cooldownUntil) return;
            _cooldownUntil = Time.time + Cooldown;
            FireCount++;
            // Fade to white, move at the peak, fade back (instant when there is no UI).
            Ion.Presentation.ScreenFx.RunTransition(OnEnter);
        }
    }
}
