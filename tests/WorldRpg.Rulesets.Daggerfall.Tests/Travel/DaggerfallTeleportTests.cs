using System.Numerics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The teleport built alone over a stand-in for the sites; the live crossing and saves are the session's (DaggerfallMysticismEffectsTests).</summary>
public sealed class DaggerfallTeleportTests
{
    [Fact]
    public void An_anchor_remembers_where_the_player_stands_and_a_recall_relocates_there_once()
    {
        Sites sites = new();
        PresentationState presentation = new("", 5d);
        DaggerfallTeleport teleport = new(sites, new PlayerControlState(new WorldPoint(1f, 2f, 3f), .5f, .1f),
            new DaggerfallTransportPolicy(DaggerfallTuning.Defaults.Transport), () => null, presentation, null, null);

        teleport.Choose("stale", "anchor");
        Assert.Equal("Teleport choice is no longer current.", presentation.LastOutcome);
        teleport.Request("first");
        Assert.False(teleport.View!.AnchorSet);
        teleport.Choose("first", "recall");
        Assert.Equal("A teleport anchor must be set first.", presentation.LastOutcome);
        Assert.Null(teleport.View);

        teleport.Request("second");
        teleport.Choose("second", "anchor");
        Assert.Equal("Teleport anchor set.", presentation.LastOutcome);
        DaggerfallTeleportAnchor anchor = teleport.Anchor!;
        Assert.Equal(sites.ActiveProfile, anchor.Profile.Require());
        Assert.Equal((11f, 2f, 3f, .5f, .1f), (anchor.Pose.X, anchor.Pose.Y, anchor.Pose.Z, anchor.Pose.YawRadians, anchor.Pose.PitchRadians));

        teleport.Request("third");
        teleport.Choose("third", "recall");
        Assert.Equal("Recalled to the teleport anchor.", presentation.LastOutcome);
        var (profile, pose) = Assert.Single(sites.Relocations);
        Assert.Equal(sites.ActiveProfile, profile);
        Assert.Equal(new WorldPoint(11f, 2f, 3f), pose.Position);
        Assert.True(sites.ReturnRestored);
        Assert.Null(teleport.Anchor);
    }

    [Fact]
    public void A_ship_anchor_refuses_to_recall_once_the_ship_is_no_longer_owned()
    {
        Sites sites = new();
        PresentationState presentation = new("", 5d);
        DaggerfallTeleportAnchor aboard = new(DaggerfallWorldProfileKeySave.Capture(sites.ActiveProfile), new(0f, 0f, 0f, 0f, 0f), null, null,
            DaggerfallShipType.Small, null);
        DaggerfallTeleport teleport = new(sites, new PlayerControlState(new WorldPoint(0f, 0f, 0f), 0f, 0f),
            new DaggerfallTransportPolicy(DaggerfallTuning.Defaults.Transport), () => null, presentation, "pending", aboard);

        teleport.Choose("pending", "recall");
        Assert.Equal("The anchored ship is no longer owned.", presentation.LastOutcome);
        Assert.Empty(sites.Relocations);
        Assert.Same(aboard, teleport.Anchor);
    }

    private sealed class Sites : IDaggerfallTeleportSites
    {
        public DaggerfallWorldProfileKey ActiveProfile { get; } = DaggerfallWorldProfileIds.Exterior(new DaggerfallSiteId(17, 4));
        public DaggerfallWorldProfileKey? ReturnProfile => null;
        public DaggerfallSiteReturnPose? ReturnPose => null;
        internal List<(DaggerfallWorldProfileKey Profile, DaggerfallSiteAnchor Pose)> Relocations { get; } = [];
        internal bool ReturnRestored { get; private set; }
        public Vector3 ProfilePosition(WorldPoint local) => local.ToVector() + new Vector3(10f, 0f, 0f);
        public bool TryRelocatePlayer(DaggerfallWorldProfileKey profile, DaggerfallSiteAnchor pose) { Relocations.Add((profile, pose)); return true; }
        public void RestoreReturnDestination(DaggerfallWorldProfileKey? profile, DaggerfallSiteReturnPose? pose) => ReturnRestored = profile is null && pose is null;
    }
}
