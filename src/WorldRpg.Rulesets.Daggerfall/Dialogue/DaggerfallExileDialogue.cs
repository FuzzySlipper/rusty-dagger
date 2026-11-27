using System.Globalization;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Result of a player dialogue attempt to restore an exiled office holder to their lost office.</summary>
internal sealed record DaggerfallExileRestoreResult(bool Accepted, string Question, string Message);

/// <summary>One legally implicated living office holder or household member that the player can denounce to the watch or court in dialogue.</summary>
internal sealed record DaggerfallHolderDenounceTarget(
    long ActorId,
    string DisplayName,
    string Title,
    string OfficeKey,
    int Region,
    string Reason,
    DaggerfallCrimeKind CrimeKind,
    bool IsOfficeHolder = true);

/// <summary>Result of denouncing an implicated office holder to the watch or court in dialogue.</summary>
internal sealed record DaggerfallHolderDenounceResult(bool Accepted, string Question, string Message);

/// <summary>
/// Exile dialogue, player-backed restoration, and legal denunciation policy in the <c>Dialogue</c> domain.
/// Reads live office-ledger exile state and writes restoration back through <see cref="DaggerfallOfficeLedger.RestoreExile"/>
/// (which publishes <see cref="DaggerfallOfficeLedger.OfficeChanged"/>) and legal banishment through <see cref="DaggerfallLaw"/>.
/// </summary>
internal static class DaggerfallExileDialogue
{
    internal const int StandingRequirement = 10;
    internal const int DenounceStandingRequirement = 5;
    internal const ulong LocalRestorationGoldCost = 500UL;
    internal const ulong MajorRestorationGoldCost = 2000UL;

    /// <summary>Gold purse required to back an exile's restoration when the player lacks direct political standing.</summary>
    internal static ulong RestorationGoldCost(DaggerfallOfficeKind kind) => kind switch
    {
        DaggerfallOfficeKind.GuildHallMaster
            or DaggerfallOfficeKind.TempleHead
            or DaggerfallOfficeKind.WatchCaptain => LocalRestorationGoldCost,
        _ => MajorRestorationGoldCost,
    };

    /// <summary>Builds a grounded exile greeting referencing the lost office, cause, and current seat state.</summary>
    internal static string BuildGreetingLine(
        DaggerfallNpc npc,
        DaggerfallOfficeExileSave exile,
        DaggerfallOfficeLedger ledger,
        DaggerfallOfficeHolders? holders,
        DaggerfallDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(exile);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(definitions);

        string officeLabel = FormatOfficeLabel(exile, definitions);
        string reason = !string.IsNullOrWhiteSpace(exile.CauseDetail)
            ? exile.CauseDetail
            : exile.Cause switch
            {
                DaggerfallOfficeChangeCause.CardMove => "was unseated by court intrigue",
                DaggerfallOfficeChangeCause.Quest => "was driven out",
                DaggerfallOfficeChangeCause.Law => "was banished by the court",
                _ => "was deposed",
            };
        string seatStatus = DescribeCurrentSeatStatus(exile, ledger, holders);
        return $"You find me in exile, far from my rightful post as {officeLabel} — I {reason}. {seatStatus}";
    }

    /// <summary>Builds an ambient bark for an exiled former office holder.</summary>
    internal static string BuildAmbientBark(DaggerfallOfficeExileSave exile)
    {
        ArgumentNullException.ThrowIfNull(exile);
        return $"Exile wears thin — I belong back as {exile.Title}, not hiding out in a strange hall.";
    }

