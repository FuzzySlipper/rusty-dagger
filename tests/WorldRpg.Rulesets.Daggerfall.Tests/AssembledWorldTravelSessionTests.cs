using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The whole catalog is reachable: a journey arrives at a location no pack publishes, its buildings and its
/// dungeon are entered through their source doors and left again, and saves taken before, at and after the
/// arrival restore there. Every place is the profile its id names, assembled from the per-block publication.
/// </summary>
public sealed class AssembledWorldTravelSessionTests
{
    private static readonly DaggerfallSiteId PrivateersHold = new(17, 179);
    private static readonly SharedFixture<World> Shared = new(() => new World());

    /// <summary>
    /// A journey to an unpublished city lands at its start marker nearest the side it arrives from; one of its
    /// buildings is entered through its door, which the block states, and left through the exit its interior
    /// states. Saves before the journey, at the arrival and inside the building each restore where they were taken.
    /// </summary>
    [Fact]
    public void Travel_to_an_unpublished_city_enters_a_building_and_leaves_it_across_saves()
    {
        World world = Shared.Value;
        DaggerfallSiteRecord city = world.Unpublished(record => record.Kind == DaggerfallSiteKind.TownCity && record.Exterior is not null);
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(city.Id);

        // The building entered: one whose door starts unlocked and whose interior the catalog places.
        DaggerfallRdbDoorDefinition door = world.Profiles.Require(exterior).Doors.First(candidate => candidate.StartingLockValue == 0
            && candidate.ExteriorBuilding is { } building && world.Profiles.Contains(DaggerfallWorldProfileIds.Interior(city.Id, building)));
        DaggerfallSiteBuildingId entered = door.ExteriorBuilding!.Value;
        DaggerfallWorldProfileKey interior = DaggerfallWorldProfileIds.Interior(city.Id, entered);

        using Run start = world.Start(city.Id, entered);
        RulesetSavePayload beforeJourney = start.Session.CaptureSave();
        using Run run = world.Restore(beforeJourney, city.Id, entered);
        run.Travel(city);
        Assert.Equal(exterior, run.Session.Sites.ActiveProfile);
        DaggerfallSiteProfile town = world.Profiles.Require(exterior);
        WorldPoint landed = run.Session.Sites.ExteriorSitePosition(run.Session.State.PlayerControl.Position!.Value);
        // A city receives the traveller at one of its own start markers.
        Assert.Contains(town.Anchors.Values, anchor => anchor.Id.StartsWith(DaggerfallLocationAssembly.StartMarkerAnchorPrefix, StringComparison.Ordinal)
            && Vector3.Distance(anchor.Position.ToVector(), landed.ToVector()) < 1e-3F);
        // Its neighbours in the exterior window stream from their blocks too.
        Assert.All(run.Session.Sites.ResidentExteriorProfiles, key => Assert.Equal(DaggerfallWorldProfileIds.Exterior(key.Site), key));

        RulesetSavePayload atArrival = run.Session.CaptureSave();
        using Run arrived = world.Restore(atArrival, city.Id, entered);
        Assert.Equal(exterior, arrived.Session.Sites.ActiveProfile);
        DaggerfallPlayerSave saved = DaggerfallSavePayload.Read(atArrival).Player, again = DaggerfallSavePayload.Read(arrived.Session.CaptureSave()).Player;
        Assert.Equal((saved.X, saved.Y, saved.Z, saved.YawRadians), (again.X, again.Y, again.Z, again.YawRadians));

        // Enter the building through its door.
        DaggerfallDoorView live = arrived.Session.Sites.Projection.Doors.Read(door.Id);
        Assert.False(live.IsLocked);
        WorldPoint outside = arrived.Use(live.Pose.Translation, live.Entity);
        Assert.Equal(interior, arrived.Session.Sites.ActiveProfile);
        Assert.Equal(exterior, arrived.Session.Sites.ReturnProfile);
        RulesetSavePayload inside = arrived.Session.CaptureSave();

        using Run resumed = world.Restore(inside, city.Id, entered);
        Assert.Equal(interior, resumed.Session.Sites.ActiveProfile);
        // Every building door plane inside leads out.
        Assert.All(resumed.Session.Sites.Projection.Portals.All, value => Assert.Equal(exterior.LogicalId, value.Portal.DestinationLogicalProfile));
        (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) exit = resumed.Session.Sites.Projection.Portals.All.First();
        resumed.Use(exit.Portal.Position.ToVector(), exit.Entity);
        // Out through the door the player came in by, where they stood to use it.
        Assert.Equal(exterior, resumed.Session.Sites.ActiveProfile);
        Assert.True(Vector3.Distance(outside.ToVector(), resumed.Session.Sites.ExteriorSitePosition(resumed.Session.State.PlayerControl.Position!.Value).ToVector()) < 1e-2F);
    }

