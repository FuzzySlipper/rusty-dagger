using System.Numerics;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Variants;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The developer town-start bundle begins an ordinary quick-started game in Charing's streets in the
/// morning, with gold for its services, and the general store's shopkeeper is reached through its door.
/// </summary>
public sealed class TownStartBundleTests
{
    private const string Bundle = "daggerfall.town-start";
    private static readonly DaggerfallSiteBuildingId GeneralStore = new(2, 1, 0); // GENRAL01

    [Fact]
    public void A_quick_started_town_game_begins_in_charing_and_walks_into_an_open_shop_with_a_merchant()
    {
        string root = TestData.RepositoryRoot;
        ProductContent full = FullContent(root);
        DaggerfallSiteProfile exterior = ReadProfile(root, full, TestPayload.Definitions, "daggerfall.charing-exterior.json");
        DaggerfallSiteProfile store = ReadProfile(root, full, TestPayload.Definitions, "daggerfall.charing-interior-2-1-0.json");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, store);
        // The town's exterior streams the assembled exteriors around it.
        foreach (DaggerfallSiteProfile near in new AssembledWorld().NearbyExteriors(exterior.Site!.Value)) AssembledWorldRun.Admit(content, near);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new(videosEnabled: false, variants: DaggerfallVariants.All);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, full, input), ruleset, new GameBundleId(Bundle));
        product.Start();
        Assert.Equal(Bundle, engine.PublishedNested("bundles", "current"));

        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"character-quick-start","name":"","race":"breton","gender":"male","faceIndex":0,"reflexes":2,"career":"class16"}""")]));
        DaggerfallSession game = ruleset.RequireSession();
        Assert.NotEqual(ProductMode.Title, product.Mode);
        Assert.Equal(exterior.ProfileKey, game.Sites.ActiveProfile);
        Assert.Equal(new DaggerfallSiteId(17, 4), game.Site.ActiveSite!.Id);
        // The start pack's gold, plus whatever the quick-started background grants.
        Assert.True(game.State.Currency.Read().Gold >= 5000UL, $"{game.State.Currency.Read().Gold} gold");
        Assert.Empty(game.State.Quests.All);
        Assert.Equal(9, DaggerfallSavePayload.Read(game.CaptureSave()).Calendar.Hour);

        // Facing the store's door with only it seen, an ordinary interaction enters during opening hours.
        DaggerfallDoorView door = game.Doors.Read(exterior.Doors.First(value => value.ExteriorBuilding == GeneralStore).Id);
        game.State.PlayerControl.MoveTo(door.Pose.Translation + Vector3.UnitZ);
        game.State.PlayerControl.YawRadians = 0;
        game.State.PlayerControl.PitchRadians = 0;
        perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(target => new PerceptionPair(1, target.Entity, 1, 1,
            target.Entity == door.Entity.Value ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, 1))]);
        spatial.FloorHit = request => request.Direction.Y < -.5 ? default : new SpatialHit
        {
            Present = true, Kind = SpatialHitKind.Entity, Entity = door.Entity.Value, Point = door.Pose.Translation, Distance = 1,
        };
        game.Update(new ProductUpdate(OuterUpdate(2), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.True(game.ActivationView.Applied, game.ActivationView.Message);
        var greeting = game.DungeonTextProjection!;
        game.Update(new ProductUpdate(OuterUpdate(3), [Ui(System.Text.Json.JsonSerializer.Serialize(new
            { action = "dungeon-text-close", item = greeting.ActionId, revision = greeting.Revision.ToString() }))]));
        Assert.Equal(DaggerfallWorldProfileIds.Interior(exterior.ProfileKey.Site, GeneralStore), game.Sites.ActiveProfile);
        Assert.Empty(game.State.Crime.Incidents);

        DaggerfallNpc merchant = Assert.Single(game.State.Npcs.All,
            npc => npc.Kind == DaggerfallNpcKind.Static && npc.Profile == game.Sites.ActiveProfile && npc.Services.Contains("shop"));
        Assert.Equal(DaggerfallServiceDenial.None, game.State.Services.ProviderAvailable(new(merchant.DurableId, merchant.Site, "shop")));
    }
}
