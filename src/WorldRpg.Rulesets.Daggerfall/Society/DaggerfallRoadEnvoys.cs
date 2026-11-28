using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The people a court sends on the road: a board move's courier (#11055), a ration convoy's drover and guards (#11180).
/// Each is a seeded actor in the actor ledger under a slot of its own in the town it sets out from (<c>population/KIND/…</c>),
/// who rides an <see cref="ActorLocationKind.OnOverlandBand"/> until the errand ends, and is then released.
/// </summary>
internal static class DaggerfallRoadEnvoys
{
    /// <summary>
    /// Stages the envoy for slot <paramref name="slotId"/> in <paramref name="origin"/>: a person of the town's people, of
    /// <paramref name="factionId"/> (else the region's people), drawn from the slot's seed, and returns its durable id.
    /// </summary>
    internal static long Stage(DaggerfallDefinitions definitions, DaggerfallActorLedger actorLedger, ulong worldSocialSeed,
        Func<DaggerfallNpcAppearance, int, string>? generateName, DaggerfallSiteRecord origin, string slotId, string spawner,
        string occupation, string fallbackName, int? factionId)
    {
        string race = DaggerfallSourcePopulation.TryResolveClimateRace(definitions, origin, out _) ?? "breton";
        DaggerfallWorldProfileKey originProfile = DaggerfallWorldProfileIds.Exterior(origin.Id);
        ulong seed = DaggerfallPeopleSlots.SlotSeed(worldSocialSeed, origin.Id, slotId);
        long durableId = DaggerfallPeopleSlots.SeededSlotIdentity(origin.Id, $"{originProfile.LogicalId}/{slotId}");

        string gender = ((seed >> 3) & 1UL) != 0 ? "Female" : "Male";
        int nameSeed = (int)((seed >> 8) & 0x7FFFUL) + 1;
        int faction = factionId is int given && given > 0
            ? given
            : DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, origin.Id.Region)?.Id ?? 0;
        DaggerfallNpcAppearance look = new(race, gender, 182, 0, nameSeed, faction);
        string displayName = generateName?.Invoke(look, origin.Id.Region) ?? fallbackName;
        ActorOrigin actorOrigin = ActorOrigin.Seeded(originProfile, slotId, spawner,
            ActorHarmPolicyKind.LawProtectedCivilian, ActorInteractionPolicyKind.FullDialogue);
        ActorSocialDiff socialDiff = new(
            (int)DaggerfallNpcKind.Civilian,
            slotId,
            origin.Id.Region,
            origin.Name,
            string.Empty,
            originProfile.LogicalId,
            look.Race,
            look.Gender,
            look.BillboardArchive,
            look.BillboardRecord,
            look.NameSeed,
            look.FactionId,
            occupation,
            ["talk"],
            DisplayNameOverride: displayName);
        actorLedger.Upsert(new ActorLedgerEntry(durableId, actorOrigin, Social: socialDiff));
        return durableId;
    }

    /// <summary>
    /// Releases the envoys still riding band <paramref name="bandId"/> once its errand is over, so a save keeps neither an
    /// orphaned band member nor a permanent person at a town nobody visited: one staged under <paramref name="slotPrefix"/>
    /// and never met or touched is forgotten, and any other goes off stage.
    /// </summary>
    internal static void Release(DaggerfallActorLedger actorLedger, string bandId, string slotPrefix, IEnumerable<long> riders)
    {
        foreach (ActorLedgerEntry member in actorLedger.EntriesOnOverlandBand(bandId))
            ReleaseOne(actorLedger, member, slotPrefix);
        foreach (long rider in riders)
            if (actorLedger.TryGet(rider, out ActorLedgerEntry? entry)
                && entry?.Location is { Kind: ActorLocationKind.OnOverlandBand, BandId: { } memberBandId }
                && StringComparer.Ordinal.Equals(memberBandId, bandId))
                ReleaseOne(actorLedger, entry, slotPrefix);
    }

    private static void ReleaseOne(DaggerfallActorLedger actorLedger, ActorLedgerEntry entry, string slotPrefix)
    {
        bool untouched = entry.Origin.SlotKey is { } slotKey
            && slotKey.StartsWith(slotPrefix, StringComparison.Ordinal)
            && entry.Vitals is null
            && entry.Combat is null
            && entry.Referral is null;
        if (untouched)
            actorLedger.Remove(entry.DurableId);
        else
            actorLedger.Upsert(entry with { Location = ActorLocationOverride.OffStage });
    }
}
