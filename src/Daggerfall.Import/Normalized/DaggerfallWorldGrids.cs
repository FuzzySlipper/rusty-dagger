using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>How a climate cell value is accounted for.</summary>
public enum DaggerfallClimateDisposition
{
    /// <summary>The donor's climate table names the value.</summary>
    Named,
    /// <summary>No donor table names the value; the cell keeps its byte.</summary>
    Unresolved,
}

/// <summary>How a politic cell value is accounted for.</summary>
public enum DaggerfallPoliticDisposition
{
    /// <summary>The value names a region: the donor's region index plus 128.</summary>
    Region,
    /// <summary>The value is the ocean every region shares.</summary>
    Ocean,
    /// <summary>The value names no region and is not the ocean; the cell keeps its byte.</summary>
    Unresolved,
}

/// <summary>One run-length encoded grid row, in column order.</summary>
/// <param name="Y">The row's index.</param>
/// <param name="Runs">The row's runs: repeat counts with the byte each repeats, tiling the row exactly.</param>
public sealed record DaggerfallGridRow(int Y, IReadOnlyList<DaggerfallGridRun> Runs)
{
    public void Validate(int width)
    {
        if (Y < 0 || Y >= PakMap.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(Y), Y, "A published grid row names a row the grid does not declare.");
        }

        if (Runs.Count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Runs), Runs.Count, $"Grid row {Y} carries no runs.");
        }

        int columns = 0;
        foreach (DaggerfallGridRun run in Runs)
        {
            run.Validate();
            columns += run.Count;
        }

        if (columns != width)
        {
            throw new InvalidOperationException($"Grid row {Y} tiles {columns} columns for a {width}-column grid.");
        }
    }
}

/// <summary>One run: a repeat count with the byte it repeats.</summary>
/// <param name="Count">How many columns the run covers, at least one.</param>
/// <param name="Value">The source byte the run repeats.</param>
public sealed record DaggerfallGridRun(int Count, int Value)
{
    public void Validate()
    {
        if (Count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Count), Count, "A published grid run covers no columns.");
        }

        if (Value is < 0 or > 0xff)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), Value, "A published grid run repeats no source byte.");
        }
    }
}

/// <summary>The source a normalized grid was read from.</summary>
/// <param name="RecordId">The documented inventory row id.</param>
/// <param name="Path">The logical path of the source.</param>
/// <param name="ByteLength">The source file's byte length.</param>
public sealed record DaggerfallGridSource(string RecordId, string Path, long ByteLength)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(RecordId, nameof(RecordId));
        NormalizedImportDocument.RequireLogicalPath(Path, nameof(Path));
        if (ByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ByteLength), ByteLength, $"Grid source '{Path}' states no bytes.");
        }
    }
}

/// <summary>One normalized climate grid: every cell the source states, sentinel column kept.</summary>
/// <param name="Source">The source the grid was read from.</param>
/// <param name="Rows">The rows in order, each tiling the full width including the sentinel column.</param>
/// <param name="Values">The distinct cell values with the climate each names, in first-appearance order.</param>
public sealed record DaggerfallClimateGrid(DaggerfallGridSource Source, IReadOnlyList<DaggerfallGridRow> Rows, IReadOnlyList<DaggerfallClimateValue> Values)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        Source.Validate();
        if (Rows.Count != PakMap.Height)
        {
            throw new InvalidOperationException($"Climate grid carries {Rows.Count} rows for a {PakMap.Height}-row grid.");
        }

        NormalizedImportDocument.ValidateUnique(Rows, row => row.Y.ToString(), "climate grid rows");
        foreach (DaggerfallGridRow row in Rows)
        {
            row.Validate(PakMap.Width);
        }

        if (Values.Count == 0)
        {
            throw new InvalidOperationException("Climate grid names no cell values.");
        }

        NormalizedImportDocument.ValidateUnique(Values, value => value.Value.ToString(), "climate values");
        foreach (DaggerfallClimateValue value in Values)
        {
            value.Validate();
        }
    }
}

