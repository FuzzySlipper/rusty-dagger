namespace WorldRpg.Rulesets.Daggerfall.Guilds;

/// <summary>Why a guild membership operation did not change the social owner.</summary>
internal enum DaggerfallGuildMembershipDenial
{
    None,
    AlreadyMember,
    NotMember,
    VariantConflict,
    InsufficientReputation,
    InsufficientSkills,
    ReviewTooSoon,
    NoRankChange,
}

/// <summary>The one membership change a policy operation may publish.</summary>
internal enum DaggerfallGuildMembershipChange
{
    None,
    Admitted,
    Rejoined,
    Promoted,
    Demoted,
    Expelled,
}

/// <summary>
/// One donor rank gate. A rank is earned when all of its reputation and skill counts pass.
/// </summary>
internal sealed record DaggerfallGuildRankRequirement(
    int Rank,
    int MinimumReputation,
    int HighSkillMinimum,
    int LowSkillMinimum,
    int RequiredHighSkills = 1,
    int RequiredTotalSkills = 2)
{
    internal void Validate()
    {
        if (Rank is < 0 or > DaggerfallSocialState.MaximumGuildRank)
            throw new ArgumentOutOfRangeException(nameof(Rank), Rank, "A guild rank is outside the admitted Daggerfall range.");
        if (MinimumReputation is < DaggerfallSocialState.MinimumReputation or > DaggerfallSocialState.MaximumReputation)
            throw new ArgumentOutOfRangeException(nameof(MinimumReputation), MinimumReputation, "A guild reputation threshold is outside the admitted Daggerfall range.");
        if (HighSkillMinimum is < 0 or > DaggerfallSkillUseReactions.MaximumSkillValue)
            throw new ArgumentOutOfRangeException(nameof(HighSkillMinimum), HighSkillMinimum, "A guild high-skill threshold is outside the admitted skill range.");
        if (LowSkillMinimum < 0 || LowSkillMinimum > HighSkillMinimum)
            throw new ArgumentOutOfRangeException(nameof(LowSkillMinimum), LowSkillMinimum, "A guild low-skill threshold must not exceed its high-skill threshold.");
        if (RequiredHighSkills < 0 || RequiredTotalSkills < RequiredHighSkills)
            throw new ArgumentOutOfRangeException(nameof(RequiredTotalSkills), RequiredTotalSkills, "Guild skill counts must require at least the high-skill count.");
    }
}

/// <summary>A named guild benefit or service gate owned by a concrete guild definition.</summary>
internal sealed record DaggerfallGuildPrivilegeDefinition(string Id, int MinimumRank)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        if (MinimumRank is < 0 or > DaggerfallSocialState.MaximumGuildRank)
            throw new ArgumentOutOfRangeException(nameof(MinimumRank), MinimumRank, "A guild privilege rank is outside the admitted Daggerfall range.");
    }
}

/// <summary>
/// Daggerfall-owned membership inputs for one faction. Concrete guild tasks supply skill lists and
/// privileges; the shared policy supplies donor rank arithmetic and social mutations.
/// </summary>
internal sealed class DaggerfallGuildPolicyDefinition
{
    internal DaggerfallGuildPolicyDefinition(
        int factionId,
        IEnumerable<string> guildSkills,
        bool neverExpels = false,
        IEnumerable<DaggerfallGuildPrivilegeDefinition>? privileges = null,
        int reviewIntervalDays = DaggerfallGuildMembershipPolicy.DefaultReviewIntervalDays,
        IEnumerable<DaggerfallGuildRankRequirement>? rankRequirements = null)
    {
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "A guild faction identity is positive.");
        ArgumentNullException.ThrowIfNull(guildSkills);
        if (reviewIntervalDays <= 0) throw new ArgumentOutOfRangeException(nameof(reviewIntervalDays), reviewIntervalDays, "A guild review interval is positive.");

        string[] skills = guildSkills.ToArray();
        if (skills.Length == 0 || skills.Any(string.IsNullOrWhiteSpace)
            || skills.Distinct(StringComparer.Ordinal).Count() != skills.Length)
            throw new ArgumentException("A guild must publish distinct non-empty skill identities.", nameof(guildSkills));

        DaggerfallGuildPrivilegeDefinition[] benefits = (privileges ?? []).ToArray();
        foreach (DaggerfallGuildPrivilegeDefinition benefit in benefits) benefit.Validate();
        if (benefits.Select(benefit => benefit.Id).Distinct(StringComparer.Ordinal).Count() != benefits.Length)
            throw new ArgumentException("A guild must publish distinct privilege identities.", nameof(privileges));

