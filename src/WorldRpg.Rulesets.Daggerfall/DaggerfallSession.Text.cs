using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallDungeonTextActions _dungeonText;
    private DaggerfallDungeonTextProjection? _dungeonTextProjection;
    private readonly Stack<DaggerfallDungeonTextProjection> _coveredDungeonText = new();

    internal DaggerfallDungeonTextProjection? DungeonTextProjection => _dungeonTextProjection;

    private DaggerfallTextContext DungeonTextContext() => DaggerfallTextContext.Empty with
    {
        Player = new DaggerfallTextPlayerContext(
            Name: State.Character.Identity.Name,
            Race: State.Character.Identity.RaceId,
            GoldCarried: State.Currency.Read().Gold.ToString(CultureInfo.InvariantCulture)),
        Location = new DaggerfallTextLocationContext(City: _site.ActiveSite?.Name),
    };

    internal DaggerfallDungeonActionExecution? ExecuteDungeonTextAction(DaggerfallDungeonActionDefinition action)
    {
        if (action.ActionFlag != (byte)DaggerfallDungeonActionFlag.ShowText
            && action.ActionFlag != (byte)DaggerfallDungeonActionFlag.ShowTextWithInput
            && action.ActionFlag != (byte)DaggerfallDungeonActionFlag.DoorText) return null;

        ulong count = State.DungeonActions[_activeProfileKey].State[action.Id].ActivationCount;
        DaggerfallDungeonTextActionResult result = _dungeonText.Execute(action, count, DungeonTextContext());
        if (result.ApplyDoorTrespass) _enemyBehavior.MakeActiveEnemiesHostile();
        if (result.Projection is { } projection)
        {
            if (_dungeonTextProjection is { } covered) _coveredDungeonText.Push(covered);
            _dungeonTextProjection = projection;
        }
        DaggerfallDungeonActionOutcome outcome = result.Outcome switch
        {
            DaggerfallDungeonTextOutcome.Presented => DaggerfallDungeonActionOutcome.Applied,
            DaggerfallDungeonTextOutcome.AwaitingAnswer => DaggerfallDungeonActionOutcome.AwaitingAnswer,
            DaggerfallDungeonTextOutcome.SkippedDoorText => DaggerfallDungeonActionOutcome.AppliedWithoutChange,
            DaggerfallDungeonTextOutcome.Busy => DaggerfallDungeonActionOutcome.RejectedOperation,
            _ => DaggerfallDungeonActionOutcome.RejectedOperation,
        };
        return new(action.Id, outcome, Diagnostic: result.Diagnostic);
    }

    private void ApplyDungeonTextInput(DaggerfallPlayerUiAction action)
    {
        if (!ulong.TryParse(action.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out ulong revision)
            || action.Item is not { Length: > 0 } actionId)
        {
            Presentation.SetOutcome("Dungeon text input has no current action identity or revision.");
            return;
        }

        if (_dungeonTextProjection is not { } projection
            || projection.ActionId != actionId || projection.Revision != revision)
        {
            Presentation.SetOutcome("Dungeon text input belongs to an older prompt.");
            return;
        }

        if (action.Action == "dungeon-text-close")
        {
            if (_dungeonText.Pending is { } pending && pending.ActionId == actionId && pending.Revision == revision)
            {
                DaggerfallDungeonTextActionResult cancelled = _dungeonText.Cancel(actionId, revision);
                if (cancelled.Outcome != DaggerfallDungeonTextOutcome.Cancelled)
                {
                    Presentation.SetOutcome(cancelled.Diagnostic ?? "Dungeon text cancellation was rejected.");
                    return;
                }
            }
            _dungeonTextProjection = _coveredDungeonText.TryPop(out DaggerfallDungeonTextProjection? coveredAfterClose)
                ? coveredAfterClose : null;
            Presentation.SetOutcome("Dungeon text closed.");
            return;
        }

        if (action.Action != "dungeon-text-answer" || action.Text is not { } answer)
            throw new ArgumentException("Dungeon text input action is invalid.", nameof(action));
        DaggerfallDungeonTextActionResult submitted = _dungeonText.Submit(actionId, revision, answer);
        if (submitted.Outcome is DaggerfallDungeonTextOutcome.StaleSubmission or DaggerfallDungeonTextOutcome.NoPendingAnswer)
        {
            Presentation.SetOutcome(submitted.Diagnostic ?? "Dungeon text answer is no longer pending.");
            return;
        }

        _dungeonTextProjection = _coveredDungeonText.TryPop(out DaggerfallDungeonTextProjection? previous)
            ? previous : null;
        if (submitted.Outcome == DaggerfallDungeonTextOutcome.AcceptedAnswer)
        {
            if (State.DungeonActions.TryGetValue(_activeProfileKey, out DaggerfallDungeonActionGraph? graph))
                _ = ReportDungeonAction(graph.ContinueAcceptedAnswer(actionId));
            Presentation.SetOutcome("Answer accepted.");
        }
        else Presentation.SetOutcome("Answer rejected.");
    }

    private void RestoreDungeonText(DaggerfallDungeonTextSnapshot snapshot)
    {
        _coveredDungeonText.Clear();
        _dungeonText.Restore(snapshot);
        if (_dungeonText.Pending is not { } pending) return;
        if (!State.DungeonActions.TryGetValue(_activeProfileKey, out DaggerfallDungeonActionGraph? graph))
            throw new ArgumentException($"Saved dungeon answer '{pending.ActionId}' has no active action graph.", nameof(snapshot));
        DaggerfallDungeonActionDefinition definition = graph.Definitions.SingleOrDefault(action => action.Id == pending.ActionId)
            ?? throw new ArgumentException($"Saved dungeon answer '{pending.ActionId}' is absent from the active graph.", nameof(snapshot));
        DaggerfallDungeonTextActionResult resumed = _dungeonText.Resume(definition, DungeonTextContext());
        _dungeonTextProjection = resumed.Projection
            ?? throw new ArgumentException(resumed.Diagnostic ?? "Saved dungeon answer could not resume.", nameof(snapshot));
    }

    private void CancelDungeonTextOnUnload()
    {
        _dungeonText.CancelPendingOnUnload();
        _dungeonTextProjection = null;
        _coveredDungeonText.Clear();
    }
}
