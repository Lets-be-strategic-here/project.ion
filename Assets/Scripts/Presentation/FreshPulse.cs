using System.Collections.Generic;
using Ion.Levels;
using Ion.Presentation.Motion;
using Ion.Projection;
using UnityEngine;

namespace Ion.Presentation
{
    /// <summary>
    /// "Freshly developed" look on the pieces a placement just pasted (art bible §9.1): they start in a warm,
    /// washed-out print tone and develop into their true colours over 0.6 s (sine in-out), written as an
    /// emission offset with a MaterialPropertyBlock. Renderers with a property block drop
    /// out of the SRP batcher, so the block is removed again as soon as the pulse ends (the batching cost
    /// lasts 0.6 s, after which the renderers are exactly as they were).
    /// </summary>
    public sealed class FreshPulse : MonoBehaviour
    {
        public const float Seconds = Feel.DevelopSeconds;
        public float Intensity = 0.38f;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        // A warm, desaturated print tone (Paper pushed toward Brass).
        static readonly Color Warm = new Color(1f, 0.92f, 0.78f);

        readonly List<Renderer> _renderers = new List<Renderer>(128);
        readonly List<Color> _baseEmission = new List<Color>(128);
        MaterialPropertyBlock _block;
        ProjectionSystem _projection;
        float _t = -1f;

        void OnDestroy()
        {
            Clear();
            if (_projection != null)
            {
                _projection.Placed -= OnPlaced;
                _projection.Rewound -= OnRewound;
            }
        }

        void OnPlaced()
        {
            Clear();
            if (_projection == null || GameBootstrap.Restarting) return;
            IReadOnlyList<Renderer> pasted = _projection.LastPastedRenderers;
            for (int i = 0; i < pasted.Count; i++)
            {
                Renderer r = pasted[i];
                if (r == null) continue;
                Material m = r.sharedMaterial;
                _renderers.Add(r);
                _baseEmission.Add(m != null && m.HasProperty(EmissionId) ? m.GetColor(EmissionId) : Color.black);
            }
            _t = _renderers.Count > 0 ? 0f : -1f;
        }

        void OnRewound() => Clear();

        void Clear()
        {
            for (int i = 0; i < _renderers.Count; i++)
                if (_renderers[i] != null) _renderers[i].SetPropertyBlock(null);
            _renderers.Clear();
            _baseEmission.Clear();
            _t = -1f;
        }

        void LateUpdate()
        {
            if (_projection == null)
            {
                var ps = ProjectionSystem.Instance;
                if (ps != null)
                {
                    _projection = ps;
                    ps.Placed += OnPlaced;
                    ps.Rewound += OnRewound;
                }
            }
            if (_t < 0f) return;

            _t += Time.unscaledDeltaTime;
            float k = 1f - _t / Seconds;
            if (k <= 0f)
            {
                Clear();
                return;
            }
            if (_block == null) _block = new MaterialPropertyBlock();
            float glow = Intensity * (1f - Ease.InOutSine(_t / Seconds)); // develops in
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                _block.SetColor(EmissionId, _baseEmission[i] + Warm * glow);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
