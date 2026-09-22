using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>How a faction reputation change follows the catalog's authored relationships.</summary>
internal enum DaggerfallFactionReputationChange
{
    Direct,
    Propagate,
}

/// <summary>One guild membership under the donor's social-group exclusivity rule.</summary>
internal sealed record DaggerfallGuildMembership(int GuildGroup, int FactionId, int Rank, int LastRankChangeDay, int NotedByGuild);

/// <summary>One current guild affiliation as it appears on the character sheet.</summary>
internal sealed record DaggerfallSocialAffiliationView(string Faction, string GuildGroup, int Rank, int Reputation, int Recognition);

/// <summary>The reusable reaction answer for a faction, NPC talk, a service, or a quest condition.</summary>
internal readonly record struct DaggerfallFactionReaction(int FactionId, int FactionReputation, int PersonalReputation)
{
    internal int Value => FactionReputation + PersonalReputation;
}

/// <summary>The shared guild eligibility answer; service and quest policy supply their own thresholds.</summary>
internal readonly record struct DaggerfallGuildEligibility(bool IsGuild, bool IsMember, int Rank, int Reputation)
{
    internal bool Meets(int minimumRank, int minimumReputation) =>
        IsGuild && IsMember && Rank >= minimumRank && Reputation >= minimumReputation;
}

/// <summary>
/// Persistent player social standing over the admitted faction catalog.  It deliberately has no
/// dependency on materialized NPC entities: talk, services and quests ask by faction identity, and
/// an NPC only supplies that identity when one is currently present.  The donor's mutable faction
/// standing, player social-group standing, regional legal standing, and one membership per guild
/// group have distinct records rather than overloaded quest variables.
/// </summary>
internal sealed class DaggerfallSocialState
{
    internal const int MinimumReputation = -100;
    internal const int MaximumReputation = 100;
    internal const int SocialGroupCount = 11;
    internal const int MinimumPersonalReputation = short.MinValue;
    internal const int MaximumPersonalReputation = short.MaxValue;
    internal const int MaximumGuildRank = 9;

    // Donor FactionFile/FactionIDs and GuildNpcServices identities used by PersistentFactionData.
    private const int DarkBrotherhoodFactionId = 108;
    private const int GenericTempleFactionId = 450;
    private const int GenericKnightlyOrderFactionId = 844;
    private const int GodFactionType = 1;
    private static readonly HashSet<int> QuestorFactionIds = [63, 851, 804, 807, 240, 846];
    internal const long NormalizeIntervalMinutes = 112L * 24L * 60L;

    private readonly DaggerfallFactionsSet _catalog;
    private readonly Dictionary<int, int> _factionReputations;
    private readonly Dictionary<int, int> _regionalReputations = [];
    private readonly Dictionary<int, int> _personalReputations = [];
    private readonly Dictionary<int, DaggerfallGuildMembership> _memberships = [];
    private int _biographyReactionModifier;

    internal DaggerfallSocialState(DaggerfallFactionsSet catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _factionReputations = catalog.Factions.Values.ToDictionary(faction => faction.Id, faction => Clamp(faction.Reputation));
    }

    internal int FactionReputation(int factionId) => _factionReputations[RequireFaction(factionId).Id];

    internal int RegionalReputation(int region)
    {
        RequireRegion(region);
        return _regionalReputations.GetValueOrDefault(region);
    }

    /// <summary>The player record's standing with one donor social group, independent of any NPC instance.</summary>
    internal int PersonalReputation(int socialGroup)
    {
        RequireSocialGroup(socialGroup);
        return _personalReputations.GetValueOrDefault(socialGroup);
    }

    /// <summary>
    /// Computes the persistent donor talk baseline from faction, social-group standing, and BIOG.
    /// </summary>
    internal DaggerfallFactionReaction ReactionForFaction(int factionId)
    {
        DaggerfallFactionDefinition faction = RequireFaction(factionId);
        int personal = faction.SocialGroup is >= 0 and < SocialGroupCount
            ? PersonalReputation(faction.SocialGroup)
            : 0;
        return new(faction.Id, FactionReputation(faction.Id), checked(personal + _biographyReactionModifier));
    }

    /// <summary>Sets the selected BIOG reaction assignment reconstructed from the character save.</summary>
    internal void SetBiographyReactionModifier(int value) => _biographyReactionModifier = value;

    /// <summary>Computes the same reaction for an NPC whether or not it is currently active in the world.</summary>
    internal DaggerfallFactionReaction ReactionForNpc(DaggerfallNpc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        // NPC identity deliberately permits faction zero for an unaffiliated civilian; those people
        // have no faction standing to contribute and answer a neutral social baseline.
        return npc.Appearance.FactionId == 0 ? new(0, 0, 0) : ReactionForFaction(npc.Appearance.FactionId);
    }

