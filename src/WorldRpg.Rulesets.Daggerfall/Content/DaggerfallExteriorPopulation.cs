using System.Globalization;
using System.Text.Json;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>The donor PopulationManager's bounded pool, shared by published and assembled exteriors.</summary>
internal static class DaggerfallExteriorPopulation
{
    internal static IReadOnlyList<DaggerfallPopulationPlacement> Create(DaggerfallDefinitions definitions,
        DaggerfallSiteRecord location, IEnumerable<(int X, int Z)> clear)
    {
        const int automapSide = 64;
        const float blockSide = DaggerfallTerrainSurfaceBuilder.HorizontalSize / 8f;
        const float cellSize = blockSide / automapSide;
        var factions = definitions.Factions.Factions.Values;
        var people = factions.Where(faction => faction.Type == 15 && faction.Region == location.Id.Region && faction.Flats.Count != 0)
            .OrderBy(faction => faction.Id).FirstOrDefault()
            ?? factions.Where(faction => faction.Type == 15 && faction.Flats.Count != 0).OrderBy(faction => faction.Id).FirstOrDefault();
        if (people is null || location.Exterior is not { } exterior) return [];
        var blocks = exterior.Blocks.ToDictionary(block => (block.X, block.Y));
        var cells = clear.Where(cell => blocks.ContainsKey((cell.X / automapSide, cell.Z / automapSide)))
            .OrderBy(cell => cell.X).ThenBy(cell => cell.Z).ToArray();
        int count = Math.Min(Math.Clamp(blocks.Count / 16, 1, 4) * 24, cells.Length);
        string slug = new string(location.Name.Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-').ToArray()).Trim('-');
        if (slug.Length == 0) slug = "source";
        List<DaggerfallPopulationPlacement> result = [];
        for (int index = 0; index < count; index++)
        {
            var cell = cells[(int)((long)index * cells.Length / count)];
            var block = blocks[(cell.X / automapSide, cell.Z / automapSide)];
            int sourceX = cell.X - block.X * automapSide;
            int sourceY = automapSide - 1 - (cell.Z - block.Y * automapSide);
            float x = block.X * blockSide + (sourceX * automapSide + automapSide / 2) * (cellSize / automapSide);
            float z = block.Y * blockSide + (sourceY * automapSide + automapSide / 2) * (cellSize / automapSide);
            string id = string.Create(CultureInfo.InvariantCulture, $"population/{slug}-{location.Id.Region}-{location.Id.Index}/dynamic/{index:D3}");
            int flat = people.Flats[index % people.Flats.Count];
            result.Add(new(id, new WorldPoint(x, 0f, -z), flat >> 7, flat & 0x7f, people.Id,
                (byte)(index % people.Flats.Count == 1 ? 0x20 : 0), StableSeed(id)));
        }
        return result;
    }

    internal static IReadOnlyList<(int X, int Z)> ReadClearCells(ReadOnlyMemory<byte> normalized)
    {
        using JsonDocument document = JsonDocument.Parse(normalized);
        return document.RootElement.GetProperty("world").GetProperty("clearOutdoorCells").EnumerateArray()
            .Select(cell => (cell.GetProperty("x").GetInt32(), cell.GetProperty("z").GetInt32())).ToArray();
    }

    internal static ushort StableSeed(string id)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char value in id) hash = (hash ^ value) * 16777619;
            return (ushort)(hash & ushort.MaxValue);
        }
    }
}
