using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11101 (Retainers 1): first-class <c>party-hire</c> spawner specs over shared combat templates,
/// sparse <see cref="ActorPartyContractDiff"/> zero-byte persistence, hazard-condition &amp; social-skill pricing,
/// daily wage rollover &amp; desertion, field sustain actions, single friendly-fire tolerance rule (non-crime under
/// contract), and hostile combat death vs player betrayal split in <see cref="DaggerfallSocialConsequences"/>.
/// </summary>
public sealed class PartyHireContractsTests
{
    private static DaggerfallNpc FindHireableInTown(DaggerfallSession session)
    {
        return session.State.Npcs.All.First(npc =>
            npc.Presence == DaggerfallNpcPresence.Active
            && session.PartyHire.IsHireable(npc)
            && session.State.Actors.TryGet(npc.DurableId, out ActorState? live)
            && !live.IsDefeated);
    }

    private static void ClearPlayerGold(DaggerfallSession session)
    {
        ulong gold = session.State.Currency.Read().Gold;
        if (gold > 0)
            Assert.True(session.State.Currency.TrySpendGold(gold, []));
    }

    private static void GrantPotion(DaggerfallSession session, int recipeKey)
    {
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        string key = $"test.potion.{recipeKey}.{Guid.NewGuid():N}";
        factory.Materialize(
            factory.Create(new("UselessItems1", key, DaggerfallItemOwner.Player, Quantity: 1, TemplateIndex: 83, PotionRecipeKey: recipeKey)),
            session.State.Inventory, session.State.ItemInstances, InventoryStackId.Parse(key));
    }

    private static void GrantHealingPotion(DaggerfallSession session)
    {
        int healRecipeKey = TestPayload.Definitions.Magic.PotionRecipes.First(pair =>
            pair.Value.Effects.Any(e => string.Equals(e.Key, "heal-health", StringComparison.Ordinal))).Key;
        GrantPotion(session, healRecipeKey);
    }

    [Fact]
    public void Party_hire_specs_load_and_validate_over_shared_combat_templates()
    {
        IReadOnlyDictionary<string, DaggerfallActorSpawnerSpec> specs = TestPayload.Definitions.ActorCatalog.ProjectPartyHireSpecs();
        Assert.Contains(DaggerfallPartyHireService.DefaultRoadGuardSpecId, specs.Keys);
        Assert.Contains(DaggerfallPartyHireService.MercenarySpecId, specs.Keys);
        Assert.Contains(DaggerfallPartyHireService.KnightRetainerSpecId, specs.Keys);
        Assert.Contains(DaggerfallPartyHireService.BattleMageSpecId, specs.Keys);

        foreach (DaggerfallActorSpawnerSpec spec in specs.Values)
        {
            Assert.Equal(ActorSpawnerFamily.PartyHire, spec.Family);
            Assert.Equal(ActorHarmPolicyKind.Combatant, spec.Harm);
            Assert.Equal("player-ally", spec.Team);
            Assert.NotNull(spec.HireTerms);
            Assert.True(spec.HireTerms!.BaseDailyWage > 0);
            Assert.NotEmpty(spec.DefaultPool ?? []);
            Assert.True(TestPayload.Definitions.ActorCatalog.Templates.ContainsKey(spec.TemplateId),
                $"Party-hire spec '{spec.Id}' must reference an existing shared template '{spec.TemplateId}'.");
        }

        Assert.Equal("warrior", specs[DaggerfallPartyHireService.DefaultRoadGuardSpecId].TemplateId);
        Assert.False(specs[DaggerfallPartyHireService.DefaultRoadGuardSpecId].HireTerms!.CanDelveDungeons);
        Assert.True(specs[DaggerfallPartyHireService.KnightRetainerSpecId].HireTerms!.CanDelveDungeons);
        Assert.True(specs[DaggerfallPartyHireService.BattleMageSpecId].HireTerms!.CanDelveDungeons);
        Assert.True(specs[DaggerfallPartyHireService.KnightRetainerSpecId].HireTerms!.RequiresVouch);
    }

    [Fact]
    public void Hire_pricing_combines_spec_terms_world_conditions_social_skills_and_deposit_waivers()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(10 * 3600);
        DaggerfallNpc hireable = FindHireableInTown(session);
        int region = hireable.Site.Region;

        // Baseline Road Guard quote from party-hire.json (25 deposit, 12/day)
        DaggerfallPartyHireQuote baseline = session.PartyHire.QuoteHire(hireable, ActorPartyRole.Companion);
        Assert.True(baseline.Eligible);
        Assert.Equal(DaggerfallPartyHireService.DefaultRoadGuardSpecId, baseline.SpawnerId);
        Assert.True(baseline.Deposit > 0);
        Assert.True(baseline.DailyWage > 0);

        // Knightly Retainer requires guild rank & vouch -> refused before qualification.
        DaggerfallPartyHireQuote gated = session.PartyHire.QuoteHire(hireable, ActorPartyRole.Companion, DaggerfallPartyHireService.KnightRetainerSpecId);
        Assert.False(gated.Eligible);
        Assert.NotNull(gated.RefusalReason);

