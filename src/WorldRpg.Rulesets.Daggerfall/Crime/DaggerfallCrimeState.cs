using WorldRpg.Rulesets.Daggerfall.Guilds;

namespace WorldRpg.Rulesets.Daggerfall.Crime;

internal static class DaggerfallCrimeValidation
{
    internal static void ValidateCommon(string operationId, long perpetrator, long? affectedActorOrOwner, int region, long gameMinute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        if (perpetrator <= 0)
            throw new ArgumentOutOfRangeException(nameof(perpetrator));
        if (affectedActorOrOwner is <= 0)
            throw new ArgumentOutOfRangeException(nameof(affectedActorOrOwner));
        if (region < 0)
            throw new ArgumentOutOfRangeException(nameof(region));
        if (gameMinute < 0)
            throw new ArgumentOutOfRangeException(nameof(gameMinute));
    }
}

/// <summary>One eligible crime interaction after its caller admitted the real operation.</summary>
internal sealed record DaggerfallCrimeAttemptSave(
    string OperationId,
    DaggerfallCrimeAction Action,
    long PerpetratorActorId,
    long? AffectedActorOrOwnerId,
    int Region,
    long GameMinute,
    DaggerfallCrimeAttemptOutcome Outcome,
    DaggerfallCrimeWitnessEvidence Witnesses)
{
    internal DaggerfallCrimeAttemptSave Validate()
    {
        DaggerfallCrimeValidation.ValidateCommon(OperationId, PerpetratorActorId, AffectedActorOrOwnerId, Region, GameMinute);
        if (!Enum.IsDefined(Action) || !Enum.IsDefined(Outcome))
            throw new ArgumentOutOfRangeException(nameof(Action), "A saved crime attempt has an unknown action or outcome.");
        bool valid = Action switch
        {
            DaggerfallCrimeAction.Pickpocket => Outcome is DaggerfallCrimeAttemptOutcome.Failed
                or DaggerfallCrimeAttemptOutcome.SucceededWithoutTakingProperty
                or DaggerfallCrimeAttemptOutcome.PropertyTransferred,
            DaggerfallCrimeAction.Shoplifting or DaggerfallCrimeAction.Theft => Outcome is DaggerfallCrimeAttemptOutcome.Failed
                or DaggerfallCrimeAttemptOutcome.PropertyTransferred,
            DaggerfallCrimeAction.Assault => Outcome is DaggerfallCrimeAttemptOutcome.Failed
                or DaggerfallCrimeAttemptOutcome.DamageAccepted,
            DaggerfallCrimeAction.Entry => Outcome is DaggerfallCrimeAttemptOutcome.Failed
                or DaggerfallCrimeAttemptOutcome.EntryAccepted,
            _ => false,
        };
        if (!valid)
            throw new ArgumentException("A crime attempt outcome does not match its admitted action.", nameof(Outcome));
        DaggerfallCrimeWitnessEvidence witnesses = (Witnesses ?? throw new ArgumentNullException(nameof(Witnesses))).Validate();
        if (witnesses.WitnessActorIds.Contains(PerpetratorActorId))
            throw new ArgumentException("The perpetrator cannot be recorded as their own witness.", nameof(Witnesses));
        return this with { Witnesses = witnesses };
    }
}

/// <summary>A witness result from one completed Engine perception query. Empty is never inferred from an unrun query.</summary>
internal sealed record DaggerfallCrimeWitnessEvidence(DaggerfallCrimeWitnessQuery Query, long[] WitnessActorIds)
{
    internal static DaggerfallCrimeWitnessEvidence NotQueried { get; } = new(DaggerfallCrimeWitnessQuery.NotQueried, []);

    internal DaggerfallCrimeWitnessEvidence Validate()
    {
        ArgumentNullException.ThrowIfNull(WitnessActorIds);
        if (!Enum.IsDefined(Query))
            throw new ArgumentOutOfRangeException(nameof(Query));
        if (WitnessActorIds.Any(actorId => actorId <= 0)
            || WitnessActorIds.Distinct().Count() != WitnessActorIds.Length)
            throw new ArgumentException("Crime witnesses must be distinct positive durable actor identities.", nameof(WitnessActorIds));
        bool valid = Query switch
        {
            DaggerfallCrimeWitnessQuery.NotQueried => WitnessActorIds.Length == 0,
            DaggerfallCrimeWitnessQuery.CompletedWithoutWitnesses => WitnessActorIds.Length == 0,
            DaggerfallCrimeWitnessQuery.CompletedWithWitnesses => WitnessActorIds.Length > 0,
            _ => false,
        };
        if (!valid)
            throw new ArgumentException("Crime witness identities must match the completed query result.", nameof(WitnessActorIds));
        return this with { WitnessActorIds = (long[])WitnessActorIds.Clone() };
    }
}

