using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The player's control settings in force: the keys the Engine maps and the input system observes, look, the pad's
/// rebinding, the sound levels, ground cover and whether time is action-driven. A change is applied whole and kept
/// for the Host to save as player preferences. Time's player clock and the horizon's ground cover are told, at
/// their places in a change, through <paramref name="selectPlayerTime"/> and <paramref name="useGroundCover"/>,
/// which the session binds: the first is a later domain's, and the horizon is composed after this owner.
/// </summary>
internal sealed class DaggerfallPlayerControls(IInputService mappings, IAudioService audio, PlayerInputSystem input,
    DaggerfallLocomotionPolicy locomotion, DaggerfallControlSettings initial, float lookSensitivity, ControllerInputTuning? pad,
    Action<bool> selectPlayerTime, Action<bool, float> useGroundCover)
{
    private string? _preferencesToSave;
    private IReadOnlyList<ControllerActionBinding>? _reboundPad;

    /// <summary>The settings in force.</summary>
    internal DaggerfallControlSettings Settings { get; private set; } = initial;

    /// <summary>What the last change or preferences save reported, for the controls screen.</summary>
    internal string Diagnostic { get; private set; } = "";

    internal bool ActionDrivenTime => Settings.ActionDrivenTime;

    /// <summary>The pad buttons in force, rebinding included, whose held state the attack and the guard read.</summary>
    internal IReadOnlyList<ControllerActionBinding> PadBindings => _reboundPad ?? PadActions;

    /// <summary>The authored pad layout the player's controller bindings start from.</summary>
    internal IReadOnlyList<ControllerActionBinding> PadActions => pad?.Actions ?? [];

    internal string Capture() => Settings.Serialize();

    /// <summary>The preferences a change left for the Host to save, once.</summary>
    internal string? TakePreferencesSave()
    {
        string? value = _preferencesToSave;
        _preferencesToSave = null;
        return value;
    }

    internal void Report(string message) => Diagnostic = message;

    internal void ApplyPreferences(string? serialized) =>
        Apply(serialized is null ? new() : DaggerfallControlSettings.Parse(serialized));

    internal void ChangePlayerTime(bool enabled)
    {
        Settings.SetActionDrivenTime(enabled);
        selectPlayerTime(enabled);
        _preferencesToSave = Settings.Serialize();
    }

    internal void Apply(DaggerfallControlSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InputMappingReplacementOutcome outcome = mappings.ReplacePhysicalMappings(settings.PhysicalMappings());
        if (outcome != InputMappingReplacementOutcome.Staged)
            throw new InvalidOperationException($"Control bindings were not applied: {outcome}.");
        Settings = settings;
        selectPlayerTime(settings.ActionDrivenTime);
        input.ConfigureLook(lookSensitivity * settings.LookSensitivity, settings.InvertVertical);
        // Direct key observations and Engine-mapped directions share the chosen bindings.
        input.Rebind(DaggerfallInput.Controls with
        {
            Forward = Key("move.forward"), Backward = Key("move.backward"),
            Left = Key("move.left"), Right = Key("move.right"),
        }, DaggerfallInput.Bindings);
        if (pad is { } authored) input.RebindControllerActions(_reboundPad = settings.ControllerActions(authored.Actions));
        foreach ((string bus, _) in DaggerfallControlSettings.SoundBuses)
            audio.SetBusVolume(new AudioBusVolumeRequest(SoundBus(bus), settings.Volume(bus)));
        useGroundCover(settings.GroundCover, settings.ClutterDistance);
        input.ClearHeldInput();
        locomotion.Rebind(settings);

        KeyboardControl Key(string action) => settings.KeysFor(action)
            .Select(key => Enum.TryParse(key, out KeyboardControl value) ? value : KeyboardControl.None)
            .FirstOrDefault(value => value != KeyboardControl.None);
    }

    /// <summary>Applies the settings in force with one edit the controls screen asked for, and keeps them to save.</summary>
    internal void Change(Action<DaggerfallControlSettings> edit) => Change(edit, reset: false);

    /// <summary>
    /// Returns the bindings to their defaults, keeping the interface scale, the ground cover and the sound levels:
    /// they are not bindings.
    /// </summary>
    internal void Reset() => Change(null, reset: true);

    private void Change(Action<DaggerfallControlSettings>? edit, bool reset)
    {
        try
        {
            DaggerfallControlSettings candidate = reset ? new() : DaggerfallControlSettings.Parse(Settings.Serialize());
            if (reset)
            {
                candidate.SetUiScale(Settings.UiScale);
                candidate.SetGroundCover(Settings.GroundCover);
                candidate.SetClutterDistance(Settings.ClutterDistance);
                foreach ((string bus, _) in DaggerfallControlSettings.SoundBuses) candidate.SetVolume(bus, Settings.Volume(bus));
            }
            edit?.Invoke(candidate);
            Apply(candidate);
            _preferencesToSave = candidate.Serialize();
            Diagnostic = "Control settings applied.";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            Diagnostic = DaggerfallRejectionText.For(error);
        }
    }

    private static AudioBus SoundBus(string bus) => bus switch
    {
        "music" => AudioBus.Music, "ambient" => AudioBus.Ambient, "effects" => AudioBus.Sfx, "interface" => AudioBus.Ui,
        _ => throw new ArgumentOutOfRangeException(nameof(bus), bus, "No sound bus carries this id."),
    };
}
