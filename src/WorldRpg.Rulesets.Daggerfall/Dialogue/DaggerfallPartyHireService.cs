using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Outcome of a party hiring, field-sustain, or dismissal dialogue action.</summary>
internal sealed record DaggerfallPartyHireResult(bool Accepted, string Question, string Message);

/// <summary>Authoritative price and eligibility quote for hiring an actor under a <c>party-hire</c> spec (#11101).</summary>
internal sealed record DaggerfallPartyHireQuote(
    string SpawnerId,
    ulong Deposit,
    int DailyWage,
    bool CanDelveDungeons,
    bool Eligible,
    string? RefusalReason = null);

/// <summary>Outcome emitted when daily wages roll over for one contracted party member (#11101).</summary>
internal sealed record DaggerfallPartyDailyOutcome(
    long ActorId,
    string Name,
    bool Paid,
    bool Deserted,
    int UnpaidDays,
    int Morale,
    ulong PaidGold,
    string Message);

/// <summary>Outcome of the single friendly-fire tolerance rule inside the party morale owner (#11101).</summary>
internal sealed record DaggerfallFriendlyFireResult(
    bool Handled,
    bool TurnedHostile,
    int Strikes,
    int Morale,
    string? Message);

/// <summary>
/// Gameplay producer for enlisting seeded townsfolk, tavern patrons, road guards, and guild retainers into
/// <see cref="ActorLocationKind.WithParty"/> as a <see cref="ActorPartyRole.Companion"/> or
/// <see cref="ActorPartyRole.Driver"/> via normal conversation, pricing upfront deposits and daily wages from
/// <c>party-hire</c> specs and world conditions, rolling over daily wages and morale, applying the single
/// friendly-fire tolerance rule, and releasing them via <see cref="DaggerfallOverlandPartyCoordinator.ReleaseFromParty"/> (#11101).
/// </summary>
internal sealed class DaggerfallPartyHireService
{
    internal const ulong CompanionHireFee = 25UL;
    internal const ulong DriverHireFee = 15UL;
    internal const int DefaultDailyWage = 12;
    internal const int MaximumPartyMembers = 3;
    internal const string DefaultRoadGuardSpecId = "hire-road-guard";
    internal const string MercenarySpecId = "hire-mercenary";
    internal const string KnightRetainerSpecId = "hire-knight-retainer";
    internal const string BattleMageSpecId = "hire-battle-mage";
    internal const string WarFrontTag = "war-front";

    internal const int FriendlyFireTolerance = 2;
    internal const int FriendlyFireMoralePenalty = 12;
    internal const int UnpaidMoralePenaltyPerDay = 15;
    internal const int PaidMoraleRecovery = 2;
    internal const int MaxUnpaidDaysBeforeDesertion = 2;
    internal const int DesertionMoraleThreshold = 20;
    internal const int DesertionDispositionPenalty = -15;
    internal const int HealingDraughtHealthAmount = 35;
    internal const int HealingDraughtMoraleBonus = 8;
    internal const int HazardBonusMoraleBonus = 15;

    private const int TavernBuildingType = 15;
    private const int GuildHallBuildingType = 11;

