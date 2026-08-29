namespace WorldRpg.Kit.Presentation;

public sealed class PresentationState(string initialOutcome)
{
    public string LastOutcome { get; private set; } = initialOutcome;
    public void SetOutcome(string outcome) => LastOutcome = outcome;
}
