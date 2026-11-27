using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Central switch for deterministic seeded people generation and news/standing spread, separate from
/// <see cref="DaggerfallGameRandom.Seed"/>. Defaults keep seeded townfolk stable and give a configurable
/// instant-news bubble (in world-map pixels) outside which word and standing travel by courier.
/// </summary>
internal sealed record DaggerfallSocialTuning(
    ulong WorldSocialSeed,
    double InstantNewsBubblePixels = 8.0,
    double CourierSpeedPixelsPerHour = 2.1,
    double WarZoneCourierSpeedMultiplier = 0.35,
    double MonsterSurgeCourierSpeedMultiplier = 0.50,
    double PlagueCourierSpeedMultiplier = 0.40,
    int LocalGrudgeDecayDaysPerPoint = 15,
    int GreatHouseGrudgeDecayDaysPerPoint = 30,
    int DynastyGrudgeDecayDaysPerPoint = 60,
    int KinArrestGrudgePenalty = -5,
    int KinHeadArrestGrudgePenalty = -10,
    int MourningDurationDays = 7,
    int MinorGrudgeBaseWergildPerPoint = 40,
    int LocalBloodFeudBaseWergild = 5000,
    int GreatHouseBloodFeudBaseWergild = 35000,
    int DynastyBloodFeudBaseWergild = 150000,
    double BloodFeudMurderExponentBase = 3.0,
    double ReconciliationServiceCostMultiplier = 0.55,
    double PardonCostMultiplier = 0.65,
    int PardonCourtFeeGold = 500)
{
    internal static DaggerfallSocialTuning Default { get; } = new(0UL);

    internal DaggerfallSocialTuning Validate()
    {
        if (!double.IsFinite(InstantNewsBubblePixels) || InstantNewsBubblePixels < 0d)
            throw new ArgumentOutOfRangeException(nameof(InstantNewsBubblePixels), "Instant news bubble radius must be finite and non-negative.");
        if (!double.IsFinite(CourierSpeedPixelsPerHour) || CourierSpeedPixelsPerHour <= 0d)
            throw new ArgumentOutOfRangeException(nameof(CourierSpeedPixelsPerHour), "Courier speed must be positive.");
        if (!double.IsFinite(WarZoneCourierSpeedMultiplier) || WarZoneCourierSpeedMultiplier is <= 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(WarZoneCourierSpeedMultiplier), "War-zone courier speed multiplier must be in (0, 1].");
        if (!double.IsFinite(MonsterSurgeCourierSpeedMultiplier) || MonsterSurgeCourierSpeedMultiplier is <= 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(MonsterSurgeCourierSpeedMultiplier), "Monster-surge courier speed multiplier must be in (0, 1].");
        if (!double.IsFinite(PlagueCourierSpeedMultiplier) || PlagueCourierSpeedMultiplier is <= 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(PlagueCourierSpeedMultiplier), "Plague courier speed multiplier must be in (0, 1].");
        if (LocalGrudgeDecayDaysPerPoint <= 0)
            throw new ArgumentOutOfRangeException(nameof(LocalGrudgeDecayDaysPerPoint), "Local grudge decay days per point must be positive.");
        if (GreatHouseGrudgeDecayDaysPerPoint <= 0)
            throw new ArgumentOutOfRangeException(nameof(GreatHouseGrudgeDecayDaysPerPoint), "Great House grudge decay days per point must be positive.");
        if (DynastyGrudgeDecayDaysPerPoint <= 0)
            throw new ArgumentOutOfRangeException(nameof(DynastyGrudgeDecayDaysPerPoint), "Royal dynasty grudge decay days per point must be positive.");
        if (KinArrestGrudgePenalty is > 0 or < -14)
            throw new ArgumentOutOfRangeException(nameof(KinArrestGrudgePenalty), "Kin arrest grudge penalty must be in [-14, 0].");
        if (KinHeadArrestGrudgePenalty is > 0 or < -14)
            throw new ArgumentOutOfRangeException(nameof(KinHeadArrestGrudgePenalty), "Kin head arrest grudge penalty must be in [-14, 0].");
        if (MourningDurationDays < 0)
            throw new ArgumentOutOfRangeException(nameof(MourningDurationDays), "Mourning duration days must be non-negative.");
        if (MinorGrudgeBaseWergildPerPoint <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinorGrudgeBaseWergildPerPoint), "Minor grudge base wergild per point must be positive.");
        if (LocalBloodFeudBaseWergild <= 0)
            throw new ArgumentOutOfRangeException(nameof(LocalBloodFeudBaseWergild), "Local blood feud base wergild must be positive.");
        if (GreatHouseBloodFeudBaseWergild < LocalBloodFeudBaseWergild)
            throw new ArgumentOutOfRangeException(nameof(GreatHouseBloodFeudBaseWergild), "Great House blood feud base wergild must be at least local base wergild.");
        if (DynastyBloodFeudBaseWergild < GreatHouseBloodFeudBaseWergild)
            throw new ArgumentOutOfRangeException(nameof(DynastyBloodFeudBaseWergild), "Royal dynasty blood feud base wergild must be at least Great House base wergild.");
        if (!double.IsFinite(BloodFeudMurderExponentBase) || BloodFeudMurderExponentBase < 1.5d || BloodFeudMurderExponentBase > 10d)
            throw new ArgumentOutOfRangeException(nameof(BloodFeudMurderExponentBase), "Blood feud murder exponent base must be in [1.5, 10].");
        if (!double.IsFinite(ReconciliationServiceCostMultiplier) || ReconciliationServiceCostMultiplier is <= 0.1d or >= 1d)
            throw new ArgumentOutOfRangeException(nameof(ReconciliationServiceCostMultiplier), "Reconciliation service cost multiplier must be in (0.1, 1).");
        if (!double.IsFinite(PardonCostMultiplier) || PardonCostMultiplier is <= 0.1d or >= 1d)
            throw new ArgumentOutOfRangeException(nameof(PardonCostMultiplier), "Pardon cost multiplier must be in (0.1, 1).");
        if (PardonCourtFeeGold < 0)
            throw new ArgumentOutOfRangeException(nameof(PardonCourtFeeGold), "Pardon court fee gold must be non-negative.");
        return this;
    }

    internal static DaggerfallSocialTuning Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Tuning section 'social' must be a JSON object.");
        ulong seed = element.TryGetProperty("worldSocialSeed", out JsonElement value)
            ? value.GetUInt64()
            : 0UL;
        double bubble = element.TryGetProperty("instantNewsBubblePixels", out JsonElement b)
            ? b.GetDouble()
            : 8.0;
        double speed = element.TryGetProperty("courierSpeedPixelsPerHour", out JsonElement s)
            ? s.GetDouble()
            : 2.1;
        double war = element.TryGetProperty("warZoneCourierSpeedMultiplier", out JsonElement w)
            ? w.GetDouble()
            : 0.35;
        double surge = element.TryGetProperty("monsterSurgeCourierSpeedMultiplier", out JsonElement m)
            ? m.GetDouble()
            : 0.50;
        double plague = element.TryGetProperty("plagueCourierSpeedMultiplier", out JsonElement p)
            ? p.GetDouble()
            : 0.40;
        int localDecay = element.TryGetProperty("localGrudgeDecayDaysPerPoint", out JsonElement ld)
            ? ld.GetInt32()
            : 15;
        int houseDecay = element.TryGetProperty("greatHouseGrudgeDecayDaysPerPoint", out JsonElement hd)
            ? hd.GetInt32()
            : 30;
        int dynastyDecay = element.TryGetProperty("dynastyGrudgeDecayDaysPerPoint", out JsonElement dd)
            ? dd.GetInt32()
            : 60;
        int arrestPenalty = element.TryGetProperty("kinArrestGrudgePenalty", out JsonElement ap)
            ? ap.GetInt32()
            : -5;
        int headArrestPenalty = element.TryGetProperty("kinHeadArrestGrudgePenalty", out JsonElement hap)
            ? hap.GetInt32()
            : -10;
        int mourningDays = element.TryGetProperty("mourningDurationDays", out JsonElement md)
            ? md.GetInt32()
            : 7;
        int minorWergild = element.TryGetProperty("minorGrudgeBaseWergildPerPoint", out JsonElement mw)
            ? mw.GetInt32()
            : 40;
        int localFeudWergild = element.TryGetProperty("localBloodFeudBaseWergild", out JsonElement lfw)
            ? lfw.GetInt32()
            : 5000;
        int houseFeudWergild = element.TryGetProperty("greatHouseBloodFeudBaseWergild", out JsonElement hfw)
            ? hfw.GetInt32()
            : 35000;
        int dynastyFeudWergild = element.TryGetProperty("dynastyBloodFeudBaseWergild", out JsonElement dfw)
            ? dfw.GetInt32()
            : 150000;
        double murderExpBase = element.TryGetProperty("bloodFeudMurderExponentBase", out JsonElement meb)
            ? meb.GetDouble()
            : 3.0;
        double serviceMult = element.TryGetProperty("reconciliationServiceCostMultiplier", out JsonElement sm)
            ? sm.GetDouble()
            : 0.55;
        double pardonMult = element.TryGetProperty("pardonCostMultiplier", out JsonElement pm)
            ? pm.GetDouble()
            : 0.65;
        int pardonFee = element.TryGetProperty("pardonCourtFeeGold", out JsonElement pf)
            ? pf.GetInt32()
            : 500;
        return new DaggerfallSocialTuning(
            seed, bubble, speed, war, surge, plague,
            localDecay, houseDecay, dynastyDecay,
            arrestPenalty, headArrestPenalty, mourningDays,
            minorWergild, localFeudWergild, houseFeudWergild, dynastyFeudWergild,
            murderExpBase, serviceMult, pardonMult, pardonFee).Validate();
    }
}

