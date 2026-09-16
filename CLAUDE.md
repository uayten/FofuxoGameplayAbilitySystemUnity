# Fofuxo Gameplay Ability System — repository instructions

> `AGENTS.md` and `CLAUDE.md` in this folder are **the same document under two
> names**, so any agent finds it under the name it looks for. Neither outranks
> the other. Edit one and copy it over the other in the same commit — the moment
> they differ, both become untrustworthy.

Public Unity package, id `com.uayten.fofuxogameplayabilitysystem`, namespace
`Fofuxo.GameplayAbilitySystem`, Unity `6000.6` or newer.

A few sections below apply only to the copy vendored inside BossRush and are
marked **[BossRush]**. Everything else holds in the standalone repository too.

## Scope

Keep package code generic and reusable. Game-specific AI, health, movement,
characters, scenes, and content assets belong in consumer projects.

## Read before changing code

- Read `README.md` for current behavior, limitations, and design direction, and
  `Documentation~/ROADMAP.md` for where the package is going.
- Read `CHANGELOG.md` and inspect `git status` before editing.
- Read the complete files involved in a public API change and their focused tests.
- Treat existing uncommitted changes as user-owned work.

## Language and compatibility

- Write identifiers, comments, logs, documentation, and commit messages in English.
- Preserve the namespace and the package id. Neither may change.
- Document intentional breaking changes before release.

## Authored assets are not a design constraint

The package is pre-1.0 and its base is still being built. **The shape of the
plugin is decided by what the plugin should be, never by what existing assets
happen to serialize.** When a change to the core is right, make it the right
way.

- Never keep a serialized field, a type, or a code path alive only so authored
  assets do not have to be re-authored.
- Never add a compatibility layer, a bridge, or a deprecation window for that
  reason alone. Two live paths for one concept is worse than one clean break.
- Re-authoring assets is expected work, not damage, and belongs in the same
  change as the code that caused it.
- Consumer-project scripts are consumers too. A cross-cutting change updates
  them instead of bending the package around them; the adapter direction stays
  game → package.

The same holds for the public API: there is no compatibility promise before 1.0,
so a signature that turned out wrong gets fixed rather than wrapped.

This does not license silent breakage. Every intentional break is written down
in `CHANGELOG.md`, and the change reports which assets were re-authored and what
the owner has to re-tune and re-test.

**[BossRush]** Re-author through the connected Editor and confirm by reading the
value back — never hand-edit `.asset` YAML while the Editor is open. Past a
handful of objects, write a migration utility under `Editor/` rather than doing
it by hand: a combo asset holds one ability plus one effect per step, and the
embedded objects are the easiest to lose track of. Then run `gas-asset-audit`.

## Architecture invariants

- `ScriptableObject` definitions hold **immutable authoring data only**. Runtime
  state is created per actor or per activation. An effect that mutates its
  definition at runtime is a bug that survives Play Mode and corrupts the asset
  on disk.
- `AbilitySystem` owns activation rules, cooldowns, tags, active abilities and
  step advancement. **Callers own intent** — no player input and no AI selection
  in the core assembly. Input System integration stays in its own assembly.
- **Animator state is never the sole authority for gameplay timing.**
- `CanActivate` and target validation are side-effect free.
- **A timeline is a kind of ability, not the shape of every ability.**
  `AbilityDefinition` holds what is decided once per activation and carries no
  steps at all; `TimelineAbilityDefinition` adds the ordered `steps` and
  everything derived from them. An ability whose animation is played elsewhere,
  or that has none, is a plain `AbilityDefinition` and stays active until
  something ends it. Never move a step field back onto the base to save a cast.
- **A combo is one `TimelineAbilityDefinition` with several `steps`, emitting one
  damage effect.** Do not reintroduce per-step abilities — that refactor already
  landed (`8ab2353`).
- **One damage effect, any shape.** `DamageEffectDefinition` carries its
  `HitShape` in the shared `targeting` block. Do not reintroduce one effect
  class per collider shape — the four that existed drifted apart by copy.
- **Damage is an attribute change plus a request, and the reaction belongs to
  the target.** The damage effect subtracts from its `targetAttribute` and then
  sends `reactionEventTag` to the target's ability system with the spec
  attached; a `HitReactionAbilityDefinition` in the target's loadout answers it
  and owns the clip, the lock, the cancellation and the knockback, read off
  `AbilityInstance.TriggeringSpec`. Nothing hands reaction data to a consumer
  callback — `AbilityHitInfo` and `IAbilityDamageReceiver` were removed, not
  renamed, and a test pins their absence. Whether a target reacts is decided by
  the target's tags (`State.Dead`, `State.Invulnerable`, `State.Parrying`),
  never by the attacker.
- **Effects that fire on the same frame of the same step with the same shape
  share one physics query**, through `AbilityInstance.AcquireTargets`. That
  guarantee is what makes damage and physics force hit the same enemy; it is
  covered by tests and must survive any change to where targeting lives.
