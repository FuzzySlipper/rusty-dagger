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
internal sealed partial class DaggerfallSession : IPlaytestGameSession, IPlaytestWorldInspectionSession, IDeveloperWorldSession, IDeveloperPlayerSession, IDeveloperInspectionSession, IDeveloperHeldSession, IDeveloperPeopleSession
{
    /// <summary>Developer movement: teleport, travel, entry, goto, where, stand, scout, find and spawn.</summary>
    private readonly DaggerfallDeveloperPlacement _developerPlacement = null!;

    /// <summary>Developer settings of the world around the player: time, weather, ground, horizon and art.</summary>
    private readonly DaggerfallDeveloperScene _developerScene = null!;

    /// <summary>Developer grants to the player: items, gold, skills, a level, vitals, spells, mounts and beast form.</summary>
    private readonly DaggerfallDeveloperGrants _developerGrants = null!;

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

    public DebugCommandResult Teleport(double x, double y, double z, bool force) => _developerPlacement.Teleport(x, y, z, force);
    public DebugCommandResult Move(double dx, double dy, double dz) => _developerPlacement.Move(dx, dy, dz);
    public DebugCommandResult Forward(double metres) => _developerPlacement.Forward(metres);
    public DebugCommandResult Travel(string place) => _developerPlacement.Travel(place);
    public DebugCommandResult Enter(string place, string building) => _developerPlacement.Enter(place, building);
    public DebugCommandResult GoTo(string target) => _developerPlacement.GoTo(target);
    public DebugCommandResult Where() => _developerPlacement.Where();
    public DebugCommandResult Spawn(string actor, double metres) => _developerPlacement.Spawn(actor, metres);
    public DebugCommandResult Find(string service) => _developerPlacement.Find(service);
    public DebugCommandResult Stand(double east, double south) => _developerPlacement.Stand(east, south);
    public DebugCommandResult Scout(string kind, double kilometres) => _developerPlacement.Scout(kind, kilometres);
    public DebugCommandResult Hour(int hour) => _developerScene.Hour(hour);
    public DebugCommandResult Season(string season) => _developerScene.Season(season);
    public DebugCommandResult Weather(string kind) => _developerScene.Weather(kind);
    public DebugCommandResult GroundWeather(double wet, double snow) => _developerScene.GroundWeather(wet, snow);
    public DebugCommandResult GroundBlend(double contrast, double repeatMetres, double triplanarSharpness) =>
        _developerScene.GroundBlend(contrast, repeatMetres, triplanarSharpness);
    public DebugCommandResult GroundSurvey() => _developerScene.GroundSurvey();
    public DebugCommandResult NearestRiver() => _developerScene.NearestRiver();
    public DebugCommandResult Horizon() => _developerScene.Horizon();
    public DebugCommandResult HorizonSet(double lift, double fogDensity) => _developerScene.HorizonSet(lift, fogDensity);
    public DebugCommandResult Flora(double density) => _developerScene.Flora(density);
    public DebugCommandResult GroundCover(string kinds) => _developerScene.GroundCover(kinds);
    public DebugCommandResult Trees(string kind) => _developerScene.Trees(kind);
    public DebugCommandResult FarForest(string state) => _developerScene.FarForest(state);
    public DebugCommandResult ClassicArt(string state) => _developerScene.ClassicArt(state);

    public DebugCommandResult Give(string item, int count) => _developerGrants.Give(item, count);
    public DebugCommandResult Hold(string item) => _developerGrants.Hold(item);
    public DebugCommandResult HoldMagic() => _developerGrants.HoldMagic();
    public DebugCommandResult Equip(string item) => _developerGrants.Equip(item);
    public DebugCommandResult Money(long amount) => _developerGrants.Money(amount);
    public DebugCommandResult Skill(string skill, int value) => _developerGrants.Skill(skill, value);
    public DebugCommandResult LevelUp() => _developerGrants.LevelUp();
    public DebugCommandResult Heal() => _developerGrants.Heal();
    public DebugCommandResult Spell(string spell) => _developerGrants.Spell(spell);
    public DebugCommandResult Ride(string mount) => _developerGrants.Ride(mount);
    public DebugCommandResult RideState() => _developerGrants.RideState();
    public DebugCommandResult Morph(string form) => _developerGrants.Morph(form);
    public DebugCommandResult Items(string filter) => _developerGrants.Items(filter);

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
