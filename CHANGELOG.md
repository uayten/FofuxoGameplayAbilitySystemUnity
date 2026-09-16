# Changelog

## [Unreleased]

### The editor assembly has a namespace, and the types you call explain themselves

Two of the three things the `1.0` milestone still wanted.

- **Breaking: every public editor type moved into
  `Fofuxo.GameplayAbilitySystem.Editor`.** They were in the global namespace,
  which every consumer project inherited - `AbilitySystemDebuggerWindow`,
  `AbilityTimeControls`, `AssetNamingConvention`, the drawers and the custom
  Inspectors. The asmdef already declared that root namespace; the files did
  not follow it. A project that referenced one of those types adds a `using`;
  nothing in BossRush did, so nothing there changed. Inside the new namespace
  `Editor` now names a namespace, so the Inspectors derive from
  `UnityEditor.Editor` spelled out.
- **Documented the members of the types a consumer calls in code**: 58 of them
  across `AbilitySystem`, `AttributeSet`, `GameplayEffectContainer`,
  `AbilityInstance` and `GameplayCueDispatcher`.
- **Documented every public method in the runtime** - 83 more, from the
  validation entry points and the `AbilityEvent` factories to the motors, the
  cue parameter copies and the target filter builders. What is left is 275
  properties, 182 of which mirror a serialized field that already carries a
  `Tooltip`; the roadmap asks whether a third copy of those earns its keep.
- **Breaking: `TargetQueries.OverlapReceivers` is now `OverlapColliders`.** It
  was named after a concept the package removed - receivers are gone - and it
  returns colliders. Nothing called it.

### A sample that runs the whole loop, and the promises around it (Milestone 1)

The package had no sample, no allocation budget anyone could check, no upgrade
guide and no release policy. Everything a consumer needed to know lived in the
README or in someone's head. This is most of the `1.0` readiness milestone; what
is left of it is in `Documentation~/ROADMAP.md`.

- **Added the Melee Combat sample** (`Samples~/MeleeCombat`, importable from the
  Package Manager). One scene, no art: a three-step combo with an embedded
  damage effect per step, a roll whose i-frames are a frame window, a block
  whose first ten frames parry, target assist, damage on `Combat.Health` and
  poise on `Combat.Poise`, knockback, target-owned hit reaction and knockdown,
  and cues presented as coloured primitives keyed by their handle. The player
  drives it through an `AbilityInputMap`; the enemy drives the same abilities
  from a brain whose only ability question is `EvaluateActivation`.
- **Poise is in the sample, not in the package.** The heavy step subtracts from
  a second attribute with an empty `reactionEventTag`, so the hit asks for
  nothing; the fighter watches the attribute and sends itself `Event.Knockdown`
  when it breaks. The package supplies the attribute, the effect and the event -
  "poise broken means floored" is a game's rule and stays in the game.
- **Added the profiling scene** (`Samples~/MeleeCombat/Scenes/Profiling.unity`):
  pairs of fighters that fight, with the three counters, their timings and their
  allocated bytes on screen, after a warm-up that excludes the spawn.
- **Added `AllocationBudgetTests`** - the budget as a test rather than a claim.
  An idle tick, `EvaluateActivation`, the reads a HUD makes, ageing live effects
  and a record site with diagnostics off all allocate nothing, and the build
  fails when one of them starts to.
- **Added `PublicApiDocumentationTests`**: every public runtime type carries a
  `/// <summary>`. Twenty-six did not and now do. Per-member documentation is
  what is left of the milestone.
- **Added `PackageBoundaryTests`**: no package assembly references
  `Assembly-CSharp`, and no sample type is compiled into the package. The
  adapter direction was a rule in `CLAUDE.md`; now it is a test.
- **Added `Documentation~/UPGRADING.md`** - what to change in a project for
  every break so far, newest first - **`Documentation~/PERFORMANCE.md`** - the
  budget, what allocates and why, the counters, the markers, and pooling
  guidance - and **`Documentation~/RELEASE_CHECKLIST.md`**, which is the
  semantic-versioning policy, the one-minor-release deprecation window that
  starts at `1.0`, the changelog discipline, and the list run before a tag.
- 584 EditMode tests pass, 9 of them new.

### An actor can be saved and put back (Milestone 1)

Nothing survived a session. Cooldowns, charges, attribute base values and every
active effect lived in dictionaries on the components that owned them, and the
only way to start a run with a buff still ticking was to apply it again on
`Awake` and lie about the duration. The persistence milestone closes that, and
it closes it without charging the actors that never save.

- **Added `AbilityPersistence`**, static, with `Capture`, `Restore`, `ToJson`,
  `FromJson` and `SecondsSince`. There is **no save component, no serialized
  field and no tick**: a game that never calls it pays one unused class in the
  assembly, and a test walks `AbilitySystem`, `AttributeSet` and
  `GameplayEffectContainer` to pin that none of them grew a field for it.
- **Added `AbilitySaveRecord`**, a versioned, `JsonUtility`-shaped object
  carrying the actor's cooldowns, charges and restore timers, attribute base
  values, active duration and infinite effects with their stacks, periods and
  remaining time, loose tags, and the UTC instant it was taken.
- **What a record is not** is as deliberate as what it is. A running
  activation is a timeline, an animator state and a set of live tasks, and none
  of it is written down - a capture taken mid-combo says so in its report. The
  source actor of an effect is not written down either: a `GameObject` is not
  data, and an effect that comes back belongs to the target carrying it.
- **A restore is not a second application.** `GameplayEffectContainer` gained an
  internal restore path that rebuilds an effect's modifier slots, granted tags
  and persistent cue without calling `ExecuteEffect` and without running a
  period, so a saved poison resumes instead of dealing its first tick again. The
  gating is skipped too: a record is not an attack, and asking `CanApply`
  whether yesterday's buff may land would drop it for holding the very tag it
  granted.
- **`AbilityOfflinePolicy` is the decision the package refuses to make.**
  `Freeze` resumes exactly where the save left off; `Advance` ages cooldowns,
  charge timers and durations by the offline seconds, banking the remainder
  towards the next charge and dropping what ran out; `Expire` clears every clock
  and keeps only what has none. `AbilityPersistence.SecondsSince` hands a game
  the elapsed real time on its own, which is what a day/night cycle or a daily
  reset is actually built on - what that time *means* stays the game's.
- **Added `IAbilitySaveResolver`** and `AbilitySaveResolver`, which knows the
  actor's loadout and every effect authored inside those abilities. Ids become
  assets here and nowhere else, so a game whose buffs live in a catalog writes
  one class instead of bending the record.
- **Added `effectId` to `GameplayEffectDefinition`.** An instant effect needs
  none - it is over before a save could carry it - so nothing authored today has
  to change. A duration or infinite effect without one cannot be resolved in the
  next session: `Capture` leaves it out and names it in the report rather than
  writing an entry nothing can read back.
- **Added `IAbilitySaveMigration` and `AbilitySaveMigrator`.** A record is
  walked from the version it was written with up to the current one before
  anything is applied; a missing step, or a record from a newer version than the
  package knows, is refused whole with the reason in the report. Half a
  restored actor is worse than a load that says it cannot.
- **Every dropped entry is named.** `AbilitySaveReport` counts what was handled
  and carries a warning per ability that left the loadout, effect the resolver
  does not know, effect with no id, and actor with no attribute set.
- 575 EditMode tests pass, 17 of them new in `AbilityPersistenceTests`: the
  round trip, the three offline policies, the banked charge, the cue that comes
  back with its effect, the base value that proves the effect was not executed
  again, the migration chain in both directions, and the cost an actor that
  never saves does not pay.

### Minimum Unity version is now 6000.6

`Documentation~/ROADMAP.md` already required `6000.6`; `package.json`,
`README.md` and the repository instructions still declared `6000.0`, so the
Package Manager accepted the package on editors the documentation no longer
covers. The floor is `6000.6` everywhere now.

### Cues have a lifecycle, typed parameters and an owner (Milestone 3)

A cue was a tag fired at a frame: `GameplayCueTriggered(ability, tag, context)`
once, and nothing about it survived the frame. An effect could not raise one, a
buff had no way to keep a loop running for as long as it lasted, a presenter
that appeared late saw nothing, and a swing that met a parry sounded exactly
like one that landed. The presentation milestone closes all of that, and the
hit that lands is its first customer.

- **Added `GameplayCueEvent`** — `Execute`, `Add`, `WhileActive`, `Remove` —
  and **`GameplayCueParameters`**, the typed payload every cue carries: the
  tag, the event, the outcome, a stable `GameplayCueHandle`, the owner it is
  presented on, the source that caused it, the ability and effect behind it,
  level, magnitude, stack count, the activation context, the contact
  `AbilityTargetHit`, a location and a normal. It is a read-only struct that
  hands a presenter no runtime object — not the activation, not the spec, not
  the system — and a test pins that shape, which is how "a cue cannot modify
  authoritative state" is verified rather than promised.
- **Added `GameplayCueDispatcher`**, one per actor behind `AbilitySystem.Cues`.
  Presenters implement `IGameplayCuePresenter` (one method, `in` parameters)
  and register with it; it raises bursts with `Execute`, keeps persistent cues
  under their handle from `Add` to `Remove`, and refreshes them with
  `WhileActive` when a stack changes. **A presenter registering late is caught
  up with `WhileActive` for each live persistent cue and never with a replayed
  burst.** A presenter that throws is logged against the actor and skipped:
  cosmetics never end an activation. `AbilitySystem.GameplayCueTriggered` is
  now `Action<GameplayCueParameters>` and fires for every event after the
  filters; the replication sink still hears `Execute` and `Add`.
- **A `GameplayEffectDefinition` drives cues.** `cueTag` is executed on the
  target when an instant effect lands, and added when a duration or infinite
  effect activates, refreshed with the stack count when it stacks, and
  **removed when the effect ends — by removal, by expiry, or by the actor
  tearing down**. `ActiveGameplayEffect.CueHandle` names the cue an effect
  owns. `cueReplacements` say what to raise instead per
  **`GameplayCueOutcome`** — `Blocked`, `Immune`, `Parried`, `Missed` — and an
  empty replacement suppresses the cue for that outcome. The container
  classifies immunity itself and asks the effect about the rest through a new
  `ClassifyRefusal` hook, which the damage effect answers with `Parried` when a
  parrying target refused a parryable hit; `ResolveCueMagnitude` lets an effect
  say what the number means, and damage says the damage dealt.
