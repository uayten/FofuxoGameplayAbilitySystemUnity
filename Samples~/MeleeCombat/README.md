# Melee Combat

The whole loop in one scene: a three-step combo, a roll with i-frames, a block
whose first frames parry, target assist, damage on one attribute and poise on
another, knockback, hit reactions owned by the target, and cues. The player
drives it from an input map; the enemy drives the same abilities from a brain
that is forty lines long.

Nothing here needs art. The fighters are capsules and the cues are coloured
spheres, so what you watch is the ability system rather than a particle budget.

## Running it

1. Import the sample from the Package Manager, then open
   `Scenes/MeleeCombat.unity`.
2. Press Play.

| Input | What happens |
| --- | --- |
| `WASD` | Move. Locked while an ability says so - the fighter asks `AbilitySystem.IsMovementLocked` |
| `J` | Attack. Press again inside the combo window for the next step |
| `K` | Roll. Invulnerable while it travels, and it cancels whatever was running |
| `L` | Block. The first ten frames parry; the rest guards |

Open **Window > Fofuxo > Ability Debugger** with a fighter selected while it
plays: the running activation, its step, phase and frame, the tags held, the
cooldowns, the active effects and the history of everything refused or
cancelled.

## What each piece shows

| File | The thing worth reading |
| --- | --- |
| `Abilities/GA_Sample_Combo.asset` | One ability, three steps, one damage effect per step embedded in the asset. Not three abilities |
| `Abilities/GA_Sample_Roll.asset` | Displacement authored as metres over frames, and `State.Invulnerable` as a frame window rather than a granted tag |
| `Abilities/GA_Sample_Block.asset` | `State.Parrying` held over the parry frames only. A tag granted for the whole guard would parry through its recovery |
| `Abilities/GA_Sample_HitReaction.asset` | The reaction belongs to the target: it answers `Event.HitReaction`, reads the hit off `TriggeringSpec`, and runs the knockback the attacker authored |
| `Abilities/GA_Sample_Knockdown.asset` | The same, for the heavy step - a separate ability, not a flag on the first |
| `Abilities/GTA_Sample_Melee.asset` | Target assist: the query that picks the target and the approach that closes the gap, before the step runs |
| `Scripts/SampleFighter.cs` | The two decisions the package leaves to the game: what death is, and what a broken poise meter means |
| `Scripts/SampleEnemyBrain.cs` | `EvaluateActivation` as an AI's question - typed, side-effect free, safe every frame |
| `Scripts/SampleCuePresenter.cs` | A presenter keyed by `GameplayCueHandle`, with bursts, persistent cues and per-outcome colours |
| `Scripts/SamplePlayer.cs` | Locomotion only. Attack, roll and block are bindings in `GIM_Sample_Player`, not code |

## Poise, and why it is not a package feature

The heavy step subtracts from `Combat.Poise` with a second damage effect whose
`reactionEventTag` is empty, so the hit asks for no reaction. `SampleFighter`
watches the attribute, and when poise reaches zero it refills it and sends
`Event.Knockdown` to itself. The package supplies the attribute, the effect and
the event; that "poise broken means floored" is this game's rule, and it lives
in the game.

## Profiling

`Scenes/Profiling.unity` spawns pairs of the same fighters and lets them fight,
with the package's counters on screen. Turn `Pair Count` up for dense actors,
and watch `Target queries`, `Effect applications` and `Tasks` - call counts,
average and maximum milliseconds, and bytes allocated. The Profiler's
`GAS.TargetQuery`, `GAS.EffectApplication` and `GAS.Task` markers cover the same
sites. `Documentation~/PERFORMANCE.md` in the package says what to expect.
