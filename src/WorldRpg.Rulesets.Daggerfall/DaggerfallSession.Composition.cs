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

/// <summary>The session's two start paths and the one constructor they share.</summary>
internal sealed partial class DaggerfallSession
{
    /// <summary>Starts a new game at the composition's start site: the one construction path for a new session.</summary>
    internal static DaggerfallSession StartNew(IEngineContext engine, DaggerfallSessionComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        return new DaggerfallSession(engine, composition, composition.StartSite, restore: null).AdmitComposition(engine, composition);
    }

    /// <summary>
    /// Resumes a current save: the one construction path for a restored session. The save is resolved
    /// against the admitted content before any session state or Engine resource exists.
    /// </summary>
    internal static DaggerfallSession Restore(IEngineContext engine, DaggerfallSessionComposition composition, RulesetSavePayload saved)
    {
        ArgumentNullException.ThrowIfNull(composition);
        DaggerfallSavePayload raw = DaggerfallSavePayload.Read(saved);
        DaggerfallSiteProfiles? profiles = composition.Profiles;
        DaggerfallSiteProfile activeInputs = profiles is null
            ? composition.StartSite
            : raw.Site.ActiveProfile is { } profile
                ? profiles.Require(profile.Require())
                : profiles.RequireUniqueSite(ToSiteId(raw.Site.Active) ?? throw new ArgumentException("A restored Daggerfall session must name an active site.", nameof(saved)));
        DaggerfallResolvedRestore restore = raw.ResolveRestore(composition.Definitions, activeInputs, profiles);
        return new DaggerfallSession(engine, composition, activeInputs, restore).AdmitComposition(engine, composition);
    }

    /// <summary>Admits the composition's site catalog and building names onto a composed session.</summary>
    private DaggerfallSession AdmitComposition(IEngineContext engine, DaggerfallSessionComposition composition)
    {
        if (composition.Profiles is { } profiles) AdmitSiteProfiles(profiles);
        if (composition.Blocks is { } blocks) Site.AdmitBuildingNames(engine.Random, composition.Definitions, blocks);
        return this;
    }

