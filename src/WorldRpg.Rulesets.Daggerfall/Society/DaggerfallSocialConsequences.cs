using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Canonical persistent snapshot of one affected household across all three tiers (local, Great House, royal dynasty).</summary>
internal readonly record struct DaggerfallHouseholdSnapshot(
    string FamilyId,
    DaggerfallHouseholdScope Scope,
    int StandingDelta,
    bool FeudActive,
    int? MourningUntilDay,
    int LastUpdatedDay,
    int MurderCount = 0,
    bool TruceMediated = false,
    string? MediatedByName = null,
    bool ReconciliationServiceCompleted = false,
    bool PardonGranted = false,
    string? PardonGrantedBy = null);

/// <summary>Per-subject offset preventing the actor whose event caused a household ripple from double-counting it.</summary>
internal sealed record DaggerfallLocalHouseholdSubjectOffsetSave(long ActorId, int Offset)
{
    internal void Validate()
    {
        if (ActorId <= 0 || ActorId == DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentOutOfRangeException(nameof(ActorId), "Saved household subject offset requires a positive non-player actor id.");
        if (Offset < DaggerfallSocialState.MinimumReputation * 2 || Offset > DaggerfallSocialState.MaximumReputation * 2)
            throw new ArgumentOutOfRangeException(nameof(Offset), "Saved household subject offset is out of range.");
    }
}

/// <summary>
/// Sparse persistent household standing record for one affected household across any tier
/// (<c>fam:local:...</c>, <c>fam:house:...</c>, or <c>fam:dynasty:...</c>).
/// Untouched and fully-decayed households write zero entries.
/// </summary>
internal sealed record DaggerfallLocalHouseholdStandingSave(
    string FamilyId,
    int Region,
    string Location,
    int StandingDelta,
    DaggerfallLocalHouseholdSubjectOffsetSave[]? SubjectOffsets = null,
    int LastUpdatedDay = 0,
    bool FeudActive = false,
    int? MourningUntilDay = null,
    int MurderCount = 0,
    bool TruceMediated = false,
    string? MediatedByName = null,
    bool ReconciliationServiceCompleted = false,
    bool PardonGranted = false,
    string? PardonGrantedBy = null)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FamilyId);
        if (!DaggerfallHouseholds.IsValidFamilyId(FamilyId))
            throw new ArgumentException($"Saved household standing requires a valid FamilyId, got '{FamilyId}'.", nameof(FamilyId));
        if (DaggerfallHouseholds.IsLocal(FamilyId))
        {
            if (Region is < 0 or > 61)
                throw new ArgumentOutOfRangeException(nameof(Region), "Saved local household standing region is out of range.");
            ArgumentException.ThrowIfNullOrWhiteSpace(Location);
        }
        else
        {
            if (Region is < -1 or > 61)
                throw new ArgumentOutOfRangeException(nameof(Region), "Saved province-wide household standing region is out of range.");
            if (Location is null)
                throw new ArgumentNullException(nameof(Location));
        }
        if (StandingDelta < DaggerfallSocialState.MinimumReputation || StandingDelta > DaggerfallSocialState.MaximumReputation)
            throw new ArgumentOutOfRangeException(nameof(StandingDelta), "Saved household standing delta is out of range.");
        if (LastUpdatedDay < 0)
            throw new ArgumentOutOfRangeException(nameof(LastUpdatedDay), "Saved household standing last-updated day must be non-negative.");
        if (MourningUntilDay is < 0)
            throw new ArgumentOutOfRangeException(nameof(MourningUntilDay), "Saved household mourning day must be non-negative.");
        if (MurderCount < 0)
            throw new ArgumentOutOfRangeException(nameof(MurderCount), "Saved household murder count must be non-negative.");
        if (SubjectOffsets is not null)
        {
            HashSet<long> seen = [];
            foreach (DaggerfallLocalHouseholdSubjectOffsetSave offset in SubjectOffsets)
            {
                ArgumentNullException.ThrowIfNull(offset);
                offset.Validate();
                if (!seen.Add(offset.ActorId))
                    throw new ArgumentException($"Saved household standing '{FamilyId}' repeats subject actor id {offset.ActorId}.");
            }
        }
    }
}

/// <summary>Sparse persisted snapshot of affected households across all tiers; empty when untouched.</summary>
internal sealed record DaggerfallLocalHouseholdsSave(DaggerfallLocalHouseholdStandingSave[] Standings)
{
    internal static DaggerfallLocalHouseholdsSave Empty { get; } = new([]);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Standings);
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (DaggerfallLocalHouseholdStandingSave standing in Standings)
        {
            ArgumentNullException.ThrowIfNull(standing);
            standing.Validate();
            if (!keys.Add(standing.FamilyId))
                throw new ArgumentException($"Saved household standings repeat family '{standing.FamilyId}'.");
        }
    }
}

