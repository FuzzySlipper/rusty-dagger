using System.Numerics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>A save made outdoors restores to the same place with the same cells and towns resident.</summary>
public sealed class ExteriorSaveSessionTests
{
    private static readonly SharedFixture<AssembledWorld> Shared = new(() => new AssembledWorld());

    /// <summary>
    /// A save in a town's street, made after walking away from where the journey landed, restores the player to the same
    /// place in the town's frame, facing the same way, with the same cell window, the same neighbouring towns, the town
    /// admitted, and the same collision placed.
    /// </summary>
    [Fact]
    public void A_save_in_a_town_street_restores_to_the_same_place_with_the_same_cells_resident()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallSiteRecord city = world.Unpublished(record => record.Kind == DaggerfallSiteKind.TownCity && record.Exterior is not null);
        DaggerfallWorldProfileKey town = DaggerfallWorldProfileIds.Exterior(city.Id);
        using AssembledWorldRun run = world.Start(city.Id);
        run.Travel(city);
        run.Walk(40);
        Assert.Equal(town, run.Session.Sites.ActiveProfile);
        Assert.True(run.Session.Exterior.LocationAtPlayer() is { } inTown && inTown.Id == city.Id, "The walk left the town.");
        Outdoors saved = Outdoors.Read(run);

        using AssembledWorldRun restored = world.Restore(run.Session.CaptureSave(), city.Id);
        Outdoors again = Outdoors.Read(restored);

        Assert.Equal(saved.Profile, again.Profile);
        Assert.True(Vector3.Distance(saved.Position.ToVector(), again.Position.ToVector()) < 1e-3F, $"{saved.Position} vs {again.Position}");
        Assert.Equal(saved.Yaw, again.Yaw, 5);
        Assert.Equal(saved.Center, again.Center);
        Assert.Equal(saved.Cells, again.Cells);
        Assert.Equal(saved.Towns, again.Towns);
        Assert.True(again.TownLoaded);
        Assert.Equal(saved.Placed, again.Placed);
    }

    private sealed record Outdoors(DaggerfallWorldProfileKey Profile, WorldPoint Position, float Yaw, DaggerfallExteriorCellId Center,
        DaggerfallExteriorCellId[] Cells, DaggerfallWorldProfileKey[] Towns, bool TownLoaded, ulong[] Placed)
    {
        internal static Outdoors Read(AssembledWorldRun run)
        {
            DaggerfallSession session = run.Session;
            DaggerfallExteriorWorld exterior = session.Exterior;
            return new(session.Sites.ActiveProfile, exterior.ExteriorSitePosition(session.State.PlayerControl.Position!.Value),
                session.State.PlayerControl.YawRadians, exterior.CaptureExteriorResidency()!.Value.Center,
                [.. exterior.ResidentExteriorCells.OrderBy(cell => cell.X).ThenBy(cell => cell.Y)], [.. exterior.ResidentExteriorProfiles],
                exterior.ActiveLocationLoaded, [.. run.Spatial.PlacedArtifacts.Order()]);
        }
    }
}
