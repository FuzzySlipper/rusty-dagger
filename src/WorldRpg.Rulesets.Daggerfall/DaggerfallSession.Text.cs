using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
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

        if (action.Kind == DaggerfallUiActionKind.DungeonTextClose)
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

        if (action.Kind != DaggerfallUiActionKind.DungeonTextAnswer || action.Text is not { } answer)
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

    private DaggerfallQuestMessageContext QuestTextContext(DaggerfallQuestRuntimeInstance instance)
    {
        World.DaggerfallCalendar calendar = _time.Calendar;
        DaggerfallCharacterIdentity player = State.Character.Identity;
        Dictionary<string, DaggerfallQuestResourceTextContext> resources = [];
        foreach (DaggerfallQuestResourceState resource in instance.Resources)
        {
            if (resource.Text is { } boundText)
            {
                resources[DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "quest presentation resource")] = boundText;
                continue;
            }
            DaggerfallQuestResourceDefinition? declared = _definitions.QuestSources.Resources
                .SingleOrDefault(value => value.SourceFile == instance.SourceFile
                    && value.CanonicalId == DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "quest presentation resource"));
            if (declared is null) continue;
            string? place = resource.Binding.Places.Select(value => _site.TryFind(new DaggerfallSiteId(value.Region!.Value, value.Index!.Value), out var site) ? site.Name : null)
                .FirstOrDefault(value => value is not null);
            string? actorRole = resource.Binding.ActorIds.Select(id => State.Npcs.All.FirstOrDefault(npc => npc.DurableId == id)?.Role)
                .FirstOrDefault(value => value is not null);
            // A quest can consume its item before ending. Its source meaning outlives the live item.
            string? item = resource.Binding.UniqueItemIds.Where(State.ItemInstances.ContainsUnique)
                .Select(id => State.ItemInstances.RequireUnique(id).ItemId).FirstOrDefault(value => value is not null);
            if (item is null && declared.Kind == "item")
                item = declared.Item?.Template is int template && _definitions.ItemTemplateCatalog.Templates.TryGetValue(template, out var itemTemplate)
                    ? itemTemplate.Name : declared.TargetSourceSpelling?.Replace('_', ' ');
            string? named = declared.Person?.Named?.Replace('_', ' ');
            string? faction = declared.Person?.Faction?.Replace('_', ' ');
            string? group = declared.Person?.Group?.Replace('_', ' ');
            string? name = place ?? named ?? item ?? actorRole ?? group;
            resources[DaggerfallQuestInstanceSave.Canonical(resource.Symbol, "quest presentation resource")] = new(
                Name: name,
                NameTwo: place,
                NameThree: place,
                NameFour: place,
                Details: item ?? declared.SourceText,
                Binding: actorRole ?? place,
                Faction: faction);
        }
        return new(new(
            new DaggerfallTextPlayerContext(Name: player.Name, Race: player.RaceId),
            new DaggerfallTextCalendarContext(
                Date: $"{calendar.Month + 1}/{calendar.Day + 1}/{calendar.Year}",
                Time: $"{calendar.Hour:D2}:{calendar.Minute:D2}",
                DayNumber: (calendar.Day + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                MonthNumber: (calendar.Month + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Year: calendar.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Minute: calendar.Minute.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Hour: calendar.Hour.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Season: calendar.Season.ToString()),
            new DaggerfallTextLocationContext(City: _site.ActiveSite?.Name,
                Region: _site.Region?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(), new(), new()), resources);
    }
}
