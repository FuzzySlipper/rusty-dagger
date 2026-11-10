using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>One stretch of a help paragraph: words, or the control a player action is bound to, named by the action.</summary>
internal sealed record DaggerfallHelpSegment(string? Text, string? Action);

/// <summary>One page of the game's help: its title and its paragraphs, each the run of segments it reads as.</summary>
internal sealed record DaggerfallHelpPage(string Title, IReadOnlyList<IReadOnlyList<DaggerfallHelpSegment>> Paragraphs);

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
        HashSet<string> actions = [.. DaggerfallControlSettings.Catalog.Select(action => action.Id)];
        List<DaggerfallHelpPage> pages = [];
        foreach (JsonElement page in Array(Object(section, "help", diagnostics), "pages", diagnostics))
        {
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
            pages.Add(new(title, paragraphs));
        }
        return pages;
    }

    [GeneratedRegex(@"\{([a-z][a-z0-9.-]*)\}")]
    private static partial Regex HelpControl();
}
