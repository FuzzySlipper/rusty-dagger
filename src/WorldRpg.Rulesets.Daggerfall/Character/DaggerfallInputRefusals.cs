namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// A player input that did nothing: which input, whether a rule refused it or it never reached one (dropped), and why.
/// </summary>
internal readonly record struct DaggerfallInputRefusal(string Input, bool Dropped, string Reason)
{
    public override string ToString() => $"{Input} {(Dropped ? "dropped" : "refused")}: {Reason}";
}

/// <summary>
/// The one seam every player input that is refused or dropped reports through, so the developer's input readout tells
/// the truth about why a press did nothing: the held-world gate, the slice an interaction or shortcut takes, the UI's
/// own claim on input, combat's admission, the guard, casting and the weapon toggle. It keeps the latest report for
/// <see cref="HeldSeconds"/> of host time, which runs while the world is held, so a press dropped behind a menu still
/// reads. It decides nothing and is never saved.
/// </summary>
internal sealed class DaggerfallInputRefusals
{
    /// <summary>How long a report stays readable.</summary>
    internal const double HeldSeconds = 5d;

    private double _now;
    private (DaggerfallInputRefusal Refusal, double At)? _latest;

    /// <summary>A rule refused <paramref name="input"/>.</summary>
    internal void Refused(string input, string reason) => _latest = (new(input, false, reason), _now);

    /// <summary><paramref name="input"/> never reached the rule that would have acted on it.</summary>
    internal void Dropped(string input, string reason) => _latest = (new(input, true, reason), _now);

    /// <summary>Host seconds pass, whether or not the world does.</summary>
    internal void Age(double hostSeconds)
    {
        if (double.IsFinite(hostSeconds) && hostSeconds > 0d) _now += hostSeconds;
    }

    /// <summary>A code name as the readout words it: <c>OutOfRange</c> reads "out of range".</summary>
    internal static string Words(string name) =>
        string.Concat(name.Select((letter, index) => index > 0 && char.IsUpper(letter) ? " " + char.ToLowerInvariant(letter) : char.ToLowerInvariant(letter).ToString()));

    /// <summary>The latest report while it is readable, or null.</summary>
    internal DaggerfallInputRefusal? Latest => _latest is { } latest && _now - latest.At < HeldSeconds ? latest.Refusal : null;
}
