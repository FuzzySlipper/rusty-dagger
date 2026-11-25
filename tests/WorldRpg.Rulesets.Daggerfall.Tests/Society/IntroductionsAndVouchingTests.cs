using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Political layer A4 (#11049): structured vouching and social introductions.
/// Verifies Patch's Q7/Q10 requirements: prominence gating, social-skill skips (with prominent figures always
/// requiring at least one intermediary), full silver-tongued vs. blunt-fighter court ladders, regional home-standing
/// vs. cosmopolitan charm, enemy rejection, trusted-vouch trade and hire-deposit terms, idempotency, and save/reload.
/// </summary>
public sealed class IntroductionsAndVouchingTests
{
    private const int DaggerfallRegion = 17;
    private const int WayrestRegion = 23;
    private const int KnightsOfTheDragonFaction = 368; // Type=Group, GuildGroup=9, Allies=[371 Mobar], Parent=367
    private const int MobarFaction = 371;              // MinorCourt (Power=4, Parent=201 Daggerfall)
    private const int LadyBridwellFaction = 377;       // CourtFavourite (Title=Lady, Region=17)
    private const int KingGothrydFaction = 364;        // Prominent (King, Ruler of 201 Daggerfall)
    private const int LordDarkworthFaction = 397;      // CourtFavourite/Noble in Wayrest (Region=23)
    private const int KnightsOfTheRoseFaction = 409;   // Enemies=[357 Gortwog]
    private const int GortwogFaction = 357;            // Prominent ruler of Orsinium

    [Fact]
    public void Silver_tongued_thief_skips_to_court_favourite_butters_them_up_and_reaches_lord_in_one_intermediary()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = RandomMinimum.Create(); // Deterministic pass on high-chance favour roll
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // Silver-tongued thief: high Etiquette, Streetwise, Personality -> skip = 2.
        SetSocialStats(session, etiquette: 85, streetwise: 85, personality: 85);

        long ladyId = SpawnCourtNpc(session, "lady-bridwell-test", "Lady Bridwell", LadyBridwellFaction, DaggerfallRegion, ["talk", "quest"]);
        long kingId = SpawnCourtNpc(session, "king-gothryd-test", "King Gothryd", KingGothrydFaction, DaggerfallRegion, ["talk", "quest"]);

        DaggerfallNpc lady = session.State.Npcs.Require(ladyId);
        DaggerfallNpc king = session.State.Npcs.Require(kingId);

        Assert.Equal(DaggerfallSocialProminence.CourtFavourite, session.Introductions.ClassifyProminence(lady));
        Assert.Equal(DaggerfallSocialProminence.Prominent, session.Introductions.ClassifyProminence(king));

        // 1. Even a very silver-tongued character CANNOT skip straight to the Lord (0 intermediaries blocked!).
        DaggerfallIntroductionAccess kingBefore = session.Introductions.AssessAccess(king);
        Assert.False(kingBefore.Allowed);
        Assert.Equal(2, kingBefore.SocialSkillSkip);
        Assert.Contains("introduction", kingBefore.DenialReason!, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.QuestWork.HasWork(kingId));
        Assert.Contains("introduction", session.QuestWork.Offer(kingId), StringComparison.OrdinalIgnoreCase);

        // Asking the Lord for work in dialogue surfaces the concrete introduction refusal.
        OpenDialogueWith(session, kingId);
        DaggerfallDialogueView kingView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains(kingView.Topics, t => t.Id == "work");
        DaggerfallActivationOutcome kingWorkReply = session.Dialogue.Ask(kingView.Revision, "work", null, null);
        Assert.Contains("introduction", kingWorkReply.Message, StringComparison.OrdinalIgnoreCase);
        session.Dialogue.Close();

        // 2. Silver-tongued thief CAN walk straight up to 1 Court Favourite (skip = 2 covers CourtFavourite!).
        DaggerfallIntroductionAccess ladyAccess = session.Introductions.AssessAccess(lady);
        Assert.True(ladyAccess.Allowed);

        OpenDialogueWith(session, ladyId);
        DaggerfallDialogueView ladyView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains(ladyView.Topics, t => t.Id == "curry-favour");
        Assert.Contains(ladyView.Topics, t => t.Id == $"introduce:{kingId}");

        // Before buttering her up, Lady Bridwell refuses to put her name behind a stranger.
        DaggerfallActivationOutcome prematureIntro = session.Dialogue.Ask(ladyView.Revision, $"introduce:{kingId}", null, null);
        Assert.False(prematureIntro.Applied);
        Assert.Empty(session.Introductions.ReadVouches(kingId));

        // 3. Butter up Lady Bridwell with Polite tone + Pay your respects, then ask for the introduction.
        Assert.True(session.Dialogue.ChooseTone(ladyView.Revision, "polite").Applied);
        DaggerfallActivationOutcome favourOutcome = session.Dialogue.Ask(ladyView.Revision, "curry-favour", null, null);
        Assert.True(favourOutcome.Applied);
        Assert.Contains("warms", favourOutcome.Message, StringComparison.OrdinalIgnoreCase);

        // Once-per-day cap: curry-favour topic is gone after using it today.
        DaggerfallDialogueView afterFavourView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.DoesNotContain(afterFavourView.Topics, t => t.Id == "curry-favour");

        // Now Lady Bridwell gladly issues a structured Court vouch to King Gothryd!
        DaggerfallActivationOutcome introOutcome = session.Dialogue.Ask(ladyView.Revision, $"introduce:{kingId}", null, null);
        Assert.True(introOutcome.Applied);
        Assert.Contains("King Gothryd", introOutcome.Message, StringComparison.Ordinal);

        ActorVouchRecord vouch = Assert.Single(session.Introductions.ReadVouches(kingId));
        Assert.Equal(ladyId, vouch.ReferrerDurableId);
        Assert.Equal(LadyBridwellFaction, vouch.ReferrerFactionId);
        Assert.Equal(ActorVouchKind.Court, vouch.Kind);

        // Repeat ask is idempotent: no duplicate vouch record.
        DaggerfallActivationOutcome repeatIntro = session.Dialogue.Ask(ladyView.Revision, $"introduce:{kingId}", null, null);
        Assert.True(repeatIntro.Applied);
        Assert.Contains("already", repeatIntro.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(session.Introductions.ReadVouches(kingId));
        session.Dialogue.Close();

        // 4. King Gothryd now grants audience & work access through Lady Bridwell's vouch!
        DaggerfallIntroductionAccess kingAfter = session.Introductions.AssessAccess(king);
        Assert.True(kingAfter.Allowed);
        Assert.NotNull(kingAfter.QualifyingVouch);
        Assert.Equal(ladyId, kingAfter.QualifyingVouch!.ReferrerDurableId);

        OpenDialogueWith(session, kingId);
        DaggerfallDialogueView kingGreetView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains("Lady Bridwell", kingGreetView.Greeting, StringComparison.Ordinal);
        session.Dialogue.Close();
    }

    [Fact]
    public void Blunt_fighter_climbs_knightly_order_to_court_nobody_to_favourite_to_lord_chain()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        // Roll = 100 guarantees low-skill charm fails so the fighter cannot talk their way past anyone.
        fixture.Random = KeyedRandomFake.Create(100).Service;
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // Blunt fighter: low social skills -> skip = 0.
        SetSocialStats(session, etiquette: 15, streetwise: 15, personality: 30);

        long knightId = SpawnCourtNpc(session, "knight-dragon-test", "Sir Aldric", KnightsOfTheDragonFaction, DaggerfallRegion, ["talk", "quests"]);
        long mobarId = SpawnCourtNpc(session, "mobar-test", "Mobar", MobarFaction, DaggerfallRegion, ["talk", "quest"]);
        long ladyId = SpawnCourtNpc(session, "lady-bridwell-chain", "Lady Bridwell", LadyBridwellFaction, DaggerfallRegion, ["talk", "quest"]);
        long kingId = SpawnCourtNpc(session, "king-gothryd-chain", "King Gothryd", KingGothrydFaction, DaggerfallRegion, ["talk", "quest"]);

        DaggerfallNpc knight = session.State.Npcs.Require(knightId);
        DaggerfallNpc mobar = session.State.Npcs.Require(mobarId);
        DaggerfallNpc lady = session.State.Npcs.Require(ladyId);
        DaggerfallNpc king = session.State.Npcs.Require(kingId);

        Assert.Equal(DaggerfallSocialProminence.Ordinary, session.Introductions.ClassifyProminence(knight));
        Assert.Equal(DaggerfallSocialProminence.MinorCourt, session.Introductions.ClassifyProminence(mobar));
        Assert.Equal(DaggerfallSocialProminence.CourtFavourite, session.Introductions.ClassifyProminence(lady));
        Assert.Equal(DaggerfallSocialProminence.Prominent, session.Introductions.ClassifyProminence(king));

        // Initially blocked from every rung of the court ladder (nobody, favourite, lord),
        // and unintroduced courtiers will not offer or issue introductions on your behalf.
        Assert.False(session.Introductions.AssessAccess(mobar).Allowed);
        Assert.False(session.Introductions.AssessAccess(lady).Allowed);
        Assert.False(session.Introductions.AssessAccess(king).Allowed);
        Assert.Empty(session.Introductions.ListIntroductionTargets(lady, session.Site.ActiveSite));
        Assert.False(session.Introductions.TryIssueIntroduction(lady, kingId, session.Site.ActiveSite).Accepted);

        // Step 1: Join Knights of the Dragon -> Sir Aldric vouches fighter to Mobar (court nobody), NOT enough for Lord directly.
        session.State.Social.JoinGuild(KnightsOfTheDragonFaction, 1);
        DaggerfallIntroductionResult orderToKing = session.Introductions.TryIssueIntroduction(knight, kingId, session.Site.ActiveSite);
        Assert.True(orderToKing.Accepted);
        // Even with an Order brother's vouch on the King, reach = 0 + 1 + 0 = 1 < 3 -> King STILL refuses!
        DaggerfallIntroductionAccess kingWithOrderOnly = session.Introductions.AssessAccess(king);
        Assert.False(kingWithOrderOnly.Allowed);
        Assert.Contains("higher in their court circle", kingWithOrderOnly.DenialReason!, StringComparison.OrdinalIgnoreCase);

        DaggerfallIntroductionResult orderToMobar = session.Introductions.TryIssueIntroduction(knight, mobarId, session.Site.ActiveSite);
        Assert.True(orderToMobar.Accepted);
        Assert.Equal(ActorVouchKind.Guild, orderToMobar.Vouch!.Kind);
        Assert.True(session.Introductions.AssessAccess(mobar).Allowed);
        Assert.False(session.Introductions.AssessAccess(lady).Allowed);

        // Step 2: Blunt charm fails on Mobar -> fighter builds standing with Mobar through work (disposition + rep).
        DaggerfallFavourResult failedCharm = session.Introductions.TryCurryFavour(mobar, politeTone: false, bluntTone: true);
        Assert.True(failedCharm.Applied);
        Assert.False(failedCharm.Succeeded);
        Assert.False(session.Introductions.TryIssueIntroduction(mobar, ladyId, session.Site.ActiveSite).Accepted);

        // Doing quests for Mobar raises personal disposition (+8 per quest) -> earns Mobar's vouch to Lady Bridwell!
        session.Introductions.AdjustActorDisposition(mobarId, DaggerfallIntroductions.QuestSuccessDispositionBonus * 2);
        DaggerfallIntroductionResult mobarToLady = session.Introductions.TryIssueIntroduction(mobar, ladyId, session.Site.ActiveSite);
        Assert.True(mobarToLady.Accepted);
        Assert.Equal(ActorVouchKind.Court, mobarToLady.Vouch!.Kind);
        Assert.True(session.Introductions.AssessAccess(lady).Allowed);
        Assert.False(session.Introductions.AssessAccess(king).Allowed);

        // Step 3: Build standing with Lady Bridwell through work -> earns her vouch to King Gothryd -> Lord unlocks!
        session.Introductions.AdjustActorDisposition(ladyId, DaggerfallIntroductions.QuestSuccessDispositionBonus * 2);
        DaggerfallIntroductionResult ladyToKing = session.Introductions.TryIssueIntroduction(lady, kingId, session.Site.ActiveSite);
        Assert.True(ladyToKing.Accepted);
        Assert.True(session.Introductions.AssessAccess(king).Allowed);
        Assert.Equal(ladyId, session.Introductions.AssessAccess(king).QualifyingVouch!.ReferrerDurableId);
    }

    [Fact]
    public void Cosmopolitan_charmer_skips_across_regions_while_settled_fighter_gains_home_standing_only_at_home()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        long daggerfallMobarId = SpawnCourtNpc(session, "df-mobar", "Mobar", MobarFaction, DaggerfallRegion, ["talk", "quest"]);
        long wayrestCourtId = SpawnCourtNpc(session, "wayrest-courtier", "Lord Darkworth", LordDarkworthFaction, WayrestRegion, ["talk", "quest"]);
        DaggerfallNpc daggerfallMobar = session.State.Npcs.Require(daggerfallMobarId);
        DaggerfallNpc wayrestCourt = session.State.Npcs.Require(wayrestCourtId);

        // Blunt fighter settles in Daggerfall (Region 17) and builds strong home reputation there (+30).
        SetSocialStats(session, etiquette: 15, streetwise: 15, personality: 30);
        session.State.Social.ChangeRegionalReputation(DaggerfallRegion, 30);

        DaggerfallIntroductionAccess homeAccess = session.Introductions.AssessAccess(daggerfallMobar);
        DaggerfallIntroductionAccess abroadAccess = session.Introductions.AssessAccess(wayrestCourt);
        Assert.Equal(1, homeAccess.HomeStandingBonus);
        Assert.True(homeAccess.Allowed);      // Settled home standing opens Daggerfall's minor court!
        Assert.Equal(0, abroadAccess.HomeStandingBonus);
        Assert.False(abroadAccess.Allowed);   // Abroad in Wayrest, the blunt fighter starts from scratch!

        // Cosmopolitan charmer with zero regional standing anywhere can approach both regions' courts directly.
        session.State.Social.ChangeRegionalReputation(DaggerfallRegion, -30);
        SetSocialStats(session, etiquette: 85, streetwise: 80, personality: 85);

        DaggerfallIntroductionAccess charmerDaggerfall = session.Introductions.AssessAccess(daggerfallMobar);
        DaggerfallIntroductionAccess charmerWayrest = session.Introductions.AssessAccess(wayrestCourt);
        Assert.Equal(2, charmerDaggerfall.SocialSkillSkip);
        Assert.Equal(2, charmerWayrest.SocialSkillSkip);
        Assert.True(charmerDaggerfall.Allowed);
        Assert.True(charmerWayrest.Allowed);
    }

    [Fact]
    public void Enemy_faction_vouch_is_rejected_and_grants_neither_access_nor_terms_bonus()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        SetSocialStats(session, etiquette: 20, streetwise: 20, personality: 30);

        long roseKnightId = SpawnCourtNpc(session, "rose-knight", "Sir Corvus", KnightsOfTheRoseFaction, WayrestRegion, ["talk", "quests"]);
        long gortwogId = SpawnCourtNpc(session, "gortwog-test", "Gortwog", GortwogFaction, 26, ["talk", "quest"]);
        DaggerfallNpc roseKnight = session.State.Npcs.Require(roseKnightId);
        DaggerfallNpc gortwog = session.State.Npcs.Require(gortwogId);

        session.State.Social.JoinGuild(KnightsOfTheRoseFaction, 1);

        // Knights of the Rose have Gortwog (357) as an explicit catalog enemy -> introduction refused!
        DaggerfallIntroductionResult refused = session.Introductions.TryIssueIntroduction(roseKnight, gortwogId, session.Site.ActiveSite);
        Assert.False(refused.Accepted);
        Assert.Contains("no weight", refused.Message, StringComparison.OrdinalIgnoreCase);

        // Even if an enemy vouch were manually recorded in the ledger, Gortwog never trusts it.
        ActorVouchRecord enemyVouch = new(roseKnightId, KnightsOfTheRoseFaction, ActorVouchKind.Guild, 1, "Sir Corvus", "Wayrest");
        session.State.ActorLedger.RecordVouch(gortwogId, enemyVouch, ActorOrigin.MintedActor(DaggerfallActorKinds.StaticNpc));
        Assert.False(session.Introductions.TrustsReferrer(gortwog, enemyVouch));
        Assert.False(session.Introductions.AssessAccess(gortwog).Allowed);
        Assert.Equal(0, session.Introductions.TrustedVouchReactionBonus(gortwogId));
    }

    [Fact]
    public void Trusted_vouch_improves_merchant_prices_waives_hire_deposit_and_survives_save_reload()
    {
        // 1. Merchant better terms + idempotency + save/reload.
        using (var fixture = new ConditionSessionFixture(random: RandomMinimum.Create()))
        {
            DaggerfallSession session = fixture.Session;
            session.AdvanceElapsedTime(6 * 60 * 60);
            session.State.Actors.Player.Stats.GetStat(StatId.Parse("mercantile")).BaseValue = 50;
            session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 50;

            DaggerfallSiteRecord site = session.Site.ActiveSite!;
            DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
            long merchantId = session.State.Npcs.RegisterStable(
                DaggerfallNpcKind.Static,
                "vouch-merchant",
                npcSite,
                new("breton", "Male", 182, 0, 7, 510),
                "merchant",
                ["shop", "buy-items", "sell-items"]);
            DaggerfallNpc merchant = session.State.Npcs.Require(merchantId);
            DaggerfallServiceProvider provider = new(merchantId, npcSite, "shop");
            DaggerfallMerchantProviderContext context = new(provider, Quality: 10, BuildingType: 13, BlockX: 1, BlockY: 1, BuildingIndex: 0);

            AddGold(session, 50_000);
            DurableIdentityReference sword = AddSellableSword(session);
            string swordKey = "unique:" + sword.Value;

            // Spawn a trusted peer who likes the player and can vouch them to the merchant.
            long peerId = SpawnCourtNpc(session, "merchant-peer", "Garrick", merchant.Appearance.FactionId, site.Id.Region, ["talk"]);
            DaggerfallNpc peer = session.State.Npcs.Require(peerId);
            session.Introductions.AdjustActorDisposition(peerId, 20);

            DaggerfallMerchantView before = session.State.Merchants.Read(context);
            DaggerfallMerchantItemView stockItem = before.Stock.First(item => item.UnitPrice >= 20);
            ulong buyBefore = stockItem.UnitPrice;
            ulong sellBefore = before.PlayerItems.Single(i => i.Key == swordKey).UnitPrice;
            int reactionBefore = session.State.Social.TradeReactionForNpc(merchant);

            DaggerfallIntroductionResult vouched = session.Introductions.TryIssueIntroduction(peer, merchant.DurableId, session.Site.ActiveSite);
            Assert.True(vouched.Accepted, vouched.Message);
            Assert.Equal(ActorVouchKind.Trade, vouched.Vouch!.Kind);
            int reactionAfter = session.State.Social.TradeReactionForNpc(merchant);
            Assert.Equal(reactionBefore + DaggerfallIntroductions.TrustedVouchStandingBonus, reactionAfter);

            DaggerfallMerchantView after = session.State.Merchants.Read(context);
            ulong buyAfter = after.Stock.Single(item => item.Key == stockItem.Key).UnitPrice;
            ulong sellAfter = after.PlayerItems.Single(i => i.Key == swordKey).UnitPrice;
            Assert.True(buyAfter < buyBefore, $"Expected trusted vouch buy price ({buyAfter}) < unvouched ({buyBefore}).");
            Assert.True(sellAfter > sellBefore, $"Expected trusted vouch sell price ({sellAfter}) > unvouched ({sellBefore}).");

            // Idempotency: asking a second time does NOT duplicate or stack price bonuses.
            DaggerfallIntroductionResult repeat = session.Introductions.TryIssueIntroduction(peer, merchant.DurableId, session.Site.ActiveSite);
            Assert.True(repeat.AlreadyVouched);
            Assert.Single(session.Introductions.ReadVouches(merchant.DurableId));
            Assert.Equal(reactionAfter, session.State.Social.TradeReactionForNpc(merchant));

            // Save & reload preserves structured vouch (referrer, faction, kind, day) and keeps the improved terms!
            RulesetSavePayload saved = session.CaptureSave();
            using DaggerfallSession restored = fixture.Restore(saved);
            DaggerfallNpc restoredMerchant = restored.State.Npcs.Require(merchant.DurableId);
            ActorVouchRecord restoredVouch = Assert.Single(restored.Introductions.ReadVouches(merchant.DurableId));
            Assert.Equal(peerId, restoredVouch.ReferrerDurableId);
            Assert.Equal(vouched.Vouch.ReferrerFactionId, restoredVouch.ReferrerFactionId);
            Assert.Equal(ActorVouchKind.Trade, restoredVouch.Kind);
            Assert.Equal(vouched.Vouch.IssuedDay, restoredVouch.IssuedDay);
            Assert.Equal(reactionAfter, restored.State.Social.TradeReactionForNpc(restoredMerchant));
        }

        // 2. Trusted vouch waives companion hire deposit (0 gold spent) and LastFavourDay persists across save/reload.
        using (var fixture = SourceBackedGuildBankSessionFixture.Create())
        {
            fixture.Random = RandomMinimum.Create();
            using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
            SetSocialStats(session, etiquette: 85, streetwise: 85, personality: 85);

            // Advance to daytime so a hireable townsperson is active.
            session.AdvanceElapsedTime(8 * 60 * 60);
            DaggerfallNpc hireable = session.State.Npcs.All.First(n =>
                session.PartyHire.TopicsFor(n).Any(t => t.Id == "hire-companion"));

            // Spend down below the 25-gold companion deposit so an unvouched hire is refused for lack of deposit.
            ulong startingGold = session.State.Currency.Read().Gold;
            if (startingGold > 5UL)
                Assert.True(session.State.Currency.TrySpendGold(startingGold - 5UL, []));
            ulong goldBefore = session.State.Currency.Read().Gold;
            Assert.True(goldBefore < DaggerfallPartyHireService.CompanionHireFee);
            DaggerfallPartyHireResult deniedNoGold = session.PartyHire.Resolve(hireable, DaggerfallDialogueTopic.HireCompanion);
            Assert.False(deniedNoGold.Accepted);

            // Request a trusted peer vouch on the ordinary talk-only hireable through real dialogue (R11049-1).
            long voucherId = SpawnCourtNpc(session, "hire-voucher", "Alena", hireable.Appearance.FactionId, DaggerfallRegion, ["talk"]);
            session.Introductions.AdjustActorDisposition(voucherId, 20);
            OpenDialogueWith(session, voucherId);
            DaggerfallDialogueView voucherView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
            string introTopicId = $"introduce:{hireable.DurableId}";
            Assert.Contains(voucherView.Topics, t => t.Id == introTopicId && t.Category == "Introductions");
            DaggerfallActivationOutcome vouchReply = session.Dialogue.Ask(voucherView.Revision, introTopicId, null, null);
            Assert.True(vouchReply.Applied, vouchReply.Message);
            ActorVouchRecord hireVouch = Assert.Single(session.Introductions.ReadVouches(hireable.DurableId));
            Assert.Equal(voucherId, hireVouch.ReferrerDurableId);
            Assert.Equal(ActorVouchKind.Contact, hireVouch.Kind);
            session.Dialogue.Close();

            // Open dialogue with the vouched hireable: greeting acknowledges Alena and companion deposit is waived (0 gold)!
            OpenDialogueWith(session, hireable.DurableId);
            DaggerfallDialogueView hireableView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
            Assert.Contains("Alena", hireableView.Greeting, StringComparison.Ordinal);
            Assert.Contains(hireableView.Topics, t => t.Id == "hire-companion" && t.Label.Contains("waived", StringComparison.OrdinalIgnoreCase));

            DaggerfallActivationOutcome waivedHire = session.Dialogue.Ask(hireableView.Revision, "hire-companion", null, null);
            Assert.True(waivedHire.Applied, waivedHire.Message);
            Assert.Contains("waives the deposit", waivedHire.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(goldBefore, session.State.Currency.Read().Gold);
            session.Dialogue.Close();

            // Verify LastFavourDay survives save/reload (cannot curry favour twice on the same day after reload).
            long favId = SpawnCourtNpc(session, "lady-persist", "Lady Bridwell", LadyBridwellFaction, DaggerfallRegion, ["talk", "quest"]);
            DaggerfallNpc fav = session.State.Npcs.Require(favId);
            Assert.True(session.Introductions.TryCurryFavour(fav, politeTone: true, bluntTone: false).Applied);
            Assert.False(session.Introductions.CanCurryFavourToday(fav));

            RulesetSavePayload saved = session.CaptureSave();
            using DaggerfallSession restored = fixture.Restore(saved);
            DaggerfallNpc restoredFav = restored.State.Npcs.Require(favId);
            Assert.False(restored.Introductions.CanCurryFavourToday(restoredFav));
            Assert.False(restored.Introductions.TryCurryFavour(restoredFav, politeTone: true, bluntTone: false).Applied);
        }
    }

    private static void SetSocialStats(DaggerfallSession session, int etiquette, int streetwise, int personality)
    {
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = etiquette;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = streetwise;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Personality.Value)).BaseValue = personality;
    }

    private static long SpawnCourtNpc(
        DaggerfallSession session,
        string stableKey,
        string displayName,
        int factionId,
        int region,
        string[] services)
    {
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(region, site.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
        DaggerfallNpcAppearance appearance = new("breton", "Female", 334, 8, Math.Abs(stableKey.GetHashCode()), factionId);
        long id = session.State.Npcs.RegisterCivilian(npcSite, appearance, displayName, services);
        session.State.Npcs.SetDisplayName(id, displayName);
        WorldPoint playerPos = session.State.PlayerControl.Position ?? new WorldPoint(0f, 0f, 0f);
        session.Civilians.Materialize(id, new ActorPose(new WorldPoint(playerPos.X + 2f, playerPos.Y, playerPos.Z + 2f), 0f));
        session.State.Npcs.Bind(id, session.Sites.ActiveProfile);
        ActorState live = session.State.Actors.Get(id);
        live.Actor.Get<FactionComponent>().SetPeopleFactions(factionId > 0 ? factionId : null, null);
        return id;
    }

    private static void OpenDialogueWith(DaggerfallSession session, long npcId)
    {
        DaggerfallActivationTarget target = Assert.Single(
            session.Dialogue.NpcTargets(),
            t => checked((long)t.Identity.Value) == npcId);
        DaggerfallActivationOutcome opened = session.Dialogue.ActivateNpc(
            new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, target));
        Assert.True(opened.Applied, opened.Message);
    }

    private static void AddGold(DaggerfallSession session, ulong amount)
    {
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        factory.Materialize(factory.Create(new("Currency", "vouch.test.gold", DaggerfallItemOwner.Player,
            Quantity: amount, TemplateIndex: 276)), session.State.Inventory, session.State.ItemInstances,
            InventoryStackId.Parse("vouch.test.gold"));
    }

    private static DurableIdentityReference AddSellableSword(DaggerfallSession session)
    {
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        DaggerfallCreatedItem created = factory.Create(new("Weapons", "vouch.test.sword", DaggerfallItemOwner.Player,
            TemplateIndex: 113, Material: "daedric"));
        DurableIdentityReference id = session.UniqueItemAllocator.AllocateReference();
        factory.Materialize(created, session.State.Inventory, session.State.ItemInstances, unique: id);
        return id;
    }
}
