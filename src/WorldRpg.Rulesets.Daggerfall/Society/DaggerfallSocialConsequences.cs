using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Social consequences when the player harms or helps faction/household members:
/// - Deducts faction reputation (routed through <see cref="FactionComponent"/>) and records witness crime memory
///   and personal disposition grudge on every living witness who observed a killing.
/// - Routes attributed household grudge &amp; goodwill ripples strictly through witnessed/reported/known deeds (never
///   straight off unwitnessed deaths):
///   - Town-local households (<c>fam:local:...</c>): sparse <see cref="DaggerfallActorLedger"/> disposition updates
///     within the subject's settlement only (zero cross-town <see cref="DaggerfallWordSpread"/> noise).
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
    internal const int PrimaryFactionKillPenalty = -10;
    internal const int RegionalPeopleKillPenalty = -5;
    internal const int WitnessMurderDispositionPenalty = -15;
    internal const int KinMurderDispositionPenalty = -20;
    internal const int KinAssaultDispositionPenalty = -10;
    internal const int KinPropertyCrimeDispositionPenalty = -8;
    internal const int KinGoodwillMinimumBonus = 2;
    internal const int KinGoodwillMaximumBonus = 10;

    private readonly HashSet<string> _rippledEventKeys = new(StringComparer.Ordinal);

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
    /// plus any arrived province-wide <see cref="DaggerfallWordSpread"/> <see cref="DaggerfallWordEventKind.HouseholdStanding"/>
    /// delta for Great House or royal dynasty kin at <paramref name="atSite"/>.
    /// </summary>
    internal int EffectiveDispositionDelta(long actorId, DaggerfallSiteRecord? atSite = null, long? minute = null)
    {
        int personal = actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) && entry?.Social is { } s
            ? s.DispositionDelta
            : 0;
        if (wordSpread is null)
            return personal;

        DaggerfallSiteRecord? targetSite = atSite ?? activeSite?.Invoke();
        if (targetSite is null)
            return personal;

        DaggerfallNpc? npc = npcs.TryGet(actorId, out DaggerfallNpc? reg) ? reg : null;
        ActorState? live = actors.TryGet(actorId, out ActorState? act) ? act : null;
        string? familyId = ResolveActorFamilyId(actorId, live, npc);
        if (string.IsNullOrWhiteSpace(familyId) || !DaggerfallHouseholds.IsProvinceWide(familyId))
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

        // Tier 1: Commoner/shop/tavern households (fam:local:...) ripple strictly within the victim's/beneficiary's settlement.
        int? targetRegion = subjectNpc?.Site.Region ?? originSite?.Id.Region ?? currentRegion();
        string? targetLocation = subjectNpc?.Site.Location ?? originSite?.Name;
        if (targetRegion is null || string.IsNullOrWhiteSpace(targetLocation))
            return;

        HashSet<long> updatedKin = [];
        foreach (ActorState live in actors.All)
        {
            long id = live.DurableId;
            if (id == DaggerfallActorIdentity.PlayerEntityId || id == subjectActorId || live.IsDefeated)
                continue;
            DaggerfallNpc? kinNpc = npcs.TryGet(id, out DaggerfallNpc? reg) ? reg : null;
            if (kinNpc is null || !SameSettlement(kinNpc.Site, targetRegion.Value, targetLocation))
                continue;
            string? kinFamily = ResolveActorFamilyId(id, live, kinNpc);
            if (!DaggerfallWitnessDiscretion.AreFamily(kinFamily, familyId))
                continue;
            if (updatedKin.Add(id))
                ApplyKinDispositionDelta(id, live, kinNpc, delta);
        }

        foreach (DaggerfallNpc kinNpc in npcs.All)
        {
            long id = kinNpc.DurableId;
            if (id == DaggerfallActorIdentity.PlayerEntityId || id == subjectActorId || updatedKin.Contains(id))
                continue;
            if (kinNpc.Presence == DaggerfallNpcPresence.Removed)
                continue;
            if (!SameSettlement(kinNpc.Site, targetRegion.Value, targetLocation))
                continue;
            string? kinFamily = ResolveActorFamilyId(id, null, kinNpc);
            if (!DaggerfallWitnessDiscretion.AreFamily(kinFamily, familyId))
                continue;
            if (updatedKin.Add(id))
                ApplyKinDispositionDelta(id, null, kinNpc, delta);
        }
    }

    private static bool SameSettlement(DaggerfallNpcSite site, int region, string location) =>
        site.Region == region && string.Equals(site.Location, location, StringComparison.OrdinalIgnoreCase);

    private void ApplyKinDispositionDelta(long kinId, ActorState? liveActor, DaggerfallNpc kinNpc, int delta)
    {
        bool hadEntry = actorLedger.TryGet(kinId, out ActorLedgerEntry? existing) && existing is not null;
        ActorOrigin? origin = existing?.Origin
            ?? (liveActor is not null && liveActor.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? oc) ? oc.Origin : null)
            ?? (kinNpc.Profile is { } profile
                ? kinNpc.StableKey.StartsWith("cast:", StringComparison.Ordinal)
                    ? ActorOrigin.Cast(kinNpc.StableKey["cast:".Length..], profile)
                    : DaggerfallPeopleSlots.IsDeterministicId(kinId)
                        ? ActorOrigin.Seeded(profile, kinNpc.StableKey)
                        : ActorOrigin.MintedActor(
                            kinNpc.Kind == DaggerfallNpcKind.Civilian ? DaggerfallActorKinds.Civilian : DaggerfallActorKinds.StaticNpc,
                            gender: kinNpc.Appearance.Gender)
                : null);
        if (origin is null)
            return;

        ActorSocialDiff socialDiff = existing?.Social ?? ActorSocialDiff.FromNpc(kinNpc);
        int nextDisposition = Math.Clamp(
            checked(socialDiff.DispositionDelta + delta),
            DaggerfallSocialState.MinimumReputation,
            DaggerfallSocialState.MaximumReputation);
        socialDiff = socialDiff with { DispositionDelta = nextDisposition };

        ActorLedgerEntry updated = hadEntry && existing is not null
            ? existing with { Social = socialDiff }
            : new ActorLedgerEntry(kinId, origin, Social: socialDiff);
        actorLedger.Upsert(updated);
    }

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