- **Step cues know whether the swing landed.** Effects apply before cues on
  the same frame, so a `GameplayCueTrigger` on a step with effect triggers is
  raised with `Landed` or `Missed`; its new `suppressOnMiss` raises nothing on
  a miss instead. `AbilityInstance.HasRegisteredHitOnStep` is the question it
  asks. `TriggerGameplayCue` goes through the same dispatcher.
- **Filters and batching.** `IGameplayCueFilter` runs before presenters and may
  rewrite or suppress a cue — the hook for block, parry, immunity and miss rules
  that do not belong on one asset. Identical bursts inside one frame — same
  tag, same source, same ability, two effects on one hit — are batched into one
  (`BatchDuplicatesPerFrame`, on by default; `BatchedCount` says how many were
  dropped).
- The Ability Debugger window gained a **Cues** section: the persistent cues
  live on the actor, the presenter count and the batched-burst count. The
  history records every cue event with its outcome.
- **Breaking: `GameplayCueTriggered` changed signature**, and cue handlers take
  `GameplayCueParameters`. `AbilityInstance.Tick` hands the system the
  `GameplayCueTrigger`s that fired, not their tags.
- **Re-authored through the Editor:** all 11 damage effects raise
  `Cue.Impact.Hit` on the target when they land (`GameplayCueTags.HitImpact` in
  the consumer project). No replacement is authored yet — a parried or blocked
  hit is silent until Antônio picks its cue — and no VFX prefab is bound: the
  presenters' slots stay empty until the effects are authored, as before.
- **Consumer project:** `GameplayCuePresenter` implements
  `IGameplayCuePresenter`, registers with `AbilitySystem.Cues`, spawns a burst
  at the cue's location plus the binding offset, keeps a persistent cue's
  effect under its handle and destroys it on `Remove`; `CueReceived` carries the
  parameters.
- Milestone 3 leaves the roadmap. 558 EditMode tests pass, 20 of them new in
  `GameplayCueLifecycleTests`.

### The target owns its hit reaction, and `AbilityHitInfo` is gone (Milestone 2)

A hit used to end in a struct: the damage effect built an `AbilityHitInfo` —
amount, source, point, knockback, duration, impact, stun — and handed it to
whatever `IAbilityDamageReceiver` it found on the target, and that receiver's
owner decided what to do with the numbers in a callback. Two consumers wrote
two state machines around it, each driving its own animator, its own lock and
its own push. The roadmap called this the last missing architectural piece of
the combat loop, and it is built.

- **Damage is an attribute change.** `DamageEffectDefinition` gained
  `targetAttribute`, the attribute it subtracts from — the target's health, or
  its poise — and its `Execute` applies the number through the target's
  `AttributeSet` with the attacker as the change's source. A target without an
  attribute set is not a damage target; the effect resolves the actor to the
  nearest `AttributeSet` above the collider, which is also its per-trigger hit
  key. Validation refuses an effect with no attribute named.
- **The reaction is the target's own ability.** After the damage lands, the
  effect sends `reactionEventTag` — `Event.HitReaction` by default,
  `Event.Knockdown` for a hit that floors, empty for a tick that should not
  flinch — to the target's ability system, with the very application attached.
  **Added `HitReactionAbilityDefinition`**, the kind of ability that answers it:
  a `TimelineAbilityDefinition` whose activation reads
  `AbilityInstance.TriggeringSpec` and runs the knockback the effect authored as
  an `ApplyKnockbackTask` in its own scope, scaled by its `knockbackScale`. The
  clip, the lock tags, the movement lock and the cancel policy are the ability's
  as for any other, so **cancelling the reaction cancels its knockback with
  it**, and a periodic tick deals its damage and requests nothing.
- **The target decides.** The event goes through the target's activation rules,
  so an owner holding `State.Invulnerable` or `State.Dead` plays nothing and the
  attacker learns only that it counted a hit. Beneath that, the damage effect
  itself refuses a dead, invulnerable, or — when `canBeParried` — parrying
  target at the container, through a new `IsBlockedOn` hook every effect kind
  may implement; the authored Application Blocked Tags add to those rules and
  nothing removes them. The refusal raises `GameplayEffectContainer.EffectBlocked`
  with the spec, which is where a parry reward now hangs. And the order inside
  `Execute` is the point: the attribute changes first, a death handler runs
  inside that change, and the reaction request meets `State.Dead`.
- **`TryHandleGameplayEvent` carries a cause and tries every candidate.** The
  new overload takes the `GameplayEffectSpec` that raised the event; the
  activation it starts exposes it as `TriggeringSpec` and `AbilityInstance` now
  knows its `System`. Every granted ability whose trigger matches is tried in
  loadout order and a refused one hands the event to the next — a guard reaction
  that requires `State.Attacking` sits above the plain flinch and takes the hit
  only while the owner swings. `AbilityActivationTrigger` gained
  `restartWhenActive`: a repeated event restarts a running reaction, cancelling
  it as superseded, instead of being consumed by it; the physics reaction keeps
  the old consume, because its body is still flying.
- **Added `GameplayEffectDefinition.TryGetKnockback`** and
  `GameplayEffectSpec.TryGetKnockback`, the generic form of "does this
  application push?": the damage effect answers with the vector it authored,
  resolved against where the target stands, and the base effect answers no.
- **Targeting resolves actors, not receivers.** `TargetQueries.ResolveActor`
  walks a collider up to the nearest `AbilitySystem`, `AttributeSet` or
  `GameplayEffectContainer`, then the Rigidbody, then the collider itself.
  `AbilityTargetHit` lost `Receiver` and `DamageReceiver`. The filter's
  `allowNonDamageable` is now `includeNonActors` (same serialized value, so no
  asset changes): off, a wall on the layer is not a target; and a new
  `includeDead`, off by default, skips an actor holding `State.Dead`. "Alive"
  is a tag now, not a property a receiver reports.
- **Breaking: `AbilityHitInfo`, `AbilityImpact` and `IAbilityDamageReceiver`
  are gone**, not renamed: nothing in the package hands reaction data to a
  consumer callback, and a test pins that the three types no longer exist.
  `AbilitySystem.ApplyKnockback(in AbilityHitInfo)` went with them; the vector
  overload stays for code-driven pushes. `DamageEffectDefinition` lost `impact`
  and `stunDurationSeconds` — how long a target is locked is its reaction's
  length, not the attacker's field — and gained `targetAttribute` and
  `reactionEventTag`.
- **Re-authored through the Editor:** every damage effect in the project (11,
  all embedded) now names `Combat.Health` and sends `Event.HitReaction`, except
  the four that carried `Knockdown` impact — `GE_Damage_GrantAttack04`,
  `GE_Damage_GrantStellarPower`, `GE_Damage_FergusAttack04`,
  `GE_Damage_FergusStrongAttack` — which send `Event.Knockdown`. Their 1.1 s
  stun is gone; the knockdown abilities below own that time now. Six reaction
  abilities were authored and granted: `GA_Grant_HitReaction`,
  `GA_Grant_Knockdown`, `GA_Fergus_GuardHit` (requires `State.Attacking`),
  `GA_Fergus_VulnerableHit` (requires `State.Vulnerable`),
  `GA_Fergus_HitReaction` and `GA_Fergus_Knockdown`, in that order in
  `GAL_Fergus_Default`. `GA_Grant_Block` and `GA_Fergus_Parry` hold
  `State.Parrying` in a step tag window over their active frames instead of as
  a granted tag, because the damage effect now refuses a parrying target for
  as long as the tag is held, and a block's startup and recovery should still
  take a hit as they always did.
- **Consumer project:** `CombatAttributeSet` is an attribute set and nothing
  else — its `Damaged(int, int, GameObject)` and `HealthDepleted(GameObject)`
  come off the attribute change, and `IsDamageable`, `IsStunned`, `ApplyStun`,
  `RemoveStun`, `InterceptDamage` and the `Stun` attribute are gone.
  `IIncomingDamageHandler` is deleted. `PlayerController.OnDamaged`,
  `ApplyParryStagger` and `CancelDamageForCurrentState` are gone; `IsHitStunned`
  reads `State.Stunned` / `State.KnockedDown` off the ability system and the
  controller only clears its own velocity, roll and animator flag when a
  reaction starts. `FergusBoss` keeps the guard after a parry, the
  vulnerability window and death; the hit reaction, the guard-while-attacking
  and the knockdown are the four abilities above, the vulnerability's hit count
  reads `AbilityStarted` of the vulnerable flinch, and the parried attacker is
  sent `Event.HitReaction` to play its own reaction for its own length
  (`playerParryStaggerDuration` is gone). Both parries detect the hit through
  `EffectBlocked`. `CombatCancelTags.HitReaction` and `.Knockdown` are gone: an
  interrupting reaction raises `Cancel.Superseded` like any other.
- Milestone 2 leaves the roadmap. 538 EditMode tests pass, 20 of them new in
  `HitReactionTests`: the loop end to end, the three acceptance criteria, the
  parry seam, the candidate fall-through, the restart, and the periodic tick.

### The actor keeps a history, and the Editor reads it (Milestone 1)

An activation that was turned down left no trace at all. The typed rejection
code existed, but only the caller that asked saw it, and a combo that "did
nothing" in play was debugged by adding logs and swinging again. The last four
deliverables of the tooling milestone close that gap, and the milestone leaves
the roadmap.

- **Added `AbilityEventHistory`**, a fixed ring per actor behind
  `AbilitySystem.History`, and `AbilityEvent`, the timestamped entry it holds:
  game time, real time and frame, plus the ability, step, rejection code,
  transition reason, tag, effect and other actor the kind carries. What lands
  in it: **every refused activation attempt with its `AbilityActivationRejection`
  code**, every start with its target, step transitions with their reason,
  completions, **cancellations with their cancel tag**, whiffs, cues, gameplay
  events, effects delivered or refused on the source side and applied, blocked
  or removed on the target side, and task starts and endings. The ring is
  created on the first record, so an actor that never records never allocates
  it; `HasHistory` says whether it exists without creating it.
- **`EvaluateActivation` and `CanActivate` stay silent.** They are questions,
  asked by AI scoring every frame; only an attempt — `TryActivate`,
  `TryActivateWithTargets`, or an activation trigger inside
  `TryHandleGameplayEvent` — records a rejection. The trigger path now also
  names the two refusals it used to swallow: a trigger that does not interrupt
  a running ability (`AnotherAbilityActive`) and a running ability that refuses
  `Cancel.Superseded` (`BlockedByUncancellableAbility`).
