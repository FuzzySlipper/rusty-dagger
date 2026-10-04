using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Consumer checks for the Charing bank and knightly-order profiles.  The callers discover the
/// providers through the normal admitted site lifecycle; no registry row, actor, or service is
/// created by the fixture.
/// </summary>
public sealed class SourceBackedGuildBankSessionTests
{
    [Fact]
    public void Source_bank_teller_opens_regional_account_and_preserves_ship_purchase_through_boarding_save()
    {
        using SourceBackedGuildBankSessionFixture fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.BankProfile);

        DaggerfallNpc teller = SourceNpc(session, faction: 510, service: "banking");
        Assert.Equal("bank teller", teller.Role);
        Assert.Equal(fixture.BankProfile.Site!.Value.Region, teller.Site.Region);
        OpenSourceNpc(session, teller);
        DaggerfallDialogueView dialogue = Assert.IsType<DaggerfallDialogueView>(session.ActivationView.Dialogue);
        Assert.True(dialogue.BankAvailable);
        Assert.Contains(dialogue.Topics, topic => topic.Id == "directions");

        Submit(session, 1, new { action = "bank-open", revision = dialogue.Revision });
        Assert.True(session.ReadPropertyPresentation().BankAvailable);

        AddGold(session, 1_000_000);
        Submit(session, 2, new { action = "currency-deposit-gold", amount = 200 });
        Assert.Equal(200UL, session.State.Bank.BalanceForRegion(fixture.BankProfile.Site.Value.Region));

        DaggerfallPropertyOfferView ship = Assert.Single(session.ReadPropertyPresentation().Offers,
            offer => offer.Key == "ship/small");
        Assert.True(ship.CanBuy);
        Submit(session, 3, new { action = "property-buy", key = ship.Key });
        Assert.True(session.State.Property.OwnsShip);

