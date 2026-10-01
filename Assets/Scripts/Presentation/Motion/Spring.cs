using UnityEngine;

namespace Ion.Presentation.Motion
{
    /// <summary>
    /// Semi-implicit damped springs (art bible §9). <c>freqHz</c> is the undamped natural frequency and
    /// <c>zeta</c> the damping ratio (1 = critical, &lt; 1 overshoots). Large frames are sub-stepped so the
    /// integration stays stable and frame-rate independent (a 50 ms WebGL hitch looks like five 10 ms steps).
    /// </summary>
    public static class Spring
    {
        /// <summary>Largest ω·h per sub-step (well inside semi-implicit Euler's stable region).</summary>
        const float MaxOmegaStep = 0.35f;
        const int MaxSubSteps = 16;

        /// <summary>Advances <paramref name="x"/> / <paramref name="v"/> toward <paramref name="target"/> by <paramref name="dt"/> seconds.</summary>
        public static void Step(ref float x, ref float v, float target, float freqHz, float zeta, float dt)
        {
            if (dt <= 0f) return;
            float omega = 2f * Mathf.PI * Mathf.Max(0.01f, freqHz);
            int n = Mathf.Clamp(Mathf.CeilToInt(omega * dt / MaxOmegaStep), 1, MaxSubSteps);
            float h = dt / n;
            float k = omega * omega, c = 2f * zeta * omega;
            for (int i = 0; i < n; i++)
            {
                v += (-k * (x - target) - c * v) * h;
                x += v * h;
            }
        }

        public static void Step(ref Vector2 x, ref Vector2 v, Vector2 target, float freqHz, float zeta, float dt)
        {
            Step(ref x.x, ref v.x, target.x, freqHz, zeta, dt);
            Step(ref x.y, ref v.y, target.y, freqHz, zeta, dt);
        }

        public static void Step(ref Vector3 x, ref Vector3 v, Vector3 target, float freqHz, float zeta, float dt)
        {
            Step(ref x.x, ref v.x, target.x, freqHz, zeta, dt);
            Step(ref x.y, ref v.y, target.y, freqHz, zeta, dt);
            Step(ref x.z, ref v.z, target.z, freqHz, zeta, dt);
        }

        /// <summary>Angle spring (degrees): follows the shortest way round to <paramref name="target"/>.</summary>
        public static void StepAngle(ref float deg, ref float v, float target, float freqHz, float zeta, float dt)
        {
            float t = deg + Mathf.DeltaAngle(deg, target);
            Step(ref deg, ref v, t, freqHz, zeta, dt);
        }

        /// <summary>True when x is within <paramref name="eps"/> of target and nearly still.</summary>
        public static bool Settled(float x, float v, float target, float eps = 1e-3f) =>
            Mathf.Abs(x - target) <= eps && Mathf.Abs(v) <= eps * 10f;
    }

    /// <summary>A float spring with its own state and parameters (handy as a field).</summary>
    public struct SpringFloat
    {
        public float Value, Velocity, Target, FreqHz, Zeta;

        public SpringFloat(float value, float freqHz, float zeta)
        {
            Value = Target = value;
            Velocity = 0f;
            FreqHz = freqHz;
            Zeta = zeta;
        }

        public float Step(float dt)
        {
            Spring.Step(ref Value, ref Velocity, Target, FreqHz, Zeta, dt);
            return Value;
        }

        /// <summary>Jumps to <paramref name="value"/> at rest.</summary>
        public void Snap(float value)
        {
            Value = Target = value;
            Velocity = 0f;
        }

        /// <summary>Adds an impulse (units per second) that the spring then absorbs.</summary>
        public void Kick(float velocity) => Velocity += velocity;

        public bool IsSettled(float eps = 1e-3f) => Spring.Settled(Value, Velocity, Target, eps);
    }

    /// <summary>Time-based tweens with the bible's named curves. All take t in [0, 1] (clamped).</summary>
    public static class Ease
    {
        public static float Linear(float t) => Mathf.Clamp01(t);
        public static float InQuad(float t) { t = Mathf.Clamp01(t); return t * t; }
        public static float OutQuad(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
        public static float InOutQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
        }
        public static float InCubic(float t) { t = Mathf.Clamp01(t); return t * t * t; }
        public static float OutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        public static float InOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }
        public static float OutQuart(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t * t; }
        public static float InOutSine(float t) { t = Mathf.Clamp01(t); return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f; }
        /// <summary>Sine bump 0 → 1 → 0 (sine in-out up, then down).</summary>
        public static float SineBump(float t) { t = Mathf.Clamp01(t); return Mathf.Sin(Mathf.PI * t); }
        /// <summary>easeOutBack with overshoot <paramref name="s"/> (bible: s = 1.3 for the pickup lift).</summary>
        public static float OutBack(float t, float s = 1.70158f)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + (s + 1f) * t * t * t + s * t * t;
        }

        /// <summary>
        /// Decaying oscillation for shakes: <paramref name="cycles"/> cycles over the unit interval with a
        /// damping-ratio-like envelope (bible: "nothing to rewind" chip, 2 cycles, ζ = 0.3).
        /// Returns -1..1 (multiply by the amplitude).
        /// </summary>
        public static float Shake(float t, float cycles = 2f, float zeta = 0.3f)
        {
            if (t <= 0f || t >= 1f) return 0f;
            float w = 2f * Mathf.PI * cycles;
            return Mathf.Sin(w * t) * Mathf.Exp(-zeta * w * t);
        }
    }
}
