using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Projection
{
    /// <summary>
    /// Accumulates closed mesh pieces (one submesh) into one mesh plus its <see cref="MeshElements"/>
    /// table: one element per piece. Used by placement (all cut pieces of one object become one object)
    /// and capture (a photo's pieces are merged per material). Fewer GameObjects, renderers, Mesh objects
    /// and collider cookings per placement; later cuts of the result stay fast (element-wise).
    ///
    /// <see cref="BeginShared"/> starts from an existing vertex buffer (kept elements keep their vertex
    /// indices; nothing is copied until a new piece is appended). Not thread-safe; reuse instances.
    /// </summary>
    internal sealed class PieceMerger
    {
        Vector3[] _p = new Vector3[512], _n = new Vector3[512];
        Color32[] _c = new Color32[512];
        int[] _t = new int[1024];
        int _vc, _tc;
        bool _colors, _missingNormals;

        // Shared source buffer (BeginShared), referenced until the first appended piece.
        Vector3[] _sp, _sn;
        Color32[] _sc;
        bool _shared;

        readonly List<int> _v0 = new List<int>(32), _vn = new List<int>(32), _t0 = new List<int>(32), _tn = new List<int>(32);
        readonly List<Bounds> _b = new List<Bounds>(32);
        Bounds _total;

        public int ElementCount => _b.Count;
        public int VertexCount => _shared ? _sp.Length : _vc;
        public Bounds TotalBounds => _total;

        /// <summary>Pre-sizes the buffers (no reallocation below these counts).</summary>
        public void Reserve(int vertices, int indices)
        {
            EnsureVertices(vertices);
            EnsureIndices(indices);
        }

        public void Reset()
        {
            _vc = 0;
            _tc = 0;
            _colors = false;
            _missingNormals = false;
            _shared = false;
            _sp = _sn = null;
            _sc = null;
            _v0.Clear(); _vn.Clear(); _t0.Clear(); _tn.Clear(); _b.Clear();
            _total = default;
        }

        /// <summary>Starts from an existing vertex buffer (normals / colours may be null). Elements are then added with <see cref="AddExistingElement"/>.</summary>
        public void BeginShared(Vector3[] positions, Vector3[] normals, Color32[] colors)
        {
            Reset();
            _shared = true;
            _sp = positions;
            _sn = normals;
            _sc = colors;
            _colors = colors != null;
            _missingNormals = normals == null;
        }

        /// <summary>Adds an element of the shared buffer (its indices are copied as they are).</summary>
        public void AddExistingElement(int[] indices, int v0, int vn, int t0, int tn, Bounds bounds)
        {
            EnsureIndices(_tc + tn);
            System.Array.Copy(indices, t0, _t, _tc, tn);
            AddElement(v0, vn, _tc, tn, bounds);
            _tc += tn;
        }

        /// <summary>Appends a piece (positions, normals, colours or an empty list, indices into the piece).</summary>
        public void AddPiece(List<Vector3> p, List<Vector3> n, List<Color32> c, List<int> t, Bounds bounds)
        {
            Unshare();
            int count = p.Count;
            EnsureVertices(_vc + count);
            EnsureIndices(_tc + t.Count);
            bool hasN = n != null && n.Count == count;
            bool hasC = c != null && c.Count == count;
            if (hasC && !_colors) StartColors();
            for (int i = 0; i < count; i++)
            {
                _p[_vc + i] = p[i];
                _n[_vc + i] = hasN ? n[i] : Vector3.zero;
            }
            if (_colors)
            {
                if (hasC) for (int i = 0; i < count; i++) _c[_vc + i] = c[i];
                else for (int i = 0; i < count; i++) _c[_vc + i] = new Color32(255, 255, 255, 255);
            }
            if (!hasN) _missingNormals = true;
            for (int i = 0; i < t.Count; i++) _t[_tc + i] = t[i] + _vc;
            AddElement(_vc, count, _tc, t.Count, bounds);
            _vc += count;
            _tc += t.Count;
        }

        /// <summary>Appends a piece given as arrays transformed by <paramref name="m"/> (normals by its inverse transpose).</summary>
        public void AddTransformed(Vector3[] p, Vector3[] n, Color32[] c, int[] t, Matrix4x4 m)
        {
            Unshare();
            int count = p.Length;
            EnsureVertices(_vc + count);
            EnsureIndices(_tc + t.Length);
            bool hasN = n != null && n.Length == count;
            bool hasC = c != null && c.Length == count;
            if (hasC && !_colors) StartColors();
            Matrix4x4 nm = m.inverse.transpose;
            bool mirrored = m.determinant < 0f;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = m.MultiplyPoint3x4(p[i]);
                _p[_vc + i] = v;
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
                _n[_vc + i] = hasN ? nm.MultiplyVector(n[i]).normalized : Vector3.zero;
            }
            if (_colors)
            {
                if (hasC) System.Array.Copy(c, 0, _c, _vc, count);
                else for (int i = 0; i < count; i++) _c[_vc + i] = new Color32(255, 255, 255, 255);
            }
            if (!hasN) _missingNormals = true;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                _t[_tc + i] = t[i] + _vc;
                _t[_tc + i + 1] = t[mirrored ? i + 2 : i + 1] + _vc;
                _t[_tc + i + 2] = t[mirrored ? i + 1 : i + 2] + _vc;
            }
            var b = new Bounds();
            b.SetMinMax(min, max);
            AddElement(_vc, count, _tc, t.Length - t.Length % 3, b);
            _vc += count;
            _tc += t.Length - t.Length % 3;
        }

        /// <summary>
        /// Builds the mesh (readable; bounds = union of the elements) and its exact CPU data. Returns null
        /// when there is nothing. The caller attaches the element table with <see cref="ApplyTo"/>.
        /// </summary>
        public Mesh Build(string name, out Vector3[] positions, out Vector3[] normals, out Color32[] colors, out int[] indices)
        {
            positions = normals = null;
            colors = null;
            indices = null;
            if (_b.Count == 0 || _tc == 0) return null;

            if (_shared)
            {
                positions = _sp;
                normals = _sn;
                colors = _sc;
            }
            else
            {
                positions = new Vector3[_vc];
                System.Array.Copy(_p, positions, _vc);
                if (!_missingNormals)
                {
                    normals = new Vector3[_vc];
                    System.Array.Copy(_n, normals, _vc);
                }
                if (_colors)
                {
                    colors = new Color32[_vc];
                    System.Array.Copy(_c, colors, _vc);
                }
            }
            indices = new int[_tc];
            System.Array.Copy(_t, indices, _tc);

            var mesh = new Mesh { name = name };
            if (positions.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            if (normals != null) mesh.SetNormals(normals);
            if (colors != null) mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            if (normals == null)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }
            mesh.bounds = _total;
            return mesh;
        }

        /// <summary>Writes the element table (and the CPU data from <see cref="Build"/>) into <paramref name="el"/>.</summary>
        public void ApplyTo(MeshElements el, Mesh mesh, Vector3[] positions, Vector3[] normals, Color32[] colors, int[] indices)
        {
            el.Set(mesh, _v0.ToArray(), _vn.ToArray(), _t0.ToArray(), _tn.ToArray(), _b.ToArray());
            el.SetData(positions, normals, colors, indices);
        }

        /// <summary>The element table as arrays (for data that is attached later, e.g. photo pieces).</summary>
        public void GetTable(out int[] v0, out int[] vn, out int[] t0, out int[] tn, out Bounds[] bounds)
        {
            v0 = _v0.ToArray();
            vn = _vn.ToArray();
            t0 = _t0.ToArray();
            tn = _tn.ToArray();
            bounds = _b.ToArray();
        }

        // ------------------------------------------------------------------ internals

        void AddElement(int v0, int vn, int t0, int tn, Bounds b)
        {
            if (_b.Count == 0) _total = b;
            else _total.Encapsulate(b);
            _v0.Add(v0);
            _vn.Add(vn);
            _t0.Add(t0);
            _tn.Add(tn);
            _b.Add(b);
        }

        /// <summary>Copies the shared source buffer into the own buffers before anything is appended.</summary>
        void Unshare()
        {
            if (!_shared) return;
            _shared = false;
            int count = _sp.Length;
            EnsureVertices(count);
            System.Array.Copy(_sp, _p, count);
            if (_sn != null) System.Array.Copy(_sn, _n, count);
            else System.Array.Clear(_n, 0, count);
            if (_sc != null) System.Array.Copy(_sc, _c, count);
            _vc = count;
            _sp = _sn = null;
            _sc = null;
        }

        void StartColors()
        {
            _colors = true;
            EnsureVertices(_vc);
            for (int i = 0; i < _vc; i++) _c[i] = new Color32(255, 255, 255, 255);
        }

        void EnsureVertices(int count)
        {
            if (_p.Length >= count) return;
            int size = Mathf.NextPowerOfTwo(count);
            System.Array.Resize(ref _p, size);
            System.Array.Resize(ref _n, size);
            System.Array.Resize(ref _c, size);
        }

        void EnsureIndices(int count)
        {
            if (_t.Length >= count) return;
            System.Array.Resize(ref _t, Mathf.NextPowerOfTwo(count));
        }
    }
}
