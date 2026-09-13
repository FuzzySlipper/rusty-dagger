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
    /// <summary>The options every published section is written with.</summary>
    public static readonly JsonSerializerOptions Section = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
