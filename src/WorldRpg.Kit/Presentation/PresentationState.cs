namespace WorldRpg.Kit.Presentation;

public sealed class PresentationState
{
    public string LastOutcome { get; private set; } = "Ready";
    public void SetOutcome(string outcome) => LastOutcome = outcome;
}