/// <summary>One legal incident submitted only after its caller accepted the corresponding world outcome.</summary>
internal sealed record DaggerfallCrimeIncidentSave(
    string OperationId,
    DaggerfallCrimeKind Crime,
    DaggerfallCrimeStage Stage,
    long PerpetratorActorId,
    long? AffectedActorOrOwnerId,
    int Region,
    long GameMinute,
    DaggerfallCrimeTargetKind TargetKind,
    DaggerfallCrimeWitnessEvidence Witnesses,
    DaggerfallCrimeGuildCredit GuildCredit)
{
    internal DaggerfallCrimeIncidentSave Validate()
    {
        DaggerfallCrimeValidation.ValidateCommon(OperationId, PerpetratorActorId, AffectedActorOrOwnerId, Region, GameMinute);
        if (!Enum.IsDefined(Crime) || !Enum.IsDefined(Stage) || !Enum.IsDefined(TargetKind)
            || !Enum.IsDefined(GuildCredit))
            throw new ArgumentOutOfRangeException(nameof(Crime), "A saved crime incident has an unknown classification.");
        if (Crime == DaggerfallCrimeKind.Murder
            && (Stage != DaggerfallCrimeStage.Completed || TargetKind == DaggerfallCrimeTargetKind.Unknown))
            throw new ArgumentException("Murder incidents require an accepted death and a classified target.", nameof(Stage));
        if (GuildCredit is DaggerfallCrimeGuildCredit.CivilianMurder
            && (Crime != DaggerfallCrimeKind.Murder || Stage != DaggerfallCrimeStage.Completed || TargetKind != DaggerfallCrimeTargetKind.Civilian))
            throw new ArgumentException("Civilian murder guild credit requires an accepted civilian death.", nameof(GuildCredit));
        if (GuildCredit is DaggerfallCrimeGuildCredit.GuardMurder
            && (Crime != DaggerfallCrimeKind.Murder || Stage != DaggerfallCrimeStage.Completed || TargetKind != DaggerfallCrimeTargetKind.Guard))
            throw new ArgumentException("Guard murder guild credit requires an accepted guard death.", nameof(GuildCredit));
        if (GuildCredit == DaggerfallCrimeGuildCredit.Thieving
            && Crime is not (DaggerfallCrimeKind.Pickpocketing or DaggerfallCrimeKind.Theft or DaggerfallCrimeKind.BreakingAndEntering))
            throw new ArgumentException("Thieving guild credit requires a pickpocket, theft, or break-in incident.", nameof(GuildCredit));
        DaggerfallCrimeWitnessEvidence witnesses = (Witnesses ?? throw new ArgumentNullException(nameof(Witnesses))).Validate();
        if (witnesses.WitnessActorIds.Contains(PerpetratorActorId))
            throw new ArgumentException("The perpetrator cannot be recorded as their own witness.", nameof(Witnesses));
        return this with { Witnesses = witnesses };
    }
}

/// <summary>One exactly-once donor guild threshold contribution, kept separate from witness/legal response.</summary>
internal sealed record DaggerfallCrimeGuildProgressSave(
    string OperationId,
    DaggerfallCrimeGuildCredit Credit,
    long GameMinute)
{
    internal DaggerfallCrimeGuildProgressSave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OperationId);
        if (!Enum.IsDefined(Credit) || Credit == DaggerfallCrimeGuildCredit.None)
            throw new ArgumentOutOfRangeException(nameof(Credit));
        if (GameMinute < 0)
            throw new ArgumentOutOfRangeException(nameof(GameMinute));
        return this;
    }
}

