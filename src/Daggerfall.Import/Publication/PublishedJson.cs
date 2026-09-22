using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daggerfall.Import.Publication;

/// <summary>
/// The canonical JSON dialect for data this repository publishes: camelCase, indented, strict
/// numbers, and enum-like values written by name in camelCase.
/// </summary>
/// <remarks>
/// One owner rather than a copy per producer. The pack, the media sidecar and the normalized
/// documents are all read by people and parsed by consumers, and an enum stored as an ordinal would
/// tie published data to the order of an enum in code. When three producers each kept their own
/// options, one future converter or encoder change would have forked the published dialect without
/// anything failing.
/// </remarks>
public static class PublishedJson
{
    /// <summary>
    /// The bulk spatial dialect: the writing dialect without indentation, for payload files whose
    /// record counts make whitespace the dominant byte. Nothing else changes: names, numbers and
    /// enums serialize identically, so a compact file parses to the same document.
    /// </summary>
    public static readonly JsonSerializerOptions SectionCompact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The options every published section is written with.</summary>
    public static readonly JsonSerializerOptions Section = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// The options a published section is read back with: the writing dialect, refusing a member no type
    /// declares rather than ignoring it.
    /// </summary>
    /// <remarks>
    /// A reader that ignores what it does not recognize can answer a different question than the section
    /// states — a renamed list reads as an empty one and a section folded from it says the opposite of what
    /// the pack carries. Reading is therefore strict, while the writing options stay permissive so a
    /// section can still be written by a build that is newer than its reader.
    /// </remarks>
    public static readonly JsonSerializerOptions SectionRead = new(Section)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}
