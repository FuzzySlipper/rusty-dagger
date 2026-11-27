using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Identified road-side overland encounter metadata for a traveling caravan merchant or regional courier.</summary>
internal sealed record DaggerfallRoadEncounterContext(
    bool IsCaravan,
    bool IsCourier,
    int OriginRegion,
    string OriginRegionName,
    string OriginLocationName,
    DaggerfallSiteRecord? OriginSite);

/// <summary>One public road news item shared by an intercepted regional courier.</summary>
internal sealed record DaggerfallCourierNewsItem(
    string Reply,
    bool AheadOfRoadWave,
    string? WavefrontEventId = null);

/// <summary>
/// Resolves road-side encounter identity for intercepted <c>DaggerfallOverlandTraffic</c> caravans and regional
/// couriers, and builds public origin-region news exchanges that surface real <see cref="DaggerfallWordSpread"/>
/// wavefronts ahead of the road wave without spoiling unrevealed sealed <see cref="DaggerfallCourierDispatches"/>.
/// </summary>
internal static class DaggerfallRoadCourierNews
{
    internal static DaggerfallRoadEncounterContext? TryResolveEncounter(
        DaggerfallNpc npc,
        DaggerfallActorLedger? actorLedger,
        DaggerfallCourierDispatches? courierDispatches,
        DaggerfallDefinitions definitions,
        Func<DaggerfallSiteId, DaggerfallSiteRecord?>? findSite)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(definitions);

        if (actorLedger is not null
            && actorLedger.TryGet(npc.DurableId, out ActorLedgerEntry? entry)
            && entry?.Location is { Kind: ActorLocationKind.AtSite, TrafficVisitor: true })
        {
            bool isCaravan = string.Equals(npc.Role, "merchant", StringComparison.OrdinalIgnoreCase)
                && npc.Services.Contains("merchant", StringComparer.Ordinal);
            bool isCourier = string.Equals(npc.Role, "courier", StringComparison.OrdinalIgnoreCase);
            if (isCaravan || isCourier)
            {
                DaggerfallSiteId? originSiteId = entry.Origin.SiteProfile?.Require().Site;
                DaggerfallSiteRecord? originSite = originSiteId is { } oid ? findSite?.Invoke(oid) : null;
                int originRegion = originSite?.Id.Region ?? originSiteId?.Region ?? npc.Site.Region;
                string originRegionName = definitions.BuildingNames.RegionName(originRegion);
                string originLocationName = originSite?.Name
                    ?? (string.IsNullOrWhiteSpace(npc.Site.Location) ? originRegionName : npc.Site.Location);
                return new(isCaravan, isCourier, originRegion, originRegionName, originLocationName, originSite);
            }
        }

        if (courierDispatches?.TryGetForCourier(npc.DurableId) is { } carried)
        {
            DaggerfallSiteId originSiteId = carried.OriginSite.Require();
            DaggerfallSiteRecord? originSite = findSite?.Invoke(originSiteId);
            int originRegion = originSiteId.Region;
            string originRegionName = definitions.BuildingNames.RegionName(originRegion);
            string originLocationName = originSite?.Name ?? originRegionName;
            return new(IsCaravan: false, IsCourier: true, originRegion, originRegionName, originLocationName, originSite);
        }