/// <summary>Current crime facts and the donor's invitation progress; witnesses and response are not fabricated here.</summary>
internal sealed record DaggerfallCrimeSave(
    DaggerfallCrimeAttemptSave[] Attempts,
    DaggerfallCrimeIncidentSave[] Incidents,
    DaggerfallCrimeGuildProgressSave[] GuildProgress,
    int ThievingRequirementTally,
    int MurderRequirementTally,
    long ThievesInvitationDueMinute,
    long MurderInvitationDueMinute)
{
    internal DaggerfallCrimeSave Validate()
    {
        ArgumentNullException.ThrowIfNull(Attempts);
        ArgumentNullException.ThrowIfNull(Incidents);
        ArgumentNullException.ThrowIfNull(GuildProgress);
        HashSet<string> attemptIds = new(StringComparer.Ordinal);
        foreach (DaggerfallCrimeAttemptSave attempt in Attempts)
        {
            ArgumentNullException.ThrowIfNull(attempt);
            _ = attempt.Validate();
            if (!attemptIds.Add(attempt.OperationId))
                throw new ArgumentException("Saved crime attempts must have unique operation identities.", nameof(Attempts));
        }
        HashSet<string> incidentIds = new(StringComparer.Ordinal);
        foreach (DaggerfallCrimeIncidentSave incident in Incidents)
        {
            ArgumentNullException.ThrowIfNull(incident);
            _ = incident.Validate();
            if (!incidentIds.Add(incident.OperationId))
                throw new ArgumentException("Saved crime incidents must have unique operation identities.", nameof(Incidents));
        }
        HashSet<string> progressIds = new(StringComparer.Ordinal);
        foreach (DaggerfallCrimeGuildProgressSave progress in GuildProgress)
        {
            ArgumentNullException.ThrowIfNull(progress);
            _ = progress.Validate();
            if (!progressIds.Add(progress.OperationId))
                throw new ArgumentException("Saved crime guild progress must have unique operation identities.", nameof(GuildProgress));
        }
        ValidateGuildProgress(ThievingRequirementTally, ThievesInvitationDueMinute, 10, nameof(ThievingRequirementTally));
        ValidateGuildProgress(MurderRequirementTally, MurderInvitationDueMinute, 15, nameof(MurderRequirementTally));
        return this;
    }

    private static void ValidateGuildProgress(int tally, long dueMinute, int threshold, string name)
    {
        if (tally is < 0 or > 100)
            throw new ArgumentOutOfRangeException(name, "Crime invitation tally is outside its current-schema range.");
        if (dueMinute < 0)
            throw new ArgumentOutOfRangeException(nameof(dueMinute));
        if (tally == 100)
        {
            if (dueMinute != 0)
                throw new ArgumentException("Started crime invitations cannot retain a due time.", name);
        }
        else if ((tally >= threshold) != (dueMinute > 0))
        {
            throw new ArgumentException("Crime invitation deadlines must accompany a reached threshold only.", name);
        }
    }
}

/// <summary>
/// Daggerfall legal and guild incident state. It accepts identities and outcomes from real callers;
/// perception, inventory transfer, damage admission, and law-enforcement response remain their owners.
/// </summary>
internal sealed class DaggerfallCrimeState
{
    internal const long InvitationDelayMinutes = 4_320;
    internal const int ThievingInvitationThreshold = 10;
    internal const int MurderInvitationThreshold = 15;

    private readonly Dictionary<string, DaggerfallCrimeAttemptSave> _attempts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DaggerfallCrimeIncidentSave> _incidents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DaggerfallCrimeGuildProgressSave> _guildProgress = new(StringComparer.Ordinal);
    private int _thievingRequirementTally;
    private int _murderRequirementTally;
    private long _thievesInvitationDueMinute;
    private long _murderInvitationDueMinute;

    internal DaggerfallCrimeState(DaggerfallCrimeSave? restored = null)
    {
        if (restored is null)
            return;
        restored.Validate();
        foreach (DaggerfallCrimeAttemptSave attempt in restored.Attempts)
            _attempts.Add(attempt.OperationId, attempt.Validate());
        foreach (DaggerfallCrimeIncidentSave incident in restored.Incidents)
            _incidents.Add(incident.OperationId, incident.Validate());
        foreach (DaggerfallCrimeGuildProgressSave progress in restored.GuildProgress)
            _guildProgress.Add(progress.OperationId, progress.Validate());
        _thievingRequirementTally = restored.ThievingRequirementTally;
        _murderRequirementTally = restored.MurderRequirementTally;
        _thievesInvitationDueMinute = restored.ThievesInvitationDueMinute;
        _murderInvitationDueMinute = restored.MurderInvitationDueMinute;
    }

