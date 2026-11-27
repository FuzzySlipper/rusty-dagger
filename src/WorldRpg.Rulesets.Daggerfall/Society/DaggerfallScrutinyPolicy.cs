using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Intensity of watch/gate and service scrutiny in a region.</summary>
internal enum DaggerfallScrutinyLevel
{
    /// <summary>Baseline classic rules (wanted below -10 or banished).</summary>
    Normal = 0,

    /// <summary>Elevated scrutiny (severity 1 condition: wanted threshold tightens to -5).</summary>
    Heightened = 1,

    /// <summary>Strict inspection/curfew (severity 2+ condition: wanted threshold tightens to 0).</summary>
    Strict = 2,
}

/// <summary>Gate and town-watch scrutiny outcome for a region given standing and active world conditions.</summary>
internal sealed record DaggerfallGateScrutinyAssessment(
    DaggerfallScrutinyLevel Level,
    int Region,
    bool PlayerExempt,
    bool WatchChallengeActive,
    int EffectiveWantedThreshold,
    int WatchDetectionChancePercent,
    string? Reason,
    string? ConditionTokenId = null);

/// <summary>Service-provider scrutiny outcome for one NPC given legal standing, active conditions, and personal exemptions.</summary>
internal sealed record DaggerfallServiceScrutinyAssessment(
    bool Denied,
    DaggerfallServiceDenial Denial,
    string? Reason,
    string? ConditionTokenId = null);

