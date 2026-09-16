using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// A reusable target acquisition: one <see cref="HitShape"/> for the volume
    /// and one <see cref="AbilityTargetFilter"/> for who inside it counts.
    ///
    /// Acquisition and filtering are separate on purpose. The volume is the
    /// expensive part and the part physics returns in an unspecified order, so
    /// it is run once per activation frame and shared; the filter is cheap and
    /// per-consumer, so two effects can read one acquisition and still disagree
    /// about what they act on without disagreeing about what is there.
    /// </summary>
    [Serializable]
    public struct AbilityTargetQuery
    {
        /// <summary>
        /// Matches the hit capacity of the effects. A melee volume that finds
        /// more than this many colliders is an authoring mistake, not a case to
        /// grow a buffer for.
        /// </summary>
        internal const int Capacity = 32;

        private static readonly HitShapeHit[] SharedBuffer = new HitShapeHit[Capacity];

        [SerializeField] private HitShape shape;
        [SerializeField] private AbilityTargetFilter filter;

        public AbilityTargetQuery(HitShape shape, AbilityTargetFilter filter)
        {
            this.shape = shape;
            this.filter = filter;
        }

        public HitShape Shape => shape;
        public AbilityTargetFilter Filter => filter;

        /// <summary>
        /// Acquires and filters in one call, for callers with no activation to
        /// share a buffer with: editor gizmos, external acquisition, and tests.
        /// </summary>
        /// <returns>How many targets the filter accepted.</returns>
        public int Resolve(
            GameObject owner,
            Vector3 aimPoint,
            Vector3 direction,
            AbilityTargetData into)
        {
            if (into == null)
            {
                return 0;
            }

            Acquire(shape, owner, aimPoint, direction, into);
            Filtered(into, filter, owner);
            return into.Count;
        }

        /// <summary>
        /// Runs the geometry and resolves every collider to its actor, ordered
        /// deterministically. No filter is applied: this is the shared half.
        /// </summary>
        internal static void Acquire(
            in HitShape shape,
            GameObject owner,
            Vector3 aimPoint,
            Vector3 direction,
            AbilityTargetData into)
        {
            using (AbilityDiagnostics.TargetQueryMarker.Auto())
            using (AbilityDiagnostics.TargetQueries.Begin())
            {
                AcquireUnsampled(in shape, owner, aimPoint, direction, into);
            }
        }

        private static void AcquireUnsampled(
            in HitShape shape,
            GameObject owner,
            Vector3 aimPoint,
            Vector3 direction,
            AbilityTargetData into)
        {
            Transform ownerTransform = owner == null ? null : owner.transform;
            Vector3 origin = shape.ResolveCenter(ownerTransform, aimPoint);
            into.Begin(origin, direction);
            if (ownerTransform == null)
            {
                return;
            }

            int found = shape.Query(ownerTransform, aimPoint, direction, SharedBuffer);
            for (int i = 0; i < found; i++)
            {
                HitShapeHit geometry = SharedBuffer[i];
                SharedBuffer[i] = default;
                if (geometry.Collider == null)
                {
                    continue;
                }

                GameObject actor = TargetQueries.ResolveActor(geometry.Collider);

                Vector3 planar = Vector3.ProjectOnPlane(geometry.Point - origin, Vector3.up);
                if (planar.sqrMagnitude <= Mathf.Epsilon)
                {
                    // The origin sits inside the collider, so the closest point
                    // is the origin. Fall back to the body's centre, which still
                    // gives the owner something to turn toward.
                    planar = Vector3.ProjectOnPlane(
                        geometry.Collider.bounds.center - origin, Vector3.up);
                }

                float distance = planar.magnitude;
                into.Add(new AbilityTargetHit(
                    actor,
                    geometry.Collider,
                    geometry.Point,
                    geometry.Normal,
                    distance > Mathf.Epsilon ? planar / distance : into.Direction,
                    distance));
            }

            into.Sort();
        }

        /// <summary>
        /// Applies a filter in place, removing what it rejects and honouring its
        /// maximum count. Used by callers that own the acquisition buffer.
        /// </summary>
        internal static void Filtered(
            AbilityTargetData data,
            in AbilityTargetFilter filter,
            GameObject owner)
        {
            AbilityTargetFilter localFilter = filter;
            for (int i = data.Count - 1; i >= 0; i--)
            {
                if (!localFilter.Accepts(data[i], owner, data.Origin))
                {
                    data.RemoveAt(i);
                }
            }

            data.Trim(localFilter.MaximumCount);
        }
    }
}
