using Debug = UnityEngine.Debug;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Unreal-style debug drawing: wireframe boxes, spheres, and capsules with
    /// a screen lifetime, rendered through <see cref="Debug.DrawLine"/> so they
    /// appear in both the Scene and Game views. All entry points are compiled
    /// out of player builds, making calls safe to leave in shipped code.
    /// </summary>
    public static class AbilityDebugDraw
    {
        private const int SphereSegments = 24;

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Sphere(Vector3 center, float radius, Color color, float duration = 1f)
        {
            if (radius <= Mathf.Epsilon || duration <= 0f)
            {
                return;
            }

            DrawCircle(center, radius, Vector3.right, Vector3.up, color, duration);
            DrawCircle(center, radius, Vector3.right, Vector3.forward, color, duration);
            DrawCircle(center, radius, Vector3.up, Vector3.forward, color, duration);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Box(
            Vector3 center,
            Vector3 halfExtents,
            Quaternion rotation,
            Color color,
            float duration = 1f)
        {
            if (halfExtents.sqrMagnitude <= Mathf.Epsilon || duration <= 0f)
            {
                return;
            }

            Vector3[] corners = ComputeBoxCorners(center, halfExtents, rotation);
            int[] edges =
            {
                0, 1, 1, 3, 3, 2, 2, 0,
                4, 5, 5, 7, 7, 6, 6, 4,
                0, 4, 1, 5, 2, 6, 3, 7,
            };

            for (int i = 0; i < edges.Length; i += 2)
            {
                Debug.DrawLine(corners[edges[i]], corners[edges[i + 1]], color, duration);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Capsule(
            Vector3 start,
            Vector3 end,
            float radius,
            Color color,
            float duration = 1f)
        {
            if (radius <= Mathf.Epsilon || duration <= 0f)
            {
                return;
            }

            Sphere(start, radius, color, duration);
            Sphere(end, radius, color, duration);
            Vector3 axis = end - start;
            if (axis.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            Vector3 direction = axis.normalized;
            Vector3 side = Vector3.Cross(direction, Vector3.up);
            if (side.sqrMagnitude <= Mathf.Epsilon)
            {
                side = Vector3.Cross(direction, Vector3.right);
            }

            side = side.normalized * radius;
            Vector3 forward = Vector3.Cross(direction, side).normalized * radius;
            Debug.DrawLine(start + side, end + side, color, duration);
            Debug.DrawLine(start - side, end - side, color, duration);
            Debug.DrawLine(start + forward, end + forward, color, duration);
            Debug.DrawLine(start - forward, end - forward, color, duration);
        }

        /// <summary>
        /// Corner order: bottom face (-Y) 0..3, top face (+Y) 4..7, each face
        /// wound as (-X,-Z), (+X,-Z), (-X,+Z), (+X,+Z) in local space.
        /// </summary>
        public static Vector3[] ComputeBoxCorners(
            Vector3 center,
            Vector3 halfExtents,
            Quaternion rotation)
        {
            return new[]
            {
                center + rotation * new Vector3(-halfExtents.x, -halfExtents.y, -halfExtents.z),
                center + rotation * new Vector3(halfExtents.x, -halfExtents.y, -halfExtents.z),
                center + rotation * new Vector3(-halfExtents.x, -halfExtents.y, halfExtents.z),
                center + rotation * new Vector3(halfExtents.x, -halfExtents.y, halfExtents.z),
                center + rotation * new Vector3(-halfExtents.x, halfExtents.y, -halfExtents.z),
                center + rotation * new Vector3(halfExtents.x, halfExtents.y, -halfExtents.z),
                center + rotation * new Vector3(-halfExtents.x, halfExtents.y, halfExtents.z),
                center + rotation * new Vector3(halfExtents.x, halfExtents.y, halfExtents.z),
            };
        }

        private static void DrawCircle(
            Vector3 center,
            float radius,
            Vector3 right,
            Vector3 up,
            Color color,
            float duration)
        {
            Vector3 previous = center + right * radius;
            for (int i = 1; i <= SphereSegments; i++)
            {
                float angle = i / (float)SphereSegments * Mathf.PI * 2f;
                Vector3 next =
                    center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
                Debug.DrawLine(previous, next, color, duration);
                previous = next;
            }
        }
    }
}
