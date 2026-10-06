using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// One finished quest the journal keeps after its runtime and tombstone are gone: the source that
/// names it, whether it succeeded, and the absolute game second it was retired. The title is read
/// from the quest corpus at presentation time rather than frozen into the save.
/// </summary>
internal sealed record DaggerfallFinishedQuestSave(string InstanceId, string SourceFile, bool Succeeded, long FinishedAtSeconds);

/// <summary>One active quest's journal page: its title, the earliest deadline the player can read, and its entries.</summary>
internal sealed record DaggerfallQuestJournalGroup(string InstanceId, string Title, string? Deadline,
    IReadOnlyList<DaggerfallQuestRenderedMessage> Entries);

/// <summary>One finished quest's journal page: its title, outcome, the day it finished, and its retained entries.</summary>
internal sealed record DaggerfallFinishedQuestJournalGroup(string InstanceId, string Title, bool Succeeded, string Status,
    IReadOnlyList<DaggerfallQuestRenderedMessage> Entries);

/// <summary>The quest journal grouped as the donor's active-quest and finished-quest pages.</summary>
internal sealed record DaggerfallQuestJournalPresentation(
    IReadOnlyList<DaggerfallQuestJournalGroup> Active,
    IReadOnlyList<DaggerfallFinishedQuestJournalGroup> Finished)
{
    /// <summary>Every rendered entry, active pages first, for callers that only read the text.</summary>
    internal IEnumerable<DaggerfallQuestRenderedMessage> Entries =>
        Active.SelectMany(group => group.Entries).Concat(Finished.SelectMany(group => group.Entries));
}

internal sealed partial class DaggerfallQuestInstances
{
    /// <summary>
    /// How many finished quests the journal keeps. The donor notebook grows without limit; this
    /// history is bounded so a long game cannot grow the save without end, and the oldest record
    /// leaves together with its retained journal text.
    /// </summary>
    internal const int FinishedQuestCapacity = 100;

    private readonly List<DaggerfallFinishedQuestSave> _finished = [];

    /// <summary>Finished quests in the order they finished, oldest first.</summary>
    internal IReadOnlyList<DaggerfallFinishedQuestSave> Finished => _finished.ToArray();

    /// <summary>
    /// Records a quest that has just retired with a journal. Like the donor's
    /// <c>PlayerNotebook.AddFinishedQuest</c>, a quest that never logged anything leaves no record.
    /// </summary>
    private void RecordFinished(DaggerfallQuestRuntimeInstance instance, long now)
    {
        _finished.Add(new(instance.InstanceId, instance.SourceFile, instance.Succeeded == true, now));
        while (_finished.Count > FinishedQuestCapacity)
        {
            string oldest = _finished[0].InstanceId;
            _finished.RemoveAt(0);
            Messages.ForgetFinished(oldest);
        }
    }

    /// <summary>The corpus display name, or the donor's generic word when the source states none.</summary>
    private string QuestTitle(string sourceFile)
    {
        string title = _definitions.QuestSources.Resolve(sourceFile).DisplayName;
        return string.IsNullOrWhiteSpace(title) ? "Quest" : title.Trim();
    }

    private DaggerfallQuestJournalPresentation ReadJournal(Func<DaggerfallQuestRuntimeInstance, DaggerfallQuestMessageContext> context, DaggerfallCalendar now)
    {
        IReadOnlyList<DaggerfallQuestJournalEntrySave> saved = Messages.Journal;
        IReadOnlyList<DaggerfallQuestRenderedMessage> rendered = Messages.RenderJournal(_instances.Values, context);
        List<(DaggerfallQuestJournalEntrySave Saved, DaggerfallQuestRenderedMessage Rendered)> rows = [.. saved.Zip(rendered)];
        // Active pages follow the order each quest first logged an entry, entries in log order.
        DaggerfallQuestJournalGroup[] active = [.. rows.Where(row => row.Saved.SourceFile is null)
            .GroupBy(row => row.Saved.InstanceId, StringComparer.Ordinal)
            .Select(group =>
            {
                DaggerfallQuestRuntimeInstance instance = _instances[group.Key];
                return new DaggerfallQuestJournalGroup(instance.InstanceId, QuestTitle(instance.SourceFile),
                    Deadline(instance, group.Select(row => row.Saved.MessageId), now), [.. group.Select(row => row.Rendered)]);
            })];
        DaggerfallFinishedQuestJournalGroup[] finished = [.. _finished.Select(record =>
        {
            string outcome = record.Succeeded ? "Completed" : "Ended";
            string day = DaggerfallCalendar.FromAbsoluteSeconds(record.FinishedAtSeconds).DescribeDate();
            return new DaggerfallFinishedQuestJournalGroup(record.InstanceId, QuestTitle(record.SourceFile), record.Succeeded,
                $"{outcome} on {day}", [.. rows.Where(row => row.Saved.InstanceId == record.InstanceId && row.Saved.SourceFile is not null)
                    .Select(row => row.Rendered)]);
        })];
        return new(active, finished);
    }

