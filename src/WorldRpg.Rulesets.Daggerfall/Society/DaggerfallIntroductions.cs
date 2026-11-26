using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Social prominence tier governing how many introductions a stranger needs to approach someone for work or audience.</summary>
internal enum DaggerfallSocialProminence
{
    /// <summary>Townsfolk, ordinary merchants, guild rank-and-file, and knightly order contacts: directly approachable.</summary>
    Ordinary = 0,

    /// <summary>Minor courtiers, palace attendants, and guarded noble retainers: need 1 step (or social skill / home standing).</summary>
    MinorCourt = 1,

    /// <summary>Senior nobles, court favourites, and notable faction figures: need 2 steps (silver-tongued characters can approach directly).</summary>
    CourtFavourite = 2,

    /// <summary>Regional rulers, monarchs, and great lords: need 3 steps and always require at least one trusted intermediary.</summary>
    Prominent = 3,
}

/// <summary>Result of evaluating whether the player currently has social access to approach <c>target</c> for work or court business.</summary>
internal sealed record DaggerfallIntroductionAccess(
    bool Allowed,
    DaggerfallSocialProminence Prominence,
    int RequiredReach,
    int AchievedReach,
    int SocialSkillSkip,
    int HomeStandingBonus,
    ActorVouchRecord? QualifyingVouch,
    string? DenialReason);

/// <summary>One concrete person a speaker knows well enough to vouch the player to.</summary>
internal sealed record DaggerfallIntroductionTarget(
    long TargetDurableId,
    string TargetName,
    int TargetFactionId,
    DaggerfallSocialProminence TargetProminence,
    ActorVouchKind VouchKind,
    bool AlreadyVouched);

/// <summary>Outcome of asking a speaker to vouch for the player to a target person.</summary>
internal sealed record DaggerfallIntroductionResult(
    bool Accepted,
    bool AlreadyVouched,
    long TargetDurableId,
    string TargetName,
    ActorVouchRecord? Vouch,
    string Message);

/// <summary>Outcome of attempting to curry favour (butter up) a person in dialogue.</summary>
internal sealed record DaggerfallFavourResult(
    bool Applied,
    bool Succeeded,
    int DispositionDelta,
    string Message);

