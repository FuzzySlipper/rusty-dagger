using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class GuildDialogueSessionTests
{
    [Fact]
    public void An_unaffiliated_house_does_not_offer_the_criminal_guild_service()
    {
        var definitions = TestPayload.Definitions;
        var (_, services) = DaggerfallNpcServiceFacts.Resolve(definitions,
            definitions.Factions.Factions[806], 18, 0, "person");
        Assert.DoesNotContain("spymaster", services);
    }

    [Fact]
    public void Dialogue_confirms_join_refuses_ineligible_and_rejoins_after_expulsion_with_saved_membership()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        using var session = fixture.Start(fixture.MagesIdentifierProvider.Profile);
        var npc = session.State.Npcs.All.Single(value => value.Appearance.FactionId == 801);
        foreach (string skill in DaggerfallConcreteGuildCatalog.ForFaction(40).GuildSkills)
            session.State.Actors.Player.Stats.GetStat(StatId.Parse(skill)).BaseValue = 0;
        Open(session, npc);
        Ask(session, "join");
        Assert.DoesNotContain(session.ActivationView.Dialogue!.Topics, value => value.Key == "yes");
        Assert.False(session.State.Social.GuildEligibility(40).IsMember);
        foreach (string skill in DaggerfallConcreteGuildCatalog.ForFaction(40).GuildSkills)
            session.State.Actors.Player.Stats.GetStat(StatId.Parse(skill)).BaseValue = 100;
        Ask(session, "join");
        Assert.Contains(session.ActivationView.Dialogue!.Topics, value => value.Key == "yes");
        Assert.False(session.State.Social.GuildEligibility(40).IsMember);
        Ask(session, "join", "no");
        Assert.False(session.State.Social.GuildEligibility(40).IsMember);
        Ask(session, "join"); Ask(session, "join", "yes");
        Assert.True(session.State.Social.GuildEligibility(40).IsMember);
        Assert.Equal(0, session.State.Social.GuildEligibility(40).Rank);
        Assert.Empty(session.ActivationView.Dialogue!.Diagnostics);
        session.State.ConcreteGuildMembership.Expel(40, 0);
        Ask(session, "join"); Ask(session, "join", "yes");
        Assert.True(session.State.Social.GuildEligibility(40).IsMember);
        using var restored = fixture.Restore(session.CaptureSave());
        Assert.True(restored.State.Social.GuildEligibility(40).IsMember);
        Assert.Equal(0, restored.State.Social.GuildEligibility(40).Rank);
    }

    [Fact]
    public void Fighters_temple_and_knightly_join_texts_render_through_source_providers()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        foreach (var kind in new[] { DaggerfallConcreteGuildKind.Fighters, DaggerfallConcreteGuildKind.Temple, DaggerfallConcreteGuildKind.KnightlyOrder })
        {
            var source = fixture.GuildProvider(kind);
            using var session = fixture.Start(source.Profile);
            var guild = GuildFor(source.Profile.InteriorBuilding!.FactionId);
            foreach (string skill in guild.GuildSkills) session.State.Actors.Player.Stats.GetStat(StatId.Parse(skill)).BaseValue = 100;
            Open(session, session.State.Npcs.All.Single(value => value.StableKey == source.Placement.Id));
            Ask(session, "join");
            Assert.Contains(session.ActivationView.Dialogue!.Topics, value => value.Key == "yes");
            Assert.Empty(session.ActivationView.Dialogue!.Diagnostics);
            Ask(session, "join", "yes");
            Assert.True(session.State.Social.GuildEligibility(guild.FactionId).IsMember);
            Assert.Empty(session.ActivationView.Dialogue!.Diagnostics);
        }
    }

    [Fact]
    public void Guild_magic_stock_is_rank_gated_identified_and_ordinary_purchase_survives_save()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        var source = fixture.GuildProvider("buy-magic-items");
        using var session = fixture.Start(source.Profile);
        var npc = session.State.Npcs.All.Single(value => value.StableKey == source.Placement.Id);
        Open(session, npc);
        Assert.False(session.ReadCurrentMerchant()!.CanBuy);
        var guild = GuildFor(source.Profile.InteriorBuilding!.FactionId);
        session.State.Social.JoinGuild(guild.FactionId, 0);
        int rank = guild.Services.Single(value => value.Service == DaggerfallConcreteGuildService.BuyMagicItems).MinimumRank!.Value;
        Assert.False(session.ReadCurrentMerchant()!.CanBuy);
        for (int day = 1; day <= rank; day++) session.State.Social.PromoteGuild(guild.FactionId, day);
        var view = session.ReadCurrentMerchant()!;
        Assert.True(view.CanBuy);
        Assert.False(view.CanShoplift);
        Assert.NotEmpty(view.Stock);
        Assert.All(view.Stock, item => Assert.True(item.Identified));
        AddGold(session);
        view = session.ReadCurrentMerchant()!;
        var item = view.Stock.First(value => value.Key.StartsWith("unique:", StringComparison.Ordinal));
        Submit(session, new { action = "merchant-buy", revision = view.Revision, item = item.Key, amount = 1 });
        Assert.Contains(session.State.Inventory.Read().UniqueItems, value => value.Definition.Value == item.Definition);
        using var restored = fixture.Restore(session.CaptureSave());
        Assert.Contains(restored.State.Inventory.Read().UniqueItems, value => value.Definition.Value == item.Definition);
    }

    [Fact]
    public void Guild_teleport_uses_rank_gate_and_existing_map_without_payment_or_time()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        var source = fixture.GuildProvider("teleport");
        using var session = fixture.Start(source.Profile);
        var npc = session.State.Npcs.All.Single(value => value.StableKey == source.Placement.Id);
        Open(session, npc); Ask(session, "teleport");
        Assert.False(session.ReadTravelPresentation().GuildTeleport);
        session.State.Social.JoinGuild(40, 0);
        for (int day = 1; day <= 7; day++) session.State.Social.PromoteGuild(40, day);
        Ask(session, "teleport"); Assert.False(session.ReadTravelPresentation().GuildTeleport);
        session.State.Social.PromoteGuild(40, 8);
        Ask(session, "teleport");
        Assert.True(session.ReadTravelPresentation().GuildTeleport);
        session.Site.Discover(fixture.PopulationProfile.Site!.Value);
        Submit(session, new { action = "travel-search", text = "" });
        var destination = session.ReadTravelPresentation().Destinations.First(value => value.Id == fixture.PopulationProfile.Site);
        Submit(session, new { action = "travel-preview", region = destination.Id.Region, destination = destination.Id.Index, cautious = false, inn = false, ship = false });
        var travel = session.ReadTravelPresentation();
        Assert.True(travel.ExecutionAvailable, travel.Message);
        Assert.Equal(0, travel.Quote!.TotalCost); Assert.Equal(0, travel.Quote.TravelMinutes);
        var before = DaggerfallSavePayload.Read(session.CaptureSave());
        Submit(session, new { action = "travel-accept", key = travel.Quote.Identity, amount = 0 });
        Assert.Equal(fixture.PopulationProfile.ProfileKey, session.Sites.ActiveProfile);
        Assert.False(session.ReadTravelPresentation().GuildTeleport);
        Assert.Equal(before.Calendar, DaggerfallSavePayload.Read(session.CaptureSave()).Calendar);
    }

    [Fact]
    public void Soul_gem_shop_and_magic_fence_enforce_membership_rank_and_item_kind()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        foreach (string service in new[] { "buy-soulgems", "sell-magic-items" })
        {
            var source = fixture.GuildProvider(service);
            using var session = fixture.Start(source.Profile);
            var npc = session.State.Npcs.All.Single(value => value.StableKey == source.Placement.Id);
            var guild = GuildFor(source.Profile.InteriorBuilding!.FactionId);
            var kind = service == "buy-soulgems" ? DaggerfallConcreteGuildService.BuySoulgems : DaggerfallConcreteGuildService.SellMagicItems;
            int rank = guild.Services.Single(value => value.Service == kind).MinimumRank!.Value;
            Open(session, npc);
            var view = session.ReadCurrentMerchant()!;
            Assert.False(service == "buy-soulgems" ? view.CanBuy && view.Stock.Any(item => item.Definition == "template-274") : view.CanSell);
            session.State.Social.JoinGuild(guild.FactionId, 0);
            for (int day = 1; day < rank; day++) session.State.Social.PromoteGuild(guild.FactionId, day);
            view = session.ReadCurrentMerchant()!;
            Assert.False(service == "buy-soulgems" ? view.CanBuy && view.Stock.Any(item => item.Definition == "template-274") : view.CanSell);
            session.State.Social.PromoteGuild(guild.FactionId, rank);
            if (service == "buy-soulgems")
            {
                AddGold(session);
                view = session.ReadCurrentMerchant()!;
                Assert.True(view.CanBuy);
                // Mages sell gems alongside magic items at their shared merchant.
                var gem = view.Stock.First(value => value.Definition == "template-274");
                Submit(session, new { action = "merchant-buy", revision = view.Revision, item = gem.Key, amount = 1 });
                Assert.Contains(session.State.Inventory.Read().UniqueItems, value => value.Definition.Value == "template-274");
            }
            else
            {
                var factory = new DaggerfallItemFactory(TestPayload.Definitions, RandomMinimum.Create());
                var item = factory.Create(new("Magic", "guild-fence-test", DaggerfallItemOwner.Player,
                    Race: "breton", Gender: "male", MagicItemKey: "magic-item.0022"));
                var id = session.UniqueItemAllocator.AllocateReference();
                factory.Materialize(item, session.State.Inventory, session.State.ItemInstances, unique: id);
                view = session.ReadCurrentMerchant()!;
                Assert.True(view.CanSell);
                Assert.Contains(view.PlayerItems, value => value.Key == "unique:" + id.Value && value.CanSell);
                Assert.Contains(view.PlayerItems, value => !value.CanSell);
                ulong gold = session.State.Currency.Read().Gold;
                if (gold > 0) Assert.True(session.State.Currency.TrySpendCarried(gold));
                Assert.Equal(0UL, session.State.Currency.Read().Gold);
                Submit(session, new { action = "merchant-sell", revision = view.Revision, item = "unique:" + id.Value, amount = 1 });
                Assert.True(session.State.Currency.Read().Gold > 0, session.Presentation.LastOutcome);
            }
        }
    }

    [Fact]
    public void Spymaster_service_is_gated_at_its_catalogued_rank()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        var source = fixture.GuildProvider("spymaster");
        using var session = fixture.Start(source.Profile);
        var npc = session.State.Npcs.All.Single(value => value.StableKey == source.Placement.Id);
        var guild = GuildFor(source.Profile.InteriorBuilding!.FactionId);
        Open(session, npc); Ask(session, "spymaster");
        Assert.Contains("join", session.ActivationView.Dialogue!.Reply);
        session.State.Social.JoinGuild(guild.FactionId, 0);
        int rank = guild.Services.Single(value => value.Service == DaggerfallConcreteGuildService.Spymaster).MinimumRank!.Value;
        for (int day = 1; day < rank; day++) session.State.Social.PromoteGuild(guild.FactionId, day);
        Ask(session, "spymaster"); Assert.Contains("rank", session.ActivationView.Dialogue!.Reply);
        session.State.Social.PromoteGuild(guild.FactionId, rank);
        Ask(session, "spymaster");
        Assert.NotEmpty(session.ActivationView.Dialogue!.Reply);
        Assert.DoesNotContain("rank does not", session.ActivationView.Dialogue!.Reply);
    }

    [Fact]
    public void Guild_recharges_only_members_with_no_regeneration_careers_when_talking()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        using var session = fixture.Start(fixture.MagesIdentifierProvider.Profile);
        var npc = session.State.Npcs.All.Single(value => value.Appearance.FactionId == 801);
        session.State.Character.BeginChoices();
        session.State.Character.ReplacePending(new("Guild tester", "breton", DaggerfallCharacterGender.Male, 0,
            DaggerfallCharacterReflexes.Average, "class03"));
        session.State.Character.CommitChoices();
        var magicka = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.SetCurrent(0);
        Open(session, npc); Assert.Equal(0, magicka.Current);
        session.State.Social.JoinGuild(40, 0);
        Open(session, npc); Assert.Equal(magicka.Maximum.Value, magicka.Current);
        session.State.ConcreteGuildMembership.Expel(40, 0);
        magicka.SetCurrent(0); Open(session, npc); Assert.Equal(0, magicka.Current);
    }

    [Fact]
    public void Temple_heals_members_at_its_rank_on_ordinary_conversation()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        var source = fixture.GuildProvider("donate");
        using var session = fixture.Start(source.Profile);
        var npc = session.State.Npcs.All.Single(value => value.StableKey == source.Placement.Id);
        var guild = GuildFor(source.Profile.InteriorBuilding!.FactionId);
        var health = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(1); Open(session, npc); Assert.Equal(1, health.Current);
        session.State.Social.JoinGuild(guild.FactionId, 0);
        int rank = guild.Services.Single(value => value.Service == DaggerfallConcreteGuildService.FreeHealing).MinimumRank!.Value;
        for (int day = 1; day <= rank; day++) session.State.Social.PromoteGuild(guild.FactionId, day);
        double strength = session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).Value;
        DaggerfallAttributeDrainEffects.DrainArtifactStrength(session.State.Effects, "guild-heal-test", 1, 1, 1, 4);
        Open(session, npc); Assert.Equal(health.Maximum.Value, health.Current);
        Assert.Equal(strength - 4, session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).Value);
        Assert.Contains(session.ActivationView.Dialogue!.Topics, value => value.Id == "restore-attributes" && value.Key == "yes");
        Ask(session, "restore-attributes", "yes");
        Assert.Equal(strength, session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).Value);
    }

    private static DaggerfallConcreteGuildDefinition GuildFor(int buildingFaction) => DaggerfallConcreteGuildCatalog.All.Single(value => value.FactionId == buildingFaction || value.ParentFactionId == buildingFaction);
    private static void Open(DaggerfallSession session, DaggerfallNpc npc)
    {
        var target = session.Dialogue.NpcTargets().Single(value => value.Identity.Value == (ulong)npc.DurableId);
        var result = session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target));
        Assert.True(result.Applied, result.Message);
    }
    private void Ask(DaggerfallSession session, string topic, string? key = null) => Submit(session,
        key is null ? new { action = "dialogue-topic", revision = session.ActivationView.Dialogue!.Revision, topic }
            : new { action = "dialogue-topic", revision = session.ActivationView.Dialogue!.Revision, topic, key });
    private ulong _step;
    private void Submit(DaggerfallSession session, object action)
    {
        string payload = JsonSerializer.Serialize(action);
        Assert.NotNull(WorldRpg.Rulesets.Daggerfall.Presentation.DaggerfallUiAction.Parse(System.Text.Encoding.UTF8.GetBytes(payload)));
        session.Update(new ProductUpdate(OuterUpdate(++_step), [Ui(payload)]));
    }
    private static void AddGold(DaggerfallSession session)
    {
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, RandomMinimum.Create());
        factory.Materialize(factory.Create(new("Currency", "guild-test-gold", DaggerfallItemOwner.Player, Quantity: 1000000, TemplateIndex: 276)),
            session.State.Inventory, session.State.ItemInstances, InventoryStackId.Parse("guild-test-gold"));
    }
}
