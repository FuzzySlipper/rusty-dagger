using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private DaggerfallTutorial _tutorial = null!;

    /// <summary>The tutorial Help offers, and the page of it that is up.</summary>
    internal DaggerfallTutorial Tutorial => _tutorial;

    private void ApplyTutorialAction(DaggerfallPlayerUiAction action)
    {
        switch (action.Kind)
        {
            case DaggerfallUiActionKind.TutorialStart: _tutorial.Start(); break;
            case DaggerfallUiActionKind.TutorialContinue: _tutorial.Continue(action.Page!.Value); break;
            case DaggerfallUiActionKind.TutorialEnd: _tutorial.End(); break;
        }
    }
}
