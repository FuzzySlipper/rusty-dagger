using System.Text.Json.Serialization;
using WorldRpg.Kit;
using Rusty.Engine;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The durable lifecycle of one instantiated quest source.</summary>
internal enum DaggerfallQuestLifecycle { Active, Completed, Failed, Ended, Tombstoned }
/// <summary>The stable product identity carried by a declared quest resource.</summary>
internal enum DaggerfallQuestResourceBindingKind { Actor, Item, Place }

/// <summary>A typed binding for one resource, without Engine handles.</summary>
internal sealed record DaggerfallQuestStackBinding(DaggerfallItemOwnerSave Owner, string StackId)
{
    internal void Validate(string owner)
    {
        ArgumentNullException.ThrowIfNull(Owner);
        _ = new DaggerfallItemOwner(Owner.Scope, Owner.Id).Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(StackId);
    }
}

internal sealed record DaggerfallQuestResourceBinding(DaggerfallQuestResourceBindingKind Kind, long[] ActorIds, ulong[] UniqueItemIds,
    DaggerfallQuestStackBinding[] Stacks, DaggerfallSiteIdSave[] Places)
{
    internal static DaggerfallQuestResourceBinding Actors(params long[] actorIds) => new(DaggerfallQuestResourceBindingKind.Actor, actorIds, [], [], []);
    internal static DaggerfallQuestResourceBinding UniqueItem(ulong itemId) => new(DaggerfallQuestResourceBindingKind.Item, [], [itemId], [], []);
    internal static DaggerfallQuestResourceBinding Stack(DaggerfallItemOwnerSave owner, string stackId) => new(DaggerfallQuestResourceBindingKind.Item, [], [], [new(owner, stackId)], []);
    internal static DaggerfallQuestResourceBinding Place(DaggerfallSiteIdSave place) => new(DaggerfallQuestResourceBindingKind.Place, [], [], [], [place]);

    internal void Validate(string owner)
    {
        ArgumentNullException.ThrowIfNull(ActorIds);
        ArgumentNullException.ThrowIfNull(UniqueItemIds);
        ArgumentNullException.ThrowIfNull(Stacks);
        ArgumentNullException.ThrowIfNull(Places);
        switch (Kind)
        {
            case DaggerfallQuestResourceBindingKind.Actor when ActorIds.Length > 0 && UniqueItemIds.Length == 0 && Stacks.Length == 0 && Places.Length == 0 && ActorIds.All(id => id > 0):
            case DaggerfallQuestResourceBindingKind.Item when ActorIds.Length == 0 && Places.Length == 0 &&
                ((UniqueItemIds.Length == 1 && UniqueItemIds[0] > 0 && Stacks.Length == 0) || (UniqueItemIds.Length == 0 && Stacks.Length == 1)):
            case DaggerfallQuestResourceBindingKind.Place when ActorIds.Length == 0 && UniqueItemIds.Length == 0 && Stacks.Length == 0 && Places.Length == 1:
                break;
            default:
                throw new ArgumentException($"Quest resource '{owner}' has an incompatible {Kind} binding.");
        }
        foreach (DaggerfallQuestStackBinding stack in Stacks)
        {
            ArgumentNullException.ThrowIfNull(stack);
            stack.Validate(owner);
        }
        foreach (DaggerfallSiteIdSave place in Places)
        {
            ArgumentNullException.ThrowIfNull(place);
            place.Validate($"quest resource '{owner}' place");
        }
    }
}

/// <summary>Durable state belonging to one declared resource.</summary>
internal sealed record DaggerfallQuestResourceState(string Symbol, DaggerfallQuestResourceBinding Binding, bool IsHidden = false, bool HasPlayerClicked = false);

/// <summary>Symbols can name resources, tasks, or textual values, so they are not resource-restricted.</summary>
internal sealed record DaggerfallQuestSymbolState(string Symbol, string Value);

