using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class SocialPricesAndWitnessDiscretionTests
{
    [Fact]
    public void Merchant_buy_and_sell_prices_respond_to_seller_personal_standing_and_faction_standing_across_save_reload()
    {
        using var fixture = new ConditionSessionFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(6 * 60 * 60);

        // Give the player neutral mercantile/personality (50/50) so social standing moves prices clearly in both directions.
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("mercantile")).BaseValue = 50;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 50;

        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
        // Mages Guild faction (40) merchant so faction standing and personal seller disposition both apply.
        long merchantId = session.State.Npcs.RegisterStable(
            DaggerfallNpcKind.Static,
            "social-merchant",
            npcSite,
            new("breton", "Male", 182, 0, 7, DaggerfallConcreteGuildCatalog.MagesFactionId),
            "merchant",
            ["shop"]);
        DaggerfallServiceProvider provider = new(merchantId, npcSite, "shop");
        DaggerfallMerchantProviderContext context = new(provider, Quality: 10, BuildingType: 13, BlockX: 1, BlockY: 1, BuildingIndex: 0);

        AddGold(session, 50_000);
        DurableIdentityReference swordId = AddSellableSword(session);
        string swordKey = "unique:" + swordId.Value;

        DaggerfallMerchantView neutral = session.State.Merchants.Read(context);
        DaggerfallMerchantItemView stockItem = neutral.Stock.First(item => item.UnitPrice >= 20);
        ulong neutralBuy = stockItem.UnitPrice;
        ulong neutralSell = neutral.PlayerItems.Single(item => item.Key == swordKey).UnitPrice;
        Assert.True(neutralSell >= 10, $"Expected sellable sword to quote >= 10 gold, got {neutralSell}.");

        // 1. Raising faction standing improves prices (lower buy, higher sell).
        session.State.Social.ChangeFactionReputation(DaggerfallConcreteGuildCatalog.MagesFactionId, 40, DaggerfallFactionReputationChange.Direct);
        DaggerfallMerchantView favoredFaction = session.State.Merchants.Read(context);
        ulong favoredFactionBuy = favoredFaction.Stock.Single(item => item.Key == stockItem.Key).UnitPrice;
        ulong favoredFactionSell = favoredFaction.PlayerItems.Single(item => item.Key == swordKey).UnitPrice;
        Assert.True(favoredFactionBuy < neutralBuy, $"Expected higher faction standing to lower buy price ({favoredFactionBuy} < {neutralBuy}).");
        Assert.True(favoredFactionSell > neutralSell, $"Expected higher faction standing to raise sell price ({favoredFactionSell} > {neutralSell}).");

        // 2. Reset faction standing, then lower it -> worse prices (higher buy, lower sell).
        session.State.Social.ChangeFactionReputation(DaggerfallConcreteGuildCatalog.MagesFactionId, -80, DaggerfallFactionReputationChange.Direct);
        DaggerfallMerchantView hostileFaction = session.State.Merchants.Read(context);
        ulong hostileFactionBuy = hostileFaction.Stock.Single(item => item.Key == stockItem.Key).UnitPrice;
        ulong hostileFactionSell = hostileFaction.PlayerItems.Single(item => item.Key == swordKey).UnitPrice;
        Assert.True(hostileFactionBuy > neutralBuy, $"Expected lower faction standing to raise buy price ({hostileFactionBuy} > {neutralBuy}).");
        Assert.True(hostileFactionSell < neutralSell, $"Expected lower faction standing to lower sell price ({hostileFactionSell} < {neutralSell}).");

        // 3. Return faction to baseline and adjust personal standing with this specific seller in ActorLedger.
        session.State.Social.ChangeFactionReputation(DaggerfallConcreteGuildCatalog.MagesFactionId, 40, DaggerfallFactionReputationChange.Direct);
        SetSellerDisposition(session, merchantId, +35);
        DaggerfallMerchantView friendlySeller = session.State.Merchants.Read(context);
        ulong friendlyBuy = friendlySeller.Stock.Single(item => item.Key == stockItem.Key).UnitPrice;
        ulong friendlySell = friendlySeller.PlayerItems.Single(item => item.Key == swordKey).UnitPrice;
        Assert.True(friendlyBuy < neutralBuy, $"Expected positive seller disposition to lower buy price ({friendlyBuy} < {neutralBuy}).");
        Assert.True(friendlySell > neutralSell, $"Expected positive seller disposition to raise sell price ({friendlySell} > {neutralSell}).");

        // 4. Negative personal standing (grudge) worsens prices AND persists cleanly across save/reload.
        SetSellerDisposition(session, merchantId, -35);
        DaggerfallMerchantView grumpySeller = session.State.Merchants.Read(context);
        ulong grumpyBuy = grumpySeller.Stock.Single(item => item.Key == stockItem.Key).UnitPrice;
        ulong grumpySell = grumpySeller.PlayerItems.Single(item => item.Key == swordKey).UnitPrice;
        Assert.True(grumpyBuy > neutralBuy, $"Expected negative seller disposition to raise buy price ({grumpyBuy} > {neutralBuy}).");
        Assert.True(grumpySell < neutralSell, $"Expected negative seller disposition to lower sell price ({grumpySell} < {neutralSell}).");

        RulesetSavePayload savedBytes = session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(savedBytes);
        DaggerfallMerchantView restoredView = restored.State.Merchants.Read(context);
        Assert.Equal(grumpyBuy, restoredView.Stock.Single(item => item.Key == stockItem.Key).UnitPrice);
        Assert.Equal(grumpySell, restoredView.PlayerItems.Single(item => item.Key == swordKey).UnitPrice);

        // 5. Extreme combined standing (+100 faction + +80 disposition) clamps safely instead of rejecting the quote.
        restored.State.Social.ChangeFactionReputation(DaggerfallConcreteGuildCatalog.MagesFactionId, 100, DaggerfallFactionReputationChange.Direct);
        SetSellerDisposition(restored, merchantId, +80);
        DaggerfallMerchantView clampedView = restored.State.Merchants.Read(context);
        Assert.True(clampedView.Stock.Single(item => item.Key == stockItem.Key).UnitPrice > 0);
        DaggerfallMerchantResult bought = restored.State.Merchants.Buy(context, clampedView.Revision, stockItem.Key, 1);
        Assert.True(bought.Accepted, bought.Outcome);
    }

    [Fact]
    public void Spell_sales_and_room_rental_prices_respond_to_seller_and_faction_standing()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession game = fixture.Start(fixture.MagesProfile);
        game.State.Actors.Player.Stats.GetStat(StatId.Parse("mercantile")).BaseValue = 50;
        game.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 50;
        game.State.Social.JoinGuild(DaggerfallConcreteGuildCatalog.MagesFactionId, checked((int)DaggerfallCalendar.Start.DayNumber));

        DaggerfallNpc seller = game.State.Npcs.All.First(npc =>
            DaggerfallSpellSales.GuildForProvider(npc.Appearance.FactionId)?.FactionId == DaggerfallConcreteGuildCatalog.MagesFactionId);
        DaggerfallServiceProvider provider = new(seller.DurableId, seller.Site, "buy-spells");

        DaggerfallSpellOffer baseline = game.SpellSales.Offers(provider, 10).First(offer => offer.Price >= 20);
        SetSellerDisposition(game, seller.DurableId, +35);
        DaggerfallSpellOffer friendly = game.SpellSales.Offers(provider, 10).Single(offer => offer.Key == baseline.Key);
        Assert.True(friendly.Price < baseline.Price, $"Expected friendly spell seller to offer cheaper spell ({friendly.Price} < {baseline.Price}).");

        SetSellerDisposition(game, seller.DurableId, -35);
        DaggerfallSpellOffer hostile = game.SpellSales.Offers(provider, 10).Single(offer => offer.Key == baseline.Key);
        Assert.True(hostile.Price > baseline.Price, $"Expected hostile spell seller to charge more ({hostile.Price} > {baseline.Price}).");

        // Room rental quote responds to social standing reaction as well.
        ulong neutralRoom = DaggerfallLodgingState.Quote(10, DaggerfallCalendar.Start, 10, 50, 50, free: false, reaction: 0);
        ulong favoredRoom = DaggerfallLodgingState.Quote(10, DaggerfallCalendar.Start, 10, 50, 50, free: false, reaction: +40);
        ulong disfavoredRoom = DaggerfallLodgingState.Quote(10, DaggerfallCalendar.Start, 10, 50, 50, free: false, reaction: -40);
        Assert.True(favoredRoom < neutralRoom, $"Expected positive standing to reduce room price ({favoredRoom} < {neutralRoom}).");
        Assert.True(disfavoredRoom > neutralRoom, $"Expected negative standing to increase room price ({disfavoredRoom} > {neutralRoom}).");
    }

    [Fact]
    public void Witness_discretion_formula_lowers_unconnected_report_chance_with_social_skills_and_disposition()
    {
        int lowSocialLockpick = DaggerfallWitnessDiscretion.CalculateUnconnectedReportChance(
            DaggerfallCrimeKind.AttemptedBreakingAndEntering, etiquette: 10, streetwise: 10, personality: 20, witnessDispositionDelta: 0);
        int highEtiquetteLockpick = DaggerfallWitnessDiscretion.CalculateUnconnectedReportChance(
            DaggerfallCrimeKind.AttemptedBreakingAndEntering, etiquette: 90, streetwise: 10, personality: 80, witnessDispositionDelta: 0);
        int highStreetwiseLockpick = DaggerfallWitnessDiscretion.CalculateUnconnectedReportChance(
            DaggerfallCrimeKind.AttemptedBreakingAndEntering, etiquette: 10, streetwise: 90, personality: 80, witnessDispositionDelta: 0);
        int friendlyBystander = DaggerfallWitnessDiscretion.CalculateUnconnectedReportChance(
            DaggerfallCrimeKind.AttemptedBreakingAndEntering, etiquette: 90, streetwise: 10, personality: 80, witnessDispositionDelta: +25);
        int grudgeBystander = DaggerfallWitnessDiscretion.CalculateUnconnectedReportChance(
            DaggerfallCrimeKind.AttemptedBreakingAndEntering, etiquette: 10, streetwise: 10, personality: 20, witnessDispositionDelta: -25);
        int murderChance = DaggerfallWitnessDiscretion.CalculateUnconnectedReportChance(
            DaggerfallCrimeKind.Murder, etiquette: 90, streetwise: 90, personality: 80, witnessDispositionDelta: 0);

        Assert.Equal(highEtiquetteLockpick, highStreetwiseLockpick);
        Assert.True(highEtiquetteLockpick < lowSocialLockpick);
        Assert.True(friendlyBystander < highEtiquetteLockpick);
        Assert.True(grudgeBystander > lowSocialLockpick);
        Assert.True(murderChance > highEtiquetteLockpick);
        Assert.InRange(friendlyBystander, DaggerfallWitnessDiscretion.MinimumUnconnectedChance, DaggerfallWitnessDiscretion.MaximumUnconnectedChance);
        Assert.InRange(grudgeBystander, DaggerfallWitnessDiscretion.MinimumUnconnectedChance, DaggerfallWitnessDiscretion.MaximumUnconnectedChance);
    }

    [Fact]
    public void Unconnected_bystander_shrugs_off_neighbour_lockpick_with_high_social_skills_but_reports_with_low_social_skills()
    {
        // Roll = 40: with low social skills (~50% chance) bystander reports; with high social skills (~17% chance) bystander shrugs it off!
        using var highSocial = CreateExteriorWitnessFixture(rollValue: 40);
        highSocial.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = 90;
        highSocial.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = 90;
        highSocial.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 80;

        long neighbour = highSocial.AddBystander("Alden Mercer", factionId: 0);
        highSocial.ObserveNeighbourLockpick(buildingKey: "0/0/1", buildingFactionId: 0);

        DaggerfallWitnessDecision ignored = Assert.Single(highSocial.Session.Crimes.LastWitnessDecisions);
        Assert.Equal(neighbour, ignored.WitnessActorId);
        Assert.False(ignored.Reported);
        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollIgnored, ignored.Reason);
        Assert.Empty(highSocial.Session.State.Crime.Incidents);
        Assert.Null(highSocial.Session.Law.Current);

        using var lowSocial = CreateExteriorWitnessFixture(rollValue: 40);
        lowSocial.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = 10;
        lowSocial.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = 10;
        lowSocial.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 20;

        long neighbour2 = lowSocial.AddBystander("Alden Mercer", factionId: 0);
        lowSocial.ObserveNeighbourLockpick(buildingKey: "0/0/1", buildingFactionId: 0);

        DaggerfallWitnessDecision reported = Assert.Single(lowSocial.Session.Crimes.LastWitnessDecisions);
        Assert.Equal(neighbour2, reported.WitnessActorId);
        Assert.True(reported.Reported);
        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollReported, reported.Reason);
        DaggerfallCrimeIncidentSave incident = Assert.Single(lowSocial.Session.State.Crime.Incidents);
        Assert.Equal(DaggerfallCrimeKind.AttemptedBreakingAndEntering, incident.Crime);
        Assert.Equal([neighbour2], incident.Witnesses.WitnessActorIds);
        Assert.NotNull(lowSocial.Session.Law.Current);
    }

    [Fact]
    public void Family_member_never_ignores_crime_against_relative_or_household_and_FamilyId_persists_across_save_reload()
    {
        // Even with maxed social skills (100) and roll = 100 (guaranteed ignore for unconnected witnesses),
        // a family member (by explicit FamilyId, shared surname, or shared household building) ALWAYS reports!
        using var fixture = CreateExteriorWitnessFixture(rollValue: 100);
        fixture.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = 100;
        fixture.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = 100;
        fixture.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 100;

        long relativeByFamilyId = fixture.AddBystander("Talia Rowan", factionId: 0, familyId: "family:ashford");
        long relativeBySurname = fixture.AddBystander("Gareth Ashford", factionId: 0);
        long unrelatedStranger = fixture.AddBystander("Bram Turner", factionId: 0);
        long victim = fixture.AddBystander("Elara Ashford", factionId: 0, familyId: "family:ashford");

        // Save and reload BEFORE the crime to prove FamilyId persists through ActorLedger!
        RulesetSavePayload saved = fixture.Session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(saved);
        Assert.Equal("family:ashford", restored.State.Actors.Get(relativeByFamilyId).Actor.Get<ActorOverlayComponent>().FamilyId);
        Assert.Equal("family:ashford", restored.State.Actors.Get(victim).Actor.Get<ActorOverlayComponent>().FamilyId);

        restored.Crimes.ObserveAttack(DaggerfallActorIdentity.PlayerEntityId, victim, "family-attack:1");

        IReadOnlyList<DaggerfallWitnessDecision> decisions = restored.Crimes.LastWitnessDecisions;
        Assert.Equal(DaggerfallWitnessReportReason.FamilyMember, decisions.Single(d => d.WitnessActorId == relativeByFamilyId).Reason);
        Assert.True(decisions.Single(d => d.WitnessActorId == relativeByFamilyId).Reported);
        Assert.Equal(DaggerfallWitnessReportReason.FamilyMember, decisions.Single(d => d.WitnessActorId == relativeBySurname).Reason);
        Assert.True(decisions.Single(d => d.WitnessActorId == relativeBySurname).Reported);
        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollIgnored, decisions.Single(d => d.WitnessActorId == unrelatedStranger).Reason);
        Assert.False(decisions.Single(d => d.WitnessActorId == unrelatedStranger).Reported);

        DaggerfallCrimeIncidentSave incident = Assert.Single(restored.State.Crime.Incidents);
        Assert.Contains(relativeByFamilyId, incident.Witnesses.WitnessActorIds);
        Assert.Contains(relativeBySurname, incident.Witnesses.WitnessActorIds);
        Assert.DoesNotContain(unrelatedStranger, incident.Witnesses.WitnessActorIds);
        Assert.NotNull(restored.Law.Current);
    }

    [Fact]
    public void Fellow_guild_member_never_ignores_crime_against_guild_hall_or_guildmate_while_outsider_may_shrug_it_off()
    {
        // Maxed social skills (100) + roll = 100: unconnected bystander ignores, fellow Mages Guild member NEVER ignores!
        using var fixture = CreateExteriorWitnessFixture(rollValue: 100);
        fixture.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = 100;
        fixture.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = 100;
        fixture.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 100;

        // Mages Guild (40) vs Fighters Guild (41) + unaffiliated bystander.
        long magesGuildMember = fixture.AddBystander("Soren Vane", factionId: DaggerfallConcreteGuildCatalog.MagesFactionId);
        long fightersGuildMember = fixture.AddBystander("Roric Hale", factionId: DaggerfallConcreteGuildCatalog.FightersFactionId);
        long unaffiliated = fixture.AddBystander("Nora Finch", factionId: 0);

        fixture.ObserveNeighbourLockpick(buildingKey: "0/0/2", buildingFactionId: DaggerfallConcreteGuildCatalog.MagesFactionId);

        IReadOnlyList<DaggerfallWitnessDecision> decisions = fixture.Session.Crimes.LastWitnessDecisions;
        Assert.Equal(DaggerfallWitnessReportReason.FellowGuildMember, decisions.Single(d => d.WitnessActorId == magesGuildMember).Reason);
        Assert.True(decisions.Single(d => d.WitnessActorId == magesGuildMember).Reported);
        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollIgnored, decisions.Single(d => d.WitnessActorId == fightersGuildMember).Reason);
        Assert.False(decisions.Single(d => d.WitnessActorId == fightersGuildMember).Reported);
        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollIgnored, decisions.Single(d => d.WitnessActorId == unaffiliated).Reason);
        Assert.False(decisions.Single(d => d.WitnessActorId == unaffiliated).Reported);

        DaggerfallCrimeIncidentSave incident = Assert.Single(fixture.Session.State.Crime.Incidents);
        Assert.Equal([magesGuildMember], incident.Witnesses.WitnessActorIds);
        Assert.NotNull(fixture.Session.Law.Current);
    }

    [Fact]
    public void Unrelated_occupants_of_public_building_use_discretion_while_actual_relatives_and_private_house_residents_always_report()
    {
        // R11048-1: Sharing Site.Building inside a public building (tavern = 15, shop = 13) must NOT classify
        // unrelated patrons/occupants as family; only explicit family, surnames, or private-residence households do.
        using var fixture = CreateExteriorWitnessFixture(rollValue: 100);
        fixture.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = 100;
        fixture.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = 100;
        fixture.Session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 100;

        const string tavernBuilding = "1/1/0";
        long unrelatedPatron = fixture.AddBystander("Bram Turner", factionId: 0, buildingKey: tavernBuilding);
        long distinctFamilyPatron = fixture.AddBystander("Caleb Ashford", factionId: 0, familyId: "family:distinct", buildingKey: tavernBuilding);
        long actualRelative = fixture.AddBystander("Elara Ashford", factionId: 0, familyId: "family:innkeeper", buildingKey: tavernBuilding);

        DaggerfallSiteRecord site = fixture.Session.Site.ActiveSite!;
        DaggerfallCrimeVictimContext tavernVictim = new(
            FactionId: null,
            BuildingKey: tavernBuilding,
            FamilyId: "family:innkeeper",
            DisplayName: "Gareth Ashford",
            Region: site.Id.Region,
            Location: site.Name,
            BuildingType: DaggerfallLodgingState.TavernBuildingType);
        DaggerfallCrimeWitnessEvidence raw = new(
            DaggerfallCrimeWitnessQuery.CompletedWithWitnesses,
            [unrelatedPatron, distinctFamilyPatron, actualRelative]);

        DaggerfallWitnessDiscretionResult result = fixture.Session.Crimes.EvaluateWitnesses(
            "tavern-theft:1", DaggerfallCrimeKind.Theft, tavernVictim, raw);

        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollIgnored, result.Decisions.Single(d => d.WitnessActorId == unrelatedPatron).Reason);
        Assert.False(result.Decisions.Single(d => d.WitnessActorId == unrelatedPatron).Reported);
        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollIgnored, result.Decisions.Single(d => d.WitnessActorId == distinctFamilyPatron).Reason);
        Assert.False(result.Decisions.Single(d => d.WitnessActorId == distinctFamilyPatron).Reported);
        Assert.Equal(DaggerfallWitnessReportReason.FamilyMember, result.Decisions.Single(d => d.WitnessActorId == actualRelative).Reason);
        Assert.True(result.Decisions.Single(d => d.WitnessActorId == actualRelative).Reported);
        Assert.Equal([actualRelative], result.ReportingEvidence.WitnessActorIds);

        // Contrast: a resident sharing Site.Building of a private house (BuildingType = 17, House1) IS household family.
        const string privateHouse = "2/2/0";
        long resident = fixture.AddBystander("Maren Holt", factionId: 0, buildingKey: privateHouse);
        DaggerfallCrimeVictimContext houseVictim = new(
            FactionId: null,
            BuildingKey: privateHouse,
            Region: site.Id.Region,
            Location: site.Name,
            BuildingType: 17);
        DaggerfallWitnessDiscretionResult houseResult = fixture.Session.Crimes.EvaluateWitnesses(
            "house-pick:1", DaggerfallCrimeKind.AttemptedBreakingAndEntering, houseVictim,
            new(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [resident]));
        Assert.Equal(DaggerfallWitnessReportReason.FamilyMember, Assert.Single(houseResult.Decisions).Reason);
        Assert.True(Assert.Single(houseResult.Decisions).Reported);
    }

    [Fact]
    public void Exterior_to_interior_forced_entry_preserves_city_watch_witness_classification_after_exterior_unload()
    {
        // R11048-2: When a city-watch mobile (146, no NPC registry entry) witnesses a forced exterior-to-interior entry,
        // TryEnterBuilding unloads the exterior actors before ReportEntry commits. Witness classification MUST be
        // preserved before unload so the city watchman never falls through to an unconnected bystander roll.
        DaggerfallSiteBuildingId residence = new(1, 1, 0); // Charing House2, locked at midnight
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile interior = Assembled(DaggerfallWorldProfileIds.Interior(Charing, residence));
        DaggerfallSiteProfiles profiles = new([exterior, interior]);

        long watchId = 0;
        ulong doorEntity = 0;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, interior);
        SpatialFake spatial = SpatialFake.Create(exterior.Geometry.Spatial[0].Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        perception.Responder = request =>
        {
            List<PerceptionPair> pairs = [];
            foreach (PerceptionObserver observer in request.Observers.Span)
            {
                if (observer.Entity == 1)
                {
                    foreach (PerceptionTarget target in request.Targets.Span)
                    {
                        PerceptionPairKind kind = target.Entity == doorEntity
                            ? PerceptionPairKind.Visible
                            : PerceptionPairKind.Occluded;
                        pairs.Add(new PerceptionPair(observer.Entity, target.Entity, 1d, 1d, kind, 1d));
                    }
                }
                else if (watchId > 0 && checked((long)observer.Entity) == watchId)
                {
                    foreach (PerceptionTarget target in request.Targets.Span)
                        pairs.Add(new PerceptionPair(observer.Entity, target.Entity, 2d, 1d, PerceptionPairKind.Visible, 1d));
                }
            }
            return Receipt([.. pairs]);
        };

        // Roll = 100 + maxed social skills (100/100/100): an unconnected witness would ALWAYS ignore (100 > chance).
        // SplitEntryRandom returns 1 for lockpick checks (so forced entry succeeds) and 100 for witness-report rolls.
        var random = new SplitEntryRandom(lockpickRoll: 1, witnessRoll: 100);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service, random: random);
        DaggerfallSessionComposition composition = new(TestPayload.Definitions, exterior, DaggerfallTuning.Defaults)
        {
            Profiles = profiles,
            Blocks = DaggerfallBlocksContent.Read(TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))),
        };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallSkills.Lockpicking)).BaseValue = 100;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = 100;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = 100;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).BaseValue = 100;

        // Spawn a real city-watch mobile (146, no NPC registry entry) standing beside the residence door.
        string watchDefId = TestPayload.Definitions.Actors.Values.Single(a => a.MobileId == 146).Id.Value;
        DaggerfallRdbDoorId doorId = exterior.Doors.First(d => d.ExteriorBuilding == residence).Id;
        DaggerfallDoorView door = session.Sites.Projection.Doors.Read(doorId);
        doorEntity = door.Entity.Value;
        Vector3 doorPos = door.Pose.Translation;
        watchId = session.SpawnActor(watchDefId, new ActorPose(WorldPoint.From(doorPos + new Vector3(1.5f, 0f, 1.5f)), 0f));
        Assert.True(session.Crimes.IsCityWatch(watchId));
        Assert.False(session.State.Npcs.TryGet(watchId, out _));

        // Position player at the locked residence door, switch to Steal mode (lockpick -> no bash alarm), and enter!
        session.State.PlayerControl.MoveTo(doorPos + Vector3.UnitZ);
        session.State.PlayerControl.YawRadians = 0;
        session.State.PlayerControl.PitchRadians = 0;
        spatial.FloorHit = request => request.Direction.Y < -.5 ? default : new SpatialHit
        {
            Present = true,
            Kind = SpatialHitKind.Entity,
            Entity = doorEntity,
            Point = doorPos,
            Distance = 1f,
        };
        session.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"activation-mode\",\"mode\":\"steal\"}")]));
        session.Update(new ProductUpdate(OuterUpdate(2), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));

        Assert.True(session.ActivationPanel.View.Applied, session.ActivationPanel.View.Message);
        Assert.Equal(interior.ProfileKey, session.Sites.ActiveProfile);
        // Exterior watchman was unloaded during TryEnterBuilding!
        Assert.False(session.State.Actors.TryGet(watchId, out _));
        Assert.False(session.Crimes.IsCityWatch(watchId));

        // Preserved witness classification still recognized the city watchman as CityWatchDuty (100% reported)!
        DaggerfallWitnessDecision watchDecision = Assert.Single(session.Crimes.LastWitnessDecisions);
        Assert.Equal(watchId, watchDecision.WitnessActorId);
        Assert.True(watchDecision.Reported);
        Assert.Equal(DaggerfallWitnessReportReason.CityWatchDuty, watchDecision.Reason);
        Assert.Equal(100, watchDecision.ReportChancePercent);

        DaggerfallCrimeIncidentSave breakIn = Assert.Single(session.State.Crime.Incidents, i => i.Crime == DaggerfallCrimeKind.BreakingAndEntering);
        Assert.Equal([watchId], breakIn.Witnesses.WitnessActorIds);
        Assert.NotNull(session.Law.Current);
    }

    private sealed class SplitEntryRandom(int lockpickRoll, int witnessRoll) : IRandomService
    {
        private readonly IRandomService _fallback = RandomMinimum.Create();

        public KeyedRngReceipt DrawKeyed(KeyedRngRequest request)
        {
            if (request.Key.Contains("witness-report", StringComparison.Ordinal))
                return new KeyedRngReceipt(Math.Clamp(witnessRoll, request.Minimum, request.Maximum));
            if (request.Scope.Contains("lock", StringComparison.OrdinalIgnoreCase)
                || request.Key.Contains("lock", StringComparison.OrdinalIgnoreCase))
                return new KeyedRngReceipt(Math.Clamp(lockpickRoll, request.Minimum, request.Maximum));
            return _fallback.DrawKeyed(request);
        }

        public Lcg15Receipt DrawLcg15(Lcg15Request request) => _fallback.DrawLcg15(request);
        public Rng CreateScoped(ScopedRngCreateRequest request) => _fallback.CreateScoped(request);
        public Rng ForkScoped(ScopedRngForkRequest request) => _fallback.ForkScoped(request);
        public RngValue NextU64(Rng rng) => _fallback.NextU64(rng);
        public RngValue NextBoundedU32(ScopedRngBoundedRequest request) => _fallback.NextBoundedU32(request);
        public RngValue NextBool(Rng rng) => _fallback.NextBool(rng);
    }

    private static void SetSellerDisposition(DaggerfallSession session, long npcId, int dispositionDelta)
    {
        DaggerfallNpc npc = session.State.Npcs.Require(npcId);
        ActorLedgerEntry? existing = session.State.ActorLedger.TryGet(npcId, out ActorLedgerEntry? found) ? found : null;
        ActorOrigin origin = existing?.Origin ?? ActorOrigin.MintedActor(DaggerfallActorKinds.StaticNpc, gender: npc.Appearance.Gender);
        ActorSocialDiff social = ActorSocialDiff.FromNpc(npc, existing?.Social) with { DispositionDelta = dispositionDelta };
        ActorLedgerEntry updated = existing is not null
            ? existing with { Social = social }
            : new ActorLedgerEntry(npcId, origin, Social: social);
        session.State.ActorLedger.Upsert(updated);
    }

    private static void AddGold(DaggerfallSession session, ulong amount)
    {
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        factory.Materialize(factory.Create(new("Currency", "social.test.gold", DaggerfallItemOwner.Player,
            Quantity: amount, TemplateIndex: 276)), session.State.Inventory, session.State.ItemInstances,
            InventoryStackId.Parse("social.test.gold"));
    }

    private static DurableIdentityReference AddSellableSword(DaggerfallSession session)
    {
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        DaggerfallCreatedItem created = factory.Create(new("Weapons", "social.test.sword", DaggerfallItemOwner.Player,
            TemplateIndex: 113, Material: "daedric"));
        DurableIdentityReference id = session.UniqueItemAllocator.AllocateReference();
        factory.Materialize(created, session.State.Inventory, session.State.ItemInstances, unique: id);
        return id;
    }

    private static ExteriorWitnessFixture CreateExteriorWitnessFixture(int rollValue) => new(rollValue);

    private sealed class ExteriorWitnessFixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition;
        private readonly DaggerfallSiteProfile _town;
        private readonly IRandomService _random;
        private readonly List<long> _visibleObservers = [];

        internal DaggerfallSession Session { get; }

        internal ExteriorWitnessFixture(int rollValue)
        {
            _town = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
            _random = KeyedRandomFake.Create(rollValue).Service;
            _composition = new(TestPayload.Definitions, _town, DaggerfallTuning.Defaults)
            {
                Profiles = new DaggerfallSiteProfiles([_town]),
            };
            Session = DaggerfallSession.StartNew(BuildEngine().Context, _composition);
        }

        internal long AddBystander(string displayName, int factionId, string? familyId = null, string buildingKey = "")
        {
            DaggerfallSiteRecord site = Session.Site.ActiveSite!;
            long id = Session.State.Npcs.RegisterCivilian(
                new DaggerfallNpcSite(site.Id.Region, site.Name, buildingKey),
                new DaggerfallNpcAppearance("breton", "Male", 182, 0, _visibleObservers.Count + 1, factionId),
                "civilian",
                ["talk"]);
            Session.State.Npcs.SetDisplayName(id, displayName);
            WorldPoint playerPos = Session.State.PlayerControl.Position ?? new WorldPoint(0f, 0f, 0f);
            Session.Civilians.Materialize(id, new ActorPose(new WorldPoint(playerPos.X + 2f + _visibleObservers.Count, playerPos.Y, playerPos.Z + 2f), 0f));
            Session.State.Npcs.Bind(id, _town.ProfileKey);
            ActorState live = Session.State.Actors.Get(id);
            live.Actor.Get<FactionComponent>().SetPeopleFactions(factionId > 0 ? factionId : null, null);
            if (!string.IsNullOrWhiteSpace(familyId))
            {
                live.Actor.Get<ActorOverlayComponent>().FamilyId = familyId;
                DaggerfallNpc npc = Session.State.Npcs.Require(id);
                ActorLedgerEntry? existing = Session.State.ActorLedger.TryGet(id, out ActorLedgerEntry? found) ? found : null;
                ActorOrigin origin = existing?.Origin ?? ActorOrigin.MintedActor(DaggerfallActorKinds.Civilian, gender: npc.Appearance.Gender);
                ActorSocialDiff social = ActorSocialDiff.FromNpc(npc, existing?.Social) with { FamilyId = familyId };
                Session.State.ActorLedger.Upsert(existing is not null
                    ? existing with { Social = social }
                    : new ActorLedgerEntry(id, origin, Social: social));
            }
            _visibleObservers.Add(id);
            return id;
        }

        internal void ObserveNeighbourLockpick(string buildingKey, int buildingFactionId)
        {
            DaggerfallSiteRecord site = Session.Site.ActiveSite!;
            DaggerfallLockInteractionDecision failedPick = new(
                DaggerfallLockInteractionKind.Lockpick,
                DaggerfallLockInteractionSurface.Exterior,
                DaggerfallLockInteractionStatus.Failed,
                Mutation: null,
                Chance: 10,
                Roll: 90,
                FailedSkillLevel: 10,
                RecordsSkillUse: true,
                EmitsNoise: false,
                DoorDamage: 0,
                ReportsBreakingAndEntering: false);
            DaggerfallCrimeVictimContext victim = new(
                FactionId: buildingFactionId > 0 ? buildingFactionId : null,
                BuildingKey: buildingKey,
                Region: site.Id.Region,
                Location: site.Name);
            Session.Crimes.ObserveLock(failedPick, DaggerfallDoorOperationResult.Locked,
                new DurableIdentityReference(DurableIdentityKind.Actor, 9999UL), site.Id.Region, victim);
        }

        internal DaggerfallSession Restore(RulesetSavePayload saved) =>
            DaggerfallSession.Restore(BuildEngine().Context, _composition, saved);

        private EngineContextFake BuildEngine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, _town);
            SpatialFake spatial = SpatialFake.Create(_town.Geometry.Spatial[0].Sha256, releases);
            spatial.KeepPosition = true;
            PerceptionFake perception = PerceptionFake.Create();
            perception.Responder = request =>
            {
                List<PerceptionPair> pairs = [];
                foreach (PerceptionObserver observer in request.Observers.ToArray())
                {
                    if (_visibleObservers.Contains(checked((long)observer.Entity)))
                    {
                        foreach (PerceptionTarget target in request.Targets.ToArray())
                            pairs.Add(new PerceptionPair(observer.Entity, target.Entity, 2d, 1d, PerceptionPairKind.Visible, 1d));
                    }
                }
                return Receipt([.. pairs]);
            };
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service, random: _random);
        }

        public void Dispose() => Session.Dispose();
    }
}
