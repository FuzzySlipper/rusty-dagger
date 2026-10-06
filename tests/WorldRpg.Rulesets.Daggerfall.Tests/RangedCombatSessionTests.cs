using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Ranged combat: cover, arrow flight and meshes, quivers and in-flight shots across saves.</summary>
public sealed class RangedCombatSessionTests
{
    /// <summary>
    /// The archer's ranged policy: it damages the player from further than any melee reaches, without ever
    /// closing, and stops when the player is out of its own attack's range.
    /// </summary>
    /// <remarks>
    /// This is the capability the archer erratum recorded as missing. The distance the Engine classifies is
    /// the fixture's, set well past melee reach and inside the archer's own authored reach; the archer's
    /// state and the damage that lands are the ruleset's answer to it.
    /// </remarks>
    [Fact]
    public void Static_cover_between_the_archer_and_the_player_stops_the_shot()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        // A wall across every horizontal query, while the floor probe the movement owner uses still
        // sees no obstruction.
        spatial.FloorHit = request => request.Direction.Y < -.5f
            ? default
            : default(SpatialHit) with { Present = true, Kind = SpatialHitKind.StaticMesh };
        PerceptionFake perception = PerceptionFake.Create();
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        const long archer = 2004;
        double separation = definitions.Actions.Values.Where(action => action.Interpretation == "fixed-melee").Max(action => action.Reach!.Value) + 1d;
        perception.Receipt = Receipt(new PerceptionPair(archer, 1, separation, 1d, PerceptionPairKind.Visible, 1d));

