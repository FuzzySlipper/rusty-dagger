using System.Globalization;
using System.Text.Json;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>The donor PopulationManager's bounded pool, shared by published and assembled exteriors.</summary>
internal static class DaggerfallExteriorPopulation
{
    internal static IReadOnlyList<DaggerfallPopulationPlacement> Create(DaggerfallDefinitions definitions,
        DaggerfallSiteRecord location, IEnumerable<(int X, int Z)> clear, WorldPoint? gateAnchor = null)
    {
        const int automapSide = 64;
        const float blockSide = DaggerfallWorldGrid.HorizontalSize / 8f;
        const float cellSize = blockSide / automapSide;
        var factions = definitions.Factions.Factions.Values;
        // The people of the location's own region, as the donor's GetPeopleOfCurrentRegion finds them; a region with none
        // (the player's ships) has no wandering townsfolk, never another region's.
        var people = factions.Where(faction => faction.Type == 15 && faction.Region == location.Id.Region && faction.Sprites.Count != 0)
            .OrderBy(faction => faction.Id).FirstOrDefault();
        if (people is null || location.Exterior is not { } exterior) return [];
        var blocks = exterior.Blocks.ToDictionary(block => (block.X, block.Y));
        var cells = clear.Where(cell => blocks.ContainsKey((cell.X / automapSide, cell.Z / automapSide)))
            .OrderBy(cell => cell.X).ThenBy(cell => cell.Z).ToArray();
        int count = Math.Min(Math.Clamp(blocks.Count / 16, 1, 4) * 24, cells.Length);
        string slug = new string(location.Name.Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-').ToArray()).Trim('-');
        if (slug.Length == 0) slug = "source";
        WorldPoint[] positions = new WorldPoint[count];
        for (int index = 0; index < count; index++)
        {
            var cell = cells[(int)((long)index * cells.Length / count)];
            var block = blocks[(cell.X / automapSide, cell.Z / automapSide)];
            int sourceX = cell.X - block.X * automapSide;
            int sourceY = automapSide - 1 - (cell.Z - block.Y * automapSide);
            float x = block.X * blockSide + (sourceX * automapSide + automapSide / 2) * (cellSize / automapSide);
            float z = block.Y * blockSide + (sourceY * automapSide + automapSide / 2) * (cellSize / automapSide);
            positions[index] = new WorldPoint(x, 0f, -z);
        }
        int gateGuardIndex = count > 1 ? 1 : 0;
        if (gateAnchor is { } anchor && count > 1)
        {
            float bestDistSq = float.MaxValue;
            for (int index = 1; index < count; index++)
            {
                float dx = positions[index].X - anchor.X;
                float dz = positions[index].Z - anchor.Z;
                float distSq = dx * dx + dz * dz;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    gateGuardIndex = index;
                }
            }
        }
        List<DaggerfallPopulationPlacement> result = [];
        for (int index = 0; index < count; index++)
        {
            string id = string.Create(CultureInfo.InvariantCulture, $"population/{slug}-{location.Id.Region}-{location.Id.Index}/dynamic/{index:D3}");
            bool isGuard = count > 1 ? Math.Abs(index - gateGuardIndex) % 32 == 0 : index == 0;
            int sprite = people.Sprites[isGuard ? 0 : index % people.Sprites.Count];
            byte flags = isGuard ? (byte)0 : (byte)(index % people.Sprites.Count == 1 ? 0x20 : 0);
            result.Add(new(id, positions[index], sprite >> 7, sprite & 0x7f, people.Id,
                flags, StableSeed(id))
            {
                IsGuard = isGuard,
            });
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
