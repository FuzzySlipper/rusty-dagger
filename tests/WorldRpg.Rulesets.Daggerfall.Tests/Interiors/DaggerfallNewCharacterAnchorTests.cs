using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Where a new character begins in its start profile, with no session.</summary>
public sealed class DaggerfallNewCharacterAnchorTests
{
    [Fact]
    public void A_dungeon_start_begins_on_its_enter_marker_and_any_other_stays_on_its_start()
    {
        DaggerfallSiteAnchor enter = new(DaggerfallLocationAssembly.EnterAnchor, new(1f, 0f, 2f), 0f, 0f);
        Dictionary<string, DaggerfallSiteAnchor> anchors = new() { [DaggerfallLocationAssembly.EnterAnchor] = enter };

        Assert.Same(enter, DaggerfallSiteCrossing.NewCharacterAnchor(DaggerfallWorldProfileKind.Dungeon, anchors));
        Assert.Null(DaggerfallSiteCrossing.NewCharacterAnchor(DaggerfallWorldProfileKind.Exterior, anchors));
        Assert.Null(DaggerfallSiteCrossing.NewCharacterAnchor(DaggerfallWorldProfileKind.Dungeon, new Dictionary<string, DaggerfallSiteAnchor>()));
    }
}