        // Seed A6 WorldConditions (MonsterSurge sev 2: +30%, GrainShortage sev 1: +10%, WarFront sev 1: +15% => +55%)
        session.WorldConditions.SeedAuthored(
        [
            new("surge", DaggerfallWorldConditionKind.MonsterSurge, region, Severity: 2),
            new("grain", DaggerfallWorldConditionKind.GrainShortage, region, Severity: 1),
            new("war", DaggerfallWorldConditionKind.Decree, region, SubjectTag: DaggerfallPartyHireService.WarFrontTag, Severity: 1),
        ]);
        Assert.Equal(155, session.PartyHire.RegionalHazardPercent(region));

        DaggerfallPartyHireQuote hazardQuote = session.PartyHire.QuoteHire(hireable, ActorPartyRole.Companion);
        Assert.True(hazardQuote.Deposit > baseline.Deposit);
        Assert.True(hazardQuote.DailyWage > baseline.DailyWage);

        // High social skill (Mercantile 85 -> 20% discount) lowers both deposit and daily wage.
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("mercantile")).BaseValue = 85;
        DaggerfallPartyHireQuote skilledQuote = session.PartyHire.QuoteHire(hireable, ActorPartyRole.Companion);
        Assert.True(skilledQuote.Deposit < hazardQuote.Deposit);
        Assert.True(skilledQuote.DailyWage < hazardQuote.DailyWage);

        // Trusted vouch waives upfront deposit to 0 while retaining daily wage, and with Fighters rank 1 unlocks Knightly Retainer!
        session.State.Social.JoinGuild(41, 0);
        session.State.Social.PromoteGuild(41, 0);
        long referrerId = session.State.Npcs.All.First(n => n.DurableId != hireable.DurableId).DurableId;
        session.Introductions.GrantVouchDirect(referrerId, hireable.DurableId, ActorVouchKind.Trade);

        DaggerfallPartyHireQuote waived = session.PartyHire.QuoteHire(hireable, ActorPartyRole.Companion);
        Assert.Equal(0UL, waived.Deposit);
        Assert.True(waived.DailyWage > 0);

        DaggerfallPartyHireQuote unlockedKnight = session.PartyHire.QuoteHire(hireable, ActorPartyRole.Companion, DaggerfallPartyHireService.KnightRetainerSpecId);
        Assert.True(unlockedKnight.Eligible, unlockedKnight.RefusalReason);
        Assert.True(unlockedKnight.CanDelveDungeons);
        Assert.Equal(0UL, unlockedKnight.Deposit);
    }

    [Fact]
    public void Daily_wage_rollover_pays_wages_warns_on_arrears_deserts_and_supports_field_sustain()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(10 * 3600);
        session.State.Currency.ReceiveGold(200UL);
        DaggerfallNpc hireable = FindHireableInTown(session);

        DaggerfallPartyHireResult hired = session.PartyHire.Resolve(hireable, DaggerfallDialogueTopic.HireCompanion);
        Assert.True(hired.Accepted, hired.Message);
        Assert.True(session.PartyHire.TryGetContract(hireable.DurableId, out ActorPartyContractDiff initialContract));
        Assert.Equal(ActorPartyContractDiff.DefaultMorale, initialContract.Morale);

        // Verify lightweight field sustain dialogue actions appear and work!
        IReadOnlyList<DaggerfallDialogueTopicOption> topics = session.PartyHire.TopicsFor(hireable);
        Assert.Contains(topics, t => t.Id == "give-healing-draught");
        Assert.Contains(topics, t => t.Id == "pay-hazard-bonus");
        Assert.Contains(topics, t => t.Id == "dismiss-party");

        // Wound the companion, share a healing draught -> heals and boosts morale!
        ActorState liveHire = session.State.Actors.Get(hireable.DurableId);
        Track health = liveHire.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        health.SetCurrent(Math.Max(1d, health.Maximum.Value - 20d));
        double hurtHealth = health.Current;
        DaggerfallPartyHireResult noDraught = session.PartyHire.Resolve(hireable, DaggerfallDialogueTopic.GiveHealingDraught);
        Assert.False(noDraught.Accepted);

        GrantHealingPotion(session);
        DaggerfallPartyHireResult healed = session.PartyHire.Resolve(hireable, DaggerfallDialogueTopic.GiveHealingDraught);
        Assert.True(healed.Accepted, healed.Message);
        Assert.True(health.Current > hurtHealth);
        Assert.True(session.PartyHire.TryGetContract(hireable.DurableId, out ActorPartyContractDiff afterHeal));
        Assert.Equal(initialContract.Morale + DaggerfallPartyHireService.HealingDraughtMoraleBonus, afterHeal.Morale);

        // Day 1 rollover with gold -> pays wage cleanly and advances PaidThroughDay.
        ulong goldBeforeRollover = session.State.Currency.Read().Gold;
        IReadOnlyList<DaggerfallPartyDailyOutcome> day1 = session.PartyHire.AdvanceDay(1);
        DaggerfallPartyDailyOutcome paid = Assert.Single(day1);
        Assert.True(paid.Paid);
        Assert.False(paid.Deserted);
        Assert.Equal((ulong)initialContract.DailyWage, goldBeforeRollover - session.State.Currency.Read().Gold);

        // Empty purse -> Day 2 warns & drops morale without deserting yet.
        ClearPlayerGold(session);
        IReadOnlyList<DaggerfallPartyDailyOutcome> day2 = session.PartyHire.AdvanceDay(2);
        DaggerfallPartyDailyOutcome warned = Assert.Single(day2);
        Assert.False(warned.Paid);
        Assert.False(warned.Deserted);
        Assert.Equal(1, warned.UnpaidDays);
        Assert.True(session.PartyHire.TryGetContract(hireable.DurableId, out ActorPartyContractDiff afterWarn));
        Assert.True(afterWarn.Morale < afterHeal.Morale);

        // Paying hazard bonus while in arrears clears unpaid days and restores morale!
        session.State.Currency.ReceiveGold(100UL);
        DaggerfallPartyHireResult bonus = session.PartyHire.Resolve(hireable, DaggerfallDialogueTopic.PayHazardBonus);
        Assert.True(bonus.Accepted, bonus.Message);
        Assert.True(session.PartyHire.TryGetContract(hireable.DurableId, out ActorPartyContractDiff afterBonus));
        Assert.Equal(0, afterBonus.UnpaidDays);
        Assert.True(afterBonus.Morale > afterWarn.Morale);

        // Now go broke for two full unpaid days -> deserts with disposition penalty and clears contract!
        ClearPlayerGold(session);
        int dispBefore = session.State.ActorLedger.Require(hireable.DurableId).Social?.DispositionDelta ?? 0;
        _ = session.PartyHire.AdvanceDay(3);
        IReadOnlyList<DaggerfallPartyDailyOutcome> day4 = session.PartyHire.AdvanceDay(4);
        DaggerfallPartyDailyOutcome deserted = Assert.Single(day4);
        Assert.True(deserted.Deserted);
        Assert.False(session.PartyHire.TryGetContract(hireable.DurableId, out _));
        Assert.False(session.PartyHire.TryGetStagedHiredRole(hireable.DurableId, out _));
        int dispAfter = session.State.ActorLedger.Require(hireable.DurableId).Social?.DispositionDelta ?? 0;
        Assert.Equal(dispBefore + DaggerfallPartyHireService.DesertionDispositionPenalty, dispAfter);
    }

    [Fact]
    public void Friendly_fire_is_never_a_crime_warns_within_tolerance_then_flips_hostile_and_splits_combat_death_from_betrayal()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(10 * 3600);
        session.State.Currency.ReceiveGold(500UL);

        // Hire two companions (both with households in Charing) to verify friendly-fire betrayal vs hostile combat death!
        DaggerfallNpc[] hireables =
        [
            .. session.State.Npcs.All.Where(npc =>
                npc.Presence == DaggerfallNpcPresence.Active
                && !string.IsNullOrWhiteSpace(npc.FamilyId)
                && session.PartyHire.IsHireable(npc)
                && session.State.Actors.TryGet(npc.DurableId, out ActorState? live)
                && !live.IsDefeated)
            .Take(2),
        ];
        Assert.Equal(2, hireables.Length);

        DaggerfallNpc betrayedHire = hireables[0];
        DaggerfallNpc fallenInCombatHire = hireables[1];
        Assert.True(session.PartyHire.Resolve(betrayedHire, DaggerfallDialogueTopic.HireCompanion).Accepted);
        Assert.True(session.PartyHire.Resolve(fallenInCombatHire, DaggerfallDialogueTopic.HireCompanion).Accepted);

        // 1. Incidental clips 1 & 2 against contracted hire: NEVER a crime, warns & drops morale without turning hostile!
        int crimesBefore = session.State.Crime.Incidents.Count;
        session.Crimes.ObserveAttack(DaggerfallActorIdentity.PlayerEntityId, betrayedHire.DurableId, "test:ff:1");
        Assert.Equal(crimesBefore, session.State.Crime.Incidents.Count);

        DaggerfallFriendlyFireResult clip1 = session.PartyHire.ObserveFriendlyFire(betrayedHire.DurableId, fatal: false);
        Assert.True(clip1.Handled);
        Assert.False(clip1.TurnedHostile);
        Assert.Equal(1, clip1.Strikes);
        Assert.NotNull(clip1.Message);

        DaggerfallFriendlyFireResult clip2 = session.PartyHire.ObserveFriendlyFire(betrayedHire.DurableId, fatal: false);
        Assert.True(clip2.Handled);
        Assert.False(clip2.TurnedHostile);
        Assert.Equal(2, clip2.Strikes);

        // 2. 3rd clip breaches tolerance -> turns hostile & marks HostileBetrayal on contract, persists Combatant + hostile across save/load, then player kill triggers betrayal feud!
        DaggerfallFriendlyFireResult clip3 = session.PartyHire.ObserveFriendlyFire(betrayedHire.DurableId, fatal: false);
        Assert.True(clip3.Handled);
        Assert.True(clip3.TurnedHostile);
        Assert.True(session.PartyHire.TryGetContract(betrayedHire.DurableId, out ActorPartyContractDiff betrayalContract));
        Assert.True(betrayalContract.HostileBetrayal);
        ActorState betrayedLiveBeforeSave = session.State.Actors.Get(betrayedHire.DurableId);
        Assert.True(betrayedLiveBeforeSave.Actor.Get<ActorHarmPolicy>().IsCombatant);
        Assert.Equal("outlaws", betrayedLiveBeforeSave.Actor.Get<FactionComponent>().EffectiveTeam);
        Assert.True(betrayedLiveBeforeSave.Actor.Get<Modules.Behavior.DaggerfallEnemyPerceptionMemory>().ForcedHostile);

        // Save/reload after betrayal MUST preserve Combatant harm, hostile team, ForcedHostile, and combat template!
        using (DaggerfallSession reloadedAfterBetrayal = fixture.Restore(session.CaptureSave()))
        {
            ActorState reloadedBetrayed = reloadedAfterBetrayal.State.Actors.Get(betrayedHire.DurableId);
            Assert.True(reloadedBetrayed.Actor.Get<ActorHarmPolicy>().IsCombatant);
            Assert.Equal("outlaws", reloadedBetrayed.Actor.Get<FactionComponent>().EffectiveTeam);
            Assert.True(reloadedBetrayed.Actor.Get<Modules.Behavior.DaggerfallEnemyPerceptionMemory>().ForcedHostile);
            Assert.True(reloadedAfterBetrayal.PartyHire.TryGetContract(betrayedHire.DurableId, out ActorPartyContractDiff reloadedBetrayalContract));
            Assert.True(reloadedBetrayalContract.HostileBetrayal);
        }

        ActorState betrayedActor = session.State.Actors.Get(betrayedHire.DurableId);
        int repBeforeBetrayal = session.State.Social.FactionReputation(betrayedHire.Appearance.FactionId);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(betrayedHire.DurableId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 20, 20d, 1UL, 1UL),
            betrayedActor, minute: 100);
        session.PartyHire.ClearContractOnDeath(betrayedHire.DurableId);
        Assert.False(session.PartyHire.TryGetContract(betrayedHire.DurableId, out _));
        Assert.True(session.State.Social.FactionReputation(betrayedHire.Appearance.FactionId) < repBeforeBetrayal);
        Assert.True(session.SocialConsequences.TryGetHouseholdSnapshot(betrayedHire.FamilyId, out DaggerfallHouseholdSnapshot betrayedSnap));
        Assert.True(betrayedSnap.FeudActive);

        // 3. Contrast: hostile-inflicted combat death of a contracted hire (non-player killer) -> mourning ONLY, ZERO blame/feud/faction loss!
        ActorState combatFallenActor = session.State.Actors.Get(fallenInCombatHire.DurableId);
        int repBeforeCombatDeath = session.State.Social.FactionReputation(fallenInCombatHire.Appearance.FactionId);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(fallenInCombatHire.DurableId, KillerId: 999_999L, DaggerfallDamageCause.PhysicalAttack, 20, 20d, 2UL, 2UL),
            combatFallenActor, minute: 120);
        session.PartyHire.ClearContractOnDeath(fallenInCombatHire.DurableId);
        Assert.False(session.PartyHire.TryGetContract(fallenInCombatHire.DurableId, out _));
        Assert.Equal(repBeforeCombatDeath, session.State.Social.FactionReputation(fallenInCombatHire.Appearance.FactionId));
        Assert.True(session.SocialConsequences.TryGetHouseholdSnapshot(fallenInCombatHire.FamilyId, out DaggerfallHouseholdSnapshot combatSnap));
        Assert.NotNull(combatSnap.MourningUntilDay);
        Assert.False(combatSnap.FeudActive);
        Assert.Equal(0, combatSnap.StandingDelta);
    }

    [Fact]
    public void Sparse_party_contract_round_trips_across_save_and_keeps_non_party_actors_at_zero_bytes()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        // Zero-byte invariant: before hiring, non-party town actors emit zero PartyContract entries and zero PartyContract bytes in the save payload.
        var baselineSave = session.CaptureSave();
        Assert.DoesNotContain(DaggerfallSavePayload.Read(baselineSave).ActorLedger, e => e.PartyContract is not null);
        Assert.DoesNotContain("PartyContract", System.Text.Encoding.UTF8.GetString(baselineSave.Bytes.Span), StringComparison.OrdinalIgnoreCase);

        session.AdvanceElapsedTime(10 * 3600);
        Assert.DoesNotContain(session.State.ActorLedger.Capture(), e => e.PartyContract is not null);

        session.State.Currency.ReceiveGold(300UL);
        DaggerfallNpc hireable = FindHireableInTown(session);
        Assert.True(session.PartyHire.Resolve(hireable, DaggerfallDialogueTopic.HireCompanion).Accepted);
        _ = session.PartyHire.ObserveFriendlyFire(hireable.DurableId, fatal: false);

        var saveBytes = session.CaptureSave();
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(saveBytes);
        // Only the hired actor has a PartyContract; every other actor in town still occupies 0 save bytes!
        ActorLedgerEntry[] contractEntries = [.. saved.ActorLedger.Where(e => e.PartyContract is not null)];
        ActorLedgerEntry savedEntry = Assert.Single(contractEntries);
        Assert.Equal(hireable.DurableId, savedEntry.DurableId);
        Assert.Equal(DaggerfallPartyHireService.DefaultRoadGuardSpecId, savedEntry.PartyContract!.SpawnerId);
        Assert.Equal(1, savedEntry.PartyContract.FriendlyFireStrikes);

        using DaggerfallSession restored = fixture.Restore(saveBytes);
        Assert.True(restored.PartyHire.TryGetContract(hireable.DurableId, out ActorPartyContractDiff restoredContract));
        Assert.Equal(savedEntry.PartyContract.SpawnerId, restoredContract.SpawnerId);
        Assert.Equal(savedEntry.PartyContract.DailyWage, restoredContract.DailyWage);
        Assert.Equal(savedEntry.PartyContract.Morale, restoredContract.Morale);
        Assert.Equal(savedEntry.PartyContract.FriendlyFireStrikes, restoredContract.FriendlyFireStrikes);
        Assert.Equal(savedEntry.PartyContract.Stance, restoredContract.Stance);
    }

    [Fact]
    public void Normal_recruitment_materializes_party_hire_specs_and_enforces_gated_retainers_through_dialogue()
    {
        // 1. Road Guard recruited through normal dialogue in town adopts warrior template, player-ally team, and Combatant harm.
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using (DaggerfallSession town = fixture.Start(fixture.ExteriorProfile))
        {
            town.AdvanceElapsedTime(10 * 3600);
            town.State.Currency.ReceiveGold(200UL);
            DaggerfallNpc roadGuardCandidate = FindHireableInTown(town);
            IReadOnlyList<DaggerfallDialogueTopicOption> offerTopics = town.PartyHire.TopicsFor(roadGuardCandidate);
            Assert.Contains(offerTopics, t => t.Id == "hire-companion" && t.Label.Contains("Road Guard", StringComparison.Ordinal));

            DaggerfallPartyHireResult hiredRoadGuard = town.PartyHire.Resolve(roadGuardCandidate, DaggerfallDialogueTopic.HireCompanion);
            Assert.True(hiredRoadGuard.Accepted, hiredRoadGuard.Message);
            ActorState liveGuard = town.State.Actors.Get(roadGuardCandidate.DurableId);
            Assert.True(liveGuard.Actor.Get<ActorHarmPolicy>().IsCombatant);
            Assert.Equal("player-ally", liveGuard.Actor.Get<FactionComponent>().EffectiveTeam);
            Assert.Equal("warrior", liveGuard.Actor.Get<ActorOverlayComponent>().ArchetypeId);
            // Live StatsComponent must adopt the shared warrior combat template's stats, not keep civilian baseline stats!
            DaggerfallActorDefinition warriorSpec = TestPayload.Definitions.RequireActor(new(DaggerfallPartyHireService.DefaultRoadGuardSpecId));
            Assert.Equal(warriorSpec.Stats.Values[DaggerfallMechanicsIds.Strength], liveGuard.Stats.GetStat(StatId.Parse("strength")).ValueInt);
            Assert.Equal(warriorSpec.Stats.Values[DaggerfallMechanicsIds.Endurance], liveGuard.Stats.GetStat(StatId.Parse("endurance")).ValueInt);
            Assert.Equal(warriorSpec.Stats.Values[new("long-blade")], liveGuard.Stats.GetStat(StatId.Parse("long-blade")).ValueInt);
            Assert.True(town.PartyHire.TryGetContract(roadGuardCandidate.DurableId, out ActorPartyContractDiff guardContract));
            Assert.Equal(DaggerfallPartyHireService.DefaultRoadGuardSpecId, guardContract.SpawnerId);
            Assert.False(guardContract.CanDelveDungeons);

            // Save/reload preserves live warrior template stats and combatant status!
            using DaggerfallSession reloadedTown = fixture.Restore(town.CaptureSave());
            ActorState reloadedGuard = reloadedTown.State.Actors.Get(roadGuardCandidate.DurableId);
            Assert.True(reloadedGuard.Actor.Get<ActorHarmPolicy>().IsCombatant);
            Assert.Equal("player-ally", reloadedGuard.Actor.Get<FactionComponent>().EffectiveTeam);
            Assert.Equal(warriorSpec.Stats.Values[DaggerfallMechanicsIds.Strength], reloadedGuard.Stats.GetStat(StatId.Parse("strength")).ValueInt);
            Assert.Equal(warriorSpec.Stats.Values[new("long-blade")], reloadedGuard.Stats.GetStat(StatId.Parse("long-blade")).ValueInt);
        }

        // 2. Gated Knightly Retainer generated in Knightly Order hall from hire-knight-retainer spec:
        //    normal dialogue refuses before rank + vouch, then enlists with shared 'knight' template, Combatant harm, player-ally team, and CanDelveDungeons.
        using (DaggerfallSession knightHall = fixture.Start(fixture.KnightlyProfile))
        {
            knightHall.State.Currency.ReceiveGold(500UL);
            // Untouched generated recruit must write 0 save bytes before interaction!
            var untouchedSave = knightHall.CaptureSave();
            Assert.DoesNotContain(DaggerfallSavePayload.Read(untouchedSave).ActorLedger, e => e.PartyContract is not null);

            DaggerfallNpc knightRecruit = Assert.Single(knightHall.State.Npcs.All, npc =>
                npc.Presence == DaggerfallNpcPresence.Active
                && string.Equals(npc.Role, "knight-retainer", StringComparison.Ordinal));
            Assert.True(knightHall.PartyHire.IsHireable(knightRecruit));
            ActorState recruitBefore = knightHall.State.Actors.Get(knightRecruit.DurableId);
            Assert.Equal("knight", recruitBefore.Actor.Get<ActorOverlayComponent>().ArchetypeId);
            Assert.Equal(DaggerfallPartyHireService.KnightRetainerSpecId, recruitBefore.Actor.Get<ActorOriginComponent>().Origin.SpawnerId);

            // Normal dialogue refuses before guild rank & vouch!
            IReadOnlyList<DaggerfallDialogueTopicOption> gatedTopics = knightHall.PartyHire.TopicsFor(knightRecruit);
            Assert.Contains(gatedTopics, t => t.Id == "hire-companion" && t.Label.Contains("Knightly Retainer", StringComparison.Ordinal));
            DaggerfallPartyHireResult refusedNoRank = knightHall.PartyHire.Resolve(knightRecruit, DaggerfallDialogueTopic.HireCompanion);
            Assert.False(refusedNoRank.Accepted);
            Assert.Contains("rank", refusedNoRank.Message, StringComparison.OrdinalIgnoreCase);

            // Join Order of the Dragon (368) and earn rank 1 -> still gated by vouch!
            knightHall.State.Social.JoinGuild(368, 0);
            knightHall.State.Social.PromoteGuild(368, 0);
            DaggerfallPartyHireResult refusedNoVouch = knightHall.PartyHire.Resolve(knightRecruit, DaggerfallDialogueTopic.HireCompanion);
            Assert.False(refusedNoVouch.Accepted);
            Assert.Contains("vouch", refusedNoVouch.Message, StringComparison.OrdinalIgnoreCase);

            // Grant vouch -> normal dialogue now enlists the Knightly Retainer!
            long referrerId = knightHall.State.Npcs.All.First(n => n.DurableId != knightRecruit.DurableId).DurableId;
            knightHall.Introductions.GrantVouchDirect(referrerId, knightRecruit.DurableId, ActorVouchKind.Trade);
            DaggerfallPartyHireResult hiredKnight = knightHall.PartyHire.Resolve(knightRecruit, DaggerfallDialogueTopic.HireCompanion);
            Assert.True(hiredKnight.Accepted, hiredKnight.Message);

            ActorState liveKnight = knightHall.State.Actors.Get(knightRecruit.DurableId);
            Assert.True(liveKnight.Actor.Get<ActorHarmPolicy>().IsCombatant);
            Assert.Equal("player-ally", liveKnight.Actor.Get<FactionComponent>().EffectiveTeam);
            Assert.Equal("knight", liveKnight.Actor.Get<ActorOverlayComponent>().ArchetypeId);
            Assert.True(knightHall.PartyHire.TryGetContract(knightRecruit.DurableId, out ActorPartyContractDiff knightContract));
            Assert.Equal(DaggerfallPartyHireService.KnightRetainerSpecId, knightContract.SpawnerId);
            Assert.True(knightContract.CanDelveDungeons);
        }

        // 3. Gated Guild Battlemage generated in Mages Guild hall from hire-battle-mage spec:
        using (DaggerfallSession magesHall = fixture.Start(fixture.MagesProfile))
        {
            magesHall.State.Currency.ReceiveGold(500UL);
            DaggerfallNpc mageRecruit = Assert.Single(magesHall.State.Npcs.All, npc =>
                npc.Presence == DaggerfallNpcPresence.Active
                && string.Equals(npc.Role, "battle-mage", StringComparison.Ordinal));
            DaggerfallPartyHireResult refusedMage = magesHall.PartyHire.Resolve(mageRecruit, DaggerfallDialogueTopic.HireCompanion);
            Assert.False(refusedMage.Accepted);

            magesHall.State.Social.JoinGuild(40, 0);
            magesHall.State.Social.PromoteGuild(40, 0);
            long referrerId = magesHall.State.Npcs.All.First(n => n.DurableId != mageRecruit.DurableId).DurableId;
            magesHall.Introductions.GrantVouchDirect(referrerId, mageRecruit.DurableId, ActorVouchKind.Trade);
            DaggerfallPartyHireResult hiredMage = magesHall.PartyHire.Resolve(mageRecruit, DaggerfallDialogueTopic.HireCompanion);
            Assert.True(hiredMage.Accepted, hiredMage.Message);
            ActorState liveMage = magesHall.State.Actors.Get(mageRecruit.DurableId);
            Assert.True(liveMage.Actor.Get<ActorHarmPolicy>().IsCombatant);
            Assert.Equal("player-ally", liveMage.Actor.Get<FactionComponent>().EffectiveTeam);
            Assert.Equal("battlemage", liveMage.Actor.Get<ActorOverlayComponent>().ArchetypeId);
        }
    }

    [Fact]
    public void Give_healing_draught_ignores_restore_power_potions_and_consumes_only_health_restoration_potions()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(10 * 3600);
        session.State.Currency.ReceiveGold(200UL);
        DaggerfallNpc hireable = FindHireableInTown(session);
        Assert.True(session.PartyHire.Resolve(hireable, DaggerfallDialogueTopic.HireCompanion).Accepted);

        ActorState live = session.State.Actors.Get(hireable.DurableId);
        Track health = live.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        health.SetCurrent(Math.Max(1d, health.Maximum.Value - 20d));
        double hurtHealth = health.Current;

        // Restore Power (5188896) restores spell points, NOT health -> must be rejected and left untouched!
        const int RestorePowerRecipeKey = 5188896;
        GrantPotion(session, RestorePowerRecipeKey);
        int stacksBefore = session.State.Inventory.Read().Stacks.Count;
        DaggerfallPartyHireResult rejected = session.PartyHire.Resolve(hireable, DaggerfallDialogueTopic.GiveHealingDraught);
        Assert.False(rejected.Accepted);
        Assert.Equal(hurtHealth, health.Current);
        Assert.Equal(stacksBefore, session.State.Inventory.Read().Stacks.Count);

        // Adding a real Healing potion (4975678, effect 'heal-health') succeeds while leaving Restore Power untouched!
        GrantHealingPotion(session);
        DaggerfallPartyHireResult healed = session.PartyHire.Resolve(hireable, DaggerfallDialogueTopic.GiveHealingDraught);
        Assert.True(healed.Accepted, healed.Message);
        Assert.True(health.Current > hurtHealth);
        Assert.Contains(session.State.Inventory.Read().Stacks, stack =>
            session.State.ItemInstances.ContainsStack(DaggerfallItemOwner.Player, stack.Id)
            && session.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player, stack.Id).PotionRecipeKey == RestorePowerRecipeKey);
    }

    [Fact]
    public void Intra_day_calendar_updates_and_save_reload_are_idempotent_for_unpaid_wages()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(10 * 3600);
        session.State.Currency.ReceiveGold(100UL);
        DaggerfallNpc hireable = FindHireableInTown(session);
        Assert.True(session.PartyHire.Resolve(hireable, DaggerfallDialogueTopic.HireCompanion).Accepted);

        // Spend all remaining gold and cross midnight into Day 1 (first unpaid day -> warn once, morale drops once).
        ClearPlayerGold(session);
        long startDay = session.Calendar.DayNumber;
        session.AdvanceElapsedTime(15 * 3600); // crosses into startDay + 1 at ~01:00
        Assert.Equal(startDay + 1, session.Calendar.DayNumber);

        Assert.True(session.PartyHire.TryGetContract(hireable.DurableId, out ActorPartyContractDiff afterFirstUnpaidDay));
        Assert.Equal(1, afterFirstUnpaidDay.UnpaidDays);
        Assert.Equal((int)(startDay + 1), afterFirstUnpaidDay.EffectiveLastBilledDay);
        int moraleAfterFirstDay = afterFirstUnpaidDay.Morale;

        // Advance multiple ordinary intervals WITHIN the exact same Day 1 -> MUST NOT add arrears, drop morale, or desert!
        session.AdvanceElapsedTime(1800);
        session.AdvanceElapsedTime(3600);
        session.AdvanceElapsedTime(3600);
        Assert.Equal(startDay + 1, session.Calendar.DayNumber);
        Assert.True(session.PartyHire.TryGetContract(hireable.DurableId, out ActorPartyContractDiff afterSameDayTicks));
        Assert.Equal(1, afterSameDayTicks.UnpaidDays);
        Assert.Equal(moraleAfterFirstDay, afterSameDayTicks.Morale);

        // Save and reload on Day 1, then advance again within Day 1 -> still idempotent!
        var midDaySave = session.CaptureSave();
        using DaggerfallSession reloaded = fixture.Restore(midDaySave);
        reloaded.AdvanceElapsedTime(3600);
        Assert.Equal(startDay + 1, reloaded.Calendar.DayNumber);
        Assert.True(reloaded.PartyHire.TryGetContract(hireable.DurableId, out ActorPartyContractDiff afterReloadTick));
        Assert.Equal(1, afterReloadTick.UnpaidDays);
        Assert.Equal(moraleAfterFirstDay, afterReloadTick.Morale);

        // Crossing into Day 2 still broke -> now reaches 2 unpaid days and deserts cleanly!
        reloaded.AdvanceElapsedTime(24 * 3600);
        Assert.False(reloaded.PartyHire.TryGetContract(hireable.DurableId, out _));
        Assert.False(reloaded.PartyHire.TryGetStagedHiredRole(hireable.DurableId, out _));
    }

    [Fact]
    public void ResolvePortraitMedia_every_race_and_gender_head_resolves_to_delivered_media_in_classic_and_refreshed_art()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        const string payload = "worldrpg/packs/daggerfall.art-replacements/payload.json";
        DaggerfallArtReplacements artReplacements = DaggerfallArtReplacements.Read(
            TestContentFiles.Read(Path.Combine(root, "content", payload)), "daggerfall.art-replacements");
        foreach (DaggerfallArtImage image in artReplacements.Images)
            content.Add(image.Path, TestContentFiles.ReadCopy(Path.Combine(root, "content", image.Path)));

        DaggerfallData data = DataOf(content);
        string[] classicHeads =
        [
            .. definitions.CharacterPresentation.Races.Values
                .SelectMany(race => race.Layers.Where(layer => layer.Kind == DaggerfallCharacterLayerKind.Head))
                .Select(layer => layer.MediaId),
        ];
        DaggerfallUiArt uiArt = DaggerfallUiArt.Read(
            data,
            inputs.ClassicPresentation.InventoryIcons.Values,
            characterIcons: classicHeads,
            replacements: artReplacements,
            drawnAnew: DaggerfallUiArtChoice.DrawnAnew(artReplacements));

        DaggerfallNpcSite charing = new(17, "Charing", string.Empty);
        HashSet<string> resolvedClassic = new(StringComparer.Ordinal);
        HashSet<string> resolvedRefreshed = new(StringComparer.Ordinal);

        // Exercise every race and gender in CharacterPresentation across the full head list (not one seed),
        // plus fallback cases (NameSeed == 0 and an unknown race).
        foreach (string raceId in definitions.CharacterPresentation.Races.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            foreach (DaggerfallCharacterGender gender in new[] { DaggerfallCharacterGender.Male, DaggerfallCharacterGender.Female })
            {
                string genderText = gender == DaggerfallCharacterGender.Female ? "Female" : "Male";
                IReadOnlyList<DaggerfallCharacterLayerDefinition> expectedHeads = definitions.CharacterPresentation.RequireRace(raceId).Heads(gender);
                Assert.NotEmpty(expectedHeads);

                HashSet<string> seenForGroup = new(StringComparer.Ordinal);
                for (int seed = 1; seed <= expectedHeads.Count; seed++)
                {
                    DaggerfallNpc npc = new(
                        0x4000_0000_0000L + seed,
                        DaggerfallNpcKind.Static,
                        $"test/{raceId}/{genderText}/{seed}",
                        charing,
                        new DaggerfallNpcAppearance(raceId, genderText, 182, 0, seed, 518),
                        "road-guard",
                        ["talk"],
                        DaggerfallNpcPresence.Active,
                        null, null, null);
                    string classicId = DaggerfallPartyHireService.ResolvePortraitMedia(definitions, npc);
                    seenForGroup.Add(classicId);
                    resolvedClassic.Add(classicId);
                    resolvedRefreshed.Add(artReplacements.Drawn(classicId));
                }

                // Every head in the race/gender list must be reachable.
                Assert.Equal(expectedHeads.Count, seenForGroup.Count);

                // NameSeed == 0 falls back to DurableId without throwing or leaving the head list.
                DaggerfallNpc zeroSeedNpc = new(
                    85073632782733L,
                    DaggerfallNpcKind.Static,
                    $"test/{raceId}/{genderText}/zero",
                    charing,
                    new DaggerfallNpcAppearance(raceId, genderText, 182, 0, 0, 518),
                    "road-guard",
                    ["talk"],
                    DaggerfallNpcPresence.Active,
                    null, null, null);
                Assert.Contains(DaggerfallPartyHireService.ResolvePortraitMedia(definitions, zeroSeedNpc), seenForGroup);
            }
        }

        // Unknown race falls back cleanly to Breton without failing.
        DaggerfallNpc unknownRaceNpc = new(
            85073632782733L,
            DaggerfallNpcKind.Static,
            "test/unknown/female",
            charing,
            new DaggerfallNpcAppearance("imperial", "Female", 182, 0, 7, 518),
            "road-guard",
            ["talk"],
            DaggerfallNpcPresence.Active,
            null, null, null);
        Assert.Contains(DaggerfallPartyHireService.ResolvePortraitMedia(definitions, unknownRaceNpc), resolvedClassic);

        // Every classic head AND every refreshed head must resolve to delivered image bytes through DaggerfallUiArt.Demand.
        uiArt.Demand(resolvedRefreshed);
        uiArt.Demand(resolvedClassic);

        Dictionary<string, DaggerfallUiArtImage> delivered = uiArt.Images.Concat(uiArt.Demanded)
            .ToDictionary(image => image.Id, StringComparer.Ordinal);
        foreach (string id in resolvedRefreshed.Concat(resolvedClassic))
        {
            Assert.True(delivered.TryGetValue(id, out DaggerfallUiArtImage? image), $"Portrait media '{id}' was not delivered by DaggerfallUiArt.");
            Assert.StartsWith("data:image/", image!.Image, StringComparison.Ordinal);
            Assert.True(image.Image.Length > 64, $"Portrait media '{id}' carried an empty image payload.");
        }
    }
}