    /// <summary>
    /// Composes one session. A new game passes no restore; a resolved restore supplies the saved
    /// sections owners are constructed from and then the relational sections
    /// <see cref="DaggerSessionPersistence.Restore"/> applies in its one order.
    /// </summary>
    private DaggerfallSession(IEngineContext engine, DaggerfallSessionComposition composition, DaggerfallSiteProfile inputs,
        DaggerfallResolvedRestore? restore)
    {
        DaggerfallSavePayload? saved = restore?.Payload;
        DaggerfallDefinitions definitions = composition.Definitions;
        DaggerfallTuning tuning = composition.Tuning;
        DaggerfallSiteAudioBundles? audioBundles = composition.Audio;
        DaggerfallMusicBundle? music = composition.Music;
        // A restore resolves its saved profiles against the catalog while composing; a new game admits
        // the catalog once composed (AdmitComposition), so its action graphs and triggers grow there.
        DaggerfallSiteProfiles? profiles = restore is null ? null : composition.Profiles;
        List<IDisposable> partiallyConstructed = [];
        try
        {
            _engine = engine;
            _tuning = tuning;
            // The score's clips are named by the site's cue list and carried by the product-wide music
            // bundle, so the resolver the director asks is this session's own: it keeps the Engine
            // resource alive for as long as the session plays that track and releases it on disposal.
            _musicBundle = music;
            if (music is not null) _music = new DaggerfallMusicDirector(engine.Audio, ResolveMusicClip, ReportMusicRetired);
            _random = engine.Random;
            tuning = tuning.Validate();
            _definitions = definitions;
            _dungeonText = new DaggerfallDungeonTextActions(new DaggerfallTextResolver(definitions.Text));
            _encounters = new DaggerfallEncounterRuntime(definitions, _random);
            Cinematics = composition.CinematicContent is null ? null : new DaggerfallCinematicPresentation(engine, composition.CinematicContent, definitions.Cinematics);
            if (Cinematics is not null) partiallyConstructed.Add(Cinematics);
            _openingCinematics = new DaggerfallOpeningCinematics(Cinematics, composition.VideosEnabled);
            DaggerActorAssembly assembled = DaggerActorFactory.Create(_random, definitions, inputs, saved, composition.QuestAdmission, composition.DisabledQuestSelection);
            State = assembled.State;
            State.Social.SetBiographyReactionModifier(State.Character.Background?.Modifiers.Reaction ?? 0);
            ActorsState actors = State.Actors;
            partiallyConstructed.Add(actors);
            Dictionary<long, DaggerfallActorDefinition> authored = assembled.Definitions;
            DaggerfallActorDefinition playerDefinition = assembled.PlayerDefinition;
            EntityId playerEntity = actors.Player.Actor.Entity;
            MechanicsInventoryCoordinator inventory = State.Inventory;
            MechanicsEquipmentCoordinator equipmentCoordinator = State.Equipment;
            MechanicsInventoryContainerCoordinator containers = State.Containers;
            Presentation = new PresentationState("Ready");
            _time = new DaggerfallWorldTime(
                saved?.Calendar is { } restored
                    ? new DaggerfallCalendar(restored.Year, restored.Month, restored.Day, restored.Hour, restored.Minute, restored.Second)
                    : DaggerfallCalendar.Start,
                saved?.Calendar?.RemainderSeconds ?? 0d,
                tuning.Time.GameSecondsPerRealSecond);
            // New games use the bundle site; current saves carry their explicit site state.
            _site = saved?.Site is { } restoredSite
                ? new World.DaggerfallSiteContext(
                    definitions.Locations,
                    ToSiteId(restoredSite.Active),
                    ToSiteId(restoredSite.ReturnAnchor),
                    ToSiteReturnPose(restoredSite.ReturnPose),
                    restoredSite.Discovered.Select(id => id.Require()))
                : new World.DaggerfallSiteContext(definitions.Locations, inputs.Site, null, []);
            _travelPolicy = new DaggerfallTravelPolicy(_site, definitions.Grids, tuning.Transport);
            DaggerfallWorldProfileKey activeProfile = saved?.Site.ActiveProfile?.Require() ?? inputs.ProfileKey;
            _controlEngine = engine;
            _input = new PlayerInputSystem(tuning.PlayerControl, DaggerfallInput.Controls, DaggerfallInput.Bindings, tuning.ControllerInput);
            _locomotion = new DaggerfallLocomotionPolicy(tuning.Locomotion, _controlSettings);
            _climbing = new DaggerfallClimbingPolicy(tuning.Climbing);
            _spatial = new SpatialMovementSystem(engine.Spatial, engine.Content, inputs.SpatialArtifact, tuning.Spatial);
            _dungeonVisibility = new DaggerfallDungeonVisibility(_spatial, tuning.Spatial.CollisionVoxelSize);
            _grounding = new DaggerfallActorGrounding(engine.Spatial, _spatial,
                tuning.EnemyBehavior.SpawnGroundProbeLift, tuning.EnemyBehavior.SpawnGroundProbeDistance);
            partiallyConstructed.Add(_spatial);
            foreach ((DaggerfallWorldProfileKey key, DaggerfallSiteProfile admitted) in DaggerfallSiteLifecycle.ActionProfiles(inputs, profiles))
            {
                DaggerfallDungeonActionGraphSnapshot? snapshot = saved?.DungeonActions
                    .SingleOrDefault(value => StringComparer.Ordinal.Equals(value.ProfileId, key.LogicalId));
                State.DungeonActions.Add(key, new DaggerfallDungeonActionGraph(
                    key.LogicalId,
                    admitted.DungeonActions,
                    State.Variables,
                    snapshot,
                    ExecuteDungeonFamilyAction));
            }
            // ResolveRestore admitted every saved discovery against a dungeon profile of the catalog.
            foreach (DaggerfallDungeonDiscoverySnapshot snapshot in saved?.DungeonDiscovery ?? [])
            {
                DaggerfallSiteProfile admitted = snapshot.Profile == inputs.ProfileKey ? inputs : profiles!.Require(snapshot.Profile);
                State.DungeonDiscoveries.Add(snapshot.Profile, new DaggerfallDungeonDiscovery(snapshot.Profile, admitted.DungeonMap!, snapshot));
            }
            if (inputs.DungeonMap is { } initialMap && !State.DungeonDiscoveries.ContainsKey(activeProfile))
                State.DungeonDiscoveries.Add(activeProfile, new DaggerfallDungeonDiscovery(activeProfile, initialMap));
            // The selected site's normalized RDB doors restore their Engine pose/collider projection
            // before activation can query them and before the first character step consumes them.
            DaggerfallSiteProjection projection = DaggerfallSiteProjection.Create(engine, State.Actors.Entities, _random, tuning, _time.Calendar,
                inputs, audioBundles?.Require(inputs.ProfileKey), _spatial, saved?.Doors, saved?.DungeonMotion);
            partiallyConstructed.Add(projection);
            DaggerfallDungeonActionTriggerRuntime actionTriggers = new(
                State.Actors.Entities, engine.Spatial, _spatial, DaggerfallSiteLifecycle.ActionProfiles(inputs, profiles), activeProfile);
            partiallyConstructed.Add(actionTriggers);
            // Dynamic actors restore before the site projection, while authored placements are
            // already in ActorSprites.  Admit their mobile media here without replaying any item
            // creation or combat rolls.
            foreach (ActorState actor in actors.All.Where(actor => !inputs.ActorSprites.ContainsKey(actor.DurableId)))
            {
                if (!authored.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition) || definition.MobileId is not int mobileId) continue;
                if (!inputs.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
                    throw new InvalidOperationException($"Restored dynamic actor '{definition.Id.Value}' has no admitted mobile {mobileId} presentation.");
                projection.Appearance.AddActor(actor.DurableId, sprite);
            }
            if (saved is null)
            {
                foreach (ActorState actor in actors.All.Where(actor => authored[actor.DurableId].GroundOnSpawn))
                    _grounding.Ground(actor);
            }
            _camera = new FirstPersonCameraSystem(engine.CameraView, State.PlayerControl, tuning.Camera);
            partiallyConstructed.Add(_camera);
            TargetingService targeting = new(engine.Perception, _spatial, State.Actors,
                new DaggerTargetingPolicy(authored, tuning.MeleeTargeting, () => _sites.Projection.Inputs));
            _staminaRecovery = new DaggerfallStaminaRecoveryModule(tuning.StaminaRecovery);
            CombatResolution combatRules = new();
            _combatResolution = combatRules;
            _vitality = new DaggerfallVitalityConsequences(combatRules);
            // One catalog answers every effect family this ruleset compiles, so a saved effect names the
            // definition that has to interpret it rather than the family that happened to start it.
            State.Effects = new DaggerfallEffectLifecycle(State.Actors, composition.Effects ?? new DaggerfallEffectCatalog(
            [
                .. DaggerfallDiseasePolicy.Definitions(
                    _random,
                    () => _time.Calendar.DayNumber,
                    () => State.Character.Career,
                    combatRules,
                    AppendEffectDamage),
                .. DaggerfallPoisonEffects.Definitions(_random, _vitality, () => State.Character.Career),
            ]));
            partiallyConstructed.Add(State.Effects);
            _rewards = new DaggerfallRewardReactions(
                State.Progression,
                State.Actors.Player.Stats,
                State.Actors.Player.Actor.Entity,
                () => State.Character.Career,
                _random,
                authored,
                tuning.Progression);
            State.SkillUses = new DaggerfallSkillUseReactions(State.Progression, State.Actors.Player.Stats, definitions, () => State.Character.Career);
            State.GuildMembership = new DaggerfallGuildMembershipPolicy(State.Social,
                State.SkillUses.PermanentSkillValue, DaggerfallConcreteGuildCatalog.AllMembershipPolicies);
            State.ConcreteGuildMembership = new DaggerfallConcreteGuildMembershipRuntime(State.GuildMembership);
            State.Quests.BindRuntime(new DaggerfallQuestRuntime(State.Progression, State.Actors.Player.Stats, definitions,
                State.QuestTraining, tuning.Locomotion, _random, () => _time.Calendar, AdvanceQuestTraining));
            State.Quests.BindTravelMinutes(site => _travelPolicy.CautiousQuestLegMinutes(QuestTravelOrigin(), site));
            State.Character.BindCareerCommitted(State.SkillUses.RebaseForCareerSelection);
            State.LevelUps = new DaggerfallLevelUpState(State.Progression, State.SkillUses, State.Actors.Player.Stats,
                definitions, () => State.Character.Career, _random, _rewards);
            _equipmentMoves = new DaggerfallEquipmentMoves(inventory, equipmentCoordinator, definitions,
                () => State.Character.Career.ForbiddenEquipment, State.ItemInstances);
            _itemCondition = new DaggerfallItemConditionService(definitions, State.ItemInstances, _equipmentMoves);
            _playerSwings = new DaggerfallSwingTracker(_tuning.MeleeTargeting.MinimumSwingGestureRadians);
            _combat = new DaggerCombatRules(_random, State.Actors, State.Equipment, State.InventoryFor, State.ItemInstances, definitions, authored, targeting, use => State.SkillUses.Record(use),
                () => State.Character.Background?.Modifiers.AvoidHit ?? 0, State.EquipmentFor, _itemCondition, combatRules,
                actorId => actorId == DaggerfallActorIdentity.PlayerEntityId
                    && State.Character.CustomCareer?.Advantages.Any(trait => trait.Id == "adrenaline-rush") == true
                    ? new DaggerfallAdrenalineRush(Enabled: true, Improved: _heldEnchantments.Talents.AdrenalineRush) : default,
                () => State.PlayerControl.Position, () => State.Character, _playerSwings.TryGesture, ShotBlockedByCover,
                () => _heldEnchantments.ArmorValueModifier, DeliverWeaponPoison);
            State.Kit = new(State.Actors, _combat.Targeting, _combat.Attacks, _combat.Execution, _combat.Rules, State.Inventory, State.Equipment);
            _enemyBehavior = new DaggerfallEnemyBehaviorModule(
                engine.Perception,
                _spatial,
                new ActorNavigationCoordinator(engine.Spatial, _spatial.Session),
                State.Actors,
                State.Kit.Attacks,
                tuning.EnemyBehavior,
                contextProvider: BuildEnemyPerceptionContext,
                recordSkillUse: use => State.SkillUses.Record(use));
            _authoredEntityIds = DaggerActorFactory.AdmittedAuthoredEntityIds(inputs, playerDefinition.Loadout);
            if (restore is null)
            {
                ulong[] actorReservations = [DaggerfallActorIdentity.PlayerEntityId, .. inputs.Project.Actors.Values.Select(placement => checked((ulong)placement.EntityId))];
                _actorIdentities = DurableIdentityAllocator.Restore(new DurableIdentityState(
                [
                    new KindAllocatorState(DurableIdentityKind.Actor, DynamicActorFirstIdentity, actorReservations, []),
                    new KindAllocatorState(DurableIdentityKind.Item, DaggerfallUniqueItemAllocator.DefaultFirstEntityId, [.. _authoredEntityIds], []),
                    new KindAllocatorState(DurableIdentityKind.Container, GroundContainerFirstIdentity, [], []),
                ]));
            }
            else
            {
                // The ledger ResolveRestore rebuilt to validate the save is the session's live allocator.
                _actorIdentities = restore.Identities;
            }

            _uniqueItems = DaggerfallUniqueItemAllocator.Sharing(_actorIdentities);
            State.Npcs.Identities = _actorIdentities;
            _heldEnchantments = new DaggerfallHeldEnchantments(State.Equipment, State.ItemInstances, definitions.Magic.MagicItems,
                State.Actors.Player.Stats, State.Actors.Entities, State.Actors.Player.Actor.Entity, () => _time.Calendar,
                () => State.PlayerControl.Position, () => DaggerfallActorRoster.NearbyCreatures(State.Actors, authored), InSunlight, _itemCondition, InHolyPlace,
                amount => _vitality.ResolveHeldEnchantmentDamage(State.Actors.Player.Actor, amount));
            State.HeldEnchantments = _heldEnchantments;
            // Poisons are active effects, so restoring them is the effect lifecycle's own restore: this
            // owner reads, starts and cures them and keeps no state of its own to carry.
            _poisons = new DaggerfallPoisonRuntime(State.Effects, PoisonRoll, () => State.Character.Career);
            State.Poisons = _poisons;
            State.Encumbrance = new DaggerfallEncumbrancePolicy(State.Inventory, State.Actors.Player.Stats,
                () => _heldEnchantments.CarryMultiplier);
            // A new game wears its loadout now; a restore refreshes once, after its equipment is restored.
            if (restore is null) _heldEnchantments.Refresh();
            State.Currency = new DaggerfallCurrencyService(definitions, State.Inventory, State.ItemInstances, State.Encumbrance, _uniqueItems, saved?.Currency);
            State.Bank = new DaggerfallRegionalBankState(State.Currency, State.Inventory, State.ItemInstances, saved?.Bank);
            State.Loans = new DaggerfallLoanState(saved?.Loans);
            State.Property = new DaggerfallPropertyState(tuning.Property, saved?.Property);
            InitializePropertyStorage(saved?.Property);
            State.Crime = new DaggerfallCrimeState(saved?.Crime);
            State.Services = new DaggerfallServiceTransactions(State.Npcs, State.Social, State.Inventory, State.ItemInstances,
                State.Currency, _uniqueItems, () => _time.Calendar, () => _site.ActiveSite is { } active
                    ? new DaggerfallNpcSite(active.Id.Region, active.Name, string.Empty)
                    : null, saved?.Services);
            State.ConcreteGuildServices = new DaggerfallConcreteGuildServiceRuntime(
                State.GuildMembership, State.Npcs, State.Services);
            State.KnightlyClaims = new DaggerfallKnightlyOrderClaimState(saved?.KnightlyClaims);
            State.KnightlyClaimActions = new DaggerfallKnightlyOrderClaimRuntime(
                State.ConcreteGuildServices, State.KnightlyClaims, _random);
            State.SkillTraining = new DaggerfallSkillTrainingService(State.Services, State.Npcs, State.Social,
                State.Progression, State.SkillUses, State.QuestTraining, State.Actors.Player.Stats,
                tuning.Locomotion, () => _time.Calendar, seconds => { _ = AdvanceElapsedTime(seconds); });
            State.RegionalPrices = new DaggerfallRegionalPriceState(definitions.Factions, _random,
                _time.Calendar.DayNumber, saved?.RegionalPrices);
            State.RegionalPrices.AdvanceToDay(_time.Calendar.DayNumber);
            State.TradeQuotes = new DaggerfallTradeQuoteService(definitions, new DaggerfallItemValuation(definitions),
                State.RegionalPrices);
            State.Transport = new DaggerfallTransportPolicy(tuning.Transport);
            State.Wagon = new DaggerfallWagonStorage(State.Containers, State.ItemInstances, definitions,
                playerEntity, _actorIdentities, tuning.Transport);
            _corpseLoot = new DaggerfallCorpseLootModule(
                engine.Perception,
                _spatial,
                containers,
                State.ItemInstances,
                playerEntity,
                State.Actors,
                authored,
                definitions,
                State.Encumbrance,
                _random,
                _uniqueItems,
                State.Progression,
                tuning.LootInteraction,
                State.Character,
                _actorIdentities);
            _groundContainers = new DaggerfallGroundContainers(containers, State.ItemInstances, playerEntity, _actorIdentities, activeProfile);
            _outcomes = new DaggerfallOutcomePresentation(Presentation, authored, () => State.Kit.Targeting.LastEvidence, definitions.Text);
            _inventoryUi = new DaggerfallInventoryPresentation(_equipmentMoves, definitions, inputs.ClassicPresentation.InventoryIcons,
                State.Encumbrance, State.Currency);
            _inventoryUi.UseBank(State.Bank, ActiveBankRegion);
            _inventoryUi.UseLoans(State.Loans, () => _time.Calendar, () => State.Progression.Level);
            _inventoryUi.UseItemValuation(new DaggerfallItemValuation(definitions), State.ItemInstances, DaggerfallItemOwner.Player,
                entity => State.Actors.Entities.IdentityOf(new Rusty.Engine.Entities.EntityId(entity)).Value);
            _inventoryUi.UseItemCondition(_itemCondition);
            _inventoryUi.UseGroundDrops(_groundContainers, () => State.PlayerControl.Position);
            _notebook = new DaggerfallBookNotebook(definitions, new DaggerfallTextResolver(definitions.Text));
            _inventoryUi.UseItemActions(new DaggerfallInventoryUseService(State.Inventory, definitions, State.ItemInstances, _uniqueItems, _site, _random, _itemCondition, _notebook,
                useDrug: variant => UseDrug(variant) == DaggerfallPoisonAdmission.Admitted));
            _inventoryUi.BookOpened += _ => RequestPanel(DaggerfallPanel.Journal);
            _lootUi = new DaggerfallLootPresentation(_corpseLoot, _inventoryUi, _groundContainers);
            _interactions = new DaggerfallOpenInteractions(
                () => _lootUi.Read(),
                () => _lootUi.Message,
                dungeonTextOpen: () => _dungeonTextProjection is not null,
                dialogueOpen: () => _activationPresentation.View.Dialogue is not null,
                characterCreationOpen: () => State.Character.Pending is not null,
                levelUpOpen: () => State.LevelUps.Pending is not null,
                bankOpen: () => ActiveBankRegion() is not null);
            _persistence = new(State, _corpseLoot, _groundContainers, _notebook, _uniqueItems, _camera, _time, _site, State.Effects, () => _doors, _locomotion, _climbing, _dungeonText, CapturePropertyStorage);
            _roster = new DaggerfallActorRoster(State, definitions, _random, assembled.Mechanics, _actorIdentities, _uniqueItems,
                _authoredEntityIds, authored, saved?.DynamicActors ?? [], _grounding, () => _sites.Projection, _lootUi, _corpseLoot);
            _sites = new DaggerfallSiteLifecycle(engine, State, definitions, tuning, _time, _site, _spatial, _camera, audioBundles,
                _roster, _persistence, _groundContainers, _enemyBehavior, ExecuteDungeonFamilyAction, this,
                projection, actionTriggers, profiles, activeProfile, saved?.Site.ReturnProfile?.Require());
            InitializeActivation(engine, tuning.LootInteraction);
            _characterUi = new DaggerfallCharacterPresentation(definitions, State.Character, playerDefinition, equipmentCoordinator, State.LevelUps, State.Social, State.SkillUses);
            _characterUi.UseGuildMembership(State.GuildMembership, () => checked((int)_time.Calendar.DayNumber));
            _characterUi.UseItemPresentation(_inventoryUi);
            // The DOM's art comes from admitted content by media identity, so a session reads the
            // published closure once and publishes it to the UI that draws it.
            _hud = new DaggerfallHudProjection(
                engine.Ui,
                definitions.HudResources,
                composition.Identity,
                DaggerfallUiArt.Read(engine.Content, inputs.ClassicPresentation.InventoryIcons.Values));
            partiallyConstructed.Add(_hud);
            if (restore is not null)
                _persistence.Restore(restore, _sites, _roster, _encounters, _heldEnchantments, RestoreDungeonText);
            _sites.AdmitInitialExterior(saved?.ExteriorResidency);
        }
        catch (Exception constructionFailure)
        {
            List<Exception> failures = [constructionFailure];
            try { DisposeAll(partiallyConstructed); }
            catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            try { _sites?.RetireExteriorAppearance(); }
            catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            if (failures.Count == 1) throw;
            throw new AggregateException(failures);
        }
    }

    private static DaggerfallSiteId? ToSiteId(DaggerfallSiteIdSave? id) => id?.Require();

    private static World.DaggerfallSiteReturnPose? ToSiteReturnPose(DaggerfallSiteReturnPoseSave? pose) => pose is null
        ? null
        : new World.DaggerfallSiteReturnPose(new WorldPoint(pose.X, pose.Y, pose.Z), pose.YawRadians, pose.PitchRadians);
}
