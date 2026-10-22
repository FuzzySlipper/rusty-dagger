using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Dagger's debug overlay sets are built from the live owners only while they are on: each publishes what it names
/// for a small scene (the Temple of Kynareth), nothing is published while every set is off, and turning one off
/// removes it. The console's probe and clearance queries draw their answer, and inspection targets name the player,
/// the person spoken to and the nearest door.
/// </summary>
public sealed class DeveloperInspectionTests
{
    private static readonly SharedFixture<AssembledWorld> Shared = new(() => new AssembledWorld());
    private static readonly DaggerfallSiteId Charing = new(17, 4);
    private static readonly DaggerfallSiteBuildingId TempleOfKynareth = new(3, 1, 13);

    [Fact]
    public void Each_overlay_set_publishes_its_items_only_while_on_and_turning_it_off_removes_it()
    {
        using AssembledWorldRun run = InTheTemple(out DaggerfallSession session);
        DaggerfallNpc trainer = session.State.Npcs.All.Single(npc => npc.Presence == DaggerfallNpcPresence.Active
            && npc.Services.Contains("training") && session.State.Actors.TryGet(npc.DurableId, out _));

        // Off by default: a frame publishes no set.
        run.Appearance.OverlaySets.Clear();
        run.Step();
        Assert.Empty(run.Appearance.OverlaySets);
        Assert.False(session.SetDebugOverlay("no-such-set", on: true));

        foreach (string set in session.DebugOverlaySets) Assert.True(session.SetDebugOverlay(set, on: true));
        run.Appearance.OverlaySets.Clear();
        run.Step();
        Dictionary<string, OverlayItem[]> drawn = run.Appearance.OverlaySets.GroupBy(request => request.Name)
            .ToDictionary(group => group.Key, group => group.Last().Items.ToArray());
        Assert.Equal(session.DebugOverlaySets.Order(), drawn.Keys.Order());
        string[] Labels(string set) => [.. drawn[set].Where(item => item.Shape == OverlayShape.Label).Select(item => item.Text)];

        // interaction: the query ray, and each target with its focus reason.
        Assert.Contains(drawn[DaggerfallSession.InteractionOverlaySet], item => item.Shape == OverlayShape.Segment);
        Assert.Contains(Labels(DaggerfallSession.InteractionOverlaySet), label => label.Contains(" · ", StringComparison.Ordinal));
        // people: the trainer with their role and service.
        Assert.Contains(Labels(DaggerfallSession.PeopleOverlaySet), label => label.Contains("trainer", StringComparison.Ordinal) && label.Contains("training", StringComparison.Ordinal));
        // doors: the temple's way out.
        Assert.NotEmpty(Labels(DaggerfallSession.DoorsOverlaySet));
        // markers: the interior's anchors at least.
        Assert.Contains(Labels(DaggerfallSession.MarkersOverlaySet), label => label.StartsWith("anchor ", StringComparison.Ordinal));
        // player: the capsule's two spheres and the floor probe.
        Assert.Equal(2, drawn[DaggerfallSession.PlayerOverlaySet].Count(item => item.Shape == OverlayShape.WireSphere));
        Assert.Contains(drawn[DaggerfallSession.PlayerOverlaySet], item => item.Shape == OverlayShape.Label);
        // actors: a temple has no creatures, so the set is published empty rather than left stale.
        Assert.Empty(drawn[DaggerfallSession.ActorsOverlaySet]);

        // Turning a set off removes it at once and stops building it.
        run.Appearance.OverlaySets.Clear();
        Assert.True(session.SetDebugOverlay(DaggerfallSession.PeopleOverlaySet, on: false));
        OverlaySetRequest removed = Assert.Single(run.Appearance.OverlaySets);
        Assert.Equal(DaggerfallSession.PeopleOverlaySet, removed.Name);
        Assert.True(removed.Items.IsEmpty);
        run.Step();
        Assert.DoesNotContain(run.Appearance.OverlaySets.Skip(1), request => request.Name == DaggerfallSession.PeopleOverlaySet);
        Assert.False(session.IsDebugOverlayOn(DaggerfallSession.PeopleOverlaySet));
        Assert.True(session.IsDebugOverlayOn(DaggerfallSession.PlayerOverlaySet));
    }

    [Fact]
    public void Probe_and_clearance_queries_draw_their_answer_and_inspection_targets_name_the_player_the_door_and_the_person_spoken_to()
    {
        using AssembledWorldRun run = InTheTemple(out DaggerfallSession session);
        run.Appearance.OverlaySets.Clear();

        Assert.True(session.ReadSpatialProbe(2).Succeeded);
        OverlaySetRequest probe = Assert.Single(run.Appearance.OverlaySets, request => request.Name == DaggerfallSession.ProbeOverlaySet);
        Assert.Equal(32, probe.Items.ToArray().Count(item => item.Shape == OverlayShape.Segment));

        Vector3 here = session.State.PlayerControl.Position!.Value.ToVector();
        Assert.True(session.ReadSpatialClearance(here.X + 1, here.Y - 1, here.Z).Succeeded);
        OverlaySetRequest clearance = run.Appearance.OverlaySets.Last();
        Assert.Equal(DaggerfallSession.ProbeOverlaySet, clearance.Name);
        Assert.Equal(2, clearance.Items.ToArray().Count(item => item.Shape == OverlayShape.WireSphere));

        Assert.Equal(["player", "door"], session.InspectionTargets().Select(target => target.Name));
        DaggerfallNpc trainer = session.State.Npcs.All.Single(npc => npc.Presence == DaggerfallNpcPresence.Active
            && npc.Services.Contains("training") && session.State.Actors.TryGet(npc.DurableId, out _));
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(), value => value.Identity.Value == checked((ulong)trainer.DurableId));
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
        Assert.Equal(["player", "dialogue", "door"], session.InspectionTargets().Select(value => value.Name));
    }

    private static AssembledWorldRun InTheTemple(out DaggerfallSession session)
    {
        AssembledWorldRun run = Shared.Value.Start(Charing, TempleOfKynareth);
        session = run.Session;
        run.Spatial.FloorHit = request => request.Direction.Y < -.99f
            ? default(SpatialHit) with { Present = true, Converged = true, Point = request.Origin - Vector3.UnitY, Normal = Vector3.UnitY }
            : default;
        Assert.True(session.Enter("Charing", "Temple of Kynareth").Succeeded);
        return run;
    }
}
