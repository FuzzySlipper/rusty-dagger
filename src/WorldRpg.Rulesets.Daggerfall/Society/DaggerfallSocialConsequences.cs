using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Social consequences when the player kills a faction member or townsperson: deducts faction reputation
/// (routed through <see cref="FactionComponent"/>) and records witness crime memory and personal disposition
/// grudge on every living witness who observed the killing.
/// </summary>
internal sealed class DaggerfallSocialConsequences(
    DaggerfallSocialState social,
    DaggerfallDefinitions definitions,
    ActorsState actors,
    DaggerfallNpcRegistry npcs,
    DaggerfallActorLedger actorLedger,
    Func<DaggerfallCrimeWitnessEvidence> queryWitnesses,
    Func<long, bool> isCityWatch,
    Func<int?> currentRegion)
{
    internal const int PrimaryFactionKillPenalty = -10;
    internal const int RegionalPeopleKillPenalty = -5;
    internal const int WitnessMurderDispositionPenalty = -15;

    /// <summary>Applies faction standing loss and witness memory when the player kills a faction-affiliated actor.</summary>
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
