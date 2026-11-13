using System.Numerics;
using Rusty.Engine.Entities;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>A save made inside a place entered through a door restores there, and leaving it brings back the same exterior.</summary>
public sealed class InteriorSaveSessionTests
{
    private static readonly SharedFixture<AssembledWorld> Shared = new(() => new AssembledWorld());

    /// <summary>
    /// A save inside a dungeon, made after walking from its start, restores to the same place facing the same way, with
    /// the same entrance to return through, no exterior window and the same collision placed; leaving through its exit,
    /// from the save and from the session it was made in, lands at the same spot with the same cells resident.
    /// </summary>
    [Fact]
    public void A_save_inside_a_dungeon_restores_to_the_same_place_with_the_same_cells_resident()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallSiteRecord site = world.Unpublished(record => record.Kind is DaggerfallSiteKind.DungeonLabyrinth or DaggerfallSiteKind.DungeonKeep or DaggerfallSiteKind.DungeonRuin
            && record.Exterior is not null && record.DungeonBlocks.Count != 0
            && world.Profiles.Require(DaggerfallWorldProfileIds.Exterior(record.Id)).Portals.Count != 0);
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(site.Id), dungeon = DaggerfallWorldProfileIds.Dungeon(site.Id);
        using AssembledWorldRun run = world.Start(site.Id);
        run.Travel(site);
        DaggerfallSitePortal entrance = world.Profiles.Require(exterior).Portals.First(portal => portal.DestinationLogicalProfile == dungeon.LogicalId);
        (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) door = run.Session.Sites.Projection.Portals.All.Single(value => value.Portal.Id == entrance.Id);
        run.Use(door.Portal, door.Entity);
        run.Walk(20);
        Assert.Equal(dungeon, run.Session.Sites.ActiveProfile);
        Inside saved = Inside.Read(run);
        Assert.Empty(saved.Cells);

        using AssembledWorldRun restored = world.Restore(run.Session.CaptureSave(), site.Id);
        Inside again = Inside.Read(restored);

        Assert.Equal(saved.Profile, again.Profile);
        Assert.Equal(exterior, again.Return);
        Assert.True(Vector3.Distance(saved.Position.ToVector(), again.Position.ToVector()) < 1e-3F, $"{saved.Position} vs {again.Position}");
        Assert.Equal(saved.Yaw, again.Yaw, 5);
        Assert.False(restored.Session.Exterior.ExteriorResidencyInitialized);
        Assert.Empty(again.Cells);
        Assert.Equal(saved.Placed, again.Placed);

        Outside left = Outside.Leave(run), leftAgain = Outside.Leave(restored);
        Assert.Equal(exterior, left.Profile);
        Assert.Equal(left.Profile, leftAgain.Profile);
        Assert.True(Vector3.Distance(left.Position.ToVector(), leftAgain.Position.ToVector()) < 1e-2F, $"{left.Position} vs {leftAgain.Position}");
        Assert.Equal(left.Cells, leftAgain.Cells);
        Assert.Equal(left.Towns, leftAgain.Towns);
        Assert.Equal(left.Placed, leftAgain.Placed);
    }

    private sealed record Inside(DaggerfallWorldProfileKey Profile, DaggerfallWorldProfileKey? Return, WorldPoint Position, float Yaw,
        DaggerfallExteriorCellId[] Cells, ulong[] Placed)
    {
        internal static Inside Read(AssembledWorldRun run) => new(run.Session.Sites.ActiveProfile, run.Session.Sites.ReturnProfile,
            run.Session.State.PlayerControl.Position!.Value, run.Session.State.PlayerControl.YawRadians,
            [.. run.Session.Exterior.ResidentExteriorCells], [.. run.Spatial.PlacedArtifacts.Order()]);
    }

    private sealed record Outside(DaggerfallWorldProfileKey Profile, WorldPoint Position, DaggerfallExteriorCellId[] Cells,
        DaggerfallWorldProfileKey[] Towns, ulong[] Placed)
    {
        internal static Outside Leave(AssembledWorldRun run)
        {
            (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) exit = run.Session.Sites.Projection.Portals.All.First();
            run.Use(exit.Portal, exit.Entity);
            DaggerfallExteriorWorld exterior = run.Session.Exterior;
            return new(run.Session.Sites.ActiveProfile, exterior.ExteriorSitePosition(run.Session.State.PlayerControl.Position!.Value),
                [.. exterior.ResidentExteriorCells.OrderBy(cell => cell.X).ThenBy(cell => cell.Y)], [.. exterior.ResidentExteriorProfiles],
                [.. run.Spatial.PlacedArtifacts.Order()]);
        }
    }
}
