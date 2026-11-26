using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;

namespace WorldRpg.Rulesets.Daggerfall.Crime;

/// <summary>Victim metadata supplied by a crime caller so witnesses can test family and guild ties.</summary>
internal sealed record DaggerfallCrimeVictimContext(
    long? ActorId = null,
    int? FactionId = null,
    string? BuildingKey = null,
    string? FamilyId = null,
    string? DisplayName = null,
    int? Region = null,
    string? Location = null,
    int? BuildingType = null);

/// <summary>
/// Witness classification attributes captured while the source scene is live, so scene transitions that unload
/// source actors (such as exterior-to-interior building entry) preserve city-watch, guild, family, and disposition data.
/// </summary>
internal sealed record DaggerfallWitnessObservation(
    long ActorId,
    bool IsGuard,
    DaggerfallNpc? Npc,
    string? FamilyId,
    IReadOnlySet<int> GuildRoots,
    int DispositionDelta);

/// <summary>Why a witness chose to report or ignore a witnessed crime.</summary>
internal enum DaggerfallWitnessReportReason
{
    CityWatchDuty,
    VictimSelf,
    FamilyMember,
    FellowGuildMember,
    DiscretionRollReported,
    DiscretionRollIgnored,
}

/// <summary>Per-witness reporting outcome for one observed crime.</summary>
internal sealed record DaggerfallWitnessDecision(
    long WitnessActorId,
    bool Reported,
    DaggerfallWitnessReportReason Reason,
    int ReportChancePercent,
    int? RollValue = null);

/// <summary>Aggregate witness discretion result: reporting evidence passed to the law plus per-witness breakdown.</summary>
internal sealed record DaggerfallWitnessDiscretionResult(
    DaggerfallCrimeWitnessEvidence ReportingEvidence,
    IReadOnlyList<DaggerfallWitnessDecision> Decisions);

/// <summary>
/// Witness discretion policy for crime reporting:
/// - City watch / guards and the victim themselves always report.
/// - Witnesses with a strong connection to the victim (family members or fellow guild members) never ignore the crime.
/// - Unconnected witnesses decide whether to report by a bounded dice roll affected by the player's social skills
///   (Etiquette, Streetwise, Personality) and the witness's personal standing with the player.
/// </summary>
internal static class DaggerfallWitnessDiscretion
{
    internal const int MinimumUnconnectedChance = 5;
    internal const int MaximumUnconnectedChance = 95;
    private const int GenericMerchantsFactionId = 510;
    private const int GenericTempleFactionId = 450;
    private const int GenericKnightlyOrderFactionId = 844;
    private const int GodFactionType = 1;
    private const int ProvinceFactionType = 7;
    private const int RegionalPeopleFactionType = 15;
    private const int GuildFactionType = 2;
    private const int SubgroupFactionType = 3;
    private const int SubrankFactionType = 5;
    private const int VampireClanFactionType = 6;
    private const int WitchesCovenFactionType = 8;
    private const int TempleFactionType = 9;
    private const int CourtFactionType = 14;

    /// <summary>
    /// Base reporting probability (0..100) for an unconnected bystander before social-skill and disposition adjustments.
    /// Petty/property crimes (such as lockpicking a neighbour's door) have a moderate base chance so social skills
    /// can meaningfully sway bystanders to look away; violent crimes stay much harder to shrug off.
    /// </summary>
    internal static int BaseUnconnectedReportChance(DaggerfallCrimeKind crime) => crime switch
    {
        DaggerfallCrimeKind.Murder => 95,
        DaggerfallCrimeKind.Assault => 80,
        DaggerfallCrimeKind.AttemptedBreakingAndEntering
            or DaggerfallCrimeKind.Trespassing
            or DaggerfallCrimeKind.BreakingAndEntering
            or DaggerfallCrimeKind.Pickpocketing
            or DaggerfallCrimeKind.Theft => 55,
        _ => 65,
    };

