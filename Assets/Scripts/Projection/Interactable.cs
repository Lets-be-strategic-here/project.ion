using UnityEngine;

namespace Ion.Projection
{
    /// <summary>
    /// Marker for gameplay objects (pickups, teleporter, ...). They are never cut: a photo
    /// captures or removes them whole, depending on whether transform.position is inside the
    /// photo frustum. Sliceables parented under an Interactable are ignored by the cutter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Interactable : MonoBehaviour { }
}
