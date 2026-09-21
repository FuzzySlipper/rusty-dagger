using System.Globalization;

namespace Daggerfall.Import.Arena2;

/// <summary>What family a map art file belongs to.</summary>
public enum MapArtKind
{
    /// <summary>A per-region travel map.</summary>
    RegionMap,
    /// <summary>A world-scale map canvas.</summary>
    WorldMap,
    /// <summary>An automap canvas or overlay.</summary>
    Automap,
    /// <summary>Travel-window chrome: picker, buttons, arrows, popup.</summary>
    TravelChrome,
    /// <summary>The town caption the exterior automap draws.</summary>
    TownCaption,
    /// <summary>A palette a map family reads.</summary>
    Palette,
}

/// <summary>What the enumeration could establish about one map art file.</summary>
public enum MapArtDisposition
{
    /// <summary>The file parsed and its shape is addressable.</summary>
    Decoded,
    /// <summary>The donor names the file unsupported: it carries no image data.</summary>
    Unsupported,
    /// <summary>The file claims a shape that does not parse.</summary>
    Malformed,
    /// <summary>No decoder claims the file's shape; it is retained, not dropped.</summary>
    Unreadable,
    /// <summary>The file is documented and no bytes are supplied for it.</summary>
    NotSupplied,
}

/// <summary>
/// One documented map art file: its family, what binds it, and what the enumeration established.
/// A file no donor reader addresses keeps its bytes and shape with an unresolved binding rather
/// than being dropped, because the corpus supplies it and a future consumer may claim it.
/// </summary>
/// <param name="FileName">The file's name.</param>
/// <param name="Kind">The family the file belongs to.</param>
/// <param name="Regions">The regions the file draws, empty when it draws none.</param>
/// <param name="Binding">The donor call site or the reason no call site was found.</param>
/// <param name="Palette">The palette the donor reads the file with, empty when it reads none.</param>
/// <param name="Width">The decoded width, or zero when the file did not decode.</param>
/// <param name="Height">The decoded height, or zero when the file did not decode.</param>
/// <param name="Records">How many image records the file carries.</param>
/// <param name="Disposition">What the enumeration established.</param>
/// <param name="Note">Why, when the disposition is anything but decoded.</param>
public sealed record MapArtRecord(
    string FileName,
    MapArtKind Kind,
    IReadOnlyList<int> Regions,
    string Binding,
    string Palette,
    int Width,
    int Height,
    int Records,
    MapArtDisposition Disposition,
    string Note);

/// <summary>
/// The map art inventory: every documented FMAP, AMAP, TMAP, TRAV, TOWN and palette file with its
/// region, call-site and palette binding. This records source facts only: it decodes each file far
/// enough to address its shape, publishes no pixels, and keeps map art separate from the world data
/// the maps draw and from the travel UI that will arrange it. Panel placement offsets live with
/// that UI task, not here.
/// </summary>
/// <remarks>
/// The corpus carries files the donor itself refuses by name: its image reader lists the three
/// empty region-map stubs among its unsupported filenames, so their unsupported record here agrees
/// with the donor rather than being a product-only judgement.
/// </remarks>
public sealed class MapArtInventory
{
    /// <summary>The files the donor names unsupported: they carry no image data.</summary>
    public static readonly IReadOnlyList<string> UnsupportedFiles = ["FMAP0I00.IMG", "FMAP0I01.IMG", "FMAP0I16.IMG"];

    private readonly Dictionary<string, MapArtRecord> byName;

    private MapArtInventory(IReadOnlyList<MapArtRecord> records)
    {
        Records = records;
        byName = records.ToDictionary(record => record.FileName, StringComparer.Ordinal);
    }

    /// <summary>Every documented file, in file-name order.</summary>
    public IReadOnlyList<MapArtRecord> Records { get; }

    /// <summary>Resolves one file by name, or reports that the inventory does not document it.</summary>
    public bool TryGet(string fileName, out MapArtRecord? record) => byName.TryGetValue(fileName, out record);

    /// <summary>
    /// Enumerates the documented set against the supplied files: name, path and bytes each.
    /// A documented file with no supplied bytes is retained as not supplied.
    /// </summary>
    public static MapArtInventory Enumerate(IReadOnlyList<(string Name, string Path, byte[] Bytes)> files, string source)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Dictionary<string, (string Path, byte[] Bytes)> supplied = [];
        foreach ((string name, string path, byte[] bytes) in files)
        {
            if (!supplied.TryAdd(name, (path, bytes)))
            {
                throw new InvalidOperationException($"Map art '{name}' is supplied twice.");
            }
        }