- **Added `AbilityDiagnostics`**, the one switch. `Enabled` defaults on in the
  Editor and development builds and off in a release player; off, every record
  site is a single static bool read and nothing is allocated. It also carries
  `HistoryCapacity`, the global `EventRecorded` event the editor tooling hangs
  on, and the three counters.
- **Added `AbilityDiagnosticCounter`** and the three instrumented sites the
  roadmap asked for: **target queries** (`AbilityTargetQuery.Acquire`), **effect
  application** (`GameplayEffectContainer.Apply`) and **tasks** (start and tick).
  Each sample records count, total, average and maximum milliseconds, and
  managed bytes allocated. Allocation is read from the Profiler's own
  `GC Allocated In Frame` recorder, which is exact in the Editor and development
  builds and absent in release — Unity's Mono returns zero from
  `GC.GetAllocatedBytesForCurrentThread`, so that was never an option. The same
  sites carry the `GAS.TargetQuery`, `GAS.EffectApplication` and `GAS.Task`
  profiler markers into the Profiler window.
- **Added `AbilitySystemDebuggerWindow`** (Window > Fofuxo > Ability Debugger,
  or the button on the actor's Inspector), editor assembly only. For a selected
  actor: running activations with step, phase, frame, target, queued input and
  tasks; tags; every attribute with base, limits and modifier count; cooldowns
  and charges; active effects with stacks and remaining duration; and the
  history, filterable by kind, newest first. Its **"Can activate now?" audit
  runs `AbilitySystem.EvaluateActivation` for every granted ability** against a
  target the user picks — the same call `TryActivate` makes, so the window holds
  no rule of its own — and the loadout's own `TryValidate` above it.
- **Added `AbilityTimeControls`**, editor-owned slow motion and hit-stop.
  Hit-stop freezes time for a moment on every `EffectDelivered` event, and two
  further switches pause the Editor on the next refused activation or the next
  cancellation (owner teardown excluded). `Time.timeScale` is written only while
  a control is engaged and the value found is handed back afterwards. **No
  runtime code writes `Time.timeScale`**; a game that wants a real hit-stop
  listens to the same `EventRecorded` and owns its own.
- **The actor panel adds what is missing.** A section that reads "— none" now
  carries an Add button; for attributes it offers the project's concrete
  `AttributeSet` subclasses through `TypeCache`, never the base class, because
  `[RequireComponent(typeof(AttributeSet))]` would have added the base and left
  the actor with two sets and `GetComponent<AttributeSet>()` returning the wrong
  one. The component lands through `Undo.AddComponent` with no hide flags, so
  its own row stays in the Inspector. Nested types are never offered, which is
  what keeps a test fixture's private subclass out of a project's menu.
- `AttributeSet.Values` (a read-only view of every tracked attribute) and
  `AbilitySystem.GetCharges` are public, for the window and for any consumer
  tooling. `AbilitySystemDebugger` stays as the component that echoes
  transitions to the Console.
- Acceptance, checked: the window and the time controls live in the editor
  assembly; the runtime pieces are off in a release player and cost one bool
  read elsewhere; the tooling's validation is the runtime's `EvaluateActivation`
  and `TryValidate`, never a second copy. 518 EditMode tests pass, 58 of them
  new: the ring, every recorded kind, the silence of the questions, the counters
  at each site, the Add filter, the hit-stop decision and the audit's equality
  with the runtime verdict.

### One asset for the keybinds, one Inspector for the actor

Input lived in two shapes at once: a list of `InputActionReference` bindings and
a second list of action *names* resolved against an `InputActionAsset`, both on
the router, both meaning the same thing. And an actor's configuration was spread
across four components, so reading a prefab meant clicking through all of them.

- **Added `AbilityInputMap`**, a `ScriptableObject` listing which action
  activates which ability. `AbilityInputRouter` reads one of these and nothing
  else: the two binding lists and the loose `InputActionAsset` field are gone.
  A character swaps its controls the way it swaps its loadout, and the actor
  shows one field instead of an array read entry by entry.
- **The map names actions instead of referencing them.** An
  `InputActionReference` is a sub-asset to drag per action; a name is picked once
  from a dropdown of the asset's own actions and validated afterwards. A renamed
  action fails `TryValidate` with the name it can no longer find, which is the
  failure a string mapping is usually accused of hiding.
- **An entry with no ability is still a binding**: the router enables the action
  and raises `ForwardedInput` for it, so a combat keybind that is not an ability
  lives in the same asset as the ones that are.
- **Added `AbilitySystemEditor`**, one panel for the actor: loadout with its
  audit, the attribute set, the input map with its bindings listed, the physics
  body, and diagnostics — each drawn through the owning component's own
  `SerializedObject`, so the data still lives where it belongs and Undo and
  prefab overrides behave as they always did. Merging the *data* would force an
  ability system onto every actor that only has health; what is merged is the
  reading of it.
- **The panel catches the mistake neither asset can see alone**: an input bound
  to an ability the actor was never granted. Both assets look right; the input is
  refused at runtime.
- **In play mode the same panel is the readout** — active ability, step, phase,
  frame, queued input, continuation deadline, tags, last transition. That is the
  Milestone 1 deliverable for a running manual combo.
- `AbilitySystem` carries `[AddComponentMenu("Fofuxo/Gameplay Ability System")]`,
  so the component is found and titled by the name of the system it is.
- **Breaking: a router's bindings must be re-authored into a map.** The consumer
  project's `GIM_Grant_Default` was created and wired in this change.
- The editor assembly now references the input assembly, which is what lets the
  naming convention cover `GIM_` and the panel draw the router. 460 EditMode
  tests pass.


### The timeline is a kind of ability, not the shape of every ability

Making `steps` optional stopped a new ability from arriving with a swing already
authored, but the *field* was still there on every ability, and so were
`stepAdvancement`, the step advance tag and the timeout policy. An ability that
displaces its owner with no animation carried an empty timeline around, and the
Inspector had a Steps list and a Timeline section to show for it.

`AbilityDefinition` now holds only what is decided once per activation —
identity, targeting, range, cost, cooldown, charges, tags, exclusion,
cancellation, activation input, AI weight, preview clip — and **no steps at
all**. `TimelineAbilityDefinition` is a new subclass that adds the ordered steps
and everything derived from them. That is the split Unreal draws: a montage is
something an ability plays, never a field every ability carries.

- **Breaking: every stepped ability asset changes class.** The eight in the
  consumer project were migrated in this change by re-pointing `m_Script`; their
  `steps` deserialize into the new type unchanged because the field name matches,
  and asset GUIDs never moved, so loadouts and scene references survived. Unity
  reports all eight as `TimelineAbilityDefinition`. An asset that is *not*
  migrated keeps loading as a plain ability and silently loses its timeline the
  next time it is written — migrate before authoring.
- **Breaking: `Fofuxo/Abilities/Ability` now creates the generic ability**, and
  `Fofuxo/Abilities/Timeline Ability` creates the stepped one. The shorter name
  belongs to the more general type.
- **Breaking: the step API moved off `AbilityDefinition`** — `Steps`,
  `StepCount`, `StepAt`, `FirstStep`, `IsCombo`, `TotalDuration`,
  `StepAdvancement`, `StepAdvanceEventTag`, `StepTimeoutPolicy`,
  `WaitsToAdvance`, and the `OnStepStarted` and `CanAdvanceStep` hooks. A caller
  that needs them says which kind of ability it is talking about:
  `ability as TimelineAbilityDefinition`. The three consumer scripts that read
  them were updated in the same change.
- **`AbilityInstance.Timeline` is the seam**, resolved once when the activation
  starts rather than cast at every step lookup. `Step` reads through it, and null
  is the ordinary shape for an ability whose work is code.
- **`IsMovementLockedAtFrame(step, frame)` became
  `IsMovementLockedDuring(instance)`**, virtual on the base. The base answers
  with the ability-level flag; the timeline overrides it to let a step hand
  movement back partway through. The old signature took an `AbilityStep`, which
  is exactly the coupling this change removes.
- **`RequiresMotor` and `TryGetAuthoringWarning` are virtual now.** A plain
  ability needs a motor only for an activation-time approach; a timeline adds its
  steps. The warning follows the same shape: the base warns that nothing can end
  a plain ability of the base type, and the timeline warns when it has no steps
  to run.
- **`manualAdvanceWindow` is gone.** It was serialized, hidden from the Inspector
  and read by nothing.
- The test suite moved with it: the fixtures that author frames create timeline
  abilities, and every reflection helper in the suite now walks the type
  hierarchy — a private field of a base class is invisible to a single
  `GetField`, which is what a subclass makes of every ability field. 453 EditMode
  tests pass.


### An ability no longer has to be a timeline

A new ability asset arrived with a step, a frame rate and a startup/active/
recovery window already filled in, and validation refused to save one without
them. That made the animation-driven swing the only shape an ability could take
— and the consumer already has abilities that are not that shape: one displaces
the player and has no animation at all, and an ability whose animation is played
somewhere else had to invent a timeline to be allowed to exist.

**Steps are now optional.** An ability with none has no timeline, no frames and
no phases. It activates, runs whatever its derived type does in `OnActivated`,
and stays active until something ends it — `TryCompleteActiveAbility`, a cancel,
or the owner going away. Cost, cooldown, tags, range, charges and exclusion are
untouched by any of it: they were never step data.

- **Breaking (authoring): a new ability asset starts with no steps.** Three
  places conspired to make one appear — the field initializer, `OnValidate`
  recreating a step whenever the array went empty, and a validation error
  refusing zero. Existing assets are untouched: their steps are serialized and
  keep working exactly as before.
- **`HasTimeline` says which shape an ability is**, and `AbilityInstance.Tick`
  reads it: with no timeline there is no last frame to complete on, so the
  activation is never completed by the clock. A null step *inside* a definition
  that does have a timeline still ends the activation, because that is a broken
  asset and ending is the safe reading of it.
- **`OnStepStarted` no longer fires for an ability with no steps.** There is no
  step to have started, and reporting index 0 was a lie a subclass would have
  had to guard against.
- **An ability-level movement lock now holds without a step.**
  `IsMovementLockedAtFrame` short-circuited on a null step, which was correct
  when null meant "broken" and wrong the moment it means "no timeline": the flag
  is the whole rule when there is no unlock frame to read.
- **The base type with no steps warns in the Inspector.** It is legal — a
  consumer may end it from outside — but nothing *in* it can, so it would hold
  its granted tags and its exclusion until something cancelled it.
- **`AbilityWithoutStepsTests`** pins all of it: activation, the missing step
  hook, staying active through 600 ticks, ending on demand, cancelling, hosting
  a task and outliving it, the movement lock, and the warning. 453 EditMode
  tests pass.

### Derived ability fields are grouped by the class that declares them

The Inspector drew every field its hand-written layout did not recognise under
one heading named after the concrete type. With two levels of inheritance that
heading was wrong for half of what sat under it: a damage ability's own settings
and its child's settings were one undifferentiated pile.

Now there is one section per class that declares fields, ordered base first —
what every ability has, then what this kind of ability adds, then what this one
adds. A field added to `AbilityDefinition` without a line in the layout lands
under `AbilityDefinition`, where it belongs, instead of being blamed on the
subclass being inspected.


### The ability timeline is dragged, not typed (Milestone 1)

Frame numbers were authored blind. `startupEndFrame`, `activeEndFrame`, the
combo window, the movement unlock, the displacement window, every tag window and
every effect trigger were separate integers in separate foldouts, and the only
way to see what they added up to was a read-only text block at the bottom of the
Inspector that printed them back as prose.

The ability Inspector now draws them: one block per step, and inside it a lane
for the phases, the combo window with its late grace, movement, displacement,
each tag window, the effect triggers and the cues. Dragging a marker writes the
frame it lands on.

- **Added `AbilityTimelineView`**, the lane view, and `AbilityTimelineLayout`,
  the frame-to-pixel geometry it drags against. The geometry is a type of its
  own because it is the part that can be wrong invisibly: a marker that reads
  back one frame off from where it was dropped looks right and authors wrong.
  `AbilityTimelineLayoutTests` pins the round trip for every frame of a step.
- **Each step gets its own ruler across the full width**, instead of one
  continuous ruler shared by the combo. Steps never overlap in time — an advance
  cuts the running step short — so a shared ruler would spend its resolution on
  a relationship that does not exist, and spend it worst on the long combos that
  need the resolution most.
- **Every drag lands through `AbilityStep.Sanitize`**, the runtime's own
  clamping, so dragging the startup boundary past the active one pushes it
  exactly the way typing the number always did. The view owns no rule about what
  a legal frame is, and one drag is one undo entry.
- **Zero is not a frame**, so dragging can never produce it: it is how these
  fields say "no window at all". A right-click menu on the affected handle is
  where a window goes back to running until the step ends, and a `+` button on an
  empty lane is where one is authored in the first place.
- **Breaking (authoring only): the `Resolved Timeline` text block is gone**, and
  the step's numeric frame fields moved into a `Frames (numeric)` foldout that is
  collapsed by default. Nothing serialized changed and no asset needs
  re-authoring — the numbers are still there, one click away, for the frame that
  is faster to type than to aim at.
- The Editor assembly gained an `AssemblyInfo` with `InternalsVisibleTo` for the
  test assembly, and the test assembly now references the Editor assembly, so
  editor tooling can be pinned by the same EditMode suite as the runtime.

### An interrupting event trigger decides before it cancels (Milestone 1)

Found by the new precedence tests, and the reason that deliverable existed:
every policy passed on its own, and the order they compose in was wrong.

`TryHandleGameplayEvent` force-cancelled the running abilities *before*
evaluating whether the triggered ability could activate at all. Two consequences,
both silent:

- **An incoming event stripped the tags that would have refused it.** An owner
  mid-roll holds `State.Invulnerable`; the cancel released it, and the reaction
  then passed the blocked-tag check that i-frames exist to win. The i-frames were
  destroyed by the very event they were supposed to refuse.
- **It cancelled abilities whose own policy refuses to be interrupted.** A death
  ability authored as `Cancel Policy: Nothing` was ended by a physics force. The
  group-exclusion path had always respected the cancel policy here; this one
  never did.

The trigger path now evaluates the activation first — every rule but the
exclusion, which is the one it is about to satisfy itself — then checks that
every running ability accepts the request, and only then cancels.

- **Breaking: an interrupting trigger cancels with `Cancel.Superseded`**, not
  `Cancel.PhysicsForce`. The cancel was raised for *any* event trigger, so a
  hit-reaction trigger reported "physics force" to the ability it ended.
  `Cancel.PhysicsForce` stays in the vocabulary for a consumer to raise.
- **Added `ActionPrecedenceTests`**, pinning the order the consumer depends on —
  `death > hit reaction > roll > attack, block`, with attack and block mutually
  exclusive rather than ordered — as a matrix and then as one end-to-end
  sequence. The hit reaction stands in for the target-owned ability of
  Milestone 3, authored the way the physics reaction already is.

### Teardown, consumer failure and the reentrancy contract (Milestone 1)

Three pieces of behaviour that were already implemented and entirely uncovered —
the dangerous shape for teardown code, which only runs once something has gone
wrong.

- **Fixed: a destroyed `GameplayEffectContainer` left its granted tags on the
  owner's `AbilitySystem`.** The release path skipped `RemoveEffectTag` whenever
  the container was tearing down. The guard was meant to stop it looking for a
  component on a half-destroyed actor; it also stopped it releasing through a
  system it had already resolved and which was still alive. It now skips the
  lookup, not the release. Found by the new teardown tests.
- **Added `LifecycleTeardownTests`**: disabling or destroying an owner
  mid-activation cancels the instance, finishes its tasks and the actor's,
  releases its granted tags, and is safe to do twice. Destroying an effect
  container leaves no active effect and no stranded tag.
- **Added consumer-failure coverage.** An effect that throws mid-timeline leaves
  no half-started ability, no leaked task and no tag held forever, and an
  `AbilityCompleted` or `AbilityCancelled` handler that throws cannot undo the
  teardown that already happened.
- **Added `EventReentrancyTests` and wrote the contract down.** Every activation
  event is raised outside the state change it reports: `AbilityStarted` after
  the activation is fully started, `AbilityCompleted` and `AbilityCancelled`
  after it is fully ended. So "when the swing ends, start the next one" — the
  first thing a boss brain reaches for — is safe, and restarting *the same*
  ability from its own cancel handler is not blocked by the
  one-ability-at-a-time default. The README tabulates what each event's handler
  may call back into.
- **`GameplayCueTrigger` gained a public constructor**, matching
  `AbilityEffectTrigger`, so a cue can be built in code.
- **The README publishes the `0.x` compatibility rule** under `Status`: no
  compatibility promise and no deprecation window below `1.0`, with what you get
  instead — every break in the changelog, every asset a break invalidates named,
  and renames that are renames rather than aliases.

### An ability that moves is refused on an actor that cannot (Milestone 1)

`AbilitySystem.Motor` never returned null. An owner with no `IAbilityMotor` and
no Rigidbody got a wrapper around a null body: it accepted every displacement
and moved nothing. The ability activated, played its animation, held its tags,
finished on time, and travelled zero metres — which reads as a broken animation,
not as a missing component.

- **Breaking: `Motor` is null when the owner has neither.** The only consumer
  inside the package already null-guarded it (`Motor?.Move`), so nothing else
  changes shape.
- **Added `AbilitySystem.HasMotor`**, and `AbilityActivationRejection.MissingMotor`.
  An activation whose ability moves its owner is refused up front on an actor
  that has nowhere for the travel to land.
- **Added `AbilityDefinition.RequiresMotor` and `AbilityStep.MovesOwner`**: true
  for an activation-time target-assist approach, and for any step with a
  displacement window or an approaching assist of its own. Whether the actor can
  satisfy it is decided by the system, because a motor belongs to the actor and
  an asset cannot see one.
- **A move task started from code says so.** `RunTask` and `RunActorTask` log an
  error when an `AbilityMoveTask` starts on a motorless actor: an authored
  displacement window is gated at activation, but a task started by hand has no
  such gate.

`TargetAssistDefinition.approachTarget` defaults to `true`, so **every ability
with a target assist now requires a motor**. Both BossRush ability owners —
`Player` and `FergusPrefab` — carry a Rigidbody, so nothing there is refused.

### Gameplay effects validate themselves (Milestone 1)

`GameplayEffectDefinition` had no validation at all. An ability validated its
steps, its frames and its tags, and then pointed at an effect asset nobody had
checked.

- **Added `GameplayEffectDefinition.TryValidate`**, virtual, overridden by
  `DamageEffectDefinition`, `PhysicsForceEffectDefinition` and
  `DebugDrawEffectDefinition`. It looks for the mistakes that produce silence
  rather than an error: a modifier with no attribute, an attribute folded in
  with a coefficient of zero, a `Duration` effect with no duration, an `Instant`
  effect carrying a period, empty entries in a tag list, and overflow effects
  that are null, self-referencing, or authored against a stack limit of zero.
- **An empty `Target Layers` mask is now an error in `Shape` mode.** An empty
  mask never meant "nothing": `HitShape` falls back to `Physics.AllLayers`, so
  the query swept terrain, triggers and the attacker's own colliders. A shape
  carried outside `Shape` mode — the debug draw copies one for drawing — is not
  layer-checked, because it is never handed to the physics layer.
- **Effects are validated through the ability that fires them.**
  `AbilityStep.TryValidate` validates every effect its triggers reach and
  `AbilityDefinition.TryValidate` does the same for its parry effects, naming
  the step, the trigger and the effect asset. This is the half that matters: an
  effect is reached indirectly, so the asset an author has open has to report it.
- **Added `GameplayEffectDefinitionEditor`**, registered for derived classes, so
  every effect asset draws its validation state and its naming violation. The
  physics-force Inspector now derives from it instead of replacing it, and the
  ability Inspector draws the same box under each embedded effect — an embedded
  effect has no Inspector of its own.

Whether the layers you picked are the ones damageable actors live on stays a
consumer question: the package validates that a mask was set, not what is in it.

#### Assets this invalidates

- `GE_Damage_FergusCombo`, embedded in
  `Assets/Characters/Fergus/Abilities/GA_Fergus_Combo.asset`, uses `Shape`
  targeting with an empty `Target Layers` mask and therefore queries every layer
  in the project. The other four damage effects in the same combo are set to
  layer 9. Set it to match them and re-test Fergus's combo.

### The ending vocabulary tells failure and teardown apart (Milestone 1)

An ability ends as `AbilityCompleted` or as `AbilityCancelled` carrying a tag,
and the tag was lying in two of the cases that matter most.

- **Added `Cancel.Failed`.** An exception thrown by an ability hook, a gameplay
  effect, a damage receiver or an event handler used to tear the activation down
  under `Cancel.Manual`, indistinguishable from the game deliberately cancelling
  it. The exception is still logged first; only the tag changed.
- **Added `Cancel.OwnerTeardown`.** `AbilitySystem.OnDisable` and `OnDestroy`
  also cancelled under `Cancel.Manual`. A handler now has a way to tell "the
  player rolled out of the swing" from "there is no actor here any more", which
  is the difference between queueing a follow-up and touching a component that
  is being destroyed.
- **Breaking: `Cancel.SupersededByGroup` is now `Cancel.Superseded`**, and
  `CommonGameplayTags.CancelSupersededByGroup` is `CancelSuperseded`. The tag
  was already raised by `Cancel Any Active`, a policy with no group at all, so
  the name described a rule the ended ability had not been subject to. Nothing
  in BossRush referenced the old name; an ability that listed it under
  `Only Listed Tags` must list the new one.

The seven tags the package raises, and what a consumer should read into each,
are tabulated in the README under **Tags and cancellation**.

### Gameplay effect specifications

- **Added `GameplayEffectDefinition`**, the one effect asset, with `Instant`,
  `Duration` and `Infinite` duration policies, attribute modifiers, periods,
  stacking, overflow, granted tags, application-required and -blocked tags,
  granted immunity tags and removal tags. It is concrete: an effect that only
  changes attributes and tags needs no subclass, and the subclasses that remain
  exist because they also do something the attribute layer cannot express.
- **Added `GameplayEffectSpec`, the per-application data.** Source, target,
  level, the attributes captured at application, the magnitudes calculated from
  them, the tags this application grants, the target data it landed on, and
  per-application overrides for duration, period and modifiers. One spec per
  target per application; the definition is never written to.
  `GameplayEffectSpecTests.RepeatedApplications_LeaveTheAssetByteIdentical`
  applies one asset forty times at varying levels and asserts the serialized
  asset is unchanged.
- **Added explicit capture rules.** `GameplayEffectMagnitude` declares whose
  attribute it reads (`Source` or `Target`) and whether it snapshots at
  application or re-reads while the effect is active. Live magnitudes are
  recalculated on the container tick and their modifier slots rewritten in
  place, so buffing an attacker mid-effect moves what the effect contributes.
  A subclass that authors a magnitude outside the modifier list declares it
  through `CaptureOwnMagnitudes`, which is how damage scaling obeys the same
  rules.
- **Added `ActiveGameplayEffect` and `GameplayEffectHandle`.** A handle is
  issued when an effect becomes active, is never reused, and stays answerable
  after the effect it names is gone: `IsActive` false, zero stacks, zero
  remaining, and every mutation refused. This is what removal by
  `(attribute, source)` could not express: two applications of one attribute
  from one source are two handles.
- **Added `GameplayEffectContainer`**, the per-actor owner of the whole
  lifecycle: application gating, instant execution, duration, periods,
  stacking, overflow, immunity, granted tags and removal. It resolves the
  actor `AttributeSet` for numbers and `AbilitySystem` for tags, needs neither,
  and is added on demand by `GameplayEffectContainer.For` — active effects are
  runtime state, so nothing about them is authored on a prefab.
- **Stacking is a policy, a scope and an overflow rule.** `EffectStacking`
  (`Stack`, `Refresh`, `Ignore`) decides what a second application does;
  `GameplayEffectStackScope` decides whether two attackers share one stack;
  a stack limit with overflow effects, `Deny Overflow Application` and
  `Clear Stack On Overflow` decide what a full stack does. A stack attaches one
  modifier set per stack rather than scaling a magnitude, so additive stacks
  accumulate and multiplicative stacks compound with no rule per operation.
  The most recent application defines the effect: refreshing or stacking adopts
  the new spec and keeps only the stack count.
- **The one-query-per-trigger-frame guarantee moved with the targeting and is
  still covered.** Effects that fire on the same frame of the same step with
  the same shape share one acquisition through `AbilityInstance.AcquireTargets`;
  `AbilityTargetDataTests.SameShapeOnOneFrame_RunsOneQueryBothEffectsRead` and
  `PhysicsForceEffectTests.DamageAndForceOnOneFrame_ResolveTheSameTarget` both
  run against the new model.

### Breaking changes

- **`AbilityEffectDefinition` is gone**, replaced outright by
  `GameplayEffectDefinition`. There is no bridge and no deprecation window: two
  live paths for one concept is worse than one clean break, and the roadmap
  deliverable that asked for a bridge has been corrected to say so.
  `AbilityEffectContext` is now `GameplayEffectContext`, and
  `AbilityEffectDefinition.Apply(context)` is
  `GameplayEffectDefinition.ApplyFrom(in context)`, which returns how many
  targets accepted the effect.
- **`ModifyAttributeEffectDefinition`, `DurationAttributeEffectDefinition` and
  `PeriodicAttributeEffectDefinition` are gone.** All three were one duration
  policy each; they collapse into `GameplayEffectDefinition`.
  `GameplayEffectDefinition.cs` inherits the retired
  `ModifyAttributeEffectDefinition` script GUID, so the one asset built on it
  rebinds to the class that replaced it instead of losing its script.
- **`DamageEffectDefinition`, `PhysicsForceEffectDefinition` and
  `DebugDrawEffectDefinition` are now `GameplayEffectDefinition` subclasses.**
  Their `shape`, `filter`, `maximumTargets` and `restrictToAbilityTarget`
  fields moved into the shared `targeting` block, so every effect reaches its
  targets the same way and the switch over effect kinds in the query gizmos and
  in the consumer debug overlay is gone. `DamageEffectDefinition.damage` is a
  `GameplayEffectMagnitude` and absorbed `scaleAttribute` and `scaleFactor`;
  `DebugDrawEffectDefinition.duration` is `drawDuration`, because the base owns
  the name `duration` now.
- **`AbilitySystem.ApplyReactiveEffect` is gone**, replaced by
  `ApplyGameplayEffect(effect, context, source, level)`. The old entry point
  fabricated a throwaway `AbilityInstance` purely to build a context; since
  ability tasks landed an instance owns a task scope, so anything that started a task
  through it would never have been cancelled. The spec is the application
  context, and no activation is invented to carry it.
- **The duration layer left `AttributeSet`.** `ApplyDurationModifier`,
  `ApplyPeriodicModifier` and `RemoveModifiers(attribute, source)` are gone,
  along with the internal duration entries. The set aggregates and does not
  decide lifetimes: it gained `AddModifier` returning a stable slot id, plus
  `UpdateModifier(slot, …)` and `RemoveModifier(slot)`. `EffectStacking` moved
  to the effect layer. Regeneration stays on the set, because it is authored on
  the set and belongs to no application.
- `AttributeValue.Modifiers` is replaced by `ModifierCount` and
  `GetModifier(index)`; modifiers are keyed by slot, so two identical modifiers
  from one source no longer remove each other.
- `HitShape` gained `WithOrigin` and `WithLayers`, so code can build a shape
  without a constructor per anchor.

### Costs and cooldowns stay explicit

The milestone asked whether to migrate costs and cooldowns onto the generic
lifecycle. Both stay as they are, and here is why.

- **Cooldowns stay a `Dictionary<AbilityDefinition, float>` over `Time.time`.**
  Expressing them as effects needs either a cooldown asset per ability — asset
  explosion for no behaviour — or one shared asset plus a per-ability cooldown
  tag authored on every ability, which means re-authoring every ability to
  express what one float on the ability already says. Neither is clearer, and
  the current form needs no tick to stay correct. The gain the effect model
  would bring, cooldown-reducing effects, has no consumer. Revisit when one
  exists.
- **Costs stay `AbilityCost` on the ability, paid by `PayCosts`.** The cost is
  authored inline on the ability that pays it, the activation check must stay
  side-effect free and is already typed (`InsufficientAttribute`), and payment
  is a single instant change that already goes through
  `AttributeSet.ApplyInstantModifier` — the same call an instant effect makes.
  Moving it would add an asset per ability and change nothing that runs.

### `AbilityHitInfo` survives, and what is missing

`AbilityHitInfo` was scheduled to die once the spec-and-reaction path covered
its consumers. The spec half now covers most of it: the damage number is a spec
magnitude captured from the source, the hit point and direction come from the
target data on the spec, source and target are spec fields, and immunity,
blocking and i-frames are expressible as application-blocked tags with a
`GameplayEffectContainer.EffectBlocked` seam a parry can hang on.

What is not covered is the reaction half. `Impact`, `StunDuration`, `Knockback`
and `KnockbackDuration` are consumed by `PlayerController.OnDamaged` and
`FergusBoss.OnDamaged`, which drive animator triggers, NavMeshAgent detachment
and their own combat state machines. The roadmap target design routes those
through a target-owned hit-reaction ability, and **no hit-reaction ability is
authored for either character** — which is what that section of the roadmap
already names as missing. Replacing `AbilityHitInfo` with a differently named
payload carrying the same five fields would be a rename, not a migration.

So `AbilityHitInfo` stays, demoted: it is no longer the effect contract, only
the payload `DamageEffectDefinition` hands to `IAbilityDamageReceiver`. It dies
when a hit-reaction ability exists for the Player and for Fergus and the two
controllers read their reactions off an activation instead of off a struct.

`CombatHealth.ApplyStun` did move: it is now a code-driven duration effect
through `GameplayEffectContainer.ApplyDynamic`, and it returns the handle that
names it. `CombatHealth.RemoveStun` ends one early.

### Consumer project

- `GrantAbilityIntegration.TryParry` applies parry rewards through
  `AbilitySystem.ApplyGameplayEffect`.
- `CombatDebugOverlay` reads one `Targeting.Shape` instead of switching over
  three effect classes.
- 18 authored effect objects were re-authored onto the new model: 11 damage,
  5 debug draw, 1 physics force and 1 heal. Values are preserved; the fields
  they live in changed.


### Ability tasks and movement

- **Added `AbilityTask` and `AbilityTaskScope`.** A task is one piece of latent
  work belonging to one activation, cancellable, with a single terminal state
  (`Succeeded`, `Failed`, `Cancelled`) reported through one `Finished` event.
  `AbilityInstance` owns a scope, and `EndActivation` cancels it before any
  other cleanup runs, so **a task never outlives the activation that started
  it** and a cancelled task never ticks again. `AbilitySystem.RunTask` starts
  one on an activation; `RunActorTask` starts one owned by the actor, for the
  rare work that belongs to no activation.
- **Added the wait tasks:** `WaitDelayTask`, `WaitGameplayEventTask`,
  `WaitInputTask` (both edges, with `WaitInputTask.Press` / `Release`) and
  `WaitAnimationEventTask`. They reuse the entry points that already existed
  rather than duplicating them: gameplay events arrive through
  `TryHandleGameplayEvent`, input edges through
  `AbilityInputRouter.NotifyInputPressed` / `NotifyInputReleased`, and animation
  events through a new `AbilityAnimationEventBridge.EmitAbilityAnimationEvent`
  kept separate from `EmitGameplayCue` so a cosmetic cue can never release
  gameplay work.
- **Added `AcquireTargetsTask`**, running the same `AbilityTargetQuery` the
  effects, the target assist and the gizmos run. It can adopt what it finds as
  the activation targets through the new
  `AbilityInstance.RetargetFromTargets`, and either query once or keep looking.
- **Added `IAbilityMotor`, the engine seam for movement.** A task produces a
  delta; the motor decides what it does to the body, under an explicit
  `AbilityMovementCollision` mode: `Unswept` (root-motion-like, the previous and
  still default behaviour), `Swept`, or `MotorAuthority`. The adapters —
  `AbilityRigidbodyMotor`, `AbilityCharacterControllerMotor` and
  `AbilityDelegateMotor` — live in a new
  `Uayten.FofuxoGameplayAbilitySystem.Motors` assembly outside the core and
  depend on nothing but Unity and the core assembly. A project with no motor
  component keeps the owner Rigidbody moved with `MovePosition`, exactly as
  ability displacement always did, so nothing existing had to be re-authored.
- **Added the movement tasks:** `MoveByDistanceTask`, `MoveTowardTargetTask` and
  `ApplyKnockbackTask`, each with an explicit
  `AbilityMovementDirectionPolicy` (`Snapshot` or `Track`). Speed is remaining
  distance over remaining duration, so total travel is the configured distance
  whatever the frame pacing.
- **Concurrent movement now has rules.** A movement task carries a `Priority`
  and an `AbilityMovementConflictPolicy`: `Yield` (the default — waits without
  spending its budget, so it still travels its full distance later), `Blend`
  (adds its delta on top of the winner) or `Abort`. The highest priority that
  has not yet moved owns the body, ties breaking by activation age, oldest
  first, which is exactly what the single displacement did when everything sits
  at the default priority.
- **Step displacement is now a task.** `AbilityInstance.BeginDisplacement` and
  `TickDisplacement` are a facade over `MoveByDistanceTask`, and
  `AbilityInstance.Tasks` / `DisplacementTask` expose it. The arithmetic,
  the authored frame window and the resulting travel are unchanged.
- **The target-assist approach is now `MoveTowardTargetTask`.** It measures the
  gap the same way the prelude always did — distance to the target surface,
  minus the owner extent along the approach, minus the authored stopping gap —
  through a new shared `AbilityBodyExtent`. `TargetAssistDefinition` is
  untouched and no authored asset changed.
- **Knockback moved into the package** as `AbilitySystem.ApplyKnockback` /
  `CancelKnockback` / `IsKnockbackActive`, replacing the per-consumer
  `FixedUpdate` loops that each reimplemented the same push. It runs on the
  physics step, keeps its vertical component, and is owned by the actor rather
  than by whatever activation happens to be running, so cancelling an
  interrupted combo cannot eat the push. A hit-reaction ability that should own
  its knockback runs `ApplyKnockbackTask` itself through `RunTask`.
- `AbilitySystem` gained a `FixedUpdate` that drives only the tasks asking for
  the physics step. Nothing on the ability timeline runs there.
- **Breaking (internal):** the `Rigidbody` parameter is gone from
  `AbilityInstance.BeginDisplacement`; the body is the motor concern now.
  `AbilityInstance.DisplacementBody` is still readable but informational.
- `SpawnProjectileAndWait` is deliberately not implemented: it waits for a
  consumer that actually fires projectiles.

### Steps, input, and cancellation

- **Two more advancement modes.** `AbilityStepAdvancement` gained `OnEvent`,
  which carries a step on a gameplay event matching the ability's
  `Step Advance Event` tag, and `OnCondition`, which polls the ability's
  `CanAdvanceStep` override or the system's `StepAdvanceCondition` delegate.
  `Automatic` and `Manual` keep their serialized values, so no authored asset
  changed. The mode says what has to happen; the step's combo window still says
  when, so all four share one set of authored frames.
- Added `Combo Input Grace Frames` per step: a late grace window past
  `Combo Input End Frame`. A request landing there is accepted and advances
  immediately, because the window it belonged to has already closed. Zero — the
  default — is the previous behaviour exactly.
- Added `Branches` per step. The first branch whose tag the owner holds decides
  which step the activation advances into, so a combo can fork on a stance, a
  phase, or a buff an earlier step granted. A branch may point backwards, which
  is how a step loops. An empty branch list keeps the plain next-step order.
- Added `Step Timeout Policy` on the ability, for what a waiting step does when
  its timeline runs out with nothing to advance it: `CompleteAbility` (the
  default, and the previous behaviour), `AdvanceStep`, `CancelAbility` (raising
  the new `Cancel.StepTimeout`), and `HoldLastStep`.
- **Queued intent, deadlines, and the last transition reason are public.**
  `QueuedStepIntent`, `HasQueuedStepAdvance`, `ComboInputDeadlineFrame`,
  `ComboInputTimeRemaining`, `StepTimeRemaining`, `IsHoldingLastStep` and
  `LastTransition` sit beside `ActiveStep`, `ActiveStepIndex` and
  `IsComboWindowOpen`. Two new events report the same thing as it happens:
  `StepTransitioned` for every step change and every ending, and
  `StepIntentChanged` for the lifecycle of a buffered request. Every accepted
  request now reaches exactly one terminal state — consumed, expired, or
  rejected on arrival for being past the deadline — instead of being dropped
  silently.
- **Cancellation propagates in explicit scopes.** `TryCancelActiveStep` ends the
  running step and lets the ability's timeout policy decide what follows;
  `TryCancelAbility` ends one named activation; `TryCancelActiveAbility` and
  `ForceCancelActiveAbility` end every activation that accepts the request. One
  shared teardown releases tag windows, granted tags, the pending request and
  the animation, so a new ending cannot forget half of it.
- A step whose targeting prelude finds nothing, on an ability that requires a
  target and has none left, now ends the activation with `Cancel.TargetLost`
  instead of leaving the ability running with a step that never started.
- Added `Activation Input` policies: `OnPress` (the default), `OnHold` and
  `OnRelease`, with `Activation Hold Duration`. `AbilityInputRouter` implements
  them on the started/performed/canceled edges, and exposes
  `NotifyInputPressed` / `NotifyInputReleased` so a game that reads its own
  devices drives the same policies without reimplementing them.
- **Ability groups and mutual exclusion.** `Group Tag` plus `Exclusion Policy`
  decide what an activation does about the abilities already running:
  `BlockWhileAnyActive` (the default, and the one-ability-at-a-time rule every
  existing asset keeps), `BlockWhileSameGroupActive`, `CancelSameGroup` and
  `CancelAnyActive`. Abilities in different groups now run concurrently;
  `ActiveInstances` and `ActiveAbilityCount` expose them, and every singular
  accessor speaks for the oldest running activation, so an ability started later
  never takes the primary slot. A cancelling policy is refused when a running
  ability's own cancel policy rejects the request, reported up front by
  `CanActivate` as `BlockedByUncancellableAbility` — evaluation stays side-effect
  free. One activation moves the owner per tick until movement tasks bring
  priority rules.
- Added `Cancel.StepTimeout` and `Cancel.SupersededByGroup` to
  `CommonGameplayTags`, and `BlockedByGroup` /
  `BlockedByUncancellableAbility` to `AbilityActivationRejection`.
- Validation gained three rules: a multi-step `OnEvent` ability without a step
  advance tag, a branch targeting a step outside the list, and a group-scoped
  exclusion policy with no group tag.

### Target data and reusable targeting

- Added `AbilityTargetData` and `AbilityTargetHit`, carrying actors, colliders,
  receivers, contact points, normals, the query origin, and per-target
  directions and distances. `AbilitySystem.ActiveTargetData` exposes the running
  activation's acquisition.
- **One query per trigger frame.** Every effect that fires on the same frame of
  the same step with the same `HitShape` now reads one acquisition through
  `AbilityInstance.AcquireTargets`. Damage and physics force each ran their own
  overlap before, so the fourth hit of a combo could damage one enemy and push
  another out of what the author wrote as one volume.
- Acquisition is ordered deterministically: planar distance with a small angular
  bias toward the query direction, ties broken by entity id. Physics returns
  overlaps unordered, which is what let two readers of one volume disagree.
- Added `AbilityTargetFilter`: owner exclusion, alive state, required and
  blocked target tags, line of sight, and a maximum count. Every default is
  inert, so an effect that gains the field keeps its behaviour. `Damage`,
  `Physics Force` and `Target Assist` all author one.
- Added `AbilityTargetQuery` (shape plus filter) with `Resolve`, for editor
  gizmos, external acquisition, and tests.
- Target Assist produces `AbilityTargetData` instead of writing loose fields
  into the parent context, and runs a `HitShape` cone — the proximity circle is
  that cone's inner radius. Added `Lock Policy`: `Reacquire` (default, the
  previous behaviour), `KeepWhileValid`, and `Lock`.