    private static readonly HashSet<string> NonHireableRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "guard",
        "merchant",
        "bank teller",
        "tavern keeper",
        "priest",
        "witch",
        "bystander",
    };

    private readonly DaggerfallDefinitions _definitions;
    private readonly IReadOnlyDictionary<string, DaggerfallActorSpawnerSpec> _partyHireSpecs;
    private readonly ActorsState _actors;
    private readonly DaggerfallActorLedger _actorLedger;
    private readonly DaggerfallOverlandPartyCoordinator _overlandParties;
    private readonly Func<DaggerfallSiteProfile> _activeProfile;
    private readonly Func<DaggerfallInteriorBuilding?> _currentBuilding;
    private readonly Func<bool> _ownsCart;
    private readonly Func<ulong> _availableGold;
    private readonly Func<ulong, bool> _spendGold;
    private readonly Func<long, bool> _isQuestUnavailable;
    private readonly Func<DaggerfallNpc, bool> _hasDepositWaiver;
    private readonly Func<int, ulong, ulong> _hireFee;
    private readonly DaggerfallWorldConditions? _worldConditions;
    private readonly Func<int> _currentDay;
    private readonly Func<string, int> _readPlayerSkill;
    private readonly Func<int, int?> _readGuildRank;
    private readonly Func<bool> _consumeHealingDraught;

    internal DaggerfallPartyHireService(
        DaggerfallDefinitions definitions,
        ActorsState actors,
        DaggerfallActorLedger actorLedger,
        DaggerfallOverlandPartyCoordinator overlandParties,
        Func<DaggerfallSiteProfile> activeProfile,
        Func<DaggerfallInteriorBuilding?> currentBuilding,
        Func<bool> ownsCart,
        Func<ulong> availableGold,
        Func<ulong, bool> spendGold,
        Func<long, bool>? isQuestUnavailable = null,
        Func<DaggerfallNpc, bool>? hasDepositWaiver = null,
        Func<int, ulong, ulong>? hireFee = null,
        DaggerfallWorldConditions? worldConditions = null,
        Func<int>? currentDay = null,
        Func<string, int>? readPlayerSkill = null,
        Func<int, int?>? readGuildRank = null,
        Func<bool>? consumeHealingDraught = null)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _partyHireSpecs = definitions.ActorCatalog.ProjectPartyHireSpecs();
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _actorLedger = actorLedger ?? throw new ArgumentNullException(nameof(actorLedger));
        _overlandParties = overlandParties ?? throw new ArgumentNullException(nameof(overlandParties));
        _activeProfile = activeProfile ?? throw new ArgumentNullException(nameof(activeProfile));
        _currentBuilding = currentBuilding ?? throw new ArgumentNullException(nameof(currentBuilding));
        _ownsCart = ownsCart ?? throw new ArgumentNullException(nameof(ownsCart));
        _availableGold = availableGold ?? throw new ArgumentNullException(nameof(availableGold));
        _spendGold = spendGold ?? throw new ArgumentNullException(nameof(spendGold));
        _isQuestUnavailable = isQuestUnavailable ?? (_ => false);
        _hasDepositWaiver = hasDepositWaiver ?? (_ => false);
        _hireFee = hireFee ?? ((_, fee) => fee);
        _worldConditions = worldConditions;
        _currentDay = currentDay ?? (() => 0);
        _readPlayerSkill = readPlayerSkill ?? (_ => 25);
        _readGuildRank = readGuildRank ?? (_ => null);
        _consumeHealingDraught = consumeHealingDraught ?? (() => false);
    }

    /// <summary>Validated <c>party-hire</c> spawner specs loaded from <c>actors/specs/party-hire.json</c>.</summary>
    internal IReadOnlyDictionary<string, DaggerfallActorSpawnerSpec> Specs => _partyHireSpecs;

    /// <summary>True when <paramref name="actorId"/> currently holds an active non-betrayed party contract (#11101).</summary>
    internal bool IsContractedHire(long actorId) =>
        _actorLedger.TryGet(actorId, out ActorLedgerEntry? entry)
        && entry?.PartyContract is { HostileBetrayal: false };

    /// <summary>Reads the sparse <see cref="ActorPartyContractDiff"/> for <paramref name="actorId"/> if present.</summary>
    internal bool TryGetContract(long actorId, out ActorPartyContractDiff contract)
    {
        if (_actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) && entry?.PartyContract is { } found)
        {
            contract = found;
            return true;
        }
        contract = null!;
        return false;
    }

    /// <summary>Dialogue service options offered for <paramref name="npc"/> under the Services topic group.</summary>
    internal IReadOnlyList<DaggerfallDialogueTopicOption> TopicsFor(DaggerfallNpc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (TryGetStagedHiredRole(npc.DurableId, out _))
        {
            List<DaggerfallDialogueTopicOption> activeTopics = [];
            if (TryGetContract(npc.DurableId, out ActorPartyContractDiff contract) && !contract.HostileBetrayal)
            {
                activeTopics.Add(new("give-healing-draught", "Give healing draught"));
                ulong bonus = HazardBonusGold(contract);
                string bonusLabel = contract.UnpaidDays > 0
                    ? $"Pay overdue wages & hazard bonus ({bonus} gold)"
                    : $"Pay hazard bonus ({bonus} gold)";
                activeTopics.Add(new("pay-hazard-bonus", bonusLabel));
            }
            activeTopics.Add(new("dismiss-party", "Part ways here"));
            return activeTopics;
        }

        if (!IsHireable(npc))
            return [];

        bool waived = _hasDepositWaiver(npc);
        DaggerfallPartyHireQuote companionQuote = QuoteHire(npc, ActorPartyRole.Companion);
        string specName = _partyHireSpecs.TryGetValue(companionQuote.SpawnerId, out DaggerfallActorSpawnerSpec? spec)
            ? spec.DisplayName
            : "Road Guard";
        string companionLabel = !companionQuote.Eligible
            ? $"Enlist {specName} ({companionQuote.RefusalReason ?? "requirements not met"})"
            : waived
                ? $"Enlist {specName} (deposit waived, {companionQuote.DailyWage} gold/day)"
                : $"Enlist {specName} ({companionQuote.Deposit} gold, {companionQuote.DailyWage} gold/day)";
        List<DaggerfallDialogueTopicOption> topics = [new("hire-companion", companionLabel)];
        if (_ownsCart() && !HasActiveDriver())
        {
            DaggerfallPartyHireQuote driverQuote = QuoteHire(npc, ActorPartyRole.Driver);
            topics.Add(new("hire-driver", waived
                ? "Drive my wagon (deposit waived)"
                : $"Drive my wagon ({driverQuote.Deposit} gold)"));
        }
        return topics;
    }

    internal DaggerfallPartyHireResult Resolve(DaggerfallNpc npc, DaggerfallDialogueTopic topic)
    {
        ArgumentNullException.ThrowIfNull(npc);
        return topic switch
        {
            DaggerfallDialogueTopic.HireCompanion => TryHire(npc, ActorPartyRole.Companion),
            DaggerfallDialogueTopic.HireDriver => TryHire(npc, ActorPartyRole.Driver),
            DaggerfallDialogueTopic.DismissParty => TryDismiss(npc),
            DaggerfallDialogueTopic.GiveHealingDraught => TryGiveHealingDraught(npc),
            DaggerfallDialogueTopic.PayHazardBonus => TryPayHazardBonus(npc),
            _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, "Not a party hire dialogue topic."),
        };
    }

    /// <summary>
    /// Computes the authoritative deposit, daily wage, and eligibility for hiring <paramref name="npc"/>
    /// from its resolved <c>party-hire</c> spec <c>hireTerms</c>, active regional A6 world conditions
    /// (<c>MonsterSurge</c>, <c>GrainShortage</c>, <c>BorderTension</c>/<c>WarFront</c>, recruitment decrees),
    /// social skills (<c>Mercantile</c>, <c>Etiquette</c>, <c>Streetwise</c>), and <see cref="DaggerfallIntroductions"/>
    /// deposit waivers (#11101).
    /// </summary>
    internal DaggerfallPartyHireQuote QuoteHire(DaggerfallNpc npc, ActorPartyRole role, string? explicitSpecId = null)
    {
        ArgumentNullException.ThrowIfNull(npc);
        bool depositWaived = _hasDepositWaiver(npc);
        if (role == ActorPartyRole.Driver)
        {
            ulong driverFee = depositWaived ? 0UL : ApplyPricingAdjustments(npc.Site.Region, DriverHireFee, applySocialDiscount: true);
            return new("hire-driver", driverFee, 0, CanDelveDungeons: false, Eligible: true);
        }

        string specId = ResolveSpecId(npc, explicitSpecId);
        DaggerfallActorSpawnerSpec? spec = _partyHireSpecs.TryGetValue(specId, out DaggerfallActorSpawnerSpec? found) ? found : null;
        DaggerfallPartyHireTerms? terms = spec?.HireTerms;

        int baseDeposit = terms?.BaseDeposit ?? (int)CompanionHireFee;
        int baseDailyWage = terms?.BaseDailyWage ?? DefaultDailyWage;
        bool canDelve = terms?.CanDelveDungeons ?? false;

        // Enforce explicit guild rank / vouch gates when hiring under a gated spec.
        if (terms is not null)
        {
            if (terms.GuildFactionId is int guildId && terms.MinimumGuildRank is int minRank)
            {
                int? playerRank = ReadQualifyingGuildRank(guildId, npc);
                if (playerRank is null || playerRank.Value < minRank)
                    return new(specId, 0UL, baseDailyWage, canDelve, Eligible: false,
                        RefusalReason: $"Requires guild rank {minRank} to enlist a {spec!.DisplayName}.");
            }
            if (terms.RequiresVouch && !depositWaived)
            {
                return new(specId, 0UL, baseDailyWage, canDelve, Eligible: false,
                    RefusalReason: $"Requires a trusted introduction or vouch to enlist a {spec!.DisplayName}.");
            }
        }

        ulong deposit = depositWaived
            ? 0UL
            : ApplyPricingAdjustments(npc.Site.Region, (ulong)Math.Max(0, baseDeposit), applySocialDiscount: true);
        int dailyWage = checked((int)ApplyPricingAdjustments(npc.Site.Region, (ulong)Math.Max(1, baseDailyWage), applySocialDiscount: true));
        return new(specId, deposit, Math.Max(1, dailyWage), canDelve, Eligible: true);
    }

    /// <summary>
    /// Rolls over daily wages on calendar day boundaries for every contracted party member (both travelling in
    /// <see cref="ActorLocationKind.WithParty"/> and staged in the active scene). Tracks <see cref="ActorPartyContractDiff.EffectiveLastBilledDay"/>
    /// separately from <see cref="ActorPartyContractDiff.PaidThroughDay"/> so repeated intra-day calendar ticks
    /// (including after save/load) are strictly idempotent and only newly elapsed days accrue arrears or morale loss (#11101).
    /// </summary>
    internal IReadOnlyList<DaggerfallPartyDailyOutcome> AdvanceDay(int currentDay)
    {
        if (currentDay < 0)
            return [];

        List<DaggerfallPartyDailyOutcome> outcomes = [];
        ActorLedgerEntry[] candidates =
        [
            .. _actorLedger.All
                .Where(e => e.PartyContract is { HostileBetrayal: false }
                    && (e.Location?.Kind == ActorLocationKind.WithParty
                        || e.Social?.StagedPartyRole is ActorPartyRole.Companion or ActorPartyRole.Driver))
                .OrderBy(e => e.DurableId),
        ];

        foreach (ActorLedgerEntry entry in candidates)
        {
            ActorPartyContractDiff contract = entry.PartyContract!;
            int lastBilled = contract.EffectiveLastBilledDay;
            if (contract.DailyWage <= 0 || currentDay <= lastBilled)
                continue;

            string name = entry.Social?.DisplayNameOverride ?? entry.Social?.Role ?? "Your hire";
            int newDays = currentDay - lastBilled;
            int totalOwedDays = contract.UnpaidDays + newDays;
            ulong dailyWage = (ulong)contract.DailyWage;
            ulong available = _availableGold();
            int affordableDays = (int)Math.Min((ulong)totalOwedDays, available / dailyWage);
            ulong paidGold = 0UL;
            if (affordableDays > 0)
            {
                ulong toSpend = checked((ulong)affordableDays * dailyWage);
                if (_spendGold(toSpend))
                    paidGold = toSpend;
                else
                    affordableDays = 0;
            }

            int remainingUnpaid = totalOwedDays - affordableDays;
            if (remainingUnpaid == 0)
            {
                int boostedMorale = Math.Min(ActorPartyContractDiff.MaximumMorale, contract.Morale + PaidMoraleRecovery);
                _actorLedger.Upsert(entry with
                {
                    PartyContract = contract with
                    {
                        PaidThroughDay = currentDay,
                        LastBilledDay = currentDay,
                        UnpaidDays = 0,
                        Morale = boostedMorale,
                    },
                });
                outcomes.Add(new(entry.DurableId, name, Paid: true, Deserted: false, 0, boostedMorale, paidGold,
                    $"Paid {name} {paidGold} gold in daily wages."));
                continue;
            }

            int newlyUnpaidDays = Math.Min(newDays, remainingUnpaid);
            int newMorale = Math.Max(ActorPartyContractDiff.MinimumMorale, contract.Morale - (newlyUnpaidDays * UnpaidMoralePenaltyPerDay));
            if (remainingUnpaid >= MaxUnpaidDaysBeforeDesertion || newMorale <= DesertionMoraleThreshold)
            {
                WorldPoint? livePos = _actors.TryGet(entry.DurableId, out ActorState? live) ? live.Position : null;
                _overlandParties.ReleaseFromParty(entry.DurableId, _activeProfile().ProfileKey, livePos, clearContract: true);
                if (_actorLedger.TryGet(entry.DurableId, out ActorLedgerEntry? released) && released?.Social is { } rs)
                {
                    _actorLedger.Upsert(released with
                    {
                        Social = rs with { DispositionDelta = rs.DispositionDelta + DesertionDispositionPenalty },
                    });
                }
                outcomes.Add(new(entry.DurableId, name, Paid: false, Deserted: true, remainingUnpaid, newMorale, paidGold,
                    $"{name} has had enough of unpaid wages and parts ways with your party."));
            }
            else
            {
                int newPaidThrough = Math.Min(currentDay, contract.PaidThroughDay + affordableDays);
                _actorLedger.Upsert(entry with
                {
                    PartyContract = contract with
                    {
                        PaidThroughDay = newPaidThrough,
                        LastBilledDay = currentDay,
                        UnpaidDays = remainingUnpaid,
                        Morale = newMorale,
                    },
                });
                ulong owed = checked((ulong)remainingUnpaid * dailyWage);
                outcomes.Add(new(entry.DurableId, name, Paid: false, Deserted: false, remainingUnpaid, newMorale, paidGold,
                    $"{name} warns you that {owed} gold in wages is overdue."));
            }
        }

        return outcomes;
    }

    /// <summary>
    /// Single friendly-fire tolerance rule owned inside the morale owner (#11101): incidental player clips against a
    /// contracted hire emit a warning bark and drop morale without turning them hostile (up to <see cref="FriendlyFireTolerance"/>
    /// strikes); exceeding tolerance or dealing a lethal blow marks <see cref="ActorPartyContractDiff.HostileBetrayal"/>,
    /// dissolves the contract, and allows hostile retaliation and betrayal social consequences.
    /// </summary>
    internal DaggerfallFriendlyFireResult ObserveFriendlyFire(long targetActorId, bool fatal)
    {
        if (!_actorLedger.TryGet(targetActorId, out ActorLedgerEntry? entry)
            || entry?.PartyContract is not { HostileBetrayal: false } contract)
            return new(Handled: false, TurnedHostile: false, 0, 0, null);

        string name = entry.Social?.DisplayNameOverride ?? entry.Social?.Role ?? "Your hire";
        int strikes = contract.FriendlyFireStrikes + 1;
        int morale = fatal ? 0 : Math.Max(ActorPartyContractDiff.MinimumMorale, contract.Morale - FriendlyFireMoralePenalty);

        if (fatal)
        {
            _actorLedger.Upsert(entry with
            {
                PartyContract = contract with
                {
                    FriendlyFireStrikes = strikes,
                    Morale = 0,
                    HostileBetrayal = true,
                },
            });
            return new(Handled: true, TurnedHostile: true, strikes, 0, null);
        }

        if (strikes <= FriendlyFireTolerance && morale > DesertionMoraleThreshold)
        {
            _actorLedger.Upsert(entry with
            {
                PartyContract = contract with
                {
                    FriendlyFireStrikes = strikes,
                    Morale = morale,
                },
            });
            string bark = strikes == FriendlyFireTolerance
                ? $"{name}: \"Watch your blade! One more strike like that and our contract is finished.\""
                : $"{name}: \"Careful with that weapon — I'm on your side!\"";
            return new(Handled: true, TurnedHostile: false, strikes, morale, bark);
        }

        // Tolerance breached: mark betrayal (kept until death so SocialConsequences distinguishes betrayal) and drop party custody.
        _actorLedger.Upsert(entry with
        {
            PartyContract = contract with
            {
                FriendlyFireStrikes = strikes,
                Morale = morale,
                HostileBetrayal = true,
            },
        });
        WorldPoint? livePos = _actors.TryGet(targetActorId, out ActorState? live) ? live.Position : null;
        _overlandParties.ReleaseFromParty(targetActorId, _activeProfile().ProfileKey, livePos, clearContract: false);
        return new(Handled: true, TurnedHostile: true, strikes, morale,
            $"{name}: \"That's betrayal! Our contract is finished — defend yourself!\"");
    }

    /// <summary>Clears any lingering party contract after death consequences have run so slain hires are never billed wages (#11101).</summary>
    internal void ClearContractOnDeath(long actorId)
    {
        if (_actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) && entry?.PartyContract is not null)
            _actorLedger.Upsert(entry with { PartyContract = null });
    }

    /// <summary>Resolves a real published Daggerfall portrait media ID for a hired party member.</summary>
    internal static string ResolvePortraitMedia(DaggerfallDefinitions definitions, DaggerfallNpc npc)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(npc);
        DaggerfallCharacterPresentationSet presentation = definitions.CharacterPresentation;
        DaggerfallCharacterGender gender = string.Equals(npc.Appearance.Gender, "Female", StringComparison.OrdinalIgnoreCase)
            ? DaggerfallCharacterGender.Female
            : DaggerfallCharacterGender.Male;
        string race = string.Equals(npc.Appearance.Race, "redguard", StringComparison.OrdinalIgnoreCase)
            ? "redguard"
            : "breton";
        var heads = presentation.RequireRace(race).Heads(gender);
        if (heads.Count == 0)
            throw new NotSupportedException($"Character presentation has no portrait heads for {race} {gender}.");
        int seed = Math.Abs(npc.Appearance.NameSeed != 0 ? npc.Appearance.NameSeed : (int)(npc.DurableId & 0x7FFF_FFFFL));
        return heads[seed % heads.Count].MediaId;
    }

    private string ResolveSpecId(DaggerfallNpc npc, string? explicitSpecId)
    {
        if (!string.IsNullOrWhiteSpace(explicitSpecId))
            return explicitSpecId;
        if (_actors.TryGet(npc.DurableId, out ActorState? live))
        {
            if (live.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? origin)
                && !string.IsNullOrWhiteSpace(origin.Origin.SpawnerId)
                && _partyHireSpecs.ContainsKey(origin.Origin.SpawnerId))
                return origin.Origin.SpawnerId;
        }
        if (_actorLedger.TryGet(npc.DurableId, out ActorLedgerEntry? ledgerEntry)
            && !string.IsNullOrWhiteSpace(ledgerEntry?.Origin.SpawnerId)
            && _partyHireSpecs.ContainsKey(ledgerEntry.Origin.SpawnerId))
            return ledgerEntry.Origin.SpawnerId;

        foreach (DaggerfallActorSpawnerSpec spec in _partyHireSpecs.Values)
        {
            if (!string.IsNullOrWhiteSpace(spec.Role)
                && string.Equals(npc.Role, spec.Role, StringComparison.OrdinalIgnoreCase))
                return spec.Id;
        }

        if (_actors.TryGet(npc.DurableId, out ActorState? liveForArchetype)
            && liveForArchetype.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov))
        {
            if (string.Equals(ov.ArchetypeId, "knight", StringComparison.OrdinalIgnoreCase)
                && _partyHireSpecs.ContainsKey(KnightRetainerSpecId))
                return KnightRetainerSpecId;
            if ((string.Equals(ov.ArchetypeId, "battlemage", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ov.ArchetypeId, "mage", StringComparison.OrdinalIgnoreCase))
                && _partyHireSpecs.ContainsKey(BattleMageSpecId))
                return BattleMageSpecId;
            if ((string.Equals(ov.ArchetypeId, "archer", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ov.ArchetypeId, "assassin", StringComparison.OrdinalIgnoreCase))
                && _partyHireSpecs.ContainsKey(MercenarySpecId))
                return MercenarySpecId;
        }

        if (_currentBuilding() is { BuildingType: GuildHallBuildingType } hall)
        {
            if (hall.FactionId == 40 && _partyHireSpecs.ContainsKey(BattleMageSpecId))
                return BattleMageSpecId;
            if ((hall.FactionId == 41 || IsKnightlyOrderFaction(hall.FactionId))
                && _partyHireSpecs.ContainsKey(KnightRetainerSpecId))
                return KnightRetainerSpecId;
        }

        return DefaultRoadGuardSpecId;
    }

    private int? ReadQualifyingGuildRank(int requiredGuildFactionId, DaggerfallNpc npc)
    {
        int? direct = _readGuildRank(requiredGuildFactionId);
        // Knightly Orders (Type 10) qualify alongside Fighters Guild (41) for Knightly Retainers.
        if (requiredGuildFactionId == 41)
        {
            int? hallFaction = _currentBuilding()?.FactionId;
            if (hallFaction is int hf && hf != 0 && IsKnightlyOrderFaction(hf))
                direct = MaxNullable(direct, _readGuildRank(hf));
            if (npc.Appearance.FactionId != 0 && IsKnightlyOrderFaction(npc.Appearance.FactionId))
                direct = MaxNullable(direct, _readGuildRank(npc.Appearance.FactionId));
        }
        return direct;
    }

    private bool IsKnightlyOrderFaction(int factionId) =>
        _definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction)
        && (faction.GuildGroup == 9 || faction.Type == 10 || StringComparer.Ordinal.Equals(faction.TypeName, "KnightlyGuard"));

    private static int? MaxNullable(int? left, int? right) =>
        left is int l && right is int r ? Math.Max(l, r) : left ?? right;

    /// <summary>
    /// Computes the combined hazard percent from active regional A6 world conditions (<c>MonsterSurge</c>,
    /// <c>GrainShortage</c>, <c>BorderTension</c>, and <c>WarFront</c> decrees) on the current day.
    /// </summary>
    internal int RegionalHazardPercent(int region)
    {
        if (_worldConditions is null)
            return 100;
        int extra = 0;
        foreach (DaggerfallWorldConditionToken token in _worldConditions.ActiveForRegion(region, _currentDay()))
        {
            extra += token.Kind switch
            {
                DaggerfallWorldConditionKind.MonsterSurge => 15 * token.Severity,
                DaggerfallWorldConditionKind.GrainShortage => 10 * token.Severity,
                DaggerfallWorldConditionKind.BorderTension => 15 * token.Severity,
                DaggerfallWorldConditionKind.Decree when IsWarFrontTag(token.SubjectTag) => 15 * token.Severity,
                _ => 0,
            };
        }
        return 100 + extra;
    }

    private static bool IsWarFrontTag(string? tag) =>
        string.Equals(tag, WarFrontTag, StringComparison.OrdinalIgnoreCase)
        || string.Equals(tag, "warfront", StringComparison.OrdinalIgnoreCase);

    private ulong ApplyPricingAdjustments(int region, ulong baseGold, bool applySocialDiscount)
    {
        if (baseGold == 0UL)
            return 0UL;
        int hazardPercent = RegionalHazardPercent(region);
        ulong adjusted = checked(baseGold * (ulong)hazardPercent / 100UL);
        adjusted = _hireFee(region, Math.Max(1UL, adjusted));
        if (applySocialDiscount)
        {
            int bestSkill = Math.Max(
                _readPlayerSkill("mercantile"),
                Math.Max(_readPlayerSkill("etiquette"), _readPlayerSkill("streetwise")));
            if (bestSkill > 25)
            {
                int discountPercent = Math.Min(25, (bestSkill - 25) / 3);
                adjusted = Math.Max(1UL, checked(adjusted * (ulong)(100 - discountPercent) / 100UL));
            }
        }
        return Math.Max(1UL, adjusted);
    }

    private static ulong HazardBonusGold(ActorPartyContractDiff contract)
    {
        ulong baseBonus = (ulong)Math.Max(10, contract.DailyWage);
        ulong arrears = contract.UnpaidDays > 0 ? checked((ulong)contract.UnpaidDays * (ulong)Math.Max(0, contract.DailyWage)) : 0UL;
        return checked(baseBonus + arrears);
    }

    private DaggerfallPartyHireResult TryHire(DaggerfallNpc npc, ActorPartyRole role, string? explicitSpecId = null)
    {
        string name = npc.DisplayName ?? npc.Role;
        string question = role == ActorPartyRole.Driver
            ? "Will you drive my wagon on the road?"
            : "Will you travel with me?";

        if (!IsHireable(npc))
            return new(false, question, $"{name} declines your offer.");

        if (ActivePartyMemberCount() >= MaximumPartyMembers)
            return new(false, question, "Your party already has as many companions as can travel together.");

        if (role == ActorPartyRole.Driver)
        {
            if (!_ownsCart())
                return new(false, question, "You need a cart or wagon before hiring a driver.");
            if (HasActiveDriver())
                return new(false, question, "You already have a driver handling your wagon.");
        }

        DaggerfallPartyHireQuote quote = QuoteHire(npc, role, explicitSpecId);
        if (!quote.Eligible)
            return new(false, question, quote.RefusalReason ?? $"{name} declines your terms.");

        bool depositWaived = _hasDepositWaiver(npc);
        ulong fee = quote.Deposit;
        if (fee > 0UL && (_availableGold() < fee || !_spendGold(fee)))
            return new(false, question, $"You need {fee} gold to hire {name}.");

        if (!_actors.TryGet(npc.DurableId, out ActorState? live))
            return new(false, question, $"{name} is no longer here.");

        WorldPoint currentPos = live.Position;
        float currentYaw = live.HeadingYawRadians;
        string portrait = ResolvePortraitMedia(_definitions, npc);
        int hireDay = Math.Max(0, _currentDay());
        ActorPartyContractDiff contract = new(
            SpawnerId: quote.SpawnerId,
            DailyWage: quote.DailyWage,
            PaidThroughDay: hireDay,
            Morale: ActorPartyContractDiff.DefaultMorale,
            Stance: ActorPartyStance.Follow,
            CanDelveDungeons: quote.CanDelveDungeons,
            LastBilledDay: hireDay);

        _overlandParties.JoinParty(npc.DurableId, role, portraitMedia: portrait, contract: contract);
        _ = _overlandParties.StagePartyMember(npc.DurableId, _activeProfile(), currentPos, currentYaw);

        string wageClause = quote.DailyWage > 0 ? $" at {quote.DailyWage} gold a day" : string.Empty;
        string reply = depositWaived
            ? (role == ActorPartyRole.Driver
                ? $"On account of your introduction, {name} waives the deposit, takes the reins, and joins your party."
                : $"On account of your introduction, {name} waives the deposit and joins your party{wageClause}.")
            : (role == ActorPartyRole.Driver
                ? $"Agreed — {name} takes the reins for {fee} gold and joins your party."
                : $"Agreed — {name} accepts {fee} gold and joins your party{wageClause}.");
        return new(true, question, reply);
    }

    private DaggerfallPartyHireResult TryGiveHealingDraught(DaggerfallNpc npc)
    {
        string name = npc.DisplayName ?? npc.Role;
        const string question = "Take this healing draught.";
        if (!TryGetStagedHiredRole(npc.DurableId, out _)
            || !_actorLedger.TryGet(npc.DurableId, out ActorLedgerEntry? entry)
            || entry?.PartyContract is not { HostileBetrayal: false } contract)
            return new(false, question, $"{name} is not under contract with your party.");

        if (!_actors.TryGet(npc.DurableId, out ActorState? live) || live.IsDefeated)
            return new(false, question, $"{name} cannot take a draught right now.");

        if (!_consumeHealingDraught())
            return new(false, question, "You have no healing draught in your pack to share.");

        Track health = live.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        double before = health.Current;
        double healedTo = Math.Min(health.Maximum.Value, before + HealingDraughtHealthAmount);
        health.SetCurrent(healedTo);

        int newMorale = Math.Min(ActorPartyContractDiff.MaximumMorale, contract.Morale + HealingDraughtMoraleBonus);
        _actorLedger.Upsert(entry with { PartyContract = contract with { Morale = newMorale } });
        return new(true, question, $"{name} drinks the healing draught and steadies their footing.");
    }

    private DaggerfallPartyHireResult TryPayHazardBonus(DaggerfallNpc npc)
    {
        string name = npc.DisplayName ?? npc.Role;
        const string question = "Here is extra coin for the hazards ahead.";
        if (!TryGetStagedHiredRole(npc.DurableId, out _)
            || !_actorLedger.TryGet(npc.DurableId, out ActorLedgerEntry? entry)
            || entry?.PartyContract is not { HostileBetrayal: false } contract)
            return new(false, question, $"{name} is not under contract with your party.");

        ulong bonus = HazardBonusGold(contract);
        if (_availableGold() < bonus || !_spendGold(bonus))
            return new(false, question, $"You need {bonus} gold to pay {name}'s hazard bonus.");

        int newMorale = Math.Min(ActorPartyContractDiff.MaximumMorale, contract.Morale + HazardBonusMoraleBonus);
        int newStrikes = Math.Max(0, contract.FriendlyFireStrikes - 1);
        int paidThrough = Math.Max(contract.EffectiveLastBilledDay, Math.Max(0, _currentDay()));
        _actorLedger.Upsert(entry with
        {
            PartyContract = contract with
            {
                PaidThroughDay = paidThrough,
                LastBilledDay = paidThrough,
                UnpaidDays = 0,
                Morale = newMorale,
                FriendlyFireStrikes = newStrikes,
            },
        });
        return new(true, question, $"{name} pockets the {bonus} gold with a sharp nod. \"Fair coin — I've got your back.\"");
    }

    private DaggerfallPartyHireResult TryDismiss(DaggerfallNpc npc)
    {
        string name = npc.DisplayName ?? npc.Role;
        const string question = "Our road together ends here.";
        if (!TryGetStagedHiredRole(npc.DurableId, out _))
            return new(false, question, $"{name} is not travelling in your party.");

        WorldPoint? currentPos = _actors.TryGet(npc.DurableId, out ActorState? live) ? live.Position : null;
        _overlandParties.ReleaseFromParty(npc.DurableId, _activeProfile().ProfileKey, currentPos, clearContract: true);
        return new(true, question, $"Safe travels. {name} parts ways with your party here.");
    }

    internal bool IsHireable(DaggerfallNpc npc)
    {
        if (npc.Presence != DaggerfallNpcPresence.Active || npc.Kind == DaggerfallNpcKind.Questor)
            return false;
        if (npc.StableKey.StartsWith("cast:", StringComparison.Ordinal))
            return false;
        if (_isQuestUnavailable(npc.DurableId))
            return false;
        if (NonHireableRoles.Contains(npc.Role))
            return false;
        if (!_actors.TryGet(npc.DurableId, out ActorState? live) || live.IsDefeated)
            return false;
        if (live.Actor.TryGet<ActorInteractionPolicy>(out ActorInteractionPolicy? interaction) && !interaction.CanTalk)
            return false;
        if (TryGetStagedHiredRole(npc.DurableId, out _))
            return false;
        if (live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
            && (!string.IsNullOrWhiteSpace(overlay.StagedPartyRole) || overlay.IsQuestor))
            return false;
        if (_actorLedger.TryGet(npc.DurableId, out ActorLedgerEntry? entry)
            && entry?.Location?.Kind is ActorLocationKind.WithParty or ActorLocationKind.OnOverlandBand or ActorLocationKind.Removed)
            return false;

        string? archetype = live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov) ? ov.ArchetypeId : null;
        if (string.Equals(archetype, "warrior", StringComparison.Ordinal)
            || string.Equals(archetype, "assassin", StringComparison.Ordinal))
            return true;

        int? buildingType = _currentBuilding()?.BuildingType;
        if (buildingType is TavernBuildingType or GuildHallBuildingType)
            return true;

        return npc.Kind == DaggerfallNpcKind.Civilian;
    }

    internal bool TryGetStagedHiredRole(long actorId, out ActorPartyRole role)
    {
        if (_actors.TryGet(actorId, out ActorState? live)
            && live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
            && !string.IsNullOrWhiteSpace(overlay.StagedPartyRole)
            && Enum.TryParse(overlay.StagedPartyRole, out role)
            && role is ActorPartyRole.Driver or ActorPartyRole.Companion)
        {
            return true;
        }
        role = default;
        return false;
    }

    private int ActivePartyMemberCount()
    {
        HashSet<long> ids = [.. _actorLedger.EntriesWithParty().Select(e => e.DurableId)];
        foreach (long stagedId in _overlandParties.StagedPartyMemberIds)
            ids.Add(stagedId);
        return ids.Count;
    }

    private bool HasActiveDriver()
    {
        if (_actorLedger.EntriesWithParty().Any(e => e.Location?.PartyRole == ActorPartyRole.Driver))
            return true;
        foreach (ActorState live in _actors.All)
        {
            if (live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
                && string.Equals(overlay.StagedPartyRole, nameof(ActorPartyRole.Driver), StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
