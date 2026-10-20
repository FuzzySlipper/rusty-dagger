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
        PlayerTime.Select(settings.ActionDrivenTime);
        _input.ConfigureLook(_tuning.PlayerControl.LookSensitivity * settings.LookSensitivity, settings.InvertVertical);
        // Direct key observations and Engine-mapped directions share the chosen bindings.
        _input.Rebind(DaggerfallInput.Controls with
        {
            Forward = Key("move.forward"), Backward = Key("move.backward"),
            Left = Key("move.left"), Right = Key("move.right"),
        }, DaggerfallInput.Bindings);
        _input.ClearHeldInput();
        _locomotion.Rebind(settings);

        KeyboardControl Key(string action) => settings.KeysFor(action)
            .Select(key => Enum.TryParse(key, out KeyboardControl value) ? value : KeyboardControl.None)
            .FirstOrDefault(value => value != KeyboardControl.None);
    }

    private void ChangeControls(DaggerfallPlayerUiAction action)
    {
        try
        {
            DaggerfallControlSettings candidate = action.Kind == DaggerfallUiActionKind.ControlsReset
                ? new() : DaggerfallControlSettings.Parse(_controlSettings.Serialize());
            // Resetting the controls keeps the interface scale: it is a display choice, not a binding.
            if (action.Kind == DaggerfallUiActionKind.ControlsReset) candidate.SetUiScale(_controlSettings.UiScale);
            if (action.Kind == DaggerfallUiActionKind.ControlsLook) candidate.SetLook(action.Sensitivity!.Value, action.Invert!.Value);
            else if (action.Kind == DaggerfallUiActionKind.ControlsRebind) candidate.Rebind(action.Item!, [action.Key!], action.Confirm);
            else if (action.Kind == DaggerfallUiActionKind.ControlsUiScale) candidate.SetUiScale(action.Scale);
            ApplyControlSettings(candidate);
            _preferencesToSave = candidate.Serialize();
            _controlDiagnostic = "Control settings applied.";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            _controlDiagnostic = DaggerfallRejectionText.For(error);
        }
    }
    private void ApplyShortcut(string id)
    {
        if (id.StartsWith("activation.", StringComparison.Ordinal))
        {
            ApplyActivationMode(new("activation-mode", Mode: id["activation.".Length..]));
            return;
        }
        switch (id)
        {
            case "quick-save": _saveSlotRequest = new(WorldRpg.Kit.SaveSlotOperation.QuickSave); break;
            case "quick-load": _saveSlotRequest = new(WorldRpg.Kit.SaveSlotOperation.QuickLoad); break;
            case "spell": RequestPanel("spells"); break;
            case "rest": RequestPanel("rest"); break;
            case "transport": RequestPanel(DaggerfallPanel.Travel); break;
            case "logbook":
            case "notebook": RequestPanel(DaggerfallPanel.Journal); break;
            case "travel-map": ChangeTravelMap(new("travel-map", Open: true)); RequestPanel(DaggerfallPanel.Travel); break;
            case "use-magic-item": RequestPanel("magic-items"); break;
            case "abort-spell": ChangeSpell(new("spell-unready")); break;
            case "recast":
                if (_lastPlayerSpell is null) Presentation.SetOutcome("You have not cast a spell yet.");
                else if (!State.Inventory.Read().UniqueItems.Any(item => _definitions.SourceTemplateFor(_definitions.RequireItem(new(item.Definition.Value)))?.Index == 132))
                    Presentation.SetOutcome("You need a spellbook to recast a spell.");
                else ChangeSpell(new("spell-ready", Key: _lastPlayerSpell));
                break;
        }
    }

}
