using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Shape drawn by <see cref="DebugDrawEffectDefinition"/>.
    /// </summary>
    public enum DebugDrawShape
    {
        Sphere,
        Box,
        Capsule,
    }

    /// <summary>
    /// Draws a wireframe shape at an ability timeline frame with a configurable
    /// screen lifetime, mirroring the query volume of a damage effect so tells
    /// and hit frames can be tuned visually. Registers no hits, deals no
    /// damage, and compiles out of player builds through
    /// <see cref="AbilityDebugDraw"/>.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DebugDrawEffect",
        menuName = "Fofuxo/Abilities/Effects/Debug Draw")]
    public sealed class DebugDrawEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField] private DebugDrawShape shape = DebugDrawShape.Sphere;
        [SerializeField] private Vector3 localCenter = new(0f, 1f, 1f);
        [SerializeField] private Vector3 localEnd = new(0f, 1f, 3f);
        [SerializeField] private Vector3 halfExtents = new(1f, 1f, 1f);
        [SerializeField, Min(0.05f)] private float radius = 1f;
        [SerializeField] private Color color = new(1f, 0.85f, 0.1f, 1f);
        [SerializeField, Min(0f)] private float duration = 1f;

        public override void Apply(AbilityEffectContext context)
        {
            if (context.Owner == null)
            {
                return;
            }

            Transform ownerTransform = context.Owner.transform;
            switch (shape)
            {
                case DebugDrawShape.Box:
                    AbilityDebugDraw.Box(
                        ownerTransform.TransformPoint(localCenter),
                        halfExtents,
                        ownerTransform.rotation,
                        color,
                        duration);
                    break;
                case DebugDrawShape.Capsule:
                    AbilityDebugDraw.Capsule(
                        ownerTransform.TransformPoint(localCenter),
                        ownerTransform.TransformPoint(localEnd),
                        radius,
                        color,
                        duration);
                    break;
                default:
                    AbilityDebugDraw.Sphere(
                        ownerTransform.TransformPoint(localCenter),
                        radius,
                        color,
                        duration);
                    break;
            }
        }

        private void OnValidate()
        {
            radius = Mathf.Max(0.05f, radius);
            duration = Mathf.Max(0f, duration);
        }
    }
}