/// <summary>One selected <c>daggerfall.people</c> pack as the reader takes it (block placements and barks, plus optional legacy inline overrides for unit tests).</summary>
internal sealed record DaggerfallPeoplePackFiles(
    string PackId,
    ReadOnlyMemory<byte> Payload,
    ReadOnlyMemory<byte>? CastBytes,
    ReadOnlyMemory<byte>? SpawnersBytes,
    IReadOnlyList<DaggerfallPeopleBlockFile> BlockFiles,
    ReadOnlyMemory<byte>? ArchetypesBytes = null,
    ReadOnlyMemory<byte>? BarksBytes = null);

/// <summary>One block-slot file under a people pack's <c>blocks/</c> folder.</summary>
internal sealed record DaggerfallPeopleBlockFile(string Path, ReadOnlyMemory<byte> Bytes);

/// <summary>Classic sprite visual fallback attached to a people slot or cast individual.</summary>
internal sealed record DaggerfallPeopleVisual(int Archive, int Record, string Gender);

/// <summary>Weighted visual candidate inside a typed spawner pool.</summary>
internal sealed record DaggerfallPeoplePoolEntry(int Archive, int Record, string Gender, int Weight);

/// <summary>Central typed spawner pool resolved by region, climate, and <see cref="DaggerfallSocialTuning.WorldSocialSeed"/>.</summary>
internal sealed record DaggerfallPeopleSpawnerDefinition(
    string Id,
    string Role,
    string Interaction,
    string Harm,
    bool DeriveRegionalFaction,
    string VisualMode,
    IReadOnlyList<DaggerfallPeoplePoolEntry> DefaultPool,
    IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>> RegionalPools,
    string? ArchetypeId = null,
    IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>? ClimatePoolsMap = null,
    ActorSocialGrouping SocialGrouping = ActorSocialGrouping.None)
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>> EmptyPools =
        new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>(
            new Dictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>(StringComparer.OrdinalIgnoreCase));

    internal IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>> ClimatePools =>
        ClimatePoolsMap ?? EmptyPools;
}

/// <summary>Authored referral phrasing templates and regional contact names loaded from <c>daggerfall.people/barks.json</c>.</summary>
internal sealed record DaggerfallPeopleReferralTemplates(
    IReadOnlyList<string> DefaultIssueTemplates,
    IReadOnlyDictionary<string, IReadOnlyList<string>> IssueTemplatesByRegion,
    IReadOnlyDictionary<string, IReadOnlyList<string>> IssueTemplatesByClimate,
    IReadOnlyList<string> DefaultResponseTemplates,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ResponseTemplatesByRole,
    IReadOnlyList<string> ContactNames,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContactNamesByRegion,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContactNamesByClimate)
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyMap =
        new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    internal static DaggerfallPeopleReferralTemplates Empty { get; } = new(
        [], EmptyMap, EmptyMap, [], EmptyMap, [], EmptyMap, EmptyMap);

    internal string ResolveContactName(ulong seed, int? regionIndex = null, string? regionName = null, string? climateName = null)
    {
        IReadOnlyList<string> pool = SelectByRegionOrClimate(ContactNamesByRegion, ContactNamesByClimate, ContactNames,
            regionIndex, regionName, climateName);
        if (pool.Count == 0)
            throw new InvalidOperationException("People pack barks.json defines no referral contactNames.");
        return pool[(int)(seed % (ulong)pool.Count)];
    }

    internal string FormatIssueLine(
        ulong seed,
        string targetName,
        string contactName,
        string speakerName,
        int? regionIndex = null,
        string? regionName = null,
        string? climateName = null)
    {
        IReadOnlyList<string> pool = SelectByRegionOrClimate(IssueTemplatesByRegion, IssueTemplatesByClimate, DefaultIssueTemplates,
            regionIndex, regionName, climateName);
        if (pool.Count == 0)
            throw new InvalidOperationException("People pack barks.json defines no referral defaultIssueTemplates.");
        string template = pool[(int)(seed % (ulong)pool.Count)];
        return template
            .Replace("{target}", targetName, StringComparison.Ordinal)
            .Replace("{contact}", contactName, StringComparison.Ordinal)
            .Replace("{speaker}", speakerName, StringComparison.Ordinal);
    }

    internal string FormatResponseLine(ulong seed, string speakerName, string referralTag, string? role = null)
    {
        IReadOnlyList<string> pool = !string.IsNullOrWhiteSpace(role)
            && ResponseTemplatesByRole.TryGetValue(role, out IReadOnlyList<string>? byRole)
            && byRole.Count > 0
                ? byRole
                : DefaultResponseTemplates;
        if (pool.Count == 0)
            throw new InvalidOperationException("People pack barks.json defines no referral defaultResponseTemplates.");
        string template = pool[(int)(seed % (ulong)pool.Count)];
        return template
            .Replace("{speaker}", speakerName, StringComparison.Ordinal)
            .Replace("{tag}", referralTag, StringComparison.Ordinal);
    }

    internal static IReadOnlyList<string> SelectByRegionOrClimate(
        IReadOnlyDictionary<string, IReadOnlyList<string>> byRegion,
        IReadOnlyDictionary<string, IReadOnlyList<string>> byClimate,
        IReadOnlyList<string> fallback,
        int? regionIndex,
        string? regionName,
        string? climateName)
    {
        if (!string.IsNullOrWhiteSpace(regionName) && byRegion.TryGetValue(regionName, out IReadOnlyList<string>? named) && named.Count > 0)
            return named;
        if (regionIndex is int r && byRegion.TryGetValue(r.ToString(CultureInfo.InvariantCulture), out IReadOnlyList<string>? indexed) && indexed.Count > 0)
            return indexed;
        if (!string.IsNullOrWhiteSpace(climateName) && byClimate.TryGetValue(climateName, out IReadOnlyList<string>? climate) && climate.Count > 0)
            return climate;
        return fallback;
    }
}

/// <summary>Authored ambient bark pools and referral templates loaded from <c>daggerfall.people/barks.json</c>.</summary>
internal sealed record DaggerfallPeopleBarks(
    IReadOnlyList<string> DefaultBarks,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ByRole,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ByArchetype,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ByRegion,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ByClimate,
    DaggerfallPeopleReferralTemplates Referrals,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ByFactionMap = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ByConditionMap = null)
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyMap =
        new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    internal IReadOnlyDictionary<string, IReadOnlyList<string>> ByFaction => ByFactionMap ?? EmptyMap;
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> ByCondition => ByConditionMap ?? EmptyMap;

    internal static DaggerfallPeopleBarks Empty { get; } = new(
        [], EmptyMap, EmptyMap, EmptyMap, EmptyMap, DaggerfallPeopleReferralTemplates.Empty, EmptyMap, EmptyMap);

    /// <summary>
    /// Resolves one deterministic ambient bark line with specificity precedence:
    /// role -&gt; archetype -&gt; region -&gt; climate -&gt; defaultBarks.
    /// </summary>
    internal string ResolveBarkLine(
        ulong seed,
        string? role = null,
        string? archetypeId = null,
        int? regionIndex = null,
        string? regionName = null,
        string? climateName = null)
    {
        IReadOnlyList<string>? pool = null;
        if (!string.IsNullOrWhiteSpace(role) && ByRole.TryGetValue(role, out IReadOnlyList<string>? rolePool) && rolePool.Count > 0)
            pool = rolePool;
        else if (!string.IsNullOrWhiteSpace(archetypeId) && ByArchetype.TryGetValue(archetypeId, out IReadOnlyList<string>? archPool) && archPool.Count > 0)
            pool = archPool;
        else
            pool = DaggerfallPeopleReferralTemplates.SelectByRegionOrClimate(ByRegion, ByClimate, DefaultBarks, regionIndex, regionName, climateName);

        if (pool.Count == 0)
            throw new InvalidOperationException("People pack barks.json defines no defaultBarks.");
        return pool[(int)(seed % (ulong)pool.Count)];
    }

    /// <summary>Tries to resolve a faction-specific ambient bark line from <see cref="ByFaction"/>.</summary>
    internal bool TryResolveFactionBarkLine(ulong seed, IEnumerable<string> factionKeys, out string? line)
    {
        line = null;
        foreach (string key in factionKeys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (ByFaction.TryGetValue(key, out IReadOnlyList<string>? pool) && pool.Count > 0)
            {
                line = pool[(int)(seed % (ulong)pool.Count)];
                return true;
            }
        }
        return false;
    }

    /// <summary>Tries to resolve a world-condition ambient bark line from <see cref="ByCondition"/>.</summary>
    internal bool TryResolveConditionBarkLine(
        ulong seed,
        DaggerfallWorldConditionKind kind,
        string regionName,
        string? factionName,
        out string? line)
    {
        line = null;
        string slug = DaggerfallWorldConditionToken.KindSlug(kind);
        if (!ByCondition.TryGetValue(slug, out IReadOnlyList<string>? pool) || pool.Count == 0)
            return false;
        string template = pool[(int)(seed % (ulong)pool.Count)];
        line = template
            .Replace("{region}", regionName, StringComparison.Ordinal)
            .Replace("{faction}", string.IsNullOrWhiteSpace(factionName) ? "the authorities" : factionName, StringComparison.Ordinal);
        return true;
    }
}

/// <summary>
/// Genuine unique named individual authored in <c>cast/individuals.json</c>. A placed individual's home is a
/// <see cref="HomeBlock"/> and either a <see cref="HomeSlotId"/>, the block's people slot it takes, or a
/// <see cref="HomePosition"/> in the block's frame where it is set. A <see cref="HomeSite"/> pins the home to that one
/// location when the block repeats across the world, and a home position always names one: there the slot is the
/// individual's, or the individual is added, and nowhere else. An unplaced individual has no home: nothing sets it in
/// the world, and it appears only where a quest or the political board names it.
/// </summary>
internal sealed record DaggerfallCastIndividualDefinition(
    string Id,
    int FactionId,
    string Name,
    string? Race,
    string Gender,
    int Archive,
    int Record,
    string? HomeBlock,
    string? HomeSlotId,
    string Interaction,
    string Harm,
    string? ArchetypeId = null,
    string? FamilyId = null,
    string? VisualAssetId = null)
{
    internal DaggerfallSiteId? HomeSite { get; init; }

    internal WorldPoint? HomePosition { get; init; }

}

