using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The Kit's developer interfaces the Host registers its live-debug modules by, each member a one-line call to the
/// Developer owner that answers it. The owners are built at the end of the session's composition.
/// </summary>
internal sealed partial class DaggerfallSession : IDeveloperInspectionSession, IDeveloperHeldSession, IDeveloperPeopleSession
{
    /// <summary>The debug overlay sets, built each presentation while on.</summary>
    private readonly DaggerfallDebugOverlays _overlays = null!;

    /// <summary>The first-person workbench over the 3D first-person art.</summary>
    private readonly DaggerfallHeldWorkbench _heldWorkbench = null!;

    /// <summary>The switch that draws walking people as the parts-rig test figure.</summary>
    private readonly DaggerfallPeopleRigSwitch _peopleRigs = null!;

    public IReadOnlyList<string> DebugOverlaySets => _overlays.Sets;
    public bool IsDebugOverlayOn(string name) => _overlays.IsOn(name);
    public bool SetDebugOverlay(string name, bool on) => _overlays.Set(name, on);
    public IReadOnlyList<InspectionTarget> InspectionTargets() => _overlays.InspectionTargets();

    public DebugCommandResult ShowHeld(string look, string action) => _heldWorkbench.Show(look, action);
    public DebugCommandResult HoldHeldKey(int key) => _heldWorkbench.Key(key);
    public DebugCommandResult StepHeldKey(int step) => _heldWorkbench.Step(step);
    public DebugCommandResult PlayHeld(float speed) => _heldWorkbench.Play(speed);
    public DebugCommandResult ShowHeldAt(float seconds) => _heldWorkbench.At(seconds);
    public DebugCommandResult ReadHeld() => _heldWorkbench.Read();
    public DebugCommandResult EndHeldView() => _heldWorkbench.Off();
    public DebugCommandResult TuneHeldShield(string part, float x, float y, float z, float turnX, float turnY, float turnZ) =>
        _heldWorkbench.Shield(part, new(x, y, z), new(turnX, turnY, turnZ));
    public DebugCommandResult ShowHeldArms(string set) => _heldWorkbench.Arms(set);
    public DebugCommandResult ShowHeldEnchanted(bool on) => _heldWorkbench.Enchant(on);
    public TunablesPage HeldWorkbench() => _heldWorkbench.Page();
    public string? ChooseHeld(string chooser, string value) => _heldWorkbench.Choose(chooser, value);

    public DebugCommandResult RigPeople(string set) => _peopleRigs.Use(set);
}
