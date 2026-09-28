using System;
using NUnit.Framework;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    /// <summary>
    /// The persistence contract: what a record carries, what elapsed time does
    /// to it, and what a restore is forbidden to do - which is to apply the
    /// effect again.
    /// </summary>
    public sealed class AbilityPersistenceTests : GameplayEffectTestBase
    {
        private const string BuffId = "test.effect.buff";
        private const string AbilityId = "test.ability.slam";
        private const string CooldownId = "test.effect.cooldown";

        private GameObject owner;
        private AbilitySystem system;
        private AttributeSet attributes;
        private AbilitySaveResolver sessionResolver;

        [SetUp]
        public void SetUp()
        {
            owner = NewActor("SaveOwner", Health, 100f);
            system = owner.AddComponent<AbilitySystem>();
            attributes = Attributes(owner);
        }

        [TearDown]
        public void ForgetMigrations()
        {
            AbilitySaveMigrator.Clear();
        }

        [Test]
        public void Capture_Then_Restore_ReproducesTheStateTheSaveWasTakenFrom()
        {
            TimelineAbilityDefinition ability = NewAbility(AbilityId, cooldown: 5f, maxCharges: 2);
            Grant(system, ability);
            SpendOneActivation(system, ability);

            attributes.ApplyInstantModifier(
                new AttributeModifier(Health, AttributeOperation.Override, 42f));
            GameplayEffectDefinition buff = NewBuff(BuffId, magnitude: 10f, duration: 10f);
            Apply(buff, owner, owner);
            Effects(owner).Tick(3f);
            system.SetLooseTag(new GameplayTag("State.Rested"), true);

            AbilitySaveRecord record = AbilityPersistence.Capture(system, out AbilitySaveReport saved);
            Assert.IsFalse(saved.HasWarnings, string.Join(" | ", saved.Warnings));

            AbilitySystem loaded = NewSession(out GameObject loadedOwner, ability, buff);
            Assert.IsTrue(Load(loaded, record, out AbilitySaveReport report));
            Assert.IsFalse(report.HasWarnings, string.Join(" | ", report.Warnings));

            Assert.AreEqual(2f, loaded.GetCooldownRemaining(ability), 0.05f,
                "the cooldown is an effect: it aged with the three seconds before the save");
            Assert.AreEqual(1f, loaded.GetCharges(ability), 0.0001f, "charges");
            Assert.AreEqual(42f, Attributes(loadedOwner).GetBase(Health), 0.0001f, "base value");
            Assert.AreEqual(52f, Attributes(loadedOwner).GetCurrent(Health), 0.0001f,
                "the restored buff is a live modifier again");
            Assert.AreEqual(2, Effects(loadedOwner).ActiveEffectCount, "the cooldown and the buff");
            Assert.AreEqual(
                7f,
                Find(loadedOwner, buff).RemainingDuration,
                0.0001f,
                "the effect resumes where the save left it");
            Assert.IsTrue(loaded.HasTag(new GameplayTag("State.Rested")), "loose tag");
        }

        [Test]
        public void Restore_DoesNotApplyTheEffectAgain()
        {
            GameplayEffectDefinition buff = NewBuff(BuffId, magnitude: 10f, duration: 10f);
            Apply(buff, owner, owner);
            AbilitySaveRecord record = AbilityPersistence.Capture(system);

            AbilitySystem loaded = NewSession(out GameObject loadedOwner, buff);
            Assert.IsTrue(Load(loaded, record, out _));

            Assert.AreEqual(
                100f,
                Attributes(loadedOwner).GetBase(Health),
                0.0001f,
                "a restore that executed the effect would have folded it into the base value");
            Assert.AreEqual(1, Effects(loadedOwner).ActiveEffectCount);
        }

        [Test]
        public void Restore_RebuildsThePersistentCueTheEffectOwns()
        {
            GameplayEffectDefinition buff = NewBuff(BuffId, magnitude: 10f, duration: 10f);
            SetField(buff, "cueTag", new GameplayTag("Cue.Test.Buff"));
            Apply(buff, owner, owner);

            AbilitySaveRecord record = AbilityPersistence.Capture(system);
            AbilitySystem loaded = NewSession(out _, buff);
            Assert.IsTrue(Load(loaded, record, out _));

            Assert.AreEqual(1, loaded.Cues.ActiveCues.Count, "the cue came back with its effect");
            Assert.AreEqual(
                new GameplayTag("Cue.Test.Buff"),
                loaded.Cues.ActiveCues[0].Cue);
        }

        [Test]
        public void Freeze_IsTheDefault_AndIgnoresTheTimeBetweenSessions()
        {
            AbilitySaveRecord record = RecordWithCooldownAndBuff(out TimelineAbilityDefinition ability, out GameplayEffectDefinition buff);
            record.SavedAtUtcTicks = DateTime.UtcNow.AddHours(-9).Ticks;

            AbilitySystem loaded = NewSession(out GameObject loadedOwner, ability, buff);
            Assert.IsTrue(Load(loaded, record, out _));

            Assert.AreEqual(5f, loaded.GetCooldownRemaining(ability), 0.05f);
            Assert.AreEqual(2, Effects(loadedOwner).ActiveEffectCount, "the cooldown and the buff");
        }

        [Test]
        public void Advance_AgesCooldownsAndDurations_ByTheOfflineSeconds()
        {
            AbilitySaveRecord record = RecordWithCooldownAndBuff(out TimelineAbilityDefinition ability, out GameplayEffectDefinition buff);

            AbilitySystem loaded = NewSession(out GameObject loadedOwner, ability, buff);
            Assert.IsTrue(Load(loaded, record, out AbilitySaveReport report, AbilityOfflinePolicy.Advance, 4d));

            Assert.AreEqual(4d, report.OfflineSecondsApplied, 0.0001d);
            Assert.AreEqual(1f, loaded.GetCooldownRemaining(ability), 0.05f);
            Assert.AreEqual(
                6f,
                Find(loadedOwner, buff).RemainingDuration,
                0.0001f);
        }

        [Test]
        public void Advance_DropsWhatRanOutAndRefillsChargesItPaidFor()
        {
            AbilitySaveRecord record = RecordWithCooldownAndBuff(out TimelineAbilityDefinition ability, out GameplayEffectDefinition buff);

            AbilitySystem loaded = NewSession(out GameObject loadedOwner, ability, buff);
            Assert.IsTrue(Load(loaded, record, out _, AbilityOfflinePolicy.Advance, 600d));

            Assert.AreEqual(0f, loaded.GetCooldownRemaining(ability), 0.0001f, "cooldown ran out");
            Assert.AreEqual(2f, loaded.GetCharges(ability), 0.0001f, "charges refilled");
            Assert.AreEqual(0, Effects(loadedOwner).ActiveEffectCount, "the buff ran out offline");
        }

        [Test]
        public void Advance_BanksPartOfTheNextCharge()
        {
            TimelineAbilityDefinition ability = NewAbility(AbilityId, maxCharges: 3, chargeRestore: 10f);
            Grant(system, ability);
            SpendOneActivation(system, ability);
            SpendOneActivation(system, ability);
            AbilitySaveRecord record = AbilityPersistence.Capture(system);

            AbilitySystem loaded = NewSession(out _, ability);
            Assert.IsTrue(Load(loaded, record, out _, AbilityOfflinePolicy.Advance, 14d));

            Assert.AreEqual(2f, loaded.GetCharges(ability), 0.0001f, "one charge came back");
            Assert.AreEqual(
                4f,
                loaded.ChargeRestoreElapsed[ability],
                0.0001f,
                "and the remaining four seconds count towards the next one");
        }

        [Test]
        public void Expire_ClearsEveryClock_AndKeepsWhatHasNone()
        {
            TimelineAbilityDefinition ability = NewAbility(AbilityId, cooldown: 5f, maxCharges: 2);
            Grant(system, ability);
            SpendOneActivation(system, ability);

            GameplayEffectDefinition timed = NewBuff(BuffId, magnitude: 10f, duration: 10f);
            GameplayEffectDefinition forever = NewBuff(
                "test.effect.mark", magnitude: 5f, duration: 0f, infinite: true);
            Apply(timed, owner, owner);
            Apply(forever, owner, owner);

            AbilitySaveRecord record = AbilityPersistence.Capture(system);
            AbilitySystem loaded = NewSession(out GameObject loadedOwner, ability, timed, forever);
            Assert.IsTrue(Load(loaded, record, out _, AbilityOfflinePolicy.Expire));

            Assert.AreEqual(0f, loaded.GetCooldownRemaining(ability), 0.0001f);
            Assert.AreEqual(2f, loaded.GetCharges(ability), 0.0001f);
            Assert.AreEqual(1, Effects(loadedOwner).ActiveEffectCount, "only the infinite one survives");
            Assert.IsTrue(Effects(loadedOwner).ActiveEffects[0].IsInfinite);
        }

        [Test]
        public void ARecordNamesWhenItWasTaken_SoOfflineProgressionCanReadIt()
        {
            AbilitySaveRecord record = AbilityPersistence.Capture(system);

            Assert.AreNotEqual(0L, record.SavedAtUtcTicks);
            Assert.AreEqual(
                90d,
                AbilityPersistence.SecondsSince(record, record.SavedAtUtc.AddSeconds(90d)),
                0.0001d);
            Assert.AreEqual(
                0d,
                AbilityPersistence.SecondsSince(record, record.SavedAtUtc.AddSeconds(-90d)),
                0.0001d,
                "a clock that went backwards is not offline progress");
        }

        [Test]
        public void ARecordSurvivesJson()
        {
            AbilitySaveRecord record = RecordWithCooldownAndBuff(out _, out _);

            AbilitySaveRecord read = AbilityPersistence.FromJson(AbilityPersistence.ToJson(record));

            Assert.IsNotNull(read);
            Assert.AreEqual(record.Version, read.Version);
            Assert.AreEqual(record.SavedAtUtcTicks, read.SavedAtUtcTicks);
            Assert.AreEqual(record.Abilities.Length, read.Abilities.Length);
            Assert.AreEqual(record.Effects[0].EffectId, read.Effects[0].EffectId);
            Assert.AreEqual(
                record.Effects[0].RemainingDuration,
                read.Effects[0].RemainingDuration,
                0.0001f);
        }

        [Test]
        public void AnEffectWithNoIdIsNamed_NotSavedSilently()
        {
            GameplayEffectDefinition anonymous = NewBuff(string.Empty, magnitude: 10f, duration: 10f);
            anonymous.name = "GE_Buff_Nameless";
            Apply(anonymous, owner, owner);

            AbilitySaveRecord record = AbilityPersistence.Capture(system, out AbilitySaveReport report);

            Assert.AreEqual(0, record.Effects.Length);
            Assert.IsTrue(report.HasWarnings);
            StringAssert.Contains("GE_Buff_Nameless", string.Join(" | ", report.Warnings));
        }

        [Test]
        public void AnAbilityThatLeftTheLoadoutIsNamed_NotRestoredSilently()
        {
            // Charges are what an ability entry still carries; a cooldown travels as an effect.
            TimelineAbilityDefinition ability = NewAbility(AbilityId, maxCharges: 2);
            Grant(system, ability);
            SpendOneActivation(system, ability);
            AbilitySaveRecord record = AbilityPersistence.Capture(system);

            AbilitySystem loaded = NewSession(out _);
            Assert.IsTrue(Load(loaded, record, out AbilitySaveReport report));

            Assert.IsTrue(report.HasWarnings);
            StringAssert.Contains(AbilityId, string.Join(" | ", report.Warnings));
        }

        [Test]
        public void CapturingAnActorMidActivationSaysSo()
        {
            TimelineAbilityDefinition ability = NewAbility(AbilityId);
            Grant(system, ability);
            Assert.IsTrue(system.TryActivate(ability, AbilityContext.FromTarget(owner, null)));

            AbilityPersistence.Capture(system, out AbilitySaveReport report);

            Assert.IsTrue(report.HasWarnings);
            StringAssert.Contains("running", string.Join(" | ", report.Warnings));
        }

        [Test]
        public void ARecordFromAnOlderSchemaIsMigratedBeforeItIsRestored()
        {
            AbilitySaveRecord record = AbilityPersistence.Capture(system);
            record.Version = AbilitySaveRecord.CurrentVersion - 1;
            AbilitySaveMigrator.Register(new TagAddingMigration(AbilitySaveRecord.CurrentVersion - 1));

            Assert.IsTrue(AbilityPersistence.Restore(system, record, null, out AbilitySaveReport report));

            Assert.AreEqual(AbilitySaveRecord.CurrentVersion, record.Version);
            Assert.IsFalse(report.HasWarnings, string.Join(" | ", report.Warnings));
            Assert.IsTrue(system.HasTag(new GameplayTag("State.Migrated")));
        }

        [Test]
        public void ARecordWithNoMigrationIsRefused_NotHalfApplied()
        {
            AbilitySaveRecord record = RecordWithCooldownAndBuff(out TimelineAbilityDefinition ability, out GameplayEffectDefinition buff);
            record.Version = AbilitySaveRecord.CurrentVersion - 1;

            AbilitySystem loaded = NewSession(out GameObject loadedOwner, ability, buff);
            Assert.IsFalse(Load(loaded, record, out AbilitySaveReport report));

            Assert.IsTrue(report.HasWarnings);
            Assert.AreEqual(0f, loaded.GetCooldownRemaining(ability), 0.0001f, "nothing was applied");
            Assert.AreEqual(0, Effects(loadedOwner).ActiveEffectCount, "nothing was applied");
        }

        [Test]
        public void ARecordFromANewerSchemaIsRefused()
        {
            AbilitySaveRecord record = AbilityPersistence.Capture(system);
            record.Version = AbilitySaveRecord.CurrentVersion + 1;

            Assert.IsFalse(AbilityPersistence.Restore(system, record, null, out AbilitySaveReport report));
            StringAssert.Contains(
                AbilitySaveRecord.CurrentVersion.ToString(),
                string.Join(" | ", report.Warnings));
        }

        [Test]
        public void AnActorThatNeverSavesPaysNothingForPersistence()
        {
            Assert.IsTrue(
                typeof(AbilityPersistence).IsAbstract && typeof(AbilityPersistence).IsSealed,
                "persistence is static: there is no component and no tick to pay for");

            foreach (Type component in new[]
                     {
                         typeof(AbilitySystem),
                         typeof(AttributeSet),
                         typeof(GameplayEffectContainer)
                     })
            {
                foreach (System.Reflection.FieldInfo field in component.GetFields(
                             System.Reflection.BindingFlags.Instance |
                             System.Reflection.BindingFlags.NonPublic |
                             System.Reflection.BindingFlags.Public |
                             System.Reflection.BindingFlags.DeclaredOnly))
                {
                    StringAssert.DoesNotContain(
                        "AbilitySave",
                        field.FieldType.Name,
                        $"{component.Name}.{field.Name} would make every actor carry a save.");
                }
            }
        }

        [Test]
        public void SetByCallerMagnitudes_TravelWithTheirEffect()
        {
            GameplayTag data = new("Data.Test.Amount");
            GameplayEffectDefinition buff = NewEffect(
                GameplayEffectDurationPolicy.Duration,
                new[]
                {
                    new GameplayEffectModifier(
                        Health, AttributeOperation.Add, new GameplayEffectMagnitude(0f).WithSetByCaller(data))
                },
                10f);
            buff.SetEffectIdForTests(BuffId);
            Effects(owner).Apply(
                new GameplayEffectSpec(buff, owner, owner).SetSetByCallerMagnitude(data, -25f));
            Assert.AreEqual(75f, attributes.GetCurrent(Health), 0.0001f);

            AbilitySaveRecord record = AbilityPersistence.Capture(system);
            AbilitySystem loaded = NewSession(out GameObject loadedOwner, buff);
            Assert.IsTrue(Load(loaded, record, out _));

            Assert.AreEqual(75f, Attributes(loadedOwner).GetCurrent(Health), 0.0001f,
                "without the saved number the restored buff would add zero");
        }

        [Test]
        public void LoadoutEffects_AreGrantedAgain_AfterARestoreThatDidNotCarryThem()
        {
            GameplayEffectDefinition passive = NewEffect(
                GameplayEffectDurationPolicy.Infinite, new[] { Add(Health, -5f) }, 0f);
            passive.name = "GE_Regen_Passive";
            AbilityLoadout loadout = Own(ScriptableObject.CreateInstance<AbilityLoadout>());
            SetField(loadout, "grantedEffects", new[] { passive });
            SetField(system, "loadout", loadout);
            system.GrantLoadoutEffects();
            Assert.AreEqual(95f, attributes.GetCurrent(Health), 0.0001f);

            AbilitySaveRecord record = AbilityPersistence.Capture(system, out AbilitySaveReport saved);
            Assert.IsFalse(saved.HasWarnings, "a loadout effect needs no id: " + string.Join(" | ", saved.Warnings));

            Assert.IsTrue(AbilityPersistence.Restore(system, record, null, out _));
            Assert.AreEqual(1, Effects(owner).ActiveEffectCount, "granted again, once");
            Assert.AreEqual(95f, attributes.GetCurrent(Health), 0.0001f);
        }

        // ------------------------------------------------------------ helpers

        private AbilitySaveRecord RecordWithCooldownAndBuff(
            out TimelineAbilityDefinition ability, out GameplayEffectDefinition buff)
        {
            ability = NewAbility(AbilityId, cooldown: 5f, maxCharges: 2);
            Grant(system, ability);
            SpendOneActivation(system, ability);

            buff = NewBuff(BuffId, magnitude: 10f, duration: 10f);
            Apply(buff, owner, owner);
            return AbilityPersistence.Capture(system);
        }

        /// <summary>
        /// A second actor, the way the next session would find it: the assets
        /// exist, the runtime state does not. The resolver it builds is what
        /// <see cref="Load"/> hands the restore.
        /// </summary>
        private AbilitySystem NewSession(out GameObject loadedOwner, params ScriptableObject[] assets)
        {
            loadedOwner = NewActor("LoadedOwner", Health, 100f);
            AbilitySystem loaded = loadedOwner.AddComponent<AbilitySystem>();

            var abilities = new System.Collections.Generic.List<AbilityDefinition>();
            var effects = new System.Collections.Generic.List<GameplayEffectDefinition>();
            foreach (ScriptableObject asset in assets)
            {
                if (asset is AbilityDefinition ability)
                {
                    abilities.Add(ability);
                }
                else if (asset is GameplayEffectDefinition effect)
                {
                    effects.Add(effect);
                }
            }

            if (abilities.Count > 0)
            {
                Grant(loaded, abilities.ToArray());
            }

            sessionResolver = new AbilitySaveResolver(abilities, effects);
            return loaded;
        }

        /// <summary>Restores onto a session actor with the assets that session knows.</summary>
        private bool Load(
            AbilitySystem loaded,
            AbilitySaveRecord record,
            out AbilitySaveReport report,
            AbilityOfflinePolicy policy = AbilityOfflinePolicy.Freeze,
            double offlineSeconds = -1d)
        {
            return AbilityPersistence.Restore(
                loaded,
                record,
                new AbilityRestoreOptions
                {
                    Resolver = sessionResolver,
                    OfflinePolicy = policy,
                    OfflineSeconds = offlineSeconds
                },
                out report);
        }

        private void SpendOneActivation(AbilitySystem target, TimelineAbilityDefinition ability)
        {
            Assert.IsTrue(target.TryActivate(
                ability, AbilityContext.FromTarget(target.gameObject, null)));
            Assert.IsTrue(target.TryCompleteActiveAbility(ability));
        }

        private TimelineAbilityDefinition NewAbility(
            string id, float cooldown = 0f, int maxCharges = 0, float chargeRestore = 10f)
        {
            TimelineAbilityDefinition ability =
                Own(ScriptableObject.CreateInstance<TimelineAbilityDefinition>());
            ability.name = "GA_Test_Save";
            ability.SetAbilityIdForTests(id);
            SetField(ability, "requiresTarget", false);
            if (cooldown > 0f)
            {
                GameplayEffectDefinition cooldownEffect =
                    Own(TestEffects.Cooldown(null, cooldown, "Cooldown.Test.Slam", CooldownId));
                SetField(ability, "cooldownGameplayEffect", cooldownEffect);
            }

            SetField(ability, "maxCharges", maxCharges);
            SetField(ability, "chargeRestoreTime", chargeRestore);

            AbilityStep step = new();
            step.ConfigureForTests(1, 2, 10, 60f);
            ability.SetStepsForTests(new[] { step });
            return ability;
        }

        private static ActiveGameplayEffect Find(GameObject actor, GameplayEffectDefinition definition)
        {
            foreach (ActiveGameplayEffect effect in Effects(actor).ActiveEffects)
            {
                if (effect.Definition == definition)
                {
                    return effect;
                }
            }

            Assert.Fail($"'{definition.name}' is not active.");
            return null;
        }

        private GameplayEffectDefinition NewBuff(
            string effectId, float magnitude, float duration, bool infinite = false)
        {
            GameplayEffectDefinition effect = NewEffect(
                infinite
                    ? GameplayEffectDurationPolicy.Infinite
                    : GameplayEffectDurationPolicy.Duration,
                new[] { Add(Health, magnitude) },
                duration);
            effect.SetEffectIdForTests(effectId);
            return effect;
        }

        private void Grant(AbilitySystem target, params AbilityDefinition[] abilities)
        {
            AbilityLoadout loadout = Own(ScriptableObject.CreateInstance<AbilityLoadout>());
            SetField(loadout, "abilities", abilities);
            SetField(target, "loadout", loadout);
        }

        private static void SetField<TValue>(object target, string fieldName, TValue value)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                System.Reflection.FieldInfo field = type.GetField(
                    fieldName,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"No field named {fieldName}.");
        }

        /// <summary>A migration that proves the chain ran, and in which order.</summary>
        private sealed class TagAddingMigration : IAbilitySaveMigration
        {
            public TagAddingMigration(int fromVersion)
            {
                FromVersion = fromVersion;
            }

            public int FromVersion { get; }

            public void Upgrade(AbilitySaveRecord record)
            {
                record.LooseTags = new[] { "State.Migrated" };
            }
        }
    }
}
