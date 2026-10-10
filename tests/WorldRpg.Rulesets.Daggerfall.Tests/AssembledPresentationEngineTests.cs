using System.Diagnostics;
using System.Numerics;
using Rusty.Engine.Entities;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using Xunit.Abstractions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The assembled world over the real Engine's content, spatial, graphics and world origin services, one Engine
/// callback per update as the product runs: every update publishes its complete appearance snapshot through the
/// Engine's validation, and the player moves on the real collision. The Engine test host has no Product.Update
/// callback, so only a sprite playback advance is answered outside the Engine (<see cref="EngineGraphicsOutsideUpdate"/>).
/// </summary>
public sealed class AssembledPresentationEngineTests(ITestOutputHelper output)
{
    private static readonly SharedFixture<AssembledWorld> Shared = new(() => new AssembledWorld());

    /// <summary>A bound on the island window's objects: its terrain cells, nature batches and the locations in it.</summary>
    private const int IslandObjectBound = 10_000;

    /// <summary>
    /// The playtested journey: a new game leaves Privateer's Hold onto its island, whose terrain, nature in batches,
    /// entrance and resident neighbouring locations the Engine accepts in one snapshot, and the player stands on the
    /// island's ground at the entrance rather than inside it. Going back down into the hold retires the whole window
    /// in one transition, and the session then retires its presentation itself.
    /// </summary>
    [Fact]
    public void Leaving_the_hold_draws_the_island_window_and_going_back_down_retires_it()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallWorldProfileKey island = DaggerfallWorldProfileIds.Exterior(AssembledWorld.PrivateersHold);
        DaggerfallWorldProfileKey hold = DaggerfallWorldProfileIds.Dungeon(AssembledWorld.PrivateersHold);
        Dictionary<string, ReadOnlyMemory<byte>> files = AssembledWorldRun.EngineContent(world, AssembledWorld.PrivateersHold);
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = files });
        long managedBefore = GC.GetTotalMemory(forceFullCollection: true);
        long processBefore = Environment.WorkingSet;
        Stopwatch admission = Stopwatch.StartNew();
        AssembledWorldRun run = host.Call(engine => world.Start(engine, files));
        admission.Stop();
        try
        {
            long managedAfter = GC.GetTotalMemory(forceFullCollection: true);
            Assert.Equal(island, run.Session.Sites.ActiveProfile);
            AssertAccepted(run);
            IReadOnlyList<ulong> ids = run.Graphics.LastSnapshotIds;
            int nature = ids.Count(id => id >= 1UL << 49 && id < 1UL << 50);
            int terrain = ids.Count(id => id >= 1UL << 48 && id < 1UL << 49);
            output.WriteLine($"Island admission (new game, hold, leaving it): {admission.Elapsed.TotalSeconds:F1} s; snapshot {ids.Count} objects "
                + $"({terrain} terrain cells, {nature} nature batches); managed +{(managedAfter - managedBefore) / (1024 * 1024)} MiB, "
                + $"process working set +{(Environment.WorkingSet - processBefore) / (1024 * 1024)} MiB.");
            Assert.True(ids.Count > 1000, $"The island window published only {ids.Count} objects.");
            Assert.True(ids.Count < IslandObjectBound, $"The island window published {ids.Count} objects rather than batches.");
            Assert.Equal(DaggerfallExteriorCellResidency.StreamingDimension * DaggerfallExteriorCellResidency.StreamingDimension, terrain);
            Assert.InRange(nature, terrain, terrain * 32);

            // Every resident neighbour's meshes are drawn, each under its own location slot.
            IReadOnlyCollection<DaggerfallWorldProfileKey> neighbours = run.Session.Sites.ResidentExteriorProfiles;
            Assert.NotEmpty(neighbours);
            int neighbourMeshes = neighbours.Sum(key => world.Profiles.Require(key).Geometry.Meshes.Count);
            Assert.True(neighbourMeshes > 0);
            Assert.Equal(neighbourMeshes, ids.Count(IsNeighbourMesh));
            output.WriteLine($"{neighbours.Count} resident neighbours draw {neighbourMeshes} meshes.");

            WorldPoint landed = run.Session.State.PlayerControl.Position!.Value;
            // The entrance threshold stands on the ground: the standing capsule centre is about half its height above it.
            DaggerfallSiteAnchor landing = world.Profiles.Require(island).RequireAnchor(DaggerfallLocationAssembly.DungeonEntranceAnchor);
            float ground = landing.Position.Y + run.Session.Sites.ExteriorProfileFrameTranslation(island).Y;
            Assert.InRange(landed.Y - ground, .7F, 1.1F);
            Steps(host, run, 10);
            AssertStanding(run, landed);
            AssertAccepted(run);

            Stopwatch descent = Stopwatch.StartNew();
            Assert.True(host.Call(_ => run.Session.TryTransitionTo(hold)));
            descent.Stop();
            Assert.Equal(hold, run.Session.Sites.ActiveProfile);
            Assert.Empty(run.Session.Sites.ResidentExteriorProfiles);
            Steps(host, run, 3);
            AssertAccepted(run);
            Assert.DoesNotContain(run.Graphics.LastSnapshotIds, id => id >= 1UL << 48 && id < 1UL << 50);
            output.WriteLine($"Going back down into the hold: {descent.Elapsed.TotalSeconds:F2} s; the hold snapshot holds {run.Graphics.LastSnapshotObjects} objects.");
        }
        finally
        {
            Stopwatch disposal = Stopwatch.StartNew();
            host.Call(_ => run.Dispose());
            output.WriteLine($"Session disposal: {disposal.Elapsed.TotalSeconds:F2} s.");
        }
    }

    /// <summary>
    /// From the island, a building of a resident neighbour (the island has none of its own) is entered and left
    /// again, each site publishing through the Engine and the island window drawn again on leaving.
    /// </summary>
    [Fact]
    public void A_building_of_the_island_window_is_entered_and_left()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallWorldProfileKey island = DaggerfallWorldProfileIds.Exterior(AssembledWorld.PrivateersHold);
        (DaggerfallSiteId site, DaggerfallSiteBuildingId building) = NeighbourBuilding(world);
        DaggerfallWorldProfileKey interior = DaggerfallWorldProfileIds.Interior(site, building);
        Dictionary<string, ReadOnlyMemory<byte>> files = AssembledWorldRun.EngineContent(world, site, building);
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = files });
        AssembledWorldRun run = host.Call(engine => world.Start(engine, files));
        try
        {
            Assert.Equal(island, run.Session.Sites.ActiveProfile);
            AssertAccepted(run);

            int accepted = run.Graphics.AcceptedSnapshots;
            Stopwatch entering = Stopwatch.StartNew();
            Assert.True(host.Call(_ => run.Session.TryTransitionTo(interior)));
            entering.Stop();
            Assert.Equal(interior, run.Session.Sites.ActiveProfile);
            Steps(host, run, 3);
            Assert.True(run.Graphics.AcceptedSnapshots > accepted);
            AssertAccepted(run);

            Stopwatch leaving = Stopwatch.StartNew();
            Assert.True(host.Call(_ => run.Session.TryTransitionTo(island)));
            leaving.Stop();
            Assert.Equal(island, run.Session.Sites.ActiveProfile);
            Steps(host, run, 3);
            AssertAccepted(run);
            Assert.True(run.Graphics.LastSnapshotObjects < IslandObjectBound);
            Assert.Contains(run.Graphics.LastSnapshotIds, IsNeighbourMesh);
            output.WriteLine($"Entering {interior.LogicalId}: {entering.Elapsed.TotalSeconds:F2} s; leaving it onto the island: {leaving.Elapsed.TotalSeconds:F2} s.");
        }
        finally
        {
            host.Call(_ => run.Dispose());
        }
    }

    /// <summary>
    /// Interiors of the island window's neighbouring locations whose entrance markers lie on their floors: each is entered
    /// from the island as a building door enters it, and the arriving player stands on the floor, walks on the real collision
    /// without the Engine reporting the capsule inside it, and leaves again onto the island to walk there.
    /// </summary>
    [Fact]
    public void Entering_a_neighbouring_interior_stands_the_player_on_its_floor()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallWorldProfileKey island = DaggerfallWorldProfileIds.Exterior(AssembledWorld.PrivateersHold);
        DaggerfallSiteId hamlet = new(AssembledWorld.PrivateersHold.Region, 197);
        DaggerfallSiteId village = new(AssembledWorld.PrivateersHold.Region, 198);
        (DaggerfallSiteId Site, DaggerfallSiteBuildingId Building)[] buildings =
            [(hamlet, new(0, 0, 0)), (hamlet, new(0, 1, 0)), (hamlet, new(2, 0, 0)), (village, new(0, 0, 3))];
        Dictionary<string, ReadOnlyMemory<byte>> files = new(StringComparer.Ordinal);
        foreach ((DaggerfallSiteId site, DaggerfallSiteBuildingId building) in buildings)
            foreach ((string path, ReadOnlyMemory<byte> bytes) in AssembledWorldRun.EngineContent(world, site, building)) files[path] = bytes;
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = files });
        AssembledWorldRun run = host.Call(engine => world.Start(engine, files));
        try
        {
            foreach ((DaggerfallSiteId site, DaggerfallSiteBuildingId building) in buildings)
            {
                DaggerfallWorldProfileKey interior = DaggerfallWorldProfileIds.Interior(site, building);
                WorldPoint marker = Assert.NotNull(world.Profiles.Require(interior).Project.PlayerPosition);
                WorldPoint entered = run.Session.Sites.ExteriorSitePosition(run.Session.State.PlayerControl.Position!.Value);
                Assert.True(host.Call(_ => run.Session.TryTransitionTo(interior)));
                Assert.Equal(interior, run.Session.Sites.ActiveProfile);
                WorldPoint landed = run.Session.State.PlayerControl.Position!.Value;
                output.WriteLine($"{interior.LogicalId}: marker {marker}, landed {landed}.");
                Assert.InRange(landed.Y - marker.Y, .7F, 1.1F);
                Steps(host, run, 3);
                AssertStanding(run, landed);
                host.Call(_ => run.Walk(30));
                WorldPoint walked = run.Session.State.PlayerControl.Position!.Value;
                output.WriteLine($"{interior.LogicalId}: walked to {walked}.");
                Assert.True(run.Session.State.PlayerControl.Motion.Grounded, $"Walking from {landed}, the player is at {walked} and not grounded.");
                Assert.InRange(walked.Y - marker.Y, .5F, 1.6F);
                AssertAccepted(run);

                Assert.True(host.Call(_ => run.Session.TryTransitionTo(island)));
                Assert.Equal(island, run.Session.Sites.ActiveProfile);
                // Leaving returns to the standing pose the player entered from, not raised again.
                WorldPoint outside = run.Session.State.PlayerControl.Position!.Value;
                WorldPoint returned = run.Session.Sites.ExteriorSitePosition(outside);
                Assert.True(Vector3.Distance(entered.ToVector(), returned.ToVector()) < 1e-2F, $"Entered from {entered}, returned to {returned}.");
                Steps(host, run, 3);
                AssertStanding(run, outside);
                host.Call(_ => run.Walk(10));
                Assert.True(run.Session.State.PlayerControl.Motion.Grounded);
                AssertAccepted(run);
            }
        }
        finally
        {
            host.Call(_ => run.Dispose());
        }
    }

    /// <summary>
    /// The hold's start marker lies above its floor but lower than a standing capsule's centre. A new game stands on the
    /// floor there; no other location of the island window has a dungeon, so the island's own is then entered from the
    /// island with no entrance to return through (as after a recall, or a game that remembers none), and the player lands
    /// standing at the same start, walks without the Engine reporting the capsule inside the collision, and returns to
    /// the island at the standing pose they left rather than raised again.
    /// </summary>
    [Fact]
    public void A_dungeon_arrival_stands_on_its_floor_and_a_remembered_entrance_is_not_raised_again()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallWorldProfileKey island = DaggerfallWorldProfileIds.Exterior(AssembledWorld.PrivateersHold);
        DaggerfallWorldProfileKey hold = DaggerfallWorldProfileIds.Dungeon(AssembledWorld.PrivateersHold);
        Dictionary<string, ReadOnlyMemory<byte>> files = AssembledWorldRun.EngineContent(world, AssembledWorld.PrivateersHold);
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = files });
        AssembledWorldRun run = host.Call(engine => world.StartInHold(engine, files));
        try
        {
            WorldPoint start = Assert.NotNull(world.Profiles.Require(hold).Project.PlayerPosition);
            WorldPoint newGame = run.Session.State.PlayerControl.Position!.Value;
            output.WriteLine($"{hold.LogicalId}: start {start}, a new game stands at {newGame}.");
            Assert.Equal(start.X, newGame.X);
            Assert.Equal(start.Z, newGame.Z);
            Assert.True(newGame.Y > start.Y, $"A new game at {start} was not raised onto the hold's floor.");
            Steps(host, run, 3);
            AssertStanding(run, newGame);

            (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) exit = Assert.Single(run.Session.Sites.Projection.Portals.All);
            _ = host.Call(_ => run.Use(exit.Portal, exit.Entity));
            Assert.Equal(island, run.Session.Sites.ActiveProfile);
            Steps(host, run, 3);
            WorldPoint leftIsland = run.Session.Sites.ExteriorSitePosition(run.Session.State.PlayerControl.Position!.Value);
            host.Call(_ => run.Session.Sites.ClearReturnDestination());
            Assert.True(host.Call(_ => run.Session.TryTransitionTo(hold)));
            Assert.Equal(hold, run.Session.Sites.ActiveProfile);
            WorldPoint landed = run.Session.State.PlayerControl.Position!.Value;
            output.WriteLine($"{hold.LogicalId}: entered from the island at {landed}.");
            Assert.Equal(newGame, landed);
            Steps(host, run, 3);
            AssertStanding(run, landed);
            host.Call(_ => run.Walk(30));
            WorldPoint walked = run.Session.State.PlayerControl.Position!.Value;
            output.WriteLine($"{hold.LogicalId}: walked to {walked}.");
            Assert.True(run.Session.State.PlayerControl.Motion.Grounded, $"Walking from {landed}, the player is at {walked} and not grounded.");
            Assert.True(MathF.Abs(walked.Y - landed.Y) < .5F, $"Walking from {landed}, the player is at {walked}.");
            AssertAccepted(run);

            // Leaving returns through the remembered entrance: the pose left on the island is already a standing centre.
            Assert.True(host.Call(_ => run.Session.TryTransitionTo(island)));
            Assert.Equal(island, run.Session.Sites.ActiveProfile);
            WorldPoint returned = run.Session.Sites.ExteriorSitePosition(run.Session.State.PlayerControl.Position!.Value);
            output.WriteLine($"Left the island at {leftIsland}, returned to {returned}.");
            Assert.True(Vector3.Distance(leftIsland.ToVector(), returned.ToVector()) < 1e-2F, $"Left the island at {leftIsland}, returned to {returned}.");
            WorldPoint outside = run.Session.State.PlayerControl.Position!.Value;
            Steps(host, run, 3);
            AssertStanding(run, outside);
            AssertAccepted(run);
        }
        finally
        {
            host.Call(_ => run.Dispose());
        }
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

    /// <summary>
    /// The first building of the village beside the island, whose assembled interior is entered.
    /// </summary>
    private static (DaggerfallSiteId Site, DaggerfallSiteBuildingId Building) NeighbourBuilding(AssembledWorld world)
    {
        DaggerfallSiteId village = new(AssembledWorld.PrivateersHold.Region, 198);
        DaggerfallSiteBuildingId building = new(0, 0, 0);
        DaggerfallSiteRecord hold = world.Definitions.Locations.Records.Single(record => record.Id == AssembledWorld.PrivateersHold);
        DaggerfallSiteRecord record = world.Definitions.Locations.Records.Single(record => record.Id == village);
        Assert.InRange(Math.Abs(record.MapPixelX - hold.MapPixelX), 0, DaggerfallExteriorCellResidency.StreamingRadius);
        Assert.InRange(Math.Abs(record.MapPixelY - hold.MapPixelY), 0, DaggerfallExteriorCellResidency.StreamingRadius);
        Assert.Contains(world.Profiles.Require(DaggerfallWorldProfileIds.Exterior(village)).Doors, door => door.ExteriorBuilding == building);
        Assert.True(world.Profiles.Contains(DaggerfallWorldProfileIds.Interior(village, building)));
        return (village, building);
    }

    /// <summary>A static mesh of a location drawn under a resident neighbour's slot rather than the active location's.</summary>
    private static bool IsNeighbourMesh(ulong id) =>
        id >= DaggerfallPresentationObjectIds.WorldMesh(DaggerfallPresentationObjectIds.ActiveLocationSlot + 1, 0)
        && id < DaggerfallPresentationObjectIds.TransientVisualFloor
        && (id & uint.MaxValue) < 1UL << 31;

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
