# Fofuxo's Gameplay Ability System

A lightweight, data-driven ability framework for Unity inspired by Unreal's
Gameplay Ability System.

- Author reusable abilities as `ScriptableObject` assets.
- Build a combo as **one ability with several steps**, each with its own clip and
  frame windows — not one asset per swing.
- Execute startup, active, and recovery phases from frame-based timelines.
- Hold gameplay tags over inclusive per-step frame windows.
- Trigger granted reaction abilities from data-bearing gameplay events.
- Gate activation with gameplay tags, range, facing, cooldowns, and targets.
- Hit with one damage effect that takes any shape: sphere, box or capsule.
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
- [Combos and steps](#combos-and-steps)
- [Ability tasks](#ability-tasks)
- [Movement and the motor seam](#movement-and-the-motor-seam)
- [Input](#input)
- [AI integration](#ai-integration)
- [Attributes and the gameplay effect lifecycle](#attributes-and-the-gameplay-effect-lifecycle)
- [Persistence](#persistence)
- [The sample](#the-sample)
- [Roadmap](#roadmap)
- [Working with AI agents](#working-with-ai-agents)
- [Repository layout](#repository-layout)
- [Design rules](#design-rules)
- [Testing and contributing](#testing-and-contributing)
- [License](#license)

## Status

The package is in early `0.x` development. The current vertical slice is usable
for local, single-player combat, but public APIs may evolve before `1.0`.

### Compatibility before 1.0

**There is no compatibility promise below `1.0`, and no deprecation window.** A
signature, a serialized field, an enum value or a whole type that turns out
wrong is fixed rather than wrapped: two live paths for one concept cost more
than one clean break, and a package this young would accumulate them faster than
it accumulates features.

What you get instead of a promise:

- **Every intentional break is written down in `CHANGELOG.md`**, under a
  `Breaking` heading or marked inline, with what to change it to.
- **A break that invalidates authored assets says which ones**, and what has to
  be re-tuned and re-tested afterwards.
- **Renames are renames, not aliases.** Nothing is kept alive under two names,
  so a compile error is the migration notice.

Pin an exact version if you need stability, and read the changelog between
bumps. From `1.0` this section is replaced by semantic versioning and a
deprecation policy.

### Available now

| Area | What ships today |
| --- | --- |
| Abilities | `AbilityDefinition` assets granted through an `AbilityLoadout`, per-activation `AbilityInstance`, base AI weight; timelines are a subclass (`TimelineAbilityDefinition`) with startup/active/recovery phases, and a plain ability runs until something ends it |
| Combos | One ability with an ordered `AbilityStep` list; `Automatic`, `Manual`, `OnEvent` and `OnCondition` advancement; frame-gated continuation windows with early input buffering and grace frames; tag-conditional branches that may jump backwards to loop a step; four step-timeout policies |
| Activation | Required and blocked gameplay tags, minimum/maximum range and facing angle, a `Cost Gameplay Effect` checked with `CheckCost` and optionally paid again every `Cost Period`, a `Cooldown Gameplay Effect` whose granted tags mean "on cooldown", a commit step (`AbilityCommitPolicy`, `TryCommitAbility`), effects that live exactly as long as the activation, Set By Caller numbers filled in `ConfigureOutgoingSpec`, charges with restore time, a motor check for abilities that move their owner, a game-owned `CanActivateAbility`, and typed `AbilityActivationRejection` codes |
| Concurrency | Group tags with four mutual-exclusion policies: one ability at a time, per-group blocking, cancel the group, or cancel everything |
| Cancellation | Tag-named cancel requests, a per-ability policy (anything / only listed tags / nothing), four cancellation scopes, and the package tags `Cancel.Manual`, `.TargetLost`, `.PhysicsForce`, `.StepTimeout`, `.Superseded`, `.Failed` and `.OwnerTeardown` |
| Effects | One `GameplayEffectDefinition` asset with instant, duration and infinite policies, periods, one `GameplayEffectSpec` per application, `ActiveGameplayEffect` behind a `GameplayEffectHandle`, stacking with scope and overflow rules, immunity, granted and removal tags, and an `EffectBlocked` seam |
| Magnitudes | Source or target capture, snapshotted at application or re-read while active, level scaling, and `CaptureOwnMagnitudes` for subclass-authored values |
| Attributes | Identifiers, `AttributeSetDefinition`, base values and limits, deterministic Add/Multiply/Override aggregation, modifier slots with stable ids, typed change events, a ceiling read from another attribute; regeneration is a periodic effect the loadout grants |
| Damage and force | One `DamageEffectDefinition` with sphere, box, capsule, cone and ray `HitShape`s, subtracting from an authored target attribute and asking the target to react with `Event.HitReaction` / `Event.Knockdown`; `HitReactionAbilityDefinition`, the target-owned reaction that plays the clip, holds the lock and runs the authored knockback in its own scope; `PhysicsForceEffectDefinition` with level scaling, `Event.PhysicsForce` and `AbilityPhysicsBody`; parry effects authored on the ability |
| Targeting | `AbilityTargetData` shared per trigger frame and deterministically ordered; actors resolved from colliders to the nearest ability system, attribute set or effect container; filters for owner exclusion, scenery, the dead, required and blocked tags, line of sight and maximum count; `TargetAssistDefinition` with proximity, cone, facing, startup approach and lock policy |
| Ability tasks | A cancellable per-activation scope; waits for delay, gameplay event, input press/release and animation event; `AcquireTargets`; `MoveByDistance`, `MoveTowardTarget` and `ApplyKnockback` with explicit direction policies, priority and conflict resolution |
| Movement | Ability-owned displacement windows in meters over frames, reaching the world only through the `IAbilityMotor` seam, with Rigidbody, CharacterController and delegate adapters |
| Presentation | Gameplay cues with `Execute`, `Add`, `WhileActive` and `Remove` lifecycles, typed `GameplayCueParameters`, stable handles, per-actor `GameplayCueDispatcher` with pooling-friendly presenters, filters, per-frame batching and late-presenter catch-up; effect-driven persistent cues with per-outcome replacements; frame-based `GameplayCueTrigger`s that know a hit from a miss; whiff events, an animation-event bridge, counted per-step gameplay-tag windows, and ability clips layered over the Animator Controller |
| Input | Input System routing in its own assembly, driven by an `AbilityInputMap` asset, with press, hold and release activation policies |
| Editor | One `Gameplay Ability System` Inspector gathering the actor's loadout, attributes, input map, physics and diagnostics, with a live readout in play mode; ability Inspector with draggable timeline lanes per step, derived-type fields grouped one section per declaring class (phases, combo window and grace, movement, displacement, tag windows, effect triggers, cues) and live validation, embedded-effect editing with its own validation box, an effect Inspector for every effect type, asset-naming enforcement with a one-click rename, edit-time query gizmos that share the runtime geometry and filters, an Add button on every missing section of the actor panel (offering the project's concrete attribute sets, never the base), and the Ability Debugger window: live readout of a selected actor, an activation audit that runs the runtime's own `EvaluateActivation`, the counters, and editor-owned slow-motion and hit-stop controls |
| Diagnostics | Per-actor timestamped `AbilityEventHistory` carrying the rejection code of every refused activation and the cancel tag of every ending, `AbilityDiagnostics` with its master switch and allocation and timing counters for target queries, effect application and tasks, `GAS.*` profiler markers, `AbilitySystemDebugger` Console readout, `AbilityDebugDraw`, `DebugDrawEffectDefinition` |
| Persistence | `AbilityPersistence.Capture` and `.Restore` over a versioned `AbilitySaveRecord` of charges, attribute base values, active effects — cooldowns included — with their Set By Caller numbers, and loose tags; `IAbilitySaveResolver` to turn ids back into assets, `AbilityOfflinePolicy` for the time between sessions, `IAbilitySaveMigration` for an older schema, and a report naming everything that could not be honoured |
| Samples | One importable sample, Melee Combat: combo, roll, block with a parry window, target assist, damage, poise, knockback, cues, player input and a minimal AI, plus a profiling scene with the counters on screen |
| Consumer hooks | `IAbilityReplicationSink`, `GameplayEffectContainer.EffectBlocked` (where a parry reward hangs), `AbilityDiagnostics.EventRecorded`, `CommonGameplayTags` including `State.Invulnerable` |

### Planned

Every entry below is specified in
[`Documentation~/ROADMAP.md`](Documentation~/ROADMAP.md), which is the only place
unbuilt work is tracked.

| Planned | Tracked as |
| --- | --- |
| Per-member API documentation, an editor namespace, the `1.0` freeze | Milestone 1 |
| `SpawnProjectileAndWait` and projectile effects | Blocked on a consumer |
| Team and faction target filters | Blocked on a consumer |
| Costs for resources that are not attributes | Blocked on a consumer |
| Effect execution calculations as assets | Blocked on a consumer |
| Generated attribute accessors | Blocked on a consumer |

Replication and client prediction are **not** planned: the package targets
single-player games, and `IAbilityReplicationSink` is the seam a netcode layer
would hang on.

### Current limitations

Intentional, and documented so consumers and contributors do not mistake planned
APIs for implemented behavior:

- By default an `AbilitySystem` runs one ability at a time, and a combo is one
  ability. Concurrency exists only where an author opted in with a group tag and
  an exclusion policy.
- A step branches on a tag the owner holds, and on nothing else. There is no
  expression, no comparison and no per-branch condition delegate.
- Effects execute at configured frames. Instant, duration, infinite and periodic
  policies all run. Damage lands on one authored attribute per effect; a poise
  meter is a second damage effect naming a second attribute, not a field.
- Effects sharing a trigger frame and a shape perform one physics query between
  them; two different shapes on one frame are two queries.
- Cues are presented locally and never pooled by the package itself: the
  dispatcher hands a presenter a stable handle and the lifecycle, and the
  presenter owns whatever it spawned.
- The package does not own health, AI decision-making, locomotion motors,
  networking, prediction, or save data. Abilities may own kinematic displacement
  windows (meters over frames), and movement reaches the world only through
  `IAbilityMotor`; velocity stays with the owner's motor.
- `SpawnProjectileAndWait` is not implemented: it waits for a consumer that
  actually fires projectiles.

## Requirements

- Unity `6000.6` or newer.
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
2. Create an ability with **Create > Fofuxo > Abilities > Timeline Ability**
   for an action authored as frames, or **> Ability** for one that runs its
   own flow in code.
3. Configure its identity, target rules, tags and cooldown, then author one
   step per swing: clip, frame timeline and effects. Several steps make a combo.
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

`EvaluateActivation` returns the same verdict as a typed
`AbilityActivationResult`, for callers that must branch on *why* the activation
failed. Branch on `Rejection`, never on the text of `Message`:

```csharp
AbilityActivationResult result = abilitySystem.EvaluateActivation(ability, context);
switch (result.Rejection)
{
    case AbilityActivationRejection.None:
        abilitySystem.TryActivate(ability, context);
        break;
    case AbilityActivationRejection.OutOfRange:
        MoveCloser();
        break;
    case AbilityActivationRejection.OnCooldown:
        PickAnotherAbility();
        break;
    default:
        Debug.Log(result.Message);
        break;
}
```

`TryActivate` has an overload reporting the same result, so a caller that only
wants the reason when the ability fails to start pays for one evaluation:

```csharp
if (!abilitySystem.TryActivate(ability, context, out AbilityActivationResult result))
{
    Debug.Log(result);
}
```

### Validating a loadout

`AbilityLoadout.TryValidate` reports empty slots, missing ability IDs, IDs
repeated across two abilities, and abilities that fail their own validation. The
`out string` form stops at the first problem; the `List` form collects every one
and is what the loadout Inspector draws, so the audit and the runtime can never
disagree about the rules:

```csharp
var issues = new List<AbilityLoadoutValidationIssue>();
if (!loadout.TryValidate(issues))
{
    foreach (AbilityLoadoutValidationIssue issue in issues)
    {
        Debug.LogWarning($"[{issue.Issue}] slot {issue.SlotIndex}: {issue.Message}", issue.Ability);
    }
}
```

A duplicate ID matters because `FindAbility` returns the first match: the second
asset is granted but unreachable by ID.

### What a handler may call back into

Every activation event is raised **outside** the state change it reports, so a
handler never sees a half-built or half-torn-down activation. That is the whole
contract, and everything below follows from it:

| Event | The world the handler sees | It may |
| --- | --- | --- |
| `AbilityStarted` | The activation is already in `ActiveInstances` and already holds its granted tags | Activate, cancel — including the activation it was just told about — and query anything |
| `AbilityCompleted` / `AbilityCancelled` | The activation is already gone: removed from `ActiveInstances`, tags released, tasks cancelled, cooldown started | Activate the next ability, **including the same one again** — the one-ability-at-a-time default no longer blocks it |
| `StepTransitioned` / `AbilityStepAdvanced` / `AbilityPhaseChanged` | Mid-tick, with the activation running on its new step | Cancel the activation, or another one; the tick re-checks that an instance is still active after every handler |
| `GameplayCueTriggered` | Mid-tick, right after the frame that fired the cue | The same. A cue is cosmetic and a presenter that ends an activation is doing gameplay, but it is not *unsafe* |
| `StepIntentChanged` / `StepTagWindowChanged` / `AbilityWhiffed` | As above | The same |

Two things follow that are worth stating outright:

- **"When the swing ends, start the next one" is safe**, which is the pattern a
  boss brain reaches for first. It is a re-entrant activation raised from inside
  the system's own teardown, and it works because the teardown finished first.
- **A handler that throws cannot corrupt the system.** The state change had
  already completed, so the exception reaches your code with the owner clean —
  no half-started ability, no leaked task, no tag held forever. It does still
  propagate out of `Tick`, and it does skip any handler after it on the same
  event, so catch what you throw.

The one thing to avoid is unbounded recursion of your own making: a handler that
activates on `AbilityStarted`, whose activation raises `AbilityStarted` again,
recurses until the stack runs out. The package does not guard against that,
because a depth limit would be a guess about your game.

`EventReentrancyTests` pins every row of that table.

### Validating against the actor

Two things an asset cannot check about itself, because they are facts about the
actor rather than about the ability, are decided by `AbilitySystem` on the same
side-effect-free path as every other activation rule:

- **A motor.** `AbilityDefinition.RequiresMotor` is true when the activation
  would move its owner — an activation-time target-assist approach, or any step
  with a displacement window or an approaching assist. On an actor with no
  `IAbilityMotor` and no Rigidbody, the activation is refused with
  `AbilityActivationRejection.MissingMotor` rather than playing an animation
  that travels nothing. `AbilitySystem.Motor` is null on such an actor, and
  `HasMotor` is the question to ask.
- **Costs and tags**, which were already checked here.

`TargetAssistDefinition.approachTarget` defaults to `true`, so an ability with
any target assist needs a motor unless the approach is turned off.

### Validating an effect

`GameplayEffectDefinition.TryValidate` covers the mistakes that produce silence
rather than an error, and a subclass overrides it to add its own:

- a modifier with no attribute assigned, or one that folds in an attribute with
  a coefficient of zero — the attribute is read and thrown away;
- a `Duration` effect whose duration resolves to zero, and an `Instant` effect
  carrying a period it will never run;
- **`Shape` targeting with an empty Target Layers mask.** An empty mask is not
  "nothing": the query falls back to every layer in the project, so the swing
  hits terrain, triggers and the attacker's own colliders. Whether the layers
  you *did* pick are the ones damageable actors live on is a question about your
  project, not about the package, and it is left to the consumer;
- an empty entry in any tag list, a null or self-referencing overflow effect,
  and overflow effects authored against a stack limit of zero;
- `DamageEffectDefinition`: zero damage, linear falloff outside `Shape`
  targeting, a knockback duration with no knockback to spend it on;
- `PhysicsForceEffectDefinition`: an empty event tag, or a level curve with no
  keys;
- `DebugDrawEffectDefinition`: a duration policy other than `Instant`, or a zero
  draw duration.

An effect is normally reached through the step that fires it, so
`AbilityStep.TryValidate` validates every effect its triggers point at and
`AbilityDefinition.TryValidate` does the same for its parry effects. The message
names the step, the trigger and the effect asset, and the ability Inspector draws
it — a broken effect embedded in a combo is reported by the combo.

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
        | walks steps,            +--> targeting, tags, cost, cooldown
        | ticks their frames      |
        |                         +--> AbilityStep[]
        |                                +--> clip and frame windows
        |                                +--> effect and cue triggers
        |                                +--> displacement
        v
GameplayEffectDefinition
        |
        | one GameplayEffectSpec per target, per application
        v
GameplayEffectContainer on the target
        |
        +--> instant base-value changes
        +--> ActiveGameplayEffect for duration and infinite effects
        +--> granted tags, immunity, removal
        +--> game-owned receivers, projectiles, VFX, audio, etc.
```

`AbilityDefinition` is immutable authoring data. Every activation creates one
`AbilityInstance` holding the owner, target, **which step is running**, elapsed
time, current frame, current phase, and registered hits. A combo is that single
instance walking its steps, so cost, cooldown and granted tags are paid and held
once for the whole chain. Cooldowns, loose tags and granted-tag counts live in
`AbilitySystem`.

The `Animator` is presentation: the step's clip is played through an
`AbilityAnimationPlayer`, while the step's frame timeline stays the gameplay
authority.

## Authoring an ability

### Identity and steps

There are two ability assets, and which one you create is the first decision:

- **Ability** (`AbilityDefinition`, menu `Fofuxo/Abilities/Ability`) has no
  timeline at all — no steps, no frames, no phases. It activates, runs whatever
  its derived type does in `OnActivated`, and stays active until something ends
  it: `TryCompleteActiveAbility`, a cancel, or the owner going away. That is the
  shape for an ability whose animation is played elsewhere, that waits on a task,
  or that has none.
- **Timeline Ability** (`TimelineAbilityDefinition`, menu
  `Fofuxo/Abilities/Timeline Ability`) adds the ordered `Steps` — a swing, or a
  combo — and with them a length, phases, frame-scheduled effects and cues, and a
  last frame to complete on.

Everything else is identical, because it lives on the base: cost, cooldown,
range, facing, target assist, tags, charges, exclusion, cancellation, activation
input, AI weight, and the preview clip. A timeline is a *kind* of ability, not
the shape every ability has — the same split Unreal draws when it keeps montages
out of `UGameplayAbility`.

- `Ability ID` is a stable identifier such as `character.melee.combo`.
- `Steps` is an ordered list on the timeline ability. One step is a single
  action; several make a combo.
- `Advancement` (shown once there is more than one step) is `Automatic` (each
  step chains straight into the next) or `Manual` (each step waits for another
  input inside its combo window).

Each step carries its own:

- `Animation Clip`, which supplies the frame rate for that step's timeline.
- `Animation Blend Duration` for the crossfade into it.
- Startup / active / recovery frames, action windows, displacement, effect
  triggers and cue triggers.

What stays on the ability is what is decided once per activation: cost,
cooldown, range, facing, activation tags, cancel policy and AI weight.

### Extending an ability

An ability's identity is its **asset**, not its class: a new asset is a new
ability, the way a new Blueprint class is one in Unreal. Do not write a class per
skill — that is a `.cs` and a recompile for something the Inspector already
expresses.

Derive from `AbilityDefinition` for a new *kind* of ability, when behaviour
cannot be expressed as data. A subclass may add serialized fields — the Inspector
draws whatever its hand-written layout does not, **one section per class that
declares fields, right under the Ability Id and before everything every ability
shares**, parent before child, so an ability reads as what makes it this kind of
ability first and the common parameters after — override
`TryValidate` to check them, and override the lifecycle hooks:

```csharp
protected internal virtual void OnActivated(AbilityInstance instance);
protected internal virtual void OnStepStarted(AbilityInstance instance, int stepIndex);
protected internal virtual void OnCompleted(AbilityInstance instance);
protected internal virtual void OnCancelled(AbilityInstance instance, GameplayTag cancelTag);
```

They run before the matching public event, so the ability's own logic settles
before anything outside observes it.

A rule of the game's own about *whether* the ability may start — "the
inventory has room", "there is something to pull" — overrides
`CanActivateAbility`, the override point Unreal's GAS gives the same question:

```csharp
protected internal virtual bool CanActivateAbility(
    AbilitySystem system, in AbilityContext context, out string reason);
```

It is asked last, after every package check, by `EvaluateActivation`,
`CanActivate` and every attempt alike, and a refusal is reported as
`AbilityActivationRejection.ConditionNotMet` with the reason as its message. It
is a question: side-effect free, and the same answer when asked twice.

Numbers the ability decides for its own effects — a sprint's speed, the stamina
each installment of its cost takes — go on the spec as Set By Caller magnitudes
in `ConfigureOutgoingSpec`, which runs on every spec the ability makes for its
cost, its cooldown and its Active Effects:

```csharp
protected internal virtual void ConfigureOutgoingSpec(GameplayEffectSpec spec);
``` The definition is a shared asset, so
per-activation state belongs on the `AbilityInstance` or on the owner, never in
a field of the subclass.

Derive from `AbilityDefinition` for an ability that owns its own flow, and from
`TimelineAbilityDefinition` when it also runs steps. Without a timeline there is
no last frame to complete on, so the activation runs until something ends it: the
subclass calls `TryCompleteActiveAbility` when its work is done, a task it
started reports back, or a consumer cancels it. `OnStepStarted` and
`CanAdvanceStep` exist only on the timeline type, because only it has steps to
report. An asset of the plain base type warns in the Inspector — nothing in it
can ever end the activation.

Prefer a `GameplayEffectDefinition` when the behaviour is a reusable consequence:
one effect asset serves every ability that triggers it, while a hook only ever
serves its own type.

The Inspector's bottom preview pane is the same native interactive panel Unity
shows for a `.anim` asset, including its model selector, playback controls, and
scrubber. The ability holds one `Preview Animation Clip`: put any animation there
and use the native panel below — it does not have to be a clip this ability
plays, which is the point, since inspecting an unrelated clip is half of what the
pane is for. It never shows a step's gameplay `Animation Clip` on its own; empty
means no preview, and `Preview This Step` inside a step copies that step's clip
into the field. It never changes activation, timing, or validation, and it is
there for every ability, timeline or not.

### Targeting

Activation can require a target and validate:

- Minimum and maximum planar range.
- Maximum facing angle.
- Target presence.

An effect may have a different physical query volume. For melee effects, make
sure the activation range and effect reach overlap; otherwise an AI can stop at
a valid activation distance while the hit query still cannot reach the target.

An attack may assign a `TargetAssistDefinition` as its `Target Assist`. It is
targeting data, not an ability: it has no timeline, cost, cooldown, tags or
animation of its own. The assist resolves before the animation and effects. It
runs a `HitShape` cone — the proximity circle is that cone's inner radius, so a
target beside the owner matches whatever the angle — and produces an
`AbilityTargetData`. The activation's target, facing and approach are then read
back off that one result instead of being written into the context field by
field. A zero assist `Search Distance` makes the cone reach twice the proximity
radius; when both values are zero, target search is disabled. Assist-driven
approach and a step's own displacement are mutually exclusive until concurrent
movement tasks are available.

`Lock Policy` decides what a later step does with the target the activation
already committed to:

| Policy | Behaviour |
| --- | --- |
| `Reacquire` (default) | Query again; the combo follows whoever is in front now. |
| `KeepWhileValid` | Keep the current target while the query still finds it. A combo cannot be stolen mid-chain by an enemy that walks closer. |
| `Lock` | Never change target once acquired. The owner still rotates and approaches, so the chain follows a moving enemy. |

Target loss stays where it was: an ability with `Requires Target` whose target is
destroyed cancels with `Cancel.TargetLost`, and one without simply keeps
swinging.

`Stopping Gap` is the space left **between the two bodies**: the owner's own
collider reach along the approach direction comes off the measured distance, so
a gap of zero ends the approach with the two colliders touching, whatever the
size of either character.

A step carries its own optional `Target Assist`. When it has one, that step
resolves it as it starts — rotating the owner and closing the gap again
— which is what makes a combo follow an enemy that moved between swings, or turn
toward a different one. A step without an assist keeps the target the activation
chose. A step-level assist overrides the ability-level one for that step; the
ability-level field still resolves the first step, so existing assets keep their
behavior.

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
owner's forward when it is empty. Author separate step tag windows such as
`State.Rolling`, `State.Invulnerable`, and `State.MovementLocked`, then set the
step's displacement distance and frame window for travel. Game code may poll
`HasTag` or `IsStepTagWindowOpen`, and may react on the exact boundary through
`StepTagWindowChanged` (for example, to filter a damage hitbox during i-frames).

### Displacement

Rolls, dashes and lunges own their travel as data on the **step**: direction
mode (`Context`, `OwnerForward`, `TowardTarget`, `AwayFromTarget`), distance in
meters, and a 1-based frame window on that step's timeline. Each step of a combo
can therefore carry its own lunge.
`AbilitySystem` resolves the direction once at activation and runs the travel as
a `MoveByDistanceTask` on the activation, at constant speed while the window is
open; cancelling or completing the ability cancels the task, which stops travel
immediately. Travel is planar, kinematic and unswept by default, like root
motion: it never touches velocity, so the owner's motor should clear competing
planar velocity at activation. Where the travel reaches the world is the
[motor seam](#movement-and-the-motor-seam).

### Timeline

The timeline uses one-based frames:

```text
1 .. Startup End Frame        Startup
next .. Active End Frame      Active
next .. Recovery End Frame    Recovery
```

Every step has its own timeline, restarted from frame 1 when the combo advances
into it. Effects trigger once when their configured frame is reached. The custom
Inspector draws each step as lanes — phases, combo window and late grace,
movement, displacement, tag windows, effect triggers, cues — and every marker is
dragged to the frame it fires on; the numbers stay one foldout away for the
frame that is faster to type than to aim at. The step header carries the
resolved frame rate, frame count and duration, and the combo's total duration
sits above the lanes. When an `Animation Clip` is assigned,
its complete frame count is the minimum resolved end of the recovery phase.
`Recovery End Frame` can extend the timeline beyond the animation, or supplies
the end when there is no clip, but cannot cut an assigned animation short. Use
`Movement Unlock Frame` and the combo windows to release control or continue the
combo before the visual animation finishes.

Tag windows also use inclusive, one-based frame bounds. A zero end frame holds
the tag through the end of the step. Windows are counted, so overlapping windows
or an ability-wide grant of the same tag cannot remove each other's state. Every
open window closes before its step advances or its ability completes, cancels,
is disabled, or is destroyed.

### Tags and cancellation

- Required tags must be present.
- Blocked tags must be absent.
- Granted tags exist only while the ability is active.
- Step tag windows exist only over their authored inclusive frame range.
- Loose tags are controlled by game code for external states such as stun,
  knockdown, or death.
- A cancel request carries a `GameplayTag`, and `Cancel Policy` declares what
  the ability accepts: `Anything` (the default), `Only Listed Tags` against its
  `Cancelled By Tags`, or `Nothing`. A game names its own reasons in the same
  namespace without editing the package. `ForceCancelActiveAbility` ignores the
  policy, which is what death, disable and destruction use.

#### The tags the package raises

An ability ends in exactly one of two ways, and the tag is what says which of
them happened *and why*. `AbilityCompleted` means the last step ran out;
everything else arrives as `AbilityCancelled` carrying one of these:

| Tag | Raised when | What a consumer should read into it |
| --- | --- | --- |
| `Cancel.Manual` | The game asked, through `TryCancelActiveAbility`, `TryCancelAbility`, `TryCancelActiveStep` or `ForceCancelActiveAbility` | A deliberate decision — a roll out of a swing, a death, a cutscene |
| `Cancel.TargetLost` | The ability requires a target and the target went away, or a step's targeting prelude found nothing | The activation was valid and the world changed under it |
| `Cancel.PhysicsForce` | An `Event.PhysicsForce` reached an owner whose activation does not itself hold `State.PhysicsControlled` | The owner is being pushed and is no longer driving itself |
| `Cancel.StepTimeout` | A waiting step ran out of timeline under a timeout policy that ends the ability as a cancellation | The player did not continue the combo; not a failure |
| `Cancel.Superseded` | Another activation took this one's place under `Cancel Same Group` or `Cancel Any Active` | Normal concurrency, not an error — the replacement is already running |
| `Cancel.Failed` | An ability hook, a gameplay effect, a damage receiver or an event handler threw while the activation was executing | A bug. The exception is logged first, and the activation is torn down rather than left half advanced |
| `Cancel.OwnerTeardown` | The `AbilitySystem` was disabled or destroyed, or its GameObject went away | There is no actor left. A handler must not start anything, queue anything, or touch the owner |

The last two are the ones worth handling separately. Before they existed both
arrived as `Cancel.Manual`, so a handler that resumed a boss phase after a
deliberate cancel also resumed it after a crash and on scene teardown.

## Effects and damage

`GameplayEffectDefinition` is the one effect asset. It is concrete: an effect
that only changes attributes and grants tags needs no code at all. Create one
from `Fofuxo/Abilities/Effects/Gameplay Effect` and fill in three blocks.

| Block | What it decides |
| --- | --- |
| `Targeting` | `Owner`, `Ability Target`, or a `Shape` query with its filter, target limit and `Restrict To Ability Target` |
| Lifecycle | `Duration Policy` (`Instant`, `Duration`, `Infinite`), `Duration`, `Period`, `Execute Period On Application` |
| Modifiers | Attribute, operation, and a `GameplayEffectMagnitude` per change |
| `Stacking` | `Stack` / `Refresh` / `Ignore`, per source or per target, a stack limit, and what an overflow does |
| Tags | `Effect Tags` (what this effect is), `Granted Tags`, `Application Required` / `Blocked Tags`, `Granted Immunity Tags`, `Remove Effects With Tags` |

A magnitude is `base + coefficient * capturedAttribute`, optionally curved by
the effect level. The capture is explicit: `Source` or `Target`, and `Snapshot`
(read once at application) or live (re-read while the effect is active).

Every application produces a `GameplayEffectSpec` — the per-use data — and a
`Duration` or `Infinite` one produces an `ActiveGameplayEffect` named by a
`GameplayEffectHandle`. The definition is never written to.

```csharp
GameplayEffectHandle handle = GameplayEffectContainer.For(target)
    .Apply(new GameplayEffectSpec(hasteEffect, attacker, target, level: 20))
    .Handle;

GameplayEffectContainer effects = GameplayEffectContainer.Find(target);
effects.GetRemainingDuration(handle);   // 0 once it is gone, never a wrong answer
effects.TryRefresh(handle);
effects.TryRemove(handle);
```

Subclass `GameplayEffectDefinition` only when the effect also does something the
attribute layer cannot express. Override `Execute` for the consequence and
`CaptureOwnMagnitudes` for any magnitude you author outside the modifier list:

```csharp
using Fofuxo.GameplayAbilitySystem;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Abilities/Effects/Example")]
public sealed class ExampleEffectDefinition : GameplayEffectDefinition
{
    protected override bool Execute(GameplayEffectSpec spec, bool periodic)
    {
        Debug.Log($"Applied by {spec.Source} to {spec.Target} at level {spec.Level}");
        return true; // false refuses the whole application
    }
}
```

`DamageEffectDefinition` is the one damage effect, and it is such a subclass:
the shared `Targeting` block says *who* is hit and the rest of the asset says
*what the hit does*, so melee arcs, wide swings, lunges and explosions are
configurations rather than separate classes:

| Field | What it controls |
| --- | --- |
| `Targeting.Shape.Kind` | `Sphere`, `Box`, `Capsule`, `Cone` or `Ray` |
| `Targeting.Shape.Origin` | `OwnerLocal` follows the attacker; `AbilityAimPoint` places it where the activation pointed |
| `Targeting.Filter` | Who inside the shape counts: owner exclusion, alive state, required and blocked target tags, line of sight, maximum count |
| `Targeting.Restrict To Ability Target` | Off lets one effect sweep everyone in the shape |
| `Target Attribute` | The attribute the damage is subtracted from: the target's health, or its poise |
| `Damage` | A `GameplayEffectMagnitude`, so attribute scaling and level curves work here like anywhere else |
| `Linear Falloff` | Fades damage to zero at the edge of the shape |
| `Knockback Direction`, `Horizontal` / `Vertical Knockback`, `Knockback Duration` | The push the hit authors; the target's reaction ability runs it |
| `Reaction Event Tag` | What the target is asked to do about it: `Event.HitReaction`, `Event.Knockdown`, or empty for a tick that should not flinch |
| `Can Be Parried` | A target holding `State.Parrying` refuses the hit, and its container says so |

This replaced four near-identical effect classes that differed only in the
physics query. They had already drifted apart by copy: only one of them ever
gained a stun duration.

Damage lands on an attribute and nowhere else. Only an actor with an
`AttributeSet` is a damage target; the effect subtracts from `Target Attribute`
through the target's container, with the attacker as the change's source, and
obeys application tags and immunity like any effect. On top of the authored
Application Blocked Tags it refuses, by rule of its kind, a target holding
`State.Dead` or `State.Invulnerable`, and `State.Parrying` when the hit can be
parried. A refused hit does not consume a target slot, and the target's
`GameplayEffectContainer.EffectBlocked` fires with the spec — which is where a
parry reward hangs: the refusal already happened, the reward is what is left.

### One query per trigger frame

Acquisition and filtering are separate, and only acquisition is shared.

`HitShape` runs the physics and hands back contact points and normals.
`AbilityTargetQuery` resolves each collider to its actor and ranks the result by
planar distance with a small angular bias toward the query direction, breaking
exact ties by entity id. The ordered result is an `AbilityTargetData`, carrying
actors, colliders, hit points, normals, the query origin and per-target
directions and distances.

**Every effect that fires on the same trigger frame of the same step with the
same shape reads one acquisition.** Two effects used to run one overlap each, so
the fourth hit of a combo could damage one enemy and push another out of what
the author wrote as one volume. Filtering stays per effect: two effects can
still act on different targets, but never disagree about what is there.

The activation's acquisition is on `AbilitySystem.ActiveTargetData`. A consumer
that keeps a result past the current frame takes a `Snapshot()`, because the
buffer is reused; `PruneDestroyed()` drops actors that are gone.

Targets can also come from outside. `AbilityTargetData.FromActors` builds a
result from actors a caller already picked, and
`TryActivateWithTargets` activates with it, skipping local acquisition
entirely — a scripted sequence, an AI that chose its victim, or a consumer with
its own reticle owns the selection, and the ability owns everything after it.

Duplicate-hit rejection is per trigger **per step**, so the next swing of a combo
may hit a target the previous one already hit.

Damage shapes are instantaneous non-allocating physics queries, not temporary
Collider GameObjects.

### The hit reaction belongs to the target

Once the damage has landed, the effect asks the target to react: it sends
`Reaction Event Tag` to the target's `AbilitySystem` through
`TryHandleGameplayEvent`, with the very `GameplayEffectSpec` that landed
attached. What answers is a `HitReactionAbilityDefinition` in the target's
loadout — a timeline ability triggered by that event — and it owns the whole
reaction: the clip on its step, the lock through its granted tags and movement
lock, its cancel policy, and the knockback, which it reads off
`AbilityInstance.TriggeringSpec` and runs as an `ApplyKnockbackTask` in its own
scope, scaled by its `Knockback Scale`. Ending the reaction ends the push;
nothing is copied into a struct and nothing is handed to a consumer callback.

```text
Attack ability step
    -> DamageEffectDefinition: -N on Target Attribute, attacker as source
    -> Event.HitReaction to the target's AbilitySystem, spec attached
        -> HitReactionAbilityDefinition activates (or is refused)
            -> its step plays the clip, its tags hold the lock
            -> ApplyKnockbackTask from spec.TryGetKnockback, in its scope
```

**The decision is the target's.** The event goes through the target's own
activation rules, so an owner holding `State.Invulnerable` from a roll or
`State.Dead` plays nothing, and the attacker only counted a hit. The order
inside the effect is what makes death work: the attribute changes first, the
owner's death handler runs inside that change, and the reaction request meets
`State.Dead`. A periodic tick deals its damage and requests nothing.

Several reactions may listen for one event. Candidates are tried in loadout
order and a refused one hands the event to the next, so a guard reaction with
`State.Attacking` in its Required Tags sits above the plain flinch and takes the
hit only while the owner swings; a heavy hit authored with `Event.Knockdown`
reaches a different ability altogether. A trigger with `Restart When Active`
restarts a running reaction on a repeated hit instead of being consumed by it.

The package still provides no health component: `Target Attribute` is whatever
attribute the game's `AttributeSet` holds, and death, kill credit and the HUD
read the attribute's change and its source.

`PhysicsForceEffectDefinition` is the same shape for physics: the trigger
supplies a level, the effect sends `Event.PhysicsForce`, and a granted
target-owned reaction ability holds `State.PhysicsControlled` while
`AbilityPhysicsBody` hands locomotion to a dynamic Rigidbody. Push, pull, and
launch share this path; slow remains an attribute effect.

## Costs, charges, buffering, and whiffs

Paying for an ability works the way Unreal's GAS does it: with effects.

- **Cost.** `Cost Gameplay Effect` is an Instant effect applied to the owner.
  `CheckCost` asks, without applying anything, whether one of its additive
  modifiers would take an attribute below its minimum; activation is refused
  with `InsufficientAttribute` when it would. An owner immune to the cost effect
  pays nothing, which is how a "free casting" buff works.
- **Paying again while active.** `Cost Period` pays the cost again every so many
  seconds while the activation runs — a sprint on stamina, a channel on mana.
  The first installment is what activation checks; the first one that cannot be
  paid ends the activation with `Cancel.InsufficientCost`, whatever its cancel
  policy.
- **Cooldown.** `Cooldown Gameplay Effect` is a Duration effect whose granted
  tags mean "on cooldown". `IsOnCooldown` looks for those tags, whatever applied
  them, and `GetCooldownTimeRemainingAndDuration` reads the effect. The Cooldown
  Start Policy applies it on commit or when a committed activation completes.
- **Commit.** Paying the cost, spending a charge and applying an On Commit
  cooldown is one step. `AbilityCommitPolicy.OnActivation` commits as the
  ability starts; `Manual` waits for the ability to call `TryCommitAbility` —
  after a wind-up, say — which checks everything again and ends the activation
  with `Cancel.CommitFailed` when it can no longer pay.
- **Numbers the ability decides.** A magnitude can read its flat part from the
  spec under a tag — Set By Caller. The ability fills those numbers in
  `ConfigureOutgoingSpec`, which runs on every spec it makes for its cost, its
  cooldown and its Active Effects, so a sprint's speed and stamina per second
  live on the sprint while its effects stay generic.
- **Active Effects** are applied to the owner on activation and removed when it
  ends, completed or cancelled, so a speed buff never outlives the ability that
  granted it.

`MaxCharges` limits consecutive uses; charges refill one per
`ChargeRestoreTime`, or all at once when the cooldown ends when the restore
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
and no central manager: every `AbilitySystem` owns a `GameplayCueDispatcher`
(`abilitySystem.Cues`), presenters register with it, and every cue the actor is
the subject of goes through it. Cues never change gameplay state, and the
package makes that a property of the type rather than a rule: a presenter is
handed `GameplayCueParameters`, a read-only struct of definitions, actors,
numbers and geometry, and nothing it could change.

**Four lifecycles.** A burst cue is `Execute`d once. A persistent cue is
`Add`ed, refreshed with `WhileActive` while it lives, and `Remove`d — under one
`GameplayCueHandle` from start to end, so a presenter that pools keys its
instance by the handle and returns it on `Remove`. A presenter that registers
while persistent cues are live is caught up with `WhileActive` for each of
them, and never with a replayed burst.

**Three sources.** A step's `GameplayCueTrigger` raises at its frame, carrying
`Landed` or `Missed` — effects apply before cues on the same frame, so the step
knows — and `Suppress On Miss` keeps a swing that met air quiet. An effect's
`Cue Tag` is executed on the target when an instant effect lands and added when
a duration or infinite one activates, refreshed with the stack count when it
stacks and removed with the effect, by expiry, removal or teardown. And
`TriggerGameplayCue` raises one from code.

**Outcomes and replacements.** A cue carries a `GameplayCueOutcome`. An
application that was refused raises the effect's `Cue Replacement` for that
outcome — `Blocked`, `Immune`, or `Parried` for a parrying target that refused
a parryable hit — or nothing when none is authored; an empty replacement
suppresses the cue for that outcome. An `IGameplayCueFilter` registered on the
dispatcher runs before the presenters and may rewrite or suppress any cue, for
the rules that belong to the game rather than to one asset. Identical bursts
inside one frame — two effects on one hit both saying `Cue.Impact` — are
batched into one.

```csharp
// A pooling presenter: one method, keyed by the handle.
public sealed class ImpactPresenter : MonoBehaviour, IGameplayCuePresenter
{
    private readonly Dictionary<GameplayCueHandle, ParticleSystem> loops = new();

    private void OnEnable() => GetComponent<AbilitySystem>().Cues.AddPresenter(this);
    private void OnDisable() => GetComponent<AbilitySystem>().Cues.RemovePresenter(this);

    public void OnGameplayCue(in GameplayCueParameters cue)
    {
        switch (cue.Event)
        {
            case GameplayCueEvent.Execute:
                pool.Burst(cue.Cue, cue.Location, cue.Normal, cue.Magnitude);
                break;
            case GameplayCueEvent.Add:
            case GameplayCueEvent.WhileActive:
                if (!loops.ContainsKey(cue.Handle)) loops[cue.Handle] = pool.Loop(cue.Cue, cue.Owner);
                break;
            case GameplayCueEvent.Remove:
                if (loops.Remove(cue.Handle, out var loop)) pool.Return(loop);
                break;
        }
    }
}

// Or subscribe: the same parameters, as an event.
abilitySystem.GameplayCueTriggered += cue =>
{
    if (cue.Cue == new GameplayTag("Cue.EnemyAttackTell") && cue.Event == GameplayCueEvent.Execute)
    {
        SpawnTellEffect(cue.Owner);
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
becomes a reaction test instead of a guess. The hit itself is the damage
effect's cue: `Cue.Impact.Hit` on the target, with the damage as magnitude and
the contact point as location, and a `Parried` replacement when the author
wants the clang.

## Combos and steps

A combo is one `AbilityDefinition` whose `Steps` list has more than one entry.
There is no separate sequence asset, no separate cooldown to keep in sync, and
no second activation path: it is granted, activated, cancelled and cooled down
exactly like any other ability.

```csharp
AbilityContext context = AbilityContext.FromTarget(gameObject, target);

if (abilitySystem.CanActivate(combo, context, out string reason))
{
    abilitySystem.TryActivate(combo, context);
}
```

`Advancement` decides how the ability walks its steps:

- **Automatic** — each step chains into the next as soon as it ends. Boss combos
  use this.
- **Manual** — each step waits for another input inside its combo window. Player
  combos use this.
- **On Event** — each step waits for a gameplay event carrying the ability's
  `Step Advance Event` tag. An animation notify or a landed hit drives a scripted
  sequence this way.
- **On Condition** — the ability's `CanAdvanceStep` override, or the system's
  `StepAdvanceCondition` delegate, is polled while the window is open.

Every mode but Automatic waits for something, and all of them share the same
frame windows: the mode says *what* has to happen, the window says *when* it may.

- `Movement Unlock Frame`: movement becomes available while the step runs.
- `Combo Continue Frame`: earliest frame that may start the next step.
- `Combo Input End Frame`: last inclusive frame that accepts the next input.
  Zero keeps the window open until the step ends.
- `Combo Input Grace Frames`: late grace. Frames past the input end frame that
  still accept a request; one arriving there advances immediately, because the
  window it belonged to has already closed.

`TryQueueStepAdvance` buffers an early input. When the running step reaches its
`Combo Continue Frame`, the instance advances to the next step and restarts its
timeline.

When a waiting step runs out of timeline with nothing to advance it, `On Timeout`
decides what happens:

- **Complete Ability** (default) — the ability ends on the step it reached. This
  is what a player combo wants, and there is no separate cancellation path to get
  wrong.
- **Advance Step** — chain anyway, as Automatic would.
- **Cancel Ability** — end as a cancellation carrying `Cancel.StepTimeout`.
- **Hold Last Step** — stay on the final frame and keep waiting. Only an advance,
  an explicit completion, or a cancellation ends it.

A step may also carry `Branches`. The first branch whose tag the owner holds
decides where the step advances to, so one combo can fork on a stance, a phase,
or a buff an earlier step granted. A branch may point backwards, which is how a
step loops. With no branch, or none matching, the activation moves to the
following step.

Reading the state:

```csharp
abilitySystem.ActiveAbility           // the combo asset
abilitySystem.ActiveStep              // the AbilityStep running now
abilitySystem.ActiveStepIndex         // 0-based
abilitySystem.IsComboWindowOpen       // an input would advance right now
abilitySystem.HasQueuedStepAdvance    // a request is buffered and pending
abilitySystem.QueuedStepIntent        // that request, and what became of it
abilitySystem.ComboInputDeadlineFrame // last frame accepting one, grace included
abilitySystem.ComboInputTimeRemaining // seconds until it closes
abilitySystem.StepTimeRemaining       // seconds left in the step
abilitySystem.IsHoldingLastStep       // waiting under Hold Last Step
abilitySystem.LastTransition          // why the step last changed, or ended
```

`AbilityStepAdvanced` fires whenever the combo carries on into its next step.
`StepTransitioned` fires for every step change *and* every ending, carrying the
reason. `StepIntentChanged` fires whenever a buffered request changes state, so
every accepted request can be accounted for: it is consumed exactly once, or it
expires — never silently dropped. A request arriving past the deadline is refused
on the spot and recorded as `Rejected`.

### Cancellation scopes

Cancellation propagates in three scopes, so a request ends as much as it means to
and no more:

```csharp
abilitySystem.TryCancelActiveStep(tag);       // ends the step; the ability's
                                              // timeout policy decides what next
abilitySystem.TryCancelAbility(ability, tag); // ends one named activation
abilitySystem.TryCancelActiveAbility(tag);    // ends every activation that accepts
abilitySystem.ForceCancelActiveAbility(tag);  // ends every activation, policy aside
```

A step whose targeting prelude finds nothing, on an ability that requires a target
and has none left, ends the activation with `Cancel.TargetLost` rather than
leaving the ability running with a step that never started.

### Ability groups and mutual exclusion

By default an activation refuses to start while anything is running: one ability
at a time. `Exclusion Policy` on the ability opens that up, scoped by its
`Group Tag`:

- **Block While Any Active** (default) — one ability at a time.
- **Block While Same Group Active** — only an ability in the same group blocks it;
  abilities in other groups keep running alongside it.
- **Cancel Same Group** — cancel the running abilities in its group and take their
  place, raising `Cancel.Superseded`.
- **Cancel Any Active** — cancel everything cancellable and take its place.

The cancelling policies are still refused when a running ability's own `Cancel
Policy` rejects the request, and `CanActivate` reports that up front without
cancelling anything.

#### How the policies compose

The knobs above are independent, and what a game depends on is the order they
produce together. BossRush authors them so that:

```text
death  >  hit reaction  >  roll  >  attack, block
```

- **Attack and block are mutually exclusive, not ordered.** Both sit in the
  action group under `Block While Same Group Active`, so neither interrupts the
  other: one committed action at a time.
- **The roll takes the group** with `Cancel Same Group`, which is what "roll out
  of a swing" means, and grants `State.Invulnerable` while it runs.
- **The hit reaction outranks every action** with `Cancel Any Active` — except
  that it lists `State.Invulnerable` among its blocked tags, so an owner mid-roll
  refuses it. That is where i-frames are decided, and the decision belongs to the
  target.
- **Death outranks everything** with `Cancel Any Active` plus `Cancel Policy:
  Nothing`, so it ends whatever is running and nothing ends it. It grants
  `State.Dead`, which every other ability blocks on, so a dead owner starts
  nothing even after the death animation finishes.

An interrupting activation — a group policy that cancels, or an activation
trigger with Interrupt Active Ability — **decides before it cancels**. It has to:
otherwise the cancel releases the tags that would have refused it, and i-frames
lose to the hit they were meant to ignore.

`ActionPrecedenceTests` pins that whole table.

`ActiveInstances` lists every running activation, oldest first. Every singular
accessor above speaks for the oldest one, so an ability started later never takes
the primary slot from the combo that was already going. Which of several
activations moves the owner is decided by [movement priority](#movement-and-the-motor-seam).

## Ability tasks

A task is one piece of latent work belonging to one activation: a wait, a target
acquisition, a displacement. Tasks are runtime instances, never authoring data.

**A task never outlives the activation that started it.** `AbilityInstance` owns
an `AbilityTaskScope`, and the single activation teardown cancels every task in
it before any other cleanup runs, so completion, cancellation, target loss, death
and disable all stop a task through one line. A cancelled task never ticks again,
which for a movement task means not one further step of travel.

```csharp
// Inside a derived ability, or from game code holding the system.
var wait = abilitySystem.RunTask(new WaitDelayTask(0.2f));
wait.Finished += task =>
{
    if (task.State == AbilityTaskState.Succeeded)
    {
        // ...
    }
};
```

`RunTask` starts the task on the primary activation and returns null when nothing
is running; the `(AbilityDefinition, task)` overload targets a named activation.
`RunActorTask` starts one owned by the actor instead, for the rare work that
belongs to no activation — a hit reaction landing while nothing is running.
Those are released by the component, not by an ability ending.

| Task | Ends when |
| --- | --- |
| `WaitDelayTask` | the configured seconds have been ticked |
| `WaitGameplayEventTask` | a gameplay event matching its tag reaches the system |
| `WaitInputTask` | the bound input reports its edge |
| `WaitAnimationEventTask` | the animation-event bridge forwards its name |
| `AcquireTargetsTask` | the shared target query accepts at least one target |
| `MoveByDistanceTask` | the distance is spent or the duration runs out |
| `MoveTowardTargetTask` | the gap is closed, the duration runs out, or the target is lost |
| `ApplyKnockbackTask` | the push has run for its duration |

The wait tasks reuse the entry points that already exist rather than reading
devices or clips of their own. `WaitGameplayEventTask` listens on
`TryHandleGameplayEvent`, the same call that advances a step on
`Step Advance Event` — a task waiting on a tag resolves work *inside* the step
it is in, while `stepAdvanceEventTag` moves the activation *on*.
`WaitInputTask` is fed by `AbilityInputRouter.NotifyInputPressed` /
`NotifyInputReleased`, so it sees exactly what the activation input policies see
and the core keeps knowing nothing about the Input System.
`WaitAnimationEventTask` is fed by `AbilityAnimationEventBridge`, through
`EmitAbilityAnimationEvent` — deliberately a different call from
`EmitGameplayCue`, so a cosmetic cue can never release gameplay work by accident.
`AcquireTargetsTask` runs the same `AbilityTargetQuery` the effects, the target
assist and the gizmos run.

Tasks tick on the frame by default. A task may ask for the physics step instead
by overriding `TickPhase`, which is what `ApplyKnockbackTask` does: a
`MovePosition` issued from `Update` is replaced by the next one before physics
reads it, so travel driven from there loses whatever the frame rate outran.

## Movement and the motor seam

Every movement task states its direction policy explicitly. `Snapshot` resolves
the aim once and commits to it; `Track` re-reads it every tick and, for
`MoveTowardTargetTask`, re-measures the remaining gap with it. A lunge that homes
onto a dodging enemy and one that lands where it was aimed are different moves,
and neither should be an accident of implementation.

Travel is deterministic under any frame pacing: speed is remaining distance over
remaining duration, recomputed each tick, so the total is the configured distance
however the frames were cut, and a hitch redistributes the travel instead of
teleporting through it.

**Priority and conflict.** Only one task moves the owner per tick. The highest
`Priority` that has not yet moved wins, ties breaking by activation age, oldest
first — which with everything at the default is exactly the rule the single
displacement always followed. A task that loses the body follows its
`ConflictPolicy`:

- **Yield** (default) — waits without spending any of its budget, so it still
  travels its full distance once it wins.
- **Blend** — adds its delta on top of the winner.
- **Abort** — ends as failed the moment it loses the body.

`AbilityMoveTask.DisplacementPriority`, `ApproachPriority` and
`KnockbackPriority` are the shipped levels: a hit reaction outranks the timeline,
because being knocked back is something done to the owner.

**The motor seam.** Movement reaches the world only through `IAbilityMotor`. A
task produces a delta; whoever owns the body decides what that delta does to it,
under an explicit collision mode:

- **Unswept** — applied as authored, nothing swept, root-motion style.
- **Swept** — swept against the world, stopping short of the first blocker.
- **MotorAuthority** — the consumer motor decides; sliding, stepping, ground
  snapping and outright refusal are all its prerogative.

The adapters live in `Uayten.FofuxoGameplayAbilitySystem.Motors`, outside the
core, and depend on nothing but Unity and the core:

| Adapter | For |
| --- | --- |
| `AbilityRigidbodyMotor` | a kinematic Rigidbody; accumulates deltas and flushes once per physics step |
| `AbilityCharacterControllerMotor` | a `CharacterController`; swept and motor-authority travel go through `Move` |
| `AbilityDelegateMotor` | a project whose own motor cannot implement the interface directly |

A consumer that can implement `IAbilityMotor` on the component that already owns
its movement should do that instead — the adapter direction is game to package.
A project that never opted in keeps the movement it always had: with no motor
component present, `AbilitySystem` falls back to the owner Rigidbody moved with
`MovePosition`, exactly as ability displacement always did.

## Input

`AbilityInputRouter` lives in a separate assembly that references Unity's Input
System, and it reads one asset: an **`AbilityInputMap`** listing which action
activates which ability.

The map is an asset rather than a list inside the prefab, so a character swaps
its controls the way it swaps its `AbilityLoadout`, two characters share a scheme
by pointing at the same map, and the actor shows one field instead of an array
that has to be read entry by entry. It names actions rather than referencing
them: an `InputActionReference` is a sub-asset to drag per action, while a name
is picked once from the asset's own list in the Inspector and validated
afterwards — a renamed action becomes a validation error instead of a keybind
that silently stopped working.

An entry with **no ability** is still owned by the router: it enables the action
and forwards it to game code through the `ForwardedInput` event, which is what
keeps every combat keybind in one asset even when what it does is not an
ability.

`Activation Input` on the ability picks which edge activates it:

- **On Press** (default) — activate as soon as the input goes down.
- **On Hold** — activate once `Hold Duration` elapses, without waiting for the
  release. Letting go early activates nothing.
- **On Release** — activate on the release, provided the press lasted at least
  `Hold Duration`.

The router is not the only input owner. A game that reads its own devices drives
the same policies through `NotifyInputPressed` / `NotifyInputReleased` instead of
reimplementing them.

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

## Attributes and the gameplay effect lifecycle

Attributes are the numeric half of gameplay state, and gameplay effects are the
only thing with a lifetime that touches them. The design stays smaller than
Unreal GAS while preserving its most useful separation between authoring data,
per-actor state, and effect execution.

### The types

| Type | Responsibility |
| --- | --- |
| `GameplayAttribute` | Serializable stable identifier such as `Combat.Health` |
| `AttributeValue` | Per-actor base value, computed current value, limits, and attached modifier slots |
| `AttributeSet` | Game-defined component that owns related runtime attributes |
| `AttributeSetDefinition` | Shared savable initial values and limits |
| `AttributeModifier` | Attribute, operation and magnitude, attached under a slot id |
| `GameplayEffectDefinition` | Immutable effect asset: targeting, duration policy, modifiers, stacking, tags |
| `GameplayEffectMagnitude` | How one number is calculated, including its capture rules |
| `GameplayEffectSpec` | Per-application data: source, target, level, captured attributes, calculated magnitudes |
| `ActiveGameplayEffect` | Runtime state of one duration or infinite application |
| `GameplayEffectHandle` | Stable name for one application, answerable after it ends |
| `GameplayEffectContainer` | Per-actor owner of the whole effect lifecycle |

The package defines the infrastructure; games define concrete sets:

```csharp
public sealed class CombatAttributeSet : AttributeSet
{
    public int CurrentHealth => Mathf.RoundToInt(GetCurrent(CombatAttributes.Health));
    public int MaxHealthValue => Mathf.RoundToInt(GetCurrent(CombatAttributes.MaxHealth));
}
```

### Value model

- Base values are persistent per-actor state.
- Instant effects change a base value, such as health damage or healing.
- Duration and infinite effects attach modifiers that contribute to the computed
  current value, and detach them when they end.
- Operations are `Add`, `Multiply` and `Override`. Evaluation order is
  deterministic: base, additive modifiers, multiplicative modifiers as
  `1 + magnitude` factors, then the last override, then the limits.
- Every attached modifier carries a slot id, so two identical modifiers from one
  source are two slots and removing one never removes the other.
- Attribute changes emit a typed `AttributeValueChanged` with old value, new
  value, source and attribute.
- Runtime values never live in `ScriptableObject` assets.
- Regeneration is an effect, not a feature of the set: an infinite periodic
  effect, usually in the loadout's `Granted Effects`, that its Ongoing Tag
  Requirements switch off — stamina that does not refill mid-sprint.
- An initial value can name a `Max Attribute` whose current value is its
  ceiling instead of a fixed `Max Value`: Stamina under Max Stamina, so an
  upgrade that raises the ceiling raises what can be refilled. `GetCurrent`
  applies the ceiling; `Add` and `Multiply` never leave the base above it,
  while `Override` writes the base as given, which is how a save restores a
  value whose ceiling comes back with the effects restored after it. A ceiling
  cannot be capped itself.

The set aggregates; it does not decide lifetimes. Durations, periods, stacking,
overflow, immunity and tags belong to `GameplayEffectContainer`, which is added
to an actor on demand the first time an effect lands on it.

### Capture rules

A magnitude that reads an attribute declares two things, and both are tested:

- **Whose** attribute: `Source` (the actor that applied the effect) or `Target`
  (the actor it landed on).
- **When**: `Snapshot` reads it once, at application, and never again — a poison
  whose damage was decided by the attacker at the moment of the hit. Live
  re-reads it while the effect is active, so buffing the source mid-effect moves
  what the effect contributes.

Instant effects cannot tell the two apart, because they evaluate exactly once.

### Stacking and overflow

`Stack` adds one modifier set per stack, which is why additive stacks accumulate
and multiplicative stacks compound with no rule per operation. `Refresh` keeps
one and restarts it. `Ignore` keeps the first application untouched. The scope
decides whether two attackers share one stack or keep one each, and a stack
limit turns further applications into overflow: run the overflow effects, and
optionally deny the application or clear the whole stack.

Refreshing or stacking adopts the newest spec: the most recent application
defines the magnitudes, the duration and the period, and only the stack count
carries over.

### Immunity, granted tags and removal

- `Granted Tags` are held by the target while the effect is active, refcounted,
  and mirrored into `AbilitySystem` so `HasTag` sees them.
- `Application Required Tags` and `Application Blocked Tags` gate an application
  against the tags the target holds.
- `Granted Immunity Tags` make the target immune to any effect carrying one of
  those `Effect Tags` for as long as this one lasts.
- `Remove Effects With Tags` clears matching active effects on application.
- A refused application raises `GameplayEffectContainer.EffectBlocked` with the
  spec that was stopped, which is the seam a parry or an armor rule hangs on.

### Ongoing Tag Requirements

An effect's `Ongoing Required Tags` and `Ongoing Blocked Tags` decide, after it
is applied, whether it is on. Off, it stays applied — its duration runs and its
granted tags stay — but it contributes no modifiers and runs no periods; on
again, it picks up where it paused. The requirements are checked on
application and on every tick of the container.

### Granted Effects

An `AbilityLoadout` can grant effects as well as abilities, the way Lyra's
AbilitySet does: its `Granted Effects` are applied when the ability system
starts, and again after a save restore that did not bring them back, never
twice. Regeneration and passive buffs live here.

## Debugging and replication hooks

**Every actor keeps a history.** `AbilitySystem.History` is a fixed ring of
timestamped `AbilityEvent` entries, oldest first: every activation attempt that
was refused and its `AbilityActivationRejection` code, every start with its
target, every step transition with its reason, every ending with its cancel
tag, cues, gameplay events, effects delivered or refused from this actor and
applied, blocked or removed on it, and task starts and endings. Only attempts
are recorded — `EvaluateActivation` and `CanActivate` are questions and leave
no trace, so AI scoring cannot flood the ring. The ring is created on the first
record; `HasHistory` says whether one exists without creating it.

`AbilityDiagnostics` is the switch for all of it. `Enabled` defaults on in the
Editor and in development builds and off in a release player, and off it costs
one static bool read per site and no allocation. It also holds `HistoryCapacity`,
the global `EventRecorded` event a combat log or a custom hit-stop can hang on,
and three `AbilityDiagnosticCounter`s — target queries, effect application,
tasks — each reporting count, total, average and maximum milliseconds, and
managed bytes allocated as the Profiler's own frame counter sees them. The same
sites carry the `GAS.TargetQuery`, `GAS.EffectApplication` and `GAS.Task`
markers into the Profiler window.

**The Ability Debugger window** (Window > Fofuxo > Ability Debugger, or the
button on the actor's Inspector) reads all of that for a selected actor:
running activations with step, phase, frame, target, queued input and tasks;
tags; attributes with base, limits and modifier count; cooldowns and charges;
active effects with stacks and remaining duration; the history, filterable by
kind. Its "Can activate now?" rows run the runtime's own `EvaluateActivation`
for every granted ability against a target you pick, so what it prints is
exactly what `TryActivate` would say. The window's Time section owns slow
motion, a hit-stop on every landed effect, and pause-on-rejection and
pause-on-cancel switches; these are editor tooling, and no runtime code in the
package writes `Time.timeScale`.

`AbilitySystemDebugger` is the drop-in component that echoes ability and cue
transitions to the Console and exposes a one-line `Summary` (active ability,
frame, tags) for Inspector monitoring while tuning. `ActiveFrame`, `ActiveTags`,
and `AbilityInstance.RegisteredHitCount` support custom tooling.

`AbilityDebugDraw` is the Unreal-style draw-debug layer: timed wireframe
boxes, spheres, capsules, cones and rays rendered in the Scene and Game views
through `Debug.DrawLine`, compiled out of player builds. `AbilityDebugDraw.Shape`
draws a `HitShape` exactly where it would query, and it is the only shape-drawing
code in the package: the debug effect, the runtime overlay and the editor gizmos
all call it, so what is drawn and what is queried cannot drift apart.

`DebugDrawEffectDefinition` carries a `HitShape`, a colour and a screen
lifetime, and no geometry of its own. Copying a damage effect's shape into it is
the point — the drawing and the query then read the same fields.

`AbilityQueryGizmos` draws an ability's query volumes in the Scene view without
entering Play Mode: drop it on the actor, pick an ability and a step, and every
effect volume that step fires appears, plus the target assist cone. It runs the
shipping geometry and the shipping filters, and marks the targets the filters
accept right now at their contact points.

Multiplayer stays out of scope, but the seams exist: assign an
`IAbilityReplicationSink` to forward activations, cues, and endings to the
netcode layer. Animation clips can also emit cues without code through
`AbilityAnimationEventBridge.EmitGameplayCue`.

## Persistence

Saving is opt-in and entirely outside the actor: `AbilityPersistence` is a
static class, there is no save component, no serialized field and no tick. A
game that never saves pays nothing for this section existing.

```csharp
// Ending a session.
AbilitySaveRecord record = AbilityPersistence.Capture(player, out AbilitySaveReport report);
File.WriteAllText(path, AbilityPersistence.ToJson(record, prettyPrint: true));

// Opening the next one.
AbilitySaveRecord loaded = AbilityPersistence.FromJson(File.ReadAllText(path));
AbilityPersistence.Restore(
    player,
    loaded,
    new AbilityRestoreOptions
    {
        Resolver = new AbilitySaveResolver(player).AddEffects(worldBuffs),
        OfflinePolicy = AbilityOfflinePolicy.Advance
    },
    out AbilitySaveReport restored);
```

**What travels:** charges and their restore timers, attribute base values,
active duration and infinite effects with their stacks, remaining time and Set
By Caller numbers — cooldowns among them, since a cooldown is an effect — and
loose tags. The loadout's Granted Effects need no id: a restore grants them
again.

**What does not:** running activations, tasks, animator state, and the source
actor of an effect. A record is taken between activations - capturing an actor
mid-combo says so in the report - and a `GameObject` is not data, so a restored
effect belongs to the target that carries it.

**A restore is not an application.** The effect comes back with its modifiers,
its granted tags and its persistent cue, but `Execute` never runs again: a saved
poison resumes poisoning instead of dealing its first tick a second time, and a
periodic effect does not pay out the periods that would have elapsed offline.

### The time between sessions

`AbilityOfflinePolicy` is the one decision the package refuses to make for a
game, because all three answers are correct somewhere:

| Policy | Cooldowns and charges | Timed effects |
| --- | --- | --- |
| `Freeze` (default) | resume with exactly what was left | resume with exactly what was left |
| `Advance` | aged by the offline seconds; charges restore at their own rate, and the leftover seconds bank towards the next one | aged by the offline seconds, and dropped when they run out |
| `Expire` | cleared; charges come back full | dropped, except infinite ones, which have no clock |

`AbilityOfflinePolicy.Advance` spends `AbilityRestoreOptions.OfflineSeconds`, or
measures it from the record itself when that is negative.
`AbilityPersistence.SecondsSince(record)` is the same number on its own - the
one a day/night cycle, a regrowing resource or a daily reset is built on. What
elapsed real time *means* stays the game's decision: the package records when
the save was taken and ages what it owns, nothing more.

### Identifying what comes back

An ability is found by its `abilityId`. An effect is found by `effectId`, a new
field on `GameplayEffectDefinition`. An **instant effect never needs one** - it
is over before a record could carry it - but a duration or infinite effect
without an id cannot be resolved later, so `Capture` leaves it out and names it
in the report rather than writing an entry nothing can read back.

`AbilitySaveResolver` covers the common case: the actor's own loadout, plus
every effect authored inside those abilities. Effects that live elsewhere are
registered with `AddEffects`, and a game with its own catalog implements
`IAbilitySaveResolver` instead.

### Schema changes

A record carries the version it was written with. `AbilitySaveMigrator.Register`
takes an `IAbilitySaveMigration` per version step, and a record is walked up to
the current schema before anything is applied. A missing step and a record from
a newer version are both refused outright, with the reason in the report - a
half-restored actor is worse than a load that says it cannot.

## The sample

**Package Manager > Fofuxo's Gameplay Ability System > Samples > Melee Combat >
Import**, then open `Scenes/MeleeCombat.unity`.

It is the whole loop in one scene, with no art: a three-step combo, a roll with
i-frames, a block whose first frames parry, target assist, damage on one
attribute and poise on another, knockback, target-owned hit reactions, and cues
presented as coloured primitives. `WASD` moves, `J` attacks - press it again
inside the combo window - `K` rolls, `L` blocks. The enemy runs the same
abilities from a forty-line brain.

`Scenes/Profiling.unity` in the same sample spawns pairs of those fighters and
puts the package's counters on screen; see
[`Documentation~/PERFORMANCE.md`](Documentation~/PERFORMANCE.md).

## Roadmap

What is left, short version:

1. Samples, performance budgets and the `1.0` surface.

`CHANGELOG.md` is the record of what shipped and why; the roadmap carries only
what has not been built.

The package targets single-player games. Replication and client prediction are
out of scope; if a consumer ever goes multiplayer, that work starts from
[tranek/GASDocumentation](https://github.com/tranek/GASDocumentation).

See [the detailed development roadmap](Documentation~/ROADMAP.md) for the GAS
concept mapping, knockback and Target Assist decisions, milestone dependencies,
deliverables, acceptance criteria, non-goals, and the path to `1.0`.

## Working with AI agents

This repository is expected to be edited across many independent AI-agent
conversations. Durable context belongs in versioned files, not chat history.

- [`AGENTS.md`](AGENTS.md) and [`CLAUDE.md`](CLAUDE.md) hold the repository-wide
  implementation rules. They are **the same document under two names**, so an
  agent finds it under whichever name it looks for; neither outranks the other.
  Editing one means copying it over the other in the same commit.
- This README describes current public behavior and limitations.
- [`Documentation~/ROADMAP.md`](Documentation~/ROADMAP.md) covers **only work
  that has not been built yet** — direction, priorities, milestones, acceptance
  criteria — plus the orientation an agent needs to pick the package up. A
  milestone leaves that file when it ships.
- [`CHANGELOG.md`](CHANGELOG.md) records user-visible changes.
- [`Documentation~/UPGRADING.md`](Documentation~/UPGRADING.md) says what to
  change in a project when a release breaks something.
- [`Documentation~/PERFORMANCE.md`](Documentation~/PERFORMANCE.md) is the
  allocation budget, the counters, and the pooling guidance.
- [`Documentation~/RELEASE_CHECKLIST.md`](Documentation~/RELEASE_CHECKLIST.md)
  is the versioning and deprecation policy, and the list run before a tag.
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
  Core/         Definitions, steps, runtime instances, system, tags, contracts
  Attributes/   Attribute identifiers, values, sets and modifiers
  Effects/      Gameplay effect definitions, specs, the container, built-in effects
  Targeting/    Target data, filters and the shared query
  Tasks/        Cancellable per-activation work: waits, targeting, movement
  Presentation/ Animation player and animation-event bridge
  Diagnostics/  The diagnostics switch, the event history, counters, debug draw
  Motors/       Engine adapters for movement, in a separate assembly
  Input/        Optional Input System integration in a separate assembly
Editor/         Custom inspectors, the Ability Debugger window, authoring tools
Tests/
  EditMode/  Package contract tests
AGENTS.md    Persistent instructions for coding agents
CLAUDE.md    Byte-identical copy of AGENTS.md
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
7. Verify the package by importing it into a Unity `6000.6+` host project.

## License

MIT — see [LICENSE.md](LICENSE.md).