        DaggerfallGuildRankRequirement[] ranks = (rankRequirements ?? DaggerfallGuildMembershipPolicy.ClassicRankRequirements).ToArray();
        if (ranks.Length != DaggerfallSocialState.MaximumGuildRank + 1
            || ranks.Select(requirement => requirement.Rank).SequenceEqual(Enumerable.Range(0, ranks.Length)) is false)
            throw new ArgumentException("A guild must publish one rank requirement for every rank from zero through nine.", nameof(rankRequirements));
        foreach (DaggerfallGuildRankRequirement requirement in ranks) requirement.Validate();

        FactionId = factionId;
        GuildSkills = Array.AsReadOnly(skills);
        NeverExpels = neverExpels;
        Privileges = Array.AsReadOnly(benefits);
        ReviewIntervalDays = reviewIntervalDays;
        RankRequirements = Array.AsReadOnly(ranks);
    }

    internal int FactionId { get; }
    internal IReadOnlyList<string> GuildSkills { get; }
    internal bool NeverExpels { get; }
    internal IReadOnlyList<DaggerfallGuildPrivilegeDefinition> Privileges { get; }
    internal int ReviewIntervalDays { get; }
    internal IReadOnlyList<DaggerfallGuildRankRequirement> RankRequirements { get; }
}

/// <summary>The result of testing each contiguous donor rank gate.</summary>
internal sealed record DaggerfallGuildRankCheck(
    DaggerfallGuildRankRequirement Requirement,
    int HighSkillCount,
    int LowSkillCount,
    bool ReputationMet,
    bool SkillsMet)
{
    internal bool Qualified => ReputationMet && SkillsMet;
    internal int TotalSkillCount => HighSkillCount + LowSkillCount;
}

/// <summary>Current rank and skill evidence used by admission, review, and presentation callers.</summary>
internal sealed record DaggerfallGuildRankAssessment(
    int FactionId,
    int Reputation,
    int HighestQualifiedRank,
    IReadOnlyList<DaggerfallGuildRankCheck> Checks)
{
    internal bool MeetsAdmission => HighestQualifiedRank >= 0;
}

/// <summary>One current guild projection; no membership data is duplicated here.</summary>
internal sealed record DaggerfallGuildMembershipView(
    int FactionId,
    int GuildGroup,
    bool IsMember,
    int Rank,
    int Reputation,
    int LastRankChangeDay,
    int Recognition,
    int DaysUntilReview,
    DaggerfallGuildRankRequirement? CurrentRankRequirement,
    DaggerfallGuildRankRequirement? NextRankRequirement,
    int HighestQualifiedRank,
    IReadOnlyList<string> Privileges);

/// <summary>One policy operation and the post-operation view of the canonical social owner.</summary>
internal sealed record DaggerfallGuildMembershipResult(
    int FactionId,
    DaggerfallGuildMembershipChange Change,
    DaggerfallGuildMembershipDenial Denial,
    int PreviousRank,
    DaggerfallGuildMembershipView View,
    DaggerfallGuildRankAssessment Assessment)
{
    internal bool Applied => Change != DaggerfallGuildMembershipChange.None;
}

/// <summary>
/// Shared Daggerfall guild membership policy. It composes the current social owner instead of
/// maintaining a second membership dictionary, so services, quests, and saves read one record.
/// </summary>
internal sealed class DaggerfallGuildMembershipPolicy
{
    internal const int DefaultReviewIntervalDays = 28;

    // Guild.rankReqReputation, rankReqSkillHigh and rankReqSkillLow in the donor Guild.cs.
    internal static IReadOnlyList<DaggerfallGuildRankRequirement> ClassicRankRequirements { get; } =
        Array.AsReadOnly(new[]
        {
            new DaggerfallGuildRankRequirement(0, 0, 22, 4),
            new DaggerfallGuildRankRequirement(1, 10, 23, 5),
            new DaggerfallGuildRankRequirement(2, 20, 31, 9),
            new DaggerfallGuildRankRequirement(3, 30, 39, 13),
            new DaggerfallGuildRankRequirement(4, 40, 47, 17),
            new DaggerfallGuildRankRequirement(5, 50, 55, 21),
            new DaggerfallGuildRankRequirement(6, 60, 63, 25),
            new DaggerfallGuildRankRequirement(7, 70, 71, 29),
            new DaggerfallGuildRankRequirement(8, 80, 79, 33),
            new DaggerfallGuildRankRequirement(9, 90, 87, 37),
        });

