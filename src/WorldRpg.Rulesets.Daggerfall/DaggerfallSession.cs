using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using Rusty.Engine;
using System.Numerics;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Loot;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Concrete Daggerfall composition of catalog policy, module state, and named Engine capabilities.</summary>
internal sealed partial class DaggerfallSession : IPlaytestGameSession, IPlaytestWorldInspectionSession, ISaveableGameSession, IModeAwareGameSession, IEntryScreenSession, IEntryScreenStartupSession, ICharacterCreationSession, ISaveRequestingGameSession, IPlayerPreferencesSession, IPlayerDefeatOutcomeSession,
    IDaggerfallSiteTransitionHost, IDaggerfallQuestWorldAdmission
{
    private readonly IRandomService _random;
    private readonly IEngineContext _engine;
    private readonly DaggerfallTuning _tuning;
    private PlayerInputSystem _input;
    private ProductMode _mode = ProductMode.Playing;
    private readonly SpatialMovementSystem _spatial;
    private readonly FirstPersonCameraSystem _camera;
    private readonly DaggerCombatRules _combat;
    // The player's weapon swing gesture, read from the look turns each admitted update commits and
    // consumed by the weapon attack it admits.
    private readonly DaggerfallSwingTracker _playerSwings;
    private readonly DaggerfallVitalityConsequences _vitality;
    // The poisons the session's actors carry, and the ordinal its draws advance on so a replay inside a
    // session draws the same values.
    private readonly DaggerfallPoisonRuntime _poisons;
    private long _poisonDraws;
    private readonly DaggerfallStaminaRecoveryModule _staminaRecovery;
    private readonly CombatResolution _combatResolution;
    private readonly DaggerfallLocomotionPolicy _locomotion;
    private readonly DaggerfallClimbingPolicy _climbing;
    private readonly DaggerfallLevitationPolicy _levitation = new();
    private readonly DaggerfallDungeonVisibility _dungeonVisibility;
    private readonly DaggerfallEnemyBehaviorModule _enemyBehavior;
    private readonly DaggerfallEnemyMagicModule _enemyMagic;
    private readonly DaggerfallCorpseLootModule _corpseLoot;
    private readonly DaggerfallGroundContainers _groundContainers;
    private readonly DaggerfallBookNotebook _notebook;
    private readonly DaggerfallEncounterRuntime _encounters;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly DaggerSessionPersistence _persistence;
    private readonly HashSet<ulong> _authoredEntityIds;
    /// <summary>
    /// Dynamic actors share one numeric base with generated unique items; durable kinds keep
    /// them distinct, the way a corpse container already shares its actor's number under a
    /// different kind.
    /// </summary>
    internal const ulong DynamicActorFirstIdentity = 1_000_000_000_000UL;
    internal const ulong GroundContainerFirstIdentity = 2_000_000_000_000UL;
    private readonly DurableIdentityAllocator _actorIdentities;
    private readonly DaggerfallHeldEnchantments _heldEnchantments;
    private readonly DaggerfallItemCastTriggers _itemCastTriggers = null!;
    private readonly DaggerfallActorRoster _roster;
    private readonly DaggerfallActorGrounding _grounding;
    private readonly DaggerfallDefinitions _definitions;
    private readonly FactBuffer<IProductFact> _facts = new();
    private readonly DaggerfallRewardReactions _rewards;
    private readonly DaggerfallOutcomePresentation _outcomes;
    private readonly DaggerfallHudProjection _hud;
    private readonly DaggerfallEquipmentMoves _equipmentMoves;
    private readonly DaggerfallItemConditionService _itemCondition;
    private readonly DaggerfallInventoryPresentation _inventoryUi;
    private readonly DaggerfallLootPresentation _lootUi;
    private readonly DaggerfallCharacterPresentation _characterUi;
    /// <summary>The one owner of the active site projection, catalog, deltas and exterior window.</summary>
    // Assigned once the roster and persistence it composes exist; readers before that are lazy.
    private readonly DaggerfallSiteLifecycle _sites = null!;
    private DaggerfallWorldProfileKey _activeProfileKey => _sites.ActiveProfile;
    private DaggerfallSiteAppearance _appearance => _sites.Projection.Appearance;
    private DaggerfallDoorRuntime _doors => _sites.Projection.Doors;

    private readonly World.DaggerfallWorldTime _time;
    private readonly World.DaggerfallSiteContext _site;

    /// <summary>
    /// Where the session is: the active site, the region it belongs to and the sites play has revealed.
    /// </summary>
    /// <remarks>
    /// This is the one owner of location context in the session, which is what lets a region consumer -
    /// a holiday, a price, a service - read the region the player is actually standing in rather than
    /// carrying a second copy of it.
    /// </remarks>
    internal World.DaggerfallSiteContext Site => _site;

    private ulong? _latestUpdateGeneration;
    private ulong? _latestSimulationStep;
    /// <summary>What is open over the world, which of it holds the world, and the pad's panel request.</summary>
    private readonly DaggerfallOpenInteractions _interactions;
    private bool _disposed;

    internal DaggerfallCinematicPresentation? Cinematics { get; }
    private readonly DaggerfallOpeningCinematics _openingCinematics;
    internal DaggerfallTransformationInfections Infections { get; }

    internal DaggerfallState State { get; }
    internal PresentationState Presentation { get; }
    /// <summary>The selected site's one authoritative RDB door owner for activation, spells, and dungeon actions.</summary>
    internal DaggerfallDoorRuntime Doors => _doors;

    /// <summary>Admits the full selected site catalog once composition has constructed this session.</summary>
    internal void AdmitSiteProfiles(DaggerfallSiteProfiles profiles) => _sites.AdmitProfiles(profiles);

    /// <summary>The session's site lifecycle: projection, catalog, deltas and exterior window.</summary>
    internal DaggerfallSiteLifecycle Sites => _sites;

    /// <summary>Relocates the existing player (or a live actor) to an admitted named anchor.</summary>
    internal bool TryRelocate(DaggerfallRelocationDestination destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _sites.TryRelocate(destination);
    }

    /// <summary>Attempts one real site transition; failed destination admission leaves the source projection live.</summary>
    internal bool TryTransitionTo(DaggerfallWorldProfileKey destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _sites.TryTransitionTo(destination);
    }

    void IDaggerfallSiteTransitionHost.RelocatePlayer(WorldPoint position, float yawRadians, float pitchRadians) =>
        ApplyRelocation(position, yawRadians, pitchRadians);

    void IDaggerfallSiteTransitionHost.RebuildActivation()
    {
        ReconcileNpcProjection();
        ReconcilePropertyContainers();
        InitializeActivation(_engine, _tuning.LootInteraction);
        SyncWeatherContext();
    }

    void IDaggerfallSiteTransitionHost.RebaseTransientWorld(Vector3 delta)
    {
        _combat.RebaseRangedFlight(delta);
        Casting.Rebase(delta);
        _encounters.RebasePending(_activeProfileKey.LogicalId, delta);
        _dialogue?.Rebase(delta);
        foreach (var entry in State.Actors.Store.Query<DaggerfallNpcBody>())
            entry.Value.Pose = new(DaggerfallExteriorSessionOrigin.Shift(entry.Value.Pose.Position, delta), 0);
        // Ship return pose is detached in the land profile; active-world origin moves do not own it.
    }

    void IDaggerfallSiteTransitionHost.EnteredSite()
    {
        Casting.ClearTransient();
        _enemyMagic.Clear();
        _enemyBehavior.ClearEnemyMagic();
        ChangeMusicSite();
        ReconcileLawSite();
    }

    void IDaggerfallSiteTransitionHost.SyncCivilianPositions() => SyncCivilianPositions();

    void IDaggerfallSiteTransitionHost.AdmitResidentCivilianAppearances(DaggerfallSiteProjection projection) =>
        AdmitResidentCivilianAppearances(projection);

    /// <summary>
    /// A swing the departing projection was still timing ends with it: retire and hand its
    /// impact back to the shared state here, inside the generation that admitted it, so the
    /// player is not left charged against an animation that no longer exists.
    /// </summary>
    void IDaggerfallSiteTransitionHost.RetireDepartingProjection(DaggerfallSiteProjection source)
    {
        source.DeactivateWaterTriggers(_latestSimulationStep ?? 0UL);
        RetireDepartingSwing(source);
        _combat.ClearRangedFlight();
        CancelDungeonTextOnUnload();
        // The destination is committed; restored item lifetimes settle before any presentation.
        ExpireConjuredItems();
    }

    /// <summary>
    /// Whether the player stands in sunlight. The donor reads daytime while not inside any structure,
    /// so a shop, home or guild counts as darkness by day exactly as a dungeon does. The donor also
    /// exempts prison; no product state reaches prison yet, so that input is false until one does.
    /// </summary>
    private bool InSunlight() => DaggerfallHeldEnchantments.InSunlight(
        _time.Calendar.IsDay,
        insideStructure: _activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior,
        inPrison: false);

    /// <summary>
    /// Whether the player stands in a holy place. The donor requires an interior whose admitted
    /// building is a temple or belongs to the Fighters Guild trainers faction.
    /// </summary>
    private bool InHolyPlace() => IsHolyPlace(_activeProfileKey.Kind,
        _activeProfileKey.Kind == DaggerfallWorldProfileKind.Interior
            ? CurrentInteriorBuilding() : null);

    private DaggerfallInteriorBuilding? CurrentInteriorBuilding()
    {
        DaggerfallInteriorBuilding? placement = _sites.Projection.Inputs.InteriorBuilding;
        if (placement is null) return null;
        DaggerfallSiteBuildingSource building = _site.RequireBuildingSource(_activeProfileKey.Site,
            new(placement.BlockX, placement.BlockY, placement.Building.Index));
        return placement with { BuildingType = building.Source.BuildingType, FactionId = building.Source.FactionId };
    }

    internal static bool IsHolyPlace(DaggerfallWorldProfileKind kind, DaggerfallInteriorBuilding? building) =>
        kind == DaggerfallWorldProfileKind.Interior
        && building is { BuildingType: 14 } or { FactionId: DaggerfallConcreteGuildCatalog.FightersTrainerFactionId };

    /// <summary>
    /// Retires the departing projection's unfinished swing. The notice it publishes belongs to the
    /// generation that admitted the swing, so it is applied with that generation rather than the
    /// destination's, and the caller's normal fact boundary publishes any consequence.
    /// </summary>
    private void RetireDepartingSwing(DaggerfallSiteProjection source)
    {
        source.Appearance.RetirePendingSwing();
        IReadOnlyList<AttackImpactNotice> impacts = source.Appearance.TakeAttackImpacts();
        if (impacts.Count == 0) return;
        if (_latestUpdateGeneration is not ulong generation) return;
        State.Kit.AttackExecution.ApplyImpacts(impacts, generation, _facts);
    }

    /// <summary>
    /// Clears retained input and open interaction state before installing a landing pose. The Kit
    /// starts the next Engine proposal with detached motion, so stale floor support, jump state,
    /// and velocity cannot carry across a teleport or admitted content replacement.
    /// </summary>
    private void ApplyRelocation(WorldPoint position, float yawRadians, float pitchRadians)
    {
        if (!float.IsFinite(yawRadians)) throw new ArgumentOutOfRangeException(nameof(yawRadians));
        if (!float.IsFinite(pitchRadians)) throw new ArgumentOutOfRangeException(nameof(pitchRadians));
        _input.Neutralize();
        _locomotion.Neutralize();
        _climbing.Detach();
        _lootUi.CloseAll();
        State.PlayerControl.YawRadians = yawRadians;
        State.PlayerControl.PitchRadians = pitchRadians;
        _spatial.Relocate(State.PlayerControl, position);
        _camera.Update(State.PlayerControl);
    }

    /// <summary>Every actor definition by durable identity: authored placements and spawned actors alike.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorDefinition> DefinitionsByActor => _roster.Definitions;

    /// <summary>Spawned actors by durable identity to the definition each was registered from.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorId> DynamicActors => _roster.Dynamic;

    /// <summary>Registers one actor from a published definition beyond the authored placements.</summary>
    internal long SpawnActor(string definitionId, ActorPose pose, int? level = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _roster.Spawn(definitionId, pose, level);
    }

    /// <summary>Retires one spawned actor through the roster's lifetime policy.</summary>
    internal void BanishActor(long durableId) => _roster.Banish(durableId);

    internal void RetireActor(long durableId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _roster.Retire(durableId);
    }

    public void PublishInitial()
    {
        // A client that attaches starts with its game menu closed. A menu left open by a page that
        // went away must not hold the world for a client that cannot see it.
        _interactions.SetMenuOpen(false);
        _hud.RequestArt();
        SyncWeatherContext();
        PublishPresentation();
    }

    /// <summary>Captures meaningful state using the current product schema.</summary>
    public RulesetSavePayload CaptureSave()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // A modal equipment action can be saved before another playing step. Capture the worn set
        // that actually owns the items, rather than a prior frame's held stat sources.
        Casting.ClearPending();
        DaggerfallMolagBalEffects.Reconcile(State.Effects, MolagBalEquipped);
        _heldEnchantments.Refresh();
        SyncCivilianPositions();
        using IDisposable detachedResidents = _sites.SuspendResidentExteriorLocationsForSave();
        return _persistence.Capture(_latestUpdateGeneration, _latestSimulationStep, _roster.Dynamic, _encounters,
            _sites.Deltas, _activeProfileKey, _sites.ReturnProfile, State.DungeonDiscoveries, State.DungeonActions,
            _sites.Projection.CaptureMotion(), _sites.CaptureExteriorResidency(), _sites.CaptureExteriorLocationResidency());
    }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        // An exception that escapes here faults the lifecycle; the Engine reports it with its full
        // text and stack, and a resume continues this same session, so nothing is torn down on the way
        // out. Session resources are released by Dispose.
        _appearance.BeginAdmittedUpdate();
        _enemyBehavior.BeginAdmittedUpdate();
        Update(update.Facts, update.Input);
        // Sprite playback consumes the Engine-bound outer update identity.  It
        // must not run for each private catch-up simulation step above, and it
        // must not run while the world is holding still.
        if (_mode == ProductMode.Playing
            && update.Facts.LifecycleState == ProductLifecycleState.Running
            && update.Facts.Mode == ProductUpdateMode.Realtime
            && update.Facts.AdmittedStepCount > 0)
        {
            _appearance.Advance(update.Facts);
            _appearance.AdvanceMobileFeedback(update.Facts, State.Actors, State.PlayerControl.Position, ShotBlockedByCover);
            // A held value is recomputed before anything this update can read it: impacts resolve, the
            // flight advances and presentation publishes against what the player is wearing now.
            _heldEnchantments.Refresh();
            ApplyAttackImpacts();
            UpdateRangedFlight(update.Facts);
            PublishPresentation();
            // The score follows the world this admitted update settled: a site change or a change of day
            // is real once the step applied it, and the director is told once per admitted update.
            AdvanceMusic();
        }
        bool ambientPlaying = _mode == ProductMode.Playing && Cinematics?.ActiveSource is null
            && _pendingDispel is null && _pendingIdentify is null && _pendingCreateItem is null
            && update.Facts.LifecycleState == ProductLifecycleState.Running
            && update.Facts.Mode == ProductUpdateMode.Realtime && update.Facts.AdmittedStepCount > 0
            && double.IsFinite(update.Facts.FixedDeltaSeconds) && update.Facts.FixedDeltaSeconds > 0;
        SyncWeatherContext(ambientPlaying ? update.Facts.FixedDeltaSeconds * update.Facts.AdmittedStepCount : 0, ambientPlaying);
        _appearance.CompleteAdmittedUpdate();
        return ProductUpdateResult.None;
    }

    internal void Update(ProductUpdateFacts facts, ReadOnlySpan<ProductInputEvent> input)
    {
        // Daggerfall is a realtime simulation. Demand and external turns have no
        // fixed delta, so this ruleset deliberately does not interpret them as steps.
        if (facts.LifecycleState != ProductLifecycleState.Running
            || facts.Mode != ProductUpdateMode.Realtime
            || facts.AdmittedStepCount == 0
            || !double.IsFinite(facts.FixedDeltaSeconds)
            || facts.FixedDeltaSeconds <= 0d
            || facts.FixedDeltaSeconds > float.MaxValue)
        {
            return;
        }

        float deltaSeconds = (float)facts.FixedDeltaSeconds;
        ProductUpdateState firstStep = new(deltaSeconds);
        // A mode that is not ordinary play interprets no gameplay input and advances no world
        // time. The modal's own semantic actions still apply, because a modal that cannot act is
        // not a modal; everything else - including the entry screen's own action, which the product
        // answers before this runs - is dropped and the presentation still publishes so the player
        // can see the mode they are in.
        Cinematics?.Poll();
        _openingCinematics.Poll();
        if (_mode == ProductMode.Playing) Infections.Poll(_openingCinematics.IsActive);
        bool playing = _mode == ProductMode.Playing && !LegalModalOpen && Cinematics?.ActiveSource is null && _pendingDispel is null && _pendingIdentify is null && _pendingCreateItem is null;
        bool modal = _mode == ProductMode.Modal || LegalModalOpen || _pendingDispel is not null || _pendingIdentify is not null || _pendingCreateItem is not null;
        DaggerfallUiPhases phase = _mode == ProductMode.Dead ? DaggerfallUiPhases.Dead
            : playing ? DaggerfallUiPhases.Playing
            : modal ? DaggerfallUiPhases.Modal
            : DaggerfallUiPhases.Held;
        DaggerfallUiInput ui = TakeUiInput(input);
        // The slice that opens an interaction admits no attack. Whichever order the Engine delivers
        // the keys in, swinging on the frame a container opens is an unintended attack.
        bool opensInteraction = playing && OpensInteraction(ui);
        bool elapsedSubmitted = false;
        for (int index = 0; index < input.Length; index++)
        {
            firstStep.Add(input[index]);
            if (ui.IsUiAction(index))
                AdmitUiAction(ui.ActionAt(index), phase, firstStep, opensInteraction, ref elapsedSubmitted);
        }

        if (elapsedSubmitted)
        {
            // Explicit elapsed time already passed through the one calendar and its consumers.
            // The same input slice must not also apply an ordinary realtime step or attack.
            DeliverFacts();
            PublishPresentation();
            return;
        }

        if (!playing)
        {
            // The mode's own line was set when the mode was entered; an outcome an action set during
            // this update stays on the line rather than being overwritten by it.
            // Reactions to modal-own actions (a loot take above) deliver here rather than waiting
            // for a playing step: the batch is stable and reentrant appends wait for the next one.
            DeliverFacts();
            PublishPresentation();
            return;
        }

        // The message line ages with the world it reports on, so it is advanced here rather than
        // while a mode holds the world still.
        Presentation.Advance(deltaSeconds * facts.AdmittedStepCount);

        // The worn set is recomputed before the round advances, so a payload that ticks with the clock
        // reads the body the player is wearing now rather than the one the previous update saw.
        State.HeldEnchantments.Refresh();
        _itemCastTriggers.Refresh();

        // A standing panel request ages on the same admitted world time as everything else.
        _interactions.AgePanelRequest(deltaSeconds * facts.AdmittedStepCount);

        // Ordering within this one admitted update is clock, calendar consumers (magic rounds among
        // them), simulation, then locomotion minutes. A normal game minute is one magic round; a
        // larger admitted interval uses the same lifecycle catch-up path as rest, travel, and prison,
        // so no second effect timer can drift from the saved calendar.
        DaggerfallCalendar calendarBefore = _time.Calendar;
        double remainderBefore = _time.RemainderSeconds;
        long MinuteAtStep(uint step) => DaggerfallWorldTime.MinuteAtAdmittedOffset(calendarBefore, remainderBefore,
            deltaSeconds * step * _time.GameSecondsPerRealSecond);
        _time.Advance(deltaSeconds * facts.AdmittedStepCount);
        AdvanceCalendar(calendarBefore, DaggerfallCalendarAdvanceKind.OrdinaryPlay, simulate: () =>
        {
            // One admitted update owns one input slice. Later catch-up steps derive
            // only committed held keyboard/mapped-direction intent; direct axes,
            // direct digital movement, pointer deltas, and semantic actions do not replay.
            // Simulation and reactions run per step; the final publication below (and the outer
            // update's, after animation impacts) happens once, not once per step.
            SimulateStep(firstStep, facts.Generation, facts.SimulationStep, MinuteAtStep(0), MinuteAtStep(1));
            DeliverFacts();
            for (uint step = 1; step < facts.AdmittedStepCount; step++)
            {
                SimulateStep(new ProductUpdateState(deltaSeconds), facts.Generation, checked(facts.SimulationStep + step),
                    MinuteAtStep(step), MinuteAtStep(checked(step + 1)));
                DeliverFacts();
            }
        });
    }

    /// <summary>
    /// The status rows owners outside this session publish: effects, escorts and quests put what
    /// they want shown here, and the projection carries it without knowing what it means.
    /// </summary>
    internal PresentationSlots Slots { get; } = new();

    /// <summary>The input system's held state, readable so the mode request and tests agree on it.</summary>
    internal ProductMode Mode => _mode;

    /// <summary>
    /// The mode this session asks the product for, derived from the one open-interaction owner. The
    /// Kit actor state owns whether the player is defeated.
    /// </summary>
    public ProductMode? PendingModeRequest => _interactions.ModeRequest(_mode, State.Actors.Player.IsDefeated);

    /// <summary>
    /// Whether the pending request closes the owned loot interaction. Ordinary play is only ever
    /// requested when an open container just closed — the getter above returns Playing exactly in
    /// that case — so a Playing request is a close by construction rather than a second opinion
    /// about the mode. Death, modal-open, and silence carry no close.
    /// </summary>
    public bool PendingModeRequestClosesModal => PendingModeRequest == ProductMode.Playing;

    private SaveSlotRequest? _saveSlotRequest;
    private IReadOnlyList<SaveSlotSummary> _saveSlots = [];
    private string? _saveSlotDiagnostic;

    /// <summary>Takes a structured named-slot action for the Host to perform.</summary>
    public SaveSlotRequest? TakeSaveSlotRequest()
    {
        SaveSlotRequest? request = _saveSlotRequest;
        _saveSlotRequest = null;
        return request;
    }

    /// <summary>Presents the product's save/load outcome after it honored a request.</summary>
    public void ReportSaveOutcome(string message)
    {
        Presentation.SetOutcome(message);
        ReopenDeathChoicesAfterSaveOutcome();
        PublishPresentation();
    }

    /// <summary>Stores Host-owned save metadata for the next ordinary HUD projection.</summary>
    public void ReportSaveSlots(IReadOnlyList<SaveSlotSummary> slots, string? diagnostic)
    {
        _saveSlots = slots?.ToArray() ?? throw new ArgumentNullException(nameof(slots));
        _saveSlotDiagnostic = diagnostic;
        SetDeathLoadAvailability(_saveSlots.Count > 0);
        PublishPresentation();
    }

    /// <summary>
    /// Applies the mode the product decided. A mode change is a focus change, so held movement is
    /// dropped: a key held when play paused, a modal opened or the player died must not keep moving
    /// the character, and a release this interpreter never sees would leave it held forever.
    /// </summary>
    public void ApplyProductMode(ProductMode mode)
    {
        if (mode == _mode) return;
        _mode = mode;
        _input.Neutralize();
        _locomotion.Neutralize();
        ApplyDeathPresentationMode(mode);
        Presentation.SetOutcome(_interactions.ModeMessage(_mode));
        PublishPresentation();
    }

    internal void Update(ProductUpdateState update)
    {
        _enemyBehavior.BeginAdmittedUpdate();
        SimulateStep(update, 0, 0);
        DeliverFacts();
        PublishPresentation();
        AdvanceMusic();
    }

    /// <summary>
    /// Resolves one rest/travel/time encounter at the player pose. The result remains durable but
    /// unmaterialized until the next admitted simulation step, which makes a save between the two
    /// operations restore the selected source result rather than draw again.
    /// </summary>
    internal DaggerfallEncounterResolution QueueEncounter(DaggerfallEncounterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorldPoint pose = State.PlayerControl.Position
            ?? throw new InvalidOperationException("Daggerfall cannot select an encounter without a player position.");
        ulong generation = _latestUpdateGeneration is > 0 ? _latestUpdateGeneration.Value : 1UL;
        return _encounters.Select(request, generation, _activeProfileKey.LogicalId, new ActorPose(pose, State.PlayerControl.YawRadians));
    }

    /// <summary>
    /// Draws one poison bound through the Engine's keyed service, so a replay inside a session draws the
    /// same values rather than whatever the process clock says.
    /// </summary>
    private int PoisonRoll(int minimum, int maximum) => checked((int)_random.DrawKeyed(new KeyedRngRequest(
        DaggerfallPoisonRandomKey.Seed,
        DaggerfallPoisonRandomKey.Scope,
        DaggerfallPoisonRandomKey.For(DaggerfallActorIdentity.PlayerEntityId, "admission", ++_poisonDraws, DaggerfallPoisonRandomKey.MinuteScope),
        minimum,
        maximum)).Value);

    public void Dispose()
    {
        if (_disposed) return;
        Casting.ClearTransient();
        _disposed = true;
        // DisposeAll walks backward: projection door entities must release before the actor store.
        Exception? failure = null;
        try { _sites.ReleaseExteriorWaterTriggers(); }
        catch (Exception exception) { failure = exception; }
        try { _sites.RetireResidentExteriorLocations(capture: false); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { DisposeAll([.. Cinematics is null ? Array.Empty<IDisposable>() : new IDisposable[] { Cinematics }, _hud, _camera, _spatial, State.Actors, _heldEnchantments, _sites.Projection, State.Effects, _sites.ActionTriggers, Infections, _weatherPresentation]); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        try { _sites.RetireExteriorAppearance(); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        // The score's loop and the clips it opened belong to this session, so they are retired before
        // the Engine context that produced them is asked for anything else.
        DisposeMusic(ref failure);
        // Site lighting may have retained an interior background after its resources were
        // released; clear that product-owned camera state only after the final projection dispose.
        try { _engine.CameraView.ClearSkyBackground(default); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        if (failure is not null) throw failure;
    }

    private static void DisposeAll(IReadOnlyList<IDisposable> values)
    {
        List<Exception>? failures = null;
        for (int index = values.Count - 1; index >= 0; index--)
        {
            try { values[index].Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private void AppendEffectDamage(DaggerfallEffectDamage effect)
    {
        AppendDamage(effect.Result, DaggerfallDamageCause.Effect, 0);
    }

    private void ReactToSpellAttack(long caster, long target, string bundle)
    {
        bool killed = State.Actors.TryGet(target, out var actor) && actor.IsDefeated;
        ObserveLegalAttack(caster, target, $"spell-attack:{bundle}:{caster}:{target}:{(killed ? "death" : "attack")}");
        ReactToAttack(caster, target);
    }

    private void ReactToAttack(long caster, long target)
    {
        DaggerfallConcealmentEffects.BreakNormal(State.Effects, caster);
        if (caster != DaggerfallActorIdentity.PlayerEntityId || target == caster) return;
        if (_enemyBehavior.IsPacified(target)) _enemyBehavior.MakeActiveEnemiesHostile();
        _enemyBehavior.MakeHostile(target);
    }

    private void AppendSpellTransfer(DaggerfallSpellTransferResult result)
    {
        if (result.HealthDamage is { } health) AppendEffectDamage(new(health));
        if (result.TrackDamage is { } fatigue) AppendSpellTrackLoss(fatigue);
        _facts.Append(new VitalTransferredFact(checked((long)result.Caster.Get<DurableEntityIdentity>().Identity.Value),
            checked((long)result.Target.Get<DurableEntityIdentity>().Identity.Value), result.Track.Value, result.Amount,
            result.ActualLoss, result.Restored, result.TargetDefeated, _latestUpdateGeneration ?? 1UL, _latestSimulationStep ?? 1UL));
    }

    private void AppendSpellTrackLoss(DaggerfallSpellTrackResult result)
    {
        long source = checked((long)result.Source.Get<DurableEntityIdentity>().Identity.Value);
        long target = checked((long)result.Target.Get<DurableEntityIdentity>().Identity.Value);
        ulong generation = _latestUpdateGeneration ?? 1UL, step = _latestSimulationStep ?? 1UL;
        _facts.Append(result.Track == TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)
            ? new FatigueAppliedFact(source, target, result.CalculatedLoss, result.ActualLoss, generation, step)
            : new SpellPointsAppliedFact(source, target, result.CalculatedLoss, result.ActualLoss, generation, step));
    }

    private void AppendDamage(DamageResult result, DaggerfallDamageCause cause, int struckBody)
    {
        long source = checked((long)result.Source.Get<DurableEntityIdentity>().Identity.Value);
        long target = checked((long)result.Target.Get<DurableEntityIdentity>().Identity.Value);
        ulong generation = _latestUpdateGeneration ?? 1UL;
        ulong step = _latestSimulationStep ?? 1UL;
        _facts.Append(new DamageAppliedFact(source, target, cause,
            result.CalculatedDamage, result.ActualHealthLost, struckBody, generation, step));
        if (result.ActualHealthLost > 0)
            _facts.Append(new ActorDamagedFact(target, source, cause,
                result.CalculatedDamage, result.ActualHealthLost) { TargetDefeated = result.Defeated });
        if (result.Defeated)
            _facts.Append(new ActorDiedFact(target, source, cause,
                result.CalculatedDamage, result.ActualHealthLost, generation, step));
    }

    private void React(IProductFact fact)
    {
        State.Quests.ObserveFoeFact(fact);
        ObservePlaytestCombatFact(fact);
        _staminaRecovery.React(fact);
        if (fact is AttackHitFact hit)
        {
            ObserveCrimeHit(hit);
            DaggerfallConcealmentEffects.AfterPhysicalHit(State.Effects, hit);
            if(hit.AttackerId==DaggerfallActorIdentity.PlayerEntityId && hit.TargetId!=hit.AttackerId)
                ReactToAttack(hit.AttackerId,hit.TargetId);
        }
        if (fact is ActorDiedFact died)
        {
            CaptureHeldSoul(died);
            if (died.ActorId==DaggerfallActorIdentity.PlayerEntityId) { _pendingIdentify=null; _pendingCreateItem=null; }
            DaggerfallMolagBalEffects.EndOnDeath(State.Effects, died.ActorId);
            DaggerfallItemSoulEffects.EndOnDeath(State.Effects, died.ActorId);
            DaggerfallParalysisEffects.EndOnDeath(State.Effects, died.ActorId);
            DaggerfallContinuousDestructionEffects.EndOnDeath(State.Effects, died.ActorId);
            DaggerfallConcealmentEffects.End(State.Effects, died.ActorId);
            foreach (var light in State.Effects.Active.Where(effect => effect.Definition.Key == DaggerfallIllusionEffects.LightKey
                && effect.Context.Target.Value == (ulong)died.ActorId).ToArray()) State.Effects.Cancel(light.Context.Instance);
            _corpseLoot.Create(died);
            _rewards.React(died, _facts);
        }
        if (fact is MagicEffectFact magic)
            _appearance.ReactEffectOutcome(magic.Outcome, State.Actors, State.PlayerControl.Position,
                _latestUpdateGeneration ?? 1UL, _latestSimulationStep ?? 1UL);
        if (fact is SpellCastFact cast)
            _appearance.ReactSpellCast(cast, State.Actors, State.PlayerControl.Position);
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        _appearance.React(fact, State.Actors);
        _outcomes.React(fact);
    }

    private void PublishPresentation()
    {
        _heldEnchantments.Refresh();
        RefreshPassiveMagery();
        RefreshLycanthropy();
        _appearance.RefreshEnemyVoices(State.Actors);
        DaggerfallConcealmentEffects.Publish(State.Effects, DaggerfallActorIdentity.PlayerEntityId, Slots);
        DaggerfallDoorMagicEffects.Publish(State.Effects, DaggerfallActorIdentity.PlayerEntityId, Slots);
        DaggerfallMagicPresentation.Publish(State.Effects, State.Actors.Player, Slots);
        _appearance.RetireUnavailableMagic(State.Actors, State.ItemInstances.ContainsUnique);
        _hud.Publish(new DaggerfallHudFrame(State.Actors.Player, State.Progression, Presentation, _mode, State.PlayerControl, Slots,
            Inventory: _inventoryUi.Read(),
            Property: ReadPropertyPresentation(),
            Loot: _lootUi.Read(),
            Character: _characterUi.Read(State.Actors.Player, State.Progression),
            CharacterCreationAvailable: !_newGameInitialized,
            PanelRequest: LatestPanelRequest,
            SaveSlots: _saveSlots,
            SaveSlotDiagnostic: _saveSlotDiagnostic,
            ControlSettings: _controlSettings,
            ControlDiagnostic: _controlDiagnostic,
            Activation: ActivationView,
            Quests: State.Quests.ReadPresentation(QuestTextContext),
            Notebook: _notebook.Read(),
            Transport: DaggerfallTransportProjection.Read(State.Transport, State.Inventory.Read(), TransportAccess(),
                ownsShip: State.Property.OwnsShip, wagon: State.Wagon),
            DungeonText: _dungeonTextProjection,
            Death: _deathPresentation.View,
            Rest: RestView,
            Lodging: LodgingView,
            Travel: ReadTravelPresentation(),
            SiteName: Site.ActiveSite?.Name,
            Map: _mapOpen ? ReadMapPresentation() : null, Legal: LegalView, CreateItem: CreateItemView, Teleport: TeleportView, Dispel: DispelView, Identify: IdentifyView, Spells: ReadSpells(), Detectors: ReadDetectors()));
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        Vector3? candlePosition = !State.Actors.Player.IsDefeated && State.PlayerControl.Position is { } playerPosition
            && State.Effects.Active.Any(effect => effect.Definition.Key == DaggerfallIllusionEffects.LightKey)
            ? playerPosition.ToVector() + new Vector3(MathF.Sin(State.PlayerControl.YawRadians) * _tuning.NormalLight.Distance,
                _spatial.CurrentController.Shape.StandingHeight * _tuning.NormalLight.HeightFraction,
                -MathF.Cos(State.PlayerControl.YawRadians) * _tuning.NormalLight.Distance) : null;
        _sites.Projection.Lighting.UpdateMagicCandle(candlePosition, _tuning.NormalLight);
        _appearance.UpdateMagicCandle(candlePosition);
        _appearance.UpdateDirections(State.Actors, _camera.Viewpoint);
        _appearance.Publish(State.Actors, _groundContainers.All.Where(pair => pair.Value.PropertyPlacement is null).ToDictionary(),
            _latestUpdateGeneration is ulong generation && _latestSimulationStep is ulong simulationStep
                ? _combat.ReadRangedFlights(generation, simulationStep) : [],
            _tuning.Camera.EyeHeight, State.Effects.PerceptionFor, ReadNpcViews(), ReadDungeonSpellFlights());
    }

    /// <summary>The panel the player asked for through a device the DOM has no channel of its own for.</summary>
    internal DaggerfallPanelRequest? LatestPanelRequest => _interactions.LatestPanelRequest;

    /// <summary>What is open over the world and which of it holds the world.</summary>
    internal DaggerfallOpenInteractions Interactions => _interactions;

    private void RequestPanel(string panel)
    {
        if (panel == DaggerfallPanel.Inventory && State.RacialOverrides.Current?.SuppressInventory == true)
        {
            Presentation.SetOutcome("You cannot use your inventory in beast form.");
            return;
        }
        _interactions.RequestPanel(panel);
    }

    /// <summary>Optional compiled ranged-flight owner; invoked once for every admitted realtime update.</summary>
    partial void UpdateRangedFlight(ProductUpdateFacts facts);

    internal TargetingEvidence? LastMeleeTargeting => State.Kit.Targeting.LastEvidence;

    private void DeliverFacts()
    {
        _facts.Deliver(React);
    }
    internal IReadOnlyDictionary<long, EnemyBehaviorEvidence> LastEnemyBehavior => _enemyBehavior.LastEvidence;
    internal IReadOnlyDictionary<long, DaggerfallEnemySpellEvidence> LastEnemySpell => _enemyMagic.LastEvidence;
    internal LootPresentation? OpenLoot => _lootUi.Read();

    /// <summary>Typed equipment moves over live state: the same operations the UI adapter uses.</summary>
    internal DaggerfallEquipmentMoves EquipmentMoves => _equipmentMoves;
    /// <summary>Typed mutation and disclosure of persisted item condition and magic meaning.</summary>
    internal DaggerfallItemConditionService ItemCondition => _itemCondition;
    internal CorpseLootEvidence? LastCorpseLoot => _corpseLoot.LastEvidence;
    internal CorpseLootCommitEvidence? LastCorpseLootCommit => _corpseLoot.LastCommit;
    internal IReadOnlyDictionary<long, CorpseContainer> Corpses => _corpseLoot.Corpses;

    /// <summary>The session's durable identity ledger for generated unique loot.</summary>
    internal DaggerfallUniqueItemAllocator UniqueItemAllocator => _uniqueItems;

    /// <summary>
    /// Records that one generated unique item no longer exists, so its identity is
    /// tombstoned. Authored content identities are refused — they are reserved rather
    /// than issued, and tombstoning one would misreport content as removed.
    /// </summary>
    internal void RemoveUniqueItemIdentity(ulong entityId)
    {
        if (_authoredEntityIds.Contains(entityId))
            throw new InvalidOperationException($"Unique item identity {entityId} is authored content and cannot be removed.");
        _uniqueItems.Remove(new DurableIdentityReference(DurableIdentityKind.Item, entityId));
    }
}
