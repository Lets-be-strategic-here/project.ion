using UnityEngine;

namespace Ion.Projection
{
    /// <summary>
    /// Describes a merged mesh (one submesh) as a list of independent closed elements, each a contiguous
    /// vertex range plus a contiguous index range, with its local-space bounds. Written by the decor
    /// combiner, by every cut (a cut object's pieces are merged into one mesh, one element per piece) and
    /// by photo capture (a photo's pieces are merged per material). <see cref="ProjectionSystem"/> uses it
    /// to cut merged meshes quickly: elements whose bounds miss the photo frustum are kept untouched,
    /// elements inside are dropped, and only the few that straddle the frustum go through the plane
    /// clipper. Only valid while <see cref="Mesh"/> is the object's mesh.
    ///
    /// The mesh's vertex buffer may also hold vertices no element references (left over from elements a
    /// cut removed: kept elements keep their vertex indices, so a cut never re-indexes them). Optional
    /// CPU copies of the vertex / index data (<see cref="Positions"/> ...) save a GPU-to-CPU readback per
    /// cut; they are shared between meshes and never modified.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MeshElements : MonoBehaviour
    {
        public Mesh Mesh;
        public int[] VertexStart, VertexCount, IndexStart, IndexCount;
        public Bounds[] Bounds;

        [System.NonSerialized] internal Vector3[] Positions, Normals;
        [System.NonSerialized] internal Color32[] Colors;
        [System.NonSerialized] internal int[] Indices;

        public int Count => Bounds != null ? Bounds.Length : 0;

        public void Set(Mesh mesh, int[] vertexStart, int[] vertexCount, int[] indexStart, int[] indexCount, Bounds[] bounds)
        {
            Mesh = mesh;
            VertexStart = vertexStart;
            VertexCount = vertexCount;
            IndexStart = indexStart;
            IndexCount = indexCount;
            Bounds = bounds;
            Positions = Normals = null;
            Colors = null;
            Indices = null;
        }

        /// <summary>Attaches CPU copies of the mesh data (positions and indices required; normals / colours may be null).</summary>
        internal void SetData(Vector3[] positions, Vector3[] normals, Color32[] colors, int[] indices)
        {
            Positions = positions;
            Normals = normals;
            Colors = colors;
            Indices = indices;
        }

        /// <summary>Makes sure the CPU copies exist (reads them back from a readable mesh once). False if impossible.</summary>
        internal bool EnsureData()
        {
            if (Mesh == null) return false;
            int vc = Mesh.vertexCount;
            if (Positions != null && Positions.Length == vc && Indices != null) return true;
            if (!Mesh.isReadable || Mesh.subMeshCount != 1) return false;
            Positions = Mesh.vertices;
            Vector3[] n = Mesh.normals;
            Normals = n != null && n.Length == vc ? n : null;
            Color32[] c = Mesh.colors32;
            Colors = c != null && c.Length == vc ? c : null;
            Indices = Mesh.GetIndices(0);
            return true;
        }
    }
}
