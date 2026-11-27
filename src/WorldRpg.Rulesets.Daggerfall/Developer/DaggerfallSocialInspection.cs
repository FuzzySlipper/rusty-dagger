using System.Globalization;
using System.Text.Json;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Live developer inspection and state-changing commands for the runtime social, schedule, and political layers
/// (<c>social.site</c>, <c>social.region</c>, <c>social.player</c>, <c>social.schedule</c>,
/// <c>social.office.vacate</c>, <c>social.household.standing</c>, <c>social.vouch.grant</c>,
/// <c>social.vouch.revoke</c>).
/// Reads and mutates authoritative runtime owners on demand and owns no state of its own.
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
    Func<DaggerfallCalendar> calendar,
    Func<DaggerfallSiteId, DaggerfallTownShelterAssignment>? resolveTownShelter = null,
    Func<DaggerfallSiteId, DaggerfallPopulationPlacement, long, int, DaggerfallScheduleDecision>? evaluateExteriorSchedule = null,
    Func<DaggerfallSiteId, IReadOnlyList<DaggerfallPopulationPlacement>>? exteriorPlacements = null,
    Func<(DaggerfallWeatherKind Weather, DaggerfallWeatherShelterElapsed Elapsed)>? scheduleWeather = null)
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
                if (!DaggerfallHouseholds.IsLocal(standing.FamilyId))
                    continue;
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
                bool hasSnapshot = socialConsequences.TryGetHouseholdSnapshot(familyId, out DaggerfallHouseholdSnapshot snapshot);
                bool feud = socialConsequences.IsFeudActiveAt(familyId, active);
                w.WriteStartObject();
                w.WriteString("familyId", familyId);
                if (label is not null) w.WriteString("label", label); else w.WriteNull("label");
                w.WriteString("scope", ScopeSlug(scope));
                w.WriteNumber("standingDelta", standingDelta);
                w.WriteNumber("persistentStandingDelta", hasSnapshot ? snapshot.StandingDelta : 0);
                w.WriteBoolean("inBloodFeud", feud);
                w.WriteBoolean("feudActive", hasSnapshot && snapshot.FeudActive);
                if (hasSnapshot && snapshot.MourningUntilDay is int mourningUntil)
                    w.WriteNumber("mourningUntilDay", mourningUntil);
                else
                    w.WriteNull("mourningUntilDay");
                w.WriteNumber("lastUpdatedDay", hasSnapshot ? snapshot.LastUpdatedDay : 0);
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
                if (!DaggerfallHouseholds.IsLocal(standing.FamilyId))
                    continue;
                if (active is not null
                    && (standing.Region != active.Id.Region
                        || !string.Equals(standing.Location, active.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                bool feudActive = standing.FeudActive || standing.StandingDelta <= DaggerfallIntroductions.BloodFeudDispositionThreshold;
                w.WriteStartObject();
                w.WriteString("familyId", standing.FamilyId);
                if (FormatFamilyLabel(standing.FamilyId, seed) is { } standingLabel)
                    w.WriteString("label", standingLabel);
                w.WriteNumber("region", standing.Region);
                w.WriteString("location", standing.Location);
                w.WriteNumber("standingDelta", standing.StandingDelta);
                w.WriteNumber("persistentStandingDelta", standing.StandingDelta);
                w.WriteBoolean("inBloodFeud", feudActive);
                w.WriteBoolean("feudActive", feudActive);
                if (standing.MourningUntilDay is int localMourningUntil)
                    w.WriteNumber("mourningUntilDay", localMourningUntil);
                else
                    w.WriteNull("mourningUntilDay");
                w.WriteNumber("lastUpdatedDay", standing.LastUpdatedDay);
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
                    if (office.VacancyUntilMinute is long localVacancyUntil) w.WriteNumber("vacancyUntilMinute", localVacancyUntil);
                    if (office.IsRegency) w.WriteBoolean("isRegency", true);
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
                if (!string.IsNullOrWhiteSpace(ruler.FamilyId))
                {
                    w.WriteString("familyId", ruler.FamilyId);
                    bool hasRulerSnap = socialConsequences.TryGetHouseholdSnapshot(ruler.FamilyId, out DaggerfallHouseholdSnapshot rulerSnap);
                    int rulerArrived = socialConsequences.HouseholdStandingDelta(ruler.FamilyId, active);
                    w.WriteNumber("arrivedStandingDelta", rulerArrived);
                    w.WriteNumber("persistentStandingDelta", hasRulerSnap ? rulerSnap.StandingDelta : 0);
                    w.WriteBoolean("inBloodFeud", socialConsequences.IsFeudActiveAt(ruler.FamilyId, active));
                    w.WriteBoolean("feudActive", hasRulerSnap && rulerSnap.FeudActive);
                    if (hasRulerSnap && rulerSnap.MourningUntilDay is int rulerMourning)
                        w.WriteNumber("mourningUntilDay", rulerMourning);
                    else
                        w.WriteNull("mourningUntilDay");
                    w.WriteNumber("lastUpdatedDay", hasRulerSnap ? rulerSnap.LastUpdatedDay : 0);
                }
                else w.WriteNull("familyId");
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
                    bool hasHouseSnap = socialConsequences.TryGetHouseholdSnapshot(house.FamilyId, out DaggerfallHouseholdSnapshot houseSnap);
                    bool feudKnown = socialConsequences.IsFeudActiveAt(house.FamilyId, active);
                    w.WriteStartObject();
                    w.WriteString("familyId", house.FamilyId);
                    if (FormatFamilyLabel(house.FamilyId, seed) is { } houseLabel) w.WriteString("label", houseLabel); else w.WriteNull("label");
                    w.WriteNumber("houseIndex", house.HouseIndex);
                    w.WriteNumber("arrivedStandingDelta", arrivedStanding);
                    w.WriteNumber("persistentStandingDelta", hasHouseSnap ? houseSnap.StandingDelta : 0);
                    w.WriteBoolean("inBloodFeud", feudKnown);
                    w.WriteBoolean("feudActive", hasHouseSnap && houseSnap.FeudActive);
                    if (hasHouseSnap && houseSnap.MourningUntilDay is int houseMourning)
                        w.WriteNumber("mourningUntilDay", houseMourning);
                    else
                        w.WriteNull("mourningUntilDay");
                    w.WriteNumber("lastUpdatedDay", hasHouseSnap ? houseSnap.LastUpdatedDay : 0);
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
                if (seat.VacancyUntilMinute is long seatVacancyUntil) w.WriteNumber("vacancyUntilMinute", seatVacancyUntil);
                if (seat.IsRegency) w.WriteBoolean("isRegency", true);
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
                if (over.IsRegency) w.WriteBoolean("isRegency", true);
                if (over.VacancyOpenedMinute is long openedMin) w.WriteNumber("vacancyOpenedMinute", openedMin);
                if (over.VacancyUntilMinute is long untilMin) w.WriteNumber("vacancyUntilMinute", untilMin);
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

            List<DaggerfallCrimeIncidentSave> pendingCharges =
            [
                .. crime.Incidents
                    .Where(i => i.Disposition is null
                        && (i.Reported || i.Witnesses.Query == DaggerfallCrimeWitnessQuery.CompletedWithWitnesses))
                    .OrderBy(i => i.Region)
                    .ThenBy(i => i.GameMinute)
                    .ThenBy(i => i.OperationId, StringComparer.Ordinal),
            ];
            Dictionary<int, int> pendingByRegion = pendingCharges
                .GroupBy(i => i.Region)
                .ToDictionary(g => g.Key, g => g.Count());
            HashSet<int> includeRegions = [];
            if (activeRegion is int ar) includeRegions.Add(ar);
            foreach (int br in crime.BanishedRegions) includeRegions.Add(br);
            foreach (DaggerfallLegalResponseSave resp in crime.LegalResponses) includeRegions.Add(resp.Region);
            foreach (int pr in pendingByRegion.Keys) includeRegions.Add(pr);

            w.WriteStartArray("regionalReputations");
            foreach ((int region, int effectiveRep) in social.ReadRegionalReputations(includeRegions))
            {
                bool banished = crime.BanishedRegions.Contains(region);
                bool wanted = effectiveRep < DaggerfallLegalServicePolicy.WantedReputationThreshold;
                int pendingCount = pendingByRegion.GetValueOrDefault(region);
                bool hasResponse = crime.Response(region) is not null;
                bool hasActiveBounty = banished || wanted || pendingCount > 0 || hasResponse;
                if (effectiveRep == 0 && !hasActiveBounty && region != activeRegion)
                    continue;
                w.WriteStartObject();
                w.WriteNumber("region", region);
                w.WriteString("regionName", definitions.BuildingNames.RegionName(region));
                w.WriteNumber("reputation", effectiveRep);
                w.WriteBoolean("wanted", wanted);
                w.WriteBoolean("banished", banished);
                w.WriteNumber("pendingChargeCount", pendingCount);
                w.WriteBoolean("hasActiveBounty", hasActiveBounty);
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
            w.WriteNumber("pendingChargeCount", pendingCharges.Count);
            w.WriteStartArray("pendingCharges");
            foreach (DaggerfallCrimeIncidentSave charge in pendingCharges)
            {
                w.WriteStartObject();
                w.WriteString("operationId", charge.OperationId);
                w.WriteString("crime", charge.Crime.ToString());
                w.WriteNumber("region", charge.Region);
                w.WriteString("regionName", definitions.BuildingNames.RegionName(charge.Region));
                w.WriteBoolean("reported", charge.Reported);
                if (charge.AffectedActorOrOwnerId is long aid) w.WriteNumber("affectedActorOrOwnerId", aid);
                w.WriteNumber("gameMinute", charge.GameMinute);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("legalResponses");
            foreach (DaggerfallLegalResponseSave response in crime.LegalResponses.OrderBy(r => r.Region))
            {
                w.WriteStartObject();
                w.WriteString("id", response.Id);
                w.WriteNumber("region", response.Region);
                w.WriteString("regionName", definitions.BuildingNames.RegionName(response.Region));
                w.WriteString("phase", response.Phase.ToString());
                w.WriteNumber("chargeCount", response.Charges.Length);
                w.WriteStartArray("charges");
                foreach (string chargeId in response.Charges) w.WriteStringValue(chargeId);
                w.WriteEndArray();
                w.WriteNumber("guardCount", response.Guards.Length);
                if (response.Penalty is { } penalty)
                {
                    w.WriteNumber("fine", penalty.Fine);
                    w.WriteNumber("prisonDays", penalty.PrisonDays);
                }
                if (response.Sentence is { } sentence)
                    w.WriteString("sentenceOutcome", sentence.Outcome.ToString());
                w.WriteNumber("prisonSecondsRemaining", response.PrisonSecondsRemaining);
                w.WriteEndObject();
            }
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
                if (inc.Disposition is { } disp)
                {
                    w.WriteStartObject("disposition");
                    w.WriteString("outcome", disp.Outcome.ToString());
                    w.WriteNumber("resolvedMinute", disp.ResolvedMinute);
                    w.WriteEndObject();
                }
                else
                {
                    w.WriteNull("disposition");
                }
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
                    DaggerfallVouchEvaluation eval = introductions.EvaluateVouch(vouch, site.ActiveSite);
                    w.WriteStartObject();
                    w.WriteNumber("targetDurableId", targetId);
                    if (!string.IsNullOrWhiteSpace(targetName)) w.WriteString("targetName", targetName); else w.WriteNull("targetName");
                    w.WriteNumber("referrerDurableId", vouch.ReferrerDurableId);
                    if (!string.IsNullOrWhiteSpace(vouch.ReferrerName)) w.WriteString("referrerName", vouch.ReferrerName); else w.WriteNull("referrerName");
                    if (!string.IsNullOrWhiteSpace(vouch.ReferrerFamilyId)) w.WriteString("referrerFamilyId", vouch.ReferrerFamilyId); else w.WriteNull("referrerFamilyId");
                    w.WriteString("kind", vouch.Kind.ToString());
                    w.WriteNumber("issuedDay", vouch.IssuedDay);
                    w.WriteString("status", eval.Status.ToString());
                    w.WriteBoolean("usable", eval.IsUsable);
                    w.WriteNumber("ageDays", eval.AgeDays);
                    if (eval.DaysUntilLapse is int remaining) w.WriteNumber("daysUntilLapse", remaining); else w.WriteNull("daysUntilLapse");
                    if (!string.IsNullOrWhiteSpace(eval.Reason)) w.WriteString("reason", eval.Reason); else w.WriteNull("reason");
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

    public DebugCommandResult Schedule()
    {
        if (site.ActiveSite is not { } active)
            return DaggerfallDeveloperReplies.Refused("No active site is loaded.");

        DaggerfallCalendar cal = calendar();
        int day = checked((int)cal.DayNumber);
        int minuteOfDay = checked(cal.Hour * DaggerfallCalendar.MinutesPerHour + cal.Minute);
        (DaggerfallWeatherKind weather, DaggerfallWeatherShelterElapsed elapsed) = scheduleWeather is not null
            ? scheduleWeather()
            : (DaggerfallWeatherKind.Sunny, new DaggerfallWeatherShelterElapsed(0, 0));

        DaggerfallTownShelterAssignment assignment = resolveTownShelter is not null
            ? resolveTownShelter(active.Id)
            : DaggerfallTownShelterAssignment.Empty;
        IReadOnlyList<DaggerfallPopulationPlacement> placements = exteriorPlacements is not null
            ? exteriorPlacements(active.Id)
            : [];
        int totalPopCount = placements.Count(p => p.CrackdownGuard == 0);
        DaggerfallWorldProfileKey exteriorKey = DaggerfallWorldProfileIds.Exterior(active.Id);
        Dictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource> buildingsById = active.Exterior is { Buildings.Count: > 0 } ext
            ? ext.Buildings.Values.ToDictionary(b => b.Id)
            : [];

        List<(long DurableId, string Name, string SlotId, string Role, string Source, DaggerfallScheduleDecision Decision, DaggerfallSiteBuildingId? HomeBuilding, DaggerfallSiteBuildingId? ShelterTarget, bool OffStageOverflow, bool TavernOverflowed)> rows = [];
        HashSet<long> seenIds = [];

        foreach (DaggerfallPopulationPlacement placement in placements)
        {
            string slotKey = $"{exteriorKey.LogicalId}/{placement.Id}";
            long durableId = DaggerfallPeopleSlots.SeededSlotIdentity(active.Id, slotKey);
            if (!seenIds.Add(durableId))
                continue;
            DaggerfallScheduleDecision decision = evaluateExteriorSchedule is not null
                ? evaluateExteriorSchedule(active.Id, placement, durableId, totalPopCount)
                : DaggerfallNpcSchedulePolicy.EvaluateExteriorPlacement(
                    worldSocialSeed(), active.Id, placement.Id, placement.IsGuard, totalPopCount,
                    definitions.ActorCatalog, minuteOfDay, weather,
                    weatherElapsedMinutes: elapsed.PrecipElapsedMinutes,
                    severeWeatherElapsedMinutes: elapsed.SevereElapsedMinutes);

            DaggerfallSiteBuildingId? shelterBuilding = assignment.AssignedBuildingByActor.TryGetValue(durableId, out DaggerfallSiteBuildingId assigned)
                ? assigned
                : null;
            bool overflow = assignment.OffStageOverflowActorIds.Contains(durableId);
            DaggerfallNpc? npc = npcs.TryGet(durableId, out DaggerfallNpc? reg) ? reg : null;
            string name = npc?.DisplayName ?? (placement.IsGuard ? "City Watch" : $"walker #{durableId}");
            string role = npc?.Role ?? (placement.IsGuard ? "guard" : "civilian");

            rows.Add((durableId, name, placement.Id, role, "exterior-walker", decision, HomeBuilding: null, shelterBuilding, overflow, TavernOverflowed: false));
        }

        Dictionary<DaggerfallSiteBuildingId, int> residentCountsByBuilding = [];
        foreach (DaggerfallScheduledInteriorResident resident in assignment.InteriorResidents)
        {
            residentCountsByBuilding[resident.HomeBuildingId] = residentCountsByBuilding.GetValueOrDefault(resident.HomeBuildingId) + 1;
            if (!seenIds.Add(resident.DurableId))
                continue;
            string name = (npcs.TryGet(resident.DurableId, out DaggerfallNpc? reg) ? reg?.DisplayName : null)
                ?? resident.DisplayName
                ?? $"{resident.Role} #{resident.DurableId}";
            DaggerfallSiteBuildingId shelterTarget = resident.VisitingBuildingId ?? resident.HomeBuildingId;
            rows.Add((
                resident.DurableId,
                name,
                resident.HomeSlotId,
                resident.Role,
                "interior-static",
                resident.Decision,
                resident.HomeBuildingId,
                shelterTarget,
                OffStageOverflow: false,
                resident.TavernOverflowed));
        }

        foreach ((DaggerfallSiteBuildingId buildingId, IReadOnlyList<DaggerfallScheduledVisitor> visitors) in assignment.VisitorsByBuilding
            .OrderBy(pair => pair.Key.BlockY)
            .ThenBy(pair => pair.Key.BlockX)
            .ThenBy(pair => pair.Key.Index))
        {
            foreach (DaggerfallScheduledVisitor visitor in visitors)
            {
                if (!seenIds.Add(visitor.DurableId))
                    continue;
                string name = visitor.DisplayName
                    ?? (npcs.TryGet(visitor.DurableId, out DaggerfallNpc? reg) ? reg?.DisplayName : null)
                    ?? $"npc #{visitor.DurableId}";
                rows.Add((
                    visitor.DurableId,
                    name,
                    visitor.HomeSlotId,
                    visitor.Role,
                    visitor.IsExteriorWalker ? "exterior-walker" : "interior-static",
                    visitor.Decision,
                    HomeBuilding: null,
                    buildingId,
                    OffStageOverflow: false,
                    TavernOverflowed: false));
            }
        }

        int outdoorsCount = 0;
        int shelteredInBuildingCount = 0;
        int shelteredInHomeCount = 0;
        int offStageOverflowCount = 0;
        int interiorResidentCount = 0;
        foreach (var row in rows)
        {
            if (row.Source == "interior-static")
                interiorResidentCount++;

            if (row.Decision.Placement == DaggerfallSchedulePlacement.ExteriorStreet)
                outdoorsCount++;
            else if (row.OffStageOverflow)
                offStageOverflowCount++;
            else if (row.Source == "interior-static")
            {
                if (row.TavernOverflowed || row.Decision.Placement == DaggerfallSchedulePlacement.ShelteredInHome)
                    shelteredInHomeCount++;
                else
                    shelteredInBuildingCount++;
            }
            else if (row.ShelterTarget is not null)
                shelteredInBuildingCount++;
            else
                shelteredInHomeCount++;
        }

        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("day", day);
            w.WriteNumber("hour", cal.Hour);
            w.WriteNumber("minute", cal.Minute);
            w.WriteNumber("minuteOfDay", minuteOfDay);
            w.WriteString("weather", weather.ToString());
            w.WriteNumber("precipElapsedMinutes", elapsed.PrecipElapsedMinutes);
            w.WriteNumber("severeElapsedMinutes", elapsed.SevereElapsedMinutes);

            w.WriteStartObject("site");
            w.WriteNumber("region", active.Id.Region);
            w.WriteString("regionName", definitions.BuildingNames.RegionName(active.Id.Region));
            w.WriteNumber("index", active.Id.Index);
            w.WriteString("name", active.Name);
            w.WriteEndObject();

            w.WriteStartObject("summary");
            w.WriteNumber("totalNpcCount", rows.Count);
            w.WriteNumber("interiorResidentCount", interiorResidentCount);
            w.WriteNumber("outdoorsCount", outdoorsCount);
            w.WriteNumber("shelteredInBuildingCount", shelteredInBuildingCount);
            w.WriteNumber("shelteredInHomeCount", shelteredInHomeCount);
            w.WriteNumber("offStageOverflowCount", offStageOverflowCount);
            w.WriteEndObject();

            w.WriteStartArray("buildings");
            foreach (DaggerfallSiteBuildingSource building in buildingsById.Values
                .OrderBy(b => b.Id.BlockY)
                .ThenBy(b => b.Id.BlockX)
                .ThenBy(b => b.Id.Index))
            {
                int capacity = DaggerfallScheduleShelterResolver.DefaultCapacityForBuilding(building);
                int visitorCount = assignment.VisitorsFor(building.Id).Count;
                int suppressedCount = assignment.SuppressedSlotsFor(building.Id).Count;
                int residentCount = residentCountsByBuilding.GetValueOrDefault(building.Id);
                if (capacity <= 0 && visitorCount == 0 && suppressedCount == 0 && residentCount == 0)
                    continue;
                w.WriteStartObject();
                w.WriteNumber("blockX", building.Id.BlockX);
                w.WriteNumber("blockY", building.Id.BlockY);
                w.WriteNumber("buildingIndex", building.Id.Index);
                w.WriteNumber("buildingType", building.Source.BuildingType);
                w.WriteNumber("capacity", capacity);
                w.WriteNumber("residentCount", residentCount);
                w.WriteNumber("visitorCount", visitorCount);
                w.WriteNumber("suppressedStaticCount", suppressedCount);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("npcs");
            foreach (var row in rows.OrderBy(r => r.DurableId))
            {
                w.WriteStartObject();
                w.WriteNumber("durableId", row.DurableId);
                w.WriteString("name", row.Name);
                w.WriteString("slotId", row.SlotId);
                w.WriteString("role", row.Role);
                w.WriteString("source", row.Source);
                w.WriteString("scheduleId", row.Decision.ScheduleId);
                w.WriteString("placement", row.Decision.Placement.ToString());
                w.WriteBoolean("outdoors", row.Decision.Placement == DaggerfallSchedulePlacement.ExteriorStreet);
                w.WriteString("reason", row.Decision.Reason);
                w.WriteBoolean("pinned", row.Decision.Pinned);
                w.WriteNumber("jitterMinutes", row.Decision.JitterMinutes);
                w.WriteNumber("stormDelayMinutes", row.Decision.StormDelayMinutes);
                w.WriteString("buildingTarget", row.Decision.BuildingTarget.ToString());
                w.WriteBoolean("offStageOverflow", row.OffStageOverflow);
                w.WriteBoolean("tavernOverflowed", row.TavernOverflowed);
                if (row.HomeBuilding is DaggerfallSiteBuildingId homeId)
                {
                    w.WriteStartObject("homeBuilding");
                    w.WriteNumber("blockX", homeId.BlockX);
                    w.WriteNumber("blockY", homeId.BlockY);
                    w.WriteNumber("buildingIndex", homeId.Index);
                    if (buildingsById.TryGetValue(homeId, out DaggerfallSiteBuildingSource? homeBuilding))
                        w.WriteNumber("buildingType", homeBuilding.Source.BuildingType);
                    w.WriteEndObject();
                }
                else
                {
                    w.WriteNull("homeBuilding");
                }
                if (row.ShelterTarget is DaggerfallSiteBuildingId targetId)
                {
                    w.WriteStartObject("shelterTarget");
                    w.WriteNumber("blockX", targetId.BlockX);
                    w.WriteNumber("blockY", targetId.BlockY);
                    w.WriteNumber("buildingIndex", targetId.Index);
                    if (buildingsById.TryGetValue(targetId, out DaggerfallSiteBuildingSource? targetBuilding))
                        w.WriteNumber("buildingType", targetBuilding.Source.BuildingType);
                    w.WriteEndObject();
                }
                else
                {
                    w.WriteNull("shelterTarget");
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult VacateOffice(string office, string cause)
    {
        if (string.IsNullOrWhiteSpace(office))
            return DaggerfallDeveloperReplies.Refused("OFFICE is required ('ruler', an exact officeKey, 'faction:<id>', or a holder durableId).");
        if (!TryParseOfficeCause(cause, out DaggerfallOfficeChangeCause parsedCause))
            return DaggerfallDeveloperReplies.Refused("CAUSE must be one of: death, card, deposed, quest, law, succession.");

        DaggerfallCalendar cal = calendar();
        int day = checked((int)cal.DayNumber);
        long minute = cal.MinuteNumber;
        string trimmed = office.Trim();
        bool lethal = parsedCause == DaggerfallOfficeChangeCause.Death;
        List<DaggerfallOfficeOverrideSave> vacated = [];

        if (trimmed.Equals("ruler", StringComparison.OrdinalIgnoreCase))
        {
            if (site.Region is not int reg)
                return DaggerfallDeveloperReplies.Refused("No active region is loaded.");
            if (lethal)
            {
                DaggerfallFactionDefinition? province = definitions.Factions.ProvinceOf(reg);
                if (province is null)
                    return DaggerfallDeveloperReplies.Refused($"Region {reg} has no province throne office.");
                vacated.Add(officeLedger.VacateFactionOffice(province.Id, parsedCause, day, "slain (developer)", currentMinute: minute));
            }
            else
            {
                DaggerfallOfficeOverrideSave? deposed = officeLedger.DeposeRegionalRuler(reg, parsedCause, day, "deposed (developer)", currentMinute: minute);
                if (deposed is null)
                    return DaggerfallDeveloperReplies.Refused($"Region {reg} has no province throne office.");
                vacated.Add(deposed);
            }
        }
        else if (trimmed.StartsWith("faction:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(trimmed["faction:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int factionId)
            && factionId > 0)
        {
            vacated.Add(lethal
                ? officeLedger.VacateFactionOffice(factionId, parsedCause, day, "slain (developer)", currentMinute: minute)
                : officeLedger.DeposeFactionOffice(factionId, parsedCause, day, "deposed (developer)", currentMinute: minute));
        }
        else if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long actorId) && actorId > 0)
        {
            IReadOnlyList<DaggerfallOfficeOverrideSave> actorOffices = officeLedger.DeposeActorOffices(
                actorId, parsedCause, day, lethal ? "slain (developer)" : "deposed (developer)", site.ActiveSite, currentMinute: minute);
            if (actorOffices.Count == 0)
                return DaggerfallDeveloperReplies.Refused($"Actor {actorId} does not hold any active office.");
            vacated.AddRange(actorOffices);
        }
        else
        {
            DaggerfallLocalOffice? localMatch = null;
            if (site.ActiveSite is { } activeSite)
            {
                localMatch = officeLedger.ResolveLocalOffices(activeSite)
                    .FirstOrDefault(o => StringComparer.OrdinalIgnoreCase.Equals(o.OfficeKey, trimmed));
            }
            if (localMatch is null && site.Region is int r)
            {
                localMatch = officeLedger.ResolveRegionalGreatHouses(r)
                    .SelectMany(h => h.HeldOffices)
                    .FirstOrDefault(o => StringComparer.OrdinalIgnoreCase.Equals(o.OfficeKey, trimmed));
            }

            if (localMatch is not null)
            {
                vacated.Add(lethal
                    ? officeLedger.SetLocalOfficeHolder(localMatch, null, null, parsedCause, day, "slain (developer)", vacant: true, currentMinute: minute)
                    : officeLedger.DeposeLocalOffice(localMatch, parsedCause, day, "deposed (developer)", currentMinute: minute));
            }
            else if (definitions.BoardSeats.All.FirstOrDefault(s =>
                StringComparer.OrdinalIgnoreCase.Equals(s.SeatId, trimmed)
                || StringComparer.OrdinalIgnoreCase.Equals(DaggerfallOfficeLedger.FactionOfficeKey(s.OfficeFactionId), trimmed)) is { } seat)
            {
                vacated.Add(officeLedger.DeposeBoardSeat(seat.SeatId, parsedCause, day, lethal ? "slain (developer)" : "deposed (developer)", currentMinute: minute));
            }
            else
            {
                return DaggerfallDeveloperReplies.Refused($"Office '{trimmed}' was not found in the active site, region, or board seats.");
            }
        }

        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("count", vacated.Count);
            w.WriteString("cause", parsedCause.ToString());
            w.WriteNumber("day", day);
            w.WriteStartArray("vacated");
            foreach (DaggerfallOfficeOverrideSave over in vacated)
            {
                w.WriteStartObject();
                w.WriteString("officeKey", over.OfficeKey);
                w.WriteString("kind", over.Kind.ToString());
                w.WriteBoolean("vacant", over.Vacant);
                if (!string.IsNullOrWhiteSpace(over.PreviousHolderName)) w.WriteString("previousHolderName", over.PreviousHolderName);
                if (over.VacancyUntilMinute is long until) w.WriteNumber("vacancyUntilMinute", until);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult SetHouseholdStanding(string familyId, int standingDelta)
    {
        if (string.IsNullOrWhiteSpace(familyId) || !DaggerfallHouseholds.IsValidFamilyId(familyId.Trim()))
            return DaggerfallDeveloperReplies.Refused($"Invalid FAMILY_ID '{familyId}'. Expected fam:local:..., fam:house:..., or fam:dynasty:....");
        if (standingDelta is < DaggerfallSocialState.MinimumReputation or > DaggerfallSocialState.MaximumReputation)
            return DaggerfallDeveloperReplies.Refused($"STANDING_DELTA must be between {DaggerfallSocialState.MinimumReputation} and {DaggerfallSocialState.MaximumReputation}.");

        string trimmed = familyId.Trim();
        DaggerfallSiteRecord? active = site.ActiveSite;
        DaggerfallCalendar cal = calendar();
        long minute = cal.MinuteNumber;
        int day = checked((int)cal.DayNumber);
        int applied;
        try
        {
            applied = socialConsequences.SetHouseholdStanding(trimmed, standingDelta, active, minute, day);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return DaggerfallDeveloperReplies.Refused(ex.Message);
        }

        bool hasSnap = socialConsequences.TryGetHouseholdSnapshot(trimmed, out DaggerfallHouseholdSnapshot snap);
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteString("familyId", trimmed);
            w.WriteString("scope", ScopeSlug(DaggerfallHouseholds.ScopeOf(trimmed)));
            w.WriteNumber("standingDelta", applied);
            w.WriteNumber("persistentStandingDelta", hasSnap ? snap.StandingDelta : 0);
            w.WriteBoolean("inBloodFeud", socialConsequences.IsFeudActiveAt(trimmed, active, minute));
            w.WriteBoolean("feudActive", hasSnap && snap.FeudActive);
            if (hasSnap && snap.MourningUntilDay is int mourningUntil)
                w.WriteNumber("mourningUntilDay", mourningUntil);
            else
                w.WriteNull("mourningUntilDay");
            w.WriteNumber("lastUpdatedDay", hasSnap ? snap.LastUpdatedDay : 0);
            w.WriteEndObject();
        });
    }

    public DebugCommandResult GrantVouch(string referrer, string target, string kind)
    {
        if (!TryResolveActorId(referrer, out long referrerId))
            return DaggerfallDeveloperReplies.Refused($"Unknown referrer '{referrer}'. Pass a durableId or castId.");
        if (!TryResolveActorId(target, out long targetId))
            return DaggerfallDeveloperReplies.Refused($"Unknown target '{target}'. Pass a durableId or castId.");
        if (referrerId == targetId)
            return DaggerfallDeveloperReplies.Refused("Referrer and target cannot be the same actor.");
        if (!TryParseVouchKind(kind, out ActorVouchKind parsedKind))
            return DaggerfallDeveloperReplies.Refused("KIND must be one of: contact, trade, guild, court.");

        ActorVouchRecord vouch = introductions.GrantVouchDirect(referrerId, targetId, parsedKind, site.ActiveSite);
        DaggerfallVouchEvaluation eval = introductions.EvaluateVouch(vouch, site.ActiveSite);
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("targetDurableId", targetId);
            w.WriteNumber("referrerDurableId", vouch.ReferrerDurableId);
            if (!string.IsNullOrWhiteSpace(vouch.ReferrerName)) w.WriteString("referrerName", vouch.ReferrerName); else w.WriteNull("referrerName");
            w.WriteString("kind", vouch.Kind.ToString());
            w.WriteNumber("issuedDay", vouch.IssuedDay);
            w.WriteString("status", eval.Status.ToString());
            w.WriteBoolean("usable", eval.IsUsable);
            w.WriteEndObject();
        });
    }

    public DebugCommandResult RevokeVouch(string target, string referrer, string reason)
    {
        long? targetId = null;
        if (!string.Equals(target?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryResolveActorId(target, out long parsedTarget))
                return DaggerfallDeveloperReplies.Refused($"Unknown target '{target}'. Pass a durableId, castId, or 'all'.");
            targetId = parsedTarget;
        }

        long? referrerId = null;
        if (!string.Equals(referrer?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryResolveActorId(referrer, out long parsedRef))
                return DaggerfallDeveloperReplies.Refused($"Unknown referrer '{referrer}'. Pass a durableId, castId, or 'all'.");
            referrerId = parsedRef;
        }

        if (!TryParseRevokeReason(reason, out ActorVouchVoidReason? voidReason))
            return DaggerfallDeveloperReplies.Refused("REASON must be one of: remove, wronged, dead, hostile.");

        int affected = introductions.RevokeVouches(targetId, referrerId, voidReason);
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("affectedCount", affected);
            w.WriteString("reason", voidReason?.ToString() ?? "Removed");
            if (targetId is long tid) w.WriteNumber("targetDurableId", tid); else w.WriteNull("targetDurableId");
            if (referrerId is long rid) w.WriteNumber("referrerDurableId", rid); else w.WriteNull("referrerDurableId");
            w.WriteEndObject();
        });
    }

    private bool TryResolveActorId(string? token, out long actorId)
    {
        actorId = 0;
        if (string.IsNullOrWhiteSpace(token))
            return false;
        string trimmed = token.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            && parsed > 0 && parsed != DaggerfallActorIdentity.PlayerEntityId)
        {
            actorId = parsed;
            return true;
        }
        if (definitions.TryGetCastOrNoble(trimmed, out DaggerfallCastIndividualDefinition? cast) && cast is not null)
        {
            actorId = DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
            return true;
        }
        return false;
    }

    private static bool TryParseOfficeCause(string? value, out DaggerfallOfficeChangeCause cause)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "death":
                cause = DaggerfallOfficeChangeCause.Death;
                return true;
            case "card":
            case "deposed":
            case "coup":
                cause = DaggerfallOfficeChangeCause.CardMove;
                return true;
            case "quest":
                cause = DaggerfallOfficeChangeCause.Quest;
                return true;
            case "law":
            case "banished":
                cause = DaggerfallOfficeChangeCause.Law;
                return true;
            case "succession":
                cause = DaggerfallOfficeChangeCause.Succession;
                return true;
            default:
                cause = default;
                return false;
        }
    }

    private static bool TryParseVouchKind(string? value, out ActorVouchKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "contact":
                kind = ActorVouchKind.Contact;
                return true;
            case "trade":
                kind = ActorVouchKind.Trade;
                return true;
            case "guild":
                kind = ActorVouchKind.Guild;
                return true;
            case "court":
                kind = ActorVouchKind.Court;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool TryParseRevokeReason(string? value, out ActorVouchVoidReason? voidReason)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "remove":
            case "clear":
                voidReason = null;
                return true;
            case "wronged":
                voidReason = ActorVouchVoidReason.Wronged;
                return true;
            case "dead":
                voidReason = ActorVouchVoidReason.Dead;
                return true;
            case "hostile":
                voidReason = ActorVouchVoidReason.Hostile;
                return true;
            default:
                voidReason = null;
                return false;
        }
    }
}