    /// <summary>Changes player standing with a social group and clamps it to the current social contract.</summary>
    internal int ChangePersonalReputation(int socialGroup, int amount)
    {
        RequireSocialGroup(socialGroup);
        // PlayerEntity stores these as signed shorts; unlike faction/legal reputation the donor
        // does not normalize or clamp them to the -100..100 standing range.
        int value = Math.Clamp(checked(PersonalReputation(socialGroup) + amount), MinimumPersonalReputation, MaximumPersonalReputation);
        _personalReputations[socialGroup] = value;
        return value;
    }

    /// <summary>Changes regional legal standing; the current social owner enforces the donor's bounds.</summary>
    internal int ChangeRegionalReputation(int region, int amount)
    {
        RequireRegion(region);
        int value = Clamp(checked(RegionalReputation(region) + amount));
        _regionalReputations[region] = value;
        return value;
    }

    /// <summary>
    /// Changes faction standing directly or through the catalog's allies, enemies and hierarchy.
    /// Relationship changes happen first, then the hierarchy root, initiator and descendants receive
    /// the donor's full/half propagation values in deterministic catalog order.
    /// </summary>
    internal int ChangeFactionReputation(int factionId, int amount, DaggerfallFactionReputationChange change = DaggerfallFactionReputationChange.Direct)
    {
        DaggerfallFactionDefinition faction = RequireFaction(factionId);
        if (change == DaggerfallFactionReputationChange.Direct)
        {
            ApplyFactionChange(faction.Id, amount);
            return FactionReputation(faction.Id);
        }
        if (change != DaggerfallFactionReputationChange.Propagate)
            throw new ArgumentOutOfRangeException(nameof(change), change, "A faction reputation change names no supported propagation policy.");

        // PersistentFactionData.ChangeReputation visits ally[i], enemy[i] for each of its three
        // source slots. The normalized catalog preserves each relation list's filed order, so zip
        // those ordinal slots instead of grouping all allies before all enemies; clamped duplicate
        // links remain observably ordered.
        int linkSlots = Math.Max(faction.Allies.Count, faction.Enemies.Count);
        for (int slot = 0; slot < linkSlots; slot++)
        {
            if (slot < faction.Allies.Count && _catalog.Factions.ContainsKey(faction.Allies[slot]))
                ApplyFactionChange(faction.Allies[slot], amount / 2);
            if (slot < faction.Enemies.Count && _catalog.Factions.ContainsKey(faction.Enemies[slot]))
                ApplyFactionChange(faction.Enemies[slot], -(amount / 2));
        }

        // Classic makes knightly-order changes local, then adjusts Generic Knightly Order itself.
        // DFU deliberately extends this with generic child propagation; the compiled Daggerfall
        // ruleset targets classic behavior and therefore does not visit Smiths/Questers/etc. here.
        if (faction.GuildGroup == 9)
        {
            ApplyFactionChange(faction.Id, amount);
            if (_catalog.Factions.ContainsKey(GenericKnightlyOrderFactionId))
                ApplyFactionChange(GenericKnightlyOrderFactionId, amount);
            return FactionReputation(faction.Id);
        }

        // For all other groups, climb to the root except Dark Brotherhood, whose donor identity is
        // deliberately treated as a root despite having a parent.  A cycle cannot cause unbounded
        // propagation even when malformed catalog data reaches this current-state owner.
        DaggerfallFactionDefinition root = FindDonorPropagationRoot(faction);
        PropagateDonorHierarchy(root, faction.Id, amount, []);

        // A God root also changes the generic temple hierarchy.  This is a second donor propagation,
        // after the deity hierarchy, rather than a universal relationship inference.
        if (root.Type == GodFactionType && _catalog.Factions.ContainsKey(GenericTempleFactionId))
            ChangeFactionReputation(GenericTempleFactionId, amount, DaggerfallFactionReputationChange.Propagate);
        return FactionReputation(faction.Id);
    }

    internal DaggerfallGuildEligibility GuildEligibility(int factionId)
    {
        DaggerfallFactionDefinition faction = RequireFaction(factionId);
        bool isGuild = faction.GuildGroup > 0;
        if (!isGuild || !_memberships.TryGetValue(faction.GuildGroup, out DaggerfallGuildMembership? membership) || membership.FactionId != faction.Id)
            return new(isGuild, false, -1, FactionReputation(faction.Id));
        return new(true, true, membership.Rank, FactionReputation(faction.Id));
    }

