using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Which of the acquired candidates a consumer accepts. The geometry says
    /// who is inside the volume; this says who counts.
    ///
    /// Every field is inert at its serialized default, so an effect that gains
    /// a filter keeps behaving exactly as it did before the field existed: the
    /// owner is excluded, scenery and the dead are skipped, no tag or line of
    /// sight is required, and the count is unlimited. Whether an actor can take
    /// what the effect does — an attribute set for damage, a physics body for a
    /// force — is the effect's own question, asked when it resolves each target.
    /// </summary>
    [Serializable]
    public struct AbilityTargetFilter
    {
        [Tooltip("Let the query return the owner. Off by default: an attack never hits itself.")]
        [SerializeField] private bool includeOwner;
        [Tooltip("Accept colliders that belong to no actor — nothing with an AbilitySystem, AttributeSet or effect container above them. Off keeps the query to actors; a wall on the layer is not a target.")]
        [FormerlySerializedAs("allowNonDamageable")]
        [SerializeField] private bool includeNonActors;
        [Tooltip("Accept an actor holding State.Dead. Off keeps the query to the living.")]
        [SerializeField] private bool includeDead;
        [Tooltip("Drop targets with no clear line from the query origin to their contact point.")]
        [SerializeField] private bool requireLineOfSight;
        [Tooltip("Layers that break line of sight. Empty means every layer blocks.")]
        [SerializeField] private LayerMask lineOfSightBlockers;
        [Tooltip("Target must own all of these tags on its own AbilitySystem.")]
        [SerializeField] private GameplayTag[] requiredTargetTags;
        [Tooltip("Target is skipped while it owns any of these tags.")]
        [SerializeField] private GameplayTag[] blockedTargetTags;
        [Tooltip("Largest number of targets the query keeps. Zero is unlimited.")]
        [SerializeField, Min(0)] private int maximumCount;

        public bool IncludeOwner => includeOwner;
        public bool IncludeNonActors => includeNonActors;
        public bool IncludeDead => includeDead;
        public bool RequireLineOfSight => requireLineOfSight;
        public int LineOfSightBlockers => lineOfSightBlockers.value;
        public GameplayTag[] RequiredTargetTags => requiredTargetTags ?? Array.Empty<GameplayTag>();
        public GameplayTag[] BlockedTargetTags => blockedTargetTags ?? Array.Empty<GameplayTag>();
        public int MaximumCount => Mathf.Max(0, maximumCount);

        /// <summary>
        /// A copy that only accepts the ability's own resolved target. This is
        /// the runtime half of the filter: the actor is a live object, so it is
        /// composed at the call site rather than authored on an asset.
        /// </summary>
        public AbilityTargetFilter RestrictedTo(GameObject requestedTarget)
        {
            AbilityTargetFilter copy = this;
            copy.RequestedTarget = requestedTarget;
            return copy;
        }

        public AbilityTargetFilter WithMaximumCount(int count)
        {
            AbilityTargetFilter copy = this;
            copy.maximumCount = Mathf.Max(0, count);
            return copy;
        }

        public AbilityTargetFilter WithRequiredTags(params GameplayTag[] tags)
        {
            AbilityTargetFilter copy = this;
            copy.requiredTargetTags = tags ?? Array.Empty<GameplayTag>();
            return copy;
        }

        public AbilityTargetFilter WithBlockedTags(params GameplayTag[] tags)
        {
            AbilityTargetFilter copy = this;
            copy.blockedTargetTags = tags ?? Array.Empty<GameplayTag>();
            return copy;
        }

        public AbilityTargetFilter WithDead(bool include)
        {
            AbilityTargetFilter copy = this;
            copy.includeDead = include;
            return copy;
        }

        public AbilityTargetFilter WithNonActors(bool include)
        {
            AbilityTargetFilter copy = this;
            copy.includeNonActors = include;
            return copy;
        }

        /// <summary>
        /// Only this actor, its children, or its parents match. Null accepts
        /// every candidate.
        /// </summary>
        public GameObject RequestedTarget { get; private set; }

        /// <summary>
        /// Runs every rule against one acquired candidate. The owner is passed
        /// separately because exclusion is about the querying actor, not about
        /// anything stored on the filter.
        /// </summary>
        public bool Accepts(in AbilityTargetHit hit, GameObject owner, Vector3 origin)
        {
            if (!hit.IsValid)
            {
                return false;
            }

            if (!includeOwner && owner != null && IsPartOf(hit.Actor.transform, owner.transform))
            {
                return false;
            }

            if (!includeNonActors && !TargetQueries.IsActor(hit.Actor))
            {
                return false;
            }

            if (!includeDead && IsDead(hit.Actor))
            {
                return false;
            }

            if (RequestedTarget != null &&
                !TargetQueries.MatchesRequestedTarget(hit.Actor.transform, RequestedTarget))
            {
                return false;
            }

            if (!MatchesTags(hit.Actor))
            {
                return false;
            }

            return !requireLineOfSight || HasLineOfSight(hit, origin);
        }

        /// <summary>
        /// Dead is a tag, held on the actor's ability system or granted by an
        /// effect on its container. An actor with neither cannot be dead any
        /// more than it can be anything else.
        /// </summary>
        private static bool IsDead(GameObject actor)
        {
            AbilitySystem system = actor.GetComponentInParent<AbilitySystem>();
            if (system != null)
            {
                return system.HasTag(CommonGameplayTags.Dead);
            }

            GameplayEffectContainer container = actor.GetComponentInParent<GameplayEffectContainer>();
            return container != null && container.HasTag(CommonGameplayTags.Dead);
        }

        private bool MatchesTags(GameObject actor)
        {
            GameplayTag[] required = RequiredTargetTags;
            GameplayTag[] blocked = BlockedTargetTags;
            if (required.Length == 0 && blocked.Length == 0)
            {
                return true;
            }

            AbilitySystem system = actor.GetComponentInParent<AbilitySystem>();
            if (system == null)
            {
                // Nothing can hold a tag it has no system for: a required tag
                // rejects, a blocked tag cannot match.
                return required.Length == 0;
            }

            for (int i = 0; i < required.Length; i++)
            {
                if (!system.HasTag(required[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < blocked.Length; i++)
            {
                if (system.HasTag(blocked[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasLineOfSight(in AbilityTargetHit hit, Vector3 origin)
        {
            Vector3 toTarget = hit.Point - origin;
            float distance = toTarget.magnitude;
            if (distance <= Mathf.Epsilon)
            {
                return true;
            }

            int blockers = lineOfSightBlockers.value == 0
                ? Physics.AllLayers
                : lineOfSightBlockers.value;
            if (!Physics.Raycast(
                    origin,
                    toTarget / distance,
                    out RaycastHit obstruction,
                    distance,
                    blockers,
                    QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            // The target's own body is not an obstruction to itself.
            return IsPartOf(obstruction.collider.transform, hit.Actor.transform);
        }

        private static bool IsPartOf(Transform candidate, Transform root)
        {
            return candidate == root ||
                candidate.IsChildOf(root) ||
                root.IsChildOf(candidate);
        }
    }
}
