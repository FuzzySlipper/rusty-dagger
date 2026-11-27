using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Resolved canonical home site and profile kind for a <see cref="DaggerfallCastIndividualDefinition"/>.</summary>
internal sealed record DaggerfallCastHomeLocation(
    DaggerfallSiteRecord Site,
    DaggerfallSiteBuildingSource? Building,
    DaggerfallWorldProfileKind ProfileKind);

/// <summary>
/// External override for a political or institutional office (for example from the political simulation / sparse
/// office ledger). Supplied via <see cref="DaggerfallOfficeHolders.BindOverrideLookup"/> so quest binding and text
/// macros automatically reflect successions, depositions, and deaths without rewriting callers.
/// </summary>
internal sealed record DaggerfallOfficeOverride(
    long? HolderActorId,
    string? HolderCastId,
    int? HolderFactionId,
    string DisplayName,
    string? Title = null,
    string? Gender = null,
    string? Race = null,
    int? BillboardArchive = null,
    int? BillboardRecord = null,
    string? PreviousHolderName = null,
    string? PreviousHolderFate = null,
    bool Vacant = false);

/// <summary>Resolved current holder of a regional throne or faction office, plus predecessor info if succession occurred.</summary>
internal sealed record DaggerfallOfficeHolder(
    int OfficeFactionId,
    int? HolderFactionId,
    long? ActorId,
    string? CastId,
    string DisplayName,
    string? Title,
    string? Gender,
    string? Race,
    int? BillboardArchive,
    int? BillboardRecord,
    DaggerfallCastHomeLocation? Home,
    bool IsAlive,
    string? PreviousHolderName = null,
    string? PreviousHolderFate = null,
    string? FamilyId = null)
{
    /// <summary>True only when the office is held by a living person (never a dead or vacant placeholder).</summary>
    internal bool HasLivingHolder => IsAlive && !string.IsNullOrWhiteSpace(DisplayName);

    /// <summary>
    /// The holder as named in public: the title before the name, unless the name already carries it, as a canonical
    /// ruler's (King Gothryd) and a provincial noble's names do.
    /// </summary>
    internal string StyledName => Styled(DisplayName, Title);

    /// <summary><paramref name="name"/> with <paramref name="title"/> before it, unless the name already begins with it.</summary>
    internal static string Styled(string name, string? title) =>
        string.IsNullOrWhiteSpace(title) || name.StartsWith($"{title} ", StringComparison.Ordinal) ? name : $"{title} {name}";

    internal static DaggerfallOfficeHolder Vacant(
        int officeFactionId,
        string? defaultTitle,
        string? previousHolderName,
        string? previousHolderFate) =>
        new(
            OfficeFactionId: officeFactionId,
            HolderFactionId: null,
            ActorId: null,
            CastId: null,
            DisplayName: string.Empty,
            Title: defaultTitle,
            Gender: null,
            Race: null,
            BillboardArchive: null,
            BillboardRecord: null,
            Home: null,
            IsAlive: false,
            PreviousHolderName: previousHolderName,
            PreviousHolderFate: previousHolderFate);
}

