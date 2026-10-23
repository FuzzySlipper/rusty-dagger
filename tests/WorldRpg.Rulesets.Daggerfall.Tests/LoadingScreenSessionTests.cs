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