/// <summary>
/// Social introductions and vouching owner (Patch Q7/Q10):
/// classifies social prominence from the faction catalog and office holders, evaluates whether a stranger has
/// access to guarded/court/prominent people (with social-skill skips and home-region standing, while always
/// requiring at least one trusted intermediary for prominent lords/rulers), issues durable structured
/// <see cref="ActorVouchRecord"/>s into <see cref="DaggerfallActorLedger"/>, grants trusted-vouch terms bonuses
/// (trade standing and deposit waivers), and handles once-per-day social-skill favour checks.
/// </summary>
internal sealed class DaggerfallIntroductions(
    DaggerfallDefinitions definitions,
    DaggerfallSocialState social,
    DaggerfallOfficeHolders officeHolders,
    ActorsState actors,
    DaggerfallNpcRegistry npcs,
    DaggerfallActorLedger actorLedger,
    DaggerfallSkillUseReactions skillUses,
    IRandomService random,
    Func<DaggerfallSiteRecord?> activeSite,
    Func<int> currentDay,
    Func<bool> insideCastle,
    DaggerfallOfficeLedger? officeLedger = null,
    Func<DaggerfallSiteId, DaggerfallSiteRecord?>? findSite = null)
{
    private const string RandomScope = "daggerfall.introductions.v1";

    internal const int TrustedVouchStandingBonus = 15;
    internal const int QuestSuccessDispositionBonus = 8;
    internal const int QuestFailureDispositionPenalty = -4;
    internal const int FavourSuccessDispositionBonus = 20;
    internal const int FavourRudeNoblePenalty = -2;
    internal const int BloodFeudDispositionThreshold = -15;

    private const int ProvinceFactionType = 7;
    private const int IndividualFactionType = 4;
    private const int OfficialFactionType = 5;
    private const int CourtsFactionType = 14;
    private const int PeopleFactionType = 15;
    private const int CommonersSocialGroup = 0;
    private const int MerchantsSocialGroup = 1;
    private const int ScholarsSocialGroup = 2;
    private const int NobilitySocialGroup = 3;
    private const int UnderworldSocialGroup = 4;
    private const int KnightlyOrderGuildGroup = 9;

    private static readonly string[] SovereignTitles =
        ["King ", "Queen ", "Emperor ", "Empress ", "Gortwog", "King of Worms"];

    private static readonly string[] NobleTitles =
        ["Lord ", "Lady ", "Duke ", "Duchess ", "Marquis ", "Marquise ", "Count ", "Countess ", "Baron ", "Baroness ", "Prince ", "Princess "];

    private Func<DaggerfallNpc, bool>? _isHireableContact;
    private Func<long, DaggerfallSiteRecord?, int>? _effectiveDisposition;
    private Func<string?, DaggerfallSiteRecord?, int>? _householdStandingDelta;

    /// <summary>Binds the party-hire eligibility lookup so ordinary hireable companions/drivers appear in introduction dialogue.</summary>
    internal void BindHireableContactLookup(Func<DaggerfallNpc, bool> lookup) =>
        _isHireableContact = lookup ?? throw new ArgumentNullException(nameof(lookup));

    /// <summary>
    /// Binds effective disposition and arrived province-wide household standing lookups (from
    /// <see cref="DaggerfallSocialConsequences"/>) so introductions, access, and deposit waivers honor household blood feuds.
    /// </summary>
    internal void BindEffectiveDisposition(
        Func<long, DaggerfallSiteRecord?, int> effectiveDisposition,
        Func<string?, DaggerfallSiteRecord?, int>? householdStandingDelta = null)
    {
        _effectiveDisposition = effectiveDisposition ?? throw new ArgumentNullException(nameof(effectiveDisposition));
        _householdStandingDelta = householdStandingDelta;
    }

    /// <summary>Classifies a person's social prominence tier from their resolved faction, office, and castle context.</summary>
    internal DaggerfallSocialProminence ClassifyProminence(DaggerfallNpc npc, bool? inCastleOverride = null)
    {
        ArgumentNullException.ThrowIfNull(npc);
        int factionId = social.ResolveNpcReactionFactionId(npc);
        bool inCastle = inCastleOverride ?? insideCastle();
        int region = ResolveRegion(npc, factionId);
        return ClassifyProminenceCore(npc.DurableId, npc.DisplayName, npc.Kind, npc.Services, factionId, region, inCastle);
    }

    /// <summary>Classifies a faction's baseline prominence when no live NPC instance is required (e.g. off-scene referrer).</summary>
    internal DaggerfallSocialProminence ClassifyFactionProminence(int factionId, long? actorId = null, string? displayName = null, int? regionHint = null) =>
        ClassifyProminenceCore(actorId ?? 0L, displayName, DaggerfallNpcKind.Static, [], factionId, regionHint ?? -1, inCastle: false);

    /// <summary>
    /// Computes how many introduction steps the player's social skills (<c>etiquette</c>, <c>streetwise</c>, <c>personality</c>)
    /// can skip: 0 (blunt), 1 (smooth), or 2 (very silver-tongued).
    /// </summary>
    internal int ComputeSocialSkillSkip(int targetSocialGroup)
    {
        int score = ComputeSocialSkillScore(targetSocialGroup);
        return score >= 75 ? 2 : score >= 45 ? 1 : 0;
    }

    /// <summary>Computes the player's 0..100 social charm score for <paramref name="targetSocialGroup"/>.</summary>
    internal int ComputeSocialSkillScore(int targetSocialGroup)
    {
        StatsComponent stats = actors.Player.Stats;
        int etiquette = Math.Clamp(stats.GetStat(StatId.Parse("etiquette")).ValueInt, 0, 100);
        int streetwise = Math.Clamp(stats.GetStat(StatId.Parse("streetwise")).ValueInt, 0, 100);
        int personality = Math.Clamp(stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Personality.Value)).ValueInt, 0, 100);

        int bestSkill = Math.Max(etiquette, streetwise);
        int groupBonus = targetSocialGroup switch
        {
            NobilitySocialGroup or ScholarsSocialGroup or MerchantsSocialGroup => etiquette >= streetwise ? 5 : 0,
            CommonersSocialGroup or UnderworldSocialGroup => streetwise >= etiquette ? 5 : 0,
            _ => 0,
        };
        return Math.Clamp((bestSkill * 2 + personality) / 3 + groupBonus, 0, 100);
    }

    /// <summary>
    /// Returns 1 when the player has settled in <paramref name="region"/> and built a strong home reputation
    /// (regional standing, regional people standing, target faction standing, or knightly/regional guild rank), else 0.
    /// </summary>
    internal int ComputeHomeStandingBonus(int targetFactionId, int region)
    {
        if (targetFactionId > 0
            && definitions.Factions.Factions.ContainsKey(targetFactionId)
            && social.FactionReputation(targetFactionId) >= 25)
        {
            return 1;
        }

        if (region is >= 0 and <= 61 && definitions.Factions.Regions.ContainsKey(region))
        {
            if (social.RegionalReputation(region) >= 25)
                return 1;

            if (DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, region) is { } peopleFaction
                && social.FactionReputation(peopleFaction.Id) >= 25)
            {
                return 1;
            }

            foreach (DaggerfallSocialAffiliationView affiliation in social.ReadAffiliations())
            {
                if (affiliation.Rank >= 2
                    && affiliation.FactionId > 0
                    && definitions.Factions.Factions.TryGetValue(affiliation.FactionId, out DaggerfallFactionDefinition? guildDef)
                    && guildDef.Region == region)
                {
                    return 1;
                }
            }
        }

        return 0;
    }

    /// <summary>Evaluates whether <paramref name="target"/> trusts <paramref name="vouch"/>'s referrer.</summary>
    internal bool TrustsReferrer(DaggerfallNpc target, ActorVouchRecord vouch)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(vouch);
        if (vouch.ReferrerDurableId == target.DurableId)
            return false;

        int targetFactionId = social.ResolveNpcReactionFactionId(target);
        int targetRegion = ResolveRegion(target, targetFactionId);
        DaggerfallSocialProminence targetProminence = ClassifyProminence(target);
        string? targetFamilyId = ResolveActorFamilyId(target.DurableId, target);
        return TrustsReferrerCore(targetFactionId, targetRegion, targetProminence, vouch, targetFamilyId);
    }

    /// <summary>True when <paramref name="target"/>'s household currently holds an attributed blood feud against the player.</summary>
    internal bool IsInBloodFeud(DaggerfallNpc target, DaggerfallSiteRecord? atSite = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        string? targetFamilyId = ResolveActorFamilyId(target.DurableId, target);
        return IsInBloodFeudCore(targetFamilyId, ResolveActorSite(target, atSite));
    }

    /// <summary>Evaluates whether the player currently has social access to approach <paramref name="target"/> for work or court business.</summary>
    internal DaggerfallIntroductionAccess AssessAccess(DaggerfallNpc target, bool? inCastleOverride = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        int targetFactionId = social.ResolveNpcReactionFactionId(target);
        int targetRegion = ResolveRegion(target, targetFactionId);
        DaggerfallSocialProminence prominence = ClassifyProminence(target, inCastleOverride);
        int requiredReach = (int)prominence;
        int targetSocialGroup = ResolveSocialGroup(targetFactionId);
        int skillSkip = ComputeSocialSkillSkip(targetSocialGroup);
        int homeBonus = ComputeHomeStandingBonus(targetFactionId, targetRegion);
        string? targetFamilyId = ResolveActorFamilyId(target.DurableId, target);
        DaggerfallSiteRecord? targetSite = ResolveActorSite(target);

        // An attributed household blood feud refuses audience, introductions, and business regardless of social charm or prior vouches.
        if (IsInBloodFeudCore(targetFamilyId, targetSite))
        {
            string feudingName = target.DisplayName ?? target.Role;
            return new(
                false,
                prominence,
                requiredReach,
                0,
                skillSkip,
                homeBonus,
                null,
                $"{feudingName}'s household holds a blood feud against you and will hear no introductions or business from you.");
        }

        if (prominence == DaggerfallSocialProminence.Ordinary)
        {
            ActorVouchRecord? bestPeerVouch = FindBestTrustedVouch(target, targetFactionId, targetRegion, prominence, targetFamilyId, targetSite, out _);
            return new(true, prominence, 0, 0, skillSkip, homeBonus, bestPeerVouch, null);
        }

        // Longtime direct standing with this exact figure/faction still grants access once earned.
        int factionRep = targetFactionId > 0 && definitions.Factions.Factions.ContainsKey(targetFactionId)
            ? social.FactionReputation(targetFactionId)
            : 0;
        int disposition = ReadActorDisposition(target.DurableId, targetSite);
        bool directStanding = prominence switch
        {
            DaggerfallSocialProminence.MinorCourt => factionRep >= 30 || disposition >= 15,
            DaggerfallSocialProminence.CourtFavourite => factionRep >= 50 || disposition >= 25,
            DaggerfallSocialProminence.Prominent => factionRep >= 70 || disposition >= 40,
            _ => true,
        };
        if (directStanding)
        {
            ActorVouchRecord? existingVouch = FindBestTrustedVouch(target, targetFactionId, targetRegion, prominence, targetFamilyId, targetSite, out _);
            return new(true, prominence, requiredReach, requiredReach, skillSkip, homeBonus, existingVouch, null);
        }

        // Evaluate trusted vouches held by target.
        ActorVouchRecord? bestVouch = FindBestTrustedVouch(target, targetFactionId, targetRegion, prominence, targetFamilyId, targetSite, out int bestVouchReachBase);
        int vouchReach = bestVouch is not null
            ? checked(bestVouchReachBase + 1 + skillSkip + homeBonus)
            : 0;
        int directReach = prominence == DaggerfallSocialProminence.Prominent
            ? 0 // Prominent figures (lords/rulers) ALWAYS require at least one trusted intermediary.
            : checked(skillSkip + homeBonus);

        int achievedReach = Math.Max(directReach, vouchReach);
        bool allowed = prominence == DaggerfallSocialProminence.Prominent
            ? bestVouch is not null && vouchReach >= requiredReach
            : achievedReach >= requiredReach;

        if (allowed)
            return new(true, prominence, requiredReach, achievedReach, skillSkip, homeBonus, bestVouch, null);

        string targetName = target.DisplayName ?? target.Role;
        string denial = BuildAccessDenialMessage(targetName, prominence, bestVouch, bestVouchReachBase, requiredReach, vouchReach);
        return new(false, prominence, requiredReach, achievedReach, skillSkip, homeBonus, bestVouch, denial);
    }

    /// <summary>Returns the strongest trusted vouch held by <paramref name="target"/>, if any.</summary>
    internal ActorVouchRecord? BestTrustedVouch(DaggerfallNpc target)
    {
        ArgumentNullException.ThrowIfNull(target);
        int targetFactionId = social.ResolveNpcReactionFactionId(target);
        int targetRegion = ResolveRegion(target, targetFactionId);
        DaggerfallSocialProminence prominence = ClassifyProminence(target);
        string? targetFamilyId = ResolveActorFamilyId(target.DurableId, target);
        DaggerfallSiteRecord? targetSite = ResolveActorSite(target);
        return FindBestTrustedVouch(target, targetFactionId, targetRegion, prominence, targetFamilyId, targetSite, out _);
    }

    /// <summary>
    /// Flat, idempotent social standing bonus for <see cref="DaggerfallSocialState.BindActorVouchBonus"/>:
    /// grants <see cref="TrustedVouchStandingBonus"/> (+15) once any trusted vouch is present on <paramref name="actorId"/>,
    /// improving merchant buy/sell quotes, spell prices, room rentals, and greetings.
    /// </summary>
    internal int TrustedVouchReactionBonus(long actorId)
    {
        if (actorId <= 0 || !npcs.TryGet(actorId, out DaggerfallNpc? npc) || npc is null)
            return 0;
        return BestTrustedVouch(npc) is not null ? TrustedVouchStandingBonus : 0;
    }

    /// <summary>True when <paramref name="npc"/> holds a trusted vouch that waives upfront hiring deposits and is not in a household blood feud.</summary>
    internal bool HasDepositWaiverVouch(DaggerfallNpc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (IsInBloodFeud(npc))
            return false;
        return BestTrustedVouch(npc) is not null;
    }

    /// <summary>All merged structured vouches currently recorded for <paramref name="actorId"/> (live overlay + ledger).</summary>
    internal IReadOnlyList<ActorVouchRecord> ReadVouches(long actorId)
    {
        IReadOnlyList<ActorVouchRecord>? liveVouches = actors.TryGet(actorId, out ActorState? live)
            && live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
            && overlay.Vouches.Count > 0
                ? overlay.Vouches
                : null;
        IReadOnlyList<ActorVouchRecord> ledgerVouches = actorLedger.GetVouches(actorId);
        return DaggerfallActorLedger.MergeVouches(liveVouches, ledgerVouches) ?? [];
    }

    /// <summary>
    /// Lists concrete people <paramref name="speaker"/> knows and can vouch the player to from dialogue:
    /// higher/peer court figures, local merchants/hireables in the active site/region, household and Great House kin
    /// (including off-scene Great House palace court factors across the province), and the current region's ruler.
    /// </summary>
    internal IReadOnlyList<DaggerfallIntroductionTarget> ListIntroductionTargets(DaggerfallNpc speaker, DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        // A guarded/prominent person who will not grant you audience, or a feuding household, does not offer vouches.
        if (!AssessAccess(speaker).Allowed)
            return [];

        int speakerFactionId = social.ResolveNpcReactionFactionId(speaker);
        int speakerRegion = ResolveRegion(speaker, speakerFactionId, site);
        DaggerfallSocialProminence speakerProminence = ClassifyProminence(speaker);
        string? speakerFamilyId = ResolveActorFamilyId(speaker.DurableId, speaker);
        int day = currentDay();
        string speakerName = speaker.DisplayName ?? speaker.Role;

        Dictionary<long, DaggerfallIntroductionTarget> results = [];

        void ConsiderTarget(DaggerfallNpc candidate)
        {
            if (candidate.DurableId == speaker.DurableId
                || candidate.Presence != DaggerfallNpcPresence.Active
                || !npcs.IsGameplayActive(candidate.DurableId)
                || DaggerfallNpcServiceFacts.IsChild(candidate.Appearance)
                || officeHolders.IsActorDead(candidate.DurableId))
            {
                return;
            }
            if (actors.TryGet(candidate.DurableId, out ActorState? liveCandidate) && liveCandidate.IsDefeated)
                return;

            string? candidateFamilyId = ResolveActorFamilyId(candidate.DurableId, candidate);
            bool isKin = DaggerfallWitnessDiscretion.AreFamily(speakerFamilyId, candidateFamilyId);
            bool isProvinceKin = isKin && DaggerfallHouseholds.IsProvinceWide(speakerFamilyId);

            int candidateFactionId = social.ResolveNpcReactionFactionId(candidate);
            int candidateRegion = ResolveRegion(candidate, candidateFactionId, site);
            if (!isProvinceKin && speakerRegion >= 0 && candidateRegion >= 0 && speakerRegion != candidateRegion)
                return;

            DaggerfallSiteRecord? candidateSite = ResolveActorSite(candidate);
            if (IsInBloodFeudCore(candidateFamilyId, candidateSite))
                return;

            DaggerfallSocialProminence candidateProminence = ClassifyProminence(candidate);
            // Offer introductions to guarded/court/prominent figures reachable from speaker's circle,
            // to local merchants/hireables/peers in the same site, or to household/Great House kin.
            bool sameSite = site is not null
                && candidate.Site.Region == site.Id.Region
                && string.Equals(candidate.Site.Location, site.Name, StringComparison.Ordinal);
            bool isServicePeer = sameSite && candidate.Services.Any(s =>
                s is "shop" or "buy-items" or "sell-items" or "buy-spells" or "tavern" or "bank" or "training" or "quests" or "quest" or "quest-candidate");
            bool isHireablePeer = sameSite && _isHireableContact?.Invoke(candidate) == true;
            bool isKinPeer = isProvinceKin || (isKin && sameSite);
            bool isLocalPeer = isServicePeer || isHireablePeer;
            if (candidateProminence == DaggerfallSocialProminence.Ordinary && !isLocalPeer && !isKinPeer)
                return;
            // Don't clutter with demotion intros from a king to a random bystander; offer upward, peer, or kin intros.
            if (candidateProminence < speakerProminence && !isLocalPeer && !isKinPeer)
                return;

            ActorVouchKind kind = DetermineVouchKind(speakerFactionId, candidate, candidateProminence, isKin);
            ActorVouchRecord probe = new(speaker.DurableId, Math.Max(0, speakerFactionId), kind, day, speakerName, site?.Name, speakerFamilyId);
            if (!TrustsReferrerCore(candidateFactionId, candidateRegion, candidateProminence, probe, candidateFamilyId))
                return;

            bool already = ReadVouches(candidate.DurableId).Any(v => v.SameGrant(probe));
            results[candidate.DurableId] = new(
                candidate.DurableId,
                candidate.DisplayName ?? candidate.Role,
                candidateFactionId,
                candidateProminence,
                kind,
                already);
        }

        foreach (DaggerfallNpc candidate in npcs.All.OrderBy(n => n.DurableId))
            ConsiderTarget(candidate);

        // Also expose the current region's living ruler (from OfficeHolders) when speaker is in the court circle or royal dynasty,
        // even if the ruler resides in the palace and hasn't been visited yet.
        if (speakerRegion >= 0
            && officeHolders.ResolveRegionalRuler(speakerRegion) is { HasLivingHolder: true, ActorId: long rulerId } ruler
            && rulerId != speaker.DurableId
            && !results.ContainsKey(rulerId))
        {
            int rulerFactionId = ruler.HolderFactionId ?? ruler.OfficeFactionId;
            string? rulerFamilyId = ResolveActorFamilyId(rulerId, officeFamilyId: ruler.FamilyId);
            DaggerfallSiteRecord? rulerSite = ruler.Home?.Site ?? site;
            if (!IsInBloodFeudCore(rulerFamilyId, rulerSite))
            {
                bool rulerIsKin = DaggerfallWitnessDiscretion.AreFamily(speakerFamilyId, rulerFamilyId);
                ActorVouchKind kind = !rulerIsKin && IsGuildOrOrderFaction(speakerFactionId) ? ActorVouchKind.Guild : ActorVouchKind.Court;
                ActorVouchRecord probe = new(speaker.DurableId, Math.Max(0, speakerFactionId), kind, day, speakerName, site?.Name, speakerFamilyId);
                if (TrustsReferrerCore(rulerFactionId, speakerRegion, DaggerfallSocialProminence.Prominent, probe, rulerFamilyId))
                {
                    bool already = ReadVouches(rulerId).Any(v => v.SameGrant(probe));
                    results[rulerId] = new(
                        rulerId,
                        ruler.DisplayName,
                        rulerFactionId,
                        DaggerfallSocialProminence.Prominent,
                        kind,
                        already);
                }
            }
        }

        // Cross-town Great House kin: expose living Tier-2 regional palace court factors of the speaker's Great House
        // even before the player has visited the regional capital's palace.
        if (officeLedger is not null
            && !string.IsNullOrWhiteSpace(speakerFamilyId)
            && DaggerfallHouseholds.IsGreatHouse(speakerFamilyId))
        {
            int houseRegion = DaggerfallHouseholds.TryParseRegion(speakerFamilyId, out int parsedRegion)
                ? parsedRegion
                : speakerRegion;
            if (houseRegion >= 0)
            {
                DaggerfallRegionalGreatHouse? house = officeLedger.ResolveRegionalGreatHouses(houseRegion)
                    .FirstOrDefault(h => StringComparer.Ordinal.Equals(h.FamilyId, speakerFamilyId));
                if (house is not null)
                {
                    foreach (DaggerfallLocalOffice office in house.HeldOffices)
                    {
                        if (office.IsVacant || !office.IsAlive || office.HolderActorId is not long holderId
                            || holderId == speaker.DurableId || results.ContainsKey(holderId))
                        {
                            continue;
                        }
                        if (officeHolders.IsActorDead(holderId)
                            || (actors.TryGet(holderId, out ActorState? liveHolder) && liveHolder.IsDefeated))
                        {
                            continue;
                        }
                        string? holderFamilyId = ResolveActorFamilyId(holderId, officeFamilyId: office.FamilyId);
                        if (!DaggerfallWitnessDiscretion.AreFamily(speakerFamilyId, holderFamilyId))
                            continue;
                        DaggerfallSiteRecord? officeSite = findSite?.Invoke(office.SiteId) ?? site;
                        if (IsInBloodFeudCore(holderFamilyId, officeSite))
                            continue;

                        DaggerfallSocialProminence officeProminence = ResolveLocalOfficeProminence(office, holderId);
                        ActorVouchKind kind = ActorVouchKind.Court;
                        ActorVouchRecord probe = new(speaker.DurableId, Math.Max(0, speakerFactionId), kind, day, speakerName, site?.Name, speakerFamilyId);
                        if (TrustsReferrerCore(office.OfficeFactionId, office.SiteId.Region, officeProminence, probe, holderFamilyId))
                        {
                            bool already = ReadVouches(holderId).Any(v => v.SameGrant(probe));
                            results[holderId] = new(
                                holderId,
                                office.DisplayName,
                                office.OfficeFactionId,
                                officeProminence,
                                kind,
                                already);
                        }
                    }
                }
            }
        }

        return
        [
            .. results.Values
                .OrderByDescending(target => (int)target.TargetProminence)
                .ThenBy(target => target.TargetName, StringComparer.Ordinal)
                .ThenBy(target => target.TargetDurableId),
        ];
    }

    /// <summary>
    /// Asks <paramref name="speaker"/> to issue a structured <see cref="ActorVouchRecord"/> for the player to
    /// <paramref name="targetDurableId"/>. Idempotent per <c>(speaker, kind)</c>; persists in <see cref="DaggerfallActorLedger"/>
    /// and hydrates onto the target's live <see cref="ActorOverlayComponent"/> when loaded.
    /// </summary>
    internal DaggerfallIntroductionResult TryIssueIntroduction(
        DaggerfallNpc speaker,
        long targetDurableId,
        DaggerfallSiteRecord? site)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        int day = currentDay();
        int speakerFactionId = social.ResolveNpcReactionFactionId(speaker);
        int speakerRegion = ResolveRegion(speaker, speakerFactionId, site);
        string? speakerFamilyId = ResolveActorFamilyId(speaker.DurableId, speaker);
        string speakerName = speaker.DisplayName ?? speaker.Role;

        if (targetDurableId == speaker.DurableId)
            return new(false, false, targetDurableId, speakerName, null, "I cannot introduce you to myself.");

        DaggerfallIntroductionAccess speakerAccess = AssessAccess(speaker);
        if (!speakerAccess.Allowed)
            return new(false, false, targetDurableId, speakerName, null,
                speakerAccess.DenialReason ?? $"{speakerName} will not vouch for an unintroduced stranger.");

        DaggerfallNpc? targetNpc = npcs.TryGet(targetDurableId, out DaggerfallNpc? reg) ? reg : null;
        DaggerfallOfficeHolder? officeTarget = null;
        DaggerfallLocalOffice? localOfficeTarget = null;
        if (targetNpc is null && speakerRegion >= 0)
        {
            DaggerfallOfficeHolder? ruler = officeHolders.ResolveRegionalRuler(speakerRegion);
            if (ruler is { HasLivingHolder: true, ActorId: long rulerId } && rulerId == targetDurableId)
                officeTarget = ruler;
        }
        if (targetNpc is null && officeTarget is null)
            localOfficeTarget = ResolveOffSceneGreatHouseOffice(speakerFamilyId, speakerRegion, targetDurableId);

        if (targetNpc is null && officeTarget is null && localOfficeTarget is null)
            return new(false, false, targetDurableId, $"contact #{targetDurableId}", null, "I do not know whom you mean.");

        if (officeHolders.IsActorDead(targetDurableId)
            || (actors.TryGet(targetDurableId, out ActorState? liveTargetCheck) && liveTargetCheck.IsDefeated))
        {
            string deadName = targetNpc?.DisplayName ?? officeTarget?.DisplayName ?? localOfficeTarget?.DisplayName ?? "that person";
            return new(false, false, targetDurableId, deadName, null, $"{deadName} is no longer among the living.");
        }

        string targetName = targetNpc?.DisplayName ?? targetNpc?.Role ?? officeTarget?.DisplayName ?? localOfficeTarget!.DisplayName;
        int targetFactionId = targetNpc is not null
            ? social.ResolveNpcReactionFactionId(targetNpc)
            : officeTarget is not null
                ? (officeTarget.HolderFactionId ?? officeTarget.OfficeFactionId)
                : localOfficeTarget!.OfficeFactionId;
        int targetRegion = targetNpc is not null
            ? ResolveRegion(targetNpc, targetFactionId, site)
            : localOfficeTarget is not null
                ? localOfficeTarget.SiteId.Region
                : speakerRegion;
        DaggerfallSocialProminence targetProminence = targetNpc is not null
            ? ClassifyProminence(targetNpc)
            : officeTarget is not null
                ? DaggerfallSocialProminence.Prominent
                : ResolveLocalOfficeProminence(localOfficeTarget!, targetDurableId);
        string? targetFamilyId = ResolveActorFamilyId(
            targetDurableId,
            targetNpc,
            officeFamilyId: officeTarget?.FamilyId ?? localOfficeTarget?.FamilyId);
        DaggerfallSiteRecord? targetSite = targetNpc is not null
            ? ResolveActorSite(targetNpc)
            : localOfficeTarget is not null
                ? (findSite?.Invoke(localOfficeTarget.SiteId) ?? site)
                : officeTarget?.Home?.Site ?? site;

        if (IsInBloodFeudCore(targetFamilyId, targetSite))
        {
            return new(false, false, targetDurableId, targetName, null,
                $"{targetName}'s household holds a blood feud against you; my word will not open their door.");
        }

        bool isKin = DaggerfallWitnessDiscretion.AreFamily(speakerFamilyId, targetFamilyId);
        ActorVouchKind kind = targetNpc is not null
            ? DetermineVouchKind(speakerFactionId, targetNpc, targetProminence, isKin)
            : (!isKin && IsGuildOrOrderFaction(speakerFactionId) ? ActorVouchKind.Guild : ActorVouchKind.Court);

        ActorVouchRecord vouch = new(
            speaker.DurableId,
            Math.Max(0, speakerFactionId),
            kind,
            day,
            speakerName,
            site?.Name,
            speakerFamilyId);

        // Idempotent check before trust/standing work: repeat asks return cleanly without duplicating records.
        if (ReadVouches(targetDurableId).FirstOrDefault(existing => existing.SameGrant(vouch)) is { } prior)
        {
            return new(true, true, targetDurableId, targetName, prior,
                $"I have already sent my word to {targetName} on your behalf.");
        }

        if (!TrustsReferrerCore(targetFactionId, targetRegion, targetProminence, vouch, targetFamilyId))
        {
            return new(false, false, targetDurableId, targetName, null,
                $"My name carries no weight with {targetName}; you will need someone in their circle to vouch for you.");
        }

        // Check whether the speaker personally trusts/respects the player enough to put their name behind them.
        int speakerSocialGroup = ResolveSocialGroup(speakerFactionId);
        RecordSocialSkillUse(speakerSocialGroup);

        int speakerReaction = social.ReactionForNpc(speaker).Value;
        int guildBonus = speakerFactionId > 0
            && definitions.Factions.Factions.ContainsKey(speakerFactionId)
            && social.GuildEligibility(speakerFactionId).IsMember
                ? 20
                : 0;
        int socialScore = ComputeSocialSkillScore(speakerSocialGroup);
        int persuasionBonus = Math.Max(0, (socialScore - 40) / 4);
        int effectiveTrust = checked(speakerReaction + guildBonus + persuasionBonus);
        int requiredTrust = kind switch
        {
            ActorVouchKind.Contact => 0,
            ActorVouchKind.Trade => 10,
            ActorVouchKind.Guild => 15,
            ActorVouchKind.Court => 20,
            _ => 10,
        };

        if (effectiveTrust < requiredTrust)
        {
            return new(false, false, targetDurableId, targetName, null,
                $"I do not know you well enough to put my name behind yours with {targetName}. Earn my trust or standing here first.");
        }

        // Persist the structured vouch on the target's ActorLedger entry (and live overlay if currently loaded).
        ActorOrigin targetOrigin = ResolveTargetOrigin(targetDurableId, targetNpc, officeTarget, localOfficeTarget);
        ActorSocialDiff? targetSocial = ResolveTargetSocialDiff(targetDurableId, targetNpc);
        bool added = actorLedger.RecordVouch(targetDurableId, vouch, targetOrigin, targetSocial);
        if (actors.TryGet(targetDurableId, out ActorState? liveTarget)
            && liveTarget.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay))
        {
            overlay.RecordVouch(vouch);
        }

        string reply = kind switch
        {
            ActorVouchKind.Court => $"Very well — I will send word ahead to {targetName}. Give my name when you seek audience.",
            ActorVouchKind.Guild => $"You have proved yourself to us. Mention {speakerName} to {targetName}; they will hear you out.",
            ActorVouchKind.Trade => $"Tell {targetName} that {speakerName} sent you, and they will offer you fairer terms.",
            _ => $"Speak with {targetName} and tell them {speakerName} vouches for you.",
        };
        return new(true, !added, targetDurableId, targetName, vouch, reply);
    }

    /// <summary>
    /// Lets the player attempt to butter up / flatter <paramref name="target"/> in dialogue once per day.
    /// High-social-skill characters reliably warm personal disposition (enabling a quick court introduction),
    /// while blunt low-social-skill characters fail and must earn trust through quests and deeds.
    /// </summary>
    internal DaggerfallFavourResult TryCurryFavour(DaggerfallNpc target, bool politeTone, bool bluntTone)
    {
        ArgumentNullException.ThrowIfNull(target);
        int day = currentDay();
        string targetName = target.DisplayName ?? target.Role;

        DaggerfallIntroductionAccess access = AssessAccess(target);
        if (!access.Allowed)
            return new(false, false, 0, access.DenialReason ?? $"{targetName} will not entertain flattery from an unintroduced stranger.");

        if (ReadLastFavourDay(target.DurableId) == day)
            return new(false, false, 0, $"You have already pressed your charm with {targetName} today.");

        int targetFactionId = social.ResolveNpcReactionFactionId(target);
        int targetSocialGroup = ResolveSocialGroup(targetFactionId);
        RecordSocialSkillUse(targetSocialGroup);

        int socialScore = ComputeSocialSkillScore(targetSocialGroup);
        bool courtOrScholar = targetSocialGroup is NobilitySocialGroup or ScholarsSocialGroup or MerchantsSocialGroup;
        int toneModifier = courtOrScholar
            ? (politeTone ? 10 : bluntTone ? -15 : 0)
            : (bluntTone ? 10 : politeTone ? -5 : 0);

        int difficulty = access.Prominence switch
        {
            DaggerfallSocialProminence.Ordinary => 30,
            DaggerfallSocialProminence.MinorCourt => 45,
            DaggerfallSocialProminence.CourtFavourite => 55,
            DaggerfallSocialProminence.Prominent => 65,
            _ => 40,
        };
        int chance = Math.Clamp(socialScore + toneModifier - difficulty + 50, 5, 95);
        int roll = checked((int)random.DrawKeyed(new KeyedRngRequest(
            DaggerfallRandomKey.Seed,
            RandomScope,
            $"favour:npc:{target.DurableId}:day:{day}",
            1,
            100)).Value);

        bool succeeded = roll <= chance;
        int delta = succeeded
            ? FavourSuccessDispositionBonus
            : (courtOrScholar && bluntTone ? FavourRudeNoblePenalty : 0);

        AdjustActorDisposition(target.DurableId, delta, setLastFavourDay: day);

        string message = succeeded
            ? $"{targetName} warms noticeably to your flattery and polished manner."
            : delta < 0
                ? $"{targetName} bristles at your blunt manner and turns cool."
                : $"{targetName} listens politely but remains unimpressed by your words.";
        return new(true, succeeded, delta, message);
    }

    /// <summary>Returns true when <paramref name="target"/> has not yet been flattered today and can be approached.</summary>
    internal bool CanCurryFavourToday(DaggerfallNpc target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (DaggerfallNpcServiceFacts.IsChild(target.Appearance))
            return false;
        if (ReadLastFavourDay(target.DurableId) == currentDay())
            return false;
        return AssessAccess(target).Allowed;
    }

    private Action<long, int>? _onDispositionAdjusted;

    /// <summary>Binds a notification invoked after personal disposition changes (used for household goodwill ripples).</summary>
    internal void BindDispositionAdjusted(Action<long, int> onAdjusted) =>
        _onDispositionAdjusted = onAdjusted ?? throw new ArgumentNullException(nameof(onAdjusted));

    /// <summary>Adjusts an actor's persistent personal <c>DispositionDelta</c> (and optional <c>LastFavourDay</c>) in <see cref="DaggerfallActorLedger"/>.</summary>
    internal void AdjustActorDisposition(long actorId, int delta, int? setLastFavourDay = null)
    {
        if (actorId <= 0 || actorId == DaggerfallActorIdentity.PlayerEntityId)
            return;
        if (delta == 0 && setLastFavourDay is null)
            return;

        if (setLastFavourDay is int favourDay
            && actors.TryGet(actorId, out ActorState? liveActor)
            && liveActor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? liveOverlay))
        {
            liveOverlay.LastFavourDay = favourDay;
        }

        bool hadEntry = actorLedger.TryGet(actorId, out ActorLedgerEntry? existing) && existing is not null;
        DaggerfallNpc? npc = npcs.TryGet(actorId, out DaggerfallNpc? reg) ? reg : null;
        ActorOrigin? origin = existing?.Origin ?? TryResolveOriginFromLiveOrNpc(actorId, npc);
        if (origin is null)
            return;

        ActorSocialDiff? socialDiff = existing?.Social ?? (npc is not null ? ActorSocialDiff.FromNpc(npc) : null);
        if (socialDiff is null && !hadEntry)
            return;

        if (socialDiff is not null)
        {
            int nextDisposition = Math.Clamp(
                checked(socialDiff.DispositionDelta + delta),
                DaggerfallSocialState.MinimumReputation,
                DaggerfallSocialState.MaximumReputation);
            socialDiff = socialDiff with
            {
                DispositionDelta = nextDisposition,
                LastFavourDay = setLastFavourDay ?? socialDiff.LastFavourDay,
            };
        }

        ActorLedgerEntry updated = existing is not null
            ? existing with { Social = socialDiff }
            : new ActorLedgerEntry(actorId, origin, Social: socialDiff);
        actorLedger.Upsert(updated);
        if (delta != 0)
            _onDispositionAdjusted?.Invoke(actorId, delta);
    }

    private DaggerfallSocialProminence ClassifyProminenceCore(
        long actorId,
        string? displayName,
        DaggerfallNpcKind kind,
        IReadOnlyList<string> services,
        int factionId,
        int region,
        bool inCastle)
    {
        // Living regional ruler resolved by OfficeHolders is always Prominent.
        if (region >= 0
            && officeHolders.ResolveRegionalRuler(region) is { HasLivingHolder: true } ruler
            && ((actorId > 0 && ruler.ActorId == actorId)
                || (factionId > 0 && ruler.HolderFactionId == factionId)))
        {
            return DaggerfallSocialProminence.Prominent;
        }

        if (factionId > 0 && definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction))
        {
            if (faction.Type == ProvinceFactionType)
                return DaggerfallSocialProminence.Prominent;

            if (faction.Type == IndividualFactionType)
            {
                string label = !string.IsNullOrWhiteSpace(displayName) ? displayName : faction.Name;
                if (HasAnyPrefixOrMatch(label, SovereignTitles) || HasAnyPrefixOrMatch(faction.Name, SovereignTitles))
                    return DaggerfallSocialProminence.Prominent;
                if (faction.SocialGroup == NobilitySocialGroup && faction.Power >= 15)
                    return DaggerfallSocialProminence.Prominent;

                if (HasAnyPrefixOrMatch(label, NobleTitles)
                    || HasAnyPrefixOrMatch(faction.Name, NobleTitles)
                    || faction.Power >= 15
                    || IsAlliedToProminentFaction(faction))
                {
                    return DaggerfallSocialProminence.CourtFavourite;
                }

                if (faction.SocialGroup == NobilitySocialGroup || IsParentedByCourtOrNoble(faction))
                    return DaggerfallSocialProminence.MinorCourt;
            }

            if (faction.Type == CourtsFactionType)
                return DaggerfallSocialProminence.MinorCourt;

            if (faction.Type == OfficialFactionType)
                return faction.Power >= 25 ? DaggerfallSocialProminence.CourtFavourite : DaggerfallSocialProminence.MinorCourt;

            if (faction.SocialGroup == NobilitySocialGroup && faction.Type != PeopleFactionType && faction.GuildGroup != KnightlyOrderGuildGroup)
                return DaggerfallSocialProminence.MinorCourt;
        }

        if (inCastle && kind == DaggerfallNpcKind.Static && services.Contains("talk", StringComparer.Ordinal))
        {
            bool isGuildOrMerchant = services.Any(s =>
                s is "buy-items" or "sell-items" or "bank" or "tavern" or "training" or "quests");
            if (!isGuildOrMerchant)
                return DaggerfallSocialProminence.MinorCourt;
        }

        return DaggerfallSocialProminence.Ordinary;
    }

    private bool IsAlliedToProminentFaction(DaggerfallFactionDefinition faction, int? requiredRegion = null)
    {
        foreach (int allyId in faction.Allies)
        {
            if (!definitions.Factions.Factions.TryGetValue(allyId, out DaggerfallFactionDefinition? ally))
                continue;
            if (requiredRegion is int reqRegion && ally.Region >= 0 && ally.Region != reqRegion)
                continue;
            if (ally.Type == ProvinceFactionType
                || HasAnyPrefixOrMatch(ally.Name, SovereignTitles)
                || (ally.Type == IndividualFactionType && ally.SocialGroup == NobilitySocialGroup && ally.Power >= 15))
            {
                return true;
            }
        }
        return false;
    }

    private bool IsParentedByCourtOrNoble(DaggerfallFactionDefinition faction) =>
        faction.Parent > 0
        && definitions.Factions.Factions.TryGetValue(faction.Parent, out DaggerfallFactionDefinition? parent)
        && (parent.Type is ProvinceFactionType or CourtsFactionType or IndividualFactionType
            || parent.SocialGroup == NobilitySocialGroup);

    private bool TrustsReferrerCore(
        int targetFactionId,
        int targetRegion,
        DaggerfallSocialProminence targetProminence,
        ActorVouchRecord vouch,
        string? targetFamilyId = null)
    {
        int refFactionId = vouch.ReferrerFactionId;
        definitions.Factions.Factions.TryGetValue(targetFactionId, out DaggerfallFactionDefinition? targetDef);
        definitions.Factions.Factions.TryGetValue(refFactionId, out DaggerfallFactionDefinition? refDef);

        // Hard refusal: enemies never trust each other's vouches.
        if (AreEnemies(targetDef, refDef))
            return false;

        // Kin trust: household, Great House, and royal dynasty members always vouch for one another.
        string? refFamilyId = ResolveReferrerFamilyId(vouch);
        if (DaggerfallWitnessDiscretion.AreFamily(targetFamilyId, refFamilyId))
            return true;

        if (targetDef is not null && refDef is not null)
        {
            if (targetDef.Id == refDef.Id)
                return true;

            // Direct ally or parent/child in either direction.
            if (targetDef.Allies.Contains(refDef.Id) || refDef.Allies.Contains(targetDef.Id)
                || targetDef.Parent == refDef.Id || refDef.Parent == targetDef.Id)
            {
                return true;
            }

            // Shared non-zero parent or shared province/court root (e.g., two nobles under 201 Daggerfall).
            if (targetDef.Parent != 0 && targetDef.Parent == refDef.Parent)
                return true;

            int targetRoot = FindHierarchyRoot(targetDef);
            int refRoot = FindHierarchyRoot(refDef);
            if (targetRoot != 0 && targetRoot == refRoot)
                return true;

            // Allied to target's parent or target's hierarchy root (e.g., Popudax allied to King Gothryd under Daggerfall).
            if (targetDef.Parent != 0 && (refDef.Allies.Contains(targetDef.Parent) || targetDef.Allies.Contains(refDef.Parent)))
                return true;

            // Same-region nobility/court circle (e.g., Lady Bridwell and Mobar both in Region 17 Nobility).
            bool sameRegion = targetRegion >= 0 && refDef.Region >= 0 && targetRegion == refDef.Region;
            if (sameRegion
                && targetDef.SocialGroup == NobilitySocialGroup
                && (refDef.SocialGroup == NobilitySocialGroup || refDef.Type == CourtsFactionType || refDef.GuildGroup == KnightlyOrderGuildGroup))
            {
                return true;
            }

            // Knightly order or regional courtier vouching into a minor/senior court figure in the same region.
            if (sameRegion
                && targetProminence >= DaggerfallSocialProminence.MinorCourt
                && (refDef.GuildGroup == KnightlyOrderGuildGroup || refDef.Type == CourtsFactionType))
            {
                return true;
            }

            // Referrer is allied to a prominent figure in the same regional court (e.g. Popudax -> King Gothryd).
            if (sameRegion && targetProminence >= DaggerfallSocialProminence.MinorCourt && IsAlliedToProminentFaction(refDef, targetRegion))
                return true;
        }

        // For ordinary targets (merchants, hireables, townsfolk), a peer Contact/Trade vouch from any non-enemy is trusted.
        if (targetProminence == DaggerfallSocialProminence.Ordinary
            && vouch.Kind is ActorVouchKind.Contact or ActorVouchKind.Trade)
        {
            return true;
        }

        return false;
    }

    private bool AreEnemies(DaggerfallFactionDefinition? a, DaggerfallFactionDefinition? b)
    {
        if (a is null || b is null) return false;
        if (a.Enemies.Contains(b.Id) || b.Enemies.Contains(a.Id)) return true;
        if (a.Parent != 0 && b.Enemies.Contains(a.Parent)) return true;
        if (b.Parent != 0 && a.Enemies.Contains(b.Parent)) return true;
        return false;
    }

    private int FindHierarchyRoot(DaggerfallFactionDefinition faction)
    {
        HashSet<int> seen = [faction.Id];
        DaggerfallFactionDefinition current = faction;
        while (current.Parent != 0
            && definitions.Factions.Factions.TryGetValue(current.Parent, out DaggerfallFactionDefinition? parent)
            && seen.Add(parent.Id))
        {
            current = parent;
        }
        return current.Id;
    }

    private ActorVouchRecord? FindBestTrustedVouch(
        DaggerfallNpc target,
        int targetFactionId,
        int targetRegion,
        DaggerfallSocialProminence targetProminence,
        string? targetFamilyId,
        DaggerfallSiteRecord? targetSite,
        out int bestVouchReachBase)
    {
        bestVouchReachBase = -1;
        if (IsInBloodFeudCore(targetFamilyId, targetSite))
            return null;

        ActorVouchRecord? best = null;
        foreach (ActorVouchRecord vouch in ReadVouches(target.DurableId))
        {
            if (vouch.ReferrerDurableId == target.DurableId)
                continue;
            if (!TrustsReferrerCore(targetFactionId, targetRegion, targetProminence, vouch, targetFamilyId))
                continue;
            int refProminence = (int)ResolveReferrerProminence(vouch, targetRegion);
            bool isKinVouch = DaggerfallWitnessDiscretion.AreFamily(targetFamilyId, ResolveReferrerFamilyId(vouch));
            int reachBase = isKinVouch
                ? Math.Max(refProminence + 1, (int)targetProminence - 1)
                : refProminence;
            if (best is null
                || reachBase > bestVouchReachBase
                || (reachBase == bestVouchReachBase && vouch.Kind > best.Kind))
            {
                best = vouch;
                bestVouchReachBase = reachBase;
            }
        }
        return best;
    }

    private DaggerfallSocialProminence ResolveReferrerProminence(ActorVouchRecord vouch, int fallbackRegion)
    {
        if (npcs.TryGet(vouch.ReferrerDurableId, out DaggerfallNpc? refNpc) && refNpc is not null)
            return ClassifyProminence(refNpc, inCastleOverride: false);
        return ClassifyFactionProminence(vouch.ReferrerFactionId, vouch.ReferrerDurableId, vouch.ReferrerName, fallbackRegion);
    }

    private ActorVouchKind DetermineVouchKind(int speakerFactionId, DaggerfallNpc target, DaggerfallSocialProminence targetProminence, bool isKin = false)
    {
        if (targetProminence >= DaggerfallSocialProminence.MinorCourt)
            return !isKin && IsGuildOrOrderFaction(speakerFactionId) ? ActorVouchKind.Guild : ActorVouchKind.Court;
        if (target.Services.Any(s => s is "shop" or "buy-items" or "sell-items" or "buy-spells" or "tavern" or "bank"))
            return ActorVouchKind.Trade;
        return ActorVouchKind.Contact;
    }

    private bool IsInBloodFeudCore(string? familyId, DaggerfallSiteRecord? atSite)
    {
        if (string.IsNullOrWhiteSpace(familyId) || _householdStandingDelta is null)
            return false;
        return _householdStandingDelta(familyId, atSite) <= BloodFeudDispositionThreshold;
    }

    private bool TryResolveKnownActorFamilyId(long actorId, out string? familyId)
    {
        familyId = null;
        if (actorId <= 0 || actorId == DaggerfallActorIdentity.PlayerEntityId)
            return false;
        if (actors.TryGet(actorId, out ActorState? live))
        {
            if (live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay))
            {
                familyId = !string.IsNullOrWhiteSpace(overlay.FamilyId) ? overlay.FamilyId : null;
                return true;
            }
            if (live.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person))
            {
                familyId = !string.IsNullOrWhiteSpace(person.Snapshot.FamilyId) ? person.Snapshot.FamilyId : null;
                return true;
            }
        }
        if (actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) && entry?.Social is { } socialDiff)
        {
            if (socialDiff.FamilyIdCleared)
            {
                familyId = null;
                return true;
            }
            if (!string.IsNullOrWhiteSpace(socialDiff.FamilyId))
            {
                familyId = socialDiff.FamilyId;
                return true;
            }
        }
        if (npcs.TryGet(actorId, out DaggerfallNpc? npc) && npc is not null && !string.IsNullOrWhiteSpace(npc.FamilyId))
        {
            familyId = npc.FamilyId;
            return true;
        }
        return false;
    }

    private string? ResolveActorFamilyId(long actorId, DaggerfallNpc? npc = null, string? officeFamilyId = null)
    {
        if (TryResolveKnownActorFamilyId(actorId, out string? known))
            return known;
        if (npc is not null && !string.IsNullOrWhiteSpace(npc.FamilyId))
            return npc.FamilyId;
        return !string.IsNullOrWhiteSpace(officeFamilyId) ? officeFamilyId : null;
    }

    private string? ResolveReferrerFamilyId(ActorVouchRecord vouch)
    {
        if (TryResolveKnownActorFamilyId(vouch.ReferrerDurableId, out string? known))
            return known;
        return !string.IsNullOrWhiteSpace(vouch.ReferrerFamilyId) ? vouch.ReferrerFamilyId : null;
    }

    private DaggerfallSiteRecord? ResolveActorSite(DaggerfallNpc? npc, DaggerfallSiteRecord? siteOverride = null)
    {
        if (siteOverride is not null)
            return siteOverride;
        DaggerfallSiteRecord? current = activeSite();
        if (npc is null)
            return current;
        if (current is not null
            && npc.Site.Region == current.Id.Region
            && string.Equals(npc.Site.Location, current.Name, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }
        if (npc.Profile is { } profile && findSite?.Invoke(profile.Site) is { } profileSite)
            return profileSite;
        if (npc.Site.Region >= 0 && !string.IsNullOrWhiteSpace(npc.Site.Location))
        {
            foreach (DaggerfallSiteRecord record in definitions.Locations.Records)
            {
                if (record.Id.Region == npc.Site.Region
                    && string.Equals(record.Name, npc.Site.Location, StringComparison.OrdinalIgnoreCase))
                {
                    return record;
                }
            }
        }
        return current;
    }

    private DaggerfallLocalOffice? ResolveOffSceneGreatHouseOffice(string? speakerFamilyId, int speakerRegion, long targetDurableId)
    {
        if (officeLedger is null || string.IsNullOrWhiteSpace(speakerFamilyId) || !DaggerfallHouseholds.IsGreatHouse(speakerFamilyId))
            return null;
        int houseRegion = DaggerfallHouseholds.TryParseRegion(speakerFamilyId, out int parsedRegion)
            ? parsedRegion
            : speakerRegion;
        if (houseRegion < 0)
            return null;
        DaggerfallRegionalGreatHouse? house = officeLedger.ResolveRegionalGreatHouses(houseRegion)
            .FirstOrDefault(h => StringComparer.Ordinal.Equals(h.FamilyId, speakerFamilyId));
        return house?.HeldOffices.FirstOrDefault(o => !o.IsVacant && o.IsAlive && o.HolderActorId == targetDurableId);
    }

    private DaggerfallSocialProminence ResolveLocalOfficeProminence(DaggerfallLocalOffice office, long holderId)
    {
        DaggerfallSocialProminence factionProminence = ClassifyFactionProminence(
            office.OfficeFactionId, holderId, office.DisplayName, office.SiteId.Region);
        return factionProminence > DaggerfallSocialProminence.MinorCourt
            ? factionProminence
            : DaggerfallSocialProminence.MinorCourt;
    }

    private bool IsGuildOrOrderFaction(int factionId) =>
        factionId > 0
        && definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction)
        && faction.GuildGroup > 0
        && faction.Type != ProvinceFactionType
        && faction.Type != CourtsFactionType
        && faction.Type != PeopleFactionType;

    private int ResolveSocialGroup(int factionId) =>
        factionId > 0 && definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction)
            ? faction.SocialGroup
            : CommonersSocialGroup;

    private int ResolveRegion(DaggerfallNpc npc, int factionId, DaggerfallSiteRecord? siteOverride = null)
    {
        if (factionId > 0
            && definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction)
            && faction.Region >= 0)
        {
            return faction.Region;
        }
        if (npc.Site.Region >= 0)
            return npc.Site.Region;
        return (siteOverride ?? activeSite())?.Id.Region ?? -1;
    }

    private int ReadActorDisposition(long actorId, DaggerfallSiteRecord? atSite = null) =>
        _effectiveDisposition is not null
            ? _effectiveDisposition(actorId, atSite)
            : actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) && entry?.Social is { } s ? s.DispositionDelta : 0;

    private int? ReadLastFavourDay(long actorId)
    {
        if (actors.TryGet(actorId, out ActorState? live)
            && live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
            && overlay.LastFavourDay is int liveDay)
        {
            return liveDay;
        }
        return actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) ? entry?.Social?.LastFavourDay : null;
    }

    private void RecordSocialSkillUse(int targetSocialGroup)
    {
        bool useEtiquette = targetSocialGroup is NobilitySocialGroup or ScholarsSocialGroup or MerchantsSocialGroup;
        skillUses.Record(new DaggerfallSkillUse(
            useEtiquette ? "etiquette" : "streetwise",
            useEtiquette ? DaggerfallSkillUseReason.DialogueEtiquette : DaggerfallSkillUseReason.DialogueStreetwise,
            DaggerfallSkillUseOutcome.Accepted));
    }

    private ActorOrigin ResolveTargetOrigin(
        long targetDurableId,
        DaggerfallNpc? targetNpc,
        DaggerfallOfficeHolder? officeTarget,
        DaggerfallLocalOffice? localOfficeTarget = null)
    {
        if (actorLedger.TryGet(targetDurableId, out ActorLedgerEntry? existing) && existing is not null)
            return existing.Origin;
        if (TryResolveOriginFromLiveOrNpc(targetDurableId, targetNpc) is { } resolved)
            return resolved;
        if (officeTarget?.CastId is { Length: > 0 } castId)
        {
            DaggerfallWorldProfileKey? homeProfile = DaggerfallCastHomes.ResolveProfileKey(officeTarget.Home);
            return ActorOrigin.Cast(castId, homeProfile);
        }
        if (localOfficeTarget is not null)
        {
            foreach (DaggerfallCastIndividualDefinition cast in definitions.People.Individuals.Values)
            {
                if (DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id) == targetDurableId)
                    return ActorOrigin.Cast(cast.Id, localOfficeTarget.ProfileKey);
            }
            return ActorOrigin.Seeded(localOfficeTarget.ProfileKey, localOfficeTarget.SlotId);
        }
        return ActorOrigin.MintedActor(DaggerfallActorKinds.StaticNpc);
    }

    private ActorOrigin? TryResolveOriginFromLiveOrNpc(long actorId, DaggerfallNpc? npc)
    {
        if (actors.TryGet(actorId, out ActorState? live)
            && live.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? originComp))
        {
            return originComp.Origin;
        }
        if (npc is null) return null;
        if (npc.Kind == DaggerfallNpcKind.Static && !string.IsNullOrWhiteSpace(npc.StableKey))
        {
            if (definitions.People.Individuals.ContainsKey(npc.StableKey))
                return ActorOrigin.Cast(npc.StableKey, npc.Profile);
            if (npc.Profile is { } profile && DaggerfallPeopleSlots.IsDeterministicId(actorId))
                return ActorOrigin.Seeded(profile, npc.StableKey);
        }
        return ActorOrigin.MintedActor(
            npc.Kind == DaggerfallNpcKind.Civilian ? DaggerfallActorKinds.Civilian : DaggerfallActorKinds.StaticNpc,
            gender: npc.Appearance.Gender);
    }

    private ActorSocialDiff? ResolveTargetSocialDiff(long targetDurableId, DaggerfallNpc? targetNpc)
    {
        if (actorLedger.TryGet(targetDurableId, out ActorLedgerEntry? existing) && existing?.Social is { } existingSocial)
            return existingSocial;
        return targetNpc is not null ? ActorSocialDiff.FromNpc(targetNpc) : null;
    }

    private static string BuildAccessDenialMessage(
        string targetName,
        DaggerfallSocialProminence prominence,
        ActorVouchRecord? bestVouch,
        int bestReferrerProminence,
        int requiredReach,
        int vouchReach)
    {
        if (bestVouch is not null && vouchReach < requiredReach)
        {
            return $"{targetName} acknowledges {bestVouch.DisplaySummary()}'s word, but requires an introduction from someone higher in their court circle before entrusting you with work.";
        }

        return prominence switch
        {
            DaggerfallSocialProminence.Prominent =>
                $"{targetName} does not receive unintroduced strangers for court work. Earn an introduction from a trusted courtier or favoured ally first.",
            DaggerfallSocialProminence.CourtFavourite =>
                $"{targetName} keeps guarded company and will not offer work to strangers without an introduction from someone in their circle.",
            _ =>
                $"{targetName} will not take business from an unknown outsider. Seek an introduction from an allied order, guild, or court contact first.",
        };
    }

    private static bool HasAnyPrefixOrMatch(string? value, string[] prefixes)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (string prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
