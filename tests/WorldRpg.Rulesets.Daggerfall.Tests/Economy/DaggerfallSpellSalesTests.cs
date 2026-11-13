using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Spell sales and the spellmaker built alone over their inputs; the frame and saves are the session's.</summary>
public sealed class DaggerfallSpellSalesTests
{
    [Fact]
    public void A_ranked_member_is_offered_the_guilds_spells_at_half_price_on_the_witches_festival_and_buys_one_into_the_spellbook()
    {
        using GuildMakerFixture f = new();
        DaggerfallSpellSales sales = f.SpellSales(_ => 10);
        DaggerfallServiceProvider provider = f.Provider("buy-spells", 36, 496, 0);
        Assert.Empty(sales.Offers(provider, 10));
        for (int rank = 0; rank < 3; rank++) f.Social.PromoteGuild(36, 0);
        ulong ordinary = sales.Offers(provider, 10).Single(offer => offer.Key == "spell.023").Price;
        f.Calendar = Enumerable.Range(0, DaggerfallCalendar.DaysPerYear).Select(day =>
            new DaggerfallCalendar(405, day / 30, day % 30, 10, 0, 0)).Single(day => day.GetHolidayId(0) == 43);
        Assert.True(sales.Offers(provider, 10).Single(offer => offer.Key == "spell.023").Price < ordinary);
        f.Calendar = DaggerfallCalendar.Start;

        Assert.Equal("ConfirmationRequired", sales.Buy(provider, 10, "spell.023", ordinary, confirm: false).Outcome);
        Assert.Equal("PriceChanged", sales.Buy(provider, 10, "spell.023", ordinary + 1, confirm: true).Outcome);
        Assert.Equal("SpellbookRequired", sales.Buy(provider, 10, "spell.023", ordinary, confirm: true).Outcome);
        f.Carry("MiscItems", 132);
        f.GrantGold(100_000);
        Assert.Equal(new DaggerfallSpellSaleResult(true, "Purchased", ordinary), sales.Buy(provider, 10, "spell.023", ordinary, confirm: true));
        Assert.Contains("spell.023", f.Character.KnownSpells);
        Assert.Equal("AlreadyKnown", sales.Buy(provider, 10, "spell.023", ordinary, confirm: true).Outcome);
    }

    [Fact]
    public void The_spellmaker_quotes_a_draft_for_a_member_and_its_purchase_records_a_custom_spell_the_player_knows()
    {
        using GuildMakerFixture f = new();
        DaggerfallEffectCatalog effects = new(DaggerfallRestorationEffects.Definitions(RandomMinimum.Create(), _ => 1,
            new DaggerfallVitalityConsequences(new WorldRpg.Kit.Combat.CombatResolution())));
        DaggerfallSpellMaker maker = f.SpellMaker(effects);
        DaggerfallServiceProvider provider = f.Provider("make-spells", 40, 64, 0);
        Assert.Contains(maker.Effects, effect => effect.Key == "free-action");
        Assert.Null(maker.Quote(provider));
        DaggerfallSpellMakerEffect option = maker.Effects.Single(effect => effect.Key == "free-action");
        DaggerfallSpellDraft draft = new("Freedom", 4, 0, 1,
            [new("free-action", option.Type, option.SubType, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1)]);
        maker.SetDraft(draft);
        DaggerfallSpellConstructionQuote quote = maker.Quote(provider)!;
        Assert.True(quote.Eligible);
        Assert.Equal("SpellbookRequired", maker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Outcome);
        f.Carry("MiscItems", 132);
        f.GrantGold(100_000);
        Assert.Equal("PriceChanged", maker.Buy(provider, quote.Key, (ulong)quote.Gold + 1, true).Outcome);
        Assert.True(maker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Accepted);
        string learned = Assert.Single(f.Character.KnownSpells);
        Assert.True(f.Definitions.Magic.Spells[learned].IsPlayerCreated);
        Assert.Null(maker.Quote(provider));
        f.Social.ExpelGuild(40);
        maker.SetDraft(draft);
        Assert.Equal("ProviderUnavailable", maker.Quote(provider)!.Reason);
    }
}