    /// <summary>
    /// Evaluates whether the player can restore <paramref name="exile"/> to office (via political/regional standing
    /// or patronage gold) and, when accepted, invokes <see cref="DaggerfallOfficeLedger.RestoreExile"/> so
    /// <see cref="DaggerfallOfficeLedger.OfficeChanged"/> reseats them in the world and on the board.
    /// </summary>
    internal static DaggerfallExileRestoreResult EvaluateAndRestore(
        DaggerfallNpc npc,
        DaggerfallOfficeExileSave exile,
        DaggerfallOfficeLedger ledger,
        DaggerfallSocialState social,
        DaggerfallCrimeState crime,
        DaggerfallDefinitions definitions,
        Func<ulong> playerGold,
        Func<ulong, bool> spendGold,
        Action<long, int>? adjustDisposition,
        string playerName,
        int currentDay,
        long currentMinute)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(exile);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(social);
        ArgumentNullException.ThrowIfNull(crime);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(playerGold);
        ArgumentNullException.ThrowIfNull(spendGold);

        string officeLabel = FormatOfficeLabel(exile, definitions);
        string question = $"I will back your return as {officeLabel}.";

        if (ledger.TryGetExile(exile.ActorId) is null)
            return new(false, question, "My restoration is already underway.");

        if (exile.OfficeRegion is int officeRegion && crime.BanishedRegions.Contains(officeRegion))
        {
            string regionName = definitions.BuildingNames.RegionName(officeRegion);
            return new(false, question, $"You stand banished from {regionName} yourself — clear your name there before backing my return.");
        }

        int reaction = social.ReactionForNpc(npc).Value;
        if (reaction < -10)
            return new(false, question, "I have no reason to trust your backing right now.");

        bool hasStanding = HasRestorationStanding(exile, social, definitions, reaction);
        ulong goldCost = RestorationGoldCost(exile.Kind);
        bool paidGold = false;

        if (!hasStanding)
        {
            if (playerGold() < goldCost)
            {
                string backingHint = exile.OfficeFactionId is int fid
                    && definitions.Factions.Factions.TryGetValue(fid, out DaggerfallFactionDefinition? fDef)
                        ? $"standing of at least {StandingRequirement} with {fDef.Name}"
                        : $"regional standing of at least {StandingRequirement}";
                return new(
                    false,
                    question,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Good intentions alone will not open the doors to {officeLabel}. Bring me {backingHint}, or {goldCost} gold to rally retainers and secure my return."));
            }
            if (!spendGold(goldCost))
                return new(false, question, "You do not have enough gold on hand to fund my return.");
            paidGold = true;
        }

        string safeName = string.IsNullOrWhiteSpace(playerName) ? "an ally" : playerName.Trim();
        DaggerfallOfficeOverrideSave? restored = ledger.RestoreExile(
            exile.ActorId,
            DaggerfallOfficeChangeCause.Quest,
            currentDay,
            causeDetail: $"was restored to office with {safeName}'s backing",
            currentMinute: currentMinute);
        if (restored is null)
            return new(false, question, "The way back to my seat is closed for now.");

        adjustDisposition?.Invoke(exile.ActorId, 10);
        if (exile.OfficeFactionId is int officeFaction && officeFaction > 0 && definitions.Factions.Factions.ContainsKey(officeFaction))
            social.ChangeFactionReputation(officeFaction, 5, DaggerfallFactionReputationChange.Propagate);

        string methodNote = paidGold
            ? string.Create(CultureInfo.InvariantCulture, $"Your {goldCost} gold buys the retainers and safe-conduct I needed. ")
            : "Your standing carries real weight with the court. ";
        return new(true, question, $"{methodNote}Word goes out at once — I return to reclaim my seat as {officeLabel}!");
    }

