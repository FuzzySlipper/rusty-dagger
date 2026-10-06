using System.Text.RegularExpressions;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The donor delivery semantics that retain a message identity instead of copied rendered text.</summary>
internal enum DaggerfallQuestMessageDelivery { Popup, Letter, Rumor, Journal, Prompt }

internal static class DaggerfallQuestMessageDeliveries
{
    /// <summary>The heading the player reads above a delivered quest message.</summary>
    internal static string Heading(DaggerfallQuestMessageDelivery delivery) => delivery switch
    {
        DaggerfallQuestMessageDelivery.Popup => "Message",
        DaggerfallQuestMessageDelivery.Letter => "Letter",
        DaggerfallQuestMessageDelivery.Rumor => "Rumor",
        DaggerfallQuestMessageDelivery.Journal => "Journal",
        _ => "Question",
    };
}

/// <summary>A source-backed message awaiting ordinary DOM presentation.</summary>
internal sealed record DaggerfallQuestMessageDeliverySave(string InstanceId, int MessageId, DaggerfallQuestMessageDelivery Delivery, int Variant = 0)
{
    [System.Text.Json.Serialization.JsonRequired]
    public ulong Id { get; init; }
    internal string EntryId => $"quest-message:{Id}";
}
internal sealed record DaggerfallQuestJournalEntrySave(string InstanceId, int Step, int MessageId)
{
    // Finished entries retain just their readable source and bound text values, not a quest runtime.
    public string? SourceFile { get; init; }
    public DaggerfallQuestMessageContext? Context { get; init; }
    internal string EntryId => $"quest-journal/{Uri.EscapeDataString(InstanceId)}/{Step}";
}
internal sealed record DaggerfallQuestPromptOption(int Id, string Label, string Target);
internal sealed record DaggerfallQuestPromptSave(string InstanceId, int MessageId, DaggerfallQuestPromptOption[] Options, string TaskSymbol, int OperationIndex, int Occurrence)
{
    internal string Id => $"{Uri.EscapeDataString(InstanceId)}/{Uri.EscapeDataString(TaskSymbol)}/{OperationIndex}/{Occurrence}";
}
internal sealed record DaggerfallQuestChoiceSave(string InstanceId, int MessageId, int ChoiceId, string Target, string TaskSymbol, int OperationIndex, int Occurrence);
internal sealed record DaggerfallQuestMessagesSave(
    DaggerfallQuestMessageDeliverySave[] Deliveries,
    DaggerfallQuestJournalEntrySave[] Journal,
    DaggerfallQuestPromptSave? Pending)
{
    [System.Text.Json.Serialization.JsonRequired]
    public DaggerfallQuestChoiceSave[] Choices { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public ulong LastDeliveryId { get; init; }
}

/// <summary>Every non-global resource spelling the retained quest message grammar selects.</summary>
internal sealed record DaggerfallQuestResourceTextContext(
    string? Name = null, string? NameTwo = null, string? NameThree = null, string? NameFour = null,
    string? Details = null, string? Binding = null, string? Faction = null, string? NpcVampireClan = null);

/// <summary>The only text values a quest delivery may read: global session context and explicit bound resources.</summary>
internal sealed record DaggerfallQuestMessageContext(
    DaggerfallTextContext Text,
    IReadOnlyDictionary<string, DaggerfallQuestResourceTextContext> Resources)
{
    internal static DaggerfallQuestMessageContext Empty { get; } = new(DaggerfallTextContext.Empty,
        new Dictionary<string, DaggerfallQuestResourceTextContext>(StringComparer.Ordinal));
}

internal sealed record DaggerfallQuestRenderedMessage(
    string InstanceId,
    int MessageId,
    DaggerfallQuestMessageDelivery Delivery,
    string Text,
    IReadOnlyList<string> Diagnostics,
    string? Signoff = null, string? PromptId = null, IReadOnlyList<DaggerfallQuestPromptOption>? Options = null)
{
    public string? EntryId { get; init; }
}
internal sealed record DaggerfallQuestEscortFace(string InstanceId, string Symbol, string Name, string MediaId);
internal sealed record DaggerfallQuestContact(string InstanceId, string Symbol);
/// <summary>
/// One source-declared topic a quest NPC can answer. The message ids stay with the quest message
/// owner; dialogue only carries the stable topic identity to the session action.
/// </summary>
internal sealed record DaggerfallQuestDialogueTopic(
    string Id,
    string Label,
    string InstanceId,
    string ResourceSymbol,
    int MessageId,
    bool PublishesRumor)
{
    internal string? Diagnostic { get; init; }
}
internal sealed record DaggerfallQuestPresentation(
    IReadOnlyList<DaggerfallQuestRenderedMessage> Deliveries,
    IReadOnlyList<DaggerfallQuestRenderedMessage> Journal,
    DaggerfallQuestRenderedMessage? Pending)
{
    public DaggerfallQuestOfferView? Offer { get; init; }
    public IReadOnlyList<DaggerfallQuestEscortFace> EscortFaces { get; init; } = [];
}

/// <summary>
/// Session-owned quest message state. It preserves source identities and choices, then resolves text
/// at presentation time from live quest bindings; a save therefore never freezes an obsolete name.
/// </summary>
internal sealed class DaggerfallQuestMessages
{
    // The donor tests the long name forms first. Keeping that order stops '__foo_' being consumed as
    // a shorter spelling and makes every retained name, faction, binding, and detail arm explicit.
    private static readonly Regex ResourceMacro = new(
        "(?<token>____(?<name4>[a-zA-Z0-9.-]+)_|___(?<name3>[a-zA-Z0-9.-]+)_|__(?<name2>[a-zA-Z0-9.-]+)_|_(?<name1>[a-zA-Z0-9.-]+)_|==(?<faction>[a-zA-Z0-9.-]+)_|=#(?<binding>[a-zA-Z0-9.-]+)_|=(?<details>[a-zA-Z0-9.-]+)_)|(?<npcClan>%vcn)(?![a-zA-Z0-9])",
        RegexOptions.CultureInvariant);
    private readonly DaggerfallTextResolver _text;
    private readonly IReadOnlyDictionary<string, DaggerfallQuestSourceDefinition> _sources;
    private readonly IReadOnlyDictionary<string, int> _staticMessages;
    private readonly IRandomService? _random;
    private readonly List<DaggerfallQuestMessageDeliverySave> _deliveries = [];
    private readonly List<DaggerfallQuestJournalEntrySave> _journal = [];
    private readonly List<DaggerfallQuestChoiceSave> _choices = [];
    private DaggerfallQuestPromptSave? _pending;
    private ulong _lastDeliveryId;
    internal Action<DaggerfallQuestRuntimeInstance, string>? ResourceNamed { get; set; }

    internal DaggerfallQuestMessages(DaggerfallDefinitions definitions, IRandomService random)
        : this(definitions?.TextPresentation ?? throw new ArgumentNullException(nameof(definitions)), definitions.QuestSources.Quests,
            definitions.QuestSources.Tables.StaticMessages.Lookup, random)
    {
    }

    /// <summary>Testable pack seam: source records and the shared normalized text resolver remain explicit.</summary>
    internal DaggerfallQuestMessages(DaggerfallTextResolver text, IReadOnlyDictionary<string, DaggerfallQuestSourceDefinition> sources,
        IReadOnlyDictionary<string, int>? staticMessages = null, IRandomService? random = null)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _staticMessages = staticMessages ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _random = random;
    }

    internal IReadOnlyList<DaggerfallQuestMessageDeliverySave> Deliveries => _deliveries;
    internal IReadOnlyList<DaggerfallQuestJournalEntrySave> Journal => _journal.ToArray();
    internal IReadOnlyList<DaggerfallQuestChoiceSave> Choices => _choices;
    internal DaggerfallQuestPromptSave? Pending => _pending;

    /// <summary>Named entry seams for later item, person, and action callers after their own interaction commits.</summary>
    internal void Popup(DaggerfallQuestRuntimeInstance instance, int messageId) => Deliver(instance, messageId, DaggerfallQuestMessageDelivery.Popup);
    /// <summary>Donor optional reward text: an absent source message does not cancel the completed reward.</summary>
    internal bool TryPopup(DaggerfallQuestRuntimeInstance instance, int messageId)
    {
        if (!_sources.TryGetValue(instance.SourceFile, out DaggerfallQuestSourceDefinition? source))
            throw new InvalidOperationException($"Quest instance '{instance.InstanceId}' refers to unavailable source '{instance.SourceFile}'.");
        if (!source.Messages.Any(message => message.Id == messageId)) return false;
        Popup(instance, messageId);
        return true;
    }

    internal void Letter(DaggerfallQuestRuntimeInstance instance, int messageId) => Deliver(instance, messageId, DaggerfallQuestMessageDelivery.Letter);
    internal void Rumor(DaggerfallQuestRuntimeInstance instance, int messageId) => Deliver(instance, messageId, DaggerfallQuestMessageDelivery.Rumor);

    /// <summary>
    /// Publishes a source-backed rumor and renders the exact variant retained by that delivery. The
    /// durable delivery remains owned by this message store; callers never copy it into dialogue state.
    /// </summary>
    internal (string Text, IReadOnlyList<string> Diagnostics) PublishRumorAndRender(
        DaggerfallQuestRuntimeInstance instance, int messageId, DaggerfallQuestMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(context);
        Rumor(instance, messageId);
        DaggerfallQuestMessageDeliverySave delivery = _deliveries[^1];
        (string text, _, IReadOnlyList<string> diagnostics) = Render(instance, messageId,
            DaggerfallQuestMessageDelivery.Rumor, delivery.Variant, context);
        return (text, diagnostics);
    }

    internal (string Text, IReadOnlyList<string> Diagnostics) RenderDelivery(DaggerfallQuestRuntimeInstance instance,
        DaggerfallQuestMessageDeliverySave delivery, DaggerfallQuestMessageContext context)
    {
        var rendered = Render(instance, delivery.MessageId, delivery.Delivery, delivery.Variant, context);
        return (rendered.Text, rendered.Diagnostics);
    }

    internal void RemoveDialogue(string instanceId) => _deliveries.RemoveAll(delivery => delivery.InstanceId == instanceId
        && delivery.Delivery == DaggerfallQuestMessageDelivery.Rumor);

    internal void Log(DaggerfallQuestRuntimeInstance instance, int messageId, int step)
    {
        RequireMessage(instance, messageId);
        if (step < 0) throw new ArgumentOutOfRangeException(nameof(step));
        _journal.RemoveAll(entry => entry.InstanceId == instance.InstanceId && entry.Step == step);
        _journal.Add(new(instance.InstanceId, step, messageId));
    }

    internal void RemoveLog(DaggerfallQuestRuntimeInstance instance, int step)
    {
        if (step < 0) throw new ArgumentOutOfRangeException(nameof(step));
        _journal.RemoveAll(entry => entry.InstanceId == instance.InstanceId && entry.Step == step);
    }

    internal string NoteText(DaggerfallQuestRuntimeInstance instance, int messageId, DaggerfallQuestMessageContext context) =>
        Render(instance, messageId, DaggerfallQuestMessageDelivery.Journal, 0, context).Text;

    internal bool Dismiss(string instanceId, string entryId) => _deliveries.RemoveAll(delivery =>
        delivery.InstanceId == instanceId && delivery.EntryId == entryId
        && delivery.Delivery is DaggerfallQuestMessageDelivery.Popup or DaggerfallQuestMessageDelivery.Letter) > 0;

    /// <summary>Opens one source-declared choice. Its owning operation remains incomplete until TryChoose records it.</summary>
    internal bool Prompt(DaggerfallQuestRuntimeInstance instance, int messageId, DaggerfallQuestPromptOption[] options, string taskSymbol, int operationIndex)
    {
        RequireMessage(instance, messageId);
        ValidateOptions(options);
        if (_pending is not null)
            return _pending.InstanceId == instance.InstanceId && _pending.MessageId == messageId
                && _pending.TaskSymbol == taskSymbol && _pending.OperationIndex == operationIndex;
        _pending = new(instance.InstanceId, messageId, [.. options],
            DaggerfallQuestInstanceSave.Canonical(taskSymbol, "quest prompt task"),
            operationIndex, NextOccurrence(instance.InstanceId, messageId, taskSymbol, operationIndex));
        Deliver(instance, messageId, DaggerfallQuestMessageDelivery.Prompt);
        return true;
    }

    /// <summary>Validates without mutation, records the answer, then activates its compiled branch.</summary>
    internal bool TryChoose(string instanceId, int messageId, string promptId, int choiceId,
        Func<DaggerfallQuestPromptSave, DaggerfallQuestChoiceSave, Action> prepare,
        out DaggerfallQuestChoiceSave? choice, out DaggerfallQuestPromptSave? prompt)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        choice = null;
        prompt = null;
        if (_pending is not { } pending || pending.InstanceId != instanceId || pending.MessageId != messageId
            || pending.Id != promptId) return false;
        DaggerfallQuestPromptOption? option = pending.Options.SingleOrDefault(value => value.Id == choiceId);
        if (option is null) return false;
        DaggerfallQuestChoiceSave proposed = new(instanceId, messageId, choiceId, option.Target,
            pending.TaskSymbol, pending.OperationIndex, pending.Occurrence);
        Action apply = prepare(pending, proposed);
        _choices.Add(proposed);
        _pending = null;
        _deliveries.RemoveAll(value => value.Delivery == DaggerfallQuestMessageDelivery.Prompt && value.InstanceId == instanceId && value.MessageId == messageId);
        apply();
        choice = proposed;
        prompt = pending;
        return true;
    }

    private static void ValidateOptions(DaggerfallQuestPromptOption[] options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Length is < 2 or > 4 || options.Select(value => value.Id).Distinct().Count() != options.Length)
            throw new ArgumentException("Quest prompt requires two to four distinct choices.");
        foreach (DaggerfallQuestPromptOption option in options)
            if (option.Id < 0 || string.IsNullOrWhiteSpace(option.Label)
                || option.Target != DaggerfallQuestInstanceSave.Canonical(option.Target, "quest prompt target"))
                throw new ArgumentException("Quest prompt choice is malformed.");
    }

    /// <summary>Resolves the precise source spelling for a persisted prompt operation.</summary>
    internal int ResolvePromptMessage(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Kind != DaggerfallQuestTaskOperationKind.Prompt)
            throw new ArgumentException("Quest source operation is not a prompt.");
        if (!TryResolveMessage(instance, operation.MessageId, operation.MessageAlias, out int resolved, out string? diagnostic))
            throw new ArgumentException($"Quest prompt source operation has no message binding: {diagnostic}");
        return resolved;
    }

    internal bool TryResolveMessage(DaggerfallQuestRuntimeInstance instance, int? messageId, string? alias, out int resolved, out string? diagnostic)
    {
        if (messageId is { } direct)
        {
            RequireMessage(instance, direct);
            resolved = direct;
            diagnostic = null;
            return true;
        }
        if (!string.IsNullOrWhiteSpace(alias) && _staticMessages.TryGetValue(alias, out int staticId))
        {
            RequireMessage(instance, staticId);
            resolved = staticId;
            diagnostic = null;
            return true;
        }
        resolved = 0;
        diagnostic = $"Quest static message '{alias}' has no admitted source text binding.";
        return false;
    }

    internal IReadOnlyList<DaggerfallQuestRenderedMessage> Render(IEnumerable<DaggerfallQuestRuntimeInstance> instances, DaggerfallQuestMessageContext? context = null)
        => Render(instances, _ => context ?? DaggerfallQuestMessageContext.Empty);

    internal IReadOnlyList<DaggerfallQuestRenderedMessage> Render(IEnumerable<DaggerfallQuestRuntimeInstance> instances,
        Func<DaggerfallQuestRuntimeInstance, DaggerfallQuestMessageContext> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Dictionary<string, DaggerfallQuestRuntimeInstance> byId = instances.ToDictionary(value => value.InstanceId, StringComparer.Ordinal);
        return _deliveries.Select(delivery =>
        {
            DaggerfallQuestRuntimeInstance instance = byId.TryGetValue(delivery.InstanceId, out DaggerfallQuestRuntimeInstance? value)
                ? value : throw new InvalidOperationException($"Quest message delivery refers to missing quest instance '{delivery.InstanceId}'.");
            (string text, string? signoff, IReadOnlyList<string> diagnostics) = Render(instance, delivery.MessageId, delivery.Delivery, delivery.Variant, context(instance));
            DaggerfallQuestPromptSave? prompt = delivery.Delivery == DaggerfallQuestMessageDelivery.Prompt
                && _pending?.InstanceId == delivery.InstanceId && _pending.MessageId == delivery.MessageId ? _pending : null;
            return new DaggerfallQuestRenderedMessage(delivery.InstanceId, delivery.MessageId, delivery.Delivery, text, diagnostics, signoff, prompt?.Id, prompt?.Options) { EntryId = delivery.EntryId };
        }).ToArray();
    }

    internal IReadOnlyList<DaggerfallQuestRenderedMessage> RenderJournal(IEnumerable<DaggerfallQuestRuntimeInstance> instances, DaggerfallQuestMessageContext? context = null)
        => RenderJournal(instances, _ => context ?? DaggerfallQuestMessageContext.Empty);

    internal IReadOnlyList<DaggerfallQuestRenderedMessage> RenderJournal(IEnumerable<DaggerfallQuestRuntimeInstance> instances,
        Func<DaggerfallQuestRuntimeInstance, DaggerfallQuestMessageContext> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Dictionary<string, DaggerfallQuestRuntimeInstance> byId = instances.ToDictionary(value => value.InstanceId, StringComparer.Ordinal);
        return Journal.Select(entry =>
        {
            (string text, _, IReadOnlyList<string> diagnostics) = entry.SourceFile is { } source
                ? Render(source, entry.MessageId, DaggerfallQuestMessageDelivery.Journal, 0, entry.Context!)
                : Render(RequireInstance(entry.InstanceId, byId), entry.MessageId, DaggerfallQuestMessageDelivery.Journal, 0,
                    context(RequireInstance(entry.InstanceId, byId)));
            return new DaggerfallQuestRenderedMessage(entry.InstanceId, entry.MessageId, DaggerfallQuestMessageDelivery.Journal, text, diagnostics) { EntryId = entry.EntryId };
        }).ToArray();
    }

    internal DaggerfallQuestMessagesSave Capture() => new([.. _deliveries], [.. Journal], _pending) { Choices = [.. _choices], LastDeliveryId = _lastDeliveryId };

    internal void RetainJournal(DaggerfallQuestRuntimeInstance instance,
        Func<DaggerfallQuestRuntimeInstance, DaggerfallQuestMessageContext> context)
    {
        if (_pending?.InstanceId == instance.InstanceId)
        {
            _deliveries.RemoveAll(delivery => delivery.InstanceId == instance.InstanceId && delivery.Delivery == DaggerfallQuestMessageDelivery.Prompt);
            _pending = null;
        }
        DaggerfallQuestJournalEntrySave[] entries = [.. _journal.Where(entry => entry.InstanceId == instance.InstanceId && entry.SourceFile is null)];
        if (entries.Length == 0) return;
        DaggerfallQuestMessageContext bound = BindSymbols(instance, context(instance));
        foreach (DaggerfallQuestJournalEntrySave entry in entries)
            _journal[_journal.IndexOf(entry)] = entry with { SourceFile = instance.SourceFile, Context = bound };
    }

    /// <summary>Releases presentation and prompt history when the owning tombstone expires.</summary>
    internal void RemoveInstances(IReadOnlySet<string> instanceIds)
    {
        ArgumentNullException.ThrowIfNull(instanceIds);
        _deliveries.RemoveAll(value => instanceIds.Contains(value.InstanceId));
        _journal.RemoveAll(entry => instanceIds.Contains(entry.InstanceId) && entry.SourceFile is null);
        _choices.RemoveAll(value => instanceIds.Contains(value.InstanceId));
        if (_pending is { } pending && instanceIds.Contains(pending.InstanceId)) _pending = null;
    }

    internal void Restore(DaggerfallQuestMessagesSave saved, IReadOnlyDictionary<string, DaggerfallQuestRuntimeInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(saved.Deliveries);
        ArgumentNullException.ThrowIfNull(saved.Journal);
        ArgumentNullException.ThrowIfNull(saved.Choices);
        _deliveries.Clear();
        _journal.Clear();
        _choices.Clear();
        _lastDeliveryId = saved.LastDeliveryId;
        HashSet<ulong> deliveryIds = [];
        foreach (DaggerfallQuestMessageDeliverySave delivery in saved.Deliveries)
        {
            DaggerfallQuestRuntimeInstance instance = RequireInstance(delivery.InstanceId, instances);
            RequireMessage(instance, delivery.MessageId);
            if (!Enum.IsDefined(delivery.Delivery)) throw new ArgumentException("Saved quest message delivery has an unknown kind.");
            if (delivery.Id == 0 || delivery.Id > _lastDeliveryId || !deliveryIds.Add(delivery.Id))
                throw new ArgumentException("Saved quest message delivery identity is malformed or duplicated.");
            _deliveries.Add(delivery);
        }
        foreach (DaggerfallQuestJournalEntrySave entry in saved.Journal)
        {
            if (entry.SourceFile is { } source)
            {
                RequireMessage(source, entry.MessageId);
                if (instances.TryGetValue(entry.InstanceId, out var retained)
                    && (retained.Lifecycle != DaggerfallQuestLifecycle.Tombstoned || retained.SourceFile != source))
                    throw new ArgumentException("Finished quest journal collides with an incompatible live instance.");
                if (_sources[source].Disposition != DaggerfallQuestDisposition.Compiled)
                    throw new ArgumentException($"Finished quest journal source '{source}' is not compiled.");
                if (string.IsNullOrWhiteSpace(entry.InstanceId) || entry.Context?.Text is null || entry.Context.Resources is null
                    || entry.Context.Text.Player is null || entry.Context.Text.Calendar is null || entry.Context.Text.Location is null
                    || entry.Context.Text.Faction is null || entry.Context.Text.Item is null || entry.Context.Text.Story is null
                    || entry.Context.Resources.Any(value => string.IsNullOrWhiteSpace(value.Key) || value.Value is null))
                    throw new ArgumentException("Finished quest journal entry has no valid bound text context.");
            }
            else
            {
                if (entry.Context is not null) throw new ArgumentException("Active quest journal entry has a finished text context.");
                RequireMessage(RequireInstance(entry.InstanceId, instances), entry.MessageId);
            }
            if (entry.Step < 0 || _journal.Any(value => value.InstanceId == entry.InstanceId && value.Step == entry.Step))
                throw new ArgumentException("Saved quest journal entries are malformed or duplicated.");
            _journal.Add(entry);
        }
        foreach (DaggerfallQuestChoiceSave choice in saved.Choices)
        {
            DaggerfallQuestRuntimeInstance instance = RequireInstance(choice.InstanceId, instances);
            RequireMessage(instance, choice.MessageId);
            if (string.IsNullOrWhiteSpace(choice.Target)) throw new ArgumentException("Saved quest choice has no task target.");
            if (choice.OperationIndex < 0 || choice.Occurrence < 0) throw new ArgumentException("Saved quest choice has an invalid prompt operation.");
            if (_choices.Any(existing => existing.InstanceId == choice.InstanceId && existing.MessageId == choice.MessageId
                && existing.TaskSymbol == choice.TaskSymbol && existing.OperationIndex == choice.OperationIndex && existing.Occurrence == choice.Occurrence))
                throw new ArgumentException("Saved quest choices contain a duplicate prompt occurrence.");
            _choices.Add(choice with { Target = DaggerfallQuestInstanceSave.Canonical(choice.Target, "saved quest choice target"),
                TaskSymbol = DaggerfallQuestInstanceSave.Canonical(choice.TaskSymbol, "saved quest choice task") });
        }
        if (saved.Pending is { } pending)
        {
            DaggerfallQuestRuntimeInstance instance = RequireInstance(pending.InstanceId, instances);
            RequireMessage(instance, pending.MessageId);
            if (pending.OperationIndex < 0 || pending.Occurrence < 0) throw new ArgumentException("Saved quest prompt has an invalid operation.");
            if (_choices.Any(choice => choice.InstanceId == pending.InstanceId && choice.MessageId == pending.MessageId
                && choice.TaskSymbol == pending.TaskSymbol && choice.OperationIndex == pending.OperationIndex && choice.Occurrence == pending.Occurrence))
                throw new ArgumentException("Saved quest prompt occurrence has already been answered.");
            ValidateOptions(pending.Options);
            _pending = pending with {
                TaskSymbol = DaggerfallQuestInstanceSave.Canonical(pending.TaskSymbol, "saved quest prompt task"),
            };
        }
        else _pending = null;
    }

    private int NextOccurrence(string instanceId, int messageId, string taskSymbol, int operationIndex)
    {
        int latest = _choices.Where(choice => choice.InstanceId == instanceId && choice.MessageId == messageId
                && choice.TaskSymbol == taskSymbol && choice.OperationIndex == operationIndex)
            .Select(choice => choice.Occurrence).DefaultIfEmpty(-1).Max();
        if (_pending is { } pending && pending.InstanceId == instanceId && pending.MessageId == messageId
            && pending.TaskSymbol == taskSymbol && pending.OperationIndex == operationIndex)
            latest = Math.Max(latest, pending.Occurrence);
        return checked(latest + 1);
    }

    private void Deliver(DaggerfallQuestRuntimeInstance instance, int messageId, DaggerfallQuestMessageDelivery delivery)
    {
        DaggerfallQuestMessageDefinition message = RequireMessage(instance, messageId);
        int variants = message.Lines.Count(line => line.Trim().Equals("<--->", StringComparison.Ordinal)) + 1;
        int variant = delivery is DaggerfallQuestMessageDelivery.Popup or DaggerfallQuestMessageDelivery.Rumor && _random is not null && variants > 1
            ? checked((int)_random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.quest.message", $"{instance.InstanceId}:{messageId}:{_deliveries.Count}", 0, variants - 1)).Value)
            : 0;
        _deliveries.Add(new(instance.InstanceId, messageId, delivery, variant) { Id = checked(++_lastDeliveryId) });
    }

    private (string Text, string? Signoff, IReadOnlyList<string> Diagnostics) Render(
        DaggerfallQuestRuntimeInstance instance,
        int messageId,
        DaggerfallQuestMessageDelivery delivery,
        int variant,
        DaggerfallQuestMessageContext context)
        => Render(instance.SourceFile, messageId, delivery, variant, BindSymbols(instance, context), delivery is DaggerfallQuestMessageDelivery.Popup or DaggerfallQuestMessageDelivery.Rumor or DaggerfallQuestMessageDelivery.Prompt or DaggerfallQuestMessageDelivery.Journal or DaggerfallQuestMessageDelivery.Letter
            ? symbol => ResourceNamed?.Invoke(instance, symbol) : null);

    private static DaggerfallQuestMessageContext BindSymbols(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestMessageContext context)
    {
        Dictionary<string, DaggerfallQuestResourceTextContext> resources = new(context.Resources, StringComparer.Ordinal);
        foreach (DaggerfallQuestSymbolState symbol in instance.Symbols)
        {
            string symbolKey = DaggerfallQuestInstanceSave.Canonical(symbol.Symbol, "quest message symbol");
            resources.TryAdd(symbolKey, new(symbol.Value, symbol.Value, symbol.Value, symbol.Value, symbol.Value, symbol.Value, symbol.Value));
        }
        return context with { Resources = resources };
    }

    private (string Text, string? Signoff, IReadOnlyList<string> Diagnostics) Render(string sourceFile, int messageId,
        DaggerfallQuestMessageDelivery delivery, int variant, DaggerfallQuestMessageContext context, Action<string>? resourceNamed = null)
    {
        DaggerfallQuestMessageDefinition message = RequireMessage(sourceFile, messageId);
        List<string> issues = [];
        string text = string.Join('\n', Variant(message.Lines, variant)).Replace("<ce>", string.Empty, StringComparison.Ordinal);

        DaggerfallTextKey key = new(DaggerfallTextKind.Resource, $"quest:{sourceFile}:{messageId}");
        string? referencedClan = null;
        text = ResourceMacro.Replace(text, match =>
        {
            if (match.Groups["npcClan"].Success)
            {
                if (referencedClan is null) return match.Value;
                var clanText = _text.ResolveRaw(match.Value, key,
                    context.Text with { Faction = context.Text.Faction with { NpcVampireClan = referencedClan } });
                issues.AddRange(clanText.Diagnostics.Select(diagnostic => $"{diagnostic.Kind}: {diagnostic.Detail}"));
                return clanText.Text;
            }
            string symbol = DaggerfallQuestInstanceSave.Canonical(match.Groups.Cast<Group>()
                .First(group => group.Success && group.Name is "name4" or "name3" or "name2" or "name1" or "faction" or "binding" or "details").Value,
                "quest message resource");
            referencedClan = context.Resources.TryGetValue(symbol, out var resource) ? resource.NpcVampireClan : null;
            string expanded = ExpandResource(match, context.Resources, delivery, issues);
            if (match.Groups["name1"].Success && !string.IsNullOrWhiteSpace(resource?.Name)) resourceNamed?.Invoke(symbol);
            return expanded;
        });
        DaggerfallTextRenderResult global = _text.ResolveRaw(text, key,
            context.Text with { Faction = context.Text.Faction with { NpcVampireClan = null } });
        issues.AddRange(global.Diagnostics.Select(diagnostic => $"{diagnostic.Kind}: {diagnostic.Detail}"));
        string rendered = global.Text;

        if (delivery != DaggerfallQuestMessageDelivery.Letter) return (rendered, null, issues);
        string[] lines = rendered.Split('\n');
        int last = Array.FindLastIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (last < 0) return (string.Empty, null, issues);
        string signoff = lines[last].Trim();
        return (string.Join('\n', lines[..last]).TrimEnd(), signoff, issues);
    }

    private static IEnumerable<string> Variant(IReadOnlyList<string> lines, int selected)
    {
        // Journal/log values use the donor's stored variant zero; popup and rumor deliveries record
        // their Engine-drawn source variant before this presentation pass.
        int variant = 0;
        foreach (string line in lines)
        {
            if (line.Trim().Equals("<--->", StringComparison.Ordinal)) { variant++; continue; }
            if (variant == selected) yield return line;
        }
    }

    private static string ExpandResource(Match match, IReadOnlyDictionary<string, DaggerfallQuestResourceTextContext> resources,
        DaggerfallQuestMessageDelivery delivery, ICollection<string> issues)
    {
        string group = match.Groups["name4"].Success ? "name4"
            : match.Groups["name3"].Success ? "name3"
            : match.Groups["name2"].Success ? "name2"
            : match.Groups["name1"].Success ? "name1"
            : match.Groups["faction"].Success ? "faction"
            : match.Groups["binding"].Success ? "binding" : "details";
        string symbol = DaggerfallQuestInstanceSave.Canonical(match.Groups[group].Value, "quest message macro");
        if (!resources.TryGetValue(symbol, out DaggerfallQuestResourceTextContext? resource))
        {
            issues.Add($"{match.Value}: quest resource '{symbol}' has no presentation binding.");
            return $"{match.Value}[unresolved]";
        }

        string? value = group switch
        {
            "name1" => resource.Name,
            "name2" => delivery == DaggerfallQuestMessageDelivery.Letter ? "..." : resource.NameTwo,
            "name3" => delivery == DaggerfallQuestMessageDelivery.Letter ? "..." : resource.NameThree,
            "name4" => delivery == DaggerfallQuestMessageDelivery.Letter ? "..." : resource.NameFour,
            "faction" => resource.Faction,
            "binding" => resource.Binding,
            _ => resource.Details,
        };
        if (!string.IsNullOrWhiteSpace(value)) return value;
        issues.Add($"{match.Value}: quest resource '{symbol}' has no {group} value.");
        // The issue list carries the diagnostic; the player text omits the unresolved symbol.
        return string.Empty;
    }

    private DaggerfallQuestMessageDefinition RequireMessage(DaggerfallQuestRuntimeInstance instance, int messageId)
        => RequireMessage(instance.SourceFile, messageId);

    private DaggerfallQuestMessageDefinition RequireMessage(string sourceFile, int messageId)
    {
        if (messageId <= 0) throw new ArgumentOutOfRangeException(nameof(messageId));
        if (!_sources.TryGetValue(sourceFile, out DaggerfallQuestSourceDefinition? source))
            throw new InvalidOperationException($"Quest journal refers to unavailable source '{sourceFile}'.");
        return source.Messages.SingleOrDefault(message => message.Id == messageId)
            ?? throw new ArgumentException($"Quest source '{sourceFile}' has no message {messageId}.");
    }

    private static DaggerfallQuestRuntimeInstance RequireInstance(string id, IReadOnlyDictionary<string, DaggerfallQuestRuntimeInstance> instances) =>
        !string.IsNullOrWhiteSpace(id) && instances.TryGetValue(id, out DaggerfallQuestRuntimeInstance? instance)
            ? instance : throw new ArgumentException($"Saved quest message state refers to unavailable quest instance '{id}'.");
}
