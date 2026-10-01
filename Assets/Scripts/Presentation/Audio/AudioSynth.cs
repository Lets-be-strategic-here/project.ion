using UnityEngine;

namespace Ion.Presentation.Audio
{
    /// <summary>
    /// Pure, deterministic sample generators for <see cref="ProceduralAudio"/> (22050 Hz mono, -1..1).
    /// Startup-only: each call allocates its result array. No Unity objects are touched here, so every
    /// generator can be unit-tested or run in edit mode.
    /// </summary>
    public static class AudioSynth
    {
        public const int SampleRate = 22050;
        const float TwoPi = Mathf.PI * 2f;

        /// <summary>Tiny xorshift RNG (deterministic, allocation free).</summary>
        public struct Rng
        {
            uint _s;
            public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }

            public uint NextUInt()
            {
                uint x = _s;
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                _s = x;
                return x;
            }

            /// <summary>Uniform in [0, 1).</summary>
            public float Value => (NextUInt() >> 8) * (1f / 16777216f);
            /// <summary>Uniform in [-1, 1).</summary>
            public float Signed => Value * 2f - 1f;
            public float Range(float a, float b) => a + (b - a) * Value;
        }

        static int Samples(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));

        /// <summary>One-pole low-pass coefficient for cutoff <paramref name="hz"/>.</summary>
        static float Lp(float hz) => 1f - Mathf.Exp(-TwoPi * Mathf.Max(1f, hz) / SampleRate);

        static void Normalize(float[] d, float peak)
        {
            float max = 1e-6f;
            for (int i = 0; i < d.Length; i++) { float a = Mathf.Abs(d[i]); if (a > max) max = a; }
            float g = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= g;
        }

        /// <summary>Short linear fades at both ends so nothing clicks.</summary>
        static void EdgeFade(float[] d, float inSec, float outSec)
        {
            int fi = Mathf.Min(d.Length, Samples(inSec));
            int fo = Mathf.Min(d.Length, Samples(outSec));
            for (int i = 0; i < fi; i++) d[i] *= (float)i / fi;
            for (int i = 0; i < fo; i++) d[d.Length - 1 - i] *= (float)i / fo;
        }

        // ------------------------------------------------------------------ ambience

        /// <summary>
        /// Seamless wind loop of <paramref name="seconds"/> in one go (see <see cref="WindBuilder"/> to
        /// spread the work over several frames).
        /// </summary>
        public static float[] WindLoop(float seconds, uint seed)
        {
            var b = new WindBuilder(seconds, seed);
            while (!b.Step(int.MaxValue)) { }
            return b.Result;
        }

        /// <summary>
        /// Incremental wind-loop synthesis: two filtered-noise layers (low rumble + an airy band whose
        /// cutoff follows the gusts) under slow gust LFOs that complete whole cycles per loop; the seam is
        /// hidden by an equal-power crossfade of the overhang back into the start. Call
        /// <see cref="Step"/> once per frame until it returns true, then read <see cref="Result"/>.
        /// </summary>
        public sealed class WindBuilder
        {
            readonly int _n, _fade, _total;
            readonly float[] _raw;
            readonly float _aLo, _aHp, _c1, _c2, _c3;
            Rng _rng;
            float _lo1, _lo2, _hi1, _hi2, _hp;
            int _i;

            public float[] Result { get; private set; }

            public WindBuilder(float seconds, uint seed)
            {
                _n = Samples(seconds);
                _fade = Mathf.Min(_n / 2, Samples(1.5f));
                _total = _n + _fade;
                _raw = new float[_total];
                _rng = new Rng(seed);
                _aLo = Lp(160f);
                _aHp = Lp(220f);
                // Whole cycles per loop so the modulation is periodic with the loop length.
                _c1 = Mathf.Max(1f, Mathf.Round(seconds * 0.07f));
                _c2 = Mathf.Max(2f, Mathf.Round(seconds * 0.19f));
                _c3 = Mathf.Max(3f, Mathf.Round(seconds * 0.43f));
            }

            /// <summary>Synthesises up to <paramref name="maxSamples"/> more samples; true when done.</summary>
            public bool Step(int maxSamples)
            {
                if (Result != null) return true;
                int end = _total - _i > maxSamples ? _i + maxSamples : _total;
                for (; _i < end; _i++)
                {
                    float ph = (float)(_i % _n) / _n * TwoPi;
                    float gust = 0.55f + 0.25f * Mathf.Sin(ph * _c1) + 0.14f * Mathf.Sin(ph * _c2 + 1.3f) +
                                 0.06f * Mathf.Sin(ph * _c3 + 0.4f);
                    gust = Mathf.Clamp01(gust);

                    float w = _rng.Signed;
                    _lo1 += _aLo * (w - _lo1);
                    _lo2 += _aLo * (_lo1 - _lo2);
                    float aHi = Lp(380f + 900f * gust);
                    _hi1 += aHi * (w - _hi1);
                    _hi2 += aHi * (_hi1 - _hi2);
                    _hp += _aHp * (_hi2 - _hp);
                    float band = _hi2 - _hp;

                    _raw[_i] = _lo2 * (0.6f + 0.6f * gust) * 2.2f + band * gust * gust * 1.6f;
                }
                if (_i < _total) return false;

                var d = new float[_n];
                System.Array.Copy(_raw, d, _n);
                for (int i = 0; i < _fade; i++)
                {
                    float t = (float)i / _fade;
                    // d[0] = raw[n], which continues raw[n-1] = d[n-1]; fades into raw[i].
                    d[i] = _raw[_n + i] * Mathf.Sqrt(1f - t) + _raw[i] * Mathf.Sqrt(t);
                }
                Normalize(d, 0.9f);
                Result = d;
                return true;
            }
        }

        /// <summary>A short bird phrase: 2-5 sine-sweep syllables with a little vibrato.</summary>
        public static float[] BirdChirp(uint seed)
        {
            var rng = new Rng(seed);
            int syllables = 2 + (int)(rng.Value * 4f);
            float baseHz = rng.Range(2300f, 3600f);
            float gap = rng.Range(0.05f, 0.11f);
            float sylLen = rng.Range(0.05f, 0.09f);
            float len = syllables * (sylLen + gap) + 0.05f;
            var d = new float[Samples(len)];

            float t0 = 0.01f;
            for (int s = 0; s < syllables; s++)
            {
                float f0 = baseHz * rng.Range(0.85f, 1.1f);
                float f1 = f0 * rng.Range(1.15f, 1.6f) * (rng.Value < 0.3f ? 0.6f : 1f);
                float dur = sylLen * rng.Range(0.8f, 1.2f);
                float amp = rng.Range(0.6f, 1f);
                float vib = rng.Range(25f, 45f);
                int start = Samples(t0), count = Samples(dur);
                float phase = 0f;
                for (int i = 0; i < count && start + i < d.Length; i++)
                {
                    float u = (float)i / count;
                    float env = Mathf.Sin(u * Mathf.PI);
                    env *= env;
                    float f = Mathf.Lerp(f0, f1, u * u * (3f - 2f * u)) * (1f + 0.03f * Mathf.Sin(TwoPi * vib * i / SampleRate));
                    phase += TwoPi * f / SampleRate;
                    if (phase > TwoPi) phase -= TwoPi;
                    d[start + i] += amp * env * (Mathf.Sin(phase) + 0.12f * Mathf.Sin(2f * phase));
                }
                t0 += dur + gap * rng.Range(0.7f, 1.3f);
            }
            EdgeFade(d, 0.002f, 0.02f);
            Normalize(d, 0.8f);
            return d;
        }

        // ------------------------------------------------------------------ photo / camera

        /// <summary>Paper rustle: band-limited noise made of many tiny crackle grains.</summary>
        public static float[] PaperRustle(uint seed)
        {
            var rng = new Rng(seed);
            var d = new float[Samples(0.38f)];
            float lp = 0f, lp2 = 0f, grain = 0f;
            float aLp = Lp(5200f), aHp = Lp(900f);
            for (int i = 0; i < d.Length; i++)
            {
                float u = (float)i / d.Length;
                // Grains: random re-triggers whose density peaks early.
                float density = 0.004f * (1f - u) + 0.0008f;
                if (rng.Value < density) grain = rng.Range(0.4f, 1f);
                grain *= 0.9965f;

                float w = rng.Signed;
                lp += aLp * (w - lp);
                lp2 += aHp * (lp - lp2);
                float band = lp - lp2;
                float env = Mathf.Min(1f, u * 18f) * (1f - u) * (1f - u);
                d[i] = band * (0.25f + grain) * env;
            }
            EdgeFade(d, 0.003f, 0.03f);
            Normalize(d, 0.75f);
            return d;
        }

        /// <summary>Instant camera: a two-stage shutter click followed by a short motor/film whirr.</summary>
        public static float[] ShutterWhirr(uint seed)
        {
            var rng = new Rng(seed);
            var d = new float[Samples(1.0f)];

            // Clicks at 0 and ~45 ms: a highpassed noise tick + a damped resonant ping.
            AddClick(d, 0f, 1f, 2100f, ref rng);
            AddClick(d, 0.045f, 0.7f, 1500f, ref rng);

            // Whirr 0.16 - 0.92 s: lowpassed saw motor with gear flutter + a little noise.
            int ws = Samples(0.16f), we = Samples(0.92f);
            float ph = 0f, lp = 0f, nlp = 0f;
            float aLp = Lp(1400f), aN = Lp(2500f);
            for (int i = ws; i < we && i < d.Length; i++)
            {
                float u = (float)(i - ws) / (we - ws);
                float env = Mathf.Min(1f, u * 10f) * Mathf.Min(1f, (1f - u) * 6f);
                float hz = 118f + 14f * Mathf.Sin(u * 3.1f) - 20f * u; // spins down slightly
                ph += hz / SampleRate;
                if (ph >= 1f) ph -= 1f;
                float saw = ph * 2f - 1f;
                lp += aLp * (saw - lp);
                nlp += aN * (rng.Signed - nlp);
                float flutter = 0.65f + 0.35f * Mathf.Sin(TwoPi * 31f * (i - ws) / SampleRate);
                d[i] += (lp * 0.55f + nlp * 0.25f) * flutter * env;
            }
            EdgeFade(d, 0.0005f, 0.02f);
            Normalize(d, 0.8f);
            return d;
        }

        static void AddClick(float[] d, float at, float amp, float pingHz, ref Rng rng)
        {
            int s = Samples(at), n = Samples(0.03f);
            float prev = 0f;
            for (int i = 0; i < n && s + i < d.Length; i++)
            {
                float t = (float)i / SampleRate;
                float w = rng.Signed;
                float hpN = w - prev; // crude highpass (first difference)
                prev = w;
                float noise = hpN * Mathf.Exp(-t * 900f);
                float ping = Mathf.Sin(TwoPi * pingHz * t) * Mathf.Exp(-t * 260f);
                d[s + i] += amp * (noise * 0.6f + ping * 0.7f);
            }
        }

        // ------------------------------------------------------------------ projection

        /// <summary>Airy swell: noise through a low-pass whose cutoff sweeps up then down.</summary>
        static void AddWhoosh(float[] d, float start, float dur, float peakAt, float amp, ref Rng rng)
        {
            int s = Samples(start), n = Samples(dur);
            float l1 = 0f, l2 = 0f;
            for (int i = 0; i < n && s + i < d.Length; i++)
            {
                float u = (float)i / n;
                // Asymmetric bell envelope peaking at peakAt.
                float e = u < peakAt ? u / peakAt : 1f - (u - peakAt) / (1f - peakAt);
                e = e * e * (3f - 2f * e);
                float a = Lp(250f + 2600f * e);
                float w = rng.Signed;
                l1 += a * (w - l1);
                l2 += a * (l1 - l2);
                d[s + i] += l2 * e * amp * 3f;
            }
        }

        /// <summary>Soft glassy bell: fundamental with weak inharmonic partials and exponential decay.</summary>
        static void AddBell(float[] d, float start, float hz, float amp, float decay)
        {
            int s = Samples(start);
            int n = d.Length - s;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float atk = Mathf.Min(1f, t * 250f);
                float v = Mathf.Sin(TwoPi * hz * t) * Mathf.Exp(-t * decay)
                        + 0.22f * Mathf.Sin(TwoPi * hz * 2.01f * t) * Mathf.Exp(-t * decay * 2.2f)
                        + 0.08f * Mathf.Sin(TwoPi * hz * 3.98f * t) * Mathf.Exp(-t * decay * 4f);
                d[s + i] += v * amp * atk;
            }
        }

        /// <summary>Placement: whoosh into a soft two-note chime (G5 + D6).</summary>
        public static float[] PlaceWhooshChime(uint seed)
        {
            var rng = new Rng(seed);
            var d = new float[Samples(1.5f)];
            AddWhoosh(d, 0f, 0.55f, 0.55f, 0.7f, ref rng);
            AddBell(d, 0.24f, 783.99f, 0.32f, 3.2f);
            AddBell(d, 0.30f, 1174.66f, 0.22f, 3.6f);
            EdgeFade(d, 0.004f, 0.08f);
            Normalize(d, 0.8f);
            return d;
        }

        /// <summary>Rewind: a reversed whoosh + reversed chime tail (swells in, stops softly).</summary>
        public static float[] RewindReverse(uint seed)
        {
            var rng = new Rng(seed);
            var d = new float[Samples(0.95f)];
            AddWhoosh(d, 0.05f, 0.75f, 0.3f, 0.8f, ref rng);
            AddBell(d, 0.02f, 659.25f, 0.18f, 4.5f);
            AddBell(d, 0.06f, 987.77f, 0.12f, 5f);
            System.Array.Reverse(d);
            EdgeFade(d, 0.01f, 0.04f);
            Normalize(d, 0.75f);
            return d;
        }

        /// <summary>Pickup: quick rising C6-E6-G6 arpeggio of soft bells.</summary>
        public static float[] PickupChime()
        {
            var d = new float[Samples(1.0f)];
            AddBell(d, 0.00f, 1046.50f, 0.30f, 5f);
            AddBell(d, 0.07f, 1318.51f, 0.26f, 5f);
            AddBell(d, 0.14f, 1567.98f, 0.24f, 4.2f);
            EdgeFade(d, 0.002f, 0.06f);
            Normalize(d, 0.75f);
            return d;
        }

        /// <summary>Soft footstep thud: a falling low sine + a short low-passed noise scuff.</summary>
        public static float[] Footstep(uint seed)
        {
            var rng = new Rng(seed);
            var d = new float[Samples(0.16f)];
            float f0 = rng.Range(85f, 110f);
            float scuffHz = rng.Range(500f, 900f);
            float a = Lp(scuffHz), l1 = 0f, l2 = 0f, ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SampleRate;
                float hz = f0 * (0.65f + 0.35f * Mathf.Exp(-t * 40f));
                ph += TwoPi * hz / SampleRate;
                float body = Mathf.Sin(ph) * Mathf.Exp(-t * 38f);
                l1 += a * (rng.Signed - l1);
                l2 += a * (l1 - l2);
                float scuff = l2 * 3f * Mathf.Exp(-t * 55f);
                float atk = Mathf.Min(1f, t * 600f);
                d[i] = (body * 0.8f + scuff * 0.6f) * atk;
            }
            EdgeFade(d, 0.001f, 0.02f);
            Normalize(d, 0.8f);
            return d;
        }

        /// <summary>Teleport: a rising cluster of twinkling sines over a thin airy hiss.</summary>
        public static float[] TeleportShimmer(uint seed)
        {
            var rng = new Rng(seed);
            var d = new float[Samples(1.3f)];
            const int Partials = 9;
            for (int p = 0; p < Partials; p++)
            {
                float hz = rng.Range(900f, 2600f);
                float onset = rng.Range(0f, 0.35f);
                float tremHz = rng.Range(7f, 15f);
                float amp = rng.Range(0.5f, 1f) / Partials;
                int s = Samples(onset);
                float ph = 0f;
                for (int i = s; i < d.Length; i++)
                {
                    float t = (float)(i - s) / SampleRate;
                    float u = (float)i / d.Length;
                    float f = hz * (1f + 0.5f * u * u); // glissando up
                    ph += TwoPi * f / SampleRate;
                    if (ph > TwoPi) ph -= TwoPi;
                    float env = Mathf.Min(1f, t * 20f) * Mathf.Exp(-t * 2.6f);
                    float trem = 0.6f + 0.4f * Mathf.Sin(TwoPi * tremHz * t);
                    d[i] += Mathf.Sin(ph) * env * trem * amp;
                }
            }
            float l1 = 0f, l2 = 0f, aLo = Lp(6000f), aHp = Lp(2500f);
            for (int i = 0; i < d.Length; i++)
            {
                float u = (float)i / d.Length;
                l1 += aLo * (rng.Signed - l1);
                l2 += aHp * (l1 - l2);
                d[i] += (l1 - l2) * 0.35f * Mathf.Sin(u * Mathf.PI);
            }
            EdgeFade(d, 0.01f, 0.1f);
            Normalize(d, 0.75f);
            return d;
        }
    }
}
