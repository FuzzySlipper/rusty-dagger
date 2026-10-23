using Rusty.Engine.Entities;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A session given the host's loading screen acknowledges the player's slow world changes on it in the update that
/// accepts them, before any slow work: a dungeon's entrance and a journey each publish their first phase with the
/// player still where they were, run one phase per later update, and clear once the player has arrived.
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
}
