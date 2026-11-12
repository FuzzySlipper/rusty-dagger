using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How far the player is through the tutorial: the page reached, whether it is up, and, while it is not, the seconds of
/// play left before it appears.
/// </summary>
internal sealed record DaggerfallTutorialProgress([property: JsonRequired] int Page, [property: JsonRequired] bool Shown,
    [property: JsonRequired] double SecondsToNext)
{
    internal void Validate(int pages)
    {
        if (Page < 0 || Page >= pages) throw new ArgumentOutOfRangeException(nameof(Page), Page, $"The saved tutorial page is not one of its {pages} pages.");
        if (!double.IsFinite(SecondsToNext) || SecondsToNext < 0) throw new ArgumentOutOfRangeException(nameof(SecondsToNext), SecondsToNext, "The saved tutorial wait must be finite and not negative.");
        if (Shown && SecondsToNext != 0) throw new ArgumentException("A shown tutorial page has no wait left.");
    }
}

/// <summary>The tutorial page that is up, for the HUD: which page of how many, and the page itself.</summary>
internal sealed record DaggerfallTutorialView(int Index, int Count, DaggerfallHelpPage Page);

/// <summary>
/// The tutorial the player takes from Help, never from a new game. Its pages come one at a time: a page that is up
/// holds the world as any screen does, and once the player reads on, the next waits its seconds of play before it
/// appears. Ending it, or reading past its last page, ends it; Help can start it again.
/// </summary>
internal sealed class DaggerfallTutorial
{
    private readonly DaggerfallTutorialDefinition? _definition;

    internal DaggerfallTutorial(DaggerfallTutorialDefinition? definition, DaggerfallTutorialProgress? saved)
    {
        _definition = definition;
        if (saved is null) return;
        if (definition is null) throw new InvalidOperationException("The save is part way through a tutorial this game has none of.");
        saved.Validate(definition.Pages.Count);
        Progress = saved;
    }

    internal DaggerfallTutorialProgress? Progress { get; private set; }

    /// <summary>Whether a tutorial page is up, holding the world.</summary>
    internal bool Shown => Progress is { Shown: true };

    /// <summary>The line Help offers the tutorial with, or null when the game has none.</summary>
    internal string? Offer => _definition is { Pages.Count: > 0 } definition ? definition.Offer : null;

    internal bool Running => Progress is not null;

    internal DaggerfallTutorialView? View => Progress is { Shown: true } progress && _definition is { } definition
        ? new(progress.Page, definition.Pages.Count, definition.Pages[progress.Page].Page) : null;

    /// <summary>Starts the tutorial at its first page; a tutorial already under way goes on where it is.</summary>
    internal bool Start()
    {
        if (Offer is null || Running) return false;
        Progress = new(0, true, 0);
        return true;
    }

    /// <summary>Reads on from the page that is up: the next page waits its seconds of play, and the last ends the tutorial.</summary>
    internal bool Continue(int page)
    {
        // A press answers the page it was made on; a stale one does not skip the page now up.
        if (Progress is not { Shown: true } progress || progress.Page != page || _definition is not { } definition) return false;
        int next = page + 1;
        Progress = next >= definition.Pages.Count ? null : new(next, false, definition.Pages[next].AfterSeconds);
        Age(0);
        return true;
    }

    internal bool End()
    {
        if (!Running) return false;
        Progress = null;
        return true;
    }

    /// <summary>Counts seconds of play down to the next page, which comes up once they have passed.</summary>
    internal void Age(double seconds)
    {
        if (Progress is not { Shown: false } progress) return;
        double left = Math.Max(0, progress.SecondsToNext - seconds);
        Progress = left == 0 ? progress with { Shown = true, SecondsToNext = 0 } : progress with { SecondsToNext = left };
    }
}
