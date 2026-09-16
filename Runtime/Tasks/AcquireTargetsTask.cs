using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>When an acquisition gives up.</summary>
    public enum AbilityTargetAcquireMode
    {
        /// <summary>One query. Empty result means the task failed.</summary>
        Once,

        /// <summary>Queries every tick until something is accepted, or forever.</summary>
        UntilFound
    }

    /// <summary>
    /// Runs the standard target query as a task, so an ability can acquire
    /// mid-step instead of only at the frame an effect fires.
    ///
    /// It is the same <see cref="AbilityTargetQuery"/> the effects, the target
    /// assist and the gizmos run — one <see cref="HitShape"/> plus one
    /// <see cref="AbilityTargetFilter"/> — so what a task selects and what an
    /// attack hits stay described in one vocabulary. Nothing is re-implemented
    /// here; the task only owns when the query runs and what happens to the
    /// result.
    /// </summary>
    public sealed class AcquireTargetsTask : AbilityTask
    {
        private readonly AbilityTargetQuery query;
        private readonly AbilityTargetAcquireMode mode;
        private readonly bool adoptAsActivationTargets;
        private readonly AbilityTargetData results = new();

        /// <param name="adoptAsActivationTargets">
        /// True replaces the activation target data and re-points its context at
        /// the primary hit, the way a step target assist does. False leaves the
        /// activation alone and only reports through <see cref="Targets"/>.
        /// </param>
        public AcquireTargetsTask(
            in HitShape shape,
            AbilityTargetFilter filter = default,
            AbilityTargetAcquireMode mode = AbilityTargetAcquireMode.Once,
            bool adoptAsActivationTargets = false)
        {
            query = new AbilityTargetQuery(shape, filter);
            this.mode = mode;
            this.adoptAsActivationTargets = adoptAsActivationTargets;
        }

        /// <summary>
        /// What the last query accepted. Empty until the task succeeds, and
        /// owned by the task, so a caller that needs to keep it past the
        /// activation takes a <see cref="AbilityTargetData.Snapshot"/>.
        /// </summary>
        public AbilityTargetData Targets => results;

        public AbilityTargetAcquireMode Mode => mode;

        protected override void OnStart()
        {
            TryAcquire();
            if (IsRunning && mode == AbilityTargetAcquireMode.Once)
            {
                Fail();
            }
        }

        protected override void OnTick(float deltaTime)
        {
            TryAcquire();
        }

        private void TryAcquire()
        {
            if (Owner == null)
            {
                Fail();
                return;
            }

            AbilityContext context = Instance != null
                ? Instance.Context
                : AbilityContext.FromDirection(Owner, null, Owner.transform.forward);
            Vector3 direction = context.Direction.sqrMagnitude > Mathf.Epsilon
                ? context.Direction
                : Owner.transform.forward;
            Vector3 aimPoint = Instance != null ? context.AimPoint : Owner.transform.position;

            query.Resolve(Owner, aimPoint, direction, results);
            if (!results.HasTargets)
            {
                return;
            }

            if (adoptAsActivationTargets && Instance != null)
            {
                Instance.RetargetFromTargets(results);
            }

            Succeed();
        }
    }
}
