using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed partial class DaggerfallLocationAssembly
{
    private IEnumerable<DaggerfallWorldBlockKey> ProfileBlocks(DaggerfallWorldProfileKey key)
    {
        if (!TryRecord(key.Site, out var record)) yield break;
        if (key.Kind == DaggerfallWorldProfileKind.Dungeon)
        {
            foreach (var block in record!.DungeonBlocks) yield return new(DaggerfallWorldBlockKind.Rdb, block.SourceKey);
        }
        else if (record!.Exterior is { } exterior)
        {
            if (key.Kind == DaggerfallWorldProfileKind.Exterior)
                foreach (var block in exterior.Blocks) yield return new(DaggerfallWorldBlockKind.RmbExterior, block.SourceName);
            else if (DaggerfallWorldProfileIds.TryParse(key.LogicalId, out _, out var building) && building is { } placed)
            {
                var block = exterior.Blocks.Single(value => value.X == placed.BlockX && value.Y == placed.BlockY);
                yield return new(DaggerfallWorldBlockKind.RmbInterior, block.SourceName, placed.Index);
            }
        }
    }

    internal bool HasBlockVariants(DaggerfallWorldProfileKey profile, Func<DaggerfallWorldBlockKey, DaggerfallBlockVariantSelection> select) =>
        ProfileBlocks(profile).Any(key => !select(key).Empty);

    internal void RequireBlockVariants(DaggerfallSiteId? site, string source, int? building,
        Func<DaggerfallSiteId, DaggerfallWorldBlockKey, DaggerfallBlockVariantSelection> select)
    {
        bool found = false;
        HashSet<(DaggerfallWorldBlockKey, string)> checkedSelections = [];
        foreach (var location in _records.Value.Values.Where(value => site is null || value.Id == site))
        {
            List<DaggerfallWorldBlockKey> keys = [];
            if (building is null && location.DungeonBlocks.Any(value => value.SourceKey == source)) keys.Add(new(DaggerfallWorldBlockKind.Rdb, source));
            if (location.Exterior is { } exterior && exterior.Blocks.Any(value => value.SourceName == source))
            {
                var indices = exterior.Buildings.Values.Where(value => value.Source.Id.SourceKey == source).Select(value => value.Id.Index).Distinct().ToArray();
                if (building is null || indices.Contains(building.Value))
                {
                    keys.Add(new(DaggerfallWorldBlockKind.RmbExterior, source));
                    foreach (int index in indices.Where(index => building is null || building == index))
                    {
                        var key = new DaggerfallWorldBlockKey(DaggerfallWorldBlockKind.RmbInterior, source, index);
                        if (_blocks.Contains(key)) keys.Add(key);
                    }
                }
            }
            foreach (var key in keys)
            {
                found = true;
                var selection = select(location.Id, key);
                if (checkedSelections.Add((key, selection.Key)) && !_blocks.HasVariant(key, selection))
                    throw new NotSupportedException($"WorldUpdate '{source}' at '{location.Id}' requires published {key.Kind} variant composition '{selection.Key}'.");
            }
        }
        if (!found) throw new NotSupportedException($"WorldUpdate block '{source}' building '{building}' is not placed at {(site is null ? "any location" : site.ToString())}.");
    }
}
