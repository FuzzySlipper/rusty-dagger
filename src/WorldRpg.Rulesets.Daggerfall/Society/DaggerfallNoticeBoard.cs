using System.Globalization;
using System.Text;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Kind of notice posted on a town's public notice board.</summary>
internal enum DaggerfallNoticeEntryKind
{
    /// <summary>Official regional or faction decree driven by an active world condition.</summary>
    Decree = 0,

    /// <summary>Bounty posting for wanted outlaws or regional monster surges.</summary>
    Bounty = 1,

    /// <summary>Proclamation announcing an office succession, appointment, or vacancy.</summary>
    OfficeNotice = 2,

    /// <summary>Classic Daggerfall sign-message rumour or public broadsheet.</summary>
    SignRumor = 3,
}

/// <summary>Kind of actionable lead attached to a town notice board posting.</summary>
internal enum DaggerfallNoticeLeadKind
{
    /// <summary>Informational posting with no direct lead action.</summary>
    None = 0,

    /// <summary>Sealed courier dispatch offer backed by <see cref="DaggerfallCourierDispatches"/>.</summary>
    CourierDelivery = 1,

    /// <summary>Regional bounty, monster surge, or supply-raider counter-play lead recorded in the notebook.</summary>
    RegionalBounty = 2,

    /// <summary>Office proclamation or public decree clipped into the player's notebook.</summary>
    PublicLead = 3,
}

/// <summary>Actionable lead metadata attached to a posted notice so players can pick it up directly at the board.</summary>
internal sealed record DaggerfallNoticeLead(
    string LeadId,
    DaggerfallNoticeLeadKind Kind,
    string ActionLabel,
    string? DispatchId = null,
    string? TargetSummary = null,
    int? RewardGold = null);

/// <summary>One entry posted on a town notice board.</summary>
internal sealed record DaggerfallNoticeEntry(
    string Id,
    DaggerfallNoticeEntryKind Kind,
    string Heading,
    string Body,
    int Region,
    int? FactionId = null,
    string? ConditionTokenId = null,
    DaggerfallNoticeLead? Lead = null);

/// <summary>Resolved town notice board view assembled on demand with zero save bytes of its own.</summary>
internal sealed record DaggerfallTownNoticeBoardView(
    DaggerfallSiteId SiteId,
    string TownName,
    string RegionName,
    string? AuthorityName,
    IReadOnlyList<DaggerfallNoticeEntry> Entries);

