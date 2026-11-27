using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Outcome of a party hiring or dismissal dialogue action.</summary>
internal sealed record DaggerfallPartyHireResult(bool Accepted, string Question, string Message);

/// <summary>
/// Gameplay producer for enlisting seeded townsfolk, tavern patrons, and guild mercenaries into
/// <see cref="ActorLocationKind.WithParty"/> as a <see cref="ActorPartyRole.Companion"/> or
/// <see cref="ActorPartyRole.Driver"/> via normal conversation, and releasing them via
/// <see cref="DaggerfallOverlandPartyCoordinator.ReleaseFromParty"/>.
/// </summary>
internal sealed class DaggerfallPartyHireService
{
    internal const ulong CompanionHireFee = 25UL;
    internal const ulong DriverHireFee = 15UL;
    internal const int MaximumPartyMembers = 3;
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
        Func<int, ulong, ulong>? hireFee = null)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
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
    }

    /// <summary>Dialogue service options offered for <paramref name="npc"/> under the Services topic group.</summary>
    internal IReadOnlyList<DaggerfallDialogueTopicOption> TopicsFor(DaggerfallNpc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (TryGetStagedHiredRole(npc.DurableId, out _))
            return [new("dismiss-party", "Part ways here")];

        if (!IsHireable(npc))
            return [];

        bool waived = _hasDepositWaiver(npc);
        List<DaggerfallDialogueTopicOption> topics =
        [
            new("hire-companion", waived ? "Travel with me (deposit waived)" : $"Travel with me ({Fee(npc, ActorPartyRole.Companion)} gold)"),
        ];
        if (_ownsCart() && !HasActiveDriver())
            topics.Add(new("hire-driver", waived ? "Drive my wagon (deposit waived)" : $"Drive my wagon ({Fee(npc, ActorPartyRole.Driver)} gold)"));
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
            _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, "Not a party hire dialogue topic."),
        };
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

    /// <summary>What hiring <paramref name="npc"/> as <paramref name="role"/> costs where they are: recruitment drives it down.</summary>
    private ulong Fee(DaggerfallNpc npc, ActorPartyRole role) =>
        _hireFee(npc.Site.Region, role == ActorPartyRole.Driver ? DriverHireFee : CompanionHireFee);

    private DaggerfallPartyHireResult TryHire(DaggerfallNpc npc, ActorPartyRole role)
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

        bool depositWaived = _hasDepositWaiver(npc);
        ulong fee = depositWaived ? 0UL : Fee(npc, role);
        if (fee > 0UL && (_availableGold() < fee || !_spendGold(fee)))
            return new(false, question, $"You need {fee} gold to hire {name}.");

        if (!_actors.TryGet(npc.DurableId, out ActorState? live))
            return new(false, question, $"{name} is no longer here.");

        WorldPoint currentPos = live.Position;
        float currentYaw = live.HeadingYawRadians;
        string portrait = ResolvePortraitMedia(_definitions, npc);

        _overlandParties.JoinParty(npc.DurableId, role, portraitMedia: portrait);
        _ = _overlandParties.StagePartyMember(npc.DurableId, _activeProfile(), currentPos, currentYaw);

        string reply = depositWaived
            ? (role == ActorPartyRole.Driver
                ? $"On account of your introduction, {name} waives the deposit, takes the reins, and joins your party."
                : $"On account of your introduction, {name} waives the deposit and joins your party.")
            : (role == ActorPartyRole.Driver
                ? $"Agreed — {name} takes the reins for {fee} gold and joins your party."
                : $"Agreed — {name} accepts {fee} gold and joins your party.");
        return new(true, question, reply);
    }

    private DaggerfallPartyHireResult TryDismiss(DaggerfallNpc npc)
    {
        string name = npc.DisplayName ?? npc.Role;
        const string question = "Our road together ends here.";
        if (!TryGetStagedHiredRole(npc.DurableId, out _))
            return new(false, question, $"{name} is not travelling in your party.");

        WorldPoint? currentPos = _actors.TryGet(npc.DurableId, out ActorState? live) ? live.Position : null;
        _overlandParties.ReleaseFromParty(npc.DurableId, _activeProfile().ProfileKey, currentPos);
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

    private bool TryGetStagedHiredRole(long actorId, out ActorPartyRole role)
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
