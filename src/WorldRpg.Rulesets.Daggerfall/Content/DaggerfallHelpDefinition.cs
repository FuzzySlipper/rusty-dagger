using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>One stretch of a help paragraph: words, or the control a player action is bound to, named by the action.</summary>
internal sealed record DaggerfallHelpSegment(string? Text, string? Action);

/// <summary>One page of the game's help: its title and its paragraphs, each the run of segments it reads as.</summary>
internal sealed record DaggerfallHelpPage(string Title, IReadOnlyList<IReadOnlyList<DaggerfallHelpSegment>> Paragraphs);

/// <summary>One page of the tutorial, and the seconds of play after the page before it that it waits to appear.</summary>
internal sealed record DaggerfallTutorialPage(DaggerfallHelpPage Page, double AfterSeconds);

/// <summary>The tutorial Help offers: the line that offers it and its pages, shown one at a time as the player plays.</summary>
internal sealed record DaggerfallTutorialDefinition(string Offer, IReadOnlyList<DaggerfallTutorialPage> Pages);

internal static partial class DaggerfallBaseContent
{
    /// <summary>
    /// Reads the authored help pages. A paragraph names a control by its action in braces (<c>{inventory}</c>), so the
    /// help shows the key or button the player has bound rather than a default; an action the controls catalog does
    /// not have is reported, since the page would name a control that does not exist.
    /// </summary>
    internal static IReadOnlyList<DaggerfallHelpPage> ReadHelp(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("help", out JsonElement section)) return [];
        return [.. Array(Object(section, "help", diagnostics), "pages", diagnostics).Select(page => ReadHelpPage(page, diagnostics))];
    }

    /// <summary>
    /// Reads the tutorial Help offers, or none when the help has no tutorial. Its pages read as help pages do; the first
    /// appears as the tutorial starts, and each later one after its seconds of play.
    /// </summary>
    internal static DaggerfallTutorialDefinition? ReadTutorial(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("help", out JsonElement section) || !section.TryGetProperty("tutorial", out JsonElement tutorial)) return null;
        string offer = Text(tutorial, "offer", diagnostics);
        List<DaggerfallTutorialPage> pages = [];
        foreach (JsonElement page in Array(tutorial, "pages", diagnostics))
        {
            double after = page.TryGetProperty("afterSeconds", out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : double.NaN;
            if (!double.IsFinite(after) || after < 0) { diagnostics.Add("A tutorial page needs afterSeconds, a finite number of seconds that is not negative."); after = 0; }
            pages.Add(new(ReadHelpPage(page, diagnostics), after));
        }
        if (offer.Length == 0 || pages.Count == 0) diagnostics.Add("The tutorial needs an offer and at least one page.");
        return new(offer, pages);
    }

    /// <summary>
    /// Reads one help or tutorial page. A paragraph names a control by its action in braces (<c>{inventory}</c>), so the
    /// page shows the key or button the player has bound rather than a default; an action the controls catalog does
    /// not have is reported, since the page would name a control that does not exist.
    /// </summary>
    private static DaggerfallHelpPage ReadHelpPage(JsonElement page, DaggerfallContentDiagnostics diagnostics)
    {
        HashSet<string> actions = [.. DaggerfallControlSettings.Catalog.Select(action => action.Id)];
        string title = Text(page, "title", diagnostics);
        List<IReadOnlyList<DaggerfallHelpSegment>> paragraphs = [];
        foreach (JsonElement paragraph in Array(page, "paragraphs", diagnostics))
        {
            string text = paragraph.ValueKind == JsonValueKind.String ? paragraph.GetString()! : string.Empty;
            if (string.IsNullOrWhiteSpace(text)) { diagnostics.Add($"Help page '{title}' has a paragraph that is not text."); continue; }
            List<DaggerfallHelpSegment> segments = [];
            int at = 0;
            foreach (Match control in HelpControl().Matches(text))
            {
                if (control.Index > at) segments.Add(new(text[at..control.Index], null));
                string action = control.Groups[1].Value;
                if (!actions.Contains(action)) diagnostics.Add($"Help page '{title}' names the control '{action}', which no action has.");
                segments.Add(new(null, action));
                at = control.Index + control.Length;
            }
            if (at < text.Length) segments.Add(new(text[at..], null));
            paragraphs.Add(segments);
        }
        if (title.Length == 0 || paragraphs.Count == 0) diagnostics.Add("A help page needs a title and at least one paragraph.");
        return new(title, paragraphs);
    }

    [GeneratedRegex(@"\{([a-z][a-z0-9.-]*)\}")]
    private static partial Regex HelpControl();
}
