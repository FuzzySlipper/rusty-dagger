using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The person across the counter, which the spellbook's panes share: who offers a service in the building the player
/// stands in and at what quality, the summoner, and the open conversation page those panes are drawn against.
/// </summary>
/// <param name="dialogue">The conversation the player is in, which activation rebuilds with each site.</param>
/// <param name="building">The interior building the player stands in.</param>
/// <param name="active">The active profile's key, whose site holds the building.</param>
internal sealed class DaggerfallServiceCounter(Func<DaggerfallDialogueService?> dialogue, Func<DaggerfallInteriorBuilding?> building,
    DaggerfallSiteContext site, Func<DaggerfallWorldProfileKey> active, DaggerfallActivationPresentation activation)
{
    /// <summary>The conversation page the panes are drawn against; present whenever a provider is.</summary>
    internal DaggerfallDialogueView Page => activation.View.Dialogue!;

    /// <summary>The person offering <paramref name="service"/> in this building, with the building's quality.</summary>
    internal (DaggerfallServiceProvider Provider, int Quality)? Provider(string? revision = null, string service = "buy-spells")
    {
        var npc = dialogue()?.CurrentNpc(revision);
        var current = building();
        if (npc is null || current is null || !npc.Services.Contains(service, StringComparer.Ordinal)) return null;
        var source = site.RequireBuildingSource(active().Site, new(current.BlockX, current.BlockY, current.Building.Index));
        return (new(npc.DurableId, npc.Site, service), source.Quality);
    }

    /// <summary>The person who summons daedra, when the player talks to one.</summary>
    internal DaggerfallServiceProvider? Summoner(string? revision = null)
    {
        var npc = dialogue()?.CurrentNpc(revision);
        return npc?.Services.Contains("daedra-summoning", StringComparer.Ordinal) == true ? new(npc.DurableId, npc.Site, "daedra-summoning") : null;
    }
}
