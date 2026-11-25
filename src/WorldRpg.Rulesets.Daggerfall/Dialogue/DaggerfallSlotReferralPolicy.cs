using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Result of registering a pre-visit structured vouch toward an actual authored <c>SeededSlot</c> at another town.</summary>
internal sealed record DaggerfallSlotReferralResult(
    long DurableId,
    DaggerfallSiteId TargetSite,
    DaggerfallWorldProfileKey TargetProfile,
    string SlotId,
    string ContactName,
    ActorVouchRecord Vouch,
    bool Added,
    string Message);

/// <summary>
/// Issues real pre-visit structured vouches during dialogue that address deterministic <see cref="ActorOriginKind.SeededSlot"/>
/// bystanders in neighboring towns from <c>daggerfall.people</c> before the player visits those towns.
/// Only the referred slot enters <see cref="DaggerfallActorLedger"/>; untouched neighbors stay at zero save bytes.
/// </summary>
internal sealed class DaggerfallSlotReferralPolicy(
    DaggerfallDefinitions definitions,
    DaggerfallActorLedger actorLedger)
{
    private readonly DaggerfallDefinitions _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly DaggerfallActorLedger _actorLedger = actorLedger ?? throw new ArgumentNullException(nameof(actorLedger));

    /// <summary>
    /// Finds a deterministic neighboring exterior town placing an authored <c>ambient-bark</c> slot in
    /// <c>daggerfall.people</c>, registers a pre-visit structured <see cref="ActorVouchRecord"/> in
    /// <see cref="DaggerfallActorLedger"/>, and returns a referral line naming the contact and town.
    /// </summary>
    internal DaggerfallSlotReferralResult? TryIssuePreVisitReferral(
        DaggerfallSiteRecord currentSite,
        DaggerfallNpc speaker,
        int speakerFactionId = 0,
        int currentDay = 0)
    {
        ArgumentNullException.ThrowIfNull(currentSite);
        ArgumentNullException.ThrowIfNull(speaker);

        (DaggerfallSiteRecord Target, DaggerfallSiteBlock Block, DaggerfallPeopleSlotDefinition Slot)? candidate =
            FindNearestReferralTarget(currentSite);
        if (candidate is null) return null;

        (DaggerfallSiteRecord target, DaggerfallSiteBlock block, DaggerfallPeopleSlotDefinition slot) = candidate.Value;
        DaggerfallWorldProfileKey targetProfile = DaggerfallWorldProfileIds.Exterior(target.Id);
        string placedSlotId = DaggerfallPeopleSlots.PlacedExteriorBlockPersonId(
            block.SourceName, block.X, block.Y, slot.SlotId);
        string speakerName = speaker.DisplayName ?? speaker.Role;
        DaggerfallPeopleReferralTemplates referrals = _definitions.People.Barks.Referrals;
        string targetRegionName = _definitions.BuildingNames.RegionName(target.Id.Region);
        string targetClimateName = _definitions.Grids.Climate.GetCell(target.MapPixelX + 1, target.MapPixelY).Name;
        ulong contactSeed = StableHash.NonZeroFnv1a64($"daggerfall.referral.contact:{target.Id.Region}:{target.Id.Index}:{placedSlotId}");
        string contactName = referrals.ResolveContactName(contactSeed, target.Id.Region, targetRegionName, targetClimateName);
        int resolvedFaction = speakerFactionId > 0 ? speakerFactionId : Math.Max(0, speaker.Appearance.FactionId);
        ActorVouchRecord vouch = new(
            speaker.DurableId,
            resolvedFaction,
            ActorVouchKind.Contact,
            currentDay,
            speakerName,
            currentSite.Name);

        (long durableId, bool added) = _actorLedger.RegisterSlotVouch(
            targetProfile,
            placedSlotId,
            vouch,
            displayNameOverride: contactName);

        string currentRegionName = _definitions.BuildingNames.RegionName(currentSite.Id.Region);
        string currentClimateName = _definitions.Grids.Climate.GetCell(currentSite.MapPixelX + 1, currentSite.MapPixelY).Name;
        ulong issueSeed = StableHash.NonZeroFnv1a64($"daggerfall.referral.issue:{currentSite.Id.Region}:{currentSite.Id.Index}:{speaker.DurableId}:{target.Id.Index}");
        string message = referrals.FormatIssueLine(issueSeed, target.Name, contactName, speakerName,
            currentSite.Id.Region, currentRegionName, currentClimateName);
        return new(durableId, target.Id, targetProfile, placedSlotId, contactName, vouch, added, message);
    }

    internal (DaggerfallSiteRecord Target, DaggerfallSiteBlock Block, DaggerfallPeopleSlotDefinition Slot)?
        FindNearestReferralTarget(DaggerfallSiteRecord currentSite)
    {
        ArgumentNullException.ThrowIfNull(currentSite);
        (DaggerfallSiteRecord Target, DaggerfallSiteBlock Block, DaggerfallPeopleSlotDefinition Slot, bool SameRegion, int Distance)? best = null;

        foreach (DaggerfallSiteRecord record in _definitions.Locations.Records)
        {
            if (record.Id == currentSite.Id || record.Exterior is not { Blocks.Count: > 0 } exterior)
                continue;

            DaggerfallSiteBlock? matchedBlock = null;
            DaggerfallPeopleSlotDefinition? matchedSlot = null;
            foreach (DaggerfallSiteBlock block in exterior.Blocks
                .OrderBy(b => b.Y).ThenBy(b => b.X).ThenBy(b => b.SourceName, StringComparer.Ordinal))
            {
                IReadOnlyList<DaggerfallPeopleSlotDefinition> slots = _definitions.People.GetRmbExteriorSlots(block.SourceName);
                DaggerfallPeopleSlotDefinition? barkSlot = slots.FirstOrDefault(s =>
                    string.Equals(s.Interaction, "ambient-bark", StringComparison.Ordinal));
                if (barkSlot is not null)
                {
                    matchedBlock = block;
                    matchedSlot = barkSlot;
                    break;
                }
            }
            if (matchedBlock is null || matchedSlot is null) continue;

            bool sameRegion = record.Id.Region == currentSite.Id.Region;
            int distance = Math.Abs(record.MapPixelX - currentSite.MapPixelX)
                + Math.Abs(record.MapPixelY - currentSite.MapPixelY);
            if (best is null
                || (sameRegion && !best.Value.SameRegion)
                || (sameRegion == best.Value.SameRegion && (distance < best.Value.Distance
                    || (distance == best.Value.Distance && (record.Id.Region < best.Value.Target.Id.Region
                        || (record.Id.Region == best.Value.Target.Id.Region && record.Id.Index < best.Value.Target.Id.Index))))))
            {
                best = (record, matchedBlock, matchedSlot, sameRegion, distance);
            }
        }

        return best is { } found ? (found.Target, found.Block, found.Slot) : null;
    }
}
