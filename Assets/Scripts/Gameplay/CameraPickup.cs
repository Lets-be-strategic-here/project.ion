using Ion.Gameplay.State;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;

namespace Ion.Gameplay
{
    /// <summary>
    /// The instant camera on its stand (moved here from Levels/LevelProps; art bible §5, §11). Walking into it
    /// (or pressing E nearby) unlocks the player's <see cref="InstantCamera"/> with <see cref="Film"/> shots and
    /// records a rewindable change. An <see cref="Interactable"/>: never sliced.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraPickup : MonoBehaviour
    {
        public int Film = 3;
        /// <summary>Walking within this horizontal radius (feet within 2.5 m vertically) takes the camera.</summary>
        public float Radius = 1.2f;
        /// <summary>The camera model (hidden while taken). Defaults to the first child.</summary>
        public GameObject Visual;

        public bool Taken { get; private set; }

        void Awake()
        {
            if (!TryGetComponent<Interactable>(out _)) gameObject.AddComponent<Interactable>();
            if (Visual == null && transform.childCount > 0) Visual = transform.GetChild(0).gameObject;
        }

        /// <summary>Puts the camera back on its stand (rewind, restart).</summary>
        public void ResetPickup()
        {
            Taken = false;
            enabled = true;
            if (Visual != null) Visual.SetActive(true);
        }

        void Update()
        {
            if (Taken) return;
            var player = FirstPersonController.Current;
            if (player == null || !player.InputEnabled) return;
            Vector3 d = player.transform.position - transform.position;
            if (d.y < -1f || d.y > 2.5f || d.x * d.x + d.z * d.z > Radius * Radius) return;
            Take(player);
        }

        /// <summary>Gives the camera to <paramref name="player"/> (walk-in or E). False if already taken.</summary>
        public bool Take(FirstPersonController player)
        {
            if (Taken || player == null) return false;
            var camera = player.GetComponent<InstantCamera>();
            if (camera == null) return false;

            var change = new CameraPickupChange
            {
                Pickup = this,
                UnlockedBefore = camera.Unlocked,
                FilmBefore = camera.Film,
                Camera = camera,
                SafePose = WorldHistory.SafePoseNow(),
            };
            Taken = true;
            camera.SetUnlockedSilently(true);
            camera.Film = Film;
            var history = WorldHistory.Instance;
            if (history != null) history.Push(change);

            GameplayUI.Toast("Instant camera  " + UIUtil.Key("C") + " take it out   " + UIUtil.Key("SHIFT") + " aim   " +
                             UIUtil.Key("LMB") + " shoot   (" + Film + " film)", 6f);
            if (Visual != null) Visual.SetActive(false);
            enabled = false;
            return true;
        }
    }
}