/// <summary>One authored people slot in an RMB interior, RMB exterior, or RDB dungeon block.</summary>
internal sealed record DaggerfallPeopleSlotDefinition(
    string SlotId,
    string? CastId,
    string? SpawnerId,
    WorldPoint Position,
    string Interaction,
    int? FactionId,
    bool DeriveRegionalFaction,
    DaggerfallPeopleVisual ClassicVisual,
    int? BuildingType,
    int? BuildingFactionId);

/// <summary>One RMB building's authored interior people slots.</summary>
internal sealed record DaggerfallPeopleBuildingSlots(
    int BuildingIndex,
    int BuildingType,
    int BuildingFactionId,
    IReadOnlyList<DaggerfallPeopleSlotDefinition> Slots);

/// <summary>The admitted <c>daggerfall.people</c> pack: unique cast, central spawners, archetypes, barks, and block slot templates.</summary>
internal sealed record DaggerfallPeopleSet(
    IReadOnlyDictionary<string, DaggerfallCastIndividualDefinition> Individuals,
    IReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition> Spawners,
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>> RmbInteriors,
    IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> RmbExteriors,
    IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> RdbDungeons,
    IReadOnlyDictionary<string, ArchetypeProfile>? ArchetypesMap = null,
    DaggerfallPeopleBarks? BarksData = null,
    IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>? SiteExteriorsMap = null)
{
    private static readonly IReadOnlyDictionary<string, ArchetypeProfile> EmptyArchetypes =
        new ReadOnlyDictionary<string, ArchetypeProfile>(new Dictionary<string, ArchetypeProfile>(StringComparer.Ordinal));

    internal IReadOnlyDictionary<string, ArchetypeProfile> Archetypes => ArchetypesMap ?? EmptyArchetypes;
    internal DaggerfallPeopleBarks Barks => BarksData ?? DaggerfallPeopleBarks.Empty;

    internal static DaggerfallPeopleSet Empty { get; } = new(
        new ReadOnlyDictionary<string, DaggerfallCastIndividualDefinition>(new Dictionary<string, DaggerfallCastIndividualDefinition>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition>(new Dictionary<string, DaggerfallPeopleSpawnerDefinition>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>>(new Dictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(new Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(new Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(StringComparer.Ordinal)),
        EmptyArchetypes,
        DaggerfallPeopleBarks.Empty);

    internal ArchetypeProfile RequireArchetype(string archetypeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archetypeId);
        return Archetypes.TryGetValue(archetypeId, out ArchetypeProfile? profile)
            ? profile
            : throw new InvalidOperationException($"Archetype '{archetypeId}' is not defined in daggerfall.people/archetypes.json.");
    }

    internal ArchetypeProfile ResolveArchetype(
        string defaultArchetypeId,
        string? explicitArchetypeId = null,
        string? spawnerId = null,
        string? castId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultArchetypeId);
        if (!string.IsNullOrWhiteSpace(explicitArchetypeId)
            && Archetypes.TryGetValue(explicitArchetypeId, out ArchetypeProfile? explicitProfile))
            return explicitProfile;
        if (!string.IsNullOrWhiteSpace(spawnerId)
            && Spawners.TryGetValue(spawnerId, out DaggerfallPeopleSpawnerDefinition? spawner)
            && !string.IsNullOrWhiteSpace(spawner.ArchetypeId)
            && Archetypes.TryGetValue(spawner.ArchetypeId, out ArchetypeProfile? spawnerProfile))
            return spawnerProfile;
        if (!string.IsNullOrWhiteSpace(castId)
            && Individuals.TryGetValue(castId, out DaggerfallCastIndividualDefinition? cast)
            && !string.IsNullOrWhiteSpace(cast.ArchetypeId)
            && Archetypes.TryGetValue(cast.ArchetypeId, out ArchetypeProfile? castProfile))
            return castProfile;
        return RequireArchetype(defaultArchetypeId);
    }

    internal bool TryGetRmbInteriorSlots(string blockSourceKey, int buildingIndex, out DaggerfallPeopleBuildingSlots? building)
    {
        building = null;
        return RmbInteriors.TryGetValue(blockSourceKey, out IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>? buildings)
            && buildings.TryGetValue(buildingIndex, out building);
    }

    internal IReadOnlyList<DaggerfallPeopleSlotDefinition> GetRmbExteriorSlots(string blockSourceKey) =>
        RmbExteriors.TryGetValue(blockSourceKey, out IReadOnlyList<DaggerfallPeopleSlotDefinition>? slots) ? slots : [];

    /// <summary>
    /// The people a <c>site-exterior</c> block file sets at one location's grid cell (<paramref name="blockX"/>,
    /// <paramref name="blockY"/>) besides its block's own, such as the guards a location keeps: placed there alone.
    /// </summary>
    internal IReadOnlyList<DaggerfallPeopleSlotDefinition> GetSiteExteriorSlots(DaggerfallSiteId site, int blockX, int blockY) =>
        SiteExteriorsMap is not null && SiteExteriorsMap.TryGetValue(SiteExteriorKey(site, blockX, blockY), out IReadOnlyList<DaggerfallPeopleSlotDefinition>? slots) ? slots : [];

    /// <summary>A <c>site-exterior</c> block file's key for one location's grid cell: <c>region/index/x,y</c>.</summary>
    internal static string SiteExteriorKey(DaggerfallSiteId site, int blockX, int blockY) =>
        string.Create(CultureInfo.InvariantCulture, $"{site.Region}/{site.Index}/{blockX},{blockY}");

    internal IReadOnlyList<DaggerfallPeopleSlotDefinition> GetRdbDungeonSlots(string blockSourceKey) =>
        RdbDungeons.TryGetValue(blockSourceKey, out IReadOnlyList<DaggerfallPeopleSlotDefinition>? slots) ? slots : [];

    /// <summary>The individuals whose home is pinned to <paramref name="site"/>'s block <paramref name="blockSourceKey"/>.</summary>
    internal IEnumerable<DaggerfallCastIndividualDefinition> HomedAt(DaggerfallSiteId site, string blockSourceKey) =>
        Individuals.Values.Where(cast => cast.HomeSite == site && string.Equals(cast.HomeBlock, blockSourceKey, StringComparison.OrdinalIgnoreCase));

    internal bool TryGetIndividualByFaction(int factionId, out DaggerfallCastIndividualDefinition? individual)
    {
        if (factionId > 0)
        {
            foreach (DaggerfallCastIndividualDefinition candidate in Individuals.Values)
            {
                if (candidate.FactionId == factionId)
                {
                    individual = candidate;
                    return true;
                }
            }
        }
        individual = null;
        return false;
    }
}

/// <summary>Concrete resolved result for a single <see cref="DaggerfallPeopleSlotDefinition"/> at a site.</summary>
internal sealed record DaggerfallResolvedPeopleSlot(
    long DurableId,
    string SlotKey,
    ulong SlotSeed,
    string? CastId,
    string? SpawnerId,
    string? CastName,
    string Role,
    string Interaction,
    string Harm,
    DaggerfallPeopleVisual Visual,
    int? PrimaryFactionId,
    int? RegionalPeopleFactionId,
    WorldPoint Position);

/// <summary>Reads and validates the owned <c>daggerfall.people</c> content pack.</summary>
internal static class DaggerfallPeopleContent
{
    internal const string CastFile = "cast/individuals.json";
    internal const string SpawnersFile = "spawners.json";
    internal const string ArchetypesFile = "archetypes.json";
    internal const string BarksFile = "barks.json";
    internal const string BlocksFolder = "blocks";
    private const int IndividualFactionType = 4;
    private const int MaximumArchetypeVital = 10_000;
    internal static readonly IReadOnlySet<string> ValidClimateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Ocean",
        "Desert",
        "Desert2",
        "Mountain",
        "Rainforest",
        "Swamp",
        "Subtropical",
        "MountainWoods",
        "Woodlands",
        "HauntedWoodlands",
    };

    internal static DaggerfallPeopleSet Read(
        DaggerfallPeoplePackFiles pack,
        DaggerfallFactionsSet? factions = null,
        DaggerfallVocabulary? vocabulary = null,
        IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition>? items = null,
        DaggerfallActorCatalog? actorCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        DaggerfallContentDiagnostics diagnostics = new();

        // Validate payload manifest pointers (daggerfall.people now owns barks and block slots only; templates/spawners/cast live in daggerfall.base).
        try
        {
            using JsonDocument payloadDoc = JsonDocument.Parse(pack.Payload);
            JsonElement root = DaggerfallBaseContent.Object(payloadDoc.RootElement, $"People pack '{pack.PackId}' payload", diagnostics);
            _ = DaggerfallBaseContent.Text(root, "barksFile", diagnostics);
            _ = DaggerfallBaseContent.Text(root, "blocksFolder", diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"People pack '{pack.PackId}' payload is malformed: {exception.Message}");
        }

        Dictionary<string, ArchetypeProfile> archetypes;
        if (pack.ArchetypesBytes is { IsEmpty: false } archBytes)
        {
            archetypes = ReadArchetypes(pack.PackId, archBytes, vocabulary, items, diagnostics);
        }
        else if (actorCatalog is not null)
        {
            archetypes = actorCatalog.ProjectArchetypes().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (string requiredId in DaggerfallArchetypeIds.Required)
            {
                if (!archetypes.ContainsKey(requiredId))
                    diagnostics.Add($"People pack '{pack.PackId}' actor catalog must define required archetype template '{requiredId}'.");
            }
        }
        else
        {
            archetypes = ReadArchetypes(pack.PackId, null, vocabulary, items, diagnostics);
        }

        DaggerfallPeopleBarks barks = ReadBarks(pack.PackId, pack.BarksBytes, archetypes, diagnostics);

        Dictionary<string, DaggerfallCastIndividualDefinition> individuals = pack.CastBytes is { IsEmpty: false } castBytes
            ? ReadIndividuals(pack.PackId, castBytes, factions, archetypes, diagnostics)
            : actorCatalog is not null
                ? actorCatalog.ProjectCastIndividuals().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, DaggerfallCastIndividualDefinition>(StringComparer.Ordinal);

        Dictionary<string, DaggerfallPeopleSpawnerDefinition> spawners = pack.SpawnersBytes is { IsEmpty: false } spawnerBytes
            ? ReadSpawners(pack.PackId, spawnerBytes, archetypes, diagnostics)
            : actorCatalog is not null
                ? actorCatalog.ProjectTownSpawners().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, DaggerfallPeopleSpawnerDefinition>(StringComparer.Ordinal);

        Dictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>> rmbInteriors = new(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> rmbExteriors = new(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> rdbDungeons = new(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> siteExteriors = new(StringComparer.Ordinal);

        foreach (DaggerfallPeopleBlockFile file in pack.BlockFiles)
            ReadBlockFile(pack.PackId, file, individuals, spawners, factions, rmbInteriors, rmbExteriors, rdbDungeons, siteExteriors, diagnostics);

        diagnostics.ThrowIfAny();
        return new(
            new ReadOnlyDictionary<string, DaggerfallCastIndividualDefinition>(individuals),
            new ReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition>(spawners),
            new ReadOnlyDictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>>(rmbInteriors),
            new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(rmbExteriors),
            new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(rdbDungeons),
            new ReadOnlyDictionary<string, ArchetypeProfile>(archetypes),
            barks,
            new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(siteExteriors));
    }

    private static DaggerfallPeopleBarks ReadBarks(
        string packId,
        ReadOnlyMemory<byte>? bytes,
        IReadOnlyDictionary<string, ArchetypeProfile> archetypes,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null || bytes.Value.IsEmpty)
        {
            diagnostics.Add($"People pack '{packId}' is missing '{BarksFile}'.");
            return DaggerfallPeopleBarks.Empty;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes.Value);
            JsonElement root = DaggerfallBaseContent.Object(doc.RootElement, $"People pack '{packId}' barks", diagnostics);
            IReadOnlyList<string> defaultBarks = ReadTextList(root, "defaultBarks", $"People pack '{packId}' barks.defaultBarks", requiredNonEmpty: true, diagnostics);
            IReadOnlyDictionary<string, IReadOnlyList<string>> byRole = ReadTextMap(root, "byRole", $"People pack '{packId}' barks.byRole", diagnostics);
            IReadOnlyDictionary<string, IReadOnlyList<string>> byArchetype = ReadTextMap(root, "byArchetype", $"People pack '{packId}' barks.byArchetype", diagnostics,
                validateKey: key => archetypes.ContainsKey(key) ? null : $"references unknown archetype '{key}'");
            IReadOnlyDictionary<string, IReadOnlyList<string>> byRegion = ReadTextMap(root, "byRegion", $"People pack '{packId}' barks.byRegion", diagnostics);
            IReadOnlyDictionary<string, IReadOnlyList<string>> byClimate = ReadTextMap(root, "byClimate", $"People pack '{packId}' barks.byClimate", diagnostics,
                validateKey: key => ValidClimateNames.Contains(key) ? null : $"references unknown climate '{key}'");
            IReadOnlyDictionary<string, IReadOnlyList<string>> byFaction = ReadTextMap(root, "byFaction", $"People pack '{packId}' barks.byFaction", diagnostics);
            IReadOnlyDictionary<string, IReadOnlyList<string>> byCondition = ReadTextMap(root, "byCondition", $"People pack '{packId}' barks.byCondition", diagnostics);

            JsonElement refEl = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "referrals", diagnostics), $"People pack '{packId}' barks.referrals", diagnostics);
            IReadOnlyList<string> defaultIssue = ReadTextList(refEl, "defaultIssueTemplates", $"People pack '{packId}' referrals.defaultIssueTemplates", requiredNonEmpty: true, diagnostics,
                requiredTokens: ["{target}", "{contact}", "{speaker}"]);
            IReadOnlyDictionary<string, IReadOnlyList<string>> issueByRegion = ReadTextMap(refEl, "issueTemplatesByRegion", $"People pack '{packId}' referrals.issueTemplatesByRegion", diagnostics,
                requiredTokens: ["{target}", "{contact}", "{speaker}"]);
            IReadOnlyDictionary<string, IReadOnlyList<string>> issueByClimate = ReadTextMap(refEl, "issueTemplatesByClimate", $"People pack '{packId}' referrals.issueTemplatesByClimate", diagnostics,
                validateKey: key => ValidClimateNames.Contains(key) ? null : $"references unknown climate '{key}'",
                requiredTokens: ["{target}", "{contact}", "{speaker}"]);

            IReadOnlyList<string> defaultResp = ReadTextList(refEl, "defaultResponseTemplates", $"People pack '{packId}' referrals.defaultResponseTemplates", requiredNonEmpty: true, diagnostics,
                requiredTokens: ["{speaker}", "{tag}"]);
            IReadOnlyDictionary<string, IReadOnlyList<string>> respByRole = ReadTextMap(refEl, "responseTemplatesByRole", $"People pack '{packId}' referrals.responseTemplatesByRole", diagnostics,
                requiredTokens: ["{speaker}", "{tag}"]);

            IReadOnlyList<string> contactNames = ReadTextList(refEl, "contactNames", $"People pack '{packId}' referrals.contactNames", requiredNonEmpty: true, diagnostics);
            IReadOnlyDictionary<string, IReadOnlyList<string>> contactsByRegion = ReadTextMap(refEl, "contactNamesByRegion", $"People pack '{packId}' referrals.contactNamesByRegion", diagnostics);
            IReadOnlyDictionary<string, IReadOnlyList<string>> contactsByClimate = ReadTextMap(refEl, "contactNamesByClimate", $"People pack '{packId}' referrals.contactNamesByClimate", diagnostics,
                validateKey: key => ValidClimateNames.Contains(key) ? null : $"references unknown climate '{key}'");

            return new DaggerfallPeopleBarks(
                defaultBarks,
                byRole,
                byArchetype,
                byRegion,
                byClimate,
                new DaggerfallPeopleReferralTemplates(
                    defaultIssue,
                    issueByRegion,
                    issueByClimate,
                    defaultResp,
                    respByRole,
                    contactNames,
                    contactsByRegion,
                    contactsByClimate),
                byFaction,
                byCondition);
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"People pack '{packId}' barks file is malformed: {exception.Message}");
            return DaggerfallPeopleBarks.Empty;
        }
    }

    private static IReadOnlyList<string> ReadTextList(
        JsonElement parent,
        string propertyName,
        string context,
        bool requiredNonEmpty,
        DaggerfallContentDiagnostics diagnostics,
        string[]? requiredTokens = null)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement el))
        {
            if (requiredNonEmpty)
                diagnostics.Add($"{context} is required and must be a non-empty string array.");
            return [];
        }
        return ReadTextArray(el, context, requiredNonEmpty, diagnostics, requiredTokens);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadTextMap(
        JsonElement parent,
        string propertyName,
        string context,
        DaggerfallContentDiagnostics diagnostics,
        Func<string, string?>? validateKey = null,
        string[]? requiredTokens = null)
    {
        Dictionary<string, IReadOnlyList<string>> map = new(StringComparer.OrdinalIgnoreCase);
        if (!parent.TryGetProperty(propertyName, out JsonElement el))
            return new ReadOnlyDictionary<string, IReadOnlyList<string>>(map);
        if (el.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add($"{context} must be an object mapping keys to string arrays.");
            return new ReadOnlyDictionary<string, IReadOnlyList<string>>(map);
        }
        foreach (JsonProperty prop in el.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(prop.Name))
            {
                diagnostics.Add($"{context} contains an empty key.");
                continue;
            }
            if (validateKey?.Invoke(prop.Name) is { } keyError)
                diagnostics.Add($"{context}.'{prop.Name}' {keyError}.");
            IReadOnlyList<string> lines = ReadTextArray(prop.Value, $"{context}.'{prop.Name}'", requiredNonEmpty: true, diagnostics, requiredTokens);
            map[prop.Name] = lines;
        }
        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(map);
    }

    private static IReadOnlyList<string> ReadTextArray(
        JsonElement el,
        string context,
        bool requiredNonEmpty,
        DaggerfallContentDiagnostics diagnostics,
        string[]? requiredTokens = null)
    {
        if (el.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add($"{context} must be an array of strings.");
            return [];
        }
        List<string> list = [];
        int idx = 0;
        foreach (JsonElement item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                diagnostics.Add($"{context}[{idx}] must be non-empty text.");
            }
            else
            {
                string line = item.GetString()!.Trim();
                if (requiredTokens is not null)
                {
                    foreach (string token in requiredTokens)
                    {
                        if (!line.Contains(token, StringComparison.Ordinal))
                            diagnostics.Add($"{context}[{idx}] must contain placeholder '{token}'.");
                    }
                }
                list.Add(line);
            }
            idx++;
        }
        if (requiredNonEmpty && list.Count == 0)
            diagnostics.Add($"{context} must contain at least one entry.");
        return list.AsReadOnly();
    }

    private static Dictionary<string, ArchetypeProfile> ReadArchetypes(
        string packId,
        ReadOnlyMemory<byte>? bytes,
        DaggerfallVocabulary? vocabulary,
        IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition>? items,
        DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, ArchetypeProfile> result = new(StringComparer.Ordinal);
        if (bytes is null || bytes.Value.IsEmpty)
        {
            diagnostics.Add($"People pack '{packId}' is missing '{ArchetypesFile}'.");
            return result;
        }
        HashSet<string>? validAttributes = vocabulary?.Attributes.Select(a => a.Value).ToHashSet(StringComparer.Ordinal);
        HashSet<string>? validSkills = vocabulary?.Skills.Select(s => s.Value).ToHashSet(StringComparer.Ordinal);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes.Value);
            JsonElement root = DaggerfallBaseContent.Object(doc.RootElement, $"People pack '{packId}' archetypes", diagnostics);
            foreach (JsonElement item in DaggerfallBaseContent.Array(root, "archetypes", diagnostics))
            {
                JsonElement obj = DaggerfallBaseContent.Object(item, "people archetype", diagnostics);
                string id = DaggerfallBaseContent.Text(obj, "id", diagnostics);
                if (!DaggerfallBaseContent.ValidId(id))
                    diagnostics.Add($"People archetype '{id}' has an invalid id.");

                JsonElement healthObj = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(obj, "health", diagnostics), $"archetype '{id}' health", diagnostics);
                int healthMin = DaggerfallBaseContent.Integer(healthObj, "min", diagnostics);
                int healthMax = DaggerfallBaseContent.Integer(healthObj, "max", diagnostics);
                if (healthMin < 1 || healthMax < healthMin || healthMax > MaximumArchetypeVital)
                    diagnostics.Add($"People archetype '{id}' has invalid health range [{healthMin}, {healthMax}].");

                int fatigue = DaggerfallBaseContent.Integer(obj, "fatigue", diagnostics);
                int magicka = DaggerfallBaseContent.Integer(obj, "magicka", diagnostics);
                if (fatigue < 0 || fatigue > MaximumArchetypeVital)
                    diagnostics.Add($"People archetype '{id}' has invalid fatigue {fatigue}.");
                if (magicka < 0 || magicka > MaximumArchetypeVital)
                    diagnostics.Add($"People archetype '{id}' has invalid magicka {magicka}.");

                int armor = obj.TryGetProperty("armor", out JsonElement armorEl) && armorEl.ValueKind == JsonValueKind.Number
                    ? armorEl.GetInt32()
                    : 0;
                if (armor is < -100 or > 100)
                    diagnostics.Add($"People archetype '{id}' has out-of-range armor {armor}.");

                int level = obj.TryGetProperty("level", out JsonElement levelEl) && levelEl.ValueKind == JsonValueKind.Number
                    ? levelEl.GetInt32()
                    : 1;
                if (level < 1 || level > 100)
                    diagnostics.Add($"People archetype '{id}' has invalid level {level}.");

                int baseAttribute = DaggerfallBaseContent.Integer(obj, "baseAttribute", diagnostics);
                if (baseAttribute is < 1 or > 100)
                    diagnostics.Add($"People archetype '{id}' has invalid baseAttribute {baseAttribute}.");

                Dictionary<DaggerfallStatId, int> attributes = new();
                if (obj.TryGetProperty("attributes", out JsonElement attrsEl))
                {
                    if (attrsEl.ValueKind != JsonValueKind.Object)
                        diagnostics.Add($"People archetype '{id}' attributes must be an object.");
                    else
                    {
                        foreach (JsonProperty prop in attrsEl.EnumerateObject())
                        {
                            if (validAttributes is not null && !validAttributes.Contains(prop.Name))
                                diagnostics.Add($"People archetype '{id}' references unknown attribute '{prop.Name}'.");
                            if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out int val) || val is < 1 or > 100)
                                diagnostics.Add($"People archetype '{id}' attribute '{prop.Name}' must be an integer in 1..100.");
                            else
                                attributes[new DaggerfallStatId(prop.Name)] = val;
                        }
                    }
                }

                Dictionary<DaggerfallStatId, int> skills = new();
                if (obj.TryGetProperty("skills", out JsonElement skillsEl))
                {
                    if (skillsEl.ValueKind != JsonValueKind.Object)
                        diagnostics.Add($"People archetype '{id}' skills must be an object.");
                    else
                    {
                        foreach (JsonProperty prop in skillsEl.EnumerateObject())
                        {
                            if (validSkills is not null && !validSkills.Contains(prop.Name))
                                diagnostics.Add($"People archetype '{id}' references unknown skill '{prop.Name}'.");
                            if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out int val) || val is < 0 or > 100)
                                diagnostics.Add($"People archetype '{id}' skill '{prop.Name}' must be an integer in 0..100.");
                            else
                                skills[new DaggerfallStatId(prop.Name)] = val;
                        }
                    }
                }

                string harmRaw = DaggerfallBaseContent.Text(obj, "defaultHarm", diagnostics);
                string interactionRaw = DaggerfallBaseContent.Text(obj, "defaultInteraction", diagnostics);
                if (!ValidHarm(harmRaw))
                    diagnostics.Add($"People archetype '{id}' has invalid defaultHarm '{harmRaw}'.");
                if (!ValidInteraction(interactionRaw))
                    diagnostics.Add($"People archetype '{id}' has invalid defaultInteraction '{interactionRaw}'.");

                if (!obj.TryGetProperty("walks", out JsonElement walksEl) || walksEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    diagnostics.Add($"People archetype '{id}' walks must be a boolean.");
                bool walks = walksEl.ValueKind == JsonValueKind.True;

                List<DaggerfallLoadoutEntry> loadout = [];
                if (obj.TryGetProperty("loadout", out JsonElement loadoutEl))
                {
                    if (loadoutEl.ValueKind != JsonValueKind.Array)
                        diagnostics.Add($"People archetype '{id}' loadout must be an array.");
                    else
                    {
                        foreach (JsonElement entryEl in loadoutEl.EnumerateArray())
                        {
                            JsonElement entryObj = DaggerfallBaseContent.Object(entryEl, $"archetype '{id}' loadout entry", diagnostics);
                            string itemIdRaw = DaggerfallBaseContent.Text(entryObj, "itemId", diagnostics);
                            int qty = DaggerfallBaseContent.Integer(entryObj, "quantity", diagnostics);
                            if (qty <= 0)
                            {
                                diagnostics.Add($"People archetype '{id}' loadout item '{itemIdRaw}' must have positive quantity.");
                                continue;
                            }
                            DaggerfallItemId itemId = new(itemIdRaw);
                            if (items is not null)
                            {
                                if (!items.TryGetValue(itemId, out DaggerfallItemDefinition? itemDef))
                                    diagnostics.Add($"People archetype '{id}' loadout references unknown item '{itemIdRaw}'.");
                                else if (!itemDef.IsFungible)
                                    diagnostics.Add($"People archetype '{id}' loadout item '{itemIdRaw}' must be fungible for deterministic baseline spawning.");
                            }
                            loadout.Add(new DaggerfallLoadoutEntry(itemId, (ulong)qty, UniqueEntityId: null, EquipSlot: null));
                        }
                    }
                }

                ArchetypeProfile profile = new(
                    id,
                    new DaggerfallVitalRange(Math.Max(1, healthMin), Math.Max(Math.Max(1, healthMin), healthMax)),
                    Math.Max(0, fatigue),
                    Math.Max(0, magicka),
                    Math.Clamp(baseAttribute, 1, 100),
                    new ReadOnlyDictionary<DaggerfallStatId, int>(attributes),
                    new ReadOnlyDictionary<DaggerfallStatId, int>(skills),
                    DaggerfallPeopleSlots.ParseHarmPolicy(harmRaw, ActorHarmPolicyKind.LawProtectedCivilian),
                    DaggerfallPeopleSlots.ParseInteractionPolicy(interactionRaw),
                    Walks: walks,
                    Armor: armor,
                    Level: Math.Max(1, level),
                    Loadout: loadout.AsReadOnly());

                if (!result.TryAdd(id, profile))
                    diagnostics.Add($"People pack '{packId}' repeats archetype '{id}'.");
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"People pack '{packId}' archetypes file is malformed: {exception.Message}");
        }

        foreach (string requiredId in DaggerfallArchetypeIds.Required)
        {
            if (!result.ContainsKey(requiredId))
                diagnostics.Add($"People pack '{packId}' archetypes must define required archetype '{requiredId}'.");
        }
        return result;
    }

    private static Dictionary<string, DaggerfallCastIndividualDefinition> ReadIndividuals(
        string packId, ReadOnlyMemory<byte> bytes, DaggerfallFactionsSet? factions,
        IReadOnlyDictionary<string, ArchetypeProfile> archetypes, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, DaggerfallCastIndividualDefinition> result = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes);
            JsonElement root = DaggerfallBaseContent.Object(doc.RootElement, $"People pack '{packId}' cast", diagnostics);
            foreach (JsonElement item in DaggerfallBaseContent.Array(root, "individuals", diagnostics))
            {
                JsonElement obj = DaggerfallBaseContent.Object(item, "cast individual", diagnostics);
                string id = DaggerfallBaseContent.Text(obj, "id", diagnostics);
                int factionId = DaggerfallBaseContent.Integer(obj, "factionId", diagnostics);
                string name = DaggerfallBaseContent.Text(obj, "name", diagnostics);
                string? race = obj.TryGetProperty("race", out JsonElement raceEl) && raceEl.ValueKind != JsonValueKind.Null
                    ? raceEl.GetString()
                    : null;
                string gender = DaggerfallBaseContent.Text(obj, "gender", diagnostics);
                JsonElement visual = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(obj, "visual", diagnostics), $"cast '{id}' visual", diagnostics);
                int archive = DaggerfallBaseContent.Integer(visual, "archive", diagnostics);
                int record = DaggerfallBaseContent.Integer(visual, "record", diagnostics);
                string? visualAssetId = DaggerfallBaseContent.OptionalText(visual, "assetId", diagnostics);
                string? homeBlock = DaggerfallBaseContent.OptionalText(obj, "homeBlock", diagnostics);
                string? homeSlotId = DaggerfallBaseContent.OptionalText(obj, "homeSlotId", diagnostics);
                DaggerfallSiteId? homeSite = null;
                if (obj.TryGetProperty("homeSite", out JsonElement siteEl) && siteEl.ValueKind != JsonValueKind.Null)
                {
                    JsonElement site = DaggerfallBaseContent.Object(siteEl, $"cast '{id}' homeSite", diagnostics);
                    homeSite = new(DaggerfallBaseContent.Integer(site, "region", diagnostics), DaggerfallBaseContent.Integer(site, "index", diagnostics));
                }
                WorldPoint? homePosition = obj.TryGetProperty("homePosition", out JsonElement positionEl) && positionEl.ValueKind != JsonValueKind.Null
                    ? ReadPosition(positionEl, $"cast '{id}' home", diagnostics)
                    : null;
                string interaction = DaggerfallBaseContent.Text(obj, "interaction", diagnostics);
                string harm = DaggerfallBaseContent.Text(obj, "harm", diagnostics);
                string? archetypeId = obj.TryGetProperty("archetype", out JsonElement archEl) && archEl.ValueKind != JsonValueKind.Null
                    ? archEl.GetString()
                    : null;

                if (!DaggerfallBaseContent.ValidId(id)) diagnostics.Add($"Cast individual '{id}' has an invalid id.");
                if (gender is not ("Male" or "Female")) diagnostics.Add($"Cast individual '{id}' has invalid gender '{gender}'.");
                if (!ValidInteraction(interaction)) diagnostics.Add($"Cast individual '{id}' has invalid interaction '{interaction}'.");
                if (!ValidHarm(harm)) diagnostics.Add($"Cast individual '{id}' has invalid harm '{harm}'.");
                if (archive < 0 || record < 0 || factionId <= 0) diagnostics.Add($"Cast individual '{id}' has invalid visual or factionId.");
                if (homeBlock is null ? homeSlotId is not null || homePosition is not null || homeSite is not null : (homeSlotId is null) == (homePosition is null))
                    diagnostics.Add($"Cast individual '{id}' must name a homeBlock with exactly one of homeSlotId and homePosition, and a homeSite only with a homeBlock.");
                if (homePosition is not null && homeSite is null)
                    diagnostics.Add($"Cast individual '{id}' has a homePosition but no homeSite to set it in.");
                if (archetypeId is not null && !archetypes.ContainsKey(archetypeId))
                    diagnostics.Add($"Cast individual '{id}' references unknown archetype '{archetypeId}'.");
                if (factions is not null)
                {
                    if (!factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction))
                        diagnostics.Add($"Cast individual '{id}' references missing faction {factionId}.");
                    else if (faction.Type != IndividualFactionType)
                        diagnostics.Add($"Cast individual '{id}' references faction {factionId} ('{faction.Name}') of type {faction.Type}, expected Individual (4).");
                }
                if (!result.TryAdd(id, new(id, factionId, name, race, gender, archive, record, homeBlock, homeSlotId, interaction, harm, archetypeId, VisualAssetId: visualAssetId)
                    { HomeSite = homeSite, HomePosition = homePosition }))
                    diagnostics.Add($"People pack '{packId}' repeats cast individual '{id}'.");
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"People pack '{packId}' cast file is malformed: {exception.Message}");
        }
        return result;
    }

    private static Dictionary<string, DaggerfallPeopleSpawnerDefinition> ReadSpawners(
        string packId, ReadOnlyMemory<byte> bytes, IReadOnlyDictionary<string, ArchetypeProfile> archetypes,
        DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, DaggerfallPeopleSpawnerDefinition> result = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes);
            JsonElement root = DaggerfallBaseContent.Object(doc.RootElement, $"People pack '{packId}' spawners", diagnostics);
            foreach (JsonElement item in DaggerfallBaseContent.Array(root, "spawners", diagnostics))
            {
                JsonElement obj = DaggerfallBaseContent.Object(item, "people spawner", diagnostics);
                string id = DaggerfallBaseContent.Text(obj, "id", diagnostics);
                string role = DaggerfallBaseContent.Text(obj, "role", diagnostics);
                string? archetypeId = obj.TryGetProperty("archetype", out JsonElement archEl) && archEl.ValueKind != JsonValueKind.Null
                    ? archEl.GetString()
                    : null;
                string interaction = DaggerfallBaseContent.Text(obj, "interaction", diagnostics);
                string harm = DaggerfallBaseContent.Text(obj, "harm", diagnostics);
                bool deriveRegional = obj.TryGetProperty("deriveRegionalFaction", out JsonElement drv) && drv.ValueKind == JsonValueKind.True;
                string visualMode = DaggerfallBaseContent.Text(obj, "visualMode", diagnostics);

                if (!DaggerfallBaseContent.ValidId(id)) diagnostics.Add($"People spawner '{id}' has an invalid id.");
                if (archetypeId is not null && !archetypes.ContainsKey(archetypeId))
                    diagnostics.Add($"People spawner '{id}' references unknown archetype '{archetypeId}'.");
                if (!ValidInteraction(interaction)) diagnostics.Add($"People spawner '{id}' has invalid interaction '{interaction}'.");
                if (!ValidHarm(harm)) diagnostics.Add($"People spawner '{id}' has invalid harm '{harm}'.");
                if (visualMode is not ("classic-slot" or "pool")) diagnostics.Add($"People spawner '{id}' has invalid visualMode '{visualMode}'.");

                IReadOnlyList<DaggerfallPeoplePoolEntry> defaultPool = ReadPool(
                    DaggerfallBaseContent.Array(obj, "defaultPool", diagnostics), $"spawner '{id}' defaultPool", diagnostics);

                Dictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>> regionalPools = new(StringComparer.OrdinalIgnoreCase);
                if (obj.TryGetProperty("regionalPools", out JsonElement regionalObj) && regionalObj.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty regionProp in regionalObj.EnumerateObject())
                    {
                        IReadOnlyList<DaggerfallPeoplePoolEntry> pool = ReadPool(
                            regionProp.Value.ValueKind == JsonValueKind.Array ? regionProp.Value.EnumerateArray() : [],
                            $"spawner '{id}' regionalPools.{regionProp.Name}", diagnostics);
                        if (regionProp.Value.ValueKind != JsonValueKind.Array)
                            diagnostics.Add($"Spawner '{id}' regionalPools.'{regionProp.Name}' must be an array.");
                        else
                            regionalPools[regionProp.Name] = pool;
                    }
                }

                Dictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>> climatePools = new(StringComparer.OrdinalIgnoreCase);
                if (obj.TryGetProperty("climatePools", out JsonElement climateObj))
                {
                    if (climateObj.ValueKind != JsonValueKind.Object)
                    {
                        diagnostics.Add($"Spawner '{id}' climatePools must be an object.");
                    }
                    else
                    {
                        foreach (JsonProperty climateProp in climateObj.EnumerateObject())
                        {
                            if (!ValidClimateNames.Contains(climateProp.Name))
                                diagnostics.Add($"Spawner '{id}' climatePools references unknown climate '{climateProp.Name}'.");
                            if (climateProp.Value.ValueKind != JsonValueKind.Array)
                            {
                                diagnostics.Add($"Spawner '{id}' climatePools.'{climateProp.Name}' must be an array.");
                            }
                            else
                            {
                                IReadOnlyList<DaggerfallPeoplePoolEntry> pool = ReadPool(
                                    climateProp.Value.EnumerateArray(),
                                    $"spawner '{id}' climatePools.{climateProp.Name}", diagnostics);
                                if (pool.Count == 0)
                                    diagnostics.Add($"Spawner '{id}' climatePools.'{climateProp.Name}' must contain at least one entry.");
                                else
                                    climatePools[climateProp.Name] = pool;
                            }
                        }
                    }
                }

                if (!result.TryAdd(id, new(id, role, interaction, harm, deriveRegional, visualMode, defaultPool,
                        new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>(regionalPools), archetypeId,
                        new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>(climatePools))))
                    diagnostics.Add($"People pack '{packId}' repeats spawner '{id}'.");
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"People pack '{packId}' spawners file is malformed: {exception.Message}");
        }
        return result;
    }

    private static IReadOnlyList<DaggerfallPeoplePoolEntry> ReadPool(
        IEnumerable<JsonElement> array, string context, DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallPeoplePoolEntry> entries = [];
        foreach (JsonElement item in array)
        {
            JsonElement obj = DaggerfallBaseContent.Object(item, context, diagnostics);
            int archive = DaggerfallBaseContent.Integer(obj, "archive", diagnostics);
            int record = DaggerfallBaseContent.Integer(obj, "record", diagnostics);
            string gender = DaggerfallBaseContent.Text(obj, "gender", diagnostics);
            int weight = DaggerfallBaseContent.Integer(obj, "weight", diagnostics);
            if (archive < 0 || record < 0 || weight <= 0 || gender is not ("Male" or "Female"))
                diagnostics.Add($"{context} has an invalid pool entry ({archive}.{record}, gender='{gender}', weight={weight}).");
            else
                entries.Add(new(archive, record, gender, weight));
        }
        return entries.AsReadOnly();
    }

    private static void ReadBlockFile(
        string packId,
        DaggerfallPeopleBlockFile file,
        IReadOnlyDictionary<string, DaggerfallCastIndividualDefinition> individuals,
        IReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition> spawners,
        DaggerfallFactionsSet? factions,
        Dictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>> rmbInteriors,
        Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> rmbExteriors,
        Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> rdbDungeons,
        Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> siteExteriors,
        DaggerfallContentDiagnostics diagnostics)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(file.Bytes);
            JsonElement root = DaggerfallBaseContent.Object(doc.RootElement, $"People block file '{file.Path}'", diagnostics);
            string kind = DaggerfallBaseContent.Text(root, "kind", diagnostics);
            JsonElement blocksObj = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "blocks", diagnostics), $"People block file '{file.Path}' blocks", diagnostics);

            foreach (JsonProperty blockProp in blocksObj.EnumerateObject())
            {
                string blockKey = blockProp.Name;
                if (kind == "rmb-interior")
                {
                    if (blockProp.Value.ValueKind != JsonValueKind.Object)
                    {
                        diagnostics.Add($"People block '{blockKey}' in '{file.Path}' must map building indices to objects.");
                        continue;
                    }
                    Dictionary<int, DaggerfallPeopleBuildingSlots> buildings = [];
                    foreach (JsonProperty buildingProp in blockProp.Value.EnumerateObject())
                    {
                        if (!int.TryParse(buildingProp.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int buildingIndex))
                        {
                            diagnostics.Add($"People block '{blockKey}' has non-integer building key '{buildingProp.Name}'.");
                            continue;
                        }
                        JsonElement buildingObj = DaggerfallBaseContent.Object(buildingProp.Value, $"block '{blockKey}' building {buildingIndex}", diagnostics);
                        int buildingType = DaggerfallBaseContent.Integer(buildingObj, "buildingType", diagnostics);
                        int buildingFactionId = DaggerfallBaseContent.Integer(buildingObj, "buildingFactionId", diagnostics);
                        IReadOnlyList<DaggerfallPeopleSlotDefinition> slots = ReadSlots(
                            DaggerfallBaseContent.Array(buildingObj, "slots", diagnostics),
                            $"{blockKey}[{buildingIndex}]", individuals, spawners, factions, diagnostics);
                        buildings[buildingIndex] = new(buildingIndex, buildingType, buildingFactionId, slots);
                    }
                    if (!rmbInteriors.TryAdd(blockKey, new ReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>(buildings)))
                        diagnostics.Add($"People pack '{packId}' repeats rmb-interior block '{blockKey}'.");
                }
                else if (kind is "rmb-exterior" or "rdb-dungeon")
                {
                    if (blockProp.Value.ValueKind != JsonValueKind.Array)
                    {
                        diagnostics.Add($"People block '{blockKey}' in '{file.Path}' must map to a slot array.");
                        continue;
                    }
                    IReadOnlyList<DaggerfallPeopleSlotDefinition> slots = ReadSlots(
                        blockProp.Value.EnumerateArray(), blockKey, individuals, spawners, factions, diagnostics);
                    Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> target =
                        kind == "rmb-exterior" ? rmbExteriors : rdbDungeons;
                    if (!target.TryAdd(blockKey, slots))
                        diagnostics.Add($"People pack '{packId}' repeats {kind} block '{blockKey}'.");
                }
                else if (kind == "site-exterior")
                {
                    // A location's own people at one grid cell, keyed region/index/x,y: they are placed as the block's
                    // standing people are, which places no one who talks, so a slot here never offers dialogue.
                    string[] parts = blockKey.Split('/');
                    string[] cell = parts.Length == 3 ? parts[2].Split(',') : [];
                    if (cell.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int region)
                        || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                        || !int.TryParse(cell[0], NumberStyles.None, CultureInfo.InvariantCulture, out int x)
                        || !int.TryParse(cell[1], NumberStyles.None, CultureInfo.InvariantCulture, out int y)
                        || DaggerfallPeopleSet.SiteExteriorKey(new(region, index), x, y) != blockKey
                        || blockProp.Value.ValueKind != JsonValueKind.Array)
                    {
                        diagnostics.Add($"People site cell '{blockKey}' in '{file.Path}' must be keyed region/index/x,y and map to a slot array.");
                        continue;
                    }
                    IReadOnlyList<DaggerfallPeopleSlotDefinition> slots = ReadSlots(
                        blockProp.Value.EnumerateArray(), blockKey, individuals, spawners, factions, diagnostics);
                    foreach (DaggerfallPeopleSlotDefinition slot in slots.Where(slot => slot.Interaction == "full-dialogue" || slot.CastId is not null))
                        diagnostics.Add($"People site cell '{blockKey}' slot '{slot.SlotId}' must be a spawner's and not offer dialogue; a cast individual's home is its cast entry's.");
                    if (!siteExteriors.TryAdd(blockKey, slots))
                        diagnostics.Add($"People pack '{packId}' repeats site cell '{blockKey}'.");
                }
                else
                {
                    diagnostics.Add($"People block file '{file.Path}' has unsupported kind '{kind}'.");
                    return;
                }
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"People block file '{file.Path}' is malformed: {exception.Message}");
        }
    }

    private static IReadOnlyList<DaggerfallPeopleSlotDefinition> ReadSlots(
        IEnumerable<JsonElement> array,
        string scope,
        IReadOnlyDictionary<string, DaggerfallCastIndividualDefinition> individuals,
        IReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition> spawners,
        DaggerfallFactionsSet? factions,
        DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallPeopleSlotDefinition> result = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonElement item in array)
        {
            JsonElement obj = DaggerfallBaseContent.Object(item, $"{scope} slot", diagnostics);
            string slotId = DaggerfallBaseContent.Text(obj, "slotId", diagnostics);
            if (string.IsNullOrWhiteSpace(slotId) || !seen.Add(slotId))
                diagnostics.Add($"Block '{scope}' has missing or duplicate slotId '{slotId}'.");

            string? castId = obj.TryGetProperty("castId", out JsonElement castEl) && castEl.ValueKind == JsonValueKind.String ? castEl.GetString() : null;
            string? spawnerId = obj.TryGetProperty("spawner", out JsonElement spEl) && spEl.ValueKind == JsonValueKind.String ? spEl.GetString() : null;
            if ((castId is null) == (spawnerId is null))
                diagnostics.Add($"Slot '{scope}/{slotId}' must specify exactly one of castId or spawner.");
            else if (castId is not null && !individuals.ContainsKey(castId))
                diagnostics.Add($"Slot '{scope}/{slotId}' references unknown castId '{castId}'.");
            else if (spawnerId is not null)
            {
                if (individuals.ContainsKey(spawnerId))
                {
                    castId = spawnerId;
                    spawnerId = null;
                }
                else if (!spawners.ContainsKey(spawnerId))
                {
                    diagnostics.Add($"Slot '{scope}/{slotId}' references unknown spawner '{spawnerId}'.");
                }
            }

            WorldPoint position = ReadPosition(DaggerfallBaseContent.Property(obj, "position", diagnostics), $"{scope}/{slotId}", diagnostics);
            string interaction = DaggerfallBaseContent.Text(obj, "interaction", diagnostics);
            if (!ValidInteraction(interaction))
                diagnostics.Add($"Slot '{scope}/{slotId}' has invalid interaction '{interaction}'.");

            int? factionId = obj.TryGetProperty("factionId", out JsonElement fEl) && fEl.ValueKind == JsonValueKind.Number ? fEl.GetInt32() : null;
            bool deriveRegional = obj.TryGetProperty("deriveRegionalFaction", out JsonElement drvEl) && drvEl.ValueKind == JsonValueKind.True;

            if (factionId is int fid && factions is not null)
            {
                if (!factions.Factions.TryGetValue(fid, out DaggerfallFactionDefinition? faction))
                    diagnostics.Add($"Slot '{scope}/{slotId}' references missing faction {fid}.");
                else if (castId is null && faction.Type == IndividualFactionType)
                    diagnostics.Add($"Non-cast spawner slot '{scope}/{slotId}' illegally carries Type-4 Individual faction {fid} ('{faction.Name}').");
            }

            JsonElement vis = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(obj, "visual", diagnostics), $"{scope}/{slotId} visual", diagnostics);
            int archive = DaggerfallBaseContent.Integer(vis, "archive", diagnostics);
            int record = DaggerfallBaseContent.Integer(vis, "record", diagnostics);
            string gender = DaggerfallBaseContent.Text(vis, "gender", diagnostics);
            if (archive < 0 || record < 0 || gender is not ("Male" or "Female"))
                diagnostics.Add($"Slot '{scope}/{slotId}' has invalid visual ({archive}.{record}, '{gender}').");

            int? buildingType = obj.TryGetProperty("buildingType", out JsonElement btEl) && btEl.ValueKind == JsonValueKind.Number ? btEl.GetInt32() : null;
            int? buildingFactionId = obj.TryGetProperty("buildingFactionId", out JsonElement bfEl) && bfEl.ValueKind == JsonValueKind.Number ? bfEl.GetInt32() : null;

            result.Add(new(slotId, castId, spawnerId, position, interaction, factionId, deriveRegional,
                new DaggerfallPeopleVisual(archive, record, gender), buildingType, buildingFactionId));
        }
        return result.AsReadOnly();
    }

    private static WorldPoint ReadPosition(JsonElement element, string context, DaggerfallContentDiagnostics diagnostics)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 3)
        {
            diagnostics.Add($"Slot '{context}' position must be a 3-element array.");
            return default;
        }
        float x = element[0].GetSingle();
        float y = element[1].GetSingle();
        float z = element[2].GetSingle();
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
        {
            diagnostics.Add($"Slot '{context}' position has a non-finite coordinate.");
            return default;
        }
        return new(x, y, z);
    }

    private static bool ValidInteraction(string value) => value is "none" or "ambient-bark" or "full-dialogue";
    private static bool ValidHarm(string value) => value is "protected" or "law-protected-civilian" or "combatant";
}

