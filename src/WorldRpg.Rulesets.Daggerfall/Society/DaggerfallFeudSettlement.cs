using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Live settlement quote for one aggrieved or feuding household across any tier.</summary>
internal sealed record DaggerfallFeudSettlementQuote(
    string FamilyId,
    string HouseholdLabel,
    DaggerfallHouseholdScope Scope,
    int StandingDelta,
    bool FeudActive,
    int MurderCount,
    ulong BaseWergildGold,
    ulong EffectiveWergildGold,
    bool TruceMediated,
    string? MediatedByName,
    bool ReconciliationServiceCompleted,
    bool PardonGranted,
    string? PardonGrantedBy,
    ulong PardonFeeGold)
{
    /// <summary>True when a feuding household refuses direct talks until a trusted intermediary brokers a truce.</summary>
    internal bool RequiresTruceForDirectTalk => FeudActive && !TruceMediated;
}

/// <summary>One household settlement action offered by a speaker in dialogue.</summary>
internal sealed record DaggerfallFeudSettlementOption(
    string FamilyId,
    string HouseholdLabel,
    DaggerfallFeudSettlementQuote Quote,
    bool ViaArbiter);

/// <summary>Result of attempting wergild payment, truce mediation, reconciliation service, or a formal pardon.</summary>
internal sealed record DaggerfallFeudSettlementResult(
    bool Accepted,
    string Question,
    string Message,
    ulong GoldSpent = 0UL);