/// <summary>
/// Resolves canonical home locations for unique cast individuals (<c>cast/individuals.json</c>) against the site catalog.
/// </summary>
internal static class DaggerfallCastHomes
{
    /// <summary>
    /// Resolves <paramref name="cast"/>'s authored <see cref="DaggerfallCastIndividualDefinition.HomeBlock"/> to its
    /// concrete <see cref="DaggerfallSiteRecord"/> (and interior building when <c>.RMB</c>).
    /// </summary>
    internal static bool TryResolve(
        DaggerfallSiteContext? sites,
        DaggerfallCastIndividualDefinition cast,
        int? preferredRegion,
        out DaggerfallCastHomeLocation? home)
    {
        ArgumentNullException.ThrowIfNull(cast);
        home = null;
        if (sites is null || string.IsNullOrWhiteSpace(cast.HomeBlock))
            return false;

        // A home pinned to one site resolves there alone; an unpinned one to the first site placing its block.
        IEnumerable<DaggerfallSiteRecord> candidates = cast.HomeSite is { } pinned ? sites.Records.Where(site => site.Id == pinned) : sites.Records;
        if (cast.HomeBlock.EndsWith(".RDB", StringComparison.OrdinalIgnoreCase))
        {
            DaggerfallSiteRecord? dungeon = candidates
                .Where(site => site.DungeonBlocks.Any(block =>
                    string.Equals(block.SourceKey, cast.HomeBlock, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(site => preferredRegion is int region && site.Region == region ? 0 : 1)
                .ThenBy(site => site.Region)
                .ThenBy(site => site.Index)
                .FirstOrDefault();
            if (dungeon is null)
                return false;

            home = new DaggerfallCastHomeLocation(dungeon, null, DaggerfallWorldProfileKind.Dungeon);
            return true;
        }

        if (cast.HomeBlock.EndsWith(".RMB", StringComparison.OrdinalIgnoreCase))
        {
            int buildingIndex = ParseBuildingIndex(cast.HomeSlotId);
            foreach (DaggerfallSiteRecord site in candidates
                .OrderBy(record => preferredRegion is int region && record.Region == region ? 0 : 1)
                .ThenBy(record => record.Region)
                .ThenBy(record => record.Index))
            {
                if (site.Exterior is null)
                    continue;
                DaggerfallSiteBuildingSource? match = site.Exterior.Buildings.Values
                    .Where(building =>
                        string.Equals(building.Source.Id.SourceKey, cast.HomeBlock, StringComparison.OrdinalIgnoreCase)
                        && building.Source.Id.Index == buildingIndex)
                    .OrderBy(building => building.Id.BlockY)
                    .ThenBy(building => building.Id.BlockX)
                    .ThenBy(building => building.Id.Index)
                    .FirstOrDefault();
                if (match is not null)
                {
                    home = new DaggerfallCastHomeLocation(site, match, DaggerfallWorldProfileKind.Interior);
                    return true;
                }
            }
        }

        return false;
    }

    internal static DaggerfallWorldProfileKey? ResolveProfileKey(DaggerfallCastHomeLocation? home) =>
        home is null ? null
        : home.ProfileKind == DaggerfallWorldProfileKind.Dungeon
            ? DaggerfallWorldProfileIds.Dungeon(home.Site.Id)
            : home.Building is { } building
                ? DaggerfallWorldProfileIds.Interior(home.Site.Id, building.Id)
                : DaggerfallWorldProfileIds.Exterior(home.Site.Id);

    private static int ParseBuildingIndex(string? homeSlotId)
    {
        if (string.IsNullOrWhiteSpace(homeSlotId))
            return 0;
        // Format in cast/individuals.json for RMB interiors: "b:<buildingIndex>:<slot>" (e.g. "b:0:p00").
        string[] parts = homeSlotId.Split(':');
        return parts.Length >= 2 && string.Equals(parts[0], "b", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx)
            ? idx
            : 0;
    }
}

/// <summary>
/// Resolves current and previous office holders for regional rulers (provinces) and faction offices.
/// Defaults to the published faction hierarchy and unique cast catalog, tracks live <see cref="DaggerfallActorLedger"/>
/// death state for automatic succession, and accepts an optional override lookup so the political simulation / sparse
/// office ledger can supersede holders cleanly without rewriting quest or text consumers.
/// </summary>
internal sealed class DaggerfallOfficeHolders(
    DaggerfallDefinitions definitions,
    DaggerfallSiteContext? sites = null,
    DaggerfallActorLedger? ledger = null,
    DaggerfallNpcRegistry? npcs = null,
    Func<int, DaggerfallOfficeOverride?>? overrideLookup = null)
{
    private const int ProvinceFactionType = 7;
    private const int IndividualFactionType = 4;
    private static readonly string[] RulerTitles =
        ["King", "Queen", "Duke", "Duchess", "Marquis", "Marquise", "Count", "Countess", "Baron", "Baroness", "Lord", "Lady"];

    private Func<int, DaggerfallOfficeOverride?>? _overrideLookup = overrideLookup;
    private Func<int, DaggerfallOfficeHolder?>? _exiledRegionalRulerLookup;

    /// <summary>Attaches or clears the external office-ledger override lookup (used by the political layer).</summary>
    internal void BindOverrideLookup(Func<int, DaggerfallOfficeOverride?>? lookup) => _overrideLookup = lookup;

    /// <summary>Attaches or clears the external lookup for an exiled provincial ruler in a region.</summary>
    internal void BindExiledRegionalRulerLookup(Func<int, DaggerfallOfficeHolder?>? lookup) => _exiledRegionalRulerLookup = lookup;

    /// <summary>Resolves an exiled provincial ruler for <paramref name="region"/> when one is currently in exile.</summary>
    internal bool TryResolveExiledRegionalRuler(int region, out DaggerfallOfficeHolder? exiled)
    {
        exiled = _exiledRegionalRulerLookup?.Invoke(region);
        return exiled is { HasLivingHolder: true };
    }

    /// <summary>Returns true when <paramref name="actorId"/> is recorded dead in the canonical actor ledger.</summary>
    internal bool IsActorDead(long actorId) =>
        ledger is not null
        && ledger.TryGet(actorId, out ActorLedgerEntry? entry)
        && entry?.Location?.Kind == ActorLocationKind.Dead;

    /// <summary>Looks up a unique cast individual by its individual faction ID and resolves its home location.</summary>
    internal bool TryResolveCastIndividual(
        int factionId,
        int? preferredRegion,
        out DaggerfallCastIndividualDefinition? cast,
        out DaggerfallCastHomeLocation? home)
    {
        if (definitions.TryGetCastOrNobleByFaction(factionId, out cast) && cast is not null)
        {
            DaggerfallCastHomes.TryResolve(sites, cast, preferredRegion, out home);
            return true;
        }
        cast = null;
        home = null;
        return false;
    }

    /// <summary>Looks up a live or ledger-registered NPC record by durable ID when available.</summary>
    internal bool TryGetNpc(long actorId, out DaggerfallNpc? npc)
    {
        if (npcs is not null && npcs.TryGet(actorId, out DaggerfallNpc? found))
        {
            npc = found;
            return true;
        }
        npc = null;
        return false;
    }

    /// <summary>Resolves the province faction for <paramref name="region"/> and its current regional ruler.</summary>
    internal DaggerfallOfficeHolder? ResolveRegionalRuler(int region)
    {
        DaggerfallFactionDefinition? province = definitions.Factions.ProvinceOf(region);
        return province is null ? null : ResolveOffice(province.Id, region);
    }

    /// <summary>
    /// Resolves the current holder of <paramref name="officeFactionId"/> (province, court, guild, temple, or order).
    /// Checks any active override first, then walks the faction's published individual children in succession order,
    /// skipping dead cast holders and recording the predecessor for news/quest macros.
    /// </summary>
    internal DaggerfallOfficeHolder? ResolveOffice(int officeFactionId, int? preferredRegion = null)
    {
        if (officeFactionId <= 0)
            return null;

        definitions.Factions.Factions.TryGetValue(officeFactionId, out DaggerfallFactionDefinition? officeFaction);
        string? defaultTitle = DefaultTitle(officeFaction);
        string? deadPredecessor = null;
        string? deadPredecessorFate = null;

        if (_overrideLookup?.Invoke(officeFactionId) is { } over)
        {
            if (over.Vacant)
            {
                return DaggerfallOfficeHolder.Vacant(
                    officeFactionId,
                    over.Title ?? defaultTitle,
                    over.PreviousHolderName,
                    over.PreviousHolderFate);
            }

            DaggerfallOfficeHolder resolvedOverride = ResolveFromOverride(officeFactionId, over, defaultTitle, preferredRegion ?? officeFaction?.Region);
            if (resolvedOverride.HasLivingHolder)
                return resolvedOverride;

            // An overridden holder who has died cannot remain the current holder; record them as
            // predecessor and fall through to resolve a living successor (or represent a vacant office).
            if (!string.IsNullOrWhiteSpace(resolvedOverride.DisplayName))
            {
                deadPredecessor = resolvedOverride.DisplayName;
                deadPredecessorFate = "was slain";
            }
            else
            {
                deadPredecessor = resolvedOverride.PreviousHolderName;
                deadPredecessorFate = resolvedOverride.PreviousHolderFate;
            }
        }

        if (officeFaction is null)
            return deadPredecessor is not null
                ? DaggerfallOfficeHolder.Vacant(officeFactionId, defaultTitle, deadPredecessor, deadPredecessorFate)
                : null;

        List<DaggerfallFactionDefinition> individualChildren = [];
        if (officeFaction.Type == IndividualFactionType)
            individualChildren.Add(officeFaction);
        foreach (int childId in officeFaction.Children)
        {
            if (definitions.Factions.Factions.TryGetValue(childId, out DaggerfallFactionDefinition? child)
                && child.Type == IndividualFactionType)
            {
                individualChildren.Add(child);
            }
        }
        // Some major organizations (e.g. Dark Brotherhood -> The Night Mother) name their individual head as Parent.
        if (individualChildren.Count == 0
            && officeFaction.Parent > 0
            && definitions.Factions.Factions.TryGetValue(officeFaction.Parent, out DaggerfallFactionDefinition? parentFaction)
            && parentFaction.Type == IndividualFactionType)
        {
            individualChildren.Add(parentFaction);
        }

        int? regionHint = preferredRegion ?? (officeFaction.Region >= 0 ? officeFaction.Region : null);

        foreach (DaggerfallFactionDefinition candidate in individualChildren)
        {
            DaggerfallOfficeHolder holder = BuildFromIndividualFaction(officeFactionId, candidate, defaultTitle, regionHint, deadPredecessor, deadPredecessorFate);
            if (holder.HasLivingHolder)
                return holder;

            if (deadPredecessor is null && !string.IsNullOrWhiteSpace(holder.DisplayName))
            {
                deadPredecessor = holder.DisplayName;
                deadPredecessorFate = "was slain";
            }
        }

        // Canon-seeded provincial ruling lord and noble house (#11156) for provinces whose classic factions.json
        // lists no living individual lord (classic cast lords above always take precedence).
        if (officeFaction.Type == ProvinceFactionType
            && officeFaction.Region >= 0
            && definitions.ProvincialNobility.TryGetProvinceHouse(officeFaction.Region, out DaggerfallProvincialNobleHouse? house)
            && house is not null
            && !house.HasClassicCastRuler)
        {
            foreach (DaggerfallProvincialNobleMember member in house.Members)
            {
                DaggerfallOfficeHolder nobleHolder = BuildFromProvincialNoble(
                    officeFactionId, member, defaultTitle, regionHint, deadPredecessor, deadPredecessorFate);
                if (nobleHolder.HasLivingHolder)
                    return nobleHolder;

                if (deadPredecessor is null && !string.IsNullOrWhiteSpace(nobleHolder.DisplayName))
                {
                    deadPredecessor = nobleHolder.DisplayName;
                    deadPredecessorFate = "was slain";
                }
            }
        }

        if (individualChildren.Count == 0 && deadPredecessor is null)
            return null;

        return DaggerfallOfficeHolder.Vacant(officeFactionId, defaultTitle, deadPredecessor, deadPredecessorFate);
    }

    /// <summary>Default ruler title key for a province faction (matching classic <c>MacroHelper.GetRulerTitle</c>).</summary>
    internal static string? DefaultTitle(DaggerfallFactionDefinition? officeFaction) =>
        officeFaction is { Type: ProvinceFactionType }
            ? RulerTitles.ElementAtOrDefault(officeFaction.Ruler - 1) ?? "Lord"
            : null;

    private DaggerfallOfficeHolder ResolveFromOverride(
        int officeFactionId,
        DaggerfallOfficeOverride over,
        string? defaultTitle,
        int? preferredRegion)
    {
        DaggerfallCastIndividualDefinition? cast = null;
        DaggerfallCastHomeLocation? home = null;
        long? actorId = over.HolderActorId;

        if (!string.IsNullOrWhiteSpace(over.HolderCastId)
            && definitions.TryGetCastOrNoble(over.HolderCastId, out DaggerfallCastIndividualDefinition? foundCast)
            && foundCast is not null)
        {
            cast = foundCast;
            actorId ??= DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
            DaggerfallCastHomes.TryResolve(sites, cast, preferredRegion, out home);
        }
        else if (over.HolderFactionId is int hf && TryResolveCastIndividual(hf, preferredRegion, out DaggerfallCastIndividualDefinition? factionCast, out DaggerfallCastHomeLocation? factionHome))
        {
            cast = factionCast;
            home = factionHome;
            actorId ??= DaggerfallPeopleSlots.CastIndividualIdentity(cast!.Id);
        }

        DaggerfallNpc? liveNpc = actorId is long id && TryGetNpc(id, out DaggerfallNpc? existing) ? existing : null;
        bool alive = actorId is not long checkId || !IsActorDead(checkId);
        string displayName = !string.IsNullOrWhiteSpace(over.DisplayName)
            ? over.DisplayName
            : liveNpc?.DisplayName ?? cast?.Name ?? string.Empty;
        string? gender = over.Gender ?? liveNpc?.Appearance.Gender ?? cast?.Gender;
        string? race = over.Race ?? liveNpc?.Appearance.Race ?? cast?.Race;
        int? archive = over.BillboardArchive ?? liveNpc?.Appearance.BillboardArchive ?? cast?.Archive;
        int? record = over.BillboardRecord ?? liveNpc?.Appearance.BillboardRecord ?? cast?.Record;
        int? holderFaction = over.HolderFactionId ?? liveNpc?.Appearance.FactionId ?? cast?.FactionId;

        return new DaggerfallOfficeHolder(
            OfficeFactionId: officeFactionId,
            HolderFactionId: holderFaction,
            ActorId: actorId,
            CastId: cast?.Id,
            DisplayName: displayName,
            Title: over.Title ?? defaultTitle,
            Gender: gender,
            Race: race,
            BillboardArchive: archive,
            BillboardRecord: record,
            Home: home,
            IsAlive: alive,
            PreviousHolderName: over.PreviousHolderName,
            PreviousHolderFate: over.PreviousHolderFate,
            FamilyId: liveNpc?.FamilyId ?? cast?.FamilyId);
    }

    private DaggerfallOfficeHolder BuildFromProvincialNoble(
        int officeFactionId,
        DaggerfallProvincialNobleMember member,
        string? defaultTitle,
        int? preferredRegion,
        string? deadPredecessor,
        string? deadPredecessorFate)
    {
        DaggerfallCastIndividualDefinition cast = member.Cast;
        long durableId = DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
        DaggerfallNpc? liveNpc = TryGetNpc(durableId, out DaggerfallNpc? existing) ? existing : null;
        bool alive = !IsActorDead(durableId);
        DaggerfallCastHomes.TryResolve(sites, cast, preferredRegion, out DaggerfallCastHomeLocation? home);
        return new DaggerfallOfficeHolder(
            OfficeFactionId: officeFactionId,
            HolderFactionId: cast.FactionId,
            ActorId: durableId,
            CastId: cast.Id,
            DisplayName: liveNpc?.DisplayName ?? cast.Name,
            Title: member.Title ?? defaultTitle,
            Gender: liveNpc?.Appearance.Gender ?? cast.Gender,
            Race: liveNpc?.Appearance.Race ?? cast.Race,
            BillboardArchive: liveNpc?.Appearance.BillboardArchive ?? cast.Archive,
            BillboardRecord: liveNpc?.Appearance.BillboardRecord ?? cast.Record,
            Home: home,
            IsAlive: alive,
            PreviousHolderName: deadPredecessor,
            PreviousHolderFate: deadPredecessor is not null ? (deadPredecessorFate ?? "was slain") : null,
            FamilyId: liveNpc?.FamilyId ?? cast.FamilyId);
    }

    private DaggerfallOfficeHolder BuildFromIndividualFaction(
        int officeFactionId,
        DaggerfallFactionDefinition individual,
        string? defaultTitle,
        int? preferredRegion,
        string? deadPredecessor,
        string? deadPredecessorFate)
    {
        if (TryResolveCastIndividual(individual.Id, preferredRegion, out DaggerfallCastIndividualDefinition? cast, out DaggerfallCastHomeLocation? home)
            && cast is not null)
        {
            long durableId = DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
            DaggerfallNpc? liveNpc = TryGetNpc(durableId, out DaggerfallNpc? existing) ? existing : null;
            bool alive = !IsActorDead(durableId);
            string? resolvedFamilyId = liveNpc?.FamilyId ?? cast.FamilyId;
            if (string.IsNullOrWhiteSpace(resolvedFamilyId)
                && preferredRegion is int famRegion
                && definitions.ProvincialNobility.TryGetProvinceHouse(famRegion, out DaggerfallProvincialNobleHouse? provHouse))
            {
                resolvedFamilyId = provHouse?.FamilyId;
            }
            return new DaggerfallOfficeHolder(
                OfficeFactionId: officeFactionId,
                HolderFactionId: cast.FactionId,
                ActorId: durableId,
                CastId: cast.Id,
                DisplayName: liveNpc?.DisplayName ?? cast.Name,
                Title: defaultTitle,
                Gender: liveNpc?.Appearance.Gender ?? cast.Gender,
                Race: liveNpc?.Appearance.Race ?? cast.Race,
                BillboardArchive: liveNpc?.Appearance.BillboardArchive ?? cast.Archive,
                BillboardRecord: liveNpc?.Appearance.BillboardRecord ?? cast.Record,
                Home: home,
                IsAlive: alive,
                PreviousHolderName: deadPredecessor,
                PreviousHolderFate: deadPredecessor is not null ? (deadPredecessorFate ?? "was slain") : null,
                FamilyId: resolvedFamilyId);
        }

        DaggerfallFactionSpriteDefinition? sprite = individual.SpriteVisuals.FirstOrDefault();
        return new DaggerfallOfficeHolder(
            OfficeFactionId: officeFactionId,
            HolderFactionId: individual.Id,
            ActorId: null,
            CastId: null,
            DisplayName: individual.Name,
            Title: defaultTitle,
            Gender: null,
            Race: individual.Race is >= 0 and <= 7
                ? definitions.Catalogs.Races.FirstOrDefault(r => r.DonorRaceId == individual.Race + 1)?.Id
                : null,
            BillboardArchive: sprite?.Archive,
            BillboardRecord: sprite?.Record,
            Home: null,
            IsAlive: true,
            PreviousHolderName: deadPredecessor,
            PreviousHolderFate: deadPredecessor is not null ? (deadPredecessorFate ?? "was slain") : null);
    }
}