/// <summary>
/// Deterministic slot-derived durable IDs, independent per-slot hashing (<c>Hash64(WorldSocialSeed, SiteId, SlotKey)</c>),
/// and regional typed spawner resolution shared by block spawner slots and exterior walkers.
/// </summary>
internal static class DaggerfallPeopleSlots
{
    private const ulong HighBandMask = 0x3fff_ffff_ffffUL;
    private const long HighBandBit = 0x4000_0000_0000L;
    private const int RegionalPeopleFactionType = 15;

    /// <summary>
    /// Whether billboard texture (<paramref name="archive"/>, <paramref name="record"/>) is a person flat
    /// that must materialize as a real actor rather than a decorative scenery billboard.
    /// Excludes non-person prop <c>(197, 6)</c>.
    /// </summary>
    internal static bool IsPersonClutterBillboard(int archive, int record) =>
        archive is (>= 175 and <= 184) or 186 or 197 or 334 or 346 or 357
        && !(archive == 197 && record == 6);

    internal static ActorInteractionPolicyKind ParseInteractionPolicy(string? value) => value switch
    {
        "none" => ActorInteractionPolicyKind.None,
        "ambient-bark" => ActorInteractionPolicyKind.AmbientBark,
        _ => ActorInteractionPolicyKind.FullDialogue,
    };