    /// <summary>
    /// A journey to an unpublished dungeon lands outside its location; its source dungeon entrance leads in to
    /// the dungeon's start, facing away from its exit, and its source exit leads back out. Without an entrance to
    /// return through, leaving lands in front of the lowest entrance, facing away from it. A save inside the
    /// dungeon restores there.
    /// </summary>
    [Fact]
    public void Travel_to_an_unpublished_dungeon_enters_it_and_leaves_through_its_exit()
    {
        World world = Shared.Value;
        DaggerfallSiteRecord site = world.Unpublished(record => record.Kind is DaggerfallSiteKind.DungeonLabyrinth or DaggerfallSiteKind.DungeonKeep or DaggerfallSiteKind.DungeonRuin
            && record.Exterior is not null && record.DungeonBlocks.Count != 0
            && world.Profiles.Require(DaggerfallWorldProfileIds.Exterior(record.Id)).Portals.Count != 0);
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(site.Id), dungeon = DaggerfallWorldProfileIds.Dungeon(site.Id);

        using Run run = world.Start(site.Id);
        run.Travel(site);
        Assert.Equal(exterior, run.Session.Sites.ActiveProfile);
        DaggerfallSiteProfile outside = world.Profiles.Require(exterior);
        // A dungeon location is no city: the traveller stands a tenth of a block outside its edge, facing in.
        DaggerfallSiteAnchor[] sides = [.. Enum.GetValues<DaggerfallArrivalSide>().Select(side => DaggerfallLocationArrival.Landing(outside, site, side))];
        WorldPoint landed = run.Session.Sites.ExteriorSitePosition(run.Session.State.PlayerControl.Position!.Value);
        Assert.Contains(sides, side => Vector3.Distance(side.Position.ToVector(), landed.ToVector()) < 1e-2F);

        DaggerfallSitePortal entrance = outside.Portals.First(portal => portal.DestinationLogicalProfile == dungeon.LogicalId);
        (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) live = run.Session.Sites.Projection.Portals.All.Single(value => value.Portal.Id == entrance.Id);
        WorldPoint stood = run.Use(live.Portal.Position.ToVector(), live.Entity);
        Assert.Equal(dungeon, run.Session.Sites.ActiveProfile);
        DaggerfallSiteProfile inside = world.Profiles.Require(dungeon);
        DaggerfallSiteAnchor start = inside.RequireAnchor(DaggerfallLocationAssembly.StartAnchor);
        Assert.Equal(start.Position, run.Session.State.PlayerControl.Position);
        Assert.Equal(start.YawRadians, run.Session.State.PlayerControl.YawRadians);
        Assert.NotEmpty(inside.Portals);
        Assert.All(inside.Portals, portal => Assert.Equal(exterior.LogicalId, portal.DestinationLogicalProfile));

        using Run resumed = world.Restore(run.Session.CaptureSave(), site.Id);
        Assert.Equal(dungeon, resumed.Session.Sites.ActiveProfile);
        Assert.Equal(exterior, resumed.Session.Sites.ReturnProfile);
        (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) exit = resumed.Session.Sites.Projection.Portals.All.First();
        resumed.Use(exit.Portal.Position.ToVector(), exit.Entity);
        Assert.Equal(exterior, resumed.Session.Sites.ActiveProfile);
        WorldPoint returned = resumed.Session.Sites.ExteriorSitePosition(resumed.Session.State.PlayerControl.Position!.Value);
        Assert.True(Vector3.Distance(stood.ToVector(), returned.ToVector()) < 1e-2F, $"{stood} vs {returned}");

        // Brought inside rather than walking in, the player has no entrance to return through.
        Assert.True(resumed.Session.TryTransitionTo(dungeon, DaggerfallLocationAssembly.StartAnchor));
        resumed.Session.Sites.ClearReturnDestination();
        exit = resumed.Session.Sites.Projection.Portals.All.First();
        resumed.Use(exit.Portal.Position.ToVector(), exit.Entity);
        DaggerfallSiteAnchor landing = outside.RequireAnchor(DaggerfallLocationAssembly.DungeonEntranceAnchor);
        Assert.Equal(exterior, resumed.Session.Sites.ActiveProfile);
        Assert.True(Vector3.Distance(landing.Position.ToVector(), resumed.Session.Sites.ExteriorSitePosition(resumed.Session.State.PlayerControl.Position!.Value).ToVector()) < 1e-2F);
        Assert.Equal(landing.YawRadians, resumed.Session.State.PlayerControl.YawRadians);
    }

