using WorldRpg.Kit;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The durable lifecycle of one instantiated quest source.</summary>
internal enum DaggerfallQuestLifecycle { Active, Completed, Failed }
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
    internal void ValidateShape()
    {
        if (string.IsNullOrWhiteSpace(InstanceId) || string.IsNullOrWhiteSpace(SourceFile) || string.IsNullOrWhiteSpace(DefinitionName))
            throw new ArgumentException("A quest instance requires its stable identity and normalized definition reference.");
        if (!Enum.IsDefined(Lifecycle) || (Lifecycle == DaggerfallQuestLifecycle.Active ? Outcome is not null : string.IsNullOrWhiteSpace(Outcome)))
            throw new ArgumentException($"Quest instance '{InstanceId}' has an incompatible lifecycle/outcome.");
        ArgumentNullException.ThrowIfNull(Resources);
        ArgumentNullException.ThrowIfNull(Symbols);
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

    internal void Validate(DaggerfallDefinitions definitions)
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

internal sealed record DaggerfallQuestInstancesSave(DaggerfallQuestInstanceSave[] Instances)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Instances);
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DaggerfallQuestInstanceSave instance in Instances)
        {
            ArgumentNullException.ThrowIfNull(instance);
            if (!ids.Add(instance.InstanceId)) throw new ArgumentException($"Quest instance '{instance.InstanceId}' appears more than once.");
            instance.ValidateShape();
        }
    }

    internal void Validate(DaggerfallDefinitions definitions)
    {
        Validate();
        foreach (DaggerfallQuestInstanceSave instance in Instances)
        {
            instance.Validate(definitions);
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

/// <summary>Session-owned quest instances. Ordered action execution remains with the later quest-action owner.</summary>
internal sealed class DaggerfallQuestInstances(DaggerfallDefinitions definitions)
{
    private readonly DaggerfallDefinitions _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly Dictionary<string, DaggerfallQuestInstanceSave> _instances = new(StringComparer.Ordinal);
    internal IReadOnlyCollection<DaggerfallQuestInstanceSave> All => _instances.Values.Select(Clone).ToArray();

    internal DaggerfallQuestInstanceSave Start(DaggerfallQuestInstanceSave instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance.Lifecycle != DaggerfallQuestLifecycle.Active)
            throw new ArgumentException("A newly started quest instance must be active.", nameof(instance));
        instance.Validate(_definitions);
        DaggerfallQuestInstanceSave copy = Clone(instance);
        if (!_instances.TryAdd(copy.InstanceId, copy)) throw new ArgumentException($"Quest instance '{copy.InstanceId}' already exists.");
        return Clone(copy);
    }

    internal DaggerfallQuestInstanceSave Complete(string instanceId, string outcome) => Transition(instanceId, DaggerfallQuestLifecycle.Completed, outcome);
    internal DaggerfallQuestInstanceSave Fail(string instanceId, string outcome) => Transition(instanceId, DaggerfallQuestLifecycle.Failed, outcome);

    internal DaggerfallQuestInstanceSave SetResource(string instanceId, DaggerfallQuestResourceState resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        DaggerfallQuestInstanceSave instance = Active(instanceId);
        string symbol = DaggerfallQuestInstanceSave.Canonical(resource.Symbol, $"quest instance '{instanceId}' resource");
        return Replace(instance with { Resources = instance.Resources.Where(value => DaggerfallQuestInstanceSave.Canonical(value.Symbol, $"quest instance '{instanceId}' resource") != symbol).Append(resource).ToArray() });
    }

    internal DaggerfallQuestInstanceSave SetSymbol(string instanceId, DaggerfallQuestSymbolState symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        DaggerfallQuestInstanceSave instance = Active(instanceId);
        string name = DaggerfallQuestInstanceSave.Canonical(symbol.Symbol, $"quest instance '{instanceId}' symbol");
        return Replace(instance with { Symbols = instance.Symbols.Where(value => DaggerfallQuestInstanceSave.Canonical(value.Symbol, $"quest instance '{instanceId}' symbol") != name).Append(symbol).ToArray() });
    }

    internal bool TryGet(string instanceId, out DaggerfallQuestInstanceSave? instance)
    {
        if (_instances.TryGetValue(instanceId, out DaggerfallQuestInstanceSave? value)) { instance = Clone(value); return true; }
        instance = null;
        return false;
    }

    internal DaggerfallQuestInstancesSave Capture() => new([.. _instances.Values.OrderBy(value => value.InstanceId, StringComparer.Ordinal).Select(Clone)]);

    internal void Restore(DaggerfallQuestInstancesSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate(_definitions);
        Dictionary<string, DaggerfallQuestInstanceSave> restored = new(StringComparer.Ordinal);
        foreach (DaggerfallQuestInstanceSave instance in saved.Instances) restored.Add(instance.InstanceId, Clone(instance));
        _instances.Clear();
        foreach ((string id, DaggerfallQuestInstanceSave instance) in restored) _instances.Add(id, instance);
    }

    private DaggerfallQuestInstanceSave Transition(string instanceId, DaggerfallQuestLifecycle lifecycle, string outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome)) throw new ArgumentException("A completed or failed quest needs an outcome.", nameof(outcome));
        return Replace(Active(instanceId) with { Lifecycle = lifecycle, Outcome = outcome });
    }

    private DaggerfallQuestInstanceSave Active(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || !_instances.TryGetValue(instanceId, out DaggerfallQuestInstanceSave? instance)) throw new KeyNotFoundException($"Quest instance '{instanceId}' does not exist.");
        if (instance.Lifecycle != DaggerfallQuestLifecycle.Active) throw new InvalidOperationException($"Quest instance '{instanceId}' is already {instance.Lifecycle}.");
        return instance;
    }

    private DaggerfallQuestInstanceSave Replace(DaggerfallQuestInstanceSave instance)
    {
        instance.Validate(_definitions);
        DaggerfallQuestInstanceSave copy = Clone(instance);
        _instances[copy.InstanceId] = copy;
        return Clone(copy);
    }

    private static DaggerfallQuestInstanceSave Clone(DaggerfallQuestInstanceSave source) => source with
    {
        Resources = source.Resources.Select(resource => resource with { Binding = resource.Binding with { ActorIds = [.. resource.Binding.ActorIds], UniqueItemIds = [.. resource.Binding.UniqueItemIds], Stacks = [.. resource.Binding.Stacks], Places = [.. resource.Binding.Places] } }).ToArray(),
        Symbols = [.. source.Symbols],
    };
}