    internal static ActorHarmPolicyKind ParseHarmPolicy(string? value, ActorHarmPolicyKind fallback = ActorHarmPolicyKind.Protected) => value switch
    {
        "protected" => ActorHarmPolicyKind.Protected,
        "law-protected-civilian" => ActorHarmPolicyKind.LawProtectedCivilian,
        "combatant" => ActorHarmPolicyKind.Combatant,
        _ => fallback,
    };

    /// <summary>
    /// Canonical placed static-person slot identifier emitted by <see cref="DaggerfallLocationAssembly"/>
    /// for an exterior RMB block slot, shared with pre-visit referral targeting so both derive the exact same ID.
    /// </summary>
    internal static string PlacedExteriorBlockPersonId(string blockSourceName, int blockX, int blockY, string templateSlotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockSourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSlotId);
        string slug = BlockScopeSlug(blockSourceName);
        string sanitized = templateSlotId.Replace(':', '-');
        return string.Create(CultureInfo.InvariantCulture, $"person/{slug}/{blockX}/{blockY}/{sanitized}");
    }

    internal static string BlockScopeSlug(string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        System.Text.StringBuilder builder = new(sourceName.Length);
        bool separator = false;
        foreach (char c in sourceName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                separator = false;
            }
            else if (!separator && builder.Length > 0)
            {
                builder.Append('-');
                separator = true;
            }
        }
        return builder.ToString().TrimEnd('-');
    }

    /// <summary>Canonical site-qualified slot key for an exterior RMB block person slot at grid cell (<paramref name="blockX"/>, <paramref name="blockY"/>).</summary>
    internal static string ExteriorBlockSlotKey(int blockX, int blockY, string templateSlotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSlotId);
        return string.Create(CultureInfo.InvariantCulture, $"ext:{blockX},{blockY}:{templateSlotId}");
    }

    /// <summary>Canonical site-qualified slot key for an interior RMB building slot placed on grid cell (<paramref name="blockX"/>, <paramref name="blockY"/>).</summary>
    internal static string InteriorBuildingSlotKey(int blockX, int blockY, string templateSlotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSlotId);
        return string.Create(CultureInfo.InvariantCulture, $"int:{blockX},{blockY}:{templateSlotId}");
    }

    /// <summary>Canonical site-qualified slot key for an RDB dungeon block slot placed at grid cell (<paramref name="blockX"/>, <paramref name="blockZ"/>).</summary>
    internal static string DungeonBlockSlotKey(int blockX, int blockZ, string templateSlotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSlotId);
        return string.Create(CultureInfo.InvariantCulture, $"dng:{blockX},{blockZ}:{templateSlotId}");
    }

    /// <summary>Canonical slot key for a procedural exterior walker at <paramref name="index"/>.</summary>
    internal static string WalkerSlotKey(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return string.Create(CultureInfo.InvariantCulture, $"w:{index:D3}");
    }

    /// <summary>
    /// Deterministic durable actor ID for any <c>SeededSlot(SiteId, SlotKey)</c>, in the same <c>0x4000_0000_0000L</c>
    /// high-bit band as <see cref="DaggerfallSiteContent.FixedActorIdentity"/> and disjoint from monotonic allocator IDs.
    /// </summary>
    internal static long SeededSlotIdentity(DaggerfallSiteId site, string slotKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotKey);
        return (long)(StableHash.NonZeroFnv1a64($"daggerfall.seeded-slot:{site.Region}:{site.Index}:{slotKey}") & HighBandMask) | HighBandBit;
    }

    /// <summary>Deterministic durable actor ID for a unique <c>cast/individuals.json</c> individual.</summary>
    internal static long CastIndividualIdentity(string castId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(castId);
        return (long)(StableHash.NonZeroFnv1a64($"daggerfall.cast:{castId}") & HighBandMask) | HighBandBit;
    }

    /// <summary>Whether <paramref name="durableId"/> sits in the deterministic high-bit band (seeded slot, cast individual, or fixed placement).</summary>
    internal static bool IsDeterministicId(long durableId) => durableId > 0 && (durableId & HighBandBit) != 0;

    /// <summary>
    /// Independent per-slot 64-bit seed derived strictly from <paramref name="worldSocialSeed"/>, <paramref name="site"/>,
    /// and <paramref name="slotKey"/> — completely separate from <see cref="DaggerfallGameRandom.Seed"/>.
    /// </summary>
    internal static ulong SlotSeed(ulong worldSocialSeed, DaggerfallSiteId site, string slotKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotKey);
        ulong slotHash = StableHash.NonZeroFnv1a64($"daggerfall.slot-seed:{site.Region}:{site.Index}:{slotKey}");
        ulong z = unchecked(slotHash ^ DaggerfallGameRandom.Mix(worldSocialSeed, 0x9E3779B97F4A7C15UL));
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    /// <summary>The region's people faction (<c>Type == 15</c>), used for both exterior walkers and interior regional people.</summary>
    internal static DaggerfallFactionDefinition? ResolveRegionalPeopleFaction(DaggerfallFactionsSet factions, int region)
    {
        ArgumentNullException.ThrowIfNull(factions);
        return factions.Factions.Values
            .Where(faction => faction.Type == RegionalPeopleFactionType && faction.Region == region)
            .OrderBy(faction => faction.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Resolves a block slot against <paramref name="people"/>, <paramref name="factions"/>, <paramref name="worldSocialSeed"/>,
    /// <paramref name="site"/>, and its canonical site-qualified <paramref name="placementSlotKey"/> (which must qualify
    /// the placed block/building so repeated or distinct block templates at one site never collide).
    /// Regional spawner pools override <c>defaultPool</c>; otherwise <c>visualMode == "pool"</c> draws from <c>defaultPool</c>
    /// and <c>visualMode == "classic-slot"</c> (or an empty pool) falls back to the slot's classic visual.
    /// </summary>
    internal static DaggerfallResolvedPeopleSlot ResolveSlot(
        DaggerfallPeopleSet people,
        DaggerfallFactionsSet factions,
        ulong worldSocialSeed,
        DaggerfallSiteId site,
        string placementSlotKey,
        DaggerfallPeopleSlotDefinition slot,
        string? regionKey = null,
        string? climateKey = null)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(factions);
        ArgumentException.ThrowIfNullOrWhiteSpace(placementSlotKey);
        ArgumentNullException.ThrowIfNull(slot);

        ulong seed = SlotSeed(worldSocialSeed, site, placementSlotKey);

        if (slot.CastId is { } castId)
        {
            DaggerfallCastIndividualDefinition cast = people.Individuals[castId];
            return new(
                CastIndividualIdentity(castId),
                placementSlotKey,
                seed,
                castId,
                SpawnerId: null,
                CastName: cast.Name,
                Role: "person",
                Interaction: cast.Interaction,
                Harm: cast.Harm,
                Visual: new DaggerfallPeopleVisual(cast.Archive, cast.Record, cast.Gender),
                PrimaryFactionId: cast.FactionId,
                RegionalPeopleFactionId: null,
                Position: slot.Position);
        }

        DaggerfallPeopleSpawnerDefinition spawner = people.Spawners[slot.SpawnerId!];
        DaggerfallPeopleVisual visual = PickVisual(spawner, slot.ClassicVisual, seed, site.Region, regionKey, climateKey);
        int? regionalFactionId = slot.DeriveRegionalFaction || spawner.DeriveRegionalFaction
            ? ResolveRegionalPeopleFaction(factions, site.Region)?.Id
            : null;
        int? primaryFactionId = slot.FactionId ?? regionalFactionId;

        return new(
            SeededSlotIdentity(site, placementSlotKey),
            placementSlotKey,
            seed,
            CastId: null,
            SpawnerId: spawner.Id,
            CastName: null,
            Role: spawner.Role,
            Interaction: slot.Interaction,
            Harm: spawner.Harm,
            Visual: visual,
            PrimaryFactionId: primaryFactionId,
            RegionalPeopleFactionId: regionalFactionId,
            Position: slot.Position);
    }

    private static DaggerfallPeopleVisual PickVisual(
        DaggerfallPeopleSpawnerDefinition spawner,
        DaggerfallPeopleVisual fallback,
        ulong seed,
        int regionIndex,
        string? regionKey,
        string? climateKey)
    {
        IReadOnlyList<DaggerfallPeoplePoolEntry>? pool = null;
        if (!string.IsNullOrWhiteSpace(regionKey) && spawner.RegionalPools.TryGetValue(regionKey, out IReadOnlyList<DaggerfallPeoplePoolEntry>? byName) && byName.Count > 0)
            pool = byName;
        else if (spawner.RegionalPools.TryGetValue(regionIndex.ToString(CultureInfo.InvariantCulture), out IReadOnlyList<DaggerfallPeoplePoolEntry>? byIndex) && byIndex.Count > 0)
            pool = byIndex;
        else if (!string.IsNullOrWhiteSpace(climateKey) && spawner.ClimatePools.TryGetValue(climateKey, out IReadOnlyList<DaggerfallPeoplePoolEntry>? byClimate) && byClimate.Count > 0)
            pool = byClimate;
        else if (spawner.VisualMode == "pool" && spawner.DefaultPool.Count > 0)
            pool = spawner.DefaultPool;

        if (pool is null || pool.Count == 0)
            return fallback;

        long totalWeight = 0;
        for (int i = 0; i < pool.Count; i++) totalWeight += pool[i].Weight;
        long pick = (long)(seed % (ulong)totalWeight);
        long cumulative = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            cumulative += pool[i].Weight;
            if (pick < cumulative)
                return new DaggerfallPeopleVisual(pool[i].Archive, pool[i].Record, pool[i].Gender);
        }
        return new DaggerfallPeopleVisual(pool[^1].Archive, pool[^1].Record, pool[^1].Gender);
    }
}