        // Boarding is available from the source exterior through the ordinary site transition. The
        // interior provider remains the authority for the purchase; the ship owner handles boarding.
        Assert.True(session.TryTransitionTo(fixture.ExteriorProfile.ProfileKey));
        Submit(session, 4, new { action = "transport-board-ship" });
        Assert.True(session.State.Transport.OnShip);

        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        using DaggerfallSession restored = fixture.Restore(DaggerfallSavePayload.Encode(saved));
        Assert.True(restored.State.Property.OwnsShip);
        Assert.True(restored.State.Transport.OnShip);
        Assert.Equal(200UL, restored.State.Bank.BalanceForRegion(fixture.BankProfile.Site.Value.Region));
        Assert.Equal(fixture.SmallShipProfile.ProfileKey, restored.Sites.ActiveProfile);
    }

    [Fact]
    public void Source_knightly_order_providers_grant_rank_armor_once_and_persist_the_claim()
    {
        using SourceBackedGuildBankSessionFixture fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.KnightlyProfile);

        DaggerfallNpc armorer = SourceNpc(session, faction: 845, service: "armor");
        DaggerfallNpc steward = SourceNpc(session, faction: 848, service: "house");
        DaggerfallNpc quester = SourceNpc(session, faction: 846, service: "quests");
        Assert.Equal("armorer", armorer.Role);
        Assert.Equal("property steward", steward.Role);
        Assert.Contains("talk", quester.Services);
        Assert.Equal(368, fixture.KnightlyProfile.InteriorBuilding!.FactionId);

        // This is the canonical social owner setup used by a real player joining the order. The
        // source provider remains the NPC admitted from KDRAAL01.RMB; no test NPC is registered.
        _ = session.State.Social.JoinGuild(368, 0);
        OpenSourceNpc(session, armorer);
        DaggerfallDialogueView dialogue = Assert.IsType<DaggerfallDialogueView>(session.ActivationView.Dialogue);
        DaggerfallDialogueTopicOption armorTopic = Assert.Single(dialogue.Topics, topic => topic.Id == "armor");
        Submit(session, 5, new { action = "dialogue-topic", revision = dialogue.Revision, topic = armorTopic.Id });

        dialogue = Assert.IsType<DaggerfallDialogueView>(session.ActivationView.Dialogue);
        Assert.Contains(dialogue.Topics, topic => topic.Id == "armor");
        DaggerfallDialogueTopicOption armorChoice = dialogue.Topics.First(topic => topic.Id == "armor");
        Assert.False(string.IsNullOrWhiteSpace(armorChoice.Key));
        Submit(session, 6, new { action = "dialogue-topic", revision = dialogue.Revision,
            topic = armorChoice.Id, key = armorChoice.Key });
        Assert.Contains(session.State.Inventory.Read().UniqueItems,
            item => item.Definition.Value.StartsWith("guild.knightly.armor.368.0.", StringComparison.Ordinal));
        Assert.True(session.State.KnightlyClaims.Read(368).HasArmorClaim(0));

        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        using DaggerfallSession restored = fixture.Restore(DaggerfallSavePayload.Encode(saved));
        Assert.True(restored.State.KnightlyClaims.Read(368).HasArmorClaim(0));
        int uniqueArmorCount = restored.State.Inventory.Read().UniqueItems.Count(
            item => item.Definition.Value.StartsWith("guild.knightly.armor.368.0.", StringComparison.Ordinal));

        // The same admitted armorer can be revisited after reload, but the canonical claim owner
        // rejects the already-used rank and does not materialize a second reward.
        DaggerfallNpc restoredArmorer = SourceNpc(restored, faction: 845, service: "armor");
        OpenSourceNpc(restored, restoredArmorer);
        DaggerfallDialogueView restoredDialogue = Assert.IsType<DaggerfallDialogueView>(restored.ActivationView.Dialogue);
        Submit(restored, 7, new { action = "dialogue-topic", revision = restoredDialogue.Revision, topic = "armor" });
        restoredDialogue = Assert.IsType<DaggerfallDialogueView>(restored.ActivationView.Dialogue);
        Assert.Contains(restoredDialogue.Topics, topic => topic.Id == "armor");
        string? repeatedChoice = restoredDialogue.Topics.First(topic => topic.Id == "armor").Key;
        Submit(restored, 8, new { action = "dialogue-topic", revision = restoredDialogue.Revision,
            topic = "armor", key = repeatedChoice });
        Assert.Equal(uniqueArmorCount, restored.State.Inventory.Read().UniqueItems.Count(
            item => item.Definition.Value.StartsWith("guild.knightly.armor.368.0.", StringComparison.Ordinal)));

        // Rank nine is the source gate for the house entitlement. The existing property owner is
        // then responsible for choosing and persisting the admitted local house.
        for (int day = 1; day <= 9; day++) _ = restored.State.Social.PromoteGuild(368, day);
        DaggerfallNpc restoredSteward = SourceNpc(restored, faction: 848, service: "house");
        OpenSourceNpc(restored, restoredSteward);
        DaggerfallDialogueView houseDialogue = Assert.IsType<DaggerfallDialogueView>(restored.ActivationView.Dialogue);
        Assert.Contains(houseDialogue.Topics, topic => topic.Id == "house");
        Submit(restored, 9, new { action = "dialogue-topic", revision = houseDialogue.Revision, topic = "house" });
        Assert.NotEmpty(restored.State.Property.OwnedHouses);
        DaggerfallHouseIdentity awardedHouse = Assert.Single(restored.State.Property.OwnedHouses);

        using DaggerfallSession reloaded = fixture.Restore(restored.CaptureSave());
        Assert.Contains(reloaded.State.Property.OwnedHouses, house => house == awardedHouse);
    }

    private static DaggerfallNpc SourceNpc(DaggerfallSession session, int faction, string service) =>
        Assert.Single(session.State.Npcs.All, npc => npc.Kind == DaggerfallNpcKind.Static
            && npc.Presence == DaggerfallNpcPresence.Active && npc.Appearance.FactionId == faction
            && npc.Services.Contains(service, StringComparer.Ordinal));

    private static void OpenSourceNpc(DaggerfallSession session, DaggerfallNpc npc)
    {
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(),
            value => value.Identity.Value == checked((ulong)npc.DurableId));
        DaggerfallActivationOutcome outcome = session.Dialogue.ActivateNpc(
            new(DaggerfallActivationMode.Talk, target));
        Assert.True(outcome.Applied, outcome.Message);
    }

    private static void Submit(DaggerfallSession session, ulong step, object action) =>
        session.Update(new ProductUpdate(OuterUpdate(step), [Ui(JsonSerializer.Serialize(action))]));

    private static void AddGold(DaggerfallSession session, ulong amount)
    {
        DaggerfallItemFactory factory = new(TestPayload.Definitions, RandomMinimum.Create());
        factory.Materialize(factory.Create(new("Currency", "source.guild-bank.test.gold", DaggerfallItemOwner.Player,
            Quantity: amount, TemplateIndex: 276)), session.State.Inventory, session.State.ItemInstances,
            InventoryStackId.Parse("source.guild-bank.test.gold"));
    }
}

internal sealed class SourceBackedGuildBankSessionFixture : IDisposable
{
    private const string SiteRole = "daggerfall.site";
    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallSiteProfile[] _sites;
    private readonly DaggerfallSiteProfiles _profiles;
    private readonly DaggerfallSessionComposition _composition;

