using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The developer first-person workbench (<see cref="IDeveloperHeldSession"/>) over the 3D first-person art, drawn by
/// the current site's held viewmodel (<see cref="DaggerfallHeldWorkbench"/>). Its looks are the classic weapon visuals
/// the art draws and its actions their classic strikes.
/// </summary>
internal sealed partial class DaggerfallSession : IDeveloperHeldSession
{
    private DaggerfallHeldWorkbench? _heldWorkbench;

    private DaggerfallHeldWorkbench Workbench => _heldWorkbench ??= new(_composition.HeldArt, _tuning.Viewmodel.Style, () => _appearance, () =>
    {
        if (!_inAdmittedUpdate && !_disposed) PublishPresentation();
    });

    public DebugCommandResult ShowHeld(string look, string action) => Workbench.Show(look, action);

    public DebugCommandResult ShowHeldIn(string hand) => Workbench.Hand(hand);

    public DebugCommandResult HoldHeldKey(int key) => Workbench.Key(key);

    public DebugCommandResult StepHeldKey(int step) => Workbench.Step(step);

    public DebugCommandResult PlayHeld(float speed) => Workbench.Play(speed);

    public DebugCommandResult ShowHeldAt(float seconds) => Workbench.At(seconds);

    public DebugCommandResult EndHeldView() => Workbench.Off();

    public TunablesPage HeldWorkbench() => Workbench.Page();

    public string? ChooseHeld(string chooser, string value) => Workbench.Choose(chooser, value);
}
