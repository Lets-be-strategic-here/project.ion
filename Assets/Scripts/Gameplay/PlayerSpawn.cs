using System.Collections.Generic;
using UnityEngine;

namespace Ion.Gameplay
{
    /// <summary>
    /// Spawn point / checkpoint marker. Position = feet position, Y rotation = facing.
    /// When the grounded player comes within <see cref="CheckpointRadius"/>, it becomes the respawn point
    /// used after falling out of the world.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawn : MonoBehaviour
    {
        static readonly List<PlayerSpawn> s_All = new List<PlayerSpawn>();

        /// <summary>All enabled spawns, in enable order.</summary>
        public static IReadOnlyList<PlayerSpawn> All => s_All;

        public float CheckpointRadius = 3f;

        public Vector3 Position => transform.position;
        public float Yaw => transform.eulerAngles.y;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();

        void OnEnable()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
        }

        void OnDisable() => s_All.Remove(this);
    }
}
