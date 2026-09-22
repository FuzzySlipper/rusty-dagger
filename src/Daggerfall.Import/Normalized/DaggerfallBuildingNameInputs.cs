using System.Text;
using System.Text.RegularExpressions;

namespace Daggerfall.Import.Normalized;

/// <summary>The source that carries the donor's region-to-name-bank mapping.</summary>
public sealed record DaggerfallBuildingNameSource(string Path, long ByteLength, int Regions)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalPath(Path, nameof(Path));
        if (ByteLength <= 0 || Regions != 62)
        {
            throw new InvalidOperationException($"Building-name source '{Path}' must retain bytes and exactly 62 classic regions.");
        }
    }
}

/// <summary>The exact classic region mapping used when a building-name fragment expands %ef.</summary>
public sealed record DaggerfallBuildingNameInputs(DaggerfallBuildingNameSource Source, IReadOnlyList<int> RegionNameBanks)
{
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
        DaggerfallBuildingNameInputs published = new(new DaggerfallBuildingNameSource(label, bytes.LongLength, banks.Length), banks);
        published.Validate();
        return published;
    }
}
