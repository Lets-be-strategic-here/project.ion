using System.Collections.Generic;
using Ion.Gameplay.State;
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
        Transform _ring;
        const string RingName = "GlowRing";
        Material _photoMaterial;
        float _phase;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Active.Clear();
            s_TaughtRaise = false;
        }

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
            _ring = transform.Find(RingName);
            if (_visual != null || GetComponentInChildren<Renderer>() != null) return;

            // Soft glow ring resting on the pedestal under the floating photo.
            var ring = new GameObject(RingName);
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            ring.AddComponent<MeshFilter>().sharedMesh = RingMesh;
            var rr = ring.AddComponent<MeshRenderer>();
            rr.sharedMaterial = Palette.GetEmissive(UIPalette.Ion, 0.8f); // ion = "use me"
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring = ring.transform;

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
            frame.GetComponent<Renderer>().sharedMaterial = Palette.Get(UIPalette.Paper);

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
                return Palette.Get(UIPalette.Frost);

            _photoMaterial = new Material(shader) { name = "PickupPhoto" };
            if (_photoMaterial.HasProperty("_MainTex")) _photoMaterial.SetTexture("_MainTex", tex);
            if (_photoMaterial.HasProperty("_BaseMap")) _photoMaterial.SetTexture("_BaseMap", tex);
            if (_photoMaterial.HasProperty("_PhotoTex")) _photoMaterial.SetTexture("_PhotoTex", tex);
            // Ion/PhotoDisplay is a UI shader (ZTest [unity_GUIZTestMode]). Outside a Canvas that global
            // is not set for us, so force a normal depth test or the photo would show through walls.
            _photoMaterial.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            return _photoMaterial;
        }

        static Mesh s_RingMesh;

        /// <summary>Flat, two-sided ring (inner radius 0.3, outer 0.36, 24 segments).</summary>
        internal static Mesh RingMesh
        {
            get
            {
                if (s_RingMesh == null) s_RingMesh = BuildRing(0.3f, 0.36f, 24);
                return s_RingMesh;
            }
        }

        /// <summary>Flat ring in the XZ plane, faces up and down, flat normals.</summary>
        internal static Mesh BuildRing(float inner, float outer, int segments)
        {
            var v = new Vector3[segments * 4 * 2];
            var n = new Vector3[v.Length];
            var t = new int[segments * 6 * 2];
            int vi = 0, ti = 0;
            for (int side = 0; side < 2; side++)
            {
                Vector3 normal = side == 0 ? Vector3.up : Vector3.down;
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                    var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    int b = vi;
                    v[vi++] = d0 * inner; v[vi++] = d0 * outer; v[vi++] = d1 * outer; v[vi++] = d1 * inner;
                    for (int k = 0; k < 4; k++) n[b + k] = normal;
                    if (side == 0)
                    {
                        t[ti++] = b; t[ti++] = b + 2; t[ti++] = b + 1;
                        t[ti++] = b; t[ti++] = b + 3; t[ti++] = b + 2;
                    }
                    else
                    {
                        t[ti++] = b; t[ti++] = b + 1; t[ti++] = b + 2;
                        t[ti++] = b; t[ti++] = b + 2; t[ti++] = b + 3;
                    }
                }
            }
            var mesh = new Mesh { name = "Ion_Ring" };
            mesh.vertices = v;
            mesh.normals = n;
            mesh.triangles = t;
            mesh.RecalculateBounds();
            return mesh;
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
            if (Collected || other == null || _waitForExit) return;
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
                // A slow float with a gentle sway (no spinning: it reads as a print, not a power-up).
                _visual.localPosition = new Vector3(0f, VisualHeight + Mathf.Sin(t * 1.6f) * 0.03f, 0f);
                _visual.localRotation = Quaternion.Euler(-15f, Mathf.Sin(t * 0.7f) * 12f, 0f);
            }
            if (Animate && _ring != null)
            {
                // Slow breathing glow.
                float t = Time.time + _phase;
                float s = 1f + 0.06f * (0.5f + 0.5f * Mathf.Sin(t * 2.0f));
                _ring.localScale = new Vector3(s, 1f, s);
            }

            // Fallback for touch collection (independent of physics trigger callbacks). Never while a rewind
            // glides the player past it.
            var player = FirstPersonController.Current;
            if (player == null || !player.InputEnabled || player.Frozen) return;
            Vector3 feet = player.transform.position;
            Vector3 p = FocusPoint;
            float y = Mathf.Clamp(p.y, feet.y + 0.35f, feet.y + 1.45f);
            Vector3 closest = new Vector3(feet.x, y, feet.z);
            float r = TouchRadius + 0.35f;
            bool touching = (p - closest).sqrMagnitude <= r * r;
            if (_waitForExit)
            {
                if (!touching) _waitForExit = false;
                return;
            }
            if (touching) Collect(player.GetComponent<PhotoInventory>());
        }

        // Set by ResetPickup: a rewind returns the player to where they picked the photo up, so walking into it
        // only collects it again after stepping away (E still collects it at once).
        bool _waitForExit;

        /// <summary>Puts the photo back where it was (rewind, checkpoint restore, game restart).</summary>
        public void ResetPickup()
        {
            Collected = false;
            _waitForExit = true;
            gameObject.SetActive(true);
        }

        static bool s_TaughtRaise;

        /// <summary>
        /// Adds the photo to the inventory (it lifts and flies into its HUD slot), records a rewindable
        /// change and disables the pickup.
        /// </summary>
        public bool Collect(PhotoInventory inventory)
        {
            if (Collected || inventory == null) return false;
            Collected = true;

            if (Photo != null && !inventory.Contains(Photo))
            {
                PlayerPose safe = WorldHistory.SafePoseNow();
                PlayerPose pose = WorldHistory.PoseNow();
                inventory.Add(Photo);
                var history = WorldHistory.Instance;
                if (history != null)
                {
                    history.Push(new PickupChange
                    {
                        Pickup = this,
                        Photo = Photo,
                        Index = inventory.IndexOf(Photo),
                        Inventory = inventory,
                        SafePose = safe,
                    }.At(pose));
                }
                GameplayUI.PhotoCollected(Photo, FocusPoint);
                string label = string.IsNullOrEmpty(Photo.Label) ? "Photo" : Photo.Label;
                GameplayUI.Toast(s_TaughtRaise ? label : label + "   " + UIUtil.Key("SHIFT") + " hold it up");
                s_TaughtRaise = true;
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
