using System.Globalization;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;

namespace Daggerfall.Import.Publication;

/// <summary>
/// The Arena2 files a site closure or the classic media publication may read, loaded from the operator's
/// corpus directory under a byte quota. Only the named dungeon sources, the fixed classic media set and
/// texture leaves are admitted; a publication that asks for anything else is refused by name.
/// </summary>
public sealed class Arena2SiteSources
{
    /// <summary>The largest single Arena2 source a publication reads.</summary>
    public const long MaximumIndividualSourceBytes = 128L * 1024L * 1024L;

    /// <summary>The largest total of Arena2 sources one publication reads.</summary>
    public const long MaximumTotalSourceBytes = 512L * 1024L * 1024L;

    /// <summary>The world sources every site closure reads before any on-demand texture leaf.</summary>
    public static readonly IReadOnlyList<string> DungeonSourceNames =
    [
        "MAPS.BSA",
        "BLOCKS.BSA",
        "ARCH3D.BSA",
        "CLIMATE.PAK",
        "PAL.PAL",
        "FACTION.TXT",
    ];

    /// <summary>
    /// The fixed classic media input set: weapon grammars, sound, UI screens, fonts, map art and the
    /// texture leaves the classic sprites are cut from. <see cref="ClassicMediaInputs"/> binds each one.
    /// </summary>
    public static readonly IReadOnlyList<string> ClassicMediaSourceNames =
    [
        "ART_PAL.COL",
        .. Enumerable.Range(0, 12).Select(index => $"WEAPON{index:D2}.CIF"),
        "DAGGER.SND",
        "MAIN00I0.IMG", "MAIN03I0.IMG", "MAIN04I0.IMG", "MAIN05I0.IMG",
        "INVE00I0.IMG", "INFO00I0.IMG", "DIE_00I0.IMG", "CHGN00I0.IMG",
        "PICK02I0.IMG", "PICK03I0.IMG", "PRIS00I0.IMG", "TITL00I0.IMG",
        "BOOK00I0.IMG", "REST00I0.IMG", "SHOP00I0.IMG", "GILD00I0.IMG",
        "BANK00I0.IMG", "REST01I0.IMG", "REST02I0.IMG", "INVE08I0.IMG",
        "INVE10I0.IMG", "INVE11I0.IMG", "INVE12I0.IMG", "INVE14I0.IMG",
        "GILD01I0.IMG",
        .. Enumerable.Range(0, 5).Select(index => $"FONT{index:D4}.FNT"),
        "FMAP_PAL.COL",
        "MAP.PAL",
        .. MapMediaNames,
        "TEXTURE.380", "TEXTURE.205", "TEXTURE.207", "TEXTURE.216", "TEXTURE.234", "TEXTURE.245",
        "PAL.PAL",
    ];

    /// <summary>The map, automap, travel and town screens the map publication renders, in source order.</summary>
    private static IEnumerable<string> MapMediaNames =>
    [
        .. new[] { 0, 1, 5, 9, 11 }.Concat(Enumerable.Range(16, 8)).Append(26).Concat(Enumerable.Range(32, 30))
            .Select(region => $"FMAP0I{region:D2}.IMG"),
        "FMAPAI00.IMG", "FMAPBI00.IMG", "FMAPAI01.IMG", "FMAPBI01.IMG", "FMAPCI01.IMG", "FMAPDI01.IMG",
        "FMAPAI16.IMG", "FMAPBI16.IMG", "FMAPCI16.IMG", "FMAPDI16.IMG",
        "AMAP00I0.IMG", "AMAP01I0.IMG", "TMAP00I0.IMG", "TOWN00I0.IMG",
        "TRAV00I0.IMG", "TRAV01I0.IMG", "TRAV01I1.IMG", "TRAV02I0.IMG",
        "TRAV0I00.IMG", "TRAV0I01.IMG", "TRAV0I03.IMG", "TRAV0I04.IMG",
        "TRAVAI05.IMG", "TRAVBI05.IMG", "TRAVCI05.IMG", "TRAVDI05.IMG",
    ];

    private readonly string arena2Directory;
    private readonly Dictionary<string, DungeonLogicalSource> loaded = new(StringComparer.Ordinal);
    private readonly SortedDictionary<ushort, byte[]> worldVisualTextureLeaves = [];
    private readonly HashSet<string> dungeonSourceNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> resolvedOnDemand = new(StringComparer.Ordinal);
    private long totalBytes;