- Added externally supplied targets: `AbilityTargetData.FromActors` and
  `AbilitySystem.TryActivateWithTargets`, which skips local acquisition.
- **Breaking.** `HitShape.Overlap` is no longer the primary entry point;
  `HitShape.Query` returns `HitShapeHit` with contact points and normals.
  `Overlap` remains for callers that only want colliders.
- **Breaking.** `TargetQueries.TryResolveReceiver` lost its `owner` parameter and
  no longer judges owner identity or alive state — `AbilityTargetFilter` owns
  those rules now, in one place.
- **Breaking.** `PhysicsForceEffectDefinition.blockedTargetTags` moved into its
  `Filter`. `GE_PhysicsForce_GrantCombo` was re-authored with the same three
  tags, so its behaviour is unchanged.
- Added `Cone` and `Ray` to `HitShapeKind`, with `Cone Half Angle` and `Cone
  Inner Radius`. `HitShape` gained value equality, which is what lets two effects
  recognise one shared query.
- **Breaking.** `DebugDrawEffectDefinition` no longer declares `DebugDrawShape`,
  `localCenter`, `localEnd`, `halfExtents` or `radius`. It carries a `HitShape`,
  so copying a damage effect's shape into it makes the drawing and the query read
  the same fields. `DebugDrawShape` is gone. No authored asset used the old
  fields.