    /// <summary>
    /// A walled city's gates are drawn and collide as their open model by day and their closed model at night,
    /// swapping as the calendar crosses dusk and dawn (DaggerfallCityGate); the gate models are not part of the
    /// static meshes, and the state is not saved but follows the restored calendar.
    /// </summary>
    [Fact]
    public void A_walled_city_s_gates_close_at_dusk_and_open_at_dawn()
    {
        World world = Shared.Value;
        DaggerfallSiteRecord city = world.Unpublished(record => record.Exterior is not null
            && world.Profiles.Require(DaggerfallWorldProfileIds.Exterior(record.Id)).CityGates.Count != 0);
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(city.Id);
        DaggerfallSiteProfile walled = world.Profiles.Require(exterior);
        Assert.All(walled.CityGates, gate => Assert.Equal((true, true),
            (gate.Open.Visual.Path.EndsWith("/mesh-446.rstatmsh", StringComparison.Ordinal), gate.Closed.Visual.Path.EndsWith("/mesh-447.rstatmsh", StringComparison.Ordinal))));
        Assert.DoesNotContain(walled.Geometry.Meshes, mesh => walled.CityGates.Any(gate => mesh.Pose == gate.Pose && mesh.Path == gate.Open.Visual.Path));

        using Run run = world.Start(city.Id);
        Assert.True(run.Session.TryTransitionTo(exterior, DaggerfallLocationAssembly.StartAnchor));
        bool day = Calendar(run.Session).IsDay;
        DaggerfallCityGates gates = run.Session.Sites.Projection.CityGates;
        Assert.Equal(day, gates.Open);
        Assert.Equal(walled.CityGates.Count, gates.Visuals.Count());
        Dictionary<ulong, ulong> before = new(run.Spatial.ResidentCollisionInstances);
        Assert.Equal(walled.CityGates.Count, gates.CharacterEnvironment().MeshInstances.Length);

        // Cross the next dusk or dawn: every gate swaps to its other model and collision.
        run.Session.AdvanceElapsedTime(12 * 60 * 60);
        run.Step();
        Assert.Equal(!day, Calendar(run.Session).IsDay);
        Assert.Equal(!day, run.Session.Sites.Projection.CityGates.Open);
        IReadOnlyDictionary<ulong, ulong> after = run.Spatial.ResidentCollisionInstances;
        Assert.Equal(walled.CityGates.Count, before.Keys.Except(after.Keys).Count());
        Assert.Equal(walled.CityGates.Count, after.Keys.Except(before.Keys).Count());
        Assert.All(after.Keys.Except(before.Keys), instance => Assert.DoesNotContain(after[instance], before.Values));

        // The state follows the restored calendar rather than a save.
        using Run restored = world.Restore(run.Session.CaptureSave(), city.Id);
        Assert.Equal(!day, restored.Session.Sites.Projection.CityGates.Open);
    }

    private static DaggerfallCalendar Calendar(DaggerfallSession session)
    {
        DaggerfallCalendarSave saved = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
        return new DaggerfallCalendar(saved.Year, saved.Month, saved.Day, saved.Hour, saved.Minute, saved.Second);
    }

