using System.Globalization;
using System.Text.Json;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>The donor PopulationManager's bounded pool, shared by published and assembled exteriors.</summary>
internal static class DaggerfallExteriorPopulation
{
    /// <summary>Canonical placement ID for a town's deterministic exterior walker/guard slot at <paramref name="index"/>.</summary>
    internal static string ExteriorSlotId(DaggerfallSiteRecord location, int index)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Exterior slot index cannot be negative.");
        string slug = LocationSlug(location.Name);
        return string.Create(CultureInfo.InvariantCulture, $"population/{slug}-{location.Id.Region}-{location.Id.Index}/dynamic/{index:D3}");
    }

    /// <summary>Canonical placement ID for a town's primary gate guard / watch captain slot (index 0).</summary>
    internal static string WatchCaptainSlotId(DaggerfallSiteRecord location) => ExteriorSlotId(location, 0);

    /// <summary>Deterministic maximum exterior walker slot count for <paramref name="location"/>.</summary>
    internal static int EstimatedSlotCount(DaggerfallSiteRecord location) =>
        location?.Exterior is { Blocks.Count: > 0 } exterior
            ? Math.Clamp(exterior.Blocks.Count / 16, 1, 4) * 24
            : 0;

    private static string LocationSlug(string name)
    {
        string slug = new string(name.Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-').ToArray()).Trim('-');
        return slug.Length == 0 ? "source" : slug;
    }

    internal static IReadOnlyList<DaggerfallPopulationPlacement> Create(DaggerfallDefinitions definitions,
        DaggerfallSiteRecord location, IEnumerable<(int X, int Z)> clear, WorldPoint? gateAnchor = null,
        ulong worldSocialSeed = 0UL)
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
        string slug = LocationSlug(location.Name);
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
        int gateGuardIndex = 0;
        if (gateAnchor is { } anchor && count > 1)
        {
            float bestDistSq = float.MaxValue;
            for (int index = 0; index < count; index++)
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
            if (gateGuardIndex != 0)
                (positions[0], positions[gateGuardIndex]) = (positions[gateGuardIndex], positions[0]);
        }
        List<DaggerfallPopulationPlacement> result = [];
        int townBuckets = DaggerfallHouseholds.TownHouseholdBucketCount(location);
        for (int index = 0; index < count; index++)
        {
            string id = string.Create(CultureInfo.InvariantCulture, $"population/{slug}-{location.Id.Region}-{location.Id.Index}/dynamic/{index:D3}");
            bool isGuard = index % 32 == 0;
            string walkerSlotKey = DaggerfallPeopleSlots.WalkerSlotKey(index);
            ulong slotSeed = DaggerfallPeopleSlots.SlotSeed(worldSocialSeed, location.Id, walkerSlotKey);
            int spriteIndex = isGuard ? 0 : (int)((slotSeed >> 16) % (ulong)people.Sprites.Count);
            int sprite = people.Sprites[spriteIndex];
            byte flags = isGuard ? (byte)0 : (byte)(spriteIndex == 1 ? 0x20 : 0);
            ushort nameSeed = (ushort)((slotSeed & 0xFFFFUL) == 0 ? 1 : (slotSeed & 0xFFFFUL));
            string? familyId = DaggerfallHouseholds.ResolveForExteriorWalker(
                worldSocialSeed, location.Id, walkerSlotKey, isGuard, townBuckets);
            result.Add(new(id, positions[index], sprite >> 7, sprite & 0x7f, people.Id,
                flags, nameSeed)
            {
                IsGuard = isGuard,
                FamilyId = familyId,
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
