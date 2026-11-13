using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallSiteIdSave(int? Region, int? Index)
{
    internal void Validate(string owner)
    {
        if (Region is not int region || Index is not int index || region < 0 || index < 0)
            throw new ArgumentException($"A saved {owner} must name a non-negative region and index.");
    }

    internal DaggerfallSiteId Require()
    {
        Validate("site");
        return new DaggerfallSiteId(Region!.Value, Index!.Value);
    }
}

/// <summary>Persisted projection identity; its geographic site remains separately owned by DaggerfallSiteContext.</summary>
internal sealed record DaggerfallWorldProfileKeySave(DaggerfallSiteIdSave Site, int Kind, string LogicalId)
{
    internal DaggerfallWorldProfileKey Require()
    {
        Validate();
        return new DaggerfallWorldProfileKey(Site.Require(), (DaggerfallWorldProfileKind)Kind, LogicalId).Validate();
    }
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Site); Site.Validate("world profile site");
        if (!Enum.IsDefined((DaggerfallWorldProfileKind)Kind) || string.IsNullOrWhiteSpace(LogicalId))
            throw new ArgumentException("Saved world profile identity is malformed.");
    }
    internal static DaggerfallWorldProfileKeySave Capture(DaggerfallWorldProfileKey key) =>
        new(new DaggerfallSiteIdSave(key.Site.Region, key.Site.Index), (int)key.Kind, key.LogicalId);
}
