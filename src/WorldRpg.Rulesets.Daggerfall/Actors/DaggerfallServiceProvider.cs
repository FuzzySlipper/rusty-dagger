namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The exact Daggerfall service provider a request addresses.</summary>
internal sealed record DaggerfallServiceProvider(long NpcId, DaggerfallNpcSite Site, string Service)
{
    internal void Validate()
    {
        if (NpcId <= 0 || string.IsNullOrWhiteSpace(Service))
            throw new ArgumentException("A service provider requires an NPC and a named service.");
        if (Site.Region is < 0 or > 61 || string.IsNullOrWhiteSpace(Site.Location))
            throw new ArgumentException("A service provider requires a classic NPC site.");
    }
}
