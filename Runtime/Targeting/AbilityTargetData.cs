using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One resolved target: the actor, the collider it was found through, and
    /// the geometry of the contact. Effects read this instead of running their
    /// own physics query, so two effects on the same trigger frame can never
    /// disagree about who was hit.
    /// </summary>
    public readonly struct AbilityTargetHit
    {
        public AbilityTargetHit(
            GameObject actor,
            Collider collider,
            Vector3 point,
            Vector3 normal,
            Vector3 direction,
            float distance)
        {
            Actor = actor;
            Collider = collider;
            Point = point;
            Normal = normal;
            Direction = direction;
            Distance = distance;
        }

        /// <summary>
        /// The actor the collider belongs to — the character, not its hitbox —
        /// as <see cref="TargetQueries.ResolveActor"/> resolves it. Which
        /// components it needs to carry is each effect's decision: damage looks
        /// for an <see cref="AttributeSet"/>, a force for a physics body.
        /// </summary>
        public GameObject Actor { get; }
        public Collider Collider { get; }

        /// <summary>Contact point on the collider, in world space.</summary>
        public Vector3 Point { get; }

        /// <summary>
        /// Surface normal at <see cref="Point"/>. Overlap queries approximate it
        /// as the direction from the shape centre outward.
        /// </summary>
        public Vector3 Normal { get; }

        /// <summary>Planar direction from the query origin to the contact point.</summary>
        public Vector3 Direction { get; }

        /// <summary>Planar distance from the query origin to the contact point.</summary>
        public float Distance { get; }

        public bool IsValid => Actor != null;
    }

    /// <summary>
    /// The result of one target acquisition: where it was run from, which way it
    /// looked, and the actors it found, ordered deterministically.
    ///
    /// This is a snapshot. The buffer is reused by the activation that owns it,
    /// so a consumer that needs to keep the result past the current frame takes
    /// a <see cref="Snapshot"/>. Live re-acquisition is the caller's decision,
    /// expressed through <see cref="AbilityTargetLockPolicy"/>.
    /// </summary>
    public sealed class AbilityTargetData
    {
        /// <summary>
        /// Angular weight in the ranking score, in units of metres per degree.
        /// Small enough that distance decides, large enough that a candidate
        /// straight ahead wins a near-tie against one off to the side.
        /// </summary>
        private const float AngularBias = 0.02f;

        private readonly List<AbilityTargetHit> hits = new();
        /// <summary>Allocated once: sorting runs on every trigger frame.</summary>
        private readonly Comparison<AbilityTargetHit> comparison;

        public AbilityTargetData()
        {
            comparison = CompareByScore;
        }

        public Vector3 Origin { get; private set; }
        public Vector3 Direction { get; private set; } = Vector3.forward;
        public int Count => hits.Count;
        public bool HasTargets => hits.Count > 0;
        public IReadOnlyList<AbilityTargetHit> Hits => hits;
        public AbilityTargetHit this[int index] => hits[index];

        /// <summary>The best-ranked target, or an invalid hit when none matched.</summary>
        public AbilityTargetHit Primary => hits.Count > 0 ? hits[0] : default;
        public GameObject PrimaryActor => hits.Count > 0 ? hits[0].Actor : null;

        /// <summary>
        /// Targets supplied from outside a query — a scripted sequence, an AI
        /// decision, or a consumer that already knows who to hit. The result
        /// behaves exactly like a locally acquired one.
        /// </summary>
        public static AbilityTargetData FromActors(
            Vector3 origin,
            Vector3 direction,
            params GameObject[] actors)
        {
            AbilityTargetData data = new();
            data.Begin(origin, direction);
            if (actors == null)
            {
                return data;
            }

            for (int i = 0; i < actors.Length; i++)
            {
                data.AddActor(actors[i]);
            }

            data.Sort();
            return data;
        }

        public void Clear()
        {
            hits.Clear();
        }

        /// <summary>Copy that survives the next acquisition into this buffer.</summary>
        public AbilityTargetData Snapshot()
        {
            AbilityTargetData copy = new();
            copy.Begin(Origin, Direction);
            copy.hits.AddRange(hits);
            return copy;
        }

        /// <summary>
        /// Drops targets whose actor was destroyed since the acquisition.
        /// Target loss is a policy decision the caller makes on the result:
        /// an ability with <c>Requires Target</c> cancels, one without keeps
        /// swinging at nothing.
        /// </summary>
        /// <returns>How many targets were removed.</returns>
        public int PruneDestroyed()
        {
            int removed = 0;
            for (int i = hits.Count - 1; i >= 0; i--)
            {
                if (hits[i].Actor == null)
                {
                    hits.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        public bool Contains(GameObject actor)
        {
            if (actor == null)
            {
                return false;
            }

            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i].Actor == actor)
                {
                    return true;
                }
            }

            return false;
        }

        internal void Begin(Vector3 origin, Vector3 direction)
        {
            hits.Clear();
            Origin = origin;
            Direction = direction.sqrMagnitude > Mathf.Epsilon
                ? direction.normalized
                : Vector3.forward;
        }

        internal void Add(in AbilityTargetHit hit)
        {
            if (hit.Actor != null)
            {
                hits.Add(hit);
            }
        }

        /// <summary>
        /// Ranks by planar distance with a small angular bias toward the query
        /// direction, and breaks exact ties by entity id so two candidates at
        /// the same distance always come out in the same order. Physics returns
        /// overlaps in an unspecified order; without this, two effects reading
        /// the same volume could each pick a different first target.
        /// </summary>
        internal void Sort()
        {
            if (hits.Count < 2)
            {
                return;
            }

            hits.Sort(comparison);
        }

        internal void RemoveAt(int index)
        {
            hits.RemoveAt(index);
        }

        internal void Trim(int maximumCount)
        {
            if (maximumCount > 0 && hits.Count > maximumCount)
            {
                hits.RemoveRange(maximumCount, hits.Count - maximumCount);
            }
        }

        private int CompareByScore(AbilityTargetHit left, AbilityTargetHit right)
        {
            int byScore = Score(left).CompareTo(Score(right));
            if (byScore != 0)
            {
                return byScore;
            }

            return left.Actor.GetEntityId().CompareTo(right.Actor.GetEntityId());
        }

        private float Score(AbilityTargetHit hit)
        {
            return hit.Distance + Vector3.Angle(Direction, hit.Direction) * AngularBias;
        }

        private void AddActor(GameObject actor)
        {
            if (actor == null)
            {
                return;
            }

            Vector3 position = actor.transform.position;
            Vector3 planar = Vector3.ProjectOnPlane(position - Origin, Vector3.up);
            float distance = planar.magnitude;
            Add(new AbilityTargetHit(
                actor,
                actor.GetComponentInChildren<Collider>(),
                position,
                distance > Mathf.Epsilon ? -planar / distance : -Direction,
                distance > Mathf.Epsilon ? planar / distance : Direction,
                distance));
        }
    }

    /// <summary>
    /// What a later step does with the target the activation already has.
    /// The default reproduces the behaviour every authored asset relies on:
    /// each step that carries its own assist runs a fresh query.
    /// </summary>
    public enum AbilityTargetLockPolicy
    {
        /// <summary>Query again; the combo follows whoever is in front now.</summary>
        Reacquire = 0,

        /// <summary>
        /// Keep the current target while the query still finds it, and only
        /// re-acquire once it is gone. A combo cannot be stolen mid-chain by an
        /// enemy that walks closer.
        /// </summary>
        KeepWhileValid = 1,

        /// <summary>
        /// Never change target after the activation acquired one. The owner
        /// still rotates and approaches, so the chain follows a moving enemy.
        /// </summary>
        Lock = 2
    }
}