        List<MapArtRecord> records = [];
        foreach (DocumentedFile documented in Documented())
        {
            if (!supplied.TryGetValue(documented.Name, out (string Path, byte[] Bytes) suppliedFile))
            {
                records.Add(new MapArtRecord(documented.Name, documented.Kind, documented.Regions, documented.Binding, documented.Palette, 0, 0, 0, MapArtDisposition.NotSupplied, $"No bytes are supplied for '{documented.Name}'."));
                continue;
            }

            records.Add(Read(documented, suppliedFile.Bytes));
        }

        return new MapArtInventory([.. records.OrderBy(record => record.FileName, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Decodes one map art file to its single image: the headered, headerless or record-sequence
    /// shape, in that order. A file with no image shape, or with more than one record, is refused
    /// by name rather than guessed at: the publication renders one image per file.
    /// </summary>
    public static IndexedImg DecodeImage(byte[] bytes, string name)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            return ImgDecoder.Decode(bytes, name);
        }
        catch (Arena2FormatException)
        {
        }

        if (ImgDecoder.TryDecodeHeaderless(bytes, name, out IndexedImg? headerless, out _) && headerless is not null)
        {
            return headerless;
        }

        try
        {
            IReadOnlyList<IndexedImg> sequence = ImgDecoder.DecodeRecordSequence(bytes, name);
            if (sequence.Count == 1)
            {
                return sequence[0];
            }
        }
        catch (Arena2FormatException)
        {
        }

        throw new Arena2FormatException(name, 0, "No single-image decoder claims this file's shape.");
    }

    private static MapArtRecord Read(DocumentedFile documented, byte[] bytes)
    {
        if (UnsupportedFiles.Contains(documented.Name, StringComparer.Ordinal))
        {
            return new MapArtRecord(documented.Name, documented.Kind, documented.Regions, documented.Binding, documented.Palette, 0, 0, 0, MapArtDisposition.Unsupported, "The donor's image reader names this file unsupported: it carries no image data.");
        }

        if (documented.Kind == MapArtKind.Palette)
        {
            try
            {
                Arena2Palette palette = PaletteDecoder.Decode(bytes, documented.Name);
                return new MapArtRecord(documented.Name, documented.Kind, [], documented.Binding, string.Empty, palette.Colors.Length, 1, 1, MapArtDisposition.Decoded, string.Empty);
            }
            catch (Arena2FormatException failure)
            {
                return new MapArtRecord(documented.Name, documented.Kind, [], documented.Binding, string.Empty, 0, 0, 0, MapArtDisposition.Malformed, failure.Message);
            }
        }

        try
        {
            IndexedImg image = DecodeImage(bytes, documented.Name);
            return new MapArtRecord(documented.Name, documented.Kind, documented.Regions, documented.Binding, documented.Palette, image.Width, image.Height, 1, MapArtDisposition.Decoded, string.Empty);
        }
        catch (Arena2FormatException)
        {
            return new MapArtRecord(documented.Name, documented.Kind, documented.Regions, documented.Binding, documented.Palette, 0, 0, 0, MapArtDisposition.Unreadable, "No image decoder claims this file's shape.");
        }
    }

    private sealed record DocumentedFile(string Name, MapArtKind Kind, IReadOnlyList<int> Regions, string Binding, string Palette);

    private static IReadOnlyList<DocumentedFile> Documented()
    {
        List<DocumentedFile> files =
        [
            new("AMAP00I0.IMG", MapArtKind.Automap, [], "DaggerfallAutomapWindow and DaggerfallExteriorAutomapWindow native texture", "ART_PAL.COL"),
            new("AMAP01I0.IMG", MapArtKind.Automap, [], "DaggerfallAutomapWindow three-dimensional grid overlay", "ART_PAL.COL"),
            new("TMAP00I0.IMG", MapArtKind.WorldMap, [], "CreateCharRaceSelect native texture", "MAP.PAL"),
            new("TOWN00I0.IMG", MapArtKind.TownCaption, [], "DaggerfallExteriorAutomapWindow native caption texture", "ART_PAL.COL"),
            new("TRAV00I0.IMG", MapArtKind.WorldMap, [], "No donor call site found", "ART_PAL.COL"),
            new("TRAV01I0.IMG", MapArtKind.TravelChrome, [], "DaggerfallTravelMapWindow location-filter enabled button", "ART_PAL.COL"),
            new("TRAV01I1.IMG", MapArtKind.TravelChrome, [], "DaggerfallTravelMapWindow location-filter disabled button", "ART_PAL.COL"),
            new("TRAV02I0.IMG", MapArtKind.WorldMap, [], "No donor call site found", "ART_PAL.COL"),
            new("TRAV0I00.IMG", MapArtKind.WorldMap, [], "DaggerfallTravelMapWindow overworld image", "ART_PAL.COL"),
            new("TRAV0I01.IMG", MapArtKind.TravelChrome, [], "DaggerfallTravelMapWindow region picker image", "ART_PAL.COL"),
            new("TRAV0I03.IMG", MapArtKind.TravelChrome, [], "DaggerfallTravelMapWindow find button image", "ART_PAL.COL"),
            new("TRAV0I04.IMG", MapArtKind.TravelChrome, [], "DaggerfallTravelPopUp native texture", "ART_PAL.COL"),
            new("TRAVAI05.IMG", MapArtKind.TravelChrome, [], "DaggerfallTravelMapWindow down arrow image", "ART_PAL.COL"),
            new("TRAVBI05.IMG", MapArtKind.TravelChrome, [], "DaggerfallTravelMapWindow up arrow image", "ART_PAL.COL"),
            new("TRAVCI05.IMG", MapArtKind.TravelChrome, [], "DaggerfallTravelMapWindow right arrow image", "ART_PAL.COL"),
            new("TRAVDI05.IMG", MapArtKind.TravelChrome, [], "DaggerfallTravelMapWindow left arrow image", "ART_PAL.COL"),
            new("FMAP_PAL.COL", MapArtKind.Palette, [], "Palette the donor reads every FMAP file with", string.Empty),
            new("MAP.PAL", MapArtKind.Palette, [], "Palette the donor reads TMAP00I0.IMG with", string.Empty),
        ];

        // The regions the donor's travel map draws per file: regions 0, 1 and 16 tile several
        // screens, and every other region draws the single screen the region number names. The
        // three stubs are the single-screen variants of regions 0, 1 and 16, which the donor
        // names unsupported rather than drawing. Regions with no supplied screen are retained as
        // not supplied: the donor's rule names a file for every region, so the absence is a fact
        // about the corpus rather than a gap in the enumeration.
        files.Add(new DocumentedFile("FMAPAI00.IMG", MapArtKind.RegionMap, [0], "DaggerfallTravelMapWindow region map", "FMAP_PAL.COL"));
        files.Add(new DocumentedFile("FMAPBI00.IMG", MapArtKind.RegionMap, [0], "DaggerfallTravelMapWindow region map", "FMAP_PAL.COL"));
        files.Add(new DocumentedFile("FMAPAI01.IMG", MapArtKind.RegionMap, [1], "DaggerfallTravelMapWindow region map", "FMAP_PAL.COL"));
        files.Add(new DocumentedFile("FMAPBI01.IMG", MapArtKind.RegionMap, [1], "DaggerfallTravelMapWindow region map", "FMAP_PAL.COL"));
        files.Add(new DocumentedFile("FMAPCI01.IMG", MapArtKind.RegionMap, [1], "DaggerfallTravelMapWindow region map", "FMAP_PAL.COL"));
        files.Add(new DocumentedFile("FMAPDI01.IMG", MapArtKind.RegionMap, [1], "DaggerfallTravelMapWindow region map", "FMAP_PAL.COL"));
        files.Add(new DocumentedFile("FMAPAI16.IMG", MapArtKind.RegionMap, [16], "DaggerfallTravelMapWindow region map", "FMAP_PAL.COL"));
        files.Add(new DocumentedFile("FMAPBI16.IMG", MapArtKind.RegionMap, [16], "DaggerfallTravelMapWindow region map", "FMAP_PAL.COL"));
        files.Add(new DocumentedFile("FMAPCI16.IMG", MapArtKind.RegionMap, [16], "DaggerfallTravelMapWindow region map", "FMAP_PAL.COL"));
        files.Add(new DocumentedFile("FMAPDI16.IMG", MapArtKind.RegionMap, [16], "DaggerfallTravelMapWindow region map", "FMAP_PAL.COL"));
        for (int region = 0; region < 62; region++)
        {
            files.Add(new DocumentedFile(string.Format(CultureInfo.InvariantCulture, "FMAP0I{0:00}.IMG", region), MapArtKind.RegionMap, [region], "DaggerfallTravelMapWindow region map", "FMAP_PAL.COL"));
        }

        return files;
    }
}