internal sealed record DaggerfallQuestInstanceSave(string InstanceId, string SourceFile, string DefinitionName,
    DaggerfallQuestLifecycle Lifecycle, string? Outcome, DaggerfallQuestResourceState[] Resources, DaggerfallQuestSymbolState[] Symbols)
{
    /// <summary>Source-order task trigger and operation state reconstructed from the normalized definition.</summary>
    public DaggerfallQuestTaskState[] Tasks { get; init; } = [];
    /// <summary>The retained end-quest message id; presentation delivery remains with its owning action.</summary>
    public int? TerminalMessageId { get; init; }
    public DaggerfallQuestClockState[] Clocks { get; init; } = [];
    /// <summary>The optional Daggerfall faction supplied by a quest giver; zero is the donor's unscoped value.</summary>
    public int FactionId { get; init; }
    /// <summary>The quest that invoked this child, if this was started by a run-quest operation.</summary>
    public string? ParentInstanceId { get; init; }
    /// <summary>Whether the retained terminal result satisfies a run-quest success branch.</summary>
    [JsonRequired]
    public bool? Succeeded { get; init; }
    /// <summary>Absolute game seconds when a terminal instance entered its one-week tombstone retention period.</summary>
    public long? TombstoneAtSeconds { get; init; }
    internal void ValidateShape()
    {
        if (string.IsNullOrWhiteSpace(InstanceId) || string.IsNullOrWhiteSpace(SourceFile) || string.IsNullOrWhiteSpace(DefinitionName))
            throw new ArgumentException("A quest instance requires its stable identity and normalized definition reference.");
        if (!Enum.IsDefined(Lifecycle) || (Lifecycle == DaggerfallQuestLifecycle.Active
                ? Outcome is not null || Succeeded is false || TombstoneAtSeconds is not null
                : string.IsNullOrWhiteSpace(Outcome) || Succeeded is null
                    || (Lifecycle == DaggerfallQuestLifecycle.Tombstoned ? TombstoneAtSeconds is null or < 0 : TombstoneAtSeconds is not null)))
            throw new ArgumentException($"Quest instance '{InstanceId}' has an incompatible lifecycle/outcome.");
        if (FactionId < 0 || (ParentInstanceId is not null && string.IsNullOrWhiteSpace(ParentInstanceId)))
            throw new ArgumentException($"Quest instance '{InstanceId}' has invalid faction or parent state.");
        ArgumentNullException.ThrowIfNull(Resources);
        ArgumentNullException.ThrowIfNull(Symbols);
        ArgumentNullException.ThrowIfNull(Tasks);
        ArgumentNullException.ThrowIfNull(Clocks);
        if (TerminalMessageId is < 0 || (TerminalMessageId is not null && Lifecycle != DaggerfallQuestLifecycle.Ended))
            throw new ArgumentException($"Quest instance '{InstanceId}' has an incompatible terminal message.");
        HashSet<string> resources = [];
        foreach (DaggerfallQuestResourceState resource in Resources)
        {
            ArgumentNullException.ThrowIfNull(resource);
            string symbol = Canonical(resource.Symbol, $"quest instance '{InstanceId}' resource");
            if (!resources.Add(symbol)) throw new ArgumentException($"Quest instance '{InstanceId}' binds resource '{symbol}' more than once.");
            ArgumentNullException.ThrowIfNull(resource.Binding);
            resource.Binding.Validate(symbol);
        }

        HashSet<string> symbols = [];
        foreach (DaggerfallQuestSymbolState symbol in Symbols)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            if (!symbols.Add(Canonical(symbol.Symbol, $"quest instance '{InstanceId}' symbol")) || symbol.Value is null)
                throw new ArgumentException($"Quest instance '{InstanceId}' has malformed or duplicate symbol state.");
        }
    }

    internal void Validate(DaggerfallDefinitions definitions, bool validateClockState = true)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ValidateShape();
        if (!definitions.QuestSources.Quests.TryGetValue(SourceFile, out DaggerfallQuestSourceDefinition? definition))
            throw new ArgumentException($"Quest instance '{InstanceId}' refers to missing definition '{SourceFile}'.");
        if (definition.Disposition != DaggerfallQuestDisposition.Compiled)
            throw new ArgumentException($"Quest instance '{InstanceId}' refers to diagnosed definition '{SourceFile}'.");
        if (!string.Equals(definition.Name, DefinitionName, StringComparison.Ordinal))
            throw new ArgumentException($"Quest instance '{InstanceId}' definition '{DefinitionName}' is incompatible with '{SourceFile}'.");
        if (definitions.QuestSources.UnresolvedReferences.Any(reference => reference.SourceFile == SourceFile))
            throw new ArgumentException($"Quest instance '{InstanceId}' refers to '{SourceFile}', whose normalized resource references are unresolved.");
        Dictionary<string, DaggerfallQuestResourceDefinition> declarations = definitions.QuestSources.Resources
            .Where(value => value.SourceFile == SourceFile).ToDictionary(value => value.CanonicalId, StringComparer.Ordinal);
        foreach (DaggerfallQuestResourceState resource in Resources)
        {
            string symbol = Canonical(resource.Symbol, $"quest instance '{InstanceId}' resource");
            if (!declarations.TryGetValue(symbol, out DaggerfallQuestResourceDefinition? declared))
                throw new ArgumentException($"Quest instance '{InstanceId}' refers to missing resource '{symbol}' in '{SourceFile}'.");
            if (resource.Binding.Kind != BindingKind(declared.Kind))
                throw new ArgumentException($"Quest instance '{InstanceId}' binds resource '{symbol}' as {resource.Binding.Kind}, but '{SourceFile}' declares it as {declared.Kind}.");
        }
        if (validateClockState)
            ValidateClocks(DaggerfallQuestClockCompiler.Compile(definition));
    }

    private void ValidateClocks(IReadOnlyList<DaggerfallQuestClockDefinition> definitions)
    {
        DaggerfallQuestClockCompiler.ValidateSavedState(InstanceId, definitions, Clocks);
    }

    internal static string Canonical(string symbol, string owner)
    {
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException($"{owner} needs a named symbol.");
        string value = symbol.Trim();
        if (value.Length >= 2 && value[0] == '_' && value[^1] == '_') value = value[1..^1];
        if (value.Length == 0) throw new ArgumentException($"{owner} needs a non-empty symbol.");
        return value.ToLowerInvariant();
    }

    private static DaggerfallQuestResourceBindingKind BindingKind(string kind) => kind.ToLowerInvariant() switch
    {
        "foe" or "person" => DaggerfallQuestResourceBindingKind.Actor,
        "item" => DaggerfallQuestResourceBindingKind.Item,
        "place" => DaggerfallQuestResourceBindingKind.Place,
        _ => throw new ArgumentException($"Quest resource declaration has unsupported kind '{kind}'."),
    };
}

internal sealed record DaggerfallQuestStartSave(string InstanceId, string SourceFile, string? ParentInstanceId, int FactionId)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(InstanceId) || string.IsNullOrWhiteSpace(SourceFile) || FactionId < 0
            || (ParentInstanceId is not null && string.IsNullOrWhiteSpace(ParentInstanceId)))
            throw new ArgumentException("A pending quest start has invalid identity, source, parent, or faction state.");
    }
}

