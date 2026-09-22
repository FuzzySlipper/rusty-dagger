using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private IEngineContext? _controlEngine;
    private DaggerfallControlSettings _controlSettings = new();
    private string? _preferencesToSave;
    private string _controlDiagnostic = "";

    public string CapturePlayerPreferences() => _controlSettings.Serialize();
    public string? TakePlayerPreferencesSave()
    {
        string? value = _preferencesToSave;
        _preferencesToSave = null;
        return value;
    }
    public void ReportPlayerPreferencesOutcome(string message)
    {
        _controlDiagnostic = message;
        PublishPresentation();
    }
    public void ApplyPlayerPreferences(string? serialized)
    {
        DaggerfallControlSettings candidate = serialized is null ? new() : DaggerfallControlSettings.Parse(serialized);
        ApplyControlSettings(candidate);
    }

    internal void ApplyControlSettings(DaggerfallControlSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InputMappingReplacementOutcome outcome = _controlEngine!.Input.ReplacePhysicalMappings(settings.PhysicalMappings());
        if (outcome != InputMappingReplacementOutcome.Staged)
            throw new InvalidOperationException($"Control bindings were not applied: {outcome}.");
        _controlSettings = settings;
        // Direct key observations and Engine-mapped directions share the chosen bindings.
        _input.Rebind(DaggerfallInput.Controls with
        {
            Forward = Key("move.forward"), Backward = Key("move.backward"),
            Left = Key("move.left"), Right = Key("move.right"),
        }, DaggerfallInput.Bindings);
        _input.ClearHeldInput();

        KeyboardControl Key(string action) => settings.KeysFor(action)
            .Select(key => Enum.TryParse(key, out KeyboardControl value) ? value : KeyboardControl.None)
            .FirstOrDefault(value => value != KeyboardControl.None);
    }

    private void ChangeControls(DaggerfallPlayerUiAction action)
    {
        try
        {
            DaggerfallControlSettings candidate = action.Action == "controls-reset"
                ? new() : DaggerfallControlSettings.Parse(_controlSettings.Serialize());
            if (action.Action != "controls-reset") candidate.Rebind(action.Item!, [action.Key!], action.Confirm);
            ApplyControlSettings(candidate);
            _preferencesToSave = candidate.Serialize();
            _controlDiagnostic = "Bindings applied.";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            _controlDiagnostic = error.Message;
        }
    }
}