- No package runtime or editor assembly may reference a consumer-project type.
- Prefer small contracts and events over inheritance from game-specific components.
- **Cues present; they never decide.** Every cue goes through the actor's
  `GameplayCueDispatcher` as read-only `GameplayCueParameters`, which hand a
  presenter no activation, spec, system or container — a test pins that
  shape. A persistent cue is owned by the effect that added it and ends with
  it; a refused application raises the effect's replacement for the outcome or
  nothing. Never route a gameplay consequence through a cue, a presenter or a
  filter.
- **Diagnostics record; they never decide, and they never own time.**
  `AbilityDiagnostics` is the one switch; a record site is guarded by
  `AbilityDiagnostics.Enabled`, costs one static bool read when it is off and
  allocates nothing then. `EvaluateActivation` and `CanActivate` are questions
  and leave nothing in the actor's `History` — only an attempt records a
  rejection. Slow motion and hit-stop live in the editor assembly
  (`AbilityTimeControls`); no runtime code writes `Time.timeScale`.

## Authoring assets

Hand-authoring ability assets is the workflow that generates the most files and
the most inconsistency. **[BossRush]** Load **`gas-new-ability`** for one
ability, or **`gas-character-setup`** for a whole character. **`gas-asset-audit`**
validates an existing tree.

Asset menu root: `Fofuxo/Abilities/…`.

## Asset naming convention

Every object created from a package `ScriptableObject` is named
`<Prefix>_<Owner-or-Category>_<Detail>`, PascalCase per segment, English, no
spaces, no dashes, no version suffixes (`_v2`, `_final`, `_new`, `_old`,
`_copy`). This covers **both** the `.asset` file and every object embedded
inside it — a combo asset holds one ability plus one effect per step, and the
embedded ones are just as easy to lose track of. Unreal's GAS convention is the
model (`GA_`, `GE_`, `GC_`); it is extended here because this package has asset
types Unreal has no equivalent for.

| Prefix | Type | Example |
| --- | --- | --- |
| `GA_` | `AbilityDefinition` and its subclasses, `TimelineAbilityDefinition` and `HitReactionAbilityDefinition` included | `GA_Fergus_SlamCombo`, `GA_Grant_HitReaction` |
| `GE_` | `GameplayEffectDefinition` and its subclasses | `GE_Damage_Slam` |
| `ABS_` | `AttributeSetDefinition` | `ABS_Player_Combat` |
| `GAL_` | `AbilityLoadout` | `GAL_Fergus_Phase2` |
| `GTA_` | `TargetAssistDefinition` | `GTA_Melee_Soft` |
| `GIM_` | `AbilityInputMap` | `GIM_Grant_Default` |
| `GC_` | reserved | no cue asset exists; see cue tags below |

`ABS_` is the one prefix that does not start with `G`: `GAS_` reads as the
package's own acronym and made attribute sets look like a system-wide asset.

Body of the name:

- `GA_` — `<Owner>_<Action>`. Owner is the character or system that activates it
  (`Player`, `Fergus`), action is what it does (`SlamCombo`, `Roll`). A combo is
  one asset, so the name describes the whole combo, never a single swing.
- `GE_` — `<Category>_<Detail>`, category first so effects group in the Project
  window: `Damage`, `Heal`, `Buff`, `Debuff`, `Cost`, `Regen`, `Debug`
  (`GE_Damage_Slam`, `GE_Buff_Speed`, `GE_Cost_Stamina`). The effect subclass is
  not in the name; the Inspector already shows it. An effect embedded in an
  ability names its detail after the step that fires it, owner included, so it
  stays findable outside its parent: `GE_Damage_FergusAttack01`,
  `GE_Debug_FergusAttack01Hit`.
- `ABS_` / `GAL_` — `<Owner>_<Variant>` (`ABS_Player_Combat`,
  `GAL_Player_Default`).
- `GTA_` — `<Context>_<Variant>` (`GTA_Melee_Soft`, `GTA_Ranged_Hard`).
- `GIM_` — `<Owner>_<Variant>` (`GIM_Grant_Default`), like a loadout: the
  scheme belongs to whoever is playing with it.

Unity keys the main object of an `.asset` to the file name: the file and its
main object always carry the same name, or the asset is orphaned in the Project
window.

The Inspector enforces this. `AssetNamingConvention` reports a misnamed asset —
an embedded effect included, listed by the ability that owns it — and offers a
button that performs the rename, through `AssetDatabase.RenameAsset` for a main
asset so the `.meta` and its GUID follow the file. It never invents the body of
the name, only the prefix.

Cues are not assets — a `GameplayCueTrigger` carries a `GameplayTag`, so the
convention applies to the tag string, in the `Cue.` namespace:
`Cue.Impact.Slam`, `Cue.Cast.Fireball`. Other tag namespaces already in use are
`State.` (see `CommonGameplayTags`), `Ability.` and `Cooldown.`.

A new `ScriptableObject` type in this package ships with two things in the same
commit: a row in this table, and a `[CreateAssetMenu]` whose `fileName` already
carries the prefix (`fileName = "GA_NewAbility"`). Pick the prefix as `G` plus
the initials of the type, and check it does not collide with a row above.

