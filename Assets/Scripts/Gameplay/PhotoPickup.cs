using System.Collections.Generic;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;

namespace Ion.Gameplay
{
    /// <summary>
    /// A photo lying in the world. Collected by walking into it or by pressing E within 2.5 m
    /// (handled by <see cref="PlayerInteractor"/>). If the object has no renderers, a floating
    /// Polaroid visual showing <see cref="PhotoData.Preview"/> is built in Start.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhotoPickup : MonoBehaviour
    {
        public const float InteractRange = 2.5f;
        const string VisualName = "PickupVisual";
        const string PhotoShaderName = "Ion/PhotoDisplay";

        static readonly List<PhotoPickup> s_Active = new List<PhotoPickup>();
        internal static List<PhotoPickup> Active => s_Active;

        public PhotoData Photo;

        /// <summary>Walking within this distance (to the player's capsule) collects the photo.</summary>
        public float TouchRadius = 0.6f;
        /// <summary>Height of the generated visual above the transform.</summary>
        public float VisualHeight = 0.45f;
        public bool Animate = true;

        public bool Collected { get; private set; }

        Transform _visual;
        Material _photoMaterial;
        float _phase;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Active.Clear();

        /// <summary>World position used for range checks and the prompt (the photo itself).</summary>
        public Vector3 FocusPoint => transform.position + Vector3.up * VisualHeight;

        void Awake()
        {
            if (!TryGetComponent<Interactable>(out _)) gameObject.AddComponent<Interactable>();
            _phase = Random.value * 10f;
        }

        void Start()
        {
            EnsureTrigger();
            EnsureVisual();
        }

        void OnEnable()
        {
            if (!s_Active.Contains(this)) s_Active.Add(this);
        }

        void OnDisable() => s_Active.Remove(this);

        void OnDestroy()
        {
            if (_photoMaterial != null) Destroy(_photoMaterial);
        }

        void EnsureTrigger()
        {
            var cols = GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
                if (cols[i].isTrigger) return;

            var sphere = gameObject.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = TouchRadius;
            sphere.center = new Vector3(0f, VisualHeight, 0f);
        }

        void EnsureVisual()
        {
            _visual = transform.Find(VisualName);
            if (_visual != null || GetComponentInChildren<Renderer>() != null) return;

            float aspect = Photo != null && Photo.Aspect > 0.1f ? Photo.Aspect : 4f / 3f;
            float photoW = 0.34f;
            float photoH = photoW / aspect;
            if (photoH > 0.34f) { photoH = 0.34f; photoW = photoH * aspect; }
            const float border = 0.025f, bottom = 0.08f, thickness = 0.02f;

            var root = new GameObject(VisualName).transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(0f, VisualHeight, 0f);
            _visual = root;

            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            RemoveCollider(frame);
            frame.transform.SetParent(root, false);
            frame.transform.localScale = new Vector3(photoW + border * 2f, photoH + border + bottom, thickness);
            frame.transform.localPosition = new Vector3(0f, -(bottom - border) * 0.5f, 0f);
            frame.GetComponent<Renderer>().sharedMaterial = Palette.Get(Palette.Cream);

            Material faceMat = CreatePhotoMaterial();
            for (int side = 0; side < 2; side++)
            {
                var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                face.name = side == 0 ? "PhotoFront" : "PhotoBack";
                RemoveCollider(face);
                face.transform.SetParent(root, false);
                face.transform.localScale = new Vector3(photoW, photoH, 1f);
                float z = thickness * 0.5f + 0.002f;
                face.transform.localPosition = new Vector3(0f, 0f, side == 0 ? -z : z);
                face.transform.localRotation = side == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                face.GetComponent<Renderer>().sharedMaterial = faceMat;
            }
        }

        Material CreatePhotoMaterial()
        {
            var shader = Shader.Find(PhotoShaderName);
            Texture2D tex = Photo != null ? Photo.Preview : null;
            if (shader == null || tex == null)
                return Palette.Get(Palette.Sky);

            _photoMaterial = new Material(shader) { name = "PickupPhoto" };
            if (_photoMaterial.HasProperty("_MainTex")) _photoMaterial.SetTexture("_MainTex", tex);
            if (_photoMaterial.HasProperty("_BaseMap")) _photoMaterial.SetTexture("_BaseMap", tex);
            if (_photoMaterial.HasProperty("_PhotoTex")) _photoMaterial.SetTexture("_PhotoTex", tex);
            // Ion/PhotoDisplay is a UI shader (ZTest [unity_GUIZTestMode]). Outside a Canvas that global
            // is not set for us, so force a normal depth test or the photo would show through walls.
            _photoMaterial.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            return _photoMaterial;
        }

        static void RemoveCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c == null) return;
            c.enabled = false;
            Destroy(c);
        }

        void OnTriggerEnter(Collider other)
        {
            if (Collected || other == null) return;
            var fpc = other.GetComponentInParent<FirstPersonController>();
            if (fpc != null) Collect(fpc.GetComponent<PhotoInventory>());
        }

        void Update()
        {
            if (Collected)
            {
                // Something (e.g. a rewind) re-enabled a collected pickup; keep it gone.
                gameObject.SetActive(false);
                return;
            }

            if (Animate && _visual != null)
            {
                float t = Time.time + _phase;
                _visual.localPosition = new Vector3(0f, VisualHeight + Mathf.Sin(t * 2f) * 0.05f, 0f);
                _visual.localRotation = Quaternion.Euler(-12f, t * 40f, 0f);
            }

            // Fallback for touch collection (independent of physics trigger callbacks).
            var player = FirstPersonController.Current;
            if (player == null || !player.InputEnabled) return;
            Vector3 feet = player.transform.position;
            Vector3 p = FocusPoint;
            float y = Mathf.Clamp(p.y, feet.y + 0.35f, feet.y + 1.45f);
            Vector3 closest = new Vector3(feet.x, y, feet.z);
            float r = TouchRadius + 0.35f;
            if ((p - closest).sqrMagnitude <= r * r)
                Collect(player.GetComponent<PhotoInventory>());
        }

        /// <summary>Adds the photo to the inventory, toasts, and disables the pickup.</summary>
        public bool Collect(PhotoInventory inventory)
        {
            if (Collected || inventory == null) return false;
            Collected = true;

            if (Photo != null)
            {
                inventory.Add(Photo);
                GameplayUI.Toast(string.IsNullOrEmpty(Photo.Label)
                    ? "Picked up a photo"
                    : "Picked up photo: " + Photo.Label);
            }
            else
            {
                GameplayUI.Toast("The photo is blank");
            }

            gameObject.SetActive(false);
            return true;
        }
    }
}
