using WorldRpg.Kit.Combat;
using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Publishes the normalized Privateer's Hold visual closure through Engine-owned resources and sprite atlases.</summary>
internal sealed class PrivateersHoldAppearance : IDisposable
{
    private readonly IGraphicsService appearance;
    private readonly IContentService content;
    private readonly IAudioService? audio;
    private readonly DaggerfallAudioBundle? audioBundle;
    private readonly IRandomService? random;
    private readonly DaggerfallPresentationAudioTuning audioTuning;
    private readonly Dictionary<string, AudioClip> audioClips = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<string> hitCues;
    private readonly IReadOnlyDictionary<string, NormalizedClassicEffect> classicEffects;
    private readonly NormalizedClassicPresentation classicPresentation;
    // Engine seals render-resource selection once Product.Create finishes.
    // Classic effects and the optional weapon remain lazy visual instances, but
    // their normalized texture handles must be admitted with the initial closure.
    private readonly Dictionary<string, RenderResourceInfo> classicTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<long, ActorVisual> actors = [];
    private readonly Dictionary<long, GroundVisual> groundVisuals = [];
    private readonly List<EffectVisual> effects = [];
    private ViewmodelVisual? viewmodel;
    private bool weaponDrawn = true;
    internal bool CanStartPlayerAttack => weaponDrawn && viewmodel?.Strike != true;
    /// <summary>
    /// Reports an enemy hit swing whose authored damage frame has not been consumed yet.
    /// The session uses this presentation-owned state to keep post-enemy actions behind the
    /// same admitted animation boundary; it does not mirror combat health or create a second
    /// combat queue.
    /// </summary>
    internal bool HasPendingEnemyHitTarget(long targetId) => actors.Values.Any(visual =>
        visual.ActiveAttack is { Identity.Target: var target, Identity.Attacker: var attacker, Identity.Outcome: "hit", ImpactReported: false }
        && target == targetId
        && attacker != DaggerfallActorIdentity.PlayerEntityId);
    /// <summary>Reads the authored weapon draw state without conflating it with an active strike.</summary>
    internal bool IsWeaponDrawn => weaponDrawn;
    internal void ToggleWeaponDrawn() => weaponDrawn = !weaponDrawn;

    /// <summary>
    /// Registers a product-owned visual coordinator to contribute facts to this class's one
    /// complete Engine appearance snapshot. The completion callback runs only after the snapshot
    /// is accepted, which lets the coordinator retire resources without racing native readers.
    /// </summary>
    internal void SetSnapshotSupplement(
        Action<List<AppearanceFact>>? appendFacts,
        Action? snapshotAccepted)
    {
        appendSnapshotFacts = appendFacts;
        completeSnapshot = snapshotAccepted;
    }

    /// <summary>Emits the admitted classic player-death cue through the Engine audio owner.</summary>
    internal void PlayPlayerDeath(ulong generation, ulong simulationStep) =>
        Emit("playerDeath", Event(DaggerfallActorIdentity.PlayerEntityId, DaggerfallActorIdentity.PlayerEntityId,
            generation, simulationStep, "player-death"), 0);
    // Appearance object identities must be exactly representable in browser snapshots.
    // These transient product visuals use a disjoint descending pool, not resource hashes.
    private ulong nextVisualEntityId = (1UL << 53) - 1;
    private readonly HashSet<PresentationEventIdentity> deliveredEvents = [];
    private readonly HashSet<PresentationEventIdentity> appliedImpacts = [];
    private readonly List<AttackImpactNotice> attackImpacts = [];
    private readonly List<SpriteAtlas> atlases = [];
    private SpriteAtlas? groundContainerAtlas;
    private readonly NormalizedGroundContainerSprite? groundContainerSprite;
    private readonly List<Material> materials = [];
    private readonly Dictionary<uint, Material> materialsBySlot = [];
    private readonly Dictionary<DaggerfallRdbDoorId, Appearance> doorVisuals = [];
    private readonly Dictionary<DaggerfallRdbDoorId, ulong> doorVisualEntityIds = [];
    private readonly Dictionary<string, Appearance> actionModelVisuals = new(StringComparer.Ordinal);
    private readonly DaggerfallDungeonMotionProjection? dungeonMotion;
    // Door source identities are not Engine entity IDs or durable actor IDs.  Keep their render
    // identities in this product-only visual range, below effect/viewmodel identities and above
    // every authored or dynamically allocated gameplay identity.
    private ulong nextDoorVisualEntityId = (1UL << 52) - 1;
    private readonly DaggerfallDoorRuntime? doors;
    // Engine resources are owning objects: a render resource opened here is released here, because the
    // materials, atlases and sprites that name it hold non-owning references.
    private readonly List<RenderResource> ownedResources = [];
    private readonly List<IDisposable> priorRetired = [];
    private readonly List<IDisposable> nextRetired = [];
    private Appearance? world;
    private readonly AuthoredWorldAppearance worldAppearance;
    private Action<List<AppearanceFact>>? appendSnapshotFacts;
    private Action? completeSnapshot;
    private bool disposed;

