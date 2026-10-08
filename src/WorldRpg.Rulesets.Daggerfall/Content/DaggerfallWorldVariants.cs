using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallWorldVariantSave(int Region, int Location, string Variant);

internal sealed partial class DaggerfallSiteProfiles
{
    private readonly Dictionary<(DaggerfallWorldProfileKey Profile, string Variant), DaggerfallSiteProfile> _variants = [];
    private readonly Dictionary<DaggerfallSiteId, string> _selectedVariants = [];

    private void AdmitVariants(IEnumerable<DaggerfallSiteProfile> variants)
    {
        foreach (var variant in variants)
        {
            if (variant.VariantBaseLogicalId is null || !DaggerfallBaseContent.ValidId(variant.VariantName!) || variant.VariantName == "-")
                throw new ArgumentException("A world variant requires a stable variant name and its base logical profile.");
            var key = variant.ProfileKey;
            if (!_profiles.TryGetValue(key, out var original)) throw new ArgumentException($"World variant '{variant.VariantName}' has no base profile '{key.LogicalId}'.");
            // Current-state actors and action graphs remain valid across a scenery replacement.
            // Reject a different gameplay topology rather than silently dropping retained state.
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
                throw new NotSupportedException($"World variant '{variant.VariantName}' changes retained actor or action topology; scenery variants must preserve these owners.");
            if (!original.Audio.SequenceEqual(variant.Audio))
                throw new NotSupportedException($"World variant '{variant.VariantName}' changes the retained audio; location scenery variants currently preserve its audio.");
            if (!_variants.TryAdd((key, variant.VariantName!), variant)) throw new ArgumentException($"Duplicate world variant '{variant.VariantName}' for '{key.LogicalId}'.");
        }
        foreach (var group in _variants.Keys.GroupBy(value => (value.Profile.Site, value.Variant)))
            if (!_profiles.Keys.Where(key => key.Site == group.Key.Site).ToHashSet().SetEquals(group.Select(value => value.Profile)))
                throw new ArgumentException($"Location variant '{group.Key.Variant}' must publish every admitted profile of '{group.Key.Site}'.");
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

    internal DaggerfallSiteProfiles ForSession(DaggerfallWorldVariantSave[]? saved = null)
    {
        var result = new DaggerfallSiteProfiles(_profiles.Values.Concat(_variants.Values));
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
        if (!_profiles.Keys.Any(key => key.Site == site)) throw new NotSupportedException($"WorldUpdate location '{site}' has no admitted profiles.");
        if (variant == "-") { _selectedVariants.Remove(site); return; }
        if (!_variants.Keys.Any(key => key.Profile.Site == site && key.Variant == variant))
            throw new NotSupportedException($"WorldUpdate location '{site}' has no admitted variant '{variant}'.");
        _selectedVariants[site] = variant;
    }

    internal DaggerfallWorldVariantSave[] CaptureVariants() => _selectedVariants.OrderBy(value => value.Key.Region).ThenBy(value => value.Key.Index)
        .Select(value => new DaggerfallWorldVariantSave(value.Key.Region, value.Key.Index, value.Value)).ToArray();
}
