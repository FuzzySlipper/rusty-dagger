using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class SpellSalesSessionTests
{
    [Fact]
    public void Purchase_uses_actual_payment_and_known_ready_state_and_survives_encoded_restore()
    {
        using var fixture = new KnownReadySpellSessionTests.Fixture();
        NewGameSessionTests.Commit(fixture.Session, "class07");
        using var game = Assert.IsType<DaggerfallSession>(fixture.Session.CreateNewGame());
        Fund(game);
        var provider = Provider(game);
        Assert.Contains("buy-spells", game.State.Npcs.Require(provider.NpcId).Services);
        var offers = game.SpellSales.Offers(provider, 10);
        Assert.NotEmpty(offers);
        Assert.All(offers, offer => Assert.False(TestPayload.Definitions.Magic.Spells[offer.Key].Name.StartsWith('!')));
        var offer = offers.Single(row => row.Key == "spell.023");
        ulong before = game.State.Currency.Read().Gold;
        Assert.Equal("ConfirmationRequired", game.SpellSales.Buy(provider, 10, offer.Key, offer.Price, false).Outcome);
        Assert.Equal(before, game.State.Currency.Read().Gold);
        var result = game.SpellSales.Buy(provider, 10, offer.Key, offer.Price, true);
        Assert.True(result.Accepted); Assert.Equal(offer.Price, result.Paid);
        Assert.Equal(before - offer.Price, game.State.Currency.Read().Gold);
        Assert.Contains(offer.Key, game.State.Character.KnownSpells);
        Assert.Equal("AlreadyKnown", game.SpellSales.Buy(provider, 10, offer.Key, offer.Price, true).Outcome);
        Assert.Equal(before - offer.Price, game.State.Currency.Read().Gold);
        game.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Maximum.BaseValue = 10_000;
        game.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).SetCurrent(10_000);
        Assert.Equal(DaggerfallCastOutcome.Ready, game.ReadyPlayerSpell(offer.Key).Outcome);
        using var restored = fixture.Restore(game.CaptureSave());
        Assert.Contains(offer.Key, restored.State.Character.KnownSpells);
        Assert.Equal(offer.Key, restored.ReadSpells().Ready);
        Assert.Equal(before - offer.Price, restored.State.Currency.Read().Gold);
        restored.ChangeSpellbook(new("spell-delete", Key: offer.Key, Confirm: false));
        Assert.Contains(offer.Key, restored.State.Character.KnownSpells);
        restored.ChangeSpellbook(new("spell-delete", Key: offer.Key, Confirm: true));
        Assert.DoesNotContain(offer.Key, restored.State.Character.KnownSpells);
        Assert.Null(restored.ReadSpells().Ready);
        Assert.Null(DaggerfallSavePayload.Read(restored.CaptureSave()).ReadySpell);
    }

    [Fact]
    public void Price_changes_and_missing_spellbook_or_provider_cannot_charge()
    {
        using var fixture = new KnownReadySpellSessionTests.Fixture();
        NewGameSessionTests.Commit(fixture.Session, "class07");
        using var game = Assert.IsType<DaggerfallSession>(fixture.Session.CreateNewGame()); Fund(game);
        var provider = Provider(game); var offer = game.SpellSales.Offers(provider, 10).Single(row => row.Key == "spell.023");
        ulong before = game.State.Currency.Read().Gold;
        game.State.Actors.Player.Stats.GetStat(StatId.Parse("restoration")).BaseValue = 100;
        Assert.Equal("PriceChanged", game.SpellSales.Buy(provider, 10, offer.Key, offer.Price, true).Outcome);
        Assert.Equal(before, game.State.Currency.Read().Gold);
        offer = game.SpellSales.Offers(provider, 10).Single(row => row.Key == offer.Key);
        var book = game.State.Inventory.Read().UniqueItems.Single(item => item.Definition.Value == "template-132");
        game.State.Inventory.Destroy(new(book.Entity.Value, new(book.Definition.Value)));
        Assert.Equal("SpellbookRequired", game.SpellSales.Buy(provider, 10, offer.Key, offer.Price, true).Outcome);
        game.State.Npcs.SetPresence(provider.NpcId, DaggerfallNpcPresence.Removed);
        Assert.Empty(game.SpellSales.Offers(provider, 10));
        Assert.Equal("ProviderUnavailable", game.SpellSales.Buy(provider, 10, offer.Key, offer.Price, true).Outcome);
        Assert.Equal(before, game.State.Currency.Read().Gold);
    }

    [Fact]
    public void Semantic_info_and_confirmed_delete_use_the_existing_UI_and_casting_owners()
    {
        using var f = new KnownReadySpellSessionTests.Fixture();
        f.Session.State.Character.LearnSpell("spell.023");
        f.Submit(new { action = "spell-info", key = "spell.023" });
        var info = Assert.IsType<DaggerfallSpellInformation>(f.Session.ReadSpells().Information);
        Assert.NotEmpty(info.Details); Assert.Equal("spell.023", info.Key);
        f.Submit(new { action = "spell-delete", key = "spell.023", confirm = false });
        Assert.Contains("spell.023", f.Session.State.Character.KnownSpells);
        f.Submit(new { action = "spell-delete", key = "spell.023", confirm = true });
        Assert.DoesNotContain("spell.023", f.Session.State.Character.KnownSpells);
        Assert.Null(f.Session.ReadSpells().Information);
        Assert.NotNull(DaggerfallUiAction.Parse("{\"action\":\"spell-buy\",\"key\":\"spell.023\",\"revision\":\"1\",\"amount\":0,\"confirm\":true}"u8));
        Assert.Null(DaggerfallUiAction.Parse("{\"action\":\"spell-buy\",\"key\":\"spell.023\",\"amount\":1,\"confirm\":true}"u8));
    }

    [Fact]
    public void Temple_rank_gate_and_holiday_discount_share_the_current_guild_and_calendar_owners()
    {
        using var f = new KnownReadySpellSessionTests.Fixture();
        var game = f.Session; var site = game.Site.ActiveSite!;
        var npcSite = new DaggerfallNpcSite(site.Id.Region, site.Name, string.Empty);
        long id = game.State.Npcs.RegisterStable(DaggerfallNpcKind.Static, "temple-seller", npcSite,
            new("Breton", "Male", 0, 0, 0, 496), "temple spell seller", ["talk", "buy-spells"]);
        var provider = new DaggerfallServiceProvider(id, npcSite, "buy-spells");
        Assert.Empty(game.SpellSales.Offers(provider, 10));
        game.State.Social.JoinGuild(36, 0);
        Assert.Empty(game.SpellSales.Offers(provider, 10));
        for (int i = 0; i < 3; i++) game.State.Social.PromoteGuild(36, 0);
        Assert.NotEmpty(game.SpellSales.Offers(provider, 10));
        var date = new World.DaggerfallCalendar(405, 5, 0, 10, 0, 0);
        var sales = new DaggerfallSpellSales(TestPayload.Definitions, game.State,
            key => game.Casting.AvailableSpellCost(1, key), () => date);
        ulong ordinary = sales.Offers(provider, 10).Single(offer => offer.Key == "spell.023").Price;
        date = Enumerable.Range(0, World.DaggerfallCalendar.DaysPerYear).Select(day =>
            new World.DaggerfallCalendar(405, day / 30, day % 30, 10, 0, 0)).Single(day => day.GetHolidayId(0) == 43);
        ulong festival = sales.Offers(provider, 10).Single(offer => offer.Key == "spell.023").Price;
        Assert.True(festival < ordinary);
        Assert.NotEqual(festival, sales.Offers(provider, 20).Single(offer => offer.Key == "spell.023").Price);
    }

    [Fact]
    public void Published_transformation_spell_cannot_be_deleted_by_player_but_registry_never_invents_services()
    {
        using var f = new KnownReadySpellSessionTests.Fixture();
        Assert.Equal("!Lycanthropy", TestPayload.Definitions.Magic.Spells["spell.085"].Name);
        f.Session.State.Character.LearnSpell("spell.085");
        f.Submit(new { action = "spell-delete", key = "spell.085", confirm = true });
        Assert.Contains("spell.085", f.Session.State.Character.KnownSpells);
        using var restored = f.Restore(f.Session.CaptureSave());
        Assert.Contains("spell.085", restored.State.Character.KnownSpells);
        var site = f.Session.Site.ActiveSite!;
        var npcSite = new DaggerfallNpcSite(site.Id.Region, site.Name, string.Empty);
        long id = f.Session.State.Npcs.RegisterStable(DaggerfallNpcKind.Static, "non-seller", npcSite,
            new("Breton", "Male", 0, 0, 0, 60), "unavailable service", []);
        Assert.Empty(f.Session.State.Npcs.Require(id).Services);
        Assert.Empty(f.Session.SpellSales.Offers(new(id, npcSite, "buy-spells"), 10));
    }

    private static DaggerfallServiceProvider Provider(DaggerfallSession game)
    {
        var site = game.Site.ActiveSite!;
        var npcSite = new DaggerfallNpcSite(site.Id.Region, site.Name, string.Empty);
        long id = game.State.Npcs.RegisterStable(DaggerfallNpcKind.Static, "spell-seller", npcSite,
            new("Breton", "Male", 0, 0, 0, 60), "spell seller", ["talk", "buy-spells"]);
        return new(id, npcSite, "buy-spells");
    }
    private static void Fund(DaggerfallSession game)
    {
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, RandomMinimum.Create());
        factory.Materialize(factory.Create(new("Currency", "sale-test.gold", DaggerfallItemOwner.Player,
            Quantity: 10_000, TemplateIndex: 276)), game.State.Inventory, game.State.ItemInstances, InventoryStackId.Parse("sale-test.gold"));
    }
}