/// <summary>
/// Assembles a town's public notice board on demand from classic sign-message rumours, active
/// <see cref="IDaggerfallWorldConditionSource"/> decrees/bounties, live regional wanted bounties, posted
/// <see cref="DaggerfallCourierDispatches"/> delivery offers, and recent <see cref="DaggerfallOfficeLedger"/> changes.
/// Holds no persisted state of its own.
/// </summary>
internal sealed class DaggerfallNoticeBoard(
    DaggerfallDefinitions definitions,
    IDaggerfallWorldConditionSource conditions,
    DaggerfallOfficeHolders officeHolders,
    DaggerfallOfficeLedger officeLedger,
    DaggerfallCrimeState crime,
    DaggerfallSocialState social,
    Func<int> currentDay,
    Func<DaggerfallTextContext> currentText,
    Func<DaggerfallCharacterIdentity> playerIdentity,
    DaggerfallWordSpread? wordSpread = null,
    DaggerfallCourierDispatches? courierDispatches = null)
{
    private const int MaxClassicSignRumors = 3;

    /// <summary>Builds the current notice board for <paramref name="site"/>.</summary>
    internal DaggerfallTownNoticeBoardView BuildForSite(DaggerfallSiteRecord site, long currentMinute)
    {
        ArgumentNullException.ThrowIfNull(site);
        int region = site.Id.Region;
        int day = currentDay();
        string regionName = definitions.BuildingNames.RegionName(region);
        DaggerfallOfficeHolder? ruler = officeHolders.ResolveRegionalRuler(region);
        string? authorityName = ruler is { HasLivingHolder: true }
            ? (string.IsNullOrWhiteSpace(ruler.Title) ? ruler.DisplayName : $"{ruler.Title} {ruler.DisplayName}")
            : null;

        List<DaggerfallNoticeEntry> entries = [];

        // 1. Posted courier delivery offers originating from this town (or unclaimed in this region).
        AddCourierDeliveryOffers(entries, site, region);

        // 2. Active world-condition decrees and regional bounties (stubbed/authored or B2 board-driven).
        foreach (DaggerfallWorldConditionToken token in conditions.ActiveForRegion(region, day))
            entries.Add(BuildConditionEntry(token, region, regionName, authorityName));

        // 3. Sparse office proclamations (regional throne or local town offices that changed from baseline).
        HashSet<string> postedOfficeKeys = new(StringComparer.Ordinal);
        AddOfficeNotices(entries, postedOfficeKeys, site, region, regionName, ruler);

        // 4. Live wanted / bounty posting when word of the player's crimes/warrant has arrived at this town.
        AddPlayerBountyNotice(entries, site, region, regionName, authorityName, currentMinute, day);

        // 5. Arrived courier news/standing wavefronts (office proclamations from distant capitals, board dispatches, and rumours).
        if (wordSpread is not null)
        {
            int nonOfficeCount = 0;
            foreach (DaggerfallWordWavefrontSave arrived in wordSpread.ArrivedNewsAt(site, currentMinute, day))
            {
                if (DaggerfallWordSpread.TryParseOfficeProclamationKey(arrived.EventId, out string officeKey))
                {
                    // Newest arrived proclamation wins per office key; skip if already rendered locally above.
                    if (!postedOfficeKeys.Add(officeKey))
                        continue;
                    string id = $"notice:word:{arrived.EventId}";
                    entries.Add(new(
                        Id: id,
                        Kind: DaggerfallNoticeEntryKind.OfficeNotice,
                        Heading: arrived.Headline ?? "Office Proclamation",
                        Body: arrived.Body!,
                        Region: region,
                        FactionId: arrived.FactionId,
                        Lead: new DaggerfallNoticeLead($"lead:{id}", DaggerfallNoticeLeadKind.PublicLead, "Note in journal")));
                    continue;
                }

                if (nonOfficeCount >= 3)
                    continue;
                nonOfficeCount++;
                DaggerfallNoticeEntryKind wordKind = arrived.Kind == DaggerfallWordEventKind.NewsRumor
                    ? DaggerfallNoticeEntryKind.Decree
                    : DaggerfallNoticeEntryKind.SignRumor;
                string wordId = $"notice:word:{arrived.EventId}";
                entries.Add(new(
                    Id: wordId,
                    Kind: wordKind,
                    Heading: arrived.Headline ?? "Courier Bulletin",
                    Body: arrived.Body!,
                    Region: region,
                    FactionId: arrived.FactionId,
                    Lead: wordKind == DaggerfallNoticeEntryKind.Decree
                        ? new DaggerfallNoticeLead($"lead:{wordId}", DaggerfallNoticeLeadKind.PublicLead, "Note in journal")
                        : null));
            }
        }

        // 6. Classic Daggerfall sign-message rumours (rumor.IsSignMessage == true) rendered with live context.
        AddClassicSignRumors(entries, site, region, regionName, currentMinute);

        return new(site.Id, site.Name, regionName, authorityName, entries);
    }

    /// <summary>Formats a town notice board view into readable prose for dialogue and UI surfaces.</summary>
    internal static string FormatBoardText(DaggerfallTownNoticeBoardView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Entries.Count == 0)
            return $"The notice board in {view.TownName} is bare today.";

        StringBuilder sb = new();
        sb.Append(view.TownName).Append(" Notice Board");
        if (!string.IsNullOrWhiteSpace(view.AuthorityName))
            sb.Append(" (").Append(view.AuthorityName).Append(')');
        sb.Append(':');
        foreach (DaggerfallNoticeEntry entry in view.Entries)
        {
            sb.Append('\n').Append('[').Append(KindLabel(entry.Kind)).Append("] ");
            sb.Append(entry.Heading).Append(" — ").Append(entry.Body);
        }
        return sb.ToString();
    }

    private DaggerfallNoticeEntry BuildConditionEntry(
        DaggerfallWorldConditionToken token,
        int region,
        string regionName,
        string? authorityName)
    {
        string issuer = ResolveFactionName(token.SourceFactionId) ?? authorityName ?? $"the authorities of {regionName}";
        string? target = ResolveFactionName(token.TargetFactionId);
        DaggerfallNoticeEntryKind kind = token.Kind == DaggerfallWorldConditionKind.MonsterSurge
            ? DaggerfallNoticeEntryKind.Bounty
            : DaggerfallNoticeEntryKind.Decree;

        string entryId = $"notice:cond:{token.TokenId}";
        DaggerfallNoticeLead lead = DaggerfallConditionCounterplay.IsCounterplayable(token.Kind)
            ? new DaggerfallNoticeLead(
                LeadId: $"lead:{entryId}",
                Kind: DaggerfallNoticeLeadKind.RegionalBounty,
                ActionLabel: "Take bounty lead",
                TargetSummary: ResolveConditionLeadTargetSummary(token, region, regionName))
            : new DaggerfallNoticeLead(
                LeadId: $"lead:{entryId}",
                Kind: DaggerfallNoticeLeadKind.PublicLead,
                ActionLabel: "Note in journal");

        if (!string.IsNullOrWhiteSpace(token.Headline) && !string.IsNullOrWhiteSpace(token.Body))
        {
            return new(
                Id: entryId,
                Kind: kind,
                Heading: token.Headline,
                Body: token.Body,
                Region: region,
                FactionId: token.SourceFactionId,
                ConditionTokenId: token.TokenId,
                Lead: lead);
        }

        string subject = string.IsNullOrWhiteSpace(token.SubjectTag) ? "monsters" : token.SubjectTag;
        (string heading, string body) = token.Kind switch
        {
            DaggerfallWorldConditionKind.MonsterSurge => (
                $"Bounty: {Capitalize(subject)} Surge in {regionName}",
                $"Issued by {issuer}: {subject} warbands have been sighted across {regionName}. Armed travelers are warned on the roads, and local halls pay well for cleared lairs."),
            DaggerfallWorldConditionKind.GrainShortage => (
                $"Market Notice: Grain Shortage in {regionName}",
                $"Issued by {issuer}: supply wagons into {regionName} are running thin. Expect steeper market prices and escort work along the border roads."),
            DaggerfallWorldConditionKind.BorderTension => (
                $"Decree: Border Watch in {regionName}",
                $"By order of {issuer}: border tensions{(target is not null ? $" with {target}" : "")} have placed town gates and highways under heightened patrol and scrutiny."),
            DaggerfallWorldConditionKind.CrimeCrackdown => (
                $"Decree: Watch Crackdown in {regionName}",
                $"By order of {issuer}: the watch is conducting strict checks at the gates and lawful halls. Citizens of poor standing will be questioned or turned away."),
            DaggerfallWorldConditionKind.GuildEmbargo => (
                $"Proclamation: {issuer} Service Embargo",
                $"{issuer} has closed its doors in {regionName} to outsiders{(target is not null ? $" and affiliates of {target}" : "")} until further notice."),
            DaggerfallWorldConditionKind.SuccessionCrisis => (
                $"Proclamation: Succession Unsettled in {regionName}",
                $"The court of {regionName} remains unsettled. Guards are inspecting travelers closely until a lawful heir secures the seat."),
            DaggerfallWorldConditionKind.PlagueScare => (
                $"Quarantine Decree in {regionName}",
                $"By order of {issuer}: travelers entering {regionName} face quarantine checks at the gates. Seek the temples for cures and blessings."),
            _ => (
                token.Headline ?? $"Public Decree in {regionName}",
                token.Body ?? $"By order of {issuer}: take heed of current decrees posted across {regionName}."),
        };

        return new(
            Id: entryId,
            Kind: kind,
            Heading: token.Headline ?? heading,
            Body: token.Body ?? body,
            Region: region,
            FactionId: token.SourceFactionId,
            ConditionTokenId: token.TokenId,
            Lead: lead);
    }

    private void AddCourierDeliveryOffers(List<DaggerfallNoticeEntry> entries, DaggerfallSiteRecord site, int region)
    {
        if (courierDispatches is null)
            return;

        foreach (DaggerfallCourierDispatchSave dispatch in courierDispatches.All
            .Where(d => d.Status is DaggerfallDispatchStatus.InTransit or DaggerfallDispatchStatus.BoughtAndRevealed or DaggerfallDispatchStatus.CarriedByPlayer)
            .Where(d => d.OriginSite.Require() == site.Id || (d.Status != DaggerfallDispatchStatus.CarriedByPlayer && d.OriginSite.Require().Region == region))
            .OrderByDescending(d => d.OriginSite.Require() == site.Id ? 1 : 0)
            .ThenBy(d => d.DispatchedAtMinute)
            .ThenBy(d => d.DispatchId, StringComparer.Ordinal)
            .Take(3))
        {
            DaggerfallSiteId destId = dispatch.DestinationSite.Require();
            string destName = ResolveSiteName(destId) ?? definitions.BuildingNames.RegionName(destId.Region);
            string entryId = $"notice:dispatch:{dispatch.DispatchId}";
            entries.Add(new(
                Id: entryId,
                Kind: DaggerfallNoticeEntryKind.Bounty,
                Heading: $"Courier Wanted: {destName}",
                Body: $"Sealed dispatch ({dispatch.Summary}) bound for {destName}. Pays {dispatch.DeliveryRewardGold} gold upon delivery.",
                Region: region,
                FactionId: dispatch.SourceFactionId,
                ConditionTokenId: dispatch.LinkedConditionTokenId,
                Lead: new DaggerfallNoticeLead(
                    LeadId: $"lead:dispatch:{dispatch.DispatchId}",
                    Kind: DaggerfallNoticeLeadKind.CourierDelivery,
                    ActionLabel: $"Take dispatch ({dispatch.DeliveryRewardGold}g)",
                    DispatchId: dispatch.DispatchId,
                    TargetSummary: $"Deliver sealed dispatch to {destName}",
                    RewardGold: dispatch.DeliveryRewardGold)));
        }
    }

    private string? ResolveConditionLeadTargetSummary(DaggerfallWorldConditionToken token, int region, string regionName)
    {
        if (token.Kind == DaggerfallWorldConditionKind.MonsterSurge)
        {
            string subject = string.IsNullOrWhiteSpace(token.SubjectTag) ? "monsters" : token.SubjectTag;
            DaggerfallSiteRecord? matchingLair = definitions.Locations.Records
                .Where(site => site.Id.Region == region
                    && site.DungeonBlocks.Count > 0
                    && site.DungeonType is int dt
                    && (string.IsNullOrWhiteSpace(token.SubjectTag)
                        || DaggerfallConditionCounterplay.DungeonTypeFactions(dt).Contains(token.SubjectTag, StringComparer.OrdinalIgnoreCase)))
                .OrderBy(site => site.Id.Index)
                .FirstOrDefault();
            return matchingLair is not null
                ? $"Clear {subject} forces at {matchingLair.Name} or across {regionName}"
                : $"Clear {subject} forces across {regionName}";
        }

        if (token.Kind == DaggerfallWorldConditionKind.GrainShortage)
            return $"Clear supply raiders along the roads of {regionName}";

        return null;
    }

    private string? ResolveSiteName(DaggerfallSiteId siteId) =>
        definitions.Locations.Records.FirstOrDefault(r => r.Id == siteId)?.Name;

    private void AddOfficeNotices(
        List<DaggerfallNoticeEntry> entries,
        HashSet<string> postedOfficeKeys,
        DaggerfallSiteRecord site,
        int region,
        string regionName,
        DaggerfallOfficeHolder? ruler)
    {
        int provinceFactionId = ruler?.OfficeFactionId
            ?? definitions.Factions.Factions.Values
                .Where(f => f.Type == 7 && f.Region == region)
                .OrderBy(f => f.Id)
                .Select(f => f.Id)
                .FirstOrDefault();

        if (provinceFactionId > 0)
        {
            string throneKey = DaggerfallOfficeLedger.FactionOfficeKey(provinceFactionId);
            // When WordSpread tracks proclamation wavefronts for this throne, gate notice posting on courier arrival
            // (including same-region towns outside the capital's instant bubble) so future news never leaks early.
            if (officeLedger.TryGetOverride(throneKey, out DaggerfallOfficeOverrideSave? throneOverride)
                && throneOverride is not null
                && (wordSpread is null || !wordSpread.HasOfficeProclamation(throneKey)))
            {
                postedOfficeKeys.Add(throneOverride.OfficeKey);
                string heading = throneOverride.Vacant
                    ? $"Throne of {regionName} Vacant"
                    : throneOverride.IsRegency
                        ? $"Regency of {throneOverride.HolderDisplayName} in {regionName}"
                        : $"Proclamation of {throneOverride.HolderDisplayName}";
                string body = throneOverride.Vacant
                    ? $"The seat of {regionName} stands vacant since {throneOverride.PreviousHolderName ?? "the former ruler"} ({FormatCause(throneOverride)})."
                    : throneOverride.IsRegency
                        ? $"{throneOverride.HolderDisplayName} governs {regionName} in regency while the succession after {throneOverride.PreviousHolderName ?? "the former ruler"} remains unsettled."
                        : $"{throneOverride.HolderDisplayName} now holds the throne of {regionName}"
                          + (string.IsNullOrWhiteSpace(throneOverride.PreviousHolderName)
                              ? "."
                              : $", succeeding {throneOverride.PreviousHolderName} ({FormatCause(throneOverride)}).");
                string throneEntryId = $"notice:office:{throneOverride.OfficeKey}";
                entries.Add(new(
                    Id: throneEntryId,
                    Kind: DaggerfallNoticeEntryKind.OfficeNotice,
                    Heading: heading,
                    Body: body,
                    Region: region,
                    FactionId: provinceFactionId,
                    Lead: new DaggerfallNoticeLead($"lead:{throneEntryId}", DaggerfallNoticeLeadKind.PublicLead, "Note in journal")));
            }
        }

        foreach (DaggerfallLocalOffice local in officeLedger.ResolveLocalOffices(site))
        {
            if (!local.IsOverridden)
                continue;
            postedOfficeKeys.Add(local.OfficeKey);
            string heading = local.IsVacant
                ? $"{local.Title} Post Vacant"
                : local.IsRegency
                    ? $"Interim {local.Title}"
                    : $"New {local.Title}";
            string body = local.IsVacant
                ? $"The office of {local.Title} is currently vacant following the loss of {local.PreviousHolderName ?? "its former holder"}."
                : local.IsRegency
                    ? $"{local.DisplayName} serves as interim {local.Title} pending formal succession."
                    : $"{local.DisplayName} has been named {local.Title}"
                      + (string.IsNullOrWhiteSpace(local.PreviousHolderName) ? "." : $", replacing {local.PreviousHolderName}.");
            string localEntryId = $"notice:office:{local.OfficeKey}";
            entries.Add(new(
                Id: localEntryId,
                Kind: DaggerfallNoticeEntryKind.OfficeNotice,
                Heading: heading,
                Body: body,
                Region: region,
                FactionId: local.OfficeFactionId > 0 ? local.OfficeFactionId : null,
                Lead: new DaggerfallNoticeLead($"lead:{localEntryId}", DaggerfallNoticeLeadKind.PublicLead, "Note in journal")));
        }
    }

    private void AddPlayerBountyNotice(
        List<DaggerfallNoticeEntry> entries,
        DaggerfallSiteRecord site,
        int region,
        string regionName,
        string? authorityName,
        long currentMinute,
        int currentDay)
    {
        bool banished = crime.BanishedRegions.Contains(region);
        int effectiveRep = social.RegionalReputation(region)
            + (wordSpread?.UnarrivedCrimePenaltyOffset(site, currentMinute, currentDay) ?? 0);
        bool wanted = effectiveRep < DaggerfallLegalServicePolicy.WantedReputationThreshold;
        List<DaggerfallCrimeIncidentSave> knownCharges = [.. crime.PendingCharges(region)
            .Where(charge =>
            {
                if (wordSpread is null) return true;
                if (!wordSpread.TryGet($"wf:crime:{charge.OperationId}", out DaggerfallWordWavefrontSave? wf) || wf is null)
                    return true;
                return wordSpread.EvaluateAt(wf, site, currentMinute, currentDay).HasArrived;
            })];
        if (!banished && !wanted && knownCharges.Count == 0)
            return;

        string playerName = playerIdentity().Name;
        string status = banished ? "Banished Outlaw" : "Wanted by the Watch";
        string chargeList = knownCharges.Count > 0
            ? $" Charges: {string.Join(", ", knownCharges.Select(c => c.Crime.ToString()).Distinct(StringComparer.Ordinal))}."
            : string.Empty;
        string issuer = authorityName ?? $"the Watch of {regionName}";
        entries.Add(new(
            Id: string.Create(CultureInfo.InvariantCulture, $"notice:bounty:player:r{region}"),
            Kind: DaggerfallNoticeEntryKind.Bounty,
            Heading: $"{status}: {playerName}",
            Body: $"By warrant of {issuer}: {playerName} is sought across {regionName}.{chargeList} Lawful halls are ordered to refuse service and summon the watch.",
            Region: region));
    }

    private void AddClassicSignRumors(
        List<DaggerfallNoticeEntry> entries,
        DaggerfallSiteRecord site,
        int region,
        string regionName,
        long currentMinute)
    {
        List<DaggerfallRumorDefinition> regionalSigns = [.. definitions.Rumors.Entries
            .Where(rumor => rumor.IsSignMessage && !rumor.IsQuestRumor && rumor.QuestId == 0
                && rumor.Region == region
                && (rumor.TimeLimit <= 0 || rumor.TimeLimit > currentMinute))
            .OrderBy(rumor => rumor.Index)];

        List<DaggerfallRumorDefinition> selected = [.. regionalSigns.Take(MaxClassicSignRumors)];
        if (selected.Count < MaxClassicSignRumors)
        {
            foreach (DaggerfallRumorDefinition baySign in definitions.Rumors.Entries
                .Where(rumor => rumor.IsSignMessage && !rumor.IsQuestRumor && rumor.QuestId == 0
                    && rumor.Region != region
                    && (rumor.TimeLimit <= 0 || rumor.TimeLimit > currentMinute))
                .OrderBy(rumor => rumor.Index))
            {
                if (selected.Count >= MaxClassicSignRumors) break;
                selected.Add(baySign);
            }
        }

        DaggerfallTextContext baseContext = currentText();
        DaggerfallFactionDefinition? province = definitions.Factions.Factions.Values
            .Where(faction => faction.Type == 7 && faction.Region == region)
            .OrderBy(faction => faction.Id)
            .FirstOrDefault();

        foreach (DaggerfallRumorDefinition rumor in selected)
        {
            string? f1 = definitions.Factions.Factions.GetValueOrDefault(rumor.Faction1)?.Name;
            string? f2 = definitions.Factions.Factions.GetValueOrDefault(rumor.Faction2)?.Name;
            DaggerfallTextContext rumorContext = baseContext with
            {
                Faction = baseContext.Faction with
                {
                    NewsFactionOne = f1,
                    NewsFactionTwo = f2,
                    RegionInContext = province?.Name ?? regionName,
                },
                Location = baseContext.Location with
                {
                    City = site.Name,
                    Region = province?.Name ?? regionName,
                },
            };
            DaggerfallTextRenderResult rendered = definitions.TextPresentation.Resolve(rumor.TextKey, rumorContext);
            if (string.IsNullOrWhiteSpace(rendered.Text))
                continue;
            string cleaned = CollapseWhitespace(rendered.Text);
            entries.Add(new(
                Id: string.Create(CultureInfo.InvariantCulture, $"notice:sign:{rumor.Index}"),
                Kind: DaggerfallNoticeEntryKind.SignRumor,
                Heading: string.IsNullOrWhiteSpace(f1) ? "Public Broadsheet" : $"Word of {f1}",
                Body: cleaned,
                Region: region,
                FactionId: rumor.Faction1 > 0 ? rumor.Faction1 : null));
        }
    }

    private string? ResolveFactionName(int? factionId) =>
        factionId is int id && id > 0 && definitions.Factions.Factions.TryGetValue(id, out DaggerfallFactionDefinition? def)
            ? def.Name
            : null;

    private static string FormatCause(DaggerfallOfficeOverrideSave over) =>
        !string.IsNullOrWhiteSpace(over.CauseDetail)
            ? over.CauseDetail
            : over.Cause switch
            {
                DaggerfallOfficeChangeCause.Death => "was slain",
                DaggerfallOfficeChangeCause.CardMove => "was unseated",
                DaggerfallOfficeChangeCause.Quest => "stepped aside",
                _ => "succeeded",
            };

    private static string KindLabel(DaggerfallNoticeEntryKind kind) => kind switch
    {
        DaggerfallNoticeEntryKind.Decree => "Decree",
        DaggerfallNoticeEntryKind.Bounty => "Bounty",
        DaggerfallNoticeEntryKind.OfficeNotice => "Proclamation",
        _ => "Notice",
    };

    private static string Capitalize(string text) =>
        string.IsNullOrWhiteSpace(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
