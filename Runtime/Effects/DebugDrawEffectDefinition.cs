using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Draws the wireframe of the effect targeting shape at an ability timeline
    /// frame with a configurable screen lifetime, so tells and hit frames can be
    /// tuned visually. Registers no hits, deals no damage, and compiles out of
    /// player builds through <see cref="AbilityDebugDraw"/>.
    ///
    /// It declares no geometry of its own. Copying a damage effect targeting
    /// into this asset is the point: the drawing and the query then read the
    /// same fields, and a shape that was tuned on screen is the shape that hits.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GE_DebugDraw",
        menuName = "Fofuxo/Abilities/Effects/Debug Draw")]
    public sealed class DebugDrawEffectDefinition : GameplayEffectDefinition
    {

        [Header("Drawing")]
        [SerializeField] private Color color = new(1f, 0.85f, 0.1f, 1f);
        [SerializeField, Min(0f)] private float drawDuration = 1f;

        public Color Color => color;
        public float DrawDuration => Mathf.Max(0f, drawDuration);

        protected override bool Execute(GameplayEffectSpec spec, bool periodic)
        {
            if (spec.Source == null)
            {
                return false;
            }

            AbilityContext abilityContext = spec.Context;
            HitShape shape = Targeting.Shape;
            AbilityDebugDraw.Shape(
                in shape,
                spec.Source.transform,
                abilityContext.AimPoint,
                abilityContext.Direction,
                color,
                drawDuration);
            return true;
        }

        /// <summary>
        /// The base validation, minus the target-layer rule: this effect draws
        /// a shape rather than querying with it.
        /// </summary>
        public override bool TryValidate(out string error)
        {
            if (!base.TryValidate(out error))
            {
                return false;
            }

            if (DurationPolicy != GameplayEffectDurationPolicy.Instant)
            {
                error =
                    "A debug draw is a one-shot: it draws when it executes and its " +
                    "lifetime is Draw Duration, not the effect duration. Use the " +
                    "Instant policy.";
                return false;
            }

            if (drawDuration <= 0f)
            {
                error = "Draw Duration is zero, so the wireframe never appears.";
                return false;
            }

            error = null;
            return true;
        }

        internal void ConfigureDrawForTests(HitShape shape, Color drawColor, float lifetime)
        {
            SetTargetingForTests(GameplayEffectTargeting.Owner.WithShape(shape));
            color = drawColor;
            drawDuration = lifetime;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            drawDuration = Mathf.Max(0f, drawDuration);
        }
    }
}
