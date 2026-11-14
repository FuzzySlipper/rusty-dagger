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
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
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
        composition = composition with { Profiles = composition.Profiles?.ForSession() };
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
        if (raw.WorldVariants is null) throw new ArgumentException("Saved world variants must contain the current selection, including an empty list.");
        composition = composition with { Profiles = composition.Profiles?.ForSession(raw.WorldVariants) };
        if (composition.Profiles is null && raw.WorldVariants.Length != 0) throw new ArgumentException("Saved world variants require an admitted site catalog.");
        DaggerfallSiteProfiles? profiles = composition.Profiles;
        DaggerfallSiteProfile activeInputs = profiles is null
            ? composition.StartSite
            : raw.Site.ActiveProfile is { } profile
                ? profiles.Require(profile.Require())
                : throw new ArgumentException("A restored Daggerfall session must name its active world profile.", nameof(saved));
        DaggerfallResolvedRestore restore = raw.ResolveRestore(composition.Definitions, activeInputs, profiles, composition.Tuning);
        return new DaggerfallSession(engine, composition, activeInputs, restore).AdmitComposition(engine, composition);
    }

    /// <summary>Admits the composition's site catalog and building names onto a composed session.</summary>
    private DaggerfallSession AdmitComposition(IEngineContext engine, DaggerfallSessionComposition composition)
    {
        if (composition.Profiles is { } profiles) AdmitSiteProfiles(profiles);
        if (composition.Blocks is { } blocks) Site.AdmitBuildingNames(engine.Random, composition.Definitions, blocks, State.Quests.LearnedBuildingName);
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
        DaggerfallDefinitions definitions = restore?.Definitions ?? composition.Definitions.ForSession([]);
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
            _playerTime = new World.DaggerfallPlayerTime(engine.GameplayTime);
            _composition = composition;
            _loading = composition.Loading;
            _newGameInitialized = saved is not null;
            _tuning = tuning;
            _viewLight = new(tuning.SiteLighting);
            // The score's tracks are read by name from the music container, so the resolver the director asks is
            // this session's own: it keeps the Engine resource alive for as long as the session plays that track
            // and releases it on disposal.
            _musicBundle = music;
            if (music is not null)
            {
                _music = new DaggerfallMusicDirector(engine.Audio, ResolveMusicClip, ReportMusicRetired, tuning.Music.AlternatePlaylists);
                ReportMissingTracks(music);
            }
            // A 3D viewmodel without its art draws the classic sprites; say why once rather than silently.
            if (tuning.Viewmodel.Style != DaggerfallViewmodelStyle.Classic && composition.HeldArt is null)
                engine.Diagnostics.Publish(new DiagnosticsPublishRequest(DiagnosticsSeverity.Warning, DiagnosticsDisposition.Accepted,
                    "daggerfall.viewmodel", "art.missing",
                    $"viewmodel.style is {tuning.Viewmodel.Style} but {DaggerfallHeldArt.ContainerName} is not installed, so the classic weapon sprites are drawn.",
                    string.Empty));
            _random = engine.Random;
            tuning = tuning.Validate();
            _definitions = definitions;
            _buildingGreeting = new DaggerfallBuildingGreeting(definitions, _random);
            _dungeonText = new DaggerfallDungeonTextActions(new DaggerfallTextResolver(definitions.Text));
            _encounters = new DaggerfallEncounterRuntime(definitions, _random);
            Cinematics = composition.Data is null ? null : new DaggerfallCinematicPresentation(engine, composition.Data, definitions.Cinematics);
            if (Cinematics is not null) partiallyConstructed.Add(Cinematics);
            _openingCinematics = new DaggerfallOpeningCinematics(Cinematics, composition.VideosEnabled);
            // Owners are built in dependency order, each from the owners it reads; the named state is
            // constructed once every service it names exists, so nothing reads a service before it is built.
            bool materializeActiveSite = saved?.ExteriorLocationResidency?.Loaded != false;
            DaggerActorAssembly assembled = DaggerActorFactory.Create(_random, definitions, inputs,
                saved is null ? null : new DaggerActorSaves(saved.Player, saved.Actors, saved.BanishedActors, saved.DynamicActors, saved.Npcs, saved.Character),
                materializeActiveSite);
            ActorsState actors = assembled.Actors;
            partiallyConstructed.Add(actors);
            DaggerfallCharacterState character = assembled.Character;
            DaggerfallVariableStore variables = new(definitions.QuestSources.Tables.Globals.Lookup);
            if (saved?.Variables is { } restoredVariables)
                variables.Restore(restoredVariables.Entries.Select(entry => (entry.Require(), entry.Value)));
            DaggerfallSocialState social = new(definitions.Factions);
            if (saved?.Social is { } restoredSocial) social.Restore(restoredSocial);
            DaggerfallQuestInstances quests = new(definitions, _random, composition.QuestAdmission, composition.DisabledQuestSelection);
            DaggerfallQuestTrainingState questTraining = new(saved?.QuestTraining);
            DaggerfallItemInstances itemInstances = assembled.ItemInstances;
            DaggerfallNpcRegistry npcs = assembled.Npcs;
            social.SetBiographyReactionModifier(character.Background?.Modifiers.Reaction ?? 0);
            Dictionary<long, DaggerfallActorDefinition> authored = assembled.Definitions;
            DaggerfallActorDefinition playerDefinition = assembled.PlayerDefinition;
            EntityId playerEntity = actors.Player.Actor.Entity;
            StatsComponent playerStats = actors.Player.Stats;
            ProgressionState progression = actors.Player.Progression;
            MechanicsInventoryCoordinator inventory = assembled.Inventory;
            MechanicsEquipmentCoordinator equipmentCoordinator = assembled.Equipment;
            MechanicsInventoryContainerCoordinator containers = assembled.Containers;
            Presentation = new PresentationState("Ready", DaggerfallOutcomePresentation.MessageLifetimeSeconds,
                DaggerfallOutcomePresentation.MaximumMessages, DaggerfallOutcomePresentation.SubsequentMessageLifetimeSeconds);
            _time = new DaggerfallWorldTime(
                saved?.Calendar is { } restored
                    ? new DaggerfallCalendar(restored.Year, restored.Month, restored.Day, restored.Hour, restored.Minute, restored.Second)
                    : DaggerfallCalendar.Start with { Hour = definitions.NewGame.StartHour },
                saved?.Calendar?.RemainderSeconds ?? 0d,
                tuning.Time.GameSecondsPerRealSecond);
            _weather = new DaggerfallWeatherState(_random, tuning.Weather, _time.Calendar, saved?.Weather);
            // New games use the bundle site; current saves carry their explicit site state.
            _site = saved?.Site is { } restoredSite
                ? new World.DaggerfallSiteContext(
                    definitions.Locations,
                    ToSiteId(restoredSite.Active),
                    ToSiteId(restoredSite.ReturnAnchor),
                    ToSiteReturnPose(restoredSite.ReturnPose),
                    restoredSite.Discovered.Select(id => id.Require()), restoredSite.Buildings, restoredSite.FailedBuildingPicks)
                : new World.DaggerfallSiteContext(definitions.Locations, inputs.Site, null, []);
            DaggerfallTravelPolicy travelPolicy = new(_site, definitions.Grids, tuning.Transport);
            DaggerfallWorldProfileKey activeProfile = saved?.Site.ActiveProfile?.Require() ?? inputs.ProfileKey;
            if (DaggerfallWorldProfileIds.TryParse(activeProfile.LogicalId, out _, out var arrivedBuilding) && arrivedBuilding is { } building)
                _site.DiscoverBuilding(activeProfile.Site, building);
            _input = new PlayerInputSystem(tuning.PlayerControl, DaggerfallInput.Controls, DaggerfallInput.Bindings, tuning.ControllerInput);
            DaggerfallControlSettings controlSettings = new();
            _locomotion = new DaggerfallLocomotionPolicy(tuning.Locomotion, controlSettings);
            _controls = new DaggerfallPlayerControls(engine.Input, engine.Audio, _input, _locomotion, controlSettings,
                tuning.PlayerControl.LookSensitivity, tuning.ControllerInput, _playerTime.Select,
                (enabled, distance) => _horizon!.UseGroundCover(enabled, distance));
            _climbing = new DaggerfallClimbingPolicy(tuning.Climbing);
            // Location artifacts are admitted by the exterior's collision placement so an exterior map-pixel
            // transition can remove and re-admit one profile without replacing the whole session.
            _spatial = new SpatialMovementSystem(engine.Spatial, engine.Content, null, tuning.Spatial);
            _dungeonVisibility = new DaggerfallDungeonVisibility(_spatial, tuning.Spatial.CollisionVoxelSize);
            _grounding = new DaggerfallActorGrounding(engine.Spatial, _spatial,
                tuning.EnemyBehavior.SpawnGroundProbeLift, tuning.EnemyBehavior.SpawnGroundProbeDistance);
            partiallyConstructed.Add(_spatial);
            _contact = new ActorContact(actors, _spatial, () => assembled.PlayerControl, DaggerfallContactBody.Envelope, id => SpellTargets.IsEligible(id));
            Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonActionGraph> dungeonActions = [];
            IReadOnlyList<(DaggerfallWorldProfileKey Key, DaggerfallSiteProfile Inputs)> actionProfiles =
                DaggerfallInteriors.ActionProfiles(inputs, profiles, saved?.DungeonActions.Select(snapshot => snapshot.ProfileId));
            foreach ((DaggerfallWorldProfileKey key, DaggerfallSiteProfile admitted) in actionProfiles)
            {
                DaggerfallDungeonActionGraphSnapshot? snapshot = saved?.DungeonActions
                    .SingleOrDefault(value => StringComparer.Ordinal.Equals(value.ProfileId, key.LogicalId));
                dungeonActions.Add(key, new DaggerfallDungeonActionGraph(
                    key.LogicalId,
                    admitted.DungeonActions,
                    variables.Write,
                    snapshot,
                    ExecuteDungeonFamilyAction));
            }
            // ResolveRestore admitted every saved discovery against a dungeon profile of the catalog.
            Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonDiscovery> dungeonDiscoveries = [];
            foreach (DaggerfallDungeonDiscoverySnapshot snapshot in saved?.DungeonDiscovery ?? [])
            {
                DaggerfallSiteProfile admitted = snapshot.Profile == inputs.ProfileKey ? inputs : profiles!.Require(snapshot.Profile);
                dungeonDiscoveries.Add(snapshot.Profile, new DaggerfallDungeonDiscovery(snapshot.Profile, admitted.DungeonMap!, snapshot));
            }
            if (inputs.DungeonMap is { } initialMap && !dungeonDiscoveries.ContainsKey(activeProfile))
                dungeonDiscoveries.Add(activeProfile, new DaggerfallDungeonDiscovery(activeProfile, initialMap));
            // The selected site's normalized RDB doors restore their Engine pose/collider projection
            // before activation can query them and before the first character step consumes them.
            DaggerfallSiteProjection projection = DaggerfallSiteProjection.Create(engine, actors.Entities, _random, tuning, _time.Calendar,
                inputs, audioBundles?.Require(inputs), _spatial, ConfigureSiteAppearance, saved?.Doors, saved?.DungeonMotion,
                sessionPresentation: composition.StartSite.ClassicPresentation, definitions: _definitions);
            partiallyConstructed.Add(projection);
            DaggerfallDungeonActionTriggerRuntime actionTriggers = new(
                actors.Entities, engine.Spatial, _spatial, actionProfiles, activeProfile);
            partiallyConstructed.Add(actionTriggers);
            // Dynamic actors restore before the site projection, while authored placements are
            // already in ActorSprites.  Admit their mobile media here without replaying any item
            // creation or combat rolls.
            foreach (ActorState actor in actors.All.Where(actor => !inputs.ActorSprites.ContainsKey(actor.DurableId)
                || DaggerfallWabbajack.DefinitionOf(actor.Actor) is not null))
            {
                if (!authored.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition) || definition.MobileId is not int mobileId) continue;
                if (!inputs.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
                    throw new InvalidOperationException($"Restored dynamic actor '{definition.Id.Value}' has no admitted mobile {mobileId} presentation.");
                projection.Appearance.RetireActor(actor.DurableId);
                projection.Appearance.AddActor(actor.DurableId, sprite.ForGender(actor.Actor.TryGet<DaggerfallActorGender>(out var gender) ? gender.Value : "unspecified"), mobileId);
            }
            if (saved is null)
            {
                foreach (ActorState actor in actors.All.Where(actor => authored[actor.DurableId].GroundOnSpawn))
                    _grounding.Ground(actor);
            }
            _camera = new FirstPersonCameraSystem(engine.CameraView, assembled.PlayerControl, tuning.Camera);
            partiallyConstructed.Add(_camera);
            TargetingService targeting = new(engine.Perception, _spatial, actors,
                new DaggerTargetingPolicy(authored, tuning.MeleeTargeting, () => _sites.Projection.Inputs, npcs.IsGameplayActive, PersonSightHeight));
            CombatResolution combatRules = new();
            _combatResolution = combatRules;
            _vitality = new DaggerfallVitalityConsequences(combatRules);
            // One catalog answers every effect family this ruleset compiles, so a saved effect names the
            // definition that has to interpret it rather than the family that happened to start it.
            DaggerfallEffectLifecycle effects = null!;
            effects = new(actors, composition.Effects ?? new DaggerfallEffectCatalog(
            [
                .. DaggerfallAlterationEffects.Definitions(effect => effects.Cancel(effect.Context.Instance)),
                .. DaggerfallThaumaturgyEffects.Definitions(),
                DaggerfallParalysisEffects.Definition(ReactToSpellAttack),
                .. DaggerfallCureEffects.Definitions(
                    target => DaggerfallDiseasePolicy.CureAllDiseases(effects, target),
                    actor => _poisoning!.Runtime.Cure(actor), target => DaggerfallParalysisEffects.Cure(effects, target)),
                .. DaggerfallMysticismEffects.Definitions(id => id == actors.Player.DurableId ? actors.Player.Progression.Level : authored[id].Level ?? 1,
                    request => DispelChoice.Request(request),
                    (effect, daedra) => DispelChoice.BanishNearby(effect, daedra, assembled.PlayerControl.Position!.Value,
                        (center, radius) => SpellTargets.Area(DaggerfallActorIdentity.PlayerEntityId, center.ToVector(), true, radius, exclusive: true),
                        id => actors.TryGet(id, out var actor) && !actor.IsDefeated && DefinitionsByActor.TryGetValue(id, out var definition) ? definition : null, BanishActor),
                    ReactToSpellAttack, request => TeleportChoice.Request(request)),
                .. DaggerfallItemSoulEffects.Definitions(request => CreateItemChoice.Request(request),
                    id => authored.TryGetValue(id, out var actorDefinition) && actorDefinition.Kind == DaggerfallActorKinds.Monster ? actorDefinition.MobileId : null,
                    mobile => ItemSouls.Gems.Capture(mobile), _random, (target, message, dies) => _facts.Append(new SoulTrapResolvedFact(target, message, dies))),
                .. DaggerfallSocialMagicEffects.Definitions(
                    (effect, group) => DaggerfallSocialMagicEffects.Pacify(effect, group, id => actors.TryGet(id, out var actor) && !actor.IsDefeated && DefinitionsByActor.TryGetValue(id, out var definition) ? definition : null, target => _enemyBehavior!.Pacify(target)),
                    (effect, state) => IdentifyChoice.Request(effect, state)),
                DaggerfallIllusionEffects.MorphSelf(() => _lycanthropy!.Morph()),
                DaggerfallIllusionEffects.NormalLight(),
                .. DaggerfallConcealmentEffects.Definitions(),
                .. DaggerfallDetectionEffects.Definitions(),
                .. DaggerfallDoorMagicEffects.Definitions(),
                .. DaggerfallFortifyEffects.Definitions(() => character.Career),
                DaggerfallMolagBalEffects.Definition(() => _time.Calendar, () => character.Career, (owner, item) => _artifacts!.MolagBalEquipped(owner, item)),
                .. DaggerfallHealingEffects.Definitions(() => effects, () => character.Career, _vitality),
                .. DaggerfallAttributeDrainEffects.Definitions(() => character.Career, ReactToSpellAttack),
                .. DaggerfallAttributeDrainEffects.TransferDefinitions(() => character.Career, ReactToSpellAttack,
                    id => id == actors.Player.DurableId ? actors.Player.Actor : actors.TryGet(id, out var actor) ? actor.Actor : null,
                    () => effects),
                .. DaggerfallRestorationEffects.Definitions(engine.Random,
                    id => id == actors.Player.DurableId ? actors.Player.Progression.Level : authored[id].Level ?? 1, _vitality),
                .. DaggerfallContinuousDestructionEffects.Definitions(_random, _vitality, AppendEffectDamage, AppendSpellTrackLoss,
                    id => authored.TryGetValue(id, out var definition) && _senses!.IsHostile(id, definition), ReactToSpellAttack),
                .. DaggerfallTransferEffects.Definitions(_vitality, AppendSpellTransfer,
                    id => authored.TryGetValue(id, out var definition) && _senses!.IsHostile(id, definition), ReactToSpellAttack),
                .. DaggerfallDestructionEffects.Definitions(_vitality, AppendEffectDamage, AppendSpellTrackLoss,
                    id => authored.TryGetValue(id, out var definition) && _senses!.IsHostile(id, definition),
                    ReactToSpellAttack),
                DaggerfallRacialOverrides.Definition(character, tuning.Lycanthropy, tuning.Vampirism),
                .. DaggerfallTransformationInfectionPolicy.Definitions(effect => Infections!.Advance(effect)),
                .. DaggerfallDiseasePolicy.Definitions(
                    _random,
                    () => _time.Calendar.DayNumber,
                    () => character.Career,
                    combatRules,
                    AppendEffectDamage, (effect,track,amount)=>_vitality.AdjustConditionTrack(effect.Source,effect.Target,track,amount), target => _diseases!.SymptomsActed(target)),
                .. DaggerfallPoisonEffects.Definitions(_random, _vitality, () => character.Career, target => _diseases!.SymptomsActed(target)),
                DaggerfallTempleBlessingEffects.Definition(social, () => character.Career, actors.Player.DurableId),
            ]));
            DaggerfallRacialOverrides racialOverrides = new(effects, character, definitions, previous => _vampirism!.Ended(previous));
            character.UseRaceOverride(racialOverrides.ApplyToBirthRace);
            effects.RestoreMagicRounds(saved?.MagicRounds ?? 0);
            Infections = new(effects, () => _time.Calendar.DayNumber, Cinematics, composition.VideosEnabled,
                message => Presentation.SetOutcome(message), saved?.Infections);
            partiallyConstructed.Add(Infections);
            Infections.BindConsumer(this);
            _diseases = new DaggerfallDiseases(effects, actors, _random, _time, character, combatRules, Presentation, definitions);
            partiallyConstructed.Add(effects);
            if (composition.Effects is null)
                foreach (var recipe in definitions.Magic.PotionRecipes.Values) recipe.ValidateEffects(effects.Catalog);
            _rewards = new DaggerfallRewardReactions(
                progression,
                playerStats,
                playerEntity,
                () => character.Career,
                _random,
                authored,
                tuning.Progression);
            DaggerfallSkillUseReactions skillUses = new(progression, playerStats, definitions, () => character.Career);
            DaggerfallGuildMembershipPolicy guildMembership = new(social,
                skillUses.PermanentSkillValue, DaggerfallConcreteGuildCatalog.AllMembershipPolicies);
            DaggerfallConcreteGuildMembershipRuntime concreteGuildMembership = new(guildMembership);
            DaggerfallQuestDiseases questDiseases = new(definitions, _diseases, effects, actors.Player.DurableId);
            DaggerfallQuestEnvironment questEnvironment = new(definitions, _time, questDiseases, _music, _musicBundle, composition.VideosEnabled,
                Cinematics, () => _sites.Projection.Inputs.Audio, () => CurrentWeather, () => CurrentClimate,
                (clip, quest, line, count) => _appearance.PlayQuestSound(clip, quest, line, count));
            quests.BindEnvironmentActions(questEnvironment.Condition, questEnvironment.Media);
            quests.BindOfferCapabilities(questEnvironment.OfferCapability);
            quests.BindDiseaseActions(questDiseases.Apply);
            quests.BindLycanthropyCure(() => _lycanthropy!.Cure(fromQuest: true));
            quests.BindVampirismCure(() => _vampirism!.Cure(fromQuest: true));
            quests.BindRuntime(new DaggerfallQuestRuntime(progression, playerStats, definitions,
                questTraining, tuning.Locomotion, _random, () => _time.Calendar,
                // Training spends its own source fatigue cost, so its minutes charge no idle loss.
                seconds => AdvanceQuestTime(seconds, idleFatigue: false)));
            quests.BindTravelMinutes(site => FastTravel.QuestLegMinutes(site));
            character.BindCareerCommitted(skillUses.RebaseForCareerSelection);
            DaggerfallLevelUpState levelUps = new(progression, skillUses, playerStats,
                definitions, () => character.Career, _random, _rewards);
            // A rest slice passes through the elapsed interval's list; skills raise once, when the whole rest ends.
            _rest = new DaggerfallRest(_time, tuning.Rest, skillUses, levelUps,
                seconds => AdvanceElapsedTime(seconds, deferSkillAdvancement: true, resting: true));
            _equipmentMoves = new DaggerfallEquipmentMoves(inventory, equipmentCoordinator, definitions,
                () => character.Career.ForbiddenEquipment, itemInstances, () => racialOverrides.Current?.State.BeastForm == true);
            _itemCondition = new DaggerfallItemConditionService(definitions, itemInstances, _equipmentMoves);
            _itemEnchanting = new DaggerfallItemEnchanting(definitions, itemInstances, _equipmentMoves, _itemCondition, inventory, () => ItemSouls.Gems);
            _itemCondition.Broken += item => ItemSouls.ReleaseBoundSoul(item);
            _placement = new DaggerfallActorPlacement(actors, assembled.PlayerControl, npcs.IsGameplayActive, authored, id => QuestFoes.Team(id),
                id => _enemyBehavior!.IsPacified(id), id => quests.AllowsFoeInfighting(id), _spatial, engine.Perception, _random,
                () => _exterior.CharacterEnvironment(assembled.PlayerControl.Motion), _tuning.EnemyBehavior.DetectionDistance,
                (float)_tuning.Camera.FieldOfViewYDegrees);
            _senses = new DaggerfallEnemySenses(actors, assembled.PlayerControl, definitions, authored, id => QuestFoes.Team(id),
                id => _enemyBehavior!.IsPacified(id), () => _enemyBehavior!.LastPerception, effects, _locomotion,
                () => State!.WeaponDrawn, () => _combat!.PlayerHoldsBow(), () => _time.Calendar.MinuteNumber, _random,
                () => _activeProfileKey, _encounters);
            WorldEncounters = new DaggerfallWorldEncounters(_encounters, _placement, definitions, assembled.PlayerControl,
                () => actors.Player.Progression.Level, () => State!.Swimming.IsSwimming, () => State!.Transport.OnShip, () => _activeProfileKey,
                () => _site.TryFind(_activeProfileKey.Site, out DaggerfallSiteRecord site) ? site : null,
                () => { DaggerfallExteriorCellId cell = _exterior.CurrentExteriorCell(); return (cell.X, cell.Y); },
                () => _exterior.LocationAtPlayer() is not null, () => _latestUpdateGeneration, (definition, pose, level) => SpawnActor(definition, pose, level));
            SpellTargets = new DaggerfallSpellTargets(definitions, actors, character, effects, _spatial, npcs.IsGameplayActive,
                id => DefinitionsByActor.TryGetValue(id, out var definition) ? definition : null, _placement.CurrentPositions,
                request => _engine.Perception.QueryVisibility(request));
            _heldEnchantments = new DaggerfallHeldEnchantments(equipmentCoordinator, itemInstances, definitions.Magic,
                playerStats, actors.Entities, playerEntity, () => _time.Calendar,
                () => assembled.PlayerControl.Position, () => DaggerfallActorRoster.NearbyCreatures(actors, authored, npcs.IsGameplayActive), InSunlight, _itemCondition, InHolyPlace,
                amount => AppendEffectDamage(new(_vitality.ResolveHeldEnchantmentDamage(actors.Player.Actor, amount))), social, () => DaggerfallHeldEnchantments.DrainNearby(assembled.PlayerControl.Position, _tuning.StrikeEnchantments.VampiricRange,
                    (center, radius) => SpellTargets.Area(actors.Player.DurableId, center.ToVector(), excludeCaster: true, radius: radius),
                    id => actors.TryGet(id, out var actor) && !actor.IsDefeated && DefinitionsByActor.TryGetValue(id, out var definition) ? definition : null, id => QuestFoes.Team(id),
                    id => AppendSpellTransfer(_vitality.ResolveSpellTransfer(actors.Player.Actor, actors.Get(id).Actor, 1, fatigue: false, permitsFatigueLoss: true))));
            effects.UseHeldDefense(actors.Player.DurableId, _heldEnchantments);
            _passives = new DaggerfallCareerPassives(character, definitions, authored, actors.Player.DurableId);
            effects.UseCareerDefense(CareerMagicDefense);
            partiallyConstructed.Add(_heldEnchantments);
            _equipmentMoves.Changed += _ =>
            {
                _heldEnchantments.Refresh();
                _itemCastTriggers?.Refresh();
                DaggerfallMolagBalEffects.Reconcile(effects, _artifacts!.MolagBalEquipped);
            };
            DaggerfallActorInventories actorInventories = assembled.ActorInventories;
            Actor EffectActor(long id) => id == actors.Player.DurableId ? actors.Player.Actor : actors.Get(id).Actor;
            _artifacts = new DaggerfallArtifactPowers(definitions, itemInstances, inventory, _itemCondition, effects, actors, _random,
                () => _time.Calendar, () => assembled.PlayerControl.Position, equipmentCoordinator, actorInventories,
                id => id == actors.Player.DurableId ? actors.Player.Actor : actors.TryGet(id, out var actor) ? actor.Actor : null,
                id => DefinitionsByActor.TryGetValue(id, out var definition) ? definition : null,
                (origin, radius, eligible) => _placement.QueryEnemies(DaggerfallActorIdentity.PlayerEntityId, origin, Vector3.UnitZ, radius, -1d, eligible),
                mobile => _sites.Projection.Inputs.MobileSprites.ContainsKey(mobile),
                (operation, scope, origin, minimum, maximum) => _placement.TrySpawnPose(operation, scope, origin, minimum, maximum, out var pose) ? pose : null,
                (definition, pose, allied) => _roster!.Spawn(definition, pose, playerAllied: allied),
                id => quests.ProtectsActor(id), (id, mobile) => _roster!.Transform(id, mobile));
            DaggerfallContactInputs contactInputs = new(definitions, () => character, _random,
                id => id == actors.Player.DurableId ? equipmentCoordinator.Read() : actorInventories.EquipmentFor(id).Read(),
                id => id == actors.Player.DurableId ? actors.Player.Stats : actors.TryGet(id, out ActorState actor) ? actor.Stats : null,
                () => _heldEnchantments.ArmorValueModifier)
            {
                PlayerAvoidHit = () => character.Background?.Modifiers.AvoidHit ?? 0,
                PlayerCurse = () => racialOverrides.Current,
            };
            _guard = new DaggerfallContactGuard(contactInputs, _tuning.Contact);
            _condition = new DaggerfallPlayerCondition(_vitality, _tuning.Attacks.Stamina, _tuning.Rest, _random, DaggerfallRandomKey.Seed);
            _condition.Stamina.Refresh(actors.Player.Stats);
            _touch = new DaggerfallContactTouch(new(_contact, _spatial, () => _sites?.Exterior.CharacterEnvironment(assembled.PlayerControl.Motion)), _tuning.Contact);
            ActorNavigationCoordinator navigation = new(engine.Spatial, _spatial.Session, actors.Store, _spatial.CurrentController,
                actor => _exterior.CharacterEnvironment(actor.Actor.Store.Get(actor.Actor.Entity, EngineComponentTypes.CharacterMotion)));
            _reactions = new ActorReactions(navigation);
            Traps = new DaggerfallTraps(_tuning.Hazards, actors, _reactions, () => assembled.PlayerControl.Position,
                () => _sites.Projection.Inputs.DungeonActions, point => _sites.Interiors.ActionTriggers.ContactActionsAt(point),
                () => actors.Player.Progression.Level, _combatResolution, _random, damage => AppendDamage(damage, DaggerfallDamageCause.Hazard, 0));
            _hazards = new DaggerfallHazards(_tuning.Hazards, actors, _reactions, () => assembled.PlayerControl.Position,
                point => _exterior.LocalToProfile(point), () => _sites.Projection.Inputs, _vitality, Presentation,
                damage => AppendDamage(damage, DaggerfallDamageCause.Hazard, 0), Traps.ForgetLanded, Traps.SpringOn);
            _monsterHits = new DaggerfallMonsterHits(_diseases, Infections, effects,
                (attacker, target) => Casting.TriggerMonsterParalysis(attacker, target), () => _activeProfileKey.Site.Region);
            StrikeAim = new DaggerfallStrikeAim(actors, () => assembled.PlayerControl.Position, () => _input.ResolveCurrentLook(assembled.PlayerControl).Forward,
                _tuning.Camera.EyeHeight, _contact, () => _sites?.Exterior.CharacterEnvironment(assembled.PlayerControl.Motion), _tuning.Attacks.AimRange,
                actor => QuestFoes.Team(actor) == "player-ally",
                actor => DefinitionsByActor.TryGetValue(actor, out DaggerfallActorDefinition? definition) && definition.Kind == DaggerfallActorKinds.Civilian);
            StrikeFeel = new DaggerfallStrikeFeel(_tuning.Attacks.Strike, _camera, () => _engine.GameplayTime.Read().Rate);
            _combat = new DaggerCombatRules(_random, actors, equipmentCoordinator, actorInventories.InventoryFor, itemInstances, definitions, authored, targeting, use => skillUses.Record(use),
                actorInventories.EquipmentFor, _itemCondition, combatRules,
                () => assembled.PlayerControl.Position, () => character, (target, coating) => _poisoning!.DeliverWeaponPoison(target, coating), () => _heldEnchantments.AttackChanceModifier, _artifacts.TransformWithWabbajack, effects.MagicDefenseFor,
                actorId => effects.ControlsFor(actorId).PhysicalAttacks, _artifacts.TransferMolagBal, (caster, target, item, damage) => _itemCastTriggers.Strike(caster, target, item, damage), _monsterHits.Deliver,
                npcs.IsGameplayActive, id => QuestFoes.Team(id),
                new DaggerfallContactMitigation(contactInputs, _tuning.Contact, _guard),
                _tuning.Contact,
                _tuning.Attacks,
                targetUnaware: id => EnemyBehavior.Unaware(id),
                alertTarget: id => EnemyBehavior.Provoke(id, _time.Calendar.MinuteNumber),
                playerRush: () => _passives.SpecialsFor(actors.Player.DurableId).AdrenalineRush
                    ? new DaggerfallAdrenalineRush(_heldEnchantments.Talents.AdrenalineRush) : null,
                _contact, StrikeAim.StrikePose, () => _sites?.Exterior.CharacterEnvironment(assembled.PlayerControl.Motion),
                // Only the player's own controller reports a velocity; site actors step between poses.
                actor => actor == actors.Player.DurableId ? assembled.PlayerControl.Motion.ControlledVelocity : Vector3.Zero,
                new DaggerfallStrikes(_tuning.Attacks.Strike, actors, _reactions, () => assembled.PlayerControl.Position,
                    id => DefinitionsByActor.TryGetValue(id, out DaggerfallActorDefinition? struck) ? struck.Weight : null, id => EnemyBehavior.NavigationMode(id)),
                StrikeAim.PassesThrough) { PlayerCurse = () => racialOverrides.Current };
            _equipmentMoves.Changed += _combat.EquipmentChanged;
            GameplayServices<IProductFact> kit = new(actors, _combat.Targeting, _combat.Attacks, _combat.Execution, _combat.Rules, inventory, equipmentCoordinator);
            _enemyBehavior = new DaggerfallEnemyBehaviorModule(
                engine.Perception,
                _spatial,
                navigation,
                actors,
                kit.Attacks,
                tuning.EnemyBehavior,
                contextProvider: _senses.PerceptionContext,
                recordSkillUse: use => skillUses.Record(use),
                random: _random, motorContext: _senses.MotorContext,
                isPlayerAllied: id => QuestFoes.Team(id) == "player-ally",
                selectAllyTarget: _placement.SelectAllyTarget, controlRestrictions: effects.ControlsFor, reactions: _reactions,
                movementPolicy: id => DaggerfallEnemyBehaviorModule.PolicyFor(
                    id,
                    authored,
                    definitions,
                    tuning.EnemyBehavior.Maneuvers,
                    effects.GrantsWaterWalking(id),
                    effects.GrantsLevitation(id)),
                canOpenDoors: id => DaggerfallEnemyBehaviorModule.CanOpenDoors(id, authored, definitions),
                openBlockedDoor: actor => _doors.TryOpenForEnemy(actor.Position), selectTarget: observer => QuestFoes.SelectTarget(observer), questRestrained: id => QuestFoes.Restrained(id), attackAtDistance: _combat.EnemyAttackAtDistance,
                engagement: new DaggerfallContactEngagement(_tuning.Contact));
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
            npcs.Identities = _actorIdentities;
            // Poisons are active effects, so restoring them is the effect lifecycle's own restore; the
            // admission keeps only its draw ordinal.
            _poisoning = new DaggerfallPoisoning(effects, _random, actors, character, itemInstances, authored, definitions,
                combatRules, saved?.PoisonDraws ?? 0);
            DaggerfallPoisonRuntime poisons = _poisoning.Runtime;
            // The curses an infection becomes; a curse's cure passes an hour, through quest time when a quest cures it.
            DaggerfallTransformationHealing transformationHealing = new(_diseases, _poisoning, effects, actors, character);
            _lycanthropy = new DaggerfallLycanthropy(racialOverrides, _time, tuning.Lycanthropy,
                () => _heldEnchantments.HircinesRingEquipped, _equipmentMoves.UnequipHands, actors, _random, Presentation, this, transformationHealing,
                seconds => AdvanceElapsedTime(seconds), seconds => AdvanceQuestTime(seconds));
            _vampirism = new DaggerfallVampirism(racialOverrides, _time, tuning.Vampirism, definitions.Factions,
                _random, Presentation, this, transformationHealing,
                seconds => AdvanceElapsedTime(seconds), seconds => AdvanceQuestTime(seconds));
            DaggerfallEncumbrancePolicy encumbrance = new(inventory, playerStats,
                () => _heldEnchantments.CarryMultiplier);
            // A new game wears its loadout now; a restore refreshes once, after its equipment is restored.
            if (restore is null) _heldEnchantments.Refresh();
            DaggerfallCurrencyService currency = new(definitions, inventory, itemInstances, encumbrance, _uniqueItems, containers, saved?.Currency);
            DaggerfallRegionalBankState bank = new(currency, inventory, itemInstances, saved?.Bank);
            DaggerfallLoanState loans = new(saved?.Loans);
            DaggerfallPropertyState property = new(tuning.Property, saved?.Property);
            DaggerfallPropertyStorage propertyStorage = new(containers, itemInstances, definitions, _actorIdentities, property, playerEntity);
            if (saved?.Property is { } savedProperty) propertyStorage.Restore(savedProperty);
            DaggerfallCrimeState crime = new(saved?.Crime, () => racialOverrides.Current?.SuppressCrime == true);
            DaggerfallServiceTransactions services = new(npcs, social, inventory, itemInstances,
                currency, _uniqueItems, containers, playerEntity, () => _time.Calendar, () => _site.ActiveSite is { } active
                    ? new DaggerfallNpcSite(active.Id.Region, active.Name, string.Empty, _sites.ActiveProfile.LogicalId)
                    : null, saved?.Services);
            DaggerfallConcreteGuildServiceRuntime concreteGuildServices = new(guildMembership, npcs, services.ProviderAvailable);
            DaggerfallTempleServiceRuntime templeServices = new(concreteGuildServices, services, npcs, social,
                effects, poisons, actors, _random, () => _time);
            DaggerfallKnightlyOrderClaimState knightlyClaims = new(saved?.KnightlyClaims);
            DaggerfallKnightlyOrderClaimRuntime knightlyClaimActions = new(concreteGuildServices, knightlyClaims, _random);
            DaggerfallSkillTrainingService skillTraining = new(services, npcs, social,
                progression, skillUses, questTraining, playerStats,
                tuning.Locomotion, () => _time.Calendar.ToAbsoluteSeconds(), seconds => { _ = AdvanceElapsedTime(seconds, idleFatigue: false); });
            DaggerfallRegionalPriceState regionalPrices = new(definitions.Factions, _random,
                _time.Calendar.DayNumber, saved?.RegionalPrices);
            regionalPrices.AdvanceToDay(_time.Calendar.DayNumber);
            DaggerfallTradeQuoteService tradeQuotes = new(definitions, new DaggerfallItemValuation(definitions), regionalPrices);
            DaggerfallMerchantService merchants = new(definitions, _random, npcs, social, playerStats, character, progression,
                inventory, containers, itemInstances, _uniqueItems, _actorIdentities, currency, services, concreteGuildServices, tradeQuotes,
                regionalPrices, _itemCondition, skillUses, crime, () => _time.Calendar,
                () => _dialogue?.CurrentNpc()?.Site,
                saved?.Merchants, () => Crimes.QueryWitnesses());
            DaggerfallTransportPolicy transport = new(tuning.Transport);
            DaggerfallWagonStorage wagon = new(containers, itemInstances, definitions,
                playerEntity, _actorIdentities, tuning.Transport);
            DaggerfallSwimmingPolicy swimming = new(tuning.Swimming);
            State = new DaggerfallState(
                assembled,
                variables: variables,
                social: social,
                quests: quests,
                questTraining: questTraining,
                kit: kit,
                effects: effects,
                racialOverrides: racialOverrides,
                skillUses: skillUses,
                levelUps: levelUps,
                heldEnchantments: _heldEnchantments,
                poisons: poisons,
                crime: crime,
                guildMembership: guildMembership,
                concreteGuildMembership: concreteGuildMembership,
                concreteGuildServices: concreteGuildServices,
                knightlyClaims: knightlyClaims,
                knightlyClaimActions: knightlyClaimActions,
                encumbrance: encumbrance,
                currency: currency,
                bank: bank,
                loans: loans,
                property: property,
                lodging: new DaggerfallLodgingState(saved?.Lodging),
                travel: new DaggerfallTravelState(saved?.Travel),
                services: services,
                templeServices: templeServices,
                skillTraining: skillTraining,
                tavern: new DaggerfallTavernService(services, playerStats, () => _time.Calendar, saved?.LastTavernMealMinute),
                regionalPrices: regionalPrices,
                tradeQuotes: tradeQuotes,
                merchants: merchants,
                transport: transport,
                wagon: wagon,
                swimming: swimming,
                dungeonDiscoveries: dungeonDiscoveries,
                dungeonActions: dungeonActions,
                dialogueWorld: new DaggerfallDialogueWorldState(saved?.DialogueWorld));
            _creation = new DaggerfallCharacterCreation(State.Character, definitions, _random);
            _atlasTiles = composition.Data is { } atlasData && DaggerfallAtlas.Read(atlasData) is { } atlas ? new(atlas, atlasData, engine.Ui) : null;
            FastTravel = new DaggerfallFastTravel(travelPolicy, State.Travel, State.Transport, _site, _random,
                definitions.BuildingNames.RegionNames, definitions.WorldNames,
                new DaggerfallExteriorWorldBounds(definitions.Terrain.Width, definitions.Terrain.Height), _atlasTiles,
                calendar: () => _time.Calendar,
                passTime: slice => AdvanceElapsedTime(slice, deferSkillAdvancement: true, idleFatigue: false, completeTimeIncrease: false),
                activeProfile: () => _activeProfileKey,
                exteriorCell: () => _exterior.CurrentExteriorCell(),
                playerDefeated: () => State.Actors.Player.IsDefeated,
                isVampire: () => State.RacialOverrides.Current?.IsVampire == true,
                encounterPending: _encounters.HasPending,
                enemyNearby: () => _senses.EnemyNearby(),
                profiles: () => _sites.Profiles,
                options: selected =>
                {
                    DaggerfallCurrencyTotals funds = State.Currency.Read();
                    return new(selected.Cautious, selected.Inn, selected.Ship,
                        State.Transport.HasHorse(State.Inventory.Read()),
                        State.Transport.HasCart(State.Inventory.Read()),
                        State.Property?.OwnsShip ?? false,
                        AvailableGold: checked(funds.Gold + funds.LettersOfCredit),
                        AvailableGoldPieces: funds.Gold,
                        FreeShipPassage: GuildProviders.FreeShipPassage());
                },
                classicArt: () => _classicArt,
                yaw: () => State.PlayerControl.YawRadians);
            Banking = new DaggerfallBanking(State.Bank, State.Currency, State.Loans, State.Services, State.Social, definitions.Factions,
                State.Progression, definitions.BuildingNames.RegionName,
                inventorySuppressed: () => State.RacialOverrides.Current?.SuppressInventory == true,
                calendar: () => _time.Calendar,
                provider: () => _sites.BankProvider,
                open: provider => _sites.BankProvider = provider);
            MerchantTrade = new DaggerfallMerchantTrade(State.Merchants, _site,
                currentNpc: revision => _dialogue?.CurrentNpc(revision),
                building: CurrentInteriorBuilding,
                activeProfile: () => _activeProfileKey,
                dialogueRevision: () => _activationPresentation.View.Dialogue?.Revision);
            RoomRental = new DaggerfallRoomRental(State.Lodging, _site, State.GuildMembership, State.Actors.Player.Stats, State.Currency,
                calendar: () => _time.Calendar,
                activeProfile: () => _activeProfileKey,
                building: CurrentInteriorBuilding);
            Holdings = new DaggerfallPropertyHoldings(State.Property, propertyStorage, State.Bank, State.Currency, State.Transport,
                State.KnightlyClaimActions, _site, _random, State.PlayerControl,
                calendar: () => _time.Calendar,
                activeProfile: () => _activeProfileKey,
                profiles: () => _sites.Profiles,
                building: CurrentInteriorBuilding,
                questBuilding: State.Quests.ClaimsBuilding,
                atEntrance: (profile, building) => State.PlayerControl.Position is { } position
                    && (_sites.Projection.Portals.All.Any(value => value.Portal.DestinationLogicalProfile == profile.LogicalId
                            && Vector3.Distance(position.ToVector(), value.Portal.Position.ToVector()) <= value.Portal.Radius)
                        || _doors.All.Any(door => _doors.ExteriorBuildingOf(door.Id) == building
                            && Vector3.Distance(position.ToVector(), door.Pose.Translation) <= _tuning.LootInteraction.MaximumDistance)));
            ItemSouls = new DaggerfallItemSouls(new(State.Inventory, State.ItemInstances, definitions.Magic, _uniqueItems), definitions,
                State.ItemInstances, _heldEnchantments, State.Effects, _facts, Presentation,
                id => DefinitionsByActor.TryGetValue(id, out var actor) ? actor : null,
                owner => owner == DaggerfallItemOwner.Player ? State.PlayerControl.Position
                    : owner.Scope == "actor" && State.Actors.TryGet(owner.Id, out var holder) ? holder.Position : null,
                spawn =>
                {
                    if (!_sites.Projection.Inputs.MobileSprites.ContainsKey(spawn.Mobile)
                        || !_placement.TrySpawnPose(spawn.Operation, spawn.RandomScope, spawn.Origin, spawn.MinimumDistance, spawn.MaximumDistance, out var pose)) return false;
                    _enemyBehavior.MakeHostile(_roster!.Spawn(spawn.Definition.Id.Value, pose, playerAllied: false));
                    return true;
                });
            _itemMaker = new DaggerfallItemMaker(definitions, State.Npcs, State.ConcreteGuildServices, State.Services,
                State.Inventory, State.ItemInstances, _itemEnchanting, ItemSouls.Gems, _uniqueItems, () => _time.Calendar);
            _potionMaker = new DaggerfallPotionMaking(definitions, State.Npcs, State.ConcreteGuildServices, State.Inventory,
                State.Containers, State.Wagon, State.ItemInstances, engine.Random, () => _time.Calendar);
            SpellSales = new DaggerfallSpellSales(definitions, State.Npcs, State.ConcreteGuildServices, State.Services,
                State.Character, State.Inventory, State.Actors.Player.Stats,
                key => Casting.AvailableSpellCost(State.Actors.Player.DurableId, key), () => _time.Calendar);
            SpellMaker = new DaggerfallSpellMaker(definitions, State.Effects.Catalog, State.Npcs, State.ConcreteGuildServices,
                State.Services, State.Character, State.Inventory, State.Actors.Player.Stats, () => _time.Calendar);
            _questTexts = new DaggerfallQuestTextContext(definitions, _site, State.Npcs, State.ItemInstances,
                item => DaggerfallInventoryPresentation.DescribeCreatedItem(definitions, item), CurrentTextContext);
            State.Quests.BindTextContext(_questTexts.For);
            DaggerfallNames questNames = new(definitions, engine.Random);
            npcs.BindNames(npc => questNames.NpcName(npc.Appearance, npc.Site.Region));
            var questPlaces = new DaggerfallQuestPlaceAllocator(definitions, _site, engine.Random,
                (site, building) => State.Property.OwnsHouse(new(site, building.Source.Id, building.Id.BlockX, building.Id.BlockY)),
                region => region >= 0 && region < definitions.BuildingNames.RegionNames.Count ? definitions.BuildingNames.RegionNames[region] : null,
                questNames.Residence);
            State.Quests.BindPlaceAllocator(questPlaces);

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
            _itemOwnership = new DaggerfallItemOwnership(State.Actors, State.InventoryStore, actor => _corpseLoot.Corpses[actor].Owner);
            _itemLifetime = new DaggerfallItemLifetime(State.Actors.Entities, State.InventoryStore, State.ItemInstances, _uniqueItems, _itemOwnership);
            _groundContainers = new DaggerfallGroundContainers(containers, State.ItemInstances, playerEntity, _actorIdentities, activeProfile);
            Detectors = new DaggerfallDetectors(State.Effects, actors, containers, _tuning.Detection.MaximumDistance,
                () => assembled.PlayerControl.Position, npcs.IsGameplayActive, id => DefinitionsByActor.TryGetValue(id, out var definition) ? definition : null,
                () => _groundContainers.All.Values.Select(ground => (ground.Id, ground.Owner, ground.Position))
                    .Concat(_corpseLoot.Corpses.Values.Where(value => value.IsRegistered && value.IsInteractable)
                        .Select(corpse => (checked((long)corpse.ContainerIdentity.Value), corpse.Owner, actors.Get(corpse.ActorId).Position))),
                (origin, positions, radius) => SpellTargets.Nearby(DaggerfallActorIdentity.PlayerEntityId, origin.ToVector(), positions, radius, true));
            _doorSpells = new DaggerfallDoorSpells(State.Effects, State.ItemInstances, () => State.Progression.Level);
            State.ItemInstances.AttachContainment(_itemOwnership.ResidentOwner);
            _outcomes = new DaggerfallOutcomePresentation(Presentation, authored, definitions.Mobiles, definitions.Text);
            _inventoryUi = new DaggerfallInventoryPresentation(_equipmentMoves, definitions, composition.StartSite.ClassicPresentation.InventoryIcons,
                State.Encumbrance, State.Currency);
            State.Quests.BindResourceAllocator(new(definitions, engine.Random, new DaggerfallItemFactory(definitions, engine.Random), questNames,
                () => new(State.Progression.Level, State.Character.Identity.RaceId,
                    State.Character.Identity.Gender.ToString().ToLowerInvariant(), _site.ActiveSite?.Region
                        ?? throw new NotSupportedException("Quest resource generation requires the current region.")),
                State.Social.GuildEligibility, State.RegionalPrices.AdjustmentForRegion, _inventoryUi.DescribeCreatedItem));
            State.Quests.BindPersonAllocator(new(definitions, engine.Random, questNames,
                instance => new(_site.ActiveSite ?? throw new NotSupportedException("Quest Person selection requires the current site."),
                    instance.QuestorId is long giver ? State.Npcs.Require(giver) : null,
                    PermanentPlayerClanFactionId: State.RacialOverrides.Current?.State.Vampire?.Clan, CurrentProfile: _sites.ActiveProfile,
                    Interior: _sites.Projection.Inputs.InteriorBuilding),
                State.Npcs.SetDisplayName, questPlaces));
            _inventoryUi.UseBank(State.Bank, () => Banking.ActiveRegion, () => State.Bank.DepositableGold(State.Wagon, TransportAccess()));
            _inventoryUi.UseLoans(State.Loans, () => _time.Calendar, () => State.Progression.Level);
            _inventoryUi.UseItemValuation(new DaggerfallItemValuation(definitions), State.ItemInstances, DaggerfallItemOwner.Player,
                entity => State.Actors.Entities.IdentityOf(new Rusty.Engine.Entities.EntityId(entity)).Value);
            _inventoryUi.UseItemCondition(_itemCondition);
            // A dropped pile rests on the floor under the player, not at the body centre the player's position names.
            _inventoryUi.UseGroundDrops(_groundContainers, () => State.PlayerControl.Position is WorldPoint at ? _grounding.RestingPosition(at) : null);
            _notebook = new DaggerfallBookNotebook(definitions, new DaggerfallTextResolver(definitions.Text));
            quests.BindNotebook(_notebook.AddQuestNote);
            quests.BindItemCleanup(RemoveCarriedQuestItems);
            State.QuestItems = new(State.Quests, State.Actors.Entities, State.InventoryStore, State.Containers, State.Equipment, State.ItemInstances,
                State.Currency.ReceiveGold, State.HeldEnchantments.Refresh, _actorIdentities, _uniqueItems, _itemOwnership.Entity, _itemLifetime.DestroyUnique, _itemLifetime.ConsumeStack, _equipmentMoves);
            State.Quests.BindItems(State.QuestItems);
            State.Quests.BindPlacementNpcs(State.Npcs);
            State.Npcs.BindQuestPresence(State.Quests.IsNpcUnavailable);
            State.Quests.BindItemInteractions((amount, goldOnly) => goldOnly ? State.Currency.TrySpendGold(amount, []) : State.Currency.TrySpendCarried(amount),
                (instance, symbol, recipient, operation, queued) => _questItemPlacement.Give(instance, symbol, recipient, operation, queued));
            _equipmentMoves.Changed += change =>
            {
                if (change.Equipped is { } item)
                    State.Quests.UseItem(State.ItemInstances.RequireUnique(State.Inventory.GetDurableItemId(new(item.EntityId)).Value));
            };
            State.Quests.BindClickGold(amount => State.Currency.TrySpendGold(amount, []));
            State.Quests.BindSocial(State.Social, State.Crime, () => _site.Region);
            State.Quests.BindWorldRead(() => _sites.ReadQuestLocation());
            State.Quests.BindWorldActions((instance, operation) => _questWorldActions.Apply(instance, operation));
            _inventoryUi.BindQuestDrops(State.Quests.CanDropItem, State.Quests.ItemDropped);
            State.Quests.BindFoeCommands((id, remove) => QuestFoes.Command(id, remove));
            State.Quests.BindFoeSpawning((operation, foe, send) => QuestFoes.Spawn(operation, foe, send));
            State.Quests.BindEnemyRelations(clear => QuestFoes.CommandAllEnemies(clear), id => actors.TryGet(id, out var actor) && !actor.IsDefeated);
            State.Quests.BindGuardSpawning((operation, immediate, elapsed, state) => _questGuards.Spawn(operation, immediate, elapsed, state));
            State.Quests.BindRewards((instance, symbol) => _questItemPlacement.OfferReward(instance, symbol), PresentQuestReward, tuning.QuestRewards);
            _inventoryUi.UseItemActions(new DaggerfallInventoryUseService(State.Inventory, definitions, State.ItemInstances, _uniqueItems, FastTravel.RevealFromMap, _itemCondition, _notebook,
                useDrug: template => _poisoning.UseDrug(template) == DaggerfallPoisonAdmission.Admitted,
                useOghma: () =>
                {
                    if (!State.LevelUps.BeginOghma()) return false;
                    RequestPanel(DaggerfallPanel.Character);
                    return true;
                }, useSanguineRose: _artifacts.UseSanguineRose, useSkullCorruption: _artifacts.UseSkullCorruption, useItemSpell: item => _itemCastTriggers.Use(item),
                useAzurasStar: item => ItemSouls.UseAzurasStar(State.Inventory.GetDurableItemId(new(item.EntityId)).Value), usePotion: key => Casting.UsePotion(actors.Player.DurableId, key), useQuestItem: State.Quests.UseItem,
                isAzurasStar: metadata => DaggerfallSoulGems.IsStar(metadata, definitions.Magic), castsOnUse: DaggerfallItemCastTriggers.CastsOnUse));
            _inventoryUi.BookOpened += _ => RequestPanel(DaggerfallPanel.Book);
            _lootUi = new DaggerfallLootPresentation(_corpseLoot, _inventoryUi, _groundContainers);
            _interactions = new DaggerfallOpenInteractions(
                () => _lootUi.Read(),
                () => _lootUi.Message,
                dungeonTextOpen: () => _dungeonTextProjection is not null,
                dialogueOpen: () => _activationPresentation.View.Dialogue is not null,
                characterCreationOpen: () => State.Character.Pending is not null,
                levelUpOpen: () => State.LevelUps.Pending is not null,
                bankOpen: () => Banking.ActiveRegion is not null, dispelOpen: () => DispelChoice.Pending is not null, identifyOpen: () => IdentifyChoice.Open, teleportOpen: () => TeleportChoice.Pending is not null, createItemOpen: () => CreateItemChoice.Pending is not null, legalOpen: () => Law.ModalOpen,
                tutorialOpen: () => _tutorial.Shown);
            itemInstances.StackChanged += State.Quests.ObserveStackChange;
            // Only a held bundle lives as long as its item is worn and whole. A used or striking item's
            // cast, its readied spell and its missile outlive the item breaking or changing hands, as in
            // the donor; a destroyed item ends them, since no effect may name an item that no longer exists.
            itemInstances.SourceUnavailable += (item, change) =>
            {
                if (change != DaggerfallItemSourceChange.Removed) { effects.CancelHeldItem(item); return; }
                effects.CancelItemReferences(item); Casting?.CancelItemReferences(item);
                IdentifyChoice?.ItemRemoved(item);
            };
            Casting = new(definitions.Magic, effects, SpellTargets.CastActor, SpellTargets.MagicProfile, item => itemInstances.ContainsUnique(item)
                    && (itemInstances.RequireUnique(item).MaximumCondition == 0 || itemInstances.RequireUnique(item).CurrentCondition > 0),
                use => State.SkillUses.Record(use), result => { State.Quests.ObserveQuestCast(result); _facts.Append(new SpellCastFact(result.Outcome, result.Bundle?.Sequence, result.Bundle?.CasterId,
                    result.Bundle?.Spell.Key, result.Bundle?.Cost ?? 0, result.Bundle?.Results.ToArray() ?? [], result.Bundle?.Absorptions.ToArray() ?? [],
                    result.Bundle?.Spell.Name, result.Bundle?.Spell.Element ?? 0, result.Bundle?.ItemId)); },
                _random, actors.Player.DurableId, saved?.NextCastSequence ?? 1, State.Character.KnownSpells.Contains,
                id => id == actors.Player.DurableId ? actors.Player.Progression.Level : authored[id].Level ?? 1,
                (caster, item) => itemInstances.RequireUnique(item).Owner == (caster == actors.Player.DurableId ? DaggerfallItemOwner.Player : DaggerfallItemOwner.Actor(caster)),
                () => _latestUpdateGeneration, () => _latestSimulationStep, State.Character.IsGrantedSpell, combatRules);
            State.Quests.BindCasting(Casting, effects.Catalog);
            SpellRelease = new DaggerfallSpellRelease(Casting, SpellTargets, definitions, actors, State.Character, State.Inventory, Presentation,
                _tuning.Camera.EyeHeight, _placement.CurrentPositions, cast => _touch.Reach(cast), _contact,
                () => _exterior.CharacterEnvironment(State.PlayerControl.Motion), StrikeAim.PassesThrough,
                (sequence, element, point) => _appearance.PlaySpellImpact(sequence, element, point));
            SpellFlights = new DaggerfallSpellFlights(Casting, SpellRelease);
            PlayerAttack = new DaggerfallPlayerAttack(_tuning.Attacks, _combat, actors, State.PlayerControl, () => _input.ResolveCurrentLook(State.PlayerControl),
                () => State.WeaponDrawn, effects, State.Kit.Attacks, _guard, StrikeAim, _contact,
                () => _sites?.Exterior.CharacterEnvironment(State.PlayerControl.Motion), Casting, SpellRelease, definitions, _facts, ReportCast,
                look =>
                {
                    // WeaponManager sends Attack to an action on the environment after the ordinary hit query. The safe
                    // Engine hit reports a static surface rather than a source id, so the normalized placement resolver
                    // is the product-side identity join.
                    _ = TryTriggerDungeonActionRay(look.Forward, _tuning.MeleeTargeting.MaximumDistance, DaggerfallDungeonActionEvent.Attack);
                    TryBashDoorWithSwing(look);
                });
            DaggerfallEnemySpellRelease enemySpells = new(Casting, SpellTargets, cast => _touch.Reach(cast), _contact, _spatial,
                () => _exterior.CharacterEnvironment(State.PlayerControl.Motion));
            _enemyMagic = new(definitions.EnemySpells, definitions.Magic, Casting, _random,
                id => authored.GetValueOrDefault(id),
                id => definitions.Mobiles.Mobiles.GetValueOrDefault(id), enemySpells.RangedPathClear, enemySpells.Execute);
            _enemyBehavior.BindEnemyMagic(_enemyMagic.Decide, _enemyMagic.HasRangedSpellReady);
            _itemCastTriggers = new(definitions.Magic, itemInstances, Casting, effects, actors.Entities,
                id => id == actors.Player.DurableId ? equipmentCoordinator : actors.TryGet(id, out var actor) && !actor.IsDefeated ? actorInventories.EquipmentFor(id) : null,
                _itemCondition, () => _time.Calendar.MinuteNumber, (owner, result) =>
                {
                    var definition = definitions.RequireItem(new DaggerfallItemId(result.Metadata.ItemId));
                    int? template = DaggerfallTemplateItemDefinitions.TemplateIndexForAuthoredItem(definition.Id) ?? definition.Template?.Index;
                    _facts.Append(new EquipmentWornFact(owner, result.DurableItemId, result.Metadata.ItemId,
                        DaggerfallTemplateItemDefinitions.BreaksInPlural(template), result.PreviousCondition, result.Metadata.CurrentCondition,
                        result.Outcome == DaggerfallItemConditionOutcome.Broken, _latestUpdateGeneration ?? 0, _latestSimulationStep ?? 0));
                }, enemyGroup: id => authored.TryGetValue(id, out var enemy) && enemy.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass
                    ? DaggerfallFormulaPolicy.EnemyGroupFor(enemy) : null,
                damageSource: (id, amount) => AppendEffectDamage(new(_vitality.ResolveHeldEnchantmentDamage(EffectActor(id), amount))),
                restoreSource: (id, amount) => _vitality.RestoreSpellTrack(EffectActor(id), TrackId.Parse("health"), amount),
                strikeTuning: tuning.StrikeEnchantments);
            _itemEnchanting.Enchanted += _itemCastTriggers.Enchanted;
            effects.Completed += _itemCastTriggers.EffectCompleted;
            _persistence = new(State, _corpseLoot, _groundContainers, _notebook, _uniqueItems, _camera, _time, _site, State.Effects, () => _doors, _locomotion, _climbing, State.Swimming, _dungeonText, propertyStorage.Capture, FastTravel.Position, authored, () => Casting.NextSequence);
            _persistence.EquipCountdown = () => _combat.EquipCountdown;
            if (saved is not null) _combat.EquipCountdown = saved.EquipCountdown;
            _roster = new DaggerfallActorRoster(actors, State.ActorInventories, State.Character, State.Containers, State.Effects, State.InventoryStore,
                State.ItemInstances, State.Npcs, definitions, _random, assembled.Mechanics, _actorIdentities, _uniqueItems,
                _authoredEntityIds, authored, saved?.DynamicActors ?? [], _grounding, () => _sites.Projection.Inputs, () => _sites.Projection.Appearance,
                _lootUi.CloseActor, _corpseLoot);
            _roster.ActorRetired += State.Quests.ObserveFoeRemoval;
            Crimes = new DaggerfallCrimes(State.Crime, State.Actors, State.Npcs, State.PlayerControl, _site, _roster.Definitions, definitions,
                State.ItemInstances, State.SkillUses, State.RacialOverrides, _random, engine.Perception, () => _spatial.Session,
                calendar: () => _time.Calendar,
                grantGold: grant => State.Currency.TrySpendGold(0, [grant]),
                provoke: target =>
                {
                    if (_enemyBehavior.IsPacified(target)) _enemyBehavior.MakeActiveEnemiesHostile();
                    _enemyBehavior.MakeHostile(target);
                });
            PropertyLoot = new DaggerfallPropertyLoot(_groundContainers, definitions, _random, _uniqueItems, Crimes, State.Crime,
                State.ItemInstances, State.Actors, State.Progression, State.Character, State.SkillUses, State.PlayerControl, _site,
                calendar: () => _time.Calendar,
                activeProfile: () => _activeProfileKey,
                placements: () => _sites.Projection.Inputs.PropertyContainers,
                building: CurrentInteriorBuilding,
                owned: Holdings.OwnsInterior);
            _dungeonPopulation = new DaggerfallDungeonPopulation(definitions, _random, _uniqueItems, _groundContainers, _grounding.RestingPosition, _encounters,
                (definition, pose, spawnDistance, passive) => _roster.Spawn(definition, pose, spawnDistance: spawnDistance, passive: passive),
                State.Character, () => State.Progression.Level);
            Civilians = new DaggerfallCivilians(State.Npcs, actors, () => _sites.Projection.Inputs, () => _appearance, _site.Require,
                point => _exterior.ProfileToLocal(point), (npc, pose) => _roster.MaterializeCivilian(npc, pose), id => _roster.UnloadActor(id),
                RetireActor, id => RetireDetachedActor(id, DaggerfallActorKinds.Civilian));
            Population = new DaggerfallSourcePopulation(State.Npcs, actors, State.PlayerControl, definitions, _tuning.CivilianWander, Civilians,
                () => _sites.Projection.Inputs, () => _sites.ActiveProfile, () => _appearance, () => _exterior.ActiveLocationLoaded,
                () => _exterior.ExteriorResidencyInitialized, point => _exterior.ProfileToLocal(point), point => _exterior.LocalToProfile(point),
                _site.Require, () => _time.Calendar.IsDay, () => State.RacialOverrides.Current?.SuppressPopulationSpawns == true,
                id => _reactions.Holds(id), (actor, policy, step, deltaSeconds) => _ = _enemyBehavior.UpdateCivilian(actor, policy, step, deltaSeconds));
            NpcProjection = new DaggerfallNpcProjection(State.Npcs, actors, () => _sites.Projection.Inputs, () => _appearance,
                () => _exterior.ActiveLocationLoaded, point => _exterior.ProfileToLocal(point));
            if (saved is null) _roster.GrantInitialAuthoredEquipment();
            CreateItemChoice = new DaggerfallCreateItem(definitions, _random, State.Inventory, State.ItemInstances, State.Encumbrance,
                _uniqueItems, State.Character, () => _time.Calendar, Presentation, saved?.PendingCreateItem);
            DispelChoice = new DaggerfallDispel(State.Effects, _random, Presentation, saved?.PendingDispel);
            _playSeconds = saved?.PlaySeconds ?? 0;
            IdentifyChoice = new DaggerfallIdentify(State.Inventory, State.ItemInstances, _itemCondition, definitions.Magic,
                State.Actors.Player.Stats, State.Actors.Player.Actor, _vitality, _random, _facts, Presentation,
                item => _inventoryUi.DescribeItem($"unique:{item.Entity.Value}", item.Definition.Value, 1, owner: DaggerfallItemOwner.Player).Label,
                saved?.PendingIdentify);
            _tutorial = new(definitions.Tutorial, saved?.Tutorial);
            State.Character.SpellForgotten+=key=>
            { if(Casting.ReadyFor(actors.Player.DurableId)?.SpellKey==key) Casting.Cancel(actors.Player.DurableId); };
            _persistence.Infections = Infections.Capture;
            _persistence.Weather = _weather.Capture;
            _persistence.WorldVariants = () => _sites.Profiles?.CaptureVariants() ?? [];
            Summoning = new(definitions, State.Npcs, State.ConcreteGuildServices, State.Services, State.Social, _random,
                composition.DisabledQuestSelection ?? DaggerfallDisabledQuestSelection.None, () => _time.Calendar,
                () => (IsRaining, IsStorming), () => _activeProfileKey,
                result => DaggerfallDaedricSummoning.PrepareQuest(definitions, State.Quests, result), saved?.Summoning);
            _persistence.Summoning = Summoning.Capture;
            _persistence.PoisonDraws = () => _poisoning.Draws;
            _persistence.ReadySpell=()=>Casting.ReadyFor(actors.Player.DurableId);
            _persistence.PendingCreateItem = () => CreateItemChoice.Pending;
            _persistence.PendingDispel = () => DispelChoice.Pending;
            _persistence.PendingTeleport = () => TeleportChoice.Pending;
            _persistence.PlaySeconds = () => _playSeconds;
            _persistence.TeleportAnchor = () => TeleportChoice.Anchor;
            _persistence.PendingIdentify = () => IdentifyChoice.Pending;
            _persistence.Tutorial = () => _tutorial.Progress;
            _roster.BanishedActors.UnionWith(saved?.BanishedActors ?? []);
            _persistence.BanishedActors = () => _roster.BanishedActors;
            DaggerfallInteriors interiors = new(actionTriggers, State.DungeonActions, State.DungeonDiscoveries,
                (key, admitted) => new DaggerfallDungeonActionGraph(key.LogicalId, admitted.DungeonActions, State.Variables.Write,
                    executeFamilyAction: ExecuteDungeonFamilyAction), _site);
            _sites = new DaggerfallSiteCrossing(engine, State, definitions, tuning, _time, _site, _spatial, _camera, audioBundles,
                _roster, _persistence, _groundContainers, _enemyBehavior, interiors, this,
                projection, profiles, activeProfile, saved?.Site.ReturnProfile?.Require(),
                composition.StartSite.ClassicPresentation);
            // The quests' foes, placements and items reach the roster, the site crossing and the persistence boundary; the
            // foes answer the teams the actor owners composed above ask of them from here on.
            _questItemPlacement = new DaggerfallQuestItemPlacement(State.Quests, State.QuestItems, State.ItemInstances, State.Actors, State.PlayerControl,
                _groundContainers, _uniqueItems, () => _sites.Deltas, _sites.ReplaceDelta, id => _corpseLoot.Corpses.ContainsKey(id), _corpseLoot.ReceiveItems);
            QuestPlacement = new DaggerfallQuestPlacement(State.Actors, State.Npcs, _site, point => _exterior.ProfileToLocal(point),
                point => _exterior.LocalToProfile(point), (point, distance) => _grounding.GroundPosition(point, distance),
                (definition, pose) => _roster.Spawn(definition, pose),
                (item, position) => _groundContainers.CreateQuestItem(item, position, _uniqueItems), _questItemPlacement.PlaceBound,
                NpcProjection.Project, BringOutRetainedQuestActor);
            QuestFoes = new DaggerfallQuestFoes(State.Quests, State.Actors, State.PlayerControl, State.Travel, _roster.Definitions, _placement, _vitality,
                tuning.QuestSpawning, tuning.EnemyBehavior, () => _sites.ReadQuestLocation(), (definition, pose) => _roster.Spawn(definition, pose),
                _roster.RemoveQuestActor, (id, allies) => _enemyBehavior.MakeHostile(id, includePlayerAllies: allies), _enemyBehavior.IsPacified,
                result => AppendDamage(result, DaggerfallDamageCause.Quest, 0), id => RetireDetachedActor(id), KillRetainedQuestFoe);
            TeleportChoice = new DaggerfallTeleport(this, State.PlayerControl, State.Transport, () => State.Property.OwnedShip,
                Presentation, saved?.PendingTeleport, saved?.TeleportAnchor);
            _grounding.FloorPending = () => _exterior.CollisionPending;
            if (composition.ArtReplacements is { } groundArt) _exterior.UseGroundArt(groundArt, () => DrawnArt is not null);
            State.LevelUps.Began += () => _appearance.PlayGameplaySound(96, "level-up");
            _equipmentMoves.Changed += change =>
            {
                if (change.Equipped is { } equipped)
                    _appearance.PlayEquip(definitions.RequireItem(new(equipped.Definition.Value)),
                        State.ItemInstances.RequireUnique(State.Inventory.GetDurableItemId(new(equipped.EntityId)).Value));
            };
            // Painted art wants its values untouched: this operator leaves everything below a highlight exact, so only
            // what emits above 1 rolls off, and it is what bloom spreads.
            engine.CameraView.SetToneMapping(new(ToneMappingOperator.Rolloff, 1));
            engine.CameraView.SetBloom(new(1, tuning.SiteLighting.Bloom));
            _weatherPresentation = new DaggerfallWeatherPresentation(engine, composition.Sky, tuning.Weather, tuning.Ambient, tuning.Horizon, tuning.PresentationAudio,
                (profile, clip) => audioBundles?.Require(profile).OpenClip(engine.Audio, clip));
            partiallyConstructed.Add(_weatherPresentation);
            DaggerfallHorizonGround ground = new(definitions.Terrain,
                (x, y) => definitions.Grids.ClimateAtWorldPixel(x, y).Value, tuning.Horizon, tuning.Ground, _exterior.Locations);
            _exterior.UseGround(ground, GroundWalkers);
            _horizon = new DaggerfallHorizon(engine, ground, tuning.Horizon, tuning.Water, tuning.Ground.RepeatMetres, definitions.Terrain.Rivers,
                new DaggerfallFloraField(engine, ground, tuning.Flora, tuning.Flats, tuning.Flora.ArchiveDensity(definitions.Grids.Climate.Values)),
                new DaggerfallFarForest(engine, ground, tuning.Flora, tuning.Flats, tuning.Flora.ArchiveDensity(definitions.Grids.Climate.Values)));
            partiallyConstructed.Add(_horizon);
            _exterior.DrawSeaWith(_horizon.AppendFacts, _horizon.CompleteSnapshot, _horizon.ReleaseSea);
            if (composition.ArtReplacements?.GroundCover is { } cover)
                _horizon.UseGroundCover(cover, tuning.GroundCover, DaggerfallGroundCover.TuftGrounds(definitions.Grids.Climate.Values),
                    new DaggerfallFloraField(engine, ground, tuning.Flora, tuning.Flats, new Dictionary<int, float>(),
                        (tuning.GroundCover.TownShare, tuning.GroundCover.TownRamp)));
            effects.Completed += outcome => _facts.Append(new MagicEffectFact(outcome));
            Law = new DaggerfallLaw(crime, State.Social, definitions, _tuning.Law, Crimes, State.Actors, State.Npcs, State.PlayerControl, _site,
                _placement, State.SkillUses, State.RacialOverrides,
                activeProfile: () => _activeProfileKey,
                calendar: () => _time.Calendar,
                passTime: seconds => AdvanceElapsedTime(seconds, idleFatigue: false),
                gold: () => State.Currency.Read().Gold,
                locationLoaded: () => _exterior.ActiveLocationLoaded,
                activeInputs: () => _sites.Projection.Inputs,
                profiles: () => _sites.Profiles,
                returnProfile: () => _sites.ReturnProfile,
                spawn: (definition, pose) => _roster.Spawn(definition, pose),
                makeHostile: id => _enemyBehavior.MakeHostile(id),
                retireCivilian: Civilians.Retire);
            crime.IncidentRecorded += Law.Admit;
            GuildProviders = new DaggerfallGuildProviders(State.ConcreteGuildServices, State.ConcreteGuildMembership, State.KnightlyClaimActions,
                new DaggerfallGuildAdmission(State.ConcreteGuildMembership, State.Social, State.SkillUses, State.Crime,
                    dialogueRevision: () => _activationPresentation.View.Dialogue?.Revision),
                State.Actors, State.Effects, State.Character, definitions, _random, _uniqueItems, State.Inventory, State.ItemInstances,
                calendar: () => _time.Calendar,
                noRegeneration: () => _rest.CharacterTraits(_passives.PlayerSpecials, _activeProfileKey.Kind).NoRegeneration,
                providerAvailable: State.Services.ProviderAvailable,
                openTeleport: OpenGuildTeleport,
                knightlyHouse: Holdings.AwardKnightlyHouse);
            _questWorldActions = new DaggerfallQuestWorldActions(_site, definitions, _notebook, () => _sites.Profiles, () => _sites.Projection.Inputs,
                _sites.TryRelocatePlayer, _sites.ClearReturnDestination);
            _questGuards = new DaggerfallQuestGuards(State.Actors, State.Npcs, State.PlayerControl, State.Crime, _roster.Definitions, _time,
                Crimes, Law, tuning.QuestSpawning, tuning.Law, () => _sites.ReadQuestLocation(), () => _activeProfileKey,
                () => _sites.Projection.Inputs, Civilians.Retire);
            QuestWork = new DaggerfallQuestWork(State.Quests, State.Npcs, State.Services, State.Social, State.Crime,
                (npc, building) => GuildProviders.TryResolve(npc, building, DaggerfallConcreteGuildService.Quests, out var guild, out _, out _) ? guild : null,
                definitions,
                State.Progression, _time, _site, tuning.QuestOffers, CurrentInteriorBuilding, () => _sites.ActiveProfile.Kind, InsideCastleZone);
            _vitality.PoisonDamageApplied += result=>AppendEffectDamage(new(result));
            _vitality.ConditionTrackLost += AppendSpellTrackLoss;
            _vitality.SpellTrackRestored += (target, track, requested, restored) =>
                _facts.Append(new SpellTrackRestoredFact(checked((long)actors.Entities.IdentityOf(target.Entity).Value),
                    track.Value, requested, restored));
            DialogueDirections = new DaggerfallDialogueDirections(_site, State.Npcs, definitions, State.PlayerControl,
                dialogue: () => _dialogue,
                activeProfile: () => _activeProfileKey,
                exteriorSitePosition: point => _exterior.ExteriorSitePosition(point),
                questSubjects: State.Quests.DialogueSubjects,
                questClaimsBuilding: State.Quests.ClaimsBuilding,
                questContacts: State.Quests.QuestContacts);
            InitializeActivation(engine, tuning.LootInteraction);
            _characterUi = new DaggerfallCharacterPresentation(definitions, State.Character, playerDefinition, equipmentCoordinator, State.LevelUps, State.Social, State.SkillUses);
            _characterUi.UseGuildMembership(State.GuildMembership, () => checked((int)_time.Calendar.DayNumber));
            _characterUi.UseLegalStanding(() => _site.Region, region => State.Crime.BanishedRegions.Contains(region));
            _characterUi.UseItemPresentation(_inventoryUi);
            _characterUi.UseArt(() => DrawnArt);
            _characterUi.UseRacialOverrides(State.RacialOverrides);
            _inventoryUi.UseArt(() => DrawnArt);
            NormalizedClassicPresentation classicPresentation = composition.StartSite.ClassicPresentation;
            if (classicPresentation.TryEffect("magicGlint", out NormalizedClassicEffect? magicGlint) && classicPresentation.TryEffect("coinGlint", out NormalizedClassicEffect? coinGlint))
                _inventoryUi.UseGlints(magicGlint!, coinGlint!);
            // The item lists draw both glints in either art set, so both are delivered up front.
            string[] glintMedia = [.. classicPresentation.Effects.Where(effect => effect.Name is "magicGlint" or "coinGlint").Select(effect => effect.MediaId)];
            _inventoryUi.UseWearer(() => State.Character.Identity);
            // The DOM's art comes from the derived content by media identity, so a session reads the
            // published closure once and publishes it to the UI that draws it.
            _hud = new DaggerfallHudProjection(
                engine.Ui,
                definitions.HudResources,
                composition.Identity,
                // The refreshed icons go with the classic ones, so choosing either art set needs no further delivery.
                composition.Data is not { } data ? DaggerfallUiArt.None : DaggerfallUiArt.Read(data, composition.StartSite.ClassicPresentation.InventoryIcons.Values
                    .Concat(composition.ArtReplacements is { } refreshed ? composition.StartSite.ClassicPresentation.InventoryIcons.Values
                        .Concat(DaggerfallSpellConstruction.RefreshableMedia())
                        .Select(refreshed.For).OfType<DaggerfallArtReplacement>().Select(replacement => replacement.MediaId) : []),
                    _definitions.CharacterPresentation.Races.Values.SelectMany(race => race.Layers.Where(layer => layer.Kind == DaggerfallCharacterLayerKind.Head)).Select(layer => layer.MediaId)
                    // The race's background is the inventory's paper-doll backdrop.
                    .Concat(_definitions.CharacterPresentation.Races.Values.Select(race => race.Background.MediaId))
                    .Concat(_definitions.CharacterPresentation.FactionFaces.Select(face => face.MediaId))
                    .Concat(_definitions.CharacterPresentation.ChildFaces.Select(face => face.MediaId))
                    .Concat(_definitions.CharacterPresentation.RacialForms.Values.SelectMany(form => new[] { form.HeadMediaId, form.BodyMediaId })),
                    DaggerfallTravelMap.MediaIds.Concat(glintMedia).Concat(composition.ArtReplacements is { } art
                        ? glintMedia.Where(art.Effects.ContainsKey).Select(DaggerfallArtReplacements.EffectMediaId) : []),
                    composition.ArtReplacements))
            {
                // The controls screen shows each action's controller buttons against the authored pad layout.
                PadActions = tuning.ControllerInput?.Actions ?? [],
                RefreshedArt = composition.ArtReplacements is not null,
                Help = definitions.Help,
            };
            partiallyConstructed.Add(_hud);
            if (restore is not null)
                _persistence.Restore(restore, _sites, _roster, _encounters, _heldEnchantments, RestoreDungeonText);
            foreach (var spell in definitions.Magic.Spells.Values.Where(spell => spell.IsCustom))
                if (Casting.AvailableSpellCost(actors.Player.DurableId, spell.Key) is null)
                    throw new ArgumentException($"Custom spell '{spell.Key}' contains an unavailable compiled construction effect or combination.");
            if (saved?.ReadySpell is { } readyKey) Casting.RestoreReadySpell(readyKey);
            ExpireConjuredItems();
            _roster.MaterializeStaticNpcs(inputs);
            _sites.Load(saved?.ExteriorResidency, saved?.ExteriorLocationResidency);
            // Treasure and dungeon enemies are placed on the floor under their markers, so the site's
            // collision must be admitted first: before it, a ground probe meets nothing and they float.
            ReconcilePropertyContainers();
            // A new game arrives at its start site's start like any other arrival; a restore resumes its saved pose.
            if (restore is null) _exterior.StandArrivingPlayer();
            // Registry positions are profile coordinates; project NPCs and the civilian population
            // only once the save is restored and the saved origin admitted, so dialogue and the
            // first snapshot share its frame and no restored presence is decided from the start pose.
            ReconcileNpcProjection();
            _itemCastTriggers.Refresh();
        }
        catch (Exception constructionFailure)
        {
            List<Exception> failures = [constructionFailure];
            try { DisposeAll(partiallyConstructed); }
            catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            try { _sites?.Exterior.RetireExteriorAppearance(); }
            catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            if (failures.Count == 1) throw;
            throw new AggregateException(failures);
        }
    }

    /// <summary>
    /// Builds the conversation for the active site with what it asks of the other domains, synchronizes its world news,
    /// and answers the owner of activating a person over it. Activation rebuilds it with each site.
    /// </summary>
    private IDaggerfallNpcActivationOwner ComposeDialogue()
    {
        _dialogue = new DaggerfallDialogueService(
            State.Npcs,
            State.Actors,
            State.Social,
            State.SkillUses,
            State.Actors.Player.Stats,
            _definitions,
            _random,
            () => _site.ActiveSite,
            () => State.Character.Identity,
            view => _activationPresentation.SetDialogue(view),
            message => Presentation.SetOutcome(message), DialogueDirections.SelectedBuilding, State.Quests.IsNpcMuted, State.Quests.QuestContacts,
            resolveDirection: DialogueDirections.Resolve,
            directionDirectory: DialogueDirections.Directory,
            questTopics: npcId => [.. State.Quests.DialogueTopics(npcId).Select(topic => new DaggerfallDialogueTopicOption(topic.Id, topic.Label))],
            questRumorCount: () => State.Quests.DialogueRumors().Count,
            resolveQuestRumor: index => State.Quests.ResolveDialogueRumor(State.Quests.DialogueRumors()[index]),
            resolveQuestTopic: ResolveQuestTopic,
            calendar: () => _time.Calendar,
            workAvailable: npc => QuestWork.HasWork(npc), offerWork: provider => QuestWork.Offer(provider),
            readVariable: address => State.Variables.Read(address),
            templeService: (npc, topic, amount) => State.TempleServices.Resolve(npc, CurrentInteriorBuilding(), topic switch
            {
                DaggerfallDialogueTopic.Donate => true,
                DaggerfallDialogueTopic.Cure => false,
                _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, "This dialogue topic is not a temple service."),
            }, amount),
            guildService: (npc, topic, key) => GuildProviders.Resolve(npc, CurrentInteriorBuilding(), topic, key),
            guildWelcome: npc => GuildProviders.Benefits(npc, CurrentInteriorBuilding()),
            currentText: CurrentTextContext,
            activeProfile: () => _sites.ActiveProfile,
            discloseDirection: DialogueDirections.Disclose,
            dialogueWorld: State.DialogueWorld, suppressTalk: () => State.RacialOverrides.Current?.SuppressTalk == true);
        _dialogue.SynchronizeWorldState();
        return new DaggerfallPersonActivationOwner(_dialogue, State.Actors, Crimes.IsPickpocketTarget, Crimes.Pickpocket, Law.IsGuard, SurrenderToGuard, id => State.Npcs.IsGameplayActive(id) && State.Quests.IsClickableActor(id),
            id => State.RacialOverrides.Current?.SuppressTalk != true && State.Quests.ActorClicked(id), PersonSightHeight);
    }

    private (string Text, IReadOnlyList<string> Diagnostics)? ResolveQuestTopic(long npcId, string topicId)
    {
        return State.Quests.TryResolveDialogueTopic(npcId, topicId,
            out string? text, out IReadOnlyList<string> diagnostics)
            ? (text!, diagnostics) : null;
    }

    private static DaggerfallSiteId? ToSiteId(DaggerfallSiteIdSave? id) => id?.Require();

    private static World.DaggerfallSiteReturnPose? ToSiteReturnPose(DaggerfallSiteReturnPoseSave? pose) => pose is null
        ? null
        : new World.DaggerfallSiteReturnPose(new WorldPoint(pose.X, pose.Y, pose.Z), pose.YawRadians, pose.PitchRadians);
}
