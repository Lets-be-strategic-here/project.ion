using System.Collections.Generic;
using UnityEngine;

namespace Ion.Projection
{
    /// <summary>
    /// Marker for world geometry that may be cut by a photo placement and captured into a photo.
    /// Requires a MeshFilter (with a readable, closed, convex mesh, like every Geo primitive) and a
    /// MeshRenderer on the same GameObject. Open meshes are not supported: caps and the sliver filter
    /// (which measures volume) assume a closed surface.
    /// A Sliceable whose component is disabled is treated as hidden (the projection system
    /// disables it when the object has been replaced by its cut pieces).
    ///
    /// While playing, enabled Sliceables keep themselves in a registry (<see cref="CopyLive"/>), so a
    /// placement does not have to search the whole scene (FindObjectsByType was ~2 ms of a placement on
    /// a slow laptop). Edit mode (tests) does not run OnEnable, so callers fall back to a scene search
    /// there. The MeshFilter / MeshRenderer and "inside an Interactable" lookups are cached per instance.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Sliceable : MonoBehaviour
    {
        static readonly List<Sliceable> s_live = new List<Sliceable>(1024);
        int _index = -1;

        // Lazily resolved component cache (never serialized, so Instantiate() clones start empty).
        [System.NonSerialized] internal MeshFilter CachedFilter;
        [System.NonSerialized] internal MeshRenderer CachedRenderer;
        /// <summary>-1 unknown, 0 no, 1 yes: the object sits inside an Interactable (cloned whole, never cut).</summary>
        [System.NonSerialized] internal sbyte InInteractable = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_live.Clear();

        /// <summary>True when the registry is maintained (play mode); otherwise search the scene.</summary>
        internal static bool RegistryActive => Application.isPlaying;

        /// <summary>Copies the enabled Sliceables into <paramref name="into"/> (a snapshot: cutting adds new ones).</summary>
        internal static void CopyLive(List<Sliceable> into)
        {
            into.Clear();
            into.AddRange(s_live);
        }

        void OnEnable()
        {
            if (_index >= 0) return;
            _index = s_live.Count;
            s_live.Add(this);
        }

        void OnDisable()
        {
            int i = _index;
            if (i < 0) return;
            _index = -1;
            int last = s_live.Count - 1;
            if (i > last || s_live[i] != this) { s_live.Remove(this); return; }
            if (i != last)
            {
                Sliceable moved = s_live[last];
                s_live[i] = moved;
                moved._index = i;
            }
            s_live.RemoveAt(last);
        }
    }
}
