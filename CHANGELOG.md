# Changelog

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