- Added `AbilityDebugDraw.Shape`, `Cone` and `Line`. Shape drawing now exists
  once: the debug effect, the runtime overlay and the editor gizmos all call it.
- Added `AbilityQueryGizmos`, an edit-time Scene view preview of an ability
  step's query volumes, the target assist cone, and the targets its filters
  accept — running the shipping geometry and the shipping filters.

### Event-triggered, level-scaled physics reactions

- Added `AbilityActivationTrigger` and
  `AbilitySystem.TryHandleGameplayEvent`. A granted ability may activate from a
  matching gameplay event and optionally interrupt the current ability.
- Added level 1–100 to `AbilityEffectTrigger` and `AbilityEffectContext`; legacy
  serialized level 0 resolves to 1.
- Added `PhysicsForceEffectDefinition` for reusable push, pull, and launch
  effects using `HitShape`, level curves, and `ForceMode.VelocityChange`.
- A force declares `blockedTargetTags`, so blocking, invulnerable, and dead
  targets absorb the hit without being moved. Its Inspector previews the
  resolved velocity, air time, and reach for levels 1, 25, 50, 75, and 100.
- Added `AbilityPhysicsBody`, the generic dynamic-Rigidbody handoff that owns
  settling, timeout, lifecycle events, and external reaction completion.
