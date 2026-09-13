namespace WorldRpg.Kit.Presentation;

/// <summary>
/// The one line a product shows about what just happened, and how long it stays.
/// </summary>
/// <remarks>
/// A message is transient: it is published for a stated span of admitted world time and then
/// clears, because a line that never expires stops being a report of what just happened and
/// becomes permanent scenery. The span is advanced only by admitted updates, so a product that
/// holds its world still also holds the message, and there is no second clock here.
/// </remarks>
public sealed class PresentationState(string initialOutcome)
{
    /// <summary>Admitted seconds a message stays published before the line clears.</summary>
    public const double LifetimeSeconds = 6d;

    // An initial message is a message like any other, so it starts its lifetime with the session
    // rather than sitting on screen until something replaces it.
    private double _remaining = initialOutcome.Length == 0 ? 0d : LifetimeSeconds;

    /// <summary>The message currently published, or empty when nothing was reported.</summary>
    public string LastOutcome { get; private set; } = initialOutcome;

    /// <summary>Publishes a message and restarts its lifetime.</summary>
    public void SetOutcome(string outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        LastOutcome = outcome;
        _remaining = outcome.Length == 0 ? 0d : LifetimeSeconds;
    }

    /// <summary>Adds a clause to the published message, or starts one when the line is empty.</summary>
    public void AppendOutcome(string clause)
    {
        ArgumentNullException.ThrowIfNull(clause);
        SetOutcome(LastOutcome.Length == 0 ? clause : $"{LastOutcome}; {clause}");
    }

    /// <summary>Advances the published message's lifetime by one admitted span of world time.</summary>
    public void Advance(double deltaSeconds)
    {
        if (_remaining <= 0d) return;
        _remaining -= deltaSeconds;
        if (_remaining > 0d) return;
        _remaining = 0d;
        LastOutcome = string.Empty;
    }
}