internal sealed record DaggerfallQuestInstancesSave(DaggerfallQuestInstanceSave[] Instances)
{
    [JsonRequired]
    public DaggerfallQuestMessagesSave Messages { get; init; } = new([], [], null);
    [JsonRequired]
    public DaggerfallQuestStartSave[] PendingStarts { get; init; } = [];
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Instances);
        ArgumentNullException.ThrowIfNull(Messages);
        ArgumentNullException.ThrowIfNull(PendingStarts);
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DaggerfallQuestInstanceSave instance in Instances)
        {
            ArgumentNullException.ThrowIfNull(instance);
            if (!ids.Add(instance.InstanceId)) throw new ArgumentException($"Quest instance '{instance.InstanceId}' appears more than once.");
            instance.ValidateShape();
        }
        HashSet<string> pending = [];
        foreach (DaggerfallQuestStartSave start in PendingStarts)
        {
            ArgumentNullException.ThrowIfNull(start);
            start.Validate();
            if (!pending.Add(start.InstanceId) || ids.Contains(start.InstanceId))
                throw new ArgumentException($"Quest start '{start.InstanceId}' repeats an active or pending identity.");
        }
    }

    internal void Validate(DaggerfallDefinitions definitions)
    {
        Validate();
        foreach (DaggerfallQuestInstanceSave instance in Instances)
        {
            instance.Validate(definitions);
        }
        foreach (DaggerfallQuestStartSave start in PendingStarts)
        {
            if (!definitions.QuestSources.Quests.TryGetValue(start.SourceFile, out DaggerfallQuestSourceDefinition? source)
                || source.Disposition != DaggerfallQuestDisposition.Compiled)
                throw new ArgumentException($"Pending quest start '{start.InstanceId}' refers to unavailable source '{start.SourceFile}'.");
        }
    }

    internal void ValidateBindings(IReadOnlySet<long> actorIds, DurableIdentityAllocator identities, IReadOnlySet<(int Region, int Index)> locations,
        IReadOnlySet<(string Scope, long OwnerId, string StackId)> stacks)
    {
        ArgumentNullException.ThrowIfNull(actorIds);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(stacks);
        foreach (DaggerfallQuestInstanceSave instance in Instances)
        foreach (DaggerfallQuestResourceState resource in instance.Resources)
        {
            switch (resource.Binding.Kind)
            {
                case DaggerfallQuestResourceBindingKind.Actor:
                    foreach (long actorId in resource.Binding.ActorIds)
                        if (!actorIds.Contains(actorId)) throw new ArgumentException($"Quest instance '{instance.InstanceId}' resource '{resource.Symbol}' refers to missing actor {actorId}.");
                    break;
                case DaggerfallQuestResourceBindingKind.Item:
                    if (resource.Binding.UniqueItemIds.Length == 1)
                    {
                        ulong itemId = resource.Binding.UniqueItemIds[0];
                        if (identities.Classify(new DurableIdentityReference(DurableIdentityKind.Item, itemId)) != DurableIdentityClassification.Live)
                            throw new ArgumentException($"Quest instance '{instance.InstanceId}' resource '{resource.Symbol}' refers to non-live unique item {itemId}.");
                    }
                    else
                    {
                        DaggerfallQuestStackBinding stack = resource.Binding.Stacks[0];
                        if (!stacks.Contains((stack.Owner.Scope, stack.Owner.Id, stack.StackId)))
                            throw new ArgumentException($"Quest instance '{instance.InstanceId}' resource '{resource.Symbol}' refers to missing item stack '{stack.StackId}' for {stack.Owner.Scope} {stack.Owner.Id}.");
                    }
                    break;
                case DaggerfallQuestResourceBindingKind.Place:
                    DaggerfallSiteId site = resource.Binding.Places[0].Require();
                    if (!locations.Contains((site.Region, site.Index)))
                        throw new ArgumentException($"Quest instance '{instance.InstanceId}' resource '{resource.Symbol}' refers to missing place {site.Region}:{site.Index}.");
                    break;
            }
        }
    }
}