    internal IReadOnlyList<DaggerfallCrimeAttemptSave> Attempts => Array.AsReadOnly(_attempts.Values
        .OrderBy(attempt => attempt.GameMinute).ThenBy(attempt => attempt.OperationId, StringComparer.Ordinal).ToArray());
    internal IReadOnlyList<DaggerfallCrimeIncidentSave> Incidents => Array.AsReadOnly(_incidents.Values
        .OrderBy(incident => incident.GameMinute).ThenBy(incident => incident.OperationId, StringComparer.Ordinal).ToArray());
    internal int ThievingRequirementTally => _thievingRequirementTally;
    internal int MurderRequirementTally => _murderRequirementTally;
    internal long ThievesInvitationDueMinute => _thievesInvitationDueMinute;
    internal long MurderInvitationDueMinute => _murderInvitationDueMinute;

    /// <summary>Accepts one gameplay-admitted attempt. Rejected targets and UI retries must not call this.</summary>
    internal bool RecordAttempt(DaggerfallCrimeAttemptSave attempt)
    {
        DaggerfallCrimeAttemptSave value = (attempt ?? throw new ArgumentNullException(nameof(attempt))).Validate();
        if (_attempts.TryGetValue(value.OperationId, out DaggerfallCrimeAttemptSave? prior))
        {
            if (!SameAttempt(prior, value))
                throw new InvalidOperationException($"Crime operation '{value.OperationId}' was reused for a different attempt.");
            return false;
        }
        _attempts.Add(value.OperationId, value);
        return true;
    }

    /// <summary>
    /// Records one caller-confirmed offense result. Attempted, completed, and witnessed remain
    /// separate facts. The same operation cannot create another legal incident after a repeated receipt.
    /// </summary>
    internal bool RecordIncident(DaggerfallCrimeIncidentSave incident)
    {
        DaggerfallCrimeIncidentSave value = (incident ?? throw new ArgumentNullException(nameof(incident))).Validate();
        if (_incidents.TryGetValue(value.OperationId, out DaggerfallCrimeIncidentSave? prior))
        {
            if (!SameIncident(prior, value))
                throw new InvalidOperationException($"Crime operation '{value.OperationId}' was reused for a different incident.");
            return false;
        }
        if (_attempts.TryGetValue(value.OperationId, out DaggerfallCrimeAttemptSave? attempt)
            && (attempt.PerpetratorActorId != value.PerpetratorActorId
                || attempt.AffectedActorOrOwnerId != value.AffectedActorOrOwnerId
                || attempt.Region != value.Region
                || attempt.GameMinute != value.GameMinute))
            throw new InvalidOperationException("A crime incident must retain the admitted attempt's participants, region, and game time.");

        if (value.GuildCredit != DaggerfallCrimeGuildCredit.None)
            RecordGuildRequirementProgress(value.OperationId, value.GuildCredit, value.GameMinute);
        _incidents.Add(value.OperationId, value);
        return true;
    }