    internal PrivateersHoldAppearance(IContentService content, IGraphicsService appearance, PrivateersHoldInputs inputs, IAudioService? audio = null, DaggerfallPresentationAudioTuning? audioTuning = null, IRandomService? random = null, DaggerfallAudioBundle? audioBundle = null, DaggerfallDoorRuntime? doors = null, DaggerfallDungeonMotionProjection? dungeonMotion = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(inputs);
        this.doors = doors;
        this.dungeonMotion = dungeonMotion;
        this.appearance = appearance;
        this.content = content;
        this.audio = audio;
        this.audioBundle = audioBundle;
        this.random = random;
        this.audioTuning = (audioTuning ?? DaggerfallTuning.Defaults.PresentationAudio).Validate();
        hitCues = inputs.Audio.Count == 0 ? [] : PrivateersHoldContent.OrderedHitCues(inputs.Audio);
        classicPresentation = inputs.ClassicPresentation;
        classicEffects = inputs.ClassicPresentation.Effects.ToDictionary(effect => effect.Name, StringComparer.Ordinal);
        worldAppearance = inputs.WorldAppearance;
        try
        {
            world = appearance.CreateStaticMeshFromContent(new StaticMeshContentAppearanceRequest(inputs.StaticMesh.Path, worldAppearance.Tint));
            foreach (NormalizedMaterial material in inputs.Materials)
            {
                RenderResourceInfo texture = appearance.OpenResource(new RenderResourceRequest(material.TexturePath, TextureFilter.Nearest, TextureWrap.Repeat));
                ownedResources.Add(texture.Handle);
                Material created = appearance.CreateMaterial(new MaterialRequest(new Color(1F, 1F, 1F, 1F), texture.Handle, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false));
                materials.Add(created);
                materialsBySlot.Add(material.Slot, created);
            }
            appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(world, inputs.Materials.Select((material, index) => new MeshMaterialBinding(material.Slot, materials[index])).ToArray()));
            if (inputs.Doors.Count != 0 && doors is null) throw new ArgumentException("Door visuals require the selected door runtime.", nameof(doors));
            foreach (DaggerfallRdbDoorDefinition door in inputs.Doors)
            {
                DaggerfallDoorVisual visual = door.Visual ?? throw new InvalidOperationException($"Selected RDB door '{door.Id}' has no normalized visual.");
                Appearance created = appearance.CreateStaticMeshFromContent(new StaticMeshContentAppearanceRequest(visual.Path, worldAppearance.Tint));
                appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(created, visual.Materials
                    .Select(binding => materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? material)
                        ? new MeshMaterialBinding(binding.MeshSlot, material)
                        : throw new InvalidOperationException($"Door '{door.Id}' refers to missing world material slot {binding.WorldMaterialSlot}."))
                    .ToArray()));
                doorVisuals.Add(door.Id, created);
                doorVisualEntityIds.Add(door.Id, nextDoorVisualEntityId--);
            }
            if (dungeonMotion is not null)
            {
                foreach ((DaggerfallDungeonActionModelDefinition model, _) in dungeonMotion.Visuals)
                {
                    Appearance created = appearance.CreateStaticMeshFromContent(new StaticMeshContentAppearanceRequest(model.Visual.Path, worldAppearance.Tint));
                    try
                    {
                        appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(created, model.Visual.Materials
                            .Select(binding => materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? material)
                                ? new MeshMaterialBinding(binding.MeshSlot, material)
                                : throw new InvalidOperationException($"Action model '{model.ActionId}' refers to missing world material slot {binding.WorldMaterialSlot}."))
                            .ToArray()));
                        actionModelVisuals.Add(model.ActionId, created);
                    }
                    catch
                    {
                        List<Exception>? failures = null;
                        Dispose(created, ref failures);
                        if (failures is { Count: > 0 }) throw new AggregateException(failures);
                        throw;
                    }
                }
            }
            foreach ((long entityId, NormalizedActorSprite sprite) in inputs.ActorSprites.OrderBy(pair => pair.Key))
            {
                ActorVisual visual = CreateActorVisual(content, entityId, sprite);
                actors.Add(entityId, visual);
            }
            groundContainerSprite = inputs.GroundContainerSprite;
            if (groundContainerSprite is { } groundSprite)
            {
                RenderResourceInfo texture = appearance.OpenResource(new RenderResourceRequest(groundSprite.TexturePath));
                ownedResources.Add(texture.Handle);
                SpriteAtlasFrame[] frames = SpriteAtlasAdapter.ToAtlasFrames(groundSprite.AtlasWidth, groundSprite.AtlasHeight,
                    groundSprite.Frames.Select(frame => new NormalizedSpriteFrame(frame.Id, frame.X, frame.Y, frame.Width, frame.Height)).ToArray());
                groundContainerAtlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, frames));
                atlases.Add(groundContainerAtlas);
            }
            AdmitClassicTextures();
            foreach (NormalizedAudioClip clip in inputs.Audio)
            {
                // An opened clip is an owned Engine resource now, so it is retained for the lifetime of
                // this appearance and released with it rather than discarded after the emit. Callers
                // that explicitly supply eager audio retain that composition; the composed product
                // supplies its bundle owner and opens a clip only when the matching cue is emitted.
                if (audio is not null && audioBundle is null) audioClips.Add(clip.Id, audio.OpenClip(new AudioClipRequest(clip.Path)));
            }
        }
        catch { Dispose(); throw; }
    }

    /// <summary>
    /// Drops one actor's visual when its registration ends. Actors without published sprite media
    /// never had an entry, so retiring one is a no-op rather than an error.
    /// </summary>
    /// <summary>Admits media for one dynamic actor after its canonical mechanics actor has been created.</summary>
    internal void AddActor(long durableId, NormalizedActorSprite sprite)
    {
        if (disposed) throw new ObjectDisposedException(nameof(PrivateersHoldAppearance));
        ArgumentNullException.ThrowIfNull(sprite);
        if (durableId <= 0) throw new ArgumentOutOfRangeException(nameof(durableId));
        if (actors.ContainsKey(durableId)) throw new InvalidOperationException($"Actor {durableId} already has an appearance.");
        ActorVisual visual = CreateActorVisual(content, durableId, sprite);
        try { actors.Add(durableId, visual); }
        catch { List<Exception>? failures = null; visual.Dispose(ref failures); if (failures is { Count: > 0 }) throw new AggregateException(failures); throw; }
    }

    internal void RetireActor(long durableId)
    {
        if (actors.Remove(durableId, out ActorVisual? visual) && visual is not null)
        {
            List<Exception>? failures = null;
            visual.Dispose(ref failures);
            if (failures is { Count: > 0 }) throw new AggregateException(failures);
        }
    }

    /// <summary>Publishes world and viewport weapon appearances; Engine owns projection and fitting.</summary>
    internal void Publish(ActorsState actors)
        => Publish(actors, new Dictionary<long, DaggerfallGroundContainer>());

    /// <summary>Publishes the active ground-container projection through the same Engine snapshot as actors.</summary>
    internal void Publish(ActorsState actors, IReadOnlyDictionary<long, DaggerfallGroundContainer> groundContainers)
    {
        if (disposed) return;
        ReconcileGroundVisuals(groundContainers);
        List<AppearanceFact> facts = [];
        if (world is { } staticWorld) facts.Add(new AppearanceFact(1, false, 0, worldAppearance.Transform, staticWorld, worldAppearance.Visible, worldAppearance.Layer));
        if (doors is not null) foreach (DaggerfallDoorView door in doors.All)
            if (doorVisuals.TryGetValue(door.Id, out Appearance? visual)) facts.Add(new AppearanceFact(doorVisualEntityIds[door.Id], false, 0, door.Pose, visual, true, RenderLayer.Scene));
        if (dungeonMotion is not null)
        {
            foreach ((DaggerfallDungeonActionModelDefinition model, EntityId entity) in dungeonMotion.Visuals)
            {
                if (actionModelVisuals.TryGetValue(model.ActionId, out Appearance? visual)
                    && dungeonMotion.TryGetTransform(model.ActionId, out Transform transform))
                    facts.Add(new AppearanceFact(entity.Value, false, 0, transform, visual, true, RenderLayer.Scene));
            }
        }
        foreach (ActorState actor in actors.All)
        {
            if (!this.actors.TryGetValue(actor.DurableId, out ActorVisual? visual)) continue;
            Appearance? chosen = actor.IsDefeated ? visual.Corpse : visual.Live;
            if (chosen is not null) facts.Add(new AppearanceFact(checked((ulong)actor.DurableId), false, 0, new Transform(actor.Position.ToVector(), Quaternion.Identity, Vector3.One), chosen, true, RenderLayer.Scene));
        }
        foreach (DaggerfallGroundContainer container in groundContainers.Values.OrderBy(container => container.Id))
        {
            if (groundVisuals.TryGetValue(container.Id, out GroundVisual? visual))
                facts.Add(new AppearanceFact(checked((ulong)container.Id), false, 0,
                    new Transform(container.Position.ToVector(), Quaternion.Identity, Vector3.One), visual.Appearance, true, RenderLayer.Scene));
        }
        foreach (EffectVisual effect in effects)
            facts.Add(new AppearanceFact(effect.EntityId, false, 0, new Transform(effect.Position.ToVector(), Quaternion.Identity, Vector3.One), effect.Appearance, true, RenderLayer.Scene));
        if (viewmodel is { } weapon)
        {
            facts.Add(new AppearanceFact(weapon.EntityId, false, 0, weapon.Transform, weapon.Appearance, true, RenderLayer.Viewmodel));
        }
        appendSnapshotFacts?.Invoke(facts);
        appearance.PublishSnapshot([.. facts]);
        completeSnapshot?.Invoke();
    }

    /// <summary>
    /// Reconciles restored actor defeat state with the presentation-owned live/corpse visual state.
    /// This performs no death reaction, audio, loot, or reward work; those owners already restored
    /// their canonical state before this projection sync.
    /// </summary>
    internal void SyncRestoredDefeat(ActorsState actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        foreach (ActorState actor in actors.All.OrderBy(actor => actor.DurableId))
            if (actor.IsDefeated)
                TransitionToCorpse(actor.DurableId);
    }

    /// <summary>
    /// Maps the Engine playback's current normalized source-frame index to the
    /// camera-relative classic directional sector.  Direction is a frame
    /// selection only; it never recreates or restarts Engine playback.
    /// </summary>
    internal void UpdateDirections(ActorsState actorState, WorldPoint viewpoint)
    {
        if (disposed) return;
        foreach (ActorState actor in actorState.All)
        {
            if (!actors.TryGetValue(actor.DurableId, out ActorVisual? visual)
                || visual.Live is null
                || visual.ActiveState is null
                || visual.SourceFrameIndices.Count == 0) continue;
            float dx = viewpoint.X - actor.Position.X;
            float dz = viewpoint.Z - actor.Position.Z;
            if (dx == 0f && dz == 0f) continue;
            int sector = RelativeSector(actor.HeadingYawRadians, dx, dz);
            uint playbackIndex = visual.LastPlaybackFrameIndex;
            if (playbackIndex >= visual.SourceFrameIndices.Count) continue;
            IReadOnlyList<uint> oriented = visual.ActiveState.SelectOrientation(sector);
            int sourceIndex = visual.SourceFrameIndices[checked((int)playbackIndex)];
            if (sourceIndex < 0 || sourceIndex >= oriented.Count) continue;
            appearance.SetSpriteFrame(new SpriteFrameUpdateRequest(visual.Live, oriented[sourceIndex]));
            visual.Orientation = sector;
        }
    }

    internal void BeginAdmittedUpdate()
    {
        // These wrappers belonged to the preceding successful callback.  Their
        // generated releases are staged in this new callback, not the one that
        // replaced their local role.
        // Direct presentation users may not call Complete between callbacks;
        // promote that completed-call queue at the next admission boundary.
        if (priorRetired.Count == 0 && nextRetired.Count > 0)
        {
            priorRetired.AddRange(nextRetired);
            nextRetired.Clear();
        }
        // A retirement that fails is named rather than replacing the update's own failure: the value's
        // kind and its reason are what a caller needs to see, and one refused release must not hide the
        // others in the same boundary.
        List<Exception>? failures = null;
        foreach (IDisposable value in priorRetired)
        {
            try { value.Dispose(); }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(new InvalidOperationException($"retired {value.GetType().Name} was not released", exception));
            }
        }

        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    internal void CompleteAdmittedUpdate()
    {
        priorRetired.Clear();
        priorRetired.AddRange(nextRetired);
        nextRetired.Clear();
    }
    /// <summary>Interprets Daggerfall combat facts while Engine owns playback timing and frame staging.</summary>
    internal void React(IProductFact fact) => React(fact, null);

    /// <summary>Interprets facts with the current authoritative actor state; presentation keeps no position mirror.</summary>
    internal void React(IProductFact fact, ActorsState? actorState)
    {
        if (disposed) return;
        switch (fact)
        {
            case PlayerAttackStartedFact started:
                PresentationEventIdentity swing = Event(DaggerfallActorIdentity.PlayerEntityId, started.TargetId ?? 0, started.OriginatingGeneration, started.OriginatingSimulationStep, "swing");
                if (deliveredEvents.Contains(swing)) break;
                // The viewmodel plays the swing at the tick time the rules published, and its hit
                // frame is what delivers the admitted impact. A viewmodel that cannot play the
                // authored strike has no frame to wait for, so the impact lands in this update
                // rather than being withheld by a presentation the composition does not have.
                if (!StartWeaponStrike(swing, started.FrameSeconds, started.TargetId, started.HitFrame))
                    attackImpacts.Add(new AttackImpactNotice(DaggerfallActorIdentity.PlayerEntityId, started.TargetId ?? 0,
                        started.OriginatingGeneration, started.OriginatingSimulationStep, Expired: false));
                Emit("swing", swing, 0);
                deliveredEvents.Add(swing);
                break;
            case EnemyAttackStartedFact started:
                // An enemy swing starts here; its consequence arrives later, when the
                // authored damage frame is reached. A sequence with no damage frame
                // resolves inside this same update instead of never landing.
                PresentationEventIdentity startEvent = Event(started.AttackerId, started.TargetId, started.OriginatingGeneration, started.OriginatingSimulationStep, started.WillHit ? "hit" : "miss");
                if (deliveredEvents.Contains(startEvent)) break;
                deliveredEvents.Add(startEvent);
                if (!StartAttack(started.AttackerId, started.TargetId, started.OriginatingGeneration, started.OriginatingSimulationStep, startEvent))
                    attackImpacts.Add(new AttackImpactNotice(started.AttackerId, started.TargetId, started.OriginatingGeneration, started.OriginatingSimulationStep, Expired: false));
                break;
            case AttackHitFact hit:
                PresentationEventIdentity hitEvent = Event(hit.AttackerId, hit.TargetId, hit.OriginatingGeneration, hit.OriginatingSimulationStep, "hit");
                // A player swing has no separate start fact, so its presentation begins
                // here. An enemy swing already began, and must not restart playback.
                if (hit.AttackerId == DaggerfallActorIdentity.PlayerEntityId && deliveredEvents.Add(hitEvent))
                    StartAttack(hit.AttackerId, hit.TargetId, hit.OriginatingGeneration, hit.OriginatingSimulationStep, hitEvent);
                if (!appliedImpacts.Add(hitEvent)) break;
                StartState(hit.TargetId, "hurt", null);
                if (hit.AttackerId == DaggerfallActorIdentity.PlayerEntityId && actorState is not null) SpawnBlood(hit, hitEvent, actorState);
                break;
            case AttackMissedFact miss:
                // An enemy miss was already presented at its swing; only the player's
                // swing is started by its own resolved outcome.
                if (miss.AttackerId != DaggerfallActorIdentity.PlayerEntityId) break;
                PresentationEventIdentity missEvent = Event(miss.AttackerId, miss.TargetId, miss.OriginatingGeneration, miss.OriginatingSimulationStep, "miss");
                if (deliveredEvents.Contains(missEvent)) break;
                StartAttack(miss.AttackerId, miss.TargetId, miss.OriginatingGeneration, miss.OriginatingSimulationStep, missEvent);
                deliveredEvents.Add(missEvent);
                break;
            case ActorDiedFact died:
                TransitionToCorpse(died.ActorId);
                break;
            case EnemyBehaviorTransitionFact transition:
                if (transition.Current == EnemyBehaviorState.Idle && actors.TryGetValue(transition.ActorId, out ActorVisual? visual)) EnsureRestState(transition.ActorId, PreferredRestState(visual.Sprite));
                else if (transition.Current == EnemyBehaviorState.Chase) EnsureRestState(transition.ActorId, "move");
                break;
        }
    }

    /// <summary>Selects authored equipped art, falling back to empty hands; Engine owns sprite realization.</summary>
    internal void UpdateRightHandEquipment(EquipmentRead equipment)
    {
        ArgumentNullException.ThrowIfNull(equipment);
        string? resource = null;
        foreach (string slot in new[] { "right-hand", "left-hand" })
            if (equipment.TryGet(new EquipmentSlotId(slot), out UniqueInventoryItem item)
                && classicPresentation.CompatibleItemVisuals.TryGetValue(item.Definition.Value, out resource)) break;
        resource ??= classicPresentation.UnarmedVisual;
        NormalizedClassicWeapon? selected = weaponDrawn && resource is not null
            && classicPresentation.Weapons.TryGetValue(resource, out NormalizedClassicWeapon? weapon) ? weapon : null;
        if (viewmodel?.Weapon.ResourceId == selected?.ResourceId) return;
        RetireViewmodel();
        if (selected is not null && classicPresentation.Viewmodel is not null) CreateViewmodel(selected);
    }

    /// <summary>Called exactly once from the outer Product.Update, never from a private catch-up step.</summary>
    internal void Advance(ProductUpdateFacts update)
    {
        if (disposed) return;
        AppearanceOuterUpdate identity = new(update.Generation, update.ControlRevision, update.SimulationStep, update.AdmittedStepCount);
        foreach (ActorVisual visual in actors.Values)
        {
            if (visual.Playback is null) continue;
            if (visual.LastOuterUpdate == identity) continue;
            SpritePlaybackAdvanceLeaseReceipt receipt = appearance.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(visual.Playback));
            if (receipt.Advanced)
            {
                visual.LastPlaybackFrameIndex = receipt.Readout.FrameIndex;
                foreach (SpritePlaybackMarkerCrossing crossing in receipt.Crossings.Span)
                {
                    if (crossing.CrossingSequence <= visual.LastMarkerCrossing) continue;
                    visual.LastMarkerCrossing = crossing.CrossingSequence;
                    if (visual.ActiveAttack is not { } crossingAttack) continue;
                    // One decided swing owns one strike beat, even when the authored sequence carries
                    // several damage frames: the first damage-frame crossing sounds and reports, and
                    // later beats of the same swing do neither. Engine's marker contract is
                    // deliberately semantics-free, so the beat is identified by the marker the author
                    // designated as the damage frame; any other marker kind crossing this swing (a cue,
                    // an alternate beat) names a frame that is not the strike and lands nothing.
                    if (crossingAttack.ImpactReported) continue;
                    if (crossingAttack.DamageMarkerId is not ulong damageMarker || crossing.MarkerId != damageMarker) continue;
                    visual.ActiveAttack = crossingAttack with { ImpactReported = true };
                    if (crossingAttack.Identity.Outcome == "hit") Emit(crossingAttack.HitCue, crossingAttack.Identity, crossing.CrossingSequence);
                    attackImpacts.Add(new AttackImpactNotice(crossingAttack.Identity.Attacker, crossingAttack.Identity.Target, crossingAttack.Identity.Generation, crossingAttack.Identity.SimulationStep, Expired: false));
                }
            }
            if (!receipt.Readout.Completed || visual.State is "idle" or "move") { visual.LastOuterUpdate = identity; continue; }
            // A swing that ended without reaching a damage frame must not land later.
            // An Engine receipt that reports completion without advancing is not
            // authoritative, so the swing stays live for a frame that can still land.
            if (receipt.Advanced && visual.ActiveAttack is { } endedAttack)
            {
                if (!endedAttack.ImpactReported)
                    attackImpacts.Add(new AttackImpactNotice(endedAttack.Identity.Attacker, endedAttack.Identity.Target, endedAttack.Identity.Generation, endedAttack.Identity.SimulationStep, Expired: true));
                visual.ActiveAttack = null;
            }
            // Publish the Engine's completed final frame for this outer update.
            // The following admitted update returns to the authored rest state.
            if (visual.CompletedOuterUpdate) StartState(visual.EntityId, PreferredRestState(visual.Sprite), null);
            else if (receipt.Advanced) visual.CompletedOuterUpdate = true;
            visual.LastOuterUpdate = identity;
        }
        foreach (EffectVisual effect in effects.ToArray())
        {
            if (effect.LastOuterUpdate == identity) continue;
            SpritePlaybackAdvanceLeaseReceipt receipt = appearance.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(effect.Playback));
            if (receipt.Readout.Completed)
            {
                if (effect.CompletedOuterUpdate) { effects.Remove(effect); Retire(effect); }
                else if (receipt.Advanced) effect.CompletedOuterUpdate = true;
            }
            effect.LastOuterUpdate = identity;
        }
        if (viewmodel is { } weapon && weapon.Playback is { } weaponPlayback && weapon.LastOuterUpdate != identity)
        {
            SpritePlaybackAdvanceLeaseReceipt receipt = appearance.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(weaponPlayback));
            // The classic swing's damage lands on its hit frame. One decided swing owns one beat, so
            // the first frame at or past it reports and later frames of the same swing do not.
            if (weapon.Strike && weapon.PendingImpact is { } pending && !weapon.ImpactReported
                && receipt.Readout.FrameIndex >= weapon.HitFrame)
            {
                weapon.ImpactReported = true;
                attackImpacts.Add(new AttackImpactNotice(pending.Attacker, pending.Target, pending.Generation, pending.SimulationStep, Expired: false));
            }
            if (weapon.Strike && receipt.Readout.Completed && receipt.Advanced)
            {
                // A swing that truly ended without reaching its hit frame must not land later. A
                // receipt that reports completion without advancing is not authoritative, so the
                // swing stays live for the frame that can still deliver it.
                RetireUnreportedImpact();
                if (weapon.CompletedOuterUpdate) StartWeaponAction("idle");
                else weapon.CompletedOuterUpdate = true;
            }
            weapon.LastOuterUpdate = identity;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? failures = null;
        try
        {
            appearance.PublishSnapshot(ReadOnlySpan<AppearanceFact>.Empty);
            completeSnapshot?.Invoke();
        }
        catch (Exception exception) { failures = [exception]; }
        foreach (ActorVisual visual in actors.Values.Reverse()) visual.Dispose(ref failures);
        foreach (GroundVisual visual in groundVisuals.Values.Reverse()) visual.Dispose(ref failures);
        foreach (EffectVisual effect in effects.AsEnumerable().Reverse()) effect.Dispose(ref failures);
        effects.Clear();
        if (viewmodel is { } weapon) { viewmodel = null; weapon.Dispose(ref failures); }
        foreach (IDisposable value in nextRetired.AsEnumerable().Reverse()) Dispose(value, ref failures);
        foreach (IDisposable value in priorRetired.AsEnumerable().Reverse()) Dispose(value, ref failures);
        nextRetired.Clear(); priorRetired.Clear();
        actors.Clear();
        groundVisuals.Clear();
        if (world is { } staticWorld) { world = null; Dispose(staticWorld, ref failures); }
        foreach (Appearance visual in doorVisuals.Values.Reverse()) Dispose(visual, ref failures);
        doorVisuals.Clear();
        doorVisualEntityIds.Clear();
        foreach (Appearance visual in actionModelVisuals.Values.Reverse()) Dispose(visual, ref failures);
        actionModelVisuals.Clear();
        foreach (SpriteAtlas atlas in atlases.AsEnumerable().Reverse()) Dispose(atlas, ref failures);
        atlases.Clear();
        groundContainerAtlas = null;
        foreach (Material material in materials.AsEnumerable().Reverse()) Dispose(material, ref failures);
        materials.Clear();
        foreach (RenderResource resource in ownedResources.AsEnumerable().Reverse()) Dispose(resource, ref failures);
        ownedResources.Clear();
        foreach (AudioClip clip in audioClips.Values.Reverse()) Dispose(clip, ref failures);
        audioClips.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private void AdmitClassicTextures()
    {
        foreach (NormalizedClassicEffect effect in classicEffects.Values.OrderBy(effect => effect.Name, StringComparer.Ordinal))
            AdmitClassicTexture(new ContentArtifact(effect.TexturePath, effect.TextureSha256));
        foreach (NormalizedClassicWeapon weapon in classicPresentation.Weapons.Values)
            AdmitClassicTexture(new ContentArtifact(weapon.TexturePath, weapon.TextureSha256));
    }

    private void AdmitClassicTexture(ContentArtifact artifact)
    {
        RenderResourceInfo texture = appearance.OpenResource(new RenderResourceRequest(artifact.Path));
        ownedResources.Add(texture.Handle);
        if (!classicTextures.TryAdd(artifact.Path, texture))
            throw new InvalidOperationException($"Classic presentation repeats normalized texture path '{artifact.Path}'.");
    }

    private static void Dispose(IDisposable value, ref List<Exception>? failures)
    {
        try { value.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }

    private void ReconcileGroundVisuals(IReadOnlyDictionary<long, DaggerfallGroundContainer> containers)
    {
        foreach (long id in groundVisuals.Keys.Where(id => !containers.ContainsKey(id)).ToArray())
        {
            GroundVisual visual = groundVisuals[id];
            groundVisuals.Remove(id);
            Retire(visual);
        }
        if (containers.Count == 0) return;
        if (groundContainerSprite is null || groundContainerAtlas is null)
            throw new InvalidOperationException("Ground containers require the normalized treasure billboard visual.");
        foreach (DaggerfallGroundContainer container in containers.Values.OrderBy(container => container.Id))
        {
            if (groundVisuals.ContainsKey(container.Id)) continue;
            Appearance visual = appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(groundContainerAtlas,
                groundContainerSprite.InitialFrameId, groundContainerSprite.Pivot, groundContainerSprite.Size,
                BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default,
                new Color(1F, 1F, 1F, 1F)));
            groundVisuals.Add(container.Id, new GroundVisual(container.Id, visual));
        }
    }

    private ActorVisual CreateActorVisual(IContentService content, long entityId, NormalizedActorSprite sprite)
    {
        Appearance? live = null;
        Appearance? corpse = null;
        try
        {
        (SpriteAtlas atlas, Appearance createdLive) = CreateSprite(content, sprite);
        live = createdLive;
        SpriteAtlas? corpseAtlas = null;
        if (sprite.Corpse is { } authoredCorpse)
        {
            (corpseAtlas, corpse) = CreateSprite(content, authoredCorpse);
        }
        ActorVisual visual = new(entityId, sprite, atlas, live, corpseAtlas, corpse);
        StartState(entityId, PreferredRestState(sprite), visual);
        return visual;
        }
        catch
        {
            live?.Dispose();
            corpse?.Dispose();
            throw;
        }
    }

    private (SpriteAtlas Atlas, Appearance Appearance) CreateSprite(IContentService content, NormalizedActorSprite sprite)
    {
        RenderResourceInfo texture = appearance.OpenResource(new RenderResourceRequest(sprite.TexturePath));
        ownedResources.Add(texture.Handle);
        SpriteAtlasFrame[] frames = SpriteAtlasAdapter.ToAtlasFrames(sprite.AtlasWidth, sprite.AtlasHeight,
            sprite.Frames.Select(frame => new NormalizedSpriteFrame(frame.Id, frame.X, frame.Y, frame.Width, frame.Height, frame.DisplaySize)).ToArray());
        SpriteAtlas atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, frames));
        atlases.Add(atlas);
        Appearance value = appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(atlas, sprite.InitialFrameId, sprite.Pivot, sprite.Size, BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F)));
        return (atlas, value);
    }

    /// <summary>
    /// Starts one attack's authored playback and reports whether it carries a damage
    /// frame. Without one there is no strike beat to wait for, so the caller resolves
    /// the swing immediately; the same fallback already covers its hit cue.
    /// </summary>
    private bool StartAttack(long entityId, long targetId, ulong generation, ulong simulationStep, PresentationEventIdentity presentationEvent)
    {
        if (!actors.TryGetValue(entityId, out ActorVisual? visual) || visual.Live is null || (visual.Sprite.AttackSequences.Count == 0 && visual.Sprite.RangedAttackSequence is null)) return false;
        NormalizedAttackSequence selected;
        string stateName;
        if (visual.Sprite.RangedAttackSequence is { } ranged && visual.Sprite.States.ContainsKey(ranged.State))
        {
            // The donor plays a ranged mobile's ranged animation for every attack it makes, with
            // no distance check; a published rangedAttack1 state is the HasRangedAttack1 fact, and
            // the donor's RangedAttack2 variant names no adopted mobile.
            selected = ranged;
            stateName = ranged.State;
        }
        else
        {
            selected = SelectAttack(visual.Sprite.AttackSequences, generation, simulationStep, entityId, targetId);
            stateName = "primaryAttack";
        }
        StartState(entityId, stateName, visual, selected);
        string hitCue = SelectHitCue(presentationEvent);
        // The authored damage frame is the playback marker at its own source position: a source step
        // number is carried as a marker, and its identity is that step's place in the sequence.
        ulong? damageMarker = null;
        for (int index = 0; index < selected.SourceFrames.Count; index++)
            if (selected.SourceFrames[index] == -1) { damageMarker = checked((ulong)index + 1); break; }
        visual.ActiveAttack = new ActiveAttackPresentation(presentationEvent, hitCue, damageMarker);
        bool hasDamageFrame = damageMarker is not null;
        if (presentationEvent.Outcome == "hit" && !hasDamageFrame) Emit(hitCue, presentationEvent, 0);
        return hasDamageFrame;
    }

    /// <summary>
    /// Retires an unfinished player swing at a boundary that ends this appearance's lifetime, so the
    /// shared attack state cannot stay charged against a viewmodel nothing will advance again. The
    /// caller drains <see cref="TakeAttackImpacts"/> before the appearance goes away.
    /// </summary>
    internal void RetirePendingSwing() => RetireUnreportedImpact();

    /// <summary>Retires a strike's admitted impact that its animation never delivered.</summary>
    private void RetireUnreportedImpact()
    {
        if (viewmodel is not { PendingImpact: { } pending, ImpactReported: false }) return;
        attackImpacts.Add(new AttackImpactNotice(pending.Attacker, pending.Target, pending.Generation, pending.SimulationStep, Expired: true));
        viewmodel.PendingImpact = null;
    }

    /// <summary>Drains the swings whose authored damage frame was reached or passed this update.</summary>
    internal IReadOnlyList<AttackImpactNotice> TakeAttackImpacts()
    {
        if (attackImpacts.Count == 0) return [];
        AttackImpactNotice[] drained = attackImpacts.ToArray();
        attackImpacts.Clear();
        return drained;
    }

    private void SpawnBlood(AttackHitFact hit, PresentationEventIdentity identity, ActorsState actors)
    {
        if (!actors.TryGet(hit.TargetId, out ActorState? target)) return;
        WorldPoint position = target.Position;
        string[] names = ["blood0", "blood1", "blood2"];
        int ordinal = random is null ? 0 : checked((int)random.DrawKeyed(new KeyedRngRequest(
            CombatRandomKey.Seed,
            "daggerfall.media.blood-effect.v1",
            CombatRandomKey.For(identity.Generation, identity.SimulationStep, identity.Attacker, identity.Target, 43),
            0,
            names.Length - 1)).Value);
        // Classic media belongs to presentation policy; the combat fact already
        // owns applied damage. A retry is stopped by deliveredEvents above.
        // These resources are reconstructed from the admitted normalized pack.
        // Missing content deliberately means no invented replacement effect.
        // The effect is world-positioned at the fact's truthful target state.
        // (A future spell fact can select magicSparkle independently.)
        //
        // The selected resource is resolved through the stored normalized input
        // at construction-time via the effect catalog injected below.
        SpawnEffect(names[ordinal], position, identity);
    }

    private void SpawnEffect(string name, WorldPoint position, PresentationEventIdentity identity)
    {
        if (!classicEffects.TryGetValue(name, out NormalizedClassicEffect? effect)) return;
        SpriteAtlas? atlas = null;
        Appearance? visual = null;
        SpritePlayback? playback = null;
        try
        {
            RenderResourceInfo texture = classicTextures[effect.TexturePath];
            SpriteAtlasFrame[] frames = SpriteAtlasAdapter.ToAtlasFrames(effect.AtlasWidth, effect.AtlasHeight,
                effect.Frames.Select(frame => new NormalizedSpriteFrame(frame.Id, frame.X, frame.Y, frame.Width, frame.Height)).ToArray());
            atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, frames));
            uint initialFrame = effect.Sequence.Select(index => effect.Frames.Single(frame => frame.Id == index).Id).First();
            visual = appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(atlas, initialFrame, effect.Pivot, effect.DisplaySize, BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F)));
            SpritePlaybackFrame[] playbackFrames = SpriteAtlasAdapter.ToPlaybackFrames(effect.Sequence.Select(index => effect.Frames.Single(frame => frame.Id == index).Id).ToArray(), effect.FramesPerSecond);
            playback = appearance.CreateSpritePlayback(new SpritePlaybackCreateRequest(visual, atlas, playbackFrames, Array.Empty<SpritePlaybackMarker>(), effect.Loops ? SpritePlaybackLoopMode.Loop : SpritePlaybackLoopMode.OneShot, 1d));
            appearance.ControlSpritePlayback(new SpritePlaybackControlRequest(playback, SpritePlaybackControl.Start));
            effects.Add(new EffectVisual(NextVisualEntityId(), position, atlas, visual, playback));
        }
        catch
        {
            playback?.Dispose();
            visual?.Dispose();
            atlas?.Dispose();
            throw;
        }
    }

    private void CreateViewmodel(NormalizedClassicWeapon weapon)
    {
        ClassicViewmodelStyle style = classicPresentation.Viewmodel ?? throw new InvalidOperationException("No authored classic viewmodel style is available.");
        SpriteAtlas? atlas = null;
        Appearance? visual = null;
        try
        {
            RenderResourceInfo texture = classicTextures[weapon.TexturePath];
            SpriteAtlasFrame[] frames = SpriteAtlasAdapter.ToAtlasFrames(weapon.AtlasWidth, weapon.AtlasHeight,
                weapon.Frames.Select(frame => new NormalizedSpriteFrame(frame.Id, frame.X, frame.Y, frame.Width, frame.Height)).ToArray());
            atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, frames));
            visual = appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(atlas, weapon.Frames[0].Id, new Vector2(.5F, 0F), new Vector2(weapon.Frames[0].Width, weapon.Frames[0].Height), BillboardMode.None, SpriteSizeMode.Pixel, style.RenderOrder, SpriteDepthPolicy.DepthTestOff, new Color(1F, 1F, 1F, 1F)));
            viewmodel = new ViewmodelVisual(weapon, NextVisualEntityId(), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), atlas, visual);
            StartWeaponAction("idle");
        }
        catch
        {
            visual?.Dispose();
            atlas?.Dispose();
            viewmodel = null;
            throw;
        }
    }

    /// <summary>
    /// Plays the swing the player's admitted attack chose. Answers whether a strike animation started:
    /// a viewmodel-less or action-less composition reports false so the caller can land the impact in
    /// the update that admitted it instead of waiting for a frame nothing will play.
    /// </summary>
    private bool StartWeaponStrike(PresentationEventIdentity identity, double frameSeconds = 0d, long? target = null,
        int hitFrame = DaggerfallFormulaPolicy.MeleeWeaponHitFrame)
    {
        if (viewmodel is null) return false;
        string[] choices = ["strikeDown", "strikeDownLeft", "strikeLeft", "strikeRight", "strikeDownRight", "strikeUp"];
        int selected = random is null ? 0 : checked((int)random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, "daggerfall.media.weapon-strike.v1", CombatRandomKey.For(identity.Generation, identity.SimulationStep, identity.Attacker, identity.Target, 44), 0, choices.Length - 1)).Value);
        string name = choices[selected];
        if (!viewmodel.Weapon.Actions.ContainsKey(name)) return false;
        // A new swing replaces whatever the last one left undelivered, then owns the impact its own
        // hit frame will deliver; an untargeted swing has none.
        RetireUnreportedImpact();
        viewmodel.PendingImpact = target is long aimed ? identity with { Target = aimed } : null;
        viewmodel.ImpactReported = false;
        viewmodel.HitFrame = hitFrame;
        StartWeaponAction(name, frameSeconds);
        return true;
    }

    private void StartWeaponAction(string name, double frameSeconds = 0d)
    {
        if (viewmodel is null || !viewmodel.Weapon.Actions.TryGetValue(name, out NormalizedClassicWeaponAction? action)) return;
        NormalizedClassicWeapon weapon = viewmodel.Weapon;
        SpritePlayback? staged = null;
        try
        {
            // Some classic frames terminate at a side of their native canvas.
            // Fit that canvas against the same viewport edge so widescreen
            // letterboxing cannot expose a cut-off hand or blade inside the view.
            float alignment = action.Alignment switch { "left" => 0F, "right" => 1F, _ => .5F };
            appearance.SetSpriteViewport(new SpriteViewportUpdateRequest(viewmodel.Appearance, true, Vector2.Zero, Vector2.One, new Vector2(alignment, 0F), SpriteViewportFit.Contain));
            // The classic weapon animation's authored table rate is its authoring default; the tick
            // time the rules published for this swing (FORM-04.GetMeleeWeaponAnimTime) is what the
            // donor actually plays at, so a hastened or slowed player swings at that rate.
            double framesPerSecond = frameSeconds > 0d ? 1d / frameSeconds : action.FramesPerSecond;
            SpritePlaybackFrame[] frames = SpriteAtlasAdapter.ToPlaybackFrames((action.Sequence ?? Enumerable.Range(action.FrameStart, action.FrameCount).ToArray())
                .Select(index => weapon.Frames.Single(frame => frame.Id == index).Id).ToArray(), checked((float)framesPerSecond));
            staged = appearance.CreateSpritePlayback(new SpritePlaybackCreateRequest(viewmodel.Appearance, viewmodel.Atlas, frames, Array.Empty<SpritePlaybackMarker>(), action.Loops ? SpritePlaybackLoopMode.Loop : SpritePlaybackLoopMode.OneShot, 1d));
            appearance.ControlSpritePlayback(new SpritePlaybackControlRequest(staged, SpritePlaybackControl.Start));
            SpritePlayback? old = viewmodel.Playback;
            viewmodel.Playback = staged;
            // Imported cells retain classic placement; Engine fits the complete
            // canvas to the viewport and advances the selected sequence.
            viewmodel.Strike = name != "idle";
            viewmodel.CompletedOuterUpdate = false;
            viewmodel.LastOuterUpdate = null;
            if (old is not null) Retire(old);
        }
        catch { staged?.Dispose(); throw; }
    }

    private void RetireViewmodel()
    {
        if (viewmodel is not { } weapon) return;
        // A retired viewmodel can never reach its strike's hit frame, so its admitted impact is
        // retired with it instead of landing later from nothing.
        RetireUnreportedImpact();
        viewmodel = null;
        Retire(weapon);
    }

    private ulong NextVisualEntityId()
    {
        while (nextVisualEntityId > 1)
        {
            ulong candidate = nextVisualEntityId--;
            if (!actors.ContainsKey(checked((long)candidate))) return candidate;
        }
        throw new InvalidOperationException("Presentation entity identities are exhausted.");
    }

    private NormalizedAttackSequence SelectAttack(IReadOnlyList<NormalizedAttackSequence> sequences, ulong generation, ulong step, long attacker, long target)
    {
        if (sequences.Count == 1 || random is null) return sequences[0];
        int roll = checked((int)random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, CombatRandomKey.MediaAttackAlternateScope, CombatRandomKey.For(generation, step, attacker, target, CombatRandomKey.MediaAttackAlternateSalt), 1, 100)).Value);
        int cumulative = 0;
        foreach (NormalizedAttackSequence sequence in sequences.Skip(1))
        {
            cumulative = checked(cumulative + sequence.Chance);
            if (roll <= cumulative) return sequence;
        }
        return sequences[0];
    }

    private string SelectHitCue(PresentationEventIdentity identity)
    {
        if (hitCues.Count == 0 || random is null) return hitCues.FirstOrDefault() ?? string.Empty;
        int ordinal = checked((int)random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, CombatRandomKey.MediaHitCueScope, CombatRandomKey.For(identity.Generation, identity.SimulationStep, identity.Attacker, identity.Target, CombatRandomKey.MediaHitCueSalt), 1, hitCues.Count)).Value);
        return hitCues[ordinal - 1];
    }

    private void StartState(long entityId, string stateName, NormalizedAttackSequence? attack)
    {
        if (actors.TryGetValue(entityId, out ActorVisual? visual)) StartState(entityId, stateName, visual, attack);
    }

    private void StartState(long entityId, string stateName, ActorVisual visual, NormalizedAttackSequence? attack = null)
    {
        if (visual.Live is null || !visual.Sprite.States.TryGetValue(stateName, out NormalizedSpriteState? state)) return;
        IReadOnlyList<int> source = attack?.SourceFrames ?? Enumerable.Range(0, state.SelectOrientation(0).Count).ToArray();
        List<int> sourceFrameIndices = [];
        List<SpritePlaybackStep> playbackSteps = [];
        for (int index = 0; index < source.Count; index++)
        {
            int value = source[index];
            if (value == -1) { playbackSteps.Add(new SpritePlaybackStep(null)); continue; }
            IReadOnlyList<uint> canonical = state.SelectOrientation(0);
            if (value < 0 || value >= canonical.Count) throw new InvalidOperationException("Normalized Daggerfall playback source index is outside the selected orientation.");
            playbackSteps.Add(new SpritePlaybackStep(canonical[value]));
            sourceFrameIndices.Add(value);
        }
        SpritePlaybackPlan playbackPlan = SpriteAtlasAdapter.ToPlaybackPlan(playbackSteps, state.EffectiveFramesPerSecond);
        SpritePlayback staged = appearance.CreateSpritePlayback(new SpritePlaybackCreateRequest(visual.Live, visual.Atlas, playbackPlan.Frames, playbackPlan.Markers, state.Loops ? SpritePlaybackLoopMode.Loop : SpritePlaybackLoopMode.OneShot, 1d));
        try { appearance.ControlSpritePlayback(new SpritePlaybackControlRequest(staged, SpritePlaybackControl.Start)); }
        catch { staged.Dispose(); throw; }
        SpritePlayback? previous = visual.Playback;
        visual.Playback = staged;
        visual.State = stateName;
        visual.CompletedOuterUpdate = false;
        visual.LastMarkerCrossing = 0;
        visual.LastPlaybackFrameIndex = 0;
        visual.ActiveState = state;
        visual.SourceFrameIndices = sourceFrameIndices;
        visual.Orientation = 0;
        if (previous is not null) Retire(previous);
    }

    private void EnsureRestState(long entityId, string requested)
    {
        if (!actors.TryGetValue(entityId, out ActorVisual? visual) || visual.Defeated || visual.State == requested) return;
        // A behavior transition out of Attack cancels the presentation-owned swing along with
        // the Kit pending attack. Without clearing this marker, a later idle playback receipt
        // could be mistaken for the canceled strike and keep the session gated indefinitely.
        visual.ActiveAttack = null;
        StartState(entityId, requested, visual);
    }

    private void TransitionToCorpse(long entityId)
    {
        if (!actors.TryGetValue(entityId, out ActorVisual? visual) || visual.Defeated) return;
        if (visual.Playback is { } playback) Retire(playback);
        if (visual.Live is { } live) Retire(live);
        visual.Playback = null;
        visual.Live = null;
        visual.ActiveAttack = null;
        visual.Defeated = true;
    }

    private static string PreferredRestState(NormalizedActorSprite sprite) => sprite.PreferredRestState ?? (sprite.States.ContainsKey("idle") ? "idle" : "move");
    internal static int RelativeSector(float actorHeadingRadians, float actorToCameraX, float actorToCameraZ)
    {
        if (!float.IsFinite(actorHeadingRadians) || !float.IsFinite(actorToCameraX) || !float.IsFinite(actorToCameraZ)) throw new ArgumentOutOfRangeException(nameof(actorHeadingRadians));
        if (actorToCameraX == 0f && actorToCameraZ == 0f) return 0;
        // Donor mobile sectors wind clockwise from forward: +X is sector 6.
        float bearing = MathF.Atan2(-actorToCameraX, -actorToCameraZ);
        float normalized = MathF.IEEERemainder(bearing + actorHeadingRadians, MathF.Tau);
        // DFU uses -RoundToInt(signedAngle / 45), including away-from-zero
        // half-sector ties. This bearing is its glTF-space equivalent.
        int sector = (int)MathF.Round(normalized / (MathF.PI / 4f), MidpointRounding.AwayFromZero);
        return ((sector % 8) + 8) % 8;
    }
    private void Retire(IDisposable value)
    {
        if (!nextRetired.Contains(value) && !priorRetired.Contains(value)) nextRetired.Add(value);
    }

    private static PresentationEventIdentity Event(long attacker, long target, ulong generation, ulong simulationStep, string outcome) => new(generation, simulationStep, attacker, target, outcome);

    private void Emit(string clipId, PresentationEventIdentity identity, ulong marker)
    {
        if (audio is null) return;
        if (!audioClips.TryGetValue(clipId, out AudioClip? clip))
        {
            if (audioBundle is null) return;
            clip = audioBundle.OpenClip(audio, clipId);
            audioClips.Add(clipId, clip);
        }
        string signalId = $"daggerfall.media.{identity.Generation}.{identity.SimulationStep}.{identity.Attacker}.{identity.Target}.{identity.Outcome}.{marker}.{clipId}";
        audio.Emit(new AudioEmitRequest(signalId, new AudioSourceDescriptor(clip, AudioBus.Sfx, audioTuning.Volume, audioTuning.Pitch, false, audioTuning.SpatialBlend, audioTuning.Attenuation, 0F, AudioEmitterKind.Global2d, Vector3.Zero, 0, Vector3.Zero)));
    }

    internal sealed class ActorVisual(long entityId, NormalizedActorSprite sprite, SpriteAtlas atlas, Appearance live, SpriteAtlas? corpseAtlas, Appearance? corpse)
    {
        internal long EntityId { get; } = entityId;
        internal NormalizedActorSprite Sprite { get; } = sprite;
        internal SpriteAtlas Atlas { get; } = atlas;
        internal Appearance? Live { get; set; } = live;
        internal SpriteAtlas? CorpseAtlas { get; } = corpseAtlas;
        internal Appearance? Corpse { get; } = corpse;
        internal SpritePlayback? Playback { get; set; }
        internal string State { get; set; } = string.Empty;
        internal bool Defeated { get; set; }
        internal bool CompletedOuterUpdate { get; set; }
        internal ulong LastMarkerCrossing { get; set; }
        internal uint LastPlaybackFrameIndex { get; set; }
        internal NormalizedSpriteState? ActiveState { get; set; }
        internal IReadOnlyList<int> SourceFrameIndices { get; set; } = Array.Empty<int>();
        internal int Orientation { get; set; }
        internal ActiveAttackPresentation? ActiveAttack { get; set; }
        internal AppearanceOuterUpdate? LastOuterUpdate { get; set; }
        internal void Dispose(ref List<Exception>? failures)
        {
            if (Playback is { } playback) PrivateersHoldAppearance.Dispose(playback, ref failures);
            Playback = null;
            if (Live is { } live) PrivateersHoldAppearance.Dispose(live, ref failures);
            Live = null;
            if (Corpse is { } corpse) PrivateersHoldAppearance.Dispose(corpse, ref failures);
        }
    }

    internal sealed class GroundVisual(long entityId, Appearance appearance) : IDisposable
    {
        internal long EntityId { get; } = entityId;
        internal Appearance Appearance { get; } = appearance;
        public void Dispose() => Appearance.Dispose();
        internal void Dispose(ref List<Exception>? failures) => PrivateersHoldAppearance.Dispose(Appearance, ref failures);
    }

    internal sealed class EffectVisual(ulong entityId, WorldPoint position, SpriteAtlas atlas, Appearance appearance, SpritePlayback playback) : IDisposable
    {
        internal ulong EntityId { get; } = entityId;
        internal WorldPoint Position { get; } = position;
        internal SpriteAtlas Atlas { get; } = atlas;
        internal Appearance Appearance { get; } = appearance;
        internal SpritePlayback Playback { get; } = playback;
        internal bool CompletedOuterUpdate { get; set; }
        internal AppearanceOuterUpdate? LastOuterUpdate { get; set; }
        internal void Dispose(ref List<Exception>? failures)
        {
            PrivateersHoldAppearance.Dispose(Playback, ref failures);
            PrivateersHoldAppearance.Dispose(Appearance, ref failures);
            PrivateersHoldAppearance.Dispose(Atlas, ref failures);
        }
        public void Dispose()
        {
            List<Exception>? failures = null;
            Dispose(ref failures);
            if (failures is { Count: > 0 }) throw new AggregateException(failures);
        }
    }

    internal sealed class ViewmodelVisual(NormalizedClassicWeapon weapon, ulong entityId, Transform transform, SpriteAtlas atlas, Appearance appearance) : IDisposable
    {
        internal NormalizedClassicWeapon Weapon { get; } = weapon;
        internal ulong EntityId { get; } = entityId;
        internal Transform Transform { get; set; } = transform;
        internal SpriteAtlas Atlas { get; } = atlas;
        internal Appearance Appearance { get; } = appearance;
        internal SpritePlayback? Playback { get; set; }
        internal bool Strike { get; set; }
        /// <summary>The move this weapon swing delivers when its animation reaches the hit frame, if any.</summary>
        internal PresentationEventIdentity? PendingImpact { get; set; }
        /// <summary>The frame of this swing's own animation that releases its impact.</summary>
        internal int HitFrame { get; set; } = DaggerfallFormulaPolicy.MeleeWeaponHitFrame;
        internal bool ImpactReported { get; set; }
        internal bool CompletedOuterUpdate { get; set; }
        internal AppearanceOuterUpdate? LastOuterUpdate { get; set; }
        internal void Dispose(ref List<Exception>? failures)
        {
            if (Playback is { } playback) PrivateersHoldAppearance.Dispose(playback, ref failures);
            PrivateersHoldAppearance.Dispose(Appearance, ref failures);
            PrivateersHoldAppearance.Dispose(Atlas, ref failures);
        }
        public void Dispose()
        {
            List<Exception>? failures = null;
            Dispose(ref failures);
            if (failures is { Count: > 0 }) throw new AggregateException(failures);
        }
    }

    internal readonly record struct PresentationEventIdentity(ulong Generation, ulong SimulationStep, long Attacker, long Target, string Outcome);
    internal readonly record struct AppearanceOuterUpdate(ulong Generation, ulong ControlRevision, ulong SimulationStep, uint AdmittedStepCount);
    internal readonly record struct ActiveAttackPresentation(PresentationEventIdentity Identity, string HitCue, ulong? DamageMarkerId = null, bool ImpactReported = false);
}
