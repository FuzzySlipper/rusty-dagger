namespace RustyDagger.Game.Modules.Presentation;

internal sealed class PresentationState
{
    internal string LastOutcome { get; private set; } = "Ready";
    internal void SetOutcome(string outcome) => LastOutcome = outcome;
}
