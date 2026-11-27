using System.Globalization;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>One household grudge, permanent blood feud, mourning status, or goodwill entry in the player journal.</summary>
internal sealed record DaggerfallSocialJournalHouseholdEntry(
    string Name,
    string Tier,
    string Standing,
    string Status,
    bool IsFeud,
    string? SettlementHint);

/// <summary>One active or fading social vouch currently held by the player.</summary>
internal sealed record DaggerfallSocialJournalVouchEntry(
    string TargetName,
    string VoucherName,
    string Kind,
    string Status);

/// <summary>One character-relevant regional, guild, or faction standing entry in the player journal.</summary>
internal sealed record DaggerfallSocialJournalFactionEntry(
    string Name,
    string Standing,
    string Detail);

/// <summary>Player-facing Social / Relations journal page projection (Den #11163).</summary>
internal sealed record DaggerfallSocialJournalView(
    IReadOnlyList<DaggerfallSocialJournalHouseholdEntry> Households,
    IReadOnlyList<DaggerfallSocialJournalVouchEntry> Vouches,
    IReadOnlyList<DaggerfallSocialJournalFactionEntry> Factions);

/// <summary>
/// Projects character-relevant social state for the journal's Relations tab:
/// active household grudges and permanent blood feuds (with mourning state and settlement progress/hints),
/// notable active/fading vouches, and key faction/guild/regional standings.
/// Emits player-facing wording only — never raw internal IDs.
/// </summary>
internal sealed class DaggerfallSocialJournalProjection(
    DaggerfallDefinitions definitions,
    DaggerfallSocialState social,
    DaggerfallSocialConsequences socialConsequences,
    DaggerfallFeudSettlement feudSettlement,
    DaggerfallIntroductions introductions,
    DaggerfallCrimeState crime,
    ActorsState actors,
    DaggerfallNpcRegistry npcs,
    DaggerfallActorLedger actorLedger,
    DaggerfallNames names,
    Func<DaggerfallSiteRecord?> activeSite,
    Func<ulong> worldSocialSeed,
    Func<int> currentDay,
    DaggerfallOfficeHolders? officeHolders = null,
    DaggerfallOfficeLedger? officeLedger = null,
    Func<string?, string?>? describeVendetta = null)
{
    private const int MaximumFactionRows = 12;

    internal DaggerfallSocialJournalView Read()
    {
        DaggerfallSiteRecord? site = activeSite();
        int day = currentDay();
        ulong seed = worldSocialSeed();

        return new DaggerfallSocialJournalView(
            ReadHouseholds(site, day, seed),
            ReadVouches(site),
            ReadFactions(site));
    }

    private IReadOnlyList<DaggerfallSocialJournalHouseholdEntry> ReadHouseholds(
        DaggerfallSiteRecord? site,
        int day,
        ulong seed)
    {
        DaggerfallLocalHouseholdsSave captured = socialConsequences.Capture();
        if (captured.Standings.Length == 0)
            return [];

        List<(int Priority, string Name, DaggerfallSocialJournalHouseholdEntry Entry)> rows = [];
        foreach (DaggerfallLocalHouseholdStandingSave standing in captured.Standings)
        {
            DaggerfallHouseholdScope scope = DaggerfallHouseholds.ScopeOf(standing.FamilyId);
            if (scope == DaggerfallHouseholdScope.None)
                continue;

            int effectiveStanding = socialConsequences.HouseholdStandingDelta(standing.FamilyId, site);
            bool feudActive = standing.FeudActive
                || effectiveStanding <= DaggerfallIntroductions.BloodFeudDispositionThreshold
                || standing.StandingDelta <= DaggerfallIntroductions.BloodFeudDispositionThreshold;
            int? mourningDaysLeft = standing.MourningUntilDay is int until && until >= day
                ? Math.Max(1, until - day + 1)
                : null;

            // Only show rows relevant to the character: active feuds, grudges, mourning, or notable goodwill.
            int displayStanding = effectiveStanding != 0 ? effectiveStanding : standing.StandingDelta;
            if (!feudActive && displayStanding == 0 && mourningDaysLeft is null)
                continue;

            string label = FormatHouseholdName(standing.FamilyId, standing.Location, seed);
            string tier = scope switch
            {
                DaggerfallHouseholdScope.Dynasty => "Royal Dynasty",
                DaggerfallHouseholdScope.GreatHouse => "Great House",
                _ => "Local Household",
            };
            string standingText = FormatHouseholdStanding(displayStanding, feudActive);
            string status = BuildHouseholdStatus(standing, displayStanding, feudActive, mourningDaysLeft, describeVendetta?.Invoke(standing.FamilyId));
            string? hint = BuildSettlementHint(standing.FamilyId, site, feudActive, displayStanding);

            int priority = feudActive ? 0 : displayStanding < 0 ? 1 : mourningDaysLeft is not null ? 2 : 3;
            rows.Add((priority, label, new DaggerfallSocialJournalHouseholdEntry(
                Name: label,
                Tier: tier,
                Standing: standingText,
                Status: status,
                IsFeud: feudActive,
                SettlementHint: hint)));
        }

        return [.. rows
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Entry)];
    }

    private IReadOnlyList<DaggerfallSocialJournalVouchEntry> ReadVouches(DaggerfallSiteRecord? site)
    {
        HashSet<long> targetIds = [];
        foreach (ActorLedgerEntry entry in actorLedger.All)
        {
            if (entry.Social?.Vouches is { Length: > 0 } || entry.Referral?.Vouches is { Length: > 0 })
                targetIds.Add(entry.DurableId);
        }
        foreach (ActorState live in actors.All)
        {
            if (live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay) && overlay.Vouches.Count > 0)
                targetIds.Add(live.DurableId);
        }
        if (targetIds.Count == 0)
            return [];

        List<(int Priority, string Target, DaggerfallSocialJournalVouchEntry Entry)> rows = [];
        foreach (long targetId in targetIds)
        {
            IReadOnlyList<ActorVouchRecord> vouches = introductions.ReadVouches(targetId);
            if (vouches.Count == 0)
                continue;

            string targetName = ResolveActorDisplayName(targetId);
            foreach (ActorVouchRecord vouch in vouches)
            {
                DaggerfallVouchEvaluation eval = introductions.EvaluateVouch(vouch, site);
                if (!eval.IsUsable)
                    continue;

                string voucherName = !string.IsNullOrWhiteSpace(vouch.ReferrerName)
                    ? vouch.ReferrerName!
                    : ResolveActorDisplayName(vouch.ReferrerDurableId);
                string kind = vouch.Kind switch
                {
                    ActorVouchKind.Court => "Court Introduction",
                    ActorVouchKind.Guild => "Guild Reference",
                    ActorVouchKind.Trade => "Trade Reference",
                    _ => "Personal Introduction",
                };
                string status = eval.Status == DaggerfallVouchStatus.Fading
                    ? eval.DaysUntilLapse is int fadeLeft
                        ? $"Fading ({fadeLeft} {(fadeLeft == 1 ? "day" : "days")} remaining — renew soon)"
                        : "Fading — renew soon"
                    : eval.DaysUntilLapse is int activeLeft
                        ? $"Active ({activeLeft} {(activeLeft == 1 ? "day" : "days")} remaining)"
                        : "Active";
                int priority = (eval.Status == DaggerfallVouchStatus.Active ? 0 : 10) + (3 - (int)vouch.Kind);
                rows.Add((priority, targetName, new DaggerfallSocialJournalVouchEntry(targetName, voucherName, kind, status)));
            }
        }

        return [.. rows
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Target, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Entry.VoucherName, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Entry)];
    }

    private IReadOnlyList<DaggerfallSocialJournalFactionEntry> ReadFactions(DaggerfallSiteRecord? site)
    {
        List<(int Priority, int AbsRep, string Name, DaggerfallSocialJournalFactionEntry Entry)> rows = [];
        HashSet<int> coveredFactions = [];

        // 1. Guild & order memberships are always relevant to the character.
        foreach (DaggerfallSocialAffiliationView aff in social.ReadAffiliations())
        {
            if (aff.FactionId > 0)
                coveredFactions.Add(aff.FactionId);
            string detail = aff.Rank > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Member — Rank {aff.Rank}")
                : "Member";
            rows.Add((0, Math.Abs(aff.Reputation), aff.Faction, new DaggerfallSocialJournalFactionEntry(
                Name: aff.Faction,
                Standing: FormatReputationWord(aff.Reputation),
                Detail: detail)));
        }

        // 2. Current region and any regions where the player has non-zero standing or banishment.
        HashSet<int> regionsToRead = [.. crime.BanishedRegions];
        if (site is not null)
            regionsToRead.Add(site.Id.Region);
        foreach ((int region, int rep) in social.ReadRegionalReputations(regionsToRead))
        {
            bool isCurrent = site is not null && site.Id.Region == region;
            bool isBanished = crime.BanishedRegions.Contains(region);
            if (!isCurrent && !isBanished && rep == 0)
                continue;

            string regionName = definitions.BuildingNames.RegionName(region);
            if (string.IsNullOrWhiteSpace(regionName))
                continue;
            string detail = isBanished
                ? (isCurrent ? "Current region — banished by law" : "Banished by law")
                : isCurrent
                    ? "Current region"
                    : "Regional standing";
            rows.Add((isBanished ? 1 : isCurrent ? 2 : 4, Math.Abs(rep), regionName, new DaggerfallSocialJournalFactionEntry(
                Name: $"{regionName} (Region)",
                Standing: FormatReputationWord(rep),
                Detail: detail)));
        }

        // 3. Notable non-zero faction standings beyond memberships (excluding raw sub-records with empty names).
        DaggerfallSocialSave socialSave = social.Capture();
        foreach (DaggerfallFactionReputationSave entry in socialSave.Factions)
        {
            if (entry.Value == 0 || coveredFactions.Contains(entry.FactionId))
                continue;
            if (!definitions.Factions.Factions.TryGetValue(entry.FactionId, out DaggerfallFactionDefinition? def)
                || string.IsNullOrWhiteSpace(def.Name))
                continue;
            if (Math.Abs(entry.Value) < 5)
                continue;
            string detail = def.TypeName is { Length: > 0 } typeName && !string.Equals(typeName, "None", StringComparison.OrdinalIgnoreCase)
                ? typeName
                : "Notable standing";
            rows.Add((3, Math.Abs(entry.Value), def.Name, new DaggerfallSocialJournalFactionEntry(
                Name: def.Name,
                Standing: FormatReputationWord(entry.Value),
                Detail: detail)));
        }

        return [.. rows
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.AbsRep)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumFactionRows)
            .Select(r => r.Entry)];
    }

    private string FormatHouseholdName(string familyId, string locationFallback, ulong seed)
    {
        string? formatted = DaggerfallHouseholds.FormatLabel(
            familyId,
            definitions,
            names.Surname,
            (bank, id) => names.FirstName(bank, false, id),
            seed);
        if (!string.IsNullOrWhiteSpace(formatted))
            return formatted;
        return !string.IsNullOrWhiteSpace(locationFallback)
            ? $"Household of {locationFallback}"
            : "Local Household";
    }

    private static string FormatHouseholdStanding(int standing, bool feudActive)
    {
        string sign = standing > 0 ? "+" : string.Empty;
        string word = feudActive
            ? "Blood Feud"
            : standing <= -10
                ? "Hostile"
                : standing < 0
                    ? "Grudge"
                    : standing >= 15
                        ? "Allied"
                        : standing > 0
                            ? "Friendly"
                            : "Neutral";
        return string.Create(CultureInfo.InvariantCulture, $"{word} ({sign}{standing})");
    }

    private static string BuildHouseholdStatus(
        DaggerfallLocalHouseholdStandingSave standing,
        int effectiveStanding,
        bool feudActive,
        int? mourningDaysLeft,
        string? activeVendetta = null)
    {
        List<string> parts = [];
        if (feudActive)
        {
            parts.Add(standing.TruceMediated
                ? !string.IsNullOrWhiteSpace(standing.MediatedByName)
                    ? $"Blood feud — truce mediated by {standing.MediatedByName}"
                    : "Blood feud — truce mediated"
                : "Permanent blood feud (refuses direct talks)");
            if (!standing.TruceMediated && !string.IsNullOrWhiteSpace(activeVendetta))
                parts.Add(activeVendetta);
        }
        else if (effectiveStanding < 0)
        {
            parts.Add("Active grudge (decays over time)");
        }
        else if (effectiveStanding > 0)
        {
            parts.Add("Household goodwill");
        }

        if (mourningDaysLeft is int days)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"In mourning ({days} {(days == 1 ? "day" : "days")} remaining)"));
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "Observed";
    }

    private string? BuildSettlementHint(string familyId, DaggerfallSiteRecord? site, bool feudActive, int displayStanding)
    {
        if (!feudActive && displayStanding >= 0)
            return null;

        // Pass atSite: null so the player's own journal always reflects canonical settlement progress and wergild
        // even after travelling outside a local household's home town (where dialogue availability is site-local).
        DaggerfallFeudSettlementQuote? quote = feudSettlement.CalculateQuote(familyId, atSite: null);
        if (quote is null)
            return null;

        List<string> progress = [];
        if (quote.TruceMediated)
            progress.Add("Truce mediated");
        if (quote.ReconciliationServiceCompleted)
            progress.Add("Reconciliation service completed");
        if (quote.PardonGranted)
            progress.Add(!string.IsNullOrWhiteSpace(quote.PardonGrantedBy)
                ? $"Pardon granted ({quote.PardonGrantedBy})"
                : "Court/temple pardon granted");

        string wergildTier = quote.FeudActive
            ? string.Create(CultureInfo.InvariantCulture, $"Wergild: {quote.EffectiveWergildGold:N0} gp (blood price)")
            : string.Create(CultureInfo.InvariantCulture, $"Wergild: {quote.EffectiveWergildGold:N0} gp");
        progress.Add(wergildTier);

        string nextStep = quote.FeudActive
            ? !quote.TruceMediated
                ? "Seek a trusted ally, patron, or local authority to mediate a truce before paying wergild."
                : !quote.ReconciliationServiceCompleted || !quote.PardonGranted
                    ? "Pay wergild through kin or a temple/court arbiter; reconciliation service or a pardon reduces the blood price."
                    : "All reductions applied — pay the remaining wergild to end the blood feud."
            : "Pay wergild through kin or an arbiter, or wait for the minor grudge to fade with time.";

        return $"{string.Join(" · ", progress)}. {nextStep}";
    }

    private string ResolveActorDisplayName(long actorId)
    {
        if (npcs.TryGet(actorId, out DaggerfallNpc? npc) && !string.IsNullOrWhiteSpace(npc.DisplayName))
            return npc.DisplayName!;
        if (actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) && entry is not null)
        {
            if (!string.IsNullOrWhiteSpace(entry.Social?.DisplayNameOverride))
                return entry.Social.DisplayNameOverride!;
            if (!string.IsNullOrWhiteSpace(entry.Referral?.DisplayNameOverride))
                return entry.Referral.DisplayNameOverride!;
            if (entry.Origin.Kind == ActorOriginKind.CastIndividual
                && !string.IsNullOrWhiteSpace(entry.Origin.CastId)
                && definitions.TryGetCastOrNoble(entry.Origin.CastId, out DaggerfallCastIndividualDefinition? cast)
                && cast is not null
                && !string.IsNullOrWhiteSpace(cast.Name))
            {
                return cast.Name;
            }
        }
        if (officeHolders is not null)
        {
            if (activeSite() is { } current && officeHolders.ResolveRegionalRuler(current.Id.Region) is { ActorId: long activeRulerId, DisplayName: { Length: > 0 } activeRulerName }
                && activeRulerId == actorId)
            {
                return activeRulerName;
            }
            for (int region = 0; region <= 61; region++)
            {
                if (officeHolders.ResolveRegionalRuler(region) is { ActorId: long rulerId, DisplayName: { Length: > 0 } rulerName }
                    && rulerId == actorId)
                {
                    return rulerName;
                }
            }
        }
        if (officeLedger is not null)
        {
            DaggerfallOfficeLedgerSave ledgerSave = officeLedger.Capture();
            foreach (DaggerfallOfficeOverrideSave ov in ledgerSave.Overrides)
            {
                if (ov.HolderActorId == actorId && !string.IsNullOrWhiteSpace(ov.HolderDisplayName))
                    return ov.HolderDisplayName!;
            }
            if (ledgerSave.Exiles is { Length: > 0 })
            {
                foreach (DaggerfallOfficeExileSave exile in ledgerSave.Exiles)
                {
                    if (exile.ActorId == actorId && !string.IsNullOrWhiteSpace(exile.DisplayName))
                        return exile.DisplayName;
                }
            }
            if (activeSite() is { } active)
            {
                foreach (DaggerfallLocalOffice office in officeLedger.ResolveLocalOffices(active))
                {
                    if (office.HolderActorId == actorId && !string.IsNullOrWhiteSpace(office.DisplayName))
                        return office.DisplayName;
                }
            }
            if (actorLedger.TryGet(actorId, out ActorLedgerEntry? officeEntry)
                && officeEntry?.Origin.SiteProfile is { } originProfileKey)
            {
                DaggerfallSiteId originSiteId = originProfileKey.Require().Site;
                DaggerfallSiteRecord? originSite = definitions.Locations.Records.FirstOrDefault(r => r.Id == originSiteId);
                if (originSite is not null)
                {
                    foreach (DaggerfallLocalOffice office in officeLedger.ResolveLocalOffices(originSite))
                    {
                        if (office.HolderActorId == actorId && !string.IsNullOrWhiteSpace(office.DisplayName))
                            return office.DisplayName;
                    }
                }
            }
        }
        return "Local Contact";
    }

    private static string FormatReputationWord(int reputation)
    {
        string sign = reputation > 0 ? "+" : string.Empty;
        string word = reputation switch
        {
            >= 40 => "Revered",
            >= 20 => "Honored",
            >= 10 => "Respected",
            > 0 => "Liked",
            0 => "Common Citizen",
            > -10 => "Disliked",
            > -25 => "Despised",
            _ => "Hated",
        };
        return string.Create(CultureInfo.InvariantCulture, $"{word} ({sign}{reputation})");
    }
}