        double beforeHealth = PlayerHealth(session);
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredRangedMarker(archer));
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        for (ulong step = 2; step <= 120 && session.Presentation.LastOutcome is null; step++)
            session.Update(new ProductUpdate(OuterUpdate(step), []));

        // The release happened and the line was obstructed, so the shot died on the cover: no damage,
        // and the line tells the player the world was in the way rather than that they were missed.
        Assert.Equal(beforeHealth, PlayerHealth(session));
        Assert.Contains("blocked by cover", session.Presentation.LastOutcome ?? string.Empty);

        // The Engine answered exactly one query, and it was the shot's own line: it runs at the
        // player, and it starts clear of the muzzle so a shooter standing against geometry is not
        // read as its own cover.
        float eye = DaggerfallTuning.Defaults.Camera.EyeHeight;
        Vector3 shooter = session.State.Actors.Get(archer).Position.ToVector() + Vector3.UnitY * eye;
        Vector3 aim = session.State.PlayerControl.Position!.Value.ToVector() + Vector3.UnitY * eye;
        Vector3 line = Vector3.Normalize(aim - shooter);
        SpatialRaycastRequest cast = Assert.Single(spatial.FloorProbes.Where(request =>
            MathF.Abs((float)(request.MaxDistance - (Vector3.Distance(shooter, aim) - .3f))) < .05f));
        Assert.Equal(line.X, cast.Direction.X, 2);
        Assert.Equal(line.Y, cast.Direction.Y, 2);
        Assert.Equal(line.Z, cast.Direction.Z, 2);
        Assert.Equal(shooter.X + line.X * .3f, cast.Origin.X, 2);
        Assert.Equal(shooter.Z + line.Z * .3f, cast.Origin.Z, 2);
        DaggerfallMissileVisual arrow = Assert.Single(inputs.ClassicPresentation.WorldVisuals,
            visual => visual.MediaId == "visual.missile.arrow");
        Assert.DoesNotContain(appearance.Snapshots.Last(), fact =>
            ReferenceEquals(fact.Appearance, appearance.StaticMeshByPath[arrow.Path]));
    }

    [Fact]
    public void A_released_arrow_uses_its_authored_world_mesh_and_retires_with_its_flight()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallMissileVisual arrow = Assert.Single(inputs.ClassicPresentation.WorldVisuals,
            visual => visual.MediaId == "visual.missile.arrow");
        List<string> releases = [];
        using DaggerfallSession session = CreateArcherSession(root, definitions, inputs, releases,
            out AppearanceFake appearance, out PerceptionFake perception);
        const long archer = 2004;
        double separation = definitions.Actions.Values.Where(action => action.Interpretation == "fixed-melee")
            .Max(action => action.Reach!.Value) + 1d;
        perception.Receipt = Receipt(new PerceptionPair(archer, 1, separation, 1d,
            PerceptionPairKind.Visible, 1d));
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredRangedMarker(archer));

        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Appearance mesh = appearance.StaticMeshByPath[arrow.Path];
        AppearanceFact released = Assert.Single(appearance.Snapshots.Last(), fact =>
            ReferenceEquals(fact.Appearance, mesh));
        Assert.Equal(RenderLayer.Scene, released.Layer);
        Assert.Equal(session.State.Actors.Get(archer).Position.Y + DaggerfallTuning.Defaults.Camera.EyeHeight,
            released.Transform.Translation.Y, 2);

        // The shot owns one stable visual identity while its released line advances; stopping later
        // enemy decisions does not cancel a missile already in flight.
        perception.Receipt = Receipt(new PerceptionPair(archer, 1, separation, 0d,
            PerceptionPairKind.FacingRejected, 0d));
        appearance.AdvanceReceiptForAll = null;
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        AppearanceFact moving = Assert.Single(appearance.Snapshots.Last(), fact =>
            ReferenceEquals(fact.Appearance, mesh));
        Assert.Equal(released.ObjectId, moving.ObjectId);
        Assert.NotEqual(released.Transform.Translation, moving.Transform.Translation);

        for (ulong step = 3; step <= 120; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.DoesNotContain(appearance.Snapshots.Last(), fact => ReferenceEquals(fact.Appearance, mesh));
    }

    [Fact]
    public void An_actor_between_the_archer_and_the_player_does_not_stop_the_shot()
    {
        // The shot stops at admitted world geometry, not at a body: an intervening actor is not cover,
        // which is what keeps the StaticMesh answer the only blocking one.
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        spatial.FloorHit = request => request.Direction.Y < -.5f
            ? default
            : default(SpatialHit) with { Present = true, Kind = SpatialHitKind.Entity };
        PerceptionFake perception = PerceptionFake.Create();
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        const long archer = 2004;
        double separation = definitions.Actions.Values.Where(action => action.Interpretation == "fixed-melee").Max(action => action.Reach!.Value) + 1d;
        perception.Receipt = Receipt(new PerceptionPair(archer, 1, separation, 1d, PerceptionPairKind.Visible, 1d));

        double beforeHealth = PlayerHealth(session);
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredRangedMarker(archer));
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        for (ulong step = 2; step <= 240 && PlayerHealth(session) == beforeHealth; step++)
            session.Update(new ProductUpdate(OuterUpdate(step), []));

        Assert.True(PlayerHealth(session) < beforeHealth, "an actor in the way is not cover, so the arrow still lands");
        Assert.DoesNotContain("blocked by cover", session.Presentation.LastOutcome ?? string.Empty);
    }

    [Fact]
    public void The_archer_damages_the_player_from_beyond_melee_reach_without_closing()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        long archer = 2004;
        Assert.Equal("archer", inputs.Project.Actors[archer].ActorId.Value);
        double shotReach = definitions.Actions["archer-shot"].Reach!.Value;
        // Past every melee reach this corpus authors, and inside the archer's own.
        double separation = definitions.Actions.Values.Where(action => action.Interpretation == "fixed-melee").Max(action => action.Reach!.Value) + 1d;
        Assert.True(separation < shotReach, "the fixture's separation must sit between melee reach and the archer's shot");

        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        // The archer sees the player at that separation, facing them, with the line clear.
        perception.Receipt = Receipt(new PerceptionPair(checked((ulong)archer), 1, separation, 1d, PerceptionPairKind.Visible, 1d));
        // The swing is decided and released on its authored frame, but the arrow is still in flight.
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredRangedMarker(archer));
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);

        ulong arrivedStep = 1;
        for (ulong step = 2; step <= 240 && session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current == healthBefore; step++)
        {
            session.Update(new ProductUpdate(OuterUpdate(step), []));
            arrivedStep = step;
        }

        // It shot rather than closed: the state is the ranged attack, the player took damage, and no
        // navigation was asked for, which is what "without closing" means here.
        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[archer].State);
        Assert.Null(session.LastEnemyBehavior[archer].Navigation);
        double healthAfterShot = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        Assert.True(healthAfterShot < healthBefore, "the archer's shot must damage the player at that separation");
        // The line names the attacker, so the player can tell which of the enemies in front of them is
        // doing it: a hit the player took reads as the actor that landed it.
        string shotOutcome = engine.PublishedField("lastOutcome") ?? string.Empty;
        Assert.Contains("archer", shotOutcome, StringComparison.Ordinal);

        // The facing limit is the Engine's and the behaviour honours it: an attacker turned away does not
        // shoot, which is the same evidence kind the melee behaviour test uses for the other actor.
        perception.Receipt = Receipt(new PerceptionPair(checked((ulong)archer), 1, separation, 0d, PerceptionPairKind.FacingRejected, 0d));
        session.Update(new ProductUpdate(OuterUpdate(arrivedStep + 1), []));
        Assert.NotEqual(EnemyBehaviorState.Attack, session.LastEnemyBehavior[archer].State);

        // The same shot cannot land twice, and out past its own reach it stops entirely rather than
        // chasing: a ranged attacker that walks into melee is a melee attacker.
        appearance.AdvanceReceiptForAll = null;
        session.Update(new ProductUpdate(OuterUpdate(arrivedStep + 2), []));
        Assert.Equal(healthAfterShot, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        perception.Receipt = Receipt(new PerceptionPair(checked((ulong)archer), 1, shotReach + 1d, 1d, PerceptionPairKind.Visible, 1d));
        session.Update(new ProductUpdate(OuterUpdate(arrivedStep + 3), []));
        Assert.NotEqual(EnemyBehaviorState.Attack, session.LastEnemyBehavior[archer].State);

        // Every placed actor carries a policy, the archer included: an actor that cannot attack is a
        // missing capability its owner has to see, and there is no exception list left to hide one in.
        Assert.All(inputs.Project.Actors.Values, placement =>
            Assert.NotNull(definitions.RequireActor(placement.ActorId).ActionId));
    }

    [Fact]
    public void Batched_ranged_flight_uses_one_step_speed_and_retires_at_the_same_arrival()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        using DaggerfallSession session = CreateArcherSession(root, definitions, inputs, releases,
            out AppearanceFake appearance, out PerceptionFake perception);
        const long archer = 2004;
        double separation = definitions.Actions.Values.Where(action => action.Interpretation == "fixed-melee")
            .Max(action => action.Reach!.Value) + 1d;
        perception.Receipt = Receipt(new PerceptionPair(archer, 1, separation, 1d,
            PerceptionPairKind.Visible, 1d));
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredRangedMarker(archer));
        double healthBefore = PlayerHealth(session);

        // Release on the outer boundary after three admitted simulation steps. Every later update
        // catches up three steps, but the combat owner still derives the 25 m/s arrival from one
        // fixed step rather than treating the whole batch as one oversized step.
        session.Update(new ProductUpdate(OuterUpdate(1) with { SimulationStep = 1, AdmittedStepCount = 3 }, []));
        appearance.AdvanceReceiptForAll = null;
        perception.Receipt = Receipt(new PerceptionPair(archer, 1, separation, 0d,
            PerceptionPairKind.FacingRejected, 0d));
        DaggerfallMissileVisual arrow = Assert.Single(inputs.ClassicPresentation.WorldVisuals,
            visual => visual.MediaId == "visual.missile.arrow");
        Assert.Contains(appearance.Snapshots.Last(), fact =>
            ReferenceEquals(fact.Appearance, appearance.StaticMeshByPath[arrow.Path]));

        // The historical aggregate-duration bug arrives this shot during this window. A correct
        // one-step flight is still travelling after thirty admitted simulation steps.
        for (ulong start = 4; start <= 31; start += 3)
            session.Update(new ProductUpdate(OuterUpdate(start) with
            {
                SimulationStep = start,
                AdmittedStepCount = 3,
            }, []));
        Assert.Equal(healthBefore, PlayerHealth(session));
        Assert.Contains(appearance.Snapshots.Last(), fact =>
            ReferenceEquals(fact.Appearance, appearance.StaticMeshByPath[arrow.Path]));

        ulong arrivalStep = 0;
        for (ulong start = 34; start <= 100; start += 3)
        {
            session.Update(new ProductUpdate(OuterUpdate(start) with
            {
                SimulationStep = start,
                AdmittedStepCount = 3,
            }, []));
            if (PlayerHealth(session) < healthBefore)
            {
                arrivalStep = checked(start + 2);
                break;
            }
        }

        Assert.InRange(arrivalStep, 34UL, 100UL);
        // One further admitted boundary gives the appearance owner a completed flight receipt and
        // retires the world mesh with that flight's identity.
        ulong afterArrival = checked(arrivalStep + 3);
        session.Update(new ProductUpdate(OuterUpdate(afterArrival) with
        {
            SimulationStep = afterArrival,
            AdmittedStepCount = 3,
        }, []));
        Assert.DoesNotContain(appearance.Snapshots.Last(), fact =>
            ReferenceEquals(fact.Appearance, appearance.StaticMeshByPath[arrow.Path]));
    }

    [Fact]
    public void Batched_ranged_flight_matches_one_step_arrival_and_retirement()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);

        ulong oneStepTravel = RunArcherFlight(root, definitions, inputs, admittedStepCount: 1);
        ulong batchedTravel = RunArcherFlight(root, definitions, inputs, admittedStepCount: 3);

        // Both runs cover the same authored world distance at 25 m/s. A three-step batch can only
        // round the arrival boundary by at most its two extra steps; it must not turn the batch
        // duration into the per-step speed or leave the mesh alive after the common impact.
        Assert.True(Math.Abs(checked((long)oneStepTravel - (long)batchedTravel)) <= 2,
            $"one-step travel={oneStepTravel}, batched travel={batchedTravel}");
    }

    [Fact]
    public void An_archer_shot_misses_when_the_player_leaves_its_release_aim_during_flight()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        const long archer = 2004;
        double separation = definitions.Actions.Values.Where(action => action.Interpretation == "fixed-melee").Max(action => action.Reach!.Value) + 1d;
        perception.Receipt = Receipt(new PerceptionPair(archer, 1, separation, 1d, PerceptionPairKind.Visible, 1d));
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredRangedMarker(archer));
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        WorldPoint releaseAim = session.State.PlayerControl.Position!.Value;
        session.State.PlayerControl.Restore(new WorldPoint(releaseAim.X + 1f, releaseAim.Y, releaseAim.Z), default);
        // Do not start a later shot while the first one flies; this test isolates the first release.
        appearance.AdvanceReceiptForAll = null;
        for (ulong step = 2; step <= 240; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));

        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.Contains("missed", engine.PublishedField("lastOutcome"), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Paralysis_stops_an_unreleased_arrow_but_preserves_an_already_released_flight(bool released)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        using SpatialMovementSystem targetingSpatial = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId, placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        TargetingService targeting = new(perception.Service, targetingSpatial, session.State.Actors,
            new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting, () => inputs));
        DaggerCombatRules combat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment,
            session.State.ActorInventories.InventoryFor, session.State.ItemInstances, definitions, authored, targeting,
            physicalAttacksBlocked: id => session.State.Effects.ControlsFor(id).PhysicalAttacks);
        const long archer = 2004;
        const ulong generation = 77;
        const ulong releaseStep = 400;
        Dictionary<long, WorldPoint> positions = new()
        {
            [archer] = new WorldPoint(0, 0, 0),
            [DaggerfallActorIdentity.PlayerEntityId] = new WorldPoint(10, 0, 0),
        };
        FactBuffer<IProductFact> facts = new();
        double healthBefore = PlayerHealth(session);
        Assert.True(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, generation, releaseStep, .125, facts));
        if (!released) DaggerfallParalysisEffectsTests.Start(session, "blocked-release", 1, archer, 1000);
        combat.Execution.ApplyImpacts([new AttackImpactNotice(archer, DaggerfallActorIdentity.PlayerEntityId, generation, releaseStep, Expired: false)], generation, facts);
        combat.AdvanceRangedFlight(generation, releaseStep, .125, positions, facts);
        if (released)
        {
            Assert.Single(combat.ReadRangedFlights(generation, releaseStep));
            DaggerfallParalysisEffectsTests.Start(session, "after-release", 1, archer, 1000);
        }
        else Assert.Empty(combat.ReadRangedFlights(generation, releaseStep));
        combat.AdvanceRangedFlight(generation, releaseStep + 100, .125, positions, facts);
        Assert.Empty(combat.ReadRangedFlights(generation, releaseStep + 100));
        if (released) Assert.True(PlayerHealth(session) < healthBefore);
        else Assert.Equal(healthBefore, PlayerHealth(session));
        Assert.False(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, generation, releaseStep + 200, .125, facts));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ranged_flight_discards_stale_generations_and_retires_missing_or_inactive_attackers(bool inactive)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        using SpatialMovementSystem targetingSpatial = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId, placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        TargetingService targeting = new(perception.Service, targetingSpatial, session.State.Actors,
            new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting, () => inputs));
        bool gameplayActive = true;
        DaggerCombatRules combat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment,
            session.State.ActorInventories.InventoryFor, session.State.ItemInstances, definitions, authored, targeting,
            actorGameplayActive: _ => gameplayActive);
        const long archer = 2004;
        const ulong generation = 77;
        const ulong releaseStep = 400;
        Dictionary<long, WorldPoint> positions = new()
        {
            [archer] = new WorldPoint(0, 0, 0),
            [DaggerfallActorIdentity.PlayerEntityId] = new WorldPoint(10, 0, 0),
        };
        FactBuffer<IProductFact> facts = new();
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        Assert.True(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, generation, releaseStep, .125, facts));
        combat.Execution.ApplyImpacts([new AttackImpactNotice(archer, DaggerfallActorIdentity.PlayerEntityId, generation, releaseStep, Expired: false)], generation, facts);
        combat.AdvanceRangedFlight(generation, releaseStep, .125, positions, facts);
        Assert.Single(combat.ReadRangedFlights(generation, releaseStep));
        DaggerfallRangedFlightView beforeRebase = Assert.Single(combat.ReadRangedFlights(generation, releaseStep + 1));
        Vector3 delta = new(-1000, 3, 40);
        combat.RebaseRangedFlight(delta);
        DaggerfallRangedFlightView afterRebase = Assert.Single(combat.ReadRangedFlights(generation, releaseStep + 1));
        Assert.Equal(beforeRebase.Position.ToVector() + delta, afterRebase.Position.ToVector());
        Assert.Equal(beforeRebase.Direction, afterRebase.Direction);
        foreach (long id in positions.Keys.ToArray())
            positions[id] = WorldPoint.From(positions[id].ToVector() + delta);
        // A fresh admitted generation drops the old transient record rather than comparing its
        // release step to the new timeline's present step and accidentally landing it.
        combat.AdvanceRangedFlight(generation + 1, releaseStep + 1, .125, positions, facts);
        Assert.Empty(combat.ReadRangedFlights(generation + 1, releaseStep + 1));
        combat.AdvanceRangedFlight(generation, releaseStep + 100, .125, positions, facts);
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);

        const ulong nextGeneration = 79;
        const ulong nextReleaseStep = 1000;
        Assert.True(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, nextGeneration, nextReleaseStep, .125, facts));
        combat.Execution.ApplyImpacts([new AttackImpactNotice(archer, DaggerfallActorIdentity.PlayerEntityId, nextGeneration, nextReleaseStep, Expired: false)], nextGeneration, facts);
        combat.AdvanceRangedFlight(nextGeneration, nextReleaseStep, .125, positions, facts);
        Assert.Single(combat.ReadRangedFlights(nextGeneration, nextReleaseStep));
        if (inactive)
        {
            gameplayActive = false;
            Assert.True(session.State.Actors.TryGet(archer, out _));
        }
        else session.State.Actors.Entities.Destroy(ActorsState.Identity(archer));
        combat.AdvanceRangedFlight(nextGeneration, nextReleaseStep + 100, .125, positions, facts);
        Assert.Empty(combat.ReadRangedFlights(nextGeneration, nextReleaseStep + 100));
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void A_save_after_ranged_release_drops_the_transient_in_flight_shot()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        ResolvedCompositionIdentity composition = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        List<string> releases = [];
        RulesetSavePayload saved;
        double healthAtRelease;
        using (DaggerfallSession original = CreateArcherSession(root, definitions, inputs, releases, out AppearanceFake appearance, out PerceptionFake perception))
        {
            perception.Receipt = Receipt(new PerceptionPair(2004, 1, 4d, 1d, PerceptionPairKind.Visible, 1d));
            appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredRangedMarker(2004));
            original.Update(new ProductUpdate(OuterUpdate(1), []));
            DaggerfallMissileVisual arrow = Assert.Single(inputs.ClassicPresentation.WorldVisuals,
                visual => visual.MediaId == "visual.missile.arrow");
            Assert.Contains(appearance.Snapshots.Last(), fact =>
                ReferenceEquals(fact.Appearance, appearance.StaticMeshByPath[arrow.Path]));
            healthAtRelease = original.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
            saved = original.CaptureSave();
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        resumedSpatial.KeepPosition = true;
        AppearanceFake resumedAppearance = new(releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, resumedAppearance);
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, composition), saved);
        DaggerfallMissileVisual restoredArrow = Assert.Single(inputs.ClassicPresentation.WorldVisuals,
            visual => visual.MediaId == "visual.missile.arrow");
        resumed.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.DoesNotContain(resumedAppearance.Snapshots.Last(), fact =>
            ReferenceEquals(fact.Appearance, resumedAppearance.StaticMeshByPath[restoredArrow.Path]));
        // The original release would arrive within this bound. A resumed session has no in-flight
        // record, so crossing that deadline cannot replay a shot from the discarded runtime queue.
        for (ulong step = 3; step <= 240; step++) resumed.Update(new ProductUpdate(OuterUpdate(step), []));

        Assert.Equal(healthAtRelease, resumed.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void A_ranged_shot_draws_one_arrow_from_the_shooter_s_authored_quiver()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        using SpatialMovementSystem targetingSpatial = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId,
            placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        TargetingService targeting = new(perception.Service, targetingSpatial, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting, () => inputs));
        DaggerCombatRules combat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment, session.State.ActorInventories.InventoryFor,
            session.State.ItemInstances, definitions, authored, targeting);
        FactBuffer<IProductFact> facts = new();
        long archer = Assert.Single(inputs.Project.Actors.Values, placement => placement.ActorId == new DaggerfallActorId("archer")).EntityId;
        WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator quiver = Assert.IsType<WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator>(session.State.ActorInventories.InventoryFor(archer));

        // The pack authors the archer's quiver; the managed inventory carries it.
        InventoryStackId arrowStack = quiver.Read().Stacks.Single(stack => stack.Definition.Value == "arrow").Id;
        Assert.Equal(12UL, quiver.Read().Stacks.Single(stack => stack.Id == arrowStack).Quantity);
        quiver.Consume(new WorldRpg.Kit.Inventory.InventoryConsume(arrowStack, 11));

        Assert.True(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, 77, 400, .125, facts));
        Assert.DoesNotContain(quiver.Read().Stacks, stack => stack.Id == arrowStack);
        Assert.Throws<InvalidOperationException>(() => session.State.ItemInstances.RequireStack(DaggerfallItemOwner.Actor(archer), arrowStack));
        List<IProductFact> decided = [];
        facts.Deliver(decided.Add);
        Assert.Contains(decided, fact => fact is EnemyAttackStartedFact);
    }

    [Fact]
    public void An_archer_with_no_arrows_refuses_the_shot_and_reports_it_instead_of_missing()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        using SpatialMovementSystem targetingSpatial = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId,
            placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        TargetingService targeting = new(perception.Service, targetingSpatial, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting, () => inputs));
        DaggerCombatRules combat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment, session.State.ActorInventories.InventoryFor,
            session.State.ItemInstances, definitions, authored, targeting);
        FactBuffer<IProductFact> facts = new();
        long archer = Assert.Single(inputs.Project.Actors.Values, placement => placement.ActorId == new DaggerfallActorId("archer")).EntityId;
        WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator archerInventory = Assert.IsType<WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator>(session.State.ActorInventories.InventoryFor(archer));
        archerInventory.Consume(new WorldRpg.Kit.Inventory.InventoryConsume(
            archerInventory.Read().Stacks.Single(stack => stack.Definition.Value == "arrow").Id, 12));
        double playerHealthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        Assert.False(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, 77, 400, .125, facts));
        List<IProductFact> decided = [];
        facts.Deliver(decided.Add);
        AttackRejectedFact rejection = Assert.Single(decided.OfType<AttackRejectedFact>());
        Assert.Equal(AttackRejection.EmptyQuiver, rejection.Reason);
        Assert.Equal(archer, rejection.ActorId);
        Assert.DoesNotContain(decided, fact => fact is EnemyAttackStartedFact);

        // No pending impact exists behind the refusal, so the player is never damaged by a
        // shot that was never made, and every later attempt is refused the same way.
        Assert.Equal(playerHealthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.False(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, 78, 401, .125, facts));
        Assert.Equal(playerHealthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void A_restored_session_keeps_the_quiver_count_its_save_carried()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases));
        long archer = Assert.Single(inputs.Project.Actors.Values, placement => placement.ActorId == new DaggerfallActorId("archer")).EntityId;
        RulesetSavePayload payload;
        using (DaggerfallSession original = DaggerfallSession.StartNew(source.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
        {
            // One drawn arrow leaves eleven; the save must carry exactly that, not a refill.
            WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator archerInventory = Assert.IsType<WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator>(original.State.ActorInventories.InventoryFor(archer));
            archerInventory.Consume(new WorldRpg.Kit.Inventory.InventoryConsume(
                archerInventory.Read().Stacks.Single(stack => stack.Definition.Value == "arrow").Id, 1));
            payload = original.CaptureSave();
        }

        Assert.Equal(11UL, DaggerfallSavePayload.Read(payload).ActorInventories
            .Single(section => section.EntityId == archer).Inventory.Stacks.Single(stack => stack.ItemId == "arrow").Quantity);

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), payload);

        Assert.Equal(11UL, Assert.IsType<WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator>(resumed.State.ActorInventories.InventoryFor(archer)).Read().Stacks.Single(stack => stack.Definition.Value == "arrow").Quantity);
    }

    private static DaggerfallSession CreateArcherSession(string root, DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs,
        List<string> releases, out AppearanceFake appearance, out PerceptionFake perception)
    {
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        perception = PerceptionFake.Create();
        appearance = new AppearanceFake(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        return DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
    }

    private static ulong RunArcherFlight(string root, DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs,
        uint admittedStepCount)
    {
        List<string> releases = [];
        using DaggerfallSession session = CreateArcherSession(root, definitions, inputs, releases,
            out AppearanceFake appearance, out PerceptionFake perception);
        const long archer = 2004;
        double separation = definitions.Actions.Values.Where(action => action.Interpretation == "fixed-melee")
            .Max(action => action.Reach!.Value) + 1d;
        perception.Receipt = Receipt(new PerceptionPair(archer, 1, separation, 1d,
            PerceptionPairKind.Visible, 1d));
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredRangedMarker(archer));
        double healthBefore = PlayerHealth(session);
        session.Update(new ProductUpdate(OuterUpdate(1) with
        {
            SimulationStep = 1,
            AdmittedStepCount = admittedStepCount,
        }, []));
        appearance.AdvanceReceiptForAll = null;
        perception.Receipt = Receipt(new PerceptionPair(archer, 1, separation, 0d,
            PerceptionPairKind.FacingRejected, 0d));
        DaggerfallMissileVisual arrow = Assert.Single(inputs.ClassicPresentation.WorldVisuals,
            visual => visual.MediaId == "visual.missile.arrow");

        for (ulong start = checked((ulong)admittedStepCount + 1); start <= 100; start = checked(start + admittedStepCount))
        {
            session.Update(new ProductUpdate(OuterUpdate(start) with
            {
                SimulationStep = start,
                AdmittedStepCount = admittedStepCount,
            }, []));
            if (PlayerHealth(session) < healthBefore)
            {
                ulong arrivalStep = checked(start + admittedStepCount - 1);
                ulong afterArrival = checked(arrivalStep + 1);
                session.Update(new ProductUpdate(OuterUpdate(afterArrival) with
                {
                    SimulationStep = afterArrival,
                    AdmittedStepCount = admittedStepCount,
                }, []));
                Assert.DoesNotContain(appearance.Snapshots.Last(), fact =>
                    ReferenceEquals(fact.Appearance, appearance.StaticMeshByPath[arrow.Path]));
                return checked(arrivalStep - admittedStepCount);
            }
        }

        throw new Xunit.Sdk.XunitException($"Archer flight did not arrive in 100 simulation steps (batch {admittedStepCount}).");
    }

    /// <summary>The damage marker the placed archer's authored ranged sequence publishes.</summary>
    private static ulong AuthoredRangedMarker(long placedEntityId) => AuthoredMarker(placedEntityId, "rangedFrames");
}