    private readonly DaggerfallSocialState _social;
    private readonly Func<string, int> _permanentSkillValue;
    private readonly IReadOnlyDictionary<int, DaggerfallGuildPolicyDefinition> _definitions;

    internal DaggerfallGuildMembershipPolicy(
        DaggerfallSocialState social,
        Func<string, int> permanentSkillValue,
        IEnumerable<DaggerfallGuildPolicyDefinition> definitions)
    {
        _social = social ?? throw new ArgumentNullException(nameof(social));
        _permanentSkillValue = permanentSkillValue ?? throw new ArgumentNullException(nameof(permanentSkillValue));
        ArgumentNullException.ThrowIfNull(definitions);

        DaggerfallGuildPolicyDefinition[] values = definitions.ToArray();
        if (values.Length == 0) throw new ArgumentException("At least one guild policy definition is required.", nameof(definitions));
        foreach (DaggerfallGuildPolicyDefinition definition in values)
        {
            if (!_social.GuildEligibility(definition.FactionId).IsGuild)
                throw new ArgumentException($"Faction {definition.FactionId} does not name an admitted guild.", nameof(definitions));
        }
        if (values.Select(definition => definition.FactionId).Distinct().Count() != values.Length)
            throw new ArgumentException("Guild policy definitions must name distinct factions.", nameof(definitions));
        _definitions = values.ToDictionary(definition => definition.FactionId);
    }

    /// <summary>Reads the current rank evidence for a faction without changing social state.</summary>
    internal DaggerfallGuildRankAssessment Assess(int factionId)
    {
        DaggerfallGuildPolicyDefinition definition = RequireDefinition(factionId);
        int reputation = _social.GuildEligibility(factionId).Reputation;
        List<DaggerfallGuildRankCheck> checks = [];
        int highest = -1;
        bool blocked = false;

        foreach (DaggerfallGuildRankRequirement requirement in definition.RankRequirements)
        {
            (int high, int low) = blocked ? (0, 0) : CountSkills(definition.GuildSkills, requirement);
            bool reputationMet = !blocked && reputation >= requirement.MinimumReputation;
            bool skillsMet = !blocked && high >= requirement.RequiredHighSkills
                && high + low >= requirement.RequiredTotalSkills;
            DaggerfallGuildRankCheck check = new(requirement, high, low, reputationMet, skillsMet);
            checks.Add(check);
            if (check.Qualified)
                highest = requirement.Rank;
            else
                blocked = true;
        }

        return new(factionId, reputation, highest, Array.AsReadOnly(checks.ToArray()));
    }

    internal bool IsConfigured(int factionId) => _definitions.ContainsKey(factionId);

    /// <summary>Reads the current membership and the privileges granted by its rank.</summary>
    internal DaggerfallGuildMembershipView Read(int factionId, int currentDay)
    {
        DaggerfallGuildPolicyDefinition definition = RequireDefinition(factionId);
        DaggerfallGuildEligibility eligibility = _social.GuildEligibility(factionId);
        DaggerfallGuildMembershipSave? saved = FindMembership(factionId);
        bool isMember = eligibility.IsMember && saved is not null;
        int rank = isMember ? eligibility.Rank : -1;
        int guildGroup = isMember ? saved!.GuildGroup : 0;
        int lastRankChangeDay = isMember ? saved!.LastRankChangeDay : 0;
        int recognition = isMember ? saved!.NotedByGuild : 0;
        long elapsed = isMember ? (long)currentDay - lastRankChangeDay : 0;
        int daysUntilReview = isMember ? checked((int)Math.Max(0L, definition.ReviewIntervalDays - elapsed)) : 0;
        DaggerfallGuildRankRequirement? current = rank >= 0 ? definition.RankRequirements[rank] : null;
        DaggerfallGuildRankRequirement? next = rank < DaggerfallSocialState.MaximumGuildRank
            ? definition.RankRequirements[Math.Max(0, rank + 1)]
            : null;
        DaggerfallGuildRankAssessment assessment = Assess(factionId);
        string[] privileges = isMember
            ? [.. definition.Privileges.Where(privilege => privilege.MinimumRank <= rank).Select(privilege => privilege.Id)]
            : [];
        return new(factionId, guildGroup, isMember, rank, eligibility.Reputation, lastRankChangeDay,
            recognition, daysUntilReview, current, next, assessment.HighestQualifiedRank, Array.AsReadOnly(privileges));
    }

