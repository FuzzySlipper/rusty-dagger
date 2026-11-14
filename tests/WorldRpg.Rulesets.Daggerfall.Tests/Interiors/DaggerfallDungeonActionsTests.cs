using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallDungeonActions"/> built without the session: one dungeon's graph and doors, the trigger and
/// spatial doubles, and its later domains' reaches (relocation, text, the outcome line) recorded as plain inputs.
/// </summary>
public sealed class DaggerfallDungeonActionsTests
{
    private static readonly DaggerfallRdbDoorId DoorId = new("S0000007.RDB", 1, 1, 3);

    [Fact]
    public void A_family_action_reaches_its_door_its_teleport_or_text_through_the_owner()
    {
        using Stage stage = new(
        [
            Action("lock", 1, DaggerfallDungeonActionFlag.LockDoor, DaggerfallDungeonActionGraph.DoorSourceId(DoorId)),
            Action("teleport", 2, DaggerfallDungeonActionFlag.Teleport) with { NextObjectPosition = new Vector3(3f, 0f, -7f) },
            Action("text", 3, DaggerfallDungeonActionFlag.ShowText),
        ]);

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, stage.Graph.Trigger("lock", DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);
        Assert.True(stage.Doors.Read(DoorId).IsLocked);

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, stage.Graph.Trigger("teleport", DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);
        (DaggerfallWorldProfileKey profile, DaggerfallSiteAnchor anchor) = Assert.Single(stage.Relocated);
        Assert.Equal(stage.Profile, profile);
        Assert.Equal(new WorldPoint(3f, 0f, -7f), anchor.Position);
        stage.Relocates = false;
        Assert.Equal(DaggerfallDungeonActionOutcome.RejectedOperation, stage.Graph.Trigger("teleport", DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);

        _ = stage.Graph.Trigger("text", DaggerfallDungeonActionEvent.Direct);
        Assert.Equal(["text"], stage.Texts);
    }

    [Fact]
    public void A_dispatchs_diagnostic_is_said_and_only_applied_work_counts()
    {
        using Stage stage = new([]);
        Assert.False(stage.Owner.Report((DaggerfallDungeonActionDispatch?)null));
        Assert.False(stage.Owner.Report(new DaggerfallDungeonActionDispatch("a", DaggerfallDungeonActionEvent.Direct,
            [new("a", DaggerfallDungeonActionOutcome.MissingTarget, Diagnostic: "No target.")])));
        Assert.Equal(["No target."], stage.Lines);
        Assert.True(stage.Owner.Report([new DaggerfallDungeonActionDispatch("b", DaggerfallDungeonActionEvent.Direct,
            [new("b", DaggerfallDungeonActionOutcome.Applied, Diagnostic: "quiet")])]));
        Assert.Equal(["No target."], stage.Lines);
    }

    [Fact]
    public void A_doors_actions_follow_its_opening_and_a_shown_warning_is_not_run_again()
    {
        string doorSource = DaggerfallDungeonActionGraph.DoorSourceId(DoorId);
        using Stage stage = new(
        [
            Action("warning", 1, DaggerfallDungeonActionFlag.DoorText, doorSource) with { TriggerFlag = (uint)DaggerfallDungeonTriggerFlag.Door },
            Action("lock", 2, DaggerfallDungeonActionFlag.LockDoor, doorSource) with { TriggerFlag = (uint)DaggerfallDungeonTriggerFlag.Door },
        ]);
        stage.Shown = "warning";

        // The first use shows the door's warning ahead of the door and stops there.
        Assert.True(stage.Owner.PreflightDoorText(DoorId, out bool direct));
        Assert.False(direct);
        Assert.Equal(["warning"], stage.Texts);

        // The door opening then runs its other Door actions, not the warning again.
        stage.Owner.TriggerDoor(DoorId);
        Assert.Equal(["warning"], stage.Texts);
        Assert.True(stage.Doors.Read(DoorId).IsLocked);
        stage.Owner.ClearPreflight();
        Assert.Null(stage.Owner.FindDoor(-Vector3.UnitZ, 3d));
        using Stage none = new([], graph: false);
        Assert.Null(none.Owner.TriggerDoorDirect(DoorId));
    }

    private static DaggerfallDungeonActionDefinition Action(string id, int offset, DaggerfallDungeonActionFlag flag, string? door = null) =>
        new(id, offset, (uint)DaggerfallDungeonTriggerFlag.Direct, (byte)flag, 0, 0, 0, 0, null, door);

    /// <summary>One dungeon's graph, doors and owner, the classic player standing nowhere, with every reach recorded.</summary>
    internal sealed class Stage : IDisposable
    {
        private readonly EntityDirectory _entities = new();
        private readonly SpatialMovementSystem _movement;
        private readonly DaggerfallDungeonActionTriggerRuntime _triggers;
        internal ActorsState Actors { get; } = new();

        internal Stage(DaggerfallDungeonActionDefinition[] actions, bool graph = true)
        {
            DaggerfallActorDefinition player = TestPayload.Definitions.RequireActor(new DaggerfallActorId("player"));
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"),
                new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, TestPayload.Definitions.Catalogs.RequireCareer("class00"))),
                "health", DaggerActorFactory.PlayerCapabilities);
            DaggerfallDungeonActionTriggerRuntimeTests.TriggerSpatialDouble spatial = DaggerfallDungeonActionTriggerRuntimeTests.TriggerSpatialDouble.Create();
            _movement = new(spatial.Service, ReferenceResolvingContentFake.Create().Service,
                new SpatialContentArtifact("spatial/action-triggers", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5d, 8, 8, 1));
            DaggerfallSiteProfile inputs = new(
                new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
                DaggerfallSiteGeometry.Closure(new SpatialContentArtifact("spatial/action-triggers", new ContentSha256(1, 2, 3, 4), 1),
                    new ContentArtifact("mesh/action-triggers", new ContentSha256(1, 2, 3, 4))),
                new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene),
                new PlayerInitialLook(0f, 0f), [], new Dictionary<long, NormalizedActorSprite>(),
                site: new DaggerfallSiteId(1, 2), logicalProfileId: "dungeon", dungeonMap: new DaggerfallDungeonMapContent([], [], []));
            Profile = inputs.ProfileKey;
            _triggers = new(_entities, spatial.Service, _movement, [(Profile, inputs)], Profile);
            Doors = new(_entities, RandomMinimum.Create(), [new DaggerfallRdbDoorDefinition(DoorId, new Vector3(2, 0, -4), Vector3.Zero,
                new Vector3(-.5F, 0, -.1F), new Vector3(.5F, 2, .1F), DaggerfallDoorKind.Normal)], "dungeon");
            Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonActionGraph> graphs = [];
            Owner = new(graphs, () => Profile, () => inputs, () => Doors,
                () => throw new InvalidOperationException("No mover is read here."), _triggers, _movement,
                () => throw new InvalidOperationException("No ray is cast here."), point => point,
                (profile, anchor) => { Relocated.Add((profile, anchor)); return Relocates; },
                Player, Actors, 1.6f, .5d, (_, _) => { }, new CombatResolution(), RandomMinimum.Create(),
                (_, _, _) => throw new InvalidOperationException("No spell is cast here."), new FactBuffer<IProductFact>(),
                _ => throw new InvalidOperationException("No hazard harms here."), () => 1UL, () => 1UL,
                action => { Texts.Add(action.Id); return new(action.Id, DaggerfallDungeonActionOutcome.Applied); },
                actionId => actionId == Shown, Lines.Add);
            Graph = new("dungeon", actions, new DaggerfallVariableStore(new Dictionary<string, int>()).Write,
                executeFamilyAction: Owner.Execute);
            if (graph) graphs.Add(Profile, Graph);
        }

        internal DaggerfallWorldProfileKey Profile { get; }
        internal PlayerControlState Player { get; } = new(null, 0f, 0f);
        internal SpatialMovementSystem Movement => _movement;
        internal EntityDirectory Entities => _entities;
        internal DaggerfallDoorRuntime Doors { get; }
        internal DaggerfallDungeonActions Owner { get; }
        internal DaggerfallDungeonActionGraph Graph { get; }
        internal List<(DaggerfallWorldProfileKey, DaggerfallSiteAnchor)> Relocated { get; } = [];
        internal bool Relocates { get; set; } = true;
        internal List<string> Texts { get; } = [];
        internal List<string> Lines { get; } = [];
        internal string? Shown { get; set; }

        public void Dispose()
        {
            Doors.Dispose();
            _triggers.Dispose();
            _movement.Dispose();
            Actors.Dispose();
            _entities.Dispose();
        }
    }
}
