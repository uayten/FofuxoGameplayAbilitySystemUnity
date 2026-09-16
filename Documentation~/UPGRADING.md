# Upgrading

What to change in a project when a release breaks something, newest first.
`CHANGELOG.md` says *why* each of these happened; this file says what to do
about it.

Below `1.0` there is no deprecation window: a rename is a rename, so the
compiler finds most of this for you. From `1.0` on, the policy in
[`RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md) applies and every break lands
with one minor version of warning.

## Table of contents

- [Unreleased](#unreleased)
- [Cues](#cues)
- [Hit reaction and damage](#hit-reaction-and-damage)
- [Timelines and abilities](#timelines-and-abilities)
- [Input](#input)
- [Effects](#effects)
- [Targeting](#targeting)
- [Cancellation](#cancellation)

## Unreleased

**The editor assembly moved into `Fofuxo.GameplayAbilitySystem.Editor`.** Its
public types used to sit in the global namespace. If your project referenced one
- a custom Inspector deriving from `GameplayEffectDefinitionEditor`, a tool
calling `AssetNamingConvention`, an editor script opening the debugger window -
add the using:

```csharp
using Fofuxo.GameplayAbilitySystem.Editor;
```

Inside that namespace the identifier `Editor` names the namespace itself, so an
Inspector of your own that derives from Unity's base class spells it
`UnityEditor.Editor`.

**`TargetQueries.OverlapReceivers` is now `OverlapColliders`.** Same
parameters, same return: it fills a buffer with overlapping colliders. The
old name came from `IAbilityDamageReceiver`, which no longer exists.

**Minimum editor is `6000.6`.** Older Unity 6 versions are no longer supported;
`package.json`, the README and the repository instructions all say so now.

**Persistence is new and additive.** Nothing to change unless you want it. If
you do save, read the [Persistence](../README.md#persistence) section: the one
thing that needs authoring is `effectId` on any effect with a `Duration` or
`Infinite` policy. Instant effects need none.

## Cues

**`AbilitySystem.GameplayCueTriggered` changed shape.**

```csharp
// Before
system.GameplayCueTriggered += (ability, cue, context) => Play(cue, context);

// After
system.GameplayCueTriggered += parameters => Play(parameters.Cue, parameters);
```

`GameplayCueParameters` carries the tag, the event (`Execute`, `Add`,
`WhileActive`, `Remove`), the outcome, a stable `GameplayCueHandle`, the owner,
the source, the ability and effect behind it, level, magnitude, stack count,
context, contact hit, location and normal.

**A presenter is a better place than that event.** Implement
`IGameplayCuePresenter` and register with `AbilitySystem.Cues`: a presenter
registering late is caught up on every live persistent cue, which an event
subscriber is not.

**A persistent cue ends with the effect that added it.** If your code removed a
looping effect's VFX by hand, delete that code and key the instance by
`GameplayCueParameters.Handle`; `Remove` arrives when the effect ends, expires,
or the actor tears down.

## Hit reaction and damage

**`AbilityHitInfo`, `AbilityImpact` and `IAbilityDamageReceiver` are gone, not
renamed.** A consumer component that implemented the receiver has no
replacement callback, and reintroducing one with the same five fields under a
new name is the migration this change exists to prevent.

What to do instead:

1. Give the target an `AttributeSet` holding the attribute the damage should
   subtract from, and name that attribute in the damage effect's
   `targetAttribute`.
2. Read health from the attribute's `Changed` event instead of from a damage
   callback.
3. Move the reaction - clip, control lock, cancellation, knockback - into a
   `HitReactionAbilityDefinition` in the *target's* loadout, triggered by the
   effect's `reactionEventTag` (`Event.HitReaction`, or `Event.Knockdown` for a
   hit that floors).
4. Delete `stunDurationSeconds` and `impact` from your damage effects: how long
   a target is locked is the length of its own reaction now.
5. A parry or block reward hangs on `GameplayEffectContainer.EffectBlocked`,
   which fires with the spec when the target refused the hit.

## Timelines and abilities

**Steps moved to `TimelineAbilityDefinition`.** An ability authored as ordered
swings over frames is a `TimelineAbilityDefinition`; a plain `AbilityDefinition`
carries no steps and runs until something ends it. Change the asset's script
reference, or recreate the asset from the `Fofuxo/Abilities/Timeline Ability`
menu. Code that read `ability.Steps` casts first.

**A combo is one ability with several steps.** Chains of one-ability-per-swing
were removed; author the swings as steps of one asset so cost, cooldown, tags
and cancellation are decided once per activation.

## Input

**`AbilityInputRouter` reads one `AbilityInputMap` asset.** The two binding
lists and the loose `InputActionAsset` field on the router are gone. Create a
map (`Fofuxo/Abilities/Input Map`), pick the action names from its dropdown, and
assign the map to the router.

## Effects

**One effect class, three duration policies.** `ModifyAttributeEffect`,
`DurationAttributeEffect` and `PeriodicAttributeEffect` were one enum value each
and are now `GameplayEffectDefinition` with `durationPolicy` set to `Instant`,
`Duration` or `Infinite` plus a `period`.

**One damage effect, any shape.** The per-collider-shape damage effects are one
`DamageEffectDefinition` carrying a `HitShape` in its `targeting` block.

**Effects validate themselves.** An effect that was silently doing nothing now
reports it, both in its own Inspector and in the ability that fires it. Expect
existing assets to light up red the first time you open them; the message says
what is missing.

## Targeting

**`AbilityTargetFilter.allowNonDamageable` is now `includeNonActors`** - same
serialized value, so no asset changes - and off it skips colliders with no
ability system, attribute set or effect container above them. The new
`includeDead` (default off) skips an actor holding `State.Dead`.

**`AbilityTargetHit` lost `Receiver` and `DamageReceiver`.** Resolve the actor
with `TargetQueries.ResolveActor` and read its components.

**Target assist is not an ability.** `TargetAssistDefinition` is a plain
`ScriptableObject` referenced by the step; the parent ability keeps cooldown,
cost, tags and completion.

## Cancellation

**Cancellation is named by `GameplayTag`, not by an enum.** The package raises
`Cancel.Manual`, `.TargetLost`, `.PhysicsForce`, `.StepTimeout`, `.Superseded`,
`.Failed` and `.OwnerTeardown`; a game adds its own tags in the same namespace
and never edits the package. `CombatCancelTags.HitReaction` and `.Knockdown`
have no replacement: an interrupting reaction raises `Cancel.Superseded` like
anything else.
