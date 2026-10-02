using System.Numerics;
using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class SpellMakerSessionTests
{
    [Fact]
    public void Purchase_deducts_once_preserves_every_setting_and_restores_known_ready_common_casting()
    {
        using var f = new KnownReadySpellSessionTests.Fixture();
        NewGameSessionTests.Commit(f.Session, "class07");
        using var game = Assert.IsType<DaggerfallSession>(f.Session.CreateNewGame());
        Fund(game); var provider = Provider(game);
        var draft = Draft("!Custom freedom", 68, Effect(game, "free-action") with { DurationBase = 3, DurationMod = 7, DurationPerLevel = 2 });
        game.SpellMaker.SetDraft(draft);
        var quote = Assert.IsType<DaggerfallSpellConstructionQuote>(game.SpellMaker.Quote(provider));
        Assert.True(quote.Eligible); Assert.True(quote.Gold > 0);
        ulong before = game.State.Currency.Read().Gold;
        Assert.Equal("ConfirmationRequired", game.SpellMaker.Buy(provider, quote.Key, (ulong)quote.Gold, false).Outcome);
        var purchase = game.SpellMaker.Buy(provider, quote.Key, (ulong)quote.Gold, true);
        Assert.True(purchase.Accepted); Assert.Equal((ulong)quote.Gold, purchase.Paid);
        Assert.Equal(before - (ulong)quote.Gold, game.State.Currency.Read().Gold);
        Assert.Equal("DraftChanged", game.SpellMaker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Outcome);
        Assert.Equal(before - (ulong)quote.Gold, game.State.Currency.Read().Gold);
        var saved = DaggerfallSavePayload.Read(game.CaptureSave());
        var custom = Assert.Single(saved.CustomSpells);
        Assert.Equal(draft.Name, custom.Name); Assert.Equal(68, custom.Icon); Assert.False(custom.SpellsForSale);
        Assert.Equal(draft.Effects[0], Assert.Single(custom.Effects));
        Assert.Contains(custom.Key, game.State.Character.KnownSpells);
        Assert.DoesNotContain(custom.Key, TestPayload.Definitions.Magic.Spells.Keys);
        Assert.DoesNotContain(game.SpellSales.Offers(Seller(game), 10), offer => offer.Key == custom.Key);
        Magicka(game); Assert.Equal(DaggerfallCastOutcome.Ready, game.ReadyPlayerSpell(custom.Key).Outcome);
        Assert.Equal(quote.SpellPoints, game.ReadSpells().Available.Single(spell => spell.Key == custom.Key).Cost);
        game.ChangeSpellbook(new("spell-info", Key: custom.Key));
        Assert.Contains("duration 3 + 7 per 2", game.ReadSpells().Information!.Details[0]);
        using var restored = f.Restore(game.CaptureSave());
        Assert.Equal(custom.Key, restored.ReadSpells().Ready);
        Assert.Equal(custom, Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).CustomSpells) with { Effects = custom.Effects });
        var cast = restored.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, cast.Outcome);
        Assert.Equal("free-action", Assert.Single(restored.State.Effects.Active).Definition.Key);
        restored.ChangeSpellbook(new("spell-delete", Key: custom.Key, Confirm: true));
        Assert.DoesNotContain(custom.Key, restored.State.Character.KnownSpells);
        Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).CustomSpells);
    }

    [Fact]
    public void Invalid_settings_combinations_funds_book_membership_and_changed_quote_preserve_money_and_known_spells()
    {
        using var f = new KnownReadySpellSessionTests.Fixture(); var game = f.Session; var provider = Provider(game);
        var effect = Effect(game, "free-action"); var good = Draft("Freedom", 1, effect);
        game.SpellMaker.SetDraft(good);
        Assert.True(game.SpellMaker.Quote(provider)!.Eligible);
        var quote = game.SpellMaker.Quote(provider)!;
        Assert.Equal("SpellbookRequired", game.SpellMaker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Outcome);
        Book(game);
        Assert.Equal("InsufficientFunds", game.SpellMaker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Outcome);
        Fund(game); ulong before = game.State.Currency.Read().Gold;
        Assert.Equal("PriceChanged", game.SpellMaker.Buy(provider, quote.Key, (ulong)quote.Gold + 1, true).Outcome);
        foreach (var invalid in new[] { good with { Name = "" }, good with { Effects = [] },
            good with { Effects = [effect, effect, effect, effect] }, good with { Icon = 69 },
            good with { Effects = [effect with { DurationPerLevel = 0 }] },
            good with { Effects = [effect with { Type = 999 }] },
            good with { Effects = [Effect(game, "identify")], RangeType = 1 }, good with { Element = 0 } })
        {
            game.SpellMaker.SetDraft(invalid); var refused = game.SpellMaker.Quote(provider)!;
            Assert.False(refused.Eligible);
            Assert.False(game.SpellMaker.Buy(provider, refused.Key, 0, true).Accepted);
        }
        Assert.Equal(before, game.State.Currency.Read().Gold); Assert.Empty(game.State.Character.KnownSpells);
        Assert.Empty(DaggerfallSavePayload.Read(game.CaptureSave()).CustomSpells);
        game.SpellMaker.SetDraft(good); quote = game.SpellMaker.Quote(provider)!;
        game.State.Social.ExpelGuild(40);
        Assert.Equal("ProviderUnavailable", game.SpellMaker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Outcome);
        game.State.Social.JoinGuild(40, 0);
        game.State.Npcs.SetPresence(provider.NpcId, DaggerfallNpcPresence.Removed);
        Assert.Equal("ProviderUnavailable", game.SpellMaker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Outcome);
        Assert.Equal(before, game.State.Currency.Read().Gold);
    }

    [Fact]
    public void Donor_element_union_preserves_magic_neutral_support_alongside_elemental_damage_in_common_delivery()
    {
        using var f = new KnownReadySpellSessionTests.Fixture(); var game = f.Session;
        Fund(game); Book(game); var provider = Provider(game); Magicka(game);
        var spell = Draft("Fire and recovery", 1, Effect(game, "heal-health") with { MagnitudeBaseLow = 5, MagnitudeBaseHigh = 5 }, Effect(game, "damage-health")) with { Element = 0 };
        game.SpellMaker.SetDraft(spell); var quote = game.SpellMaker.Quote(provider)!;
        Assert.True(quote.Eligible); Assert.True(game.SpellMaker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Accepted);
        var key = Assert.Single(game.State.Character.KnownSpells);
        Assert.Equal(DaggerfallCastOutcome.Ready, game.ReadyPlayerSpell(key).Outcome);
        var health = game.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(health.Maximum.Value - 10); double beforeHealth = health.Current;
        var delivery = game.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, delivery.Outcome);
        Assert.Equal(2, delivery.Bundle!.Results.Count);
        Assert.All(delivery.Bundle.Results, result => Assert.Equal(DaggerfallCastOutcome.Applied, result.Outcome));
        Assert.True(health.Current > beforeHealth);
        using var restored = f.Restore(game.CaptureSave()); Assert.Contains(key, restored.State.Character.KnownSpells);
    }

    [Fact]
    public void Malformed_missing_and_colliding_custom_definitions_fail_current_save_admission()
    {
        using var f = new KnownReadySpellSessionTests.Fixture(); var game = f.Session;
        Fund(game); Book(game); var provider = Provider(game);
        game.SpellMaker.SetDraft(Draft("Freedom", 1, Effect(game, "free-action"))); var quote = game.SpellMaker.Quote(provider)!;
        Assert.True(game.SpellMaker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Accepted);
        var raw = DaggerfallSavePayload.Read(game.CaptureSave()); var row = Assert.Single(raw.CustomSpells);
        foreach (var bad in new[] { raw with { CustomSpells = [] }, raw with { CustomSpells = [row, row] },
            raw with { CustomSpells = [row with { Key = "spell.023" }] },
            raw with { CustomSpells = [row with { Key = "custom-spell.bad" }] },
            raw with { CustomSpells = [row with { Effects = [row.Effects[0] with { DurationBase = 61 }] }] },
            raw with { CustomSpells = [row with { Effects = [row.Effects[0] with { Type = 999 }] }] },
            raw with { CustomSpells = [row with { IsPlayerCreated = false }] },
            raw with { CustomSpells = [row with { SpellsForSale = true }] } })
            Assert.ThrowsAny<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(bad)));
    }

    [Fact]
    public void Standard_and_source_eligible_custom_sale_rows_use_one_catalog_payment_cast_and_encoded_restore()
    {
        var definitions = TestPayload.Definitions.ForSession([]);
        var settings = new DaggerfallSpellEffectDefinition("free-action", 26, -1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        var eligible = new DaggerfallSpellDefinition("authored.custom.freedom", -1, false, "Authored freedom", 4, 0, 0, 1, [settings])
            { IsCustom = true, SpellsForSale = true };
        definitions.Magic.AddConstructedSpell(eligible);
        definitions.Magic.AddConstructedSpell(eligible with { Key = "authored.custom.private", SpellsForSale = false });
        using var f = new KnownReadySpellSessionTests.Fixture(definitions); var game = f.Session;
        Fund(game); Book(game); var seller = Seller(game); Magicka(game);
        var offers = game.SpellSales.Offers(seller, 10);
        Assert.Contains(offers, offer => offer.Key == "spell.023");
        Assert.DoesNotContain(offers, offer => offer.Key == "authored.custom.private");
        var offer = offers.Single(offer => offer.Key == eligible.Key); ulong before = game.State.Currency.Read().Gold;
        Assert.True(game.SpellSales.Buy(seller, 10, offer.Key, offer.Price, true).Accepted);
        Assert.Equal("AlreadyKnown", game.SpellSales.Buy(seller, 10, offer.Key, offer.Price, true).Outcome);
        Assert.Equal(before - offer.Price, game.State.Currency.Read().Gold);
        Assert.Equal(DaggerfallCastOutcome.Ready, game.ReadyPlayerSpell(eligible.Key).Outcome);
        using var restored = f.Restore(game.CaptureSave());
        Assert.Equal(eligible.Key, restored.ReadSpells().Ready);
        Assert.Empty(DaggerfallSavePayload.Read(restored.CaptureSave()).CustomSpells);
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, restored.ReleaseReadySpell(1, Vector3.UnitZ).Outcome);
        Assert.Equal("free-action", Assert.Single(restored.State.Effects.Active).Definition.Key);
    }

    [Fact]
    public void Semantic_maker_actions_require_current_provider_and_well_formed_wire_fields()
    {
        using var f = new KnownReadySpellSessionTests.Fixture();
        f.Submit(new { action = "spellmaker-draft", revision = "missing", text = "{}" });
        Assert.Equal("ProviderUnavailable", f.Session.ReadSpells().Result);
        Assert.NotNull(DaggerfallUiAction.Parse("{\"action\":\"spellmaker-draft\",\"revision\":\"1\",\"text\":\"{}\"}"u8));
        Assert.Null(DaggerfallUiAction.Parse("{\"action\":\"spellmaker-draft\",\"text\":\"{}\"}"u8));
        Assert.NotNull(DaggerfallUiAction.Parse("{\"action\":\"spellmaker-buy\",\"revision\":\"1\",\"key\":\"2\",\"amount\":10,\"confirm\":true}"u8));
        Assert.Null(DaggerfallUiAction.Parse("{\"action\":\"spellmaker-buy\",\"revision\":\"1\",\"key\":\"2\",\"amount\":-1,\"confirm\":true}"u8));
    }

    private static DaggerfallSpellDraft Draft(string name, int icon, params DaggerfallSpellEffectDefinition[] effects) => new(name, 4, 0, icon, effects);
    private static DaggerfallSpellEffectDefinition Effect(DaggerfallSession game, string key)
    {
        var effect = game.SpellMaker.Effects.Single(effect => effect.Key == key);
        return new(key, effect.Type, effect.SubType, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
    }
    private static DaggerfallServiceProvider Provider(DaggerfallSession game)
    {
        game.State.Social.JoinGuild(40, 0);
        return Register(game, "test-spellmaker", 64, "make-spells");
    }
    private static DaggerfallServiceProvider Seller(DaggerfallSession game) => Register(game, "test-seller", 60, "buy-spells");
    private static DaggerfallServiceProvider Register(DaggerfallSession game, string key, int faction, string service)
    {
        var site = game.Site.ActiveSite!; var npcSite = new DaggerfallNpcSite(site.Id.Region, site.Name, string.Empty);
        long id = game.State.Npcs.RegisterStable(DaggerfallNpcKind.Static, key, npcSite, new("Breton", "Male", 0, 0, 0, faction), key, ["talk", service]);
        return new(id, npcSite, service);
    }
    private static void Fund(DaggerfallSession game) => Materialize(game, "Currency", 276, "maker-test.gold", 100_000);
    private static void Book(DaggerfallSession game) => Materialize(game, "MiscItems", 132, "maker-test.book", 1);
    private static void Materialize(DaggerfallSession game, string group, int template, string key, int quantity)
    {
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, RandomMinimum.Create());
        factory.Materialize(factory.Create(new(group, key, DaggerfallItemOwner.Player, Quantity: checked((ulong)quantity), TemplateIndex: template)),
            game.State.Inventory, game.State.ItemInstances, InventoryStackId.Parse(key), template == 132 ? game.UniqueItemAllocator.AllocateReference() : null);
    }
    private static void Magicka(DaggerfallSession game)
    { var track = game.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); track.Maximum.BaseValue = 10000; track.SetCurrent(10000); }
}
