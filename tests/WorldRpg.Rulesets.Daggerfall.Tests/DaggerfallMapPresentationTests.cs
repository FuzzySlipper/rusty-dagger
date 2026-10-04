using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using System.Text.Json.Nodes;
using System.Text.Json;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallMapPresentationTests
{
    [Fact]
    public void Dungeon_projection_only_exposes_persisted_discovery_and_current_player()
    {
        DaggerfallSiteProfile profile = ReadInputs(TestData.RepositoryRoot);
        DaggerfallDungeonMapContent content = profile.DungeonMap!;
        DaggerfallDungeonDiscovery discovery = new(profile.ProfileKey, content);
        WorldPoint player = new(1, 2, 3);
        var empty = DaggerfallMapProjection.Dungeon(profile, discovery, player, .7f);
        Assert.Empty(empty.Areas); Assert.Empty(empty.Labels); Assert.Equal(player, empty.Player); Assert.Equal(.7f, empty.Yaw);
        var geometry = content.GeometryPlacements.First();
        discovery.ObservePlacement(geometry.PlacementId);
        discovery.ObserveSurface(geometry.SamplePoints.First());
        discovery.RevealMarker(content.Markers.First().Id);
        discovery.AddNoteMarker("map-note", new(geometry.SamplePoints.First().X, geometry.SamplePoints.First().Y, geometry.SamplePoints.First().Z), "Turn back here");
        var saved = discovery.Capture();
        var map = DaggerfallMapProjection.Dungeon(profile, discovery, player, .7f);
        Assert.Contains(map.Areas, area => area.Id == geometry.PlacementId);
        Assert.DoesNotContain(map.Areas, area => content.GeometryPlacements.Skip(1).Any(placement => placement.PlacementId == area.Id));
        Assert.Equal(2, map.Labels.Count);
        Assert.Contains(map.Labels, label => label.Name == "Turn back here");
        var restored = new DaggerfallDungeonDiscovery(profile.ProfileKey, content, saved);
        var replay = DaggerfallMapProjection.Dungeon(profile, restored, player, .7f);
        Assert.Equal(map.Areas, replay.Areas); Assert.Equal(map.Labels, replay.Labels);
        Assert.Equal(saved.DiscoveredPlacementIds, discovery.Capture().DiscoveredPlacementIds);
    }

    [Fact]
    public void City_map_uses_real_placed_ids_source_footprints_and_building_names()
    {
        using var session = FreshSession();
        var sites = session.Site;
        var blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")));
        sites.AdmitBuildingNames(MapRandom.Create(), TestPayload.Definitions, blocks);
        var city = sites.Records.Single(site => site.Name == "Charing");
        sites.Enter(city.Id);
        var map = DaggerfallMapProjection.City(sites, new(7, 8, 9), 1);
        Assert.Equal(new WorldPoint(7, 8, 9), map.Player);
        Assert.Equal(city.Exterior!.Buildings.Values.Count(building => !string.IsNullOrWhiteSpace(sites.RequireBuilding(city.Id, building.Id).Name)), map.Labels.Count);
        Assert.DoesNotContain(map.Labels, label => string.IsNullOrWhiteSpace(label.Name));
        var residence = sites.BuildingsAt(city.Id).First(building => building.Source.BuildingType == 18);
        Assert.Throws<InvalidOperationException>(() => sites.SelectBuilding(city.Id, residence.Id));
        Assert.Equal(city.Exterior.Blocks.Sum(block => blocks.Maps[block.SourceName].Footprints.Count), map.Areas.Count);
        var source = sites.BuildingsAt(city.Id).First(building => building.Source.BuildingType == 23);
        var selected = sites.SelectBuilding(city.Id, source.Id);
        var after = DaggerfallMapProjection.City(sites, map.Player, map.Yaw);
        var label = Assert.Single(after.Labels, label => label.Selected);
        Assert.Equal(selected.Name, label.Name); Assert.Equal(source.Id.ToString(), label.Id);
        WorldPoint point = DaggerfallMapProjection.BuildingPosition(sites, city.Id, source.Id);
        Assert.Equal(point.X, label.X); Assert.Equal(point.Z, label.Z);
        Assert.Throws<InvalidOperationException>(() => sites.SelectBuilding(city.Id, new(999, 999, 999)));
        Assert.Equal((city.Id, source.Id), sites.SelectedBuilding);
    }

    [Fact]
    public void Actual_exterior_session_routes_map_selection_and_projects_authoritative_pose()
    {
        string root = TestData.RepositoryRoot;
        var definitions = TestPayload.Definitions;
        var profile = ReadProfile(root, FullContent(root), definitions, "daggerfall.charing-exterior.json");
        List<string> releases = [];
        ContentFake content = new(releases); PopulateContent(content, profile);
        var engine = EngineContextFake.Create(content, SpatialFake.Create(profile.SpatialArtifact.Sha256, releases).Service,
            new AppearanceFake(releases), random: MapRandom.Create());
        var composition = new DaggerfallSessionComposition(definitions, profile, DaggerfallTuning.Defaults)
        {
            Blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.blocks.json"))),
            Profiles = new([profile]),
        };
        using var session = DaggerfallSession.StartNew(engine.Context, composition);
        var before = session.ReadMapPresentation()!;
        WorldPoint canonicalProfilePose = WorldPoint.From(session.Sites.LocalToProfile(
            session.State.PlayerControl.Position!.Value.ToVector()));
        Assert.Equal(canonicalProfilePose, before.Player);
        WorldPoint requestedProfilePose = profile.Project.PlayerPosition
            ?? throw new InvalidOperationException("Charing exterior has no authored player position.");
        Assert.Equal(requestedProfilePose.X, before.Player.X, 4);
        Assert.Equal(requestedProfilePose.Y, before.Player.Y, 4);
        Assert.Equal(requestedProfilePose.Z, before.Player.Z, 4);
        var label = before.Labels.First();
        session.Update(new ProductUpdate(OuterUpdate(1), [Ui(JsonSerializer.Serialize(new {
            action = "map-building", region = before.Region, destination = before.Location, item = label.Id }))]));
        var after = session.ReadMapPresentation()!;
        Assert.Equal(label.Id, Assert.Single(after.Labels, label => label.Selected).Id);
        Assert.Contains(label.Name, session.Presentation.LastOutcome);
        // A read follows the canonical current pose, rather than an independently stored map marker.
        session.State.PlayerControl.MoveTo(new(31, 2, -25));
        Assert.Equal(session.Sites.ExteriorSitePosition(new(31, 2, -25)), session.ReadMapPresentation()!.Player);
        var saved = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Equal(session.Site.Active, saved.Site.Active.Require());
        Assert.Equal(session.State.PlayerControl.Position, new WorldPoint(saved.Player.X, saved.Player.Y, saved.Player.Z));
    }

    [Fact]
    public void Malformed_published_map_is_refused_instead_of_showing_wrong_footprints()
    {
        var payload = JsonNode.Parse(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")))!;
        payload["maps"]![0]!["footprints"]![0]!["minX"] = -1;
        Assert.Throws<DaggerfallContentException>(() => DaggerfallBlocksContent.Read(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString())));
    }
}

internal class MapRandom : DispatchProxy
{
    internal static IRandomService Create() => DispatchProxy.Create<IRandomService, MapRandom>();
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method?.Name == nameof(IRandomService.DrawKeyed)) return new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum);
        if (method?.Name != nameof(IRandomService.DrawLcg15)) throw new NotSupportedException(method?.Name);
        Lcg15Request request = (Lcg15Request)arguments![0]!;
        uint state = unchecked(request.State * 1103515245u + 12345u);
        return new Lcg15Receipt(state, ((state >> 16) & 0x7fffu) % request.UpperExclusive);
    }
}
