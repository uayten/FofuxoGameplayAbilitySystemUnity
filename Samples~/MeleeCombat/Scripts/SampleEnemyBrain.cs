using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Samples
{
    /// <summary>
    /// The smallest AI that exercises the same ability system the player drives:
    /// close the distance, swing when the swing would be accepted, and block
    /// while waiting. It decides nothing about animation, damage or recovery -
    /// those belong to the abilities it activates.
    ///
    /// The one call worth copying is <see cref="AbilitySystem.EvaluateActivation"/>:
    /// it answers with a typed rejection and leaves no trace in the actor's
    /// history, which is what makes it safe to ask every frame.
    /// </summary>
    [RequireComponent(typeof(SampleFighter))]
    public sealed class SampleEnemyBrain : MonoBehaviour
    {
        [SerializeField] private SampleFighter target;

        [Tooltip("Ability used when the target is in reach.")]
        [SerializeField] private AbilityDefinition attack;

        [Tooltip("Ability held while waiting for an opening. Optional.")]
        [SerializeField] private AbilityDefinition block;

        [Tooltip("Metres kept from the target while circling.")]
        [SerializeField, Min(0f)] private float preferredDistance = 2f;

        [Tooltip("Seconds between decisions. An AI that decides every frame is an AI that stutters.")]
        [SerializeField, Min(0.05f)] private float decisionInterval = 0.4f;

        [Tooltip("Chance of blocking instead of waiting, per decision.")]
        [SerializeField, Range(0f, 1f)] private float blockChance = 0.35f;

        private SampleFighter fighter;
        private float nextDecision;

        private void Awake()
        {
            fighter = GetComponent<SampleFighter>();
        }

        /// <summary>Points the brain at another fighter, for a scene that spawns its actors.</summary>
        public void SetTarget(SampleFighter fighterToFight)
        {
            target = fighterToFight;
        }

        private void Update()
        {
            if (fighter.IsDead || target == null || target.IsDead)
            {
                return;
            }

            Vector3 toTarget = target.transform.position - transform.position;
            float distance = toTarget.magnitude;
            fighter.Face(toTarget);

            if (distance > preferredDistance)
            {
                fighter.Move(toTarget);
            }

            if (Time.time < nextDecision)
            {
                return;
            }

            nextDecision = Time.time + decisionInterval;
            Decide(distance);
        }

        private void Decide(float distance)
        {
            AbilityContext context = AbilityContext.FromTarget(gameObject, target.gameObject);

            if (attack != null && fighter.System.CanActivate(attack, context, out _))
            {
                fighter.System.TryActivate(attack, context);
                return;
            }

            if (block == null || distance > preferredDistance * 2f)
            {
                return;
            }

            if (Random.value < blockChance && fighter.System.CanActivate(block, context, out _))
            {
                fighter.System.TryActivate(block, context);
            }
        }
    }
}