    /// <summary>Applies donor admission requirements and creates rank-zero membership.</summary>
    internal DaggerfallGuildMembershipResult Admit(int factionId, int currentDay) =>
        AdmitCore(factionId, currentDay, DaggerfallGuildMembershipChange.Admitted);

    /// <summary>Re-admits an explicitly expelled member through the same donor gates.</summary>
    internal DaggerfallGuildMembershipResult Rejoin(int factionId, int currentDay) =>
        AdmitCore(factionId, currentDay, DaggerfallGuildMembershipChange.Rejoined);

    /// <summary>
    /// Reviews promotion, demotion, or donor expulsion after the elapsed rank interval. Thieves and
    /// Dark Brotherhood definitions set <see cref="DaggerfallGuildPolicyDefinition.NeverExpels"/>.
    /// </summary>
    internal DaggerfallGuildMembershipResult ReviewRank(int factionId, int currentDay)
    {
        DaggerfallGuildPolicyDefinition definition = RequireDefinition(factionId);
        DaggerfallGuildRankAssessment assessment = Assess(factionId);
        DaggerfallGuildEligibility eligibility = _social.GuildEligibility(factionId);
        if (!eligibility.IsMember)
            return Result(factionId, DaggerfallGuildMembershipChange.None, DaggerfallGuildMembershipDenial.NotMember, -1, currentDay, assessment);

        DaggerfallGuildMembershipSave membership = FindMembership(factionId)
            ?? throw new InvalidOperationException($"Social state reports membership for faction {factionId} without a saved membership record.");
        long elapsed = (long)currentDay - membership.LastRankChangeDay;
        if (elapsed < definition.ReviewIntervalDays)
            return Result(factionId, DaggerfallGuildMembershipChange.None, DaggerfallGuildMembershipDenial.ReviewTooSoon,
                membership.Rank, currentDay, assessment);

        int targetRank = assessment.HighestQualifiedRank;
        if (targetRank < 0 && definition.NeverExpels) targetRank = 0;
        if (targetRank < 0)
        {
            _social.ExpelGuild(factionId);
            return Result(factionId, DaggerfallGuildMembershipChange.Expelled, DaggerfallGuildMembershipDenial.None,
                membership.Rank, currentDay, assessment);
        }

        if (targetRank == membership.Rank)
            return Result(factionId, DaggerfallGuildMembershipChange.None, DaggerfallGuildMembershipDenial.NoRankChange,
                membership.Rank, currentDay, assessment);

        if (targetRank > membership.Rank)
        {
            for (int rank = membership.Rank; rank < targetRank; rank++) _social.PromoteGuild(factionId, currentDay);
            return Result(factionId, DaggerfallGuildMembershipChange.Promoted, DaggerfallGuildMembershipDenial.None,
                membership.Rank, currentDay, assessment);
        }

        for (int rank = membership.Rank; rank > targetRank; rank--) _social.DemoteGuild(factionId, currentDay);
        return Result(factionId, DaggerfallGuildMembershipChange.Demoted, DaggerfallGuildMembershipDenial.None,
            membership.Rank, currentDay, assessment);
    }

    /// <summary>Removes membership immediately through the social owner.</summary>
    internal DaggerfallGuildMembershipResult Expel(int factionId, int currentDay)
    {
        DaggerfallGuildPolicyDefinition definition = RequireDefinition(factionId);
        _ = definition;
        DaggerfallGuildRankAssessment assessment = Assess(factionId);
        DaggerfallGuildEligibility eligibility = _social.GuildEligibility(factionId);
        if (!eligibility.IsMember)
            return Result(factionId, DaggerfallGuildMembershipChange.None, DaggerfallGuildMembershipDenial.NotMember, -1, currentDay, assessment);

        int previousRank = eligibility.Rank;
        _social.ExpelGuild(factionId);
        return Result(factionId, DaggerfallGuildMembershipChange.Expelled, DaggerfallGuildMembershipDenial.None,
            previousRank, currentDay, assessment);
    }