Renaming an existing asset means renaming the `.asset` **and** its `.asset.meta`
together, with Unity closed. The GUID lives in the `.meta`, so references
survive; renaming only one of the two loses the asset's identity.

## Before changing a public API

Find the callers before editing the signature. **[BossRush]** `graft callers
<symbol>` first — both the Player (`GrantAbilityIntegration`) and Fergus
(`FergusBrain`, `FergusBoss`) consume this package, and thirteen files in
`Assets/Scripts` reference it.

Then read `CHANGELOG.md` and the EditMode tests under `Tests/EditMode`, and
document intentional breaking changes before release.

**[BossRush]** A change to serialized field names on any definition also
invalidates the field tables in the GAS skills — update
`references/definition-fields.md` in the same commit.

## Attributes and gameplay effects

Two layers, and the line between them is load-bearing.

`AttributeSet` **aggregates and does not decide lifetimes**: base values,
limits, deterministic `Add` / `Multiply` / `Override` aggregation, typed change
events, modifier slots with stable ids, and regeneration. Nothing in it knows
what a duration is.

`GameplayEffectContainer` **owns every lifetime**: application gating, instant
execution, duration, periods, stacking, overflow, immunity, granted tags and
removal, one `GameplayEffectSpec` per application and one
`ActiveGameplayEffect` per live effect, named by a `GameplayEffectHandle`.

- The package supplies generic identifiers, values, sets, and modifiers.
  Consumer projects define concrete sets such as Health, Stamina, and Poise.
- Runtime attribute values never live in `ScriptableObject` assets, and neither
  does anything an application calculates.
- A timed attribute change goes through the effect container, never back into
  the attribute set. If you find yourself wanting a duration on `AttributeSet`,
  that is the container asking to be used.
- Add generated accessors or further aggregation modes only with tests and a
  demonstrated use case.

**One effect asset.** `GameplayEffectDefinition` is concrete: an effect that
only changes attributes and tags needs no subclass. Subclass it only for a
consequence the attribute layer cannot express — damage delivery, physics force,
debug drawing — and override `Execute` plus `CaptureOwnMagnitudes`. Do not
reintroduce one effect class per duration policy; the three that existed
(`ModifyAttribute`, `DurationAttribute`, `PeriodicAttribute`) were one enum
value each.

## Change workflow

1. Inspect current code, tests, README, changelog, and working tree.
2. Make the smallest generic change that satisfies the requested capability —
   smallest in concepts, not in disruption. Never trade the right structure for
   a smaller diff or for assets that would otherwise need re-authoring.
3. Add focused EditMode tests for behavior and public contracts.
4. Update README examples and the roadmap when architecture changes.
5. Update `CHANGELOG.md` under `[Unreleased]` for user-visible changes.
6. Preserve or create Unity `.meta` files for every Unity-visible file.
7. Verify compilation and focused tests in a Unity host project.

## Verification

- The standalone package repository is not a Unity project; use a host project
  to compile and run tests. **[BossRush]** BossRush is that host.
- Prefer focused EditMode tests over broad PlayMode sessions.
- Do not enter Play Mode unless the requested behavior requires interactive
  validation. Gameplay, animation and viewport feel are Antonio's to test.
- Report tests that could not run and the exact host-project precondition that
  blocked them.
- Never claim networking, persistence, or production readiness without
  implemented tests.

## Repository skills

Use a skill only for repeatable procedures with clear triggers, inputs, outputs,
and verification. Keep architecture and product facts in `README.md` and in this
file. **[BossRush]** The GAS skills live in `.claude/skills/`, mirrored under
`.agents/skills/`; both copies must be updated together.

This plugin is under active development. Its skills describe the plugin's
current API, assets, and workflows; they are working documentation, not frozen
contracts. As the plugin grows or changes, update every affected skill in the
same change instead of preserving instructions that describe an older version.

## Code review rules

- Flag runtime state stored on definition assets.
- Flag core dependencies on Input System or consumer-project assemblies.
- Flag activation checks that mutate state.
- Flag step-advancement changes that blur completion, rejection, and
  cancellation semantics.
- Flag public API changes without tests, README updates, or changelog entries.
- Flag missing `.meta` files for new Unity-visible files.
- Flag compatibility shims, dead serialized fields, and duplicate code paths
  kept only to spare authored assets — and, on the other side, breaks that were
  made without saying which assets they invalidate.
- Flag a runtime path that writes `Time.timeScale`, a record site that runs
  without the `AbilityDiagnostics.Enabled` guard, and editor tooling that
  re-implements a rule `TryValidate` or `EvaluateActivation` already owns.

## Port-back **[BossRush]**

This folder has no `.git`. Changes are recorded only by a BossRush commit until
copied by hand into `uayten/FofuxoGameplayAbilitySystemUnity`. Port at
milestones; the pre-vendoring snapshot at
`C:\Users\ant7a\OneDrive\Documentos\UnityPackages\` must survive until the first
port-back is done.
