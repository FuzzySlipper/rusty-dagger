using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Immutable source facts for one placed building in its profile's geographic site.</summary>
internal sealed record DaggerfallInteriorBuilding(
    int BlockX, int BlockY, DaggerfallRmbBuildingId Building, int BuildingType, int FactionId)
{
    internal DaggerfallInteriorBuilding Validate()
    {
        if (BlockX is < 0 or > 255 || BlockY is < 0 or > 255 || Building.Index < 0
            || string.IsNullOrWhiteSpace(Building.SourceKey)
            || BuildingType is < 0 or > 255 || FactionId is < 0 or > 65535)
            throw new ArgumentException("Interior building metadata must identify a placed source building and its type/faction.");
        return this;
    }

    /// <summary>Rejects an inconsistent publication rather than granting access from unproved metadata.</summary>
    internal void ValidateAgainst(DaggerfallBlocksSnapshot blocks)
    {
        Validate();
        if (!blocks.RmbBuildings.TryGetValue(Building, out DaggerfallRmbBuildingSource? source)
            || source.BuildingType != BuildingType || source.FactionId != FactionId)
            throw new ArgumentException($"Interior building '{Building}' does not match the admitted block catalog.");
    }

    internal static DaggerfallInteriorBuilding? Read(ReadOnlyMemory<byte> normalized, DaggerfallWorldProfileKind kind)
    {
        using JsonDocument document = JsonDocument.Parse(normalized);
        JsonElement world = document.RootElement.GetProperty("world");
        if (!world.TryGetProperty("interiorBuilding", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (kind != DaggerfallWorldProfileKind.Interior)
            throw new ArgumentException("Only an interior profile can publish interior building metadata.");
        return new DaggerfallInteriorBuilding(value.GetProperty("blockX").GetInt32(), value.GetProperty("blockY").GetInt32(),
            new(value.GetProperty("sourceKey").GetString()!, value.GetProperty("buildingIndex").GetInt32()),
            value.GetProperty("buildingType").GetInt32(), value.GetProperty("factionId").GetInt32()).Validate();
    }
}