/// <summary>
/// Evaluates gate/watch scrutiny, lawful service scrutiny, guild embargoes, and regional scarcity surcharges
/// from the player's standing, personal vouches/allies, and active <see cref="IDaggerfallWorldConditionSource"/> tokens.
/// When no world conditions are active, all thresholds remain identical to baseline classic Daggerfall rules.
/// </summary>
internal sealed class DaggerfallScrutinyPolicy(
    DaggerfallSocialState social,
    DaggerfallCrimeState crime,
    DaggerfallLegalServicePolicy legalServices,
    DaggerfallActorLedger actorLedger,
    IDaggerfallWorldConditionSource conditions,
    DaggerfallDefinitions definitions,
    Func<int> currentDay,
    Func<long, FactionComponent?> actorFactions,
    Func<DaggerfallInteriorBuilding?> currentInteriorBuilding,
    Func<DaggerfallSiteRecord?, bool>? hasWatchOrRulerVouch = null,
    DaggerfallWordSpread? wordSpread = null,
    Func<long>? currentMinute = null,
    Func<DaggerfallSiteRecord?>? activeSite = null)
{
    internal const int HighStandingExemptThreshold = 20;
    internal const int HeightenedWantedThreshold = -5;
    internal const int StrictWantedThreshold = 0;

    /// <summary>Effective regional reputation known at <paramref name="site"/> (accounting for crimes whose couriers have not arrived yet).</summary>
    internal int EffectiveRegionalReputation(int region, DaggerfallSiteRecord? site = null)
    {
        int raw = social.RegionalReputation(region);
        DaggerfallSiteRecord? resolvedSite = site ?? activeSite?.Invoke();
        if (wordSpread is null || currentMinute is null || resolvedSite is null || resolvedSite.Id.Region != region)
            return raw;
        return raw + wordSpread.UnarrivedCrimePenaltyOffset(resolvedSite, currentMinute(), currentDay());
    }

    /// <summary>Evaluates gate and watch scrutiny in <paramref name="region"/> (optionally at <paramref name="site"/>).</summary>
    internal DaggerfallGateScrutinyAssessment EvaluateGateScrutiny(int region, DaggerfallSiteRecord? site = null)
    {
        bool banished = crime.BanishedRegions.Contains(region);
        int regionalRep = EffectiveRegionalReputation(region, site);
        int day = currentDay();

        DaggerfallWorldConditionToken? driving = SelectGateScrutinyCondition(conditions.ActiveForRegion(region, day));
        if (driving is null)
        {
            bool baseWanted = regionalRep < DaggerfallLegalServicePolicy.WantedReputationThreshold;
            int baseChance = banished ? 10 : baseWanted ? 5 : 0;
            return new(
                Level: DaggerfallScrutinyLevel.Normal,
                Region: region,
                PlayerExempt: false,
                WatchChallengeActive: banished || baseWanted,
                EffectiveWantedThreshold: DaggerfallLegalServicePolicy.WantedReputationThreshold,
                WatchDetectionChancePercent: baseChance,
                Reason: banished ? "You are banished from this region." : baseWanted ? "The watch knows your face here." : null,
                ConditionTokenId: null);
        }

        DaggerfallScrutinyLevel level = driving.Severity >= 2
            ? DaggerfallScrutinyLevel.Strict
            : DaggerfallScrutinyLevel.Heightened;
        int threshold = level == DaggerfallScrutinyLevel.Strict ? StrictWantedThreshold : HeightenedWantedThreshold;
        bool vouched = hasWatchOrRulerVouch?.Invoke(site) == true;
        bool exempt = !banished && (regionalRep >= HighStandingExemptThreshold || vouched);
        bool challenge = banished || (!exempt && regionalRep < threshold);
        int chance = challenge
            ? Math.Min(30, (banished ? 10 : 5) + 5 * driving.Severity)
            : 0;

        string regionName = definitions.BuildingNames.RegionName(region);
        string reason = driving.Kind switch
        {
            DaggerfallWorldConditionKind.CrimeCrackdown =>
                exempt
                    ? $"Watch patrols in {regionName} are checking papers under a crime crackdown, but your standing clears you."
                    : $"The watch in {regionName} is enforcing {(level == DaggerfallScrutinyLevel.Strict ? "strict" : "heightened")} gate scrutiny under a crime crackdown.",
            DaggerfallWorldConditionKind.BorderTension =>
                exempt
                    ? $"Border patrols in {regionName} are questioning travelers, though your standing gets you waved through."
                    : $"Gates in {regionName} are under {(level == DaggerfallScrutinyLevel.Strict ? "strict" : "heightened")} military scrutiny amid border tensions.",
            DaggerfallWorldConditionKind.PlagueScare =>
                exempt
                    ? $"Gate wardens in {regionName} are inspecting travelers for contagion, but wave you through."
                    : $"Quarantine watch is active at the gates of {regionName}.",
            DaggerfallWorldConditionKind.SuccessionCrisis =>
                exempt
                    ? $"Court guards in {regionName} are questioning outsiders during the succession dispute, but know your name."
                    : $"Gate scrutiny is tightened across {regionName} while the succession remains unsettled.",
            _ => $"Gate scrutiny is heightened in {regionName}.",
        };

        return new(
            Level: level,
            Region: region,
            PlayerExempt: exempt,
            WatchChallengeActive: challenge,
            EffectiveWantedThreshold: threshold,
            WatchDetectionChancePercent: chance,
            Reason: reason,
            ConditionTokenId: driving.TokenId);
    }

    /// <summary>Evaluates whether <paramref name="npc"/> refuses service due to legal standing or active political conditions.</summary>
    internal DaggerfallServiceScrutinyAssessment EvaluateServiceScrutiny(DaggerfallNpc npc, DaggerfallInteriorBuilding? building = null)
    {
        ArgumentNullException.ThrowIfNull(npc);
        building ??= currentInteriorBuilding();

        // 1. Baseline legal denial (wanted < -10 or banished, respecting outlaw exemptions and courier word spread).
        int effectiveRep = EffectiveRegionalReputation(npc.Site.Region);
        bool knownWantedOrBanished = crime.BanishedRegions.Contains(npc.Site.Region)
            || effectiveRep < DaggerfallLegalServicePolicy.WantedReputationThreshold;
        if (knownWantedOrBanished)
        {
            if (legalServices.IsOutlawExempt(npc, building) || HasTrustedVouchFor(npc.DurableId))
                return new(false, DaggerfallServiceDenial.None, null);
            return new(true, DaggerfallServiceDenial.WantedOrBanished, "I do not deal with wanted outlaws.");
        }

        int region = npc.Site.Region;
        int day = currentDay();
        IReadOnlyList<DaggerfallWorldConditionToken> active = conditions.ActiveForRegion(region, day);
        if (active.Count == 0)
            return new(false, DaggerfallServiceDenial.None, null);

        // Personal allies, trusted vouches, and outlaw-exempt underworld providers bypass condition scrutiny.
        bool exempt = legalServices.IsOutlawExempt(npc, building) || HasTrustedVouchFor(npc.DurableId);

        // 2. Guild / faction embargo: refuses non-members or rival-faction members when this provider's faction is under embargo.
        int providerFactionId = ResolveProviderFactionId(npc, building);
        if (providerFactionId > 0)
        {
            foreach (DaggerfallWorldConditionToken token in active)
            {
                if (token.Kind != DaggerfallWorldConditionKind.GuildEmbargo)
                    continue;
                if (!MatchesFactionLineage(providerFactionId, token.SourceFactionId))
                    continue;
                if (exempt)
                    continue;

                bool isMember = IsMemberOfFactionOrDescendant(token.SourceFactionId!.Value);
                bool isRivalMember = token.TargetFactionId is int rivalId && rivalId > 0
                    && IsMemberOfFactionOrDescendant(rivalId);
                if (!isMember || isRivalMember)
                {
                    string factionName = definitions.Factions.Factions.TryGetValue(token.SourceFactionId.Value, out DaggerfallFactionDefinition? def)
                        ? def.Name
                        : "the guild";
                    string reason = isRivalMember
                        ? $"By decree of {factionName}, services are closed to rival members during this embargo."
                        : $"By decree of {factionName}, services are restricted to members during this embargo.";
                    return new(true, DaggerfallServiceDenial.UnderScrutiny, reason, token.TokenId);
                }
            }
        }

        // 3. Heightened / strict regional watch scrutiny tightens lawful service standing threshold from -10 to -5 / 0.
        DaggerfallGateScrutinyAssessment gate = EvaluateGateScrutiny(region);
        if (gate.Level != DaggerfallScrutinyLevel.Normal && !exempt
            && effectiveRep < gate.EffectiveWantedThreshold)
        {
            return new(true, DaggerfallServiceDenial.UnderScrutiny,
                gate.Reason ?? "Local authorities have ordered strict scrutiny; I cannot serve you.",
                gate.ConditionTokenId);
        }

        return new(false, DaggerfallServiceDenial.None, null);
    }

    /// <summary>Whether room rental at <paramref name="building"/> in <paramref name="region"/> is denied under legal standing or active scrutiny.</summary>
    internal bool IsRoomRentalDenied(int region, DaggerfallInteriorBuilding? building, DaggerfallNpc? tavernKeeper = null)
    {
        int effectiveRep = EffectiveRegionalReputation(region);
        bool knownWantedOrBanished = crime.BanishedRegions.Contains(region)
            || effectiveRep < DaggerfallLegalServicePolicy.WantedReputationThreshold;
        if (knownWantedOrBanished)
        {
            if (tavernKeeper is not null && (legalServices.IsOutlawExempt(tavernKeeper, building) || HasTrustedVouchFor(tavernKeeper.DurableId)))
                return false;
            return !legalServices.IsShadyOrCriminalBuilding(building);
        }
        DaggerfallGateScrutinyAssessment gate = EvaluateGateScrutiny(region);
        if (gate.Level == DaggerfallScrutinyLevel.Normal || effectiveRep >= gate.EffectiveWantedThreshold)
            return false;
        if (tavernKeeper is not null && (legalServices.IsOutlawExempt(tavernKeeper, building) || HasTrustedVouchFor(tavernKeeper.DurableId)))
            return false;
        return !legalServices.IsShadyOrCriminalBuilding(building);
    }

    private bool HasTrustedVouchFor(long npcDurableId) =>
        actorLedger.TryGet(npcDurableId, out ActorLedgerEntry? entry)
        && entry?.Social?.Vouches is { Length: > 0 };

    private int ResolveProviderFactionId(DaggerfallNpc npc, DaggerfallInteriorBuilding? building)
    {
        FactionComponent? fc = actorFactions(npc.DurableId);
        if (fc?.PrimaryFactionId is int pf && pf > 0) return pf;
        if (npc.Appearance.FactionId > 0) return npc.Appearance.FactionId;
        if (building is { FactionId: > 0 } b) return b.FactionId;
        return 0;
    }

    private bool IsMemberOfFactionOrDescendant(int factionId)
    {
        if (factionId <= 0 || !definitions.Factions.Factions.ContainsKey(factionId))
            return false;
        if (social.GuildEligibility(factionId).IsMember)
            return true;
        foreach (DaggerfallSocialAffiliationView affiliation in social.ReadAffiliations())
        {
            if (affiliation.FactionId > 0 && MatchesFactionLineage(affiliation.FactionId, factionId))
                return true;
        }
        return false;
    }

    private bool MatchesFactionLineage(int candidateFactionId, int? targetFactionId)
    {
        if (targetFactionId is not int wanted || wanted <= 0 || candidateFactionId <= 0)
            return false;
        return social.Liege.Lineage(candidateFactionId).Any(faction => faction.Id == wanted);
    }

    private static DaggerfallWorldConditionToken? SelectGateScrutinyCondition(IReadOnlyList<DaggerfallWorldConditionToken> active)
    {
        foreach (DaggerfallWorldConditionToken token in active)
        {
            if (token.Kind is DaggerfallWorldConditionKind.CrimeCrackdown
                or DaggerfallWorldConditionKind.BorderTension
                or DaggerfallWorldConditionKind.PlagueScare
                or DaggerfallWorldConditionKind.SuccessionCrisis)
                return token;
        }
        return null;
    }
}
