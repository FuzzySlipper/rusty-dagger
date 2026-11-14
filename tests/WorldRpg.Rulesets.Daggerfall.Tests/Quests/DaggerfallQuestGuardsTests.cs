using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The city guards a quest calls, built without the session over <see cref="SocietyStage"/>'s actors, people, crime and
/// law (whose perception sees no one); where the player is, the active profile and the civilians' retirement are plain
/// inputs.
/// </summary>
public sealed class DaggerfallQuestGuardsTests
{
    private static DaggerfallQuestGuards Guards(SocietyStage stage, bool town = true, List<long>? retired = null)
    {
        DaggerfallSiteProfile profile = TestSessions.MediaInputs();
        return new(stage.Actors, stage.Npcs, stage.Player, stage.Crime, stage.ActorDefinitions, new DaggerfallWorldTime(DaggerfallCalendar.Start, 0d, 12d),
            stage.Crimes, stage.Law, DaggerfallTuning.Defaults.QuestSpawning, DaggerfallTuning.Defaults.Law,
            () => new DaggerfallQuestLocationRead(profile, town ? stage.Site : null, null), () => stage.Active, () => profile,
            id => (retired ?? []).Add(id));
    }

    [Fact]
    public void A_report_nobody_saw_brings_no_guards()
    {
        using SocietyStage stage = new();
        stage.Player.MoveTo(new Vector3(0, 0, 0));
        stage.Civilian();

        DaggerfallQuestGuardSpawnState? state = Guards(stage).Spawn("guards/unseen", immediate: false, 0, null);

        Assert.Equal(DaggerfallQuestGuardSpawnOutcome.NoWitness, state?.Outcome);
        Assert.Equal(stage.Site.Id, state!.Location);
    }

    [Fact]
    public void A_full_watch_brings_no_more_and_a_call_left_behind_ends()
    {
        using SocietyStage stage = new();
        stage.Player.MoveTo(new Vector3(0, 0, 0));
        for (int watch = 0; watch <= DaggerfallTuning.Defaults.QuestSpawning.MaximumActiveGuards; watch++) stage.CityWatch();

        Assert.Equal(DaggerfallQuestGuardSpawnOutcome.ActiveLimit, Guards(stage).Spawn("guards/full", immediate: true, 0, null)?.Outcome);

        DaggerfallQuestGuardSpawnState pending = new(DaggerfallWorldProfileIds.Dungeon(stage.Site.Id), stage.Site.Id, 3, 2, 0, [], DaggerfallQuestGuardSpawnOutcome.Pending);
        DaggerfallQuestGuardSpawnState? left = Guards(stage).Spawn("guards/left", immediate: false, 1, pending);
        Assert.Equal(DaggerfallQuestGuardSpawnOutcome.LeftLocation, left?.Outcome);
        Assert.Equal(0, left!.Remaining);
    }

    [Fact]
    public void Neither_a_dungeon_nor_the_wilderness_has_a_watch_to_call()
    {
        using SocietyStage stage = new();
        stage.Player.MoveTo(new Vector3(0, 0, 0));
        Assert.Contains("wilderness", Assert.Throws<NotSupportedException>(() =>
            Guards(stage, town: false).Spawn("guards/wilderness", immediate: true, 0, null)).Message);

        stage.Active = DaggerfallWorldProfileIds.Dungeon(stage.Site.Id);
        Assert.Contains("dungeon", Assert.Throws<NotSupportedException>(() =>
            Guards(stage).Spawn("guards/dungeon", immediate: true, 0, null)).Message);
    }
}
