using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The exterior owner built from its inputs without the session (#10747's deferred requirement): the actor factory's
/// assembly, the roster over it, the ground containers, and the site, host and site-delta answers it asks for. Its window,
/// towns and origin are drawn through the session in ExteriorSaveSessionTests and ExteriorOriginSessionTests.
/// </summary>
public sealed class DaggerfallExteriorWorldTests
{
    private sealed class Site(DaggerfallWorldProfileKey active) : IDaggerfallExteriorSite
    {
        public DaggerfallWorldProfileKey ActiveProfile { get; set; } = active;
        public DaggerfallSiteProjection Projection => throw new InvalidOperationException("No projection is read while the exterior holds no window.");
        public DaggerfallSiteProfiles? Profiles => null;
        public void RebaseActionTriggers(Vector3 delta) => throw new InvalidOperationException("No origin moves here.");
        public void RebaseRestoredPlayerTriggers() => throw new InvalidOperationException("No origin moves here.");
    }

    private sealed class Host : IDaggerfallExteriorHost, IDaggerfallSiteDeltas
    {
        public void RelocatePlayer(WorldPoint position, float yawRadians, float pitchRadians) => throw new InvalidOperationException();
        public void RebuildActivation() => throw new InvalidOperationException();
        public void ConfigureSiteAppearance(DaggerfallSiteAppearance appearance) => throw new InvalidOperationException();
        public void RebaseTransientWorld(Vector3 delta) => throw new InvalidOperationException();
        public void AdmitResidentCivilianAppearances(DaggerfallSiteProjection projection) => throw new InvalidOperationException();
        public void GroundArrivedActors() => throw new InvalidOperationException();
        public DaggerfallSiteRuntimeDelta Capture(DaggerfallSiteProfile inputs, DaggerfallSiteProjection projection,
            IReadOnlyDictionary<long, DaggerfallActorId> dynamicActors, IReadOnlySet<long>? actorFilter, Vector3 actorFrameOffset) =>
            throw new InvalidOperationException();
        public void Restore(DaggerfallSiteRuntimeDelta delta) => throw new InvalidOperationException();
    }

    [Fact]
    public void The_exterior_is_built_from_its_inputs_and_holds_no_window_until_one_is_admitted()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallTuning tuning = DaggerfallTuning.Defaults;
        DaggerfallSiteProfile inputs = TestSessions.ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases);
        TestSessions.PopulateContent(content, inputs);
        SpatialFake spatialFake = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatialFake.Service, new AppearanceFake(releases));
        IRandomService random = RandomMinimum.Create();
        DaggerActorAssembly assembled = DaggerActorFactory.Create(random, definitions, inputs, saved: null);
        using ActorsState actors = assembled.Actors;
        using SpatialMovementSystem spatial = new(spatialFake.Service, content, null, tuning.Spatial);
        DaggerfallEffectLifecycle effects = new(actors, new DaggerfallEffectCatalog([]));
        DurableIdentityAllocator identities = new(DurableIdentityKind.Actor, 10_000);
        DaggerfallUniqueItemAllocator uniqueItems = new(DaggerfallUniqueItemAllocator.DefaultFirstEntityId);
        DaggerfallCorpseLootModule corpses = new(engine.Context.Perception, spatial, assembled.Containers, assembled.ItemInstances,
            actors.Player.Actor.Entity, actors, assembled.Definitions, definitions, random, uniqueItems, actors.Player.Progression,
            tuning.LootInteraction, assembled.Character);
        Site site = new(inputs.ProfileKey);
        DaggerfallActorRoster roster = new(actors, assembled.ActorInventories, assembled.Character, assembled.Containers, effects,
            assembled.InventoryStore, assembled.ItemInstances, assembled.Npcs, definitions, random, assembled.Mechanics, identities, uniqueItems,
            DaggerActorFactory.AdmittedAuthoredEntityIds(inputs, assembled.PlayerDefinition.Loadout), assembled.Definitions, [],
            new DaggerfallActorGrounding(spatialFake.Service, spatial, tuning.EnemyBehavior.SpawnGroundProbeLift, tuning.EnemyBehavior.SpawnGroundProbeDistance),
            () => inputs, () => throw new InvalidOperationException("No appearance is read here."), _ => { }, corpses);
        DaggerfallGroundContainers ground = new(assembled.Containers, assembled.ItemInstances, actors.Player.Actor.Entity,
            new DurableIdentityAllocator(DurableIdentityKind.Container, 100), inputs.ProfileKey);
        Host host = new();

        DaggerfallExteriorWorld exterior = new(engine.Context, actors, assembled.Npcs, assembled.PlayerControl, effects, definitions, tuning.Site,
            new DaggerfallWorldTime(DaggerfallCalendar.Start, 0d, 12d), new DaggerfallSiteContext(definitions.Locations), spatial,
            new FirstPersonCameraSystem(engine.Context.CameraView, assembled.PlayerControl, tuning.Camera), null, roster, host, ground, site, host,
            []);

        // A dungeon is active: its location is loaded by being entered, and no cell window or origin has been admitted.
        Assert.Equal(DaggerfallWorldProfileKind.Dungeon, inputs.ProfileKind);
        Assert.True(exterior.ActiveLocationLoaded);
        Assert.False(exterior.ExteriorResidencyInitialized);
        Assert.Equal(Vector3.Zero, exterior.LocalCompensation);
        WorldPoint point = new(3f, 1f, -2f);
        Assert.Equal(point, exterior.ProfileToLocal(point));

        // The same owner over an exterior holds its town unloaded until the window admits it.
        site.ActiveProfile = new(inputs.ProfileKey.Site, DaggerfallWorldProfileKind.Exterior, "outside");
        DaggerfallExteriorWorld outside = new(engine.Context, actors, assembled.Npcs, assembled.PlayerControl, effects, definitions, tuning.Site,
            new DaggerfallWorldTime(DaggerfallCalendar.Start, 0d, 12d), new DaggerfallSiteContext(definitions.Locations), spatial,
            new FirstPersonCameraSystem(engine.Context.CameraView, assembled.PlayerControl, tuning.Camera), null, roster, host, ground, site, host,
            []);
        Assert.False(outside.ActiveLocationLoaded);
        Assert.False(outside.ExteriorResidencyInitialized);
    }
}
