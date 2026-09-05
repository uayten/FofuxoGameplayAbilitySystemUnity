# Fofuxo's Gameplay Ability System

A lightweight Unreal-GAS-inspired ability system for Unity: data-driven
abilities as `ScriptableObject`s, frame-based phases, gameplay tags,
cooldowns, sequences (combos) and pluggable damage effects.

## Requirements

- Unity 6000.0+
- [Input System](https://docs.unity3d.com/Packages/com.unity.inputsystem@latest)
  (only needed for `AbilityInputRouter`; the core works without it)

## Install

In any Unity project, open **Window > Package Manager**, press **+** and
choose **Install package from git URL...**, then paste:

```text
https://github.com/uayten/FofuxoGameplayAbilitySystemUnity.git
```

To update later, select the package in the Package Manager and press
**Update**. To pin a release instead of tracking the default branch, append
`#v0.1.0` (or any tag) to the URL.

## Contents

```text
Runtime/
  Core/      AbilitySystem, AbilityDefinition, AbilityInstance,
             AbilityContext, AbilityLoadout, AbilitySequenceDefinition,
             AbilityEffectDefinition, AbilityEffectTrigger,
             AbilityTypes, GameplayTag, IAbilityDamageReceiver
  Effects/   MeleeDamageEffectDefinition (sphere query vs IAbilityDamageReceiver)
  Input/     AbilityInputRouter (Input System -> TryActivate / TryActivateSequence)
Editor/      AbilityDefinitionEditor (timeline summary + validation + effect helper)
Tests/       EditMode validation tests (import via Package Manager > Tests)
```

`CreateAssetMenu` entries live under **Fofuxo > Abilities**.

## Quick start

1. Add `AbilitySystem` to an actor (it finds a child `Animator` by itself).
2. Create abilities via **Create > Fofuxo > Abilities > Ability**, configure
   the timeline in frames, tags, cooldown and effect triggers.
3. Group them in a **Loadout** asset and assign it to the `AbilitySystem`.
4. Activate from code, AI or input:

```csharp
using Fofuxo.GameplayAbilitySystem;

AbilitySystem system = GetComponent<AbilitySystem>();
AbilityContext context = AbilityContext.FromTarget(gameObject, target);
system.TryActivate(someAbility, context);
```

### Receiving damage

Implement `IAbilityDamageReceiver` on the game's health component so effects
such as `MeleeDamageEffectDefinition` can hit it without knowing the game's
types:

```csharp
using Fofuxo.GameplayAbilitySystem;
using UnityEngine;

public sealed class MyHealth : MonoBehaviour, IAbilityDamageReceiver
{
    public bool IsDamageable => currentHealth > 0;

    public bool TryReceiveDamage(AbilityHitInfo hit)
    {
        // hit.Amount, hit.Source, hit.HitPoint, hit.Direction,
        // hit.Knockback, hit.KnockbackDuration, hit.Impact, hit.CanBeParried
        ...
    }
}
```

### Input

Add `AbilityInputRouter`, bind `InputActionReference`s (or action names from
an `InputActionAsset`) to abilities/sequences, and optionally resolve a
default target when none is assigned:

```csharp
AbilityInputRouter.GlobalFallbackTargetResolver = () => FindFirstObjectByType<MyEnemy>()?.gameObject;
```

## Design rules

- `ScriptableObject`s hold configuration only — never runtime state
  (cooldowns, active phase, targets live in `AbilityInstance`).
- Identifiers, comments and logs are written in English.
- One active ability per `AbilitySystem`; sequences chain steps on completion.

## License

MIT — see [LICENSE.md](LICENSE.md).
