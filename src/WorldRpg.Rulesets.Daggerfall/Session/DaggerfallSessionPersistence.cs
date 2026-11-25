using System.Text.Json;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using Rusty.Engine;
using System.Numerics;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Property;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Explicit current-state capture and restoration; native presentation and input are reconstructed by composition.</summary>
internal sealed class DaggerfallSessionPersistence : IDaggerfallSiteDeltas
{
    private readonly DaggerfallState State;
    private readonly DaggerfallCorpseLootModule _corpseLoot;
    private readonly DaggerfallGroundContainers _groundContainers;
    private readonly DaggerfallEquipmentMoves _pack;
    private readonly DaggerfallBookNotebook _notebook;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly FirstPersonCameraSystem _camera;
    private readonly DaggerfallWorldTime _time;
    private readonly DaggerfallSiteContext _site;
    private readonly DaggerfallEffectLifecycle _effects;
    private readonly Func<DaggerfallDoorRuntime> _doors;
    private readonly DaggerfallLocomotionPolicy _locomotion;
    private readonly DaggerfallClimbingPolicy _climbing;
    private readonly DaggerfallSwimmingPolicy _swimming;
    private readonly DaggerfallDungeonText _dungeonText;
    private readonly Func<DaggerfallPropertyStorageKey, DaggerfallInventorySave> _capturePropertyStorage;
    private readonly Func<DaggerfallExpeditionSave> _expedition;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _actorDefinitions;
    private readonly Func<long> _nextCastSequence;
    internal Func<DaggerfallInfectionsSave> Infections { get; set; } = () => DaggerfallInfectionsSave.Empty;
    internal Func<DaggerfallWorldVariantSave[]> WorldVariants { get; set; } = () => [];
    internal Func<DaggerfallSummoningSave> Summoning { get; set; } = () => DaggerfallSummoningSave.Empty;
    internal Func<DaggerfallHandEquipCountdown> EquipCountdown { get; set; } = () => new(0, 0);
    internal Func<long> PoisonDraws { get; set; } = () => 0;
    internal Func<DaggerfallWeatherSave> Weather { get; set; } = null!;
    internal Func<DaggerfallReadySpell?> ReadySpell {get;set;}=()=>null;
    internal Func<DaggerfallCreateItemRequest?> PendingCreateItem { get; set; } = () => null;
    internal Func<DaggerfallIdentifyRequest?> PendingIdentify { get; set; } = () => null;
    internal Func<DaggerfallDispelRequest?> PendingDispel { get; set; } = () => null;
    internal Func<DaggerfallTutorialProgress?> Tutorial { get; set; } = () => null;
    internal Func<string?> PendingTeleport { get; set; } = () => null;
    internal Func<double> PlaySeconds { get; set; } = () => 0;
    internal Func<ulong> GameSeed { get; set; } = () => 0;
    internal Func<ulong> WorldSocialSeed { get; set; } = () => 0;
    internal Func<DaggerfallTeleportAnchor?> TeleportAnchor { get; set; } = () => null;
    internal Func<IReadOnlySet<long>> BanishedActors { get; set; } = () => new HashSet<long>();
    internal DaggerfallSessionPersistence(DaggerfallState state, DaggerfallCorpseLootModule corpses, DaggerfallGroundContainers groundContainers, DaggerfallEquipmentMoves pack, DaggerfallBookNotebook notebook,
        DaggerfallUniqueItemAllocator uniqueItems, FirstPersonCameraSystem camera, DaggerfallWorldTime time, DaggerfallSiteContext site,
        DaggerfallEffectLifecycle effects, Func<DaggerfallDoorRuntime> doors, DaggerfallLocomotionPolicy locomotion,
        DaggerfallClimbingPolicy climbing, DaggerfallSwimmingPolicy swimming, DaggerfallDungeonText dungeonText,
        Func<DaggerfallPropertyStorageKey, DaggerfallInventorySave> capturePropertyStorage, Func<DaggerfallExpeditionSave> expedition, IReadOnlyDictionary<long, DaggerfallActorDefinition> actorDefinitions, Func<long> nextCastSequence)
    {
        _nextCastSequence = nextCastSequence;
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(locomotion);
        ArgumentNullException.ThrowIfNull(climbing);
        ArgumentNullException.ThrowIfNull(swimming);
        ArgumentNullException.ThrowIfNull(dungeonText);
        ArgumentNullException.ThrowIfNull(capturePropertyStorage);
        _actorDefinitions = actorDefinitions;
        State = state; _corpseLoot = corpses; _groundContainers = groundContainers ?? throw new ArgumentNullException(nameof(groundContainers)); _pack = pack ?? throw new ArgumentNullException(nameof(pack)); _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook)); _uniqueItems = uniqueItems; _camera = camera; _time = time; _site = site; _effects = effects; _doors = doors; _locomotion = locomotion; _climbing = climbing; _swimming = swimming; _dungeonText = dungeonText;
        _capturePropertyStorage = capturePropertyStorage;
        _expedition = expedition ?? throw new ArgumentNullException(nameof(expedition));
    }
    internal static ActorSocialDiff ToSocialDiff(DaggerfallNpc npc, ActorSocialDiff? prior = null)
    {
        ArgumentNullException.ThrowIfNull(npc);
        return ActorSocialDiff.FromNpc(npc, prior);
    }

    internal RulesetSavePayload Capture(ulong? generation, ulong? step, IReadOnlyDictionary<long, DaggerfallActorId> dynamicActors, DaggerfallEncounterRuntime encounters, DaggerfallSiteDeltaSave[] siteDeltas, IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> inactiveDeltas, DaggerfallWorldProfileKey activeProfile, DaggerfallWorldProfileKey? returnProfile, IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallDungeonDiscovery> dungeonDiscoveries, IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallDungeonActionGraph> dungeonActions, DaggerfallDungeonMotionSnapshot dungeonMotion, DaggerfallExteriorCellResidencySave? exteriorResidency, DaggerfallExteriorLocationResidencySave? exteriorLocationResidency = null, Func<WorldPoint, WorldPoint>? localToProfile = null)
    {
        ArgumentNullException.ThrowIfNull(dynamicActors);
        ArgumentNullException.ThrowIfNull(encounters);
        ArgumentNullException.ThrowIfNull(siteDeltas);
        ArgumentNullException.ThrowIfNull(inactiveDeltas);
        ArgumentNullException.ThrowIfNull(dungeonDiscoveries);
        ArgumentNullException.ThrowIfNull(dungeonActions);
        ArgumentNullException.ThrowIfNull(dungeonMotion);
        PlayerControlState control = State.PlayerControl;
        WorldPoint playerPosition = control.Position
            ?? throw new InvalidOperationException("Daggerfall cannot save without a player position.");
        DaggerfallPlayerSave player = new(
            playerPosition.X, playerPosition.Y, playerPosition.Z,
            control.YawRadians, control.PitchRadians,
            DaggerfallStatsSaveBoundary.Capture(State.Actors.Player.Stats, State.Actors.Player.Actor.Entity))
        { WeaponDrawn = State.WeaponDrawn, Crouched = control.Motion.Stance == CharacterStance.Crouched };
        Dictionary<long, DaggerfallActorInventorySave> capturedActorInventories = State.ActorInventories.All.OrderBy(entry => entry.Key)
            .ToDictionary(entry => entry.Key, entry => new DaggerfallActorInventorySave(entry.Key,
                CaptureInventory(entry.Value, State.ActorInventories.EquipmentFor(entry.Key), DaggerfallItemOwner.Actor(entry.Key))));
        (ActorLedgerEntry[] actorLedger, HashSet<long> prunedDeterministicIds) = CaptureActorLedger(
            dynamicActors, inactiveDeltas, activeProfile, capturedActorInventories, localToProfile ?? (p => p));
        DaggerfallInventorySave inventorySave = CaptureInventory(State.Inventory, State.Equipment, DaggerfallItemOwner.Player);
        DaggerfallCorpseSave[] corpses = _corpseLoot.Corpses.Values.OrderBy(corpse => corpse.ActorId).Select(corpse =>
        {
            // Corpses never carry equipment: anything worn stays on the actor's own inventory
            // section, so the corpse save shares the stack/unique contents mapping only.
            (DaggerfallStackSave[] stacks, DaggerfallUniqueSave[] uniques) = corpse.IsRegistered
                ? CaptureContents(State.Containers.Read(corpse.Owner), DaggerfallItemOwner.Corpse(corpse.ActorId))
                : ([], []);
            return new DaggerfallCorpseSave(
                corpse.ActorId,
                corpse.ContainerIdentity.Value,
                corpse.OriginatingSequence,
                corpse.IsRegistered,
                corpse.IsInteractable,
                stacks,
                uniques);
        }).ToArray();
        DaggerfallActorInventorySave[] actorInventories = capturedActorInventories.Values
            .Where(inv => !prunedDeterministicIds.Contains(inv.EntityId))
            .OrderBy(inv => inv.EntityId)
            .ToArray();
        return DaggerfallSavePayload.Encode(new DaggerfallSavePayload(
            player,
            actorLedger,
            State.Progression.Experience,
            State.Progression.Level,
            inventorySave,
            corpses,
            _uniqueItems.CaptureState(),
            State.Kit.AttackExecution.CaptureCooldowns(generation, step)
                .Select(value => new DaggerfallCombatCooldownSave(value.AttackerId, value.RemainingSteps)).ToArray(),
            new DaggerfallCalendarSave(_time.Calendar.Year, _time.Calendar.Month, _time.Calendar.Day, _time.Calendar.Hour, _time.Calendar.Minute, _time.Calendar.Second, _time.RemainderSeconds),
            _site.Capture() with { ActiveProfile = DaggerfallWorldProfileKeySave.Capture(activeProfile), ReturnProfile = returnProfile is { } returned ? DaggerfallWorldProfileKeySave.Capture(returned) : null },
            actorInventories,
            new DaggerfallVariablesSave([.. State.Variables.Capture().Select(entry => new DaggerfallVariableSave((int)entry.Address.Scope, entry.Address.Owner, entry.Address.Key, entry.Value))]),
            _effects.Capture(),
            State.SkillUses.Capture(),
            State.Social.Capture(),
            Character: State.Character.Capture(),
            LevelUp: State.LevelUps.Capture())
        {
            CustomSpells = State.Character.CaptureConstructedSpells(),
            MagicRounds = State.Effects.MagicRounds,
            Infections = Infections(),
            NextCastSequence = _nextCastSequence(),
            Pack = _pack.CaptureLayout(),
            PoisonDraws = PoisonDraws(),
            ReadySpell=ReadySpell(),
            PendingCreateItem = PendingCreateItem(),
            PendingDispel = PendingDispel(),
            PendingTeleport = PendingTeleport(),
            PlaySeconds = PlaySeconds(),
            GameSeed = GameSeed(),
            WorldSocialSeed = WorldSocialSeed(),
            TeleportAnchor = TeleportAnchor(),
            PendingIdentify = PendingIdentify(),
            Tutorial = Tutorial(),
            Quests = State.Quests.Capture(),
            Doors = _doors().Capture(),
            SiteDeltas = siteDeltas,
            DungeonMotion = dungeonMotion,
            ExteriorResidency = exteriorResidency,
            ExteriorLocationResidency = exteriorLocationResidency,
            Currency = State.Currency.Capture(),
            Bank = State.Bank.Capture(),
            Loans = State.Loans.Capture(),
            Property = State.Property.Capture(_capturePropertyStorage),
            Lodging = State.Lodging.Capture(_time.Calendar.ToAbsoluteSeconds()),
            Expedition = _expedition(),
            Crime = State.Crime.Capture(),
            KnightlyClaims = State.KnightlyClaims.Capture(),
            Services = State.Services.Capture(),
            LastTavernMealMinute = State.Tavern.LastMealMinute,
            Merchants = [.. State.Merchants.Capture()],
            QuestTraining = State.QuestTraining.Capture(),
            RegionalPrices = State.RegionalPrices.Capture(),
            Weather = Weather(),
            Summoning = Summoning(),
            WorldVariants = WorldVariants(),
            Transport = State.Transport.Capture(),
            Swimming = State.Swimming.Capture(),
            EquipCountdown = EquipCountdown(),
            DialogueWorld = State.DialogueWorld.Capture(),
            Wagon = State.Wagon.Capture(),
            Locomotion = _locomotion.Capture(),
            Climbing = _climbing.Capture(),
            DungeonDiscovery = dungeonDiscoveries.Values.OrderBy(value => value.Profile.Site.Region)
                .ThenBy(value => value.Profile.Site.Index).ThenBy(value => value.Profile.LogicalId, StringComparer.Ordinal)
                .Select(value => value.Capture()).ToArray(),
            DungeonActions = dungeonActions.Values.OrderBy(value => value.ProfileId, StringComparer.Ordinal)
                .Select(value => value.Capture()).ToArray(),
            DungeonText = _dungeonText.Capture(),
            Encounters = encounters.Capture(),
            Notebook = _notebook.Capture(),
            QuestCustody = State.QuestItems.Custody.OrderBy(value => value.Id).Select(value =>
            {
                var (stacks, uniques) = CaptureContents(State.Containers.Read(value.Owner), DaggerfallItemOwner.Quest(value.Id));
                return new DaggerfallQuestCustodySave(value.InstanceId, value.Id, new(stacks, uniques, []));
            }).ToArray(),
            GroundContainers = _groundContainers.Capture(),
        });
    }

    private (ActorLedgerEntry[] Entries, HashSet<long> PrunedDeterministicIds) CaptureActorLedger(
        IReadOnlyDictionary<long, DaggerfallActorId> dynamicActors,
        IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> inactiveDeltas,
        DaggerfallWorldProfileKey activeProfile,
        IReadOnlyDictionary<long, DaggerfallActorInventorySave> actorInventories,
        Func<WorldPoint, WorldPoint> localToProfile)
    {
        Dictionary<long, ActorLedgerEntry> entries = [];
        HashSet<long> prunedDeterministicIds = [];
        HashSet<long> capturedLiveDeterministicIds = [];
        HashSet<long> referencedNpcs = CollectReferencedNpcs();

        // 1. Active-site live deterministic actors (FixedPlacement, SeededSlot, CastIndividual) — origin-aware sparse capture.
        // Untouched baseline entries self-prune to zero bytes in both ActorLedger and ActorInventories.
        foreach (ActorState actor in State.Actors.All
            .Where(actor => !dynamicActors.ContainsKey(actor.DurableId))
            .OrderBy(actor => actor.DurableId))
        {
            capturedLiveDeterministicIds.Add(actor.DurableId);
            ActorLedgerEntry sparse = CaptureSparseDeterministicActor(actor, activeProfile, localToProfile, referencedNpcs,
                actorInventories.TryGetValue(actor.DurableId, out DaggerfallActorInventorySave? inv) ? inv : null,
                out bool inventoryIsDefault);
            bool hasCorpseOrEffect = _corpseLoot.Corpses.ContainsKey(actor.DurableId)
                || State.Effects.Active.Any(e => e.Context.Target.Value == checked((ulong)actor.DurableId));
            if (sparse.IsEmptyBaseline && inventoryIsDefault && !hasCorpseOrEffect)
            {
                prunedDeterministicIds.Add(actor.DurableId);
                continue;
            }
            if (!sparse.IsEmptyBaseline)
                entries[actor.DurableId] = sparse;
            if (inventoryIsDefault && !hasCorpseOrEffect)
                prunedDeterministicIds.Add(actor.DurableId);
        }

        // 2. Active-site live dynamically minted actors (reading live person/overlay metadata directly from EntityStore)
        foreach (KeyValuePair<long, DaggerfallActorId> dynEntry in dynamicActors.OrderBy(entry => entry.Key))
        {
            DaggerfallDynamicActorSave dyn = CaptureDynamicActor(dynEntry.Key, dynEntry.Value.Value);
            State.ActorLedger.TryGet(dyn.EntityId, out ActorLedgerEntry? priorDynLedger);
            ActorSocialDiff? dynSocial = CaptureLiveDynamicSocial(dyn.EntityId, priorDynLedger?.Social);
            ActorPoseOverride dynPose = new(dyn.X, dyn.Y, dyn.Z, dyn.HeadingRadians);
            bool dynDefeated = State.Actors.TryGet(dyn.EntityId, out ActorState? liveDyn) && liveDyn.IsDefeated;
            entries[dyn.EntityId] = new ActorLedgerEntry(
                dyn.EntityId,
                ActorOrigin.MintedActor(dyn.Definition, dyn.Level, gender: dyn.Gender),
                Vitals: new ActorVitalsDiff(dyn.Stats),
                Pose: dynPose,
                Combat: ToDynamicCombatDiff(dyn),
                Social: dynSocial,
                Location: dynDefeated
                    ? ActorLocationOverride.Dead(activeProfile, dynPose)
                    : ActorLocationOverride.AtSite(activeProfile),
                Referral: priorDynLedger?.Referral);
        }

        // 3. Inactive-site (and unloaded active exterior) detached actors + banished placements
        foreach (KeyValuePair<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> siteEntry in inactiveDeltas
            .OrderBy(entry => entry.Key.Site.Region)
            .ThenBy(entry => entry.Key.Site.Index)
            .ThenBy(entry => entry.Key.LogicalId, StringComparer.Ordinal))
        {
            DaggerfallWorldProfileKey profile = siteEntry.Key;
            DaggerfallSiteRuntimeDelta delta = siteEntry.Value;
            Dictionary<long, DaggerfallActorInventorySave> inventoriesByActor = delta.ActorInventories.ToDictionary(i => i.EntityId);
            Dictionary<long, DaggerfallCorpseSave> corpsesByActor = delta.Corpses.ToDictionary(c => c.ActorId);
            Dictionary<long, DaggerfallActiveEffectSave[]> effectsByActor = delta.Effects
                .GroupBy(e => e.TargetId)
                .ToDictionary(g => g.Key, g => g.ToArray());

            foreach (DaggerfallActorSave actor in delta.Actors.OrderBy(a => a.EntityId))
            {
                inventoriesByActor.TryGetValue(actor.EntityId, out DaggerfallActorInventorySave? inv);
                corpsesByActor.TryGetValue(actor.EntityId, out DaggerfallCorpseSave? corpse);
                effectsByActor.TryGetValue(actor.EntityId, out DaggerfallActiveEffectSave[]? effects);
                ActorCombatStateDiff? combat = actor.WabbajackDefinition is not null || actor.ForcedHostile || actor.LanguagePacified || actor.MagicallyPacified
                    || inv is not null || corpse is not null || effects is { Length: > 0 }
                    ? new ActorCombatStateDiff(
                        WabbajackDefinition: actor.WabbajackDefinition,
                        ForcedHostile: actor.ForcedHostile,
                        LanguagePacified: actor.LanguagePacified,
                        MagicallyPacified: actor.MagicallyPacified,
                        DetachedInventory: inv,
                        DetachedCorpse: corpse,
                        DetachedEffects: effects is { Length: > 0 } ? effects : null)
                    : null;
                entries[actor.EntityId] = new ActorLedgerEntry(
                    actor.EntityId,
                    ActorOrigin.FixedPlacement(profile, actor.EntityId),
                    Vitals: new ActorVitalsDiff(actor.Stats),
                    Pose: new ActorPoseOverride(actor.X, actor.Y, actor.Z, actor.HeadingRadians),
                    Combat: combat,
                    Location: ActorLocationOverride.AtSite(profile, detached: true));
            }

            foreach (DaggerfallDynamicActorSave dyn in delta.DynamicActors.OrderBy(a => a.EntityId))
            {
                inventoriesByActor.TryGetValue(dyn.EntityId, out DaggerfallActorInventorySave? inv);
                corpsesByActor.TryGetValue(dyn.EntityId, out DaggerfallCorpseSave? corpse);
                effectsByActor.TryGetValue(dyn.EntityId, out DaggerfallActiveEffectSave[]? effects);
                State.ActorLedger.TryGet(dyn.EntityId, out ActorLedgerEntry? priorDetachedLedger);
                entries[dyn.EntityId] = new ActorLedgerEntry(
                    dyn.EntityId,
                    ActorOrigin.MintedActor(dyn.Definition, dyn.Level, gender: dyn.Gender),
                    Vitals: new ActorVitalsDiff(dyn.Stats),
                    Pose: new ActorPoseOverride(dyn.X, dyn.Y, dyn.Z, dyn.HeadingRadians),
                    Combat: ToDynamicCombatDiff(dyn, inv, corpse, effects),
                    Social: priorDetachedLedger?.Social,
                    Location: ActorLocationOverride.AtSite(profile, detached: true),
                    Referral: priorDetachedLedger?.Referral);
            }

            foreach (long banishedId in delta.BanishedActors.Order())
            {
                entries[banishedId] = new ActorLedgerEntry(
                    banishedId,
                    ActorOrigin.FixedPlacement(profile, banishedId),
                    Location: ActorLocationOverride.Removed);
            }
        }

        // 4. Active-site banished authored placements not already captured in an inactive delta
        foreach (long banishedId in BanishedActors().Order())
        {
            if (entries.ContainsKey(banishedId)) continue;
            entries[banishedId] = new ActorLedgerEntry(
                banishedId,
                ActorOrigin.FixedPlacement(activeProfile, banishedId),
                Location: ActorLocationOverride.Removed);
        }

        // 5. Carry forward remaining non-empty sparse ledger entries (off-scene SeededSlot/CastIndividual,
        // unmaterialized/hidden/removed NPCs, WithParty, OnOverlandBand, OffStage, pre-visit referrals),
        // never resurrecting retired non-person Minted AtSite entries or stale entries for live deterministic
        // actors whose current state was freshly captured/pruned in Step 1.
        foreach (ActorLedgerEntry extra in State.ActorLedger.All)
        {
            if (extra.IsEmptyBaseline || entries.ContainsKey(extra.DurableId)
                || capturedLiveDeterministicIds.Contains(extra.DurableId))
                continue;
            if (extra.Origin.Kind == ActorOriginKind.Minted && extra.Social is null
                && extra.Location?.Kind is not (ActorLocationKind.WithParty or ActorLocationKind.OnOverlandBand or ActorLocationKind.OffStage))
                continue;
            if (extra.Location is null && extra.Social is not null && extra.Origin.Kind == ActorOriginKind.Minted)
            {
                entries[extra.DurableId] = extra with
                {
                    Location = extra.Social.Presence == (int)DaggerfallNpcPresence.Removed
                        ? ActorLocationOverride.Removed
                        : ActorLocationOverride.OffStage,
                };
                continue;
            }
            entries[extra.DurableId] = extra;
        }

        return ([.. entries.Values.Where(entry => !entry.IsEmptyBaseline).OrderBy(entry => entry.DurableId)], prunedDeterministicIds);
    }

    private ActorSocialDiff? CaptureLiveDynamicSocial(long actorId, ActorSocialDiff? priorSocial)
    {
        if (!State.Actors.TryGet(actorId, out ActorState? liveActor)) return priorSocial;
        if (!liveActor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person)) return priorSocial;
        ActorSocialDiff social = ActorSocialDiff.FromNpc(person.Snapshot, priorSocial);
        if (liveActor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? liveOverlay))
        {
            string[]? liveWitnessed = liveOverlay.WitnessedCrimes.Count > 0
                ? [.. liveOverlay.WitnessedCrimes]
                : social.WitnessedCrimes;
            string[]? liveReferrals = liveOverlay.ReferralTags.Count > 0
                ? [.. liveOverlay.ReferralTags]
                : social.ReferralTags;
            ActorPartyRole? liveStagedRole = liveOverlay.StagedPartyRole is { Length: > 0 } rawDynRole
                && Enum.TryParse<ActorPartyRole>(rawDynRole, out ActorPartyRole parsedDynRole)
                ? parsedDynRole
                : social.StagedPartyRole;
            social = social with
            {
                Met = liveOverlay.Met || social.Met,
                Muted = liveOverlay.IsMuted,
                WitnessedCrimes = liveWitnessed,
                ReferralTags = liveReferrals,
                EscortPortrait = liveOverlay.EscortPortraitMedia ?? social.EscortPortrait,
                EscortOrder = liveOverlay.EscortOrder > 0 ? liveOverlay.EscortOrder : social.EscortOrder,
                StagedPartyRole = liveStagedRole,
                FamilyId = !string.IsNullOrWhiteSpace(liveOverlay.FamilyId) ? liveOverlay.FamilyId : social.FamilyId,
            };
        }
        return social;
    }

    private HashSet<long> CollectReferencedNpcs()
    {
        HashSet<long> ids = [];
        foreach (DaggerfallMerchantSave merchant in State.Merchants.Capture()) ids.Add(merchant.ProviderNpcId);
        foreach (DaggerfallServiceQueuedWork pending in State.Services.Capture().Pending) ids.Add(pending.Provider.NpcId);
        DaggerfallQuestInstancesSave quests = State.Quests.Capture();
        foreach (DaggerfallQuestWorkContact contact in quests.WorkPool.Contacts) ids.Add(contact.Npc);
        if (quests.PendingOffer?.Quest.QuestorId is long offerGiver) ids.Add(offerGiver);
        foreach (DaggerfallQuestInstanceSave instance in quests.Instances)
        {
            if (instance.QuestorId is long qid) ids.Add(qid);
            foreach (DaggerfallQuestResourceState resource in instance.Resources)
            {
                if (resource.SelectedPerson?.QuestorId is long pqid) ids.Add(pqid);
                foreach (long aid in resource.Binding.ActorIds) ids.Add(aid);
            }
        }
        return ids;
    }

    private ActorLedgerEntry CaptureSparseDeterministicActor(
        ActorState actor,
        DaggerfallWorldProfileKey activeProfile,
        Func<WorldPoint, WorldPoint> localToProfile,
        HashSet<long> referencedNpcs,
        DaggerfallActorInventorySave? capturedInventory,
        out bool inventoryIsDefault)
    {
        ActorOriginComponent? originComp = actor.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? oc) ? oc : null;
        ActorOrigin origin = originComp?.Origin ?? ActorOrigin.FixedPlacement(activeProfile, actor.DurableId);
        DaggerfallStatsSave currentStats = DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity);
        ActorVitalsDiff? vitals = originComp?.BaselineStats is { } baseStats && StatsEqual(currentStats, baseStats)
            ? null
            : new ActorVitalsDiff(currentStats);

        bool relocated = (originComp?.IsRelocated == true)
            || (origin.SiteProfile is { } homeProfile && homeProfile.Require() != activeProfile);

        WorldPoint currentPos = origin.IsFixedPlacement ? actor.Position : localToProfile(actor.Position);
        DaggerfallEnemyPerceptionMemory memory = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>();
        string? wabbajack = DaggerfallWabbajack.DefinitionOf(actor.Actor);
        string? teamOverride = actor.Actor.TryGet<FactionComponent>(out FactionComponent? faction) ? faction.TeamOverride : null;
        ActorOverlayComponent? overlay = actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov) ? ov : null;
        string? promotedArchetype = string.Equals(overlay?.ArchetypeId, DaggerfallArchetypeIds.Guard, StringComparison.Ordinal)
            ? DaggerfallArchetypeIds.Guard
            : null;
        ActorCombatStateDiff? combat = wabbajack is not null || memory.ForcedHostile || memory.LanguagePacified
            || memory.MagicallyPacified || teamOverride is not null || promotedArchetype is not null
            ? new ActorCombatStateDiff(
                WabbajackDefinition: wabbajack,
                ForcedHostile: memory.ForcedHostile,
                LanguagePacified: memory.LanguagePacified,
                MagicallyPacified: memory.MagicallyPacified,
                TeamOverride: teamOverride,
                PromotedArchetype: promotedArchetype)
            : null;

        State.ActorLedger.TryGet(actor.DurableId, out ActorLedgerEntry? priorLocationLedger);
        bool trafficVisitor = priorLocationLedger?.Location?.TrafficVisitor == true && !actor.IsDefeated;
        ActorPoseOverride currentPoseOverride = new(currentPos.X, currentPos.Y, currentPos.Z, actor.HeadingYawRadians);
        ActorLocationOverride? location = actor.IsDefeated
            ? ActorLocationOverride.Dead(activeProfile, currentPoseOverride)
            : relocated
                ? ActorLocationOverride.AtSite(activeProfile, trafficVisitor: trafficVisitor)
                : null;

        ActorSocialDiff? social = null;
        ActorReferralDiff? referral = null;
        if (!origin.IsFixedPlacement && State.Npcs.TryGet(actor.DurableId, out DaggerfallNpc? npc))
        {
            State.ActorLedger.TryGet(actor.DurableId, out ActorLedgerEntry? priorLedger);
            bool met = overlay is not null ? overlay.Met : priorLedger?.Social?.Met == true;
            bool muted = overlay is not null ? overlay.IsMuted : priorLedger?.Social?.Muted == true;
            string[]? witnessedCrimes = overlay is { WitnessedCrimes.Count: > 0 }
                ? [.. overlay.WitnessedCrimes]
                : priorLedger?.Social?.WitnessedCrimes;
            string[]? referralTags = overlay is { ReferralTags.Count: > 0 }
                ? [.. overlay.ReferralTags]
                : priorLedger?.Referral?.Tags ?? priorLedger?.Social?.ReferralTags;
            string? referralName = priorLedger?.Referral?.DisplayNameOverride;
            if (referralTags is { Length: > 0 })
                referral = new ActorReferralDiff(referralTags, referralName);
            bool nameChanged = originComp?.BaselineNpc is { DisplayName: not null } baseNpc
                && !StringComparer.Ordinal.Equals(npc.DisplayName, baseNpc.DisplayName)
                && !StringComparer.Ordinal.Equals(npc.DisplayName, referralName);
            bool presenceChanged = npc.Presence != DaggerfallNpcPresence.Active;
            ActorPartyRole? stagedPartyRole = actor.IsDefeated
                ? null
                : overlay?.StagedPartyRole is { Length: > 0 } rawRole
                    && Enum.TryParse<ActorPartyRole>(rawRole, out ActorPartyRole parsedRole)
                    ? parsedRole
                    : priorLedger?.Social?.StagedPartyRole;
            string? escortPortrait = overlay is not null && stagedPartyRole is not null
                ? overlay.EscortPortraitMedia ?? priorLedger?.Social?.EscortPortrait
                : priorLedger?.Social?.EscortPortrait;
            int escortOrder = overlay is not null && stagedPartyRole is not null && overlay.EscortOrder > 0
                ? overlay.EscortOrder
                : priorLedger?.Social?.EscortOrder ?? 0;
            string? familyId = !string.IsNullOrWhiteSpace(overlay?.FamilyId)
                ? overlay.FamilyId
                : priorLedger?.Social?.FamilyId;
            bool hasPriorSocialDelta = priorLedger?.Social is { } ps
                && (ps.DispositionDelta != 0 || ps.WitnessedCrimes is { Length: > 0 } || ps.ReferralTags is { Length: > 0 } || ps.EscortPortrait is not null || ps.StagedPartyRole is not null || ps.FamilyId is not null);
            bool mustKeepSocial = met || muted || witnessedCrimes is { Length: > 0 } || promotedArchetype is not null
                || stagedPartyRole is not null || familyId is not null || nameChanged || presenceChanged || hasPriorSocialDelta
                || relocated || referencedNpcs.Contains(actor.DurableId);
            if (mustKeepSocial)
            {
                social = ToSocialDiff(npc, priorLedger?.Social) with
                {
                    Met = met,
                    Muted = muted,
                    WitnessedCrimes = witnessedCrimes,
                    ReferralTags = referralTags,
                    EscortPortrait = escortPortrait,
                    EscortOrder = escortOrder,
                    StagedPartyRole = stagedPartyRole,
                    FamilyId = familyId,
                };
            }
        }

        inventoryIsDefault = IsDefaultActorInventory(actor.DurableId, origin, capturedInventory);
        bool hasNonPoseDelta = vitals is not null || combat is not null || social is not null || referral is not null || relocated || location is not null || !inventoryIsDefault;
        bool idleWanderOnly = !hasNonPoseDelta && originComp is not null
            && originComp.MatchesIdleWanderPose(currentPos, actor.HeadingYawRadians);
        ActorPoseOverride? pose = (!relocated && !actor.IsDefeated && originComp is not null
            && (PoseEqual(currentPos, actor.HeadingYawRadians, originComp.BaselinePose, originComp.BaselineHeading) || idleWanderOnly))
            ? null
            : currentPoseOverride;
        return new ActorLedgerEntry(actor.DurableId, origin, vitals, pose, combat, social, location, referral);
    }

    /// <summary>
    /// Writes <see cref="ActorLocationKind.Dead"/> into <see cref="DaggerfallActorLedger"/> immediately when a
    /// deterministic or ledger-tracked actor dies so death is persisted before save or site unload.
    /// </summary>
    internal void RecordActorDeath(
        ActorState fallen,
        DaggerfallWorldProfileKey activeProfile,
        Func<WorldPoint, WorldPoint> localToProfile)
    {
        ArgumentNullException.ThrowIfNull(fallen);
        bool hasPrior = State.ActorLedger.TryGet(fallen.DurableId, out ActorLedgerEntry? prior) && prior is not null;
        ActorOriginComponent? originComp = fallen.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? oc) ? oc : null;
        ActorOrigin? origin = prior?.Origin ?? originComp?.Origin;
        if (origin is null) return;
        if (!hasPrior && (origin.Kind == ActorOriginKind.Minted || origin.IsFixedPlacement)) return;

        WorldPoint profilePos = origin.IsFixedPlacement ? fallen.Position : localToProfile(fallen.Position);
        ActorPoseOverride corpsePose = new(profilePos.X, profilePos.Y, profilePos.Z, fallen.HeadingYawRadians);
        ActorVitalsDiff vitals = new(DaggerfallStatsSaveBoundary.Capture(fallen.Stats, fallen.Actor.Entity));
        ActorOverlayComponent? overlay = fallen.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov) ? ov : null;
        if (overlay is not null)
        {
            overlay.StagedPartyRole = null;
            overlay.EscortPortraitMedia = null;
            overlay.EscortOrder = 0;
        }

        ActorSocialDiff? social = prior?.Social;
        if (State.Npcs.TryGet(fallen.DurableId, out DaggerfallNpc? npc))
        {
            social = ToSocialDiff(npc, social) with
            {
                Met = overlay?.Met ?? social?.Met ?? false,
                Muted = overlay?.IsMuted ?? social?.Muted ?? false,
                WitnessedCrimes = overlay is { WitnessedCrimes.Count: > 0 } ? [.. overlay.WitnessedCrimes] : social?.WitnessedCrimes,
                ReferralTags = overlay is { ReferralTags.Count: > 0 } ? [.. overlay.ReferralTags] : social?.ReferralTags,
                EscortPortrait = null,
                EscortOrder = 0,
                StagedPartyRole = null,
            };
        }
        else if (social is not null)
        {
            social = social with { EscortPortrait = null, EscortOrder = 0, StagedPartyRole = null };
        }

        State.ActorLedger.Upsert(new ActorLedgerEntry(
            fallen.DurableId,
            origin,
            Vitals: vitals,
            Pose: corpsePose,
            Combat: prior?.Combat,
            Social: social,
            Location: ActorLocationOverride.Dead(activeProfile, corpsePose),
            Referral: prior?.Referral));
    }

    private bool IsDefaultActorInventory(long actorId, ActorOrigin origin, DaggerfallActorInventorySave? saved)
    {
        if (saved is null) return true;
        if (saved.Inventory.Equipment.Length > 0 || saved.Inventory.UniqueItems.Length > 0) return false;
        if (!_actorDefinitions.TryGetValue(actorId, out DaggerfallActorDefinition? definition))
            return saved.Inventory.Stacks.Length == 0;
        var expected = definition.Loadout
            .Where(entry => entry.UniqueEntityId is null)
            .Select((entry, ordinal) => (StackId: DaggerfallInventoryStackIds.ForInitialLoadout(actorId, ordinal).Value, ItemId: entry.ItemId.Value, entry.Quantity))
            .ToArray();
        if (saved.Inventory.Stacks.Length != expected.Length) return false;
        for (int i = 0; i < expected.Length; i++)
        {
            DaggerfallStackSave stack = saved.Inventory.Stacks[i];
            if (!StringComparer.Ordinal.Equals(stack.StackId, expected[i].StackId)
                || !StringComparer.Ordinal.Equals(stack.ItemId, expected[i].ItemId)
                || stack.Quantity != expected[i].Quantity)
                return false;
        }
        return true;
    }

    private static bool StatsEqual(DaggerfallStatsSave left, DaggerfallStatsSave right) =>
        JsonSerializer.SerializeToUtf8Bytes(left, DaggerfallSaveJsonContext.Default.DaggerfallStatsSave)
            .AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, DaggerfallSaveJsonContext.Default.DaggerfallStatsSave));

    private static bool PoseEqual(WorldPoint current, float heading, WorldPoint baseline, float baselineHeading) =>
        MathF.Abs(current.X - baseline.X) < 1e-4f
        && MathF.Abs(current.Y - baseline.Y) < 1e-4f
        && MathF.Abs(current.Z - baseline.Z) < 1e-4f
        && MathF.Abs(heading - baselineHeading) < 1e-4f;

    private static ActorCombatStateDiff? ToDynamicCombatDiff(DaggerfallDynamicActorSave dyn,
        DaggerfallActorInventorySave? detachedInventory = null,
        DaggerfallCorpseSave? detachedCorpse = null,
        DaggerfallActiveEffectSave[]? detachedEffects = null)
    {
        bool hasAny = dyn.SpawnDistance != 0 || dyn.Passive || dyn.WabbajackActive || dyn.ForcedHostile
            || dyn.LanguagePacified || dyn.MagicallyPacified || dyn.PlayerAllied || dyn.CorruptionOrigin is not null
            || dyn.TeamOverride is not null || dyn.PromotedArchetype is not null || dyn.EncounterBandId is not null
            || detachedInventory is not null || detachedCorpse is not null || detachedEffects is { Length: > 0 };
        if (!hasAny) return null;
        return new ActorCombatStateDiff(
            SpawnDistance: dyn.SpawnDistance,
            Passive: dyn.Passive,
            WabbajackActive: dyn.WabbajackActive,
            ForcedHostile: dyn.ForcedHostile,
            LanguagePacified: dyn.LanguagePacified,
            MagicallyPacified: dyn.MagicallyPacified,
            PlayerAllied: dyn.PlayerAllied,
            CorruptionOrigin: dyn.CorruptionOrigin,
            TeamOverride: dyn.TeamOverride,
            PromotedArchetype: dyn.PromotedArchetype,
            EncounterBandId: dyn.EncounterBandId,
            EncounterGeneration: dyn.EncounterGeneration,
            DetachedInventory: detachedInventory,
            DetachedCorpse: detachedCorpse,
            DetachedEffects: detachedEffects is { Length: > 0 } ? detachedEffects : null);
    }

    DaggerfallSiteRuntimeDelta IDaggerfallSiteDeltas.Capture(DaggerfallSiteProfile inputs, DaggerfallSiteProjection projection,
        IReadOnlyDictionary<long, DaggerfallActorId> dynamicActors, IReadOnlySet<long>? actorFilter, Vector3 actorFrameOffset) =>
        CaptureSiteDelta(inputs, projection.Doors, projection.Motion, dynamicActors, actorFilter, actorFrameOffset);

    void IDaggerfallSiteDeltas.Restore(DaggerfallSiteRuntimeDelta delta) => RestoreSiteDelta(delta);

    /// <summary>
    /// Captures one live actor's full canonical state (vitals, pose, combat, social, referral, non-default inventory,
    /// corpse, and active effects) into a sparse <see cref="ActorLedgerEntry"/> right before unloading it into
    /// travel custody (<see cref="ActorLocationKind.OnOverlandBand"/> or <see cref="ActorLocationKind.WithParty"/>).
    /// Returns null when <paramref name="actorId"/> is not currently live in <c>EntityStore</c>.
    /// </summary>
    internal ActorLedgerEntry? CaptureLiveActorForCustody(
        long actorId,
        DaggerfallWorldProfileKey activeProfile,
        Func<WorldPoint, WorldPoint> localToProfile)
    {
        if (!State.Actors.TryGet(actorId, out ActorState? actor)) return null;
        DaggerfallActorInventorySave? capturedInv = State.ActorInventories.InventoryFor(actorId) is { } invCoord
            ? new DaggerfallActorInventorySave(actorId,
                CaptureInventory(invCoord, State.ActorInventories.EquipmentFor(actorId), DaggerfallItemOwner.Actor(actorId)))
            : null;
        ActorLedgerEntry sparse = CaptureSparseDeterministicActor(
            actor, activeProfile, localToProfile, CollectReferencedNpcs(), capturedInv, out bool inventoryIsDefault);
        if (!actor.Actor.Has<ActorOriginComponent>())
        {
            State.ActorLedger.TryGet(actorId, out ActorLedgerEntry? priorOriginEntry);
            ActorOrigin resolvedOrigin;
            if (priorOriginEntry is not null && !priorOriginEntry.Origin.IsFixedPlacement)
            {
                resolvedOrigin = priorOriginEntry.Origin;
            }
            else if (State.Npcs.TryGet(actorId, out DaggerfallNpc? mintedNpc))
            {
                DaggerfallWorldProfileKey homeProfile = mintedNpc.Profile ?? activeProfile;
                resolvedOrigin = new ActorOrigin(
                    DaggerfallPeopleSlots.IsDeterministicId(actorId) ? ActorOriginKind.SeededSlot : ActorOriginKind.Minted,
                    SiteProfile: DaggerfallWorldProfileKeySave.Capture(homeProfile),
                    SlotKey: DaggerfallPeopleSlots.IsDeterministicId(actorId) ? mintedNpc.StableKey : null,
                    DefinitionId: DaggerfallPeopleSlots.IsDeterministicId(actorId)
                        ? null
                        : mintedNpc.Kind == DaggerfallNpcKind.Civilian ? DaggerfallActorKinds.Civilian : DaggerfallActorKinds.StaticNpc);
            }
            else if (_actorDefinitions.TryGetValue(actorId, out DaggerfallActorDefinition? def))
            {
                resolvedOrigin = ActorOrigin.MintedActor(def.Id.Value, def.Level ?? 1);
            }
            else
            {
                resolvedOrigin = sparse.Origin;
            }
            sparse = sparse with { Origin = resolvedOrigin };
        }
        if (sparse.Origin.SiteProfile is null && State.Npcs.TryGet(actorId, out DaggerfallNpc? npcWithSite))
        {
            sparse = sparse with
            {
                Origin = sparse.Origin with
                {
                    SiteProfile = DaggerfallWorldProfileKeySave.Capture(npcWithSite.Profile ?? activeProfile),
                },
            };
        }
        DaggerfallActorInventorySave? detachedInv = inventoryIsDefault ? null : capturedInv;
        DaggerfallCorpseSave? detachedCorpse = CaptureCorpses([actorId]).SingleOrDefault();
        DaggerfallActiveEffectSave[] detachedEffects = State.Effects.Capture()
            .Where(effect => effect.TargetId == actorId)
            .ToArray();
        ActorCombatStateDiff? combat = detachedInv is not null || detachedCorpse is not null || detachedEffects.Length > 0
            ? (sparse.Combat ?? new ActorCombatStateDiff()) with
            {
                DetachedInventory = detachedInv,
                DetachedCorpse = detachedCorpse,
                DetachedEffects = detachedEffects.Length > 0 ? detachedEffects : null,
            }
            : sparse.Combat;
        ActorSocialDiff? social = sparse.Social;
        if (social is null && State.Npcs.TryGet(actorId, out DaggerfallNpc? npc))
        {
            State.ActorLedger.TryGet(actorId, out ActorLedgerEntry? priorLedger);
            ActorOverlayComponent? overlay = actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov) ? ov : null;
            social = ToSocialDiff(npc, priorLedger?.Social) with
            {
                Met = overlay?.Met ?? priorLedger?.Social?.Met == true,
                Muted = overlay?.IsMuted ?? priorLedger?.Social?.Muted == true,
                WitnessedCrimes = overlay is { WitnessedCrimes.Count: > 0 } ? [.. overlay.WitnessedCrimes] : priorLedger?.Social?.WitnessedCrimes,
                ReferralTags = overlay is { ReferralTags.Count: > 0 } ? [.. overlay.ReferralTags] : priorLedger?.Social?.ReferralTags,
                EscortPortrait = overlay?.EscortPortraitMedia ?? priorLedger?.Social?.EscortPortrait,
                EscortOrder = overlay is { EscortOrder: > 0 } ? overlay.EscortOrder : priorLedger?.Social?.EscortOrder ?? 0,
                FamilyId = !string.IsNullOrWhiteSpace(overlay?.FamilyId) ? overlay.FamilyId : priorLedger?.Social?.FamilyId,
            };
        }
        return sparse with { Combat = combat, Social = social };
    }

    /// <summary>Captures one unloading site's actor-owned state without retaining runtime entities.</summary>
    internal DaggerfallSiteRuntimeDelta CaptureSiteDelta(DaggerfallSiteProfile inputs, DaggerfallDoorRuntime doors, DaggerfallDungeonMotionProjection motion,
        IReadOnlyDictionary<long, DaggerfallActorId> dynamicActors, IReadOnlySet<long>? actorFilter = null,
        Vector3 actorFrameOffset = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(dynamicActors);
        // Flush any sparse diffs (including non-default inventories, corpses, and effects) for deterministic
        // SeededSlot/CastIndividual actors leaving this site into State.ActorLedger (untouched slots self-prune).
        HashSet<long> referencedNpcs = CollectReferencedNpcs();
        DaggerfallActiveEffectSave[] allActiveEffects = State.Effects.Capture();
        foreach (ActorState actor in State.Actors.All)
        {
            if (dynamicActors.ContainsKey(actor.DurableId) || inputs.Project.Actors.ContainsKey(actor.DurableId)) continue;
            if (!actor.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? originComp)
                || originComp.Origin.Kind is not (ActorOriginKind.SeededSlot or ActorOriginKind.CastIndividual)
                || originComp.Origin.IsFixedPlacement)
                continue;
            if (State.Npcs.TryGet(actor.DurableId, out DaggerfallNpc? npc) && npc.Profile is { } npcProfile && npcProfile != inputs.ProfileKey)
                continue;
            DaggerfallActorInventorySave? capturedInv = State.ActorInventories.InventoryFor(actor.DurableId) is { } invCoord
                ? new DaggerfallActorInventorySave(actor.DurableId, CaptureInventory(invCoord, State.ActorInventories.EquipmentFor(actor.DurableId), DaggerfallItemOwner.Actor(actor.DurableId)))
                : null;
            ActorLedgerEntry sparse = CaptureSparseDeterministicActor(actor, inputs.ProfileKey,
                point => WorldPoint.From(point.ToVector() - actorFrameOffset), referencedNpcs, capturedInv, out bool inventoryIsDefault);
            DaggerfallActorInventorySave? detachedInv = inventoryIsDefault ? null : capturedInv;
            DaggerfallCorpseSave? detachedCorpse = CaptureCorpses([actor.DurableId]).SingleOrDefault();
            DaggerfallActiveEffectSave[] detachedEffects = allActiveEffects.Where(effect => effect.TargetId == actor.DurableId).ToArray();
            if (sparse.IsEmptyBaseline && detachedInv is null && detachedCorpse is null && detachedEffects.Length == 0)
            {
                State.ActorLedger.Remove(actor.DurableId);
                continue;
            }
            ActorCombatStateDiff? combat = detachedInv is not null || detachedCorpse is not null || detachedEffects.Length > 0
                ? (sparse.Combat ?? new ActorCombatStateDiff()) with
                {
                    DetachedInventory = detachedInv,
                    DetachedCorpse = detachedCorpse,
                    DetachedEffects = detachedEffects.Length > 0 ? detachedEffects : null,
                }
                : sparse.Combat;
            ActorLocationOverride location = sparse.Location is { Kind: ActorLocationKind.Dead }
                ? sparse.Location with { Detached = true }
                : sparse.Location is { Kind: ActorLocationKind.AtSite, Profile: { } locProfile }
                    ? ActorLocationOverride.AtSite(locProfile.Require(), sparse.Location.Pose, detached: true, trafficVisitor: sparse.Location.TrafficVisitor)
                    : ActorLocationOverride.AtSite(inputs.ProfileKey, detached: true);
            ActorSocialDiff? social = sparse.Social;
            if (social is null && actor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? detachedPerson))
            {
                State.ActorLedger.TryGet(actor.DurableId, out ActorLedgerEntry? priorDet);
                social = ActorSocialDiff.FromNpc(detachedPerson.Snapshot, priorDet?.Social);
            }
            State.ActorLedger.Upsert(sparse with { Combat = combat, Social = social, Location = location });
        }
        long[] authoredIds = [.. inputs.Project.Actors.Keys.Where(id => !BanishedActors().Contains(id) && State.Actors.TryGet(id, out _)).OrderBy(id => id)];
        DaggerfallActorSave[] actors = authoredIds.Select(id =>
        {
            ActorState actor = State.Actors.TryGet(id, out ActorState? current)
                ? current
                : throw new InvalidOperationException($"Site actor {id} disappeared before its site state could be captured.");
            Vector3 position = actor.Position.ToVector() - actorFrameOffset;
            return new DaggerfallActorSave(actor.DurableId, position.X, position.Y, position.Z,
                actor.HeadingYawRadians, DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity)) { WabbajackDefinition = DaggerfallWabbajack.DefinitionOf(actor.Actor), ForcedHostile = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile, LanguagePacified=actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().LanguagePacified, MagicallyPacified=actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().MagicallyPacified };
        }).ToArray();
        DaggerfallDynamicActorSave[] spawned = dynamicActors
            .Where(entry => actorFilter is null || actorFilter.Contains(entry.Key))
            .OrderBy(entry => entry.Key)
            .Select(entry => CaptureDynamicActor(entry.Key, entry.Value.Value, actorFrameOffset)).ToArray();
        // Flush live overlay/person metadata for dynamically minted NPCs leaving this site into ActorLedger
        // before UnloadSite destroys their EntityStore entities, so Met/Muted/WitnessedCrimes/StagedPartyRole survive.
        foreach (DaggerfallDynamicActorSave dyn in spawned)
        {
            State.ActorLedger.TryGet(dyn.EntityId, out ActorLedgerEntry? priorDyn);
            if (CaptureLiveDynamicSocial(dyn.EntityId, priorDyn?.Social) is not { } liveSocial) continue;
            ActorOrigin origin = priorDyn?.Origin ?? ActorOrigin.MintedActor(dyn.Definition, dyn.Level, gender: dyn.Gender);
            bool dynDead = State.Actors.TryGet(dyn.EntityId, out ActorState? liveDyn) && liveDyn.IsDefeated;
            ActorLocationOverride dynLoc = dynDead
                ? ActorLocationOverride.Dead(inputs.ProfileKey, new ActorPoseOverride(dyn.X, dyn.Y, dyn.Z, dyn.HeadingRadians)) with { Detached = true }
                : ActorLocationOverride.AtSite(inputs.ProfileKey, detached: true);
            State.ActorLedger.Upsert(priorDyn is not null
                ? priorDyn with { Social = liveSocial, Location = dynLoc }
                : new ActorLedgerEntry(dyn.EntityId, origin, Social: liveSocial, Location: dynLoc));
        }
        long[] ids = [.. authoredIds, .. spawned.Select(actor => actor.EntityId)];
        DaggerfallActorInventorySave[] inventories = ids.Select(id => new DaggerfallActorInventorySave(
            id,
            CaptureInventory(State.ActorInventories.InventoryFor(id) ?? throw new InvalidOperationException($"Site actor {id} has no inventory."),
                State.ActorInventories.EquipmentFor(id), DaggerfallItemOwner.Actor(id)))).ToArray();
        DaggerfallActiveEffectSave[] effects = State.Effects.Capture().Where(effect => ids.Contains(effect.TargetId)).ToArray();
        return new DaggerfallSiteRuntimeDelta(actors, spawned, inventories, CaptureCorpses(ids), doors.Capture(), effects, motion.Capture()) { BanishedActors = [.. BanishedActors().Where(inputs.Project.Actors.ContainsKey).Order()] };
    }

    private DaggerfallDynamicActorSave CaptureDynamicActor(long id, string definition, Vector3 actorFrameOffset = default)
    {
        ActorState actor = LiveDynamicActor(id);
        Vector3 position = actor.Position.ToVector() - actorFrameOffset;
        return new(id, definition, position.X, position.Y, position.Z, actor.HeadingYawRadians,
            DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity))
        {
            Level = _actorDefinitions[id].Level ?? 1,
            SpawnDistance = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().SpawnDistance,
            Passive = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Passive,
            CorruptionOrigin = actor.Actor.TryGet<DaggerfallCorruptionOrigin>(out var origin) ? origin : null,
            Gender = actor.Actor.TryGet<DaggerfallActorGender>(out var gender) ? gender.Value : null,
            WabbajackActive = DaggerfallWabbajack.DefinitionOf(actor.Actor) is not null,
            PlayerAllied = _actorDefinitions[id].Team == "player-ally",
            ForcedHostile = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile,
            LanguagePacified = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().LanguagePacified,
            MagicallyPacified = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().MagicallyPacified,
            TeamOverride = actor.Actor.TryGet<FactionComponent>(out FactionComponent? faction) ? faction.TeamOverride : null,
            PromotedArchetype = actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
                && string.Equals(overlay.ArchetypeId, DaggerfallArchetypeIds.Guard, StringComparison.Ordinal)
                ? DaggerfallArchetypeIds.Guard
                : null,
            EncounterBandId = actor.Actor.TryGet<DaggerfallEncounterBandMember>(out var bandMember) ? bandMember.BandId : null,
            EncounterGeneration = bandMember?.Generation,
        };
    }

    /// <summary>Captures one canonical materialized actor back to its detached site owner.</summary>
    internal DaggerfallSiteRuntimeDelta CaptureDynamicActorDelta(long id, string definition, Vector3 actorFrameOffset = default) => new([], [CaptureDynamicActor(id, definition, actorFrameOffset)],
        [new(id, CaptureInventory(State.ActorInventories.InventoryFor(id) ?? throw new InvalidOperationException($"Actor {id} has no inventory."),
            State.ActorInventories.EquipmentFor(id), DaggerfallItemOwner.Actor(id)))], CaptureCorpses([id]), [],
        State.Effects.Capture().Where(effect => effect.TargetId == id).ToArray());

    /// <summary>Reapplies detached inventory, corpse, and effect state onto a freshly materialized deterministic actor.</summary>
    internal void RestoreDetachedActorState(DaggerfallActorInventorySave? inventory, DaggerfallCorpseSave? corpse,
        DaggerfallActiveEffectSave[]? effects)
    {
        if (inventory is not null)
            ApplyActorInventories([inventory]);
        if (corpse is not null)
            _corpseLoot.Restore([corpse], CorpseIdentities([corpse]));
        if (effects is { Length: > 0 })
        {
            _effects.Restore(effects);
            _ = DaggerfallTempleBlessingEffects.ExpireDue(_effects, _time);
        }
    }

    /// <summary>Reapplies detached values after the destination has created fresh authored actors.</summary>
    internal void RestoreSiteDelta(DaggerfallSiteRuntimeDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        ApplyActorInventories(delta.ActorInventories);
        _corpseLoot.Restore(delta.Corpses, CorpseIdentities(delta.Corpses));
        _effects.Restore(delta.Effects);
        _ = DaggerfallTempleBlessingEffects.ExpireDue(_effects, _time);
    }

    private DaggerfallCorpseSave[] CaptureCorpses(IEnumerable<long> actorIds)
    {
        HashSet<long> selected = [.. actorIds];
        return _corpseLoot.Corpses.Values.Where(corpse => selected.Contains(corpse.ActorId)).OrderBy(corpse => corpse.ActorId).Select(corpse =>
        {
            (DaggerfallStackSave[] stacks, DaggerfallUniqueSave[] uniques) = corpse.IsRegistered
                ? CaptureContents(State.Containers.Read(corpse.Owner), DaggerfallItemOwner.Corpse(corpse.ActorId))
                : ([], []);
            return new DaggerfallCorpseSave(corpse.ActorId, corpse.ContainerIdentity.Value, corpse.OriginatingSequence,
                corpse.IsRegistered, corpse.IsInteractable, stacks, uniques);
        }).ToArray();
    }

    private ActorState LiveDynamicActor(long durableId) =>
        State.Actors.TryGet(durableId, out ActorState? actor)
            ? actor
            : throw new InvalidOperationException($"Daggerfall cannot save dynamic actor {durableId} without a live entity.");

    /// <summary>
    /// Applies a resolved save's relational sections to a freshly composed session, mirroring
    /// <see cref="Capture"/>. Composition has already built each owner from its own section: actor,
    /// player and dynamic-actor stats, variables, NPCs, social standing, character and quest training
    /// (<see cref="DaggerfallActorFactory"/>); then the calendar, site context, action graphs, dungeon
    /// discovery, doors and motion, the identity ledger, currency, bank, loans, property, crime,
    /// services, knightly claims and regional prices, and the roster's spawned actors. This applies the
    /// rest, in this order:
    /// <list type="number">
    /// <item>inactive sites' deltas;</item>
    /// <item>encounters, against every live and detached spawned actor and the retired identities;</item>
    /// <item>actor and spawned-actor poses;</item>
    /// <item>progression, skill uses, locomotion, climbing, level-up and quests;</item>
    /// <item>player inventory and its pack layout, actor and ground inventories, wagon, transport and notebook;</item>
    /// <item>the player pose, corpses, attack cooldowns and active effects, then the camera;</item>
    /// <item>held enchantments over the restored equipment, once;</item>
    /// <item>defeated actors' appearance, the pending dungeon text and the action-trigger player rebase.</item>
    /// </list>
    /// The exterior window is admitted by the site lifecycle afterwards for both start kinds.
    /// </summary>
    internal void Restore(DaggerfallResolvedRestore restore, DaggerfallSiteCrossing sites, DaggerfallActorRoster roster,
        DaggerfallEncounterRuntime encounters, DaggerfallHeldEnchantments heldEnchantments,
        Action<DaggerfallDungeonTextSnapshot> restoreDungeonText)
    {
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(encounters);
        ArgumentNullException.ThrowIfNull(heldEnchantments);
        ArgumentNullException.ThrowIfNull(restoreDungeonText);
        DaggerfallSavePayload saved = restore.Payload;
        sites.RestoreDeltas(saved.SiteDeltas, saved.ActorLedger);
        encounters.Restore(saved.Encounters);
        RestoreSections(saved, sites.Projection.Inputs, roster);
        heldEnchantments.Refresh();
        sites.Projection.Appearance.SyncRestoredDefeat(State.Actors);
        restoreDungeonText(saved.DungeonText);
        sites.Interiors.ActionTriggers.RebaseRestoredPlayer(State.PlayerControl, State.Actors.Player.Actor.Entity);
    }

    private void RestoreSections(DaggerfallSavePayload saved, DaggerfallSiteProfile activeInputs, DaggerfallActorRoster roster)
    {
        DaggerfallWorldProfileKey activeProfile = activeInputs.ProfileKey;
        foreach (ActorLedgerEntry entry in saved.ActorLedger.OrderBy(e => e.DurableId))
        {
            if (!entry.Origin.IsFixedPlacement || entry.Location is { Detached: true } || entry.Location?.Kind == ActorLocationKind.Removed)
                continue;
            if (entry.Pose is not { } pose) continue;
            if (!State.Actors.TryGet(entry.DurableId, out ActorState? current))
                throw new ArgumentException($"The saved Daggerfall actor '{entry.DurableId}' is not present in the selected content.", nameof(saved));
            current.ApplyPose(new ActorPose(new WorldPoint(pose.X, pose.Y, pose.Z), pose.HeadingRadians));
        }

        // Dynamic actors were materialized by construction; restore re-applies their poses and social overlays.
        Dictionary<long, ActorSocialDiff> restoredDynamicSocial = saved.ActorLedger
            .Where(e => e.Origin.Kind == ActorOriginKind.Minted && e.Social is not null)
            .ToDictionary(e => e.DurableId, e => e.Social!);
        foreach (DaggerfallDynamicActorSave spawned in saved.ActiveDynamicActors(activeProfile).OrderBy(actor => actor.EntityId))
        {
            if (!State.Actors.TryGet(spawned.EntityId, out ActorState? current))
                throw new InvalidOperationException($"The saved dynamic actor '{spawned.EntityId}' was not materialized by the selected content.");
            current.ApplyPose(new ActorPose(new WorldPoint(spawned.X, spawned.Y, spawned.Z), spawned.HeadingRadians));
            if (restoredDynamicSocial.TryGetValue(spawned.EntityId, out ActorSocialDiff? dynSocial)
                && current.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? dynOverlay))
            {
                dynOverlay.FamilyId = dynSocial.FamilyId;
                dynOverlay.Met = dynSocial.Met;
                dynOverlay.IsMuted = dynSocial.Muted;
                dynOverlay.EscortPortraitMedia = dynSocial.EscortPortrait;
                dynOverlay.EscortOrder = dynSocial.EscortOrder;
                dynOverlay.StagedPartyRole = dynSocial.StagedPartyRole?.ToString();
                if (dynSocial.WitnessedCrimes is { Length: > 0 })
                    foreach (string operationId in dynSocial.WitnessedCrimes)
                        dynOverlay.RecordWitnessedCrime(operationId);
                if (dynSocial.ReferralTags is { Length: > 0 })
                    foreach (string tag in dynSocial.ReferralTags)
                        dynOverlay.RecordReferralTag(tag);
            }
        }

        // Materialize deterministic active-site actors (static, cast, relocated, and any active-site
        // deterministic population civilians referenced by saved ledger/inventories/corpses/effects)
        // BEFORE applying saved actor inventories, corpses, and effects.
        roster.MaterializeStaticNpcs(activeInputs);
        HashSet<long> requiredActiveDeterministicIds = [];
        foreach (ActorLedgerEntry entry in saved.ActorLedger)
        {
            if (entry.Origin.Kind is not (ActorOriginKind.SeededSlot or ActorOriginKind.CastIndividual) || entry.Origin.IsFixedPlacement)
                continue;
            if (entry.Location is null && entry.Origin.SiteProfile?.Require() == activeProfile
                || entry.Location is { Kind: ActorLocationKind.AtSite or ActorLocationKind.Dead, Detached: false, Profile: { } p } && p.Require() == activeProfile)
                requiredActiveDeterministicIds.Add(entry.DurableId);
        }
        foreach (DaggerfallActorInventorySave inv in saved.ActorInventories)
            if (DaggerfallPeopleSlots.IsDeterministicId(inv.EntityId)) requiredActiveDeterministicIds.Add(inv.EntityId);
        foreach (DaggerfallCorpseSave corpse in saved.Corpses)
            if (DaggerfallPeopleSlots.IsDeterministicId(corpse.ActorId)) requiredActiveDeterministicIds.Add(corpse.ActorId);
        foreach (DaggerfallActiveEffectSave effect in saved.ActiveEffects)
            if (DaggerfallPeopleSlots.IsDeterministicId(effect.TargetId)) requiredActiveDeterministicIds.Add(effect.TargetId);
        roster.MaterializeActiveRestoredPopulation(activeInputs, requiredActiveDeterministicIds);

        State.Progression.AdvanceTo(saved.Experience, saved.Level);
        State.SkillUses.Restore(saved.SkillUses);
        _locomotion.Restore(saved.Locomotion);
        _climbing.Restore(saved.Climbing);
        _swimming.Restore(saved.Swimming);
        State.LevelUps.Restore(saved.LevelUp);
        State.Quests.Restore(saved.Quests);

        ApplyInventory(saved.Inventory, State.Inventory, State.Equipment, DaggerfallItemOwner.Player);
        _pack.RestoreLayout(saved.Pack);
        ApplyActorInventories(saved.ActorInventories);
        _groundContainers.Restore(saved.GroundContainers);
        State.QuestItems.Restore(saved.QuestCustody);
        if (saved.Wagon is { } wagon) State.Wagon.Restore(wagon);
        State.Transport.Restore(saved.Transport);
        _notebook.Restore(saved.Notebook);

        WorldPoint position = new(saved.Player.X, saved.Player.Y, saved.Player.Z);
        State.PlayerControl.YawRadians = saved.Player.YawRadians;
        State.PlayerControl.PitchRadians = saved.Player.PitchRadians;
        State.WeaponDrawn = saved.Player.WeaponDrawn;
        State.PlayerControl.Restore(position, default(CharacterMotion) with
        {
            Stance = saved.Player.Crouched ? CharacterStance.Crouched : CharacterStance.Standing,
            PeakY = position.Y,
            FallOriginY = position.Y,
        });
        // Native continuation and held input start fresh; durable pose and corpse relationships are restored.
        _corpseLoot.Restore(saved.Corpses, CorpseIdentities(saved.Corpses));
        State.Kit.AttackExecution.RestoreCooldowns(saved.CombatCooldowns.Select(value => new AttackCooldown(value.AttackerId, value.RemainingSteps)));
        // Actors, their shared stats/tracks, and item identities exist before active effects rebuild
        // their reversible contributions. Resume deliberately does not replay an initial magic round.
        _effects.Restore(saved.ActiveEffects);
        _ = DaggerfallTempleBlessingEffects.ExpireDue(_effects, _time);
        _camera.Update(State.PlayerControl);
        // Enemy behavior, perception leases, held input, pending loot, facts,
        // presentation effects and a swing still waiting for its damage frame are
        // intentionally transient.  The next admitted step observes rebuilt actor
        // state without replaying them; a swing cut off by a save keeps the cooldown
        // it already charged but never lands.
    }

    private DaggerfallInventorySave CaptureInventory(MechanicsInventoryCoordinator inventoryOwner, MechanicsEquipmentCoordinator equipmentOwner, DaggerfallItemOwner owner)
    {
        InventoryView inventory = inventoryOwner.Read();
        EquipmentRead equipped = equipmentOwner.Read();
        (DaggerfallStackSave[] stacks, DaggerfallUniqueSave[] uniques) = CaptureContents(inventory, owner);
        return new(
            stacks,
            uniques,
            equipped.Assignments.OrderBy(assignment => assignment.Slot.Value, StringComparer.Ordinal)
                .Select(assignment => new DaggerfallEquipmentSave(assignment.Slot.Value, State.Actors.Entities.IdentityOf(new EntityId(assignment.Item.EntityId)).Value)).ToArray());
    }

    /// <summary>Shared stack/unique contents mapping for actor inventories and corpse containers.</summary>
    private (DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems) CaptureContents(InventoryView contents, DaggerfallItemOwner owner) =>
        DaggerfallInventorySaveBoundary.CaptureContents(contents, owner, State.ItemInstances, State.Actors.Entities);

    private static IReadOnlyDictionary<long, DurableIdentityReference> CorpseIdentities(IEnumerable<DaggerfallCorpseSave> corpses) =>
        corpses.ToDictionary(
            corpse => corpse.ActorId,
            corpse => new DurableIdentityReference(DurableIdentityKind.Container, corpse.ContainerId));

    private void ApplyInventory(DaggerfallInventorySave saved, MechanicsInventoryCoordinator inventory, MechanicsEquipmentCoordinator equipment, DaggerfallItemOwner owner)
    {
        foreach (DaggerfallStackSave stack in saved.Stacks)
        {
            InventoryStackId stackId = InventoryStackId.Parse(stack.StackId);
            inventory.Grant(new InventoryGrant(new InventoryItemId(stack.ItemId), stackId, stack.Quantity));
            State.ItemInstances.RegisterStack(owner, stackId, DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata));
        }
        Dictionary<ulong, KitUniqueInventoryItem> unique = [];
        foreach (DaggerfallUniqueSave item in saved.UniqueItems)
        {
            unique.Add(item.EntityId, equipment.Materialize(new DurableIdentityReference(DurableIdentityKind.Item, item.EntityId), new InventoryItemId(item.ItemId), DaggerfallEncumbrancePolicy.CapacityOverride(item.Metadata.WeightClassicUnits)));
            State.ItemInstances.RegisterUnique(item.EntityId, DaggerfallItemInstanceMetadata.Restore(item.ItemId, item.Metadata));
        }
        foreach (IGrouping<ulong, DaggerfallEquipmentSave> group in saved.Equipment.GroupBy(value => value.ItemEntityId))
        {
            equipment.Equip(unique[group.Key], group.Select(value => new KitEquipmentSlotId(value.SlotId)).ToArray());
        }
    }

    private void ApplyActorInventories(DaggerfallActorInventorySave[] saved)
    {
        foreach (DaggerfallActorInventorySave section in saved)
        {
            MechanicsInventoryCoordinator inventory = State.ActorInventories.InventoryFor(section.EntityId)
                ?? throw new ArgumentException($"Saved inventory owner {section.EntityId} is missing.");
            DaggerfallItemOwner owner = DaggerfallItemOwner.Actor(section.EntityId);
            foreach (InventoryStack existing in inventory.Read().Stacks.ToArray())
            {
                inventory.Consume(new InventoryConsume(existing.Id, existing.Quantity));
                if (State.ItemInstances.ContainsStack(owner, existing.Id))
                    State.ItemInstances.RemoveStack(owner, existing.Id);
            }
            ApplyInventory(section.Inventory, inventory, State.ActorInventories.EquipmentFor(section.EntityId), owner);
        }
    }
}
