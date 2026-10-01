using Ion.Gameplay;
using UnityEngine;

namespace Ion.Presentation.Audio
{
    /// <summary>
    /// Ambience: one wind loop (a touch windier higher up, quieter in the tutorial darkroom zone and in limbo),
    /// occasional positional birds (Medium/High tiers only), the hum of the nearest powered teleporter and the
    /// limbo drone.
    /// </summary>
    public sealed partial class IonAudio
    {
        const float HumRange = 14f;

        LoopHandle _wind, _hum, _drone;
        Teleporter _humTarget;
        bool _birdsEnabled = true;
        float _nextBird, _nextHumCheck;
        float _windLevel = 1f;

        void AwakeAmbience()
        {
            _nextBird = Time.time + 6f;
        }

        void UpdateAmbience(float dt)
        {
            var fpc = FirstPersonController.Current;
            if (fpc == null) return;

            // Wind: started once (isPlaying is unreliable on the web before the first click), fades in over 4 s.
            if (!_wind.IsValid) _wind = StartLoop(Sfx.AmbWindLoop, null, null, 0f, 4f);
            float height = Mathf.Clamp01((fpc.transform.position.y + 5f) / 30f);
            float target = (0.85f + 0.4f * height) * (_inLimbo ? 0.35f : 1f) * (_area == MusicArea.None ? 0.6f : 1f);
            _windLevel = Mathf.MoveTowards(_windLevel, target, dt * 0.5f);
            SetLoopVolume(_wind, _windLevel);

            // Limbo drone.
            if (_inLimbo && !_drone.IsValid) _drone = StartLoop(Sfx.LimboDroneLoop, null, null, 1f, 0.8f);
            else if (!_inLimbo && _drone.IsValid) StopLoop(ref _drone, 0.6f);

            UpdateBirds(fpc);
            UpdateHum(fpc);
        }

        void UpdateBirds(FirstPersonController fpc)
        {
            float now = Time.time;
            if (now < _nextBird) return;
            _nextBird = now + Random.Range(6f, 16f);
            if (!_birdsEnabled || _inLimbo || _falling) return;

            // Somewhere off to the side and a little above the player.
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(12f, 30f);
            Vector3 p = fpc.transform.position + new Vector3(Mathf.Cos(ang) * dist, Random.Range(4f, 12f), Mathf.Sin(ang) * dist);
            Play(Sfx.AmbBird, p, Random.Range(0.7f, 1.1f), Random.Range(0.92f, 1.1f));
            // Sometimes a quick answer from another bird.
            if (Random.value < 0.35f) _nextBird = now + Random.Range(0.8f, 1.6f);
        }

        void UpdateHum(FirstPersonController fpc)
        {
            float now = Time.unscaledTime;
            if (now < _nextHumCheck) return;
            _nextHumCheck = now + 0.25f;

            Teleporter best = null;
            float bestD = HumRange * HumRange;
            Vector3 p = fpc.transform.position;
            for (int i = 0; i < _teleporters.Count; i++)
            {
                Teleporter t = _teleporters[i];
                if (t == null || !t.isActiveAndEnabled || !t.Powered) continue;
                float d = (t.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            if (best == _humTarget && (best == null || IsLoopActive(_hum))) return;
            if (_hum.IsValid) StopLoop(ref _hum, 0.4f);
            _humTarget = best;
            if (best != null) _hum = StartLoop(Sfx.TeleporterHumLoop, best.transform, null, 1f, 0.6f);
        }

        void StopAmbienceForRestart()
        {
            if (_hum.IsValid) StopLoop(ref _hum, 0.2f);
            if (_drone.IsValid) StopLoop(ref _drone, 0.2f);
            _humTarget = null;
        }
    }
}
