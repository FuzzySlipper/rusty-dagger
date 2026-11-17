using System.Numerics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The shipped scenario quest Market Closed (<c>MKTCLOSE</c>), over the quests pack's world variant: Charing's market lane
/// is barricaded and the shops' signs taken in from the next build of the town, through a save and a load, until dawn
/// opens it again.
/// </summary>
public sealed class QuestWorldVariantSessionTests
{
    private static readonly DaggerfallSiteId Charing = new(17, 4);
    private static readonly DaggerfallWorldProfileKey Exterior = DaggerfallWorldProfileIds.Exterior(Charing);
    private const string Closed = "closed";
    private const string Quest = "MKTCLOSE.txt";

    private static readonly SharedFixture<AssembledWorld> Shared = new(() => new AssembledWorld());

    [Fact]
    public void Market_closed_bars_charings_market_lane_from_its_next_build_through_a_save_and_load_until_dawn()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallSiteRecord charing = world.Definitions.Locations.Records.Single(record => record.Id == Charing);
        using AssembledWorldRun start = world.Start(Charing, variant: Closed);
        start.Travel(charing);
        DaggerfallSiteProfile open = start.Session.Sites.Projection.Inputs;
        Assert.Same(world.Profiles.Require(Exterior), open);

        // Started in Charing, the quest's warden waits in one of its taverns and its strongbox lies in one of its houses.
        start.Session.State.Quests.StartNewGame([Quest]);
        Advance(start.Session);
        var quest = Assert.Single(start.Session.State.Quests.All, value => value.SourceFile == Quest);
        Assert.All(new[] { "inn", "hideout" }, place => Assert.Equal(new DaggerfallSiteIdSave(Charing.Region, Charing.Index),
            Assert.Single(Assert.Single(quest.Resources, resource => resource.Symbol == place).Binding.Places)));
        Assert.Equal(new DaggerfallWorldVariantSave(Charing.Region, Charing.Index, Closed),
            Assert.Single(DaggerfallSavePayload.Read(start.Session.CaptureSave()).WorldVariants));

        // The town the player stands in keeps what it was built with; its next build is barred. (A journey by the travel
        // window would take days, and the quest would open the lane at dawn on the way: the developer's travel is instant.)
        Assert.Same(open, start.Session.Sites.Projection.Inputs);
        IDeveloperPlacementCommands travel = start.Session.Commands<IDeveloperPlacementCommands>();
        Assert.True(travel.Travel("17/179").Succeeded);
        Assert.True(travel.Travel("Charing").Succeeded);
        AssertClosed(open, start.Session.Sites.Projection.Inputs);

        using AssembledWorldRun restored = world.Restore(start.Session.CaptureSave(), Charing, variant: Closed);
        Assert.Equal(Exterior, restored.Session.Sites.ActiveProfile);
        AssertClosed(open, restored.Session.Sites.Projection.Inputs);

        // The night passes: at dawn the quest takes the barricade down and ends; the town's next build is open.
        var dawn = Assert.Single(Assert.Single(restored.Session.State.Quests.All, value => value.SourceFile == Quest).Clocks, clock => clock.Symbol == "dawn");
        restored.Session.AdvanceElapsedTime(dawn.RemainingSeconds);
        Advance(restored.Session);
        Assert.Equal("end quest", Assert.Single(restored.Session.State.Quests.All, value => value.SourceFile == Quest).Outcome);
        Assert.Empty(DaggerfallSavePayload.Read(restored.Session.CaptureSave()).WorldVariants);
        IDeveloperPlacementCommands back = restored.Session.Commands<IDeveloperPlacementCommands>();
        Assert.True(back.Travel("17/179").Succeeded);
        Assert.True(back.Travel("Charing").Succeeded);
        Assert.Same(open, restored.Session.Sites.Projection.Inputs);
    }

    /// <summary>
    /// The variant redraws only the market's block, cell (3, 3): the shops' nine hanging signs are taken in, two carts
    /// end to end bar the west lane with crates behind them and the reeve's notice post before them, and it collides and navigates by the variant's own artifact.
    /// Everything else, doors and portals included, is the town as the source places it.
    /// </summary>
    private static void AssertClosed(DaggerfallSiteProfile open, DaggerfallSiteProfile closed)
    {
        Assert.NotSame(open, closed);
        Assert.Equal(open.ProfileKey, closed.ProfileKey);
        static bool InMarket(DaggerfallSiteMesh mesh) =>
            mesh.Pose.Translation is { } at && at.X is >= 307.2F and < 409.6F && at.Z is <= -307.2F and > -409.6F;
        static string[] Models(DaggerfallSiteProfile profile, bool market) =>
            [.. profile.Geometry.Meshes.Where(mesh => InMarket(mesh) == market).Select(mesh => mesh.ModelId ?? "").Order(StringComparer.Ordinal)];
        Assert.Equal(Models(open, market: false), Models(closed, market: false));
        static bool Sign(string model) => model.StartsWith("437", StringComparison.Ordinal);
        static bool Barricade(string model) => model is "41214" or "41239" or "41832" or "41833" or "41834" or "41739";
        Assert.Equal(9, Models(open, market: true).Count(Sign));
        Assert.DoesNotContain(Models(closed, market: true), Sign);
        Assert.DoesNotContain(Models(open, market: true), Barricade);
        Assert.Equal(6, Models(closed, market: true).Count(Barricade));
        Assert.Equal(Models(open, market: true).Where(model => !Sign(model)), Models(closed, market: true).Where(model => !Barricade(model)));
        // Across the lane between the two buildings the player starts between, west of the square.
        Vector3[] barricade = [.. closed.Geometry.Meshes.Where(mesh => InMarket(mesh) && Barricade(mesh.ModelId ?? "")).Select(mesh => mesh.Pose.Translation)];
        Assert.All(barricade, piece => Assert.InRange(piece.X, 307.2F + 17F, 307.2F + 24F));
        Assert.All(barricade, piece => Assert.InRange(piece.Z, -307.2F - 58F, -307.2F - 51F));

        DaggerfallSiteSpatialPart market = closed.Geometry.Spatial.Single(part => part.Id == "block/3/3");
        Assert.DoesNotContain(open.Geometry.Spatial, part => part.Path == market.Path);
        Assert.Equal(open.Geometry.Spatial.Where(part => part.Id != "block/3/3"), closed.Geometry.Spatial.Where(part => part.Id != "block/3/3"));
        Assert.Equal(open.Doors.Select(door => door.Id), closed.Doors.Select(door => door.Id));
        Assert.Equal(open.Portals.Select(portal => portal.Id), closed.Portals.Select(portal => portal.Id));
    }

    private static void Advance(DaggerfallSession session) => QuestDiseaseTests.Advance(session);
}
