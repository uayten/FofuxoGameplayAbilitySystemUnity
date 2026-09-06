using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Nested ability: target assist executed on the parent activation, before
    /// displacement and animation. Queries damageable enemies around the
    /// activation direction (cone, with a proximity sphere that ignores the
    /// cone) and snaps the owner toward the best one. By default, the cone
    /// reaches twice the proximity radius. The chosen target and direction are
    /// forwarded to the parent ability, and optional approach movement closes
    /// the gap during the parent's startup. Zero layers disable the query.
    /// </summary>
    [CreateAssetMenu(
        fileName = "TargetAssist",
        menuName = "Fofuxo/Abilities/Target Assist")]
    public sealed class TargetAssistDefinition : AbilityDefinition
    {
        [SerializeField] private LayerMask targetLayers;
        [Tooltip("Zero uses twice Proximity Radius. If both are zero, target search is disabled.")]
        [SerializeField, Min(0f)] private float searchDistance;
        [SerializeField, Range(0f, 90f)] private float coneHalfAngle = 35f;
        [Tooltip("Enemies inside this radius match regardless of the cone.")]
        [SerializeField, Min(0f)] private float proximityRadius = 4f;
        [Tooltip("Moves the owner toward the chosen target during the parent ability's startup.")]
        [SerializeField] private bool approachTarget = true;
        [Tooltip("Distance preserved from the chosen target during approach.")]
        [SerializeField, Min(0f)] private float stoppingDistance = 3f;

        public int TargetLayerMask => targetLayers.value;
        public float SearchDistance => searchDistance;
        public float ConeHalfAngle => coneHalfAngle;
        public float ProximityRadius => proximityRadius;
        public bool ApproachTarget => approachTarget;
        public float StoppingDistance => stoppingDistance;

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

        internal float ResolveStoppingDistance()
        {
            return Mathf.Max(0f, stoppingDistance);
        }

        public override bool TryValidate(out string error)
        {
            if (!base.TryValidate(out error))
            {
                return false;
            }

            if (NestedAssist != null)
            {
                error = "A target assist cannot nest another ability.";
                return false;
            }

            if (searchDistance < 0f || proximityRadius < 0f || stoppingDistance < 0f)
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
    }
}
