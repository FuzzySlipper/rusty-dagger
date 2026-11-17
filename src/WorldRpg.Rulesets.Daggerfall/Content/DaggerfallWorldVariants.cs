using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallWorldVariantSave(int Region, int Location, string Variant)
{
    internal void Validate()
    {
        if (!DaggerfallBaseContent.ValidId(Variant) || Variant == "-" || Region < 0 || Location < 0)
            throw new ArgumentException("Saved world variants require a valid location selection.");
    }
}

internal sealed partial class DaggerfallSiteProfiles
{
    private readonly Dictionary<DaggerfallSiteId, string> _selectedVariants = [];

    /// <summary>
    /// This session's view of the shared catalog: the same resolved profiles, with its own selected variants.
    /// </summary>
    internal DaggerfallSiteProfiles ForSession(DaggerfallWorldVariantSave[]? saved = null)
    {
        var result = new DaggerfallSiteProfiles(_catalog);
        foreach (var entry in saved ?? []) result.SetLocationVariant(new DaggerfallSiteId(entry.Region, entry.Location), entry.Variant);
        return result;
    }

    /// <summary>
    /// Selects a location variant, which a quest's world update names and the save keeps; the dash clears it. The location
    /// draws it from its next build, when this view next resolves it (<see cref="TryGet"/>). As in the donor, a variant
    /// no selected pack carries still holds, and the base location shows.
    /// </summary>
    internal void SetLocationVariant(DaggerfallSiteId site, string variant)
    {
        if (variant == "-") { _selectedVariants.Remove(site); return; }
        _selectedVariants[site] = variant;
    }

    internal DaggerfallWorldVariantSave[] CaptureVariants() => [.. _selectedVariants.OrderBy(value => value.Key.Region).ThenBy(value => value.Key.Index)
        .Select(value => new DaggerfallWorldVariantSave(value.Key.Region, value.Key.Index, value.Value))];
}