    /// <summary>
    /// Lists living office holders at <paramref name="site"/> (or the region's ruler when speaking in court/watch)
    /// who are currently implicated by an active regional crackdown, guild embargo, or court crisis and can be
    /// denounced to a watchman or court official.
    /// </summary>
    internal static IReadOnlyList<DaggerfallHolderDenounceTarget> ListDenounceTargets(
        DaggerfallNpc speaker,
        DaggerfallSiteRecord? site,
        DaggerfallOfficeLedger ledger,
        DaggerfallOfficeHolders? holders,
        IDaggerfallWorldConditionSource? worldConditions,
        DaggerfallCrimeState crime,
        DaggerfallDefinitions definitions,
        int currentDay,
        DaggerfallNpcRegistry? npcs = null,
        ActorsState? actors = null,
        DaggerfallActorLedger? actorLedger = null)
    {
        if (site is null || worldConditions is null || !IsLawOrCourtAuthority(speaker, definitions))
            return [];

        int region = site.Id.Region;
        if (crime.BanishedRegions.Contains(region))
            return [];

        IReadOnlyList<DaggerfallWorldConditionToken> active = worldConditions.ActiveForRegion(region, currentDay);
        if (active.Count == 0)
            return [];

        DaggerfallWorldConditionToken? crackdown = active.FirstOrDefault(t => t.Kind == DaggerfallWorldConditionKind.CrimeCrackdown);
        DaggerfallWorldConditionToken? crisis = active.FirstOrDefault(t => t.Kind == DaggerfallWorldConditionKind.SuccessionCrisis);
        List<DaggerfallWorldConditionToken> embargoes = [.. active.Where(t => t.Kind == DaggerfallWorldConditionKind.GuildEmbargo)];
        if (crackdown is null && crisis is null && embargoes.Count == 0)
            return [];

        List<DaggerfallHolderDenounceTarget> targets = [];
        HashSet<long> seen = [];

        foreach (DaggerfallLocalOffice local in ledger.ResolveLocalOffices(site))
        {
            if (local.IsVacant || !local.IsAlive || local.HolderActorId is not long holderId || holderId == speaker.DurableId)
                continue;
            bool implicatedByCrackdown = crackdown is not null;
            bool implicatedByEmbargo = local.OfficeFactionId > 0
                && embargoes.Any(e => e.TargetFactionId == local.OfficeFactionId || e.SourceFactionId == local.OfficeFactionId);
            if (!implicatedByCrackdown && !implicatedByEmbargo)
                continue;
            if (!seen.Add(holderId))
                continue;
            DaggerfallCrimeKind crimeKind = implicatedByEmbargo ? DaggerfallCrimeKind.Smuggling : DaggerfallCrimeKind.CriminalConspiracy;
            string reason = implicatedByEmbargo ? "violating the guild embargo" : "corruption uncovered in the crackdown";
            targets.Add(new(holderId, local.DisplayName, local.Title, local.OfficeKey, region, reason, crimeKind, IsOfficeHolder: true));
        }

        if ((crackdown is { Severity: >= 2 } || crisis is not null)
            && holders?.ResolveRegionalRuler(region) is { HasLivingHolder: true, ActorId: long rulerId } ruler
            && rulerId != speaker.DurableId
            && seen.Add(rulerId))
        {
            string officeKey = DaggerfallOfficeLedger.FactionOfficeKey(ruler.OfficeFactionId);
            targets.Add(new(
                rulerId,
                ruler.DisplayName,
                ruler.Title ?? "Ruler",
                officeKey,
                region,
                "high treason and misrule",
                DaggerfallCrimeKind.HighTreason,
                IsOfficeHolder: true));
        }

        // During an active crackdown or guild embargo, household-bearing citizens and untitled household/noble heads
        // present at the active site can also be denounced to the watch for arrest/imprisonment.
        if (npcs is not null && (crackdown is not null || embargoes.Count > 0))
        {
            foreach (DaggerfallNpc resident in npcs.All.OrderBy(n => n.DurableId))
            {
                if (resident.DurableId == speaker.DurableId
                    || resident.Presence != DaggerfallNpcPresence.Active
                    || string.IsNullOrWhiteSpace(resident.FamilyId)
                    || resident.Site.Region != region
                    || !string.Equals(resident.Site.Location, site.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (resident.Role.Equals("guard", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (actors is not null && (!actors.TryGet(resident.DurableId, out ActorState? live) || live.IsDefeated))
                    continue;
                if (actorLedger is not null
                    && actorLedger.TryGet(resident.DurableId, out ActorLedgerEntry? entry)
                    && entry?.Location?.Kind is ActorLocationKind.OffStage or ActorLocationKind.Dead or ActorLocationKind.Removed)
                {
                    continue;
                }
                if (!seen.Add(resident.DurableId))
                    continue;

                bool implicatedByEmbargo = resident.Appearance.FactionId > 0
                    && embargoes.Any(e => e.TargetFactionId == resident.Appearance.FactionId || e.SourceFactionId == resident.Appearance.FactionId);
                if (crackdown is null && !implicatedByEmbargo)
                    continue;

                DaggerfallCrimeKind crimeKind = implicatedByEmbargo ? DaggerfallCrimeKind.Smuggling : DaggerfallCrimeKind.CriminalConspiracy;
                string reason = implicatedByEmbargo ? "smuggling under the guild embargo" : "unlawful dealings uncovered in the crackdown";
                string title = string.IsNullOrWhiteSpace(resident.Role) || resident.Role.Equals("person", StringComparison.OrdinalIgnoreCase)
                    ? "Citizen"
                    : char.ToUpperInvariant(resident.Role[0]) + resident.Role[1..];
                targets.Add(new(
                    resident.DurableId,
                    resident.DisplayName ?? resident.Role,
                    title,
                    string.Empty,
                    region,
                    reason,
                    crimeKind,
                    IsOfficeHolder: false));
            }
        }

        return targets;
    }

    /// <summary>
    /// Evaluates and carries out a player denunciation of an implicated office holder (via
    /// <see cref="DaggerfallLaw.ConvictOrBanishOfficeHolder"/>) or household member / untitled head
    /// (via <see cref="DaggerfallLaw.ArrestOrImprisonNpc"/>).
    /// </summary>
    internal static DaggerfallHolderDenounceResult EvaluateAndDenounce(
        DaggerfallNpc speaker,
        long targetActorId,
        DaggerfallSiteRecord? site,
        DaggerfallOfficeLedger ledger,
        DaggerfallOfficeHolders? holders,
        IDaggerfallWorldConditionSource? worldConditions,
        DaggerfallSocialState social,
        DaggerfallCrimeState crime,
        DaggerfallLaw law,
        DaggerfallDefinitions definitions,
        int currentDay,
        DaggerfallNpcRegistry? npcs = null,
        ActorsState? actors = null,
        DaggerfallActorLedger? actorLedger = null)
    {
        IReadOnlyList<DaggerfallHolderDenounceTarget> targets = ListDenounceTargets(
            speaker, site, ledger, holders, worldConditions, crime, definitions, currentDay, npcs, actors, actorLedger);
        DaggerfallHolderDenounceTarget? target = targets.FirstOrDefault(t => t.ActorId == targetActorId);
        if (target is null || site is null)
            return new(false, "I have evidence against someone here.", "There is no actionable charge against that person right now.");

        string question = $"I am reporting {target.Title} {target.DisplayName} for {target.Reason}.";
        int regionalRep = social.RegionalReputation(site.Id.Region);
        int speakerReaction = social.ReactionForNpc(speaker).Value;
        if (regionalRep < DenounceStandingRequirement && speakerReaction < DenounceStandingRequirement)
        {
            return new(
                false,
                question,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The court will not move against {target.DisplayName} on the word of a stranger — build standing of at least {DenounceStandingRequirement} here first."));
        }

        string regionName = definitions.BuildingNames.RegionName(site.Id.Region);
        if (target.IsOfficeHolder)
        {
            int removed = law.ConvictOrBanishOfficeHolder(
                target.ActorId,
                site.Id.Region,
                target.CrimeKind,
                banished: true,
                detail: $"was banished from {regionName} for {target.Reason}");
            if (removed <= 0)
                return new(false, question, $"{target.DisplayName} no longer holds office here.");

            return new(
                true,
                question,
                $"The warrant is sealed. {target.DisplayName} has been stripped of the post of {target.Title} and banished from {regionName}.");
        }

        DaggerfallNpcArrestOutcome outcome = law.ArrestOrImprisonNpc(
            target.ActorId,
            site.Id.Region,
            target.CrimeKind,
            banished: false,
            detail: $"was arrested in {regionName} for {target.Reason}");
        if (!outcome.Imprisoned)
            return new(false, question, $"{target.DisplayName} could not be taken into custody right now.");

        return new(
            true,
            question,
            $"The warrant is sealed. The watch has taken {target.DisplayName} into custody in {regionName} for {target.Reason}.");
    }

    private static bool HasRestorationStanding(
        DaggerfallOfficeExileSave exile,
        DaggerfallSocialState social,
        DaggerfallDefinitions definitions,
        int npcReaction)
    {
        if (npcReaction >= StandingRequirement)
            return true;
        if (exile.OfficeRegion is int region && social.RegionalReputation(region) >= StandingRequirement)
            return true;
        if (exile.OfficeFactionId is int factionId && factionId > 0)
        {
            if (social.FactionReputation(factionId) >= StandingRequirement)
                return true;
            int parent = social.Liege.ParentOf(factionId);
            if (parent > 0 && definitions.Factions.Factions.ContainsKey(parent) && social.FactionReputation(parent) >= StandingRequirement)
                return true;
        }
        return false;
    }

    private static string DescribeCurrentSeatStatus(
        DaggerfallOfficeExileSave exile,
        DaggerfallOfficeLedger ledger,
        DaggerfallOfficeHolders? holders)
    {
        DaggerfallOfficeOverrideSave? over = ledger.TryGetOverride(exile.OfficeKey);
        if (over is { Vacant: true })
            return "My old seat stands vacant while I bide my time here.";
        if (over is { IsRegency: true })
        {
            string regent = !string.IsNullOrWhiteSpace(over.HolderDisplayName) ? over.HolderDisplayName : "an interim regent";
            return $"For now {regent} holds regency over my seat while I wait for a chance to return.";
        }
        if (over is { Vacant: false } && !string.IsNullOrWhiteSpace(over.HolderDisplayName))
            return $"{over.HolderDisplayName} sits in my place now, though I have not given up my claim.";
        if (exile.OfficeFactionId is int fid && fid > 0 && holders?.ResolveOffice(fid, exile.OfficeRegion) is { HasLivingHolder: true } current)
            return $"{current.DisplayName} holds the post now, though I have not given up my claim.";
        return "I am waiting here until I have the backing to reclaim what is mine.";
    }

    private static string FormatOfficeLabel(DaggerfallOfficeExileSave exile, DaggerfallDefinitions definitions)
    {
        if (exile.Kind == DaggerfallOfficeKind.Ruler && exile.OfficeRegion is int region)
        {
            string regionName = definitions.BuildingNames.RegionName(region);
            return exile.Title.Contains(regionName, StringComparison.OrdinalIgnoreCase)
                ? exile.Title
                : $"{exile.Title} of {regionName}";
        }
        return exile.Title;
    }

    private static bool IsLawOrCourtAuthority(DaggerfallNpc speaker, DaggerfallDefinitions definitions)
    {
        if (speaker.Role.Equals("guard", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("captain", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("watch", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("factor", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("regent", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("ruler", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (speaker.Appearance.FactionId > 0
            && definitions.Factions.Factions.TryGetValue(speaker.Appearance.FactionId, out DaggerfallFactionDefinition? faction)
            && (faction.Type is 7 or 14 or 5 || (faction.SocialGroup == 3 && faction.Type != 15 && faction.GuildGroup != 9)))
        {
            return true;
        }
        return false;
    }
}
