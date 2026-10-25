using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A session given the host's loading screen acknowledges the player's slow world changes on it in the update that
/// accepts them, before any slow work: a dungeon's entrance and a journey each publish their first phase with the
/// player still where they were, run one phase per later update, and clear once the player has arrived. The arrival's
/// terrain bakes off the update under the screen, which measures it, and the player stands on it only once it is live.
/// </summary>
public sealed class LoadingScreenSessionTests
{
    private static readonly SharedFixture<AssembledWorld> Shared = new(() => new AssembledWorld());

    [Fact]
    public void A_dungeon_entrance_is_acknowledged_before_the_dungeon_is_read_and_entered_on_the_updates_after()
    {
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold, loadingScreen: true);
        DaggerfallSession session = run.Session;
        LoadingScreen loading = run.Loading!;
        DaggerfallWorldProfileKey island = DaggerfallWorldProfileIds.Exterior(AssembledWorld.PrivateersHold);
        DaggerfallWorldProfileKey dungeon = DaggerfallWorldProfileIds.Dungeon(AssembledWorld.PrivateersHold);
        (DaggerfallSitePortal entrance, _, EntityId entity) = Assert.Single(session.Sites.Projection.Portals.All,
            portal => portal.Portal.DestinationLogicalProfile == dungeon.LogicalId);
        Assert.False(loading.IsLoading);

        // The update that takes the entrance shows the screen; the player is still outside.
        run.Use(entrance, entity);
        Assert.Equal(new LoadingState("Entering Privateer's Hold", "Reading Privateer's Hold", 0, 2, 0d), loading.Current);
        Assert.Equal(island, session.Sites.ActiveProfile);
        // That update's snapshot carries the screen, so the page shows it before any slow work runs.
        Assert.Equal("Reading Privateer's Hold", run.PublishedLoading()!["label"]);

        // The next reads the dungeon, the one after enters it.
        run.Step();
        Assert.Equal(new LoadingState("Entering Privateer's Hold", "Entering Privateer's Hold", 1, 2, .5d), loading.Current);
        Assert.Equal(island, session.Sites.ActiveProfile);
        Assert.Equal("Entering Privateer's Hold", run.PublishedLoading()!["label"]);
        run.Step();
        Assert.Equal(dungeon, session.Sites.ActiveProfile);
        Assert.Equal(1d, loading.Current!.Fraction);
        Assert.Equal(1d, run.PublishedLoading()!["fraction"]);

