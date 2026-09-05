# Changelog

## [Unreleased]

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
