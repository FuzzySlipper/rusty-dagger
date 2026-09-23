using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonActionGraphTests
{
    [Theory]
    [InlineData(0u, 0, true)]
    [InlineData(0u, 1, false)]
    [InlineData(0u, 2, false)]
    [InlineData(1u, 2, true)]
    [InlineData(1u, 3, false)]
    [InlineData(2u, 1, true)]
    [InlineData(3u, 3, true)]
    [InlineData(5u, 4, true)]
    [InlineData(6u, 1, true)]
    [InlineData(8u, 1, true)]
    [InlineData(8u, 4, true)]
    [InlineData(8u, 3, true)]
    [InlineData(9u, 1, true)]
    [InlineData(9u, 3, true)]
    [InlineData(10u, 5, true)]
    public void Donor_trigger_flags_admit_their_named_events(uint triggerFlag, byte eventValue, bool admitted)
    {
        DaggerfallDungeonActionEvent @event = (DaggerfallDungeonActionEvent)eventValue;
        DaggerfallDungeonActionGraph graph = Create(
            new DaggerfallDungeonActionDefinition("action", 1, triggerFlag, 0x1F, 0, 0, 0, -1, null));

        DaggerfallDungeonActionDispatch result = graph.Trigger("action", @event);

        Assert.Equal(admitted, result.Executions.Single().Outcome is DaggerfallDungeonActionOutcome.Applied or DaggerfallDungeonActionOutcome.AppliedWithoutChange);
    }

    [Fact]
    public void Linked_actions_run_in_deterministic_donor_order_and_set_global()
    {
        DaggerfallDungeonActionDefinition root = new("root", 10, 2, 0x1E, 0, 0, 0, 20, "child");
        DaggerfallDungeonActionDefinition child = new("child", 20, 0, 0x1F, 7, 13, 19, -1, null);
        DaggerfallVariableStore variables = Variables();
        DaggerfallDungeonActionGraph graph = new("profile", [root, child], variables);

        DaggerfallDungeonActionDispatch result = graph.Trigger("root", DaggerfallDungeonActionEvent.Direct);

        Assert.Equal(["child", "root"], result.Executions.Select(execution => execution.ActionId));
        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, result.Executions[0].Outcome);
        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, result.Executions[1].Outcome);
        Assert.True(variables.Read(new DaggerfallVariableAddress(DaggerfallVariableScope.Global, 0, 7)));
        Assert.Equal((1UL, 0d), graph.State["child"]);
        Assert.Equal((1UL, 0d), graph.State["root"]);
    }

    [Fact]
    public void Activate_without_a_resolved_next_object_reports_the_missing_target()
    {
        DaggerfallDungeonActionDefinition definition = new("activate", 1, 2, 0x1E, 0, 0, 0, -2, null);
        DaggerfallDungeonActionGraph graph = Create(definition);

        DaggerfallDungeonActionExecution result = graph.Trigger("activate", DaggerfallDungeonActionEvent.Direct).Executions.Single();

        Assert.Equal(DaggerfallDungeonActionOutcome.MissingTarget, result.Outcome);
        Assert.Contains("no resolved next action target", result.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Activate_with_zero_source_offset_is_a_donor_null_link()
    {
        DaggerfallDungeonActionDefinition definition = new("activate", 1, 2, 0x1E, 0, 0, 0, 0, null);
        DaggerfallDungeonActionGraph graph = Create(definition);

        DaggerfallDungeonActionExecution result = graph.Trigger("activate", DaggerfallDungeonActionEvent.Direct).Executions.Single();

        Assert.Equal(DaggerfallDungeonActionOutcome.AppliedWithoutChange, result.Outcome);
        Assert.Contains("source null-link offset 0", result.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Attack_trigger_can_activate_a_door_chain_and_linked_node_bypasses_its_none_trigger()
    {
        DaggerfallDungeonActionDefinition door = new("door", 10, 5, 0x1E, 0, 0, 0, 20, "set-global", DoorId: "door/one");
        DaggerfallDungeonActionDefinition setGlobal = new("set-global", 20, 0, 0x1F, 9, 0, 0, -1, null, DoorId: "door/one");
        DaggerfallVariableStore variables = Variables();
        DaggerfallDungeonActionGraph graph = new("profile", [door, setGlobal], variables);

        DaggerfallDungeonActionDispatch result = graph.Trigger("door", DaggerfallDungeonActionEvent.Attack);

        Assert.Equal(["set-global", "door"], result.Executions.Select(execution => execution.ActionId));
        Assert.True(variables.Read(new DaggerfallVariableAddress(DaggerfallVariableScope.Global, 0, 9)));
    }

    [Fact]
    public void Raw_lock_selector_does_not_change_the_low_trigger_nibble()
    {
        DaggerfallDungeonActionDefinition definition = new("door", 1, 0xD2, 0x1F, 3, 0, 0, -1, null, DoorId: "door/one");
        DaggerfallDungeonActionGraph graph = Create(definition);

        DaggerfallDungeonActionDispatch result = graph.Trigger("door", DaggerfallDungeonActionEvent.Direct);

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, result.Executions.Single().Outcome);
    }

    [Fact]
    public void High_trigger_value_without_action_door_identity_remains_unknown()
    {
        DaggerfallDungeonActionDefinition definition = new("action", 1, 0xD2, 0x1F, 3, 0, 0, -1, null);
        DaggerfallDungeonActionGraph graph = Create(definition);

        DaggerfallDungeonActionExecution result = graph.Trigger("action", DaggerfallDungeonActionEvent.Direct).Executions.Single();

        Assert.Equal(DaggerfallDungeonActionOutcome.RejectedTrigger, result.Outcome);
        Assert.Contains("unknown source trigger flag", result.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Placement_and_door_source_id_helpers_dispatch_admitted_actions()
    {
        DaggerfallDungeonActionDefinition model = new(
            "action/b0000001-rdb/0/-1/model-3", 1, 2, 0x1F, 4, 0, 0, -1, null);
        DaggerfallRdbDoorId door = new("B0000001.RDB", 0, -1, 3);
        DaggerfallDungeonActionDefinition doorAction = model with
        {
            Id = "action-door",
            TriggerFlag = 0xA,
            Axis = 5,
            DoorId = DaggerfallDungeonActionGraph.DoorSourceId(door),
        };
        DaggerfallDungeonActionGraph graph = Create(model, doorAction);

        DaggerfallDungeonActionDispatch? placement = graph.TriggerForPlacement("model/b0000001-rdb/0/-1/3", DaggerfallDungeonActionEvent.Direct);
        IReadOnlyList<DaggerfallDungeonActionDispatch> doorDispatches = graph.TriggerForDoor(door, DaggerfallDungeonActionEvent.Door);

        Assert.NotNull(placement);
        Assert.True(placement!.Executions.Single().Outcome is DaggerfallDungeonActionOutcome.Applied or DaggerfallDungeonActionOutcome.AppliedWithoutChange);
        Assert.Single(doorDispatches);
        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, doorDispatches[0].Executions.Single().Outcome);
        Assert.Null(graph.TriggerForPlacement("flat/b0000001-rdb/0/-1/3", DaggerfallDungeonActionEvent.Direct));
    }

    [Fact]
    public void Flat_source_action_dispatches_direct_and_attack_events_by_admitted_identity()
    {
        DaggerfallDungeonActionDefinition flat = new(
            "action/block/flat-4", 4, 8, 0x1F, 0, 0, 0, -1, null,
            IsFlat: true);
        DaggerfallDungeonActionGraph graph = Create(flat);

        DaggerfallDungeonActionDispatch direct = graph.Trigger(flat.Id, DaggerfallDungeonActionEvent.Direct);
        DaggerfallDungeonActionDispatch attack = graph.Trigger(flat.Id, DaggerfallDungeonActionEvent.Attack);

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, direct.Executions.Single().Outcome);
        Assert.True(attack.Executions.Single().Outcome is DaggerfallDungeonActionOutcome.Applied
            or DaggerfallDungeonActionOutcome.AppliedWithoutChange);
    }

    [Fact]
    public void Graph_dispatch_does_not_duplicate_Engine_contact_edge_state()
    {
        const string placement = "model/block/0/0/4";
        DaggerfallDungeonActionDefinition definition = new(
            "action/block/0/0/model-4", 4, 3, 0x1F, 0, 0, 0, -1, null);
        DaggerfallDungeonActionGraph graph = Create(definition);

        DaggerfallDungeonActionDispatch? first = graph.TriggerForPlacement(placement, DaggerfallDungeonActionEvent.WalkInto);
        DaggerfallDungeonActionDispatch? sustained = graph.TriggerForPlacement(placement, DaggerfallDungeonActionEvent.WalkInto);

        Assert.NotNull(first);
        Assert.NotNull(sustained);
        Assert.Equal(2UL, graph.State[definition.Id].ActivationCount);
    }

    [Fact]
    public void Linked_cycles_are_reported_once_and_do_not_reenter_forever()
    {
        DaggerfallDungeonActionDefinition first = new("first", 1, 2, 0x1E, 0, 0, 0, 2, "second");
        DaggerfallDungeonActionDefinition second = new("second", 2, 0, 0x1E, 0, 0, 0, 1, "first");
        DaggerfallDungeonActionGraph graph = Create(first, second);

        DaggerfallDungeonActionDispatch result = graph.Trigger("first", DaggerfallDungeonActionEvent.Direct);

        Assert.Equal(["first", "second", "first"], result.Executions.Select(execution => execution.ActionId));
        Assert.Contains(result.Executions, execution => execution.Outcome == DaggerfallDungeonActionOutcome.CycleSuppressed);
        Assert.Equal(1UL, graph.State["first"].ActivationCount);
        Assert.Equal(1UL, graph.State["second"].ActivationCount);
    }

    [Fact]
    public void Retrigger_cooldown_advances_inside_the_admitted_timeline_and_round_trips()
    {
        DaggerfallDungeonActionDefinition definition = new("action", 1, 2, 0x1F, 0, 0, 0, -1, null, CooldownSeconds: 2.5d);
        DaggerfallDungeonActionGraph graph = Create(definition);

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied,
            graph.Trigger("action", DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);
        Assert.Equal(DaggerfallDungeonActionOutcome.Cooldown,
            graph.Trigger("action", DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);
        graph.Advance(2.5d);
        Assert.Equal(DaggerfallDungeonActionOutcome.AppliedWithoutChange,
            graph.Trigger("action", DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);

        DaggerfallDungeonActionGraph restored = Create(definition, graph.Capture());
        Assert.Equal(2UL, restored.State["action"].ActivationCount);
        Assert.Equal(2.5d, restored.State["action"].RemainingCooldownSeconds);
    }

    [Fact]
    public void Unknown_actions_and_invalid_global_parameters_are_diagnostics_not_success()
    {
        DaggerfallDungeonActionDefinition unknown = new("unknown", 1, 2, 0x7E, 0, 0, 0, -1, null);
        DaggerfallDungeonActionDefinition invalidGlobal = new("invalid-global", 2, 2, 0x1F, 64, 0, 0, -1, null);
        DaggerfallDungeonActionGraph graph = new("profile", [unknown, invalidGlobal], Variables());

        DaggerfallDungeonActionExecution unsupported = graph.Trigger("unknown", DaggerfallDungeonActionEvent.Direct).Executions.Single();
        DaggerfallDungeonActionExecution invalid = graph.Trigger("invalid-global", DaggerfallDungeonActionEvent.Direct).Executions.Single();

        Assert.Equal(DaggerfallDungeonActionOutcome.UnsupportedAction, unsupported.Outcome);
        Assert.Contains("unknown source action flag", unsupported.Diagnostic, StringComparison.Ordinal);
        Assert.Equal(DaggerfallDungeonActionOutcome.InvalidVariable, invalid.Outcome);
        Assert.Contains("global variable 64", invalid.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Restored_snapshot_must_match_the_loaded_profile_and_nodes()
    {
        DaggerfallDungeonActionDefinition definition = new("action", 1, 2, 0x1E, 0, 0, 0, -1, null);
        DaggerfallDungeonActionGraph graph = Create(definition);
        DaggerfallDungeonActionGraphSnapshot snapshot = graph.Capture();

        Assert.Throws<ArgumentException>(() => new DaggerfallDungeonActionGraph(
            "other-profile", [definition], Variables(), snapshot));
        Assert.Throws<ArgumentException>(() => new DaggerfallDungeonActionGraph(
            "profile", [definition], Variables(), new DaggerfallDungeonActionGraphSnapshot(
                "profile", [new DaggerfallDungeonActionNodeSave("missing", 0, 0)])));
    }

    private static DaggerfallDungeonActionGraph Create(params DaggerfallDungeonActionDefinition[] definitions) =>
        new("profile", definitions, Variables());

    private static DaggerfallDungeonActionGraph Create(DaggerfallDungeonActionDefinition definition, DaggerfallDungeonActionGraphSnapshot snapshot) =>
        new("profile", [definition], Variables(), snapshot);

    private static DaggerfallVariableStore Variables() => new(new Dictionary<string, int>(StringComparer.Ordinal));
}
