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

        /// <summary>
        /// Master switch for all debug drawing (default on). Game code can
        /// bind it to a debug key so a play session goes fully clean, like
        /// Unreal's debug toggles. Compiled out of player builds together
        /// with every draw call.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Draws a <see cref="HitShape"/> exactly where it would query. Every
        /// on-screen representation of a shape goes through here — the debug
        /// draw effect, the runtime overlay and the editor gizmos — so what is
        /// drawn and what is queried can never drift apart.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Shape(
            in HitShape shape,
            Transform owner,
            Vector3 aimPoint,
            Vector3 direction,
            Color color,
            float duration = 1f)
        {
            if (owner == null)
            {
                return;
            }

            Vector3 center = shape.ResolveCenter(owner, aimPoint);
            switch (shape.Kind)
            {
                case HitShapeKind.Box:
                    Box(center, shape.HalfExtents, owner.rotation, color, duration);
                    break;
                case HitShapeKind.Capsule:
                    Capsule(
                        center,
                        shape.ResolveEnd(owner, aimPoint, direction),
                        shape.Radius,
                        color,
                        duration);
                    break;
                case HitShapeKind.Cone:
                    Cone(
                        center,
                        ResolveFacing(owner, direction),
                        shape.Radius,
                        shape.ConeHalfAngle,
                        shape.ConeInnerRadius,
                        color,
                        duration);
                    break;
                case HitShapeKind.Ray:
                    Line(
                        center,
                        shape.ResolveEnd(owner, aimPoint, direction),
                        color,
                        duration);
                    break;
                default:
                    Sphere(center, shape.Radius, color, duration);
                    break;
            }
        }

        /// <summary>
        /// A ground-plane cone: the outer arc, its two edges, and the inner
        /// circle that matches whatever the angle.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Cone(
            Vector3 center,
            Vector3 facing,
            float radius,
            float halfAngle,
            float innerRadius,
            Color color,
            float duration = 1f)
        {
            if (!Enabled || radius <= Mathf.Epsilon || duration <= 0f)
            {
                return;
            }

            Vector3 planar = Vector3.ProjectOnPlane(facing, Vector3.up);
            planar = planar.sqrMagnitude > Mathf.Epsilon
                ? planar.normalized
                : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, planar);

            Vector3 previous = center + Rotate(planar, side, -halfAngle) * radius;
            Debug.DrawLine(center, previous, color, duration);
            int segments = Mathf.Max(2, Mathf.CeilToInt(halfAngle * 2f / 10f));
            for (int i = 1; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-halfAngle, halfAngle, i / (float)segments);
                Vector3 next = center + Rotate(planar, side, angle) * radius;
                Debug.DrawLine(previous, next, color, duration);
                previous = next;
            }

            Debug.DrawLine(previous, center, color, duration);
            if (innerRadius > Mathf.Epsilon)
            {
                DrawCircle(center, innerRadius, planar, side, color, duration);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Line(Vector3 start, Vector3 end, Color color, float duration = 1f)
        {
            if (!Enabled || duration <= 0f)
            {
                return;
            }

            Debug.DrawLine(start, end, color, duration);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Sphere(Vector3 center, float radius, Color color, float duration = 1f)
        {
            if (!Enabled || radius <= Mathf.Epsilon || duration <= 0f)
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
            if (!Enabled || halfExtents.sqrMagnitude <= Mathf.Epsilon || duration <= 0f)
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
            if (!Enabled || radius <= Mathf.Epsilon || duration <= 0f)
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

        private static Vector3 ResolveFacing(Transform owner, Vector3 direction)
        {
            return direction.sqrMagnitude > Mathf.Epsilon ? direction : owner.forward;
        }

        private static Vector3 Rotate(Vector3 forward, Vector3 side, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return forward * Mathf.Cos(radians) + side * Mathf.Sin(radians);
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