- Added `Event.PhysicsForce`, `State.PhysicsControlled`, and the shared
  `GA_Common_PhysicsReaction` ability. Player and boss loadouts grant it; the
  reaction holds movement/state tags until physics ends.
- Migrated only Grant's fourth combo hit to the new effect at level 20. Its
  damage-owned knockback was cleared to prevent double displacement; the
  legacy damage payload remains compatible for other attacks.

### Derived abilities

- `AbilityDefinition` gained four lifecycle hooks — `OnActivated`,
  `OnStepStarted`, `OnCompleted` and `OnCancelled` — called before the matching
  public event so a derived ability's own logic runs before outside listeners.
  The base implementations do nothing, so existing abilities are unaffected.
- The ability Inspector now draws every serialized property its hand-written
  layout does not, grouped under the concrete type's name. A derived ability's
  fields are editable without touching the editor, and a field added to the base
  class can no longer go silently invisible.

### Cancellation by gameplay tag

- **Breaking.** `AbilityCancelReason` and `AbilityCancelMask` are gone. A cancel
  request now carries a `GameplayTag`:
  `TryCancelActiveAbility(GameplayTag)`, `ForceCancelActiveAbility(GameplayTag)`
  and `AbilityCancelled(AbilityDefinition, GameplayTag)`.