    private Arena2SiteSources(string arena2Directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arena2Directory);
        this.arena2Directory = arena2Directory;
    }

    /// <summary>The fixed classic media inputs alone, for the product-wide classic media publication.</summary>
    public static Arena2SiteSources ForClassicMedia(string arena2Directory)
    {
        Arena2SiteSources sources = new(arena2Directory);
        foreach (string name in ClassicMediaSourceNames) sources.LoadClassic(name);
        return sources;
    }

    /// <summary>
    /// The sources the product-wide world media publication starts from: the classic media inputs, the mesh
    /// archive and the palette. Texture leaves are admitted by the selection that names them.
    /// </summary>
    public static Arena2SiteSources ForWorldMedia(string arena2Directory)
    {
        Arena2SiteSources sources = ForClassicMedia(arena2Directory);
        sources.LoadDungeon("ARCH3D.BSA");
        sources.LoadDungeon("PAL.PAL");
        return sources;
    }

    /// <summary>Admits one texture leaf to the dungeon media source closure.</summary>
    public void AdmitTextureLeaf(ushort archive) => LoadDungeon($"TEXTURE.{archive:000}");

    /// <summary>
    /// Parses one supplied texture leaf without admitting it, so a selection can ask which of its records
    /// are publishable before the exact closure is fixed.
    /// </summary>
    public TextureArchive ParseTextureLeaf(ushort archive)
    {
        DungeonLogicalSource source = ReadSource($"TEXTURE.{archive:000}");
        return TextureArchive.Parse(source.Bytes.Span, source.Label);
    }

    /// <summary>The world sources and the classic media inputs a site closure starts from.</summary>
    public static Arena2SiteSources ForSite(string arena2Directory)
    {
        Arena2SiteSources sources = ForClassicMedia(arena2Directory);
        foreach (string name in DungeonSourceNames) sources.LoadDungeon(name);
        return sources;
    }

    public IReadOnlyList<DungeonLogicalSource> DungeonSources => dungeonSourceNames
        .OrderBy(name => name, StringComparer.Ordinal)
        .Select(name => loaded[name])
        .ToArray();

    /// <summary>The mesh archive every site's geometry is cut from.</summary>
    public DungeonLogicalSource MeshArchive => loaded["ARCH3D.BSA"];

    /// <summary>
    /// Only PAL.PAL and the exact dynamically selected dungeon texture closure enter this source set.
    /// Classic-only TEXTURE archives never reach the dungeon media exact-closure validator.
    /// </summary>
    public IReadOnlyList<Arena2DungeonMediaSource> DungeonMediaSources => dungeonSourceNames
        .Where(name => name is "PAL.PAL" || IsTextureLeaf(name))
        .OrderBy(name => name, StringComparer.Ordinal)
        .Select(name => new Arena2DungeonMediaSource(loaded[name].Label, loaded[name].Bytes.Span))
        .ToArray();

    /// <summary>
    /// Every texture leaf the corpus supplies, read so a material reference resolves against what is
    /// actually there rather than against whatever this pass happened to load.
    /// </summary>
    public TextureLeafInventory TextureLeaves() => ReadTextureLeaves(arena2Directory);

    /// <summary>Reads every <c>TEXTURE.###</c> leaf in a corpus directory, refusing a name that carries no leaf number.</summary>
    public static TextureLeafInventory ReadTextureLeaves(string arena2Directory)
    {
        List<(int Id, string Path, ReadOnlyMemory<byte> Bytes)> leaves = [];
        foreach (string path in Directory.EnumerateFiles(arena2Directory, "TEXTURE.*"))
        {
            string name = Path.GetFileName(path);
            if (!int.TryParse(name["TEXTURE.".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int leafId))
            {
                // A supplied name that carries no leaf number would leave a material looking unsupplied
                // while the corpus supplies it, so it is refused rather than skipped.
                throw new InvalidOperationException($"'{name}' does not carry a texture leaf number, so it cannot be enumerated as a leaf.");
            }

            leaves.Add((leafId, name, File.ReadAllBytes(path)));
        }

        return TextureLeafInventory.Enumerate(leaves, Path.GetFileName(Path.TrimEndingDirectorySeparator(arena2Directory)));
    }

    /// <summary>
    /// Reads one classic texture leaf a published world visual's mesh selects. The visual's own set
    /// decides which leaves are needed, so they are admitted here rather than added to the fixed
    /// classic source names.
    /// </summary>
    public void AdmitWorldVisualTextureLeaf(ushort archive)
    {
        if (worldVisualTextureLeaves.ContainsKey(archive)) return;
        DungeonLogicalSource source = ReadSource($"TEXTURE.{archive:000}");
        worldVisualTextureLeaves.Add(archive, source.Bytes.ToArray());
    }

    public Arena2ClassicMediaInputs ClassicMediaInputs => new(
        Require("WEAPON01.CIF"), Require("WEAPON02.CIF"), Require("WEAPON04.CIF"), Require("WEAPON05.CIF"),
        Require("WEAPON06.CIF"), Require("WEAPON07.CIF"), Require("WEAPON08.CIF"), Require("WEAPON09.CIF"),
        Require("WEAPON10.CIF"),
        Require("ART_PAL.COL"),
        Require("TEXTURE.380"),
        Require("PAL.PAL"),
        Require("DAGGER.SND"),
        Require("MAIN00I0.IMG"), Require("MAIN03I0.IMG"), Require("MAIN04I0.IMG"), Require("MAIN05I0.IMG"),
        Require("INVE00I0.IMG"), Require("INFO00I0.IMG"), Require("DIE_00I0.IMG"), Require("CHGN00I0.IMG"),
        Require("PICK02I0.IMG"), Require("PICK03I0.IMG"), Require("PRIS00I0.IMG"), Require("TITL00I0.IMG"),
        Require("BOOK00I0.IMG"), Require("REST00I0.IMG"), Require("SHOP00I0.IMG"), Require("GILD00I0.IMG"),
        Require("BANK00I0.IMG"), Require("REST01I0.IMG"), Require("REST02I0.IMG"), Require("INVE08I0.IMG"),
        Require("INVE10I0.IMG"), Require("INVE11I0.IMG"), Require("INVE12I0.IMG"), Require("INVE14I0.IMG"),
        Require("GILD01I0.IMG"),
        Require("TEXTURE.207"), Require("TEXTURE.216"), Require("TEXTURE.234"), Require("TEXTURE.245"),
        Require("FONT0003.FNT"),
        Require("WEAPON00.CIF"), Require("WEAPON03.CIF"), Require("WEAPON11.CIF"),
        Require("FONT0000.FNT"), Require("FONT0001.FNT"), Require("FONT0002.FNT"), Require("FONT0004.FNT"),
        [.. MapMediaNames.Order(StringComparer.Ordinal).Select(name => new MapMediaInput(name, Require(name)))],
        Require("FMAP_PAL.COL"),
        Require("MAP.PAL"),
        [.. worldVisualTextureLeaves.Select(leaf => new ClassicMissileTextureLeaf(leaf.Key, leaf.Value))], Require("TEXTURE.205"));

    /// <summary>
    /// Builds a publication, loading the dungeon source or texture leaves a normalizer reports missing and
    /// retrying. Discovery rides typed missing-source data, never diagnostic wording. Each retry loads at
    /// least one previously unrequested source; a repeated request means the requirement does not resolve,
    /// so the loop always terminates instead of spinning.
    /// </summary>
    public T LoadingOnDemand<T>(Func<T> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        while (true)
        {
            try
            {
                return build();
            }
            catch (MissingArena2SourceException missing)
            {
                LoadOnDemand(missing.SourceName);
            }
            catch (MissingDungeonMediaTexturesException mismatch)
                when (mismatch.UnneededTextureNames.Count == 0 && mismatch.MissingTextureNames.Count != 0)
            {
                foreach (string textureName in mismatch.MissingTextureNames) LoadOnDemand(textureName);
            }
        }
    }

    private void LoadOnDemand(string sourceName)
    {
        LoadDungeon(sourceName);
        if (!resolvedOnDemand.Add(sourceName))
            throw new InvalidOperationException($"Dungeon source '{sourceName}' is still required after loading it on demand.");
    }

    private void LoadDungeon(string fileName)
    {
        if (!DungeonSourceNames.Contains(fileName, StringComparer.Ordinal) && !IsTextureLeaf(fileName))
        {
            throw new InvalidOperationException($"'{fileName}' is not an admitted Arena2 dungeon source name.");
        }

        Load(fileName);
        dungeonSourceNames.Add(fileName);
    }

    private void LoadClassic(string fileName) => Load(fileName);

    private byte[] Require(string fileName) => loaded.TryGetValue(fileName, out DungeonLogicalSource? source)
        ? source.Bytes.ToArray()
        : throw new InvalidOperationException($"The admitted Arena2 source '{fileName}' was not loaded.");

    private void Load(string fileName)
    {
        if (loaded.ContainsKey(fileName)) return;
        DungeonLogicalSource source = ReadSource(fileName);
        long nextTotal = checked(totalBytes + source.Bytes.Length);
        if (nextTotal > MaximumTotalSourceBytes)
        {
            throw new InvalidOperationException("The admitted Arena2 source closure exceeds the total byte quota.");
        }

        loaded.Add(fileName, source);
        totalBytes = nextTotal;
    }

    private DungeonLogicalSource ReadSource(string fileName)
    {
        if (!IsAdmittedSourceName(fileName))
        {
            throw new InvalidOperationException($"'{fileName}' is not an admitted Arena2 source name.");
        }

        string sourcePath = Path.Combine(arena2Directory, fileName);
        FileInfo info = new(sourcePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"Required Arena2 source '{fileName}' was not found.", sourcePath);
        }

        if (info.Length is <= 0 or > MaximumIndividualSourceBytes)
        {
            throw new InvalidOperationException($"Arena2 source '{fileName}' is outside the permitted byte range.");
        }

        byte[] bytes = File.ReadAllBytes(sourcePath);
        if (bytes.LongLength != info.Length)
        {
            throw new IOException($"Arena2 source '{fileName}' changed while it was being read.");
        }

        return new DungeonLogicalSource(PublishedSourcePath.Arena2(fileName), bytes);
    }

    private static bool IsAdmittedSourceName(string value) => DungeonSourceNames.Contains(value, StringComparer.Ordinal)
        || ClassicMediaSourceNames.Contains(value, StringComparer.Ordinal)
        || IsTextureLeaf(value);

    private static bool IsTextureLeaf(string value) => value.Length == "TEXTURE.000".Length
        && value.StartsWith("TEXTURE.", StringComparison.Ordinal)
        && value[8..].All(char.IsAsciiDigit);
}
