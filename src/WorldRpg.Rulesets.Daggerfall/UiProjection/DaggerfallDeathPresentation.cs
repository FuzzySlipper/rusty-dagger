namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The choices and presentation meaning that remain after a Daggerfall player is defeated.
///
/// The product mode owns input admission and session replacement. This class owns the ruleset
/// meaning that the mode publishes: a one-shot death transition, the supported death effects, and
/// the three semantic outcomes the Host can carry out. It deliberately does not call Host,
/// persistence, or browser code; those owners remain responsible for lifecycle, save storage, and
/// DOM presentation respectively.
/// </summary>
internal sealed class DaggerfallDeathPresentation
{
    internal const string ScreenId = "screen.death";
    internal const string NewGameAction = "death-new-game";
    internal const string LoadGameAction = "death-load-game";
    internal const string QuitAction = "death-quit";

    private bool _active;
    private bool _loadAvailable;
    private ulong _revision;
    private DaggerfallDeathChoiceId? _selected;

    /// <summary>Current death-screen projection state.</summary>
    internal DaggerfallDeathView View => new(
        _active,
        ScreenId,
        _revision,
        "You have died.",
        ControlsSuppressed: _active,
        CameraEffect: _active ? DaggerfallDeathCameraEffect.Fall : DaggerfallDeathCameraEffect.None,
        FadeEffect: _active ? DaggerfallDeathFadeEffect.ToBlack : DaggerfallDeathFadeEffect.None,
        AudioCue: _active ? DaggerfallDeathAudioCue.PlayerDeath : DaggerfallDeathAudioCue.None,
        Choices: Choices(),
        Selected: _selected);

    /// <summary>
    /// Enters the death presentation once. Repeated defeat facts do not restart camera, fade, or
    /// audio meaning, which keeps a held input or a repeated lethal result from replaying the
    /// transition.
    /// </summary>
    internal bool Enter()
    {
        if (_active) return false;
        _active = true;
        _selected = null;
        _revision = checked(_revision + 1);
        return true;
    }

    /// <summary>Clears the transient presentation when the Host adopts a new or loaded session.</summary>
    internal void Clear()
    {
        if (!_active && _selected is null) return;
        _active = false;
        _selected = null;
        _revision = checked(_revision + 1);
    }

    /// <summary>
    /// Reopens the choices after a Host operation that stayed in the death mode, such as a
    /// missing or malformed save. A successful operation replaces the session instead; this
    /// method exists so a failed load does not leave every death choice disabled.
    /// </summary>
    internal void ClearSelection()
    {
        if (_selected is null) return;
        _selected = null;
        _revision = checked(_revision + 1);
    }

    /// <summary>Updates whether a real save exists for the load choice.</summary>
    internal void SetLoadAvailable(bool available)
    {
        if (_loadAvailable == available) return;
        _loadAvailable = available;
        if (_active) _revision = checked(_revision + 1);
    }

    /// <summary>
    /// Accepts one of the exact death-screen action names. The choice is latched, so a repeated
    /// held button cannot request two session replacements. The Host still owns whether the
    /// requested operation succeeds.
    /// </summary>
    internal bool TrySelect(string action, out DaggerfallDeathChoiceId choice)
    {
        ArgumentNullException.ThrowIfNull(action);
        DaggerfallDeathChoiceId? selected = action switch
        {
            NewGameAction => DaggerfallDeathChoiceId.NewGame,
            LoadGameAction => DaggerfallDeathChoiceId.LoadGame,
            QuitAction => DaggerfallDeathChoiceId.QuitToTitle,
            _ => null,
        };
        if (!_active || selected is null || _selected is not null || (selected == DaggerfallDeathChoiceId.LoadGame && !_loadAvailable))
        {
            choice = default;
            return false;
        }

        choice = selected.Value;
        _selected = choice;
        _revision = checked(_revision + 1);
        return true;
    }

    private IReadOnlyList<DaggerfallDeathChoice> Choices() =>
    [
        new(NewGameAction, DaggerfallDeathChoiceId.NewGame, "New game", _active && _selected is null),
        new(LoadGameAction, DaggerfallDeathChoiceId.LoadGame, "Load game", _active && _loadAvailable && _selected is null),
        new(QuitAction, DaggerfallDeathChoiceId.QuitToTitle, "Quit to title", _active && _selected is null),
    ];
}

/// <summary>Semantic outcomes offered by the ruleset's death presentation.</summary>
internal enum DaggerfallDeathChoiceId
{
    NewGame,
    LoadGame,
    QuitToTitle,
}

/// <summary>One user-visible choice and its semantic action identifier.</summary>
internal sealed record DaggerfallDeathChoice(string Action, DaggerfallDeathChoiceId Id, string Label, bool Available);

/// <summary>Projection data the HUD adapter can carry without owning death policy.</summary>
internal sealed record DaggerfallDeathView(
    bool Active,
    string Screen,
    ulong Revision,
    string Message,
    bool ControlsSuppressed,
    DaggerfallDeathCameraEffect CameraEffect,
    DaggerfallDeathFadeEffect FadeEffect,
    DaggerfallDeathAudioCue AudioCue,
    IReadOnlyList<DaggerfallDeathChoice> Choices,
    DaggerfallDeathChoiceId? Selected);

/// <summary>Supported camera meaning from the donor's falling death sequence.</summary>
internal enum DaggerfallDeathCameraEffect
{
    None,
    Fall,
}

/// <summary>Supported fade meaning from the donor's death sequence.</summary>
internal enum DaggerfallDeathFadeEffect
{
    None,
    ToBlack,
}

/// <summary>Typed cue identity for the death presentation; Engine audio resolves the actual clip.</summary>
internal enum DaggerfallDeathAudioCue
{
    None,
    PlayerDeath,
}