- `AbilityDefinition.allowedCancellation` became `cancelPolicy`
  (`Anything` / `OnlyListedTags` / `Nothing`) plus `cancelledByTags`. Assets that
  accepted every reason need no migration: `Anything` is the default. An ability
  that listed specific reasons must list the matching tags instead.
- The package raises `Cancel.Manual`, `Cancel.TargetLost` and
  `Cancel.PhysicsForce`. Games add their own reasons as tags in the same
  namespace, so a new reason never edits an enum inside the package again.
- `OnlyListedTags` with an empty list fails validation, pointing at `Nothing`.

### Per-step Target Assist

- `AbilityStep` carries its own optional `TargetAssistDefinition`. A step with
  one re-acquires its target as it starts, rotates the owner and closes the gap
  again, so a combo follows an enemy that moved between swings. A step without
  one keeps the target the activation chose, and the ability-level field still
  resolves the first step.
- `TargetAssistDefinition.stoppingDistance` became `stoppingGap`
  (`FormerlySerializedAs` keeps old assets loading): the approach now subtracts
  the owner's own collider reach, so the authored value is the space left
  between the two bodies rather than a distance from the owner's origin. Old
  assets keep their number and must be re-authored to the new meaning.
- Target-assist approach and a step's own displacement fail validation on the
  same step, matching the existing ability-level rule.

### Per-step gameplay-tag windows

- Added `AbilityStepTagWindow` and `AbilityStep.tagWindows` for inclusive,
  frame-authored state such as rolling, invulnerability, movement locks, and
  armour, with `endFrame = 0` meaning through the end of the step.
- Window tags are reference-counted alongside ability-wide grants, and
  overlapping or adjacent windows for the same tag emit only effective open and
  close transitions through `AbilitySystem.StepTagWindowChanged`.
- Open windows are closed before step advancement, completion, cancellation,
  disable, or destruction. Missing `tagWindows` data from older assets is
  treated as an empty array.
- Added `AbilitySystem.IsStepTagWindowOpen` so consumers can distinguish the
  current step's window from the same tag supplied by another source.
- Added Inspector serialization, authoring validation, and focused EditMode
  coverage for timing, counts, event order, legacy assets, and cleanup paths.

### Loadout validation and typed activation rejection codes (Milestone 1)

- `AbilityLoadout` performed no validation at all. It now reports empty slots,
  abilities with no ability ID, IDs repeated across two abilities, and abilities
  that fail their own `TryValidate`, through `AbilityLoadoutValidationIssue`,
  which names the slot, the ability and the kind of problem.
- Two forms of the same rules: `TryValidate(out string)` stops at the first
  problem, matching the shape `AbilityDefinition` already uses;
  `TryValidate(List<AbilityLoadoutValidationIssue>)` collects every problem and
  reuses the caller's list. Both route through one private implementation, so
  there is no second copy of the rules to keep in step.
- A duplicate ID is reported on the *second* slot and names the first, because
  `FindAbility` returns the first match and the later asset is granted but
  unreachable by ID. IDs differing only in case are not duplicates: `FindAbility`
  compares Ordinal, so they really are two lookups.
- New `AbilityLoadoutEditor` draws the audit under the loadout Inspector, one box
  per issue with a button that pings the offending ability. It formats what
  `AbilityLoadout.TryValidate` returns and owns no rule of its own.
- New `AbilityActivationRejection` enum and `AbilityActivationResult` struct.
  Activation rejection was a bare `bool` plus a message an AI could only have
  matched on by text; the code is now the contract and the message is detail.
  Codes: `NullAbility`, `InvalidDefinition`, `AnotherAbilityActive`, `NotGranted`,
  `OnCooldown`, `NoChargesLeft`, `InsufficientAttribute`, `MissingRequiredTag`,
  `BlockedByTag`, `MissingTarget`, `OutOfRange`, `OutsideFacingAngle`.
- New `AbilitySystem.EvaluateActivation(ability, context)` returns that result,
  and a new `TryActivate(ability, context, out AbilityActivationResult)` overload
  reports why an activation did not start. Both are side-effect free up to the
  point the ability actually activates.
- **No breaking change.** `CanActivate(ability, context, out string)` and
  `TryActivate(ability, context)` keep their signatures and their exact messages;
  they now read from the same single evaluation, so the code and the text can
  never disagree. Every check kept its original order.
- Focused EditMode tests: 11 for loadout validation, 18 for the rejection codes,
  including one that asserts `EvaluateActivation` changes no runtime state and
  one that asserts `CanActivate` still produces the identical message.

### `AGENTS.md` and `CLAUDE.md` are one document under two names

- The two files had diverged into 124 and 63 lines with almost nothing in
  common, and each claimed a different rank: the README called `AGENTS.md`
  "the first file coding agents should follow", while `CLAUDE.md` called itself
  a subset of it. Merged them into one document, written to both files
  byte-identically, with a header saying so. Sections that only apply to the
  copy vendored inside BossRush are marked `[BossRush]`.
- Corrected three statements that the merge would otherwise have duplicated
  into both files: `AbilitySystem` owns *step advancement*, not sequences; the
  code-review rule flags step-advancement changes, not sequence changes; and
  the Attributes subsystem is implemented, not "proposed in the README".
- Added two invariants that were only in one of the two files, or in neither:
  one damage effect with a `HitShape` rather than one class per collider shape,
  and the combo-is-one-ability rule.

### Roadmap rewritten against the current runtime

- `Documentation~/ROADMAP.md` still described `AbilitySequenceDefinition`, four
  damage effect classes, ability-owned displacement and Target Assist as an
  `AbilityDefinition` subclass — all removed by the combo, damage and targeting
  refactors. Rewrote the baseline, the concept table and the milestone text
  around `AbilityStep`, `HitShape` and the standalone `TargetAssistDefinition`.
