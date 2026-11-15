using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Interaction;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The Kit's developer interfaces the Host registers its live-debug modules by, each member a one-line call to the
/// Developer owner that answers it. The owners are built at the end of the session's composition.
/// </summary>
internal sealed partial class DaggerfallSession : IPlaytestGameSession, IPlaytestWorldInspectionSession, IDeveloperInspectionSession, IDeveloperHeldSession, IDeveloperPeopleSession
{
    /// <summary>The playtest state writer: the gameplay facts, the last combat event and the last use.</summary>
    private readonly DaggerfallPlaytestState _playtest = null!;

    /// <summary>Read-only inspection of interaction and collision about the player.</summary>
    private readonly DaggerfallSpatialInspection _spatialInspection = null!;

    /// <summary>The debug overlay sets, built each presentation while on.</summary>
    private readonly DaggerfallDebugOverlays _overlays = null!;

    /// <summary>The first-person workbench over the 3D first-person art.</summary>
    private readonly DaggerfallHeldWorkbench _heldWorkbench = null!;

    /// <summary>The switch that draws walking people as the parts-rig test figure.</summary>
    private readonly DaggerfallPeopleRigSwitch _peopleRigs = null!;

    public IReadOnlyList<string> PlaytestActions => _playtest.Actions;
    public DebugCommandResult ReadPlaytestObservation() => _playtest.ReadObservation();
    public PlaytestAction InspectPlaytestAction(string id) => _playtest.InspectAction(id);
    public DebugCommandResult InspectPlaytestLook(double yawDegrees, double pitchDegrees) => _playtest.InspectLook(yawDegrees, pitchDegrees);
    public DebugCommandResult ReadPlaytestTargets() => _playtest.ReadTargets();

    public WorldInteraction CreateInteractionInspection() => _spatialInspection.CreateInteractionInspection();
    public DebugCommandResult ReadSpatialGrid(int radius, int verticalRadius, double cellSize) => _spatialInspection.ReadGrid(radius, verticalRadius, cellSize);
    public DebugCommandResult ReadSpatialProbe(double distance) => _spatialInspection.ReadProbe(distance);
    public DebugCommandResult ReadJumpPlan(double x, double y, double z) => _spatialInspection.ReadJumpPlan(x, y, z);
    public DebugCommandResult ReadSpatialClearance(double x, double y, double z) => _spatialInspection.ReadClearance(x, y, z);

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