/// <summary>One distinct climate value with the climate it names.</summary>
/// <param name="Value">The source byte.</param>
/// <param name="Name">The donor's climate name, empty when no table names it.</param>
/// <param name="Disposition">Whether the donor's table names the value.</param>
public sealed record DaggerfallClimateValue(int Value, string Name, DaggerfallClimateDisposition Disposition)
{
    public void Validate()
    {
        if (Value is < 0 or > 0xff)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), Value, "A published climate value is no source byte.");
        }

        if (!Enum.IsDefined(Disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(Disposition), Disposition, "A published climate value names a disposition the contract does not declare.");
        }

        if (Disposition == DaggerfallClimateDisposition.Named && string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException($"Climate value {Value} is named with no name.", nameof(Name));
        }

        if (Disposition == DaggerfallClimateDisposition.Unresolved && Name.Length != 0)
        {
            throw new ArgumentException($"Climate value {Value} is unresolved with the name '{Name}'.", nameof(Name));
        }
    }
}

/// <summary>One normalized politic grid: every cell the source states, sentinel column kept.</summary>
/// <param name="Source">The source the grid was read from.</param>
/// <param name="Rows">The rows in order, each tiling the full width including the sentinel column.</param>
/// <param name="Values">The distinct cell values with the region each names, in first-appearance order.</param>
public sealed record DaggerfallPoliticGrid(DaggerfallGridSource Source, IReadOnlyList<DaggerfallGridRow> Rows, IReadOnlyList<DaggerfallPoliticValue> Values)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        Source.Validate();
        if (Rows.Count != PakMap.Height)
        {
            throw new InvalidOperationException($"Politic grid carries {Rows.Count} rows for a {PakMap.Height}-row grid.");
        }

        NormalizedImportDocument.ValidateUnique(Rows, row => row.Y.ToString(), "politic grid rows");
        foreach (DaggerfallGridRow row in Rows)
        {
            row.Validate(PakMap.Width);
        }

        if (Values.Count == 0)
        {
            throw new InvalidOperationException("Politic grid names no cell values.");
        }

        NormalizedImportDocument.ValidateUnique(Values, value => value.Value.ToString(), "politic values");
        foreach (DaggerfallPoliticValue value in Values)
        {
            value.Validate();
        }
    }
}

/// <summary>One distinct politic value with the region it names.</summary>
/// <param name="Value">The source byte.</param>
/// <param name="Region">The zero-based region index, or -1 when the value names no region.</param>
/// <param name="Disposition">Whether the value names a region, the ocean, or nothing.</param>
public sealed record DaggerfallPoliticValue(int Value, int Region, DaggerfallPoliticDisposition Disposition)
{
    public void Validate()
    {
        if (Value is < 0 or > 0xff)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), Value, "A published politic value is no source byte.");
        }

        if (!Enum.IsDefined(Disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(Disposition), Disposition, "A published politic value names a disposition the contract does not declare.");
        }

        if (Disposition == DaggerfallPoliticDisposition.Region && Region < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Region), Region, $"Politic value {Value} names a region with no index.");
        }

        if (Disposition != DaggerfallPoliticDisposition.Region && Region != -1)
        {
            throw new ArgumentOutOfRangeException(nameof(Region), Region, $"Politic value {Value} names no region with index {Region}.");
        }
    }
}

/// <summary>
/// Builds the normalized climate and politic grids from the classic PAK files. Cells keep their
/// source bytes and the sentinel column stays tiled; names and regions are the donor's
/// interpretation of those bytes, and a byte no interpretation covers is published unresolved
/// rather than dropped.
/// </summary>
public static class DaggerfallWorldGridsBuilder
{
    public const string ClimateFamilyId = "CNT-002";
    public const string PoliticFamilyId = "CNT-003";

    /// <summary>The donor's climate names by cell value.</summary>
    public static readonly IReadOnlyDictionary<int, string> ClimateNames = new Dictionary<int, string>
    {
        [223] = "Ocean",
        [224] = "Desert",
        [225] = "Desert2",
        [226] = "Mountain",
        [227] = "Rainforest",
        [228] = "Swamp",
        [229] = "Subtropical",
        [230] = "MountainWoods",
        [231] = "Woodlands",
        [232] = "HauntedWoodlands",
    };

