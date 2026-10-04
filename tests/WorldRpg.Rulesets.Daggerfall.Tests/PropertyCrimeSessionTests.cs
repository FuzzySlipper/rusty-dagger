using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class PropertyCrimeSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_furniture_take_marks_real_items_and_one_incident_then_survives_restore(bool caught)
    {
        using var fixture = new Fixture(caught);
        var session = fixture.Session;
        fixture.OpenFurniture(session);
        var loot = Assert.IsType<LootPresentation>(session.OpenLoot);
        var item = Assert.Single(loot.Items);
        int notified = 0;
        session.CrimeReported += _ => notified++;
        string action = System.Text.Json.JsonSerializer.Serialize(new { action = "loot-take", container = loot.Container, revision = loot.Revision, item = item.Key, amount = 1 });
        fixture.Ui(session, action);
        Assert.True(session.State.Crime.Incidents.Count > 0, session.Presentation.LastOutcome);
        var incident = Assert.Single(session.State.Crime.Incidents);
        Assert.Equal(DaggerfallCrimeKind.Theft, incident.Crime);
        Assert.Equal(caught, incident.Reported);
        Assert.Equal(DaggerfallCrimeAttemptOutcome.PropertyTransferred, Assert.Single(session.State.Crime.Attempts).Outcome);
        Assert.Equal(1, notified);
        fixture.Ui(session, action);
        Assert.Single(session.State.Crime.Incidents);
        Assert.Equal(1, notified);
        var save = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Contains(save.Inventory.UniqueItems.Select(value => value.Metadata).Concat(save.Inventory.Stacks.Select(value => value.Metadata)), value => value.Stolen);
        var emptied = Assert.Single(save.GroundContainers, value => value.Id == incident.AffectedActorOrOwnerId);
        Assert.NotNull(emptied.PropertyPlacement);
        Assert.Empty(emptied.Inventory.UniqueItems);
        Assert.Empty(emptied.Inventory.Stacks);
        using var restored = fixture.Restore(session.CaptureSave());
        Assert.Equal(incident, Assert.Single(restored.State.Crime.Incidents) with { Witnesses = incident.Witnesses });
        fixture.OpenFurniture(restored);
        Assert.Empty(Assert.IsType<LootPresentation>(restored.OpenLoot).Items);
        Assert.Single(restored.State.Crime.Incidents);
    }

    [Fact]
    public void Owned_furniture_contents_are_taken_without_crime_or_stolen_metadata()
    {
        using var fixture = new Fixture(caught: true);
        var session = fixture.Session;
        fixture.OpenFurniture(session);
        var building = session.Sites.Projection.Inputs.InteriorBuilding!;
        var site = session.Sites.ActiveProfile.Site;
        session.State.Property.Restore(session.State.Property.Capture() with { Houses = [new(site.Region, site.Index,
            building.Building.SourceKey, building.Building.Index, building.BlockX, building.BlockY)] });
        var loot = Assert.IsType<LootPresentation>(session.OpenLoot);
        var item = Assert.Single(loot.Items);
        fixture.Ui(session, System.Text.Json.JsonSerializer.Serialize(new { action = "loot-take", container = loot.Container,
            revision = loot.Revision, item = item.Key, amount = 1 }));
        Assert.Empty(session.State.Crime.Incidents);
        Assert.Empty(session.State.Crime.Attempts);
        var save = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.All(save.Inventory.UniqueItems.Select(value => value.Metadata).Concat(save.Inventory.Stacks.Select(value => value.Metadata)),
            value => Assert.False(value.Stolen));
    }

    [Fact]
    public void Source_furniture_surface_is_visible_through_retained_native_static_collision()
    {
        string root = TestData.RepositoryRoot;
        var profile = ReadProfile(root, FullContent(root), TestPayload.Definitions, "daggerfall.charing-interior-1-1-0.json");
        using var host = EngineTestHost.Create(new EngineTestHostOptions { Content = new Dictionary<string, ReadOnlyMemory<byte>>
        {
            [profile.SpatialArtifact.Path] = File.ReadAllBytes(Path.Combine(root, "content", profile.SpatialArtifact.Path)),
        } });
        host.Call(engine =>
        {
            using SpatialMovementSystem spatial = new(engine.Spatial, engine.Content, profile.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
            foreach (var placement in profile.PropertyContainers)
            {
                bool visible = false;
                foreach (Vector3 direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                {
                    Vector3 observer = placement.Position.ToVector() + direction * 2 + Vector3.UnitY;
                    var target = placement.InteractionPoints.MinBy(point => Vector3.DistanceSquared(observer, point.ToVector()));
                    var result = engine.Perception.QueryVisibility(new(spatial.Session,
                        new[] { new PerceptionObserver(1, observer, Vector3.Normalize(target.ToVector() - observer), 4, -1, 1) },
                        new[] { new PerceptionTarget(2, target.ToVector()), new PerceptionTarget(3, placement.Position.ToVector()) },
                        ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 0, 64));
                    if (!result.Pairs.ToArray().Any(pair => pair.Target == 2 && pair.Kind == PerceptionPairKind.Visible)) continue;
                    Assert.DoesNotContain(result.Pairs.ToArray(), pair => pair.Target == 3 && pair.Kind == PerceptionPairKind.Visible);
                    visible = true;
                    break;
                }
                Assert.True(visible, $"No exposed source interaction face for {placement.Id}.");
            }
        });
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition;
        private readonly bool _caught;
        private SpatialFake _spatial = null!;
        private PerceptionFake _perception = null!;
        private ulong _step;
        internal DaggerfallSession Session { get; }
        internal Fixture(bool caught)
        {
            _caught = caught;
            var profile = ReadProfile(TestData.RepositoryRoot, FullContent(TestData.RepositoryRoot), TestPayload.Definitions, "daggerfall.charing-interior-1-1-0.json");
            Assert.NotEmpty(profile.PropertyContainers);
            _composition = new(TestPayload.Definitions, profile, DaggerfallTuning.Defaults) { Profiles = new([profile]), Blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))) };
            Session = DaggerfallSession.StartNew(Engine(), _composition);
        }
        private IEngineContext Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, _composition.StartSite);
            _spatial = SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256, releases);
            _spatial.KeepPosition = true;
            _perception = PerceptionFake.Create();
            return EngineContextFake.Create(content, _spatial.Service, new AppearanceFake(releases), _perception.Service, random: PropertyRandom.Create(_caught)).Context;
        }
        internal DaggerfallSession Restore(RulesetSavePayload save) => DaggerfallSession.Restore(Engine(), _composition, save);
        internal void Ui(DaggerfallSession session, string action) => session.Update(new ProductUpdate(OuterUpdate(++_step), [TestSessions.Ui(action)]));
        internal void OpenFurniture(DaggerfallSession session)
        {
            session.Update(new ProductUpdate(OuterUpdate(++_step), []));
            var placement = _composition.StartSite.PropertyContainers.First(value => value.ItemGroups.Length > 0);
            var container = DaggerfallSavePayload.Read(session.CaptureSave()).GroundContainers.Single(value => value.PropertyPlacement == placement.Id);
            var entity = session.State.Actors.Entities.Resolve(new(DurableIdentityKind.Container, checked((ulong)container.Id)));
            _perception.Responder = request => request.Observers.ToArray().Any(observer => observer.Entity == 1)
                ? Receipt(new PerceptionPair(1, checked((ulong)container.Id), 1, 1, PerceptionPairKind.Visible, 1)) : Receipt();
            session.State.PlayerControl.MoveTo(placement.Position.ToVector() + Vector3.UnitZ);
            session.State.PlayerControl.YawRadians = 0;
            _spatial.FloorHit = request => request.Direction.Y < -.5 ? default : new SpatialHit { Present = true, Kind = SpatialHitKind.Entity, Entity = entity.Value, Point = placement.Position.ToVector(), Distance = 1 };
            session.Update(new ProductUpdate(OuterUpdate(++_step), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
            Assert.True(session.ActivationView.Applied, session.ActivationView.Message);
        }
        public void Dispose() => Session.Dispose();
    }
}

internal class PropertyRandom : DispatchProxy
{
    private bool _caught;
    internal static IRandomService Create(bool caught)
    {
        var service = Create<IRandomService, PropertyRandom>();
        ((PropertyRandom)(object)service)._caught = caught;
        return service;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name == nameof(IRandomService.DrawKeyed))
        {
            var request = (KeyedRngRequest)args![0]!;
            // Stock terminates after its first real item; the legal roll alone changes by scenario.
            var value = request.Key.Contains("noticed", StringComparison.Ordinal) && _caught ? request.Minimum : request.Maximum;
            return new KeyedRngReceipt(value);
        }
        if (method?.Name == nameof(IRandomService.DrawLcg15))
        {
            var request = (Lcg15Request)args![0]!;
            uint state = unchecked(request.State * 1103515245u + 12345u);
            return new Lcg15Receipt(state, ((state >> 16) & 0x7fffu) % request.UpperExclusive);
        }
        throw new NotSupportedException(method?.Name);
    }
}
