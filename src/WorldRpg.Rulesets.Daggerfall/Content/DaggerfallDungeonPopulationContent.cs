using System.Text.Json;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallDungeonTreasurePlacement(string Id, WorldPoint Position);
internal sealed record DaggerfallDungeonEnemyPlacement(string Id, WorldPoint Position, int EncounterSlot,
    int SpawnDistance, bool Passive, bool Underwater, bool AquaticSpawnAllowed = false);

/// <summary>The same normalized marker contract for site closures and placed source blocks.</summary>
internal sealed record DaggerfallDungeonPopulationContent(IReadOnlyList<DaggerfallDungeonTreasurePlacement> Treasures,
    IReadOnlyList<DaggerfallDungeonEnemyPlacement> Enemies)
{
    internal static DaggerfallDungeonPopulationContent Empty { get; } = new([], []);

    internal static DaggerfallDungeonPopulationContent Read(ReadOnlyMemory<byte> normalized, DaggerfallContentDiagnostics diagnostics)
    {
        using var document = JsonDocument.Parse(normalized);
        var world = DaggerfallBaseContent.Property(document.RootElement, "world", diagnostics);
        List<DaggerfallDungeonTreasurePlacement> treasures = [];
        List<DaggerfallDungeonEnemyPlacement> enemies = [];
        foreach (var marker in DaggerfallBaseContent.Array(world, "treasures", diagnostics))
            treasures.Add(new(DaggerfallBaseContent.Text(marker, "id", diagnostics), Position(marker)));
        foreach (var marker in DaggerfallBaseContent.Array(world, "randomEnemies", diagnostics))
        {
            var enemy = new DaggerfallDungeonEnemyPlacement(DaggerfallBaseContent.Text(marker, "id", diagnostics), Position(marker),
                DaggerfallBaseContent.Integer(marker, "encounterSlot", diagnostics), DaggerfallBaseContent.Integer(marker, "spawnDistance", diagnostics),
                DaggerfallBaseContent.Boolean(marker, "passive", diagnostics), DaggerfallBaseContent.Boolean(marker, "underwater", diagnostics),
                DaggerfallBaseContent.Boolean(marker, "aquaticSpawnAllowed", diagnostics));
            if (enemy.EncounterSlot is < 0 or > 255 || enemy.SpawnDistance is < 0 or > 6)
                diagnostics.Add($"Dungeon enemy '{enemy.Id}' has invalid selection metadata.");
            enemies.Add(enemy);
        }
        if (treasures.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != treasures.Count
            || enemies.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != enemies.Count)
            diagnostics.Add("Dungeon population marker identities must be distinct.");
        return new(treasures, enemies);

        WorldPoint Position(JsonElement marker)
        {
            var point = DaggerfallBaseContent.Property(marker, "position", diagnostics);
            return new(DaggerfallBaseContent.Number(point, "x", diagnostics), DaggerfallBaseContent.Number(point, "y", diagnostics),
                DaggerfallBaseContent.Number(point, "z", diagnostics));
        }
    }
}
