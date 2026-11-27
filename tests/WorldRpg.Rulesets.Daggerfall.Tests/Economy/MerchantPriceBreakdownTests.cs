using Rusty.Engine;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11158: Authoritative line-by-line price breakdowns across buying, selling, repairs, and magic services
/// (identify, spell sales, spell maker, item maker). Every breakdown comes from the same calculation that produces
/// the final price, its lines sum identically to the final total, and no internal IDs appear in player text.
/// </summary>
public sealed class MerchantPriceBreakdownTests
{
    private const int GlenpointRegion = 18;

    private static void AssertSumsExactly(DaggerfallPriceBreakdown? breakdown, int expectedTotal)
    {
        Assert.NotNull(breakdown);
        Assert.Equal(expectedTotal, breakdown!.FinalGold);
        int sum = breakdown.BaseGold + breakdown.Steps.Sum(s => s.DeltaGold);
        Assert.Equal(expectedTotal, sum);
        string[] lines = breakdown.FormatLines();
        Assert.True(lines.Length >= 2);
        Assert.EndsWith($"Total: {expectedTotal:N0} gold", lines[^1], StringComparison.Ordinal);
        // No internal IDs or raw subject tags may appear in player-facing tooltip text.
        string text = breakdown.FormatTooltip();
        Assert.DoesNotContain("crime-wave", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("witch-hunt", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("price-surge", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("corrupt-advisor", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("template-", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fam:", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seat:", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trade_quote_breaks_down_regional_market_world_conditions_wear_standing_and_office_allegiance_to_exact_total()
    {
        DaggerfallDefinitions defs = TestPayload.Definitions;
        DaggerfallWorldConditions conditions = new();
        conditions.SeedAuthored(
        [
            new("grain", DaggerfallWorldConditionKind.GrainShortage, GlenpointRegion, Severity: 2),
            new("crime", DaggerfallWorldConditionKind.CrimeCrackdown, GlenpointRegion, SubjectTag: DaggerfallConditionPrices.CrimeWaveTag, Severity: 1),
            new("fest", DaggerfallWorldConditionKind.Decree, GlenpointRegion, SubjectTag: DaggerfallConditionPrices.FestivalTag, Severity: 1),
            new("surge", DaggerfallWorldConditionKind.Decree, GlenpointRegion, SubjectTag: DaggerfallConditionPrices.PriceSurgeTag, Severity: 1),
            new("advisor", DaggerfallWorldConditionKind.Decree, GlenpointRegion, SubjectTag: DaggerfallConditionPrices.CorruptAdvisorTag, Severity: 1),
            new("prosp", DaggerfallWorldConditionKind.Decree, GlenpointRegion, SubjectTag: DaggerfallConditionPrices.ProsperityTag, Severity: 1),
        ]);
        DaggerfallConditionPrices conditionPrices = new(conditions, () => 10, DaggerfallConditionPriceTuning.Default, defs.BuildingNames.RegionName);

        int[] factors = Enumerable.Repeat(DaggerfallRegionalEconomyPolicy.NeutralRegionalAdjustment, DaggerfallRegionalEconomyPolicy.RegionCount).ToArray();
        factors[GlenpointRegion] = 1120; // +12% classic regional market
        DaggerfallRegionalPriceState regionalPrices = new(defs.Factions, RandomMinimum.Create(), 10, new(10, factors));
        DaggerfallTradeQuoteService quotes = new(defs, new DaggerfallItemValuation(defs), regionalPrices,
            regionalConditionFactors: conditionPrices.DescribeGoodsAdjustments);

        DaggerfallItemDefinition item = defs.RequireItem(new("template-121-daedric"));
        DaggerfallItemInstanceMetadata wornMeta = DaggerfallItemInstanceMetadata.Default(item, DaggerfallItemOwner.Player) with
        {
            CurrentCondition = 60,
            MaximumCondition = 100,
        };
        DaggerfallTradeLine line = new(item, wornMeta, 1);

        DaggerfallTradeReactionBreakdown reaction = new(
            Total: 25,
            BaseSocialReaction: 15,
            OfficeShifts: [new DaggerfallReactionShiftCause("Court Factor — Wayrest (member)", 10)]);

        foreach (DaggerfallTradeSide side in new[] { DaggerfallTradeSide.BuyFromMerchant, DaggerfallTradeSide.SellToMerchant })
        {
            DaggerfallTradeQuoteResult res = quotes.Quote(side, [line], shopQuality: 15, playerMercantile: 40, playerPersonality: 50,
                reaction: reaction.Total, region: GlenpointRegion, reactionBreakdown: reaction);
            Assert.True(res.Accepted, res.Detail);
            DaggerfallTradeQuote quote = res.Quote!;
            AssertSumsExactly(quote.Breakdown, quote.Total);

            string tip = quote.Breakdown!.FormatTooltip();
            Assert.Contains("Item condition (60/100)", tip, StringComparison.Ordinal);
            Assert.Contains("+12% regional market in Glenpoint", tip, StringComparison.Ordinal);
            Assert.Contains("+30% grain shortage in Glenpoint (severity 2)", tip, StringComparison.Ordinal);
            Assert.Contains("+7.5% crime wave in Glenpoint (severity 1)", tip, StringComparison.Ordinal);
            Assert.Contains("\u221210% festival in Glenpoint (severity 1)", tip, StringComparison.Ordinal);
            Assert.Contains("+5% price surge in Glenpoint (severity 1)", tip, StringComparison.Ordinal);
            Assert.Contains("+5% corrupt advisor in Glenpoint (severity 1)", tip, StringComparison.Ordinal);
            Assert.Contains("+5% prosperity in Glenpoint (severity 1)", tip, StringComparison.Ordinal);
            Assert.Contains("Standing with merchant (+15)", tip, StringComparison.Ordinal);
            Assert.Contains("Court Factor — Wayrest (member)", tip, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Covert_office_allegiance_does_not_leak_secret_seat_name_in_reaction_shift_labels()
    {
        using DaggerfallSession session = FreshSession();
        Board board = session.PoliticalBoard!.Board;
        Region betony = board.Region("betony");
        DaggerfallSiteRecord town = session.PoliticalBoard.Town("betony")!;
        DaggerfallFactionDefinition wayrest = TestPayload.Definitions.Factions.Factions[207];
        Seat mages = board.Seat("mages-guild");
        board.SetOffice(betony, Office.Factor, mages, covert: true);
        session.State.Social.JoinGuild(DaggerfallConcreteGuildCatalog.MagesFactionId, 0);

        DaggerfallNpc shopkeeper = new(
            900_001L,
            DaggerfallNpcKind.Static,
            "test-covert-merchant",
            new DaggerfallNpcSite(town.Id.Region, town.Name, "shop", "19/0/interior/0"),
            new DaggerfallNpcAppearance("breton", "Male", 182, 0, 1, wayrest.Id),
            "merchant",
            ["talk", "merchant", "shop"],
            DaggerfallNpcPresence.Active,
            null,
            null,
            null) { DisplayName = "Bramwell" };

        IReadOnlyList<DaggerfallReactionShiftCause> shifts = session.OfficeAllegiance!.DescribeReactionShifts(shopkeeper);
        DaggerfallReactionShiftCause shift = Assert.Single(shifts);
        Assert.True(shift.Delta > 0);
        Assert.Contains("Court Factor", shift.Label, StringComparison.Ordinal);
        Assert.DoesNotContain("Mages Guild", shift.Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mages-guild", shift.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Merchant_repairs_identify_spell_sales_spellmaker_and_itemmaker_emit_exact_breakdowns_including_witch_hunt()
    {
        using var fixture = new GuildMakerFixture();
        fixture.Calendar = fixture.Calendar.Advance(10 * 3600, out _);
        const int providerRegion = 0; // GuildMakerFixture's site region (Alik'r Desert)

        // Seed a severity-2 witch hunt in the provider's region (+50% magic services).
        DaggerfallWorldConditions conditions = new();
        conditions.SeedAuthored([new("hunt", DaggerfallWorldConditionKind.CrimeCrackdown, providerRegion,
            SubjectTag: DaggerfallConditionPrices.WitchHuntTag, Severity: 2)]);
        DaggerfallConditionPrices conditionPrices = new(conditions, () => 10, DaggerfallConditionPriceTuning.Default,
            fixture.Definitions.BuildingNames.RegionName);

        // 1. Magic service quote (witch hunt +50%)
        (int magicPrice, DaggerfallPriceBreakdown magicBreakdown) = conditionPrices.QuoteMagicService(providerRegion, 200, "Base identification fee");
        Assert.Equal(300, magicPrice);
        AssertSumsExactly(magicBreakdown, 300);
        Assert.Contains("+50% witch hunt", magicBreakdown.FormatTooltip(), StringComparison.Ordinal);

        // 2. Merchant repair (with regional market + Fighters Guild member rank discount) and identify (with witch hunt)
        int[] factors = Enumerable.Repeat(DaggerfallRegionalEconomyPolicy.NeutralRegionalAdjustment, DaggerfallRegionalEconomyPolicy.RegionCount).ToArray();
        factors[providerRegion] = 1200; // +20% regional market
        DaggerfallRegionalPriceState regionalPrices = new(fixture.Definitions.Factions, RandomMinimum.Create(), 10, new(10, factors));
        DaggerfallEquipmentMoves moves = new(fixture.Inventory, fixture.Equipment, fixture.Definitions, itemInstances: fixture.Instances);
        Kit.Progression.ProgressionState progression = new();
        DaggerfallMerchantService merchants = new(
            fixture.Definitions, RandomMinimum.Create(), fixture.Npcs, fixture.Social, fixture.Stats, fixture.Character,
            progression, fixture.Inventory, fixture.Containers, fixture.Instances, fixture.UniqueItems,
            new DurableIdentityAllocator(DurableIdentityKind.Container, 200), fixture.Currency, fixture.Services,
            fixture.GuildServices,
            new DaggerfallTradeQuoteService(fixture.Definitions, new DaggerfallItemValuation(fixture.Definitions), regionalPrices,
                regionalConditionFactors: conditionPrices.DescribeGoodsAdjustments),
            regionalPrices,
            new DaggerfallItemConditionService(fixture.Definitions, fixture.Instances, moves),
            new DaggerfallSkillUseReactions(progression, fixture.Stats, fixture.Definitions, () => fixture.Character.Career),
            new Crime.DaggerfallCrimeState(null, () => false), QuestItemDropsFake.Exits(fixture.Instances),
            () => fixture.Calendar, () => new DaggerfallNpcSite(providerRegion, "Daggerfall", "guild"),
            magicServicePrice: conditionPrices.MagicService,
            magicServiceQuote: conditionPrices.QuoteMagicService);

        // Add a worn weapon to player's pack and inspect via a Fighters Guild repair provider at rank 3.
        (_, ulong weaponId) = fixture.Weapon();
        DaggerfallItemInstanceMetadata weaponMeta = fixture.Instances.RequireUnique(weaponId);
        int maxCond = Math.Max(100, weaponMeta.MaximumCondition);
        fixture.Instances.ReplaceUnique(weaponId, weaponMeta with { MaximumCondition = maxCond, CurrentCondition = maxCond / 2 });
        DaggerfallConcreteGuildCatalog.ForFaction(DaggerfallConcreteGuildCatalog.FightersFactionId).TryGetService(DaggerfallConcreteGuildService.Repair, out var repairSvc);
        DaggerfallServiceProvider repairProvider = fixture.Provider("repair", DaggerfallConcreteGuildCatalog.FightersFactionId, repairSvc.ProviderFactionId!.Value, rank: 3);
        DaggerfallMerchantView repairView = merchants.Read(new(repairProvider, Quality: 10, BuildingType: 2, BlockX: 0, BlockY: 0, BuildingIndex: 0));
        DaggerfallMerchantItemView weaponRow = Assert.Single(repairView.PlayerItems, r => r.Key == $"unique:{weaponId}");
        Assert.NotNull(weaponRow.RepairCost);
        AssertSumsExactly(weaponRow.RepairPriceBreakdown, checked((int)weaponRow.RepairCost!.Value));
        string repairTip = weaponRow.RepairPriceBreakdown!.FormatTooltip();
        Assert.Contains("+20% regional market", repairTip, StringComparison.Ordinal);
        Assert.Contains("Fighters Guild member discount", repairTip, StringComparison.Ordinal);

        // Add an unidentified magic item and inspect via a Mages Guild identifier under witch hunt.
        string magicKey = fixture.Definitions.Magic.MagicItems.Keys.First();
        (_, ulong magicId) = fixture.MagicItem(magicKey);
        DaggerfallItemInstanceMetadata magicMeta = fixture.Instances.RequireUnique(magicId);
        fixture.Instances.ReplaceUnique(magicId, magicMeta with { Identified = false });
        DaggerfallConcreteGuildCatalog.ForFaction(DaggerfallConcreteGuildCatalog.MagesFactionId).TryGetService(DaggerfallConcreteGuildService.Identify, out var idSvc);
        DaggerfallServiceProvider idProvider = fixture.Provider("identify", DaggerfallConcreteGuildCatalog.MagesFactionId, idSvc.ProviderFactionId!.Value, rank: 1);
        DaggerfallMerchantView idView = merchants.Read(new(idProvider, Quality: 10, BuildingType: 12, BlockX: 0, BlockY: 0, BuildingIndex: 1));
        DaggerfallMerchantItemView magicRow = Assert.Single(idView.PlayerItems, r => r.Key == $"unique:{magicId}");
        Assert.NotNull(magicRow.IdentifyCost);
        AssertSumsExactly(magicRow.IdentifyPriceBreakdown, checked((int)magicRow.IdentifyCost!.Value));
        Assert.Contains("+50% witch hunt", magicRow.IdentifyPriceBreakdown!.FormatTooltip(), StringComparison.Ordinal);

        // 3. Spell sales breakdown under witch hunt + standing + office allegiance
        DaggerfallConcreteGuildCatalog.ForFaction(DaggerfallConcreteGuildCatalog.MagesFactionId).TryGetService(DaggerfallConcreteGuildService.BuySpells, out var spellSvc);
        DaggerfallServiceProvider mageBuyProvider = fixture.Provider("buy-spells", DaggerfallConcreteGuildCatalog.MagesFactionId, spellSvc.ProviderFactionId!.Value, rank: 1);
        DaggerfallSpellSales sales = new(
            fixture.Definitions, fixture.Npcs, fixture.GuildServices, fixture.Services,
            fixture.Character, fixture.Inventory, fixture.Stats, _ => 25, () => fixture.Calendar,
            tradeReactionBreakdown: _ => new(15, 5, [new("Guild Hall Master — The Mages Guild (member)", 10)]),
            magicServiceQuote: conditionPrices.QuoteMagicService);
        DaggerfallSpellOffer[] offers = sales.Offers(mageBuyProvider, quality: 10);
        Assert.NotEmpty(offers);
        DaggerfallSpellOffer offer = offers[0];
        AssertSumsExactly(offer.PriceBreakdown, checked((int)offer.Price));
        string spellTip = offer.PriceBreakdown!.FormatTooltip();
        Assert.Contains("Base spell price", spellTip, StringComparison.Ordinal);
        Assert.Contains("Guild Hall Master — The Mages Guild (member)", spellTip, StringComparison.Ordinal);
        Assert.Contains("+50% witch hunt", spellTip, StringComparison.Ordinal);

        // 4. Spell maker breakdown under witch hunt
        using var magicFixture = new MagicCastFixture();
        DaggerfallConcreteGuildCatalog.ForFaction(DaggerfallConcreteGuildCatalog.MagesFactionId).TryGetService(DaggerfallConcreteGuildService.MakeSpells, out var makeSpellSvc);
        DaggerfallServiceProvider spellMakerProvider = fixture.Provider("make-spells", DaggerfallConcreteGuildCatalog.MagesFactionId, makeSpellSvc.ProviderFactionId!.Value, rank: 1);
        DaggerfallSpellMaker spellMaker = new(
            fixture.Definitions, magicFixture.Effects.Catalog, fixture.Npcs, fixture.GuildServices,
            fixture.Services, fixture.Character, fixture.Inventory, fixture.Stats, () => fixture.Calendar,
            magicServiceQuote: conditionPrices.QuoteMagicService);
        DaggerfallSpellMakerEffect effect = spellMaker.Effects.Single(e => e.Key == "free-action");
        spellMaker.SetDraft(new("Test Freedom", 4, 0, 1, [new(effect.Key, effect.Type, effect.SubType, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1)]));
        DaggerfallSpellConstructionQuote? spellQuote = spellMaker.Quote(spellMakerProvider);
        Assert.NotNull(spellQuote);
        Assert.True(spellQuote!.Eligible, spellQuote.Reason);
        AssertSumsExactly(spellQuote.PriceBreakdown, spellQuote.Gold);
        Assert.Contains("+50% witch hunt", spellQuote.PriceBreakdown!.FormatTooltip(), StringComparison.Ordinal);

        // 5. Item maker (enchanting) breakdown under witch hunt
        DaggerfallConcreteGuildCatalog.ForFaction(DaggerfallConcreteGuildCatalog.MagesFactionId).TryGetService(DaggerfallConcreteGuildService.MakeMagicItems, out var makeItemSvc);
        DaggerfallServiceProvider itemMakerProvider = fixture.Provider("make-magic-items", DaggerfallConcreteGuildCatalog.MagesFactionId, makeItemSvc.ProviderFactionId!.Value, rank: 2);
        DaggerfallItemMaker itemMaker = new(
            fixture.Definitions, fixture.Npcs, fixture.GuildServices, fixture.Services,
            fixture.Inventory, fixture.Instances, fixture.Enchanting,
            new DaggerfallSoulGems(fixture.Inventory, fixture.Instances, fixture.Definitions.Magic, fixture.UniqueItems),
            fixture.UniqueItems, () => fixture.Calendar,
            magicServiceQuote: conditionPrices.QuoteMagicService);
        DaggerfallItemMakerItem[] eligibleItems = itemMaker.Items();
        Assert.NotEmpty(eligibleItems);
        DaggerfallItemMakerSetting setting = itemMaker.Settings().First(s => s.Cost > 0 && s.Cost <= eligibleItems[0].Capacity);
        itemMaker.SetDraft(new(eligibleItems[0].Key, "Runed Blade", [setting.Key]));
        DaggerfallItemMakerQuote? itemQuote = itemMaker.Quote(itemMakerProvider);
        Assert.NotNull(itemQuote);
        Assert.True(itemQuote!.Eligible, itemQuote.Reason);
        AssertSumsExactly(itemQuote.PriceBreakdown, itemQuote.Gold);
        Assert.Contains("+50% witch hunt", itemQuote.PriceBreakdown!.FormatTooltip(), StringComparison.Ordinal);
    }
}