/// <summary>
/// Social consequences when the player harms, arrests, or helps faction/household members:
/// - Deducts faction reputation (routed through <see cref="FactionComponent"/>) and records witness crime memory
///   and personal disposition grudge on every living witness who observed a killing.
/// - Routes attributed household grudge, arrest, &amp; goodwill ripples strictly through witnessed/reported/known deeds
///   onto canonical sparse persistent state across all three tiers (<c>fam:local:...</c>, <c>fam:house:...</c>,
///   and <c>fam:dynasty:...</c>; zero save bytes for untouched households):
///   - Great Houses and royal dynasties also emit <see cref="DaggerfallWordSpread"/> wavefronts so distant sites
///     still learn at courier speed, while settled standing and blood-feud state persist permanently after
///     30-day news wavefronts expire.
///   - Minor grudges (-1..-14) decay slowly toward 0 over time by tier via <see cref="DaggerfallSocialTuning"/>;
///     blood feuds (&lt;= -15) never decay from time alone.
/// </summary>
internal sealed class DaggerfallSocialConsequences(
    DaggerfallSocialState social,
    DaggerfallDefinitions definitions,
    ActorsState actors,
    DaggerfallNpcRegistry npcs,
    DaggerfallActorLedger actorLedger,
    Func<DaggerfallCrimeWitnessEvidence> queryWitnesses,
    Func<long, bool> isCityWatch,
    Func<int?> currentRegion,
    DaggerfallWordSpread? wordSpread = null,
    Func<DaggerfallSiteRecord?>? activeSite = null,
    Func<long>? currentMinute = null,
    Func<int>? currentDay = null,
    DaggerfallSocialTuning? socialTuning = null)
{
    private sealed class HouseholdStandingState(int region, string location)
    {
        internal int Region { get; } = region;
        internal string Location { get; } = location;
        internal int StandingDelta { get; set; }
        internal int LastUpdatedDay { get; set; }
        internal bool FeudActive { get; set; }
        internal int? MourningUntilDay { get; set; }
        internal int MurderCount { get; set; }
        internal bool TruceMediated { get; set; }
        internal string? MediatedByName { get; set; }
        internal bool ReconciliationServiceCompleted { get; set; }
        internal bool PardonGranted { get; set; }
        internal string? PardonGrantedBy { get; set; }
        internal Dictionary<long, int> SubjectOffsets { get; } = [];

        internal bool IsEmpty =>
            StandingDelta == 0
            && !FeudActive
            && MourningUntilDay is null
            && MurderCount == 0
            && !TruceMediated
            && !ReconciliationServiceCompleted
            && !PardonGranted
            && SubjectOffsets.Count == 0;

        internal void ClearSettlementFlags()
        {
            FeudActive = false;
            MurderCount = 0;
            TruceMediated = false;
            MediatedByName = null;
            ReconciliationServiceCompleted = false;
            PardonGranted = false;
            PardonGrantedBy = null;
        }
    }

    internal const int PrimaryFactionKillPenalty = -10;
    internal const int RegionalPeopleKillPenalty = -5;
    internal const int WitnessMurderDispositionPenalty = -15;
    internal const int KinMurderDispositionPenalty = -20;
    internal const int KinAssaultDispositionPenalty = -10;
    internal const int KinPropertyCrimeDispositionPenalty = -8;
    internal const int KinGoodwillMinimumBonus = 2;
    internal const int KinGoodwillMaximumBonus = 10;

    private const long MinutesPerDay = 24L * 60L;
    private readonly DaggerfallSocialTuning _tuning = (socialTuning ?? DaggerfallSocialTuning.Default).Validate();
    private readonly HashSet<string> _rippledEventKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HouseholdStandingState> _householdStandings = new(StringComparer.Ordinal);

    internal DaggerfallSocialTuning Tuning => _tuning;

    internal DaggerfallLocalHouseholdsSave Capture()
    {
        if (_householdStandings.Count == 0)
            return DaggerfallLocalHouseholdsSave.Empty;

        List<DaggerfallLocalHouseholdStandingSave> entries = [];
        foreach ((string familyId, HouseholdStandingState state) in _householdStandings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (state.IsEmpty)
                continue;
            DaggerfallLocalHouseholdSubjectOffsetSave[]? offsets = state.SubjectOffsets.Count == 0
                ? null
                : [.. state.SubjectOffsets
                    .Where(pair => pair.Value != 0)
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new DaggerfallLocalHouseholdSubjectOffsetSave(pair.Key, pair.Value))];
            entries.Add(new DaggerfallLocalHouseholdStandingSave(
                familyId,
                state.Region,
                state.Location,
                state.StandingDelta,
                offsets is { Length: > 0 } ? offsets : null,
                state.LastUpdatedDay,
                state.FeudActive,
                state.MourningUntilDay,
                state.MurderCount,
                state.TruceMediated,
                state.MediatedByName,
                state.ReconciliationServiceCompleted,
                state.PardonGranted,
                state.PardonGrantedBy));
        }
        return entries.Count == 0 ? DaggerfallLocalHouseholdsSave.Empty : new([.. entries]);
    }

    internal void Restore(DaggerfallLocalHouseholdsSave? saved)
    {
        _householdStandings.Clear();
        if (saved is null)
            return;
        saved.Validate();
        foreach (DaggerfallLocalHouseholdStandingSave entry in saved.Standings)
        {
            HouseholdStandingState state = new(entry.Region, entry.Location)
            {
                StandingDelta = entry.StandingDelta,
                LastUpdatedDay = entry.LastUpdatedDay,
                FeudActive = entry.FeudActive || entry.StandingDelta <= DaggerfallIntroductions.BloodFeudDispositionThreshold,
                MourningUntilDay = entry.MourningUntilDay,
                MurderCount = entry.MurderCount,
                TruceMediated = entry.TruceMediated,
                MediatedByName = entry.MediatedByName,
                ReconciliationServiceCompleted = entry.ReconciliationServiceCompleted,
                PardonGranted = entry.PardonGranted,
                PardonGrantedBy = entry.PardonGrantedBy,
            };
            if (entry.SubjectOffsets is { Length: > 0 } offsets)
            {
                foreach (DaggerfallLocalHouseholdSubjectOffsetSave offset in offsets)
                {
                    if (offset.Offset != 0)
                        state.SubjectOffsets[offset.ActorId] = offset.Offset;
                }
            }
            if (!state.IsEmpty)
                _householdStandings[entry.FamilyId] = state;
        }
    }

    /// <summary>Reads the canonical persistent snapshot for <paramref name="familyId"/> across all tiers.</summary>
    internal bool TryGetHouseholdSnapshot(string? familyId, out DaggerfallHouseholdSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(familyId)
            && _householdStandings.TryGetValue(familyId, out HouseholdStandingState? state)
            && !state.IsEmpty)
        {
            snapshot = new DaggerfallHouseholdSnapshot(
                familyId,
                DaggerfallHouseholds.ScopeOf(familyId),
                state.StandingDelta,
                state.FeudActive,
                state.MourningUntilDay,
                state.LastUpdatedDay,
                state.MurderCount,
                state.TruceMediated,
                state.MediatedByName,
                state.ReconciliationServiceCompleted,
                state.PardonGranted,
                state.PardonGrantedBy);
            return true;
        }
        snapshot = default;
        return false;
    }

    /// <summary>Returns all non-empty persistent household snapshots ordered by family id.</summary>
    internal IReadOnlyList<DaggerfallHouseholdSnapshot> ReadAllSnapshots() =>
    [
        .. _householdStandings
            .Where(pair => !pair.Value.IsEmpty)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DaggerfallHouseholdSnapshot(
                pair.Key,
                DaggerfallHouseholds.ScopeOf(pair.Key),
                pair.Value.StandingDelta,
                pair.Value.FeudActive,
                pair.Value.MourningUntilDay,
                pair.Value.LastUpdatedDay,
                pair.Value.MurderCount,
                pair.Value.TruceMediated,
                pair.Value.MediatedByName,
                pair.Value.ReconciliationServiceCompleted,
                pair.Value.PardonGranted,
                pair.Value.PardonGrantedBy)),
    ];

    /// <summary>Records that a trusted intermediary mediated a truce, unlocking direct settlement talks for a feuding household.</summary>
    internal bool RecordTruceMediated(string familyId, string mediatorName, int day)
    {
        if (string.IsNullOrWhiteSpace(familyId)
            || !_householdStandings.TryGetValue(familyId, out HouseholdStandingState? state)
            || (!state.FeudActive && state.StandingDelta >= 0))
        {
            return false;
        }
        state.TruceMediated = true;
        state.MediatedByName = string.IsNullOrWhiteSpace(mediatorName) ? "a trusted intermediary" : mediatorName.Trim();
        state.LastUpdatedDay = Math.Max(state.LastUpdatedDay, Math.Max(0, day));
        return true;
    }

    /// <summary>Records that the player completed reconciliation service/penance for <paramref name="familyId"/>, reducing wergild cost.</summary>
    internal bool RecordReconciliationServiceCompleted(string familyId, int day)
    {
        if (string.IsNullOrWhiteSpace(familyId)
            || !_householdStandings.TryGetValue(familyId, out HouseholdStandingState? state)
            || (!state.FeudActive && state.StandingDelta >= 0))
        {
            return false;
        }
        if (state.ReconciliationServiceCompleted)
            return false;
        state.ReconciliationServiceCompleted = true;
        state.LastUpdatedDay = Math.Max(state.LastUpdatedDay, Math.Max(0, day));
        return true;
    }

    /// <summary>Records a formal court or temple pardon for <paramref name="familyId"/>, reducing wergild cost.</summary>
    internal bool RecordPardonGranted(string familyId, string issuerName, int day)
    {
        if (string.IsNullOrWhiteSpace(familyId)
            || !_householdStandings.TryGetValue(familyId, out HouseholdStandingState? state)
            || (!state.FeudActive && state.StandingDelta >= 0))
        {
            return false;
        }
        if (state.PardonGranted)
            return false;
        state.PardonGranted = true;
        state.PardonGrantedBy = string.IsNullOrWhiteSpace(issuerName) ? "the court" : issuerName.Trim();
        state.LastUpdatedDay = Math.Max(state.LastUpdatedDay, Math.Max(0, day));
        return true;
    }

    /// <summary>
    /// Clears an active grudge or blood feud upon wergild settlement: resets standing delta to 0, clears feud/murder/mediation/pardon
    /// flags, clears pending hostile wavefronts for province-wide tiers (publishing a settlement rumor), and self-prunes when empty.
    /// </summary>
    internal bool ClearHouseholdGrudgeAndFeud(
        string familyId,
        DaggerfallSiteRecord? originSite,
        long minute,
        int day,
        string? settlementNote = null)
    {
        if (string.IsNullOrWhiteSpace(familyId)
            || !_householdStandings.TryGetValue(familyId, out HouseholdStandingState? state)
            || (!state.FeudActive && state.StandingDelta >= 0))
        {
            return false;
        }

        state.StandingDelta = 0;
        state.ClearSettlementFlags();
        state.SubjectOffsets.Clear();
        state.LastUpdatedDay = Math.Max(0, day);

        if (DaggerfallHouseholds.IsProvinceWide(familyId) && wordSpread is not null)
        {
            wordSpread.ClearHouseholdWavefronts(familyId);
            if (originSite is not null)
            {
                string label = DaggerfallHouseholds.FormatLabel(familyId, definitions, null, null, 0UL) ?? familyId;
                wordSpread.RecordNews(
                    eventId: $"wf:house:settled:{familyId}:{minute}",
                    headline: $"Feud settled with {label}",
                    body: string.IsNullOrWhiteSpace(settlementNote)
                        ? $"Word spreads that wergild has been paid and peace restored with {label}."
                        : settlementNote,
                    originSite: originSite,
                    currentMinute: minute);
            }
        }

        if (state.IsEmpty)
            _householdStandings.Remove(familyId);
        return true;
    }

    /// <summary>
    /// Advances daily household state: expires completed mourning windows and decays minor grudges
    /// (-1..-14, strictly above <see cref="DaggerfallIntroductions.BloodFeudDispositionThreshold"/>) toward 0
    /// at tier-specific rates (<see cref="DaggerfallSocialTuning"/>). Blood feuds (&lt;= -15) never decay from time alone.
    /// Fully-healed households self-prune so untouched/resolved families write zero save bytes.
    /// </summary>
    internal int AdvanceDay(int day)
    {
        if (_householdStandings.Count == 0 || day < 0)
            return 0;

        int changed = 0;
        List<string>? prune = null;
        foreach ((string familyId, HouseholdStandingState state) in _householdStandings)
        {
            bool entryChanged = false;
            if (state.MourningUntilDay is int until && day > until)
            {
                state.MourningUntilDay = null;
                entryChanged = true;
            }

            if (!state.FeudActive
                && state.StandingDelta < 0
                && state.StandingDelta > DaggerfallIntroductions.BloodFeudDispositionThreshold)
            {
                int decayDays = DecayDaysPerPoint(DaggerfallHouseholds.ScopeOf(familyId));
                int elapsed = day - state.LastUpdatedDay;
                if (elapsed >= decayDays)
                {
                    int steps = elapsed / decayDays;
                    int next = Math.Min(0, state.StandingDelta + steps);
                    if (next != state.StandingDelta)
                    {
                        int appliedSteps = next - state.StandingDelta;
                        state.StandingDelta = next;
                        state.LastUpdatedDay += steps * decayDays;
                        if (DaggerfallHouseholds.IsProvinceWide(familyId) && wordSpread is not null)
                        {
                            if (state.StandingDelta == 0)
                                wordSpread.ClearNegativeHouseholdWavefronts(familyId);
                            else
                                wordSpread.DecayHouseholdGrudgeWavefronts(familyId, appliedSteps);
                        }
                        if (state.StandingDelta == 0)
                        {
                            state.ClearSettlementFlags();
                            state.SubjectOffsets.Clear();
                        }
                        entryChanged = true;
                    }
                }
            }

            if (entryChanged)
                changed++;
            if (state.IsEmpty)
                (prune ??= []).Add(familyId);
        }

        if (prune is not null)
        {
            foreach (string key in prune)
                _householdStandings.Remove(key);
        }
        return changed;
    }

    /// <summary>
    /// Classifies whether an arrested NPC counts as a household/noble head (heavier arrest grudge) versus an
    /// ordinary household member (lighter arrest grudge), based on vacated offices, regional rulership, noble titles,
    /// and proprietor/head roles.
    /// </summary>
    internal bool IsHouseholdHead(
        long actorId,
        int region,
        int vacatedOffices = 0,
        DaggerfallOfficeHolders? officeHolders = null)
    {
        if (vacatedOffices > 0)
            return true;
        if (region is >= 0 and <= 61
            && officeHolders?.ResolveRegionalRuler(region) is { ActorId: long rulerId }
            && rulerId == actorId)
        {
            return true;
        }
        if (npcs.TryGet(actorId, out DaggerfallNpc? npc) && npc is not null)
        {
            if (npc.Role.Contains("ruler", StringComparison.OrdinalIgnoreCase)
                || npc.Role.Contains("noble", StringComparison.OrdinalIgnoreCase)
                || npc.Role.Contains("regent", StringComparison.OrdinalIgnoreCase)
                || npc.Role.Contains("factor", StringComparison.OrdinalIgnoreCase)
                || npc.Role.Contains("head", StringComparison.OrdinalIgnoreCase)
                || npc.Role.Contains("master", StringComparison.OrdinalIgnoreCase)
                || npc.Role.Contains("captain", StringComparison.OrdinalIgnoreCase)
                || npc.Role.Contains("merchant", StringComparison.OrdinalIgnoreCase)
                || npc.Role.Contains("shopkeeper", StringComparison.OrdinalIgnoreCase)
                || npc.Role.Contains("innkeeper", StringComparison.OrdinalIgnoreCase)
                || npc.Role.Contains("banker", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            string? name = npc.DisplayName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                string[] headTitles =
                [
                    "King ", "Queen ", "Emperor ", "Empress ",
                    "Lord ", "Lady ", "Duke ", "Duchess ", "Marquis ", "Marquise ",
                    "Count ", "Countess ", "Baron ", "Baroness ", "Prince ", "Princess ",
                ];
                foreach (string title in headTitles)
                {
                    if (name.StartsWith(title, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Inflicts a lighter, decaying arrest/imprisonment grudge on the household of <paramref name="arrestedActorId"/>
    /// (<see cref="DaggerfallSocialTuning.KinHeadArrestGrudgePenalty"/> for a family/noble head or office holder,
    /// <see cref="DaggerfallSocialTuning.KinArrestGrudgePenalty"/> for an ordinary member). Arrest alone never
    /// opens a blood feud (capped at -14 when no blood feud is already active).
    /// </summary>
    internal int ReactToHouseholdArrest(
        long arrestedActorId,
        bool isHouseholdHead,
        DaggerfallSiteRecord? originSite,
        long minute,
        int? dayOverride = null,
        string? familyIdOverride = null,
        string? reason = null)
    {
        if (arrestedActorId <= 0 || arrestedActorId == DaggerfallActorIdentity.PlayerEntityId)
            return 0;

        DaggerfallNpc? npc = npcs.TryGet(arrestedActorId, out DaggerfallNpc? reg) ? reg : null;
        ActorState? live = actors.TryGet(arrestedActorId, out ActorState? act) ? act : null;
        string? familyId = !string.IsNullOrWhiteSpace(familyIdOverride)
            ? familyIdOverride
            : ResolveActorFamilyId(arrestedActorId, live, npc);
        if (string.IsNullOrWhiteSpace(familyId) || !DaggerfallHouseholds.IsValidFamilyId(familyId))
            return 0;

        int penalty = isHouseholdHead ? _tuning.KinHeadArrestGrudgePenalty : _tuning.KinArrestGrudgePenalty;
        if (penalty >= 0)
            return 0;

        string name = npc?.DisplayName ?? $"actor #{arrestedActorId}";
        string headline = isHouseholdHead
            ? $"Household head {name} seized by law"
            : $"Kin member {name} arrested";
        string body = !string.IsNullOrWhiteSpace(reason)
            ? $"Word spreads that {name} was arrested ({reason}); their kin resent the hand behind it."
            : $"Word spreads that {name} was arrested; their kin resent the hand behind it.";

        return ApplyAttributedHouseholdRipple(
            familyId,
            penalty,
            subjectActorId: arrestedActorId,
            subjectNpc: npc,
            originSite: originSite ?? activeSite?.Invoke(),
            minute: minute,
            rippleKey: $"arrest:{arrestedActorId}:{minute}",
            headline: headline,
            body: body,
            isMurder: false,
            isArrest: true,
            dayOverride: dayOverride);
    }

    /// <summary>
    /// Applies faction standing loss, direct witness memory, and attributed kin grudge ripple when the player
    /// kills a faction-affiliated or household-affiliated actor. An unwitnessed killing never triggers a kin grudge.
    /// </summary>
    internal void ReactToDeath(ActorDiedFact died, ActorState? fallen, long minute)
    {
        ArgumentNullException.ThrowIfNull(died);
        if (died.KillerId != DaggerfallActorIdentity.PlayerEntityId
            || died.ActorId == DaggerfallActorIdentity.PlayerEntityId
            || fallen is null)
            return;

        FactionComponent? factions = fallen.Actor.TryGet<FactionComponent>(out FactionComponent? fc) ? fc : null;
        DaggerfallNpc? npc = npcs.TryGet(died.ActorId, out DaggerfallNpc? registered) ? registered : null;
        bool watch = isCityWatch(died.ActorId);

        int? primaryFaction = ResolveCatalogFaction(factions?.PrimaryFactionId)
            ?? ResolveCatalogFaction(npc?.Appearance.FactionId);
        int? regionalFaction = ResolveCatalogFaction(factions?.RegionalPeopleFactionId);
        if (primaryFaction is null && regionalFaction is null && watch && currentRegion() is int region)
            regionalFaction = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, region)?.Id;
        if (regionalFaction == primaryFaction)
            regionalFaction = null;

        if (primaryFaction is int pf)
            social.ChangeFactionReputation(pf, PrimaryFactionKillPenalty, DaggerfallFactionReputationChange.Propagate);
        if (regionalFaction is int rf)
            social.ChangeFactionReputation(rf, RegionalPeopleKillPenalty, DaggerfallFactionReputationChange.Propagate);

        if (primaryFaction is null && regionalFaction is null && npc is null && !watch)
            return;

        DaggerfallCrimeWitnessEvidence witnesses = queryWitnesses();
        if (witnesses.WitnessActorIds.Length == 0)
            return;

        string crimeRecord = $"murder:{died.ActorId}:{minute}";
        foreach (long witnessId in witnesses.WitnessActorIds)
        {
            if (witnessId == died.ActorId || witnessId == DaggerfallActorIdentity.PlayerEntityId)
                continue;
            if (!actors.TryGet(witnessId, out ActorState? witnessActor) || witnessActor.IsDefeated)
                continue;

            if (witnessActor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay))
                overlay.RecordWitnessedCrime(crimeRecord);

            RecordWitnessGrudge(witnessId, witnessActor, crimeRecord);
        }

        string? victimFamilyId = ResolveActorFamilyId(died.ActorId, fallen, npc);
        if (victimFamilyId is not null)
        {
            string victimName = npc?.DisplayName ?? $"actor #{died.ActorId}";
            ApplyAttributedHouseholdRipple(
                victimFamilyId,
                KinMurderDispositionPenalty,
                subjectActorId: died.ActorId,
                subjectNpc: npc,
                originSite: activeSite?.Invoke(),
                minute: minute,
                rippleKey: $"murder:{died.ActorId}",
                headline: $"Blood feud against {victimName}'s kin",
                body: $"Word spreads that {victimName} was slain before witnesses; their house will not forgive the killer.",
                isMurder: true);
        }
    }

    /// <summary>
    /// Applies an attributed household grudge ripple when a witnessed or reported crime against a household member
    /// or their home is admitted by the law. Unreported/unwitnessed incidents are ignored.
    /// </summary>
    internal void ReactToAttributedCrime(
        DaggerfallCrimeIncidentSave incident,
        DaggerfallSiteRecord? originSite,
        long minute,
        string? victimFamilyId = null)
    {
        ArgumentNullException.ThrowIfNull(incident);
        if (!incident.Reported && incident.Witnesses.Query != DaggerfallCrimeWitnessQuery.CompletedWithWitnesses)
            return;

        int penalty = incident.Crime switch
        {
            DaggerfallCrimeKind.Murder => KinMurderDispositionPenalty,
            DaggerfallCrimeKind.Assault => KinAssaultDispositionPenalty,
            DaggerfallCrimeKind.Theft
                or DaggerfallCrimeKind.Pickpocketing
                or DaggerfallCrimeKind.BreakingAndEntering
                or DaggerfallCrimeKind.AttemptedBreakingAndEntering
                or DaggerfallCrimeKind.Trespassing => KinPropertyCrimeDispositionPenalty,
            _ => 0,
        };
        if (penalty == 0)
            return;

        long? subjectId = incident.AffectedActorOrOwnerId;
        DaggerfallNpc? subjectNpc = subjectId is long sid && npcs.TryGet(sid, out DaggerfallNpc? found) ? found : null;
        string? familyId = !string.IsNullOrWhiteSpace(victimFamilyId)
            ? victimFamilyId
            : subjectId is long actorId
                ? ResolveActorFamilyId(actorId, actors.TryGet(actorId, out ActorState? st) ? st : null, subjectNpc)
                : null;
        if (string.IsNullOrWhiteSpace(familyId))
            return;

        string rippleKey = incident.Crime == DaggerfallCrimeKind.Murder && subjectId is long murderedId
            ? $"murder:{murderedId}"
            : $"crime:{incident.OperationId}";

        string? headline = incident.Crime is DaggerfallCrimeKind.Murder or DaggerfallCrimeKind.Assault
            ? $"Offense against household {familyId}"
            : null;
        string? body = incident.Crime == DaggerfallCrimeKind.Murder && subjectNpc?.DisplayName is { Length: > 0 } name
            ? $"Word spreads that {name} was murdered; their house marks the killer as an enemy."
            : null;

        ApplyAttributedHouseholdRipple(
            familyId,
            penalty,
            subjectActorId: subjectId,
            subjectNpc: subjectNpc,
            originSite: originSite ?? activeSite?.Invoke(),
            minute: minute,
            rippleKey: rippleKey,
            headline: headline,
            body: body,
            isMurder: incident.Crime == DaggerfallCrimeKind.Murder);
    }

    /// <summary>
    /// Ripples goodwill to a beneficiary's kin when the player earns positive personal standing with them
    /// (e.g., completing a favour or quest). Uses the same two-tier reach as grudges: settlement-local for
    /// <c>fam:local:...</c> and province-wide <see cref="DaggerfallWordSpread"/> for Great Houses and royal dynasties.
    /// </summary>
    internal void ReactToKinGoodwill(
        long beneficiaryActorId,
        int directPositiveDelta,
        DaggerfallSiteRecord? originSite = null,
        long? minute = null,
        string? rippleKey = null)
    {
        if (beneficiaryActorId <= 0 || directPositiveDelta <= 0)
            return;

        DaggerfallNpc? beneficiaryNpc = npcs.TryGet(beneficiaryActorId, out DaggerfallNpc? reg) ? reg : null;
        ActorState? beneficiaryActor = actors.TryGet(beneficiaryActorId, out ActorState? live) ? live : null;
        string? familyId = ResolveActorFamilyId(beneficiaryActorId, beneficiaryActor, beneficiaryNpc);
        if (string.IsNullOrWhiteSpace(familyId))
            return;

        int kinBonus = Math.Clamp(directPositiveDelta / 2, KinGoodwillMinimumBonus, KinGoodwillMaximumBonus);
        ApplyAttributedHouseholdRipple(
            familyId,
            kinBonus,
            subjectActorId: beneficiaryActorId,
            subjectNpc: beneficiaryNpc,
            originSite: originSite ?? activeSite?.Invoke(),
            minute: minute ?? currentMinute?.Invoke() ?? 0L,
            rippleKey: rippleKey,
            headline: null,
            body: null);
    }

    /// <summary>
    /// Computes an actor's effective personal disposition delta: sparse <see cref="DaggerfallActorLedger"/> standing
    /// plus any canonical persistent household standing (adjusted by unarrived courier wavefronts for Great Houses
    /// and royal dynasties so distant sites still learn at courier speed while settled standing persists forever).
    /// </summary>
    internal int EffectiveDispositionDelta(long actorId, DaggerfallSiteRecord? atSite = null, long? minute = null)
    {
        int personal = actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) && entry?.Social is { } s
            ? s.DispositionDelta
            : 0;

        DaggerfallNpc? npc = npcs.TryGet(actorId, out DaggerfallNpc? reg) ? reg : null;
        ActorState? live = actors.TryGet(actorId, out ActorState? act) ? act : null;
        string? familyId = ResolveActorFamilyId(actorId, live, npc);
        if (string.IsNullOrWhiteSpace(familyId))
            return personal;

        if (DaggerfallHouseholds.IsLocal(familyId))
        {
            if (!_householdStandings.TryGetValue(familyId, out HouseholdStandingState? localState))
                return personal;
            if (npc is not null)
            {
                if (!SameSettlement(npc.Site, localState.Region, localState.Location))
                    return personal;
            }
            else if ((atSite ?? activeSite?.Invoke()) is { } checkSite
                && (checkSite.Id.Region != localState.Region
                    || !string.Equals(checkSite.Name, localState.Location, StringComparison.OrdinalIgnoreCase)))
            {
                return personal;
            }

            int localDelta = checked(localState.StandingDelta + localState.SubjectOffsets.GetValueOrDefault(actorId));
            if (localDelta == 0)
                return personal;
            return Math.Clamp(
                checked(personal + localDelta),
                DaggerfallSocialState.MinimumReputation,
                DaggerfallSocialState.MaximumReputation);
        }

        if (!DaggerfallHouseholds.IsProvinceWide(familyId))
            return personal;

        int houseDelta = HouseholdStandingDelta(familyId, atSite, minute);
        if (houseDelta != 0
            && _householdStandings.TryGetValue(familyId, out HouseholdStandingState? houseState)
            && houseState.SubjectOffsets.TryGetValue(actorId, out int subjectOffset))
        {
            houseDelta = checked(houseDelta + subjectOffset);
        }
        if (houseDelta == 0)
            return personal;

        return Math.Clamp(
            checked(personal + houseDelta),
            DaggerfallSocialState.MinimumReputation,
            DaggerfallSocialState.MaximumReputation);
    }

    /// <summary>
    /// Returns the active household standing delta for <paramref name="familyId"/> at <paramref name="atSite"/>:
    /// settlement-local standing for <c>fam:local:...</c> or persistent province-wide standing minus any unarrived
    /// courier wavefronts for Great Houses and royal dynasties.
    /// </summary>
    internal int HouseholdStandingDelta(string? familyId, DaggerfallSiteRecord? atSite = null, long? minute = null)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            return 0;
        DaggerfallSiteRecord? targetSite = atSite ?? activeSite?.Invoke();
        if (DaggerfallHouseholds.IsLocal(familyId))
        {
            if (!_householdStandings.TryGetValue(familyId, out HouseholdStandingState? localState))
                return 0;
            if (targetSite is not null
                && (targetSite.Id.Region != localState.Region
                    || !string.Equals(targetSite.Name, localState.Location, StringComparison.OrdinalIgnoreCase)))
            {
                return 0;
            }
            return localState.StandingDelta;
        }
        if (!DaggerfallHouseholds.IsProvinceWide(familyId))
            return 0;

        long nowMinute = minute ?? currentMinute?.Invoke() ?? 0L;
        if (!_householdStandings.TryGetValue(familyId, out HouseholdStandingState? persistent))
            return 0;

        int unarrived = wordSpread is not null && targetSite is not null
            ? wordSpread.UnarrivedHouseholdStandingDelta(familyId, targetSite, nowMinute)
            : 0;
        int effective = checked(persistent.StandingDelta - unarrived);
        // Pending negative news at an uninformed site can only mask the negative standing; it never invents positive goodwill.
        if (persistent.StandingDelta <= 0 && unarrived < 0 && effective > 0)
            effective = 0;
        return Math.Clamp(
            effective,
            DaggerfallSocialState.MinimumReputation,
            DaggerfallSocialState.MaximumReputation);
    }

    /// <summary>
    /// Returns whether <paramref name="familyId"/> is currently in an active blood feud known at <paramref name="atSite"/>.
    /// Respects courier-paced arrival at distant sites while persisting permanently once news arrives or wavefronts age out.
    /// </summary>
    internal bool IsFeudActiveAt(string? familyId, DaggerfallSiteRecord? atSite = null, long? minute = null)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            return false;
        int effective = HouseholdStandingDelta(familyId, atSite, minute);
        if (effective <= DaggerfallIntroductions.BloodFeudDispositionThreshold)
            return true;

        if (!_householdStandings.TryGetValue(familyId, out HouseholdStandingState? state) || !state.FeudActive)
            return false;

        DaggerfallSiteRecord? targetSite = atSite ?? activeSite?.Invoke();
        if (DaggerfallHouseholds.IsLocal(familyId))
        {
            return targetSite is null
                || (targetSite.Id.Region == state.Region
                    && string.Equals(targetSite.Name, state.Location, StringComparison.OrdinalIgnoreCase));
        }

        long nowMinute = minute ?? currentMinute?.Invoke() ?? 0L;
        int unarrived = wordSpread is not null && targetSite is not null
            ? wordSpread.UnarrivedHouseholdStandingDelta(familyId, targetSite, nowMinute)
            : 0;
        return unarrived >= 0;
    }

    /// <summary>
    /// Sets a household's standing delta (-100..100) through the canonical persistent owner across all three tiers
    /// (also emitting a courier wavefront for Great Houses and royal dynasties) so saves, blood-feud gates, decay,
    /// and inspection surfaces behave identically to play.
    /// </summary>
    internal int SetHouseholdStanding(
        string familyId,
        int targetStandingDelta,
        DaggerfallSiteRecord? atSite = null,
        long? minute = null,
        int? dayOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyId);
        if (!DaggerfallHouseholds.IsValidFamilyId(familyId))
            throw new ArgumentException($"Invalid household family id '{familyId}'.", nameof(familyId));

        int clamped = Math.Clamp(targetStandingDelta, DaggerfallSocialState.MinimumReputation, DaggerfallSocialState.MaximumReputation);
        DaggerfallSiteRecord? site = atSite ?? activeSite?.Invoke();
        long nowMinute = minute ?? currentMinute?.Invoke() ?? 0L;
        int day = dayOverride ?? CurrentDayValue(nowMinute);

        if (DaggerfallHouseholds.IsLocal(familyId))
        {
            if (clamped == 0)
            {
                if (_householdStandings.TryGetValue(familyId, out HouseholdStandingState? existingLocal))
                {
                    existingLocal.StandingDelta = 0;
                    existingLocal.ClearSettlementFlags();
                    existingLocal.SubjectOffsets.Clear();
                    existingLocal.LastUpdatedDay = day;
                    if (existingLocal.IsEmpty)
                        _householdStandings.Remove(familyId);
                }
                return 0;
            }

            (int targetRegion, string targetLocation) = ResolveLocalSettlement(familyId, subjectNpc: null, site);
            if (!_householdStandings.TryGetValue(familyId, out HouseholdStandingState? state))
            {
                state = new HouseholdStandingState(targetRegion, targetLocation);
                _householdStandings[familyId] = state;
            }
            state.StandingDelta = clamped;
            state.FeudActive = clamped <= DaggerfallIntroductions.BloodFeudDispositionThreshold;
            state.LastUpdatedDay = day;
            state.SubjectOffsets.Clear();
            return clamped;
        }

        if (!DaggerfallHouseholds.IsProvinceWide(familyId))
            throw new ArgumentException($"Unsupported household scope for '{familyId}'.", nameof(familyId));

        int priorStanding = _householdStandings.TryGetValue(familyId, out HouseholdStandingState? priorState)
            ? priorState.StandingDelta
            : 0;
        wordSpread?.ClearHouseholdWavefronts(familyId);

        if (clamped == 0)
        {
            if (priorState is not null)
            {
                priorState.StandingDelta = 0;
                priorState.ClearSettlementFlags();
                priorState.SubjectOffsets.Clear();
                priorState.LastUpdatedDay = day;
                if (priorState.IsEmpty)
                    _householdStandings.Remove(familyId);
            }
            return 0;
        }

        int houseRegion = ResolveProvinceWideRegion(familyId, site);
        if (!_householdStandings.TryGetValue(familyId, out HouseholdStandingState? houseState))
        {
            houseState = new HouseholdStandingState(houseRegion, string.Empty);
            _householdStandings[familyId] = houseState;
        }
        houseState.StandingDelta = clamped;
        houseState.FeudActive = clamped <= DaggerfallIntroductions.BloodFeudDispositionThreshold;
        houseState.LastUpdatedDay = day;
        houseState.SubjectOffsets.Clear();

        int delta = clamped - priorStanding;
        if (delta != 0 && wordSpread is not null && site is not null)
        {
            string label = DaggerfallHouseholds.FormatLabel(familyId, definitions, null, null, 0UL) ?? familyId;
            wordSpread.RecordHouseholdStanding(
                familyId,
                delta,
                site,
                nowMinute,
                headline: $"Standing shifted with {label}",
                body: $"Word spreads of a shift in your standing with {label}.");
        }
        return HouseholdStandingDelta(familyId, site, nowMinute);
    }

    private int ApplyAttributedHouseholdRipple(
        string familyId,
        int delta,
        long? subjectActorId,
        DaggerfallNpc? subjectNpc,
        DaggerfallSiteRecord? originSite,
        long minute,
        string? rippleKey,
        string? headline,
        string? body,
        bool isMurder = false,
        bool isArrest = false,
        int? dayOverride = null)
    {
        if (delta == 0)
            return 0;
        if (!string.IsNullOrWhiteSpace(rippleKey) && !_rippledEventKeys.Add(rippleKey))
            return 0;

        bool isLocal = DaggerfallHouseholds.IsLocal(familyId);
        bool isProvinceWide = DaggerfallHouseholds.IsProvinceWide(familyId);
        if (!isLocal && !isProvinceWide)
            return 0;

        int day = dayOverride ?? CurrentDayValue(minute);

        HouseholdStandingState state;
        if (!_householdStandings.TryGetValue(familyId, out HouseholdStandingState? existing))
        {
            if (isLocal)
            {
                if (!TryResolveLocalSettlement(familyId, subjectNpc, originSite, out int targetRegion, out string targetLocation))
                    return 0;
                state = new HouseholdStandingState(targetRegion, targetLocation);
            }
            else
            {
                state = new HouseholdStandingState(ResolveProvinceWideRegion(familyId, originSite), string.Empty);
            }
            _householdStandings[familyId] = state;
        }
        else
        {
            state = existing;
        }

        int nextStanding = Math.Clamp(
            checked(state.StandingDelta + delta),
            DaggerfallSocialState.MinimumReputation,
            DaggerfallSocialState.MaximumReputation);
        if (isArrest && !state.FeudActive && state.StandingDelta > DaggerfallIntroductions.BloodFeudDispositionThreshold)
        {
            // Arresting a household member or head never opens a blood feud on its own.
            nextStanding = Math.Max(nextStanding, DaggerfallIntroductions.BloodFeudDispositionThreshold + 1);
        }

        int appliedDelta = nextStanding - state.StandingDelta;
        state.StandingDelta = nextStanding;
        state.LastUpdatedDay = day;
        if (isMurder)
        {
            state.MurderCount++;
            state.FeudActive = true;
            // A fresh murder breaks any prior mediated truce.
            state.TruceMediated = false;
            state.MediatedByName = null;
            if (_tuning.MourningDurationDays > 0)
                state.MourningUntilDay = Math.Max(state.MourningUntilDay ?? 0, checked(day + _tuning.MourningDurationDays));
        }
        else if (nextStanding <= DaggerfallIntroductions.BloodFeudDispositionThreshold)
        {
            state.FeudActive = true;
        }

        if (subjectActorId is long sid && appliedDelta != 0)
        {
            int nextOffset = Math.Clamp(
                checked(state.SubjectOffsets.GetValueOrDefault(sid) - appliedDelta),
                DaggerfallSocialState.MinimumReputation * 2,
                DaggerfallSocialState.MaximumReputation * 2);
            if (nextOffset == 0)
                state.SubjectOffsets.Remove(sid);
            else
                state.SubjectOffsets[sid] = nextOffset;
        }

        if (isProvinceWide && wordSpread is not null && originSite is not null && appliedDelta != 0)
        {
            string? eventId = !string.IsNullOrWhiteSpace(rippleKey) ? $"wf:house:{rippleKey}" : null;
            wordSpread.RecordHouseholdStanding(familyId, appliedDelta, originSite, minute, headline, body, eventId);
        }

        if (state.IsEmpty)
            _householdStandings.Remove(familyId);

        return appliedDelta;
    }

    private int DecayDaysPerPoint(DaggerfallHouseholdScope scope) => scope switch
    {
        DaggerfallHouseholdScope.Local => _tuning.LocalGrudgeDecayDaysPerPoint,
        DaggerfallHouseholdScope.GreatHouse => _tuning.GreatHouseGrudgeDecayDaysPerPoint,
        DaggerfallHouseholdScope.Dynasty => _tuning.DynastyGrudgeDecayDaysPerPoint,
        _ => _tuning.LocalGrudgeDecayDaysPerPoint,
    };

    private int CurrentDayValue(long minute) =>
        currentDay is not null
            ? Math.Max(0, currentDay())
            : minute > 0
                ? checked((int)(minute / MinutesPerDay))
                : 0;

    private (int Region, string Location) ResolveLocalSettlement(string familyId, DaggerfallNpc? subjectNpc, DaggerfallSiteRecord? originSite)
    {
        if (!TryResolveLocalSettlement(familyId, subjectNpc, originSite, out int region, out string location))
            throw new InvalidOperationException($"Cannot resolve settlement for local household '{familyId}'.");
        return (region, location);
    }

    private bool TryResolveLocalSettlement(
        string familyId,
        DaggerfallNpc? subjectNpc,
        DaggerfallSiteRecord? originSite,
        out int region,
        out string location)
    {
        int? targetRegion = null;
        string? targetLocation = null;
        if (DaggerfallHouseholds.TryParseLocalSite(familyId, out DaggerfallSiteId localSiteId))
        {
            DaggerfallSiteRecord? homeSite = definitions.Locations.Records.FirstOrDefault(r => r.Id == localSiteId);
            if (homeSite is not null)
            {
                targetRegion = homeSite.Id.Region;
                targetLocation = homeSite.Name;
            }
        }
        targetRegion ??= subjectNpc?.Site.Region ?? originSite?.Id.Region ?? currentRegion();
        targetLocation ??= subjectNpc?.Site.Location ?? originSite?.Name;
        if (targetRegion is null || string.IsNullOrWhiteSpace(targetLocation))
        {
            region = 0;
            location = string.Empty;
            return false;
        }
        region = targetRegion.Value;
        location = targetLocation;
        return true;
    }

    private int ResolveProvinceWideRegion(string familyId, DaggerfallSiteRecord? originSite)
    {
        if (DaggerfallHouseholds.TryParseRegion(familyId, out int houseRegion))
            return houseRegion;
        return originSite?.Id.Region ?? currentRegion() ?? -1;
    }

    private static bool SameSettlement(DaggerfallNpcSite site, int region, string location) =>
        site.Region == region && string.Equals(site.Location, location, StringComparison.OrdinalIgnoreCase);

    private string? ResolveActorFamilyId(long actorId, ActorState? liveActor, DaggerfallNpc? npc)
    {
        if (liveActor is not null)
        {
            if (liveActor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay))
                return !string.IsNullOrWhiteSpace(overlay.FamilyId) ? overlay.FamilyId : null;
            if (liveActor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person)
                && !string.IsNullOrWhiteSpace(person.Snapshot.FamilyId))
                return person.Snapshot.FamilyId;
        }
        if (actorLedger.TryGet(actorId, out ActorLedgerEntry? entry)
            && entry?.Social is { } social)
        {
            if (social.FamilyIdCleared)
                return null;
            if (!string.IsNullOrWhiteSpace(social.FamilyId))
                return social.FamilyId;
        }
        if (npc is not null && !string.IsNullOrWhiteSpace(npc.FamilyId))
            return npc.FamilyId;
        return null;
    }

    private void RecordWitnessGrudge(long witnessId, ActorState witnessActor, string crimeRecord)
    {
        bool hadEntry = actorLedger.TryGet(witnessId, out ActorLedgerEntry? existing) && existing is not null;
        DaggerfallNpc? witnessNpc = npcs.TryGet(witnessId, out DaggerfallNpc? reg) ? reg : null;
        ActorOrigin? origin = existing?.Origin
            ?? (witnessActor.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? oc) ? oc.Origin : null);
        if (origin is null)
            return;

        ActorSocialDiff? socialDiff = existing?.Social
            ?? (witnessNpc is not null ? ActorSocialDiff.FromNpc(witnessNpc) : null);
        if (socialDiff is null && !hadEntry)
            return;

        if (socialDiff is not null)
        {
            string[] existingCrimes = socialDiff.WitnessedCrimes ?? [];
            string[] updatedCrimes = existingCrimes.Contains(crimeRecord, StringComparer.Ordinal)
                ? existingCrimes
                : [.. existingCrimes, crimeRecord];
            socialDiff = socialDiff with
            {
                DispositionDelta = socialDiff.DispositionDelta + WitnessMurderDispositionPenalty,
                WitnessedCrimes = updatedCrimes,
            };
        }

        ActorLedgerEntry updated = existing is not null
            ? existing with { Social = socialDiff }
            : new ActorLedgerEntry(witnessId, origin, Social: socialDiff);
        actorLedger.Upsert(updated);
    }

    private int? ResolveCatalogFaction(int? factionId) =>
        factionId is int id && id != 0 && definitions.Factions.Factions.ContainsKey(id) ? id : null;
}
