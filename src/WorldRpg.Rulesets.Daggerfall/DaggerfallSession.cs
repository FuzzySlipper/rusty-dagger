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
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Concrete Daggerfall composition of catalog policy, module state, and named Engine capabilities.</summary>
internal sealed class DaggerfallSession : ISaveableGameSession, IRestoringGameSession, IModeAwareGameSession
{
    private const ulong PlayerMechanicsEntityId = (ulong)DaggerfallActorIdentity.PlayerEntityId;

    /// <summary>Admitted world seconds a panel request stands before the DOM is assumed not to need it.</summary>
    private const double PanelRequestLifetimeSeconds = 1d;
    private readonly IRandomService _random;
    private readonly PlayerInputSystem _input;
    private ProductMode _mode = ProductMode.Playing;
    private readonly SpatialMovementSystem _spatial;
    private readonly FirstPersonCameraSystem _camera;
    private readonly CombatModule _combat;
    private readonly Dictionary<long, DaggerfallActorDefinition> _authoredDefinitions;
    private readonly DaggerfallStaminaRecoveryModule _staminaRecovery;
    private readonly DaggerfallEnemyBehaviorModule _enemyBehavior;
    private readonly DaggerfallCorpseLootModule _corpseLoot;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly HashSet<ulong> _authoredEntityIds;
    private PendingCorpseLoot? _pendingLoot;
    private readonly FactBuffer<IProductFact> _facts = new();
    private FactBuffer<IProductFact>.FactTransaction? _outerFacts;
    private readonly DaggerfallRewardReactions _rewards;
    private readonly DaggerfallOutcomePresentation _outcomes;
    private readonly DaggerfallHudProjection _hud;
    private readonly DaggerfallInventoryPresentation _inventoryUi;
    private readonly DaggerfallLootPresentation _lootUi;
    private readonly DaggerfallCharacterPresentation _characterUi;
    private readonly PrivateersHoldAppearance _appearance;
    private readonly IReadOnlyList<IDaggerfallSaveOwner> _saveOwners;

    private readonly List<SaveRestoreNotice> _restoreNotices;
    private IReadOnlyList<DaggerfallOwnerSave> _carriedOwnerSections;
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
    private string? _panelRequest;
    private ulong _panelRequestRevision;
    private double _panelRequestRemainingSeconds;
    private bool _disposed;

