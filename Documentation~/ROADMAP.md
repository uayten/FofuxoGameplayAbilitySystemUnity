# Fofuxo Gameplay Ability System roadmap

**This file lists only work that has not been built yet.** Nothing here
describes shipped behaviour: `README.md` documents what the package does today
and where it stops, and `CHANGELOG.md` records what changed and why. A milestone
is deleted from this file the moment it ships, and the ones left behind are
renumbered, so the numbers below are positions in the remaining queue rather
than stable identifiers — a `Milestone N` mentioned in `CHANGELOG.md` refers to
the numbering in force when that entry was written.

The package is inspired by Unreal's Gameplay Ability System as documented in
[tranek/GASDocumentation](https://github.com/tranek/GASDocumentation), but it
does not aim for API parity. A feature enters the package only when it has a
clear Unity use case, deterministic runtime ownership, focused tests, and
useful authoring tools.

## Table of contents

- [Orientation for an agent picking this up](#orientation-for-an-agent-picking-this-up)
- [Scope: single player](#scope-single-player)
- [Rules any new work must respect](#rules-any-new-work-must-respect)
- [Delivery order](#delivery-order)
- [Milestone 1 — The 1.0 surface](#milestone-1--the-10-surface)
- [Blocked on a consumer](#blocked-on-a-consumer)
- [What waits on a play test](#what-waits-on-a-play-test)
- [Non-goals](#non-goals)

## Orientation for an agent picking this up

Read these before writing a line, in this order:

1. **`CLAUDE.md`** (byte-identical to `AGENTS.md`) — the architecture
   invariants, the asset naming convention, and the rule that authored assets
   and the public API are *not* design constraints before `1.0`. Not optional;
   it is not auto-loaded, and a change that skips it is made against the wrong
   defaults.
2. **`README.md`** — current behaviour and its documented limitations.
3. **`CHANGELOG.md`** — why the current shape was chosen. Most "why isn't this a
   subclass / an enum / an effect?" questions are answered there.
4. **`Tests/EditMode`** — the executable contract. Fifty-six test files, 584
   tests; the fastest way to learn a subsystem is to read its test.

Facts you would otherwise have to go find:

| | |
| --- | --- |
| Package id | `com.uayten.fofuxogameplayabilitysystem` |
| Namespace | `Fofuxo.GameplayAbilitySystem` |
| Unity | `6000.6` or newer |
| Version | pre-1.0, `0.x`, no compatibility promise |
| Assemblies | `Uayten.FofuxoGameplayAbilitySystem` (core), `.Motors`, `.Input`, `.Editor`, `.Tests` |
| Asset menu root | `Fofuxo/Abilities/…` |
| Only consumer | BossRush — the Player (`GrantAbilityIntegration`, `PlayerController`) and the boss (`FergusBrain`, `FergusBoss`) |

Layout: `Runtime/Core` (definitions, steps, instances, `AbilitySystem`, tags,
contracts), `Runtime/Attributes`, `Runtime/Effects`, `Runtime/Targeting`,
`Runtime/Tasks`, `Runtime/Motors`, `Runtime/Input`, `Runtime/Presentation`,
`Runtime/Diagnostics`, `Editor`, `Tests/EditMode`.

The debugging tools are built: the Ability Debugger window
(`AbilitySystemDebuggerWindow`), the per-actor `AbilityEventHistory` behind
`AbilitySystem.History`, the `AbilityDiagnostics` switch and counters, and the
editor-owned `AbilityTimeControls`. Use them before adding a log line — a
refused activation is in the actor's history with its rejection code, and the
window's audit runs the runtime's own `EvaluateActivation`.

The combat loop is closed: damage is an attribute change made by
`DamageEffectDefinition` on the target's `AttributeSet`, followed by a gameplay
event (`Event.HitReaction`, `Event.Knockdown`) the target's own
`HitReactionAbilityDefinition` answers — clip, lock, cancellation and knockback
all on the target, read off `AbilityInstance.TriggeringSpec`. `AbilityHitInfo`
and `IAbilityDamageReceiver` no longer exist; do not bring a receiver callback
back under another name.

Presentation is built too: every actor's `GameplayCueDispatcher`
(`AbilitySystem.Cues`) raises `Execute`, `Add`, `WhileActive` and `Remove` with
read-only `GameplayCueParameters`; an effect's `cueTag` and `cueReplacements`
drive persistent cues and per-outcome replacements; step cues know a hit from
a miss. A presenter gets a handle and a lifecycle, never a runtime object.

Persistence is built: `AbilityPersistence.Capture` and `.Restore` write an
actor's cooldowns, charges, attribute base values, active effects and loose tags
into an `AbilitySaveRecord` and put them back, through an `IAbilitySaveResolver`
the game supplies and an `AbilityOfflinePolicy` it picks. A restore rebuilds an
effect without executing it. Nothing of it touches an actor that never saves:
there is no component, no serialized field and no tick.

### The vocabulary

| Unreal GAS concept | Fofuxo type | Responsibility |
| --- | --- | --- |
| `AbilitySystemComponent` | `AbilitySystem` | Activation, active runtime state, tags, cooldowns, events |
| `GameplayAbility` | `AbilityDefinition` + `AbilityInstance` | An action and its per-activation state; it has no timeline of its own |
| `GameplayAbility` + montage | `TimelineAbilityDefinition` + `AbilityStep` | The kind of ability authored as ordered swings over frames |
| `GameplayEffect` | `GameplayEffectDefinition` + `GameplayEffectSpec` + `ActiveGameplayEffect` | Attribute/tag changes and their duration, stacking, and source context |
| `AttributeSet` | `AttributeSet` + `AttributeSetDefinition` | Per-actor numerical gameplay state |
| `GameplayTag` | `GameplayTag` | State and semantic labels used by activation and effects |
| `AbilityTask` | `AbilityTask` + `AbilityTaskScope` | Cancellable work that waits, moves, targets, or listens over time |
| `TargetData` | `AbilityTargetData` | Serializable target actors, hit results, positions, directions |
| `GameplayCue` | `GameplayCueTrigger` + consumer presenters | Cosmetic VFX, SFX, camera, animation, UI |

Three deliberate divergences from the reference, each of which will look like a
bug until you know it was a decision:

- **A combo is one ability, not a chain of them.** Unreal links a combo across
  several abilities by input; here it is one `AbilityDefinition` with an ordered
  `steps` list, so cost, cooldown, tags and cancellation are decided once per
  activation.
- **Cancellation is named by `GameplayTag`, not by an enum.** The package raises
  only what it causes itself — `Cancel.Manual`, `Cancel.TargetLost`,
  `Cancel.PhysicsForce`, `Cancel.StepTimeout`, `Cancel.Superseded`,
  `Cancel.Failed`, `Cancel.OwnerTeardown`. A game adds its own tags in the same
  namespace and never edits the package.
- **An ability's identity is its asset, not its class.** The class is the
  schema; the asset is the instance. Two abilities that share values are two
  assets, never a base class and a child.

## Scope: single player

**This package targets single-player games, and every milestone below assumes
one authoritative local runtime.** Replication, client prediction, prediction
keys, rollback, and server-authoritative validation are deliberately absent.
They are the largest and most invasive part of Unreal's GAS, and paying for them
without a multiplayer host would complicate every API for a feature nothing
consumes.

The seam that exists is enough: assign an `IAbilityReplicationSink` to forward
activations, cues, and endings to a netcode layer.

If a consumer ever goes multiplayer, the concepts to implement at that point —
prediction keys, prediction windows, effect replication modes, replicated target
data, rollback-safe scopes — are described in detail by
[tranek/GASDocumentation](https://github.com/tranek/GASDocumentation). Treat that
as a new roadmap written against a concrete multiplayer host, not as a milestone
waiting at the end of this one.

## Rules any new work must respect

These are the constraints that shape whatever is built next; the full set lives
in `CLAUDE.md`.

- **Actions, state changes and presentation stay separate.** An attack, dash,
  roll, block, cast or hit reaction is an ability. Damage, healing, costs, buffs,
  debuffs and granted tags are gameplay effects. Timed movement, target
  acquisition and event waits are ability tasks. Particles, sounds, camera shake
  and UI feedback are gameplay cues.
- **Definition assets never contain runtime state.** Candidate buffers, selected
  targets, elapsed time, remaining movement, stacks and effect handles belong to
  actor or activation runtime objects. An effect that writes to its definition is
  a bug that survives Play Mode and corrupts the asset on disk.
- **Gameplay cues stay cosmetic.** A cue may present an event but must never
  apply damage, healing, movement, invulnerability, parry success, or any other
  authoritative state.
- **`CanActivate` and target validation are side-effect free.** That includes
  the diagnostics: a question leaves nothing in the actor's history, only an
  attempt does.
- **Effects sharing a trigger frame, a step and a shape share one physics
  query**, through `AbilityInstance.AcquireTargets`. That is what makes damage
  and physics force hit the same enemy; it is covered by tests and must survive
  any change to where targeting lives.
- **Animator state is never the sole authority for gameplay timing.**
- **No package assembly, runtime or editor, may reference a consumer type.** The
  adapter direction is game → package.
- **`AbilityDefinition` is subclassed for a new *kind* of ability, never one per
  skill.** Reusable consequences belong in `GameplayEffectDefinition`, which is
  where the package does its subclassing.
- **Target Assist is a targeting prelude, not an ability.**
  `TargetAssistDefinition` is a plain `ScriptableObject` of query and approach
  fields that resolves before the step runs — it never becomes an active
  ability, and the parent ability keeps ownership of cooldown, costs, tags,
  timeline, animation, hit registration, completion and cancellation. A step that
  runs assist-driven approach must not also declare its own displacement window
  until multiple concurrent movement tasks exist.
- **Diagnostics record; they never decide.** A new runtime path that matters to
  a debugger records an `AbilityEvent` behind the `AbilityDiagnostics.Enabled`
  check and allocates nothing when it is off. Time control stays in the editor
  assembly: no runtime code writes `Time.timeScale`.

## Delivery order

Ordered by cost paid against value returned for a solo developer with one
consumer project — not by Unreal parity.

1. **Milestone 1** — what is left before the public surface can be frozen.

## Milestone 1 — The 1.0 surface

Most of this milestone shipped: the Melee Combat sample, the profiling scene,
the allocation budget tests, the documentation test, the versioning and
deprecation policy, the release checklist and the upgrade guide. `README.md`
and `CHANGELOG.md` describe them. What is left is what actually declares `1.0`.

Deliverables:

- **The public API documentation that is still missing.** Every public type,
  every public method and every member of the five types a consumer calls in
  code are documented, and a test keeps the types that way. What is left is
  275 properties: 182 of them mirror a serialized field the Inspector already
  explains with a `Tooltip` and the skill tables already list, and ~93 are
  computed reads (`IsEmpty`, `IsAccepted`, clamped getters). The question
  before `1.0` is whether a third copy of the field tooltips earns its keep;
  the computed ones probably do.
- **The version itself**: `package.json` to `1.0.0`, the changelog heading
  dated, the tag, and the README's "no compatibility promise" section replaced
  by the policy in `Documentation~/RELEASE_CHECKLIST.md`.

Acceptance criteria:

- Clean import and focused tests on the supported Unity 6 versions.
- No game-specific type dependencies in runtime or editor assemblies — pinned by
  `PackageBoundaryTests`.
- Public API changes are documented and covered by migration guidance.
- **A real consumer project uses every core subsystem.** BossRush uses all of
  them except persistence, which is built and unused until the day/night
  ability that reads the elapsed time exists. That ability is the last gate.

## Blocked on a consumer

These are designed, agreed, and deliberately not built: each one would be
invented against nothing. Build it when the trigger fires, not before.

- **Team and faction target filters.** `AbilityTargetFilter` covers owner
  exclusion, alive state, required and blocked tags, line of sight and maximum
  count. *Trigger:* a consumer with more than two sides — the current one has
  two and expresses them with layers.
- **`SpawnProjectileAndWait`.** *Trigger:* a consumer that actually fires
  projectiles.
- **Costs for resources that are not attributes.** Ammunition, inventory items
  or anything else the consumer owns, checked and paid through a cost type the
  consumer implements, the way Lyra's `AdditionalCosts` extend Unreal's
  `CheckCost` and `ApplyCost`. *Trigger:* a consumer ability that spends such a
  resource.
- **Effect execution calculations as assets.** *Trigger:* a damage formula that
  a `GameplayEffectMagnitude` cannot express.
- **Generated attribute accessors.** `AttributeSet` is reached by
  `GameplayAttribute` identifier today. *Trigger:* a consumer with enough
  attributes that the identifiers become the authoring cost, plus a measured
  benefit — codegen is a non-goal without one.

## What waits on a play test

The package is verified by 584 EditMode tests and by validation that runs the
runtime's own rules. Neither can tell whether something *feels* right, and
nothing below is a bug report - it is the list of what has never been played.

- **The Melee Combat sample.** Import it, open `Scenes/MeleeCombat.unity`, and
  play. What to watch, in the order it usually needs tuning: the combo window
  (does the second press land where the hand expects?), the roll's i-frame
  window against the enemy's swing, the parry window at ten frames, the
  knockback distance of the third step, and the enemy brain's decision interval.
  Everything on that list is a number in an asset.
- **The profiling scene**, `Scenes/Profiling.unity`: raise `Pair Count` until the
  frame rate moves, and read the counters against
  [`PERFORMANCE.md`](PERFORMANCE.md).
- **Persistence, once a game saves with it.** The round trip, the three offline
  policies and the migration chain are covered by tests; what no test covers is
  a real session closed and reopened, which only exists when the day/night
  ability does.

## Non-goals

- Owning consumer-game input mappings, AI decision systems, health UI, or
  character controllers.
- Reproducing every Unreal GAS class one-to-one.
- Multiplayer replication, client prediction, or rollback. See
  [Scope: single player](#scope-single-player).
- Reintroducing per-step abilities, one damage effect class per collider shape,
  or one effect class per duration policy. All three existed and all three were
  collapsed; the changelog says why.
- Using gameplay cues as authoritative gameplay logic.
- Storing per-actor runtime values in `ScriptableObject` assets.
- Adding reflection-heavy or generated APIs without a measured authoring
  benefit.