    /// <summary>Returns whether a current rank grants one concrete service or benefit identity.</summary>
    internal bool HasPrivilege(int factionId, string privilegeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privilegeId);
        DaggerfallGuildPolicyDefinition definition = RequireDefinition(factionId);
        DaggerfallGuildEligibility eligibility = _social.GuildEligibility(factionId);
        return eligibility.IsMember && definition.Privileges.Any(privilege =>
            StringComparer.Ordinal.Equals(privilege.Id, privilegeId) && privilege.MinimumRank <= eligibility.Rank);
    }

    private DaggerfallGuildMembershipResult AdmitCore(int factionId, int currentDay, DaggerfallGuildMembershipChange change)
    {
        DaggerfallGuildPolicyDefinition definition = RequireDefinition(factionId);
        DaggerfallGuildRankAssessment assessment = Assess(factionId);
        DaggerfallGuildEligibility eligibility = _social.GuildEligibility(factionId);
        if (eligibility.IsMember)
            return Result(factionId, DaggerfallGuildMembershipChange.None, DaggerfallGuildMembershipDenial.AlreadyMember,
                eligibility.Rank, currentDay, assessment);
        DaggerfallGuildRankRequirement admission = definition.RankRequirements[0];
        if (assessment.Reputation < admission.MinimumReputation)
            return Result(factionId, DaggerfallGuildMembershipChange.None, DaggerfallGuildMembershipDenial.InsufficientReputation,
                -1, currentDay, assessment);
        if (!assessment.MeetsAdmission)
            return Result(factionId, DaggerfallGuildMembershipChange.None, DaggerfallGuildMembershipDenial.InsufficientSkills,
                -1, currentDay, assessment);

        try
        {
            _social.JoinGuild(factionId, currentDay);
        }
        catch (InvalidOperationException)
        {
            // Social's keyed membership is the variant authority. A different faction in this
            // guild group is the only admitted join failure after the checks above.
            return Result(factionId, DaggerfallGuildMembershipChange.None, DaggerfallGuildMembershipDenial.VariantConflict,
                -1, currentDay, assessment);
        }

        return Result(factionId, change, DaggerfallGuildMembershipDenial.None, -1, currentDay, assessment);
    }

    private DaggerfallGuildMembershipResult Result(
        int factionId,
        DaggerfallGuildMembershipChange change,
        DaggerfallGuildMembershipDenial denial,
        int previousRank,
        int currentDay,
        DaggerfallGuildRankAssessment assessment) =>
        new(factionId, change, denial, previousRank, Read(factionId, currentDay), assessment);

    private DaggerfallGuildPolicyDefinition RequireDefinition(int factionId) =>
        _definitions.TryGetValue(factionId, out DaggerfallGuildPolicyDefinition? definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "No guild policy definition names this faction.");

    private DaggerfallGuildMembershipSave? FindMembership(int factionId) =>
        _social.Capture().Memberships.SingleOrDefault(membership => membership.FactionId == factionId);

    private (int High, int Low) CountSkills(IReadOnlyList<string> skills, DaggerfallGuildRankRequirement requirement)
    {
        int high = 0;
        int low = 0;
        foreach (string skill in skills)
        {
            int value = _permanentSkillValue(skill);
            if (value >= requirement.HighSkillMinimum) high++;
            else if (value >= requirement.LowSkillMinimum) low++;
        }
        return (high, low);
    }
}

/// <summary>Source-backed skill lists for the four standalone donor guilds.</summary>
internal static class DaggerfallGuildPolicyCatalog
{
    internal const int FightersFactionId = 41;
    internal const int MagesFactionId = 40;
    internal const int ThievesFactionId = 42;
    internal const int DarkBrotherhoodFactionId = 108;

    internal static IReadOnlyList<DaggerfallGuildPolicyDefinition> CoreGuilds { get; } =
        Array.AsReadOnly(new[]
        {
            new DaggerfallGuildPolicyDefinition(FightersFactionId,
                ["archery", "axe", "blunt-weapon", "giantish", "long-blade", "orcish", "short-blade"]),
            new DaggerfallGuildPolicyDefinition(MagesFactionId,
                ["alteration", "destruction", "illusion", "mysticism", "restoration", "thaumaturgy"]),
            new DaggerfallGuildPolicyDefinition(ThievesFactionId,
                ["backstabbing", "climbing", "lockpicking", "pickpocket", "short-blade", "stealth", "streetwise"],
                neverExpels: true),
            new DaggerfallGuildPolicyDefinition(DarkBrotherhoodFactionId,
                ["archery", "backstabbing", "climbing", "critical-strike", "daedric", "destruction", "short-blade", "stealth", "streetwise"],
                neverExpels: true),
        });
}
