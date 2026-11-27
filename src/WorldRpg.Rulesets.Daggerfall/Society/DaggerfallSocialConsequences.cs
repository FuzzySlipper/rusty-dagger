using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Per-subject offset preventing the actor whose event caused a local household ripple from double-counting it.</summary>
internal sealed record DaggerfallLocalHouseholdSubjectOffsetSave(long ActorId, int Offset)
{
    internal void Validate()
    {
        if (ActorId <= 0 || ActorId == DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentOutOfRangeException(nameof(ActorId), "Saved local household subject offset requires a positive non-player actor id.");
        if (Offset < DaggerfallSocialState.MinimumReputation * 2 || Offset > DaggerfallSocialState.MaximumReputation * 2)
            throw new ArgumentOutOfRangeException(nameof(Offset), "Saved local household subject offset is out of range.");
    }
}

/// <summary>
/// Sparse settlement-local household standing delta for one affected <c>fam:local:...</c> household.
/// Applies deterministically to every member of that household (whether currently loaded or admitted later)
/// without emitting cross-town <see cref="DaggerfallWordSpread"/> wavefronts or writing per-NPC ledger rows for untouched kin.
/// </summary>
internal sealed record DaggerfallLocalHouseholdStandingSave(
    string FamilyId,
    int Region,
    string Location,
    int StandingDelta,
    DaggerfallLocalHouseholdSubjectOffsetSave[]? SubjectOffsets = null)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FamilyId);
        if (!DaggerfallHouseholds.IsValidFamilyId(FamilyId) || !DaggerfallHouseholds.IsLocal(FamilyId))
            throw new ArgumentException($"Saved local household standing requires a valid local FamilyId, got '{FamilyId}'.", nameof(FamilyId));
        if (Region is < 0 or > 61)
            throw new ArgumentOutOfRangeException(nameof(Region), "Saved local household standing region is out of range.");
        ArgumentException.ThrowIfNullOrWhiteSpace(Location);
        if (StandingDelta < DaggerfallSocialState.MinimumReputation || StandingDelta > DaggerfallSocialState.MaximumReputation)
            throw new ArgumentOutOfRangeException(nameof(StandingDelta), "Saved local household standing delta is out of range.");
        if (SubjectOffsets is not null)
        {
            HashSet<long> seen = [];
            foreach (DaggerfallLocalHouseholdSubjectOffsetSave offset in SubjectOffsets)
            {
                ArgumentNullException.ThrowIfNull(offset);
                offset.Validate();
                if (!seen.Add(offset.ActorId))
                    throw new ArgumentException($"Saved local household standing '{FamilyId}' repeats subject actor id {offset.ActorId}.");
            }
        }
    }
}

/// <summary>Sparse persisted snapshot of affected settlement-local households; empty when untouched.</summary>
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
                throw new ArgumentException($"Saved local household standings repeat family '{standing.FamilyId}'.");
        }
    }
}