    /// <summary>The ocean cell every region shares.</summary>
    public const int OceanValue = 64;

    /// <summary>The donor's region offset: a politic cell names the region its value minus this.</summary>
    public const int RegionOffset = 128;

    /// <summary>How many regions the corpus publishes; politic cells above this range name none.</summary>
    public const int RegionCount = 62;

    /// <summary>Builds the climate grid and the rows it tiles.</summary>
    public static DaggerfallClimateGrid BuildClimate(ReadOnlySpan<byte> bytes, string label, IReadOnlyList<SourceInventoryRow> inventory)
    {
        string recordId = RequireFile(inventory, ClimateFamilyId, label);
        PakMap map = PakDecoder.Decode(bytes, label);
        return new DaggerfallClimateGrid(
            new DaggerfallGridSource(recordId, label, bytes.Length),
            Rows(map),
            ClimateValues(map));
    }

    /// <summary>Builds the politic grid and the rows it tiles.</summary>
    public static DaggerfallPoliticGrid BuildPolitic(ReadOnlySpan<byte> bytes, string label, IReadOnlyList<SourceInventoryRow> inventory)
    {
        string recordId = RequireFile(inventory, PoliticFamilyId, label);
        PakMap map = PakDecoder.Decode(bytes, label);
        return new DaggerfallPoliticGrid(
            new DaggerfallGridSource(recordId, label, bytes.Length),
            Rows(map),
            PoliticValues(map));
    }

    private static List<DaggerfallGridRow> Rows(PakMap map)
    {
        List<DaggerfallGridRow> rows = [];
        for (int y = 0; y < PakMap.Height; y++)
        {
            List<DaggerfallGridRun> runs = [];
            int x = 0;
            while (x < PakMap.Width)
            {
                byte value = map.Pixels.Span[(y * PakMap.Width) + x];
                int count = 1;
                while (x + count < PakMap.Width && map.Pixels.Span[(y * PakMap.Width) + x + count] == value)
                {
                    count++;
                }

                runs.Add(new DaggerfallGridRun(count, value));
                x += count;
            }

            DaggerfallGridRow row = new(y, runs);
            row.Validate(PakMap.Width);
            rows.Add(row);
        }

        return rows;
    }

    private static List<DaggerfallClimateValue> ClimateValues(PakMap map)
    {
        List<DaggerfallClimateValue> values = [];
        HashSet<byte> seen = [];
        foreach (byte value in map.Pixels.Span)
        {
            if (!seen.Add(value))
            {
                continue;
            }

            bool named = ClimateNames.TryGetValue(value, out string? name);
            values.Add(new DaggerfallClimateValue(
                value,
                named ? name! : string.Empty,
                named ? DaggerfallClimateDisposition.Named : DaggerfallClimateDisposition.Unresolved));
        }

        return values;
    }

    private static List<DaggerfallPoliticValue> PoliticValues(PakMap map)
    {
        List<DaggerfallPoliticValue> values = [];
        HashSet<byte> seen = [];
        foreach (byte value in map.Pixels.Span)
        {
            if (!seen.Add(value))
            {
                continue;
            }

            if (value == OceanValue)
            {
                values.Add(new DaggerfallPoliticValue(value, -1, DaggerfallPoliticDisposition.Ocean));
            }
            else if (value >= RegionOffset && value < RegionOffset + RegionCount)
            {
                values.Add(new DaggerfallPoliticValue(value, value - RegionOffset, DaggerfallPoliticDisposition.Region));
            }
            else
            {
                values.Add(new DaggerfallPoliticValue(value, -1, DaggerfallPoliticDisposition.Unresolved));
            }
        }

        return values;
    }

    private static string RequireFile(IReadOnlyList<SourceInventoryRow> inventory, string familyId, string label)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return inventory.FirstOrDefault(row => row.FamilyId == familyId && StringComparer.Ordinal.Equals(row.PathOrPattern, label))?.Id
            ?? throw new InvalidOperationException($"The documented inventory does not carry '{label}', so the grids cite no provenance.");
    }
}
