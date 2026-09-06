# Fofuxo Gameplay Ability System roadmap

This roadmap describes the intended evolution of the package from its current
single-player combat foundation into a reusable, production-oriented ability
framework. It is inspired by Unreal's Gameplay Ability System (GAS), but it does
not aim for API parity. Features should enter the package only when they have a
clear Unity use case, deterministic runtime ownership, focused tests, and useful
authoring tools.

## Table of contents

- [Product direction](#product-direction)
- [Unreal GAS concepts and Fofuxo equivalents](#unreal-gas-concepts-and-fofuxo-equivalents)
- [Architecture decisions](#architecture-decisions)
- [Current baseline](#current-baseline)
- [Delivery priorities](#delivery-priorities)
- [Milestone 1 — Core contracts and validation](#milestone-1--core-contracts-and-validation)
- [Milestone 2 — Gameplay effect specifications](#milestone-2--gameplay-effect-specifications)
- [Milestone 3 — Target data and reusable targeting](#milestone-3--target-data-and-reusable-targeting)
- [Milestone 4 — Ability tasks and movement](#milestone-4--ability-tasks-and-movement)
- [Milestone 5 — Sequences, input, and cancellation](#milestone-5--sequences-input-and-cancellation)
- [Milestone 6 — Gameplay cues and presentation](#milestone-6--gameplay-cues-and-presentation)
- [Milestone 7 — Editor tooling and diagnostics](#milestone-7--editor-tooling-and-diagnostics)
- [Milestone 8 — Persistence and optional networking](#milestone-8--persistence-and-optional-networking)
- [Milestone 9 — Samples, performance, and 1.0 readiness](#milestone-9--samples-performance-and-10-readiness)
- [Non-goals](#non-goals)

## Product direction

Fofuxo GAS should provide generic combat orchestration while consumer projects
retain control of character motors, health presentation, AI decisions, input,
camera behavior, and game-specific rules. The package owns:

- Immutable ability, effect, targeting, and cue definitions.
- Per-actor ability state, attributes, tags, cooldowns, charges, and active effects.
- Per-activation contexts, target data, tasks, hit registration, and cancellation.
- Deterministic ordering of activation, targeting, movement, effects, and cues.
- Validation, diagnostics, tests, and authoring support for these contracts.

The package should remain useful in local games without forcing networking
concepts into every API. Networking, replication, and prediction are extension
layers that must not weaken deterministic single-player behavior.

## Unreal GAS concepts and Fofuxo equivalents

The architectural split follows the concepts documented in
[tranek/GASDocumentation](https://github.com/tranek/GASDocumentation):

| Unreal GAS concept | Fofuxo direction | Responsibility |
| --- | --- | --- |
| `AbilitySystemComponent` | `AbilitySystem` | Activation, active runtime state, tags, cooldowns, effects, and events |
| `GameplayAbility` | `AbilityDefinition` + `AbilityInstance` | An action an actor performs and its per-activation state |
| `GameplayEffect` | `GameplayEffectDefinition` + `GameplayEffectSpec` + `ActiveGameplayEffect` | Attribute/tag changes and their duration, stacking, and source context |
| `AttributeSet` | `AttributeSet` + `AttributeSetDefinition` | Per-actor numerical gameplay state |
| `GameplayTag` | `GameplayTag` | State and semantic labels used by activation and effects |
| `AbilityTask` | Planned `AbilityTask` runtime instances | Cancellable operations that wait, move, target, or respond to events over time |
| `TargetData` | Planned `AbilityTargetData` | Serializable target actors, hit results, positions, and directions |
| `GameplayCue` | `GameplayCueTrigger` + consumer presenters | Cosmetic VFX, SFX, camera, animation, and UI reactions |

The reference describes Gameplay Effects as data-only vessels for attribute and
tag changes, Gameplay Abilities as actor actions, and Ability Tasks as the place
for latent work such as root-motion movement. Fofuxo should preserve that
separation even when a short-term compatibility API combines damage and reaction
data in one hit payload.

## Architecture decisions

### Actions, state changes, and presentation stay separate

- An attack, dash, roll, block, cast, or hit reaction is an ability.
- Damage, healing, resource costs, buffs, debuffs, and granted tags are gameplay effects.
- Timed movement, target acquisition, event waits, and projectile waits are ability tasks.
- Particles, sounds, camera shake, hit stop presentation, and UI feedback are gameplay cues.
- Attributes store numbers; tags store semantic state; neither should drive presentation directly.

### Knockback ownership

Knockback is not a standalone attribute effect and should not be implemented as
an attacker-owned ability that directly controls another actor for its full
lifetime.

The current compatibility path is:

```text
Attack ability
    -> damage query effect
        -> AbilityHitInfo(damage, impact, knockback velocity, duration)
            -> consumer damage receiver applies health and movement
```

This remains supported while the task/effect model matures. The target design is:

```text
Attack ability
    -> target data / hit result
    -> instant damage GameplayEffectSpec changes Health or Poise
    -> gameplay event requests a target-owned HitReaction ability
        -> movement task applies knockback
        -> reaction ability owns animation, control lock, and cancellation
    -> gameplay cue presents impact cosmetics
```

This split lets immunity, armor, poise, blocking, super armor, and death decide
whether the target should move. It also gives cancellation and future network
prediction one authoritative owner. `AbilityHitInfo` should be deprecated only
after the effect-spec and target-reaction path covers existing consumers.

### Target Assist is a parent-ability prelude

`TargetAssistDefinition` is composed inside an attack through `Nested Assist`.
It runs first, without becoming a separately active ability:

1. Query damageable candidates once at activation.
2. Accept any candidate in the proximity circle.
3. Accept candidates in the forward cone up to the configured search distance.
4. When search distance is zero, use twice the proximity radius by default.
5. Rank valid candidates deterministically by distance with a small angular bias.
6. Propagate the selected actor and direction into the parent activation context.
7. Rotate the owner toward the selected target.
8. Optionally approach during the parent's startup until its `Maximum Range` is reached.
9. Start the parent animation and later execute its attack effects against the resolved target.

The parent remains the owner of cooldown, costs, tags, timeline, animation, hit
registration, completion, and cancellation. A parent that enables assist-driven
approach must not also declare a separate displacement window until multiple
concurrent movement tasks exist.

### Definition assets never contain runtime state

Definitions are immutable authoring data. Candidate buffers, selected targets,
elapsed time, remaining movement, stacks, prediction keys, and effect handles
belong to actor or activation runtime objects.

### Gameplay cues remain cosmetic

Cues may present an event but must never apply damage, healing, movement,
invulnerability, parry success, or other authoritative gameplay state.

## Current baseline

The following capabilities are already available:

- Frame-based ability timelines with startup, active, and recovery phases.
- Ability and sequence activation, cooldowns, costs, charges, tags, and cancellation reasons.
- Automatic and manual sequences with input buffering and continuation state.
- Directional activation contexts and ability-owned displacement windows.
- Nested target assist with proximity-circle and frontal-cone selection.
- Sphere, box, capsule, and area damage query effects.
- Hit deduplication, whiff events, parry payloads, stun data, and knockback payloads.
- Attribute identifiers, asset-authored initial sets, instant modifiers, duration modifiers,
  periodic modifiers, regeneration, stacking policies, and change events.
- Frame-based and manually triggered gameplay cues.
- Animation event bridging, debug drawing, lifecycle events, a debugger component,
  and replication sink interfaces.

The largest missing architectural pieces are generic Gameplay Effect specs,
target data, cancellable Ability Tasks, a complete active-effect lifecycle,
authoring/debugger windows, and validated optional networking semantics.

## Delivery priorities

Work should normally proceed in this order:

1. Protect current behavior with validation and focused tests.
2. Introduce effect specs and active-effect handles without breaking current effects.
3. Standardize target data and target queries.
4. Add cancellable ability tasks, then migrate movement and reactions onto them.
5. Harden combo, input, and cancellation policies.
6. Expand cue presentation contracts.
7. Build editor tooling around stable runtime APIs.
8. Add persistence and networking only behind explicit adapters.
9. Ship samples, performance budgets, upgrade notes, and a stable 1.0 API.

## Milestone 1 — Core contracts and validation

Goal: make the existing local runtime difficult to misconfigure and safe to extend.

Deliverables:

- Validate duplicate ability and sequence IDs across a loadout.
- Validate nested-definition cycles, missing references, invalid trigger ordering,
  incompatible movement sources, and target-layer masks.
- Define explicit activation outcomes: accepted, rejected, completed, cancelled,
  interrupted, target lost, and failed during execution.
- Add typed activation rejection codes alongside human-readable messages.
- Define public API compatibility and deprecation rules for the `0.x` line.
- Guarantee lifecycle event ordering and document reentrancy behavior.
- Add tests for disable/destroy cleanup and exceptions thrown by consumer effects.

Acceptance criteria:

- Every invalid asset reports an actionable Inspector message.
- `CanActivate` remains side-effect free.
- Runtime definitions are never mutated.
- Lifecycle order is covered by EditMode tests.

## Milestone 2 — Gameplay effect specifications

Goal: separate immutable effect authoring data from calculated per-application data.

Deliverables:

- Add `GameplayEffectDefinition` with Instant, Duration, and Infinite policies.
- Add `GameplayEffectSpec` carrying source, target, level, captured attributes,
  calculated magnitudes, tags, and contextual target data.
- Add `ActiveGameplayEffect` and stable handles for query, refresh, and removal.
- Support modifiers, executions, duration, periods, stacking, overflow, immunity,
  granted tags, and removal tags.
- Provide source/target capture rules and snapshot versus live evaluation.
- Migrate costs and cooldowns only after the generic lifecycle proves clearer than
  their current explicit implementation.
- Bridge existing `AbilityEffectDefinition` assets to specs for a deprecation window.

Acceptance criteria:

- Instant effects change base values deterministically.
- Duration and Infinite effects contribute to current values and clean up completely.
- Stack, refresh, ignore, overflow, and removal behavior are independently tested.
- Specs are per application and definitions remain immutable.

## Milestone 3 — Target data and reusable targeting

Goal: make target selection a reusable input to abilities and effects rather than
an effect-specific physics query.

Deliverables:

- Add `AbilityTargetData` for actors, colliders, hit points, normals, origins,
  directions, and world positions.
- Add allocation-conscious sphere, cone, box, capsule, ray, and overlap queries.
- Add filters for owner exclusion, teams/factions, interfaces, tags, alive state,
  line of sight, maximum count, and deterministic sorting.
- Allow local acquisition, externally supplied targets, and later replicated target data.
- Make Target Assist produce target data consumed by the parent ability.
- Add editor gizmos that use the same geometry and filters as runtime queries.
- Define policies for target loss, retargeting, locking, and snapshot versus live targets.

Acceptance criteria:

- Query and effect geometry share one tested implementation.
- Target ordering is deterministic for equal-distance candidates.
- Target Assist circle, cone, direction, context propagation, and no-target behavior
  are covered by focused tests.

## Milestone 4 — Ability tasks and movement

Goal: represent time-based work as cancellable per-activation runtime tasks.

Initial tasks:

- `WaitDelay`
- `WaitGameplayEvent`
- `WaitInputPress` and `WaitInputRelease`
- `WaitAnimationEvent`
- `AcquireTargets`
- `MoveByDistance`
- `MoveTowardTarget`
- `ApplyKnockback`
- `SpawnProjectileAndWait`

Movement requirements:

- A task snapshots or tracks its direction according to an explicit policy.
- Cancellation, completion, target loss, death, and disable stop movement immediately.
- Character-controller, Rigidbody, and custom-motor adapters remain outside the core.
- Collision behavior is explicit: unswept root-motion-like travel, swept travel,
  or consumer-motor authority.
- Concurrent movement tasks declare priority and conflict policy.
- Target Assist approach migrates to `MoveTowardTarget` without changing authored assets.
- Target-owned hit-reaction abilities migrate knockback to `ApplyKnockback`.

Acceptance criteria:

- Tasks never survive their owning activation.
- Cancellation produces no extra movement tick.
- Total displacement is deterministic across variable frame times.
- Movement adapters have focused tests and no dependency on game-specific controllers.

## Milestone 5 — Sequences, input, and cancellation

Goal: support responsive player combos and deterministic AI/scripted sequences.

Deliverables:

- Formalize automatic, manual, event-gated, and conditional advancement policies.
- Add per-step input windows, early buffering, late grace, branch conditions, and timeouts.
- Preserve one-input-per-step player combos while keeping automatic AI combos.
- Expose current step, queued intent, deadlines, and last transition reason.
- Define cancellation propagation between sequence, step, nested prelude, and tasks.
- Add input-held and input-released activation policies.
- Support ability groups and mutual-exclusion policies beyond the current single active ability.

Acceptance criteria:

- Every buffered input is consumed once or expires with an observable reason.
- A failed next step cannot leave a sequence active.
- Attack, block, roll, hit reaction, and death precedence is covered by tests.

## Milestone 6 — Gameplay cues and presentation

Goal: provide reliable local presentation contracts without allowing cues to own gameplay.

Deliverables:

- Define Execute, Add, WhileActive, and Remove cue lifecycles.
- Add typed cue parameters with source, target data, magnitude, location, and normal.
- Add cue suppression and replacement hooks for block, parry, immunity, and miss outcomes.
- Add pooling-friendly presenter interfaces and stable cue handles.
- Batch duplicate cues within one activation where appropriate.
- Keep replication behavior in an optional adapter.

Acceptance criteria:

- Removing an active effect reliably removes its persistent cue.
- Late presenter registration can reconstruct persistent cues without replaying burst cues.
- Tests verify that cues cannot modify authoritative package state.

## Milestone 7 — Editor tooling and diagnostics

Goal: make authoring and debugging faster than inspecting raw ScriptableObjects and logs.

Deliverables:

- Ability timeline Inspector with effect, cue, movement, and cancellation lanes.
- Loadout audit for duplicate IDs, missing references, cycles, and incompatible options.
- Shared query gizmos and previews for targeting and damage volumes.
- Runtime debugger window for active ability, phase, frame, context, tasks, tags,
  attributes, cooldowns, charges, effects, stacks, and recent events.
- Timestamped per-actor event history with rejection and cancellation reasons.
- Manual-sequence inspector showing step, queued input, and continuation deadline.
- Optional hit-stop and slow-motion diagnostic controls owned by editor tooling.
- Allocation and timing counters for target queries, effect application, and tasks.

Acceptance criteria:

- The runtime debugger introduces no player-build dependency.
- Validation uses the same rules as runtime activation.
- Diagnostics can be globally disabled with negligible overhead.

## Milestone 8 — Persistence and optional networking

Goal: define extension seams without claiming built-in production networking prematurely.

Persistence deliverables:

- Stable serialization identifiers for granted abilities, cooldowns, charges, attributes,
  and persistent active effects.
- Versioned save records and migration hooks.
- Explicit policy for restoring elapsed durations and offline progression.

Networking deliverables, only after a concrete multiplayer host exists:

- Authority adapter for activation requests and authoritative target data.
- Replication records for abilities, effects, attributes, tags, and cues.
- Prediction keys and rollback-safe scopes for explicitly supported operations.
- Server validation of targets, costs, cooldowns, and movement requests.
- Clear prediction support matrix; unsupported operations remain server authoritative.

Acceptance criteria:

- Local-only users pay no networking complexity or runtime cost.
- No feature is described as predicted until correction and rollback tests exist.
- Damage and death remain authoritative by default.

## Milestone 9 — Samples, performance, and 1.0 readiness

Goal: prove the API through representative games and freeze a supportable public surface.

Deliverables:

- Minimal samples for melee combo, roll, block/parry, projectile, area effect,
  buff/debuff, periodic damage, AI activation, and target assist.
- A complete target-reaction sample demonstrating damage, poise, knockback, and cues.
- Package documentation tests and upgrade guides.
- Profiling scenes for dense actors, simultaneous effects, and target queries.
- Allocation budgets and pooling guidance.
- Semantic versioning, deprecation windows, release checklist, and changelog discipline.
- API documentation for every public runtime contract.

Acceptance criteria:

- Clean import and focused tests on supported Unity 6 versions.
- No game-specific type dependencies in runtime or editor assemblies.
- Public API changes are documented and covered by migration guidance.
- The 1.0 surface has at least one real consumer project using every core subsystem.

## Non-goals

- Owning consumer-game input mappings, AI decision systems, health UI, or character controllers.
- Reproducing every Unreal GAS class or networking behavior one-to-one.
- Using gameplay cues as authoritative gameplay logic.
- Storing per-actor runtime values in ScriptableObject assets.
- Adding reflection-heavy or generated APIs without a measured authoring benefit.
- Claiming multiplayer prediction, persistence, or production readiness before tests prove it.