/// <summary>Mutable runtime representation of one quest; save DTOs are captured only at explicit boundaries.</summary>
internal sealed class DaggerfallQuestRuntimeInstance
{
    internal DaggerfallQuestRuntimeInstance(DaggerfallQuestInstanceSave saved, DaggerfallQuestTaskProgram program)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(program);
        DaggerfallQuestTaskCompiler.ValidateState(program, saved.Tasks, saved.SourceFile);
        InstanceId = saved.InstanceId;
        SourceFile = saved.SourceFile;
        DefinitionName = saved.DefinitionName;
        Lifecycle = saved.Lifecycle;
        Outcome = saved.Outcome;
        Resources = CopyResources(saved.Resources);
        Symbols = [.. saved.Symbols];
        TerminalMessageId = saved.TerminalMessageId;
        FactionId = saved.FactionId;
        ParentInstanceId = saved.ParentInstanceId;
        Succeeded = saved.Succeeded;
        TombstoneAtSeconds = saved.TombstoneAtSeconds;
        Tasks = saved.Tasks.Select(task => new DaggerfallQuestTaskRuntimeState(task)).ToArray();
        Clocks = [.. saved.Clocks];
    }

    internal string InstanceId { get; }
    internal string SourceFile { get; }
    internal string DefinitionName { get; }
    internal DaggerfallQuestLifecycle Lifecycle { get; set; }
    internal string? Outcome { get; set; }
    internal DaggerfallQuestResourceState[] Resources { get; set; }
    internal DaggerfallQuestSymbolState[] Symbols { get; set; }
    internal int? TerminalMessageId { get; set; }
    internal int FactionId { get; }
    internal string? ParentInstanceId { get; }
    internal bool? Succeeded { get; set; }
    internal long? TombstoneAtSeconds { get; set; }
    internal DaggerfallQuestTaskRuntimeState[] Tasks { get; }
    internal DaggerfallQuestClockState[] Clocks { get; set; }

    internal bool StartClock(string symbol)
    {
        int index = Array.FindIndex(Clocks, clock => clock.Symbol == symbol);
        if (index < 0) return false;
        DaggerfallQuestClockState clock = Clocks[index];
        if (DaggerfallQuestClockCompiler.UnsupportedStartCondition(clock) is { } condition)
            throw new NotSupportedException($"Quest clock '{symbol}' requires {condition}; #8051 owns the missing quest-place/travel policy.");
        if (!clock.Finished) Clocks[index] = clock with { Enabled = true };
        return true;
    }

    internal bool StopClock(string symbol)
    {
        int index = Array.FindIndex(Clocks, clock => clock.Symbol == symbol);
        if (index < 0) return false;
        DaggerfallQuestClockState clock = Clocks[index];
        if (!clock.Finished) Clocks[index] = clock with { Enabled = false };
        return true;
    }

    internal DaggerfallQuestInstanceSave Capture() => new(InstanceId, SourceFile, DefinitionName, Lifecycle, Outcome,
        CopyResources(Resources),
        [.. Symbols])
    {
        TerminalMessageId = TerminalMessageId,
        Tasks = [.. Tasks.Select(task => task.Capture())],
        Clocks = [.. Clocks],
        FactionId = FactionId,
        ParentInstanceId = ParentInstanceId,
        Succeeded = Succeeded,
        TombstoneAtSeconds = TombstoneAtSeconds,
    };

    private static DaggerfallQuestResourceState[] CopyResources(IEnumerable<DaggerfallQuestResourceState> resources) =>
        resources.Select(resource => resource with { Binding = resource.Binding with { ActorIds = [.. resource.Binding.ActorIds], UniqueItemIds = [.. resource.Binding.UniqueItemIds], Stacks = [.. resource.Binding.Stacks], Places = [.. resource.Binding.Places] } }).ToArray();
}

/// <summary>Session-owned quest instances and immutable admitted task programs.</summary>
internal sealed class DaggerfallQuestInstances : IDaggerfallQuestTaskLifecycle
{
    private readonly DaggerfallDefinitions _definitions;
    private readonly IRandomService _random;
    private readonly DaggerfallQuestRuntimeAdmission? _admission;
    private readonly DaggerfallDisabledQuestSelection? _disabledSelection;
    private readonly IReadOnlyDictionary<string, DaggerfallQuestTaskProgram> _programs;
    private readonly Dictionary<string, DaggerfallQuestRuntimeInstance> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DaggerfallQuestStartSave> _pendingStarts = new(StringComparer.Ordinal);
    private DaggerfallQuestRuntime? _runtime;
    private const long TombstoneRetentionSeconds = 7 * 24 * 60 * 60;

