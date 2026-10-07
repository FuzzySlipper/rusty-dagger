using System.Text.Json.Nodes;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A quest action can admit an interval of its own (training, a cure) while an ordinary or elapsed
/// interval is still being delivered. Each minute of the combined time reaches its consumers once.
/// </summary>
public sealed class CalendarIntervalReconciliationTests
{
    [Fact]
    public void Quest_training_inside_ordinary_play_charges_its_source_fatigue_once()
    {
        using var f = new SanguineRoseSessionTests.Fixture(definitions: QuestDiseaseTests.Definitions("train pc LongBlade"));
        DaggerfallSession s = f.Session;
        Track stamina = Stamina(s);
        double full = stamina.Maximum.Value;
        Assert.True(full > 11 * 200, $"Stamina maximum {full} cannot show a doubled training charge.");
        stamina.SetCurrent(full);
        long minuteBefore = Minute(s);
        long roundsBefore = s.State.Effects.MagicRounds;
        QuestDiseaseTests.Start(s);

        f.Update();

        long minutes = Minute(s) - minuteBefore;
        Assert.True(minutes >= 180, $"Training admitted {minutes} minutes.");
        // The source training cost is 180 idle minutes; every other minute of the update is idle.
        Assert.Equal(full - (11d * minutes), stamina.Current);
        Assert.Equal(minutes, s.State.Effects.MagicRounds - roundsBefore);
    }

    [Fact]
    public void Quest_training_inside_an_elapsed_interval_advances_magic_rounds_once_per_minute()
    {
        using var f = new SanguineRoseSessionTests.Fixture(definitions: QuestDiseaseTests.Definitions("train pc LongBlade"));
        DaggerfallSession s = f.Session;
        Track stamina = Stamina(s);
        double full = stamina.Maximum.Value;
        Assert.True(full > 11 * 200, $"Stamina maximum {full} cannot show a doubled training charge.");
        stamina.SetCurrent(full);
        long minuteBefore = Minute(s);
        long roundsBefore = s.State.Effects.MagicRounds;
        QuestDiseaseTests.Start(s);

        s.AdvanceElapsedTime(60);

        Assert.Equal(minuteBefore + 1 + 180, Minute(s));
        Assert.Equal(181, s.State.Effects.MagicRounds - roundsBefore);
        // One elapsed idle minute plus the training's own 180-minute source cost.
        Assert.Equal(full - (11d * 181), stamina.Current);
    }

    [Fact]
    public void A_clock_deadline_reached_by_quest_training_runs_its_task_once_after_the_training_pass()
    {
        using var f = new SanguineRoseSessionTests.Fixture(definitions: ClockDefinitions.Value);
        DaggerfallSession s = f.Session;
        s.State.Quests.Start(new("clocked", "clocked-training.txt", "clocked-training", DaggerfallQuestLifecycle.Active, null, [], []));

        f.Update();
        f.Update();

        DaggerfallQuestInstanceSave quest = Assert.Single(s.State.Quests.All);
        Assert.True(Assert.Single(quest.Clocks).Finished);
        Assert.True(quest.Tasks.Single(task => task.Symbol == "hit").IsSet);
    }

    private static Track Stamina(DaggerfallSession session) =>
        session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));

    private static long Minute(DaggerfallSession session)
    {
        DaggerfallCalendarSave date = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
        return new DaggerfallCalendar(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second)
            .ToAbsoluteSeconds() / DaggerfallCalendar.SecondsPerMinute;
    }

    private static readonly SharedFixture<DaggerfallDefinitions> ClockDefinitions = new(() =>
    {
        JsonObject root = TestPayload.Sections("questSources");
        root["questSources"]!["quests"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"clocked-training","displayName":"Clocked training","sourceFile":"clocked-training.txt","disposition":"compiled",
            "messages":[],
            "blocks":[{"kind":"clock","firstLine":1,"lines":["clock _deadline_ 30"],"global":null},
            {"kind":"variable","firstLine":2,"lines":["variable _hit_"],"global":null},
            {"kind":"task","firstLine":3,"lines":["_deadline_ task:","start task _hit_"],"global":null},
            {"kind":"headless","firstLine":5,"lines":["start timer _deadline_","train pc LongBlade"],"global":null}],"diagnostics":[]}
            """));
        return DaggerfallBaseContent.Read(TestPayload.Splice(root.AsObject()));
    });
}
