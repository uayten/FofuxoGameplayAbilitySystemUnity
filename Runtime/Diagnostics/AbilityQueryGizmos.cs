using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Draws an ability's target queries in the Scene view, without entering
    /// Play Mode. Select the actor, pick an ability and a step, and every effect
    /// volume that step fires appears anchored on this transform — plus the
    /// target assist cone that decides who the step commits to.
    ///
    /// It runs the shipping geometry and the shipping filters: the shapes come
    /// from <see cref="AbilityDebugDraw.Shape"/> and the accepted targets from
    /// <see cref="AbilityTargetQuery"/>. A volume that looks right here is the
    /// volume that queries at runtime, because there is only one of them.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Fofuxo/Ability Query Gizmos")]
    public sealed class AbilityQueryGizmos : MonoBehaviour
    {
        [Tooltip("Ability whose queries are drawn. Empty draws the running one in Play Mode.")]
        [SerializeField] private AbilityDefinition ability;
        [Tooltip("Which step of that ability. Clamped to the ability's step count.")]
        [SerializeField, Min(0)] private int stepIndex;
        [Tooltip("Draw even when the object is not selected.")]
        [SerializeField] private bool alwaysDraw;
        [Tooltip("Also draw the step's target assist cone.")]
        [SerializeField] private bool drawTargetAssist = true;
        [Tooltip("Mark the targets the filters accept right now, at their contact points.")]
        [SerializeField] private bool drawAcceptedTargets = true;

        [SerializeField] private Color shapeColor = new(1f, 0.55f, 0.1f, 1f);
        [SerializeField] private Color assistColor = new(0.2f, 0.8f, 1f, 1f);
        [SerializeField] private Color targetColor = new(1f, 0.2f, 0.25f, 1f);

        private readonly AbilityTargetData preview = new();

        private void OnDrawGizmos()
        {
            if (alwaysDraw)
            {
                Draw();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!alwaysDraw)
            {
                Draw();
            }
        }

        private void Draw()
        {
            AbilitySystem system = GetComponent<AbilitySystem>();
            AbilityDefinition selected = ability != null
                ? ability
                : system != null
                    ? system.ActiveAbility
                    : null;
            // Only a timeline has query volumes to draw: its steps are where the
            // shapes are authored.
            var drawn = selected as TimelineAbilityDefinition;
            if (drawn == null)
            {
                return;
            }

            int index = ability != null || system == null ? stepIndex : system.ActiveStepIndex;
            AbilityStep step = drawn.StepAt(index);
            if (step == null)
            {
                return;
            }

            Vector3 direction = transform.forward;
            Vector3 aimPoint = transform.position + direction;

            if (drawTargetAssist)
            {
                DrawAssist(step, drawn, aimPoint, direction);
            }

            for (int i = 0; i < step.EffectTriggers.Count; i++)
            {
                GameplayEffectDefinition effect = step.EffectTriggers[i].Effect;
                if (!TryGetQuery(effect, out AbilityTargetQuery query))
                {
                    continue;
                }

                DrawQuery(query, aimPoint, direction, shapeColor);
            }
        }

        private void DrawAssist(
            AbilityStep step,
            AbilityDefinition drawn,
            Vector3 aimPoint,
            Vector3 direction)
        {
            TargetAssistDefinition assist = step.TargetAssist != null
                ? step.TargetAssist
                : drawn.TargetAssist;
            if (assist == null || !assist.HasQuery)
            {
                return;
            }

            DrawQuery(assist.BuildQuery(), transform.position, direction, assistColor);
        }

        private void DrawQuery(
            AbilityTargetQuery query,
            Vector3 aimPoint,
            Vector3 direction,
            Color color)
        {
            HitShape shape = query.Shape;
            AbilityDebugDraw.Shape(in shape, transform, aimPoint, direction, color, 0f);
            if (!drawAcceptedTargets)
            {
                return;
            }

            query.Resolve(gameObject, aimPoint, direction, preview);
            for (int i = 0; i < preview.Count; i++)
            {
                AbilityTargetHit hit = preview[i];
                AbilityDebugDraw.Sphere(hit.Point, 0.12f, targetColor, 0f);
                AbilityDebugDraw.Line(preview.Origin, hit.Point, targetColor, 0f);
            }
        }

        /// <summary>
        /// The query an effect would run. Every effect carries the same
        /// targeting block, so there is no switch over effect kinds left: an
        /// effect with no volume simply draws nothing.
        /// </summary>
        private static bool TryGetQuery(
            GameplayEffectDefinition effect,
            out AbilityTargetQuery query)
        {
            query = default;
            if (effect == null)
            {
                return false;
            }

            GameplayEffectTargeting targeting = effect.Targeting;
            if (targeting.Shape.Reach() <= Mathf.Epsilon)
            {
                return false;
            }

            query = new AbilityTargetQuery(targeting.Shape, targeting.Filter);
            return true;
        }

        private void OnValidate()
        {
            stepIndex = Mathf.Max(0, stepIndex);
        }
    }
}
