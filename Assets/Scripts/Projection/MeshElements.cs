using UnityEngine;

namespace Ion.Projection
{
    /// <summary>
    /// Describes a merged mesh (one submesh) as a list of independent closed elements, each a contiguous
    /// vertex range plus a contiguous index range, with its local-space bounds. Written by the decor
    /// combiner. <see cref="ProjectionSystem"/> uses it to cut merged decor quickly: elements whose bounds
    /// miss the photo frustum are copied untouched, elements inside are dropped, and only the few that
    /// straddle the frustum go through the plane clipper. Only valid while <see cref="Mesh"/> is the
    /// object's mesh.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MeshElements : MonoBehaviour
    {
        public Mesh Mesh;
        public int[] VertexStart, VertexCount, IndexStart, IndexCount;
        public Bounds[] Bounds;

        public int Count => Bounds != null ? Bounds.Length : 0;

        public void Set(Mesh mesh, int[] vertexStart, int[] vertexCount, int[] indexStart, int[] indexCount, Bounds[] bounds)
        {
            Mesh = mesh;
            VertexStart = vertexStart;
            VertexCount = vertexCount;
            IndexStart = indexStart;
            IndexCount = indexCount;
            Bounds = bounds;
        }
    }
}
