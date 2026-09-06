# Changelog

## [Unreleased]

- Added an animation preview to the ability Inspector's bottom preview pane:
  it renders an interactive model preview (toolbar play and scrub, frame
  readout, damage-frame hint) in every `AbilityDefinition` editor (inherited
  by subclasses, no per-ability code required) with an optional per-ability
  `Preview Clip` override. The model is resolved from the clip's parent
  folder like the rest of the Fofuxo tooling; the override is editor-only
  and never affects gameplay timing, validation, or builds.
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
