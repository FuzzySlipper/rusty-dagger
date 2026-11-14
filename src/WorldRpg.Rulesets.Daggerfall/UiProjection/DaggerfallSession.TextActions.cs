using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The text modal's actions: each reads the text's action and revision and asks Text's owner, a building's greeting
/// closing lets its entry go ahead, and a riddle answered right goes on along the dungeon's graph.
/// </summary>
internal sealed partial class DaggerfallSession
{
    private void ApplyDungeonTextInput(DaggerfallPlayerUiAction action)
    {
        if (!ulong.TryParse(action.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out ulong revision)
            || action.Item is not { Length: > 0 } actionId)
        {
            Presentation.SetOutcome("Dungeon text input has no current action identity or revision.");
            return;
        }

        if (!DungeonText.IsUp(actionId, revision))                                                  // Text
        {
            Presentation.SetOutcome("Dungeon text input belongs to an older prompt.");
            return;
        }

        if (action.Kind == DaggerfallUiActionKind.DungeonTextClose)
        {
            if (DungeonText.Projection!.Kind == DaggerfallDungeonTextActionKind.BuildingGreeting)
            { ContinueBuildingEntry(); return; }                                                   // the held entry, a Session list
            Presentation.SetOutcome(DungeonText.Close(actionId, revision) ?? "Dungeon text closed.");   // Text
            return;
        }

        if (action.Kind != DaggerfallUiActionKind.DungeonTextAnswer || action.Text is not { } answer)
            throw new ArgumentException("Dungeon text input action is invalid.", nameof(action));
        DaggerfallDungeonTextActionResult submitted = DungeonText.Answer(actionId, revision, answer);   // Text
        if (submitted.Outcome is DaggerfallDungeonTextOutcome.StaleSubmission or DaggerfallDungeonTextOutcome.NoPendingAnswer)
        {
            Presentation.SetOutcome(submitted.Diagnostic ?? "Dungeon text answer is no longer pending.");
            return;
        }
        if (submitted.Outcome == DaggerfallDungeonTextOutcome.AcceptedAnswer)
        {
            DungeonActions.ContinueAcceptedAnswer(actionId);                                       // Interiors
            Presentation.SetOutcome("Answer accepted.");
        }
        else Presentation.SetOutcome("Answer rejected.");
    }

}
