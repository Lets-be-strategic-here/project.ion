using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Presentation
{
    /// <summary>
    /// Warm dust motes / pollen: one world-space ParticleSystem whose box emitter follows the view
    /// camera. Tiny soft billboards (alpha blended), slow random drift, fade in/out. Lives on the
    /// Player layer so photo previews never contain motes. Cost knob: particle count (by tier); the
    /// noise module (per-particle CPU) only runs on High.
    /// </summary>
    public sealed class AmbienceMotes : MonoBehaviour
    {
        static readonly int[] k_TierCounts = { 30, 90, 180 };
        public static int CountForTier(int tier) => k_TierCounts[Mathf.Clamp(tier, 0, k_TierCounts.Length - 1)];

        const float LifetimeMin = 6f, LifetimeMax = 11f;
        static readonly Vector3 k_Volume = new Vector3(26f, 10f, 26f);

        ParticleSystem _ps;
        Material _material;
        AmbienceDirector _director;
        int _count = -1;
        bool _noise;

        void Awake()
        {
            _director = AmbienceDirector.Of(this);
            _material = Ambience.CreateSoftMaterial("Ion_Motes", Color.white, false, 1.6f, 0.6f);
            if (_material == null) { enabled = false; return; }

            _ps = gameObject.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.duration = 10f;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(LifetimeMin, LifetimeMax);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
            main.startRotation = 0f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            // Warm butter -> peach, mostly opaque at the core (the soft falloff does the rest).
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.93f, 0.72f, 0.85f),
                new Color(1f, 0.82f, 0.68f, 0.6f));

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = k_Volume;

            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.16f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.03f, 0.09f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var noise = _ps.noise;
            noise.enabled = false;
            noise.strength = new ParticleSystem.MinMaxCurve(0.18f);
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.08f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var r = GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.View;
            r.sortMode = ParticleSystemSortMode.None;
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.maxParticleSize = 0.012f; // a mote drifting through the near plane stays a speck
        }

        /// <summary>Sets the particle budget and whether turbulence (noise) runs.</summary>
        public void Configure(int count, bool noise)
        {
            if (_ps == null) return;
            count = Mathf.Max(1, count);
            if (count == _count && noise == _noise) return;
            _count = count;
            _noise = noise;

            var main = _ps.main;
            main.maxParticles = count;
            var em = _ps.emission;
            em.enabled = true;
            em.rateOverTime = count / ((LifetimeMin + LifetimeMax) * 0.5f);
            var n = _ps.noise;
            n.enabled = noise;

            FollowCamera();
            if (!_ps.isPlaying) _ps.Play(true);
        }

        void OnEnable()
        {
            if (_ps != null && _count > 0 && !_ps.isPlaying)
            {
                FollowCamera();
                _ps.Play(true);
            }
        }

        void LateUpdate() => FollowCamera();

        void FollowCamera()
        {
            Camera cam = _director != null ? _director.ViewCamera : Camera.main;
            if (cam == null) return;
            Transform ct = cam.transform;
            // Bias the volume slightly ahead so most motes are where the player looks.
            transform.SetPositionAndRotation(ct.position + ct.forward * 4f, Quaternion.identity);
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