    /// <summary>
    /// Computes the final reporting percentage (clamped to [5, 95]) for an unconnected witness based on crime severity,
    /// the player's social skills (Etiquette, Streetwise, Personality), and the witness's personal standing delta.
    /// </summary>
    internal static int CalculateUnconnectedReportChance(
        DaggerfallCrimeKind crime,
        int etiquette,
        int streetwise,
        int personality,
        int witnessDispositionDelta = 0)
    {
        int clampedEtiquette = Math.Clamp(etiquette, 0, 100);
        int clampedStreetwise = Math.Clamp(streetwise, 0, 100);
        int clampedPersonality = Math.Clamp(personality, 0, 100);

        int baseChance = BaseUnconnectedReportChance(crime);
        int socialScore = (Math.Max(clampedEtiquette, clampedStreetwise) * 2 + clampedPersonality) / 3;
        int skillReduction = socialScore * 45 / 100;
        int dispositionAdjustment = Math.Clamp(witnessDispositionDelta, -25, 25) / 2;
        return Math.Clamp(baseChance - skillReduction - dispositionAdjustment, MinimumUnconnectedChance, MaximumUnconnectedChance);
    }

    /// <summary>
    /// Captures live guard, NPC, family, guild, and disposition metadata for every witness in
    /// <paramref name="rawWitnesses"/> before a scene transition unloads the source actors.
    /// </summary>
    internal static IReadOnlyDictionary<long, DaggerfallWitnessObservation> CaptureObservations(
        DaggerfallCrimeWitnessEvidence rawWitnesses,
        DaggerfallDefinitions definitions,
        ActorsState actors,
        DaggerfallNpcRegistry npcs,
        DaggerfallActorLedger? actorLedger,
        Func<long, bool> isCityWatch)
    {
        ArgumentNullException.ThrowIfNull(rawWitnesses);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(npcs);
        ArgumentNullException.ThrowIfNull(isCityWatch);

        Dictionary<long, DaggerfallWitnessObservation> captured = [];
        if (rawWitnesses.Query != DaggerfallCrimeWitnessQuery.CompletedWithWitnesses
            || rawWitnesses.WitnessActorIds.Length == 0)
            return captured;

        foreach (long witnessId in rawWitnesses.WitnessActorIds)
        {
            DaggerfallNpc? witnessNpc = npcs.TryGet(witnessId, out DaggerfallNpc? wn) ? wn : null;
            bool guard = IsGuard(witnessId, actors, npcs, isCityWatch);
            string? familyId = ResolveActorFamilyId(witnessId, actors, actorLedger, npcs);
            HashSet<int> guildRoots = CollectActorGuildRoots(witnessId, witnessNpc, definitions, actors);
            int dispositionDelta = actorLedger is not null && actorLedger.TryGet(witnessId, out ActorLedgerEntry? ledgerEntry)
                && ledgerEntry?.Social is { } socialDiff
                ? socialDiff.DispositionDelta
                : 0;
            captured[witnessId] = new DaggerfallWitnessObservation(witnessId, guard, witnessNpc, familyId, guildRoots, dispositionDelta);
        }
        return captured;
    }

