using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The item maker built alone over its inputs; saves and held powers are the session's (ItemMakerSessionTests).</summary>
public sealed class DaggerfallItemMakerTests
{
    private const string Listener = "enchantment.13.0";
    private const string Heavy = "enchantment.23.-1";

    [Fact]
    public void A_confirmed_purchase_at_the_quoted_price_enchants_the_item_in_place_and_spends_the_draft()
    {
        using GuildMakerFixture f = new();
        DaggerfallServiceProvider provider = f.Provider("make-magic-items", 40, 802, 5);
        f.GrantGold(1_000_000);
        var (_, id) = f.Weapon();
        DaggerfallItemMaker maker = f.ItemMaker();
        Assert.True(maker.CanUse(provider));
        Assert.Contains(maker.Items(), item => item.Key == "unique:" + id);

        maker.SetDraft(new("unique:" + id, "Listener", [Listener, Heavy]));
        DaggerfallItemMakerQuote quote = maker.Quote(provider)!;
        Assert.True(quote.Eligible, quote.Reason);
        ulong before = f.Currency.Read().Gold;

        Assert.Equal("ConfirmationRequired", maker.Buy(provider, quote.Key, (ulong)quote.Gold, false).Outcome);
        Assert.Equal("PriceChanged", maker.Buy(provider, quote.Key, (ulong)quote.Gold + 1, true).Outcome);
        DaggerfallItemMakerResult made = maker.Buy(provider, quote.Key, (ulong)quote.Gold, true);

        Assert.True(made.Accepted, made.Outcome);
        Assert.Equal(id, made.Item);
        Assert.Equal((ulong)quote.Gold, made.Paid);
        Assert.Equal(before - (ulong)quote.Gold, f.Currency.Read().Gold);
        Assert.Equal("Listener", f.Instances.RequireUnique(id).MadeEnchantment!.Name);
        Assert.Null(maker.Quote(provider));
        Assert.Equal("DraftChanged", maker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Outcome);
    }

    [Fact]
    public void Refusals_leave_the_item_and_the_purse_untouched()
    {
        using GuildMakerFixture f = new();
        DaggerfallServiceProvider provider = f.Provider("make-magic-items", 40, 802, 5);
        var (_, id) = f.Weapon();
        DaggerfallItemInstanceMetadata original = f.Instances.RequireUnique(id);
        DaggerfallItemMaker maker = f.ItemMaker();

        maker.SetDraft(new("unique:" + id, "Listener", [Listener]));
        DaggerfallItemMakerQuote quote = maker.Quote(provider)!;
        Assert.True(quote.Gold > 0);
        Assert.Equal("InsufficientFunds", maker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Outcome);

        maker.SetDraft(new("unique:missing", "Nothing", [Listener]));
        Assert.Equal("ItemUnavailable", maker.Quote(provider)!.Reason);

        f.Social.ExpelGuild(40);
        maker.SetDraft(new("unique:" + id, "Listener", [Listener]));
        Assert.False(maker.CanUse(provider));
        Assert.Equal("ProviderUnavailable", maker.Quote(provider)!.Reason);
        Assert.Equal(original, f.Instances.RequireUnique(id));
        Assert.Equal(0UL, f.Currency.Read().Gold);
    }

    [Fact]
    public void A_draft_is_bounded_before_it_is_kept()
    {
        using GuildMakerFixture f = new();
        DaggerfallItemMaker maker = f.ItemMaker();
        Assert.Throws<ArgumentException>(() => maker.SetDraft(new("unique:1", new string('n', 65), [Listener])));
        Assert.Throws<ArgumentException>(() => maker.SetDraft(new("unique:1", "Blank", [" "])));
        Assert.Equal("0", maker.Revision);
        maker.SetDraft(new("unique:1", "Kept", [Listener]));
        Assert.Equal("1", maker.Revision);
        Assert.Equal("Kept", maker.Draft.Name);
    }
}
