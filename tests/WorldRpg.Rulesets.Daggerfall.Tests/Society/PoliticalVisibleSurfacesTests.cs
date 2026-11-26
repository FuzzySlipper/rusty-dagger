using System.Text;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Political layer A6 (#11051): WorldCondition seam driving town notice boards (classic sign rumours, bounties,
/// decrees), expedition band staging, gate/service scrutiny, and faction-aware talk &amp; barks.
/// </summary>
public sealed class PoliticalVisibleSurfacesTests
{
    private const int DaggerfallRegion = 17;

    [Fact]
    public void Untouched_world_writes_zero_condition_bytes_and_notice_board_shows_classic_sign_rumors_bounties_and_office_notices()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // 1. Untouched world persists zero WorldCondition tokens.
        Assert.Equal(0, session.WorldConditions.Count);
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Empty(decoded.WorldConditions.Tokens);

        // 2. Notice board renders classic IsSignMessage sign rumours immediately with zero conditions.
        DaggerfallTownNoticeBoardView? board = session.ReadTownNoticeBoard();
        Assert.NotNull(board);
        Assert.Contains(board!.Entries, entry => entry.Kind == DaggerfallNoticeEntryKind.SignRumor && !string.IsNullOrWhiteSpace(entry.Body));
        Assert.DoesNotContain(board.Entries, entry => entry.Kind == DaggerfallNoticeEntryKind.Bounty);

        // 3. Wanted player + deposed ruler immediately surface as Bounty & OfficeNotice entries on the town board!
        session.State.Social.ChangeRegionalReputation(DaggerfallRegion, -20);
        session.OfficeLedger.SetBoardSeatHolder(
            seatId: "daggerfall",
            holderActorId: null,
            holderCastId: "lord-bridwell",
            holderFactionId: 367,
            holderDisplayName: "Regent Bridwell",
            cause: DaggerfallOfficeChangeCause.CardMove,
            changedDay: 2,
            causeDetail: "was deposed in a palace coup");

        DaggerfallTownNoticeBoardView updated = session.ReadTownNoticeBoard()!;
        Assert.Contains(updated.Entries, e => e.Kind == DaggerfallNoticeEntryKind.Bounty && e.Body.Contains(session.State.Character.Identity.Name, StringComparison.Ordinal));
        Assert.Contains(updated.Entries, e => e.Kind == DaggerfallNoticeEntryKind.OfficeNotice && e.Body.Contains("Regent Bridwell", StringComparison.Ordinal) && e.Body.Contains("King Gothryd", StringComparison.Ordinal));

        // 4. Talking to a guild clerk offers the live "notice-board" topic!
        Assert.True(session.TryTransitionTo(fixture.MagesProfile.ProfileKey));
        session.State.Social.ChangeRegionalReputation(DaggerfallRegion, 25); // back to neutral so talk opens cleanly
        DaggerfallNpc talkNpc = session.State.Npcs.All.First(npc =>
            npc.Profile == fixture.MagesProfile.ProfileKey
            && npc.Services.Contains("talk", StringComparer.Ordinal)
            && npc.Services.Contains("quests", StringComparer.Ordinal));
        DaggerfallActivationOutcome opened = ActivateNpc(session, talkNpc.DurableId);
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView view = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains(view.Topics, t => t.Id == "notice-board");

        DaggerfallActivationOutcome boardReply = session.Dialogue.Ask(view.Revision, "notice-board", null, null);
        Assert.True(boardReply.Applied, boardReply.Message);
        Assert.Contains("Notice Board", boardReply.Message, StringComparison.Ordinal);
        Assert.Contains("Regent Bridwell", boardReply.Message, StringComparison.Ordinal);
        session.Dialogue.Close();
    }

    [Fact]
    public void World_conditions_drive_notice_board_decrees_bounties_scarcity_prices_external_seam_and_save_reload()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(6 * 60 * 60);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("mercantile")).BaseValue = 50;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 50;

        // Baseline shop buy quote before GrainShortage.
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
        long merchantId = session.State.Npcs.RegisterStable(
            DaggerfallNpcKind.Static,
            "a6-grain-merchant",
            npcSite,
            new("breton", "Male", 182, 0, 7, DaggerfallConcreteGuildCatalog.MagesFactionId),
            "merchant",
            ["shop"]);
        DaggerfallServiceProvider provider = new(merchantId, npcSite, "shop");
        DaggerfallMerchantProviderContext merchantCtx = new(provider, Quality: 10, BuildingType: 13, BlockX: 1, BlockY: 1, BuildingIndex: 0);
        DaggerfallMerchantView baselineShop = session.State.Merchants.Read(merchantCtx);
        DaggerfallMerchantItemView stockItem = baselineShop.Stock.First(item => item.UnitPrice >= 20);
        ulong baselinePrice = stockItem.UnitPrice;

        // Add stubbed/authored WorldCondition tokens: MonsterSurge (undead) and GrainShortage (severity 2).
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            TokenId: DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, "undead"),
            Kind: DaggerfallWorldConditionKind.MonsterSurge,
            Region: DaggerfallRegion,
            SourceFactionId: DaggerfallConcreteGuildCatalog.FightersFactionId,
            SubjectTag: "undead",
            Severity: 2,
            StartedDay: 0,
            ExpiresDay: 10));
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            TokenId: DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.GrainShortage, DaggerfallRegion),
            Kind: DaggerfallWorldConditionKind.GrainShortage,
            Region: DaggerfallRegion,
            Severity: 2,
            StartedDay: 0,
            ExpiresDay: 10));

        // Scarcity raises regional shop quotes cleanly!
        DaggerfallMerchantView shortageShop = session.State.Merchants.Read(merchantCtx);
        ulong shortagePrice = shortageShop.Stock.Single(item => item.Key == stockItem.Key).UnitPrice;
        Assert.True(shortagePrice > baselinePrice,
            $"Expected GrainShortage buy total ({shortagePrice}) to exceed baseline ({baselinePrice}).");

        // External provider seam (for B2 board game) merges live derived tokens cleanly alongside authored tokens.
        session.WorldConditions.BindExternalSource(region => region == DaggerfallRegion
            ? [new DaggerfallWorldConditionToken("b2:turn-1:decree:17", DaggerfallWorldConditionKind.Decree, DaggerfallRegion,
                SourceFactionId: 201, Severity: 1, StartedDay: 0, Headline: "Royal Levy", Body: "Daggerfall calls levies to the Betony border.")]
            : []);

        DaggerfallTownNoticeBoardView board = session.ReadTownNoticeBoard()!;
        Assert.Contains(board.Entries, e => e.Kind == DaggerfallNoticeEntryKind.Bounty && e.Body.Contains("undead", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(board.Entries, e => e.Kind == DaggerfallNoticeEntryKind.Decree && e.Heading.Contains("Grain Shortage", StringComparison.Ordinal));
        Assert.Contains(board.Entries, e => e.Kind == DaggerfallNoticeEntryKind.Decree && e.Heading == "Royal Levy");

        // Authored tokens survive save/reload (external B2 tokens are not duplicated into authored save bytes) and prune on expiry.
        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(saved);
        Assert.Equal(2, decoded.WorldConditions.Tokens.Length);

        using DaggerfallSession restored = fixture.Restore(saved);
        Assert.Equal(2, restored.WorldConditions.Count);
        Assert.True(restored.WorldConditions.HasActive(DaggerfallRegion, DaggerfallWorldConditionKind.MonsterSurge, currentDay: 5));
        Assert.Equal(2, restored.WorldConditions.PruneExpired(currentDay: 10));
        Assert.Equal(0, restored.WorldConditions.Count);
    }

    [Fact]
    public void Expedition_bands_stage_from_world_conditions_with_matching_published_mobiles_and_condition_tags()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        DaggerfallDefinitions defs = TestPayload.Definitions;
        DaggerfallTravelPolicy policy = session.Expedition.Policy;
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;
        (double X, double Y) party = (charing.MapPixelX + 0.5, charing.MapPixelY + 0.5);

        // Find a valid non-ocean, non-location bearing/distance from Charing.
        double chosenBearing = 0;
        double chosenDistance = 3.0;
        bool foundLand = false;
        for (int b = 0; b < 360 && !foundLand; b += 15)
        {
            double rad = b * Math.PI / 180d;
            DaggerfallTravelMapPixel px = new((int)Math.Floor(party.X + Math.Sin(rad) * chosenDistance),
                (int)Math.Floor(party.Y - Math.Cos(rad) * chosenDistance));
            if (px.IsInBounds && !policy.IsOcean(px) && !defs.Locations.Records.Any(r => r.MapPixelX == px.X && r.MapPixelY == px.Y))
            {
                chosenBearing = b;
                foundLand = true;
            }
        }
        Assert.True(foundLand);

        DaggerfallExpeditionBands bands = new(policy, defs.Encounters, defs.Mobiles, DaggerfallBandTuning.Default,
            px => defs.Locations.Records.Any(r => r.MapPixelX == px.X && r.MapPixelY == px.Y));
        long now = DaggerfallCalendar.Start.ToAbsoluteSeconds() + 12 * 3600;
        Func<string, int, int, int> lowRoll = (_, min, _) => min;

        // 1. Undead MonsterSurge stages an Undead band with ConditionTokenId and published Undead mobile!
        DaggerfallWorldConditionToken undeadSurge = new(
            "cond:monster-surge:r17:undead", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion,
            SubjectTag: "undead", Severity: 2, StartedDay: 0);
        var (undeadBand, undeadErr) = bands.StageAt(now, party, playerLevel: 5, lowRoll, chosenDistance, chosenBearing, camp: false, [undeadSurge]);
        Assert.Null(undeadErr);
        Assert.NotNull(undeadBand);
        Assert.Equal(DaggerfallBandKind.Undead, undeadBand!.Kind);
        Assert.Equal(undeadSurge.TokenId, undeadBand.ConditionTokenId);
        Assert.Contains("Undead Surge", undeadBand.Name, StringComparison.Ordinal);
        Assert.True(defs.Mobiles.Mobiles[undeadBand.Choice.MobileId!.Value].IsPublished);

        // 2. Orc MonsterSurge stages a published Orc band!
        DaggerfallWorldConditionToken orcSurge = new(
            "cond:monster-surge:r17:orcs", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion,
            SubjectTag: "orcs", Severity: 2, StartedDay: 0);
        var (orcBand, orcErr) = bands.StageAt(now + 100, party, playerLevel: 5, lowRoll, chosenDistance, chosenBearing, camp: false, [orcSurge]);
        Assert.Null(orcErr);
        Assert.NotNull(orcBand);
        Assert.Equal(orcSurge.TokenId, orcBand!.ConditionTokenId);
        Assert.Contains("Orc Surge", orcBand.Name, StringComparison.Ordinal);
        Assert.Contains("Orc", defs.Mobiles.Mobiles[orcBand.Choice.MobileId!.Value].DonorName, StringComparison.OrdinalIgnoreCase);

        // 3. BorderTension stages a Border Patrol band!
        DaggerfallWorldConditionToken borderTension = new(
            "cond:border-tension:r17", DaggerfallWorldConditionKind.BorderTension, DaggerfallRegion, Severity: 2, StartedDay: 0);
        var (patrolBand, patrolErr) = bands.StageAt(now + 200, party, playerLevel: 5, lowRoll, chosenDistance, chosenBearing, camp: false, [borderTension]);
        Assert.Null(patrolErr);
        Assert.NotNull(patrolBand);
        Assert.Equal(DaggerfallBandKind.Patrol, patrolBand!.Kind);
        Assert.Equal(borderTension.TokenId, patrolBand.ConditionTokenId);
        Assert.Contains("Border Patrol", patrolBand.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate_and_service_scrutiny_reacts_to_standing_conditions_guild_embargoes_and_exemptions()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.MagesProfile);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;
        DaggerfallNpc magesProvider = session.State.Npcs.All.First(npc =>
            npc.Profile == fixture.MagesProfile.ProfileKey
            && npc.Services.Contains("training", StringComparer.Ordinal));

        // 1. Baseline Normal scrutiny: regional rep -7 is NOT wanted (< -10) and provider serves normally.
        session.State.Social.ChangeRegionalReputation(DaggerfallRegion, -7);
        DaggerfallGateScrutinyAssessment normalGate = session.Scrutiny.EvaluateGateScrutiny(DaggerfallRegion, charing);
        Assert.Equal(DaggerfallScrutinyLevel.Normal, normalGate.Level);
        Assert.False(normalGate.WatchChallengeActive);
        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(
            new DaggerfallServiceProvider(magesProvider.DurableId, magesProvider.Site, "training")));

        // 2. Strict CrimeCrackdown tightens wanted threshold to 0 -> rep -7 now draws gate watch scrutiny, Law.CheckStanding, AND lawful service denial!
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.CrimeCrackdown, DaggerfallRegion),
            DaggerfallWorldConditionKind.CrimeCrackdown,
            DaggerfallRegion,
            Severity: 2,
            StartedDay: 0));

        DaggerfallGateScrutinyAssessment strictGate = session.Scrutiny.EvaluateGateScrutiny(DaggerfallRegion, charing);
        Assert.Equal(DaggerfallScrutinyLevel.Strict, strictGate.Level);
        Assert.True(strictGate.WatchChallengeActive);
        Assert.Equal(0, strictGate.EffectiveWantedThreshold);
        Assert.True(strictGate.WatchDetectionChancePercent > 5);
        Assert.Equal(DaggerfallServiceDenial.UnderScrutiny, session.State.Services.ProviderAvailable(
            new DaggerfallServiceProvider(magesProvider.DurableId, magesProvider.Site, "training")));
        // Law.CheckStanding enforces the tightened scrutiny threshold over elapsed minutes!
        session.Law.CheckStanding(0, 60);
        Assert.NotNull(session.State.Crime.Response(DaggerfallRegion));

        // 3. Trusted vouch for the provider exempts them from condition scrutiny, and a ruler vouch exempts the gates!
        ActorLedgerEntry priorEntry = session.State.ActorLedger.TryGet(magesProvider.DurableId, out ActorLedgerEntry? e) && e is not null
            ? e
            : new ActorLedgerEntry(magesProvider.DurableId, ActorOrigin.Seeded(fixture.MagesProfile.ProfileKey, magesProvider.StableKey ?? "slot"));
        ActorSocialDiff vouchedSocial = (priorEntry.Social ?? ActorSocialDiff.FromNpc(magesProvider)) with
        {
            Vouches = [new ActorVouchRecord(DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd"), 201, ActorVouchKind.Court, 0, "King Gothryd")],
        };
        session.State.ActorLedger.Upsert(priorEntry with { Social = vouchedSocial });
        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(
            new DaggerfallServiceProvider(magesProvider.DurableId, magesProvider.Site, "training")));
        DaggerfallGateScrutinyAssessment vouchedGate = session.Scrutiny.EvaluateGateScrutiny(DaggerfallRegion, charing);
        Assert.True(vouchedGate.PlayerExempt);
        Assert.False(vouchedGate.WatchChallengeActive);

        // 4. GuildEmbargo on Mages Guild (40) denies an unvouched non-member and admits a Mages Guild member!
        session.WorldConditions.Clear();
        session.State.Social.ChangeRegionalReputation(DaggerfallRegion, 7); // neutral regional rep
        session.State.ActorLedger.Upsert(priorEntry with { Social = ActorSocialDiff.FromNpc(magesProvider) });
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.GuildEmbargo, DaggerfallRegion, "mages"),
            DaggerfallWorldConditionKind.GuildEmbargo,
            DaggerfallRegion,
            SourceFactionId: DaggerfallConcreteGuildCatalog.MagesFactionId,
            Severity: 1,
            StartedDay: 0));
        Assert.Equal(DaggerfallServiceDenial.UnderScrutiny, session.State.Services.ProviderAvailable(
            new DaggerfallServiceProvider(magesProvider.DurableId, magesProvider.Site, "training")));

        session.State.Social.JoinGuild(DaggerfallConcreteGuildCatalog.MagesFactionId, 0);
        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(
            new DaggerfallServiceProvider(magesProvider.DurableId, magesProvider.Site, "training")));
    }

    [Fact]
    public void Faction_aware_talk_and_barks_react_to_guild_membership_standing_guard_scrutiny_and_world_conditions()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.MagesProfile);
        DaggerfallNpc magesNpc = session.State.Npcs.All.First(npc =>
            npc.Profile == fixture.MagesProfile.ProfileKey
            && npc.Services.Contains("talk", StringComparer.Ordinal)
            && npc.Services.Contains("training", StringComparer.Ordinal));

        // 1. Faction-aware talk: joining Mages Guild triggers shared-faction camaraderie in greeting!
        session.State.Social.JoinGuild(DaggerfallConcreteGuildCatalog.MagesFactionId, 0);
        DaggerfallActivationOutcome memberTalk = ActivateNpc(session, magesNpc.DurableId);
        Assert.True(memberTalk.Applied, memberTalk.Message);
        DaggerfallDialogueView memberView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains("Mages Guild", memberView.Greeting, StringComparison.Ordinal);
        session.Dialogue.Close();

        // 2. Active WorldCondition surfaces in "Any news?" dialogue!
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.GrainShortage, DaggerfallRegion),
            DaggerfallWorldConditionKind.GrainShortage,
            DaggerfallRegion,
            Severity: 2,
            StartedDay: 0,
            Headline: "Grain Shortage",
            Body: "Grain carts into Daggerfall have been halved by border raids."));
        Assert.True(ActivateNpc(session, magesNpc.DurableId).Applied);
        DaggerfallDialogueView newsView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        DaggerfallActivationOutcome newsReply = session.Dialogue.Ask(newsView.Revision, "news", null, null);
        Assert.True(newsReply.Applied, newsReply.Message);
        Assert.False(string.IsNullOrWhiteSpace(newsReply.Message));
        session.Dialogue.Close();

        // 3. Guard scrutiny in dialogue + faction/condition-aware AmbientBark!
        Assert.True(session.TryTransitionTo(fixture.ExteriorProfile.ProfileKey));
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;
        DaggerfallLocalOffice watchCaptain = session.OfficeLedger.ResolveWatchCaptain(charing);
        DaggerfallNpc guardNpc = session.State.Npcs.Require(watchCaptain.HolderActorId!.Value);
        ActorState guardActor = session.State.Actors.Get(guardNpc.DurableId);

        session.WorldConditions.Clear();
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.CrimeCrackdown, DaggerfallRegion),
            DaggerfallWorldConditionKind.CrimeCrackdown,
            DaggerfallRegion,
            Severity: 2,
            StartedDay: 0));

        // Guard greets with active crackdown scrutiny warning when FullDialogue!
        ActorInteractionPolicy interactionPolicy = guardActor.Actor.Get<ActorInteractionPolicy>();
        ActorInteractionPolicyKind originalPolicy = interactionPolicy.Policy;
        interactionPolicy.Set(ActorInteractionPolicyKind.FullDialogue);
        DaggerfallActivationOutcome guardTalk = ActivateNpc(session, guardNpc.DurableId);
        Assert.True(guardTalk.Applied, guardTalk.Message);
        DaggerfallDialogueView guardView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains("crackdown", guardView.Greeting, StringComparison.OrdinalIgnoreCase);
        session.Dialogue.Close();

        // Guard as AmbientBark emits condition-aware crackdown bark mentioning Daggerfall!
        interactionPolicy.Set(ActorInteractionPolicyKind.AmbientBark);
        DaggerfallActivationOutcome crackdownBark = ActivateNpc(session, guardNpc.DurableId);
        Assert.True(crackdownBark.Applied, crackdownBark.Message);
        Assert.Contains("Daggerfall", crackdownBark.Message, StringComparison.Ordinal);

        // Clear conditions -> city-watch guard emits faction-aware "city-watch" bark from barks.json!
        session.WorldConditions.Clear();
        DaggerfallActivationOutcome watchBark = ActivateNpc(session, guardNpc.DurableId);
        Assert.True(watchBark.Applied, watchBark.Message);
        Assert.True(
            TestPayload.Definitions.People.Barks.ByFaction["city-watch"]
                .Any(line => watchBark.Message.Contains(line, StringComparison.Ordinal)),
            $"Expected city-watch faction bark, got: {watchBark.Message}");
        interactionPolicy.Set(originalPolicy);
    }

    [Fact]
    public void Authored_start_pack_world_conditions_seed_fresh_game_and_drive_notice_board_prices_scrutiny_and_talk()
    {
        // Real daggerfall.new-game-start pack payload authoring worldConditions (zero test-only WorldConditions.Set calls!).
        byte[] startPackJson = Encoding.UTF8.GetBytes("""
            {
              "ruleset": "daggerfall",
              "start": { "profile": "17/4/exterior", "position": [312.4, 0, -362.8], "yawRadians": 0 },
              "gold": 5000,
              "quests": [],
              "startHour": 9,
              "quickEntry": true,
              "worldConditions": [
                {
                  "tokenId": "scenario:undead-surge:17",
                  "kind": "monster-surge",
                  "region": 17,
                  "sourceFactionId": 41,
                  "subjectTag": "undead",
                  "severity": 2,
                  "startedDay": 0,
                  "expiresDay": 21
                },
                {
                  "tokenId": "scenario:grain-shortage:17",
                  "kind": "grain-shortage",
                  "region": 17,
                  "severity": 2,
                  "startedDay": 0,
                  "expiresDay": 14
                },
                {
                  "tokenId": "scenario:crackdown:17",
                  "kind": "crime-crackdown",
                  "region": 17,
                  "severity": 2,
                  "startedDay": 0
                }
              ]
            }
            """);

        DaggerfallNewGameDefinition authoredNewGame = DaggerfallBaseContent.ReadNewGameStart(
            startPackJson, TestPayload.Definitions.NewGame, "daggerfall.political-crisis-start");
        Assert.Equal(3, authoredNewGame.WorldConditions.Length);

        DaggerfallDefinitions scenarioDefs = TestPayload.Definitions.WithNewGame(authoredNewGame);
        using var fixture = SourceBackedGuildBankSessionFixture.Create();

        // Compare baseline quote (no start-pack conditions) against authored scenario start quote.
        ulong baselinePrice;
        using (DaggerfallSession baseline = fixture.Start(fixture.ExteriorProfile))
        {
            baseline.AdvanceElapsedTime(6 * 60 * 60);
            baseline.State.Actors.Player.Stats.GetStat(StatId.Parse("mercantile")).BaseValue = 50;
            baseline.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 50;
            DaggerfallSiteRecord baseSite = baseline.Site.ActiveSite!;
            DaggerfallNpcSite baseNpcSite = new(baseSite.Id.Region, baseSite.Name, string.Empty, baseline.Sites.ActiveProfile.LogicalId);
            long baseMerchant = baseline.State.Npcs.RegisterStable(
                DaggerfallNpcKind.Static, "r2-base-merchant", baseNpcSite,
                new("breton", "Male", 182, 0, 7, DaggerfallConcreteGuildCatalog.MagesFactionId), "merchant", ["shop"]);
            DaggerfallMerchantView baseView = baseline.State.Merchants.Read(new(new(baseMerchant, baseNpcSite, "shop"), 10, 13, 1, 1, 0));
            baselinePrice = baseView.Stock.First(item => item.UnitPrice >= 20).UnitPrice;
        }

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, fixture.ExteriorProfile);
        PopulateContent(content, fixture.MagesProfile);
        SpatialFake spatial = SpatialFake.Create(fixture.ExteriorProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(scenarioDefs, fixture.ExteriorProfile, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([fixture.ExteriorProfile, fixture.MagesProfile]),
        };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.AdvanceElapsedTime(8 * 60 * 60);

        // 1. Authored start-pack tokens are live immediately without any test Set call.
        Assert.Equal(3, session.WorldConditions.Count);
        DaggerfallTownNoticeBoardView board = session.ReadTownNoticeBoard()!;
        Assert.Contains(board.Entries, e => e.Kind == DaggerfallNoticeEntryKind.Bounty && e.ConditionTokenId == "scenario:undead-surge:17");
        Assert.Contains(board.Entries, e => e.Kind == DaggerfallNoticeEntryKind.Decree && e.ConditionTokenId == "scenario:grain-shortage:17");
        Assert.Contains(board.Entries, e => e.Kind == DaggerfallNoticeEntryKind.Decree && e.ConditionTokenId == "scenario:crackdown:17");

        // 2. Authored GrainShortage raises merchant buy quotes cleanly.
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("mercantile")).BaseValue = 50;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 50;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
        long merchantId = session.State.Npcs.RegisterStable(
            DaggerfallNpcKind.Static, "r2-base-merchant", npcSite,
            new("breton", "Male", 182, 0, 7, DaggerfallConcreteGuildCatalog.MagesFactionId), "merchant", ["shop"]);
        DaggerfallMerchantView scenarioShop = session.State.Merchants.Read(new(new(merchantId, npcSite, "shop"), 10, 13, 1, 1, 0));
        ulong scenarioPrice = scenarioShop.Stock.First(item => item.UnitPrice >= 20).UnitPrice;
        Assert.True(scenarioPrice > baselinePrice, $"Expected authored GrainShortage price ({scenarioPrice}) > baseline ({baselinePrice}).");

        // 3. Authored CrimeCrackdown tightens gate scrutiny and drives guard talk immediately!
        session.State.Social.ChangeRegionalReputation(DaggerfallRegion, -6);
        DaggerfallGateScrutinyAssessment gate = session.Scrutiny.EvaluateGateScrutiny(DaggerfallRegion, site);
        Assert.Equal(DaggerfallScrutinyLevel.Strict, gate.Level);
        Assert.True(gate.WatchChallengeActive);

        DaggerfallLocalOffice watchCaptain = session.OfficeLedger.ResolveWatchCaptain(site);
        ActorState guardActor = session.State.Actors.Get(watchCaptain.HolderActorId!.Value);
        guardActor.Actor.Get<ActorInteractionPolicy>().Set(ActorInteractionPolicyKind.AmbientBark);
        DaggerfallActivationOutcome bark = ActivateNpc(session, watchCaptain.HolderActorId.Value);
        Assert.True(bark.Applied, bark.Message);
        Assert.Contains("Daggerfall", bark.Message, StringComparison.Ordinal);

        // 4. Authored tokens survive CaptureSave / Restore cleanly!
        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saved);
        Assert.Equal(3, restored.WorldConditions.Count);
        Assert.True(restored.WorldConditions.HasActive(DaggerfallRegion, DaggerfallWorldConditionKind.MonsterSurge, currentDay: 1));
    }

    [Fact]
    public void Developer_world_condition_commands_author_inspect_and_clear_tokens_driving_live_consumers()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.MagesProfile);

        // Resolve the real registered developer command module from session.DebugCommands (zero test-only Set calls!).
        IDeveloperWorldConditionCommands commands = Assert.Single(
            session.DebugCommands.OfType<IDeveloperWorldConditionCommands>());

        DaggerfallNpc magesProvider = session.State.Npcs.All.First(npc =>
            npc.Profile == fixture.MagesProfile.ProfileKey
            && npc.Services.Contains("training", StringComparer.Ordinal));
        DaggerfallServiceProvider trainingProvider = new(magesProvider.DurableId, magesProvider.Site, "training");
        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(trainingProvider));

        // 1. Author stubbed conditions via world.condition-set (guild-embargo on 40 + monster-surge undead).
        DebugCommandResult embargoResult = commands.SetCondition("guild-embargo", 1, "40", 0);
        Assert.True(embargoResult.Succeeded, embargoResult.Message);
        Assert.Contains("guild-embargo", embargoResult.Message, StringComparison.Ordinal);

        DebugCommandResult surgeResult = commands.SetCondition("monster-surge", 2, "undead", 7);
        Assert.True(surgeResult.Succeeded, surgeResult.Message);
        Assert.Contains("monster-surge", surgeResult.Message, StringComparison.Ordinal);

        // 2. Real consumers react immediately: Mages Guild service denied, notice board updated, list JSON reflects both!
        Assert.Equal(DaggerfallServiceDenial.UnderScrutiny, session.State.Services.ProviderAvailable(trainingProvider));
        DebugCommandResult boardResult = commands.NoticeBoard();
        Assert.True(boardResult.Succeeded, boardResult.Message);
        Assert.Contains("Embargo", boardResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Undead", boardResult.Message, StringComparison.OrdinalIgnoreCase);

        DebugCommandResult listed = commands.ListConditions();
        Assert.True(listed.Succeeded, listed.Message);
        Assert.Contains("cond:guild-embargo:r17:40", listed.Message, StringComparison.Ordinal);
        Assert.Contains("cond:monster-surge:r17:undead", listed.Message, StringComparison.Ordinal);

        // 3. Clearing via world.condition-clear restores baseline immediately!
        DebugCommandResult cleared = commands.ClearCondition("all");
        Assert.True(cleared.Succeeded, cleared.Message);
        Assert.Equal(0, session.WorldConditions.Count);
        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(trainingProvider));
    }

    private static DaggerfallActivationOutcome ActivateNpc(DaggerfallSession session, long npcId)
    {
        DaggerfallActivationTarget target = Assert.Single(
            session.Dialogue.NpcTargets(),
            t => checked((long)t.Identity.Value) == npcId);
        return session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, target));
    }
}
