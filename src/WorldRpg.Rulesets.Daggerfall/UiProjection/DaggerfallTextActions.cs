using System.Globalization;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Text's actions. The text modal's read the text's action and revision and ask Text's owner, a building's greeting
/// closing lets its entry go ahead, and a riddle answered right goes on along the dungeon's graph. The reader's and
/// notebook's each ask the notebook for one revision-guarded change.
/// </summary>
/// <param name="continueAcceptedAnswer">Interiors: a riddle answered right goes on along the dungeon's graph.</param>
/// <param name="continueBuildingEntry">The held building entry's list, which a closed greeting lets go ahead.</param>
internal sealed class DaggerfallTextActions(DaggerfallDungeonText dungeonText, DaggerfallBookNotebook notebook,
    Action<string> continueAcceptedAnswer, PresentationState presentation, Action continueBuildingEntry)
{
    internal void ApplyDungeonText(DaggerfallPlayerUiAction action)
    {
        if (!ulong.TryParse(action.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out ulong revision)
            || action.Item is not { Length: > 0 } actionId)
        {
            presentation.SetOutcome("Dungeon text input has no current action identity or revision.");
            return;
        }

        if (!dungeonText.IsUp(actionId, revision))
        {
            presentation.SetOutcome("Dungeon text input belongs to an older prompt.");
            return;
        }

        if (action.Kind == DaggerfallUiActionKind.DungeonTextClose)
        {
            if (dungeonText.Projection!.Kind == DaggerfallDungeonTextActionKind.BuildingGreeting)
            { continueBuildingEntry(); return; }
            presentation.SetOutcome(dungeonText.Close(actionId, revision) ?? "Dungeon text closed.");
            return;
        }

        if (action.Kind != DaggerfallUiActionKind.DungeonTextAnswer || action.Text is not { } answer)
            throw new ArgumentException("Dungeon text input action is invalid.", nameof(action));
        DaggerfallDungeonTextActionResult submitted = dungeonText.Answer(actionId, revision, answer);
        if (submitted.Outcome is DaggerfallDungeonTextOutcome.StaleSubmission or DaggerfallDungeonTextOutcome.NoPendingAnswer)
        {
            presentation.SetOutcome(submitted.Diagnostic ?? "Dungeon text answer is no longer pending.");
            return;
        }
        if (submitted.Outcome == DaggerfallDungeonTextOutcome.AcceptedAnswer)
        {
            continueAcceptedAnswer(actionId);
            presentation.SetOutcome("Answer accepted.");
        }
        else presentation.SetOutcome("Answer rejected.");
    }

    internal void ApplyNotebook(DaggerfallPlayerUiAction action)
    {
        DaggerfallNotebookActionResult result = action.Kind switch
        {
            DaggerfallUiActionKind.NotebookPage => notebook.TurnPage(action.Revision, action.Page!.Value),
            DaggerfallUiActionKind.NotebookAdd => notebook.AddNote(action.Revision, action.Text!),
            DaggerfallUiActionKind.NotebookEdit => notebook.EditNote(action.Revision, action.Note!, action.Text!),
            DaggerfallUiActionKind.NotebookRemove => notebook.RemoveNote(action.Revision, action.Note!),
            DaggerfallUiActionKind.NotebookMove => notebook.MoveNote(action.Revision, action.Note!, action.Destination!.Value),
            _ => new(false, "That notebook action is not available."),
        };
        presentation.SetOutcome(result.Message);
    }
}
