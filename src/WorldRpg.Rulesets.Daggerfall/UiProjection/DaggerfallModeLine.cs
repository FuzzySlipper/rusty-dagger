using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The line a mode change says. Returning to play clears the line a mode set for itself, but not a result reported
/// while that mode was up: a journey or rest that ends by closing the menu keeps its line for the player. A screen
/// that says what it is itself adds no line, and leaves the recent messages where they are.
/// </summary>
internal sealed class DaggerfallModeLine(PresentationState presentation)
{
    // The line the last mode change set for its mode, so returning to play can tell it from a result.
    private string? _line = string.Empty;

    internal void Say(ProductMode mode, string line)
    {
        if (line.Length == 0 && mode == ProductMode.Modal) _line = null;
        else
        {
            if (mode != ProductMode.Playing || presentation.LastOutcome == _line) presentation.SetOutcome(line);
            _line = presentation.LastOutcome;
        }
    }
}