    /// <summary>
    /// The earliest running clock the player can read about. A clock is visible when one of the
    /// quest's current journal entries names it through the details macro (<c>=clock_</c>), which is
    /// how the donor's log text tells the player how long they have (<c>Clock.ExpandMacro</c>).
    /// </summary>
    private string? Deadline(DaggerfallQuestRuntimeInstance instance, IEnumerable<int> messageIds, DaggerfallCalendar now)
    {
        HashSet<string> named = new(StringComparer.Ordinal);
        foreach (int messageId in messageIds) named.UnionWith(Messages.DetailSymbols(instance.SourceFile, messageId));
        DaggerfallQuestClockState? clock = instance.Clocks
            .Where(value => value.Enabled && !value.Finished && value.RemainingSeconds > 0 && named.Contains(value.Symbol))
            .OrderBy(value => value.RemainingSeconds).FirstOrDefault();
        if (clock is null) return null;
        DaggerfallCalendar due = DaggerfallCalendar.FromAbsoluteSeconds(checked(now.ToAbsoluteSeconds() + clock.RemainingSeconds));
        return $"Due by {due.DescribeDate()} at {due.DescribeTime()} ({DaggerfallCalendar.DescribeDuration(clock.RemainingSeconds)} left)";
    }

    /// <summary>
    /// Restores the finished history after the journal it names. Every retained finished entry must
    /// belong to a record of the same source and every record must still own its journal; anything
    /// else is malformed current data rather than history to drop silently.
    /// </summary>
    private void RestoreFinished(DaggerfallFinishedQuestSave[] saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (saved.Length > FinishedQuestCapacity)
            throw new ArgumentException($"Saved finished-quest history holds {saved.Length} quests; the journal keeps {FinishedQuestCapacity}.");
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DaggerfallFinishedQuestSave record in saved)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (string.IsNullOrWhiteSpace(record.InstanceId) || string.IsNullOrWhiteSpace(record.SourceFile) || !ids.Add(record.InstanceId))
                throw new ArgumentException("Saved finished-quest history has a missing or repeated quest identity.");
            if (!_definitions.QuestSources.Quests.ContainsKey(record.SourceFile))
                throw new ArgumentException($"Saved finished quest '{record.InstanceId}' names unavailable source '{record.SourceFile}'.");
            if (_instances.TryGetValue(record.InstanceId, out DaggerfallQuestRuntimeInstance? live)
                && (live.Lifecycle != DaggerfallQuestLifecycle.Tombstoned || live.SourceFile != record.SourceFile))
                throw new ArgumentException($"Saved finished quest '{record.InstanceId}' collides with an unfinished or different live instance.");
            if (!Messages.Journal.Any(entry => entry.InstanceId == record.InstanceId && entry.SourceFile == record.SourceFile))
                throw new ArgumentException($"Saved finished quest '{record.InstanceId}' has no retained journal.");
        }
        if (Messages.Journal.FirstOrDefault(entry => entry.SourceFile is not null
                && !saved.Any(record => record.InstanceId == entry.InstanceId && record.SourceFile == entry.SourceFile)) is { } orphan)
            throw new ArgumentException($"Finished quest journal for '{orphan.InstanceId}' has no finished-quest record.");
        _finished.Clear();
        _finished.AddRange(saved);
    }
}
