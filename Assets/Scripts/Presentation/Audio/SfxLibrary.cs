using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ion.Presentation.Audio
{
    /// <summary>How one <see cref="Sfx"/> is played (mix level, randomisation, 3D falloff).</summary>
    public readonly struct SfxDef
    {
        /// <summary>Resource path under <see cref="SfxLibrary.SfxRoot"/>, no extension. With variants: a
        /// prefix that gets "_0".."_n-1" appended.</summary>
        public readonly string Path;
        public readonly int Variants;
        /// <summary>AudioSource volume before the SFX bus (clips peak at -6 dBFS).</summary>
        public readonly float Volume;
        public readonly float Pitch;
        /// <summary>± fraction of random pitch per play.</summary>
        public readonly float PitchJitter;
        /// <summary>Positional when <see cref="IonAudio.Play(Sfx, Vector3?)"/> gets a position.</summary>
        public readonly bool Spatial;
        public readonly float MinDistance, MaxDistance;
        /// <summary>Retrigger guard: a second play of the same id inside this window is dropped (so an event
        /// and a direct call can't double up).</summary>
        public readonly float MinInterval;
        /// <summary>AudioSource.priority (0 = most important).</summary>
        public readonly int Priority;

        public SfxDef(string path, float volume, float pitchJitter = 0.03f, int variants = 1, bool spatial = false,
                      float minDistance = 2f, float maxDistance = 25f, float minInterval = 0.05f, int priority = 128,
                      float pitch = 1f)
        {
            Path = path;
            Volume = volume;
            PitchJitter = pitchJitter;
            Variants = Mathf.Max(1, variants);
            Spatial = spatial;
            MinDistance = minDistance;
            MaxDistance = maxDistance;
            MinInterval = minInterval;
            Priority = priority;
            Pitch = pitch;
        }

        public bool IsLoop => Path != null && Path.EndsWith("_loop", StringComparison.Ordinal);
    }

    /// <summary>
    /// The sound table and clip cache. Clips are loaded from <c>Resources/Audio</c> (there are no scenes or
    /// prefabs to reference them). A missing clip falls back to a synthesised one from <see cref="AudioSynth"/>
    /// (or silence), with a single warning for the whole session.
    /// </summary>
    public static class SfxLibrary
    {
        public const string SfxRoot = "Audio/Sfx/";
        public const string MusicRoot = "Audio/Music/";
        public const string CreditsPath = "Audio/CREDITS";

        /// <summary>Music loop resource names (under <see cref="MusicRoot"/>).</summary>
        public const string MusicRooms = "rooms", MusicHub = "hub", MusicTutorial = "tutorial";

        static readonly Dictionary<Sfx, SfxDef> s_Defs = new Dictionary<Sfx, SfxDef>
        {
            // Photo and camera.
            { Sfx.PhotoRaise,       new SfxDef("photo_raise", 0.55f, 0.06f, priority: 64) },
            { Sfx.PhotoLower,       new SfxDef("photo_lower", 0.42f, 0.06f, priority: 80) },
            { Sfx.PhotoPlace,       new SfxDef("photo_place", 0.85f, 0.015f, priority: 8, minInterval: 0.2f) },
            { Sfx.PhotoRewind,      new SfxDef("photo_rewind", 0.8f, 0.015f, priority: 8, minInterval: 0.2f) },
            { Sfx.PhotoRotate,      new SfxDef("photo_rotate", 0.35f, 0.08f, priority: 120, minInterval: 0.04f) },
            { Sfx.CameraShutter,    new SfxDef("camera_shutter", 0.8f, 0.02f, priority: 16, minInterval: 0.2f) },
            { Sfx.CameraEmpty,      new SfxDef("camera_empty", 0.55f, 0.04f, priority: 32, minInterval: 0.15f) },
            { Sfx.PhotoPickup,      new SfxDef("photo_pickup", 0.72f, 0.015f, priority: 24, minInterval: 0.25f) },
            { Sfx.CameraRaise,      new SfxDef("photo_raise", 0.5f, 0.04f, priority: 64, pitch: 0.86f) },

            // Teleporters.
            { Sfx.TeleporterHumLoop, new SfxDef("teleporter_hum_loop", 0.42f, 0f, spatial: true, minDistance: 1.5f, maxDistance: 12f, priority: 96) },
            { Sfx.TeleportTravel,   new SfxDef("teleport_travel", 0.75f, 0.02f, priority: 8, minInterval: 0.6f) },

            // UI (quiet, never randomised much).
            { Sfx.UiHover,          new SfxDef("ui_hover", 0.22f, 0.02f, priority: 160, minInterval: 0.04f) },
            { Sfx.UiClick,          new SfxDef("ui_click", 0.45f, 0.02f, priority: 48, minInterval: 0.04f) },

            // Ambience.
            { Sfx.AmbWindLoop,      new SfxDef("amb_wind_loop", 0.2f, 0f, priority: 200) },
            { Sfx.AmbBird,          new SfxDef("amb_bird", 0.2f, 0.1f, variants: 4, spatial: true, minDistance: 8f, maxDistance: 70f, priority: 220, minInterval: 0.3f) },

            // Devices.
            { Sfx.SwitchPress,      new SfxDef("switch_press", 0.8f, 0.03f, spatial: true, minDistance: 2.5f, maxDistance: 25f, priority: 24, minInterval: 0.08f) },
            { Sfx.SwitchRelease,    new SfxDef("switch_release", 0.65f, 0.03f, spatial: true, minDistance: 2.5f, maxDistance: 25f, priority: 32, minInterval: 0.08f) },
            { Sfx.SwitchDenied,     new SfxDef("switch_denied", 0.6f, 0.03f, spatial: true, minDistance: 2.5f, maxDistance: 20f, priority: 40, minInterval: 0.15f) },
            { Sfx.MoverLoop,        new SfxDef("mover_loop", 0.5f, 0f, spatial: true, minDistance: 3f, maxDistance: 35f, priority: 64) },
            { Sfx.MoverStop,        new SfxDef("mover_stop", 0.75f, 0.04f, spatial: true, minDistance: 3f, maxDistance: 35f, priority: 40, minInterval: 0.1f) },
            { Sfx.CollapseCrack,    new SfxDef("collapse_crack", 0.9f, 0.03f, spatial: true, minDistance: 3f, maxDistance: 40f, priority: 16, minInterval: 0.6f) },
            { Sfx.CollapseFall,     new SfxDef("collapse_fall", 0.85f, 0.03f, spatial: true, minDistance: 4f, maxDistance: 60f, priority: 16, minInterval: 0.6f) },
            { Sfx.HatchRise,        new SfxDef("hatch_rise", 0.85f, 0.02f, spatial: true, minDistance: 4f, maxDistance: 45f, priority: 24, minInterval: 1f) },
            { Sfx.ExhibitWake,      new SfxDef("exhibit_wake", 0.65f, 0.01f, spatial: true, minDistance: 3f, maxDistance: 30f, priority: 32, minInterval: 0.3f) },
            { Sfx.DeviceWake,       new SfxDef("exhibit_wake", 0.5f, 0.01f, spatial: true, minDistance: 2.5f, maxDistance: 25f, priority: 40, minInterval: 0.3f, pitch: 1.335f) }, // G# -> C#, still in key

            // Rewind, falls and checkpoints.
            { Sfx.FallWhoosh,       new SfxDef("fall_whoosh", 0.6f, 0.04f, priority: 24, minInterval: 1f) },
            { Sfx.LimboDroneLoop,   new SfxDef("limbo_drone_loop", 0.55f, 0f, priority: 32) },
            { Sfx.RewindNothing,    new SfxDef("rewind_nothing", 0.55f, 0.01f, priority: 24, minInterval: 0.12f) },
            { Sfx.RewindCheckpoint, new SfxDef("rewind_checkpoint", 0.9f, 0f, priority: 4, minInterval: 0.3f) },
            { Sfx.CheckpointSet,    new SfxDef("checkpoint_set", 0.6f, 0f, priority: 40, minInterval: 1f) },

            // Body.
            { Sfx.Land,             new SfxDef("land", 0.6f, 0.06f, priority: 100, minInterval: 0.1f) },
            { Sfx.FootstepTile,     new SfxDef("Footsteps/step_tile", 0.3f, 0.07f, variants: 5, priority: 180, minInterval: 0.1f) },
            { Sfx.FootstepStone,    new SfxDef("Footsteps/step_stone", 0.32f, 0.07f, variants: 5, priority: 180, minInterval: 0.1f) },
            { Sfx.FootstepWood,     new SfxDef("Footsteps/step_wood", 0.32f, 0.07f, variants: 5, priority: 180, minInterval: 0.1f) },
            { Sfx.FootstepGrass,    new SfxDef("Footsteps/step_grass", 0.36f, 0.07f, variants: 5, priority: 180, minInterval: 0.1f) },
        };

        static readonly Dictionary<string, AudioClip> s_Clips = new Dictionary<string, AudioClip>();
        static readonly HashSet<string> s_Missing = new HashSet<string>();
        static bool s_Warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Clips.Clear();
            s_Missing.Clear();
            s_Warned = false;
        }

        /// <summary>Every defined id (tests, preloading).</summary>
        public static IEnumerable<Sfx> All => s_Defs.Keys;

        public static bool TryGet(Sfx id, out SfxDef def) => s_Defs.TryGetValue(id, out def);

        /// <summary>Resource path (under Resources/) of one variant.</summary>
        public static string ResourcePath(in SfxDef def, int variant) =>
            SfxRoot + (def.Variants > 1 ? def.Path + "_" + Mathf.Clamp(variant, 0, def.Variants - 1) : def.Path);

        /// <summary>Every SFX resource path (tests).</summary>
        public static IEnumerable<string> AllResourcePaths()
        {
            var seen = new HashSet<string>();
            foreach (var kv in s_Defs)
                for (int v = 0; v < kv.Value.Variants; v++)
                {
                    string p = ResourcePath(kv.Value, v);
                    if (seen.Add(p)) yield return p;
                }
        }

        /// <summary>Clip paths that were missing this session (diagnostics, tests).</summary>
        public static IReadOnlyCollection<string> Missing => s_Missing;

        /// <summary>The clip for one variant of <paramref name="id"/>; a synthesised stand-in or null when missing.</summary>
        public static AudioClip Clip(Sfx id, int variant)
        {
            if (!s_Defs.TryGetValue(id, out SfxDef def)) return null;
            string path = ResourcePath(def, variant);
            if (s_Clips.TryGetValue(path, out AudioClip clip)) return clip;

            clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                s_Missing.Add(path);
                if (!s_Warned)
                {
                    s_Warned = true;
                    Debug.LogWarning("[IonAudio] Missing audio clip Resources/" + path +
                                     " (and possibly others: see SfxLibrary.Missing). Using synthesised stand-ins. " +
                                     "Run tools/audio/ionsfx.py.");
                }
                clip = Synthesize(id, variant);
            }
            s_Clips[path] = clip;
            return clip;
        }

        /// <summary>A music loop clip (null when missing; one warning).</summary>
        public static AudioClip Music(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string path = MusicRoot + name;
            if (s_Clips.TryGetValue(path, out AudioClip clip)) return clip;
            clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                s_Missing.Add(path);
                if (!s_Warned)
                {
                    s_Warned = true;
                    Debug.LogWarning("[IonAudio] Missing music Resources/" + path + ". Run tools/audio/ionmusic.py.");
                }
            }
            s_Clips[path] = clip;
            return clip;
        }

        /// <summary>True if the music clip exists in Resources (without loading its sample data).</summary>
        public static bool HasMusic(string name) => Music(name) != null;

        // ------------------------------------------------------------------ fallback synthesis

        static AudioClip Synthesize(Sfx id, int variant)
        {
            float[] data;
            uint seed = 977u + (uint)id * 7919u + (uint)variant * 104729u;
            switch (id)
            {
                case Sfx.PhotoRaise:
                case Sfx.PhotoLower:
                case Sfx.CameraRaise:
                case Sfx.PhotoRotate:
                    data = AudioSynth.PaperRustle(seed); break;
                case Sfx.PhotoPlace:
                    data = AudioSynth.PlaceWhooshChime(seed); break;
                case Sfx.PhotoRewind:
                case Sfx.RewindCheckpoint:
                    data = AudioSynth.RewindReverse(seed); break;
                case Sfx.PhotoPickup:
                case Sfx.CheckpointSet:
                case Sfx.ExhibitWake:
                case Sfx.DeviceWake:
                    data = AudioSynth.PickupChime(); break;
                case Sfx.CameraShutter:
                    data = AudioSynth.ShutterWhirr(seed); break;
                case Sfx.TeleportTravel:
                    data = AudioSynth.TeleportShimmer(seed); break;
                case Sfx.AmbWindLoop:
                    data = AudioSynth.WindLoop(6f, seed); break;
                case Sfx.AmbBird:
                    data = AudioSynth.BirdChirp(seed); break;
                case Sfx.Land:
                case Sfx.FootstepTile:
                case Sfx.FootstepStone:
                case Sfx.FootstepWood:
                case Sfx.FootstepGrass:
                case Sfx.SwitchPress:
                case Sfx.SwitchRelease:
                case Sfx.SwitchDenied:
                case Sfx.MoverStop:
                case Sfx.CameraEmpty:
                case Sfx.UiClick:
                    data = AudioSynth.Footstep(seed); break;
                default:
                    return null; // silent-safe: loops, drones and the rest just don't sound
            }
            var clip = AudioClip.Create("IonSynth_" + id + "_" + variant, data.Length, 1, AudioSynth.SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
