using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11160: All three blood-feud &amp; household-grudge settlement paths:
/// 1. Exponential wergild / blood money (via kin or neutral temple/court arbiter across all tiers).
/// 2. Intermediary truce mediation unlocking direct talks with a feuding household.
/// 3. Reconciliation service and formal court/temple pardon reducing (never removing) exponential wergild.
/// </summary>
public sealed class BloodFeudSettlementTests
{
    [Fact]
    public void Wergild_scales_moderately_for_minor_grudges_and_exponentially_for_murder_feuds_across_all_tiers()
    {
        DaggerfallSocialTuning tuning = DaggerfallSocialTuning.Default;

        // 1. Minor non-lethal grudges scale moderately by severity and tier.
        ulong localMinor = DaggerfallFeudSettlement.ComputeBaseWergild(DaggerfallHouseholdScope.Local, 5, feudActive: false, murderCount: 0, tuning);
        ulong houseMinor = DaggerfallFeudSettlement.ComputeBaseWergild(DaggerfallHouseholdScope.GreatHouse, 5, feudActive: false, murderCount: 0, tuning);
        ulong dynastyMinor = DaggerfallFeudSettlement.ComputeBaseWergild(DaggerfallHouseholdScope.Dynasty, 5, feudActive: false, murderCount: 0, tuning);
        Assert.Equal(200UL, localMinor);
        Assert.Equal(600UL, houseMinor);
        Assert.Equal(1600UL, dynastyMinor);
        Assert.True(localMinor < houseMinor && houseMinor < dynastyMinor);

        // 2. Murder blood feuds carry extraordinarily high exponential wergild at all tiers (including Great Houses & royal dynasties).
        ulong localMurder1 = DaggerfallFeudSettlement.ComputeBaseWergild(DaggerfallHouseholdScope.Local, 20, feudActive: true, murderCount: 1, tuning);
        ulong localMurder2 = DaggerfallFeudSettlement.ComputeBaseWergild(DaggerfallHouseholdScope.Local, 40, feudActive: true, murderCount: 2, tuning);
        ulong localMurder3 = DaggerfallFeudSettlement.ComputeBaseWergild(DaggerfallHouseholdScope.Local, 60, feudActive: true, murderCount: 3, tuning);

        ulong houseMurder1 = DaggerfallFeudSettlement.ComputeBaseWergild(DaggerfallHouseholdScope.GreatHouse, 20, feudActive: true, murderCount: 1, tuning);
        ulong dynastyMurder1 = DaggerfallFeudSettlement.ComputeBaseWergild(DaggerfallHouseholdScope.Dynasty, 20, feudActive: true, murderCount: 1, tuning);
        ulong dynastyMurder2 = DaggerfallFeudSettlement.ComputeBaseWergild(DaggerfallHouseholdScope.Dynasty, 40, feudActive: true, murderCount: 2, tuning);

        Assert.True(localMurder1 >= localMinor * 20UL);
        Assert.True(houseMurder1 >= houseMinor * 50UL);
        Assert.True(dynastyMurder1 >= dynastyMinor * 50UL);
        Assert.Equal(localMurder1 * 3UL, localMurder2);
        Assert.Equal(localMurder1 * 9UL, localMurder3);
        Assert.Equal(dynastyMurder1 * 3UL, dynastyMurder2);

        // 3. Paying wergild through a neutral temple/court arbiter settles even a royal dynasty murder blood feud!
        using var fixture = new SettlementSessionFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/1/0", session.Sites.ActiveProfile.LogicalId);
        const string dynasty = "fam:dynasty:daggerfall";

        long royalVictimId = AddNpc(session, site.Id, npcSite, "t11160/royal-victim", "Prince Lhotun", dynasty, "noble", ["talk"]);
        long royalKinId = AddNpc(session, site.Id, npcSite, "t11160/royal-kin", "Queen Aubk-i", dynasty, "noble", ["talk"]);
        long templeArbiterId = AddNpc(session, site.Id, npcSite, "t11160/priest", "Brother Alaric", null, "priest", ["talk", "donate"]);
        long witnessId = AddNpc(session, site.Id, npcSite, "t11160/witness", "Oren Watch", null, "guard", ["talk"]);

        CommitWitnessedMurder(session, fixture.Perception, royalVictimId, witnessId, session.Calendar.MinuteNumber);

        DaggerfallFeudSettlementQuote quote = Assert.IsType<DaggerfallFeudSettlementQuote>(session.FeudSettlement.CalculateQuote(dynasty, site));
        Assert.True(quote.FeudActive);
        Assert.Equal(1, quote.MurderCount);
        Assert.Equal((ulong)tuning.DynastyBloodFeudBaseWergild, quote.EffectiveWergildGold);

        // Without enough gold, arbiter refuses and hints at reconciliation service / pardon reductions.
        DaggerfallNpc priest = session.State.Npcs.Require(templeArbiterId);
        DaggerfallFeudSettlementResult poorResult = session.FeudSettlement.PayWergild(priest, dynasty, site);
        Assert.False(poorResult.Accepted);
        Assert.Contains("reconciliation service", poorResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pardon", poorResult.Message, StringComparison.OrdinalIgnoreCase);

        // Grant exact gold and pay via neutral arbiter in live dialogue -> feud closes cleanly!
        session.State.Currency.ReceiveGold(quote.EffectiveWergildGold);
        ulong goldBefore = session.State.Currency.Read().Gold;

        DaggerfallActivationOutcome opened = ActivateNpc(session, templeArbiterId);
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView view = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        string settleTopicId = $"settle-wergild:{dynasty}";
        Assert.Contains(view.Topics, t => t.Id == settleTopicId);

        DaggerfallActivationOutcome paid = session.Dialogue.Ask(view.Revision, settleTopicId, null, null);
        Assert.True(paid.Applied, paid.Message);
        Assert.Equal(goldBefore - quote.EffectiveWergildGold, session.State.Currency.Read().Gold);
        Assert.Equal(0, session.SocialConsequences.HouseholdStandingDelta(dynasty, site));
        Assert.False(session.SocialConsequences.IsFeudActiveAt(dynasty, site));
        Assert.False(session.Introductions.IsInBloodFeud(session.State.Npcs.Require(royalKinId), site));
    }

    [Fact]
    public void Feuding_household_refuses_unmediated_talks_with_hints_until_intermediary_brokers_truce_and_survives_save_load()
    {
        using var fixture = new SettlementSessionFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/2/0", session.Sites.ActiveProfile.LogicalId);
        string localHousehold = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 2, 0);

        long victimId = AddNpc(session, site.Id, npcSite, "t11160/local-victim", "Corin Holt", localHousehold, "person", ["talk"]);
        long kinId = AddNpc(session, site.Id, npcSite, "t11160/local-kin", "Maren Holt", localHousehold, "person", ["talk", "shop"]);
        long mediatorId = AddNpc(session, site.Id, npcSite, "t11160/mediator", "Alden Vance", null, "person", ["talk"]);
        long witnessId = AddNpc(session, site.Id, npcSite, "t11160/local-witness", "Tarin", null, "person", ["talk"]);

        CommitWitnessedMurder(session, fixture.Perception, victimId, witnessId, session.Calendar.MinuteNumber);

        // 1. Access refusal & live dialogue refusal with feuding kin name the blood feud and hint at settlement paths.
        DaggerfallNpc kin = session.State.Npcs.Require(kinId);
        DaggerfallIntroductionAccess denied = session.Introductions.AssessAccess(kin);
        Assert.False(denied.Allowed);
        Assert.Contains("blood feud", denied.DenialReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intermediary", denied.DenialReason!, StringComparison.OrdinalIgnoreCase);

        DaggerfallActivationOutcome refusedTalk = ActivateNpc(session, kinId);
        Assert.False(refusedTalk.Applied);
        Assert.Contains("blood feud", refusedTalk.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intermediary", refusedTalk.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arbiter", refusedTalk.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(session.ActivationPanel.View.Dialogue);

        // Direct wergild payment to feuding kin while unmediated is also refused with settlement hints.
        session.State.Currency.ReceiveGold(10000UL);
        DaggerfallFeudSettlementResult refusedDirect = session.FeudSettlement.PayWergild(kin, localHousehold, site);
        Assert.False(refusedDirect.Accepted);
        Assert.Contains("intermediary", refusedDirect.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Trusted ally mediates a truce in live dialogue!
        session.Introductions.AdjustActorDisposition(mediatorId, 20);
        DaggerfallActivationOutcome openedMediator = ActivateNpc(session, mediatorId);
        Assert.True(openedMediator.Applied, openedMediator.Message);
        DaggerfallDialogueView medView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        string mediateTopicId = $"mediate-truce:{localHousehold}";
        Assert.Contains(medView.Topics, t => t.Id == mediateTopicId);

        DaggerfallActivationOutcome mediated = session.Dialogue.Ask(medView.Revision, mediateTopicId, null, null);
        Assert.True(mediated.Applied, mediated.Message);
        Assert.Contains("parley", mediated.Message, StringComparison.OrdinalIgnoreCase);
        session.Dialogue.Close();

        // 3. Truce mediation persists across save/load and unlocks direct settlement dialogue with the feuding household!
        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(saved);
        long restoredKinId = AddNpc(restored, site.Id, npcSite, "t11160/local-kin", "Maren Holt", localHousehold, "person", ["talk", "shop"]);

        DaggerfallFeudSettlementQuote restoredQuote = Assert.IsType<DaggerfallFeudSettlementQuote>(
            restored.FeudSettlement.CalculateQuote(localHousehold, site));
        Assert.True(restoredQuote.TruceMediated);
        Assert.Equal("Alden Vance", restoredQuote.MediatedByName);
        Assert.False(restoredQuote.RequiresTruceForDirectTalk);

        DaggerfallActivationOutcome openedRestoredKin = ActivateNpc(restored, restoredKinId);
        Assert.True(openedRestoredKin.Applied, openedRestoredKin.Message);
        DaggerfallDialogueView truceView = Assert.IsType<DaggerfallDialogueView>(restored.ActivationPanel.View.Dialogue);
        Assert.Contains("Alden Vance", truceView.Greeting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(truceView.Topics, t => t.Id == $"reconcile-service:{localHousehold}");
        Assert.Contains(truceView.Topics, t => t.Id == $"settle-wergild:{localHousehold}");

        DaggerfallActivationOutcome settled = restored.Dialogue.Ask(truceView.Revision, $"settle-wergild:{localHousehold}", null, null);
        Assert.True(settled.Applied, settled.Message);
        Assert.False(restored.SocialConsequences.IsFeudActiveAt(localHousehold, site));
        Assert.Equal(0, restored.SocialConsequences.HouseholdStandingDelta(localHousehold, site));
    }

    [Fact]
    public void Reconciliation_service_and_court_temple_pardon_reduce_exponential_wergild_without_eliminating_it_and_clear_feud_on_payment()
    {
        using var fixture = new SettlementSessionFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/3/0", session.Sites.ActiveProfile.LogicalId);
        string greatHouse = DaggerfallHouseholds.GreatHouseId(site.Id.Region, 0);

        long victimId = AddNpc(session, site.Id, npcSite, "t11160/house-victim", "Lord Cassian", greatHouse, "noble", ["talk"]);
        long kinId = AddNpc(session, site.Id, npcSite, "t11160/house-kin", "Lady Elara", greatHouse, "noble", ["talk"]);
        long arbiterId = AddNpc(session, site.Id, npcSite, "t11160/court-arbiter", "Magistrate Corvus", null, "judge", ["talk"]);
        long witnessId = AddNpc(session, site.Id, npcSite, "t11160/house-witness", "Guard", null, "guard", ["talk"]);

        CommitWitnessedMurder(session, fixture.Perception, victimId, witnessId, session.Calendar.MinuteNumber);

        DaggerfallFeudSettlementQuote baseQuote = Assert.IsType<DaggerfallFeudSettlementQuote>(
            session.FeudSettlement.CalculateQuote(greatHouse, site));
        Assert.Equal((ulong)DaggerfallSocialTuning.Default.GreatHouseBloodFeudBaseWergild, baseQuote.BaseWergildGold);
        Assert.Equal(baseQuote.BaseWergildGold, baseQuote.EffectiveWergildGold);

        // Give player standing with arbiter & enough gold for writ fee + reduced wergild (NOT full 35,000g!).
        session.State.Social.ChangeRegionalReputation(site.Id.Region, 15);
        session.State.Currency.ReceiveGold(20000UL);

        // 1. Open dialogue with neutral court arbiter -> reconciliation service + formal pardon + wergild topics offered.
        DaggerfallActivationOutcome opened = ActivateNpc(session, arbiterId);
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView view = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains(view.Topics, t => t.Id == $"reconcile-service:{greatHouse}");
        Assert.Contains(view.Topics, t => t.Id == $"request-pardon:{greatHouse}");
        Assert.Contains(view.Topics, t => t.Id == $"settle-wergild:{greatHouse}");

        // 2. Undertake reconciliation service -> lowers wergild (35,000 -> 19,250) without eliminating the feud!
        long minuteBeforeService = session.Calendar.MinuteNumber;
        DaggerfallActivationOutcome serviceOutcome = session.Dialogue.Ask(view.Revision, $"reconcile-service:{greatHouse}", null, null);
        Assert.True(serviceOutcome.Applied, serviceOutcome.Message);
        Assert.True(session.Calendar.MinuteNumber > minuteBeforeService);
        Assert.True(session.SocialConsequences.IsFeudActiveAt(greatHouse, site));

        DaggerfallFeudSettlementQuote afterService = Assert.IsType<DaggerfallFeudSettlementQuote>(
            session.FeudSettlement.CalculateQuote(greatHouse, site));
        Assert.True(afterService.ReconciliationServiceCompleted);
        Assert.False(afterService.PardonGranted);
        Assert.True(afterService.EffectiveWergildGold < baseQuote.BaseWergildGold);
        Assert.True(afterService.EffectiveWergildGold > 0UL);

        // 3. Request formal court pardon -> deducts modest writ fee and further reduces wergild (19,250 -> 12,513), still NOT free!
        DaggerfallDialogueView afterServiceView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        ulong goldBeforePardon = session.State.Currency.Read().Gold;
        DaggerfallActivationOutcome pardonOutcome = session.Dialogue.Ask(afterServiceView.Revision, $"request-pardon:{greatHouse}", null, null);
        Assert.True(pardonOutcome.Applied, pardonOutcome.Message);
        Assert.Equal(goldBeforePardon - afterService.PardonFeeGold, session.State.Currency.Read().Gold);
        Assert.True(session.SocialConsequences.IsFeudActiveAt(greatHouse, site));

        DaggerfallFeudSettlementQuote afterPardon = Assert.IsType<DaggerfallFeudSettlementQuote>(
            session.FeudSettlement.CalculateQuote(greatHouse, site));
        Assert.True(afterPardon.ReconciliationServiceCompleted);
        Assert.True(afterPardon.PardonGranted);
        Assert.Equal("Magistrate Corvus", afterPardon.PardonGrantedBy);
        Assert.True(afterPardon.EffectiveWergildGold < afterService.EffectiveWergildGold);
        Assert.True(afterPardon.EffectiveWergildGold > 0UL);
        session.Dialogue.Close();

        // 4. Verify inspection surfaces and save/load preserve the reduced quote and flags.
        IDeveloperSocialCommands socialCmds = session.Commands<IDeveloperSocialCommands>();
        DebugCommandResult regionRes = socialCmds.Region();
        Assert.Equal(DebugCommandStatus.Success, regionRes.Status);
        using (JsonDocument doc = JsonDocument.Parse(regionRes.Message))
        {
            JsonElement houseJson = doc.RootElement.GetProperty("greatHouses").EnumerateArray()
                .Single(e => e.GetProperty("familyId").GetString() == greatHouse);
            Assert.Equal(1, houseJson.GetProperty("murderCount").GetInt32());
            Assert.True(houseJson.GetProperty("reconciliationServiceCompleted").GetBoolean());
            Assert.True(houseJson.GetProperty("pardonGranted").GetBoolean());
            Assert.Equal(afterPardon.EffectiveWergildGold, houseJson.GetProperty("effectiveWergildGold").GetUInt64());
        }

        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(saved);
        DaggerfallFeudSettlementQuote restoredQuote = Assert.IsType<DaggerfallFeudSettlementQuote>(
            restored.FeudSettlement.CalculateQuote(greatHouse, site));
        Assert.Equal(afterPardon.EffectiveWergildGold, restoredQuote.EffectiveWergildGold);
        Assert.True(restoredQuote.ReconciliationServiceCompleted);
        Assert.True(restoredQuote.PardonGranted);

        // 5. Pay the reduced wergild through the arbiter -> clears the feud completely!
        long restoredArbiterId = AddNpc(restored, site.Id, npcSite, "t11160/court-arbiter", "Magistrate Corvus", null, "judge", ["talk"]);
        long restoredKinId = AddNpc(restored, site.Id, npcSite, "t11160/house-kin", "Lady Elara", greatHouse, "noble", ["talk"]);
        DaggerfallActivationOutcome openedRestored = ActivateNpc(restored, restoredArbiterId);
        Assert.True(openedRestored.Applied, openedRestored.Message);
        DaggerfallDialogueView restoredView = Assert.IsType<DaggerfallDialogueView>(restored.ActivationPanel.View.Dialogue);

        DaggerfallActivationOutcome paid = restored.Dialogue.Ask(restoredView.Revision, $"settle-wergild:{greatHouse}", null, null);
        Assert.True(paid.Applied, paid.Message);
        Assert.False(restored.SocialConsequences.IsFeudActiveAt(greatHouse, site));
        Assert.Equal(0, restored.SocialConsequences.HouseholdStandingDelta(greatHouse, site));
        Assert.False(restored.Introductions.IsInBloodFeud(restored.State.Npcs.Require(restoredKinId), site));
    }

    private static void CommitWitnessedMurder(DaggerfallSession session, PerceptionFake perception, long victimId, long witnessId, long minute)
    {
        perception.Responder = _ => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witnessId), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        ActorState victim = session.State.Actors.Get(victimId);
        victim.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(victimId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            victim,
            minute);
    }

    private static DaggerfallActivationOutcome ActivateNpc(DaggerfallSession session, long npcId)
    {
        DaggerfallActivationTarget target = Assert.Single(
            session.Dialogue.NpcTargets(),
            candidate => checked((long)candidate.Identity.Value) == npcId);
        return session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, target));
    }

    private static long AddNpc(
        DaggerfallSession session,
        DaggerfallSiteId siteId,
        DaggerfallNpcSite binding,
        string slotId,
        string displayName,
        string? familyId,
        string role,
        string[] services)
    {
        DaggerfallWorldProfileKey profile = session.Sites.ActiveProfile;
        string slotKey = $"{profile.LogicalId}/{slotId}";
        long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, slotKey);
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, Math.Abs(slotId.GetHashCode()), 0);
        session.State.Npcs.RegisterDeterministic(
            id, DaggerfallNpcKind.Static, slotId, binding, look, role, services,
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

    private sealed class SettlementSessionFixture : IDisposable
    {
        private readonly DaggerfallSiteProfile _profile;
        private readonly List<string> _releases = [];
        private readonly ContentFake _content;
        private readonly SpatialFake _spatial;
        private readonly IRandomService? _random;

        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal DaggerfallSession Session { get; }

        internal SettlementSessionFixture(IRandomService? random = null)
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
