# Performance

What the package promises about allocation and cost, how to watch it, and what
to do when a promise breaks.

## Table of contents

- [The budget](#the-budget)
- [What allocates, and why](#what-allocates-and-why)
- [Watching it](#watching-it)
- [The profiling scene](#the-profiling-scene)
- [Pooling and consumer guidance](#pooling-and-consumer-guidance)
- [When a budget test fails](#when-a-budget-test-fails)

## The budget

These allocate **nothing**, and `Tests/EditMode/AllocationBudgetTests.cs` fails
the build if that stops being true. They are the calls a game makes every frame
on every actor:

| Call | Why it is in the budget |
| --- | --- |
| `AbilitySystem.Tick` on an idle actor | Every actor runs it, every frame, whether or not it is doing anything |
| `AbilitySystem.EvaluateActivation` | An AI scores every granted ability with it, every frame |
| `HasTag`, `IsOnCooldown`, `GetCooldownRemaining`, `GetCharges` | What a HUD and an AI read |
| `AttributeSet.GetCurrent` / `GetBase` | The same |
| `GameplayEffectContainer.Tick` with live effects | Ageing durations and periods must not allocate per frame |
| A diagnostic record site with `AbilityDiagnostics.Enabled` off | Diagnostics off is one static bool read, and creates no history ring |

## What allocates, and why

These allocate on purpose. They happen per *event*, not per frame, and trying to
pool them away would cost more in complexity than it returns:

| Event | What is allocated |
| --- | --- |
| An activation | One `AbilityInstance` and its task scope, plus the tasks the steps start |
| An effect application | One `GameplayEffectSpec`; a duration or infinite one also gets an `ActiveGameplayEffect` and its modifier slots |
| A target acquisition | Whatever Unity's physics query returns. Effects sharing a trigger frame, a step and a shape share **one** query through `AbilityInstance.AcquireTargets` - that is the main lever, and it is authored: give two effects the same shape and they cost one query |
| A persistent cue | One `ActiveGameplayCue` per live cue, released on `Remove` |
| The first recorded event on an actor | The history ring, sized by `AbilityDiagnostics.HistoryCapacity`, allocated once and reused |

## Watching it

Three counters, on by default in the Editor and development builds, off in a
release player:

```csharp
AbilityDiagnostics.TargetQueries      // AbilityTargetQuery.Acquire
AbilityDiagnostics.EffectApplications // GameplayEffectContainer.Apply
AbilityDiagnostics.Tasks              // task starts and ticks
```

Each carries `Count`, `TotalMilliseconds`, `AverageMilliseconds`,
`MaxMilliseconds` and `AllocatedBytes`, and `Reset()` to start a measurement
from now. The bytes come from the Profiler's own `GC Allocated In Frame`
recorder: exact in the Editor and development builds, absent in a release
player, where the counters are off anyway.

The same three sites carry `GAS.TargetQuery`, `GAS.EffectApplication` and
`GAS.Task` profiler markers, so a Profiler capture shows them without any
package API.

**Window > Fofuxo > Ability Debugger** shows the counters and, per actor, the
running activation, tags, attributes, cooldowns, active effects and the event
history.

Turning everything off:

```csharp
AbilityDiagnostics.Enabled = false; // one static bool read at every record site
```

## The profiling scene

The Melee Combat sample ships `Scenes/Profiling.unity`: pairs of fighters that
fight each other, with the counters on screen. The three axes:

- **Dense actors** - raise `Pair Count` on the harness. Every actor ticks,
  activates, reacts and presents cues.
- **Simultaneous effects** - the same knob: each fighter carries its reaction
  and damage applications, so effects scale with actors. Author a duration
  effect into the combo to push it further.
- **Target queries** - widen the damage effect's `HitShape` radius, or lower the
  brain's decision interval, and watch `Target queries`.

The harness resets the counters a couple of seconds after start, so the spawn is
not measured as gameplay.

## Pooling and consumer guidance

- **Key pooled presentation by `GameplayCueParameters.Handle`.** A persistent cue
  keeps the same handle from `Add` to `Remove`, which is exactly the lifetime a
  pooled VFX instance wants. The sample's `SampleCuePresenter` is that shape.
- **Do not allocate in `OnGameplayCue`.** It runs inside the activation that
  raised it. Look the handle up, move an instance, return it - do not build
  strings or LINQ there.
- **Let duplicate bursts batch.** `GameplayCueDispatcher.BatchDuplicatesPerFrame`
  is on by default: two effects hitting one target on one frame present once.
  `BatchedCount` says how many were dropped.
- **Give effects that should agree the same shape.** One query serves them all;
  two different shapes are two queries.
- **Do not cache a `GameplayEffectSpec` and apply it twice.** A spec is one
  application. Reapplying the asset makes a new one, which is cheaper than the
  bugs the other way round.
- **Keep `AbilitySystem.StepAdvanceCondition` allocation-free.** It is polled on
  every conditional step; assign the delegate once rather than a lambda that
  captures per call.
- **Raise `AbilityDiagnostics.HistoryCapacity` deliberately.** It is per actor,
  allocated on the first recorded event, and a dense scene multiplies it.

## When a budget test fails

A failing `AllocationBudgetTests` names the call that started allocating. The
usual causes, in the order they are usually true:

1. A `foreach` over a collection exposed as an interface, boxing its enumerator.
   Iterate the concrete list, or keep the loop indexed.
2. A closure captured per call - a lambda passed to a per-frame method.
3. A `string` built for a log, a message or a tooltip outside a diagnostics
   guard.
4. `params` or LINQ on a path that used to be a plain loop.

Fix the allocation rather than the test: the test is the promise, and a game
with a hundred actors pays it a hundred times a frame.
