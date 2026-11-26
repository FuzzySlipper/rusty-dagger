using System.Text;
using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Daggerfall.Source.Tool.ReferenceExports;

namespace Daggerfall.Source.Tool.Commands;

/// <summary>
/// One location a pack lays out anew (its <c>locationOverrides</c> section): the classic location keeps its identity,
/// kind and map pixel, and takes this block grid, its city buildings with <see cref="Buildings"/> added for the named
/// buildings the new blocks bring, and <see cref="Name"/> when one is given. <see cref="Reserved"/> is the ruleset's
/// alone: a reserved location's buildings are never a random quest's.
/// </summary>
internal sealed record LocationOverrideRecipe(
    int Region,
    int Index,
    string? Name,
    int Width,
    int Height,
    IReadOnlyList<LocationOverrideBlock> Blocks,
    IReadOnlyList<LocationOverrideBuilding> Buildings,
    bool Reserved);

internal sealed record LocationOverrideBlock(int X, int Y, string Block);

/// <summary>A city building the grid's named buildings draw from, at the block it stands in.</summary>
internal sealed record LocationOverrideBuilding(int X, int Y, int BuildingType, int FactionId, int NameSeed, int Quality);

/// <summary>
/// Bakes a content pack's location overrides: each recipe in its <c>locationOverrides</c> section is normalized over
/// the player's Arena2 files as the import normalizes a location (<see cref="DaggerfallLocationExteriorBuilder"/>), and
/// its record, detail and ground are written below the pack's <c>locations/</c> folder for the pack to commit, where the
/// ruleset reads them in place of the imported location. The folder is the bake's alone: a file no recipe produces is
/// removed.
/// </summary>
internal static class LocationBakeCommand
{
    /// <summary>The folder beside a pack's payload that holds the baked locations.</summary>
    internal const string Folder = "locations";

    public static ToolCommand Command { get; } = new("location-bake",
    [
        Options.Arena2,
        CommandOption.Required("--pack", "PACK_DIR"),
    ], Run, Program.Tool);

    private static int Run(CommandArguments args)
    {
        string arena2 = Path.GetFullPath(args[Options.Arena2.Name]);
        string pack = Path.GetFullPath(args["--pack"]);
        string payload = Path.Combine(pack, "payload.json");
        IReadOnlyList<LocationOverrideRecipe> recipes = Read(payload);
        BsaArchive maps = BsaArchive.Parse(File.ReadAllBytes(Path.Combine(arena2, "MAPS.BSA")), Options.Arena2Label("MAPS.BSA"));
        BsaArchive blocks = BsaArchive.Parse(File.ReadAllBytes(Path.Combine(arena2, "BLOCKS.BSA")), Options.Arena2Label("BLOCKS.BSA"));
        BsaArchive models = BsaArchive.Parse(File.ReadAllBytes(Path.Combine(arena2, "ARCH3D.BSA")), Options.Arena2Label("ARCH3D.BSA"));
        PakMap climate = PakDecoder.Decode(File.ReadAllBytes(Path.Combine(arena2, "CLIMATE.PAK")), Options.Arena2Label("CLIMATE.PAK"));
        DaggerfallLocationExteriorBuilder builder = new(blocks, models);
        string folder = Path.Combine(pack, Folder);
        HashSet<string> written = [];
        foreach (LocationOverrideRecipe recipe in recipes)
        {
            DaggerfallLocationMap location = Bake(maps, builder, climate, recipe);
            DaggerfallLocationExterior exterior = location.Exterior!;
            DaggerfallLocationDetail detail = new(exterior.Buildings, exterior.BuildingReferences);
            written.Add(Write(folder, RecordPath(recipe.Region, recipe.Index), JsonSerializer.SerializeToUtf8Bytes(location, PublishedJson.Section)));
            written.Add(Write(folder, DaggerfallLocationDetails.DetailPath(".", recipe.Region, recipe.Index)[2..],
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(detail, PublishedJson.SectionCompact))));
            written.Add(Write(folder, DaggerfallLocationDetails.GroundPath(".", recipe.Region, recipe.Index)[2..], exterior.GroundTiles));
            Console.WriteLine($"location-bake: {recipe.Region}/{recipe.Index} '{location.Name}': {exterior.Width}x{exterior.Height} blocks, {exterior.Buildings.Count} buildings"
                + (exterior.MissingCityBuildings.Count == 0 ? "" : $"; warning: {string.Join("; ", exterior.MissingCityBuildings)}"));
        }

        if (Directory.Exists(folder))
            foreach (string stale in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(path => !written.Contains(path)))
            {
                File.Delete(stale);
                Console.WriteLine($"location-bake: removed {Path.GetRelativePath(pack, stale)}, which no recipe produces");
            }

