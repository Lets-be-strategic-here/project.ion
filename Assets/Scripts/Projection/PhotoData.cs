using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Projection
{
    /// <summary>
    /// A captured photo: frustum shape, preview image and caption, plus the captured world
    /// contents (internal) that ProjectionSystem.Place pastes back into the world.
    /// Captured meshes are shared by every placement of the photo and are never modified.
    /// </summary>
    public sealed class PhotoData
    {
        public float FovY, Aspect;
        public Texture2D Preview;
        public string Label;

        internal readonly List<PhotoPiece> Pieces = new List<PhotoPiece>();
        internal readonly List<PhotoEntity> Entities = new List<PhotoEntity>();

        /// <summary>Number of captured mesh pieces (debug / UI info).</summary>
        public int PieceCount => Pieces.Count;

        /// <summary>Number of captured whole objects (Interactables).</summary>
        public int EntityCount => Entities.Count;

        /// <summary>True if the photo contains nothing (placing it only cuts the world).</summary>
        public bool IsEmpty => Pieces.Count == 0 && Entities.Count == 0;
    }

    /// <summary>A clipped mesh and how to draw it, positioned relative to the capture pose.</summary>
    internal sealed class PhotoPiece
    {
        public string Name;
        public Mesh Mesh;
        public Material[] Materials;
        /// <summary>captureFrame⁻¹ * pieceLocalToWorld at capture time.</summary>
        public Matrix4x4 Relative;
        public int Layer;
        public ShadowCastingMode ShadowCasting;
        public bool ReceiveShadows;
    }

    /// <summary>A captured Interactable: an inactive template clone and its pose relative to the capture pose.</summary>
    internal sealed class PhotoEntity
    {
        public GameObject Template;
        public Vector3 RelativePosition;
        public Quaternion RelativeRotation;
        public Vector3 WorldScale;
    }
}