/// <summary>
/// Social consequences when the player harms or helps faction/household members:
/// - Deducts faction reputation (routed through <see cref="FactionComponent"/>) and records witness crime memory
///   and personal disposition grudge on every living witness who observed a killing.
/// - Routes attributed household grudge &amp; goodwill ripples strictly through witnessed/reported/known deeds (never
///   straight off unwitnessed deaths):
///   - Town-local households (<c>fam:local:...</c>): sparse settlement-local household standings so both currently
///     loaded and later-admitted relatives in that settlement receive the grudge/goodwill with zero cross-town
///     <see cref="DaggerfallWordSpread"/> noise and zero bytes for unrelated households.
///   - Provincial Great Houses (<c>fam:house:...</c>) and royal cast dynasties (<c>fam:dynasty:...</c>): province-wide
///     <see cref="DaggerfallWordSpread"/> <see cref="DaggerfallWordEventKind.HouseholdStanding"/> wavefronts so kin
///     across the province learn at courier speed.
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
    Func<long>? currentMinute = null)
{
    private sealed class LocalHouseholdStandingState(int region, string location)
    {
        internal int Region { get; } = region;
        internal string Location { get; } = location;
        internal int StandingDelta { get; set; }
        internal Dictionary<long, int> SubjectOffsets { get; } = [];
    }

    internal const int PrimaryFactionKillPenalty = -10;
    internal const int RegionalPeopleKillPenalty = -5;
    internal const int WitnessMurderDispositionPenalty = -15;
    internal const int KinMurderDispositionPenalty = -20;
    internal const int KinAssaultDispositionPenalty = -10;
    internal const int KinPropertyCrimeDispositionPenalty = -8;
    internal const int KinGoodwillMinimumBonus = 2;
    internal const int KinGoodwillMaximumBonus = 10;

    private readonly HashSet<string> _rippledEventKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LocalHouseholdStandingState> _localHouseholdStandings = new(StringComparer.Ordinal);

    internal DaggerfallLocalHouseholdsSave Capture()
    {
        if (_localHouseholdStandings.Count == 0)
            return DaggerfallLocalHouseholdsSave.Empty;

        List<DaggerfallLocalHouseholdStandingSave> entries = [];
        foreach ((string familyId, LocalHouseholdStandingState state) in _localHouseholdStandings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (state.StandingDelta == 0 && state.SubjectOffsets.Count == 0)
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
                offsets is { Length: > 0 } ? offsets : null));
        }
        return entries.Count == 0 ? DaggerfallLocalHouseholdsSave.Empty : new([.. entries]);
    }

    internal void Restore(DaggerfallLocalHouseholdsSave? saved)
    {
        _localHouseholdStandings.Clear();
        if (saved is null)
            return;
        saved.Validate();
        foreach (DaggerfallLocalHouseholdStandingSave entry in saved.Standings)
        {
            LocalHouseholdStandingState state = new(entry.Region, entry.Location)
            {
                StandingDelta = entry.StandingDelta,
            };
            if (entry.SubjectOffsets is { Length: > 0 } offsets)
            {
                foreach (DaggerfallLocalHouseholdSubjectOffsetSave offset in offsets)
                {
                    if (offset.Offset != 0)
                        state.SubjectOffsets[offset.ActorId] = offset.Offset;
                }
            }
            _localHouseholdStandings[entry.FamilyId] = state;
        }
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
                body: $"Word spreads that {victimName} was slain before witnesses; their house will not forgive the killer.");
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
            body: body);
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
    /// plus any settlement-local household standing (for <c>fam:local:...</c> kin, including relatives admitted after
    /// the triggering event) or arrived province-wide <see cref="DaggerfallWordSpread"/>
    /// <see cref="DaggerfallWordEventKind.HouseholdStanding"/> delta (for Great House or royal dynasty kin).
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
            if (!_localHouseholdStandings.TryGetValue(familyId, out LocalHouseholdStandingState? localState))
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

        if (wordSpread is null || !DaggerfallHouseholds.IsProvinceWide(familyId))
            return personal;

        DaggerfallSiteRecord? targetSite = atSite ?? activeSite?.Invoke();
        if (targetSite is null)
            return personal;

        int houseDelta = wordSpread.ArrivedHouseholdStandingDelta(
            familyId,
            targetSite,
            minute ?? currentMinute?.Invoke() ?? 0L);
        if (houseDelta == 0)
            return personal;

        return Math.Clamp(
            checked(personal + houseDelta),
            DaggerfallSocialState.MinimumReputation,
            DaggerfallSocialState.MaximumReputation);
    }

    /// <summary>
    /// Returns the active household standing delta for <paramref name="familyId"/> at <paramref name="atSite"/>:
    /// settlement-local standing for <c>fam:local:...</c> or arrived province-wide
    /// <see cref="DaggerfallWordEventKind.HouseholdStanding"/> delta for Great Houses and royal dynasties.
    /// </summary>
    internal int HouseholdStandingDelta(string? familyId, DaggerfallSiteRecord? atSite = null, long? minute = null)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            return 0;
        DaggerfallSiteRecord? targetSite = atSite ?? activeSite?.Invoke();
        if (DaggerfallHouseholds.IsLocal(familyId))
        {
            if (!_localHouseholdStandings.TryGetValue(familyId, out LocalHouseholdStandingState? localState))
                return 0;
            if (targetSite is not null
                && (targetSite.Id.Region != localState.Region
                    || !string.Equals(targetSite.Name, localState.Location, StringComparison.OrdinalIgnoreCase)))
            {
                return 0;
            }
            return localState.StandingDelta;
        }
        if (wordSpread is null || !DaggerfallHouseholds.IsProvinceWide(familyId) || targetSite is null)
            return 0;
        return wordSpread.ArrivedHouseholdStandingDelta(familyId, targetSite, minute ?? currentMinute?.Invoke() ?? 0L);
    }

    /// <summary>
    /// Sets a household's standing delta (-100..100) through the canonical owner (local household table for
    /// <c>fam:local:...</c> or <see cref="DaggerfallWordSpread.RecordHouseholdStanding"/> for Great Houses and royal
    /// dynasties) so saves, blood-feud gates, and inspection surfaces behave identically to play.
    /// </summary>
    internal int SetHouseholdStanding(string familyId, int targetStandingDelta, DaggerfallSiteRecord? atSite = null, long? minute = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyId);
        if (!DaggerfallHouseholds.IsValidFamilyId(familyId))
            throw new ArgumentException($"Invalid household family id '{familyId}'.", nameof(familyId));

        int clamped = Math.Clamp(targetStandingDelta, DaggerfallSocialState.MinimumReputation, DaggerfallSocialState.MaximumReputation);
        DaggerfallSiteRecord? site = atSite ?? activeSite?.Invoke();

        if (DaggerfallHouseholds.IsLocal(familyId))
        {
            if (clamped == 0)
            {
                _localHouseholdStandings.Remove(familyId);
                return 0;
            }

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
            targetRegion ??= site?.Id.Region ?? currentRegion();
            targetLocation ??= site?.Name;
            if (targetRegion is null || string.IsNullOrWhiteSpace(targetLocation))
                throw new InvalidOperationException($"Cannot resolve settlement for local household '{familyId}'.");

            if (!_localHouseholdStandings.TryGetValue(familyId, out LocalHouseholdStandingState? state))
            {
                state = new LocalHouseholdStandingState(targetRegion.Value, targetLocation);
                _localHouseholdStandings[familyId] = state;
            }
            state.StandingDelta = clamped;
            state.SubjectOffsets.Clear();
            return clamped;
        }

        if (!DaggerfallHouseholds.IsProvinceWide(familyId))
            throw new ArgumentException($"Unsupported household scope for '{familyId}'.", nameof(familyId));
        if (wordSpread is null || site is null)
            throw new InvalidOperationException($"Cannot set province-wide household standing for '{familyId}' without an active site and word spread owner.");

        long nowMinute = minute ?? currentMinute?.Invoke() ?? 0L;
        int current = HouseholdStandingDelta(familyId, site, nowMinute);
        int delta = clamped - current;
        if (delta != 0)
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

    private void ApplyAttributedHouseholdRipple(
        string familyId,
        int delta,
        long? subjectActorId,
        DaggerfallNpc? subjectNpc,
        DaggerfallSiteRecord? originSite,
        long minute,
        string? rippleKey,
        string? headline,
        string? body)
    {
        if (delta == 0)
            return;
        if (!string.IsNullOrWhiteSpace(rippleKey) && !_rippledEventKeys.Add(rippleKey))
            return;

        // Tier 2: Great Houses and royal cast dynasties spread at courier speed via DaggerfallWordSpread
        // (instant within InstantNewsBubblePixels, courier-paced across the province, zero per-NPC ledger spam).
        if (DaggerfallHouseholds.IsProvinceWide(familyId) && wordSpread is not null && originSite is not null)
        {
            string? eventId = !string.IsNullOrWhiteSpace(rippleKey) ? $"wf:house:{rippleKey}" : null;
            wordSpread.RecordHouseholdStanding(familyId, delta, originSite, minute, headline, body, eventId);
            return;
        }

        // Tier 1: Commoner/shop/tavern households (fam:local:...) record one sparse settlement-local standing entry
        // so both currently loaded kin and untouched deterministic relatives admitted later receive the consequence
        // without cross-town WordSpread wavefronts or per-NPC ledger bloat.
        if (!DaggerfallHouseholds.IsLocal(familyId))
            return;

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
            return;

        if (!_localHouseholdStandings.TryGetValue(familyId, out LocalHouseholdStandingState? state))
        {
            state = new LocalHouseholdStandingState(targetRegion.Value, targetLocation);
            _localHouseholdStandings[familyId] = state;
        }

        int nextStanding = Math.Clamp(
            checked(state.StandingDelta + delta),
            DaggerfallSocialState.MinimumReputation,
            DaggerfallSocialState.MaximumReputation);
        int appliedDelta = nextStanding - state.StandingDelta;
        state.StandingDelta = nextStanding;
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