    internal DaggerfallSiteProfile BankProfile { get; }
    internal DaggerfallSiteProfile KnightlyProfile { get; }
    internal DaggerfallSiteProfile ExteriorProfile { get; }
    internal DaggerfallSiteProfile SmallShipProfile { get; }

    private SourceBackedGuildBankSessionFixture(
        DaggerfallDefinitions definitions,
        DaggerfallSiteProfile[] sites,
        DaggerfallSiteProfiles profiles,
        DaggerfallSessionComposition composition,
        DaggerfallSiteProfile bank,
        DaggerfallSiteProfile knightly,
        DaggerfallSiteProfile exterior,
        DaggerfallSiteProfile smallShip)
    {
        _definitions = definitions;
        _sites = sites;
        _profiles = profiles;
        _composition = composition;
        BankProfile = bank;
        KnightlyProfile = knightly;
        ExteriorProfile = exterior;
        SmallShipProfile = smallShip;
    }

    internal static SourceBackedGuildBankSessionFixture Create()
    {
        string root = TestData.RepositoryRoot;
        ProductContent content = FullContent(root);
        ResolvedGameComposition resolved = GameCompositionResolver.Resolve(content,
            new GameBundleId("daggerfall.privateers-hold")).RequireComposition();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile[] sites = [.. resolved.ContentPacks
            .Where(pack => pack.Role == new ContentPackRoleId(SiteRole))
            .Select(pack => DaggerfallSiteContent.Read(content, pack.Payload, definitions))];
        DaggerfallSiteProfiles profiles = new(sites);
        DaggerfallSiteProfile bank = SourceProfile(sites, "BANKAL01.RMB", 1, 5, 17, 3, 0);
        DaggerfallSiteProfile knightly = SourceProfile(sites, "KDRAAL01.RMB", 3, 2, 14, 11, 368);
        DaggerfallSiteProfile exterior = Assert.Single(sites, profile =>
            profile.ProfileKind == DaggerfallWorldProfileKind.Exterior && profile.Site == bank.Site);
        DaggerfallSiteProfile smallShip = Assert.Single(sites, profile =>
            profile.ProfileKey.LogicalId == "small-ship");

        ContentPack blocksPack = resolved.ContentPacks.Single(pack => pack.Role == new ContentPackRoleId("daggerfall.blocks"));
        DaggerfallBlocksSnapshot blocks = DaggerfallBlocksContent.Read(blocksPack.Payload);
        blocks.AdmitLocations(definitions.Locations);
        DaggerfallSessionComposition composition = new(definitions, bank, DaggerfallTuning.Defaults, resolved.Identity)
        {
            Profiles = profiles,
            Blocks = blocks,
        };
        return new(definitions, sites, profiles, composition, bank, knightly, exterior, smallShip);
    }

    private static DaggerfallSiteProfile SourceProfile(IEnumerable<DaggerfallSiteProfile> sites, string sourceKey,
        int blockX, int blockY, int buildingIndex, int buildingType, int factionId)
    {
        DaggerfallSiteProfile[] matches = [.. sites.Where(profile =>
            profile.ProfileKind == DaggerfallWorldProfileKind.Interior
            && profile.InteriorBuilding is { } building
            && building.Building.SourceKey == sourceKey
            && building.BlockX == blockX && building.BlockY == blockY
            && building.Building.Index == buildingIndex
            && building.BuildingType == buildingType && building.FactionId == factionId)];
        return Assert.Single(matches);
    }

    internal DaggerfallSession Start(DaggerfallSiteProfile profile)
    {
        List<string> releases = [];
        ContentFake content = new(releases);
        foreach (DaggerfallSiteProfile site in _sites) PopulateContent(content, site);
        SpatialFake spatial = SpatialFake.Create(profile.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        return DaggerfallSession.StartNew(engine.Context, _composition with { StartSite = profile });
    }

    internal DaggerfallSession Restore(RulesetSavePayload saved)
    {
        DaggerfallSavePayload payload = DaggerfallSavePayload.Read(saved);
        DaggerfallSiteProfile active = payload.Site.ActiveProfile is { } profile
            ? _profiles.Require(profile.Require())
            : payload.Site.Active is { } activeSite
                ? _profiles.RequireUniqueSite(new(activeSite.Region!.Value, activeSite.Index!.Value))
                : throw new InvalidOperationException("A source-backed save must name its active site.");
        List<string> releases = [];
        ContentFake content = new(releases);
        foreach (DaggerfallSiteProfile site in _sites) PopulateContent(content, site);
        SpatialFake spatial = SpatialFake.Create(active.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        return DaggerfallSession.Restore(engine.Context, _composition with { StartSite = active }, saved);
    }

    public void Dispose() { }
}
