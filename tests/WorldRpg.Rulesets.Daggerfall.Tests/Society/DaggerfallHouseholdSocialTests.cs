using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallHouseholdSocialTests
{
    [Fact]
    public void Exact_FamilyId_witness_discretion_prevents_false_family_reports_across_palace_courtiers_and_guildhall_members()
    {
        using var fixture = new ConditionSessionFixture(random: RandomMaximum.Create());
        DaggerfallSession session = fixture.Session;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = 100;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = 100;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 100;

        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite palaceSite = new(site.Id.Region, site.Name, "0/0/0", session.Sites.ActiveProfile.LogicalId);

        long royalVictim = AddNpcWithFamily(session, site.Id, palaceSite, "palace/king", "King Gothryd", "fam:dynasty:daggerfall");
        long royalKin = AddNpcWithFamily(session, site.Id, palaceSite, "palace/queen", "Queen Aubk-i", "fam:dynasty:daggerfall");
        long courtierHouseA = AddNpcWithFamily(session, site.Id, palaceSite, "palace/courtier-a", "Lord Vane", "fam:house:r17:0");
        long courtierHouseB = AddNpcWithFamily(session, site.Id, palaceSite, "palace/courtier-b", "Lady Vane", "fam:house:r17:1");
        long guildhallMemberA = AddNpcWithFamily(session, site.Id, palaceSite, "guild/member-a", "Soren Ashford", null);
        long guildhallMemberB = AddNpcWithFamily(session, site.Id, palaceSite, "guild/member-b", "Mira Ashford", null);

        // 1. Crime against royal dynasty: royal kin ALWAYS reports (FamilyMember); rival Great House courtiers in the same palace use discretion.
        DaggerfallCrimeVictimContext royalVictimCtx = new(
            ActorId: royalVictim,
            BuildingKey: "0/0/0",
            Region: site.Id.Region,
            Location: site.Name,
            BuildingType: 16);
        DaggerfallWitnessDiscretionResult palaceResult = session.Crimes.EvaluateWitnesses(
            "palace-theft:1",
            DaggerfallCrimeKind.Theft,
            royalVictimCtx,
            new(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [royalKin, courtierHouseA, courtierHouseB]));

        Assert.Equal(DaggerfallWitnessReportReason.FamilyMember, palaceResult.Decisions.Single(d => d.WitnessActorId == royalKin).Reason);
        Assert.True(palaceResult.Decisions.Single(d => d.WitnessActorId == royalKin).Reported);
        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollIgnored, palaceResult.Decisions.Single(d => d.WitnessActorId == courtierHouseA).Reason);
        Assert.False(palaceResult.Decisions.Single(d => d.WitnessActorId == courtierHouseA).Reported);
        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollIgnored, palaceResult.Decisions.Single(d => d.WitnessActorId == courtierHouseB).Reason);
        Assert.False(palaceResult.Decisions.Single(d => d.WitnessActorId == courtierHouseB).Reported);

        // 2. Rival courtiers with identical surname ("Vane") but different Great Houses do NOT trigger FamilyMember.
        DaggerfallWitnessDiscretionResult rivalCourtiers = session.Crimes.EvaluateWitnesses(
            "palace-theft:2",
            DaggerfallCrimeKind.Theft,
            new DaggerfallCrimeVictimContext(ActorId: courtierHouseA, BuildingKey: "0/0/0", Region: site.Id.Region, Location: site.Name, BuildingType: 16),
            new(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [courtierHouseB]));
        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollIgnored, Assert.Single(rivalCourtiers.Decisions).Reason);
        Assert.False(Assert.Single(rivalCourtiers.Decisions).Reported);

        // 3. Guildhall members with null FamilyId and identical surname ("Ashford") do NOT trigger FamilyMember.
        DaggerfallWitnessDiscretionResult guildResult = session.Crimes.EvaluateWitnesses(
            "guild-theft:1",
            DaggerfallCrimeKind.Theft,
            new DaggerfallCrimeVictimContext(ActorId: guildhallMemberA, BuildingKey: "0/0/0", Region: site.Id.Region, Location: site.Name, BuildingType: 11),
            new(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [guildhallMemberB]));
        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollIgnored, Assert.Single(guildResult.Decisions).Reason);
        Assert.False(Assert.Single(guildResult.Decisions).Reported);
    }

    [Fact]
    public void Unwitnessed_murder_causes_zero_kin_grudge_while_witnessed_local_murder_ripples_only_within_settlement()
    {
        using var fixture = new HouseholdSessionFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite localSite = new(site.Id.Region, site.Name, "1/1/0", session.Sites.ActiveProfile.LogicalId);

        string householdA = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 1, 0);
        string householdB = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 1, 1);

        long unwitnessedVictim = AddNpcWithFamily(session, site.Id, localSite, "local/unwitnessed-victim", "Corin Hale", householdA);
        long unwitnessedKin = AddNpcWithFamily(session, site.Id, localSite, "local/unwitnessed-kin", "Tessa Hale", householdA);

        // 1. Unwitnessed murder (no perception witnesses): kin stays completely untouched (0 disposition, 0 ledger, 0 WordSpread).
        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            ReadOnlyMemory<PerceptionPair>.Empty, ReadOnlyMemory<PerceptionAggregate>.Empty,
            0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        ActorState victimA = session.State.Actors.Get(unwitnessedVictim);
        victimA.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(unwitnessedVictim, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            victimA,
            minute: 100L);

        Assert.Equal(0, session.SocialConsequences.EffectiveDispositionDelta(unwitnessedKin));
        Assert.False(session.State.ActorLedger.TryGet(unwitnessedKin, out _));
        Assert.DoesNotContain(session.WordSpread.Wavefronts, wf => wf.Kind == DaggerfallWordEventKind.HouseholdStanding);

        // 2. Witnessed murder of a town-local household member: same-settlement kin gets grudge in ActorLedger,
        //    unrelated bystander does not, and ZERO HouseholdStanding WordSpread wavefronts are emitted for fam:local:...
        long witnessedVictim = AddNpcWithFamily(session, site.Id, localSite, "local/witnessed-victim", "Bram Mercer", householdB);
        long witnessedKin = AddNpcWithFamily(session, site.Id, localSite, "local/witnessed-kin", "Lina Mercer", householdB);
        long witnessStranger = AddNpcWithFamily(session, site.Id, localSite, "local/witness-stranger", "Oren Pike", "fam:local:r17:s100:h7");
        long innocentStranger = AddNpcWithFamily(session, site.Id, localSite, "local/innocent-stranger", "Dara Croft", "fam:local:r17:s100:h6");

        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witnessStranger), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        ActorState victimB = session.State.Actors.Get(witnessedVictim);
        victimB.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(witnessedVictim, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            victimB,
            minute: 200L);

        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty, session.SocialConsequences.EffectiveDispositionDelta(witnessedKin));
        Assert.Equal(DaggerfallSocialConsequences.WitnessMurderDispositionPenalty, session.SocialConsequences.EffectiveDispositionDelta(witnessStranger));
        Assert.Equal(0, session.SocialConsequences.EffectiveDispositionDelta(innocentStranger));
        Assert.DoesNotContain(session.WordSpread.Wavefronts, wf => wf.Kind == DaggerfallWordEventKind.HouseholdStanding);
    }

    [Fact]
    public void Great_house_and_dynasty_crimes_spread_province_wide_at_courier_speed_and_survive_save_reload()
    {
        using var fixture = new HouseholdSessionFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord originSite = session.Site.ActiveSite!;
        DaggerfallSiteRecord distantSite = TestPayload.Definitions.Locations.Records
            .First(r => r.Id != originSite.Id
                && double.Hypot(r.MapPixelX - originSite.MapPixelX, r.MapPixelY - originSite.MapPixelY) >= 25.0);

        const string greatHouse = "fam:house:r17:0";
        DaggerfallNpcSite originNpcSite = new(originSite.Id.Region, originSite.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
        long victimNoble = AddNpcWithFamily(session, originSite.Id, originNpcSite, "noble/victim", "Lord Aldric", greatHouse);
        long kinNoble = AddNpcWithFamily(session, originSite.Id, originNpcSite, "noble/kin", "Lady Seraphina", greatHouse);
        long witness = AddNpcWithFamily(session, originSite.Id, originNpcSite, "noble/witness", "Guard Witness", null);

        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witness), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        long startMinute = ((WorldRpg.Rulesets.Daggerfall.Travel.IDaggerfallExpeditionSurroundings)session).Calendar.MinuteNumber;
        ActorState victimActor = session.State.Actors.Get(victimNoble);
        victimActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(victimNoble, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            victimActor,
            minute: startMinute);

        DaggerfallWordWavefrontSave wf = Assert.Single(session.WordSpread.Wavefronts, w => w.Kind == DaggerfallWordEventKind.HouseholdStanding);
        Assert.Equal(greatHouse, wf.FamilyId);
        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty, wf.Delta);

        // Origin town (instant bubble): kin immediately feels the grudge.
        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty,
            session.SocialConsequences.EffectiveDispositionDelta(kinNoble, originSite, startMinute));

        // Distant town: in transit at t=0, arrives after courier travel time!
        Assert.Equal(0, session.SocialConsequences.EffectiveDispositionDelta(kinNoble, distantSite, startMinute));
        long arrivedMinute = startMinute + 14L * 24L * 60L; // 14 days later
        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty,
            session.SocialConsequences.EffectiveDispositionDelta(kinNoble, distantSite, arrivedMinute));

        // Save/reload preserves the HouseholdStanding wavefront cleanly (while untouched kinNoble wrote 0 ledger bytes!).
        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(saved);
        DaggerfallWordWavefrontSave restoredWf = Assert.Single(restored.WordSpread.Wavefronts, w => w.Kind == DaggerfallWordEventKind.HouseholdStanding);
        Assert.Equal(greatHouse, restoredWf.FamilyId);
        // Re-materialize deterministic kinNoble in the restored session; the persisted HouseholdStanding wavefront applies!
        long restoredKin = AddNpcWithFamily(restored, originSite.Id, originNpcSite, "noble/kin", "Lady Seraphina", greatHouse);
        Assert.Equal(kinNoble, restoredKin);
        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty,
            restored.SocialConsequences.EffectiveDispositionDelta(restoredKin, distantSite, arrivedMinute));
    }

    [Fact]
    public void Helping_kin_ripples_goodwill_locally_for_commoners_and_province_wide_for_great_houses()
    {
        using var fixture = new ConditionSessionFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord originSite = session.Site.ActiveSite!;
        DaggerfallSiteRecord distantSite = TestPayload.Definitions.Locations.Records
            .First(r => r.Id != originSite.Id
                && double.Hypot(r.MapPixelX - originSite.MapPixelX, r.MapPixelY - originSite.MapPixelY) >= 25.0);

        DaggerfallNpcSite localSite = new(originSite.Id.Region, originSite.Name, "0/0/2", session.Sites.ActiveProfile.LogicalId);
        string localHousehold = DaggerfallHouseholds.BuildingHouseholdId(originSite.Id, 0, 0, 2);

        long shopkeeper = AddNpcWithFamily(session, originSite.Id, localSite, "shop/keeper", "Garrick Fen", localHousehold);
        long shopSibling = AddNpcWithFamily(session, originSite.Id, localSite, "shop/sibling", "Nora Fen", localHousehold);

        // Positive personal standing bump (e.g. completing a quest for shopkeeper) gives local kin +5 goodwill, 0 WordSpread.
        session.Introductions.AdjustActorDisposition(shopkeeper, +10);
        Assert.Equal(+10, session.SocialConsequences.EffectiveDispositionDelta(shopkeeper));
        Assert.Equal(+5, session.SocialConsequences.EffectiveDispositionDelta(shopSibling));
        Assert.Empty(session.WordSpread.Wavefronts);

        // Positive standing bump for a Great House member emits a province-wide positive HouseholdStanding wavefront!
        const string greatHouse = "fam:house:r17:1";
        long factor = AddNpcWithFamily(session, originSite.Id, localSite, "house/factor", "Marcus Valen", greatHouse);
        long distantCousin = AddNpcWithFamily(session, originSite.Id, localSite, "house/cousin", "Celia Valen", greatHouse);

        long t0 = ((WorldRpg.Rulesets.Daggerfall.Travel.IDaggerfallExpeditionSurroundings)session).Calendar.MinuteNumber;
        session.Introductions.AdjustActorDisposition(factor, +12);
        DaggerfallWordWavefrontSave goodwillWf = Assert.Single(session.WordSpread.Wavefronts);
        Assert.Equal(DaggerfallWordEventKind.HouseholdStanding, goodwillWf.Kind);
        Assert.Equal(greatHouse, goodwillWf.FamilyId);
        Assert.Equal(+6, goodwillWf.Delta);
        Assert.Equal(+6, session.SocialConsequences.EffectiveDispositionDelta(distantCousin, originSite, t0));
        Assert.Equal(0, session.SocialConsequences.EffectiveDispositionDelta(distantCousin, distantSite, t0));
        Assert.Equal(+6, session.SocialConsequences.EffectiveDispositionDelta(distantCousin, distantSite, t0 + 14L * 24L * 60L));
    }

    [Fact]
    public void Local_household_grudge_and_goodwill_reach_unmaterialized_same_settlement_relatives_across_admission_and_save_reload()
    {
        using var fixture = new HouseholdSessionFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite localSite = new(site.Id.Region, site.Name, "1/2/0", session.Sites.ActiveProfile.LogicalId);

        string grudgeHousehold = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 2, 0);
        string goodwillHousehold = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 2, 1);
        string unrelatedHousehold = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 2, 2);

        long murderedMember = AddNpcWithFamily(session, site.Id, localSite, "r11103/grudge-victim", "Alden Moss", grudgeHousehold);
        long witness = AddNpcWithFamily(session, site.Id, localSite, "r11103/witness", "Garret Watch", null);
        long helpedMember = AddNpcWithFamily(session, site.Id, localSite, "r11103/goodwill-subject", "Nora Fen", goodwillHousehold);

        // Precompute durable IDs for relatives that have NEVER been registered, materialized, or entered into ActorLedger.
        DaggerfallWorldProfileKey profile = session.Sites.ActiveProfile;
        long unmaterializedGrudgeKinId = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{profile.LogicalId}/r11103/grudge-late-kin");
        long unmaterializedGoodwillKinId = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{profile.LogicalId}/r11103/goodwill-late-kin");
        long unrelatedRelativeId = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{profile.LogicalId}/r11103/unrelated-late");

        Assert.False(session.State.Npcs.TryGet(unmaterializedGrudgeKinId, out _));
        Assert.False(session.State.ActorLedger.TryGet(unmaterializedGrudgeKinId, out _));
        Assert.False(session.State.Npcs.TryGet(unmaterializedGoodwillKinId, out _));
        Assert.False(session.State.ActorLedger.TryGet(unmaterializedGoodwillKinId, out _));

        // 1. Witnessed murder of grudgeHousehold member while late-kin is absent from Actors, Npcs, and ActorLedger.
        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witness), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        ActorState victimActor = session.State.Actors.Get(murderedMember);
        victimActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(murderedMember, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            victimActor,
            minute: 300L);

        // 2. Help goodwillHousehold member (+10 personal -> +5 kin goodwill) while late-kin is absent from Actors, Npcs, and ActorLedger.
        session.Introductions.AdjustActorDisposition(helpedMember, +10);
        Assert.Equal(+10, session.SocialConsequences.EffectiveDispositionDelta(helpedMember));

        // Zero cross-town HouseholdStanding WordSpread wavefronts for local households!
        Assert.DoesNotContain(session.WordSpread.Wavefronts, wf => wf.Kind == DaggerfallWordEventKind.HouseholdStanding);

        // Save before admitting the unmaterialized relatives: verify sparse LocalHouseholds and 0 ledger bytes for untouched relatives/households!
        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallSavePayload raw = DaggerfallSavePayload.Read(saved);
        Assert.Contains(raw.LocalHouseholds.Standings, s => s.FamilyId == grudgeHousehold && s.StandingDelta == DaggerfallSocialConsequences.KinMurderDispositionPenalty);
        Assert.Contains(raw.LocalHouseholds.Standings, s => s.FamilyId == goodwillHousehold && s.StandingDelta == +5);
        Assert.DoesNotContain(raw.LocalHouseholds.Standings, s => s.FamilyId == unrelatedHousehold);
        Assert.DoesNotContain(raw.ActorLedger ?? [], e => e.DurableId == unmaterializedGrudgeKinId || e.DurableId == unmaterializedGoodwillKinId || e.DurableId == unrelatedRelativeId);

        // 3. Admit the previously unmaterialized relatives in the live session -> both immediately inherit local grudge / goodwill!
        long liveLateGrudgeKin = AddNpcWithFamily(session, site.Id, localSite, "r11103/grudge-late-kin", "Bryn Moss", grudgeHousehold);
        long liveLateGoodwillKin = AddNpcWithFamily(session, site.Id, localSite, "r11103/goodwill-late-kin", "Sera Fen", goodwillHousehold);
        long liveUnrelated = AddNpcWithFamily(session, site.Id, localSite, "r11103/unrelated-late", "Corin Hale", unrelatedHousehold);
        Assert.Equal(unmaterializedGrudgeKinId, liveLateGrudgeKin);
        Assert.Equal(unmaterializedGoodwillKinId, liveLateGoodwillKin);
        Assert.Equal(unrelatedRelativeId, liveUnrelated);

        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty, session.SocialConsequences.EffectiveDispositionDelta(liveLateGrudgeKin));
        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty, session.State.Social.ReactionForNpc(session.State.Npcs.Require(liveLateGrudgeKin)).ActorDisposition);
        Assert.Equal(+5, session.SocialConsequences.EffectiveDispositionDelta(liveLateGoodwillKin));
        Assert.Equal(+5, session.State.Social.ReactionForNpc(session.State.Npcs.Require(liveLateGoodwillKin)).ActorDisposition);
        Assert.Equal(0, session.SocialConsequences.EffectiveDispositionDelta(liveUnrelated));
        Assert.Equal(0, session.State.Social.ReactionForNpc(session.State.Npcs.Require(liveUnrelated)).ActorDisposition);

        // 4. Restore from the save captured BEFORE admission, then admit the relatives for the first time -> grudge & goodwill survive save/load!
        using DaggerfallSession restored = fixture.Restore(saved);
        long restoredGrudgeKin = AddNpcWithFamily(restored, site.Id, localSite, "r11103/grudge-late-kin", "Bryn Moss", grudgeHousehold);
        long restoredGoodwillKin = AddNpcWithFamily(restored, site.Id, localSite, "r11103/goodwill-late-kin", "Sera Fen", goodwillHousehold);
        long restoredUnrelated = AddNpcWithFamily(restored, site.Id, localSite, "r11103/unrelated-late", "Corin Hale", unrelatedHousehold);

        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty, restored.SocialConsequences.EffectiveDispositionDelta(restoredGrudgeKin));
        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty, restored.State.Social.ReactionForNpc(restored.State.Npcs.Require(restoredGrudgeKin)).ActorDisposition);
        Assert.Equal(+5, restored.SocialConsequences.EffectiveDispositionDelta(restoredGoodwillKin));
        Assert.Equal(+5, restored.State.Social.ReactionForNpc(restored.State.Npcs.Require(restoredGoodwillKin)).ActorDisposition);
        Assert.Equal(0, restored.SocialConsequences.EffectiveDispositionDelta(restoredUnrelated));
        Assert.False(restored.State.ActorLedger.TryGet(restoredGrudgeKin, out _));
        Assert.False(restored.State.ActorLedger.TryGet(restoredGoodwillKin, out _));
        Assert.False(restored.State.ActorLedger.TryGet(restoredUnrelated, out _));
    }

    private static long AddNpcWithFamily(
        DaggerfallSession session,
        DaggerfallSiteId siteId,
        DaggerfallNpcSite binding,
        string slotId,
        string displayName,
        string? familyId)
    {
        DaggerfallWorldProfileKey profile = session.Sites.ActiveProfile;
        string slotKey = $"{profile.LogicalId}/{slotId}";
        long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, slotKey);
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, Math.Abs(slotId.GetHashCode()), 0);
        session.State.Npcs.RegisterDeterministic(
            id, DaggerfallNpcKind.Static, slotId, binding, look, "person", ["talk"],
            displayName: displayName, homeProfile: profile, familyId: familyId);
        DaggerfallNpc npc = session.State.Npcs.Require(id);
        WorldPoint pose = new(2, 0, -2);
        session.Roster.MaterializeDeterministicCivilian(
            npc,
            ActorOrigin.Seeded(profile, slotId),
            pose,
            new ActorPose(pose, 0f),
            Vector3.Zero,
            familyId);
        session.State.Npcs.Bind(id, profile);
        return id;
    }

    private sealed class HouseholdSessionFixture : IDisposable
    {
        private readonly DaggerfallSiteProfile _profile;
        private readonly List<string> _releases = [];
        private readonly ContentFake _content;
        private readonly SpatialFake _spatial;
        private readonly IRandomService? _random;

        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal DaggerfallSession Session { get; }

        internal HouseholdSessionFixture(IRandomService? random = null)
        {
            _random = random;
            _profile = ReadInputs(TestData.RepositoryRoot);
            _content = new ContentFake(_releases);
            PopulateContent(_content, _profile);
            _spatial = SpatialFake.Create(_profile.Geometry.Spatial[0].Sha256, _releases);
            EngineContextFake engine = EngineContextFake.Create(_content, _spatial.Service, new AppearanceFake(_releases), Perception.Service, random: _random);
            Session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, _profile, DaggerfallTuning.Defaults));
        }

        internal DaggerfallSession Restore(RulesetSavePayload save)
        {
            EngineContextFake engine = EngineContextFake.Create(_content, _spatial.Service, new AppearanceFake(_releases), Perception.Service, random: _random);
            return DaggerfallSession.Restore(engine.Context, new(TestPayload.Definitions, _profile, DaggerfallTuning.Defaults), save);
        }

        public void Dispose() => Session.Dispose();
    }
}