    /// <summary>
    /// Reads the player's current guild affiliations without taking a persistence snapshot.  The
    /// sheet needs membership standing, while saves remain an explicit boundary owned by Capture.
    /// </summary>
    internal IReadOnlyList<DaggerfallSocialAffiliationView> ReadAffiliations() =>
        _memberships.Values.OrderBy(membership => membership.GuildGroup).Select(membership =>
        {
            DaggerfallFactionDefinition faction = RequireFaction(membership.FactionId);
            return new DaggerfallSocialAffiliationView(faction.Name, faction.GuildGroupName, membership.Rank,
                FactionReputation(faction.Id), membership.NotedByGuild);
        }).ToArray();

    /// <summary>Joins the faction's guild group at rank zero; a different variant must be left first.</summary>
    internal DaggerfallGuildMembership JoinGuild(int factionId, int currentDay)
    {
        DaggerfallFactionDefinition faction = RequireGuild(factionId);
        if (_memberships.TryGetValue(faction.GuildGroup, out DaggerfallGuildMembership? existing))
        {
            if (existing.FactionId != faction.Id)
                throw new InvalidOperationException($"Guild group {faction.GuildGroup} already has membership in faction {existing.FactionId}.");
            return existing;
        }

        DaggerfallGuildMembership membership = new(faction.GuildGroup, faction.Id, 0, currentDay, 0);
        _memberships.Add(faction.GuildGroup, membership);
        return membership;
    }

    internal DaggerfallGuildMembership PromoteGuild(int factionId, int currentDay) => ChangeGuildRank(factionId, 1, currentDay);
    internal DaggerfallGuildMembership DemoteGuild(int factionId, int currentDay) => ChangeGuildRank(factionId, -1, currentDay);

    /// <summary>Records work the current guild notices; the donor field is byte-bounded.</summary>
    internal DaggerfallGuildMembership ChangeGuildRecognition(int factionId, int amount)
    {
        DaggerfallGuildMembership membership = RequireMembership(factionId);
        DaggerfallGuildMembership changed = membership with { NotedByGuild = Math.Clamp(checked(membership.NotedByGuild + amount), 0, byte.MaxValue) };
        _memberships[membership.GuildGroup] = changed;
        return changed;
    }

    /// <summary>Expulsion removes membership as the donor GuildManager does; no hidden parallel flag remains.</summary>
    internal bool ExpelGuild(int factionId)
    {
        DaggerfallGuildMembership membership = RequireMembership(factionId);
        return _memberships.Remove(membership.GuildGroup);
    }

    /// <summary>Moves only the donor's faction and regional standing one point toward zero at each 112-day boundary.</summary>
    internal void AdvanceElapsedMinutes(long minuteBefore, long minuteAfter)
    {
        if (minuteAfter < minuteBefore) throw new ArgumentOutOfRangeException(nameof(minuteAfter));
        // DaggerfallCalendar deliberately admits dates before its nominal start, whose DayNumber
        // and minute index are negative. Division must floor around zero so crossing any exact
        // 112-day boundary, including -interval and zero, normalizes exactly once.
        long first = FloorInterval(minuteBefore);
        long last = FloorInterval(minuteAfter);
        for (long interval = first; interval < last; interval++) NormalizeReputations();
    }

    internal DaggerfallSocialSave Capture() => new(
        [.. _factionReputations.OrderBy(entry => entry.Key).Select(entry => new DaggerfallFactionReputationSave(entry.Key, entry.Value))],
        [.. _regionalReputations.OrderBy(entry => entry.Key).Select(entry => new DaggerfallRegionalReputationSave(entry.Key, entry.Value))],
        [.. _personalReputations.OrderBy(entry => entry.Key).Select(entry => new DaggerfallPersonalReputationSave(entry.Key, entry.Value))],
        [.. _memberships.Values.OrderBy(entry => entry.GuildGroup).Select(entry => new DaggerfallGuildMembershipSave(entry.GuildGroup, entry.FactionId, entry.Rank, entry.LastRankChangeDay, entry.NotedByGuild))]);