/// <summary>
/// Blood-feud &amp; household-grudge settlement owner in the <c>Society</c> domain (Den #11160):
/// 1. Wergild / blood money paid via a living household member (when minor grudge or mediated truce) or a neutral
///    temple/court arbiter, with moderate scaling for minor grudges and extraordinarily high exponential costs for
///    murder blood feuds across town-local, Great House, and royal dynasty tiers.
/// 2. Intermediary mediation: a trusted ally, patron, office holder, or liege vouches to broker a truce, unlocking
///    direct settlement dialogue with a feuding household that otherwise refuses to speak.
/// 3. Reconciliation service or formal court/temple pardon: reduces (never removes outright) the exponential wergild
///    cost so active atonement combines with payment to clear the feud.
/// </summary>
internal sealed class DaggerfallFeudSettlement(
    DaggerfallSocialConsequences socialConsequences,
    DaggerfallSocialState social,
    DaggerfallIntroductions introductions,
    DaggerfallOfficeHolders officeHolders,
    DaggerfallOfficeLedger officeLedger,
    DaggerfallCrimeState crime,
    DaggerfallDefinitions definitions,
    DaggerfallNames names,
    Func<ulong> worldSocialSeed,
    Func<ulong> playerGold,
    Func<ulong, bool> spendGold,
    Func<int> currentDay,
    Func<long> currentMinute,
    Action<long>? passTime = null)
{
    internal const long ReconciliationServiceSeconds = 4L * 60L * 60L;
    internal const int LocalMediatorReactionThreshold = 10;
    internal const int OfficeMediatorReactionThreshold = 5;
    internal const int GreatHouseMediatorReactionThreshold = 10;
    internal const int DynastyMediatorReactionThreshold = 15;
    internal const int PardonStandingRequirement = 5;
    private const ulong MinimumFeudWergildGold = 100UL;
    private const ulong MinimumMinorWergildGold = 25UL;

    /// <summary>Computes the authoritative settlement quote for <paramref name="familyId"/>, or null if no grudge/feud exists.</summary>
    internal DaggerfallFeudSettlementQuote? CalculateQuote(string? familyId, DaggerfallSiteRecord? atSite = null)
    {
        if (string.IsNullOrWhiteSpace(familyId)
            || !socialConsequences.TryGetHouseholdSnapshot(familyId, out DaggerfallHouseholdSnapshot snap))
        {
            return null;
        }
        int effectiveStanding = socialConsequences.HouseholdStandingDelta(familyId, atSite);
        bool knownFeud = socialConsequences.IsFeudActiveAt(familyId, atSite);
        if (!knownFeud && effectiveStanding >= 0 && !snap.FeudActive && snap.StandingDelta >= 0)
            return null;

        // Respect courier delay at distant sites when querying with a concrete site.
        if (atSite is not null && !knownFeud && effectiveStanding >= 0)
            return null;

        int severity = Math.Max(1, -Math.Min(effectiveStanding, snap.StandingDelta));
        bool feudActive = snap.FeudActive || knownFeud;
        DaggerfallSocialTuning tuning = socialConsequences.Tuning;
        ulong baseGold = ComputeBaseWergild(snap.Scope, severity, feudActive, snap.MurderCount, tuning);
        ulong effectiveGold = ApplyCostReductions(baseGold, feudActive, snap.ReconciliationServiceCompleted, snap.PardonGranted, tuning);
        ulong pardonFee = ComputePardonFee(snap.Scope, tuning);

        return new DaggerfallFeudSettlementQuote(
            FamilyId: familyId,
            HouseholdLabel: FormatHouseholdLabel(familyId),
            Scope: snap.Scope,
            StandingDelta: effectiveStanding != 0 ? effectiveStanding : snap.StandingDelta,
            FeudActive: feudActive,
            MurderCount: snap.MurderCount,
            BaseWergildGold: baseGold,
            EffectiveWergildGold: effectiveGold,
            TruceMediated: snap.TruceMediated,
            MediatedByName: snap.MediatedByName,
            ReconciliationServiceCompleted: snap.ReconciliationServiceCompleted,
            PardonGranted: snap.PardonGranted,
            PardonGrantedBy: snap.PardonGrantedBy,
            PardonFeeGold: pardonFee);
    }

    /// <summary>Computes unreduced wergild gold: moderate for minor grudges, extraordinarily high and exponential for murder blood feuds.</summary>
    internal static ulong ComputeBaseWergild(
        DaggerfallHouseholdScope scope,
        int severityPoints,
        bool feudActive,
        int murderCount,
        DaggerfallSocialTuning tuning)
    {
        int clampedSeverity = Math.Max(1, severityPoints);
        if (!feudActive && murderCount <= 0)
        {
            ulong tierScale = scope switch
            {
                DaggerfallHouseholdScope.Local => 1UL,
                DaggerfallHouseholdScope.GreatHouse => 3UL,
                DaggerfallHouseholdScope.Dynasty => 8UL,
                _ => 1UL,
            };
            return Math.Max(
                MinimumMinorWergildGold,
                checked((ulong)tuning.MinorGrudgeBaseWergildPerPoint * (ulong)clampedSeverity * tierScale));
        }

        ulong feudBase = scope switch
        {
            DaggerfallHouseholdScope.Local => (ulong)tuning.LocalBloodFeudBaseWergild,
            DaggerfallHouseholdScope.GreatHouse => (ulong)tuning.GreatHouseBloodFeudBaseWergild,
            DaggerfallHouseholdScope.Dynasty => (ulong)tuning.DynastyBloodFeudBaseWergild,
            _ => (ulong)tuning.LocalBloodFeudBaseWergild,
        };

        if (murderCount <= 0)
        {
            // Non-lethal blood feud (e.g. cumulative severe assaults/crimes crossing -15): half murder base + severity.
            ulong nonLethalBase = Math.Max(MinimumFeudWergildGold, feudBase / 2UL);
            ulong surcharge = (ulong)clampedSeverity * (ulong)tuning.MinorGrudgeBaseWergildPerPoint;
            return checked(nonLethalBase + surcharge);
        }

        // Exponential blood-money scaling per attributed murder: base * (expBase ^ (murders - 1)).
        int exponent = Math.Min(6, Math.Max(0, murderCount - 1));
        double multiplier = Math.Pow(tuning.BloodFeudMurderExponentBase, exponent);
        double raw = feudBase * multiplier;
        ulong rounded = raw >= 1_000_000_000d ? 1_000_000_000UL : (ulong)Math.Round(raw, MidpointRounding.AwayFromZero);
        return Math.Max(MinimumFeudWergildGold, rounded);
    }

    /// <summary>Applies reconciliation-service and court/temple-pardon reductions (never removing the cost outright).</summary>
    internal static ulong ApplyCostReductions(
        ulong baseGold,
        bool feudActive,
        bool reconciliationServiceCompleted,
        bool pardonGranted,
        DaggerfallSocialTuning tuning)
    {
        double factor = 1.0;
        if (reconciliationServiceCompleted)
            factor *= tuning.ReconciliationServiceCostMultiplier;
        if (pardonGranted)
            factor *= tuning.PardonCostMultiplier;
        ulong floor = feudActive ? MinimumFeudWergildGold : MinimumMinorWergildGold;
        ulong reduced = (ulong)Math.Round(baseGold * factor, MidpointRounding.AwayFromZero);
        return Math.Max(floor, Math.Min(baseGold, reduced));
    }

    internal static ulong ComputePardonFee(DaggerfallHouseholdScope scope, DaggerfallSocialTuning tuning)
    {
        ulong scale = scope switch
        {
            DaggerfallHouseholdScope.Local => 1UL,
            DaggerfallHouseholdScope.GreatHouse => 2UL,
            DaggerfallHouseholdScope.Dynasty => 4UL,
            _ => 1UL,
        };
        return checked((ulong)tuning.PardonCourtFeeGold * scale);
    }

    /// <summary>True when <paramref name="speaker"/> is a neutral temple priest or court/law official who can arbitrate wergild or pardons.</summary>
    internal bool IsNeutralArbiter(DaggerfallNpc speaker)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        if (speaker.Services.Any(s => s is "donate" or "cure-disease" or "temple"))
            return true;
        if (speaker.Role.Contains("priest", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("cleric", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("temple", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("guard", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("captain", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("watch", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("factor", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("regent", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("ruler", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("judge", StringComparison.OrdinalIgnoreCase)
            || speaker.Role.Contains("arbiter", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        int factionId = social.ResolveNpcReactionFactionId(speaker);
        if (factionId > 0 && definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction))
        {
            // Temple (Type 2 or GuildGroup 10), Province/Court/Official (Type 7, 14, 5), or Court Nobility (SocialGroup 3, non-People).
            if (faction.Type is 2 or 5 or 7 or 14
                || faction.GuildGroup == 10
                || (faction.SocialGroup == 3 && faction.Type != 15 && faction.GuildGroup != 9))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether <paramref name="mediator"/> can vouch to broker a truce with <paramref name="targetFamilyId"/>.</summary>
    internal bool CanMediateForHousehold(DaggerfallNpc mediator, string targetFamilyId, DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        if (string.IsNullOrWhiteSpace(targetFamilyId))
            return false;
        if (string.Equals(mediator.FamilyId, targetFamilyId, StringComparison.Ordinal))
            return false;
        if (introductions.IsInBloodFeud(mediator, site))
            return false;
        if (CalculateQuote(targetFamilyId, site) is not { FeudActive: true, TruceMediated: false } quote)
            return false;

        int reaction = social.ReactionForNpc(mediator).Value;
        bool holdsOffice = IsActiveOfficeHolderOrLiege(mediator, site);
        DaggerfallSocialProminence prominence = introductions.ClassifyProminence(mediator);

        return quote.Scope switch
        {
            DaggerfallHouseholdScope.Local =>
                reaction >= LocalMediatorReactionThreshold
                || (holdsOffice && reaction >= OfficeMediatorReactionThreshold),
            DaggerfallHouseholdScope.GreatHouse =>
                (holdsOffice && reaction >= OfficeMediatorReactionThreshold)
                || (prominence >= DaggerfallSocialProminence.MinorCourt && reaction >= OfficeMediatorReactionThreshold)
                || reaction >= GreatHouseMediatorReactionThreshold,
            DaggerfallHouseholdScope.Dynasty =>
                (holdsOffice && reaction >= GreatHouseMediatorReactionThreshold)
                || (prominence >= DaggerfallSocialProminence.MinorCourt && reaction >= GreatHouseMediatorReactionThreshold)
                || reaction >= DynastyMediatorReactionThreshold,
            _ => false,
        };
    }

    /// <summary>Builds a grounded greeting line naming any active household feud or grudge and hinting at settlement paths.</summary>
    internal string? BuildGreetingLine(DaggerfallNpc speaker, DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        if (string.IsNullOrWhiteSpace(speaker.FamilyId))
            return null;
        if (CalculateQuote(speaker.FamilyId, site) is not { } quote)
            return null;

        if (quote.FeudActive)
        {
            if (!quote.TruceMediated)
            {
                return $"There is a blood feud between you and {quote.HouseholdLabel}. My kin will hear no terms from your lips alone — seek a trusted intermediary to broker a truce, or petition a temple priest or court arbiter to arrange wergild ({quote.EffectiveWergildGold} gold), reconciliation service, or a formal pardon.";
            }

            List<string> hints = [];
            if (!quote.ReconciliationServiceCompleted)
                hints.Add("undertake reconciliation service");
            if (!quote.PardonGranted)
                hints.Add("secure a temple or court pardon");
            string reductionNote = hints.Count > 0
                ? $" You may still {string.Join(" or ", hints)} to lower the blood-price."
                : " Both your service and pardon are already weighed into the sum.";
            string mediator = string.IsNullOrWhiteSpace(quote.MediatedByName) ? "your intermediary" : quote.MediatedByName;
            return $"Only {mediator}'s truce keeps steel sheathed while we speak of our blood feud with {quote.HouseholdLabel}. Full wergild stands at {quote.EffectiveWergildGold} gold.{reductionNote}";
        }

        if (quote.StandingDelta < 0)
        {
            return $"{quote.HouseholdLabel} still holds a grievance against you. Pay {quote.EffectiveWergildGold} gold in restitution, or undertake reconciliation service to make amends.";
        }
        return null;
    }

    /// <summary>Lists households for which the player may pay wergild through <paramref name="speaker"/>.</summary>
    internal IReadOnlyList<DaggerfallFeudSettlementOption> ListWergildOptions(DaggerfallNpc speaker, DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        List<DaggerfallFeudSettlementOption> options = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(speaker.FamilyId)
            && CalculateQuote(speaker.FamilyId, site) is { } ownQuote
            && seen.Add(ownQuote.FamilyId))
        {
            // Direct option is listed even on unmediated feuds so selecting it returns the explicit refusal + settlement hints.
            options.Add(new(ownQuote.FamilyId, ownQuote.HouseholdLabel, ownQuote, ViaArbiter: false));
        }

        if (IsNeutralArbiter(speaker))
        {
            foreach (DaggerfallHouseholdSnapshot snap in socialConsequences.ReadAllSnapshots())
            {
                if (!seen.Add(snap.FamilyId))
                    continue;
                if (string.Equals(speaker.FamilyId, snap.FamilyId, StringComparison.Ordinal))
                    continue;
                if (CalculateQuote(snap.FamilyId, site) is { } quote)
                    options.Add(new(quote.FamilyId, quote.HouseholdLabel, quote, ViaArbiter: true));
            }
        }
        return options;
    }

    /// <summary>Lists unmediated blood-feud households that <paramref name="mediator"/> can broker a truce with.</summary>
    internal IReadOnlyList<DaggerfallFeudSettlementOption> ListMediationOptions(DaggerfallNpc mediator, DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        List<DaggerfallFeudSettlementOption> options = [];
        foreach (DaggerfallHouseholdSnapshot snap in socialConsequences.ReadAllSnapshots())
        {
            if (!CanMediateForHousehold(mediator, snap.FamilyId, site))
                continue;
            if (CalculateQuote(snap.FamilyId, site) is { } quote)
                options.Add(new(quote.FamilyId, quote.HouseholdLabel, quote, ViaArbiter: false));
        }
        return options;
    }

    /// <summary>Lists aggrieved/feuding households for which the player can undertake reconciliation service via <paramref name="speaker"/>.</summary>
    internal IReadOnlyList<DaggerfallFeudSettlementOption> ListReconciliationOptions(DaggerfallNpc speaker, DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        List<DaggerfallFeudSettlementOption> options = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(speaker.FamilyId)
            && CalculateQuote(speaker.FamilyId, site) is { ReconciliationServiceCompleted: false } ownQuote
            && (!ownQuote.FeudActive || ownQuote.TruceMediated)
            && seen.Add(ownQuote.FamilyId))
        {
            options.Add(new(ownQuote.FamilyId, ownQuote.HouseholdLabel, ownQuote, ViaArbiter: false));
        }

        if (IsNeutralArbiter(speaker))
        {
            foreach (DaggerfallHouseholdSnapshot snap in socialConsequences.ReadAllSnapshots())
            {
                if (!seen.Add(snap.FamilyId))
                    continue;
                if (string.Equals(speaker.FamilyId, snap.FamilyId, StringComparison.Ordinal))
                    continue;
                if (CalculateQuote(snap.FamilyId, site) is { ReconciliationServiceCompleted: false } quote)
                    options.Add(new(quote.FamilyId, quote.HouseholdLabel, quote, ViaArbiter: true));
            }
        }
        return options;
    }

    /// <summary>Lists aggrieved/feuding households for which a neutral temple/court arbiter can grant a formal pardon.</summary>
    internal IReadOnlyList<DaggerfallFeudSettlementOption> ListPardonOptions(DaggerfallNpc arbiter, DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(arbiter);
        if (!IsNeutralArbiter(arbiter))
            return [];
        List<DaggerfallFeudSettlementOption> options = [];
        foreach (DaggerfallHouseholdSnapshot snap in socialConsequences.ReadAllSnapshots())
        {
            if (string.Equals(arbiter.FamilyId, snap.FamilyId, StringComparison.Ordinal))
                continue;
            if (CalculateQuote(snap.FamilyId, site) is { PardonGranted: false } quote)
                options.Add(new(quote.FamilyId, quote.HouseholdLabel, quote, ViaArbiter: true));
        }
        return options;
    }

    /// <summary>Path 1: Pays wergild / restitution to settle a household grudge or blood feud.</summary>
    internal DaggerfallFeudSettlementResult PayWergild(DaggerfallNpc speaker, string familyId, DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        if (CalculateQuote(familyId, site) is not { } quote)
            return new(false, "I wish to settle my standing with that house.", "That household holds no active grievance against you.");

        string question = string.Create(
            CultureInfo.InvariantCulture,
            $"I am here to pay {quote.EffectiveWergildGold} gold in wergild to settle matters with {quote.HouseholdLabel}.");

        bool isKin = string.Equals(speaker.FamilyId, familyId, StringComparison.Ordinal);
        bool isArbiter = !isKin && IsNeutralArbiter(speaker);
        if (!isKin && !isArbiter)
            return new(false, question, $"Only kin of {quote.HouseholdLabel} or a temple or court arbiter can accept wergild for them.");

        if (isKin && quote.RequiresTruceForDirectTalk)
        {
            return new(
                false,
                question,
                $"{quote.HouseholdLabel} holds an open blood feud against you and refuses to take coin from your hand unmediated. Bring a trusted intermediary to broker a truce first, or pay the {quote.EffectiveWergildGold} gold wergild through a neutral temple priest or court arbiter.");
        }

        if (playerGold() < quote.EffectiveWergildGold)
        {
            List<string> discountHints = [];
            if (!quote.ReconciliationServiceCompleted)
                discountHints.Add("undertake reconciliation service");
            if (!quote.PardonGranted)
                discountHints.Add("obtain a court or temple pardon");
            string hint = discountHints.Count > 0
                ? $" You can {string.Join(" or ", discountHints)} to reduce the wergild."
                : string.Empty;
            return new(
                false,
                question,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The blood-price for {quote.HouseholdLabel} stands at {quote.EffectiveWergildGold} gold, and you do not carry enough.{hint}"));
        }

        if (!spendGold(quote.EffectiveWergildGold))
            return new(false, question, "You do not have enough gold on hand to pay the wergild.");

        string note = isArbiter
            ? $"Word spreads that {speaker.DisplayName ?? "an arbiter"} witnessed {quote.EffectiveWergildGold} gold in wergild paid to {quote.HouseholdLabel}, closing the feud."
            : $"Word spreads that {quote.EffectiveWergildGold} gold in wergild was paid directly to {quote.HouseholdLabel}, restoring peace.";
        socialConsequences.ClearHouseholdGrudgeAndFeud(familyId, site, currentMinute(), currentDay(), note);

        string reply = quote.FeudActive
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"The {quote.EffectiveWergildGold} gold wergild is weighed and accepted. The blood feud with {quote.HouseholdLabel} is closed, and their kin stand down.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Your {quote.EffectiveWergildGold} gold restitution is accepted. {quote.HouseholdLabel} puts the grievance behind them.");
        return new(true, question, reply, quote.EffectiveWergildGold);
    }

    /// <summary>Path 2: Trusted intermediary brokers a truce, unlocking direct settlement talks with a feuding household.</summary>
    internal DaggerfallFeudSettlementResult BrokerMediation(DaggerfallNpc mediator, string familyId, DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        if (CalculateQuote(familyId, site) is not { } quote)
            return new(false, "Will you mediate a truce for me?", "That household holds no active blood feud against you.");

        string question = $"Will you vouch for me and mediate a truce with {quote.HouseholdLabel}?";
        if (!quote.FeudActive)
            return new(false, question, $"{quote.HouseholdLabel} is not in a blood feud with you — you may speak with them directly.");
        if (quote.TruceMediated)
            return new(false, question, $"A truce is already open with {quote.HouseholdLabel}.");
        if (!CanMediateForHousehold(mediator, familyId, site))
            return new(false, question, $"I do not have the trust or standing with you to stake my word before {quote.HouseholdLabel}.");

        string mediatorName = mediator.DisplayName ?? mediator.Role;
        socialConsequences.RecordTruceMediated(familyId, mediatorName, currentDay());
        return new(
            true,
            question,
            string.Create(
                CultureInfo.InvariantCulture,
                $"I will send my seal to {quote.HouseholdLabel} and vouch for a parley. They will now hear you directly on wergild ({quote.EffectiveWergildGold} gold) and reconciliation service."));
    }

    /// <summary>Path 3a: Performs a reconciliation penance/service, reducing (not removing) the wergild cost.</summary>
    internal DaggerfallFeudSettlementResult PerformReconciliationService(DaggerfallNpc speaker, string familyId, DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        if (CalculateQuote(familyId, site) is not { } quote)
            return new(false, "Let me perform reconciliation service to make amends.", "That household holds no active grievance against you.");

        string question = $"I will perform reconciliation service to make amends to {quote.HouseholdLabel}.";
        if (quote.ReconciliationServiceCompleted)
            return new(false, question, $"Your reconciliation service for {quote.HouseholdLabel} has already been credited toward the wergild ({quote.EffectiveWergildGold} gold remaining).");

        bool isKin = string.Equals(speaker.FamilyId, familyId, StringComparison.Ordinal);
        bool isArbiter = !isKin && IsNeutralArbiter(speaker);
        if (!isKin && !isArbiter)
            return new(false, question, $"Speak with {quote.HouseholdLabel} under truce or with a temple or court arbiter to arrange reconciliation service.");
        if (isKin && quote.RequiresTruceForDirectTalk)
            return new(false, question, $"{quote.HouseholdLabel} refuses unmediated dealings while the blood feud stands. Have a trusted intermediary broker a truce or arrange penance through a temple or court arbiter.");

        passTime?.Invoke(ReconciliationServiceSeconds);
        socialConsequences.RecordReconciliationServiceCompleted(familyId, currentDay());
        DaggerfallFeudSettlementQuote updated = CalculateQuote(familyId, site)!;
        return new(
            true,
            question,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Your hours of restitution and service are witnessed on behalf of {updated.HouseholdLabel}. Blood-price is not erased by labor alone, but the wergild drops from {quote.EffectiveWergildGold} gold to {updated.EffectiveWergildGold} gold."));
    }

    /// <summary>Credits a completed quest or favour toward reconciliation service cost reduction for <paramref name="familyId"/>.</summary>
    internal bool CreditQuestReconciliation(string familyId) =>
        socialConsequences.RecordReconciliationServiceCompleted(familyId, currentDay());

    /// <summary>Path 3b: Neutral temple or court arbiter grants a formal pardon, reducing (not removing) the wergild cost.</summary>
    internal DaggerfallFeudSettlementResult RequestPardon(DaggerfallNpc arbiter, string familyId, DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(arbiter);
        if (CalculateQuote(familyId, site) is not { } quote)
            return new(false, "I petition for a formal pardon.", "That household holds no active grievance against you.");

        string question = string.Create(
            CultureInfo.InvariantCulture,
            $"I petition for a formal pardon ({quote.PardonFeeGold} gold writ fee) regarding {quote.HouseholdLabel}.");
        if (!IsNeutralArbiter(arbiter) || string.Equals(arbiter.FamilyId, familyId, StringComparison.Ordinal))
            return new(false, question, "Only a neutral temple priest or court official may seal a formal pardon.");
        if (quote.PardonGranted)
            return new(false, question, $"A formal pardon is already sealed for {quote.HouseholdLabel}; remaining wergild is {quote.EffectiveWergildGold} gold.");

        int region = site?.Id.Region ?? arbiter.Site.Region;
        if (region is >= 0 and <= 61 && crime.BanishedRegions.Contains(region))
            return new(false, question, "You stand banished in this region — clear your banishment before petitioning for a pardon.");

        int reaction = social.ReactionForNpc(arbiter).Value;
        int regionalRep = region is >= 0 and <= 61 ? social.RegionalReputation(region) : 0;
        int arbiterFaction = social.ResolveNpcReactionFactionId(arbiter);
        int factionRep = arbiterFaction > 0 && definitions.Factions.Factions.ContainsKey(arbiterFaction)
            ? social.FactionReputation(arbiterFaction)
            : 0;
        if (reaction < PardonStandingRequirement && regionalRep < PardonStandingRequirement && factionRep < PardonStandingRequirement)
        {
            return new(
                false,
                question,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The court and temples do not seal pardons for disreputable strangers — build standing of at least {PardonStandingRequirement} here first."));
        }

        if (quote.PardonFeeGold > 0 && playerGold() < quote.PardonFeeGold)
        {
            return new(
                false,
                question,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Sealing a formal writ of pardon requires {quote.PardonFeeGold} gold in court or temple dues."));
        }
        if (quote.PardonFeeGold > 0 && !spendGold(quote.PardonFeeGold))
            return new(false, question, "You do not have enough gold on hand for the writ of pardon.");

        string issuer = arbiter.DisplayName ?? "the court";
        socialConsequences.RecordPardonGranted(familyId, issuer, currentDay());
        DaggerfallFeudSettlementQuote updated = CalculateQuote(familyId, site)!;
        return new(
            true,
            question,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The writ of pardon is sealed by {issuer}. A pardon tempers the blood-price rather than erasing it: wergild owed to {updated.HouseholdLabel} drops from {quote.EffectiveWergildGold} gold to {updated.EffectiveWergildGold} gold."),
            quote.PardonFeeGold);
    }

    private bool IsActiveOfficeHolderOrLiege(DaggerfallNpc npc, DaggerfallSiteRecord? site)
    {
        if (site is not null)
        {
            foreach (DaggerfallLocalOffice office in officeLedger.ResolveLocalOffices(site))
            {
                if (!office.IsVacant && office.IsAlive && office.HolderActorId == npc.DurableId)
                    return true;
            }
        }
        int region = site?.Id.Region ?? npc.Site.Region;
        if (region is >= 0 and <= 61
            && officeHolders.ResolveRegionalRuler(region) is { HasLivingHolder: true, ActorId: long rulerId }
            && rulerId == npc.DurableId)
        {
            return true;
        }
        return false;
    }

    private string FormatHouseholdLabel(string familyId) =>
        DaggerfallHouseholds.FormatLabel(
            familyId,
            definitions,
            names.Surname,
            (bank, id) => names.FirstName(bank, false, id),
            worldSocialSeed()) ?? familyId;
}
