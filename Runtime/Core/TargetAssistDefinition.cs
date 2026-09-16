using UnityEngine;
using UnityEngine.Serialization;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Targeting data: which enemy an attack should commit to, and how far the
    /// owner may close the gap to reach it. Queries damageable enemies around
    /// the activation direction (cone, plus a proximity sphere that ignores the
    /// cone) and hands the chosen target and direction back to the ability that
    /// referenced it. Zero layers disable the query.
    ///
    /// This is deliberately NOT an ability. It has no timeline, no cost, no
    /// cooldown, no tags and no animation, and inheriting those from
    /// <see cref="AbilityDefinition"/> only ever meant a targeting asset was
    /// validated against rules for a timeline it does not have.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GTA_NewTargetAssist",
        menuName = "Fofuxo/Abilities/Target Assist")]
    public sealed class TargetAssistDefinition : ScriptableObject
    {
        [SerializeField] private LayerMask targetLayers;
        [Tooltip("Zero uses twice Proximity Radius. If both are zero, target search is disabled.")]
        [SerializeField, Min(0f)] private float searchDistance;
        [SerializeField, Range(0f, 90f)] private float coneHalfAngle = 35f;
        [Tooltip("Enemies inside this radius match regardless of the cone.")]
        [SerializeField, Min(0f)] private float proximityRadius = 4f;
        [Tooltip("Moves the owner toward the chosen target during the ability's startup.")]
        [SerializeField] private bool approachTarget = true;
        [Tooltip("Gap left between the owner's own collider surface and the target's. Zero ends the approach with the two surfaces touching.")]
        [FormerlySerializedAs("stoppingDistance")]
        [SerializeField, Min(0f)] private float stoppingGap = 0.05f;
        [Tooltip("What a later step does with the target the activation already committed to.")]
        [SerializeField] private AbilityTargetLockPolicy lockPolicy = AbilityTargetLockPolicy.Reacquire;
        [Tooltip("Extra rules on the candidates the query finds: line of sight, required or blocked target tags, and a hard count.")]
        [SerializeField] private AbilityTargetFilter filter;

        public int TargetLayerMask => targetLayers.value;
        public float SearchDistance => searchDistance;
        public float ConeHalfAngle => coneHalfAngle;
        public float ProximityRadius => proximityRadius;
        public bool ApproachTarget => approachTarget;
        public float StoppingGap => stoppingGap;
        public AbilityTargetLockPolicy LockPolicy => lockPolicy;
        public AbilityTargetFilter Filter => filter;

        /// <summary>
        /// False when zero layers or a zero search distance disable the search
        /// altogether, in which case the activation keeps the context it had.
        /// </summary>
        public bool HasQuery =>
            targetLayers.value != 0 && ResolveSearchDistance() > Mathf.Epsilon;

        /// <summary>
        /// The assist as a reusable query: a cone around the activation
        /// direction, with the proximity radius as the inner circle that matches
        /// whatever the angle. It is the same <see cref="HitShape"/> the effects
        /// and the gizmos run, so what assist selects and what an attack hits
        /// are described in one vocabulary.
        /// </summary>
        public AbilityTargetQuery BuildQuery()
        {
            return new AbilityTargetQuery(
                HitShape.Cone(
                    targetLayers.value,
                    ResolveSearchDistance(),
                    Mathf.Clamp(coneHalfAngle, 0f, 90f),
                    Mathf.Max(0f, proximityRadius)),
                filter);
        }

        internal float ResolveSearchDistance()
        {
            if (searchDistance > Mathf.Epsilon)
            {
                return searchDistance;
            }

            if (proximityRadius > Mathf.Epsilon)
            {
                return proximityRadius * 2f;
            }

            return 0f;
        }

        internal float ResolveStoppingGap()
        {
            return Mathf.Max(0f, stoppingGap);
        }

        public bool TryValidate(out string error)
        {
            if (searchDistance < 0f || proximityRadius < 0f || stoppingGap < 0f)
            {
                error = "Assist distances must not be negative.";
                return false;
            }

            if (coneHalfAngle < 0f || coneHalfAngle > 90f)
            {
                error = "Assist cone must stay within 0-90 degrees.";
                return false;
            }

            error = null;
            return true;
        }

        private void OnValidate()
        {
            searchDistance = Mathf.Max(0f, searchDistance);
            proximityRadius = Mathf.Max(0f, proximityRadius);
            stoppingGap = Mathf.Max(0f, stoppingGap);
            coneHalfAngle = Mathf.Clamp(coneHalfAngle, 0f, 90f);
        }
    }
}
