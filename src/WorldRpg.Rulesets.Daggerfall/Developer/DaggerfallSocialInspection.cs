using System.Text.Json;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Live developer inspection commands for the runtime social and political layers
/// (<c>social.site</c>, <c>social.region</c>, <c>social.player</c>).
/// Reads authoritative runtime owners on demand and owns no state of its own.
/// </summary>
internal sealed class DaggerfallSocialInspection(
    DaggerfallDefinitions definitions,
    DaggerfallSiteContext site,
    ActorsState actors,
    DaggerfallNpcRegistry npcs,
    DaggerfallActorLedger actorLedger,
    DaggerfallSocialState social,
    DaggerfallSocialConsequences socialConsequences,
    DaggerfallIntroductions introductions,
    DaggerfallOfficeHolders officeHolders,
    DaggerfallOfficeLedger officeLedger,
    DaggerfallCrimeState crime,
    DaggerfallNames names,
    Func<ulong> worldSocialSeed,
    Func<DaggerfallCalendar> calendar)
    : IDeveloperSocialCommands
{
    private static readonly (int Group, string Name)[] SocialGroups =
    [
        (0, "Commoners"),
        (1, "Merchants"),
        (2, "Scholars"),
        (3, "Nobility"),
        (4, "Underworld"),
    ];

    public DebugCommandResult Site()
    {
        DaggerfallSiteRecord? active = site.ActiveSite;
        int day = checked((int)calendar().DayNumber);
        ulong seed = worldSocialSeed();
        DaggerfallLocalHouseholdsSave localSnapshot = socialConsequences.Capture();
        Dictionary<string, DaggerfallLocalHouseholdStandingSave> localByFamily = localSnapshot.Standings
            .ToDictionary(s => s.FamilyId, StringComparer.Ordinal);

        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("day", day);
            w.WriteNumber("worldSocialSeed", seed);
            if (active is not null)
            {
                w.WriteStartObject("site");
                w.WriteNumber("region", active.Id.Region);
                w.WriteString("regionName", definitions.BuildingNames.RegionName(active.Id.Region));
                w.WriteNumber("index", active.Id.Index);
                w.WriteString("name", active.Name);
                w.WriteEndObject();
            }
            else
            {
                w.WriteNull("site");
            }

            Dictionary<string, List<(long Id, string Name, string Role)>> grouped = new(StringComparer.Ordinal);
            w.WriteStartArray("loadedPeople");
            foreach (ActorState actor in actors.All.OrderBy(a => a.DurableId))
            {
                if (actor.DurableId == actors.Player.DurableId)
                    continue;
                DaggerfallNpc? npc = npcs.TryGet(actor.DurableId, out DaggerfallNpc? reg) ? reg : null;
                if (npc is null && !actor.Actor.Has<ActorPersonComponent>())
                    continue;

                string? familyId = EffectiveFamilyId(actor, npc);
                string displayName = actor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person)
                    && !string.IsNullOrWhiteSpace(person.Snapshot.DisplayName)
                        ? person.Snapshot.DisplayName!
                        : npc?.DisplayName ?? npc?.Role ?? $"actor #{actor.DurableId}";
                string role = npc?.Role ?? "person";
                DaggerfallHouseholdScope scope = DaggerfallHouseholds.ScopeOf(familyId);

                w.WriteStartObject();
                w.WriteNumber("durableId", actor.DurableId);
                w.WriteString("name", displayName);
                w.WriteString("role", role);
                if (familyId is not null) w.WriteString("familyId", familyId); else w.WriteNull("familyId");
                w.WriteString("scope", ScopeSlug(scope));
                if (npc is not null)
                {
                    DaggerfallFactionReaction reaction = social.ReactionForNpc(npc);
                    w.WriteNumber("reactionTotal", reaction.Value);
                    w.WriteBoolean("inBloodFeud", introductions.IsInBloodFeud(npc, active));
                }
                w.WriteNumber("vouchCount", introductions.ReadVouches(actor.DurableId).Count);
                w.WriteBoolean("ledgerUntouched", !actorLedger.TryGet(actor.DurableId, out _));
                w.WriteEndObject();

                if (familyId is not null)
                {
                    if (!grouped.TryGetValue(familyId, out List<(long, string, string)>? members))
                    {
                        members = [];
                        grouped[familyId] = members;
                    }
                    members.Add((actor.DurableId, displayName, role));
                }
            }
            w.WriteEndArray();

            // Include any affected local households at the active site even if none of their members are loaded yet.
            foreach (DaggerfallLocalHouseholdStandingSave standing in localSnapshot.Standings)
            {
                if (active is null
                    || (standing.Region == active.Id.Region
                        && string.Equals(standing.Location, active.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!grouped.ContainsKey(standing.FamilyId))
                        grouped[standing.FamilyId] = [];
                }
            }

            w.WriteStartArray("households");
            foreach (string familyId in grouped.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                DaggerfallHouseholdScope scope = DaggerfallHouseholds.ScopeOf(familyId);
                string? label = FormatFamilyLabel(familyId, seed);
                int standingDelta = socialConsequences.HouseholdStandingDelta(familyId, active);
                bool feud = standingDelta <= DaggerfallIntroductions.BloodFeudDispositionThreshold;
                w.WriteStartObject();
                w.WriteString("familyId", familyId);
                if (label is not null) w.WriteString("label", label); else w.WriteNull("label");
                w.WriteString("scope", ScopeSlug(scope));
                w.WriteNumber("standingDelta", standingDelta);
                w.WriteBoolean("inBloodFeud", feud);
                if (localByFamily.TryGetValue(familyId, out DaggerfallLocalHouseholdStandingSave? localEntry)
                    && localEntry.SubjectOffsets is { Length: > 0 } offsets)
                {
                    w.WriteStartArray("subjectOffsets");
                    foreach (DaggerfallLocalHouseholdSubjectOffsetSave offset in offsets)
                    {
                        w.WriteStartObject();
                        w.WriteNumber("actorId", offset.ActorId);
                        w.WriteNumber("offset", offset.Offset);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                w.WriteStartArray("loadedMembers");
                foreach ((long id, string name, string role) in grouped[familyId])
                {
                    w.WriteStartObject();
                    w.WriteNumber("durableId", id);
                    w.WriteString("name", name);
                    w.WriteString("role", role);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("localStandings");
            foreach (DaggerfallLocalHouseholdStandingSave standing in localSnapshot.Standings)
            {
                if (active is not null
                    && (standing.Region != active.Id.Region
                        || !string.Equals(standing.Location, active.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                w.WriteStartObject();
                w.WriteString("familyId", standing.FamilyId);
                if (FormatFamilyLabel(standing.FamilyId, seed) is { } standingLabel)
                    w.WriteString("label", standingLabel);
                w.WriteNumber("region", standing.Region);
                w.WriteString("location", standing.Location);
                w.WriteNumber("standingDelta", standing.StandingDelta);
                w.WriteBoolean("inBloodFeud", standing.StandingDelta <= DaggerfallIntroductions.BloodFeudDispositionThreshold);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("localOffices");
            if (active is not null)
            {
                foreach (DaggerfallLocalOffice office in officeLedger.ResolveLocalOffices(active))
                {
                    w.WriteStartObject();
                    w.WriteString("officeKey", office.OfficeKey);
                    w.WriteString("kind", office.Kind.ToString());
                    w.WriteString("title", office.Title);
                    if (office.HolderActorId is long hid) w.WriteNumber("holderActorId", hid); else w.WriteNull("holderActorId");
                    w.WriteString("displayName", office.DisplayName);
                    if (!string.IsNullOrWhiteSpace(office.FamilyId)) w.WriteString("familyId", office.FamilyId); else w.WriteNull("familyId");
                    w.WriteBoolean("isAlive", office.IsAlive);
                    w.WriteBoolean("isVacant", office.IsVacant);
                    w.WriteBoolean("isOverridden", office.IsOverridden);
                    if (!string.IsNullOrWhiteSpace(office.PreviousHolderName)) w.WriteString("previousHolderName", office.PreviousHolderName);
                    if (office.ChangeCause is { } cause) w.WriteString("changeCause", cause.ToString());
                    if (!string.IsNullOrWhiteSpace(office.CauseDetail)) w.WriteString("causeDetail", office.CauseDetail);
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult Region()
    {
        int? region = site.Region;
        DaggerfallSiteRecord? active = site.ActiveSite;
        int day = checked((int)calendar().DayNumber);
        ulong seed = worldSocialSeed();

        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("day", day);
            if (region is int reg)
            {
                w.WriteNumber("region", reg);
                w.WriteString("regionName", definitions.BuildingNames.RegionName(reg));
            }
            else
            {
                w.WriteNull("region");
                w.WriteNull("regionName");
            }

            if (region is int rulerRegion && officeHolders.ResolveRegionalRuler(rulerRegion) is { } ruler)
            {
                w.WriteStartObject("ruler");
                if (ruler.ActorId is long aid) w.WriteNumber("actorId", aid); else w.WriteNull("actorId");
                if (!string.IsNullOrWhiteSpace(ruler.CastId)) w.WriteString("castId", ruler.CastId); else w.WriteNull("castId");
                w.WriteString("displayName", ruler.DisplayName);
                if (!string.IsNullOrWhiteSpace(ruler.Title)) w.WriteString("title", ruler.Title); else w.WriteNull("title");
                w.WriteNumber("officeFactionId", ruler.OfficeFactionId);
                if (ruler.HolderFactionId is int hfid) w.WriteNumber("holderFactionId", hfid); else w.WriteNull("holderFactionId");
                if (!string.IsNullOrWhiteSpace(ruler.FamilyId)) w.WriteString("familyId", ruler.FamilyId); else w.WriteNull("familyId");
                w.WriteBoolean("hasLivingHolder", ruler.HasLivingHolder);
                if (!string.IsNullOrWhiteSpace(ruler.PreviousHolderName)) w.WriteString("previousHolderName", ruler.PreviousHolderName);
                if (!string.IsNullOrWhiteSpace(ruler.PreviousHolderFate)) w.WriteString("previousHolderFate", ruler.PreviousHolderFate);
                w.WriteEndObject();
            }
            else
            {
                w.WriteNull("ruler");
            }

            w.WriteStartArray("greatHouses");
            if (region is int houseRegion)
            {
                foreach (DaggerfallRegionalGreatHouse house in officeLedger.ResolveRegionalGreatHouses(houseRegion))
                {
                    int arrivedStanding = socialConsequences.HouseholdStandingDelta(house.FamilyId, active);
                    w.WriteStartObject();
                    w.WriteString("familyId", house.FamilyId);
                    if (FormatFamilyLabel(house.FamilyId, seed) is { } houseLabel) w.WriteString("label", houseLabel); else w.WriteNull("label");
                    w.WriteNumber("houseIndex", house.HouseIndex);
                    w.WriteNumber("arrivedStandingDelta", arrivedStanding);
                    w.WriteBoolean("inBloodFeud", arrivedStanding <= DaggerfallIntroductions.BloodFeudDispositionThreshold);
                    w.WriteStartArray("heldOffices");
                    foreach (DaggerfallLocalOffice office in house.HeldOffices)
                    {
                        w.WriteStartObject();
                        w.WriteString("officeKey", office.OfficeKey);
                        w.WriteString("siteId", $"{office.SiteId.Region}/{office.SiteId.Index}");
                        if (site.TryFind(office.SiteId, out DaggerfallSiteRecord officeSite))
                            w.WriteString("siteName", officeSite.Name);
                        w.WriteString("title", office.Title);
                        if (office.HolderActorId is long hid) w.WriteNumber("holderActorId", hid); else w.WriteNull("holderActorId");
                        w.WriteString("displayName", office.DisplayName);
                        w.WriteBoolean("isAlive", office.IsAlive);
                        w.WriteBoolean("isVacant", office.IsVacant);
                        w.WriteBoolean("isOverridden", office.IsOverridden);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();

            w.WriteStartArray("boardSeats");
            foreach (DaggerfallSeatLeaderPersona seat in officeLedger.ResolveAllBoardSeatPersonas())
            {
                w.WriteStartObject();
                w.WriteString("seatId", seat.SeatId);
                w.WriteString("seatName", seat.SeatName);
                w.WriteString("kind", seat.Kind.ToString());
                w.WriteNumber("officeFactionId", seat.OfficeFactionId);
                if (seat.HolderActorId is long hid) w.WriteNumber("holderActorId", hid); else w.WriteNull("holderActorId");
                w.WriteString("displayName", seat.DisplayName);
                if (!string.IsNullOrWhiteSpace(seat.Title)) w.WriteString("title", seat.Title); else w.WriteNull("title");
                w.WriteBoolean("isVacant", seat.IsVacant);
                w.WriteBoolean("isOverridden", seat.IsOverridden);
                if (!string.IsNullOrWhiteSpace(seat.PreviousHolderName)) w.WriteString("previousHolderName", seat.PreviousHolderName);
                if (seat.ChangeCause is { } cause) w.WriteString("changeCause", cause.ToString());
                if (!string.IsNullOrWhiteSpace(seat.CauseDetail)) w.WriteString("causeDetail", seat.CauseDetail);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("officeOverrides");
            foreach (DaggerfallOfficeOverrideSave over in officeLedger.Overrides.OrderBy(o => o.OfficeKey, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WriteString("officeKey", over.OfficeKey);
                w.WriteString("kind", over.Kind.ToString());
                if (!string.IsNullOrWhiteSpace(over.SeatId)) w.WriteString("seatId", over.SeatId);
                if (over.HolderActorId is long hid) w.WriteNumber("holderActorId", hid); else w.WriteNull("holderActorId");
                if (!string.IsNullOrWhiteSpace(over.HolderDisplayName)) w.WriteString("holderDisplayName", over.HolderDisplayName);
                w.WriteBoolean("vacant", over.Vacant);
                w.WriteString("cause", over.Cause.ToString());
                if (!string.IsNullOrWhiteSpace(over.CauseDetail)) w.WriteString("causeDetail", over.CauseDetail);
                w.WriteNumber("changedDay", over.ChangedDay);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult Player()
    {
        int? activeRegion = site.Region;
        int day = checked((int)calendar().DayNumber);
        DaggerfallSocialSave socialSave = social.Capture();

        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("day", day);
            w.WriteNumber("actorLedgerCount", actorLedger.Count);
            if (activeRegion is int reg)
            {
                int rep = social.RegionalReputation(reg);
                w.WriteNumber("activeRegion", reg);
                w.WriteString("activeRegionName", definitions.BuildingNames.RegionName(reg));
                w.WriteNumber("activeRegionReputation", rep);
                w.WriteBoolean("wantedInActiveRegion", rep < DaggerfallLegalServicePolicy.WantedReputationThreshold);
                w.WriteBoolean("banishedInActiveRegion", crime.BanishedRegions.Contains(reg));
            }
            else
            {
                w.WriteNull("activeRegion");
                w.WriteNull("activeRegionName");
                w.WriteNumber("activeRegionReputation", 0);
                w.WriteBoolean("wantedInActiveRegion", false);
                w.WriteBoolean("banishedInActiveRegion", false);
            }

            w.WriteStartObject("personalReputations");
            foreach ((int group, string name) in SocialGroups)
                w.WriteNumber(name, social.PersonalReputation(group));
            w.WriteEndObject();

            w.WriteStartArray("regionalReputations");
            foreach (DaggerfallRegionalReputationSave entry in socialSave.Regions)
            {
                bool banished = crime.BanishedRegions.Contains(entry.Region);
                bool wanted = entry.Value < DaggerfallLegalServicePolicy.WantedReputationThreshold;
                if (entry.Value == 0 && !banished && entry.Region != activeRegion)
                    continue;
                w.WriteStartObject();
                w.WriteNumber("region", entry.Region);
                w.WriteString("regionName", definitions.BuildingNames.RegionName(entry.Region));
                w.WriteNumber("reputation", entry.Value);
                w.WriteBoolean("wanted", wanted);
                w.WriteBoolean("banished", banished);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("factionStandings");
            foreach (DaggerfallFactionReputationSave entry in socialSave.Factions)
            {
                if (entry.Value == 0)
                    continue;
                string name = definitions.Factions.Factions.TryGetValue(entry.FactionId, out DaggerfallFactionDefinition? def)
                    ? def.Name
                    : $"Faction #{entry.FactionId}";
                w.WriteStartObject();
                w.WriteNumber("factionId", entry.FactionId);
                w.WriteString("name", name);
                w.WriteNumber("reputation", entry.Value);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("affiliations");
            foreach (DaggerfallSocialAffiliationView aff in social.ReadAffiliations())
            {
                w.WriteStartObject();
                w.WriteNumber("factionId", aff.FactionId);
                w.WriteString("faction", aff.Faction);
                w.WriteString("guildGroup", aff.GuildGroup);
                w.WriteNumber("rank", aff.Rank);
                w.WriteNumber("reputation", aff.Reputation);
                w.WriteNumber("recognition", aff.Recognition);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartObject("law");
            w.WriteStartArray("banishedRegions");
            foreach (int r in crime.BanishedRegions.OrderBy(v => v))
                w.WriteNumberValue(r);
            w.WriteEndArray();
            w.WriteNumber("incidentCount", crime.Incidents.Count);
            w.WriteNumber("reportedIncidentCount", crime.Incidents.Count(i => i.Reported));
            w.WriteNumber("attemptCount", crime.Attempts.Count);
            w.WriteNumber("thievingTally", crime.ThievingRequirementTally);
            w.WriteNumber("murderTally", crime.MurderRequirementTally);
            w.WriteStartArray("recentIncidents");
            foreach (DaggerfallCrimeIncidentSave inc in crime.Incidents.TakeLast(10).Reverse())
            {
                w.WriteStartObject();
                w.WriteString("operationId", inc.OperationId);
                w.WriteString("crime", inc.Crime.ToString());
                w.WriteNumber("region", inc.Region);
                w.WriteBoolean("reported", inc.Reported);
                if (inc.AffectedActorOrOwnerId is long aid) w.WriteNumber("affectedActorOrOwnerId", aid);
                w.WriteNumber("gameMinute", inc.GameMinute);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartArray("vouches");
            HashSet<long> targetIds = [];
            foreach (ActorLedgerEntry entry in actorLedger.All)
            {
                if (actorLedger.GetVouches(entry.DurableId).Count > 0)
                    targetIds.Add(entry.DurableId);
            }
            foreach (ActorState live in actors.All)
            {
                if (live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov) && ov.Vouches.Count > 0)
                    targetIds.Add(live.DurableId);
            }
            foreach (long targetId in targetIds.OrderBy(id => id))
            {
                string? targetName = npcs.TryGet(targetId, out DaggerfallNpc? targetNpc) && !string.IsNullOrWhiteSpace(targetNpc?.DisplayName)
                    ? targetNpc.DisplayName
                    : actorLedger.TryGet(targetId, out ActorLedgerEntry? targetEntry)
                        ? targetEntry?.Social?.DisplayNameOverride ?? targetEntry?.Referral?.DisplayNameOverride
                        : null;
                foreach (ActorVouchRecord vouch in introductions.ReadVouches(targetId))
                {
                    w.WriteStartObject();
                    w.WriteNumber("targetDurableId", targetId);
                    if (!string.IsNullOrWhiteSpace(targetName)) w.WriteString("targetName", targetName); else w.WriteNull("targetName");
                    w.WriteNumber("referrerDurableId", vouch.ReferrerDurableId);
                    if (!string.IsNullOrWhiteSpace(vouch.ReferrerName)) w.WriteString("referrerName", vouch.ReferrerName); else w.WriteNull("referrerName");
                    if (!string.IsNullOrWhiteSpace(vouch.ReferrerFamilyId)) w.WriteString("referrerFamilyId", vouch.ReferrerFamilyId); else w.WriteNull("referrerFamilyId");
                    w.WriteString("kind", vouch.Kind.ToString());
                    w.WriteNumber("issuedDay", vouch.IssuedDay);
                    if (!string.IsNullOrWhiteSpace(vouch.OriginSiteName)) w.WriteString("originSiteName", vouch.OriginSiteName); else w.WriteNull("originSiteName");
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    private static string? EffectiveFamilyId(ActorState actor, DaggerfallNpc? npc)
    {
        if (actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay))
            return !string.IsNullOrWhiteSpace(overlay.FamilyId) ? overlay.FamilyId : null;
        if (actor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person)
            && !string.IsNullOrWhiteSpace(person.Snapshot.FamilyId))
        {
            return person.Snapshot.FamilyId;
        }
        return npc is not null && !string.IsNullOrWhiteSpace(npc.FamilyId) ? npc.FamilyId : null;
    }

    private string? FormatFamilyLabel(string? familyId, ulong seed) =>
        DaggerfallHouseholds.FormatLabel(
            familyId,
            definitions,
            names.Surname,
            (bank, id) => names.FirstName(bank, false, id),
            seed);

    private static string ScopeSlug(DaggerfallHouseholdScope scope) => scope switch
    {
        DaggerfallHouseholdScope.Dynasty => "dynasty",
        DaggerfallHouseholdScope.GreatHouse => "great-house",
        DaggerfallHouseholdScope.Local => "local",
        _ => "none",
    };
}
