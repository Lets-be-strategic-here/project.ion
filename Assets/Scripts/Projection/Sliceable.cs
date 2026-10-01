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
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Sliceable : MonoBehaviour { }
}
