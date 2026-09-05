using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Nested ability: target assist executed on the parent activation, before
    /// displacement and animation. Queries damageable enemies around the
    /// activation direction (cone, with a proximity sphere that ignores the
    /// cone) and snaps the owner toward the best one. Zero search distance
    /// inherits the parent step range; zero layers disable the query.
    /// </summary>
    [CreateAssetMenu(
        fileName = "TargetAssist",
        menuName = "Fofuxo/Abilities/Target Assist")]
    public sealed class TargetAssistDefinition : AbilityDefinition
    {
        [SerializeField] private LayerMask targetLayers;
        [Tooltip("Zero inherits the parent ability range.")]
        [SerializeField, Min(0f)] private float searchDistance;
        [SerializeField, Range(0f, 90f)] private float coneHalfAngle = 35f;
        [Tooltip("Enemies inside this radius match regardless of the cone.")]
        [SerializeField, Min(0f)] private float proximityRadius = 4f;

        public int TargetLayerMask => targetLayers.value;
        public float SearchDistance => searchDistance;
        public float ConeHalfAngle => coneHalfAngle;
        public float ProximityRadius => proximityRadius;

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

            if (searchDistance < 0f || proximityRadius < 0f)
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
