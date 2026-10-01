using Ion.Gameplay.State;
using Ion.Presentation;
using Ion.Presentation.Motion;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Levels.Props
{
    /// <summary>
    /// The checkpoint inlay's one-shot Ion ring: a thin square that grows out of the inlay's Ion dot whenever the
    /// checkpoint with <see cref="Id"/> becomes the current one (a marker visit, or a zone-entry checkpoint that
    /// shares the id), next to <c>CheckpointMarker</c>'s own dot pulse. Reads
    /// <see cref="WorldHistory.LastCheckpoint"/>; <see cref="Pulse"/> can also be called directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CheckpointPulse : MonoBehaviour
    {
        public string Id;

        [SerializeField] Transform _ring;
        [SerializeField] Transform[] _sides = new Transform[4];

        Checkpoint _last;
        bool _seen;
        float _t = 10f;

        internal void Init(string id)
        {
            Id = id;
            var ring = new GameObject("Pulse");
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            for (int i = 0; i < 4; i++)
            {
                var s = new GameObject("Side");
                s.transform.SetParent(ring.transform, false);
                s.AddComponent<MeshFilter>().sharedMesh = Geo.CubeMesh;
                var r = s.AddComponent<MeshRenderer>();
                r.sharedMaterial = Palette.Get(Mat.Ion);
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                _sides[i] = s.transform;
            }
            _ring = ring.transform;
            ring.SetActive(false);
        }

        /// <summary>Plays the pulse now.</summary>
        public void Pulse()
        {
            _t = 0f;
            if (_ring != null) _ring.gameObject.SetActive(true);
        }

        void Update()
        {
            if (Application.isPlaying)
            {
                WorldHistory wh = WorldHistory.Instance;
                Checkpoint cp = wh != null ? wh.LastCheckpoint : null;
                if (!_seen) { _seen = true; _last = cp; }
                else if (cp != _last)
                {
                    _last = cp;
                    if (cp != null && cp.Id == Id) Pulse();
                }
            }

            if (_ring == null || _t >= Feel.CheckpointPulseSeconds) return;
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Feel.CheckpointPulseSeconds);
            float size = Mathf.Lerp(0.125f, 1.5f, Ease.OutCubic(k));
            float w = Mathf.Lerp(0.0625f, 0.008f, k);
            float h = 0.01f * (1f - k) + 0.002f;
            float half = size * 0.5f;
            _sides[0].localPosition = new Vector3(0f, 0f, half); _sides[0].localScale = new Vector3(size + w, h, w);
            _sides[1].localPosition = new Vector3(0f, 0f, -half); _sides[1].localScale = new Vector3(size + w, h, w);
            _sides[2].localPosition = new Vector3(half, 0f, 0f); _sides[2].localScale = new Vector3(w, h, size + w);
            _sides[3].localPosition = new Vector3(-half, 0f, 0f); _sides[3].localScale = new Vector3(w, h, size + w);
            if (k >= 1f) _ring.gameObject.SetActive(false);
        }
    }

    /// <summary>Slow "breathing" scale on an Ion edge (pickup prints): reads as a soft glow without a new material.</summary>
    [DisallowMultipleComponent]
    public sealed class IonBreath : MonoBehaviour
    {
        public float Amount = 0.012f;
        public float Period = 2.6f;
        float _phase;

        void Awake() => _phase = (transform.position.x * 1.7f + transform.position.z * 0.9f) % 6.283f;

        void Update()
        {
            if (Feel.ReducedMotion) { transform.localScale = Vector3.one; return; }
            float s = 1f + Amount * (0.5f + 0.5f * Mathf.Sin(Time.time * 6.2832f / Period + _phase));
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
