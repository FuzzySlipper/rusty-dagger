using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>Applies one revision-guarded reader or notebook change through the single durable owner.</summary>
    private void ApplyNotebookAction(DaggerfallPlayerUiAction action)
    {
        DaggerfallNotebookActionResult result = _notebook.Apply(action);
        Presentation.SetOutcome(result.Message);
    }
}