    internal void Restore(DaggerfallSocialSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate(_catalog);
        // Validate() requires the complete current catalog set, so no missing entry can silently
        // fall back to an authored default during a development-schema restore.
        _factionReputations.Clear();
        foreach (DaggerfallFactionReputationSave entry in saved.Factions)
            _factionReputations.Add(entry.FactionId, entry.Value);
        _regionalReputations.Clear();
        foreach (DaggerfallRegionalReputationSave entry in saved.Regions)
            _regionalReputations.Add(entry.Region, entry.Value);
        _personalReputations.Clear();
        foreach (DaggerfallPersonalReputationSave entry in saved.Personal)
            _personalReputations.Add(entry.SocialGroup, entry.Value);
        _memberships.Clear();
        foreach (DaggerfallGuildMembershipSave entry in saved.Memberships)
            _memberships.Add(entry.GuildGroup, new(entry.GuildGroup, entry.FactionId, entry.Rank, entry.LastRankChangeDay, entry.NotedByGuild));
    }

    private void NormalizeReputations()
    {
        foreach (int factionId in _factionReputations.Keys.ToArray())
            _factionReputations[factionId] = MoveTowardZero(_factionReputations[factionId]);
        foreach (int region in _regionalReputations.Keys.ToArray())
            _regionalReputations[region] = MoveTowardZero(_regionalReputations[region]);
    }

    private DaggerfallGuildMembership ChangeGuildRank(int factionId, int delta, int currentDay)
    {
        DaggerfallGuildMembership membership = RequireMembership(factionId);
        int rank = Math.Clamp(checked(membership.Rank + delta), 0, MaximumGuildRank);
        DaggerfallGuildMembership changed = membership with { Rank = rank, LastRankChangeDay = rank == membership.Rank ? membership.LastRankChangeDay : currentDay };
        _memberships[membership.GuildGroup] = changed;
        return changed;
    }

    private void ApplyFactionChange(int factionId, int amount) =>
        _factionReputations[factionId] = Clamp(checked(_factionReputations[factionId] + amount));

    private DaggerfallFactionDefinition FindDonorPropagationRoot(DaggerfallFactionDefinition faction)
    {
        HashSet<int> seen = [faction.Id];
        while (faction.Id != DarkBrotherhoodFactionId && faction.Parent != 0
            && _catalog.Factions.TryGetValue(faction.Parent, out DaggerfallFactionDefinition? parent) && seen.Add(parent.Id))
        {
            faction = parent;
        }
        return faction;
    }

    private void PropagateDonorHierarchy(DaggerfallFactionDefinition faction, int initiator, int amount, HashSet<int> visited)
    {
        if (!visited.Add(faction.Id)) return;
        // PersistentFactionData.PropagateReputationChange gives full standing to the initiator,
        // a parentless root, and the six guild questor service identities; all other descendants
        // receive half.  Preserve authored child order to retain donor propagation order.
        int applied = faction.Id == initiator || faction.Parent == 0 || QuestorFactionIds.Contains(faction.Id)
            ? amount
            : amount / 2;
        ApplyFactionChange(faction.Id, applied);
        foreach (int child in faction.Children)
            if (_catalog.Factions.TryGetValue(child, out DaggerfallFactionDefinition? definition)) PropagateDonorHierarchy(definition, initiator, amount, visited);
    }

    private DaggerfallFactionDefinition RequireFaction(int factionId) =>
        _catalog.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction)
            ? faction
            : throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "No admitted faction carries this identity.");

    private DaggerfallFactionDefinition RequireGuild(int factionId)
    {
        DaggerfallFactionDefinition faction = RequireFaction(factionId);
        if (faction.GuildGroup <= 0)
            throw new InvalidOperationException($"Faction {factionId} has no guild group.");
        return faction;
    }

    private DaggerfallGuildMembership RequireMembership(int factionId)
    {
        DaggerfallFactionDefinition faction = RequireGuild(factionId);
        if (!_memberships.TryGetValue(faction.GuildGroup, out DaggerfallGuildMembership? membership) || membership.FactionId != faction.Id)
            throw new InvalidOperationException($"The player is not a member of faction {factionId}.");
        return membership;
    }

    private void RequireRegion(int region)
    {
        if (region is < 0 or > 61 || !_catalog.Regions.ContainsKey(region))
            throw new ArgumentOutOfRangeException(nameof(region), region, "No admitted classic region carries this identity.");
    }

    private static void RequireSocialGroup(int socialGroup)
    {
        if (socialGroup is < 0 or >= SocialGroupCount)
            throw new ArgumentOutOfRangeException(nameof(socialGroup), socialGroup, "No donor social group carries this identity.");
    }

    private static long FloorInterval(long minute)
    {
        long quotient = Math.DivRem(minute, NormalizeIntervalMinutes, out long remainder);
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static int Clamp(int value) => Math.Clamp(value, MinimumReputation, MaximumReputation);
    private static int MoveTowardZero(int value) => value < 0 ? value + 1 : value > 0 ? value - 1 : 0;
}