- **Dropped the networking milestone.** Replication, prediction keys, rollback
  and server validation are out of scope for a single-player package; they are
  now one `Scope: single player` section pointing at
  [tranek/GASDocumentation](https://github.com/tranek/GASDocumentation) for the
  day a consumer needs them, plus a non-goal entry. `IAbilityReplicationSink`
  stays as the seam.
- Reordered the milestones by cost against value for one developer with one
  consumer: validation, then editor tooling, targeting, step policies, tasks,
  and only then Gameplay Effect specs, which are the most invasive change.
- Recorded a new debt item under targeting: `DebugDrawEffectDefinition` still
  declares its own shape, center, end, half-extents and radius, duplicating the
  geometry `HitShape` now owns.
- Trimmed the 1.0 sample list from nine slices to one complete combat loop.

### Asset naming enforced in the Inspector

- Added `AssetNamingConvention` (Editor): an asset whose name breaks the
  convention draws a warning naming the prefix it should carry, plus a button
  that performs the rename. Main assets route through
  `AssetDatabase.RenameAsset` so the `.meta` and its GUID follow the file;
  embedded sub-assets are renamed by object name. The body of the name is never
  invented — only the prefix is applied, because only the author knows the
  owner.
- The ability Inspector reports its embedded effects too. They have no
  Inspector of their own, so that was the only place a misnamed embedded effect
  could ever surface.
- Added default Inspectors for `AbilityLoadout`, `AttributeSetDefinition`,
  `TargetAssistDefinition` and `AbilityEffectDefinition` that carry the same
  warning.
- Renamed the attribute-set prefix `GAS_` to `ABS_`. `GAS_` reads as the
  package's own acronym, which made an attribute set look like a system-wide
  asset. It is the one prefix that does not start with `G`.
- Added `Heal` to the `GE_` category list, and a rule for effects embedded in an
  ability: the detail names the step that fires it, owner included
  (`GE_Damage_FergusAttack01`), so it stays findable outside its parent.

### Warning when leaving an ability with unsaved changes

- `AbilityDefinitionEditor` now logs a warning when the Inspector stops showing
  an ability that still has unsaved changes, which is what selecting another
  asset does. The existing save bar only helped while the asset was on screen;
  the edit was silent once it left. The log pings the asset so it can be
  selected again.

### Asset naming convention

- Documented the asset naming convention in `AGENTS.md` (full table) and
  `CLAUDE.md` (short form): `GA_` ability, `GE_` effect, `ABS_` attribute set,
  `GAL_` loadout, `GTA_` target assist, then `_<Owner-or-Category>_<Detail>`.
  Cues stay tags in the `Cue.` namespace.
- Changed the `fileName` of every `[CreateAssetMenu]` so new assets are created
  already prefixed: `GA_NewAbility`, `GAL_NewLoadout`, `GTA_NewTargetAssist`,
  `ABS_NewAttributeSet`, `GE_Damage`, `GE_DebugDraw`, `GE_DurationAttribute`,
  `GE_ModifyAttribute`, `GE_PeriodicAttribute`. Assets already on disk are
  unaffected; rename them by hand with their `.meta` files.

### Breaking: one preview clip per ability

- Moved `previewAnimationClip` from `AbilityStep` to `AbilityDefinition`.
  The preview panel is one panel, so a clip per step meant four fields
  feeding one view, three of them dead at any moment. `AbilityStep.PreviewClip`
  and `AbilityStep.HasAnimationPreview` moved to `AbilityDefinition` under the
  same names.
- Existing assets keep the first non-empty step preview clip. A per-step value
  in an unmigrated asset is dropped silently by Unity, so reassign the field
  once per ability.
- Added `AbilityStepDrawer`: each step draws a `Preview This Step` button under
  its animation fields, loading that step’s gameplay clip into the ability
  preview clip. The drawer walks the step's children through their own
  property handlers, so headers, tooltips and attribute drawers keep working
  and a new field needs no change to the drawer. The preview clip field itself
  sits at the end of the Inspector, next to the panel it drives.
- Renamed the test-only `AbilityStep.SetAnimationClipsForTests(clip, preview)`
  to `SetAnimationClipForTests(clip)`; preview goes through
  `AbilityDefinition.SetPreviewClipForTests`.

### Breaking: combos are one ability with steps

- Added `AbilityStep`: the per-swing half of an ability (clip, blend, frame
  timeline, action windows, displacement, effect and cue triggers).
  `AbilityDefinition` now holds an ordered `Steps` list plus `Advancement`
  (`Automatic` / `Manual`), and keeps only what is decided once per activation:
  cost, cooldown, range, facing, activation tags, cancellation mask, AI weight.
  A four-hit combo is one asset with four steps.
- Removed `AbilitySequenceDefinition` and the whole parallel sequence path on
  `AbilitySystem` (`CanActivateSequence`, `TryActivateSequence`,
  `TryAdvanceSequence`, `TryQueueSequenceAdvance`, `TryCancelSequence`,
  `IsAwaitingSequenceAdvance`, sequence cooldowns and the three sequence
  events). `AbilityLoadout.sequences` is gone. A combo is granted, activated,
  cancelled and cooled down exactly like any other ability.
- Added `TryQueueStepAdvance`, `ActiveStep`, `ActiveStepIndex`,
  `IsComboWindowOpen` and the `AbilityStepAdvanced` event. A buffered input
  advances the running instance to its next step instead of completing one
  ability and activating another; hit registration is keyed per step, so the
  next swing may hit a target the previous one already hit.
- A manual combo that receives no input now simply ends on its current step.
  The dedicated sequence-cancellation path it replaced no longer exists.

### Breaking: one damage effect, any shape

- Added `HitShape` (sphere / box / capsule, owner-local or aim-point anchored)
  and `DamageEffectDefinition`, which pairs a shape with the damage payload.
  Replaces `MeleeDamageEffectDefinition`, `BoxDamageEffectDefinition`,
  `CapsuleDamageEffectDefinition` and `AreaDamageEffectDefinition`, which
  differed only in the physics query and had drifted apart by copy — only one of
  them ever gained a stun duration. `Knockback Direction`, `Linear Falloff` and
  `Restrict To Ability Target` cover what the separate classes encoded.

### Breaking: target assist is targeting data

- `TargetAssistDefinition` no longer derives from `AbilityDefinition`. It is a
  plain `ScriptableObject` with only the query and approach fields, so a
  targeting asset is no longer validated against timeline rules it does not
  have. The ability field is renamed `nestedAssist` -> `targetAssist`
  (`FormerlySerializedAs` keeps existing references), and the defensive "a
  target assist cannot nest another ability" rule is gone with the inheritance
  that required it.

### Fixed

- An effect trigger scheduled after the combo continue frame is now an authoring
  **warning** instead of a validation failure. As a fatal error it silently
  prevented activation, which disabled an entire boss combo at runtime; the
  effect still fires whenever the combo does not continue.

- Added Unity's native `.anim` preview panel to every `AbilityDefinition`
  Inspector, inherited by subclasses without per-ability code. A final Preview
  section holds the explicit `Preview Clip` (any animation, never the gameplay
  clip), while the delegated native panel supplies its standard model selector,
  playback controls, and scrubber. Preview authoring never affects gameplay
  timing or validation.
- Fixed hosted native ability previews to use the authored clip start and stop
  times instead of the internal `TimeControl` one-second default, so clips
  longer than 60 frames play and scrub through their complete range.
- Fixed resolved ability timelines to extend through the assigned gameplay
  animation's complete frame count. The manual end frame is now a minimum that
  may extend the ability, while movement unlock and combo windows still permit
  earlier control and sequence transitions.
- Added per-ability `Movement Unlock Frame`, `Combo Continue Frame`, and
  `Combo Input End Frame`. Manual sequences can now retain an early input,
  advance exactly when the authored continuation frame arrives, and expire the
  combo window without cancelling the current attack.
- Added `AbilitySystem.IsMovementLocked` and `TryQueueSequenceAdvance`, with
  focused tests for early buffering, frame-gated advancement, movement release,
  input-window expiration, and post-completion cutoff behavior.
- Added a compact attack Inspector centered on animation, four combat-frame controls,
  and inline damage-box authoring. Generic targeting, nested-assist wiring, and
  other implementation details stay out of the attack's default view.
- New embedded attack damage effects now default to `BoxDamageEffectDefinition`.
- Target Assist now resolves before its parent ability, defaults its cone reach
  to twice the proximity radius, propagates the selected target and direction
  into the parent context, and can approach during the parent's startup until
  its maximum range is reached. Added focused circle/cone, context, movement,
  and displacement-conflict tests.
- Moved the detailed GAS-inspired development plan to
  `Documentation~/ROADMAP.md`; the README now keeps a short linked summary and
  documents the intended split between damage effects and knockback reactions.
- Router no longer resolves a fallback target for targetless single
  abilities: `RequiresTarget` false means no target, so self novas never hit
  a meaningless range gate. Sequences keep the fallback for their steps.
- Added periodic modifiers (`ApplyPeriodicModifier` with Stack/Refresh/Ignore
  stacking) and `PeriodicAttributeEffectDefinition` for damage/heal over time:
  every period folds into the base value like an instant change, firing the
  set's change event per tick.
- Added `ModifyAttributeEffectDefinition`: instant attribute damage, healing,
  and resource changes as effect data (target or owner, optional attribute
  scaling), firing the set's change event like any other attribute change.
- Added ability-owned displacement: `AbilityDisplacementDirection`
  (context, owner-forward, toward/away from target), distance plus a 1-based
  frame window on `AbilityDefinition`, and kinematic `Rigidbody.MovePosition`
  travel applied by `AbilitySystem` while the window is open. Direction and
  body resolve once at activation; cancelling ends travel immediately.
  Locomotion motors keep owning velocity — the ability only adds travel.
- Added Unreal-style debug drawing: `AbilityDebugDraw` (timed wireframe
  box/sphere/capsule, editor-only, zero cost in builds) and
  `DebugDrawEffectDefinition` so abilities can visualize query volumes at any
  timeline frame with configurable shape, color, and screen lifetime.
  `AbilityDebugDraw.Enabled` is the master switch for clean play sessions.
- Added `BoxDamageEffectDefinition` and `CapsuleDamageEffectDefinition`;
  melee, box, capsule, and area effects share `TargetQueries` and support
  attribute damage scaling.
- Added ability costs (`AbilityCost`), limited charges with restore timers,
  router input buffering, and `AbilityWhiffed` events.
- Added duration modifiers with stacking policies, regeneration, and a
  deterministic `AttributeSet.Tick`.
- Added `AbilityAnimationEventBridge`, `AbilitySystemDebugger`,
  `IAbilityReplicationSink` hooks, `ActiveFrame`/`ActiveTags` accessors,
  and the `State.Invulnerable` tag.
- Added `AreaDamageEffectDefinition`: aim-point or self-centered sphere damage
  with linear falloff, radial knockback, and multi-target limits.
- Added the Attributes subsystem (`GameplayAttribute`, `AttributeModifier`,
  `AttributeValue`, `AttributeSet`) with deterministic Add/Multiply/Override
  aggregation, limits, and typed change events.
- Added manual sequence advancement (`SequenceAdvancement.Manual`,
  `TryAdvanceSequence`, `SequenceAwaitingAdvance`, `TryCancelSequence`) so
  player combos can require one input per step.
- Added frame-based gameplay cues (`GameplayCueTrigger` on `AbilityDefinition`,
  `AbilitySystem.GameplayCueTriggered`, and manual `TriggerGameplayCue`) for
  cosmetic tells such as enemy attack warnings.
- Added `AbilityContext.FromDirection` for directional, targetless abilities
  such as rolls and dashes, with ground-plane projection and owner-forward
  fallback.
- Added `AbilitySystem.CanActivateSequence` so AI and other callers can score
  granted sequences without starting them.
- Expanded the README with current capabilities, limitations, architecture,
  authoring guidance, the proposed Attributes model, and a phased roadmap.
- Added repository-level `AGENTS.md` instructions for consistent AI-assisted
  development across independent sessions.

## [0.1.0] — Initial public extraction

Extracted from the BossRush project as a standalone UPM package:

- Core runtime (`AbilitySystem`, `AbilityDefinition`, `AbilityInstance`,
  `AbilityContext`, `AbilityLoadout`, `AbilitySequenceDefinition`, effects,
  gameplay tags) under the `Fofuxo.GameplayAbilitySystem` namespace.
- New `IAbilityDamageReceiver` / `AbilityHitInfo` / `AbilityImpact` contract
  so `MeleeDamageEffectDefinition` no longer depends on game-specific
  damage types.
- `AbilityInputRouter` resolves fallback targets through
  `FallbackTargetResolver` / `GlobalFallbackTargetResolver` hooks instead of
  a hard-coded enemy lookup.
- `CreateAssetMenu` entries moved to **Fofuxo > Abilities**.
- EditMode validation tests for `AbilityDefinition`.