    /// <summary>
    /// Evaluates every witness in <paramref name="rawWitnesses"/> and returns only the witnesses who choose to report.
    /// </summary>
    internal static DaggerfallWitnessDiscretionResult Evaluate(
        string operation,
        DaggerfallCrimeKind crime,
        DaggerfallCrimeVictimContext? victim,
        DaggerfallCrimeWitnessEvidence rawWitnesses,
        DaggerfallDefinitions definitions,
        ActorsState actors,
        DaggerfallNpcRegistry npcs,
        DaggerfallActorLedger? actorLedger,
        Func<long, bool> isCityWatch,
        Func<string, string, int, int, int> roll,
        IReadOnlyDictionary<long, DaggerfallWitnessObservation>? capturedObservations = null)
    {
        ArgumentNullException.ThrowIfNull(rawWitnesses);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(npcs);
        ArgumentNullException.ThrowIfNull(isCityWatch);
        ArgumentNullException.ThrowIfNull(roll);

        if (rawWitnesses.Query != DaggerfallCrimeWitnessQuery.CompletedWithWitnesses
            || rawWitnesses.WitnessActorIds.Length == 0)
            return new(rawWitnesses, []);

        int etiquette = ReadPlayerStat(actors, "etiquette", 50);
        int streetwise = ReadPlayerStat(actors, "streetwise", 50);
        int personality = ReadPlayerStat(actors, "personality", 50);

        HashSet<int> victimGuildRoots = CollectVictimGuildRoots(victim, definitions, actors, npcs);
        string? victimFamilyId = ResolveVictimFamilyId(victim, actors, actorLedger, npcs);

        List<long> reporting = [];
        List<DaggerfallWitnessDecision> decisions = [];

        foreach (long witnessId in rawWitnesses.WitnessActorIds)
        {
            DaggerfallWitnessObservation? observed = capturedObservations is not null
                && capturedObservations.TryGetValue(witnessId, out DaggerfallWitnessObservation? snapshot)
                ? snapshot
                : null;

            bool isGuard = observed?.IsGuard ?? IsGuard(witnessId, actors, npcs, isCityWatch);
            if (isGuard)
            {
                reporting.Add(witnessId);
                decisions.Add(new(witnessId, true, DaggerfallWitnessReportReason.CityWatchDuty, 100));
                continue;
            }

            if (victim?.ActorId == witnessId)
            {
                reporting.Add(witnessId);
                decisions.Add(new(witnessId, true, DaggerfallWitnessReportReason.VictimSelf, 100));
                continue;
            }

            DaggerfallNpc? witnessNpc = observed is not null ? observed.Npc : (npcs.TryGet(witnessId, out DaggerfallNpc? wn) ? wn : null);
            string? witnessFamilyId = observed is not null ? observed.FamilyId : ResolveActorFamilyId(witnessId, actors, actorLedger, npcs);

            if (AreFamily(witnessFamilyId, victimFamilyId))
            {
                reporting.Add(witnessId);
                decisions.Add(new(witnessId, true, DaggerfallWitnessReportReason.FamilyMember, 100));
                continue;
            }

            if (victimGuildRoots.Count > 0)
            {
                IReadOnlySet<int> witnessGuildRoots = observed is not null
                    ? observed.GuildRoots
                    : CollectActorGuildRoots(witnessId, witnessNpc, definitions, actors);
                if (victimGuildRoots.Any(witnessGuildRoots.Contains))
                {
                    reporting.Add(witnessId);
                    decisions.Add(new(witnessId, true, DaggerfallWitnessReportReason.FellowGuildMember, 100));
                    continue;
                }
            }

            int dispositionDelta = observed is not null
                ? observed.DispositionDelta
                : actorLedger is not null && actorLedger.TryGet(witnessId, out ActorLedgerEntry? ledgerEntry)
                    && ledgerEntry?.Social is { } socialDiff
                    ? socialDiff.DispositionDelta
                    : 0;
            int chance = CalculateUnconnectedReportChance(crime, etiquette, streetwise, personality, dispositionDelta);
            int rolled = roll(operation, $"witness-report:{witnessId}", 1, 100);
            bool reported = rolled <= chance;
            if (reported)
                reporting.Add(witnessId);
            decisions.Add(new(
                witnessId,
                reported,
                reported ? DaggerfallWitnessReportReason.DiscretionRollReported : DaggerfallWitnessReportReason.DiscretionRollIgnored,
                chance,
                rolled));
        }

        DaggerfallCrimeWitnessEvidence evidence = reporting.Count > 0
            ? new(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [.. reporting])
            : new(DaggerfallCrimeWitnessQuery.CompletedWithoutWitnesses, []);
        return new(evidence, decisions);
    }

    /// <summary>
    /// Exact resolved <c>FamilyId</c> kin match: two actors (or an actor and a household building) are family
    /// if and only if both carry the exact same non-empty <c>FamilyId</c>. Retires fragile surname and
    /// building-address heuristics so unrelated palace courtiers, guildhall members, and coincidental
    /// surnames never produce false family reports.
    /// </summary>
    internal static bool AreFamily(string? witnessFamilyId, string? victimFamilyId) =>
        !string.IsNullOrWhiteSpace(witnessFamilyId)
        && !string.IsNullOrWhiteSpace(victimFamilyId)
        && string.Equals(witnessFamilyId, victimFamilyId, StringComparison.OrdinalIgnoreCase);

    internal static HashSet<int> CollectGuildRoots(int factionId, DaggerfallFactionsSet factions)
    {
        HashSet<int> roots = [];
        if (factionId <= 0)
            return roots;

        int current = factionId;
        for (int depth = 0; depth < 8 && current > 0; depth++)
        {
            if (current is GenericMerchantsFactionId or GenericTempleFactionId or GenericKnightlyOrderFactionId)
                break;
            if (!factions.Factions.TryGetValue(current, out DaggerfallFactionDefinition? def))
                break;
            if (def.Type is GodFactionType or ProvinceFactionType or RegionalPeopleFactionType)
                break;

            if (def.Type is GuildFactionType or SubgroupFactionType or SubrankFactionType
                or VampireClanFactionType or WitchesCovenFactionType or TempleFactionType or CourtFactionType
                || DaggerfallConcreteGuildCatalog.All.Any(g => g.FactionId == def.Id))
            {
                roots.Add(def.Id);
            }

            if (def.Parent <= 0 || def.Parent == current)
                break;
            current = def.Parent;
        }
        return roots;
    }