        // Then the screen clears, and that update is the session's own again.
        run.Step();
        Assert.Null(loading.Current);
        Assert.False(loading.IsLoading);
        Assert.Null(run.PublishedLoading());
    }

    [Fact]
    public void An_entrance_taken_while_an_enemy_hit_is_pending_is_still_staged_on_the_loading_screen()
    {
        // Every draw comes up lowest, so the rat's swing lands.
        using AssembledWorldRun run = Shared.Value.Start(AssembledWorld.PrivateersHold, loadingScreen: true, random: LowRandom.Create());
        DaggerfallSession session = run.Session;
        LoadingScreen loading = run.Loading!;
        DaggerfallWorldProfileKey island = DaggerfallWorldProfileIds.Exterior(AssembledWorld.PrivateersHold);
        DaggerfallWorldProfileKey dungeon = DaggerfallWorldProfileIds.Dungeon(AssembledWorld.PrivateersHold);
        (DaggerfallSitePortal entrance, _, EntityId entity) = Assert.Single(session.Sites.Projection.Portals.All,
            portal => portal.Portal.DestinationLogicalProfile == dungeon.LogicalId);

        // A rat beside the doorway swings at the player. Its hit has not landed when the player uses the door, so the
        // use is deferred to the end of the update, after the hit's frame had its chance.
        Vector3 standing = entrance.Position.ToVector() + entrance.Normal!.Value;
        session.State.PlayerControl.MoveTo(standing);
        Vector3 side = Vector3.Normalize(Vector3.Cross(entrance.Normal.Value, Vector3.UnitY));
        session.SpawnActor("rat", new(WorldPoint.From(standing + side), 0));
        long player = session.State.Actors.Player.DurableId;
        run.SeeEverything();
        bool Pending() => session.Sites.Appearances.Any(appearance => appearance.HasPendingEnemyHitTarget(player));
        for (int swing = 0; swing < 40 && !Pending(); swing++)
        {
            session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1000, clamp: true);
            // Let the last swing finish, then give the next time to start; a hit's swing plays without reaching
            // its hit frame, so the hit stays pending.
            run.Appearance.AdvanceReceiptForAll = TestSessions.CompletedMarker(1);
            run.Step();
            run.Appearance.AdvanceReceiptForAll = TestSessions.Reading(1, 0);
            for (int step = 0; step < 30 && !Pending(); step++) run.Step();
        }
        Assert.True(Pending(), "The rat's hit on the player is pending.");

        run.Use(entrance, entity);
        Assert.Equal(new LoadingState("Entering Privateer's Hold", "Reading Privateer's Hold", 0, 2, 0d), loading.Current);
        Assert.Equal(island, session.Sites.ActiveProfile);
        Assert.Equal("Reading Privateer's Hold", run.PublishedLoading()!["label"]);
        run.FinishLoading();
        Assert.Equal(dungeon, session.Sites.ActiveProfile);
    }

    [Fact]
    public void A_journey_is_acknowledged_before_it_is_made_and_arrives_on_the_updates_after()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallSiteRecord destination = world.Unpublished(_ => true);
        using AssembledWorldRun run = world.Start(AssembledWorld.PrivateersHold, loadingScreen: true);
        DaggerfallSession session = run.Session;
        LoadingScreen loading = run.Loading!;
        DaggerfallWorldProfileKey island = DaggerfallWorldProfileIds.Exterior(AssembledWorld.PrivateersHold);

        run.AcceptJourney(destination);
        Assert.Equal(new LoadingState($"Travelling to {destination.Name}", $"Reading {destination.Name}", 0, 3, 0d), loading.Current);
        Assert.Equal(island, session.Sites.ActiveProfile);
        Assert.Null(session.State.Travel.LastResult);
        Assert.Equal($"Reading {destination.Name}", run.PublishedLoading()!["label"]);

        // The destination is read; then the days pass on the road; then the player arrives.
        run.Step();
        Assert.Equal($"On the road to {destination.Name}", loading.Current!.Label);
        Assert.Null(session.State.Travel.LastResult);
        run.Step();
        Assert.Equal($"Arriving at {destination.Name}", loading.Current!.Label);
        Assert.Equal(island, session.Sites.ActiveProfile);
        Assert.Null(session.State.Travel.LastResult);
        run.Step();
        Assert.Equal(DaggerfallTravelOutcome.Arrived, session.State.Travel.LastResult!.Outcome);
        Assert.Equal(DaggerfallWorldProfileIds.Exterior(destination.Id), session.Sites.ActiveProfile);
        run.Step();
        Assert.Null(loading.Current);
    }

    [Fact]
    public void A_journeys_ground_and_town_are_prepared_off_the_update_under_the_screen_and_the_player_is_stood_only_once_they_are_live()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallSiteRecord destination = world.Unpublished(_ => true);
        using AssembledWorldRun run = world.Start(AssembledWorld.PrivateersHold, loadingScreen: true);
        DaggerfallSession session = run.Session;
        LoadingScreen loading = run.Loading!;
        SpatialFake spatial = run.Spatial;
        string title = $"Travelling to {destination.Name}", arriving = $"Arriving at {destination.Name}";
        // The terrain window's instances and the town's placements, and whether each downward floor probe (a stand)
        // found both live.
        ulong[] town = [];
        bool WindowLive() => spatial.ResidentCollisionInstances.Keys.Any(id => id >> 56 == 0xD7);
        bool TownLive() => town.Length != 0 && town.All(spatial.PlacedArtifacts.Contains);
        List<bool> probes = [];
        spatial.FloorHit = request =>
        {
            if (request.Direction.Y < 0) probes.Add(WindowLive() && TownLive());
            return default;
        };
        spatial.HoldDeferredCollision = true;

        run.AcceptJourney(destination);
        run.Step();
        run.Step();
        int waited = spatial.SynchronousWaitsOnDeferred;
        int placements = spatial.ContentResidencyRequests.Count;
        probes.Clear();
        run.Step();

        // Arrived: the destination is the session's, and the town's placements and its window are handed to the Engine
        // as two deferred batches, the placements first, after everything else the arrival did; nothing waited on them.
        Assert.Equal(DaggerfallWorldProfileIds.Exterior(destination.Id), session.Sites.ActiveProfile);
        Assert.Equal(2, spatial.PendingDeferredBatches);
        SpatialContentArtifactResidencyRequest placed = Assert.Single(spatial.ContentResidencyRequests.Skip(placements));
        Assert.True(placed.Deferred);
        town = [.. placed.Admitted.ToArray().Select(instance => instance.Id)];
        Assert.NotEmpty(town);
        Assert.True(spatial.CollisionResidencyRequests.Last(request => request.Deferred).Instances.Length > 0);
        Assert.Equal(waited, spatial.SynchronousWaitsOnDeferred);
        Assert.False(WindowLive());
        Assert.False(TownLive());
        // The placements still parse, so the bar moves unmeasured, and nobody is stood: there is no ground yet.
        Assert.Equal(new LoadingState(title, arriving, 2, 3, null), loading.Current);
        Assert.Null(run.PublishedLoading()!["fraction"]);
        Assert.Empty(probes);

        // Composing: the bar counts the share of the town's cells composed, of the two batches, within the arrival's third.
        spatial.DeferredCompose = (250, 1000);
        run.Step();
        Assert.Equal(new LoadingState(title, arriving, 2, 3, (2d + .125d) / 3d), loading.Current);
        Assert.Equal((2d + .125d) / 3d, run.PublishedLoading()!["fraction"]);
        Assert.Empty(probes);

        // Live: the arrival is stood on it, and the screen keeps its full bar for one update before play resumes.
        spatial.HoldDeferredCollision = false;
        run.Step();
        Assert.True(WindowLive());
        Assert.True(TownLive());
        Assert.NotEmpty(probes);
        Assert.All(probes, Assert.True);
        Assert.Equal(new LoadingState(title, arriving, 3, 3, 1d), loading.Current);
        run.Step();
        Assert.Null(loading.Current);
        Assert.Equal(waited, spatial.SynchronousWaitsOnDeferred);
    }

    [Fact]
    public void A_journeys_town_placements_the_Engine_refuses_fail_the_arrival_and_are_handed_over_again_while_the_world_is_held()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallSiteRecord destination = world.Unpublished(_ => true);
        using AssembledWorldRun run = world.Start(AssembledWorld.PrivateersHold, loadingScreen: true);
        DaggerfallSession session = run.Session;
        SpatialFake spatial = run.Spatial;
        int placements = spatial.ContentResidencyRequests.Count;
        HashSet<ulong> placedBefore = [.. spatial.PlacedArtifacts];
        spatial.RefuseDeferredPlacements = true;

        run.AcceptJourney(destination);
        run.FinishLoading();

        // The window went live, the town did not: the arrival failed and says why.
        Assert.Null(run.Loading!.Current);
        Assert.Equal(DaggerfallWorldProfileIds.Exterior(destination.Id), session.Sites.ActiveProfile);
        Assert.Contains("the Engine refused its collision", session.Presentation.LastOutcome);
        ulong[] town = [.. spatial.ContentResidencyRequests.Skip(placements).Single(request => request.Deferred)
            .Admitted.ToArray().Select(instance => instance.Id)];
        // The destination's own parts: the island's are placed again too, in the journey's new frame.
        ulong[] arriving = [.. town.Where(id => !placedBefore.Contains(id))];
        Assert.NotEmpty(arriving);
        Assert.DoesNotContain(arriving, spatial.PlacedArtifacts.Contains);

        // Held: the same placements go to the Engine again off the update, and nobody steps until they are live.
        int steps = spatial.StepCalls, requests = spatial.ContentResidencyRequests.Count;
        spatial.RefuseDeferredPlacements = false;
        spatial.HoldDeferredCollision = true;
        run.Step();
        SpatialContentArtifactResidencyRequest again = Assert.Single(spatial.ContentResidencyRequests.Skip(requests));
        Assert.True(again.Deferred);
        Assert.Equal(town, again.Admitted.ToArray().Select(instance => instance.Id));
        Assert.Equal(steps, spatial.StepCalls);
        spatial.HoldDeferredCollision = false;
        run.Step();
        Assert.All(arriving, id => Assert.Contains(id, spatial.PlacedArtifacts));
        Assert.True(spatial.StepCalls > steps);
    }

    [Fact]
    public void A_journeys_ground_the_Engine_refuses_fails_the_arrival_and_holds_the_world_until_it_is_live_and_stood_on()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallSiteRecord destination = world.Unpublished(_ => true);
        using AssembledWorldRun run = world.Start(AssembledWorld.PrivateersHold, loadingScreen: true);
        DaggerfallSession session = run.Session;
        SpatialFake spatial = run.Spatial;
        bool WindowLive() => spatial.ResidentCollisionInstances.Keys.Any(id => id >> 56 == 0xD7);
        List<bool> probes = [];
        spatial.FloorHit = request =>
        {
            if (request.Direction.Y < 0) probes.Add(WindowLive());
            return default;
        };
        spatial.RefuseDeferredCollision = true;

        run.AcceptJourney(destination);
        run.FinishLoading();

        // The screen cleared on the refusal and says why; the player is at the destination with no ground under them.
        Assert.Null(run.Loading!.Current);
        Assert.Equal(DaggerfallWorldProfileIds.Exterior(destination.Id), session.Sites.ActiveProfile);
        Assert.Contains("the Engine refused its collision", session.Presentation.LastOutcome);
        Assert.False(WindowLive());
        Assert.Empty(probes);

        // The world is held: the window goes to the Engine again off the update, and nobody steps meanwhile, even
        // while that batch is refused in turn.
        int steps = spatial.StepCalls, waited = spatial.SynchronousWaitsOnDeferred;
        spatial.HoldDeferredCollision = true;
        run.Step();
        Assert.Equal(1, spatial.PendingDeferredBatches);
        Assert.True(spatial.CollisionResidencyRequests[^1].Deferred);
        run.Step();
        Assert.Equal(steps, spatial.StepCalls);
        Assert.False(WindowLive());
        Assert.Empty(probes);
        Assert.Equal(waited, spatial.SynchronousWaitsOnDeferred);
        spatial.HoldDeferredCollision = false;
        run.Step();
        Assert.Equal(steps, spatial.StepCalls);
        Assert.False(WindowLive());

        // Admitted again: the arrival is stood on the live ground, and play takes its first step in that update.
        spatial.RefuseDeferredCollision = false;
        List<(bool Live, bool Stood)> stepped = [];
        spatial.StepResult = (_, receipt) =>
        {
            stepped.Add((WindowLive(), probes.Count > 0));
            return receipt;
        };
        run.Step();
        Assert.True(WindowLive());
        Assert.All(probes, Assert.True);
        Assert.NotEmpty(stepped);
        Assert.All(stepped, step => Assert.Equal((true, true), step));
    }

    /// <summary>The Engine's random service drawing every value at its lowest, keyed and classic alike.</summary>
    private class LowRandom : System.Reflection.DispatchProxy
    {
        internal static IRandomService Create() => System.Reflection.DispatchProxy.Create<IRandomService, LowRandom>();

        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IRandomService.DrawKeyed) => new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum),
            nameof(IRandomService.DrawLcg15) => new Lcg15Receipt(unchecked(((Lcg15Request)arguments![0]!).State * 1103515245u + 12345u), 0),
            _ => throw new NotSupportedException(method?.Name),
        };
    }
}
