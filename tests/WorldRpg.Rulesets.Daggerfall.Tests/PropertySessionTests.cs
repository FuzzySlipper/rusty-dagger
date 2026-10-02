using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class PropertySessionTests
{
    [Fact]
    public void Ordinary_source_place_start_claims_only_its_selected_monthly_house_and_releases_after_save_unload()
    {
        using Fixture f = new(questBuildingScenario: true, automaticQuest: true); f.OpenBank();
        var session = f.Session; var site = session.Site.ActiveSite!;
        var before = session.ReadPropertyPresentation().Offers;
        var started = session.State.Quests.Start(new("allocated-house", "allocation.txt", "allocation", DaggerfallQuestLifecycle.Active, null, [], []));
        var resource = Assert.Single(started.Resources); var claim = resource.Binding.Building!;
        var building = site.Exterior!.Buildings[new(claim.BlockX, claim.BlockY, claim.Index)];
        string key = HouseKey(site.Id, building);
        Assert.Contains(before, offer => offer.Key == key);
        Assert.DoesNotContain(session.ReadPropertyPresentation().Offers, offer => offer.Key == key);
        Assert.Contains(session.ReadPropertyPresentation().Offers, offer => offer.Key == HouseKey(site.Id,
            site.Exterior.Buildings.Values.First(value => value.Source.BuildingType == 1 && value.ModelRadius > 0)));
        Assert.True(session.TryTransitionTo(f.Destination.ProfileKey));
        using var restored = f.Restore(session.CaptureSave());
        Assert.True(restored.State.Quests.ClaimsBuilding(site.Id, building));
        Assert.True(restored.State.Quests.TryGet("allocated-house", out var restoredQuest));
        Assert.Equal(resource.Text, restoredQuest!.Resources[0].Text);
        restored.State.Quests.Complete("allocated-house", "complete");
        Assert.False(restored.State.Quests.ClaimsBuilding(site.Id, building));
        Assert.True(restored.TryTransitionTo(f.Land.ProfileKey)); f.OpenBank(restored);
        Assert.Contains(restored.ReadPropertyPresentation().Offers, offer => offer.Key == key);
    }

    [Fact]
    public void Quest_claim_preserves_existing_owned_ordinary_house_entry_and_sale()
    {
        using Fixture f = new(questBuildingScenario: true, ordinaryHouse: true); f.OpenBank();
        var s = f.Session; var site = s.Site.ActiveSite!;
        var house = site.Exterior!.Buildings.Values.Single(building => HouseKey(site.Id, building)
            == DaggerfallPropertyStorageKey.ForHouse(f.HouseIdentity).Value);
        var offer = s.ReadPropertyPresentation().Offers.Single(value => value.Key == HouseKey(site.Id, house));
        Assert.True(s.State.Bank.TryCreditAccount(site.Region, ulong.Parse(offer.Price)));
        f.Submit(new { action = "property-buy", key = offer.Key });
        Assert.True(s.State.Property.OwnsHouse(f.HouseIdentity));
        StartBuilding(s, "owned-claim", house);
        Assert.True(s.State.Quests.ClaimsBuilding(site.Id, house));
        Assert.True(s.ReadPropertyPresentation().Offers.Single(value => value.Key == offer.Key).CanEnter);
        f.Submit(new { action = "property-enter", key = offer.Key });
        Assert.Equal(f.House.ProfileKey, s.Sites.ActiveProfile);
        using var restored = f.Restore(s.CaptureSave());
        Assert.True(restored.State.Property.OwnsHouse(f.HouseIdentity));
        Assert.True(s.TryTransitionTo(f.Land.ProfileKey)); f.OpenBank();
        f.Submit(new { action = "property-sell", key = offer.Key });
        Assert.False(s.State.Property.OwnsHouse(f.HouseIdentity));
        Assert.All(s.ReadPropertyPresentation().Offers.Where(value => value.Key == offer.Key), value =>
        { Assert.False(value.Owned); Assert.False(value.CanSell); Assert.False(value.CanEnter); });
    }

    [Fact]
    public void Quest_building_claim_filters_only_that_monthly_house_and_survives_unload_and_save()
    {
        using Fixture f = new(questBuildingScenario: true); f.OpenBank();
        var s = f.Session; var site = s.Site.ActiveSite!;
        var offers = s.ReadPropertyPresentation().Offers;
        var house = site.Exterior!.Buildings.Values.First(building => building.Source.BuildingType is >= 17 and <= 20
            && offers.Any(offer => offer.Key == HouseKey(site.Id, building)));
        var other = site.Exterior.Buildings.Values.First(building => building.Source.BuildingType is >= 17 and <= 20 && building.Id != house.Id);
        StartBuilding(s, "claimed-house", house);
        Assert.True(s.State.Quests.ClaimsBuilding(site.Id, house));
        Assert.False(s.State.Quests.ClaimsBuilding(site.Id, other));
        Assert.DoesNotContain(s.ReadPropertyPresentation().Offers, offer => offer.Key == HouseKey(site.Id, house));
        Assert.Contains(s.ReadPropertyPresentation().Offers, offer => offer.Key.StartsWith("house/")
            && site.Exterior.Buildings.Values.Any(building => building.Source.BuildingType is >= 17 and <= 20 && HouseKey(site.Id, building) == offer.Key));
        var explicitHouse = site.Exterior.Buildings.Values.Single(building => building.Source.Id == f.HouseIdentity.Building
            && building.Id.BlockX == f.HouseIdentity.BlockX && building.Id.BlockY == f.HouseIdentity.BlockY);
        StartBuilding(s, "claimed-explicit", explicitHouse);
        Assert.Contains(s.ReadPropertyPresentation().Offers, offer => offer.Key == HouseKey(site.Id, explicitHouse));
        Assert.True(s.TryTransitionTo(f.Destination.ProfileKey));
        Assert.True(s.State.Quests.ClaimsBuilding(site.Id, house));
        using var restored = f.Restore(s.CaptureSave());
        Assert.True(restored.State.Quests.ClaimsBuilding(site.Id, house));
        Assert.True(restored.TryTransitionTo(f.Land.ProfileKey));
        f.OpenBank(restored);
        Assert.DoesNotContain(restored.ReadPropertyPresentation().Offers, offer => offer.Key == HouseKey(site.Id, house));
        restored.State.Quests.Complete("claimed-house", "finished");
        Assert.False(restored.State.Quests.ClaimsBuilding(site.Id, house));
        Assert.Contains(restored.ReadPropertyPresentation().Offers, offer => offer.Key == HouseKey(site.Id, house));
        using var finished = f.Restore(restored.CaptureSave());
        Assert.False(finished.State.Quests.ClaimsBuilding(site.Id, house));
        Assert.True(finished.State.Quests.ClaimsBuilding(site.Id, explicitHouse));
    }

    [Fact]
    public void Building_claim_rebinding_and_multiple_active_quests_use_current_resource_owners()
    {
        using Fixture f = new(questBuildingScenario: true); var s = f.Session; var site = s.Site.ActiveSite!;
        var houses = site.Exterior!.Buildings.Values.Where(building => building.Source.BuildingType is >= 17 and <= 20).Take(2).ToArray();
        StartBuilding(s, "first", houses[0]); StartBuilding(s, "second", houses[0]);
        s.State.Quests.SetResource("first", new("_mondung_", Binding(s, houses[1])));
        Assert.True(s.State.Quests.ClaimsBuilding(site.Id, houses[0]));
        Assert.True(s.State.Quests.ClaimsBuilding(site.Id, houses[1]));
        s.State.Quests.Complete("second", "finished");
        Assert.False(s.State.Quests.ClaimsBuilding(site.Id, houses[0]));
        s.State.Quests.Fail("first", "failed");
        Assert.False(s.State.Quests.ClaimsBuilding(site.Id, houses[1]));
    }

    [Fact]
    public void Source_building_resolution_handles_origin_key_and_diagnoses_missing_or_malformed_claims()
    {
        using Fixture f = new(); var s = f.Session;
        var originSite = s.Site.Records.First(site => site.Exterior?.Buildings.ContainsKey(new(0, 0, 0)) == true
            && s.Site.Records.Count(other => other.MapId == site.MapId) == 1);
        var binding = DaggerfallQuestResourceBinding.PlaceBuilding(s.Site, originSite.MapId, 1 << 24);
        Assert.Equal(originSite.Id, binding.Places[0].Require());
        Assert.Equal(originSite.Exterior!.Buildings[new(0, 0, 0)].Source.Id.SourceKey, binding.Building!.SourceKey);
        Assert.Equal(0, binding.Building.Index); Assert.Equal(0, binding.Building.BlockX); Assert.Equal(0, binding.Building.BlockY);
        Assert.Contains("resolves to 0", Assert.Throws<ArgumentException>(() => DaggerfallQuestResourceBinding.PlaceBuilding(s.Site, int.MaxValue, 1)).Message);
        Assert.Contains("invalid building key", Assert.Throws<ArgumentException>(() => DaggerfallQuestResourceBinding.PlaceBuilding(s.Site, originSite.MapId, 0)).Message);
        Assert.Contains("no building", Assert.Throws<InvalidOperationException>(() => DaggerfallQuestResourceBinding.PlaceBuilding(s.Site, originSite.MapId, 0xffffff)).Message);
        var house = s.Site.ActiveSite!.Exterior!.Buildings.Values.First(); StartBuilding(s, "malformed", house);
        var saved = DaggerfallSavePayload.Read(s.CaptureSave());
        var quest = saved.Quests.Instances.Single(); var resource = quest.Resources.Single();
        var bad = saved with { Quests = saved.Quests with { Instances = [quest with { Resources = [resource with
            { Binding = resource.Binding with { Building = resource.Binding.Building! with { SourceKey = "MISSING.RMB" } } }] }] } };
        Assert.Contains("missing building", Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(bad))).Message);
    }

    private static string HouseKey(DaggerfallSiteId site, DaggerfallSiteBuildingSource building) =>
        DaggerfallPropertyStorageKey.ForHouse(new(site, building.Source.Id, building.Id.BlockX, building.Id.BlockY)).Value;
    private static DaggerfallQuestResourceBinding Binding(DaggerfallSession session, DaggerfallSiteBuildingSource building)
    {
        int key = (building.Id.BlockX << 16) | (building.Id.BlockY << 8) | building.Id.Index;
        return DaggerfallQuestResourceBinding.PlaceBuilding(session.Site, session.Site.ActiveSite!.MapId, key == 0 ? 1 << 24 : key);
    }
    private static void StartBuilding(DaggerfallSession session, string instance, DaggerfallSiteBuildingSource building) =>
        session.State.Quests.Start(new(instance, "00B00Y00.txt", "00B00Y00", DaggerfallQuestLifecycle.Active, null,
            [new("_mondung_", Binding(session, building))], []));

    [Fact]
    public void Teleport_ship_anchor_restores_owned_boarding_context_and_refuses_a_sold_ship()
    {
        using Fixture f = new(); f.OpenBank();
        Assert.True(f.Session.State.Bank.TryCreditAccount(f.Land.Site!.Value.Region, 100000));
        f.Submit(new { action = "property-buy", key = "ship/small" });
        var landPosition = f.Session.State.PlayerControl.Position;
        f.Submit(new { action = "transport-board-ship" });
        int sequence = 0;
        void Teleport(DaggerfallSession s, string choice)
        {
            var setting = new DaggerfallSpellEffectDefinition("teleport-test", 43, -1, 1, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1);
            s.State.Effects.Start(new($"ship-teleport-{sequence++}", "teleport", "spell.teleport", 1, 1, "teleport", "Magic", null, 1, 1,
                JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(setting, 1, 0, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)));
            s.ChooseTeleport(s.TeleportView!.Revision, choice);
        }
        Teleport(f.Session, "anchor");
        f.Submit(new { action = "transport-leave-ship" });
        using var restored = f.Restore(f.Session.CaptureSave());
        Teleport(restored, "recall");
        Assert.True(restored.State.Transport.OnShip); Assert.Equal(f.Small.ProfileKey, restored.Sites.ActiveProfile);
        Assert.Null(restored.Sites.ReturnProfile);
        Assert.Equal(f.Land.ProfileKey, restored.State.Transport.ShipReturnProfile);
        restored.Update(new ProductUpdate(OuterUpdate(80), [Ui("{\"action\":\"transport-leave-ship\"}")]));
        Assert.Equal(landPosition, restored.State.PlayerControl.Position);
        // The other session still has its unspent remembered ship location, but a sale does not
        // let that remembered location stand in for current property ownership.
        f.OpenBank(); f.Submit(new { action = "property-sell", key = "ship/small" });
        Teleport(f.Session, "recall");
        Assert.Contains("no longer owned", f.Session.Presentation.LastOutcome);
        Assert.Equal(f.Land.ProfileKey, f.Session.Sites.ActiveProfile);
        Assert.False(f.Session.State.Transport.OnShip);
        Assert.NotNull(DaggerfallSavePayload.Read(f.Session.CaptureSave()).TeleportAnchor);
        using var sold = f.Restore(f.Session.CaptureSave());
        Assert.False(sold.State.Property.OwnsShip);
    }

    [Theory]
    [InlineData("small", 100000UL)]
    [InlineData("large", 200000UL)]
    public void Ordinary_purchase_boards_actual_owned_ship_stores_and_restores_land_return(string type, ulong price)
    {
        using Fixture f = new();
        string key = $"ship/{type}";
        f.Submit(new { action = "property-buy", key });
        Assert.False(f.Session.State.Property.OwnsShip);
        f.OpenBank(); f.Submit(new { action = "property-buy", key });
        Assert.False(f.Session.State.Property.OwnsShip);
        Assert.Contains("could not cover", f.Session.Presentation.LastOutcome);
        Assert.True(f.Session.State.Bank.TryCreditAccount(f.Land.Site!.Value.Region, price));
        var funds = f.Session.State.Currency.Read();
        ulong bank = f.Session.State.Bank.BalanceForRegion(f.Land.Site.Value.Region);
        f.Submit(new { action = "property-buy", key });
        Assert.True(f.Session.State.Property.OwnsShip);
        Assert.Equal(bank + funds.Gold - price, f.Session.State.Bank.BalanceForRegion(f.Land.Site.Value.Region));
        ulong after = f.Session.State.Bank.BalanceForRegion(f.Land.Site.Value.Region);
        f.Submit(new { action = "property-buy", key });
        Assert.Equal(after, f.Session.State.Bank.BalanceForRegion(f.Land.Site.Value.Region));
        WorldPoint landPosition = f.Session.State.PlayerControl.Position!.Value;
        f.Submit(new { action = "transport-board-ship" });
        DaggerfallSiteProfile ship = type == "small" ? f.Small : f.Large;
        Assert.True(f.Session.State.Transport.OnShip);
        Assert.Equal(ship.ProfileKey, f.Session.Sites.ActiveProfile);
        Assert.Equal(ship.RequireAnchor("start").Position, f.Session.State.PlayerControl.Position);
        f.AddGold(4);
        DaggerfallPropertyStorageView storage = f.Session.ReadPropertyPresentation().Storage!;
        string coins = f.Session.State.Inventory.Read().Stacks.First(item => item.Definition.Value == "gold-piece").Id.Value;
        f.Submit(new { action = "property-put", key, item = $"stack:{coins}", amount = 4, revision = storage.Revision });
        Assert.Equal("4", Assert.Single(f.Session.ReadPropertyPresentation().Storage!.Items).Quantity);
        RulesetSavePayload saved = f.Session.CaptureSave();
        using DaggerfallSession restored = f.Restore(saved);
        Assert.Equal(ship.ProfileKey, restored.Sites.ActiveProfile);
        Assert.Equal("4", Assert.Single(restored.ReadPropertyPresentation().Storage!.Items).Quantity);
        restored.Update(new ProductUpdate(OuterUpdate(99), [Ui("{\"action\":\"transport-leave-ship\"}")]));
        Assert.False(restored.State.Transport.OnShip);
        Assert.Equal(f.Land.ProfileKey, restored.Sites.ActiveProfile);
        Assert.Equal(landPosition, restored.State.PlayerControl.Position);
        Assert.Null(restored.ReadPropertyPresentation().Storage);
        Assert.True(restored.State.Property.OwnsShip);
        // Storage is property-owned, so leaving does not retire its item contents.
        Assert.Equal(4UL, Assert.Single(restored.PropertyStorage.Read(new(key)).Stacks).Quantity);
    }

    [Fact]
    public void Purchased_house_gates_real_entrance_rest_storage_and_sale_retains_contents()
    {
        using Fixture f = new(); f.OpenBank();
        DaggerfallPropertyOfferView offer = f.Session.ReadPropertyPresentation().Offers.Single(value => value.Key == DaggerfallPropertyStorageKey.ForHouse(f.HouseIdentity).Value);
        Assert.True(f.Session.State.Bank.TryCreditAccount(f.Land.Site!.Value.Region, ulong.Parse(offer.Price)));
        f.Submit(new { action = "property-buy", key = offer.Key });
        Assert.True(f.Session.State.Property.OwnsHouse(f.HouseIdentity));
        Assert.True(f.Session.ReadPropertyPresentation().Offers.Single(value => value.Key == offer.Key).CanEnter);
        f.Submit(new { action = "property-enter", key = offer.Key });
        Assert.Equal(f.House.ProfileKey, f.Session.Sites.ActiveProfile);
        f.AddGold(2);
        var storage = f.Session.ReadPropertyPresentation().Storage!;
        string coins = f.Session.State.Inventory.Read().Stacks.First(item => item.Definition.Value == "gold-piece").Id.Value;
        f.Submit(new { action = "property-put", key = offer.Key, item = $"stack:{coins}", amount = 2, revision = storage.Revision });
        var saved = f.Session.CaptureSave();
        using var restored = f.Restore(saved);
        Assert.Equal("2", Assert.Single(restored.ReadPropertyPresentation().Storage!.Items).Quantity);
        restored.Update(new ProductUpdate(OuterUpdate(98), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":1}")]));
        Assert.Equal(3600, restored.RestView.ElapsedSeconds);
        Assert.True(f.Session.TryTransitionTo(f.Land.ProfileKey)); f.OpenBank();
        f.Submit(new { action = "property-sell", key = offer.Key });
        Assert.False(f.Session.State.Property.OwnsHouse(f.HouseIdentity));
        Assert.Equal(2UL, Assert.Single(f.Session.PropertyStorage.Read(new(offer.Key)).Stacks).Quantity);
    }

    [Fact]
    public void House_without_admitted_interior_cannot_be_purchased_or_entered()
    {
        using Fixture f = new(); f.OpenBank();
        var unavailable = f.Session.ReadPropertyPresentation().Offers.First(offer => offer.Key.StartsWith("house/")
            && offer.Key != DaggerfallPropertyStorageKey.ForHouse(f.HouseIdentity).Value);
        Assert.False(unavailable.CanBuy);
        Assert.True(f.Session.State.Bank.TryCreditAccount(f.Land.Site!.Value.Region, ulong.Parse(unavailable.Price)));
        ulong before = f.Session.State.Bank.BalanceForRegion(f.Land.Site.Value.Region);
        f.Submit(new { action = "property-buy", key = unavailable.Key });
        Assert.Empty(f.Session.State.Property.OwnedHouses);
        Assert.Equal(before, f.Session.State.Bank.BalanceForRegion(f.Land.Site.Value.Region));
        f.Submit(new { action = "property-enter", key = unavailable.Key });
        Assert.Equal(f.Land.ProfileKey, f.Session.Sites.ActiveProfile);
    }

    [Fact]
    public void Missing_ship_profile_and_forged_storage_actions_preserve_location_and_ownership()
    {
        using Fixture f = new(admitShips: false); f.OpenBank();
        Assert.True(f.Session.State.Bank.TryCreditAccount(f.Land.Site!.Value.Region, 100000));
        f.Submit(new { action = "property-buy", key = "ship/small" });
        f.Submit(new { action = "transport-board-ship" });
        Assert.False(f.Session.State.Transport.OnShip);
        Assert.Equal(f.Land.ProfileKey, f.Session.Sites.ActiveProfile);
        f.Submit(new { action = "property-put", key = "ship/small", item = "stack:missing", revision = "1", amount = 1 });
        Assert.Empty(f.Session.PropertyStorage.MaterializedKeys);
    }

    [Fact]
    public void Bank_open_uses_current_targeted_dialogue_and_rejects_stale_or_removed_provider()
    {
        using Fixture f = new();
        string revision = f.OpenBankDialogue();
        Assert.True(f.Session.ActivationView.Dialogue!.BankAvailable);
        f.Submit(new { action = "bank-open", revision = "stale" });
        Assert.False(f.Session.ReadPropertyPresentation().BankAvailable);
        f.Submit(new { action = "bank-open", revision });
        Assert.True(f.Session.ReadPropertyPresentation().BankAvailable);
        Assert.Null(f.Session.ActivationView.Dialogue);
        f.Session.State.Npcs.SetPresence(f.BankerId, DaggerfallNpcPresence.Removed);
        Assert.False(f.Session.ReadPropertyPresentation().BankAvailable);
        f.Submit(new { action = "bank-open", revision });
        Assert.False(f.Session.ReadPropertyPresentation().BankAvailable);
    }

    [Fact]
    public void Boarded_ship_travel_uses_land_origin_disembarks_and_preserves_ship_storage_on_reload()
    {
        using Fixture f = new(); f.OpenBank();
        Assert.True(f.Session.State.Bank.TryCreditAccount(f.Land.Site!.Value.Region, 100000));
        f.Submit(new { action = "property-buy", key = "ship/small" });
        f.AddGold(1000);
        f.Session.Site.Discover(f.Destination.Site!.Value);
        f.Submit(new { action = "travel-preview", region = f.Destination.Site.Value.Region,
            destination = f.Destination.Site.Value.Index, cautious = false, inn = true, ship = true });
        var landQuote = f.Session.ReadTravelPresentation().Quote!;
        f.Submit(new { action = "transport-board-ship" });
        f.Submit(new { action = "travel-preview", region = f.Destination.Site.Value.Region,
            destination = f.Destination.Site.Value.Index, cautious = false, inn = true, ship = true });
        var shipQuote = f.Session.ReadTravelPresentation().Quote!;
        Assert.Equal(landQuote.Origin, shipQuote.Origin);
        Assert.Equal(landQuote.TravelSeconds, shipQuote.TravelSeconds);
        Assert.Equal(0, shipQuote.ShipCost);
        f.Submit(new { action = "travel-accept", key = shipQuote.Identity, amount = shipQuote.TotalCost });
        Assert.Equal(DaggerfallTravelOutcome.Arrived, f.Session.State.Travel.LastResult!.Outcome);
        Assert.False(f.Session.State.Transport.OnShip);
        Assert.Equal(f.Destination.ProfileKey, f.Session.Sites.ActiveProfile);
        using var restored = f.Restore(f.Session.CaptureSave());
        Assert.Equal(f.Destination.ProfileKey, restored.Sites.ActiveProfile);
        Assert.True(restored.State.Property.OwnsShip);
        Assert.Null(restored.State.Transport.ShipReturnProfile);
    }

    [Fact]
    public void Restore_rejects_unadmitted_house_and_duplicate_ship_return_authority()
    {
        using Fixture f = new(); f.OpenBank();
        Assert.True(f.Session.State.Bank.TryCreditAccount(f.Land.Site!.Value.Region, 100000));
        f.Submit(new { action = "property-buy", key = "ship/small" });
        f.Submit(new { action = "transport-board-ship" });
        var saved = DaggerfallSavePayload.Read(f.Session.CaptureSave());
        var malformed = saved with { Site = saved.Site with { ReturnAnchor = saved.Transport.ShipReturnProfile!.Site,
            ReturnProfile = saved.Transport.ShipReturnProfile,
            ReturnPose = new(saved.Transport.ShipReturnX!.Value + 1, saved.Transport.ShipReturnY!.Value,
                saved.Transport.ShipReturnZ!.Value, 0, 0) } };
        Assert.Contains("duplicate", Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(malformed))).Message);
        var badHouse = DaggerfallHouseOwnershipSave.Capture(f.HouseIdentity) with { BuildingSourceKey = "ABSENT.RMB" };
        malformed = saved with { Property = saved.Property with { Houses = [badHouse] } };
        Assert.Contains("admitted house", Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(malformed))).Message);
        var nonTown = TestPayload.Definitions.Locations.Records.First(site => site.Exterior is not null
            && site.Kind is not (DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage)
            && site.Exterior.Buildings.Values.Any(building => building.Source.BuildingType is 1 or >= 17 and <= 20));
        var nonTownBuilding = nonTown.Exterior!.Buildings.Values.First(building => building.Source.BuildingType is 1 or >= 17 and <= 20);
        var invalidHouse = DaggerfallHouseOwnershipSave.Capture(new(nonTown.Id, nonTownBuilding.Source.Id,
            nonTownBuilding.Id.BlockX, nonTownBuilding.Id.BlockY));
        foreach (bool retained in new[] { false, true })
        {
            malformed = saved with { Property = saved.Property with {
                Houses = retained ? [] : [invalidHouse], RetainedHouses = retained ? [invalidHouse] : [] } };
            Assert.Contains("admitted house", Assert.Throws<ArgumentException>(() => f.Resolve(malformed)).Message);
        }
        malformed = saved with { Site = saved.Site with {
            ActiveProfile = saved.Site.ActiveProfile! with { Kind = (int)DaggerfallWorldProfileKind.Dungeon },
            ReturnAnchor = saved.Site.Active, ReturnProfile = saved.Site.ActiveProfile,
            ReturnPose = new(1, 1, 1, 0, 0) } };
        Assert.Contains("ship interior", Assert.Throws<ArgumentException>(() => f.Resolve(malformed)).Message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallDefinitions definitions = TestPayload.Definitions;
        private readonly List<string> releases = [];
        private readonly DaggerfallSiteProfiles profiles;
        private readonly DaggerfallBlocksSnapshot blocks;
        private readonly ResolvedCompositionIdentity identity;
        private ulong step = 1;
        internal DaggerfallSession Session { get; }
        internal DaggerfallSiteProfile Land { get; }
        internal DaggerfallSiteProfile House { get; }
        internal DaggerfallSiteProfile Destination { get; }
        internal long BankerId { get; private set; }
        internal DaggerfallSiteProfile Small { get; }
        internal DaggerfallSiteProfile Large { get; }
        internal DaggerfallHouseIdentity HouseIdentity { get; }
        internal Fixture(bool admitShips = true, bool questBuildingScenario = false, bool ordinaryHouse = false, bool automaticQuest = false)
        {
            if (automaticQuest) definitions = QuestPlaceAllocationTests.Definitions();
            string root = TestData.RepositoryRoot;
            var content = FullContent(root); var source = ReadInputs(root);
            identity = GameCompositionResolver.Resolve(content, new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
            blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.blocks.json")));
            var site = definitions.Locations.Records.First(value => value.Kind == DaggerfallSiteKind.TownCity
                && value.Exterior is { PortTownAndUnknown: > 0 } exterior
                && exterior.Buildings.Values.Any(building => building.Source.BuildingType == 1 && building.ModelRadius > 0)
                && (!questBuildingScenario || (exterior.Buildings.Values.Count(building => building.Source.BuildingType == 1)
                    < Math.Min(exterior.Buildings.Count / 10, 20)
                    && exterior.Buildings.Values.Count(building => building.Source.BuildingType is >= 17 and <= 20 && building.ModelRadius > 0) >= 2))
                && (!automaticQuest || EligibleLastHouse(value)));
            bool EligibleLastHouse(DaggerfallSiteRecord candidate)
            {
                var last = candidate.Exterior!.Buildings.Values.Where(building => building.Source.BuildingType is >= 17 and <= 20)
                    .OrderBy(building => building.Id.BlockY).ThenBy(building => building.Id.BlockX).ThenBy(building => building.Id.Index).LastOrDefault();
                return last is { ModelRadius: > 0 } && last.Source.FactionId is not (42 or 108)
                    && blocks.QuestMarkers.TryGetValue(new(last.Source.Id.SourceKey, last.Source.Id.Index), out var markers) && markers.Count > 0;
            }
            var ordered = site.Exterior!.Buildings.Values.Where(value => value.ModelRadius > 0
                && (ordinaryHouse ? value.Source.BuildingType is >= 17 and <= 20 : value.Source.BuildingType == 1))
                .OrderBy(value => value.Id.BlockY).ThenBy(value => value.Id.BlockX).ThenBy(value => value.Id.Index);
            var building = ordinaryHouse ? ordered.Last() : ordered.First();
            HouseIdentity = new(site.Id, building.Source.Id, building.Id.BlockX, building.Id.BlockY);
            House = new(new ProjectFacts(new WorldPoint(3, 1, 1), new Dictionary<long, AuthoredActor>()), source.SpatialArtifact,
                source.StaticMesh, source.WorldAppearance, source.InitialLook, source.Materials, new Dictionary<long, NormalizedActorSprite>(),
                source.MobileSprites, source.Audio, source.ClassicPresentation, site.Id, profileKind: DaggerfallWorldProfileKind.Interior,
                logicalProfileId: "property-house", interiorBuilding: new(building.Id.BlockX, building.Id.BlockY, building.Source.Id, building.Source.BuildingType, building.Source.FactionId));
            Land = new(new ProjectFacts(new WorldPoint(1, 1, 1), new Dictionary<long, AuthoredActor>()), source.SpatialArtifact,
                source.StaticMesh, source.WorldAppearance, source.InitialLook, source.Materials, new Dictionary<long, NormalizedActorSprite>(),
                source.MobileSprites, source.Audio, source.ClassicPresentation, site.Id, profileKind: DaggerfallWorldProfileKind.Exterior,
                logicalProfileId: "property-land", portals: [new("house-entrance", new(1, 1, 1), 2, House.ProfileKey.LogicalId)]);
            var destination = definitions.Locations.Records.Where(value => value.Kind == DaggerfallSiteKind.TownCity && value.Id != site.Id)
                .OrderBy(value => Math.Abs(value.MapPixelX - site.MapPixelX) + Math.Abs(value.MapPixelY - site.MapPixelY)).First();
            Destination = new(new ProjectFacts(new WorldPoint(1, 1, 1), new Dictionary<long, AuthoredActor>()), source.SpatialArtifact,
                source.StaticMesh, source.WorldAppearance, source.InitialLook, source.Materials, new Dictionary<long, NormalizedActorSprite>(),
                source.MobileSprites, source.Audio, source.ClassicPresentation, destination.Id,
                profileKind: DaggerfallWorldProfileKind.Exterior, logicalProfileId: "property-destination");
            Small = ReadProfile(root, content, definitions, "daggerfall.small-ship.json");
            Large = ReadProfile(root, content, definitions, "daggerfall.large-ship.json");
            profiles = new(admitShips ? [Land, House, Small, Large, Destination] : [Land, House, Destination]);
            Session = Create(null);
        }
        private DaggerfallSession Create(RulesetSavePayload? saved)
        {
            ContentFake content = new(releases);
            foreach (var profile in new[] { Land, House, Small, Large, Destination }) PopulateContent(content, profile);
            var spatial = SpatialFake.Create(Land.SpatialArtifact.Sha256, releases);
            spatial.KeepPosition = true;
            var engine = EngineContextFake.Create(content, spatial.Service,
                new AppearanceFake(releases), random: LodgingRandom.Create());
            DaggerfallSessionComposition composition = new(definitions, Land, DaggerfallTuning.Defaults, identity) { Profiles = profiles, Blocks = blocks };
            return saved is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, saved);
        }
        internal void OpenBank(DaggerfallSession? session = null)
        {
            session ??= Session;
            var site = session.Site.ActiveSite!;
            var npcSite = new DaggerfallNpcSite(site.Region, site.Name, string.Empty);
            long id = session.State.Npcs.RegisterStable(DaggerfallNpcKind.Static, "property-test.banker", npcSite,
                new("Breton", "Male", 0, 0, 0, 0), "banker", ["banking"]);
            Assert.True(session.TryOpenBank(new(id, npcSite, "banking")));
        }
        internal string OpenBankDialogue()
        {
            var site = Session.Site.ActiveSite!;
            BankerId = Session.State.Npcs.RegisterCivilian(
                new(site.Region, site.Name, string.Empty), new("Breton", "Male", 0, 0, 0, 0), "banker", ["talk", "banking"]);
            Session.MaterializeNpcActor(BankerId, new ActorPose(Session.State.PlayerControl.Position!.Value, 0));
            var target = Session.Dialogue.NpcTargets().Single(value => value.Identity.Value == (ulong)BankerId);
            Assert.True(Session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
            return Session.ActivationView.Dialogue!.Revision;
        }
        internal void AddGold(ulong amount)
        {
            var stack = InventoryStackId.Parse($"property.gold.{step}");
            Session.State.Inventory.Grant(new(new InventoryItemId("gold-piece"), stack, amount));
            var item = definitions.RequireItem(new("gold-piece"));
            Session.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Player, stack, DaggerfallItemInstanceMetadata.Default(item, DaggerfallItemOwner.Player));
        }
        internal void Submit(object action) => Session.Update(new ProductUpdate(OuterUpdate(step++), [Ui(JsonSerializer.Serialize(action))]));
        internal DaggerfallSession Restore(RulesetSavePayload save) => Create(save);
        internal void Resolve(DaggerfallSavePayload save) => save.ResolveRestore(definitions, Land, profiles);
        public void Dispose() => Session.Dispose();
    }
}