    /// <summary>The composed catalog over the shipped content: the authored packs by their ids and every other place assembled.</summary>
    private sealed class World
    {
        internal DaggerfallDefinitions Definitions { get; } = TestPayload.Definitions;
        internal DaggerfallSiteProfile Source { get; }
        internal DaggerfallSiteProfiles Profiles { get; }
        internal ResolvedCompositionIdentity Identity { get; }
        internal DaggerfallSkyMedia Sky { get; }
        private readonly HashSet<DaggerfallWorldProfileKey> _authored;

        internal World()
        {
            string root = TestData.RepositoryRoot;
            Source = ReadInputs(root);
            ProductContent full = FullContent(root);
            DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(full);
            Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(full, media, Definitions));
            Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(full, media));
            DaggerfallLocationAssembly assembly = new(Definitions, new DaggerfallWorldBlocks(full), () => product.Value, () => meshes.Value);
            Profiles = DaggerfallSiteProfiles.Resolve([DaggerfallAuthoredSite.Of(Source)], assembly, null);
            _authored = [.. Profiles.AuthoredKeys];
            Identity = GameCompositionResolver.Resolve(full, new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
            Sky = DaggerfallSkyMedia.Read(full);
        }

        /// <summary>The location of Privateer's Hold's region nearest it that no pack publishes and the catalog places.</summary>
        internal DaggerfallSiteRecord Unpublished(Func<DaggerfallSiteRecord, bool> wanted)
        {
            DaggerfallSiteRecord origin = Definitions.Locations.Records.Single(record => record.Id == PrivateersHold);
            return Definitions.Locations.Records
                .Where(record => record.Region == PrivateersHold.Region && record.Id != PrivateersHold && record.Climate is not null)
                .Where(record => !_authored.Any(key => key.Site == record.Id))
                .OrderBy(record => Math.Abs(record.MapPixelX - origin.MapPixelX) + Math.Abs(record.MapPixelY - origin.MapPixelY))
                .First(record => Profiles.Contains(DaggerfallWorldProfileIds.Exterior(record.Id)) && wanted(record));
        }

        /// <summary>
        /// A new game in Privateer's Hold, walked out through its exit onto its assembled exterior: with no
        /// entrance to return through, the player lands in front of the island's dungeon entrance.
        /// </summary>
        internal Run Start(DaggerfallSiteId destination, DaggerfallSiteBuildingId? building = null)
        {
            Run run = new(this, null, destination, building);
            (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) exit = Assert.Single(run.Session.Sites.Projection.Portals.All);
            run.Use(exit.Portal.Position.ToVector(), exit.Entity);
            DaggerfallWorldProfileKey island = DaggerfallWorldProfileIds.Exterior(PrivateersHold);
            Assert.Equal(island, run.Session.Sites.ActiveProfile);
            DaggerfallSiteAnchor landing = Profiles.Require(island).RequireAnchor(DaggerfallLocationAssembly.DungeonEntranceAnchor);
            Assert.True(Vector3.Distance(landing.Position.ToVector(),
                run.Session.Sites.ExteriorSitePosition(run.Session.State.PlayerControl.Position!.Value).ToVector()) < 1e-2F);
            return run;
        }

