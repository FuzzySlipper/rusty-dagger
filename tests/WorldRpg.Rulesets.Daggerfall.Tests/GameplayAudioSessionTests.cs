using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class GameplayAudioSessionTests
{
    [Fact]
    public void Door_operations_and_equipping_emit_their_source_cues_only_when_applied()
    {
        using var f = Fixture(); var s = f.Session;
        var door = f.Inputs.Doors.First(value => value.Kind == DaggerfallDoorKind.Normal);
        s.Doors.Unlock(door.Id, DaggerfallDoorOperationSource.DungeonAction);
        Assert.Equal(DaggerfallDoorOperationResult.Started, s.Doors.Open(door.Id, DaggerfallDoorOperationSource.Player));
        Cue(f, 25, "door-open");
        s.Doors.Open(door.Id, DaggerfallDoorOperationSource.Player);
        Assert.Single(f.Engine.EmittedAudio, request => request.SignalId.Contains("door-open"));
        s.Doors.Close(door.Id, DaggerfallDoorOperationSource.Player); Cue(f, 24, "door-close");
        s.Doors.Advance(10); s.Doors.Bash(door.Id); Cue(f, 7, "door-bash");
        var dagger = s.State.Inventory.Read().UniqueItems.Single(value => value.Definition.Value == "iron-dagger");
        Assert.Equal(EquipmentMoveOutcome.Applied, s.EquipmentMoves.MoveToSlot(new(dagger.Entity.Value, new(dagger.Definition.Value)), new("right-hand")).Outcome);
        Cue(f, 377, "equip");
    }

    [Fact]
    public void Accepted_movement_drives_alternating_footsteps_and_pausing_is_silent()
    {
        using var f = Fixture(); var s = f.Session;
        f.Spatial.KeepPosition = true; f.Update();
        Assert.DoesNotContain(f.Engine.EmittedAudio, request => request.SignalId.Contains("footstep"));
        f.Spatial.KeepPosition = false;
        for (int step = 0; step < 6; step++) f.Update();
        Cue(f, 309, "footstep"); Cue(f, 330, "footstep");
        int count = f.Engine.EmittedAudio.Count;
        s.ApplyProductMode(ProductMode.Paused); f.Update();
        Assert.Equal(count, f.Engine.EmittedAudio.Count);
    }

    [Fact]
    public void Damaging_landing_plays_fall_and_player_pain_cues()
    {
        using var f = Fixture(); var s = f.Session;
        var health = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        health.Maximum.BaseValue = 1000; health.SetCurrent(1000);
        s.State.PlayerControl.Motion = s.State.PlayerControl.Motion with { Grounded = false, PeakY = 12, ControlledVelocity = -Vector3.UnitY };
        f.Spatial.KeepPosition = true; f.Update();
        Assert.True(health.Current < 1000);
        Cue(f, 91, "fall-damage"); Cue(f, 391, "player-pain");
    }

    [Fact]
    public void Swimming_entry_and_strokes_use_distinct_classic_splash_clips()
    {
        using var f = Fixture(); var audio = f.Session.Sites.Projection.Appearance;
        audio.AdvanceFootsteps(new(0, 0, 0), Vector3.Zero, false, false, false, true, false, true, false);
        Cue(f, 342, "water-entry");
        audio.AdvanceFootsteps(new(0, 0, 0), new(3, 0, 0), false, false, true, true, false, true, false);
        Cue(f, 346, "swim-stroke");
        audio.AdvanceFootsteps(new(3, 0, 0), new(6, 0, 0), true, true, false, false, true, true, false);
        Cue(f, 334, "footstep");
    }

    [Fact]
    public void Newly_eligible_level_plays_once_and_reading_the_pending_allocation_does_not_replay()
    {
        using var f = Fixture(); var s = f.Session;
        foreach (var skill in TestPayload.Definitions.Vocabulary.Skills)
            s.State.Actors.Player.Stats.GetStat(StatId.Parse(skill.Value)).BaseValue = 100;
        Assert.True(s.State.LevelUps.BeginIfEligible()); Cue(f, 96, "level-up");
        Assert.False(s.State.LevelUps.BeginIfEligible());
        Assert.Single(f.Engine.EmittedAudio, request => request.SignalId.Contains("level-up"));
    }

    private static SanguineRoseSessionTests.Fixture Fixture() => new(prepareComposition: value => value with
    {
        Tuning = value.Tuning with { PresentationAudio = value.Tuning.PresentationAudio with { PlayerPainChancePercent = 100 } },
    });

    private static void Cue(SanguineRoseSessionTests.Fixture fixture, int ordinal, string trigger)
    {
        string clip = Assert.Single(fixture.Inputs.Audio, value => value.SourceRecordOrdinal == ordinal).Id;
        Assert.Contains(fixture.Engine.EmittedAudio, request => request.SignalId.Contains(trigger, StringComparison.Ordinal)
            && request.SignalId.EndsWith("." + clip, StringComparison.Ordinal));
    }
}
