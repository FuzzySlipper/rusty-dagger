using WorldRpg.Kit;
using Rusty.Engine.Debugging;
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
        Assert.False(session.Commands<IDeveloperInspectionCommands>().Set("no-such-set", on: true));

        foreach (string set in session.Commands<IDeveloperInspectionCommands>().Sets) Assert.True(session.Commands<IDeveloperInspectionCommands>().Set(set, on: true));
        run.Appearance.OverlaySets.Clear();
        run.Step();
        Dictionary<string, OverlayItem[]> drawn = run.Appearance.OverlaySets.GroupBy(request => request.Name)
            .ToDictionary(group => group.Key, group => group.Last().Items.ToArray());
        Assert.Equal(session.Commands<IDeveloperInspectionCommands>().Sets.Order(), drawn.Keys.Order());
        string[] Labels(string set) => [.. drawn[set].Where(item => item.Shape == OverlayShape.Label).Select(item => item.Text)];

        // interaction: the query ray, and each target with its focus reason.
        Assert.Contains(drawn[DaggerfallDebugOverlays.InteractionOverlaySet], item => item.Shape == OverlayShape.Segment);
        Assert.Contains(Labels(DaggerfallDebugOverlays.InteractionOverlaySet), label => label.Contains(" · ", StringComparison.Ordinal));
        // people: the trainer with their role and service.
        Assert.Contains(Labels(DaggerfallDebugOverlays.PeopleOverlaySet), label => label.Contains("trainer", StringComparison.Ordinal) && label.Contains("training", StringComparison.Ordinal));
        // doors: the temple's way out.
        Assert.NotEmpty(Labels(DaggerfallDebugOverlays.DoorsOverlaySet));
        // markers: the interior's anchors at least.
        Assert.Contains(Labels(DaggerfallDebugOverlays.MarkersOverlaySet), label => label.StartsWith("anchor ", StringComparison.Ordinal));
        // player: the capsule's two spheres and the floor probe.
        Assert.Equal(2, drawn[DaggerfallDebugOverlays.PlayerOverlaySet].Count(item => item.Shape == OverlayShape.WireSphere));
        Assert.Contains(drawn[DaggerfallDebugOverlays.PlayerOverlaySet], item => item.Shape == OverlayShape.Label);
        // actors: a temple has no creatures, so the set is published empty rather than left stale.
        Assert.Empty(drawn[DaggerfallDebugOverlays.ActorsOverlaySet]);

        // Turning a set off removes it at once and stops building it.
        run.Appearance.OverlaySets.Clear();
        Assert.True(session.Commands<IDeveloperInspectionCommands>().Set(DaggerfallDebugOverlays.PeopleOverlaySet, on: false));
        OverlaySetRequest removed = Assert.Single(run.Appearance.OverlaySets);
        Assert.Equal(DaggerfallDebugOverlays.PeopleOverlaySet, removed.Name);
        Assert.True(removed.Items.IsEmpty);
        run.Step();
        Assert.DoesNotContain(run.Appearance.OverlaySets.Skip(1), request => request.Name == DaggerfallDebugOverlays.PeopleOverlaySet);
        Assert.False(session.Commands<IDeveloperInspectionCommands>().IsOn(DaggerfallDebugOverlays.PeopleOverlaySet));
        Assert.True(session.Commands<IDeveloperInspectionCommands>().IsOn(DaggerfallDebugOverlays.PlayerOverlaySet));
    }

    [Fact]
    public void The_actors_set_draws_a_creatures_live_senses_and_the_target_it_pursues()
    {
        using AssembledWorldRun run = InTheTemple(out DaggerfallSession session);
        Vector3 here = session.State.PlayerControl.Position!.Value.ToVector();
        long rat = session.SpawnActor("rat", new(new WorldRpg.Kit.Controls.WorldPoint(here.X + 2, here.Y, here.Z), 0));
        for (int step = 0; step < 3; step++) run.Step();
        Assert.True(session.Commands<IDeveloperInspectionCommands>().Set(DaggerfallDebugOverlays.ActorsOverlaySet, on: true));
        run.Appearance.OverlaySets.Clear();
        run.Step();
        OverlayItem[] items = run.Appearance.OverlaySets.Last(request => request.Name == DaggerfallDebugOverlays.ActorsOverlaySet).Items.ToArray();

        // The senses the perception policy judged the rat with: sight within the query's reach, and hearing.
        var senses = session.EnemyBehavior.LastSenses[rat];
        float[] spheres = [.. items.Where(item => item.Shape == OverlayShape.WireSphere).Select(item => item.Size)];
        Assert.Contains((float)Math.Min(senses.SightRadius + senses.SightModifier, session.EnemyBehavior.DetectionDistance), spheres);
        Assert.Contains((float)(senses.HearingRadius + senses.HearingModifier), spheres);
        // The line to what it pursues (the player) and the label naming it.
        Vector3 player = session.State.PlayerControl.Position!.Value.ToVector();
        Assert.Contains(items, item => item.Shape == OverlayShape.Segment && Vector3.Distance(item.B, player) < .01f);
        Assert.Contains(items, item => item.Shape == OverlayShape.Label && item.Text.StartsWith("rat · ", StringComparison.Ordinal)
            && item.Text.Contains($"→ {session.State.Actors.Player.DurableId}", StringComparison.Ordinal));
    }

    [Fact]
    public void Probe_and_clearance_queries_draw_their_answer_and_inspection_targets_name_the_player_the_door_and_the_person_spoken_to()
    {
        using AssembledWorldRun run = InTheTemple(out DaggerfallSession session);
        run.Appearance.OverlaySets.Clear();

        Assert.True(session.Commands<SpatialInspectionDebugModule>().Probe(2).Succeeded);
        OverlaySetRequest probe = Assert.Single(run.Appearance.OverlaySets, request => request.Name == DaggerfallDebugOverlays.ProbeOverlaySet);
        Assert.Equal(32, probe.Items.ToArray().Count(item => item.Shape == OverlayShape.Segment));

        Vector3 here = session.State.PlayerControl.Position!.Value.ToVector();
        Assert.True(session.Commands<SpatialClearanceDebugModule>().Inspect(here.X + 1, here.Y - 1, here.Z).Succeeded);
        OverlaySetRequest clearance = run.Appearance.OverlaySets.Last();
        Assert.Equal(DaggerfallDebugOverlays.ProbeOverlaySet, clearance.Name);
        Assert.Equal(2, clearance.Items.ToArray().Count(item => item.Shape == OverlayShape.WireSphere));

        Assert.Equal(["player", "door"], session.Commands<DaggerfallDebugOverlays>().InspectionTargets().Select(target => target.Name));
        DaggerfallNpc trainer = session.State.Npcs.All.Single(npc => npc.Presence == DaggerfallNpcPresence.Active
            && npc.Services.Contains("training") && session.State.Actors.TryGet(npc.DurableId, out _));
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(), value => value.Identity.Value == checked((ulong)trainer.DurableId));
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
        Assert.Equal(["player", "dialogue", "door"], session.Commands<DaggerfallDebugOverlays>().InspectionTargets().Select(value => value.Name));
    }

    private static AssembledWorldRun InTheTemple(out DaggerfallSession session)
    {
        AssembledWorldRun run = Shared.Value.Start(Charing, TempleOfKynareth);
        session = run.Session;
        run.Spatial.FloorHit = request => request.Direction.Y < -.99f
            ? default(SpatialHit) with { Present = true, Converged = true, Point = request.Origin - Vector3.UnitY, Normal = Vector3.UnitY }
            : default;
        Assert.True(session.Commands<IDeveloperPlacementCommands>().Enter("Charing", "Temple of Kynareth").Succeeded);
        return run;
    }
}