        return null;
    }

    internal static string? BuildGreetingLine(DaggerfallRoadEncounterContext encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        string from = StringComparer.OrdinalIgnoreCase.Equals(encounter.OriginLocationName, encounter.OriginRegionName)
            ? encounter.OriginRegionName
            : $"{encounter.OriginLocationName} in {encounter.OriginRegionName}";
        if (encounter.IsCaravan)
            return $"We're hauling trade goods out of {from} — care to look over our wares or trade?";
        if (encounter.IsCourier)
            return $"Riding out of {from} — ask if you want the latest word from along the road.";
        return null;
    }

    internal static IReadOnlyList<DaggerfallCourierNewsItem> BuildNewsItems(
        DaggerfallRoadEncounterContext encounter,
        DaggerfallNpc npc,
        DaggerfallSiteRecord activeSite,
        long currentMinute,
        int currentDay,
        DaggerfallWordSpread? wordSpread,
        IDaggerfallWorldConditionSource? worldConditions,
        DaggerfallCourierDispatches? courierDispatches,
        DaggerfallDefinitions definitions,
        Func<DaggerfallRumorDefinition, DaggerfallSiteRecord, string>? renderRumor = null)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(activeSite);
        ArgumentNullException.ThrowIfNull(definitions);

        HashSet<string> blockedWavefronts = new(StringComparer.Ordinal);
        HashSet<string> blockedConditions = new(StringComparer.Ordinal);
        if (courierDispatches is not null)
        {
            foreach (DaggerfallCourierDispatchSave dispatch in courierDispatches.All)
            {
                if (dispatch.Revealed
                    || dispatch.Status is not (DaggerfallDispatchStatus.InTransit or DaggerfallDispatchStatus.CarriedByPlayer))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(dispatch.LinkedWavefrontId))
                    blockedWavefronts.Add(dispatch.LinkedWavefrontId);
                if (!string.IsNullOrWhiteSpace(dispatch.LinkedConditionTokenId))
                    blockedConditions.Add(dispatch.LinkedConditionTokenId);
            }
        }

        List<DaggerfallCourierNewsItem> items = [];
        HashSet<string> seenBodies = new(StringComparer.Ordinal);

        void AddUnique(string bodyKey, DaggerfallCourierNewsItem item)
        {
            if (!string.IsNullOrWhiteSpace(bodyKey) && seenBodies.Add(bodyKey))
                items.Add(item);
        }

        // 1. Real DaggerfallWordSpread wavefronts from the courier's origin region (or already known at their origin site),
        //    prioritizing news that has NOT YET reached the player's current site (ahead of the road wave).
        if (wordSpread is not null)
        {
            List<(DaggerfallWordWavefrontSave Wavefront, DaggerfallWordArrivalEvaluation LocalEval, bool FromOriginRegion)> ahead = [];
            List<(DaggerfallWordWavefrontSave Wavefront, DaggerfallWordArrivalEvaluation LocalEval)> arrivedFromOrigin = [];

            foreach (DaggerfallWordWavefrontSave wf in wordSpread.Wavefronts)
            {
                if (wf.Kind is not (DaggerfallWordEventKind.NewsRumor
                    or DaggerfallWordEventKind.FactionStanding
                    or DaggerfallWordEventKind.HouseholdStanding))
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(wf.Body))
                    continue;
                if (wf.CourierIntercepted)
                    continue;
                if (blockedWavefronts.Contains(wf.EventId))
                    continue;
                if (wf.SuppressedRegions is { Length: > 0 } sup && sup.Contains(activeSite.Id.Region))
                    continue;

                bool fromOriginRegion = wf.OriginRegion == encounter.OriginRegion;
                bool knownAtOrigin = fromOriginRegion
                    || (encounter.OriginSite is { } originSite
                        && wordSpread.EvaluateAt(wf, originSite, currentMinute, currentDay).HasArrived);
                if (!knownAtOrigin)
                    continue;

                DaggerfallWordArrivalEvaluation localEval = wordSpread.EvaluateAt(wf, activeSite, currentMinute, currentDay);
                if (localEval.Status == DaggerfallWordArrivalStatus.Blocked)
                    continue;

                if (!localEval.HasArrived)
                    ahead.Add((wf, localEval, fromOriginRegion));
                else if (fromOriginRegion)
                    arrivedFromOrigin.Add((wf, localEval));
            }

            foreach ((DaggerfallWordWavefrontSave wf, DaggerfallWordArrivalEvaluation localEval, _) in ahead
                .OrderByDescending(entry => entry.FromOriginRegion)
                .ThenByDescending(entry => entry.Wavefront.EmittedAtMinute)
                .ThenBy(entry => entry.Wavefront.EventId, StringComparer.Ordinal))
            {
                string delayNote = !string.IsNullOrWhiteSpace(localEval.DisruptionReason)
                    ? $" (the road posts are slowed by {localEval.DisruptionReason})"
                    : string.Empty;
                string reply = $"Ahead of the road posts from {encounter.OriginRegionName}{delayNote}: {wf.Body}";
                AddUnique(wf.Body!, new(reply, AheadOfRoadWave: true, WavefrontEventId: wf.EventId));
            }

            foreach ((DaggerfallWordWavefrontSave wf, _) in arrivedFromOrigin
                .OrderByDescending(entry => entry.Wavefront.EmittedAtMinute)
                .ThenBy(entry => entry.Wavefront.EventId, StringComparer.Ordinal))
            {
                string reply = $"Word out of {encounter.OriginRegionName}: {wf.Body}";
                AddUnique(wf.Body!, new(reply, AheadOfRoadWave: false, WavefrontEventId: wf.EventId));
            }
        }

        // 2. Active regional WorldConditions in the courier's origin region (excluding unrevealed sealed dispatches).
        if (worldConditions is not null)
        {
            foreach (DaggerfallWorldConditionToken token in worldConditions.ActiveForRegion(encounter.OriginRegion, currentDay))
            {
                if (blockedConditions.Contains(token.TokenId))
                    continue;
                string? sourceName = token.SourceFactionId is int sf && sf > 0
                    && definitions.Factions.Factions.TryGetValue(sf, out DaggerfallFactionDefinition? sfDef)
                        ? sfDef.Name
                        : null;
                string? text = !string.IsNullOrWhiteSpace(token.Body)
                    ? token.Body
                    : definitions.People.Barks.TryResolveConditionBarkLine(
                        unchecked((ulong)npc.DurableId + (ulong)token.Kind),
                        token.Kind,
                        encounter.OriginRegionName,
                        sourceName,
                        out string? condLine)
                        ? condLine
                        : null;
                if (!string.IsNullOrWhiteSpace(text))
                    AddUnique(text, new($"From {encounter.OriginRegionName}: {text}", AheadOfRoadWave: false));
            }
        }

        // 3. Classic ambient rumours from the courier's origin region (up to 3 deterministic picks).
        if (renderRumor is not null)
        {
            DaggerfallRumorDefinition[] originRumors = [.. definitions.Rumors.Entries
                .Where(rumor => rumor.Region == encounter.OriginRegion
                    && !rumor.IsQuestRumor
                    && rumor.QuestId == 0
                    && !rumor.IsSignMessage
                    && (rumor.TimeLimit <= 0 || rumor.TimeLimit > currentMinute))
                .OrderBy(rumor => rumor.Index)];
            if (originRumors.Length > 0)
            {
                DaggerfallSiteRecord rumorSite = encounter.OriginSite ?? activeSite;
                int start = checked((int)(unchecked((ulong)npc.DurableId) % (ulong)originRumors.Length));
                int take = Math.Min(3, originRumors.Length);
                for (int i = 0; i < take; i++)
                {
                    DaggerfallRumorDefinition rumor = originRumors[(start + i) % originRumors.Length];
                    string rendered = renderRumor(rumor, rumorSite);
                    if (!string.IsNullOrWhiteSpace(rendered))
                        AddUnique(rendered, new($"Heard in {encounter.OriginRegionName}: {rendered}", AheadOfRoadWave: false));
                }
            }
        }

        return items;
    }

    internal static string EmptyReply(
        DaggerfallRoadEncounterContext encounter,
        DaggerfallNpc npc,
        DaggerfallCourierDispatches? courierDispatches)
    {
        bool carriesUnrevealed = courierDispatches?.TryGetForCourier(npc.DurableId) is { Revealed: false };
        return carriesUnrevealed
            ? $"The roads out of {encounter.OriginRegionName} have been quiet — and my sealed pouch is not for idle talk."
            : $"The roads out of {encounter.OriginLocationName} in {encounter.OriginRegionName} have been quiet — no fresh word since I rode out.";
    }
}
