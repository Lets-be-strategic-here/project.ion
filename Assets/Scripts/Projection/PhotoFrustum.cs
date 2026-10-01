using UnityEngine;

namespace Ion.Projection
{
    /// <summary>
    /// World-space view volume of a photo: a perspective frustum with its apex at Pose.position,
    /// looking along Pose.rotation * forward.
    /// </summary>
    public struct PhotoFrustum
    {
        public Pose Pose;
        /// <summary>Vertical field of view in degrees.</summary>
        public float FovY;
        /// <summary>Width / height.</summary>
        public float Aspect;
        public float Near;
        public float Far;

        public const int PlaneCount = 6;

        /// <summary>
        /// The 6 bounding planes with normals pointing INTO the volume, in the same order as
        /// GeometryUtility.CalculateFrustumPlanes: left, right, bottom, top, near, far.
        /// Allocates a new array; use <see cref="GetPlanes"/> to fill an existing one.
        /// </summary>
        public Plane[] Planes()
        {
            var planes = new Plane[PlaneCount];
            GetPlanes(planes);
            return planes;
        }

        /// <summary>Fills <paramref name="planes"/> (length >= 6) without allocating.</summary>
        public void GetPlanes(Plane[] planes)
        {
            Quaternion rot = Pose.rotation;
            Vector3 apex = Pose.position;
            Vector3 fwd = rot * Vector3.forward;
            Vector3 right = rot * Vector3.right;
            Vector3 up = rot * Vector3.up;
            float tanY = Mathf.Tan(0.5f * FovY * Mathf.Deg2Rad);
            float tanX = tanY * Aspect;

            // A side plane through the apex containing the edge direction (fwd - tanX*right) has
            // inward normal (right + tanX*fwd); likewise for the others.
            planes[0] = new Plane((right + tanX * fwd).normalized, apex);   // left
            planes[1] = new Plane((-right + tanX * fwd).normalized, apex);  // right
            planes[2] = new Plane((up + tanY * fwd).normalized, apex);      // bottom
            planes[3] = new Plane((-up + tanY * fwd).normalized, apex);     // top
            planes[4] = new Plane(fwd, apex + fwd * Near);                  // near
            planes[5] = new Plane(-fwd, apex + fwd * Far);                  // far
        }

        /// <summary>True if the point lies inside (or on the boundary of) the volume.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = Quaternion.Inverse(Pose.rotation) * (worldPoint - Pose.position);
            float z = local.z;
            if (z < Near || z > Far) return false;
            float tanY = Mathf.Tan(0.5f * FovY * Mathf.Deg2Rad);
            float tanX = tanY * Aspect;
            const float eps = 1e-5f;
            return Mathf.Abs(local.x) <= z * tanX + eps && Mathf.Abs(local.y) <= z * tanY + eps;
        }

        /// <summary>Rotation of a camera rolled by <paramref name="rollDegrees"/> about its own forward axis.
        /// Positive roll turns the photo counter-clockwise on screen (same sense as a RectTransform's
        /// positive Z rotation).</summary>
        public static Quaternion RolledRotation(Quaternion cameraRotation, float rollDegrees)
        {
            return cameraRotation * Quaternion.AngleAxis(rollDegrees, Vector3.forward);
        }

        public static PhotoFrustum FromCamera(Camera cam, float fovY, float aspect, float rollDegrees, float near, float far)
        {
            Transform t = cam.transform;
            return new PhotoFrustum
            {
                Pose = new Pose(t.position, RolledRotation(t.rotation, rollDegrees)),
                FovY = fovY,
                Aspect = aspect,
                Near = near,
                Far = far,
            };
        }
    }
}
