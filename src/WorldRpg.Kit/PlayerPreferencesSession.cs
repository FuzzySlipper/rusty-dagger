namespace WorldRpg.Kit;

/// <summary>
/// Optional ruleset seam for player preferences owned durably by the Host. A ruleset interprets
/// its own current preference string and stages changes; the Host applies it before publication
/// and persists requested replacements through the Engine-owned current-state store.
/// </summary>
public interface IPlayerPreferencesSession : IGameSession
{
    /// <summary>Captures the ruleset's active player preferences for a settings projection.</summary>
    string CapturePlayerPreferences();

    /// <summary>Applies persisted preferences, or defaults when no current value is supplied.</summary>
    void ApplyPlayerPreferences(string? serialized);

    /// <summary>Takes one active preference value that the Host should persist.</summary>
    string? TakePlayerPreferencesSave();

    /// <summary>Reports the Host's durable-preference outcome for the ruleset to present.</summary>
    void ReportPlayerPreferencesOutcome(string message);
}
