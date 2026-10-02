using System.Text;
using System.Text.RegularExpressions;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>The exact classic region mapping used when a building-name fragment expands %ef.</summary>
public sealed record DaggerfallBuildingNameInputs(PublishedSource Source, IReadOnlyList<int> RegionNameBanks)
{
    public IReadOnlyList<string> RegionNames { get; init; } = [];
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentNullException.ThrowIfNull(RegionNameBanks);
        Source.Validate();
        if (RegionNameBanks.Count != 62 || RegionNameBanks.Any(bank => bank is < 0 or > 1))
        {
            throw new InvalidOperationException("Building-name inputs must map each of the 62 classic regions to the Breton (0) or Redguard (1) name bank.");
        }
    }
}

/// <summary>Extracts the exact FALL.EXE-derived regionRaces array from the consulted MapsFile donor.</summary>
public static partial class DaggerfallBuildingNameInputsBuilder
{
    [GeneratedRegex(@"regionNames\s*=\s*\{(?<values>.*?)\};", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex RegionNames();

    [GeneratedRegex("\"(?<name>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedName();
    [GeneratedRegex(@"regionRaces\s*=\s*\{(?<values>.*?)\};", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex RegionRaces();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Number();

    public static DaggerfallBuildingNameInputs Build(byte[] bytes, string label)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        string source;
        try
        {
            source = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException($"Building-name source '{label}' is not valid UTF-8.", exception);
        }

        Match match = RegionRaces().Match(source);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Building-name source '{label}' carries no regionRaces array.");
        }

        int[] banks = [.. Number().Matches(match.Groups["values"].Value).Select(value => int.Parse(value.Value, System.Globalization.CultureInfo.InvariantCulture))];
        Match names = RegionNames().Match(source);
        string[] regionNames = names.Success ? [.. QuotedName().Matches(names.Groups["values"].Value).Select(value => value.Groups["name"].Value)] : [];
        if (names.Success && regionNames.Length != banks.Length)
            throw new InvalidOperationException($"Building-name source '{label}' carries {regionNames.Length} names for {banks.Length} region banks.");
        DaggerfallBuildingNameInputs published = new(PublishedSource.Of(label, bytes), banks) { RegionNames = regionNames };
        published.Validate();
        return published;
    }
}
