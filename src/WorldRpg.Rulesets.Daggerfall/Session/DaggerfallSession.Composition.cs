using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using Rusty.Engine;
using Rusty.Engine.Interaction;
using Rusty.Engine.Debugging;
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
    /// <param name="variants">
    /// The location variants a new game's opening quests selected as they started (<see cref="CreateNewGame"/>): the
    /// session's view of the catalog starts with them, and the start site is built with its own, as a restored game's is.
    /// </param>
    internal static DaggerfallSession StartNew(IEngineContext engine, DaggerfallSessionComposition composition, DaggerfallWorldVariantSave[]? variants = null)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ulong worldSocialSeed = composition.Tuning.Social.WorldSocialSeed;
        composition = composition with { Profiles = composition.Profiles?.ForSession(variants, worldSocialSeed) };
        DaggerfallSiteProfile start = composition.Profiles is { } profiles
            && (variants is { Length: > 0 } || profiles.HasCustomWorldSocialSeed)
            ? profiles.Require(composition.StartSite.ProfileKey)
            : composition.StartSite;
        return new DaggerfallSession(engine, composition, start, restore: null).AdmitComposition(engine, composition);
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
        ulong worldSocialSeed = raw.WorldSocialSeed;
        composition = composition with { Profiles = composition.Profiles?.ForSession(raw.WorldVariants, worldSocialSeed) };
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
        if (composition.Blocks is { } blocks) Site.AdmitBuildingNames(_random, composition.Definitions, blocks, State.Quests.LearnedBuildingName);
        return this;
    }

    /// <summary>
    /// Composes one session. A new game passes no restore; a resolved restore supplies the saved
    /// sections owners are constructed from and then the relational sections
    /// <see cref="DaggerfallSessionPersistence.Restore"/> applies in its one order.
    /// </summary>
    private DaggerfallSession(IEngineContext engine, DaggerfallSessionComposition composition, DaggerfallSiteProfile inputs,
        DaggerfallResolvedRestore? restore)
    {
        DaggerfallSavePayload? saved = restore?.Payload;
        DaggerfallDefinitions definitions = restore?.Definitions ?? composition.Definitions.ForSession([], DaggerfallSpellConstruction.ValidateDefinition);
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
            _drawn = new DrawnScene(engine.Graphics.ReadResourcePreparation);
            _newGameInitialized = saved is not null;
            _tuning = tuning;
            _viewLight = new(tuning.SiteLighting);
            Music = new DaggerfallMusic(engine, music,
                DaggerfallConcreteGuildCatalog.MagesFactionId, DaggerfallConcreteGuildCatalog.FightersTrainerFactionId);
            // A 3D viewmodel without its art draws the classic sprites; say why once rather than silently.
            if (tuning.Viewmodel.Style != DaggerfallViewmodelStyle.Classic && composition.HeldArt is null)
                engine.Diagnostics.Publish(new DiagnosticsPublishRequest(DiagnosticsSeverity.Warning, DiagnosticsDisposition.Accepted,
                    "daggerfall.viewmodel", "art.missing",
                    $"viewmodel.style is {tuning.Viewmodel.Style} but {DaggerfallHeldArt.ContainerName} is not installed, so the classic weapon sprites are drawn.",
                    string.Empty));
            _random = new DaggerfallGameRandom(engine.Random, saved?.GameSeed ?? composition.GameSeed);
            _worldSocialSeed = saved?.WorldSocialSeed ?? tuning.Social.WorldSocialSeed;
            _definitions = definitions;
            _buildingGreeting = new DaggerfallBuildingGreeting(definitions, _random);
            DungeonText = new DaggerfallDungeonText(new DaggerfallTextResolver(definitions.Text));
            _encounters = new DaggerfallEncounterRuntime(definitions, _random);
            Cinematics = composition.Data is null ? null : new DaggerfallCinematicPresentation(engine, composition.Data, definitions.Cinematics);
            if (Cinematics is not null) partiallyConstructed.Add(Cinematics);
            _openingCinematics = new DaggerfallOpeningCinematics(Cinematics, composition.VideosEnabled);
            // Owners are built in dependency order, each from the owners it reads; the named state is
            // constructed once every service it names exists, so nothing reads a service before it is built.
            bool materializeActiveSite = saved?.ExteriorLocationResidency?.Loaded != false;
            DaggerfallActorAssembly assembled = DaggerfallActorFactory.Create(_random, definitions, inputs,
                saved is null ? null : saved.ToActorSaves(inputs.ProfileKey),
                materializeActiveSite, composition.StartPose);
            ActorsState actors = assembled.Actors;
            partiallyConstructed.Add(actors);
            DaggerfallCharacterState character = assembled.Character;
            DaggerfallVariableStore variables = new(definitions.QuestSources.Tables.Globals.Lookup);
            if (saved?.Variables is { } restoredVariables)
                variables.Restore(restoredVariables.Entries.Select(entry => (entry.Require(), entry.Value)));
            DaggerfallSocialState social = new(definitions.Factions);
            if (saved?.Social is { } restoredSocial) social.Restore(restoredSocial);
            social.BindActorFactions(id => actors.TryGet(id, out ActorState? actor)
                && actor.Actor.TryGet<FactionComponent>(out FactionComponent? fc) ? fc : null);
            social.BindActorDisposition(id => assembled.ActorLedger.TryGet(id, out ActorLedgerEntry? entry)
                && entry?.Social is { } s ? s.DispositionDelta : 0);
            DaggerfallQuestInstances quests = new(definitions, _random, composition.QuestAdmission, composition.DisabledQuestSelection);
            DaggerfallQuestTrainingState questTraining = new(saved?.QuestTraining);
            DaggerfallItemInstances itemInstances = assembled.ItemInstances;
            DaggerfallQuestItemExits questItemExits = new(itemInstances, quests);
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
            _deathScreen = new DaggerfallDeathScreen(Presentation, ReportPlayerDefeatOutcome);
            _bundleChoice = new DaggerfallBundleChoice(Presentation);
            _quit = new DaggerfallQuit(Presentation);                                                // UiProjection
            _modeLine = new DaggerfallModeLine(Presentation);
            _time = new DaggerfallWorldTime(
                saved?.Calendar is { } restored
                    ? new DaggerfallCalendar(restored.Year, restored.Month, restored.Day, restored.Hour, restored.Minute, restored.Second)
                    : DaggerfallCalendar.Start with { Hour = definitions.NewGame.StartHour },
                saved?.Calendar?.RemainderSeconds ?? 0d,
                tuning.Time.GameSecondsPerRealSecond);
            DaggerfallWeatherState weather = new(_random, tuning.Weather, _time.Calendar, saved?.Weather);
            // New games use the bundle site; current saves carry their explicit site state.
            _site = saved?.Site is { } restoredSite
                ? new World.DaggerfallSiteContext(
                    definitions.Locations,
                    ToSiteId(restoredSite.Active),
                    ToSiteId(restoredSite.ReturnAnchor),
                    restoredSite.ReturnPose?.Restore(),
                    restoredSite.Discovered.Select(id => id.Require()), restoredSite.Buildings, restoredSite.FailedBuildingPicks)
                : new World.DaggerfallSiteContext(definitions.Locations, inputs.Site, null, []);
            LocalWeather = new DaggerfallWeather(weather, (x, y) => definitions.Grids.ClimateAtWorldPixel(x, y).Value,
                () => _sites?.Exterior is { ExteriorResidencyInitialized: true } exterior ? exterior.CurrentExteriorCell() : null,
                () => _site.ActiveSite is { } site ? new(site.MapPixelX, site.MapPixelY) : null);
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
            _soundCover = new DaggerfallSoundCover(_spatial, _tuning.Camera.EyeHeight);
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
                    action => DungeonActions.Execute(action)));
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
            DaggerfallSiteProjection projection = DaggerfallSiteProjection.Create(engine, actors.Entities, _random, tuning.Site, _time.Calendar,
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
                foreach (ActorState actor in actors.All.Where(actor => definitions.Walks(authored[actor.DurableId])))
                {
                    _grounding.Ground(actor);
                    if (actor.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? originComp))
                        originComp.SetBaselinePose(actor.Position);
                }
            }
            _camera = new FirstPersonCameraSystem(engine.CameraView, assembled.PlayerControl, tuning.Camera);
            partiallyConstructed.Add(_camera);
            DaggerfallActorSight sight = new(authored, definitions, () => _sites.Projection.Inputs, (float)tuning.Camera.EyeHeight,
                _spatial.CurrentController.Shape.StandingHeight * .5F, id => _sight.PersonHeight(id));
            TargetingService targeting = new(engine.Perception, _spatial, actors,
                new DaggerfallTargetingPolicy(authored, tuning.MeleeTargeting, sight, npcs.IsGameplayActive));
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
                .. DaggerfallRestorationEffects.Definitions(_random,
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
            DaggerfallQuestEnvironment questEnvironment = new(definitions, _time, questDiseases, Music.Director, Music.Bundle, composition.VideosEnabled,
                Cinematics, () => _sites.Projection.Inputs.Audio, () => LocalWeather.Current, () => LocalWeather.Climate,
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
            quests.BindTravelMinutes(site => MapPosition.QuestLegMinutes(site));
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
            _placement = new DaggerfallActorPlacement(actors, assembled.PlayerControl, npcs.IsGameplayActive, authored, id => QuestFoes.TeamOverride(id),
                id => _enemyBehavior!.IsPacified(id), id => quests.AllowsFoeInfighting(id), id => quests.IsFoeRestrained(id),
                id => quests.FoeRelationsFor(id)?.Infighting == true, () => quests.HasInfightingFoes, _spatial, engine.Perception, _random,
                () => _exterior.CharacterEnvironment(assembled.PlayerControl.Motion), _tuning.EnemyBehavior.DetectionDistance,
                (float)_tuning.Camera.FieldOfViewYDegrees, sight, definitions.CreatureFactions,
                isUnderLairTruce: id => _enemyBehavior!.IsUnderLairTruce(id, assembled.PlayerControl.Position, _time.Calendar.MinuteNumber),
                liftLairTruce: id => _enemyBehavior!.LiftLairTruce(id),
                joinsFactionInfighting: id => _enemyBehavior!.JoinsFactionInfighting(id),
                hasFactionInfightingPotential: () =>
                {
                    if (_enemyBehavior!.HasFactionInfightingActors())
                        return true;
                    return _site.Region is int r
                        && WorldConditions is not null
                        && DaggerfallCreatureFactionPolicy.HasClashDrivingCondition(
                            WorldConditions.ActiveForRegion(r, checked((int)_time.Calendar.DayNumber)));
                },
                boardConditionDrivesClash: (teamA, teamB) =>
                    _site.Region is int r
                    && WorldConditions is not null
                    && DaggerfallCreatureFactionPolicy.IsConditionDrivingClash(
                        definitions.CreatureFactions,
                        WorldConditions.ActiveForRegion(r, checked((int)_time.Calendar.DayNumber)),
                        teamA,
                        teamB),
                isProvoked: id => _enemyBehavior!.IsProvoked(id, _time.Calendar.MinuteNumber));
            _senses = new DaggerfallEnemySenses(actors, assembled.PlayerControl, definitions, authored, id => QuestFoes.TeamOverride(id),
                id => _enemyBehavior!.IsPacified(id), () => _enemyBehavior!.LastPerception, effects, _locomotion,
                () => State!.WeaponDrawn, () => _combat!.PlayerHoldsBow(), () => _time.Calendar.MinuteNumber, _random,
                () => _activeProfileKey, _encounters);
            WorldEncounters = new DaggerfallWorldEncounters(_encounters, _placement, definitions, assembled.PlayerControl,
                () => actors.Player.Progression.Level, () => State!.Swimming.IsSwimming, () => State!.Transport.OnShip, () => _activeProfileKey,
                () => _site.TryFind(_activeProfileKey.Site, out DaggerfallSiteRecord site) ? site : null,
                () => { DaggerfallExteriorCellId cell = _exterior.CurrentExteriorCell(); return (cell.X, cell.Y); },
                () => _exterior.LocationAtPlayer() is not null, () => _latestUpdateGeneration, (definition, pose, level) => SpawnActor(definition, pose, level),
                id => actors.TryGet(id, out var actor) && actor is not null ? actor.Pose : null, id => _enemyBehavior!.Provoke(id, _time.Calendar.MinuteNumber),
                findBandLeader: (bandId, gen) =>
                {
                    foreach (ActorState candidate in actors.All)
                        if (candidate.Actor.TryGet<DaggerfallEncounterBandMember>(out var member) && member.BandId == bandId && member.Generation == gen)
                            return candidate.Pose;
                    return null;
                },
                tagBandMember: (actorId, bandId, gen, condTokenId) =>
                {
                    if (actors.TryGet(actorId, out ActorState? spawned))
                    {
                        spawned.Actor.Add(new DaggerfallEncounterBandMember(bandId, gen, condTokenId));
                        DaggerfallEnemyPerceptionMemory mem = spawned.Actor.Get<DaggerfallEnemyPerceptionMemory>();
                        mem.LairTruce = false;
                        mem.FactionInfighting = true;
                    }
                });
            _nearbySpawn = new DaggerfallNearbySpawn(definitions.Actors, mobile => _sites.Projection.Inputs.MobileSprites.ContainsKey(mobile), _placement,
                assembled.PlayerControl, (actor, pose) => SpawnActor(actor, pose),
                id => { _enemyBehavior!.MakeHostile(id); _enemyBehavior.Provoke(id, _time.Calendar.MinuteNumber); }, line => Presentation.SetOutcome(line));
            CreatureClashes = new DaggerfallCreatureFactionClashes(
                definitions,
                _placement,
                mobile => _sites.Projection.Inputs.MobileSprites.ContainsKey(mobile),
                (actor, pose, lairTruce, factionInfighting) => SpawnActor(actor, pose, lairTruce: lairTruce, factionInfighting: factionInfighting));
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
                CanDeflect = id => id == actors.Player.DurableId
                    || !actors.TryGet(id, out ActorState target)
                    || !target.Actor.TryGet<ActorHarmPolicy>(out ActorHarmPolicy? harm)
                    || harm.CanDeflectAttacks,
            };
            _guard = new DaggerfallContactGuard(contactInputs, _tuning.Contact);
            _condition = new DaggerfallPlayerCondition(_vitality, _tuning.Attacks.Stamina, _tuning.Rest, _random, DaggerfallRandomKey.Seed);
            _condition.Stamina.Refresh(actors.Player.Stats);
            _touch = new DaggerfallContactTouch(new(_contact, _spatial, () => _sites?.Exterior.CharacterEnvironment(assembled.PlayerControl.Motion)), _tuning.Contact);
            WildNavigation = new DaggerfallWildNavigation(engine.Spatial, _spatial, tuning.WildNavigation);
            ActorNavigationCoordinator navigation = new(engine.Spatial, _spatial.Session, actors.Store, _spatial.CurrentController,
                actor => _exterior.CharacterEnvironment(actor.Actor.Store.Get(actor.Actor.Entity, EngineComponentTypes.CharacterMotion)),
                () => WildNavigation.Grids(_sites.Projection.Inputs.Geometry.NavigationGridId), WildNavigation.WalkerOffEveryGrid);
            Navigation = navigation;
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
                actor => actors.TryGet(actor, out ActorState target) && target.Actor.TryGet<ActorHarmPolicy>(out ActorHarmPolicy? harm) && harm.IsLawProtectedCivilian);
            StrikeFeel = new DaggerfallStrikeFeel(_tuning.Attacks.Strike, _camera, () => _engine.GameplayTime.Read().Rate);
            _combat = new DaggerfallCombatRules(_random, actors, equipmentCoordinator, actorInventories.InventoryFor, itemInstances, definitions, authored, targeting, use => skillUses.Record(use),
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
                motorContext: _senses.MotorContext, selectTarget: _placement.SelectTarget,
                isPlayerAllied: id => QuestFoes.Team(id) == "player-ally",
                controlRestrictions: effects.ControlsFor, reactions: _reactions,
                movementPolicy: id => DaggerfallEnemyBehaviorModule.PolicyFor(
                    id,
                    authored,
                    definitions,
                    tuning.EnemyBehavior.Maneuvers,
                    effects.GrantsWaterWalking(id),
                    effects.GrantsLevitation(id)),
                canOpenDoors: id => DaggerfallEnemyBehaviorModule.CanOpenDoors(id, authored, definitions),
                openBlockedDoor: actor => _doors.TryOpenForEnemy(actor.Position), questRestrained: id => QuestFoes.Restrained(id), attackAtDistance: _combat.EnemyAttackAtDistance,
                gameMinute: () => _time.Calendar.MinuteNumber, sight: sight);
            HashSet<ulong> authoredEntityIds = DaggerfallActorFactory.AdmittedAuthoredEntityIds(inputs, playerDefinition.Loadout);
            if (restore is null)
            {
                ulong[] actorReservations = [DaggerfallActorIdentity.PlayerEntityId, .. inputs.Project.Actors.Values.Select(placement => checked((ulong)placement.EntityId))];
                _actorIdentities = DurableIdentityAllocator.Restore(new DurableIdentityState(
                [
                    new KindAllocatorState(DurableIdentityKind.Actor, DaggerfallActorRoster.DynamicFirstIdentity, actorReservations, []),
                    new KindAllocatorState(DurableIdentityKind.Item, DaggerfallUniqueItemAllocator.DefaultFirstEntityId, [.. authoredEntityIds], []),
                    new KindAllocatorState(DurableIdentityKind.Container, DaggerfallGroundContainers.FirstIdentity, [], []),
                ]));
            }
            else
            {
                // The ledger ResolveRestore rebuilt to validate the save is the session's live allocator.
                _actorIdentities = restore.Identities;
            }

            _uniqueItems = DaggerfallUniqueItemAllocator.Sharing(_actorIdentities, authoredEntityIds);
            npcs.Identities = _actorIdentities;
            // Poisons are active effects, so restoring them is the effect lifecycle's own restore; the
            // admission keeps only its draw ordinal.
            _poisoning = new DaggerfallPoisoning(effects, _random, actors, character, itemInstances, authored, definitions,
                combatRules, saved?.PoisonDraws ?? 0);
            DaggerfallPoisonRuntime poisons = _poisoning.Runtime;
            // The curses an infection becomes; a curse's cure passes an hour, through quest time when a quest cures it.
            DaggerfallTransformationHealing transformationHealing = new(_diseases, _poisoning, effects, actors, character);
            DaggerfallAfflictionQuests afflictionQuests = new(quests, definitions, actors, social, character);
            _lycanthropy = new DaggerfallLycanthropy(racialOverrides, _time, tuning.Lycanthropy,
                () => _heldEnchantments.HircinesRingEquipped, _equipmentMoves.UnequipHands, actors, _random, Presentation, afflictionQuests, transformationHealing,
                seconds => AdvanceElapsedTime(seconds), seconds => AdvanceQuestTime(seconds));
            _vampirism = new DaggerfallVampirism(racialOverrides, _time, tuning.Vampirism, definitions.Factions,
                _random, Presentation, afflictionQuests, transformationHealing,
                seconds => AdvanceElapsedTime(seconds), seconds => AdvanceQuestTime(seconds));
            DaggerfallEncumbrancePolicy encumbrance = new(inventory, playerStats,
                () => _heldEnchantments.CarryMultiplier);
            // A new game wears its loadout now; a restore refreshes once, after its equipment is restored.
            if (restore is null) _heldEnchantments.Refresh();
            DaggerfallCurrencyService currency = new(definitions, inventory, itemInstances, encumbrance, _uniqueItems, containers, saved?.Currency);
            DaggerfallRegionalBankState bank = new(currency, inventory, itemInstances, saved?.Bank);
            DaggerfallLoanState loans = new(saved?.Loans);
            DaggerfallPropertyState property = new(tuning.Property, saved?.Property);
            DaggerfallPropertyStorage propertyStorage = new(containers, itemInstances, definitions, _actorIdentities, property, playerEntity, questItemExits);
            if (saved?.Property is { } savedProperty) propertyStorage.Restore(savedProperty);
            DaggerfallCrimeState crime = new(saved?.Crime, () => racialOverrides.Current?.SuppressCrime == true);
            WorldConditions = new DaggerfallWorldConditions();
            if (saved is not null)
                WorldConditions.Restore(saved.WorldConditions);
            else
                WorldConditions.SeedAuthored(definitions.NewGame.WorldConditions);
            WordSpread = new DaggerfallWordSpread(tuning.Social, WorldConditions, travelPolicy);
            if (saved is not null)
                WordSpread.Restore(saved.WordSpread);
            WorldConditions.BeforeMutation += () => WordSpread.CheckpointAll(_time.Calendar.MinuteNumber);
            social.GuildRankChanged += (factionId, newRank) =>
            {
                if (_site.ActiveSite is not { } origin) return;
                string? factionName = definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? fDef)
                    ? fDef.Name
                    : null;
                string? rankTitle = newRank > 0
                    ? DaggerfallGuildRankTitles.Title(factionId, newRank, character.Identity.Gender == DaggerfallCharacterGender.Female)
                    : null;
                string? headline = newRank > 0 && !string.IsNullOrWhiteSpace(rankTitle)
                    ? $"{factionName ?? "Guild"} Promotion: {rankTitle}"
                    : null;
                string? body = newRank > 0 && !string.IsNullOrWhiteSpace(rankTitle)
                    ? $"Word from {origin.Name}: {character.Identity.Name} now holds the rank of {rankTitle} in {factionName ?? "the guild"}."
                    : null;
                WordSpread.RecordFactionStanding(factionId, delta: 0, guildRank: newRank, origin, _time.Calendar.MinuteNumber, headline, body);
            };
            social.FactionStandingChanged += (factionId, amount) =>
            {
                if (_site.ActiveSite is not { } origin) return;
                WordSpread.RecordFactionStanding(factionId, delta: amount, guildRank: null, origin, _time.Calendar.MinuteNumber);
            };
            LegalServices = new DaggerfallLegalServicePolicy(social, crime, assembled.ActorLedger, definitions,
                id => actors.TryGet(id, out ActorState? actor) && actor.Actor.TryGet<FactionComponent>(out FactionComponent? fc) ? fc : null,
                CurrentInteriorBuilding,
                building => _site.TryFind(_activeProfileKey.Site, out _)
                    ? _site.RequireBuildingSource(_activeProfileKey.Site, new(building.BlockX, building.BlockY, building.Building.Index)).Quality
                    : 10);
            Scrutiny = new DaggerfallScrutinyPolicy(
                social, crime, LegalServices, assembled.ActorLedger, WorldConditions, definitions,
                () => checked((int)_time.Calendar.DayNumber),
                id => actors.TryGet(id, out ActorState? actor) && actor.Actor.TryGet<FactionComponent>(out FactionComponent? fc) ? fc : null,
                CurrentInteriorBuilding,
                HasWatchOrRulerVouch,
                wordSpread: WordSpread,
                currentMinute: () => _time.Calendar.MinuteNumber,
                activeSite: () => _site.ActiveSite);
            DaggerfallServiceTransactions services = new(npcs, social, inventory, itemInstances,
                currency, _uniqueItems, containers, playerEntity, () => _time.Calendar, () => _site.ActiveSite is { } active
                    ? new DaggerfallNpcSite(active.Id.Region, active.Name, string.Empty, _sites.ActiveProfile.LogicalId)
                    : null, saved?.Services);
            services.BindServiceScrutinyPolicy(npc => Scrutiny.EvaluateServiceScrutiny(npc).Denial);
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
            DaggerfallConditionPrices conditionPrices = new(WorldConditions, () => checked((int)_time.Calendar.DayNumber));
            DaggerfallTradeQuoteService tradeQuotes = new(definitions, new DaggerfallItemValuation(definitions), regionalPrices,
                conditionPrices.GoodsAdjustment);
            DaggerfallMerchantService merchants = new(definitions, _random, npcs, social, playerStats, character, progression,
                inventory, containers, itemInstances, _uniqueItems, _actorIdentities, currency, services, concreteGuildServices, tradeQuotes,
                regionalPrices, _itemCondition, skillUses, crime, questItemExits, () => _time.Calendar,
                () => _dialogue?.CurrentNpc()?.Site,
                saved?.Merchants, () => Crimes.QueryWitnesses(),
                (op, kind, victim, raw) => Crimes.EvaluateWitnesses(op, kind, victim, raw).ReportingEvidence,
                conditionPrices.MagicService);
            DaggerfallTransportPolicy transport = new(tuning.Transport);
            DaggerfallWagonStorage wagon = new(containers, itemInstances, definitions,
                playerEntity, _actorIdentities, tuning.Transport, questItemExits);
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
            _restPlaces = new DaggerfallRestPlaces(State.Lodging, State.Property, State.GuildMembership);
            _movement = new DaggerfallPlayerMovement(_input, _spatial, _locomotion, _climbing, _levitation, State.Swimming, State.PlayerControl,
                State.Actors, State.Character, State.SkillUses, State.Effects, State.Transport, _random, _tuning.Locomotion, _tuning.Time.GameSecondsPerRealSecond,
                Presentation);
            // An enemy hit on the player is in flight while Combat holds its impact or a site appearance still times it.
            _actTiming = new DaggerfallPlayerActTiming(() => _combat.HoldsImpactAt(DaggerfallActorIdentity.PlayerEntityId)
                || _sites.Appearances.Any(appearance => appearance.HasPendingEnemyHitTarget(DaggerfallActorIdentity.PlayerEntityId)), InputRefusals);
            _creation = new DaggerfallCharacterCreation(State.Character, definitions, _random);
            _atlasTiles = composition.Data is { } atlasData && DaggerfallAtlas.Read(atlasData) is { } atlas ? new(atlas, atlasData, engine.Ui) : null;
            MapPosition = new DaggerfallMapPosition(travelPolicy, State.Transport, _site, _random,
                new DaggerfallExteriorWorldBounds(definitions.Terrain.Width, definitions.Terrain.Height),
                activeProfile: () => _activeProfileKey,
                exteriorCell: () => _exterior.CurrentExteriorCell());
            Expedition = new DaggerfallExpedition(travelPolicy, _site, tuning.Expedition, this, saved?.Expedition, definitions.Encounters,
                definitions.Mobiles, definitions.CreatureFactions);
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
                building: CurrentInteriorBuilding,
                isLegallyDenied: (region, building) => Scrutiny.IsRoomRentalDenied(region, building, _dialogue?.CurrentNpc()),
                tradeReaction: (region, building) =>
                {
                    if (_dialogue?.CurrentNpc() is { } keeper)
                        return State.Social.TradeReactionForNpc(keeper);
                    if (building.FactionId > 0 && definitions.Factions.Factions.ContainsKey(building.FactionId))
                        return Math.Clamp(State.Social.ReactionForFaction(building.FactionId).Value,
                            DaggerfallRegionalEconomyPolicy.MinimumReaction, DaggerfallRegionalEconomyPolicy.MaximumReaction);
                    if (DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, region) is { } people)
                        return Math.Clamp(State.Social.ReactionForFaction(people.Id).Value,
                            DaggerfallRegionalEconomyPolicy.MinimumReaction, DaggerfallRegionalEconomyPolicy.MaximumReaction);
                    return 0;
                });
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
                State.Inventory, State.ItemInstances, _itemEnchanting, ItemSouls.Gems, _uniqueItems, () => _time.Calendar,
                conditionPrices.MagicService);
            _potionMaker = new DaggerfallPotionMaking(definitions, State.Npcs, State.ConcreteGuildServices, State.Inventory,
                State.Containers, State.Wagon, State.ItemInstances, _random, () => _time.Calendar);
            SpellSales = new DaggerfallSpellSales(definitions, State.Npcs, State.ConcreteGuildServices, State.Services,
                State.Character, State.Inventory, State.Actors.Player.Stats,
                key => Casting.AvailableSpellCost(State.Actors.Player.DurableId, key), () => _time.Calendar,
                npc => State.Social.TradeReactionForNpc(npc), conditionPrices.MagicService);
            SpellMaker = new DaggerfallSpellMaker(definitions, State.Effects.Catalog, State.Npcs, State.ConcreteGuildServices,
                State.Services, State.Character, State.Inventory, State.Actors.Player.Stats, () => _time.Calendar,
                conditionPrices.MagicService);
            OfficeHolders = new DaggerfallOfficeHolders(definitions, _site, State.ActorLedger, State.Npcs);
            OfficeLedger = new DaggerfallOfficeLedger(
                definitions, OfficeHolders, State.ActorLedger, State.Npcs,
                () => _worldSocialSeed, new DaggerfallNames(definitions, _random).NpcName,
                () => _time.Calendar.MinuteNumber, _site);
            OfficeLedger.Restore(saved?.OfficeLedger);
            OfficeHolders.BindOverrideLookup(OfficeLedger.TryGetFactionOverride);
            OfficeLedger.OfficeChanged += change => WordSpread.ObserveOfficeChanged(
                change,
                n => OfficeLedger.ResolveOfficeOriginSite(n, _site.ActiveSite),
                definitions.BuildingNames.RegionName);
            OfficeLedger.OfficeChanged += change => WorldConditions.ObserveOfficeChanged(
                change,
                definitions.BuildingNames.RegionName);
            OfficeLedger.OfficeOverrideCleared += WorldConditions.ObserveOfficeOverrideCleared;
            NoticeBoard = new DaggerfallNoticeBoard(
                definitions, WorldConditions, OfficeHolders, OfficeLedger, State.Crime, State.Social,
                () => checked((int)_time.Calendar.DayNumber), CurrentTextContext, () => State.Character.Identity,
                wordSpread: WordSpread);
            CourierDispatches = new DaggerfallCourierDispatches(
                definitions, WordSpread, WorldConditions, State.Social, State.ActorLedger,
                amount => amount <= 0 || State.Currency.TrySpendCarried((ulong)amount),
                amount => { if (amount > 0) _ = State.Currency.ReceiveGold((ulong)amount); },
                () => _worldSocialSeed,
                new DaggerfallNames(definitions, _random).NpcName,
                () => OverlandParties,
                () => Expedition);
            if (saved is not null)
                CourierDispatches.Restore(saved.CourierDispatches);
            Introductions = new DaggerfallIntroductions(
                definitions, State.Social, OfficeHolders, State.Actors, State.Npcs, State.ActorLedger,
                State.SkillUses, _random, () => _site.ActiveSite, () => checked((int)_time.Calendar.DayNumber), InsideCastleZone,
                officeLedger: OfficeLedger,
                findSite: id => _site.TryFind(id, out DaggerfallSiteRecord record) ? record : null);
            State.Social.BindActorVouchBonus(Introductions.TrustedVouchReactionBonus);
            State.Quests.BindQuestorDisposition((actorId, delta) => Introductions.AdjustActorDisposition(actorId, delta));
            _questTexts = new DaggerfallQuestTextContext(definitions, _site, State.Npcs, State.ItemInstances,
                item => DaggerfallInventoryPresentation.DescribeCreatedItem(definitions, item), CurrentTextContext, CurrentInteriorBuilding, _random,
                faction => State.Social.GuildEligibility(faction) is { IsMember: true } membership
                    ? DaggerfallGuildRankTitles.Title(faction, membership.Rank, State.Character.Identity.Gender == DaggerfallCharacterGender.Female) : null,
                (place, building) => DialogueDirections.QuestPlace(place, building), OfficeHolders);
            State.Quests.BindTextContext(_questTexts.For);
            State.Quests.BindCalendar(() => _time.Calendar);
            DaggerfallNames questNames = new(definitions, _random);
            npcs.BindNames(npc => questNames.NpcName(npc.Appearance, npc.Site.Region, npc.FamilyId, _worldSocialSeed));
            var questPlaces = new DaggerfallQuestPlaceAllocator(definitions, _site, _random,
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
                _actorIdentities,
                feet => sight.Eye(DaggerfallActorIdentity.PlayerEntityId, feet));
            _itemOwnership = new DaggerfallItemOwnership(State.Actors, State.InventoryStore, actor => _corpseLoot.Corpses[actor].Owner);
            _itemLifetime = new DaggerfallItemLifetime(State.Actors.Entities, State.InventoryStore, State.ItemInstances, _uniqueItems, _itemOwnership);
            _groundContainers = new DaggerfallGroundContainers(containers, State.ItemInstances, playerEntity, _actorIdentities, activeProfile, questItemExits);
            LooseItems = new DaggerfallLooseItems(engine.Dynamics, _tuning.LooseItems);
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
            _inventoryUi.BindItemExits(questItemExits);
            State.Quests.BindResourceAllocator(new(definitions, _random, new DaggerfallItemFactory(definitions, _random), questNames,
                () => new(State.Progression.Level, State.Character.Identity.RaceId,
                    State.Character.Identity.Gender.ToString().ToLowerInvariant(), _site.ActiveSite?.Region
                        ?? throw new NotSupportedException("Quest resource generation requires the current region.")),
                State.Social.GuildEligibility, State.RegionalPrices.AdjustmentForRegion, _inventoryUi.DescribeCreatedItem));
            State.Quests.BindPersonAllocator(new(definitions, _random, questNames,
                instance => new(_site.ActiveSite ?? throw new NotSupportedException("Quest Person selection requires the current site."),
                    instance.QuestorId is long giver ? State.Npcs.Require(giver) : null,
                    PermanentPlayerClanFactionId: State.RacialOverrides.Current?.State.Vampire?.Clan, CurrentProfile: _sites.ActiveProfile,
                    Interior: _sites.Projection.Inputs.InteriorBuilding),
                State.Npcs.SetDisplayName, questPlaces, OfficeHolders));
            _inventoryUi.UseBank(State.Bank, () => Banking.ActiveRegion, () => State.Bank.DepositableGold(State.Wagon, TransportAccess()));
            _inventoryUi.UseLoans(State.Loans, () => _time.Calendar, () => State.Progression.Level);
            _inventoryUi.UseItemValuation(new DaggerfallItemValuation(definitions), State.ItemInstances, DaggerfallItemOwner.Player,
                entity => State.Actors.Entities.IdentityOf(new Rusty.Engine.Entities.EntityId(entity)).Value);
            _inventoryUi.UseItemCondition(_itemCondition);
            // A dropped item is let go above the floor under the player and tossed ahead, so it falls before the player and settles.
            _inventoryUi.UseGroundDrops(_groundContainers, () => State.PlayerControl.Position is WorldPoint at
                    ? WorldPoint.From(_grounding.RestingPosition(at).ToVector() + Vector3.UnitY * _tuning.LooseItems.DropHeight) : null,
                id => LooseItems.Drop(id, Toss()));
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
            State.Quests.BindGuildInvitationAdmission(factionId =>
                State.ConcreteGuildMembership.AdmitByInvitation(factionId, checked((int)_time.Calendar.DayNumber)));
            State.Quests.BindWorldRead(() => _sites.ReadQuestLocation());
            State.Quests.BindWorldActions((instance, operation) => _questWorldActions.Apply(instance, operation));
            State.Quests.BindFoeCommands((id, remove) => QuestFoes.Command(id, remove));
            State.Quests.BindFoeSpawning((operation, foe, send) => QuestFoes.Spawn(operation, foe, send));
            State.Quests.BindEnemyRelations(clear => QuestFoes.CommandAllEnemies(clear), id => actors.TryGet(id, out var actor) && !actor.IsDefeated);
            State.Quests.BindGuardSpawning((operation, immediate, elapsed, state) => _questGuards.Spawn(operation, immediate, elapsed, state));
            State.Quests.BindRewards((instance, symbol) => _questItemPlacement.OfferReward(instance, symbol), pile => _lootActions.PresentQuestReward(pile), tuning.QuestRewards);
            _inventoryUi.UseItemActions(new DaggerfallInventoryUseService(State.Inventory, definitions, State.ItemInstances, _uniqueItems, MapPosition.RevealFromMap, _itemCondition, _notebook,
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
                dungeonTextOpen: () => DungeonText.Projection is not null,
                dialogueOpen: () => _activationPresentation.View.Dialogue is not null,
                characterCreationOpen: () => State.Character.Pending is not null,
                levelUpOpen: () => State.LevelUps.Pending is not null,
                bankOpen: () => Banking.ActiveRegion is not null, dispelOpen: () => DispelChoice.Pending is not null, identifyOpen: () => IdentifyChoice.Open, teleportOpen: () => TeleportChoice.Pending is not null, createItemOpen: () => CreateItemChoice.Pending is not null, legalOpen: () => Law.ModalOpen,
                tutorialOpen: () => _tutorial.Shown, expeditionOpen: () => Expedition.Open, worldMapOpen: () => _travelPanel?.WorldMapOpen == true);
            itemInstances.StackChanged += State.Quests.ObserveStackChange;
            itemInstances.Transferred += questItemExits.Moved;
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
                () => _sites?.Exterior.CharacterEnvironment(State.PlayerControl.Motion), Casting, SpellRelease, definitions, _facts, outcome => Spellbook.ReportCast(outcome),
                look =>
                {
                    // WeaponManager sends Attack to an action on the environment after the ordinary hit query. The safe
                    // Engine hit reports a static surface rather than a source id, so the normalized placement resolver
                    // is the product-side identity join.
                    _ = DungeonActions.TriggerRay(look.Forward, _tuning.MeleeTargeting.MaximumDistance, DaggerfallDungeonActionEvent.Attack);
                    TryBashDoorWithSwing(look);
                }, InputRefusals);
            DaggerfallEnemySpellRelease enemySpells = new(Casting, SpellTargets, cast => _touch.Reach(cast), _contact, _spatial,
                () => _exterior.CharacterEnvironment(State.PlayerControl.Motion));
            _enemyMagic = new(definitions.EnemySpells, definitions.Magic, Casting, _random,
                id => authored.GetValueOrDefault(id),
                id => definitions.Mobiles.Mobiles.GetValueOrDefault(id), enemySpells.LaneClear, enemySpells.Execute);
            // Every mobile fights with the tactic its record names; the legacy tactic is the only one compiled yet.
            DaggerfallContactEngagement engagement = new(_tuning.Contact);
            DaggerfallScoredEnemyTactic Scored(DaggerfallScoredPreset preset) => new(preset, _combat.EnemyAttackOptions, _combat.LineOfFireClear, _enemyMagic.SpellOptions, _enemyMagic.Aim,
                id => authored.GetValueOrDefault(id)?.MobileId is int mobile && definitions.Mobiles.Mobiles.TryGetValue(mobile, out var record) ? record.Tendencies : null,
                engagement, _tuning.EnemyBehavior.Maneuvers, _tuning.EnemyBehavior.Tactics, _random);
            DaggerfallEnemyTactics tactics = new(new Dictionary<string, IDaggerfallEnemyTactic>(StringComparer.Ordinal)
            {
                [DaggerfallMobileDefinition.LegacyTactic] = new DaggerfallLegacyEnemyTactic(_enemyMagic, engagement, _tuning.EnemyBehavior.Maneuvers, _random),
                [DaggerfallScoredEnemyTactic.IntelligentName] = Scored(DaggerfallScoredPreset.Intelligent),
                [DaggerfallScoredEnemyTactic.BeastName] = Scored(DaggerfallScoredPreset.Beast),
            }, definitions.Mobiles, id => authored.GetValueOrDefault(id));
            _enemyBehavior.BindDecisions(tactics, _enemyMagic.Cast, _enemyMagic.LaneClear);
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
            _persistence = new(State, _corpseLoot, _groundContainers, _equipmentMoves, _notebook, _uniqueItems, _camera, _time, _site, State.Effects, () => _doors, _locomotion, _climbing, State.Swimming, DungeonText, propertyStorage.Capture, () => Expedition.Capture() with { PendingTransientClashes = OverlandParties.CapturePendingTransientClashes() }, authored, () => Casting.NextSequence);
            _persistence.EquipCountdown = () => _combat.EquipCountdown;
            if (saved is not null) _combat.EquipCountdown = saved.EquipCountdown;
            _roster = new DaggerfallActorRoster(actors, State.ActorInventories, State.Character, State.Containers, State.Effects, State.InventoryStore,
                State.ItemInstances, State.Npcs, definitions, _random, assembled.Mechanics, _actorIdentities, _uniqueItems,
                authored, saved?.ActiveDynamicActors(inputs.ProfileKey) ?? [], _grounding, () => _sites.Projection.Inputs, () => _sites.Projection.Appearance,
                _lootUi.CloseActor, _corpseLoot, State.ActorLedger, _persistence.RestoreDetachedActorState);
            _roster.ActorRetired += State.Quests.ObserveFoeRemoval;
            OfficeLedger.OfficeChanged += _roster.ObserveOfficeChanged;
            Crimes = new DaggerfallCrimes(State.Crime, State.Actors, State.Npcs, State.PlayerControl, _site, _roster.Definitions, definitions,
                State.ItemInstances, State.SkillUses, State.RacialOverrides, _random, engine.Perception, () => _spatial.Session,
                calendar: () => _time.Calendar,
                grantGold: grant => State.Currency.TrySpendGold(0, [grant]),
                provoke: target =>
                {
                    if (_enemyBehavior.IsPacified(target)) _enemyBehavior.MakeActiveEnemiesHostile();
                    _enemyBehavior.MakeHostile(target);
                },
                actorLedger: State.ActorLedger);
            PropertyLoot = new DaggerfallPropertyLoot(_groundContainers, definitions, _random, _uniqueItems, Crimes, State.Crime,
                State.ItemInstances, State.Actors, State.Progression, State.Character, State.SkillUses, State.PlayerControl, _site,
                calendar: () => _time.Calendar,
                activeProfile: () => _activeProfileKey,
                placements: () => _sites.Projection.Inputs.PropertyContainers,
                building: CurrentInteriorBuilding,
                owned: Holdings.OwnsInterior);
            _dungeonPopulation = new DaggerfallDungeonPopulation(definitions, _random, _uniqueItems, _groundContainers, _grounding.RestingPosition, _encounters,
                (definition, pose, spawnDistance, passive) => _roster.Spawn(definition, pose, spawnDistance: spawnDistance, passive: passive, lairTruce: true),
                State.Character, () => State.Progression.Level);
            Civilians = new DaggerfallCivilians(State.Npcs, actors, () => _sites.Projection.Inputs, () => _appearance, _site.Require,
                point => _exterior.ProfileToLocal(point), (npc, pose) => _roster.MaterializeCivilian(npc, pose), id => _roster.UnloadActor(id),
                RetireActor, id => RetireDetachedActor(id, DaggerfallActorKinds.Civilian),
                (npc, origin, baseline, pose, frameOffset, baselineFamilyId) => _roster.MaterializeDeterministicCivilian(npc, origin, baseline, pose, frameOffset, baselineFamilyId),
                id => State.Quests.IsEscortWithParty(id)
                    || State.ActorLedger.IsHomeSlotSuppressed(id, _activeProfileKey));
            OverlandParties = new DaggerfallOverlandPartyCoordinator(
                Expedition, State.ActorLedger, actors, State.Npcs, _roster,
                () => _sites.Projection.Inputs, () => _appearance,
                point => WorldPoint.From(_exterior.LocalToProfile(point.ToVector())),
                () => _exterior.ProfileToLocal(default).ToVector(),
                DaggerfallSessionPersistence.ToSocialDiff,
                id => _persistence.CaptureLiveActorForCustody(
                    id, _activeProfileKey, point => WorldPoint.From(_exterior.LocalToProfile(point.ToVector()))),
                siteCoordinates: siteId => _site.TryFind(siteId, out DaggerfallSiteRecord rec) ? (rec.MapPixelX, rec.MapPixelY) : null,
                provokeHostile: id => { _enemyBehavior.MakeHostile(id); _enemyBehavior.Provoke(id, _time.Calendar.MinuteNumber); },
                spawnClashMobile: (mobileId, pose) =>
                {
                    DaggerfallActorDefinition? def = definitions.Actors.Values
                        .Where(a => a.MobileId == mobileId)
                        .OrderBy(a => a.Id.Value, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (def is null) return null;
                    return SpawnActor(def.Id.Value, pose, lairTruce: false, factionInfighting: true);
                },
                tryResolveSpawnPose: (operation, origin, minimum, maximum, direction) =>
                    _placement.TrySpawnPose(operation, "daggerfall.overland-clash", origin, minimum, maximum, out ActorPose pose, direction)
                        ? pose
                        : null);
            if (saved?.Expedition.PendingTransientClashes is { Length: > 0 } pendingClashes)
                OverlandParties.RestorePendingTransientClashes(pendingClashes);
            OverlandTraffic = new DaggerfallOverlandTraffic(
                definitions, State.ActorLedger, actors, State.Npcs, Expedition, OverlandParties,
                questNames.NpcName, () => _worldSocialSeed, () => _site.ActiveSite?.Id);
            HostileOverlandBands = new DaggerfallHostileOverlandBands(
                definitions, State.ActorLedger, _actorIdentities, Expedition, OverlandParties,
                ReadHostileStanding, () => State.Progression.Level, () => _worldSocialSeed, () => _site.ActiveSite?.Id);
            SocialConsequences = new DaggerfallSocialConsequences(
                State.Social, definitions, State.Actors, State.Npcs, State.ActorLedger,
                () => Crimes.QueryWitnesses(), Crimes.IsCityWatch, () => _site.Region,
                WordSpread, () => _site.ActiveSite, () => _time.Calendar.MinuteNumber);
            if (saved is not null)
                SocialConsequences.Restore(saved.LocalHouseholds);
            State.Social.BindActorDisposition(id => SocialConsequences.EffectiveDispositionDelta(id));
            Introductions.BindEffectiveDisposition(
                (actorId, atSite) => SocialConsequences.EffectiveDispositionDelta(actorId, atSite),
                (familyId, atSite) => SocialConsequences.HouseholdStandingDelta(familyId, atSite));
            Introductions.BindDispositionAdjusted((actorId, delta) =>
            {
                if (delta > 0)
                    SocialConsequences.ReactToKinGoodwill(actorId, delta);
            });
            ConditionCounterplay = new DaggerfallConditionCounterplay(
                definitions, WorldConditions, WordSpread, State.Actors, _roster.Definitions,
                () => _activeProfileKey, () => _site.ActiveSite, () => State.Character.Identity.Name,
                id => QuestFoes.Team(id) == "player-ally",
                hasPendingBandMembers: (bandId, gen) => _encounters.Pending.Any(p => p.Band is { Id: int id } && id == bandId && p.Generation == gen));
            if (saved is not null)
                ConditionCounterplay.Restore(saved.ConditionCounterplay);
            if (definitions.PoliticalCanon is { } politicalCanon)
                PoliticalBoard = new DaggerfallPoliticalBoard(politicalCanon, definitions.BoardSeats, WorldConditions,
                    definitions.Locations.Records, CourierDispatches, _time.Calendar.DayNumber, saved?.PoliticalBoard);
            PartyHire = new DaggerfallPartyHireService(
                definitions, actors, State.ActorLedger, OverlandParties,
                () => _sites.Projection.Inputs, CurrentInteriorBuilding,
                () => State.Transport.HasCart(State.Inventory.Read()),
                () => State.Currency.Read().Gold,
                amount => State.Currency.TrySpendGold(amount, []),
                id => State.Quests.IsNpcMuted(id) || State.Quests.IsEscortWithParty(id),
                hasDepositWaiver: Introductions.HasDepositWaiverVouch);
            Introductions.BindHireableContactLookup(PartyHire.IsHireable);
            Population = new DaggerfallSourcePopulation(State.Npcs, actors, State.PlayerControl, definitions, _tuning.CivilianWander, Civilians,
                () => _sites.Projection.Inputs, () => _sites.ActiveProfile, () => _appearance, () => _exterior.ActiveLocationLoaded,
                () => _exterior.ExteriorResidencyInitialized, point => _exterior.ProfileToLocal(point), point => _exterior.LocalToProfile(point),
                _site.Require, () => _time.Calendar.IsDay, () => State.RacialOverrides.Current?.SuppressPopulationSpawns == true,
                id => _reactions.Holds(id), (actor, policy, step, deltaSeconds) => _ = _enemyBehavior.UpdateCivilian(actor, policy, step, deltaSeconds),
                message => engine.Diagnostics.Publish(new DiagnosticsPublishRequest(DiagnosticsSeverity.Warning, DiagnosticsDisposition.Accepted,
                    "daggerfall.people", "population", message, string.Empty)),
                State.ActorLedger, id => State.Quests.IsEscortWithParty(id),
                (siteId, placement, actorId, totalCount) =>
                    EvaluateExteriorPlacementSchedule(siteId, placement, actorId, totalCount).Placement == DaggerfallSchedulePlacement.ExteriorStreet);
            State.Quests.BindEscortOverlay((actorId, media, order) =>
            {
                if (media is not null)
                {
                    // HUD-only escorts travel WithParty through the unified overland party coordinator and leave EntityStore.
                    if (State.Npcs.TryGet(actorId, out _) || State.ActorLedger.TryGet(actorId, out _))
                        OverlandParties.JoinParty(actorId, ActorPartyRole.Escort, media, explicitOrder: order);
                    else
                        _roster.UnloadActor(actorId);
                }
                else
                {
                    if (State.Quests.IsEscortWithParty(actorId)) return;
                    DaggerfallWorldProfileKey? returnProfile = null;
                    ActorPoseOverride? returnPose = null;
                    ActorLedgerEntry? priorEntry = null;
                    if (State.ActorLedger.TryGet(actorId, out ActorLedgerEntry? existing) && existing is not null)
                    {
                        priorEntry = existing;
                        returnProfile = existing.Origin.SiteProfile?.Require();
                        returnPose = existing.Pose;
                    }
                    bool hasReturnTarget = false;
                    if (!State.Actors.TryGet(actorId, out _)
                        && State.Npcs.TryGet(actorId, out DaggerfallNpc? returningNpc)
                        && returningNpc.Presence != DaggerfallNpcPresence.Removed)
                    {
                        DaggerfallWorldProfileKey? profileKey = returningNpc.Profile ?? returnProfile;
                        WorldPoint? profilePoint = returnPose is { } rp
                            ? new WorldPoint(rp.X, rp.Y, rp.Z)
                            : returningNpc.X is float rx && returningNpc.Y is float ry && returningNpc.Z is float rz
                                ? new WorldPoint(rx, ry, rz)
                                : null;
                        if (profileKey is { } pk && profilePoint is { } pt)
                        {
                            hasReturnTarget = true;
                            if (priorEntry is not null)
                            {
                                bool isHomeSite = priorEntry.Origin.Kind is ActorOriginKind.SeededSlot or ActorOriginKind.CastIndividual
                                    && priorEntry.Origin.SiteProfile?.Require() == pk;
                                State.ActorLedger.Upsert(priorEntry with
                                {
                                    Location = isHomeSite ? null : ActorLocationOverride.AtSite(pk),
                                    Social = priorEntry.Social is { } s ? s with { EscortPortrait = null, EscortOrder = 0 } : null,
                                });
                            }
                            State.Npcs.Place(actorId, pk, pt);
                            if (pk == _activeProfileKey && _exterior.ActiveLocationLoaded)
                            {
                                if (returningNpc.Kind == DaggerfallNpcKind.Civilian)
                                {
                                    if (DaggerfallPeopleSlots.IsDeterministicId(actorId) && priorEntry is not null)
                                    {
                                        WorldPoint baseline = _sites.Projection.Inputs.Population
                                            .FirstOrDefault(p => StringComparer.Ordinal.Equals(p.Id, priorEntry.Origin.SlotKey))?.Position ?? pt;
                                        Civilians.MaterializeDeterministic(actorId, priorEntry.Origin, baseline,
                                            new ActorPose(_exterior.ProfileToLocal(pt), returnPose?.HeadingRadians ?? 0f),
                                            _exterior.ProfileToLocal(default).ToVector());
                                    }
                                    else
                                    {
                                        Civilians.Materialize(actorId, new ActorPose(_exterior.ProfileToLocal(pt), returnPose?.HeadingRadians ?? 0f));
                                    }
                                    State.Npcs.Bind(actorId, pk);
                                    State.Npcs.ReleasePose(actorId);
                                }
                                else
                                {
                                    ReconcileNpcProjection();
                                }
                            }
                        }
                    }
                    if (!hasReturnTarget && priorEntry is not null)
                    {
                        State.ActorLedger.Upsert(priorEntry with
                        {
                            Location = priorEntry.Location?.Kind == ActorLocationKind.WithParty ? ActorLocationOverride.OffStage : priorEntry.Location,
                            Social = priorEntry.Social is { } s ? s with { EscortPortrait = null, EscortOrder = 0 } : null,
                        });
                    }
                }
            });
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
            _persistence.Weather = LocalWeather.State.Capture;
            _persistence.WorldVariants = () => _sites.Profiles?.CaptureVariants() ?? [];
            Summoning = new(definitions, State.Npcs, State.ConcreteGuildServices, State.Services, State.Social, _random,
                composition.DisabledQuestSelection ?? DaggerfallDisabledQuestSelection.None, () => _time.Calendar,
                () => (LocalWeather.IsRaining, LocalWeather.IsStorming), () => _activeProfileKey,
                result => DaggerfallDaedricSummoning.PrepareQuest(definitions, State.Quests, result), saved?.Summoning);
            _persistence.Summoning = Summoning.Capture;
            _persistence.PoisonDraws = () => _poisoning.Draws;
            _persistence.ReadySpell=()=>Casting.ReadyFor(actors.Player.DurableId);
            _persistence.PendingCreateItem = () => CreateItemChoice.Pending;
            _persistence.PendingDispel = () => DispelChoice.Pending;
            _persistence.PendingTeleport = () => TeleportChoice.Pending;
            _persistence.PlaySeconds = () => _playSeconds;
            _persistence.GameSeed = () => _random.Seed;
            _persistence.WorldSocialSeed = () => _worldSocialSeed;
            _persistence.OfficeLedger = OfficeLedger.Capture;
            _persistence.WorldConditions = WorldConditions.Capture;
            _persistence.WordSpread = WordSpread.Capture;
            _persistence.LocalHouseholds = SocialConsequences.Capture;
            _persistence.CourierDispatches = CourierDispatches.Capture;
            _persistence.ConditionCounterplay = ConditionCounterplay.Capture;
            _persistence.PoliticalBoard = () => PoliticalBoard?.Capture();
            _persistence.TeleportAnchor = () => TeleportChoice.Anchor;
            _persistence.PendingIdentify = () => IdentifyChoice.Pending;
            _persistence.Tutorial = () => _tutorial.Progress;
            _roster.BanishedActors.UnionWith(saved?.ActiveBanishedActors(inputs.ProfileKey) ?? []);
            _persistence.BanishedActors = () => _roster.BanishedActors;
            DaggerfallInteriors interiors = new(actionTriggers, State.DungeonActions, State.DungeonDiscoveries,
                (key, admitted) => new DaggerfallDungeonActionGraph(key.LogicalId, admitted.DungeonActions, State.Variables.Write,
                    executeFamilyAction: action => DungeonActions.Execute(action)), _site);
            _sites = new DaggerfallSiteCrossing(engine, _random, State.Actors, State.Npcs, State.PlayerControl, State.Effects, State.Transport.ForceFootOnInteriorTransition,
                definitions, tuning.Site, _time, _site, _spatial, _camera, audioBundles,
                _roster, _persistence, (id, definition, offset) => _persistence.CaptureDynamicActorDelta(id, definition, offset),
                _groundContainers, _enemyBehavior, interiors, this,
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
                (npc, profile, pose, sprite) =>
                {
                    if (State.ActorLedger.TryGet(npc.DurableId, out ActorLedgerEntry? entry) && entry is not null
                        && entry.Location?.Kind == ActorLocationKind.WithParty)
                    {
                        State.ActorLedger.Upsert(entry with { Location = ActorLocationOverride.AtSite(profile.ProfileKey) });
                    }
                    _roster.MaterializeQuestor(npc, profile, new ActorPose(pose, 0f), sprite);
                }, BringOutRetainedQuestActor,
                (castId, profile, floor, appearance, displayName) =>
                {
                    DaggerfallCastHomeLocation? home = definitions.People.Individuals.TryGetValue(castId, out DaggerfallCastIndividualDefinition? castDef)
                        && DaggerfallCastHomes.TryResolve(_site, castDef, profile.Site?.Region, out DaggerfallCastHomeLocation? resolvedHome)
                            ? resolvedHome
                            : null;
                    return _roster.RelocateCastIndividual(castId, profile, floor, appearance, displayName,
                        DaggerfallCastHomes.ResolveProfileKey(home), _exterior.ProfileToLocal(default).ToVector());
                }, OfficeHolders.IsActorDead);
            // A quest placement holds a person only while the quest lasts: once it ends the person goes home.
            State.Quests.BindPlacementRelease(people => { foreach (long id in people) _roster.ReturnPlacedPersonHome(id); });
            QuestFoes = new DaggerfallQuestFoes(State.Quests, State.Actors, State.PlayerControl, () => Expedition.Open, _roster.Definitions, _placement, _vitality,
                tuning.QuestSpawning, () => _sites.ReadQuestLocation(), (definition, pose) => _roster.Spawn(definition, pose),
                _roster.RemoveQuestActor, (id, allies) => _enemyBehavior.MakeHostile(id, includePlayerAllies: allies),
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
                        State.ItemInstances.RequireUnique(State.Inventory.GetDurableItemId(new(equipped.EntityId)).Value).Material);
            };
            // Painted art wants its values untouched: this operator leaves everything below a highlight exact, so only
            // what emits above 1 rolls off, and it is what bloom spreads.
            engine.CameraView.SetToneMapping(new(ToneMappingOperator.Rolloff, 1));
            engine.CameraView.SetBloom(new(1, tuning.SiteLighting.Bloom));
            _weatherPresentation = new DaggerfallWeatherPresentation(engine, _random, composition.Sky, tuning.Ambient, tuning.Horizon, tuning.PresentationAudio,
                (profile, clip) => audioBundles?.Require(profile).OpenClip(engine.Audio, clip));
            partiallyConstructed.Add(_weatherPresentation);
            _interiorLook = new DaggerfallInteriorLook(engine, composition.Data, tuning.SiteLighting);
            partiallyConstructed.Add(_interiorLook);
            Func<int, int, int> climateAtPixel = (x, y) => definitions.Grids.ClimateAtWorldPixel(x, y).Value;
            DaggerfallOverlandRoads overlandRoads = new(definitions.Terrain, climateAtPixel, definitions.Locations.Records);
            DaggerfallHorizonGround ground = new(definitions.Terrain,
                climateAtPixel, tuning.Horizon, tuning.Ground, _exterior.Locations, overlandRoads);
            _exterior.UseGround(ground, GroundWalkers);
            DaggerfallArtFile? mapLandShader = composition.Data is not null
                && composition.Data.TryReadFile(DaggerfallOverlandTerrain.LandShaderPath, out ReadOnlyMemory<byte> landShaderBytes)
                ? new(DaggerfallOverlandTerrain.LandShaderPath,
                    DaggerfallContentHash.Parse(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(landShaderBytes.Span)), "The travel map's land shader"))
                : null;
            _horizon = new DaggerfallHorizon(engine, ground, tuning.Horizon, tuning.Water, tuning.Ground.RepeatMetres, definitions.Terrain.Rivers,
                new DaggerfallFloraField(engine, ground, tuning.Flora, tuning.Sprites, tuning.Flora.ArchiveDensity(definitions.Grids.Climate.Values)),
                new DaggerfallFarForest(engine, ground, tuning.Flora, tuning.Sprites, tuning.Flora.ArchiveDensity(definitions.Grids.Climate.Values)),
                composition.ArtReplacements?.Sea, tuning.Expedition.Map.Relief, tuning.Expedition.Map.Ground, composition.ArtReplacements?.TravelMap,
                mapLandShader);
            partiallyConstructed.Add(_horizon);
            _overland = new DaggerfallOverlandMap(engine, _horizon.Map, tuning.Expedition.Map, Expedition.Policy.AllDestinations(), _camera.Activate,
                mobile => _sites.Projection.Inputs.MobileSprites.GetValueOrDefault(mobile), PartyMobile, tuning.Sprites.Actors,
                composition.ArtReplacements?.TravelMap);
            partiallyConstructed.Add(_overland);
            _exterior.DrawSeaWith(_horizon.AppendFacts, _horizon.CompleteSnapshot, _horizon.ReleaseSea);
            if (composition.ArtReplacements?.GroundCover is { } cover)
                _horizon.UseGroundCover(cover, tuning.GroundCover, DaggerfallGroundCover.TuftGrounds(definitions.Grids.Climate.Values),
                    new DaggerfallFloraField(engine, ground, tuning.Flora, tuning.Sprites, new Dictionary<int, float>(),
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
                promoteGuard: id => _roster.PromoteToCityWatch(id));
            Law.BindGateScrutiny(region => Scrutiny.EvaluateGateScrutiny(region, _site.ActiveSite));
            Law.BindCrimeSpread((incident, loss) =>
            {
                DaggerfallSiteRecord? origin = _site.ActiveSite;
                if (origin is not null)
                    WordSpread.RecordCrime(incident.OperationId, incident.Region, loss, origin, _time.Calendar.MinuteNumber);
                SocialConsequences.ReactToAttributedCrime(
                    incident,
                    origin,
                    _time.Calendar.MinuteNumber,
                    Crimes.TryGetVictimFamilyId(incident.OperationId));
            });
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
                () => _sites.Projection.Inputs);
            QuestWork = new DaggerfallQuestWork(State.Quests, State.Npcs, State.Services, State.Social, State.Crime,
                (npc, building) => GuildProviders.TryResolve(npc, building, DaggerfallConcreteGuildService.Quests, out var guild, out _, out _) ? guild : null,
                definitions,
                State.Progression, _time, _site, tuning.QuestOffers, CurrentInteriorBuilding, () => _sites.ActiveProfile.Kind, InsideCastleZone,
                assessIntroductionAccess: (npc, castle) => Introductions.AssessAccess(npc, castle));
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
            Entrance = new DaggerfallBuildingEntrance(_site, definitions, _time, _buildingGreeting, CurrentTextContext,
                hallStanding: (faction, region) => State.GuildMembership.HallStanding(faction, _time.Calendar, region),
                ownsHouse: (site, building) => State.Property.OwnsHouse(new(site, building.Source.Id, building.Id.BlockX, building.Id.BlockY)),
                ownsShip: () => State.Property.OwnsShip,
                ownsInterior: interior => Holdings.OwnsInterior(interior),
                questClaims: State.Quests.ClaimsBuilding);
            Locks = new DaggerfallLocks(State.Actors, State.SkillUses, State.Equipment, _site, definitions, _random);
            _sight = new DaggerfallActivationSight(() => _sites.Projection.Inputs, State.Npcs, State.Actors, _roster.Definitions);
            DungeonActions = new DaggerfallDungeonActions(State.DungeonActions, () => _activeProfileKey,
                () => _sites.Projection.Inputs, () => _doors, () => _sites.Projection.Motion,
                _sites.Interiors.ActionTriggers, _spatial, () => _exterior.CharacterEnvironment(State.PlayerControl.Motion),
                _exterior.LocalToProfile, _sites.TryRelocatePlayer, State.PlayerControl, State.Actors,
                tuning.Camera.EyeHeight, tuning.Spatial.CollisionVoxelSize, Traps.SpreadHurt, combatRules, _random,
                (source, spell, target) => Casting.TriggerDungeonAction(source, spell, target), _facts,
                damage => AppendDamage(damage, DaggerfallDamageCause.Hazard, 0),
                () => _latestUpdateGeneration, () => _latestSimulationStep, ExecuteDungeonTextAction, DungeonText.ShowsDoorText,
                message => Presentation.SetOutcome(message));
            Activation = new DaggerfallActivation(DungeonActions, State.Actors, State.PlayerControl, () => _doors,
                tuning.LootInteraction.MaximumDistance, tuning.MeleeTargeting.MaximumDistance);
            ComposeActivation(engine, tuning.LootInteraction);
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
            // published group once and publishes it to the UI that draws it.
            // The refreshed icons go up front; the classic images the art set draws anew are read when a developer switches to
            // classic art.
            _uiArt = composition.Data is not { } data ? DaggerfallUiArt.None : DaggerfallUiArt.Read(data, composition.StartSite.ClassicPresentation.InventoryIcons.Values
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
                    composition.ArtReplacements, composition.ArtReplacements is { } drawn ? DaggerfallUiArtChoice.DrawnAnew(drawn) : null);
            _hud = new DaggerfallHudProjection(engine.Ui, definitions.HudResources, composition.Identity, _uiArt)
            {
                // The controls screen shows each action's controller buttons against the authored pad layout.
                PadActions = tuning.ControllerInput?.Actions ?? [],
                RefreshedArt = composition.ArtReplacements is not null,
                Help = definitions.Help,
            };
            partiallyConstructed.Add(_hud);
            Radial = new DaggerfallRadialCommands(State.Transport, State.RacialOverrides, () => State.WeaponDrawn);
            RestPanel = new DaggerfallRestPanel(_rest, State.Actors.Player.Stats, this, _passives, () => _sites.ActiveProfile.Kind, ApplyRest, RequestPanel,
                () => { _input.Neutralize(); _locomotion.Neutralize(); NeutralizeGuard(); });
            LocalMap = new DaggerfallLocalMap(State.PlayerControl, _site, State.DungeonDiscoveries, () => _sites.Projection.Inputs,
                () => _sites.ActiveProfile, point => _exterior.ExteriorSitePosition(point), Presentation);
            DaggerfallCharacterActions characterActions = _characterActions = new(_creation, State.Character, State.LevelUps, _controls, definitions.NewGame.QuickEntry,
                () => _mode == ProductMode.Title && !_newGameInitialized, Presentation);
            _lootActions = new DaggerfallLootActions(_lootUi, _corpseLoot, _groundContainers, _facts, State.Encumbrance, definitions,
                State.ItemInstances, State.Inventory, State.PlayerControl, State.Quests.ItemClicked, PropertyLoot.Open, PropertyLoot.ObserveTake,
                Presentation);
            EconomyPanels = new DaggerfallEconomyPanels(Banking, State.Bank, MerchantTrade, RoomRental, State.Tavern, State.SkillTraining, Holdings,
                State.Inventory, State.Wagon, _inventoryUi, definitions, () => _dialogue, CurrentInteriorBuilding, TransportAccess, RequestPanel,
                destination => TryTransitionTo(destination), Presentation);
            DaggerfallExpeditionPanel travelPanel = _travelPanel = new(Expedition, travelPolicy, this, State.Actors, definitions.BuildingNames.RegionNames,
                definitions.WorldNames, _atlasTiles, () => _classicArt, State.Transport, State.Wagon, State.Inventory, _inventoryUi,
                _hud.RequestTravelPlaces, TransportAccess, OpenExpedition, EnterExpedition, BoardPropertyShip, LeavePropertyShip,
                ApplyTransportChange, Presentation, _overland, MapPosition.Position, DescribePersistentExpeditionBand);
            DaggerfallServiceCounter counter = new(() => _dialogue, CurrentInteriorBuilding, _site, () => _sites.ActiveProfile, _activationPresentation);
            DaggerfallMakerPanels makers = new(_itemMaker, _potionMaker, definitions, counter, Presentation);
            SummoningPanel = new DaggerfallSummoningPanel(Summoning, State.Quests, Cinematics, composition.VideosEnabled, counter,
                ReconcileSummoningFoes, Presentation);
            Spellbook = new DaggerfallSpellbookPanel(State.Character, State.Actors, Casting, SpellRelease, definitions, SpellSales, SpellMaker,
                counter, makers, SummoningPanel, () => DrawnArt, Presentation);
            ActivationPanel = new DaggerfallActivationPanel(_activationPresentation, Activation, () => _dialogue, Banking, State.Effects,
                State.SkillTraining, State.Tavern, CurrentInteriorBuilding, EconomyPanels, Spellbook, counter, Presentation);
            DaggerfallTextActions textActions = new(DungeonText, _notebook, DungeonActions.ContinueAcceptedAnswer, Presentation, ContinueBuildingEntry);
            _dispatch = new DaggerfallUiDispatch(State.Character, definitions.NewGame.QuickEntry, State.RacialOverrides, Law, _playerTime,
                CreateItemChoice, IdentifyChoice, TeleportChoice, DispelChoice, _openingCinematics, Cinematics, _tutorial, PlayerAttack, State.Quests,
                State.Variables, _input, _hud, _inventoryUi, _lootUi, characterActions, LocalMap, ActivationPanel, Spellbook, makers, SummoningPanel,
                EconomyPanels, travelPanel, RestPanel, textActions, _lootActions, _deathScreen,
                new DaggerfallUiDispatch.SessionCalls(ChooseLegal, AnswerQuestOffer, ToggleWeapon, RequestPanel,
                    request => _saveSlotRequest = request, key => _bundleChoice.Choose(key, _mode, _newGameInitialized), _bundleChoice.StartUnderChoice, open => _interactions.SetMenuOpen(open), _quit.Request, _quit.Applied, () => _interactions.HoldsWorldOpen),
                Presentation, InputRefusals);
            InputReadout = new DaggerfallInputReadout(PlayerAttack, State.Actors, _guard, Casting, definitions, () => _appearance.DrawnHeld,
                () => State.WeaponDrawn, InputRefusals);                                                  // UiProjection
            HudFrames = new DaggerfallHudFrames(_hud, State.Actors, Presentation, State.PlayerControl, Slots, _inventoryUi, EconomyPanels, _lootUi,
                _characterUi, definitions, CurrentTextContext, _controls, ActivationPanel, State.Swimming, State.Effects, State.Quests, _questTexts, _time,
                _notebook, State.Transport, State.Inventory, TransportAccess, State.Property, State.Wagon, DungeonText, _deathScreen, RestPanel,
                RoomRental, travelPanel, _site, EffectItemName, Cinematics, LocalMap, Law, CreateItemChoice, TeleportChoice, DispelChoice,
                IdentifyChoice, Spellbook, Detectors, _playerTime, _loading, _tutorial, Radial, Activation, _input, () => DrawnArt,
                () => State.WeaponDrawn, () => _interactions.HoldsWorldOpen,
                Math.Min(_tuning.ControllerInput.MovementDeadzone, _tuning.ControllerInput.LookDeadzone), InputReadout,
                () => OverlandParties.PartyPortraits());
            DaggerfallHeldWorkbench heldWorkbench = new(composition.HeldArt, tuning.Attacks, tuning.Viewmodel.Style, () => _appearance, () =>
            {
                if (!_inAdmittedUpdate && !_disposed) PublishPresentation();
            });                                                                                   // Developer
            _overlays = new DaggerfallDebugOverlays(engine, State.PlayerControl, _camera, _input, State.Actors, State.Npcs, State.Kit.Targeting,
                _roster, _senses, _enemyBehavior, _combat, PlayerAttack, _spatial, _sites, Activation, Entrance, () => _dialogue,
                tuning.LootInteraction.MaximumDistance);                                          // Developer
            DaggerfallSpatialInspection spatialInspection = new(engine, _spatial, _sites, State.PlayerControl, _input, State.Actors, Activation,
                _controls, () => _latestUpdateGeneration, () => _latestSimulationStep, _overlays, WildNavigation);    // Developer
            _playtest = new DaggerfallPlaytestState(_controls, () => _mode, State.Actors, State.Effects, _combat, () => State.WeaponDrawn, PlayerAttack,
                State.Kit.AttackExecution, State.Kit.Targeting, () => _latestUpdateGeneration, () => _latestSimulationStep, State.Encumbrance,
                State.PlayerControl, _input, _camera, Presentation, Activation, ActivationPanel, _lootUi, _roster, _senses, _enemyBehavior, _reactions,
                tuning.LootInteraction.MaximumDistance, spatialInspection.PlayerFloorY);                // Developer
            DaggerfallDeveloperHold hold = new(() => _mode, State.Actors, State.PlayerControl,
                () => Enum.GetValues<DaggerfallInteractionScreen>().Where(screen => screen != DaggerfallInteractionScreen.Menu && _interactions.IsOpen(screen))
                    .Select(screen => screen.ToString()),
                Summoning, State.Quests, Law, DispelChoice, IdentifyChoice, CreateItemChoice, _openingCinematics, Cinematics);   // Developer
            DaggerfallDeveloperPlacement placement = new(hold, engine, _spatial, _sites, _site, State.PlayerControl, State.Actors, State.Npcs,
                definitions, Expedition, MapPosition, Activation, Entrance, _horizon, _time, () => _mode, Presentation, _nearbySpawn.Bring,
                local => _exterior.ProfilePosition(local), BuildingLanding);                                                    // Developer
            DaggerfallDeveloperScene scene = new(_time, seconds => { _ = AdvanceElapsedTime(seconds, idleFatigue: false); }, _horizon, _sites,
                LocalWeather, _weatherPresentation, State.PlayerControl, definitions, UseClassicArt, () => _hanging, LooseItems,
                _interiorLook, () => SyncWeatherContext());   // Developer
            DaggerfallDeveloperGrants grants = new(hold, definitions, _random, State.Actors, State.PlayerControl, EquipmentMoves,
                () => State.WeaponDrawn = true, State.Currency, State.SkillUses, State.LevelUps, State.Character, State.Transport, State.Inventory,
                State.Encumbrance, State.ItemInstances, _uniqueItems, State.RacialOverrides, _lycanthropy, _time, TransportAccess,
                ApplyTransportChange);                                                                // Developer
            DaggerfallPeopleStage peopleStage = new(State.Actors, id => Population.Walks(id), State.PlayerControl, (at, moving) => _placement.StandingAt(at, moving));   // Developer
            Action<string> warnPeople = message => engine.Diagnostics.Publish(new DiagnosticsPublishRequest(DiagnosticsSeverity.Warning, DiagnosticsDisposition.Accepted,
                "daggerfall.people", "people.splat", message, string.Empty));   // Developer
            // The gsplat townspeople's clips ship in their own container; without it walkers draw their sprites, said once.
            if (composition.ArtReplacements?.SplatPeople is not null && composition.Animations is null)
                warnPeople($"{DaggerfallAnimations.ContainerName} is not installed, so the gsplat townspeople have no clips and walkers draw their sprites.");   // Developer
            _people = new DaggerfallPeopleSwitch(composition.ArtReplacements?.PeopleRig, composition.ArtReplacements?.SplatPeople, composition.Animations,
                () => _engine.PartsRigs, () => _engine.Animation, () => _engine.Content, _tuning.CivilianWander.MovementSpeedUnitsPerSecond, _tuning.SplatPeople,
                State.Actors, peopleStage, [.. definitions.Catalogs.Races.SelectMany(race => race.Townspeople.Values.SelectMany(archives => archives))],
                id => Population.Townsperson(id), id => Population.Seed(id), () => DrawnArt is not null, warnPeople);   // Developer
            DaggerfallActorInspection actorInspection = new(State.Actors, State.PlayerControl, _roster, _senses, _enemyBehavior, _enemyMagic,
                Casting, _combat, State.Effects, id => State.Actors.TryGet(id, out _) ? actorInventories.EquipmentFor(id).Read() : null, id => QuestFoes.Team(id),
                () => _latestSimulationStep, definitions, State.Npcs, questNames, () => _worldSocialSeed,
                State.Social, SocialConsequences, Introductions, State.ActorLedger, () => _site.ActiveSite);                                       // Developer
            // One object per module type: the Engine's catalogue binds an instance to the first type it matches.
            DebugCommands =
            [
                new PlaytestDebugModule(_playtest.ReadObservation, _playtest.InspectAction, _playtest.Actions, _playtest.InspectLook),
                new PlaytestTargetsDebugModule(_playtest.ReadTargets),
                new InteractionDebugModule(new WorldInteraction(new CurrentInteractionInspectionScene(spatialInspection.CreateInteractionInspection),
                    targetedUseEnabled: false)),
                new SpatialInspectionDebugModule(spatialInspection.ReadGrid, spatialInspection.ReadProbe, spatialInspection.ReadJumpPlan),
                new SpatialClearanceDebugModule(spatialInspection.ReadClearance),
                spatialInspection,
                _overlays,
                new InspectionTargetsDebugModule(engine.Graphics, _overlays.InspectionTargets),
                placement,
                scene,
                grants,
                _people,
                peopleStage,
                actorInspection,
                InputReadout,
                heldWorkbench,
                new TunablesDebugModule(engine.ContentSource, heldWorkbench.Page, heldWorkbench.Choose),
                new DaggerfallExpeditionBandReadout(Expedition, OverlandParties, State.Actors, State.Npcs),
                new DaggerfallOverlandViewCommands(_overland),
                new DaggerfallWorldConditionCommands(WorldConditions, Scrutiny, NoticeBoard, WordSpread, CourierDispatches, CreatureClashes, State.PlayerControl, definitions, _site, () => _time.Calendar),
                new DaggerfallSocialInspection(definitions, _site, State.Actors, State.Npcs, State.ActorLedger, State.Social, SocialConsequences, Introductions, OfficeHolders, OfficeLedger, State.Crime, questNames, () => _worldSocialSeed, () => _time.Calendar),
            ];                                                                                     // Developer
            _roster.BindScheduleStaticSuppression(SuppressedStaticSlotsFor);
            if (restore is not null)
                _persistence.Restore(restore, _sites, _roster, _encounters, _heldEnchantments,
                    snapshot => DungeonText.Restore(snapshot, () => DungeonActions.ActiveActions, CurrentTextContext),
                    activeProfile => ReconcileInteriorScheduledVisitors(activeProfile, _appearance));
            foreach (var spell in definitions.Magic.Spells.Values.Where(spell => spell.IsCustom))
                if (Casting.AvailableSpellCost(actors.Player.DurableId, spell.Key) is null)
                    throw new ArgumentException($"Custom spell '{spell.Key}' contains an unavailable compiled construction effect or combination.");
            if (saved?.ReadySpell is { } readyKey) Casting.RestoreReadySpell(readyKey);
            ExpireConjuredItems();
            if (restore is null)
            {
                _roster.MaterializeStaticNpcs(inputs);
                ReconcileInteriorScheduledVisitors(inputs, _appearance);
            }
            _sites.Load(saved?.ExteriorResidency, saved?.ExteriorLocationResidency);
            // Treasure and dungeon enemies are placed on the floor under their markers, so the site's
            // collision must be admitted first: before it, a ground probe meets nothing and they float.
            ReconcilePropertyContainers();
            // A new game arrives at its start site's start like any other arrival; a restore resumes its saved pose.
            if (restore is null) _exterior.StandArrivingPlayer();
            // Registry positions are profile coordinates; project NPCs and the civilian population
            // only once the save is restored and the saved origin admitted, so dialogue and the
            // first snapshot share its frame and no restored presence is decided from the start pose.
            ReconcileNpcProjection(immediate: true);
            _itemCastTriggers.Refresh();
            _interiorLook.WarmSite(
                _sites.Projection.Inputs,
                _sites.Projection.Appearance,
                State.PlayerControl.Position?.ToVector() ?? Vector3.Zero,
                _site.ActiveSite?.DungeonType,
                _controls.Settings.InteriorExtrude,
                _controls.Settings.InteriorExtrudeTuning,
                _controls.Settings.ExteriorExtrude);
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
            dialogueWorld: State.DialogueWorld, suppressTalk: () => State.RacialOverrides.Current?.SuppressTalk == true,
            issueReferral: (site, npc) => new DaggerfallSlotReferralPolicy(_definitions, State.ActorLedger)
                .TryIssuePreVisitReferral(site, npc, State.Social.ResolveNpcReactionFactionId(npc), checked((int)_time.Calendar.DayNumber))?.Message,
            partyHireTopics: npc => PartyHire.TopicsFor(npc),
            partyHireService: (npc, topic) => PartyHire.Resolve(npc, topic),
            introductionAccess: npc => Introductions.AssessAccess(npc),
            introductionTargets: (npc, site) => Introductions.ListIntroductionTargets(npc, site),
            requestIntroduction: (npc, targetId, site) => Introductions.TryIssueIntroduction(npc, targetId, site),
            canCurryFavour: npc => Introductions.CanCurryFavourToday(npc),
            curryFavour: (npc, polite, blunt) => Introductions.TryCurryFavour(npc, polite, blunt),
            worldConditions: WorldConditions,
            readNoticeBoard: site => DaggerfallNoticeBoard.FormatBoardText(NoticeBoard.BuildForSite(site, _time.Calendar.MinuteNumber)),
            evaluateGateScrutiny: Scrutiny.EvaluateGateScrutiny,
            wordSpread: WordSpread,
            courierDispatches: CourierDispatches);
        _dialogue.SynchronizeWorldState();
        return new DaggerfallPersonActivationOwner(_dialogue, State.Actors, Crimes.IsPickpocketTarget, Crimes.Pickpocket, Law.IsGuard, SurrenderToGuard, id => State.Npcs.IsGameplayActive(id) && State.Quests.IsClickableActor(id),
            id => State.RacialOverrides.Current?.SuppressTalk != true && State.Quests.ActorClicked(id), _sight.PersonHeight);
    }

    private (string Text, IReadOnlyList<string> Diagnostics)? ResolveQuestTopic(long npcId, string topicId)
    {
        return State.Quests.TryResolveDialogueTopic(npcId, topicId,
            out string? text, out IReadOnlyList<string> diagnostics)
            ? (text!, diagnostics) : null;
    }

    // A wielded item's template and metal, which choose how the 3D first-person art draws it.
    private (int? Template, string Metal, bool Enchanted) HeldItem(KitUniqueInventoryItem item)
    {
        DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(item.Definition.Value));
        var metadata = _inventoryUi.EquippedMetadata(item.EntityId);
        return (_definitions.SourceTemplateFor(definition)?.Index, metadata?.Material ?? definition.Weapon?.Material ?? DaggerfallHeldDrawing.Steel, metadata?.HasEnchantment == true);
    }

    // The shield worn on the off arm: its template and metal, which the 3D first-person art draws it by.
    private (int Template, string Metal)? HeldShield(EquipmentRead equipment)
    {
        foreach (WorldRpg.Kit.Inventory.EquipmentAssignment assignment in equipment.Assignments)
        {
            if (assignment.Slot.Value != "left-hand") continue;
            DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(assignment.Item.Definition.Value));
            if (definition.Shield is not null && _definitions.SourceTemplateFor(definition) is { } template)
                return (template.Index, _inventoryUi.EquippedMetadata(assignment.Item.EntityId)?.Material ?? definition.Armor?.Material ?? DaggerfallHeldDrawing.Steel);
        }
        return null;
    }

    // The arm set the 3D first-person art draws: the player's gender, and the heaviest armour on the arms.
    private string HeldArms(DaggerfallHeldArt art, EquipmentRead equipment)
    {
        if (State.RacialOverrides.Current is { State.BeastForm: true } form) return art.Arms(form.State.Kind);
        List<string> worn = [];
        foreach (WorldRpg.Kit.Inventory.EquipmentAssignment assignment in equipment.Assignments)
        {
            if (!DaggerfallArmWearPolicy.Slots.Contains(assignment.Slot.Value)) continue;
            DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(assignment.Item.Definition.Value));
            if ((_inventoryUi.EquippedMetadata(assignment.Item.EntityId)?.Material ?? definition.Armor?.Material) is { } material) worn.Add(material);
        }
        return art.Arms(State.Character.Identity.Gender, DaggerfallArmWearPolicy.Heaviest(worn));
    }

    /// <summary>Binds live session policy once whenever a site appearance is created.</summary>
    private void ConfigureSiteAppearance(DaggerfallSiteAppearance appearance)
    {
        appearance.UseEnemyAudibleRange(() => EnemyAudibleRange);
        appearance.UsePlayerVampireGender(() => State.RacialOverrides.Current?.IsVampire == true ? State.Character.Identity.Gender == DaggerfallCharacterGender.Female : null);
        appearance.UsePlayerRide(() => (State.Transport.Mode, new Vector2(State.PlayerControl.Motion.ControlledVelocity.X, State.PlayerControl.Motion.ControlledVelocity.Z).Length(), State.PlayerControl.PitchRadians, State.Transport.Ride.TurnFrom(State.PlayerControl.YawRadians)));
        appearance.UseViewLight(_engine.Tween, _viewLight);
        appearance.UsePlayerBeastForm(() => State.RacialOverrides.Current is { State.BeastForm: true } form ? form.State.Kind : null);
        appearance.UsePeopleRigs(() => _people);
        appearance.UseNatureField(record => _horizon.GrowsPlaced(record));
        if (_tuning.Viewmodel.Style != DaggerfallViewmodelStyle.Classic && _composition.HeldArt is { } art)
            appearance.UseHeldArt(_engine, art, _tuning.Viewmodel.Style, _composition.ArtReplacements?.Tints ?? new Dictionary<string, DaggerfallDyeTint>(), HeldItem, HeldShield, equipment => HeldArms(art, equipment));
        appearance.UseSurfaceRelief(_tuning.SurfaceRelief, () => (_controls.Settings.SurfaceRelief, _controls.Settings.ReliefDistance));
        if (_composition.ArtReplacements is { } refreshed)
            appearance.UseEffectArt(_engine.Presentation, _engine.Animation, refreshed, () => DrawnArt is not null,
                message => _engine.Diagnostics.Publish(new DiagnosticsPublishRequest(DiagnosticsSeverity.Warning, DiagnosticsDisposition.Accepted,
                    "daggerfall.hanging", "hanging.rig", message, string.Empty)));
    }

    /// <summary>
    /// Composes activation over the site now active: Dialogue's people first, then the corpse, door, portal and pile
    /// owners in a new module handed to <see cref="Activation"/>, so each site starts in Grab. Composition calls it
    /// once and every site change again.
    /// </summary>
    private void ComposeActivation(IEngineContext engine, DaggerfallLootInteractionTuning reach)
    {
        ArgumentNullException.ThrowIfNull(engine);
        IDaggerfallNpcActivationOwner people = ComposeDialogue();
        DaggerfallDoorActivationOwner door;
        DaggerfallActivationModule module = new(
            new InteractionTargetingService(engine.Perception, _spatial, State.Actors.Entities),
            reach,
            new DaggerfallActivationContributions(
                new DaggerfallCorpseActivationOwner(() => _corpseLoot.Corpses.Keys, _corpseLoot.ContainerName, State.Actors, _sight.Corpse, actor => _lootActions.OpenCorpse(actor)),
                door = new DaggerfallDoorActivationOwner(_doors, DungeonActions.TriggerDoor, ForceLock, id => _doorSpells.Activate(_doors, id),
                    (door, forceMode) => EnterExteriorBuilding(door, forceMode, skipGreeting: false),
                    door => Entrance.Door(_doors, door, _activeProfileKey.Site), door => Entrance.Name(_doors, door, _activeProfileKey.Site),
                    Locks.DescribeDifficulty, door => Entrance.Discover(_doors, door, _activeProfileKey.Site)),
                new DaggerfallPortalActivationOwner(_sites.Projection.Portals,
                    logicalProfile => (_sites.Profiles ?? throw new InvalidOperationException("Site profiles have not been admitted."))
                        .RequireLogicalKey(logicalProfile),
                    EnterPortal),
                new DaggerfallGroundActivationOwner(() => _groundContainers.All.Values, PropertyLoot.InteractionPoint, id => _lootActions.OpenGround(id)),
                npc: people));
        Activation.Use(module, door);
    }

    /// <summary>Who needs exterior ground beside the player: active combatants who have its ground plane.</summary>
    private IEnumerable<Vector3> GroundWalkers() => State.Actors.All
        .Where(actor => actor.DurableId != DaggerfallActorIdentity.PlayerEntityId && !actor.IsDefeated
            && actor.Actor.TryGet<ActorHarmPolicy>(out ActorHarmPolicy? harm) && harm.IsCombatant)
        .Select(actor => actor.Position.ToVector());

    private DaggerfallHostileStandingSnapshot ReadHostileStanding(int region)
    {
        bool wantedOrBanished = LegalServices.IsWantedOrBanished(region);
        List<DaggerfallHostileFactionStanding> hostileGuilds = [];
        foreach (DaggerfallConcreteGuildDefinition guild in DaggerfallConcreteGuildCatalog.All)
        {
            int rep = State.Social.FactionReputation(guild.FactionId);
            if (rep > DaggerfallHostileOverlandBands.GuildHitSquadThreshold) continue;
            int mobile = guild.Kind switch
            {
                DaggerfallConcreteGuildKind.DarkBrotherhood => 139,
                DaggerfallConcreteGuildKind.Thieves => 138,
                DaggerfallConcreteGuildKind.Mages => 130,
                DaggerfallConcreteGuildKind.KnightlyOrder => 145,
                DaggerfallConcreteGuildKind.Fighters => 144,
                _ => 129,
            };
            hostileGuilds.Add(new(guild.FactionId, guild.DisplayName, rep, mobile));
        }
        List<DaggerfallHostileFactionStanding> hostileCourts = [];
        foreach (DaggerfallFactionDefinition faction in _definitions.Factions.Factions.Values)
        {
            if (faction.Region != region || faction.Type is not (1 or 14)) continue;
            int rep = State.Social.FactionReputation(faction.Id);
            if (rep <= DaggerfallHostileOverlandBands.RivalPatrolThreshold)
                hostileCourts.Add(new(faction.Id, faction.Name, rep, 145));
        }
        return new(wantedOrBanished, hostileGuilds, hostileCourts);
    }

    private bool HasWatchOrRulerVouch(DaggerfallSiteRecord? site)
    {
        if (site is null || OfficeHolders is null || OfficeLedger is null)
            return false;
        long? rulerId = OfficeHolders.ResolveRegionalRuler(site.Id.Region)?.ActorId;
        long? captainId = OfficeLedger.ResolveWatchCaptain(site).HolderActorId;
        if (rulerId is null && captainId is null)
            return false;
        foreach (ActorLedgerEntry entry in State.ActorLedger.All)
        {
            if (entry.Social?.Vouches is not { Length: > 0 } vouches)
                continue;
            if ((rulerId is long r && entry.DurableId == r) || (captainId is long c && entry.DurableId == c))
                return true;
            foreach (ActorVouchRecord vouch in vouches)
            {
                if ((rulerId is long r2 && vouch.ReferrerDurableId == r2)
                    || (captainId is long c2 && vouch.ReferrerDurableId == c2))
                    return true;
            }
        }
        return false;
    }

    private static DaggerfallSiteId? ToSiteId(DaggerfallSiteIdSave? id) => id?.Require();
}
