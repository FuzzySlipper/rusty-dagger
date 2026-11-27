using System.Globalization;
using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Where a bounty the player took from a notice board stands.</summary>
internal enum DaggerfallNoticeBountyStatus
{
    /// <summary>Taken: its condition still holds.</summary>
    Taken = 0,

    /// <summary>The player broke its condition: the reward waits to be claimed.</summary>
    Earned = 1,
}

/// <summary>
/// A bounty the player took from a notice board: the lead it came from, the occurrence of the condition it is posted
/// against (its token, and the day that occurrence started, since a token id recurs), the classic region, what it names and who posted it (the town's authority, never a condition's hidden source), its reward
/// in gold, and when it was taken and earned.
/// </summary>
internal sealed record DaggerfallNoticeBountySave(
    [property: JsonRequired] string LeadId,
    [property: JsonRequired] string TokenId,
    [property: JsonRequired] int TokenStartedDay,
    [property: JsonRequired] int Region,
    [property: JsonRequired] string Target,
    [property: JsonRequired] string Issuer,
    [property: JsonRequired] int Reward,
    [property: JsonRequired] int TakenDay,
    [property: JsonRequired] DaggerfallNoticeBountyStatus Status,
    int? EarnedDay = null)
{
    internal DaggerfallNoticeBountySave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(LeadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(TokenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Target);
        ArgumentException.ThrowIfNullOrWhiteSpace(Issuer);
        if (Region is < 0 or > 61) throw new ArgumentOutOfRangeException(nameof(Region), Region, $"Bounty '{LeadId}' names no classic region.");
        if (Reward <= 0) throw new ArgumentOutOfRangeException(nameof(Reward), Reward, $"Bounty '{LeadId}' pays nothing.");
        if (!Enum.IsDefined(Status)) throw new ArgumentOutOfRangeException(nameof(Status), Status, $"Bounty '{LeadId}' has no status.");
        if ((Status == DaggerfallNoticeBountyStatus.Earned) != EarnedDay.HasValue)
            throw new ArgumentException($"Bounty '{LeadId}' is earned exactly when it has an earned day.", nameof(EarnedDay));
        return this;
    }
}

/// <summary>The notice-board bounties the player holds, as saved.</summary>
internal sealed record DaggerfallNoticeBountiesSave([property: JsonRequired] IReadOnlyList<DaggerfallNoticeBountySave> Bounties)
{
    internal static DaggerfallNoticeBountiesSave Empty { get; } = new([]);

    internal DaggerfallNoticeBountiesSave Validate()
    {
        foreach (DaggerfallNoticeBountySave bounty in Bounties) bounty.Validate();
        if (Bounties.Select(bounty => bounty.LeadId).Distinct(StringComparer.Ordinal).Count() != Bounties.Count)
            throw new ArgumentException("A notice-board bounty is held twice.", nameof(Bounties));
        return this;
    }
}

/// <summary>What claiming a bounty from someone came to.</summary>
internal sealed record DaggerfallNoticeBountyClaim(bool Accepted, string Question, string Message);