    internal DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, IReadOnlyList<IDaggerfallSaveOwner>? saveOwners = null)
        : this(engine, definitions, inputs, tuning, compositionIdentity: null, saved: null, restoreNotices: [], saveOwners)
    {
    }

    internal DaggerfallSession(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, IReadOnlyList<IDaggerfallSaveOwner>? saveOwners = null)
        : this(engine, definitions, inputs, tuning, compositionIdentity, saved: null, restoreNotices: [], saveOwners)
    {
    }

    /// <summary>
    /// Builds a restored session the way the ruleset does: the payload is read into the
    /// current shape, every reference is resolved against the selected content and the
    /// ledger the save carries, and what that had to report rides the session. There is
    /// deliberately no path that restores a payload nobody resolved.
    /// </summary>
    internal static DaggerfallSession Restore(
        IEngineContext engine,
        ResolvedCompositionIdentity compositionIdentity,
        DaggerfallDefinitions definitions,
        PrivateersHoldInputs inputs,
        DaggerfallTuning tuning,
        RulesetSavePayload saved,
        IRandomService random,
        IReadOnlyList<IDaggerfallSaveOwner>? saveOwners = null)
    {
        DaggerfallSaveRead read = DaggerfallSavePayload.Read(saved);
        DaggerfallRestorePlan plan = read.Payload.ResolveRestore(definitions, inputs, tuning, random);
        return new DaggerfallSession(engine, compositionIdentity, definitions, inputs, tuning, plan.Payload, [.. read.Notices, .. plan.Notices], saveOwners);
    }

    private DaggerfallSession(IEngineContext engine, ResolvedCompositionIdentity compositionIdentity, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, DaggerfallSavePayload? saved, IReadOnlyList<SaveRestoreNotice>? restoreNotices = null, IReadOnlyList<IDaggerfallSaveOwner>? saveOwners = null)
        : this(engine, definitions, inputs, tuning, compositionIdentity, saved, restoreNotices ?? [], saveOwners)
    {
    }

    private DaggerfallSession(IEngineContext engine, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, ResolvedCompositionIdentity? compositionIdentity, DaggerfallSavePayload? saved, IReadOnlyList<SaveRestoreNotice> restoreNotices, IReadOnlyList<IDaggerfallSaveOwner>? saveOwners)
    {
        ArgumentNullException.ThrowIfNull(restoreNotices);
        _saveOwners = saveOwners ?? [];
        HashSet<string> ownerIds = new(StringComparer.Ordinal);
        foreach (IDaggerfallSaveOwner owner in _saveOwners)
        {
            ArgumentNullException.ThrowIfNull(owner);
            if (string.IsNullOrWhiteSpace(owner.OwnerId) || !ownerIds.Add(owner.OwnerId))
            {
                throw new ArgumentException("Save owners must declare one non-empty, distinct owner id each.", nameof(saveOwners));
            }
        }

        // A section named for an owner this build has is restored by that owner, which
        // is what makes the seam a read path rather than a label. Anything else is kept
        // verbatim and reported, so a save written by a later owner is neither
        // discarded nor passed off as understood.
        List<DaggerfallOwnerSave> carried = [];
        List<SaveRestoreNotice> sectionNotices = [];
        foreach (DaggerfallOwnerSave section in (saved?.Owners ?? []).OrderBy(value => value.OwnerId, StringComparer.Ordinal))
        {
            IDaggerfallSaveOwner? owner = _saveOwners.FirstOrDefault(value => StringComparer.Ordinal.Equals(value.OwnerId, section.OwnerId));
            if (owner is null)
            {
                carried.Add(section);
                sectionNotices.Add(new SaveRestoreNotice("unread-owner-section",
                    $"Saved section for owner '{section.OwnerId}' has no owner in this build; it is preserved unchanged and not interpreted."));
                continue;
            }

            try
            {
                owner.Restore(section.Section);
            }
            catch (Exception failure) when (failure is not (OutOfMemoryException or StackOverflowException))
            {
                // An owner that cannot read its own section starts fresh rather than
                // costing the save: that is recoverable drift, and it is reported.
                sectionNotices.Add(new SaveRestoreNotice("owner-section-unreadable",
                    $"Saved section for owner '{section.OwnerId}' could not be read by that owner ({failure.Message}); the owner starts from its default state."));
            }
        }

        // The mirror case: an owner this build has, restoring a save that carries no
        // section for it. It continues from fresh state, which is a difference worth
        // reporting for the same reason an unread section is. A new game has no save to
        // be missing anything from, so this is reported only when restoring one.
        foreach (IDaggerfallSaveOwner owner in saved is null
            ? Enumerable.Empty<IDaggerfallSaveOwner>()
            : _saveOwners.OrderBy(value => value.OwnerId, StringComparer.Ordinal))
        {
            if ((saved!.Owners ?? []).Any(section => StringComparer.Ordinal.Equals(section.OwnerId, owner.OwnerId)))
            {
                continue;
            }

            sectionNotices.Add(new SaveRestoreNotice("owner-section-absent",
                $"The save carries no section for owner '{owner.OwnerId}', which this build has; that owner starts from its default state."));
        }

        _carriedOwnerSections = carried;
        _restoreNotices = [.. restoreNotices, .. sectionNotices];
        List<IDisposable> partiallyConstructed = [];
        try
        {
            _random = engine.Random;
            tuning = tuning.Validate();
            DaggerfallMechanicsState mechanics = new();
            DaggerfallActorDefinition playerDefinition = definitions.RequireActor(new DaggerfallActorId("player"));
            ValidateInitialEntityIds(inputs, playerDefinition.Loadout);
            Dictionary<InventoryItemId, ItemDefinition> itemDefinitions = definitions.Items.Values
                .ToDictionary(item => new InventoryItemId(item.Id.Value), ToManagedItem);
            Dictionary<KitEquipmentSlotId, EquipmentSlotDefinition> equipmentSlots = definitions.EquipmentSlots.Values
                .ToDictionary(slot => new KitEquipmentSlotId(slot.Id.Value), ToManagedSlot);
            EntityId playerEntity = new(PlayerMechanicsEntityId);
            InventoryStore inventoryWorld = new();
            inventoryWorld.RegisterInventory(new InventoryState(playerEntity));
            inventoryWorld.RegisterEquipment(new EquipmentState(playerEntity));
            MechanicsInventoryCoordinator inventory = new(inventoryWorld, playerEntity, itemDefinitions);
            MechanicsInventoryContainerCoordinator containers = new(inventoryWorld, itemDefinitions);
            MechanicsEquipmentCoordinator equipmentCoordinator = new(inventoryWorld, playerEntity, itemDefinitions, equipmentSlots);
            partiallyConstructed.Add(equipmentCoordinator);
            foreach (DaggerfallLoadoutEntry entry in playerDefinition.Loadout.Where(entry => saved is null && definitions.Items[entry.ItemId].IsFungible))
            {
                inventory.Grant(new InventoryGrant(
                    "daggerfall.initial-loadout",
                    $"daggerfall.loadout.{entry.ItemId.Value}",
                    new InventoryItemId(entry.ItemId.Value),
                    entry.Quantity));
            }
            foreach (DaggerfallLoadoutEntry entry in playerDefinition.Loadout.Where(entry => saved is null && !definitions.Items[entry.ItemId].IsFungible))
            {
                KitUniqueInventoryItem item = equipmentCoordinator.Materialize(new UniqueItemMaterialization(
                    $"daggerfall.loadout.{entry.UniqueEntityId!.Value}",
                    entry.UniqueEntityId.Value,
                    new InventoryItemId(entry.ItemId.Value)));
                if (entry.EquipSlot is DaggerfallEquipmentSlotId slot)
                {
                    equipmentCoordinator.Equip(
                        item,
                        [new KitEquipmentSlotId(slot.Value)],
                        new EquipmentChange(
                            "daggerfall.initial-equipment",
                            $"daggerfall.equipment.{entry.UniqueEntityId.Value}"));
                }
            }
            PlayerActorState player = new(mechanics.CreateActor(playerDefinition, playerDefinition.PlayerInitialVitals, PlayerMechanicsEntityId), playerDefinition.Combat.Health.Value);
            partiallyConstructed.Add(player);
            List<ActorState> actorStates = [];
            Dictionary<long, DaggerfallActorDefinition> authored = [];
            Dictionary<long, MechanicsInventoryCoordinator> actorInventories = [];
            foreach (AuthoredActor source in inputs.Project.Actors.Values)
            {
                if (!definitions.Actors.TryGetValue(source.ActorId, out DaggerfallActorDefinition? definition))
                    throw new InvalidOperationException($"Privateer's Hold placement '{source.EntityId}' refers to missing actor '{source.ActorId.Value}'.");
                ActorState actor = new(
                    source.EntityId,
                    mechanics.CreateActor(definition, InitialVitals(definition, source.EntityId), checked((ulong)source.EntityId)),
                    source.Position,
                    definition.Combat.Health.Value);
                partiallyConstructed.Add(actor);
                actorStates.Add(actor);
                authored.Add(source.EntityId, definition);
                // A placed actor whose definition declares a loadout carries it in a managed
                // inventory over the session's one world: today that is the ranged actors'
                // quiver, which a shot draws from and the save persists. A unique loadout entry
                // would need an equipped placement, which no placed actor has yet, so one is
                // refused rather than half-granted.
                if (definition.Loadout.Count > 0)
                {
                    if (definitions.Items.Where(item => definition.Loadout.Any(entry => entry.ItemId == item.Key)).Any(item => item.Value.IsFungible is false))
                        throw new InvalidOperationException($"Placed actor '{source.ActorId.Value}' loadout carries a unique item, which placed actors do not equip yet.");
                    EntityId actorEntity = new(checked((ulong)source.EntityId));
                    inventoryWorld.RegisterInventory(new InventoryState(actorEntity));
                    MechanicsInventoryCoordinator actorInventory = new(inventoryWorld, actorEntity, itemDefinitions);
                    actorInventories.Add(source.EntityId, actorInventory);
                    if (saved is null)
                    {
                        foreach (DaggerfallLoadoutEntry entry in definition.Loadout.Where(entry => definitions.Items[entry.ItemId].IsFungible))
                        {
                            actorInventory.Grant(new InventoryGrant(
                                "daggerfall.initial-loadout",
                                $"daggerfall.loadout.{source.EntityId}.{entry.ItemId.Value}",
                                new InventoryItemId(entry.ItemId.Value),
                                entry.Quantity));
                        }
                    }
                }
            }
            ActorsState actors = new(player, actorStates);
            State = new DaggerfallState(new PlayerControlState(inputs.Project.PlayerPosition, inputs.InitialLook.YawRadians, inputs.InitialLook.PitchRadians), actors, new ProgressionState(), inventory, equipmentCoordinator, containers, actorInventories);
            Presentation = new PresentationState("Ready");
            _time = new DaggerfallWorldTime(
                saved?.Calendar is { } restored
                    ? new DaggerfallCalendar(restored.Year, restored.Month, restored.Day, restored.Hour, restored.Minute, restored.Second)
                    : DaggerfallCalendar.Start,
                saved?.Calendar?.RemainderSeconds ?? 0d,
                tuning.Time.GameSecondsPerRealSecond);
            // A save that carries a site section is where the player actually is, including when that is
            // nowhere. A save with no section at all predates site persistence, so it starts at the site
            // its own bundle starts at rather than at a location invented for it.
            _site = saved?.Site is { } restoredSite
                ? new World.DaggerfallSiteContext(
                    definitions.Locations,
                    ToSiteId(restoredSite.Active),
                    ToSiteId(restoredSite.ReturnAnchor),
                    restoredSite.Discovered.Select(id => id.Require()))
                : new World.DaggerfallSiteContext(definitions.Locations, inputs.Site, null, []);
            _input = new PlayerInputSystem(tuning.PlayerControl, DaggerfallInput.Controls, DaggerfallInput.Bindings, tuning.ControllerInput);
            _spatial = new SpatialMovementSystem(engine.Spatial, engine.Content, inputs.SpatialArtifact, tuning.Spatial);
            partiallyConstructed.Add(_spatial);
            if (saved is null)
            {
                // RDB marker heights are probe origins, not floor contacts. Unlike
                // DFU's centered capsule, our navigation pose is the sprite's base.
                foreach (ActorState actor in actors.All.Values.Where(actor => authored[actor.EntityId].GroundOnSpawn))
                {
                    SpatialHit floor = engine.Spatial.CastRay(new SpatialRaycastRequest(
                        _spatial.Session,
                        actor.Position.ToVector() + Vector3.UnitY * tuning.EnemyBehavior.SpawnGroundProbeLift,
                        -Vector3.UnitY,
                        tuning.EnemyBehavior.SpawnGroundProbeDistance,
                        new SpatialQueryFilter(uint.MaxValue, uint.MaxValue), default, default, default));
                    if (floor.Present && !floor.StartSolid && floor.Normal.Y > 0f)
                        actor.ApplyPose(new ActorPose(WorldPoint.From(floor.Point), actor.HeadingYawRadians));
                }
            }
            _camera = new FirstPersonCameraSystem(engine.CameraView, State.PlayerControl, tuning.Camera);
            partiallyConstructed.Add(_camera);
            authored.Add(checked((long)PlayerMechanicsEntityId), playerDefinition);
            _authoredDefinitions = authored;
            DaggerfallMeleeTargetingModule targeting = new(engine.Perception, _spatial, State.Actors, authored, tuning.MeleeTargeting);
            _combat = new CombatModule(_random, State.Actors, State.Equipment, State.ActorInventories, definitions, authored, targeting);
            _staminaRecovery = new DaggerfallStaminaRecoveryModule(tuning.StaminaRecovery);
            _enemyBehavior = new DaggerfallEnemyBehaviorModule(
                engine.Perception,
                _spatial,
                new ActorNavigationCoordinator(engine.Spatial, _spatial.Session),
                State.Actors,
                _combat,
                tuning.EnemyBehavior);
            _rewards = new DaggerfallRewardReactions(
                State.Progression,
                State.Actors.Player.Mechanics,
                playerDefinition,
                _random,
                authored);
            _authoredEntityIds = DaggerfallSavePayload.ContentEntityIds(inputs, playerDefinition.Loadout);
            _uniqueItems = saved is null
                ? new DaggerfallUniqueItemAllocator(DaggerfallUniqueItemAllocator.DefaultFirstEntityId, _authoredEntityIds)
                : DaggerfallUniqueItemAllocator.Restore(saved.RestoredIdentities());
            _corpseLoot = new DaggerfallCorpseLootModule(
                engine.Perception,
                _spatial,
                containers,
                playerEntity,
                State.Actors,
                authored,
                definitions,
                _random,
                _uniqueItems,
                State.Progression,
                tuning.LootInteraction);
            _outcomes = new DaggerfallOutcomePresentation(Presentation, authored, () => _combat.LastMeleeTargeting);
            _inventoryUi = new DaggerfallInventoryPresentation(inventory, equipmentCoordinator, definitions, inputs.ClassicPresentation.InventoryIcons);
            _lootUi = new DaggerfallLootPresentation(_corpseLoot, _inventoryUi);
            _characterUi = new DaggerfallCharacterPresentation(definitions, playerDefinition, equipmentCoordinator);
            // The DOM's art comes from admitted content by media identity, so a session reads the
            // published closure once and publishes it to the UI that draws it.
            _hud = new DaggerfallHudProjection(
                engine.Ui,
                definitions.HudResources,
                compositionIdentity,
                DaggerfallUiArt.Read(engine.Content, inputs.ClassicPresentation.InventoryIcons.Values));
            partiallyConstructed.Add(_hud);
            _appearance = new PrivateersHoldAppearance(engine.Content, engine.Graphics, inputs, engine.Audio, tuning.PresentationAudio, _random);
            partiallyConstructed.Add(_appearance);
            if (saved is not null) ApplySave(saved, playerDefinition);
        }
        catch (Exception constructionFailure)
        {
            List<Exception> failures = [constructionFailure];
            try { DisposeAll(partiallyConstructed); }
            catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            if (failures.Count == 1) throw;
            throw new AggregateException(failures);
        }
    }

    internal DaggerfallState State { get; }
    internal PresentationState Presentation { get; }
    public void PublishInitial() => PublishPresentation();

    /// <summary>
    /// What the restore of this session had to report: a migrated schema, a reference
    /// the content could not explain, or a section no current owner reads. None of
    /// these refused the save, and none of them is silent.
    /// </summary>
    public IReadOnlyList<SaveRestoreNotice> RestoreNotices => _restoreNotices;

    public RulesetSavePayload CaptureSave()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallSession));
        if (_outerFacts is not null || _pendingLoot is not null)
            throw new InvalidOperationException("Daggerfall state can only be captured at a quiescent admitted-update boundary.");
        PlayerControlState control = State.PlayerControl;
        WorldPoint playerPosition = control.Position
            ?? throw new InvalidOperationException("Daggerfall cannot save without a player position.");
        DaggerfallPlayerSave player = new(
            playerPosition.X, playerPosition.Y, playerPosition.Z,
            control.YawRadians, control.PitchRadians,
            ReadTrack(State.Actors.Player.Mechanics, DaggerfallMechanicsIds.Health),
            ReadTrack(State.Actors.Player.Mechanics, DaggerfallMechanicsIds.Stamina),
            ReadTrack(State.Actors.Player.Mechanics, DaggerfallMechanicsIds.Magicka));
        DaggerfallActorSave[] actors = State.Actors.All.Values
            .OrderBy(actor => actor.EntityId)
            .Select(actor => new DaggerfallActorSave(
                actor.EntityId,
                actor.Position.X, actor.Position.Y, actor.Position.Z, actor.HeadingYawRadians,
                ReadTrack(actor.Mechanics, DaggerfallMechanicsIds.Health),
                ReadTrack(actor.Mechanics, DaggerfallMechanicsIds.Stamina),
                ReadTrack(actor.Mechanics, DaggerfallMechanicsIds.Magicka)))
            .ToArray();
        InventoryView inventory = State.Inventory.Read();
        EquipmentRead equipped = State.Equipment.Read();
        DaggerfallInventorySave inventorySave = new(
            inventory.Stacks.OrderBy(stack => stack.Definition.Value, StringComparer.Ordinal)
                .Select(stack => new DaggerfallStackSave(stack.Definition.Value, stack.Quantity)).ToArray(),
            inventory.UniqueItems.OrderBy(item => item.Entity.Value)
                .Select(item => new DaggerfallUniqueSave(item.Definition.Value, item.Entity.Value)).ToArray(),
            equipped.Assignments.OrderBy(assignment => assignment.Slot.Value, StringComparer.Ordinal)
                .Select(assignment => new DaggerfallEquipmentSave(assignment.Slot.Value, assignment.Item.EntityId)).ToArray());
        DaggerfallCorpseSave[] corpses = _corpseLoot.Corpses.Values.OrderBy(corpse => corpse.ActorId).Select(corpse =>
        {
            InventoryView? contents = corpse.IsRegistered ? State.Containers.Read(corpse.Owner) : null;
            return new DaggerfallCorpseSave(
                corpse.ActorId,
                corpse.OriginatingSequence,
                corpse.IsRegistered,
                corpse.IsInteractable,
                contents?.Stacks.OrderBy(stack => stack.Definition.Value, StringComparer.Ordinal)
                    .Select(stack => new DaggerfallStackSave(stack.Definition.Value, stack.Quantity)).ToArray() ?? [],
                contents?.UniqueItems.OrderBy(item => item.Entity.Value)
                    .Select(item => new DaggerfallUniqueSave(item.Definition.Value, item.Entity.Value)).ToArray() ?? []);
        }).ToArray();
        DaggerfallContinuationSave? continuation = _spatial.HasContinuation
            ? new DaggerfallContinuationSave(_spatial.CaptureContinuation())
            : null;
        // Every placed actor's managed inventory persists: today that is the ranged actors'
        // quiver, and an uncounted quiver would silently refill on restore.
        DaggerfallActorInventorySave[] actorInventories = State.ActorInventories
            .OrderBy(entry => entry.Key)
            .Select(entry => new DaggerfallActorInventorySave(
                entry.Key,
                entry.Value.Read().Stacks
                    .OrderBy(stack => stack.Definition.Value, StringComparer.Ordinal)
                    .Select(stack => new DaggerfallStackSave(stack.Definition.Value, stack.Quantity)).ToArray()))
            .ToArray();
        return DaggerfallSavePayload.Encode(new DaggerfallSavePayload(
            DaggerfallSavePayload.CurrentSchemaVersion,
            player,
            actors,
            State.Progression.Experience,
            State.Progression.Level,
            inventorySave,
            corpses,
            _uniqueItems.CaptureState(),
            _combat.CaptureCooldowns(_latestUpdateGeneration, _latestSimulationStep)
                .Select(value => new DaggerfallCombatCooldownSave(value.AttackerId, value.RemainingSteps)).ToArray(),
            continuation,
            [.. _saveOwners.Select(owner => new DaggerfallOwnerSave(
                    owner.OwnerId,
                    owner.Capture() is { Length: > 0 } captured
                        ? captured
                        : throw new InvalidOperationException($"Save owner '{owner.OwnerId}' captured no section; a durable owner must write the state it owns.")))
                .Concat(_carriedOwnerSections)
                .OrderBy(section => section.OwnerId, StringComparer.Ordinal)],
            new DaggerfallCalendarSave(_time.Calendar.Year, _time.Calendar.Month, _time.Calendar.Day, _time.Calendar.Hour, _time.Calendar.Minute, _time.Calendar.Second, _time.RemainderSeconds),
            _site.Capture(),
            actorInventories));
    }

    private static DaggerfallSiteId? ToSiteId(DaggerfallSiteIdSave? id) => id?.Require();

    public ProductUpdateResult Update(ProductUpdate update)
    {
        _appearance.BeginAdmittedUpdate();
        PrivateersHoldAppearance.PresentationCheckpoint mediaCheckpoint = _appearance.Checkpoint();
        DaggerfallStaminaRecoveryModule.Checkpoint staminaCheckpoint = _staminaRecovery.Capture();
        _outerFacts = _facts.BeginTransaction();
        PendingCorpseLoot? committedBoundaryLoot;
        try
        {
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
            ApplyAttackImpacts();
            PublishPresentation();
        }
        _appearance.CompleteAdmittedUpdate();
        FactBuffer<IProductFact>.FactTransaction transaction = _outerFacts;
        _outerFacts = null;
        transaction.Commit();
        committedBoundaryLoot = _pendingLoot;
        _pendingLoot = null;
        }
        catch (Exception failure)
        {
            _staminaRecovery.Restore(staminaCheckpoint);
            _pendingLoot = null;
            FactBuffer<IProductFact>.FactTransaction? transaction = _outerFacts;
            _outerFacts = null;
            List<Exception> failures = [failure];
            try { transaction?.Rollback(); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            try { _appearance.Restore(mediaCheckpoint); }
            catch (Exception restoreFailure) { failures.Add(restoreFailure); }
            if (failures.Count == 1) throw;
            throw new AggregateException(failures);
        }

        if (committedBoundaryLoot is { } pending)
        {
            CorpseLootCommitResult result = _corpseLoot.TryCommitLoot(pending, _facts);
            _lootUi.Complete(result);
            Presentation.SetOutcome(_lootUi.Message);
        }
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
        bool playing = _mode == ProductMode.Playing;
        bool modal = _mode == ProductMode.Modal;
        // The slice that opens an interaction admits no attack. A key pressed in the same admitted
        // update as the interaction key is a coincidence of timing rather than an instruction, and
        // whichever order the Engine delivers them in, swinging on the frame a container opens is
        // the "unintended attack" this task exists to prevent.
        bool opensInteraction = playing && ContainsInteractionAction(input);
        foreach (ProductInputEvent inputEvent in input)
        {
            firstStep.Add(inputEvent);
            if (inputEvent.ValueKind != InputValueKind.ProductPayload
                || !inputEvent.PayloadContract.Span.SequenceEqual("dagger.ui.action.v1"u8)) continue;
            DaggerfallPlayerUiAction? action = DaggerfallUiAction.Parse(inputEvent.PayloadData.Span);
            switch (action?.Action)
            {
                // The entry screen's own action is the product's to answer, so the session knows the
                // shape and does nothing with it; the product has already left the mode by the time an
                // action in ordinary play could arrive.
                case "begin": break;
                case "attack": if (playing && !opensInteraction) firstStep.Request(DaggerfallInput.Attack); break;
                // A reloaded DOM holds no art and asks for the revision it is missing; the projection
                // answers on its next snapshot rather than a second delivery channel existing.
                case "art-request": _hud.RequestArt(); break;
                case "inventory": break;
                case "inventory-move": if (playing || modal) _inventoryUi.Move(action!); break;
                case "character": break;
                case "loot": if (playing) firstStep.Request(DaggerfallInput.Interact); break;
                case "loot-close": if (playing || modal) _lootUi.Close(action!.Container); break;
                case "loot-take":
                    if (playing || modal)
                    {
                        _pendingLoot ??= _lootUi.PrepareTake(action!, State.PlayerControl, _input.ResolveCurrentLook(State.PlayerControl));
                    }
                    break;
                default: Presentation.SetOutcome("Unrecognized player UI action."); break;
            }
        }

        if (!playing)
        {
            Presentation.SetOutcome(ModalMessage());
            PublishPresentation();
            return;
        }

        // The message line ages with the world it reports on, so it is advanced here rather than
        // while a mode holds the world still.
        Presentation.Advance(deltaSeconds * facts.AdmittedStepCount);

        // The world's clock runs on the same admitted duration the message line ages by, scaled by the
        // tuning the corpus authors, so there is one clock and it is this one.
        _time.Advance(deltaSeconds * facts.AdmittedStepCount);
        AgePanelRequest(deltaSeconds * facts.AdmittedStepCount);

        // One admitted update owns one input slice. Later catch-up steps derive
        // only committed held keyboard/mapped-direction intent; direct axes,
        // direct digital movement, pointer deltas, and semantic actions do not replay.
        Update(firstStep, facts.Generation, facts.SimulationStep);
        for (uint step = 1; step < facts.AdmittedStepCount; step++)
            Update(new ProductUpdateState(deltaSeconds), facts.Generation, checked(facts.SimulationStep + step));
    }

    /// <summary>
    /// The status rows owners outside this session publish: effects, escorts and quests put what
    /// they want shown here, and the projection carries it without knowing what it means.
    /// </summary>
    internal PresentationSlots Slots { get; } = new();

    /// <summary>Whether this admitted slice asks to open the loot interaction.</summary>
    private static bool ContainsInteractionAction(ReadOnlySpan<ProductInputEvent> input)
    {
        foreach (ProductInputEvent inputEvent in input)
        {
            if (inputEvent.ValueKind != InputValueKind.ProductPayload
                || !inputEvent.PayloadContract.Span.SequenceEqual("dagger.ui.action.v1"u8)) continue;
            if (DaggerfallUiAction.Parse(inputEvent.PayloadData.Span)?.Action == "loot") return true;
        }

        return false;
    }

    /// <summary>The input system's held state, readable so the mode request and tests agree on it.</summary>
    internal ProductMode Mode => _mode;

    /// <summary>
    /// The mode this session asks the product for. Death outranks everything and only a session
    /// replacement leaves it; a modal exists exactly while a loot container is open, so the
    /// request follows that container rather than the key that opened it.
    /// </summary>
    public ProductMode? PendingModeRequest
    {
        get
        {
            // The Kit actor state already owns the question of whether the player is defeated.
            if (State.Actors.Player.IsDefeated) return ProductMode.Dead;
            if (_mode == ProductMode.Dead) return null;
            // The menu pair is what this asks about: a mode the product holds on its own, such as the
            // entry screen, has no loot container to follow, so this asks for nothing rather than
            // dragging the world into ordinary play behind the product's back.
            if (_mode is not (ProductMode.Playing or ProductMode.Modal)) return null;
            bool open = _lootUi.Read() is not null;
            return open == (_mode == ProductMode.Modal) ? null : open ? ProductMode.Modal : ProductMode.Playing;
        }
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
        Presentation.SetOutcome(ModalMessage());
        PublishPresentation();
    }

    /// <summary>What the outcome line says while a mode other than ordinary play holds the world.</summary>
    private string ModalMessage() => _mode switch
    {
        ProductMode.Modal => _lootUi.Read() is not null ? _lootUi.Message : "Interaction open.",
        ProductMode.Dead => "You have died.",
        ProductMode.Paused => "Paused.",
        // The entry screen says its own thing; a status line would compete with the screen that is up.
        ProductMode.Title => string.Empty,
        _ => string.Empty,
    };

    internal void Update(ProductUpdateState update) => Update(update, 0, 0);

    private void Update(ProductUpdateState update, ulong generation, ulong simulationStep)
    {
        _latestUpdateGeneration = generation;
        _latestSimulationStep = simulationStep;
        _combat.ObserveTimeline(generation, simulationStep);
        PreparedPlayerInput input = _input.Prepare(State.PlayerControl, update);
        _input.EnsureCommittable(input, State.PlayerControl);
        // Daggerfall presently has no authored dynamic support or obstacle facts.
        // The generic Kit environment remains call-local and is resubmitted per proposal.
        PreparedSpatialStep? step = _spatial.Prepare(State.PlayerControl, update, input, CharacterStepEnvironment.Empty);
        if (step is { } prepared)
        {
            CharacterStepReceipt receipt = _spatial.Propose(prepared);
            _input.Commit(input, State.PlayerControl, update);
            State.PlayerControl.Apply(receipt);
        }
        else _input.Commit(input, State.PlayerControl, update);
        _camera.Update(State.PlayerControl);
        _enemyBehavior.Update(State.PlayerControl, generation, simulationStep, update.DeltaSeconds, _facts);
        LookReceipt currentLook = _input.ResolveCurrentLook(State.PlayerControl);
        _staminaRecovery.Update(State.Actors.Player.Mechanics, update.DeltaSeconds);
        if (update.IsRequested(DaggerfallInput.ToggleWeapon)) _appearance.ToggleWeaponDrawn();
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        if (update.IsRequested(DaggerfallInput.Attack) && _appearance.CanStartPlayerAttack) _combat.TryPlayerMelee(State.PlayerControl, currentLook, generation, simulationStep, update.DeltaSeconds, _facts);
        if (update.IsRequested(DaggerfallInput.Interact))
        {
            _pendingLoot ??= _lootUi.Open(State.PlayerControl, currentLook);
            Presentation.SetOutcome(_lootUi.Message);
        }
        // A panel button asks the DOM for a panel during ordinary play, which is where the keyboard's
        // own I, C and Escape are heard. While a modal or a death holds the world the DOM already has
        // a panel in front of the player, so a request there would fight the mode rather than serve it.
        // Two panel buttons in one admitted slice ask in a fixed order and the last one stands, which
        // is what the DOM's own key handling does with two keys in one frame: one panel can open, so
        // the earlier press must not swallow the later one.
        if (update.IsRequested(DaggerfallInput.Inventory)) RequestPanel(DaggerfallPanel.Inventory);
        if (update.IsRequested(DaggerfallInput.Character)) RequestPanel(DaggerfallPanel.Character);
        if (update.IsRequested(DaggerfallInput.Menu)) RequestPanel(DaggerfallPanel.Menu);
        DeliverFacts();
        PublishPresentation();
    }

    private void ApplySave(DaggerfallSavePayload saved, DaggerfallActorDefinition playerDefinition)
    {
        saved.Validate();
        // Every saved actor was resolved against the selected content before this
        // session existed, so this applies what the content explains rather than
        // requiring the saved set to equal the authored set. An authored placement the
        // save does not mention keeps its authored pose and vitals.
        DaggerfallActorSave[] actors = saved.Actors.OrderBy(actor => actor.EntityId).ToArray();
        foreach (DaggerfallActorSave actor in actors)
        {
            if (!State.Actors.TryGet(actor.EntityId, out ActorState? current))
                throw new ArgumentException($"The saved Daggerfall actor '{actor.EntityId}' is not present in the selected content.", nameof(saved));
            current.ApplyPose(new ActorPose(new WorldPoint(actor.X, actor.Y, actor.Z), actor.HeadingRadians));
        }

        // Recreate deterministic health-max sources before restoring mutable
        // current values, so level-up semantics never collapse into a bare max.
        _rewards.RestoreProgression(saved.Experience, saved.Level);
        ApplyTracks(State.Actors.Player.Mechanics, saved.Player.Health, saved.Player.Stamina, saved.Player.Magicka, "player");
        foreach (DaggerfallActorSave actor in actors)
            ApplyTracks(State.Actors.All[actor.EntityId].Mechanics, actor.Health, actor.Stamina, actor.Magicka, $"actor {actor.EntityId}");

        ApplyInventory(saved.Inventory);
        ApplyActorInventories(saved.ActorInventories);

        WorldPoint position = new(saved.Player.X, saved.Player.Y, saved.Player.Z);
        State.PlayerControl.YawRadians = saved.Player.YawRadians;
        State.PlayerControl.PitchRadians = saved.Player.PitchRadians;
        if (saved.Continuation is { } continuation)
        {
            CharacterContinuationRestoreReceipt receipt = _spatial.RestoreContinuation(continuation.Checkpoint);
            State.PlayerControl.Restore(position, receipt.Motion);
        }
        else State.PlayerControl.Restore(position, default);
        // Corpse interaction policy is restored only after the canonical
        // spatial session has accepted its continuation.
        _corpseLoot.Restore(saved.Corpses);
        _combat.RestoreCooldowns(saved.CombatCooldowns.Select(value => new CombatCooldown(value.AttackerId, value.RemainingSteps)));
        _camera.Update(State.PlayerControl);
        // Enemy behavior, perception leases, held input, pending loot, facts,
        // presentation effects and a swing still waiting for its damage frame are
        // intentionally transient.  The next admitted step observes rebuilt actor
        // state without replaying them; a swing cut off by a save keeps the cooldown
        // it already charged but never lands.
    }

    private static long ReadTrack(ActorMechanicsState mechanics, DaggerfallTrackId track) =>
        mechanics.ReadTrack(TrackId.Parse(track.Value)).Current.Raw;

    /// <summary>
    /// Writes the saved tracks, reduced to the bounds this session actually resolves.
    /// Resolution already reports a saved value the selected content disagrees with, but
    /// the Engine is the authority on what a track can hold — most visibly for a
    /// level-up health maximum, which depends on rolls made while restoring — so the
    /// value is checked against the resolved bounds here rather than trusted.
    /// </summary>
    private void ApplyTracks(ActorMechanicsState mechanics, long health, long stamina, long magicka, string owner)
    {
        ApplyTrack(mechanics, DaggerfallMechanicsIds.Health, health, owner);
        ApplyTrack(mechanics, DaggerfallMechanicsIds.Stamina, stamina, owner);
        ApplyTrack(mechanics, DaggerfallMechanicsIds.Magicka, magicka, owner);
    }

    private void ApplyTrack(ActorMechanicsState mechanics, DaggerfallTrackId track, long value, string owner)
    {
        ActorTrackRead read = mechanics.ReadTrack(TrackId.Parse(track.Value));
        long reduced = Math.Clamp(value, read.Bounds.Minimum.Raw, read.Bounds.Maximum.Raw);
        if (reduced != value)
        {
            _restoreNotices.Add(new SaveRestoreNotice("track-reduced-to-resolved-bounds",
                $"Saved {owner} {track.Value} {value} is outside the bounds this session resolves ({read.Bounds.Minimum.Raw} to {read.Bounds.Maximum.Raw}) and was reduced to {reduced}."));
        }

        mechanics.SetTrack(TrackId.Parse(track.Value), new ExactValue(reduced));
    }

    private void ApplyInventory(DaggerfallInventorySave saved)
    {
        saved.Validate();
        foreach (DaggerfallStackSave stack in saved.Stacks)
        {
            State.Inventory.Grant(new InventoryGrant(
                "daggerfall.restore.inventory", $"daggerfall.restore.stack.{stack.ItemId}",
                new InventoryItemId(stack.ItemId), stack.Quantity));
        }
        Dictionary<ulong, KitUniqueInventoryItem> unique = [];
        foreach (DaggerfallUniqueSave item in saved.UniqueItems)
        {
            unique.Add(item.EntityId, State.Equipment.Materialize(new UniqueItemMaterialization(
                $"daggerfall.restore.unique.{item.EntityId}", item.EntityId, new InventoryItemId(item.ItemId))));
        }
        foreach (IGrouping<ulong, DaggerfallEquipmentSave> group in saved.Equipment.GroupBy(value => value.ItemEntityId))
        {
            State.Equipment.Equip(unique[group.Key], group.Select(value => new KitEquipmentSlotId(value.SlotId)).ToArray(),
                new EquipmentChange("daggerfall.restore.equipment", $"daggerfall.restore.equipment.{group.Key}"));
        }
    }

    /// <summary>
    /// Refills each placed actor's managed inventory from its saved stacks. A save from before
    /// the sections existed carries none, so those actors start from their authored loadout:
    /// the composition registered their inventories fresh and skipped the new-game grants only
    /// when a section said otherwise, so the authored default is granted here.
    /// </summary>
    private void ApplyActorInventories(DaggerfallActorInventorySave[]? saved)
    {
        Dictionary<long, DaggerfallActorInventorySave> sections = (saved ?? []).ToDictionary(section => section.EntityId);
        foreach ((long entityId, MechanicsInventoryCoordinator inventory) in State.ActorInventories)
        {
            foreach (DaggerfallStackSave stack in sections.TryGetValue(entityId, out DaggerfallActorInventorySave? section)
                ? section.Stacks
                : AuthoredLoadoutStacks(entityId))
            {
                if (stack.Quantity == 0) continue;
                inventory.Grant(new InventoryGrant(
                    "daggerfall.restore.actor-inventory", $"daggerfall.restore.actor.{entityId}.{stack.ItemId}",
                    new InventoryItemId(stack.ItemId), stack.Quantity));
            }
        }
    }

    private IReadOnlyList<DaggerfallStackSave> AuthoredLoadoutStacks(long entityId)
    {
        if (!_authoredDefinitions.TryGetValue(entityId, out DaggerfallActorDefinition? definition)) return [];
        // Composition refuses unique loadout entries for placed actors, so every entry that
        // reaches here is a fungible stack.
        return definition.Loadout.Select(entry => new DaggerfallStackSave(entry.ItemId.Value, entry.Quantity)).ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeAll([_hud, State.Actors, State.Equipment, _camera, _spatial, _appearance]);
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

    private void React(IProductFact fact)
    {
        _staminaRecovery.React(fact);
        if (fact is ActorDiedFact died)
        {
            _corpseLoot.Create(died);
            _rewards.React(died, _facts);
        }
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        _appearance.React(fact, State.Actors);
        _outcomes.React(fact);
    }

    private void PublishPresentation()
    {
        _hud.Publish(State.Actors.Player, State.Progression, Presentation, _mode, State.PlayerControl, Slots, _inventoryUi.Read(), _lootUi.Read(), _characterUi.Read(State.Actors.Player, State.Progression), LatestPanelRequest);
        _appearance.UpdateRightHandEquipment(State.Equipment.Read());
        _appearance.UpdateDirections(State.Actors, _camera.Viewpoint);
        _appearance.Publish(State.Actors);
    }

    /// <summary>
    /// The panel the player asked for through a device the DOM has no channel of its own for.
    /// </summary>
    /// <remarks>
    /// The keyboard reaches the panels because the DOM hears the keys itself; a pad reaches the
    /// product. The panels stay where they are — the DOM owns whether one is open — so a button that
    /// opens one travels as that panel's own menu action and the product invents no second notion of
    /// a panel. The revision is what lets the DOM apply each request once while the request stays
    /// published, the same way a published loot revision is recognised rather than replayed.
    /// </remarks>
    internal DaggerfallPanelRequest? LatestPanelRequest => _panelRequest is null ? null : new DaggerfallPanelRequest(_panelRequest, _panelRequestRevision);

    private void RequestPanel(string panel)
    {
        _panelRequest = panel;
        _panelRequestRevision = checked(_panelRequestRevision + 1);
        _panelRequestRemainingSeconds = PanelRequestLifetimeSeconds;
    }

    /// <summary>
    /// Ages a standing panel request on the same admitted world time everything else ages on.
    /// </summary>
    /// <remarks>
    /// Opening a panel is an event, not state: the product does not own whether one is open, so a
    /// request that outlived the DOM that performed it would re-open a panel nobody asked for on the
    /// next page load — and a player holding only a pad has no way back out of it. The window is
    /// generous because the DOM acts on the next snapshot, and it is admitted world time because
    /// there is one clock here.
    /// </remarks>
    private void AgePanelRequest(double deltaSeconds)
    {
        if (_panelRequest is null) return;
        _panelRequestRemainingSeconds -= deltaSeconds;
        if (_panelRequestRemainingSeconds <= 0d) _panelRequest = null;
    }

    internal static ItemDefinition ToManagedItem(DaggerfallItemDefinition item)
    {
        ItemEquipmentPolicy? equipment = item.Equipment is null
            ? null
            : new ItemEquipmentPolicy(
                item.Equipment.RequiredSlots,
                item.Equipment.ExclusiveGroup is { } group ? EquipmentExclusivityId.Parse(group) : null);
        // Weight is authored item metadata for every catalog entry.  The current
        // reference session does not register a carrying-capacity policy, so this
        // is a cost declaration rather than a claim that encumbrance is enforced.
        IEnumerable<ItemCapacityCost>? capacity = item.Weight > 0
            ? [new ItemCapacityCost(CapacityMetricId.Parse("weight"), checked((ulong)item.Weight))]
            : null;
        return new ItemDefinition(
            ItemDefinitionId.Parse(item.Id.Value),
            item.IsFungible ? ItemKind.Fungible : ItemKind.Unique,
            item.MaximumQuantity,
            item.Equipment?.Classifications.Select(ItemClassificationId.Parse),
            capacity,
            equipment);
    }

    internal static EquipmentSlotDefinition ToManagedSlot(DaggerfallEquipmentSlotDefinition slot) =>
        new(Rusty.Engine.Mechanics.EquipmentSlotId.Parse(slot.Id.Value), slot.AllowedClassifications.Select(ItemClassificationId.Parse));

    /// <summary>
    /// Applies the enemy swings whose authored damage frame was reached in the sprite
    /// playback this update consumed. The presentation reports the beat; the ruleset
    /// owns what it means, and a swing that expired or lost its target applies nothing.
    /// </summary>
    private void ApplyAttackImpacts()
    {
        IReadOnlyList<AttackImpactNotice> impacts = _appearance.TakeAttackImpacts();
        if (impacts.Count == 0) return;
        if (_latestUpdateGeneration is not ulong generation) return;
        _combat.ApplyImpacts(impacts, generation, _facts);
        DeliverFacts();
    }

    internal void ResolveExplicitMelee(ExplicitMeleeRequest request)
    {
        _latestUpdateGeneration = request.Generation;
        _latestSimulationStep = request.SimulationStep;
        _combat.ObserveTimeline(request.Generation, request.SimulationStep);
        _combat.ResolveExplicit(request, _facts);
        DeliverFacts();
        PublishPresentation();
    }

    internal DaggerfallMeleeTargetingEvidence? LastMeleeTargeting => _combat.LastMeleeTargeting;

    private void DeliverFacts()
    {
        if (_outerFacts is { } transaction) transaction.Deliver(React);
        else _facts.Deliver(React);
    }
    internal IReadOnlyDictionary<long, EnemyBehaviorEvidence> LastEnemyBehavior => _enemyBehavior.LastEvidence;
    internal LootPresentation? OpenLoot => _lootUi.Read();
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

    private DaggerfallVitalValues InitialVitals(DaggerfallActorDefinition definition, long entityId)
    {
        if (definition.Id.Value == "player") return definition.PlayerInitialVitals;
        int health = checked((int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, CombatRandomKey.EnemyScope, CombatRandomKey.InitialHealth(entityId, definition.Id.Value), definition.Health.Minimum, definition.Health.Maximum)).Value);
        return new DaggerfallVitalValues(health, 0, 0);
    }

    private static void ValidateInitialEntityIds(PrivateersHoldInputs inputs, IReadOnlyList<DaggerfallLoadoutEntry> loadout)
    {
        HashSet<ulong> ids = [PlayerMechanicsEntityId];
        foreach (AuthoredActor actor in inputs.Project.Actors.Values)
        {
            if (actor.EntityId <= 0 || !ids.Add(checked((ulong)actor.EntityId)))
                throw new InvalidOperationException($"Initial Mechanics entity id '{actor.EntityId}' collides with another player, placement, or item entity.");
        }
        foreach (DaggerfallLoadoutEntry item in loadout)
            if (item.UniqueEntityId is ulong entityId && !ids.Add(entityId))
                throw new InvalidOperationException($"Initial Mechanics entity id '{entityId}' collides with another player, placement, or item entity.");
    }

}

