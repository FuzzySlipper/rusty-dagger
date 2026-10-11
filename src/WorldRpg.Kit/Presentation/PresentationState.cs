namespace WorldRpg.Kit.Presentation;

/// <summary>A bounded set of transient messages, aged by admitted world time.</summary>
/// <remarks>The product chooses the visible capacity, initial hold and subsequent removal interval.</remarks>
public sealed class PresentationState
{
    private readonly List<string> _messages = [];
    private double _remaining;

    public PresentationState(string initialOutcome, double lifetimeSeconds, int maximumMessages = 1,
        double? subsequentMessageLifetimeSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(initialOutcome);
        LifetimeSeconds = double.IsFinite(lifetimeSeconds) && lifetimeSeconds > 0d
            ? lifetimeSeconds : throw new ArgumentOutOfRangeException(nameof(lifetimeSeconds));
        MaximumMessages = maximumMessages > 0 ? maximumMessages : throw new ArgumentOutOfRangeException(nameof(maximumMessages));
        SubsequentMessageLifetimeSeconds = subsequentMessageLifetimeSeconds ?? lifetimeSeconds;
        if (!double.IsFinite(SubsequentMessageLifetimeSeconds) || SubsequentMessageLifetimeSeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(subsequentMessageLifetimeSeconds));
        SetOutcome(initialOutcome);
    }

    public double LifetimeSeconds { get; }
    public int MaximumMessages { get; }
    public double SubsequentMessageLifetimeSeconds { get; }
    public IReadOnlyList<string> Messages => _messages;
    /// <summary>The latest visible outcome, for a focused interaction or entry screen.</summary>
    public string LastOutcome => _messages.Count == 0 ? string.Empty : _messages[^1];

    /// <summary>Adds a line, holding the stack for the product's stated lifetime. Empty text clears it.</summary>
    public void SetOutcome(string outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Length == 0) { _messages.Clear(); _remaining = 0; return; }
        _messages.Add(outcome);
        if (_messages.Count > MaximumMessages) _messages.RemoveAt(0);
        _remaining = Math.Max(_remaining, LifetimeSeconds);
    }

    /// <summary>Extends the newest outcome without publishing its first clause again as another line.</summary>
    public void AppendOutcome(string clause)
    {
        ArgumentNullException.ThrowIfNull(clause);
        if (_messages.Count == 0) { SetOutcome(clause); return; }
        _messages[^1] += $"; {clause}";
        _remaining = Math.Max(_remaining, LifetimeSeconds);
    }

    /// <summary>Expires the oldest line first, then advances through the remaining timed lines.</summary>
    public void Advance(double deltaSeconds)
    {
        while (_messages.Count > 0)
        {
            if (deltaSeconds < _remaining) { _remaining -= deltaSeconds; return; }
            deltaSeconds -= _remaining;
            _messages.RemoveAt(0);
            _remaining = _messages.Count > 0 ? SubsequentMessageLifetimeSeconds : 0;
        }
    }
}
