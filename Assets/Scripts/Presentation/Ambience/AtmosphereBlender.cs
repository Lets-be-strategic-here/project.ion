using UnityEngine;

namespace Ion.Presentation
{
    /// <summary>
    /// Drives <see cref="Atmosphere.BlendTo"/>: a hidden object that eases the zone mood from one to the next
    /// (sine in-out) and removes itself when done. Only one blend runs; a new one starts from the current look.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class AtmosphereBlender : MonoBehaviour
    {
        static AtmosphereBlender s_Instance;

        ZoneMood _from, _to;
        float _duration, _time;

        internal static bool IsBlending => s_Instance != null && s_Instance.enabled;
        internal static ZoneMood To => s_Instance != null ? s_Instance._to : Atmosphere.Current;

        internal static void Begin(ZoneMood from, ZoneMood to, float seconds)
        {
            if (s_Instance == null)
            {
                var go = new GameObject("Ion Atmosphere Blend") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(go);
                s_Instance = go.AddComponent<AtmosphereBlender>();
            }
            s_Instance._from = from;
            s_Instance._to = to;
            s_Instance._duration = Mathf.Max(0.01f, seconds);
            s_Instance._time = 0f;
            s_Instance.enabled = true;
        }

        internal static void Stop()
        {
            if (s_Instance != null) s_Instance.enabled = false;
        }

        void Update()
        {
            _time += Time.deltaTime;
            float t = Mathf.Clamp01(_time / _duration);
            float e = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI);
            Atmosphere.Push(ZoneMood.Lerp(_from, _to, e));
            if (t >= 1f) enabled = false;
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }
    }
}
