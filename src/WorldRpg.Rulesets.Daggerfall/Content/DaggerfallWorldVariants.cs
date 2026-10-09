using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallWorldVariantSave(int Region, int Location, string Variant);

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

    /// <summary>This session's view of the shared catalog: the same resolved profiles, with its own selected variants.</summary>
    internal DaggerfallSiteProfiles ForSession(DaggerfallWorldVariantSave[]? saved = null)
    {
        var result = new DaggerfallSiteProfiles(_catalog);
        var seen = new HashSet<DaggerfallSiteId>();
        foreach (var entry in saved ?? [])
        {
            var site = new DaggerfallSiteId(entry.Region, entry.Location);
            if (!seen.Add(site) || entry.Variant == "-") throw new ArgumentException("Saved world variants must name one current variant per location.");
            result.SetLocationVariant(site, entry.Variant);
        }
        return result;
    }

    internal void SetLocationVariant(DaggerfallSiteId site, string variant)
    {
        if (!_catalog.HasProfilesAt(site)) throw new NotSupportedException($"WorldUpdate location '{site}' has no admitted profiles.");
        if (variant == "-") { _selectedVariants.Remove(site); return; }
        if (!_catalog.HasVariant(site, variant))
            throw new NotSupportedException($"WorldUpdate location '{site}' has no admitted variant '{variant}'.");
        _selectedVariants[site] = variant;
    }

    internal DaggerfallWorldVariantSave[] CaptureVariants() => _selectedVariants.OrderBy(value => value.Key.Region).ThenBy(value => value.Key.Index)
        .Select(value => new DaggerfallWorldVariantSave(value.Key.Region, value.Key.Index, value.Value)).ToArray();
}
