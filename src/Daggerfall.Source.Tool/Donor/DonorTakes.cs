using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Source.Tool.Donor;

/// <summary>One of our tables and what a Daggerfall Unity checkout says it should hold, as JSON to compare.</summary>
/// <param name="Label">Where our table lives.</param>
/// <param name="Ours">Our table, read from a repository root.</param>
/// <param name="Donor">The same table read from a Daggerfall Unity checkout's root.</param>
internal sealed record DonorTake(string Label, Func<string, JsonNode?> Ours, Func<string, JsonNode?> Donor);

/// <summary>
/// Every table we took from Daggerfall Unity, each with the donor files and parser it was taken with. The drift check
/// (<c>donor-drift</c>) is the only reader of the donor.
/// </summary>
internal static class DonorTakes
{
    public static IReadOnlyList<DonorTake> All { get; } =
    [
        Section("buildingNames", donor => DonorBuildingNames.Build(File.ReadAllBytes(Path.Combine(donor, "Assets/Scripts/API/MapsFile.cs")),
            PublishedSourcePath.Donor("Assets/Scripts/API/MapsFile.cs"))),
        Section("internalText", donor => DonorInternalText.Build(File.ReadAllBytes(Path.Combine(donor, $"{TextDirectory}/Internal_Strings.csv")),
            PublishedSourcePath.Donor($"{TextDirectory}/Internal_Strings.csv"), "en")),
    ];

    /// <summary>The base pack's payload, where the tables the import never needs are sections the ruleset reads.</summary>
    internal const string BasePayload = "content/worldrpg/packs/daggerfall.base/payload.json";

    private const string TextDirectory = "Assets/StreamingAssets/Text/Master Localization CSV Files";
    /// <summary>A section of the base pack's payload.</summary>
    private static DonorTake Section<T>(string section, Func<string, T> fromDonor) => new(
        $"{BasePayload} section {section}",
        repository => JsonNode.Parse(File.ReadAllBytes(Path.Combine(repository, BasePayload)))?[section],
        donor => JsonSerializer.SerializeToNode(fromDonor(donor), PublishedJson.Section));
}