    private static HashSet<int> CollectActorGuildRoots(
        long actorId,
        DaggerfallNpc? npc,
        DaggerfallDefinitions definitions,
        ActorsState actors)
    {
        HashSet<int> roots = [];
        if (actors.TryGet(actorId, out ActorState? actor)
            && actor.Actor.TryGet<FactionComponent>(out FactionComponent? fc)
            && fc.PrimaryFactionId is int primary)
        {
            roots.UnionWith(CollectGuildRoots(primary, definitions.Factions));
        }
        if (npc is not null && npc.Appearance.FactionId > 0)
            roots.UnionWith(CollectGuildRoots(npc.Appearance.FactionId, definitions.Factions));
        return roots;
    }

    private static HashSet<int> CollectVictimGuildRoots(
        DaggerfallCrimeVictimContext? victim,
        DaggerfallDefinitions definitions,
        ActorsState actors,
        DaggerfallNpcRegistry npcs)
    {
        HashSet<int> roots = [];
        if (victim is null)
            return roots;
        if (victim.FactionId is int vf && vf > 0)
            roots.UnionWith(CollectGuildRoots(vf, definitions.Factions));
        if (victim.ActorId is long vid)
        {
            DaggerfallNpc? victimNpc = npcs.TryGet(vid, out DaggerfallNpc? vn) ? vn : null;
            roots.UnionWith(CollectActorGuildRoots(vid, victimNpc, definitions, actors));
        }
        return roots;
    }

    internal static string? ResolveVictimFamilyId(
        DaggerfallCrimeVictimContext? victim,
        ActorsState actors,
        DaggerfallActorLedger? actorLedger,
        DaggerfallNpcRegistry? npcs = null)
    {
        if (!string.IsNullOrWhiteSpace(victim?.FamilyId))
            return victim.FamilyId;
        return victim?.ActorId is long vid ? ResolveActorFamilyId(vid, actors, actorLedger, npcs) : null;
    }

    internal static string? ResolveActorFamilyId(
        long actorId,
        ActorsState actors,
        DaggerfallActorLedger? actorLedger,
        DaggerfallNpcRegistry? npcs = null)
    {
        if (actors.TryGet(actorId, out ActorState? live))
        {
            if (live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay))
                return !string.IsNullOrWhiteSpace(overlay.FamilyId) ? overlay.FamilyId : null;
            if (live.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person)
                && !string.IsNullOrWhiteSpace(person.Snapshot.FamilyId))
                return person.Snapshot.FamilyId;
        }
        if (actorLedger is not null
            && actorLedger.TryGet(actorId, out ActorLedgerEntry? entry)
            && entry?.Social is { } social)
        {
            if (social.FamilyIdCleared)
                return null;
            if (!string.IsNullOrWhiteSpace(social.FamilyId))
                return social.FamilyId;
        }
        if (npcs is not null
            && npcs.TryGet(actorId, out DaggerfallNpc? npc)
            && !string.IsNullOrWhiteSpace(npc.FamilyId))
            return npc.FamilyId;
        return null;
    }

    private static bool IsGuard(long actorId, ActorsState actors, DaggerfallNpcRegistry npcs, Func<long, bool> isCityWatch)
    {
        if (isCityWatch(actorId))
            return true;
        if (actors.TryGet(actorId, out ActorState? actor)
            && actor.Actor.TryGet<FactionComponent>(out FactionComponent? faction)
            && string.Equals(faction.CreatureFactionId, "city-watch", StringComparison.Ordinal))
            return true;
        return npcs.TryGet(actorId, out DaggerfallNpc? npc)
            && string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadPlayerStat(ActorsState actors, string statKey, int fallback)
    {
        if (actors.Player.Stats.TryGetStat(StatId.Parse(statKey), out Stat? stat) && stat is not null)
            return Math.Clamp(stat.ValueInt, 0, 100);
        return fallback;
    }
}
