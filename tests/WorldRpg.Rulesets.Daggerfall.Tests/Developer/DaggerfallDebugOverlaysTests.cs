using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallDebugOverlays"/> built without the session: an Engine whose graphics record the overlay sets,
/// and a player with or without a position. The owners the sets read (activation, actors, senses, collision) are never
/// reached on these paths and are passed as null; what each set draws in a real place is
/// <c>DeveloperInspectionTests</c>' end-to-end case.
/// </summary>
public sealed class DaggerfallDebugOverlaysTests
{
    private readonly AppearanceFake _graphics = new([]);

    [Fact]
    public void Sets_turn_on_and_off_by_name_and_turning_one_off_removes_it()
    {
        DaggerfallDebugOverlays overlays = Overlays(new PlayerControlState(null, 0f, 0f));
        Assert.Equal(["interaction", "people", "doors", "actors", "markers", "player", "swing"], overlays.Sets);
        Assert.False(overlays.Set("probe", on: true));
        Assert.False(overlays.Set("no-such-set", on: true));

        Assert.True(overlays.Set(DaggerfallDebugOverlays.PeopleOverlaySet, on: true));
        Assert.True(overlays.IsOn(DaggerfallDebugOverlays.PeopleOverlaySet));
        Assert.Empty(_graphics.OverlaySets);

        // Off a set that is off: nothing to remove.
        Assert.True(overlays.Set(DaggerfallDebugOverlays.DoorsOverlaySet, on: false));
        Assert.Empty(_graphics.OverlaySets);

        Assert.True(overlays.Set(DaggerfallDebugOverlays.PeopleOverlaySet, on: false));
        OverlaySetRequest removed = Assert.Single(_graphics.OverlaySets);
        Assert.Equal(DaggerfallDebugOverlays.PeopleOverlaySet, removed.Name);
        Assert.Equal(0, removed.Items.Length);
        Assert.False(overlays.IsOn(DaggerfallDebugOverlays.PeopleOverlaySet));
    }

    [Fact]
    public void Nothing_is_published_while_every_set_is_off_or_the_player_has_no_position()
    {
        DaggerfallDebugOverlays nowhere = Overlays(new PlayerControlState(null, 0f, 0f));
        foreach (string set in nowhere.Sets) nowhere.Set(set, on: true);
        nowhere.Publish();
        Assert.Empty(_graphics.OverlaySets);
        Assert.Empty(nowhere.InspectionTargets());

        Overlays(new PlayerControlState(new WorldPoint(0f, 0f, 0f), 0f, 0f)).Publish();
        Assert.Empty(_graphics.OverlaySets);
    }

    [Fact]
    public void A_probe_draws_each_ray_to_its_hit_or_its_full_range()
    {
        DaggerfallDebugOverlays overlays = Overlays(new PlayerControlState(null, 0f, 0f));
        overlays.PublishProbe("""
            {"samples": [
              {"origin": [0, 1, 0], "direction": [0, 0, -1], "range": 2, "hit": true, "point": [0, 1, -1.5]},
              {"origin": [0, 0.3, 0], "direction": [0, 0, -1], "range": 2, "hit": false}]}
            """);
        OverlaySetRequest probe = Assert.Single(_graphics.OverlaySets);
        Assert.Equal(DaggerfallDebugOverlays.ProbeOverlaySet, probe.Name);
        OverlayItem[] items = probe.Items.ToArray();
        Assert.Equal(2, items.Count(item => item.Shape == OverlayShape.Segment));
        OverlayItem hit = Assert.Single(items, item => item.Shape == OverlayShape.Marker);
        Assert.Equal(new Vector3(0, 1, -1.5F), hit.A);
        Assert.Contains(items, item => item.Shape == OverlayShape.Segment && item.B == new Vector3(0, .3F, -2));
    }

    private DaggerfallDebugOverlays Overlays(PlayerControlState player) => new(EngineContextFake.Create(null!, null!, _graphics).Context, player,
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, () => null, 2.5);
}
