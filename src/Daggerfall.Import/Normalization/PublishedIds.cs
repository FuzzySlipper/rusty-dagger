using System.Text;

namespace Daggerfall.Import.Normalization;

/// <summary>The two ways a published identity or path segment is derived from a source name.</summary>
public static class PublishedIds
{
    /// <summary>
    /// A lowercase identity segment: every character that is not an ASCII letter or digit becomes a dash, the
    /// ends are trimmed of dashes, and a name with nothing left is <c>source</c>.
    /// </summary>
    public static string Slug(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        StringBuilder result = new(value.Length);
        foreach (char character in value)
        {
            result.Append(char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        string slug = result.ToString().Trim('-');
        return slug.Length == 0 ? "source" : slug;
    }

    /// <summary>A media identity as a file-name segment: its dots become dashes and its case is kept.</summary>
    public static string FileSlug(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace('.', '-');
    }
}
