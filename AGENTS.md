# Repository instructions

## Scope

This repository contains the public Unity package
`com.uayten.fofuxogameplayabilitysystem`. Keep package code generic and reusable.
Game-specific AI, health, movement, characters, scenes, and content assets belong
in consumer projects.

## Read before changing code

- Read `README.md` for current behavior, limitations, design direction, and roadmap.
- Read `CHANGELOG.md` and inspect `git status` before editing.
- Read the complete files involved in a public API change and their focused tests.
- Treat existing uncommitted changes as user-owned work.

## Language and compatibility

- Write identifiers, comments, logs, documentation, and commit messages in English.
- Target Unity `6000.0` or newer.
- Use the namespace `Fofuxo.GameplayAbilitySystem`.
- Preserve the package ID `com.uayten.fofuxogameplayabilitysystem`.
- Preserve API compatibility within a released minor line when practical.
- Document intentional breaking changes before release.

## Architecture invariants

- `ScriptableObject` definitions contain immutable authoring data only.
- Runtime state must be created per actor or per activation.
- `AbilitySystem` owns activation rules, cooldowns, tags, active abilities, and sequences.
- Callers own intent. Do not place player input or AI selection in the core assembly.
- Keep Input System integration in its separate input assembly.
- Do not reference consumer-project types from package runtime or editor assemblies.
- Prefer small contracts and events over inheritance from game-specific components.
- Do not use Animator state as the sole authority for gameplay timing.
- Keep target validation side-effect free in `CanActivate` methods.
- New effect types must not mutate their definition assets at runtime.

## Attributes direction

- Follow the proposed Attributes design in `README.md`; it is not implemented API yet.
- The package supplies generic identifiers, values, sets, modifiers, specs, and active effects.
- Consumer projects define concrete sets such as Health, Stamina, and Poise.
- Runtime attribute values never live in `ScriptableObject` assets.
- Start with deterministic `Add`, `Multiply`, and `Override` aggregation.
- Add duration, stacking, prediction, or generated accessors only with tests and a demonstrated use case.

## Change workflow

1. Inspect current code, tests, README, changelog, and working tree.
2. Make the smallest generic change that satisfies the requested capability.
3. Add focused EditMode tests for behavior and public contracts.
4. Update README examples and roadmap when architecture changes.
5. Update `CHANGELOG.md` under `[Unreleased]` for user-visible changes.
6. Preserve or create Unity `.meta` files for every Unity-visible file.
7. Verify compilation and focused tests in a Unity host project.

## Verification

- This package repository is not a standalone Unity project; use a host project to compile and run tests.
- Prefer focused EditMode tests over broad PlayMode sessions.
- Do not enter Play Mode unless the requested behavior requires interactive validation.
- Report tests that could not run and the exact host-project precondition that blocked them.
- Never claim multiplayer prediction, networking, persistence, or production readiness without implemented tests.

## Repository skills

Use `.agents/skills/` only for repeatable procedures with clear triggers, inputs,
outputs, and verification. Keep architecture and product facts in `README.md`.
Potential future skills are ability-effect authoring, public API review, and package release.

## Code review rules

- Flag runtime state stored on definition assets.
- Flag core dependencies on Input System or consumer-project assemblies.
- Flag activation checks that mutate state.
- Flag sequence changes that blur completion, rejection, and cancellation semantics.
- Flag public API changes without tests, README updates, or changelog entries.
- Flag missing `.meta` files for new Unity-visible files.
