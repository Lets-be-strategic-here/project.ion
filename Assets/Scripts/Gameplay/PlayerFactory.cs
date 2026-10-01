using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Ion.Gameplay
{
    /// <summary>Builds the complete player rig from code.</summary>
    public static class PlayerFactory
    {
        public const int PlayerLayer = 8;
        public const float EyeHeight = 1.62f;
        public const float Height = 1.8f;
        public const float Radius = 0.35f;

        /// <summary>
        /// Creates the player at <paramref name="position"/> (feet) facing <paramref name="yaw"/> degrees:
        /// CharacterController + FirstPersonController, a child MainCamera (fov 70, near 0.05),
        /// PhotoInventory, PhotoHolder, InstantCamera and PlayerInteractor. All on layer 8 (Player).
        /// </summary>
        public static FirstPersonController Create(Vector3 position, float yaw)
        {
            var root = new GameObject("Player");
            root.layer = PlayerLayer;
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var cc = root.AddComponent<CharacterController>();
            cc.height = Height;
            cc.radius = Radius;
            cc.center = new Vector3(0f, Height * 0.5f, 0f);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;

            // Make sure Camera.main resolves to the player camera.
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].CompareTag("MainCamera"))
                {
                    cameras[i].tag = "Untagged";
                    Debug.LogWarning("[PlayerFactory] Untagged existing MainCamera '" + cameras[i].name + "'.");
                }
            }

            var camGo = new GameObject("PlayerCamera");
            camGo.layer = PlayerLayer;
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
            camGo.transform.localRotation = Quaternion.identity;

            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 600f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.GetUniversalAdditionalCameraData(); // ensures URP camera data with defaults

            if (Object.FindFirstObjectByType<AudioListener>() == null)
                camGo.AddComponent<AudioListener>();

            var fpc = root.AddComponent<FirstPersonController>();
            fpc.AttachCamera(cam);
            root.AddComponent<PhotoInventory>();
            root.AddComponent<InstantCamera>();
            root.AddComponent<PhotoHolder>();
            root.AddComponent<PlayerInteractor>();

            return fpc;
        }
    }
}
