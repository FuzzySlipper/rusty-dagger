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

    /// <summary>
    /// The guards a town holds in reserve for a crime crackdown, after its walkers: the most a crackdown can call out
    /// (<see cref="DaggerfallPopulationPlacement.CrackdownGuard"/>).
    /// </summary>
    internal const int CrackdownGuardSlots = 6;

    internal static IReadOnlyList<DaggerfallPopulationPlacement> Create(DaggerfallDefinitions definitions,
        DaggerfallSiteRecord location, IEnumerable<(int X, int Z)> clear, WorldPoint? gateAnchor = null,
        ulong worldSocialSeed = 0UL, DaggerfallGuardTierTuning? guardTiers = null,
        IReadOnlyList<WorldPoint>? gateAnchors = null)
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
        WorldPoint Position((int X, int Z) cell)
        {
            var block = blocks[(cell.X / automapSide, cell.Z / automapSide)];
            int sourceX = cell.X - block.X * automapSide;
            int sourceY = automapSide - 1 - (cell.Z - block.Y * automapSide);
            float x = block.X * blockSide + (sourceX * automapSide + automapSide / 2) * (cellSize / automapSide);
            float z = block.Y * blockSide + (sourceY * automapSide + automapSide / 2) * (cellSize / automapSide);
            return new WorldPoint(x, 0f, -z);
        }
        WorldPoint[] positions = new WorldPoint[count];
        for (int index = 0; index < count; index++)
            positions[index] = Position(cells[(int)((long)index * cells.Length / count)]);
        WorldPoint? primaryGateAnchor = gateAnchors is { Count: > 0 } ? gateAnchors[0] : gateAnchor;
        int gateGuardIndex = 0;
        if (primaryGateAnchor is { } anchor && count > 1)
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
        DaggerfallGuardTierProfile watchProfile = (guardTiers ?? DaggerfallGuardTierTuning.Default).ResolveProfile(location);
        HashSet<int> guardIndices = ResolvePatrolGuardIndices(count, watchProfile.BaselinePatrolGuards);
        // Proper walled cities (gateAnchors.Count > 0) post one liege-livery watch officer/knight at each gate,
        // placed directly on the nearest walkable clear ground cell to that gate and retaining its gate anchor;
        // unwalled hamlets and villages keep purely local rank-and-file StreetWatch patrols.
        Dictionary<int, WorldPoint> gatePostAnchorByIndex = [];
        if (gateAnchors is { Count: > 0 } walledGates && count > 0)
        {
            HashSet<(int X, int Z)> claimedGateCells = [];
            WorldPoint SnapToNearestClearCell(WorldPoint gatePoint)
            {
                (int X, int Z) bestCell = cells[0];
                WorldPoint bestPos = Position(bestCell);
                float bestDistSq = float.MaxValue;
                foreach ((int X, int Z) cell in cells)
                {
                    if (claimedGateCells.Contains(cell))
                        continue;
                    WorldPoint candidate = Position(cell);
                    float dx = candidate.X - gatePoint.X;
                    float dz = candidate.Z - gatePoint.Z;
                    float distSq = dx * dx + dz * dz;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestCell = cell;
                        bestPos = candidate;
                    }
                }
                claimedGateCells.Add(bestCell);
                return bestPos;
            }

            // Slot 0 (WatchCaptainSlotId) is stationed at gate 0.
            positions[0] = SnapToNearestClearCell(walledGates[0]);
            gatePostAnchorByIndex[0] = walledGates[0];
            guardIndices.Add(0);

            for (int gateIdx = 1; gateIdx < walledGates.Count && gatePostAnchorByIndex.Count < count; gateIdx++)
            {
                WorldPoint extraGate = walledGates[gateIdx];
                WorldPoint postPosition = SnapToNearestClearCell(extraGate);
                int bestIndex = -1;
                float bestDistSq = float.MaxValue;
                for (int idx = 1; idx < count; idx++)
                {
                    if (gatePostAnchorByIndex.ContainsKey(idx))
                        continue;
                    float dx = positions[idx].X - postPosition.X;
                    float dz = positions[idx].Z - postPosition.Z;
                    float distSq = dx * dx + dz * dz;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestIndex = idx;
                    }
                }
                if (bestIndex >= 0)
                {
                    positions[bestIndex] = postPosition;
                    gatePostAnchorByIndex[bestIndex] = extraGate;
                    guardIndices.Add(bestIndex);
                }
            }
        }
        for (int index = 0; index < count; index++)
        {
            string id = string.Create(CultureInfo.InvariantCulture, $"population/{slug}-{location.Id.Region}-{location.Id.Index}/dynamic/{index:D3}");
            bool isGuard = guardIndices.Contains(index);
            bool isGateOfficer = gatePostAnchorByIndex.TryGetValue(index, out WorldPoint gatePostAnchor);
            DaggerfallGuardPostKind guardPost = isGateOfficer
                ? DaggerfallGuardPostKind.WalledCityGateOfficer
                : DaggerfallGuardPostKind.StreetWatch;
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
                GuardPost = guardPost,
                GatePostAnchor = isGateOfficer ? gatePostAnchor : null,
                FamilyId = familyId,
            });
        }
        // The town's reserve watch, spread between the walkers' cells, walks only while a crackdown calls it out.
        for (int rank = 1; rank <= CrackdownGuardSlots && cells.Length > 0; rank++)
        {
            int index = count + rank - 1;
            string id = string.Create(CultureInfo.InvariantCulture, $"population/{slug}-{location.Id.Region}-{location.Id.Index}/dynamic/{index:D3}");
            string walkerSlotKey = DaggerfallPeopleSlots.WalkerSlotKey(index);
            ulong slotSeed = DaggerfallPeopleSlots.SlotSeed(worldSocialSeed, location.Id, walkerSlotKey);
            ushort nameSeed = (ushort)((slotSeed & 0xFFFFUL) == 0 ? 1 : (slotSeed & 0xFFFFUL));
            int sprite = people.Sprites[0];
            var cell = cells[(int)((long)(2 * rank - 1) * cells.Length / (2 * CrackdownGuardSlots))];
            result.Add(new(id, Position(cell), sprite >> 7, sprite & 0x7f, people.Id, 0, nameSeed)
            {
                IsGuard = true,
                FamilyId = DaggerfallHouseholds.ResolveForExteriorWalker(worldSocialSeed, location.Id, walkerSlotKey, true, townBuckets),
                CrackdownGuard = rank,
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

    internal static HashSet<int> ResolvePatrolGuardIndices(int slotCount, int desiredGuards)
    {
        HashSet<int> indices = [];
        if (slotCount <= 0)
            return indices;
        int guardCount = Math.Clamp(desiredGuards, 1, slotCount);
        indices.Add(0); // Index 0 is always the canonical WatchCaptainSlotId / gate guard.
        for (int i = 1; i < guardCount; i++)
        {
            int idx = Math.Clamp((int)Math.Round(i * (double)slotCount / guardCount, MidpointRounding.AwayFromZero), 1, slotCount - 1);
            while (!indices.Add(idx) && idx < slotCount - 1)
                idx++;
        }
        return indices;
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
