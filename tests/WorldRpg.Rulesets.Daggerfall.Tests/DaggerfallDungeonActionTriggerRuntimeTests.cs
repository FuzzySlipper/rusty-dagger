using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonActionTriggerRuntimeTests
{
    [Fact]
    public void Engine_contact_edges_dispatch_once_and_profile_restore_rebases_entry()
    {
        TriggerSpatialDouble spatial = TriggerSpatialDouble.Create();
        ContentDouble content = ContentDouble.Create();
        using SpatialMovementSystem movement = new(
            spatial.Service,
            content.Service,
            new SpatialContentArtifact("spatial/action-triggers", new ContentSha256(1, 2, 3, 4), 1),
            new SpatialTuning(.5d, 8, 8, 1));
        using EntityDirectory entities = new();

        DaggerfallDungeonActionDefinition firstAction = Action("profile-one-action");
        DaggerfallDungeonActionDefinition secondAction = Action("profile-two-action");
        PrivateersHoldInputs firstInputs = Inputs("profile-one", firstAction);
        PrivateersHoldInputs secondInputs = Inputs("profile-two", secondAction);
        DaggerfallWorldProfileKey firstKey = firstInputs.ProfileKey;
        DaggerfallWorldProfileKey secondKey = secondInputs.ProfileKey;

        using DaggerfallDungeonActionTriggerRuntime runtime = new(
            entities,
            spatial.Service,
            movement,
            [(firstKey, firstInputs), (secondKey, secondInputs)],
            firstKey);

        EntityId playerEntity = entities.Create(
            new DurableIdentityReference(DurableIdentityKind.Resource, 0xCAFE),
            new EntityTypeId("test-player"));
        SpatialEntityCollider proxy = Assert.Single(runtime.ActiveRayEntities().ToArray());
        Assert.Equal(DurableIdentityKind.Resource, entities.IdentityOf(new EntityId(proxy.Entity)).Kind);
        Assert.True(runtime.TryResolveAction(new EntityId(proxy.Entity), out string actionId));
        Assert.Equal(firstAction.Id, actionId);

        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        DaggerfallDungeonActionGraph firstGraph = new(firstKey.LogicalId, firstInputs.DungeonActions, Variables());
        DaggerfallDungeonActionGraph secondGraph = new(secondKey.LogicalId, secondInputs.DungeonActions, Variables());

        IReadOnlyList<DaggerfallDungeonActionDispatch> firstEntry = runtime.Reconcile(firstGraph, player, playerEntity, 1);
        DaggerfallDungeonActionDispatch firstDispatch = Assert.Single(firstEntry);
        Assert.Equal(firstAction.Id, firstDispatch.RootActionId);
        Assert.Equal(DaggerfallDungeonActionEvent.WalkOn, firstDispatch.Event);
        Assert.Equal(1UL, firstGraph.State[firstAction.Id].ActivationCount);
        Assert.Empty(runtime.Reconcile(firstGraph, player, playerEntity, 2));
        Assert.Equal(1UL, firstGraph.State[firstAction.Id].ActivationCount);

        runtime.ActivateProfile(secondKey);
        IReadOnlyList<DaggerfallDungeonActionDispatch> secondEntry = runtime.Reconcile(secondGraph, player, playerEntity, 3);
        DaggerfallDungeonActionDispatch secondDispatch = Assert.Single(secondEntry);
        Assert.Equal(secondAction.Id, secondDispatch.RootActionId);
        Assert.Equal(DaggerfallDungeonActionEvent.WalkOn, secondDispatch.Event);
        Assert.Equal(1UL, secondGraph.State[secondAction.Id].ActivationCount);

        runtime.ActivateProfile(firstKey);
        Assert.Single(runtime.Reconcile(firstGraph, player, playerEntity, 4));
        Assert.Equal(2UL, firstGraph.State[firstAction.Id].ActivationCount);
    }

    [Fact]
    public void Restored_player_inside_contact_does_not_replay_until_leave_and_reenter()
    {
        TriggerSpatialDouble spatial = TriggerSpatialDouble.Create();
        ContentDouble content = ContentDouble.Create();
        using SpatialMovementSystem movement = new(
            spatial.Service,
            content.Service,
            new SpatialContentArtifact("spatial/action-triggers", new ContentSha256(1, 2, 3, 4), 1),
            new SpatialTuning(.5d, 8, 8, 1));
        using EntityDirectory entities = new();
        DaggerfallDungeonActionDefinition action = Action("restored-contact-action");
        PrivateersHoldInputs inputs = Inputs("restored-profile", action);
        DaggerfallWorldProfileKey key = inputs.ProfileKey;
        EntityId playerEntity = entities.Create(
            new DurableIdentityReference(DurableIdentityKind.Resource, 0xCAFE),
            new EntityTypeId("test-player"));
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);

        using DaggerfallDungeonActionTriggerRuntime runtime = new(
            entities,
            spatial.Service,
            movement,
            [(key, inputs)],
            key);
        runtime.RebaseRestoredPlayer(player, playerEntity);
        DaggerfallDungeonActionGraph graph = new(key.LogicalId, inputs.DungeonActions, Variables());

        Assert.Empty(runtime.Reconcile(graph, player, playerEntity, 1));
        Assert.Equal(0UL, graph.State[action.Id].ActivationCount);

        player.MoveTo(new Vector3(10f, 0f, 0f));
        Assert.Empty(runtime.Reconcile(graph, player, playerEntity, 2));
        player.MoveTo(Vector3.Zero);
        IReadOnlyList<DaggerfallDungeonActionDispatch> reentry = runtime.Reconcile(graph, player, playerEntity, 3);

        Assert.Single(reentry);
        Assert.Equal(action.Id, reentry[0].RootActionId);
        Assert.Equal(1UL, graph.State[action.Id].ActivationCount);
    }

    [Fact]
    public void Late_profile_admission_deactivates_omitted_rows_until_activation()
    {
        TriggerSpatialDouble spatial = TriggerSpatialDouble.Create();
        ContentDouble content = ContentDouble.Create();
        using SpatialMovementSystem movement = new(
            spatial.Service,
            content.Service,
            new SpatialContentArtifact("spatial/action-triggers", new ContentSha256(1, 2, 3, 4), 1),
            new SpatialTuning(.5d, 8, 8, 1));
        using EntityDirectory entities = new();

        DaggerfallDungeonActionDefinition firstAction = Action("late-first-action");
        DaggerfallDungeonActionDefinition secondAction = Action("late-second-action");
        PrivateersHoldInputs firstInputs = Inputs("late-first-profile", firstAction);
        PrivateersHoldInputs secondInputs = Inputs("late-second-profile", secondAction);
        DaggerfallWorldProfileKey firstKey = firstInputs.ProfileKey;
        DaggerfallWorldProfileKey secondKey = secondInputs.ProfileKey;

        using DaggerfallDungeonActionTriggerRuntime runtime = new(
            entities,
            spatial.Service,
            movement,
            [(firstKey, firstInputs)],
            firstKey);

        EntityId playerEntity = entities.Create(
            new DurableIdentityReference(DurableIdentityKind.Resource, 0xCAFE),
            new EntityTypeId("test-player"));
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        DaggerfallDungeonActionGraph firstGraph = new(firstKey.LogicalId, firstInputs.DungeonActions, Variables());
        DaggerfallDungeonActionGraph secondGraph = new(secondKey.LogicalId, secondInputs.DungeonActions, Variables());

        Assert.Single(runtime.Reconcile(firstGraph, player, playerEntity, 1));

        runtime.AdmitProfile(secondKey, secondInputs);
        Assert.Empty(runtime.Reconcile(firstGraph, player, playerEntity, 2));
        Assert.Equal(0u, spatial.LastDiagnosticCount);

        runtime.ActivateProfile(secondKey);
        IReadOnlyList<DaggerfallDungeonActionDispatch> entry = runtime.Reconcile(secondGraph, player, playerEntity, 3);
        Assert.Single(entry);
        Assert.Equal(secondAction.Id, entry[0].RootActionId);
        Assert.Equal(1UL, secondGraph.State[secondAction.Id].ActivationCount);
    }

    private static DaggerfallDungeonActionDefinition Action(string id) => new(
        id,
        SourceOffset: 1,
        TriggerFlag: (uint)DaggerfallDungeonTriggerFlag.Collision01,
        ActionFlag: (byte)DaggerfallDungeonActionFlag.SetGlobalVar,
        Axis: 1,
        Duration: 0,
        Magnitude: 0,
        NextObjectOffset: -1,
        NextActionId: null,
        IsFlat: true,
        SourcePosition: Vector3.Zero);

    private static PrivateersHoldInputs Inputs(string logicalProfileId, DaggerfallDungeonActionDefinition action) => new(
        new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
        new SpatialContentArtifact("spatial/action-triggers", new ContentSha256(1, 2, 3, 4), 1),
        new ContentArtifact("mesh/action-triggers", new ContentSha256(1, 2, 3, 4)),
        new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene),
        new PlayerInitialLook(0f, 0f),
        [],
        new Dictionary<long, NormalizedActorSprite>(),
        site: new DaggerfallSiteId(1, logicalProfileId == "profile-one" ? 1 : 2),
        logicalProfileId: logicalProfileId,
        dungeonActions: [action]);

    private static DaggerfallVariableStore Variables() => new(new Dictionary<string, int>(StringComparer.Ordinal));

    private class ContentDouble : DispatchProxy
    {
        internal IContentService Service { get; private set; } = null!;

        internal static ContentDouble Create()
        {
            IContentService service = DispatchProxy.Create<IContentService, ContentDouble>();
            ContentDouble proxy = (ContentDouble)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IContentService.ResolveReference)
            ? new ContentReference(new ContentReferenceHandle(1), static () => { })
            : throw new NotSupportedException(method?.Name);
    }

    private class TriggerSpatialDouble : DispatchProxy
    {
        private readonly Dictionary<ulong, SpatialTriggerRegisterRequest> _definitions = [];
        private HashSet<ulong> _active = [];
        private HashSet<(ulong Trigger, ulong Subject)> _overlaps = [];
        private SpatialTriggerFactAtReceipt[] _facts = [];
        private ulong _revision;

        internal ISpatialService Service { get; private set; } = null!;
        internal uint LastDiagnosticCount { get; private set; }

        internal static TriggerSpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, TriggerSpatialDouble>();
            TriggerSpatialDouble proxy = (TriggerSpatialDouble)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ISpatialService.CreateSession) => new SpatialSession(new SpatialSessionHandle(1), static () => { }),
            nameof(ISpatialService.DefaultCharacterControllerConfig) => ControllerConfig(),
            nameof(ISpatialService.ValidateCharacterControllerConfig) => null,
            nameof(ISpatialService.ReplaceContentArtifact) => new SpatialContentArtifactReplaceReceipt(),
            nameof(ISpatialService.RegisterTrigger) => Register((SpatialTriggerRegisterRequest)arguments![0]!),
            nameof(ISpatialService.SetTriggerActive) => SetTriggerActive((SpatialTriggerSetActiveRequest)arguments![0]!),
            nameof(ISpatialService.RestoreTriggers) => Restore((SpatialTriggerRestoreRequest)arguments![0]!),
            nameof(ISpatialService.ReconcileTriggers) => Reconcile((SpatialTriggerReconcileRequest)arguments![0]!),
            nameof(ISpatialService.ReadTriggerFactAt) => ReadFact((SpatialTriggerFactAtRequest)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private object? Register(SpatialTriggerRegisterRequest request)
        {
            if (!_definitions.TryAdd(request.Trigger, request))
                throw new InvalidOperationException($"Trigger {request.Trigger} was registered twice.");
            _active.Add(request.Trigger);
            return null;
        }

        private SpatialTriggerLifecycleReceipt SetTriggerActive(SpatialTriggerSetActiveRequest request)
        {
            RequireRevision(request.ExpectedRevision);
            if (!_definitions.ContainsKey(request.Trigger))
                throw new InvalidOperationException("SetActive referenced an unknown trigger.");
            bool wasActive = _active.Contains(request.Trigger);
            if (wasActive == request.Active)
                throw new InvalidOperationException("SetActive repeated the current trigger state.");

            ulong before = _revision;
            if (request.Active) _active.Add(request.Trigger);
            else _active.Remove(request.Trigger);
            _revision = checked(_revision + 1);
            return new SpatialTriggerLifecycleReceipt(
                request.Trigger,
                request.Active,
                before,
                _revision,
                0,
                0);
        }

        private SpatialTriggerRestoreReceipt Restore(SpatialTriggerRestoreRequest request)
        {
            RequireRevision(request.ExpectedRevision);
            HashSet<ulong> active = request.ActiveTriggers.ToArray().ToHashSet();
            if (active.Any(trigger => !_definitions.ContainsKey(trigger)))
                throw new InvalidOperationException("Restore referenced an unknown trigger.");
            HashSet<(ulong Trigger, ulong Subject)> overlaps = ComputeOverlaps(active, request.Entities.Span, out uint diagnosticCount);
            ulong before = _revision;
            bool changed = !_active.SetEquals(active) || !_overlaps.SetEquals(overlaps);
            _active = active;
            _overlaps = overlaps;
            if (changed) _revision = checked(_revision + 1);
            _facts = [];
            LastDiagnosticCount = diagnosticCount;
            return new SpatialTriggerRestoreReceipt(
                before,
                _revision,
                checked((uint)_definitions.Count),
                checked((uint)_active.Count),
                checked((uint)_overlaps.Count),
                0,
                0);
        }

        private SpatialTriggerReceipt Reconcile(SpatialTriggerReconcileRequest request)
        {
            HashSet<(ulong Trigger, ulong Subject)> next = ComputeOverlaps(_active, request.Entities.Span, out uint diagnosticCount);
            List<SpatialTriggerFactAtReceipt> facts = [];
            foreach ((ulong trigger, ulong subject) in _overlaps.Except(next).OrderBy(pair => pair.Trigger).ThenBy(pair => pair.Subject))
                facts.Add(new(false, false, trigger, subject, request.Tick, request.Cause));
            foreach ((ulong trigger, ulong subject) in next.Except(_overlaps).OrderBy(pair => pair.Trigger).ThenBy(pair => pair.Subject))
                facts.Add(new(true, true, trigger, subject, request.Tick, request.Cause));
            if (facts.Count != 0) _revision = checked(_revision + 1);
            _overlaps = next;
            _facts = facts.ToArray();
            LastDiagnosticCount = diagnosticCount;
            return new SpatialTriggerReceipt(
                request.Tick,
                request.Cause,
                _revision,
                checked((uint)_facts.Length),
                0,
                checked((uint)_overlaps.Count),
                0);
        }

        private SpatialTriggerFactAtReceipt ReadFact(SpatialTriggerFactAtRequest request) =>
            request.Index < (uint)_facts.Length ? _facts[request.Index] : default;

        private HashSet<(ulong Trigger, ulong Subject)> ComputeOverlaps(
            IEnumerable<ulong> active,
            ReadOnlySpan<SpatialEntityCollider> entities,
            out uint diagnosticCount)
        {
            diagnosticCount = 0;
            Dictionary<ulong, SpatialEntityCollider> rows = [];
            foreach (SpatialEntityCollider entity in entities) rows.Add(entity.Entity, entity);
            HashSet<ulong> triggerIds = _definitions.Keys.ToHashSet();
            HashSet<(ulong Trigger, ulong Subject)> result = [];
            foreach (ulong trigger in active)
            {
                if (!rows.TryGetValue(trigger, out SpatialEntityCollider triggerRow))
                {
                    diagnosticCount++;
                    continue;
                }
                if (!triggerRow.Enabled) continue;
                foreach (SpatialEntityCollider subject in entities)
                {
                    if (subject.Entity == trigger || triggerIds.Contains(subject.Entity) || !subject.Enabled || subject.Trigger) continue;
                    if (Overlaps(triggerRow, subject)) result.Add((trigger, subject.Entity));
                }
            }
            return result;
        }

        private static bool Overlaps(SpatialEntityCollider left, SpatialEntityCollider right) =>
            left.Min.X < right.Max.X && left.Max.X > right.Min.X
            && left.Min.Y < right.Max.Y && left.Max.Y > right.Min.Y
            && left.Min.Z < right.Max.Z && left.Max.Z > right.Min.Z;

        private void RequireRevision(ulong expected)
        {
            if (expected != _revision) throw new InvalidOperationException("Trigger revision is stale.");
        }

        private static CharacterControllerConfig ControllerConfig() => default(CharacterControllerConfig) with
        {
            Shape = new CharacterShapeConfig(2f, 1.2f, .4f, .02f, .01f),
        };
    }
}
