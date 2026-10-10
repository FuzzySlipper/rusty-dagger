using Rusty.Engine.Testing;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The assembled world over the real Engine's content, spatial, graphics and world origin services, one Engine
/// callback per update as the product runs: every update publishes its complete appearance snapshot through the
/// Engine's validation, and the player moves on the real collision. The Engine test host has no Product.Update
/// callback, so only a sprite playback advance is answered outside the Engine (<see cref="EngineGraphicsOutsideUpdate"/>).
/// </summary>
public sealed class AssembledPresentationEngineTests
{
    private static readonly SharedFixture<AssembledWorld> Shared = new(() => new AssembledWorld());

    /// <summary>
    /// The playtested journey: a new game leaves Privateer's Hold onto its island, whose terrain, nature, entrance and
    /// neighbouring locations the Engine accepts in one snapshot, and the player stands on the island's ground at the
    /// entrance rather than inside it.
    /// </summary>
    /// <remarks>
    /// The window's nature is about two hundred thousand separate sprite appearances, whose retirement takes the Engine
    /// minutes; the host is destroyed with them rather than retiring the session's presentation one by one.
    /// </remarks>
    [Fact]
    public void Leaving_the_hold_publishes_the_island_and_stands_the_player_on_its_ground()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallWorldProfileKey island = DaggerfallWorldProfileIds.Exterior(AssembledWorld.PrivateersHold);
        Dictionary<string, ReadOnlyMemory<byte>> files = AssembledWorldRun.EngineContent(world, AssembledWorld.PrivateersHold);
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = files });
        AssembledWorldRun run = host.Call(engine => world.Start(engine, files));
        Assert.Equal(island, run.Session.Sites.ActiveProfile);
        AssertAccepted(run);
        Assert.True(run.Graphics.LastSnapshotObjects > 1000, $"The island window published only {run.Graphics.LastSnapshotObjects} objects.");

        WorldPoint landed = run.Session.State.PlayerControl.Position!.Value;
        // The entrance threshold stands on the ground: the standing capsule centre is about half its height above it.
        DaggerfallSiteAnchor landing = world.Profiles.Require(island).RequireAnchor(DaggerfallLocationAssembly.DungeonEntranceAnchor);
        float ground = landing.Position.Y + run.Session.Sites.ExteriorProfileFrameTranslation(island).Y;
        Assert.InRange(landed.Y - ground, .7F, 1.1F);
        Steps(host, run, 10);
        AssertStanding(run, landed);
        AssertAccepted(run);
    }

    /// <summary>
    /// A new game's hold (actors, doors and moving action models) and an assembled building interior entered from it
    /// publish through the Engine, as does the hold again on returning.
    /// </summary>
    [Fact]
    public void The_hold_and_an_assembled_interior_publish_through_the_engine()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallSiteRecord city = world.Unpublished(record => record.Kind == DaggerfallSiteKind.TownCity && record.Exterior is not null);
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(city.Id);
        DaggerfallSiteBuildingId building = world.Profiles.Require(exterior).Doors
            .Select(door => door.ExteriorBuilding)
            .OfType<DaggerfallSiteBuildingId>()
            .First(candidate => world.Profiles.Contains(DaggerfallWorldProfileIds.Interior(city.Id, candidate)));
        DaggerfallWorldProfileKey interior = DaggerfallWorldProfileIds.Interior(city.Id, building);
        DaggerfallWorldProfileKey hold = DaggerfallWorldProfileIds.Dungeon(AssembledWorld.PrivateersHold);
        Dictionary<string, ReadOnlyMemory<byte>> files = AssembledWorldRun.EngineContent(world, city.Id, building);
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = files });
        AssembledWorldRun run = host.Call(engine => world.StartInHold(engine, files));
        try
        {
            Steps(host, run, 3);
            AssertAccepted(run);
            Assert.NotEmpty(run.Session.Sites.Projection.Motion.Visuals);

            int accepted = run.Graphics.AcceptedSnapshots;
            Assert.True(host.Call(_ => run.Session.TryTransitionTo(interior)));
            Assert.Equal(interior, run.Session.Sites.ActiveProfile);
            Steps(host, run, 3);
            Assert.True(run.Graphics.AcceptedSnapshots > accepted);
            AssertAccepted(run);

            Assert.True(host.Call(_ => run.Session.TryTransitionTo(hold)));
            Assert.Equal(hold, run.Session.Sites.ActiveProfile);
            Steps(host, run, 3);
            AssertAccepted(run);
        }
        finally
        {
            host.Call(_ => run.Dispose());
        }
    }

    private static void Steps(EngineTestHost host, AssembledWorldRun run, int count)
    {
        for (int step = 0; step < count; step++) host.Call(_ => run.Step());
    }

    /// <summary>The Engine accepted the update's complete snapshot, every object identity within its safe range.</summary>
    private static void AssertAccepted(AssembledWorldRun run)
    {
        Assert.True(run.Graphics.AcceptedSnapshots > 0);
        Assert.InRange(run.Graphics.LargestObjectId, 1UL, DaggerfallPresentationObjectIds.Maximum);
    }

    /// <summary>The player stands grounded on the real collision where they arrived, neither falling nor sunk into it.</summary>
    private static void AssertStanding(AssembledWorldRun run, WorldPoint landed)
    {
        WorldPoint now = run.Session.State.PlayerControl.Position!.Value;
        Assert.True(run.Session.State.PlayerControl.Motion.Grounded, $"Arrived at {landed}, the player is at {now} and not grounded.");
        Assert.True(MathF.Abs(now.Y - landed.Y) < .25F, $"Arrived at {landed}, the player settled at {now}.");
    }
}