        internal Run Restore(RulesetSavePayload save, DaggerfallSiteId destination, DaggerfallSiteBuildingId? building = null) =>
            new(this, save, destination, building);
    }

    /// <summary>One session over content admitting every place the journey and its window reach.</summary>
    private sealed class Run : IDisposable
    {
        private readonly PerceptionFake _perception = PerceptionFake.Create();
        private readonly World _world;
        private ulong _step;

        internal Run(World world, RulesetSavePayload? save, DaggerfallSiteId destination, DaggerfallSiteBuildingId? building)
        {
            _world = world;
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, world.Source);
            foreach (DaggerfallSiteId site in new[] { PrivateersHold, destination })
                Populate(content, world, site);
            if (building is { } entered) Admit(content, world.Profiles.Require(DaggerfallWorldProfileIds.Interior(destination, entered)));
            Spatial = SpatialFake.Create(world.Source.SpatialArtifact.Sha256, releases);
            // The player stays where a test places them, so a door is used from where they stood.
            Spatial.KeepPosition = true;
            EngineContextFake engine = EngineContextFake.Create(content, Spatial.Service,
                new AppearanceFake(releases), _perception.Service, random: LodgingRandom.Create());
            DaggerfallSessionComposition composition = new(world.Definitions, world.Source, DaggerfallTuning.Defaults, world.Identity)
                { Profiles = world.Profiles, Sky = world.Sky };
            Session = save is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, save);
        }

        internal DaggerfallSession Session { get; }

        internal SpatialFake Spatial { get; }

        /// <summary>One admitted update with no input.</summary>
        internal void Step() => Session.Update(new ProductUpdate(OuterUpdate(++_step), []));

        /// <summary>Pays for and makes the journey through the travel window, arriving.</summary>
        internal void Travel(DaggerfallSiteRecord destination)
        {
            Session.Site.Discover(destination.Id);
            DaggerfallItemDefinition gold = _world.Definitions.RequireItem(new DaggerfallItemId("gold-piece"));
            InventoryStackId stack = InventoryStackId.Parse("journey.gold");
            Session.State.Inventory.Grant(new(new InventoryItemId(gold.Id.Value), stack, 100_000));
            Session.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Player, stack, DaggerfallItemInstanceMetadata.Default(gold, DaggerfallItemOwner.Player));
            Submit(new { action = "travel-preview", region = destination.Region, destination = destination.Index, cautious = false, inn = true, ship = false });
            DaggerfallTravelQuote quote = Session.ReadTravelPresentation().Quote ?? throw new InvalidOperationException(Session.ReadTravelPresentation().Message);
            Assert.Null(Session.ReadTravelPresentation().Message);
            Submit(new { action = "travel-accept", key = quote.Identity, amount = quote.TotalCost });
            Assert.Equal(DaggerfallTravelOutcome.Arrived, Session.State.Travel.LastResult!.Outcome);
        }

        /// <summary>Uses the target at a live position, returning where the player stood (in the active frame's profile coordinates when outside).</summary>
        internal WorldPoint Use(Vector3 target, EntityId entity)
        {
            AimActivationAt(Session, WorldPoint.From(target));
            WorldPoint stood = Session.Sites.ActiveProfile.Kind == DaggerfallWorldProfileKind.Exterior
                ? Session.Sites.ExteriorSitePosition(Session.State.PlayerControl.Position!.Value)
                : Session.State.PlayerControl.Position!.Value;
            _perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(candidate => new PerceptionPair(1, candidate.Entity, 1d, 1d,
                candidate.Entity == entity.Value ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, 1d))]);
            Submit(new { action = "loot" });
            return stood;
        }

        private void Submit(object action) => Session.Update(new ProductUpdate(OuterUpdate(++_step), [Ui(JsonSerializer.Serialize(action))]));

        public void Dispose() => Session.Dispose();

        /// <summary>
        /// Admits a location's exterior and dungeon and every exterior its window streams, including the cells a
        /// step across its own map pixel's edge brings in.
        /// </summary>
        private static void Populate(ContentFake content, World world, DaggerfallSiteId site)
        {
            DaggerfallSiteRecord centre = world.Definitions.Locations.Records.Single(record => record.Id == site);
            foreach (DaggerfallSiteRecord near in world.Definitions.Locations.Records.Where(record => record.Exterior is not null
                && Math.Abs(record.Exterior.MapPixelX - centre.MapPixelX) <= 4 && Math.Abs(record.Exterior.MapPixelY - centre.MapPixelY) <= 4))
                if (world.Profiles.TryGet(DaggerfallWorldProfileIds.Exterior(near.Id), out DaggerfallSiteProfile exterior)) Admit(content, exterior);
            if (world.Profiles.TryGet(DaggerfallWorldProfileIds.Dungeon(site), out DaggerfallSiteProfile dungeon)) Admit(content, dungeon);
        }

        private static void Admit(ContentFake content, DaggerfallSiteProfile profile)
        {
            PopulateContent(content, profile);
            foreach (NormalizedTerrainTexture texture in profile.TerrainTextures.Values) content.Add(texture.TexturePath, texture.TextureSha256);
        }
    }
}
