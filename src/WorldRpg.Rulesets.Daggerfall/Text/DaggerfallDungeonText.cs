using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The text that is up, whatever raised it (a dungeon action or a building's greeting): the dungeon text actions with
/// their waiting answer, the text shown and the texts it covers, which come back as it closes or is answered.
/// </summary>
internal sealed class DaggerfallDungeonText(DaggerfallTextResolver text)
{
    private readonly DaggerfallDungeonTextActions _actions = new(text);
    private readonly Stack<DaggerfallDungeonTextProjection> _covered = new();

    /// <summary>The text that is up, or null.</summary>
    internal DaggerfallDungeonTextProjection? Projection { get; private set; }

    /// <summary>Whether a dungeon action is one of the text families this owner shows.</summary>
    internal static bool Shows(DaggerfallDungeonActionDefinition action) =>
        action.ActionFlag is (byte)DaggerfallDungeonActionFlag.ShowText
            or (byte)DaggerfallDungeonActionFlag.ShowTextWithInput
            or (byte)DaggerfallDungeonActionFlag.DoorText;

    /// <summary>Resolves a text action at the graph's one-based activation count; <see cref="Show"/> puts it up.</summary>
    internal DaggerfallDungeonTextActionResult Execute(DaggerfallDungeonActionDefinition action, ulong activationCount,
        DaggerfallTextContext context) => _actions.Execute(action, activationCount, context);

    /// <summary>Puts a text up over the one that is up, which comes back when it closes. Nothing for no text.</summary>
    internal void Show(DaggerfallDungeonTextProjection? projection)
    {
        if (projection is null) return;
        if (Projection is { } covered) _covered.Push(covered);
        Projection = projection;
    }

    /// <summary>Whether the text up is this action's door warning.</summary>
    internal bool ShowsDoorText(string actionId) =>
        Projection is { Kind: DaggerfallDungeonTextActionKind.DoorText, ActionId: var shown }
        && string.Equals(shown, actionId, StringComparison.Ordinal);

    /// <summary>A building's greeting goes up in place of what is up.</summary>
    internal void Greet(DaggerfallDungeonTextProjection greeting) => Projection = greeting;

    /// <summary>The greeting is read and goes; nothing it covered comes back.</summary>
    internal void CloseGreeting() => Projection = null;

    /// <summary>Whether an input names the text that is up, by its action and revision.</summary>
    internal bool IsUp(string actionId, ulong revision) =>
        Projection is { } projection && projection.ActionId == actionId && projection.Revision == revision;

    /// <summary>
    /// Closes the text up, cancelling its waiting answer when it is this one, and brings back what it covered. Null
    /// when it closed, else why it did not.
    /// </summary>
    internal string? Close(string actionId, ulong revision)
    {
        if (_actions.Pending is { } pending && pending.ActionId == actionId && pending.Revision == revision)
        {
            DaggerfallDungeonTextActionResult cancelled = _actions.Cancel(actionId, revision);
            if (cancelled.Outcome != DaggerfallDungeonTextOutcome.Cancelled)
                return cancelled.Diagnostic ?? "Dungeon text cancellation was rejected.";
        }
        Projection = _covered.TryPop(out DaggerfallDungeonTextProjection? beneath) ? beneath : null;
        return null;
    }

    /// <summary>
    /// Answers the waiting text. An answer to no current wait changes nothing; any other closes the text and brings
    /// back what it covered, accepted or not.
    /// </summary>
    internal DaggerfallDungeonTextActionResult Answer(string actionId, ulong revision, string answer)
    {
        DaggerfallDungeonTextActionResult submitted = _actions.Submit(actionId, revision, answer);
        if (submitted.Outcome is DaggerfallDungeonTextOutcome.StaleSubmission or DaggerfallDungeonTextOutcome.NoPendingAnswer)
            return submitted;
        Projection = _covered.TryPop(out DaggerfallDungeonTextProjection? previous) ? previous : null;
        return submitted;
    }

    internal DaggerfallDungeonTextSnapshot Capture() => _actions.Capture();

    /// <summary>
    /// Restores the actions' revision and waiting answer and puts a waiting riddle back up, over the active graph's
    /// actions (null with no graph). A wait whose action is gone refuses the save.
    /// </summary>
    internal void Restore(DaggerfallDungeonTextSnapshot snapshot,
        Func<IReadOnlyList<DaggerfallDungeonActionDefinition>?> activeActions, Func<DaggerfallTextContext> context)
    {
        _covered.Clear();
        _actions.Restore(snapshot);
        if (_actions.Pending is not { } pending) return;
        IReadOnlyList<DaggerfallDungeonActionDefinition> actions = activeActions()
            ?? throw new ArgumentException($"Saved dungeon answer '{pending.ActionId}' has no active action graph.", nameof(snapshot));
        DaggerfallDungeonActionDefinition definition = actions.SingleOrDefault(action => action.Id == pending.ActionId)
            ?? throw new ArgumentException($"Saved dungeon answer '{pending.ActionId}' is absent from the active graph.", nameof(snapshot));
        DaggerfallDungeonTextActionResult resumed = _actions.Resume(definition, context());
        Projection = resumed.Projection
            ?? throw new ArgumentException(resumed.Diagnostic ?? "Saved dungeon answer could not resume.", nameof(snapshot));
    }

    /// <summary>The site unloads: a waiting answer is cancelled and no text stays up.</summary>
    internal void CancelOnUnload()
    {
        _actions.CancelPendingOnUnload();
        Projection = null;
        _covered.Clear();
    }
}
