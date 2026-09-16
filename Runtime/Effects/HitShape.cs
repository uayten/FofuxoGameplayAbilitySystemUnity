using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// The volume a hit is tested with.
    /// </summary>
    public enum HitShapeKind
    {
        Sphere,
        Box,
        Capsule,

        /// <summary>
        /// A forward cone around the query direction, plus an inner radius that
        /// matches regardless of the angle. This is the form target assist uses.
        /// </summary>
        Cone,

        /// <summary>
        /// A thin line probe from the shape centre along the local axis, or
        /// along the query direction when the axis is empty.
        /// </summary>
        Ray
    }

    /// <summary>
    /// Where the shape is anchored. Owner-local follows the attacker; aim point
    /// places the shape where the activation context pointed, which is what a
    /// ground-targeted area attack needs.
    /// </summary>
    public enum HitShapeOrigin
    {
        OwnerLocal,
        AbilityAimPoint
    }

    /// <summary>
    /// One collider the geometry found, with whatever surface information the
    /// query could recover. Overlap forms have no true contact normal, so
    /// <see cref="Normal"/> points from the shape centre toward the closest
    /// point; the ray form carries the real surface normal.
    /// </summary>
    public readonly struct HitShapeHit
    {
        public HitShapeHit(Collider collider, Vector3 point, Vector3 normal)
        {
            Collider = collider;
            Point = point;
            Normal = normal;
        }

        public Collider Collider { get; }
        public Vector3 Point { get; }
        public Vector3 Normal { get; }
    }

    /// <summary>
    /// The volume an effect queries for receivers, separated from what the
    /// effect then does to them. Keeping the two apart is why one damage effect
    /// can cover every shape instead of one near-identical class per shape, and
    /// why damage, physics force, debug drawing and the editor gizmos all read
    /// the same geometry instead of each carrying a copy of it.
    /// </summary>
    [Serializable]
    public struct HitShape : IEquatable<HitShape>
    {
        private const int ScratchCapacity = 64;
        private static readonly Collider[] ColliderScratch = new Collider[ScratchCapacity];
        private static readonly RaycastHit[] RaycastScratch = new RaycastHit[ScratchCapacity];

        [SerializeField] private HitShapeKind kind;
        [SerializeField] private HitShapeOrigin origin;
        [SerializeField] private LayerMask targetLayers;
        [Tooltip("Sphere, box and cone centre, capsule and ray start. Local to the owner.")]
        [SerializeField] private Vector3 localCenter;
        [Tooltip("Capsule and ray end, local to the owner. Ignored by the other shapes.")]
        [SerializeField] private Vector3 localEnd;
        [Tooltip("Box half extents. Ignored by the other shapes.")]
        [SerializeField] private Vector3 halfExtents;
        [Tooltip("Sphere, capsule and cone radius. Ignored by the box and the ray.")]
        [SerializeField, Min(0.05f)] private float radius;
        [Tooltip("Cone half angle around the query direction. Ignored by the other shapes.")]
        [SerializeField, Range(0f, 180f)] private float coneHalfAngle;
        [Tooltip("Cone inner radius: candidates this close match whatever the angle. Ignored by the other shapes.")]
        [SerializeField, Min(0f)] private float coneInnerRadius;

        public HitShapeKind Kind => kind;
        public HitShapeOrigin Origin => origin;
        public int TargetLayerMask => targetLayers.value;
        public Vector3 LocalCenter => localCenter;
        public Vector3 LocalEnd => localEnd;
        public Vector3 HalfExtents => halfExtents;
        public float Radius => Mathf.Max(0.05f, radius);
        public float ConeHalfAngle => Mathf.Clamp(coneHalfAngle, 0f, 180f);
        public float ConeInnerRadius => Mathf.Max(0f, coneInnerRadius);

        public static HitShape Sphere(Vector3 localCenter, float radius)
        {
            return new HitShape
            {
                kind = HitShapeKind.Sphere,
                localCenter = localCenter,
                radius = radius,
                halfExtents = Vector3.one,
            };
        }

        /// <summary>
        /// The target-assist form: everything inside <paramref name="innerRadius"/>
        /// matches, everything else has to sit inside the cone.
        /// </summary>
        public static HitShape Cone(
            int layerMask,
            float radius,
            float halfAngle,
            float innerRadius)
        {
            return new HitShape
            {
                kind = HitShapeKind.Cone,
                origin = HitShapeOrigin.OwnerLocal,
                targetLayers = layerMask,
                radius = radius,
                coneHalfAngle = halfAngle,
                coneInnerRadius = innerRadius,
                halfExtents = Vector3.one,
            };
        }

        public static HitShape Ray(int layerMask, Vector3 localStart, Vector3 localEnd)
        {
            return new HitShape
            {
                kind = HitShapeKind.Ray,
                origin = HitShapeOrigin.OwnerLocal,
                targetLayers = layerMask,
                localCenter = localStart,
                localEnd = localEnd,
                radius = 0.05f,
                halfExtents = Vector3.one,
            };
        }

        /// <summary>
        /// The same volume anchored somewhere else. Shapes are authored in the
        /// Inspector; this is how code builds one without a second constructor
        /// per anchor.
        /// </summary>
        public HitShape WithOrigin(HitShapeOrigin shapeOrigin)
        {
            HitShape shape = this;
            shape.origin = shapeOrigin;
            return shape;
        }

        /// <summary>The same volume, restricted to these layers.</summary>
        public HitShape WithLayers(int layerMask)
        {
            HitShape shape = this;
            shape.targetLayers = layerMask;
            return shape;
        }

        /// <summary>
        /// Anchor of the query in world space, and the point damage falloff and
        /// radial knockback are measured from.
        /// </summary>
        public Vector3 ResolveCenter(Transform owner, Vector3 aimPoint)
        {
            if (owner == null)
            {
                return aimPoint;
            }

            return origin == HitShapeOrigin.AbilityAimPoint
                ? aimPoint
                : owner.TransformPoint(localCenter);
        }

        /// <summary>Capsule and ray far end, in world space.</summary>
        public Vector3 ResolveEnd(Transform owner, Vector3 aimPoint, Vector3 direction)
        {
            if (owner == null)
            {
                return aimPoint;
            }

            Vector3 start = ResolveCenter(owner, aimPoint);
            Vector3 axis = owner.TransformVector(localEnd - localCenter);
            if (axis.sqrMagnitude > Mathf.Epsilon)
            {
                return start + axis;
            }

            return kind == HitShapeKind.Ray
                ? start + ResolveDirection(owner, direction) * Reach()
                : start;
        }

        /// <summary>
        /// Largest distance the shape reaches from its centre, used to fade
        /// damage with distance and to size a ray with no authored axis.
        /// </summary>
        public float Reach()
        {
            switch (kind)
            {
                case HitShapeKind.Box:
                    return Mathf.Max(0.05f, halfExtents.magnitude);
                case HitShapeKind.Capsule:
                    return Mathf.Max(0.05f, Radius + Vector3.Distance(localCenter, localEnd) * 0.5f);
                case HitShapeKind.Ray:
                    float axisLength = Vector3.Distance(localCenter, localEnd);
                    return axisLength > Mathf.Epsilon ? axisLength : Radius;
                default:
                    return Radius;
            }
        }

        /// <summary>
        /// Runs the geometry and fills <paramref name="buffer"/> with what it
        /// found. This is the single physics call every consumer shares, so a
        /// shape can never mean one thing in a query and another one on screen.
        /// </summary>
        /// <param name="direction">
        /// Query direction, used by the cone and by a ray with no authored axis.
        /// Empty falls back to the owner's forward.
        /// </param>
        public int Query(
            Transform owner,
            Vector3 aimPoint,
            Vector3 direction,
            HitShapeHit[] buffer)
        {
            if (owner == null || buffer == null || buffer.Length == 0)
            {
                return 0;
            }

            int capacity = Mathf.Min(buffer.Length, ScratchCapacity);
            int layerMask = targetLayers.value == 0 ? Physics.AllLayers : targetLayers.value;
            Vector3 center = ResolveCenter(owner, aimPoint);

            if (kind == HitShapeKind.Ray)
            {
                return FillRay(owner, center, direction, layerMask, capacity, buffer);
            }

            int found = OverlapScratch(owner, aimPoint, center, layerMask, capacity);
            return FillOverlap(owner, center, direction, found, buffer);
        }

        /// <summary>
        /// Collider-only overlap, for consumers that want the raw physics result
        /// and nothing else. <see cref="Query"/> is the richer entry point and
        /// the one the effects use.
        /// </summary>
        public int Overlap(Transform owner, Vector3 aimPoint, Collider[] buffer)
        {
            if (owner == null || buffer == null || buffer.Length == 0)
            {
                return 0;
            }

            int layerMask = targetLayers.value == 0 ? Physics.AllLayers : targetLayers.value;
            int found = OverlapScratch(
                owner,
                aimPoint,
                ResolveCenter(owner, aimPoint),
                layerMask,
                Mathf.Min(buffer.Length, ScratchCapacity));
            Array.Copy(ColliderScratch, buffer, found);
            Array.Clear(ColliderScratch, 0, found);
            return found;
        }

        public void Sanitize()
        {
            radius = Mathf.Max(0.05f, radius);
            halfExtents = new Vector3(
                Mathf.Max(0.05f, halfExtents.x),
                Mathf.Max(0.05f, halfExtents.y),
                Mathf.Max(0.05f, halfExtents.z));
            coneHalfAngle = Mathf.Clamp(coneHalfAngle, 0f, 180f);
            coneInnerRadius = Mathf.Max(0f, coneInnerRadius);
        }

        /// <summary>
        /// Value equality over every authored field. Two effects that fire on
        /// the same trigger frame with the same shape run one physics query
        /// instead of two, which is what stops them resolving different targets
        /// out of what the author wrote as one volume.
        /// </summary>
        public bool Equals(HitShape other)
        {
            return kind == other.kind &&
                origin == other.origin &&
                targetLayers.value == other.targetLayers.value &&
                localCenter == other.localCenter &&
                localEnd == other.localEnd &&
                halfExtents == other.halfExtents &&
                Mathf.Approximately(radius, other.radius) &&
                Mathf.Approximately(coneHalfAngle, other.coneHalfAngle) &&
                Mathf.Approximately(coneInnerRadius, other.coneInnerRadius);
        }

        public override bool Equals(object obj) => obj is HitShape other && Equals(other);

        public override int GetHashCode()
        {
            return HashCode.Combine(
                kind,
                origin,
                targetLayers.value,
                localCenter,
                localEnd,
                halfExtents,
                radius);
        }

        /// <summary>Query direction, falling back to the owner's forward.</summary>
        private static Vector3 ResolveDirection(Transform owner, Vector3 direction)
        {
            return direction.sqrMagnitude > Mathf.Epsilon
                ? direction.normalized
                : owner.forward;
        }

        private int OverlapScratch(
            Transform owner,
            Vector3 aimPoint,
            Vector3 center,
            int layerMask,
            int capacity)
        {
            switch (kind)
            {
                case HitShapeKind.Box:
                    return Mathf.Min(capacity, Physics.OverlapBoxNonAlloc(
                        center,
                        new Vector3(
                            Mathf.Max(0.05f, halfExtents.x),
                            Mathf.Max(0.05f, halfExtents.y),
                            Mathf.Max(0.05f, halfExtents.z)),
                        ColliderScratch,
                        owner.rotation,
                        layerMask,
                        QueryTriggerInteraction.Collide));

                case HitShapeKind.Capsule:
                    return Mathf.Min(capacity, Physics.OverlapCapsuleNonAlloc(
                        center,
                        origin == HitShapeOrigin.AbilityAimPoint
                            ? aimPoint + owner.TransformVector(localEnd - localCenter)
                            : owner.TransformPoint(localEnd),
                        Radius,
                        ColliderScratch,
                        layerMask,
                        QueryTriggerInteraction.Collide));

                default:
                    // Sphere and cone share the broad phase; the cone narrows it
                    // by angle while the result is being filled.
                    return Mathf.Min(capacity, Physics.OverlapSphereNonAlloc(
                        center,
                        Radius,
                        ColliderScratch,
                        layerMask,
                        QueryTriggerInteraction.Collide));
            }
        }

        private int FillOverlap(
            Transform owner,
            Vector3 center,
            Vector3 direction,
            int found,
            HitShapeHit[] buffer)
        {
            Vector3 facing = ResolveDirection(owner, direction);
            float halfAngle = ConeHalfAngle;
            float innerRadius = ConeInnerRadius;
            int count = 0;

            for (int i = 0; i < found; i++)
            {
                Collider candidate = ColliderScratch[i];
                ColliderScratch[i] = null;
                if (candidate == null)
                {
                    continue;
                }

                Vector3 point = candidate.ClosestPoint(center);
                Vector3 offset = point - center;
                if (kind == HitShapeKind.Cone &&
                    !MatchesCone(offset, facing, halfAngle, innerRadius))
                {
                    continue;
                }

                Vector3 normal = offset.sqrMagnitude > Mathf.Epsilon
                    ? offset.normalized
                    : -facing;
                buffer[count++] = new HitShapeHit(candidate, point, normal);
            }

            return count;
        }

        /// <summary>
        /// Inside the inner radius the angle is ignored, which is how a target
        /// standing beside the owner still counts as a melee candidate.
        /// </summary>
        private static bool MatchesCone(
            Vector3 offset,
            Vector3 facing,
            float halfAngle,
            float innerRadius)
        {
            Vector3 planar = Vector3.ProjectOnPlane(offset, Vector3.up);
            float distance = planar.magnitude;
            if (distance <= innerRadius || distance <= Mathf.Epsilon)
            {
                return true;
            }

            return Vector3.Angle(
                Vector3.ProjectOnPlane(facing, Vector3.up),
                planar) <= halfAngle;
        }

        private int FillRay(
            Transform owner,
            Vector3 center,
            Vector3 direction,
            int layerMask,
            int capacity,
            HitShapeHit[] buffer)
        {
            Vector3 axis = owner.TransformVector(localEnd - localCenter);
            float length;
            Vector3 rayDirection;
            if (axis.sqrMagnitude > Mathf.Epsilon)
            {
                length = axis.magnitude;
                rayDirection = axis / length;
            }
            else
            {
                length = Reach();
                rayDirection = ResolveDirection(owner, direction);
            }

            int found = Mathf.Min(capacity, Physics.RaycastNonAlloc(
                center,
                rayDirection,
                RaycastScratch,
                length,
                layerMask,
                QueryTriggerInteraction.Collide));
            int count = 0;
            for (int i = 0; i < found; i++)
            {
                RaycastHit raycastHit = RaycastScratch[i];
                if (raycastHit.collider == null)
                {
                    continue;
                }

                buffer[count++] = new HitShapeHit(
                    raycastHit.collider,
                    raycastHit.point,
                    raycastHit.normal);
            }

            return count;
        }
    }
}
