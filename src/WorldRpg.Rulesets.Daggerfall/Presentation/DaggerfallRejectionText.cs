namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The sentence a refused player action shows. An argument exception's message carries the .NET
/// parameter name and, when out of range, the offending value after it; the player reads only the
/// ruleset's own sentence.
/// </summary>
internal static class DaggerfallRejectionText
{
    internal static string For(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        string message = error.Message;
        if (error is ArgumentOutOfRangeException { ActualValue: not null })
        {
            int actual = message.LastIndexOf(Environment.NewLine, StringComparison.Ordinal);
            if (actual >= 0) message = message[..actual];
        }
        if (error is ArgumentException { ParamName: { } parameter }
            && message.EndsWith($" (Parameter '{parameter}')", StringComparison.Ordinal))
            message = message[..^$" (Parameter '{parameter}')".Length];
        return message;
    }
}
