namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallCastOutcome
{
    Ready, Released, Cancelled, Unready, UnknownSpell, UnsupportedEffect, SourceUnavailable,
    InsufficientMagicka, Silenced, InvalidTarget, Immune, Absorbed, Reflected, Resisted, ChanceFailed,
    Missed, Applied, NoMatch, Refreshed, Replaced, DeliveryCompleted, IncumbentRejected, AlreadyDelivered, TargetUnavailable,
}

internal sealed record DaggerfallCastEffectResult(int EffectIndex, long? TargetId, DaggerfallCastOutcome Outcome,
    int SavePercent = 100, string? Instance = null);

internal sealed record DaggerfallSpellAbsorptionResult(long TargetId, int AdmittedSpellPoints, double RestoredSpellPoints, ulong[] SourceItems);

/// <summary>The narrow completed-change signal cast, item, time, combat, and presentation owners consume.</summary>
internal enum DaggerfallEffectOutcomeKind
{
    Started,
    Refreshed,
    Replaced,
    Rejected,
    NoMatch,
    SourceUnavailable,
    TargetUnavailable,
    Cancelled,
    Cured,
    Expired,
}

internal sealed record DaggerfallEffectOutcome(
    DaggerfallEffectOutcomeKind Kind,
    string Instance,
    string EffectKey,
    long TargetId)
{
    internal DaggerfallEffectFeedback Feedback { get; init; }
    internal long? CasterId { get; init; }
    internal ulong? ItemId { get; init; }
    internal bool CompletedImmediately { get; init; }
}

internal enum DaggerfallEffectFeedback { None, MagicSparkle }

internal enum DaggerfallStarCaptureOutcome { Captured, Occupied, Ineligible, Unavailable }

internal enum DaggerfallWabbajackOutcome { Transformed, ProtectedQuestTarget, AlreadyTransformed, InvalidTarget, SourceUnavailable, UnavailableAppearance }