internal static class DaggerfallInput
{
    internal static readonly InputActionId ToggleWeapon = new("daggerfall.toggle-weapon");
    internal static readonly InputActionId Attack = new("daggerfall.attack");
    internal static readonly InputActionId Interact = new("daggerfall.interact");
    internal static readonly InputActionId Inventory = new("daggerfall.inventory");
    internal static readonly InputActionId Character = new("daggerfall.character");
    internal static readonly InputActionId Menu = new("daggerfall.menu");
    internal static readonly PlayerControlBindings Controls = new(
        ["move"u8.ToArray(), "movement"u8.ToArray()],
        KeyboardControl.KeyW,
        KeyboardControl.KeyS,
        KeyboardControl.KeyA,
        KeyboardControl.KeyD,
        new DirectionalMovementBindings("move.forward"u8.ToArray(), "move.backward"u8.ToArray(), "move.left"u8.ToArray(), "move.right"u8.ToArray()));
    internal static readonly IReadOnlyList<InputActionBinding> Bindings = [
        new(Attack, "attack"u8.ToArray()),
        new(ToggleWeapon, "toggle-weapon"u8.ToArray()),
        new(Interact, "interact"u8.ToArray()),
    ];

    /// <summary>
    /// The pad's action buttons in the layout the browser shell delivers: the bottom face attacks, the
    /// right face reaches for what the player is facing, the left face readies the weapon, the top
    /// face opens the character sheet, select opens the pack, and start opens the menu. They are
    /// product bindings rather than engine mappings because the Engine publishes positions and this is
    /// the layer that knows what an action means.
    /// </summary>
    internal static readonly IReadOnlyList<ControllerActionBinding> PadActions = [
        new(ControllerButton.Button0, Attack),
        new(ControllerButton.Button1, Interact),
        new(ControllerButton.Button2, ToggleWeapon),
        new(ControllerButton.Button3, Character),
        new(ControllerButton.Button8, Inventory),
        new(ControllerButton.Button9, Menu),
    ];
}
