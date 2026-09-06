# Fofuxo's Gameplay Ability System

A lightweight, data-driven ability framework for Unity inspired by Unreal's
Gameplay Ability System.

- Author reusable abilities as `ScriptableObject` assets.
- Execute startup, active, and recovery phases from frame-based timelines.
- Gate activation with gameplay tags, range, facing, cooldowns, and targets.
- Compose automatic ability sequences for combos and scripted actions.
- Apply pluggable effects without coupling the package to game-specific health code.
- Drive the same abilities from player input, AI, cutscenes, or tests.

## Table of contents

- [Status](#status)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Authoring an ability](#authoring-an-ability)
- [Effects and damage](#effects-and-damage)
- [Gameplay cues](#gameplay-cues)
- [Sequences](#sequences)
- [Input](#input)
- [AI integration](#ai-integration)
- [Planned Attributes system](#planned-attributes-system)
- [Roadmap](#roadmap)
- [Working with AI agents](#working-with-ai-agents)
- [Repository layout](#repository-layout)
- [Design rules](#design-rules)
- [Testing and contributing](#testing-and-contributing)
- [License](#license)

## Status

The package is in early `0.x` development. The current vertical slice is usable
for local, single-player combat, but public APIs may evolve before `1.0`.

| Available now | Planned |
| --- | --- |
| Ability definitions and runtime instances | Attributes and attribute sets |
| Frame-based startup/active/recovery phases | Duration and infinite effects |
| Gameplay-tag activation gates | Effect stacking and granted tags |
| Ability and sequence cooldowns | Ability costs |
| Automatic and manual sequences | Conditional and branching sequences |
| Input System routing | Ability tasks and movement policies |
| Sphere-based melee damage effect | Additional targeting and hit shapes |
| Lifecycle events and cancellation reasons | Runtime debugger and active-effect inspector |
| Frame-based gameplay cues | Cue replication |
| Attributes: identifiers, sets, instant modifiers | Duration/infinite effects and stacking |
| Frame-gated combo continuation with early input buffering | Conditional combo branches |
| Box/capsule damage effects, shared target queries | Projectile and collider-window effects |
| Attribute costs, charges, input buffering | Dynamic cooldowns and cost discounts |
| Duration modifiers with stacking, regeneration | Infinite effects and effect specs |
| Whiff events, animation-event cue bridge | Full ability-task graph (beyond displacement) |
| Ability-owned displacement windows | |
| Replication sink hooks, Invulnerable tag | Cue replication |
| AbilitySystemDebugger readout | Runtime debugger window |
| Nested Target Assist (circle, cone, facing, startup approach) | General target-data and ability-task graph |

Current limitations are intentional and documented so consumers and
contributors do not mistake planned APIs for implemented behavior:

- An `AbilitySystem` executes one ability at a time.
- Sequences advance automatically when each step completes.
- Effects execute at configured frames and are currently instant.
- `MeleeDamageEffectDefinition` performs one sphere query per trigger.
- The package does not own health, AI decision-making, locomotion motors,
  networking, prediction, or save data. Abilities may own kinematic
  displacement windows (meters over frames); planar velocity stays with the
  owner's motor.

## Requirements

- Unity `6000.0` or newer.
- [Input System](https://docs.unity3d.com/Packages/com.unity.inputsystem@latest)
  for `AbilityInputRouter`. The core runtime assembly does not reference the
  Input System assembly.

## Installation

Open **Window > Package Management > Package Manager**, press **+**, choose
**Install package from git URL...**, and use:

```text
https://github.com/uayten/FofuxoGameplayAbilitySystemUnity.git
```

Pin a release by appending its Git tag:

```text
https://github.com/uayten/FofuxoGameplayAbilitySystemUnity.git#v0.1.0
```

The package ID is:

```text
com.uayten.fofuxogameplayabilitysystem
```

## Quick start

1. Add `AbilitySystem` to an actor.
2. Create an ability with **Create > Fofuxo > Abilities > Ability**.
3. Configure its identity, animation state, target rules, frame timeline, tags,
   cooldown, and effects.
4. Create an `AbilityLoadout`, add the ability, and assign the loadout to the
   actor's `AbilitySystem`.
5. Activate it from game code:

```csharp
using Fofuxo.GameplayAbilitySystem;
using UnityEngine;

public sealed class ExampleAbilityUser : MonoBehaviour
{
    [SerializeField] private AbilitySystem abilitySystem;
    [SerializeField] private AbilityDefinition ability;
    [SerializeField] private Transform target;

    public bool TryUseAbility()
    {
        GameObject targetObject = target != null ? target.gameObject : null;
        AbilityContext context = AbilityContext.FromTarget(gameObject, targetObject);
        return abilitySystem.TryActivate(ability, context);
    }
}
```

Use `CanActivate` when a caller needs a rejection reason without changing
runtime state:

```csharp
AbilityContext context = AbilityContext.FromTarget(gameObject, target);
if (!abilitySystem.CanActivate(ability, context, out string reason))
{
    Debug.Log(reason);
}
```

## How it works

```text
Input / AI / game code
        |
        | CanActivate / TryActivate
        v
AbilitySystem
        |
        | creates per-activation runtime state
        v
AbilityInstance ------> AbilityDefinition.asset
        |                         |
        | ticks frames            +--> animation and targeting rules
        |                         +--> tags and cooldown
        |                         +--> effect triggers
        v
AbilityEffectDefinition
        |
        +--> game-owned receivers, attributes, projectiles, VFX, audio, etc.
```

`AbilityDefinition` is immutable authoring data. Every activation creates an
`AbilityInstance` containing the owner, target, elapsed time, current frame,
current phase, and registered hits. Cooldowns, loose tags, granted-tag counts,
and active sequences live in `AbilitySystem`.

The `Animator` is presentation. `AbilitySystem` crossfades to the configured
state, while the ability timeline remains the gameplay authority.

## Authoring an ability

### Identity and animation

- `Ability ID` is a stable identifier such as `character.melee.attack.01`.
- `Animation Clip` supplies the frame rate used by the timeline.
- `Animator State Name` is crossfaded on layer `0` when activation succeeds.
- `Animation Blend Duration` controls the crossfade.

The state name can be either a short state name or a fully qualified Animator
path. Keep the Animator state's playback speed aligned with the authored ability
timeline. If a state plays at a custom speed, rescale the ability frames to keep
gameplay and animation synchronized.

### Targeting

Activation can require a target and validate:

- Minimum and maximum planar range.
- Maximum facing angle.
- Target presence.

An effect may have a different physical query volume. For melee effects, make
sure the activation range and effect reach overlap; otherwise an AI can stop at
a valid activation distance while the hit query still cannot reach the target.

An attack may assign a `TargetAssistDefinition` as its `Nested Assist`. The
assist runs before the parent animation and effects. It accepts targets anywhere
inside its proximity circle or inside its forward cone, propagates the chosen
target and direction into the parent context, snaps facing, and optionally
approaches during startup until its own `Stopping Distance` is reached. A zero
assist `Search Distance` makes the cone reach twice the proximity radius; when
both values are zero, target search is disabled. Assist-driven approach
and the parent's own displacement are mutually exclusive until concurrent
movement tasks are available.

Directional, targetless abilities such as rolls or dashes set
`Requires Target` to `false` and carry their facing explicitly:

```csharp
Vector3 rollDirection = GetRollDirection(); // game-specific facing logic
AbilityContext context = AbilityContext.FromDirection(gameObject, null, rollDirection);

if (abilitySystem.CanActivate(rollAbility, context, out string reason))
{
    abilitySystem.TryActivate(rollAbility, context);
}
```

`FromDirection` projects the vector onto the ground plane and falls back to the
owner's forward when it is empty. Grant a tag such as `State.Rolling` on the
ability so health and animation code can read invulnerability from the system,
set displacement distance and frame window on the asset for the travel itself,
while hitbox control stays in game code subscribed to the
ability lifecycle events.

### Displacement

Abilities such as rolls, dashes, and lunges own their travel as data:
direction mode (`Context`, `OwnerForward`, `TowardTarget`, `AwayFromTarget`),
distance in meters, and a 1-based frame window on the ability timeline.
`AbilitySystem` resolves direction and `Rigidbody` once at activation and
applies planar `MovePosition` travel at constant speed while the window is
open; cancelling or completing the ability stops travel immediately. Travel
is kinematic and unswept, like root motion: it never touches velocity, so
the owner's motor should clear competing planar velocity at activation.

### Timeline

The timeline uses one-based frames:

```text
1 .. Startup End Frame        Startup
next .. Active End Frame      Active
next .. Recovery End Frame    Recovery
```

Effects trigger once when their configured frame is reached. The custom
Inspector shows the resolved frame rate, duration, phases, and validation result.

### Tags and cancellation

- Required tags must be present.
- Blocked tags must be absent.
- Granted tags exist only while the ability is active.
- Loose tags are controlled by game code for external states such as stun,
  rolling, knockdown, or death.
- `AbilityCancelMask` declares which cancellation reasons the ability accepts.

## Effects and damage

Effects derive from `AbilityEffectDefinition`:

```csharp
using Fofuxo.GameplayAbilitySystem;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Abilities/Effects/Example")]
public sealed class ExampleEffectDefinition : AbilityEffectDefinition
{
    public override void Apply(AbilityEffectContext context)
    {
        Debug.Log($"Applied by {context.Owner} to {context.Target}");
    }
}
```

`AreaDamageEffectDefinition` covers ground slams and bursts: a sphere
centered on the ability aim point or on an owner-local offset, with optional
linear damage falloff and radial knockback. Every damageable receiver in
radius is hit (no requested-target filter).

`MeleeDamageEffectDefinition` (directed sphere), `BoxDamageEffectDefinition`
(wide swings), and `CapsuleDamageEffectDefinition` (lunges) share the
`TargetQueries` helpers: non-allocating queries, receiver resolution from
colliders or parents, owner exclusion, and hierarchy-aware requested-target
matching. All damage effects accept optional attribute scaling (for example,
Strength adds damage per point).

`MeleeDamageEffectDefinition` is the single-target melee effect. It performs a
non-allocating sphere query, resolves `IAbilityDamageReceiver` from each matching
collider, prevents duplicate hits for the same trigger, and sends an
`AbilityHitInfo` containing:

- Damage amount and source.
- Hit point and direction.
- World-space knockback velocity and duration.
- `Light`, `Heavy`, or `Knockdown` impact.
- Whether the hit can be parried.

Damage shapes are instantaneous non-allocating physics queries, not temporary
Collider GameObjects. An attack Inspector exposes its first damage-query trigger
as `Damage Frame`; changing it also keeps the single-frame startup/active phase
boundary aligned. Embedded box settings are edited inline under `Damage Box`.

The package deliberately does not provide a health component. Implement the
receiver in game code:

```csharp
using Fofuxo.GameplayAbilitySystem;
using UnityEngine;

public sealed class MyHealth : MonoBehaviour, IAbilityDamageReceiver
{
    [SerializeField] private int health = 100;

    public bool IsDamageable => health > 0;

    public bool TryReceiveDamage(AbilityHitInfo hit)
    {
        if (!IsDamageable || hit.Amount <= 0)
        {
            return false;
        }

        health = Mathf.Max(0, health - hit.Amount);
        return true;
    }
}
```

The current damage effects carry knockback as reaction data in `AbilityHitInfo`;
the consumer receiver decides whether and how the target moves. In the planned
GAS-style architecture, numerical damage/status changes become Gameplay Effect
specifications while a target-owned hit-reaction ability uses a cancellable
movement task for knockback. Knockback is therefore not intended to become a
standalone attribute effect. See the [detailed roadmap](Documentation~/ROADMAP.md#knockback-ownership).

## Costs, charges, buffering, and whiffs

Abilities declare `AbilityCost` entries (attribute + amount), checked before
activation and deducted on success — Stamina for a sprint attack, for example.
`MaxCharges` limits consecutive uses; charges refill one per
`ChargeRestoreTime`, or all at once when the cooldown elapses when the restore
time is zero. Validation requires limited charges to have a restore path.

`AbilityInputRouter` buffers rejected inputs for `bufferWindow` seconds and
retries them, so combo inputs pressed during recovery still land. When an
ability with effect triggers completes without registering a hit, the system
fires `AbilityWhiffed` alongside `AbilityCompleted` — roll and buff abilities
without effects never whiff.

## Gameplay cues

Yes — this is the Unreal GameplayCue concept translated to a local,
single-player package. In Unreal, abilities and effects trigger cue tags that
the `GameplayCueManager` executes as cosmetics (particles, sounds, camera
shakes) on owning clients and simulated proxies. Here there is no replication
or central manager: an ability author places `GameplayCueTrigger` entries
(frame + cue tag) on the `AbilityDefinition`, the `AbilitySystem` raises
`GameplayCueTriggered` while ticking, and game-side presenters turn tags into
VFX/SFX. Cues never change gameplay state.

```csharp
// React to cues from game code (for example, a per-actor presenter).
abilitySystem.GameplayCueTriggered += (ability, cue, context) =>
{
    if (cue == new GameplayTag("Cue.EnemyAttackTell"))
    {
        SpawnTellEffect(context.Owner);
    }
};

// Or fire one manually, outside any ability (AI tell, successful parry).
abilitySystem.TriggerGameplayCue(
    new GameplayTag("Cue.ParrySuccess"),
    AbilityContext.FromTarget(gameObject, target));
```

Typical parry-tell setup: the enemy attack ability carries a cue trigger about
half a second before its damage frame (for example, tell at frame 25 when the
hit lands at frame 52 on a 60 fps timeline). The player's parry window then
becomes a reaction test instead of a guess.

## Sequences

`AbilitySequenceDefinition` groups ordered steps and an optional sequence-level
cooldown. Add the sequence to the actor's loadout, then call:

```csharp
AbilityContext context = AbilityContext.FromTarget(gameObject, target);

if (abilitySystem.CanActivateSequence(sequence, context, out string reason))
{
    abilitySystem.TryActivateSequence(sequence, context);
}
```

Manual sequence steps can author three frame windows on their
`AbilityDefinition`:

- `Movement Unlock Frame`: movement becomes available while the ability may continue.
- `Combo Continue Frame`: earliest frame that may start the next sequence step.
- `Combo Input End Frame`: last inclusive frame that accepts the next input.

`TryQueueSequenceAdvance` records an early input during the current step. When
the continuation frame arrives, the current step completes and the next begins
without another press. If no input was queued when the input window closes, the
sequence ends while the current ability is allowed to finish normally. Zero
frame values preserve the legacy post-completion `TryAdvanceSequence` behavior.

## Input

`AbilityInputRouter` lives in a separate assembly that references Unity's Input
System. It supports direct `InputActionReference` bindings and named actions from
an `InputActionAsset`.

When no explicit target is configured, game code can provide a resolver:

```csharp
AbilityInputRouter.GlobalFallbackTargetResolver = () =>
    FindFirstObjectByType<MyEnemy>()?.gameObject;
```

Use the per-instance `FallbackTargetResolver` when different actors need
different targeting policies.

## AI integration

The package does not choose actions. An AI controller, behavior tree, utility
system, or simple state machine evaluates context and calls the same public API
used by player input.

```csharp
AbilityContext context = AbilityContext.FromTarget(gameObject, target);

float score = ability.BaseAiWeight;
if (abilitySystem.CanActivate(ability, context, out _))
{
    // Combine the authored weight with game-specific distance, threat,
    // phase, and repetition scores before choosing an action.
}
```

`BaseAiWeight` is authoring data, not a built-in AI policy. Keeping decisions
outside the package lets projects use custom code, Unity Behavior, or another AI
framework without changing ability execution.

## Planned Attributes system

Attributes are the next major runtime subsystem. The design stays smaller
than Unreal GAS while preserving its most useful separation between authoring
data, per-actor state, and effect execution.

Shipped as a first slice: `GameplayAttribute` identifiers, `AttributeSet`
components with authored initial values, instant `AttributeModifier`
application (`Add`/`Multiply`/`Override` with deterministic aggregation and
limits), and typed `AttributeValueChanged` events. Duration modifiers with
`Stack`/`Refresh`/`Ignore` policies and per-second regeneration also shipped
(`ApplyDurationModifier`, `Tick`). Infinite effects, specs, and GameplayEffect
execution remain planned below.

### Proposed types

| Type | Responsibility |
| --- | --- |
| `GameplayAttribute` | Serializable stable identifier such as `Combat.Health` |
| `GameplayAttributeValue` | Per-actor base value, computed current value, and limits |
| `AttributeSet` | Game-defined component that owns related runtime attributes |
| `AttributeModifier` | Attribute, operation, magnitude, and evaluation target |
| `GameplayEffectDefinition` | Immutable effect asset with duration, modifiers, tags, and stacking rules |
| `GameplayEffectSpec` | Runtime snapshot containing source, target, level, and calculated magnitudes |
| `ActiveGameplayEffect` | Runtime state for duration or infinite effects |

The package will define the infrastructure, while games define concrete sets:

```csharp
public sealed class CombatAttributeSet : AttributeSet
{
    [SerializeField] private GameplayAttributeValue health = new(100f, 0f, 100f);
    [SerializeField] private GameplayAttributeValue maxHealth = new(100f, 1f, 9999f);
    [SerializeField] private GameplayAttributeValue stamina = new(100f, 0f, 100f);
    [SerializeField] private GameplayAttributeValue poise = new(50f, 0f, 100f);
}
```

This sketch documents direction, not current API.

### Value model

- Base values are persistent per-actor state.
- Instant effects may change a base value, such as health damage or healing.
- Duration and infinite effects contribute modifiers to the computed current value.
- Initial operations are `Add`, `Multiply`, and `Override`.
- Evaluation order is deterministic: base, additive modifiers, multiplicative
  modifiers, then the highest-priority override.
- Attribute sets provide pre-change clamping and post-effect hooks for rules such
  as keeping Health between `0` and MaxHealth.
- Attribute changes emit a typed event containing old value, new value, source,
  effect, and attribute identifier.
- Runtime values never live in `ScriptableObject` assets.

The first version will avoid reflection-heavy property expressions, generated
accessors, execution calculations, and network prediction. Those should be added
only after real game use demonstrates a need.

### Effect integration

The planned flow is:

```text
AbilityEffectDefinition
        |
        | creates or applies
        v
GameplayEffectSpec
        |
        +--> instant attribute changes
        +--> duration/infinite ActiveGameplayEffect
        +--> granted and blocked gameplay tags
        +--> stacking and expiration
```

Cooldowns and costs can later become specialized gameplay effects, but they will
remain explicit fields until the generic effect lifecycle is proven.

## Debugging and replication hooks

`AbilitySystemDebugger` is a drop-in readout component: it logs ability and
cue transitions and exposes a one-line `Summary` (active ability, frame,
tags) for Inspector monitoring while tuning. `ActiveFrame`, `ActiveTags`,
and `AbilityInstance.RegisteredHitCount` support custom tooling.

`AbilityDebugDraw` is the Unreal-style draw-debug layer: timed wireframe
boxes, spheres, and capsules rendered in the Scene and Game views through
`Debug.DrawLine`, compiled out of player builds. Call it from game code for
one-off shapes, or attach `DebugDrawEffectDefinition` to an ability timeline
(shape, color, and screen lifetime are authoring data) to visualize a damage
query volume at the exact frame it fires — for example, the same sphere as
the melee effect on the damage frame, plus a tell-colored shape on an
earlier frame.

Multiplayer stays out of scope, but the seams exist: assign an
`IAbilityReplicationSink` to forward activations, cues, and endings to the
netcode layer. Animation clips can also emit cues without code through
`AbilityAnimationEventBridge.EmitGameplayCue`.

## Roadmap

The short version:

1. Stabilize current lifecycle, validation, and public contracts.
2. Add Gameplay Effect specs and a complete active-effect lifecycle.
3. Standardize target data and reusable target queries.
4. Add cancellable Ability Tasks, then migrate approach and knockback movement.
5. Expand sequence/input policies, cues, editor tooling, and diagnostics.
6. Add persistence and optional networking only after local behavior is proven.

See [the detailed development roadmap](Documentation~/ROADMAP.md) for the GAS
concept mapping, knockback and Target Assist decisions, milestone dependencies,
deliverables, acceptance criteria, non-goals, and the path to `1.0`.

## Working with AI agents

This repository is expected to be edited across many independent AI-agent
conversations. Durable context belongs in versioned files, not chat history.

- [`AGENTS.md`](AGENTS.md) contains repository-wide implementation rules and is
  the first file coding agents should follow.
- This README describes current public behavior and limitations.
- [`Documentation~/ROADMAP.md`](Documentation~/ROADMAP.md) records architectural
  direction, priorities, milestones, and acceptance criteria.
- [`CHANGELOG.md`](CHANGELOG.md) records user-visible changes.
- Tests preserve behavioral contracts more reliably than prose alone.
- Future architectural decisions should be stored as short ADRs under
  `Documentation~/Decisions/`.

### What are agent skills?

An agent skill is a reusable workflow packaged as a directory with a required
`SKILL.md` plus optional scripts, references, and templates. Agents load the full
instructions only when the task matches the skill, which keeps normal repository
context smaller.

This repository can benefit from skills once a workflow is repeated often enough
to justify automation. Good candidates are:

- `ability-effect-authoring`: add a new effect type, editor support, tests, docs,
  and changelog entry using the same checklist every time.
- `package-release`: validate tests and `.meta` files, update version/changelog,
  create a tag, and verify Git installation.
- `public-api-review`: detect game-specific dependencies, runtime state stored in
  assets, missing tests, and undocumented breaking changes.

Repository-scoped skills should live in `.agents/skills/<skill-name>/SKILL.md`.
Do not turn ordinary architecture notes into a skill: keep stable facts here and
use skills for procedural work with clear inputs and outputs.

Official references:

- [Custom instructions with AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md)
- [Build skills](https://learn.chatgpt.com/docs/build-skills)

## Repository layout

```text
Runtime/
  Core/      Definitions, runtime instances, system, tags, sequences, contracts
  Effects/   Built-in effect implementations
  Input/     Optional Input System integration in a separate assembly
Editor/      Custom inspectors and authoring tools
Tests/
  EditMode/  Package contract tests
AGENTS.md    Persistent instructions for coding agents
CHANGELOG.md User-visible release history
package.json UPM manifest
```

`CreateAssetMenu` entries live under **Fofuxo > Abilities**.

## Design rules

- `ScriptableObject` assets contain immutable configuration only.
- Runtime state belongs to `AbilitySystem`, `AbilityInstance`, attribute sets,
  effect specs, or active-effect instances.
- The core assembly must not depend on project-specific actors, health systems,
  input, AI, or character controllers.
- Optional integrations belong in separate assemblies.
- Callers choose intent; the ability system validates and executes it.
- Stable IDs are independent from asset filenames and display names.
- Public API changes require tests, README updates, and a changelog entry.
- Identifiers, comments, logs, documentation, and commit messages are written in English.

## Testing and contributing

This repository is a Unity package rather than a standalone Unity project. Import
it into a Unity host project, enable package tests in Package Manager, and run the
`Uayten.FofuxoGameplayAbilitySystem.Tests` EditMode assembly.

Before submitting a change:

1. Inspect the existing working tree and preserve unrelated work.
2. Keep the core free from game-specific dependencies.
3. Add or update focused EditMode tests for behavior changes.
4. Preserve Unity `.meta` files for every added, moved, or renamed asset.
5. Update this README when behavior or architecture changes.
6. Update `CHANGELOG.md` under `[Unreleased]` for user-visible changes.
7. Verify the package by importing it into a Unity `6000.0+` host project.

## License

MIT — see [LICENSE.md](LICENSE.md).