    internal DaggerfallQuestInstances(DaggerfallDefinitions definitions, IRandomService random, DaggerfallQuestRuntimeAdmission? admission = null, DaggerfallDisabledQuestSelection? disabledSelection = null)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _admission = admission;
        _disabledSelection = disabledSelection;
        Messages = new DaggerfallQuestMessages(definitions, random);
        _programs = definitions.QuestSources.Quests.Values
            .Where(source => source.Disposition == DaggerfallQuestDisposition.Compiled)
            .ToDictionary(source => source.SourceFile, DaggerfallQuestTaskCompiler.Compile, StringComparer.Ordinal);
    }

    internal IReadOnlyCollection<DaggerfallQuestInstanceSave> All => _instances.Values.Select(instance => instance.Capture()).ToArray();
    internal DaggerfallQuestMessages Messages { get; }

    /// <summary>Binds the one session's live player and elapsed-time owners after composition completes.</summary>
    internal void BindRuntime(DaggerfallQuestRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    internal DaggerfallQuestPresentation ReadPresentation(Func<DaggerfallQuestRuntimeInstance, DaggerfallQuestMessageContext> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        DaggerfallQuestRenderedMessage[] deliveries = [.. Messages.Render(_instances.Values, context)];
        DaggerfallQuestPromptSave? pending = Messages.Pending;
        DaggerfallQuestRenderedMessage? prompt = pending is null ? null
            : deliveries.SingleOrDefault(delivery => delivery.Delivery == DaggerfallQuestMessageDelivery.Prompt
                && delivery.InstanceId == pending.InstanceId && delivery.MessageId == pending.MessageId);
        return new(deliveries, Messages.RenderJournal(_instances.Values, context), prompt);
    }


    internal DaggerfallQuestInstanceSave Start(DaggerfallQuestInstanceSave instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_disabledSelection?.IsSummonOnlySource(instance.SourceFile) == true)
            throw new ArgumentException($"Quest source '{instance.SourceFile}' requires an explicit Daedric summoning identity.", nameof(instance));
        return StartCore(instance);
    }

    /// <summary>Starts a source-backed Daedric quest only after the caller supplies its published summon identity.</summary>
    internal DaggerfallQuestInstanceSave StartSummoned(string summoningIdentity, DaggerfallQuestInstanceSave instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        DaggerfallDisabledQuestSelection selection = _disabledSelection
            ?? throw new ArgumentException($"Daedric summoning identity '{summoningIdentity}' is unavailable in this session.", nameof(summoningIdentity));
        if (!selection.TryResolveSummon(summoningIdentity, out DaggerfallSummonQuestResolution? resolution)
            || !string.Equals(resolution!.SourceFile, instance.SourceFile, StringComparison.Ordinal))
            throw new ArgumentException($"Daedric summoning identity '{summoningIdentity}' does not select quest source '{instance.SourceFile}'.", nameof(summoningIdentity));
        return StartCore(instance);
    }

    private DaggerfallQuestInstanceSave StartCore(DaggerfallQuestInstanceSave instance)
    {
        _admission?.RequireRunnable(instance.SourceFile);
        if (instance.Lifecycle != DaggerfallQuestLifecycle.Active)
            throw new ArgumentException("A newly started quest instance must be active.", nameof(instance));
        instance.Validate(_definitions, validateClockState: false);
        if (instance.Tasks.Length != 0) throw new ArgumentException("A newly started quest instance cannot supply prior task state.", nameof(instance));
        DaggerfallQuestTaskProgram program = Program(instance.SourceFile);
        DaggerfallQuestClockDefinition[] clocks = DaggerfallQuestClockCompiler.Compile(_definitions.QuestSources.Resolve(instance.SourceFile));
        DaggerfallQuestRuntimeInstance started = new(instance with { Tasks = DaggerfallQuestTaskCompiler.InitialState(program), Clocks = [.. clocks.Select(clock =>
        {
            long duration = DaggerfallQuestClockCompiler.UnsupportedTravelCondition(clock) is not null ? 0
                : clock.MaximumSeconds == clock.MinimumSeconds ? clock.MinimumSeconds
                : _random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.quest.clock", $"{instance.InstanceId}:{clock.Symbol}", clock.MinimumSeconds, clock.MaximumSeconds)).Value;
            return new DaggerfallQuestClockState(clock.Symbol, duration, duration, clock.Flag, clock.MinRange, clock.MaxRange, false, false);
        })] }, program);
        if (!_instances.TryAdd(started.InstanceId, started)) throw new ArgumentException($"Quest instance '{started.InstanceId}' already exists.");
        return started.Capture();
    }

    internal DaggerfallQuestInstanceSave Complete(string instanceId, string outcome) => Transition(instanceId, DaggerfallQuestLifecycle.Completed, outcome);
    internal DaggerfallQuestInstanceSave Fail(string instanceId, string outcome) => Transition(instanceId, DaggerfallQuestLifecycle.Failed, outcome);

    /// <summary>Advances active quest task blocks once within the already-admitted session simulation step.</summary>
    internal void Advance(DaggerfallVariableStore variables, DaggerfallCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(variables);
        long now = calendar.ToAbsoluteSeconds();
        // A terminal parent disposes children before any queued start or child operation can advance this admitted step.
        TombstoneAndCleanup(now);
        AdmitPendingStarts();
        foreach (DaggerfallQuestRuntimeInstance instance in _instances.Values.ToArray())
            if (instance.Lifecycle == DaggerfallQuestLifecycle.Active)
                DaggerfallQuestTaskRunner.Advance(instance, Program(instance.SourceFile), variables, calendar, Messages, this);
        TombstoneAndCleanup(now);
    }

    /// <summary>Records a DOM prompt answer once, then starts its source-declared target task.</summary>
    internal bool ChoosePrompt(DaggerfallVariableStore variables, string instanceId, int messageId, string promptId, int choiceId)
    {
        ArgumentNullException.ThrowIfNull(variables);
        if (!_instances.TryGetValue(instanceId, out DaggerfallQuestRuntimeInstance? instance)
            || instance.Lifecycle != DaggerfallQuestLifecycle.Active) return false;
        DaggerfallQuestTaskProgram program = Program(instance.SourceFile);
        return Messages.TryChoose(instanceId, messageId, promptId, choiceId,
            (prompt, choice) => DaggerfallQuestTaskRunner.PrepareChoice(instance, program, variables, prompt, choice,
                operation => Messages.ResolvePromptMessage(instance, operation)), out _, out _);
    }

    /// <summary>Consumes elapsed calendar time once; clocks never own a timer or update loop.</summary>
    internal void AdvanceClocks(DaggerfallVariableStore variables, DaggerfallCalendar before, DaggerfallCalendar after)
    {
        ArgumentNullException.ThrowIfNull(variables);
        foreach (DaggerfallQuestRuntimeInstance instance in _instances.Values)
            DaggerfallQuestClockAdvancer.Advance(instance, Program(instance.SourceFile), variables, before, after);
    }

    internal DaggerfallQuestInstanceSave SetResource(string instanceId, DaggerfallQuestResourceState resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        DaggerfallQuestRuntimeInstance instance = Active(instanceId);
        string symbol = DaggerfallQuestInstanceSave.Canonical(resource.Symbol, $"quest instance '{instanceId}' resource");
        instance.Resources = instance.Resources.Where(value => DaggerfallQuestInstanceSave.Canonical(value.Symbol, $"quest instance '{instanceId}' resource") != symbol).Append(resource).ToArray();
        ValidateRuntime(instance);
        return instance.Capture();
    }

    internal DaggerfallQuestInstanceSave SetSymbol(string instanceId, DaggerfallQuestSymbolState symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        DaggerfallQuestRuntimeInstance instance = Active(instanceId);
        string name = DaggerfallQuestInstanceSave.Canonical(symbol.Symbol, $"quest instance '{instanceId}' symbol");
        instance.Symbols = instance.Symbols.Where(value => DaggerfallQuestInstanceSave.Canonical(value.Symbol, $"quest instance '{instanceId}' symbol") != name).Append(symbol).ToArray();
        ValidateRuntime(instance);
        return instance.Capture();
    }

    internal bool TryGet(string instanceId, out DaggerfallQuestInstanceSave? instance)
    {
        if (_instances.TryGetValue(instanceId, out DaggerfallQuestRuntimeInstance? value)) { instance = value.Capture(); return true; }
        instance = null;
        return false;
    }

    internal DaggerfallQuestInstancesSave Capture() => new([.. _instances.Values.OrderBy(value => value.InstanceId, StringComparer.Ordinal).Select(instance => instance.Capture())])
    {
        Messages = Messages.Capture(),
        PendingStarts = [.. _pendingStarts.Values.OrderBy(value => value.InstanceId, StringComparer.Ordinal)],
    };

    internal void Restore(DaggerfallQuestInstancesSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        foreach (DaggerfallQuestInstanceSave instance in saved.Instances)
            _admission?.RequireRunnable(instance.SourceFile);
        foreach (DaggerfallQuestStartSave start in saved.PendingStarts)
            _admission?.RequireRunnable(start.SourceFile);
        saved.Validate(_definitions);
        Dictionary<string, DaggerfallQuestRuntimeInstance> restored = new(StringComparer.Ordinal);
        foreach (DaggerfallQuestInstanceSave instance in saved.Instances)
        {
            DaggerfallQuestTaskProgram program = Program(instance.SourceFile);
            restored.Add(instance.InstanceId, new DaggerfallQuestRuntimeInstance(instance, program));
        }
        _instances.Clear();
        foreach ((string id, DaggerfallQuestRuntimeInstance instance) in restored) _instances.Add(id, instance);
        _pendingStarts.Clear();
        foreach (DaggerfallQuestStartSave start in saved.PendingStarts) _pendingStarts.Add(start.InstanceId, start);
        ValidateRelationships();
        foreach (DaggerfallQuestRuntimeInstance instance in _instances.Values)
            ValidateOperationReceipts(instance, Program(instance.SourceFile));
        foreach (DaggerfallQuestChoiceSave choice in saved.Messages.Choices)
        {
            DaggerfallQuestRuntimeInstance instance = _instances.TryGetValue(choice.InstanceId, out DaggerfallQuestRuntimeInstance? runtime)
                ? runtime : throw new ArgumentException($"Saved quest choice refers to missing instance '{choice.InstanceId}'.");
            DaggerfallQuestTaskRunner.ValidateChoice(instance, Program(instance.SourceFile), choice,
                operation => Messages.ResolvePromptMessage(instance, operation));
        }
        if (saved.Messages.Pending is { } pending)
        {
            DaggerfallQuestRuntimeInstance instance = _instances.TryGetValue(pending.InstanceId, out DaggerfallQuestRuntimeInstance? runtime)
                ? runtime : throw new ArgumentException($"Saved quest prompt refers to missing instance '{pending.InstanceId}'.");
            DaggerfallQuestTaskRunner.ValidatePrompt(instance, Program(instance.SourceFile), pending,
                operation => Messages.ResolvePromptMessage(instance, operation));
        }
        Messages.Restore(saved.Messages, _instances);
    }

    private DaggerfallQuestInstanceSave Transition(string instanceId, DaggerfallQuestLifecycle lifecycle, string outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome)) throw new ArgumentException("A completed or failed quest needs an outcome.", nameof(outcome));
        DaggerfallQuestRuntimeInstance instance = Active(instanceId);
        instance.Lifecycle = lifecycle;
        instance.Outcome = outcome;
        instance.Succeeded = lifecycle == DaggerfallQuestLifecycle.Completed;
        ValidateRuntime(instance);
        return instance.Capture();
    }

    private DaggerfallQuestRuntimeInstance Active(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || !_instances.TryGetValue(instanceId, out DaggerfallQuestRuntimeInstance? instance)) throw new KeyNotFoundException($"Quest instance '{instanceId}' does not exist.");
        if (instance.Lifecycle != DaggerfallQuestLifecycle.Active) throw new InvalidOperationException($"Quest instance '{instanceId}' is already {instance.Lifecycle}.");
        return instance;
    }

    private DaggerfallQuestTaskProgram Program(string sourceFile)
    {
        _admission?.RequireRunnable(sourceFile);
        return _programs.TryGetValue(sourceFile, out DaggerfallQuestTaskProgram? program)
            ? program : throw new ArgumentException($"Quest source '{sourceFile}' has no admitted task program.");
    }

    private void ValidateRuntime(DaggerfallQuestRuntimeInstance instance)
    {
        DaggerfallQuestInstanceSave captured = instance.Capture();
        captured.Validate(_definitions);
        DaggerfallQuestTaskCompiler.ValidateState(Program(instance.SourceFile), captured.Tasks, instance.SourceFile);
    }

    internal static bool IsProtectedMainQuest(string sourceFile)
    {
        string name = Path.GetFileNameWithoutExtension(sourceFile);
        return name.Equals("S0000999", StringComparison.OrdinalIgnoreCase)
            || name.Equals("S0000977", StringComparison.OrdinalIgnoreCase)
            || name.Equals("_BRISIEN", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Schedules a named child source under an existing parent using the same cycle and duplicate policy as run-quest.</summary>
    internal void ScheduleChild(string parentInstanceId, string sourceReference, int factionId, string childInstanceId)
    {
        if (!_instances.TryGetValue(parentInstanceId, out DaggerfallQuestRuntimeInstance? parent))
            throw new KeyNotFoundException($"Quest parent '{parentInstanceId}' does not exist.");
        string source = ResolveSource(sourceReference);
        if (WouldCreateCycle(parent.InstanceId, source))
            throw new ArgumentException($"Daggerfall policy rejects a child cycle through '{source}'.");
        ScheduleStart(source, parent.InstanceId, factionId, childInstanceId);
    }

    string IDaggerfallQuestTaskLifecycle.Pick(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation, int operationIndex, DaggerfallQuestTaskRuntimeState state)
    {
        DaggerfallQuestTaskOperationState receipt = state.OperationState[operationIndex];
        if (receipt.PickedTarget is { } persisted) return persisted;
        int selected = checked((int)_random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.quest.pick-one-of", $"{instance.InstanceId}:{operation.SourceLine}", 0, operation.Targets.Length - 1)).Value);
        string target = operation.Targets[selected];
        state.OperationState[operationIndex] = receipt with { PickedTarget = target };
        return target;
    }

    bool IDaggerfallQuestTaskLifecycle.IsLevelCompleted(int minimum) => Runtime.IsLevelCompleted(minimum);
    bool IDaggerfallQuestTaskLifecycle.IsAttributeAtLeast(string attribute, int minimum) => Runtime.IsAttributeAtLeast(attribute, minimum);
    bool IDaggerfallQuestTaskLifecycle.IsSkillAtLeast(string skill, int minimum) => Runtime.IsSkillAtLeast(skill, minimum);
    void IDaggerfallQuestTaskLifecycle.Train(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) => Runtime.Train(instance, operation);

    void IDaggerfallQuestTaskLifecycle.Schedule(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) =>
        ScheduleStart(ResolveSource(operation.Targets.Single()), null, instance.FactionId,
            $"{instance.InstanceId}:start:{operation.SourceLine}");


    string? IDaggerfallQuestTaskLifecycle.RunChild(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskDefinition task,
        DaggerfallQuestTaskOperation operation, int operationIndex, DaggerfallQuestTaskRuntimeState state)
    {
        DaggerfallQuestTaskOperationState receipt = state.OperationState[operationIndex];
        string childId = receipt.ChildInstanceId ?? $"{instance.InstanceId}:run:{task.Symbol}:{operationIndex}";
        if (receipt.ChildInstanceId is null)
        {
            if (!TryResolveSource(operation.Targets[0], out string? source)) return operation.Targets[2];
            if (WouldCreateCycle(instance.InstanceId, source!))
                throw new ArgumentException($"Quest child action at line {operation.SourceLine} rejects a child cycle through '{source}'.");
            ScheduleStart(source!, instance.InstanceId, instance.FactionId, childId);
            state.OperationState[operationIndex] = receipt with { ChildInstanceId = childId };
            return null;
        }
        if (_pendingStarts.ContainsKey(childId)) return null;
        if (!_instances.TryGetValue(childId, out DaggerfallQuestRuntimeInstance? child))
        {
            return operation.Targets[2];
        }
        if (child.Lifecycle is DaggerfallQuestLifecycle.Active) return null;
        return child.Succeeded == true ? operation.Targets[1] : operation.Targets[2];
    }

    private void ScheduleStart(string sourceFile, string? parentInstanceId, int factionId, string instanceId)
    {
        if (factionId < 0) throw new ArgumentOutOfRangeException(nameof(factionId));
        if (_instances.ContainsKey(instanceId) || _pendingStarts.ContainsKey(instanceId)) return;
        _pendingStarts.Add(instanceId, new(instanceId, sourceFile, parentInstanceId, factionId));
    }

    private void AdmitPendingStarts()
    {
        DaggerfallQuestStartSave[] pending = [.. _pendingStarts.Values.OrderBy(value => value.InstanceId, StringComparer.Ordinal)];
        foreach (DaggerfallQuestStartSave start in pending)
        {
            DaggerfallQuestSourceDefinition source = _definitions.QuestSources.Resolve(start.SourceFile);
            StartCore(new(start.InstanceId, source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], [])
            {
                ParentInstanceId = start.ParentInstanceId,
                FactionId = start.FactionId,
            });
            _pendingStarts.Remove(start.InstanceId);
        }
    }

    private void TombstoneAndCleanup(long now)
    {
        foreach (DaggerfallQuestRuntimeInstance instance in _instances.Values)
            if (instance.Lifecycle is DaggerfallQuestLifecycle.Completed or DaggerfallQuestLifecycle.Failed or DaggerfallQuestLifecycle.Ended)
            {
                TerminateChildren(instance);
                instance.Lifecycle = DaggerfallQuestLifecycle.Tombstoned;
                instance.TombstoneAtSeconds = now;
            }
        string[] expired = [.. _instances.Values
            .Where(instance => instance.Lifecycle == DaggerfallQuestLifecycle.Tombstoned && !IsProtectedMainQuest(instance.SourceFile)
                && now - instance.TombstoneAtSeconds!.Value > TombstoneRetentionSeconds)
            .Select(instance => instance.InstanceId)];
        foreach (string id in expired)
            _instances.Remove(id);
        if (expired.Length > 0) Messages.RemoveInstances(new HashSet<string>(expired, StringComparer.Ordinal));
    }

    private string ResolveSource(string reference)
    {
        if (TryResolveSource(reference, out string? source)) return source!;
        throw new ArgumentException($"Quest source '{reference}' is not admitted.");
    }

    private bool TryResolveSource(string reference, out string? source)
    {
        string requested = reference.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? reference : reference + ".txt";
        if (_definitions.QuestSources.Quests.TryGetValue(requested, out DaggerfallQuestSourceDefinition? direct))
        {
            source = RequireActiveOffer(direct).SourceFile;
            return true;
        }
        DaggerfallQuestSourceDefinition? named = _definitions.QuestSources.Quests.Values.SingleOrDefault(source => source.Name.Equals(reference, StringComparison.OrdinalIgnoreCase));
        if (named is null)
        {
            source = null;
            return false;
        }
        source = RequireActiveOffer(named).SourceFile;
        return true;
    }

    private DaggerfallQuestSourceDefinition RequireActiveOffer(DaggerfallQuestSourceDefinition source)
    {
        DaggerfallQuestCatalogRow? catalog = _definitions.QuestSources.Catalog.Rows.SingleOrDefault(row => row.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
        if (catalog is not null && !catalog.Active) throw new ArgumentException($"Quest source '{source.Name}' is not an active Daggerfall catalog offer.");
        return source;
    }

    private bool WouldCreateCycle(string parentInstanceId, string childSource)
    {
        for (DaggerfallQuestRuntimeInstance? cursor = _instances[parentInstanceId]; cursor is not null;)
        {
            if (cursor.SourceFile.Equals(childSource, StringComparison.Ordinal)) return true;
            cursor = cursor.ParentInstanceId is not null && _instances.TryGetValue(cursor.ParentInstanceId, out DaggerfallQuestRuntimeInstance? parent) ? parent : null;
        }
        return false;
    }

    private void TerminateChildren(DaggerfallQuestRuntimeInstance parent)
    {
        foreach (DaggerfallQuestRuntimeInstance child in _instances.Values.Where(child => child.ParentInstanceId == parent.InstanceId && child.Lifecycle == DaggerfallQuestLifecycle.Active))
        {
            child.Lifecycle = DaggerfallQuestLifecycle.Failed;
            child.Outcome = $"Parent quest '{parent.InstanceId}' ended.";
            child.Succeeded = false;
        }
        foreach (string id in _pendingStarts.Values.Where(start => start.ParentInstanceId == parent.InstanceId).Select(start => start.InstanceId).ToArray())
            _pendingStarts.Remove(id);
    }

    private void ValidateRelationships()
    {
        foreach (DaggerfallQuestRuntimeInstance instance in _instances.Values)
        {
            if (instance.ParentInstanceId is null) continue;
            if (!_instances.ContainsKey(instance.ParentInstanceId))
                throw new ArgumentException($"Quest instance '{instance.InstanceId}' refers to missing parent '{instance.ParentInstanceId}'.");
            HashSet<string> ancestry = [instance.InstanceId];
            for (DaggerfallQuestRuntimeInstance current = instance; current.ParentInstanceId is { } parent; current = _instances[parent])
                if (!ancestry.Add(parent)) throw new ArgumentException($"Quest instance '{instance.InstanceId}' has a cyclic parent relationship.");
        }
        foreach (DaggerfallQuestStartSave start in _pendingStarts.Values)
            if (start.ParentInstanceId is not null && !_instances.ContainsKey(start.ParentInstanceId))
                throw new ArgumentException($"Pending quest start '{start.InstanceId}' refers to missing parent '{start.ParentInstanceId}'.");
    }

    private void ValidateOperationReceipts(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program)
    {
        for (int taskIndex = 0; taskIndex < program.Tasks.Count; taskIndex++)
        {
            DaggerfallQuestTaskDefinition task = program.Tasks[taskIndex];
            DaggerfallQuestTaskRuntimeState state = instance.Tasks[taskIndex];
            for (int operationIndex = 0; operationIndex < task.Operations.Count; operationIndex++)
            {
                DaggerfallQuestTaskOperation operation = task.Operations[operationIndex];
                DaggerfallQuestTaskOperationState receipt = state.OperationState[operationIndex];
                if (operation.Kind == DaggerfallQuestTaskOperationKind.PickOneOf)
                {
                    if (receipt.ChildInstanceId is not null || receipt.PickedTarget is not null && !operation.Targets.Contains(receipt.PickedTarget, StringComparer.Ordinal))
                        throw new ArgumentException($"Quest instance '{instance.InstanceId}' has an invalid pick-one-of receipt at line {operation.SourceLine}.");
                    continue;
                }
                if (operation.Kind == DaggerfallQuestTaskOperationKind.RunQuest)
                {
                    if (receipt.PickedTarget is not null) throw new ArgumentException($"Quest instance '{instance.InstanceId}' has an invalid child receipt at line {operation.SourceLine}.");
                    if (receipt.ChildInstanceId is { } childId)
                    {
                        string expectedChildId = $"{instance.InstanceId}:run:{task.Symbol}:{operationIndex}";
                        if (!string.Equals(childId, expectedChildId, StringComparison.Ordinal))
                            throw new ArgumentException($"Quest instance '{instance.InstanceId}' has an invalid child receipt at line {operation.SourceLine}.");
                        if (!TryResolveSource(operation.Targets[0], out string? source)
                            || !(_instances.TryGetValue(childId, out DaggerfallQuestRuntimeInstance? child) && child.ParentInstanceId == instance.InstanceId && child.SourceFile == source
                                || _pendingStarts.TryGetValue(childId, out DaggerfallQuestStartSave? pending) && pending.ParentInstanceId == instance.InstanceId && pending.SourceFile == source
                                || state.OperationCompleted[operationIndex]))
                            throw new ArgumentException($"Quest instance '{instance.InstanceId}' has an invalid child receipt at line {operation.SourceLine}.");
                    }
                    continue;
                }
                if (receipt.PickedTarget is not null || receipt.ChildInstanceId is not null)
                    throw new ArgumentException($"Quest instance '{instance.InstanceId}' has state for a non-persistent operation at line {operation.SourceLine}.");
            }
        }
    }

    private DaggerfallQuestRuntime Runtime => _runtime ?? throw new InvalidOperationException("Daggerfall quest runtime has not been bound to this session.");

}
