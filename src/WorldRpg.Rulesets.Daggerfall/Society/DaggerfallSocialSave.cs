using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One mutable reputation entry for an admitted faction.</summary>
internal sealed record DaggerfallFactionReputationSave(int FactionId, int Value);

/// <summary>One mutable regional legal-reputation entry.</summary>
internal sealed record DaggerfallRegionalReputationSave(int Region, int Value);

/// <summary>One player social-group reputation entry.</summary>
internal sealed record DaggerfallPersonalReputationSave(int SocialGroup, int Value);

/// <summary>One guild-group membership and the compact counters the donor persists with it.</summary>
internal sealed record DaggerfallGuildMembershipSave(int GuildGroup, int FactionId, int Rank, int LastRankChangeDay, int NotedByGuild);

/// <summary>Meaningful social state, independently persisted from loaded NPC entities.</summary>
internal sealed record DaggerfallSocialSave(
    DaggerfallFactionReputationSave[] Factions,
    DaggerfallRegionalReputationSave[] Regions,
    DaggerfallPersonalReputationSave[] Personal,
    DaggerfallGuildMembershipSave[] Memberships)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Factions);
        ArgumentNullException.ThrowIfNull(Regions);
        ArgumentNullException.ThrowIfNull(Personal);
        ArgumentNullException.ThrowIfNull(Memberships);
        HashSet<int> factions = [];
        foreach (DaggerfallFactionReputationSave entry in Factions)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.FactionId < 0 || !factions.Add(entry.FactionId) || entry.Value is < DaggerfallSocialState.MinimumReputation or > DaggerfallSocialState.MaximumReputation)
                throw new ArgumentException("Saved faction reputation entries must name distinct non-negative factions within the social bounds.");
        }
        HashSet<int> regions = [];
        foreach (DaggerfallRegionalReputationSave entry in Regions)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.Region is < 0 or > 61 || !regions.Add(entry.Region) || entry.Value is < DaggerfallSocialState.MinimumReputation or > DaggerfallSocialState.MaximumReputation)
                throw new ArgumentException("Saved regional reputation entries must name distinct classic regions within the social bounds.");
        }
        HashSet<int> groups = [];
        foreach (DaggerfallPersonalReputationSave entry in Personal)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.SocialGroup is < 0 or >= DaggerfallSocialState.SocialGroupCount || !groups.Add(entry.SocialGroup) || entry.Value is < DaggerfallSocialState.MinimumPersonalReputation or > DaggerfallSocialState.MaximumPersonalReputation)
                throw new ArgumentException("Saved personal reputation entries must name distinct donor social groups within the signed-short source bounds.");
        }
        HashSet<int> guilds = [];
        foreach (DaggerfallGuildMembershipSave entry in Memberships)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.GuildGroup <= 0 || entry.FactionId < 0 || !guilds.Add(entry.GuildGroup)
                || entry.Rank is < 0 or > DaggerfallSocialState.MaximumGuildRank
                || entry.NotedByGuild is < 0 or > byte.MaxValue)
                throw new ArgumentException("Saved guild memberships must name distinct guild groups with valid faction, rank, day, and recognition values.");
        }
    }

    internal void Validate(DaggerfallFactionsSet catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Validate();
        HashSet<int> savedFactionIds = [.. Factions.Select(entry => entry.FactionId)];
        if (!savedFactionIds.SetEquals(catalog.Factions.Keys))
            throw new ArgumentException("Current social state must carry one faction reputation entry for every admitted faction.");
        foreach (DaggerfallRegionalReputationSave entry in Regions)
            if (!catalog.Regions.ContainsKey(entry.Region))
                throw new ArgumentException($"Saved regional reputation names absent region {entry.Region}.");
        foreach (DaggerfallGuildMembershipSave entry in Memberships)
        {
            if (!catalog.Factions.TryGetValue(entry.FactionId, out DaggerfallFactionDefinition? faction) || faction.GuildGroup != entry.GuildGroup)
                throw new ArgumentException($"Saved guild membership group {entry.GuildGroup} does not match admitted faction {entry.FactionId}.");
        }
    }
}