/// <summary>
/// Notice-board bounties that pay out (#11120's notice boards, #11137). Taking a bounty lead from a board records who
/// posted it and its reward (<see cref="DaggerfallLawTuning.BountyRewardPerSeverity"/> for each point of its condition's
/// severity when taken). When the player's own clearing strips that condition (<see cref="DaggerfallConditionCounterplay"/>),
/// the bounty is earned, and any guard or court official of its region pays it: the gold, a gain in standing with the
/// region and a journal entry. A bounty whose condition ends before the player breaks it lapses with it, and an earned
/// one left unclaimed lapses after <see cref="DaggerfallLawTuning.BountyClaimDays"/> days.
/// </summary>
internal sealed class DaggerfallNoticeBounties(
    IDaggerfallWorldConditionSource conditions,
    DaggerfallBookNotebook notebook,
    DaggerfallSocialState social,
    DaggerfallDefinitions definitions,
    DaggerfallLawTuning law,
    Func<int> currentDay,
    Action<int> payGold)
{
    private const int ProvinceFactionType = 7;
    private const int CourtFactionType = 14;
    private const int PeopleFactionType = 15;

    private readonly Dictionary<string, DaggerfallNoticeBountySave> _bounties = new(StringComparer.Ordinal);

    internal IReadOnlyCollection<DaggerfallNoticeBountySave> All => _bounties.Values;

    /// <summary>
    /// Takes the bounty <paramref name="entry"/> posts, against its live condition token, issued by
    /// <paramref name="issuer"/>. Returns the reward, or null where it posts no live condition or is already held.
    /// </summary>
    internal int? Take(DaggerfallNoticeEntry entry, string issuer)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Prune();
        if (entry.Lead is not { Kind: DaggerfallNoticeLeadKind.RegionalBounty } lead || entry.ConditionTokenId is not { } tokenId
            || _bounties.ContainsKey(lead.LeadId)
            || conditions.ActiveForRegion(entry.Region, currentDay()).FirstOrDefault(token => token.TokenId == tokenId) is not { } token)
            return null;
        int reward = checked(token.Severity * law.BountyRewardPerSeverity);
        _bounties[lead.LeadId] = new DaggerfallNoticeBountySave(lead.LeadId, tokenId, token.StartedDay, entry.Region, entry.Heading, issuer, reward,
            currentDay(), DaggerfallNoticeBountyStatus.Taken).Validate();
        return reward;
    }

    /// <summary>The player's clearing weakened or stripped a condition: a bounty held against one now stripped is earned.</summary>
    internal void ObserveCounterplay(DaggerfallConditionCounterplayRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.Stripped) return;
        foreach (DaggerfallNoticeBountySave bounty in _bounties.Values
            .Where(bounty => bounty.Status == DaggerfallNoticeBountyStatus.Taken && bounty.TokenId == record.TokenId
                && bounty.TokenStartedDay == record.TokenStartedDay).ToArray())
        {
            _bounties[bounty.LeadId] = bounty with { Status = DaggerfallNoticeBountyStatus.Earned, EarnedDay = record.Day };
            notebook.TryAddNote($"bounty-earned:{bounty.LeadId}",
                $"Bounty earned: {bounty.Target}. Claim the {bounty.Reward} gold from the watch or the court of {RegionName(bounty.Region)}.");
        }
    }

    /// <summary>The bounties <paramref name="npc"/> can pay: earned ones of their region, for a guard or a court official.</summary>
    internal IReadOnlyList<DaggerfallNoticeBountySave> Claimable(DaggerfallNpc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        Prune();
        if (!CanPay(npc)) return [];
        return [.. _bounties.Values
            .Where(bounty => bounty.Status == DaggerfallNoticeBountyStatus.Earned && bounty.Region == npc.Site.Region)
            .OrderBy(bounty => bounty.LeadId, StringComparer.Ordinal)];
    }

    /// <summary><paramref name="npc"/> pays the player bounty <paramref name="leadId"/>.</summary>
    internal DaggerfallNoticeBountyClaim Claim(DaggerfallNpc npc, string leadId)
    {
        ArgumentNullException.ThrowIfNull(npc);
        const string question = "I've come about a bounty.";
        Prune();
        if (!_bounties.TryGetValue(leadId, out DaggerfallNoticeBountySave? bounty))
            return new(false, question, "There's no bounty of yours on the books.");
        if (bounty.Status != DaggerfallNoticeBountyStatus.Earned)
            return new(false, question, $"The bounty on {bounty.Target} stands until the job is done.");
        if (bounty.Region != npc.Site.Region || !CanPay(npc))
            return new(false, question, $"Take that to the watch or the court of {RegionName(bounty.Region)}.");

        _bounties.Remove(leadId);
        payGold(bounty.Reward);
        social.ChangeRegionalReputation(bounty.Region, law.BountyReputation);
        notebook.TryAddNote($"bounty-paid:{leadId}",
            $"Bounty paid: {bounty.Reward} gold for {bounty.Target}, by {npc.DisplayName ?? npc.Role} for {bounty.Issuer}.");
        return new(true, question, $"Well done. Here are your {bounty.Reward} gold, with the thanks of {bounty.Issuer}.");
    }

    internal DaggerfallNoticeBountiesSave Capture() =>
        new([.. _bounties.Values.OrderBy(bounty => bounty.LeadId, StringComparer.Ordinal)]);

    internal void Restore(DaggerfallNoticeBountiesSave? saved)
    {
        _bounties.Clear();
        foreach (DaggerfallNoticeBountySave bounty in (saved ?? DaggerfallNoticeBountiesSave.Empty).Validate().Bounties)
            _bounties[bounty.LeadId] = bounty;
    }

    /// <summary>
    /// A guard, or someone of the region's court (its province, its court, or those under them, but not its common
    /// people), pays bounties.
    /// </summary>
    private bool CanPay(DaggerfallNpc npc) =>
        string.Equals(npc.Role, "guard", StringComparison.Ordinal)
        || (definitions.Factions.Factions.GetValueOrDefault(npc.Appearance.FactionId) is { Type: not PeopleFactionType }
            && social.Liege.Lineage(npc.Appearance.FactionId)
                .Any(faction => faction.Type is ProvinceFactionType or CourtFactionType && faction.Region == npc.Site.Region));

    /// <summary>Lets go of a taken bounty whose condition has ended, and of an earned one left too long.</summary>
    private void Prune()
    {
        int day = currentDay();
        foreach (DaggerfallNoticeBountySave bounty in _bounties.Values.ToArray())
        {
            bool lapsed = bounty.Status == DaggerfallNoticeBountyStatus.Taken
                ? !conditions.ActiveForRegion(bounty.Region, day).Any(token => token.TokenId == bounty.TokenId && token.StartedDay == bounty.TokenStartedDay)
                : day > bounty.EarnedDay + law.BountyClaimDays;
            if (!lapsed) continue;
            _bounties.Remove(bounty.LeadId);
            notebook.TryAddNote(string.Create(CultureInfo.InvariantCulture, $"bounty-lapsed:{bounty.LeadId}:{day}"),
                bounty.Status == DaggerfallNoticeBountyStatus.Taken
                    ? $"Bounty lapsed: {bounty.Target}. The trouble in {RegionName(bounty.Region)} has passed without you."
                    : $"Bounty lapsed: {bounty.Target}. You never claimed the {bounty.Reward} gold.");
        }
    }

    private string RegionName(int region) => definitions.BuildingNames.RegionName(region);
}
