using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallSwimmingCadenceSessionTests
{
    [Fact]
    public void Catch_up_swimming_visits_each_calendar_minute_and_restores_its_cadence_gate()
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        var tuning = DaggerfallTuning.Defaults with {Time = new(60)};
        var composition = new DaggerfallSessionComposition(TestPayload.Definitions, inputs, tuning);
        RulesetSavePayload save;
        using (var session = DaggerfallSession.StartNew(Engine(), composition))
        {
            session.ApplyProductMode(ProductMode.Playing);
            var update = Update(1, 3);
            Assert.Equal(3u, update.Facts.AdmittedStepCount);
            session.Update(update);
            Assert.Equal(4, session.State.Progression.SkillUses["swimming"]);
            save = session.CaptureSave();
        }
        using var restored = DaggerfallSession.Restore(Engine(), composition, save);
        restored.ApplyProductMode(ProductMode.Playing);
        restored.Update(Update(4, 1));
        Assert.Equal(5, restored.State.Progression.SkillUses["swimming"]);

        IEngineContext Engine()
        {
            List<string> releases = [];
            var content = new ContentFake(releases);
            PopulateContent(content, inputs);
            var spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
            spatial.KeepPosition = true;
            // Fix the admitted Engine movement fact to isolate the session's persisted skill cadence.
            spatial.MovementFact = _ => new(CharacterMovementMode.Swimming, .5f, false, false, false, false);
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases)).Context;
        }
    }

    [Fact]
    public void Slice_minute_reads_use_the_same_fractional_boundary_as_the_one_clock()
    {
        var before = DaggerfallCalendar.Start with {Second = 59};
        var time = new DaggerfallWorldTime(before, .9, 1);
        long start = before.ToAbsoluteSeconds() / 60;
        Assert.Equal(start, DaggerfallWorldTime.MinuteAtAdmittedOffset(before, time.RemainderSeconds, .05));
        Assert.Equal(start + 1, DaggerfallWorldTime.MinuteAtAdmittedOffset(before, time.RemainderSeconds, .1));
        Assert.Equal(before, time.Calendar);
        time.Advance(.1);
        Assert.Equal(start + 1, time.Calendar.ToAbsoluteSeconds() / 60);
    }

    private static ProductUpdate Update(ulong first, uint count) =>
        new(OuterUpdate(first) with {AdmittedStepCount = count, FixedDeltaSeconds = 1}, []);
}
