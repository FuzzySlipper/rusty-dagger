using System.Text;
using Rusty.Engine;

namespace WorldRpg.Kit;

/// <summary>
/// Renders an Engine failure with the diagnostics its call actually carried.
/// </summary>
/// <remarks>
/// An <see cref="EngineCallException"/>'s message names only the service, operation and status, so a
/// failure reported as "returned status 0" says nothing about why. The Engine puts the reason in
/// <see cref="EngineCallException.Diagnostics"/>; this walks the exception tree — aggregates included —
/// and states those reasons beside the message, which is what makes a refused call diagnosable.
/// </remarks>
public static class EngineFailureText
{
    public static string Describe(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        StringBuilder text = new();
        Append(text, failure, depth: 0);
        return text.ToString().TrimEnd();
    }

    private static void Append(StringBuilder text, Exception failure, int depth)
    {
        string indent = new(' ', depth * 2);
        text.Append(indent).Append(failure.GetType().Name).Append(": ").AppendLine(failure.Message);
        if (failure is EngineCallException engine)
        {
            foreach (EngineDiagnostic diagnostic in engine.Diagnostics.Span)
            {
                text.Append(indent).Append("  engine ").Append(diagnostic.Code).Append(": ").AppendLine(diagnostic.Message);
            }
        }

        if (failure is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions) Append(text, inner, depth + 1);
            return;
        }

        if (failure.InnerException is { } nested) Append(text, nested, depth + 1);
    }
}