        return 0;
    }

    /// <summary>A baked location's record, below the pack's <see cref="Folder"/>: beside its detail and ground files.</summary>
    internal static string RecordPath(int region, int index) => $"{region}/{index}.location.json";

    /// <summary>The location <paramref name="recipe"/> lays out, normalized as the import normalizes a classic one.</summary>
    internal static DaggerfallLocationMap Bake(BsaArchive maps, DaggerfallLocationExteriorBuilder builder, PakMap climate, LocationOverrideRecipe recipe)
    {
        string owner = $"Location override {recipe.Region}/{recipe.Index}";
        if (recipe.Width is < 1 or > 8 || recipe.Height is < 1 or > 8)
            throw new InvalidDataException($"{owner} lays out {recipe.Width}x{recipe.Height} blocks; a location's grid is 1 to 8 blocks each way.");
        if (recipe.Blocks.Count != recipe.Width * recipe.Height
            || recipe.Blocks.Select(block => (block.X, block.Y)).Distinct().Count() != recipe.Blocks.Count
            || recipe.Blocks.Any(block => block.X < 0 || block.X >= recipe.Width || block.Y < 0 || block.Y >= recipe.Height))
            throw new InvalidDataException($"{owner} must place one block in each of its {recipe.Width}x{recipe.Height} cells.");
        if (recipe.Buildings.Any(building => building.X < 0 || building.X >= recipe.Width || building.Y < 0 || building.Y >= recipe.Height))
            throw new InvalidDataException($"{owner} adds a city building outside its grid.");
        if (recipe.Name is { Length: 0 })
            throw new InvalidDataException($"{owner} renames the location to nothing.");
        MapsLocationRecord source = MapsDecoder.DecodeRegionLocations(maps, recipe.Region).FirstOrDefault(location => location.Index == recipe.Index)
            ?? throw new InvalidDataException($"{owner} names no classic location.");
        MapsExteriorLayout classic = MapsDecoder.DecodeExteriorLayout(maps, recipe.Region, recipe.Index);

        // The classic city buildings keep their place in the pool, so the named buildings of the blocks the location
        // kept draw the same ones; those the recipe adds follow, each in the sector of the block it stands in.
        ushort locationId = classic.Buildings.Count > 0 ? classic.Buildings[0].LocationId : (ushort)classic.LocationId;
        MapsExteriorLayout layout = classic with
        {
            Width = (byte)recipe.Width,
            Height = (byte)recipe.Height,
            Blocks = [.. recipe.Blocks.OrderBy(block => block.Y).ThenBy(block => block.X)
                .Select(block => new MapsExteriorBlock(block.Block, (byte)block.X, (byte)block.Y))],
            Buildings = [.. classic.Buildings, .. recipe.Buildings.Select(building =>
                new MapsExteriorBuilding(building.NameSeed, building.FactionId, building.BuildingType, building.Quality)
                {
                    Sector = (short)(building.X + building.Y * recipe.Width),
                    LocationId = locationId,
                })],
        };
        DaggerfallLocationMap location = new(source.Region, source.Index, recipe.Name ?? source.Name, source.MapId,
            source.Longitude, source.Latitude, source.DungeonType, source.LocationType, source.Discovered);
        location = location with
        {
            Exterior = builder.Build(layout, location),
            Climate = DaggerfallLocationBuilder.LocationClimate(climate, location),
        };
        location.ValidateExterior();
        if (location.Exterior!.MissingCityBuildings.Count > 0)
            throw new InvalidDataException($"{owner} leaves named buildings without a city building: {string.Join("; ", location.Exterior.MissingCityBuildings)}");
        return location;
    }

    /// <summary>
    /// The location overrides of the pack whose payload manifest is <paramref name="path"/> (its <c>locationOverrides</c>
    /// section), none stated twice; a pack with no such section has none.
    /// </summary>
    internal static IReadOnlyList<LocationOverrideRecipe> Read(string path)
    {
        LocationOverrideRecipe[] recipes;
        try
        {
            using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(path)))
                if (!manifest.RootElement.TryGetProperty("sections", out JsonElement sections) || !sections.TryGetProperty("locationOverrides", out _))
                    return [];
            using JsonDocument section = BasePackFiles.ReadSectionDocument(path, "locationOverrides");
            recipes = section.RootElement.Deserialize<LocationOverrideRecipe[]>(PublishedJson.SectionRead)
                ?? throw new InvalidDataException($"'{path}' states no location overrides.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The location overrides of '{path}' do not read: {exception.Message}", exception);
        }

        if (recipes.GroupBy(recipe => (recipe.Region, recipe.Index)).FirstOrDefault(group => group.Count() > 1) is { } repeated)
            throw new InvalidDataException($"'{path}' overrides location {repeated.Key.Region}/{repeated.Key.Index} twice.");
        return recipes;
    }

    private static string Write(string folder, string relativePath, ReadOnlySpan<byte> bytes)
    {
        string path = Path.GetFullPath(Path.Combine(folder, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes.ToArray());
        return path;
    }
}
