using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestSpawnTests
{
    [Theory]
    [InlineData("create foe _enemy_ every 2 minutes 3 times with 45% success", 120, 3, false)]
    [InlineData("create foe _enemy_ every 0 minutes indefinitely with 100% success msg 100", 0, -1, false)]
    [InlineData("send _enemy_ every 2 minutes 3 times with 45% success", 120, 3, true)]
    [InlineData("send _enemy_ every 2 minutes with 45% success", 120, -1, true)]
    public void Source_variants_compile_with_count_interval_and_send_semantics(string source, int interval, int count, bool send)
    {
        var definition = new DaggerfallQuestSourceDefinition("spawn", "", "spawn.txt", DaggerfallQuestDisposition.Compiled, [], [new("headless", 1, [source], null)], []);
        var operation = Assert.Single(Assert.Single(DaggerfallQuestTaskCompiler.Compile(definition).Tasks).Operations);
        Assert.Equal(DaggerfallQuestTaskOperationKind.CreateFoe, operation.Kind);
        Assert.Equal(interval, operation.FoeSpawn!.IntervalSeconds);
        Assert.Equal(count < 0 ? null : (int?)count, operation.FoeSpawn.MaximumGroups);
        Assert.Equal(send, operation.FoeSpawn.Send);
    }

    [Fact]
    public void Pending_and_partial_groups_restore_without_invented_or_reused_actor_identity()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["create foe _enemy_ every 1 minutes 2 times with 100% success msg 100"], messages: ["An enemy arrives."], foeCount: 2);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions);
        var quests = f.Session.State.Quests;
        quests.Advance(f.Session.State.Variables, At(0));
        Assert.Empty(Resource(f.Session).Binding.ActorIds);
        f.Spatial.OverlapHit = _ => default(SpatialHit) with { Present = true };
        quests.Advance(f.Session.State.Variables, At(60));
        Assert.Empty(Resource(f.Session).Binding.ActorIds);
        Assert.Equal(2, Schedule(f.Session).PendingRemaining);
        using var restored = f.Restore(out var engine);
        restored.State.Quests.Advance(restored.State.Variables, At(60));
        long first = Assert.Single(Resource(restored).Binding.ActorIds);
        Assert.True(restored.State.Actors.TryGet(first, out _));
        Assert.Equal(1, Schedule(restored).PendingRemaining);
        using var partial = DaggerfallSession.Restore(engine.Context, f.Composition, restored.CaptureSave());
        partial.State.Quests.Advance(partial.State.Variables, At(60));
        Assert.Equal(2, Resource(partial).Binding.ActorIds.Length);
        Assert.Equal(first, Resource(partial).Binding.ActorIds[0]);
        Assert.Equal(1, Schedule(partial).CompletedGroups);
        partial.State.Quests.Advance(partial.State.Variables, At(119));
        Assert.Equal(2, Resource(partial).Binding.ActorIds.Length);
        partial.State.Quests.Advance(partial.State.Variables, At(120));
        partial.State.Quests.Advance(partial.State.Variables, At(120));
        long[] ids = Resource(partial).Binding.ActorIds;
        Assert.Equal(4, ids.Distinct().Count());
        Assert.Equal(2, Schedule(partial).CompletedGroups);
        partial.State.Quests.Advance(partial.State.Variables, At(240));
        Assert.Equal(ids, Resource(partial).Binding.ActorIds);
        Assert.Single(partial.State.Quests.Messages.Deliveries);
    }

    [Fact]
    public void Failed_chance_consumes_interval_without_spawning_or_counting_a_group()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["create foe _enemy_ every 1 minutes 2 times with 0% success"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions);
        foreach (int second in new[] { 0, 60, 60, 119, 120 }) f.Session.State.Quests.Advance(f.Session.State.Variables, At(second));
        Assert.Equal(2, Schedule(f.Session).Attempts);
        Assert.Equal(0, Schedule(f.Session).CompletedGroups);
        Assert.Empty(Resource(f.Session).Binding.ActorIds);
    }

    [Fact]
    public void Rearming_restarts_group_limit_but_keeps_first_arrival_message_once()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(rearmPlacement: true,
            actions: ["create foe _enemy_ every 0 minutes 1 times with 100% success msg 100", "clear headless.1"], messages: ["Arrived"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions);
        for (int pass = 0; pass < 3; pass++) f.Session.State.Quests.Advance(f.Session.State.Variables, At(0));
        Assert.Equal(3, Resource(f.Session).Binding.ActorIds.Distinct().Count());
        Assert.Single(f.Session.State.Quests.Messages.Deliveries);
    }

    [Fact]
    public void Negative_initial_backdate_is_a_real_timestamp_and_survives_restore()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["create foe _enemy_ every 1 minutes 1 times with 100% success"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions);
        f.Session.State.Quests.Advance(f.Session.State.Variables, At(-100));
        long? first = Schedule(f.Session).LastAttemptSeconds;
        Assert.True(first < 0);
        using var restored = f.Restore();
        restored.State.Quests.Advance(restored.State.Variables, At(-99));
        Assert.Equal(first, Schedule(restored).LastAttemptSeconds);
        Assert.Empty(Resource(restored).Binding.ActorIds);
    }

    [Fact]
    public void Restore_rejects_an_unknown_pending_spawn_profile()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["create foe _enemy_ every 0 minutes 1 times with 100% success"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions);
        f.Spatial.OverlapHit = _ => default(SpatialHit) with { Present = true };
        f.Session.State.Quests.Advance(f.Session.State.Variables, At(0));
        var save = DaggerfallSavePayload.Read(f.Session.CaptureSave());
        var operation = save.Quests.Instances.Single().Tasks.Single().OperationState[0];
        save.Quests.Instances.Single().Tasks.Single().OperationState[0] = operation with
        { FoeSpawn = operation.FoeSpawn! with { PendingProfile = f.Inputs.ProfileKey with { LogicalId = "missing-profile" } } };
        Assert.Contains("unavailable admitted profile", Assert.Throws<ArgumentException>(() =>
            DaggerfallSession.Restore(f.Engine.Context, f.Composition, DaggerfallSavePayload.Encode(save))).Message);
    }

    private static DaggerfallCalendar At(int seconds) => DaggerfallCalendar.FromAbsoluteSeconds(DaggerfallCalendar.Start.ToAbsoluteSeconds() + seconds);
    private static DaggerfallQuestResourceState Resource(DaggerfallSession session) => session.State.Quests.Capture().Instances.Single().Resources.Single(value => value.SelectedFoe is not null);
    private static DaggerfallQuestFoeSpawnState Schedule(DaggerfallSession session) => session.State.Quests.Capture().Instances.Single().Tasks.Single().OperationState.Single().FoeSpawn!;
    private static void Start(SanguineRoseSessionTests.Fixture f, DaggerfallDefinitions definitions)
    {
        var site = definitions.Locations.Records.Single(value => value.Id == f.Inputs.Site);
        f.Session.State.Quests.Start(new("spawn", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(f.Inputs.ProfileKind, site.MapId) })], []));
    }
}
