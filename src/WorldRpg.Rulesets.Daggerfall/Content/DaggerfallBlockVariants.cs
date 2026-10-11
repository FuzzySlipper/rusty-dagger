using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>A compiled block replacement may carry a block variant and several independently selected building variants.</summary>
internal sealed record DaggerfallBlockVariantSelection(string? Block, IReadOnlyDictionary<int, string> Buildings)
{
    internal string Key => (Block ?? "") + "|" + string.Join(";", Buildings.OrderBy(value => value.Key).Select(value => $"{value.Key}:{value.Value}"));
    internal bool Empty => Block is null && Buildings.Count == 0;
}

internal sealed partial class DaggerfallWorldBlocks
{
    private readonly Dictionary<(DaggerfallWorldBlockKey Block, string Selection), IndexEntry> _variants = [];

    private void ReadVariants(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("variants", out var entries)) return;
        foreach (var value in entries.EnumerateArray())
        {
            var kind = Kind(DaggerfallBaseContent.Text(value, "kind", diagnostics));
            string source = DaggerfallBaseContent.Text(value, "sourceKey", diagnostics);
            int? building = DaggerfallBaseContent.OptionalInteger(value, "buildingIndex", diagnostics);
            string? blockVariant = DaggerfallBaseContent.OptionalText(value, "variant", diagnostics);
            Dictionary<int, string> buildings = [];
            if (value.TryGetProperty("buildingVariants", out var selected))
                foreach (var entry in selected.EnumerateArray())
                {
                    int index = DaggerfallBaseContent.Integer(entry, "building", diagnostics);
                    string variant = DaggerfallBaseContent.Text(entry, "variant", diagnostics);
                    if (index < 0 || !DaggerfallBaseContent.ValidId(variant) || variant == "-" || !buildings.TryAdd(index, variant))
                        diagnostics.Add($"Block variant '{source}' has an invalid or repeated building selection.");
                }
            var selection = new DaggerfallBlockVariantSelection(blockVariant, buildings);
            if (kind is null || selection.Empty || blockVariant is not null && (!DaggerfallBaseContent.ValidId(blockVariant) || blockVariant == "-")
                || (kind == DaggerfallWorldBlockKind.RmbInterior) != building.HasValue
                || kind == DaggerfallWorldBlockKind.Rdb && buildings.Count != 0
                || kind == DaggerfallWorldBlockKind.RmbInterior && buildings.Keys.Any(index => index != building))
            { diagnostics.Add($"Block variant '{source}' has an invalid selection or kind."); continue; }
            var key = new DaggerfallWorldBlockKey(kind.Value, source, building);
            var entryValue = new IndexEntry(DaggerfallBaseContent.Text(value, "key", diagnostics),
                DaggerfallBaseContent.Text(value, "document", diagnostics), DaggerfallBaseContent.OptionalText(value, "spatial", diagnostics));
            if (!_variants.TryAdd((key, selection.Key), entryValue)) diagnostics.Add($"Block '{source}' repeats variant selection '{selection.Key}'.");
        }
    }

    internal bool HasVariant(DaggerfallWorldBlockKey key, DaggerfallBlockVariantSelection selection)
    {
        lock (_gate)
            if (_index is not null) return selection.Empty ? _index.ContainsKey(key) : _variants.ContainsKey((key, selection.Key));
        using var bundle = Open();
        _ = RequireIndex(bundle);
        return selection.Empty ? _index!.ContainsKey(key) : _variants.ContainsKey((key, selection.Key));
    }
}

internal sealed partial class DaggerfallSiteProfiles
{
    private readonly record struct BlockSelectionKey(DaggerfallSiteId? Site, string Block, int? Building);
    private readonly Dictionary<BlockSelectionKey, string> _blockVariants = [];
    private readonly Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteProfile> _assembledVariants = [];

    /// <summary>
    /// Records a block or building selection. Like the donor, a selection whose content is not published still
    /// holds (and is saved): affected blocks assemble from their base documents, reported by one warning.
    /// </summary>
    internal void SetBlockVariant(DaggerfallSiteId? site, string block, int? building, string variant)
    {
        if (building < 0 || !DaggerfallBaseContent.ValidId(variant)) throw new ArgumentException("Invalid world block variant selection.");
        var key = new BlockSelectionKey(site, block.ToUpperInvariant(), building);
        if (variant == "-") _blockVariants.Remove(key); else _blockVariants[key] = variant;
        _assembledVariants.Clear();
        if (variant != "-") WarnMissingBlockVariants(key);
    }

    private void WarnMissingBlockVariants(BlockSelectionKey key)
    {
        IReadOnlyList<string> missing = _catalog.Assembly is { } assembly
            ? assembly.MissingBlockVariants(key.Site, key.Block, key.Building, (location, part) => SelectBlock(_blockVariants, location, part))
            : ["no normalized world assembly is admitted"];
        if (missing.Count != 0)
            System.Diagnostics.Trace.TraceWarning($"World variant for block '{key.Block}'{(key.Building is { } index ? $" building {index}" : "")} "
                + $"at {(key.Site?.ToString() ?? "any location")} falls back to base content: {string.Join("; ", missing)}.");
    }

    private static DaggerfallBlockVariantSelection SelectBlock(IReadOnlyDictionary<BlockSelectionKey, string> selected,
        DaggerfallSiteId site, DaggerfallWorldBlockKey block)
    {
        string? Find(int? building) => selected.GetValueOrDefault(new(site, block.SourceKey.ToUpperInvariant(), building))
            ?? selected.GetValueOrDefault(new(null, block.SourceKey.ToUpperInvariant(), building));
        Dictionary<int, string> buildings = [];
        foreach (int building in selected.Keys.Where(value => value.Block == block.SourceKey.ToUpperInvariant()
            && (value.Site is null || value.Site == site) && value.Building is not null).Select(value => value.Building!.Value).Distinct())
            if (block.Kind == DaggerfallWorldBlockKind.RmbExterior || block.Kind == DaggerfallWorldBlockKind.RmbInterior && block.BuildingIndex == building)
                if (Find(building) is { } variant) buildings.Add(building, variant);
        return new(Find(null), buildings);
    }

    private bool TryAssembledVariant(DaggerfallWorldProfileKey key, out DaggerfallSiteProfile profile)
    {
        profile = null!;
        if (_blockVariants.Count == 0 || _catalog.Assembly is not { } assembly || !assembly.Places(key)
            || !assembly.HasBlockVariants(key, part => SelectBlock(_blockVariants, key.Site, part))) return false;
        if (_assembledVariants.TryGetValue(key, out profile!)) return true;
        profile = assembly.Assemble(key, part => SelectBlock(_blockVariants, key.Site, part));
        _catalog.AdmitAssembledVariant(key, profile);
        _assembledVariants.Add(key, profile);
        return true;
    }
}
