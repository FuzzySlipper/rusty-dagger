using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallWorldVariantSave(int Region, int Location, string Variant)
{
    [System.Text.Json.Serialization.JsonRequired] public string Kind { get; init; } = "location";
    public string? Block { get; init; }
    public int? Building { get; init; }

    internal void Validate()
    {
        bool global = Region == -1 && Location == -1;
        if (!DaggerfallBaseContent.ValidId(Variant) || Variant == "-"
            || (!global && (Region < 0 || Location < 0))
            || Kind is not ("location" or "block" or "building")
            || Kind == "location" && (global || Block is not null || Building is not null)
            || Kind != "location" && (string.IsNullOrWhiteSpace(Block) || Block != Block.ToUpperInvariant())
            || Kind == "block" && Building is not null || Kind == "building" && (Building is null || Building < 0))
            throw new ArgumentException("Saved world variants require a valid location, block or building selection.");
    }
}

internal sealed partial class DaggerfallSiteProfiles
{
    private readonly Dictionary<DaggerfallSiteId, string> _selectedVariants = [];

    /// <summary>
    /// A scenery variant may replace how a location looks but not what the session retains about it. A
    /// variant that changes retained actor or action topology is refused rather than silently dropping state.
    /// </summary>
    private static void RequireSameTopology(DaggerfallSiteProfile original, DaggerfallSiteProfile variant, string name)
    {
        if (!original.Project.Actors.OrderBy(value => value.Key).SequenceEqual(variant.Project.Actors.OrderBy(value => value.Key))
            || !original.QuestMarkers.SequenceEqual(variant.QuestMarkers)
            || !original.DungeonActions.SequenceEqual(variant.DungeonActions)
            || !original.Doors.Select(value => value with { Visual = null }).SequenceEqual(variant.Doors.Select(value => value with { Visual = null }))
            || !original.Population.SequenceEqual(variant.Population)
            || !SameMap(original.DungeonMap, variant.DungeonMap)
            || original.PropertyContainers.Count != variant.PropertyContainers.Count
            || original.PropertyContainers.Zip(variant.PropertyContainers).Any(pair => pair.First.Id != pair.Second.Id
                || pair.First.Position != pair.Second.Position || !pair.First.ItemGroups.SequenceEqual(pair.Second.ItemGroups)
                || !pair.First.InteractionPoints.SequenceEqual(pair.Second.InteractionPoints))
            || original.StaticNpcs.Count != variant.StaticNpcs.Count
            || original.StaticNpcs.Zip(variant.StaticNpcs).Any(pair => pair.First.Id != pair.Second.Id || pair.First.Position != pair.Second.Position
                || pair.First.Appearance != pair.Second.Appearance || pair.First.Role != pair.Second.Role || !pair.First.Services.SequenceEqual(pair.Second.Services)))
            throw new NotSupportedException($"World variant '{name}' changes retained actor or action topology; scenery variants must preserve these owners.");
        if (!original.Audio.SequenceEqual(variant.Audio))
            throw new NotSupportedException($"World variant '{name}' changes the retained audio; location scenery variants currently preserve its audio.");
    }

    private static bool SameMap(DaggerfallDungeonMapContent? first, DaggerfallDungeonMapContent? second)
    {
        if (first is null || second is null) return first is null && second is null;
        return first.DoorIds.SequenceEqual(second.DoorIds) && first.Markers.SequenceEqual(second.Markers)
            && first.GeometryPlacements.Count == second.GeometryPlacements.Count
            && first.GeometryPlacements.Zip(second.GeometryPlacements).All(pair => pair.First.PlacementId == pair.Second.PlacementId
                && pair.First.BoundsMin == pair.Second.BoundsMin && pair.First.BoundsMax == pair.Second.BoundsMax && pair.First.DoorId == pair.Second.DoorId
                && pair.First.MeshIds.SequenceEqual(pair.Second.MeshIds) && pair.First.SamplePoints.SequenceEqual(pair.Second.SamplePoints));
    }

    /// <summary>
    /// This session's view of the shared catalog: the same resolved profiles, with its own selected variants. A
    /// saved selection whose content is absent loads, falls back to base content and is reported by a warning.
    /// </summary>
    internal DaggerfallSiteProfiles ForSession(DaggerfallWorldVariantSave[]? saved = null)
    {
        var result = new DaggerfallSiteProfiles(_catalog);
        foreach (var entry in saved ?? [])
        {
            var site = new DaggerfallSiteId(entry.Region, entry.Location);
            if (entry.Kind == "location") result.SetLocationVariant(site, entry.Variant);
            else result._blockVariants[new(entry.Region == -1 ? null : site, entry.Block!, entry.Building)] = entry.Variant;
        }
        // Check the complete saved combination, rather than the intermediate selections.
        foreach (var key in result._blockVariants.Keys) result.WarnMissingBlockVariants(key);
        return result;
    }

    /// <summary>
    /// Selects a location variant. It combines with block and building selections: an admitted location variant
    /// supplies the profiles it publishes, and block selections apply to every profile assembled from blocks.
    /// As in the donor, a variant without published content still holds and the base location shows.
    /// </summary>
    internal void SetLocationVariant(DaggerfallSiteId site, string variant)
    {
        if (variant == "-") { _selectedVariants.Remove(site); return; }
        if (!_catalog.HasVariant(site, variant))
            System.Diagnostics.Trace.TraceWarning($"World location '{site}' has no admitted variant '{variant}'; its base content remains.");
        _selectedVariants[site] = variant;
    }

    internal DaggerfallWorldVariantSave[] CaptureVariants() => _selectedVariants.OrderBy(value => value.Key.Region).ThenBy(value => value.Key.Index)
        .Select(value => new DaggerfallWorldVariantSave(value.Key.Region, value.Key.Index, value.Value))
        .Concat(_blockVariants.OrderBy(value => value.Key.Site?.Region ?? -1).ThenBy(value => value.Key.Site?.Index ?? -1)
            .ThenBy(value => value.Key.Block, StringComparer.Ordinal).ThenBy(value => value.Key.Building)
            .Select(value => new DaggerfallWorldVariantSave(value.Key.Site?.Region ?? -1, value.Key.Site?.Index ?? -1, value.Value)
            { Kind = value.Key.Building is null ? "block" : "building", Block = value.Key.Block, Building = value.Key.Building })).ToArray();
}