    /// <summary>
    /// Records the donor's threshold credit at the accepted gameplay boundary. Some source paths
    /// award this before the theft detection roll; their caller can credit the admitted attempt
    /// without claiming that property moved or that a witness saw it.
    /// </summary>
    internal bool RecordGuildRequirementProgress(
        string operationId,
        DaggerfallCrimeGuildCredit credit,
        long gameMinute)
    {
        DaggerfallCrimeGuildProgressSave progress = new DaggerfallCrimeGuildProgressSave(operationId, credit, gameMinute).Validate();
        if (_guildProgress.TryGetValue(progress.OperationId, out DaggerfallCrimeGuildProgressSave? prior))
        {
            if (prior != progress)
                throw new InvalidOperationException($"Crime operation '{progress.OperationId}' was reused for a different guild contribution.");
            return false;
        }

        int thievingTally = _thievingRequirementTally;
        int murderTally = _murderRequirementTally;
        long thievesDue = _thievesInvitationDueMinute;
        long murderDue = _murderInvitationDueMinute;
        switch (credit)
        {
            case DaggerfallCrimeGuildCredit.Thieving:
                (thievingTally, thievesDue) = ApplyProgress(thievingTally, thievesDue,
                    ThievingInvitationThreshold, gameMinute, DaggerfallCrimePolicy.GuildCreditPoints(credit));
                break;
            case DaggerfallCrimeGuildCredit.CivilianMurder:
            case DaggerfallCrimeGuildCredit.GuardMurder:
                (murderTally, murderDue) = ApplyProgress(murderTally, murderDue,
                    MurderInvitationThreshold, gameMinute, DaggerfallCrimePolicy.GuildCreditPoints(credit));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(credit));
        }
        _guildProgress.Add(progress.OperationId, progress);
        _thievingRequirementTally = thievingTally;
        _murderRequirementTally = murderTally;
        _thievesInvitationDueMinute = thievesDue;
        _murderInvitationDueMinute = murderDue;
        return true;
    }

    /// <summary>Whether an invitation's three-day delay has strictly elapsed while the player is outdoors.</summary>
    internal bool IsInvitationDue(DaggerfallCrimeGuildRequirement requirement, long currentMinute, bool playerInside)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentMinute);
        if (playerInside)
            return false;
        (int tally, long dueMinute) = Progress(requirement);
        return tally != 100 && dueMinute > 0 && dueMinute < currentMinute;
    }

    /// <summary>
    /// Marks the initiation quest as started after its real quest owner accepted it. This method
    /// does not start quests or synthesize letters.
    /// </summary>
    internal bool MarkInvitationStarted(
        DaggerfallCrimeGuildRequirement requirement,
        long currentMinute,
        bool playerInside)
    {
        if (!IsInvitationDue(requirement, currentMinute, playerInside))
            return false;
        switch (requirement)
        {
            case DaggerfallCrimeGuildRequirement.Thieving:
                _thievingRequirementTally = 100;
                _thievesInvitationDueMinute = 0;
                break;
            case DaggerfallCrimeGuildRequirement.Murder:
                _murderRequirementTally = 100;
                _murderInvitationDueMinute = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(requirement));
        }
        return true;
    }

    /// <summary>Projects only completed donor invitation requirements into Guild policy.</summary>
    internal DaggerfallGuildCrimeInvitationEvidence InvitationEvidence =>
        new(_thievingRequirementTally == 100, _murderRequirementTally == 100);

    internal DaggerfallCrimeSave Capture() => new DaggerfallCrimeSave(
        _attempts.Values.OrderBy(attempt => attempt.GameMinute).ThenBy(attempt => attempt.OperationId, StringComparer.Ordinal).ToArray(),
        _incidents.Values.OrderBy(incident => incident.GameMinute).ThenBy(incident => incident.OperationId, StringComparer.Ordinal).ToArray(),
        _guildProgress.Values.OrderBy(progress => progress.GameMinute).ThenBy(progress => progress.OperationId, StringComparer.Ordinal).ToArray(),
        _thievingRequirementTally,
        _murderRequirementTally,
        _thievesInvitationDueMinute,
        _murderInvitationDueMinute).Validate();

    private static (int Tally, long DueMinute) ApplyProgress(int tally, long dueMinute, int threshold, long gameMinute, int amount)
    {
        if (dueMinute != 0 || tally == 100)
            return (tally, dueMinute);
        int updated = checked(tally + amount);
        long updatedDue = updated >= threshold ? checked(gameMinute + InvitationDelayMinutes) : 0;
        return (updated, updatedDue);
    }

    private (int Tally, long DueMinute) Progress(DaggerfallCrimeGuildRequirement requirement) => requirement switch
    {
        DaggerfallCrimeGuildRequirement.Thieving => (_thievingRequirementTally, _thievesInvitationDueMinute),
        DaggerfallCrimeGuildRequirement.Murder => (_murderRequirementTally, _murderInvitationDueMinute),
        _ => throw new ArgumentOutOfRangeException(nameof(requirement)),
    };

    private static bool SameAttempt(DaggerfallCrimeAttemptSave left, DaggerfallCrimeAttemptSave right) =>
        left.OperationId == right.OperationId && left.Action == right.Action
        && left.PerpetratorActorId == right.PerpetratorActorId
        && left.AffectedActorOrOwnerId == right.AffectedActorOrOwnerId
        && left.Region == right.Region && left.GameMinute == right.GameMinute && left.Outcome == right.Outcome
        && SameWitnesses(left.Witnesses, right.Witnesses);

    private static bool SameIncident(DaggerfallCrimeIncidentSave left, DaggerfallCrimeIncidentSave right) =>
        left.OperationId == right.OperationId && left.Crime == right.Crime && left.Stage == right.Stage
        && left.PerpetratorActorId == right.PerpetratorActorId
        && left.AffectedActorOrOwnerId == right.AffectedActorOrOwnerId
        && left.Region == right.Region && left.GameMinute == right.GameMinute && left.TargetKind == right.TargetKind
        && left.GuildCredit == right.GuildCredit && SameWitnesses(left.Witnesses, right.Witnesses);

    private static bool SameWitnesses(DaggerfallCrimeWitnessEvidence left, DaggerfallCrimeWitnessEvidence right) =>
        left.Query == right.Query && left.WitnessActorIds.SequenceEqual(right.WitnessActorIds);
}
