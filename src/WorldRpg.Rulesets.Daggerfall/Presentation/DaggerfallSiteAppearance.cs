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

/// <summary>Publishes one admitted site profile's normalized visual closure through Engine-owned resources and sprite atlases.</summary>
internal sealed class DaggerfallSiteAppearance : IDisposable
{
    private readonly IGraphicsService appearance;
    private readonly IContentService content;
    private readonly IAudioService? audio;
    private readonly DaggerfallAudioBundle? audioBundle;
    private readonly IRandomService? random;
    private readonly DaggerfallPresentationAudioTuning audioTuning;
    private readonly DaggerfallSiteProfile inputs;
    private readonly Dictionary<string, AudioClip> audioClips = new(StringComparer.Ordinal);
    private readonly HashSet<AudioSignalHandle> oneShotSignals = [];
    private readonly Dictionary<AudioVoiceHandle, EnemyVoice> enemyVoices = [];
    private Func<bool?> playerVampireFemale = () => null;
    internal void UsePlayerVampireGender(Func<bool?> female) => playerVampireFemale = female;
    private Func<DaggerfallRacialKind?> playerBeastForm = () => null;
    internal void UsePlayerBeastForm(Func<DaggerfallRacialKind?> form) => playerBeastForm = form;
    private double? playerBeastVoiceSeconds;
    private readonly NormalizedBillboardSprite? candleSprite;
    private SpriteAtlas? candleAtlas;
    private Appearance? candleAppearance;
    private Vector3? candlePosition;
    private ulong candleVisualId;

    internal void UpdateMagicCandle(Vector3? position)
    {
        if (disposed || locationSuspended) position = null;
        candlePosition = position;
        if (position is null)
        {
            if (candleAppearance is { } candle) Retire(candle); candleAppearance = null;
            return;
        }
        if (candleAppearance is not null) return;
        var sprite = candleSprite ?? throw new InvalidOperationException("Normal light requires the published magic candle billboard 210/3.");
        candleVisualId = NextVisualEntityId();
        if (candleAtlas is null)
            (candleAtlas, candleAppearance) = CreateSprite(content, new NormalizedActorSprite(sprite.TexturePath,
                sprite.TextureSha256, sprite.AtlasWidth, sprite.AtlasHeight, sprite.Frames, sprite.InitialFrameId, sprite.Pivot, sprite.Size));
        else candleAppearance = appearance.CreateSpriteFromAtlas(new(candleAtlas, sprite.InitialFrameId, sprite.Pivot,
            sprite.Size, BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1f, 1f, 1f, 1f)));
    }

    private Func<float> enemyAudibleRange = () => 16F;
    internal void UseEnemyAudibleRange(Func<float> range) => enemyAudibleRange = range;
    private sealed record EnemyVoice(AudioVoice Voice, AudioSourceDescriptor Descriptor, long ActorId);

    internal void RefreshEnemyVoices(ActorsState state)
    {
        foreach (var (handle, entry) in enemyVoices.ToArray())
        {
            if (!state.TryGet(entry.ActorId, out var actor))
            { entry.Voice.Dispose(); enemyVoices.Remove(handle); continue; }
            var descriptor = entry.Descriptor with { MaxDistance = enemyAudibleRange(), Position = actor.Position.ToVector() };
            if (descriptor == entry.Descriptor) continue;
            audio!.UpdateVoice(new(entry.Voice, descriptor));
            enemyVoices[handle] = entry with { Descriptor = descriptor };
        }
    }
    private readonly IReadOnlyList<string> hitCues;
    private readonly IReadOnlyDictionary<string, NormalizedClassicEffect> classicEffects;
    private readonly NormalizedClassicPresentation classicPresentation;
    // Engine seals render-resource selection once Product.Create finishes.
    // Classic effects and the optional weapon remain lazy visual instances, but
    // their normalized texture handles must be admitted with the initial closure.
    private readonly Dictionary<string, RenderResourceInfo> classicTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<long, ActorVisual> actors = [];
    private readonly Dictionary<long, BillboardVisual> groundVisuals = [];
    private readonly Dictionary<long, BillboardVisual> npcVisuals = [];
    private readonly Dictionary<RangedShotIdentity, ulong> arrowVisualEntityIds = [];
    private readonly Dictionary<long, ulong> dungeonSpellVisualEntityIds = [];
    private readonly List<EffectVisual> effects = [];
    private ViewmodelVisual? viewmodel;
    // The next swing may select any authored strike. Report a bounded observation window
    // covering its longest animation at the current ruleset tick rate (or authored fallback).
    internal double InspectPlayerStrikeSeconds(EquipmentRead equipment, bool weaponDrawn, double frameSeconds) =>
        SelectPlayerWeapon(equipment, weaponDrawn)?.Actions.Where(pair => pair.Key.StartsWith("strike", StringComparison.Ordinal))
            .Select(pair => pair.Value.PlayedFrameCount *
                (frameSeconds > 0 ? frameSeconds : 1d / pair.Value.FramesPerSecond)).DefaultIfEmpty(0).Max() ?? 0;

    /// <summary>
    /// Whether the viewmodel is free for a new swing: no strike is still playing. Whether the weapon is
    /// drawn at all is session state the caller checks beside this.
    /// </summary>
    internal bool CanStartPlayerAttack => viewmodel?.Strike != true;
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
    private ulong questSoundEmission;
    internal void PlayQuestSound(string clip, string quest, int line, int count)
    {
        if (audio is null || audioBundle is null && !audioClips.ContainsKey(clip))
            throw new NotSupportedException($"Quest sound '{clip}' has no admitted audio owner.");
        Emit(clip, Event(0, 0, checked(++questSoundEmission), checked((ulong)count), $"quest:{quest}:{line}"), 0);
    }
    // Transient visuals count down through their own band of the snapshot's object identities.
    private ulong nextVisualEntityId = DaggerfallPresentationObjectIds.TransientVisualFirst;
    private readonly HashSet<PresentationEventIdentity> deliveredEvents = [];
    private readonly HashSet<PresentationEventIdentity> appliedImpacts = [];
    private readonly HashSet<(string Instance, DaggerfallEffectOutcomeKind Kind, ulong Generation, ulong Step)> effectFeedback = [];
    private readonly List<AttackImpactNotice> attackImpacts = [];
    private readonly List<SpriteAtlas> atlases = [];
    private SpriteAtlas? groundContainerAtlas;
    private readonly NormalizedBillboardSprite? groundContainerSprite;
    private readonly List<Material> materials = [];
    private readonly Dictionary<uint, Material> materialsBySlot = [];
    private readonly Dictionary<DaggerfallRdbDoorId, Appearance> doorVisuals = [];
    private readonly Dictionary<DaggerfallRdbDoorId, ulong> doorVisualEntityIds = [];
    private readonly Dictionary<DaggerfallRdbDoorId, Transform> doorVisualPoses = [];
    private readonly Dictionary<string, (Appearance Visual, ulong ObjectId)> actionModelVisuals = new(StringComparer.Ordinal);
    // Each city gate draws its open or its closed model; both are created with the location and the
    // current state's is published under the gate's one object identity.
    private readonly Dictionary<string, (Appearance Open, Appearance Closed, ulong ObjectId)> gateVisuals = new(StringComparer.Ordinal);
    private DaggerfallCityGates? cityGates;
    private DaggerfallDungeonMotionProjection? dungeonMotion;
    // Door, gate and action model visuals are drawn under identities of the location visual band rather than
    // their source or Engine entity identities, which other owners' visuals may share.
    private ulong nextLocationVisualId = DaggerfallPresentationObjectIds.LocationVisualFirst;
    private DaggerfallDoorRuntime? doors;
    // Engine resources are owning objects: a render resource opened here is released here, because the
    // materials, atlases and sprites that name it hold non-owning references.
    private readonly List<RenderResource> ownedResources = [];
    private readonly List<RenderResource> locationResources = [];
    private readonly List<Material> locationMaterials = [];
    private readonly List<IDisposable> priorRetired = [];
    private readonly List<IDisposable> nextRetired = [];
    // Each static mesh the profile draws, with its pose in the profile frame and its snapshot identity.
    private readonly List<(Appearance Appearance, Transform Pose, ulong EntityId)> world = [];
    private Appearance? arrowAppearance;
    private AuthoredWorldAppearance worldAppearance;
    private Action<List<AppearanceFact>>? appendSnapshotFacts;
    private Action? completeSnapshot;
    private bool locationSuspended;
    private bool disposed;

    internal void Rebase(Vector3 delta)
    {
        worldAppearance = worldAppearance with
        {
            Transform = worldAppearance.Transform with { Translation = worldAppearance.Transform.Translation + delta },
        };
        foreach (EffectVisual effect in effects)
            effect.Position = DaggerfallExteriorSessionOrigin.Shift(effect.Position, delta);
    }

    /// <param name="sessionPresentation">
    /// The session's classic presentation (weapon viewmodels, held-item visuals, spell effects), which the
    /// start site publishes for the whole session; null uses this site's own, as a lone-site composition does.
    /// </param>
    internal DaggerfallSiteAppearance(IContentService content, IGraphicsService appearance, DaggerfallSiteProfile inputs, IAudioService? audio = null, DaggerfallPresentationAudioTuning? audioTuning = null, IRandomService? random = null, DaggerfallAudioBundle? audioBundle = null, DaggerfallDoorRuntime? doors = null, DaggerfallDungeonMotionProjection? dungeonMotion = null, NormalizedClassicPresentation? sessionPresentation = null, DaggerfallCityGates? cityGates = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(inputs);
        this.inputs = inputs;
        this.doors = doors;
        this.dungeonMotion = dungeonMotion;
        this.cityGates = cityGates;
        this.appearance = appearance;
        this.content = content;
        candleSprite = inputs.BillboardSprites.GetValueOrDefault((210, 3));
        this.audio = audio;
        this.audioBundle = audioBundle;
        this.random = random;
        this.audioTuning = (audioTuning ?? DaggerfallTuning.Defaults.PresentationAudio).Validate();
        hitCues = inputs.Audio.Count == 0 ? [] : DaggerfallSiteContent.OrderedHitCues(inputs.Audio);
        classicPresentation = sessionPresentation ?? inputs.ClassicPresentation;
        classicEffects = classicPresentation.Effects.ToDictionary(effect => effect.Name, StringComparer.Ordinal);
        worldAppearance = inputs.WorldAppearance;
        try
        {
            AdmitLocationResources();
            DaggerfallMissileVisual? arrow = classicPresentation.WorldVisuals.SingleOrDefault(visual =>
                visual.MediaId == "visual.missile.arrow");
            if (arrow is not null)
            {
                arrowAppearance = appearance.CreateStaticMeshFromContent(
                    new StaticMeshContentAppearanceRequest(arrow.Path, worldAppearance.Tint));
                List<MeshMaterialBinding> bindings = [];
                foreach (DaggerfallMissileTextureBinding texture in arrow.Textures)
                {
                    RenderResourceInfo resource = appearance.OpenResource(new RenderResourceRequest(
                        texture.TexturePath, TextureFilter.Nearest, TextureWrap.Repeat));
                    ownedResources.Add(resource.Handle);
                    Material material = appearance.CreateMaterial(new MaterialRequest(
                        new Color(1F, 1F, 1F, 1F), resource.Handle, 1F,
                        new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false));
                    materials.Add(material);
                    bindings.Add(new MeshMaterialBinding(texture.MeshSlot, material));
                }
                appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(arrowAppearance, bindings.ToArray()));
            }
            if (inputs.Doors.Count != 0 && doors is null) throw new ArgumentException("Door visuals require the selected door runtime.", nameof(doors));
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
        if (disposed) throw new ObjectDisposedException(nameof(DaggerfallSiteAppearance));
        ArgumentNullException.ThrowIfNull(sprite);
        if (durableId <= 0) throw new ArgumentOutOfRangeException(nameof(durableId));
        if (actors.ContainsKey(durableId)) throw new InvalidOperationException($"Actor {durableId} already has an appearance.");
        ActorVisual visual = CreateActorVisual(content, durableId, sprite);
        try { actors.Add(durableId, visual); }
        catch { List<Exception>? failures = null; visual.Dispose(ref failures); if (failures is { Count: > 0 }) throw new AggregateException(failures); throw; }
    }

    internal void AdmitActor(long durableId, NormalizedActorSprite sprite)
    {
        if (!actors.ContainsKey(durableId)) AddActor(durableId, sprite);
    }

    /// <summary>Admits a source billboard as the live visual for a civilian actor.</summary>
    internal void AddActor(long durableId, NormalizedBillboardSprite sprite) => AddActor(durableId,
        new NormalizedActorSprite(sprite.TexturePath, sprite.TextureSha256, sprite.AtlasWidth, sprite.AtlasHeight,
            sprite.Frames, sprite.InitialFrameId, sprite.Pivot, sprite.Size));

    /// <summary>Whether a dynamic actor currently has a live visual in this projection.</summary>
    internal bool HasActor(long durableId) => actors.ContainsKey(durableId);

    internal void RetireActor(long durableId)
    {
        List<Exception>? failures = null;
        foreach (var (handle, entry) in enemyVoices.Where(pair => pair.Value.ActorId == durableId).ToArray())
        { Dispose(entry.Voice, ref failures); enemyVoices.Remove(handle); }
        if (actors.Remove(durableId, out ActorVisual? visual) && visual is not null)
            Retire(new RetiredActorVisual(this, visual));
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    internal void RetireAllActors()
    {
        foreach (long durableId in actors.Keys.Concat(enemyVoices.Values.Select(entry => entry.ActorId)).Distinct().ToArray())
            RetireActor(durableId);
    }

    /// <summary>
    /// Releases static location geometry and its material closure while retaining the profile's
    /// durable definitions. Actor, ground and effect visuals have their own lifecycle owners and
    /// are retired by the roster/cell coordinator before this method is called.
    /// </summary>
    internal void SuspendLocationResources()
    {
        if (disposed) throw new ObjectDisposedException(nameof(DaggerfallSiteAppearance));
        if (locationSuspended) return;
        UpdateMagicCandle(null);
        Retire(new RetiredLocationResources(TakeLocationResources()));
        locationSuspended = true;
    }

    /// <summary>Recreates the retained location geometry after its content placement is admitted.</summary>
    internal void ResumeLocationResources(DaggerfallDoorRuntime doors, DaggerfallDungeonMotionProjection motion)
    {
        if (disposed) throw new ObjectDisposedException(nameof(DaggerfallSiteAppearance));
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(motion);
        if (!locationSuspended) return;
        this.doors = doors;
        dungeonMotion = motion;
        try
        {
            AdmitLocationResources();
            locationSuspended = false;
        }
        catch
        {
            List<Exception>? failures = null;
            ReleaseLocationResources(ref failures);
            if (failures is { Count: > 0 }) throw new AggregateException(failures);
            throw;
        }
    }

    /// <summary>Publishes world and viewport weapon appearances; Engine owns projection and fitting.</summary>
    internal void Publish(ActorsState actors)
        => Publish(actors, new Dictionary<long, DaggerfallGroundContainer>());

    /// <summary>Publishes the active ground-container projection through the same Engine snapshot as actors.</summary>
    internal void Publish(ActorsState actors, IReadOnlyDictionary<long, DaggerfallGroundContainer> groundContainers,
        IReadOnlyList<DaggerfallRangedFlightView>? rangedFlights = null, float arrowHeight = 0f,
        Func<long, DaggerfallPerceptionEffectState>? perception = null, IReadOnlyList<DaggerfallNpcView>? npcs = null,
        IReadOnlyList<DaggerfallDungeonSpellFlightView>? dungeonSpellFlights = null, Func<long, bool>? actorActive = null)
    {
        if (disposed) return;
        ReconcileGroundVisuals(groundContainers);
        List<AppearanceFact> facts = [];
        if (candleAppearance is { } candle && candlePosition is Vector3 candleAt)
            facts.Add(new(candleVisualId, false, 0, new Transform(candleAt, Quaternion.Identity, Vector3.One), candle, true, RenderLayer.Scene));
        foreach ((Appearance staticWorld, Transform pose, ulong entityId) in world)
            facts.Add(new AppearanceFact(entityId, false, 0, Compose(worldAppearance.Transform, pose), staticWorld, worldAppearance.Visible, worldAppearance.Layer));
        if (doors is not null) foreach (DaggerfallDoorView door in doors.All)
            if (doorVisuals.TryGetValue(door.Id, out Appearance? visual))
                facts.Add(new AppearanceFact(doorVisualEntityIds[door.Id], false, 0,
                    doorVisualPoses.TryGetValue(door.Id, out Transform local) ? Compose(door.Pose, local) : door.Pose, visual, true, RenderLayer.Scene));
        if (cityGates is not null)
            foreach ((DaggerfallCityGateDefinition gate, EntityId _, DaggerfallDoorVisual _, Transform pose) in cityGates.Visuals)
                if (gateVisuals.TryGetValue(gate.Id, out (Appearance Open, Appearance Closed, ulong ObjectId) visual))
                    facts.Add(new AppearanceFact(visual.ObjectId, false, 0, pose, cityGates.Open ? visual.Open : visual.Closed, true, RenderLayer.Scene));
        if (dungeonMotion is not null)
        {
            foreach ((DaggerfallDungeonActionModelDefinition model, EntityId _) in dungeonMotion.Visuals)
            {
                if (actionModelVisuals.TryGetValue(model.ActionId, out (Appearance Visual, ulong ObjectId) visual)
                    && dungeonMotion.TryGetTransform(model.ActionId, out Transform transform))
                    facts.Add(new AppearanceFact(visual.ObjectId, false, 0, transform, visual.Visual, true, RenderLayer.Scene));
            }
        }
        foreach (ActorState actor in actors.All)
        {
            if (!this.actors.TryGetValue(actor.DurableId, out ActorVisual? visual)) continue;
            Appearance? chosen = actor.IsDefeated ? visual.Corpse : visual.Live;
            if (chosen is not null) facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.Gameplay(actor.DurableId, "Actor"), false, 0, new Transform(actor.Position.ToVector(), Quaternion.Identity, Vector3.One), chosen,
                actorActive?.Invoke(actor.DurableId) != false && (actor.IsDefeated || perception?.Invoke(actor.DurableId).Invisible != true), RenderLayer.Scene));
        }
        foreach (DaggerfallGroundContainer container in groundContainers.Values.OrderBy(container => container.Id))
        {
            if (groundVisuals.TryGetValue(container.Id, out BillboardVisual? visual))
                facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.Gameplay(container.Id, "Ground container"), false, 0,
                    new Transform(container.Position.ToVector(), Quaternion.Identity, Vector3.One), visual.Appearance, true, RenderLayer.Scene));
        }
        foreach (var npc in npcs ?? [])
            if (npcVisuals.TryGetValue(npc.Id, out var visual))
                facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.Gameplay(npc.Id, "NPC"), false, 0,
                    new Transform(npc.Position.ToVector(), Quaternion.Identity, Vector3.One), visual.Appearance, true, RenderLayer.Scene));
        foreach (EffectVisual effect in effects)
            facts.Add(new AppearanceFact(effect.EntityId, false, 0, new Transform(effect.Position.ToVector(), Quaternion.Identity, Vector3.One), effect.Appearance, true, RenderLayer.Scene));
        if (arrowAppearance is { } arrowVisual)
        {
            IReadOnlyList<DaggerfallRangedFlightView> flights = rangedFlights ?? [];
            HashSet<RangedShotIdentity> active = [.. flights.Select(flight => flight.Identity)];
            foreach (RangedShotIdentity retired in arrowVisualEntityIds.Keys.Where(id => !active.Contains(id)).ToArray())
                arrowVisualEntityIds.Remove(retired);
            foreach (DaggerfallRangedFlightView flight in flights)
            {
                if (!arrowVisualEntityIds.TryGetValue(flight.Identity, out ulong visualId))
                    arrowVisualEntityIds.Add(flight.Identity, visualId = NextVisualEntityId());
                Vector3 direction = flight.Direction.LengthSquared() > .000001f
                    ? flight.Direction : Vector3.UnitZ;
                Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > .99f
                    ? Vector3.UnitX : Vector3.UnitY;
                Quaternion rotation = Quaternion.CreateFromRotationMatrix(
                    Matrix4x4.CreateWorld(Vector3.Zero, direction, up));
                facts.Add(new AppearanceFact(visualId, false, 0,
                    new Transform(flight.Position.ToVector() + Vector3.UnitY * arrowHeight,
                        rotation, Vector3.One), arrowVisual, true, RenderLayer.Scene));
            }
            IReadOnlyList<DaggerfallDungeonSpellFlightView> spellFlights = dungeonSpellFlights ?? [];
            HashSet<long> activeSpells = [.. spellFlights.Select(flight => flight.Sequence)];
            foreach (long retired in dungeonSpellVisualEntityIds.Keys.Where(id => !activeSpells.Contains(id)).ToArray())
                dungeonSpellVisualEntityIds.Remove(retired);
            foreach (DaggerfallDungeonSpellFlightView flight in spellFlights)
            {
                if (!dungeonSpellVisualEntityIds.TryGetValue(flight.Sequence, out ulong spellVisualId))
                    dungeonSpellVisualEntityIds.Add(flight.Sequence, spellVisualId = NextVisualEntityId());
                Vector3 direction = flight.Direction.LengthSquared() > .000001f
                    ? flight.Direction : Vector3.UnitZ;
                Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > .99f
                    ? Vector3.UnitX : Vector3.UnitY;
                Quaternion rotation = Quaternion.CreateFromRotationMatrix(
                    Matrix4x4.CreateWorld(Vector3.Zero, direction, up));
                facts.Add(new AppearanceFact(spellVisualId, false, 0,
                    new Transform(flight.Position.ToVector() + Vector3.UnitY * arrowHeight,
                        rotation, Vector3.One), arrowVisual, true, RenderLayer.Scene));
            }
        }
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
        RetireRealizedOneShots();
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
                if (!StartWeaponStrike(swing, started.Swing, started.FrameSeconds, started.TargetId, started.HitFrame))
                {
                    attackImpacts.Add(new AttackImpactNotice(DaggerfallActorIdentity.PlayerEntityId, started.TargetId ?? 0,
                        started.OriginatingGeneration, started.OriginatingSimulationStep, Expired: false));
                    if (started.Feedback.SwingCue != "sound.3") EmitPlayerVampireVoice(swing);
                }
                else if (started.Feedback.SwingCue != "sound.3") viewmodel!.PendingAttackVoice = swing;
                Emit(started.Feedback.SwingCue, swing, 0);
                deliveredEvents.Add(swing);
                break;
            case EnemyAttackStartedFact started:
                // An enemy swing starts here; its consequence arrives later, when the
                // authored damage frame is reached. A sequence with no damage frame
                // resolves inside this same update instead of never landing.
                PresentationEventIdentity startEvent = Event(started.AttackerId, started.TargetId, started.OriginatingGeneration, started.OriginatingSimulationStep, started.WillHit ? "hit" : "miss");
                if (deliveredEvents.Contains(startEvent)) break;
                deliveredEvents.Add(startEvent);
                if (started.Feedback.SwingCue == "sound.3") EmitAtActor(started.Feedback.SwingCue, startEvent, started.AttackerId, actorState);
                if (actors.TryGetValue(started.AttackerId, out ActorVisual? attackingVisual)
                    && CanVoice(attackingVisual.Sprite.Feedback)
                    && DrawCue(startEvent, "attack", 1, 100) <= audioTuning.AttackCueChancePercent)
                    EmitAtActor(attackingVisual.Sprite.Feedback!.AttackCue, startEvent, started.AttackerId, actorState);
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
                if (hit.ActualHealthLost <= 0) { EmitMiss(hit.Feedback, hitEvent, actorState); break; }
                if (hit.AttackerId == DaggerfallActorIdentity.PlayerEntityId && playerBeastForm() is { } beast)
                {
                    string? cue = DrawCue(hitEvent, "beast-attack", 1, 100) <= audioTuning.BeastAttackChancePercent
                        ? beast == DaggerfallRacialKind.Werewolf ? "sound.144" : "sound.159"
                        : DrawCue(hitEvent, "beast-bark", 1, 100) <= audioTuning.BeastBarkChancePercent ? beast == DaggerfallRacialKind.Werewolf ? "sound.143" : "sound.158" : null;
                    if (cue is not null) Emit(cue, hitEvent, 0);
                }
                EmitAtActor(SelectHitCue(hitEvent, hit.Feedback.Weapon), hitEvent,
                    hit.TargetId == DaggerfallActorIdentity.PlayerEntityId ? hit.AttackerId : hit.TargetId,
                    actorState, pitch: audioTuning.ContactPitch);
                StartState(hit.TargetId, "hurt", null);
                if (hit.AttackerId == DaggerfallActorIdentity.PlayerEntityId && actorState is not null) SpawnBlood(hit, hitEvent, actorState);
                break;
            case AttackMissedFact miss:
                PresentationEventIdentity missEvent = Event(miss.AttackerId, miss.TargetId, miss.OriginatingGeneration, miss.OriginatingSimulationStep, "miss");
                if (!appliedImpacts.Add(missEvent)) break;
                EmitMiss(miss.Feedback, missEvent, actorState);
                if (miss.AttackerId == DaggerfallActorIdentity.PlayerEntityId && deliveredEvents.Add(missEvent))
                    StartAttack(miss.AttackerId, miss.TargetId, miss.OriginatingGeneration, miss.OriginatingSimulationStep, missEvent);
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

    /// <summary>
    /// Selects authored equipped art, falling back to empty hands, or none while the session holds the
    /// weapon sheathed; Engine owns sprite realization.
    /// </summary>
    internal void UpdateRightHandEquipment(EquipmentRead equipment, bool weaponDrawn)
    {
        ArgumentNullException.ThrowIfNull(equipment);
        NormalizedClassicWeapon? selected = SelectPlayerWeapon(equipment, weaponDrawn);
        if (viewmodel?.Weapon.ResourceId == selected?.ResourceId) return;
        RetireViewmodel();
        if (selected is not null && classicPresentation.Viewmodel is not null) CreateViewmodel(selected);
    }

    private NormalizedClassicWeapon? SelectPlayerWeapon(EquipmentRead equipment, bool weaponDrawn)
    {
        string? resource = playerBeastForm() is not null ? "weapon.werecreature" : null;
        foreach (string slot in new[] { "right-hand", "left-hand" })
            if (resource is null && equipment.TryGet(new EquipmentSlotId(slot), out UniqueInventoryItem item)
                && classicPresentation.CompatibleItemVisuals.TryGetValue(item.Definition.Value, out resource)) break;
        resource ??= classicPresentation.UnarmedVisual;
        return weaponDrawn && resource is not null
            && classicPresentation.Weapons.TryGetValue(resource, out NormalizedClassicWeapon? weapon) ? weapon : null;
    }

    /// <summary>Called exactly once from the outer Product.Update, never from a private catch-up step.</summary>
    /// <summary>The marker a weapon swing's playback carries at its hit frame.</summary>
    private const ulong WeaponHitMarkerId = 1;

    internal void Advance(ProductUpdateFacts update)
    {
        if (disposed) return;
        AppearanceOuterUpdate identity = new(update.Generation, update.ControlRevision, update.SimulationStep, update.AdmittedStepCount);
        foreach (ActorVisual visual in actors.Values)
        {
            if (visual.Playback is null) continue;
            if (visual.LastOuterUpdate == identity) continue;
            SpritePlaybackAdvanceResult receipt = appearance.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(visual.Playback));
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
                    // The marker admits an impact; only an applied result can sound a hit.
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
            SpritePlaybackAdvanceResult receipt = appearance.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(effect.Playback));
            if (receipt.Readout.Completed)
            {
                if (effect.CompletedOuterUpdate) { effects.Remove(effect); Retire(effect); }
                else if (receipt.Advanced) effect.CompletedOuterUpdate = true;
            }
            effect.LastOuterUpdate = identity;
        }
        if (viewmodel is { } weapon && weapon.Playback is { } weaponPlayback && weapon.LastOuterUpdate != identity)
        {
            SpritePlaybackAdvanceResult receipt = appearance.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(weaponPlayback));
            // The classic swing's damage lands on its hit frame. One decided swing owns one beat: the
            // Engine reports the hit marker's crossing once, and later frames of the same swing do not.
            foreach (SpritePlaybackMarkerCrossing crossing in receipt.Crossings.Span)
                if (crossing.MarkerId == WeaponHitMarkerId) weapon.HitCrossed = true;
            if (weapon.Strike && weapon.PendingAttackVoice is { } voice && weapon.HitCrossed)
            {
                weapon.PendingAttackVoice = null;
                EmitPlayerVampireVoice(voice);
            }
            if (weapon.Strike && weapon.PendingImpact is { } pending && !weapon.ImpactReported
                && weapon.HitCrossed)
            {
                weapon.ImpactReported = true;
                attackImpacts.Add(new AttackImpactNotice(pending.Attacker, pending.Target, pending.Generation, pending.SimulationStep, Expired: false));
            }
            if (weapon.Strike && receipt.Readout.Completed)
            {
                // Keep the first completed frame visible, then return to idle on the next
                // admitted update. A completed one-shot no longer advances; the previous
                // advancing completion is sufficient evidence to release the attack guard.
                if (weapon.CompletedOuterUpdate) StartWeaponAction("idle");
                else if (receipt.Advanced)
                {
                    RetireUnreportedImpact();
                    weapon.CompletedOuterUpdate = true;
                }
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
        foreach (ActorVisual visual in actors.Values.Reverse()) DisposeActorVisual(visual, ref failures);
        if (candleAppearance is { } candle) Dispose(candle, ref failures); candleAppearance = null;
        foreach (BillboardVisual visual in groundVisuals.Values.Reverse()) visual.Dispose(ref failures);
        foreach (BillboardVisual visual in npcVisuals.Values.Reverse()) visual.Dispose(ref failures);
        foreach (EffectVisual effect in effects.AsEnumerable().Reverse()) effect.Dispose(ref failures);
        effects.Clear();
        if (viewmodel is { } weapon) { viewmodel = null; weapon.Dispose(ref failures); }
        foreach (IDisposable value in nextRetired.AsEnumerable().Reverse()) Dispose(value, ref failures);
        foreach (IDisposable value in priorRetired.AsEnumerable().Reverse()) Dispose(value, ref failures);
        nextRetired.Clear(); priorRetired.Clear();
        actors.Clear();
        groundVisuals.Clear();
        npcVisuals.Clear();
        if (arrowAppearance is { } arrowVisual) { arrowAppearance = null; Dispose(arrowVisual, ref failures); }
        arrowVisualEntityIds.Clear();
        dungeonSpellVisualEntityIds.Clear();
        foreach (Appearance visual in doorVisuals.Values.Reverse()) Dispose(visual, ref failures);
        doorVisuals.Clear();
        doorVisualEntityIds.Clear();
        doorVisualPoses.Clear();
        foreach ((Appearance visual, ulong _) in actionModelVisuals.Values.Reverse()) Dispose(visual, ref failures);
        actionModelVisuals.Clear();
        // Sprite atlases borrow textures that location materials may also use. Retire every
        // atlas before releasing the location material/resource closure so dependents observe a
        // deterministic atlas -> material -> resource lifetime.
        foreach (SpriteAtlas atlas in atlases.AsEnumerable().Reverse()) Dispose(atlas, ref failures);
        atlases.Clear();
        groundContainerAtlas = null;
        ReleaseLocationResources(ref failures);
        foreach (Material material in materials.AsEnumerable().Reverse()) Dispose(material, ref failures);
        materials.Clear();
        foreach (RenderResource resource in ownedResources.AsEnumerable().Reverse()) Dispose(resource, ref failures);
        ownedResources.Clear();
        foreach (AudioSignalHandle signal in oneShotSignals)
            try { audio!.RetireOneShot(signal); } catch (Exception exception) { (failures ??= []).Add(exception); }
        oneShotSignals.Clear();
        foreach (var voice in enemyVoices.Values) Dispose(voice.Voice, ref failures);
        enemyVoices.Clear();
        foreach (AudioClip clip in audioClips.Values.Reverse()) Dispose(clip, ref failures);
        audioClips.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private void AdmitLocationResources()
    {
        if (inputs.Doors.Count != 0 && doors is null)
            throw new ArgumentException("Door visuals require the selected door runtime.", nameof(doors));
        foreach (NormalizedMaterial material in inputs.Materials)
        {
            RenderResourceInfo texture = appearance.OpenResource(
                new RenderResourceRequest(material.TexturePath, TextureFilter.Nearest, TextureWrap.Repeat));
            locationResources.Add(texture.Handle);
            Material created = appearance.CreateMaterial(new MaterialRequest(
                new Color(1F, 1F, 1F, 1F), texture.Handle, 1F,
                new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false));
            locationMaterials.Add(created);
            materials.Add(created);
            materialsBySlot.Add(material.Slot, created);
        }
        for (int index = 0; index < inputs.Geometry.Meshes.Count; index++)
        {
            DaggerfallSiteMesh mesh = inputs.Geometry.Meshes[index];
            Appearance created = appearance.CreateStaticMeshFromContent(new StaticMeshContentAppearanceRequest(mesh.Path, worldAppearance.Tint));
            world.Add((created, mesh.Pose, DaggerfallPresentationObjectIds.WorldMesh(index)));
            // A published closure's combined mesh is drawn with every material at its own slot; an assembled
            // block model binds each of its slots to the profile slot its (climate-swapped) texture has.
            MeshMaterialBinding[] bindings = mesh.Materials is null
                ? [.. inputs.Materials.Select(material => new MeshMaterialBinding(material.Slot, materialsBySlot[material.Slot]))]
                : [.. mesh.Materials.Select(binding => materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? material)
                    ? new MeshMaterialBinding(binding.MeshSlot, material)
                    : throw new InvalidOperationException($"Mesh '{mesh.Path}' refers to missing world material slot {binding.WorldMaterialSlot}."))];
            appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(created, bindings));
        }
        foreach (DaggerfallRdbDoorDefinition door in inputs.Doors)
        {
            // An assembled exterior's door is a plane of its model's mesh, which draws it: the door is entered
            // rather than swung, so it has no visual of its own.
            if (door.Visual is null && inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior) continue;
            DaggerfallDoorVisual visual = door.Visual
                ?? throw new InvalidOperationException($"Selected RDB door '{door.Id}' has no normalized visual.");
            Appearance created = appearance.CreateStaticMeshFromContent(
                new StaticMeshContentAppearanceRequest(visual.Path, worldAppearance.Tint));
            appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(created, visual.Materials
                .Select(binding => materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? material)
                    ? new MeshMaterialBinding(binding.MeshSlot, material)
                    : throw new InvalidOperationException($"Door '{door.Id}' refers to missing world material slot {binding.WorldMaterialSlot}."))
                .ToArray()));
            doorVisuals.Add(door.Id, created);
            doorVisualEntityIds.Add(door.Id, NextLocationVisualId());
            if (visual.LocalPose is { } local) doorVisualPoses.Add(door.Id, local);
        }
        foreach (DaggerfallCityGateDefinition gate in inputs.CityGates)
            gateVisuals.Add(gate.Id, (StaticVisual(gate.Open.Visual, $"City gate '{gate.Id}'"), StaticVisual(gate.Closed.Visual, $"City gate '{gate.Id}'"),
                NextLocationVisualId()));
        if (dungeonMotion is null) return;
        foreach ((DaggerfallDungeonActionModelDefinition model, _) in dungeonMotion.Visuals)
        {
            Appearance created = appearance.CreateStaticMeshFromContent(
                new StaticMeshContentAppearanceRequest(model.Visual.Path, worldAppearance.Tint));
            try
            {
                appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(created, model.Visual.Materials
                    .Select(binding => materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? material)
                        ? new MeshMaterialBinding(binding.MeshSlot, material)
                        : throw new InvalidOperationException($"Action model '{model.ActionId}' refers to missing world material slot {binding.WorldMaterialSlot}."))
                    .ToArray()));
                actionModelVisuals.Add(model.ActionId, (created, NextLocationVisualId()));
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

    /// <summary>A static mesh appearance drawing a visual with the location's materials at its slots.</summary>
    private Appearance StaticVisual(DaggerfallDoorVisual visual, string owner)
    {
        Appearance created = appearance.CreateStaticMeshFromContent(new StaticMeshContentAppearanceRequest(visual.Path, worldAppearance.Tint));
        try
        {
            appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(created, visual.Materials
                .Select(binding => materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? material)
                    ? new MeshMaterialBinding(binding.MeshSlot, material)
                    : throw new InvalidOperationException($"{owner} refers to missing world material slot {binding.WorldMaterialSlot}."))
                .ToArray()));
            return created;
        }
        catch
        {
            List<Exception>? failures = null;
            Dispose(created, ref failures);
            if (failures is { Count: > 0 }) throw new AggregateException(failures);
            throw;
        }
    }

    private void ReleaseLocationResources(ref List<Exception>? failures)
    {
        foreach (IDisposable value in TakeLocationResources()) Dispose(value, ref failures);
    }

    private IDisposable[] TakeLocationResources()
    {
        List<IDisposable> retired = [];
        retired.AddRange(world.Select(part => part.Appearance).Reverse());
        world.Clear();
        retired.AddRange(doorVisuals.Values.Reverse());
        doorVisuals.Clear();
        doorVisualEntityIds.Clear();
        doorVisualPoses.Clear();
        retired.AddRange(actionModelVisuals.Values.Select(value => value.Visual).Reverse());
        actionModelVisuals.Clear();
        foreach ((Appearance open, Appearance closed, ulong _) in gateVisuals.Values.Reverse()) retired.AddRange([closed, open]);
        gateVisuals.Clear();
        retired.AddRange(locationMaterials.AsEnumerable().Reverse());
        foreach (Material material in locationMaterials) materials.Remove(material);
        locationMaterials.Clear();
        materialsBySlot.Clear();
        retired.AddRange(locationResources.AsEnumerable().Reverse());
        locationResources.Clear();
        return [.. retired];
    }

    private void DisposeActorVisual(ActorVisual visual, ref List<Exception>? failures)
    {
        visual.Dispose(ref failures);
        if (visual.CorpseAtlas is { } corpseAtlas)
        {
            atlases.Remove(corpseAtlas);
            Dispose(corpseAtlas, ref failures);
        }
        atlases.Remove(visual.Atlas);
        Dispose(visual.Atlas, ref failures);
    }

    /// <summary>A profile-frame pose placed by the world appearance's transform.</summary>
    private static Transform Compose(Transform world, Transform pose) => new(
        world.Translation + Vector3.Transform(pose.Translation * world.Scale, world.Rotation),
        Quaternion.Normalize(world.Rotation * pose.Rotation),
        world.Scale * pose.Scale);

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

    internal void AddNpc(long id, NormalizedBillboardSprite sprite)
    {
        if (npcVisuals.ContainsKey(id)) return;
        var (_, visual) = CreateSprite(content, new NormalizedActorSprite(sprite.TexturePath, sprite.TextureSha256,
            sprite.AtlasWidth, sprite.AtlasHeight, sprite.Frames, sprite.InitialFrameId, sprite.Pivot, sprite.Size));
        npcVisuals.Add(id, new(id, visual));
    }

    internal void RetireNpc(long id)
    {
        if (npcVisuals.Remove(id, out var visual)) Retire(visual);
    }

    private void ReconcileGroundVisuals(IReadOnlyDictionary<long, DaggerfallGroundContainer> containers)
    {
        foreach (long id in groundVisuals.Keys.Where(id => !containers.ContainsKey(id)).ToArray())
        {
            BillboardVisual visual = groundVisuals[id];
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
            groundVisuals.Add(container.Id, new BillboardVisual(container.Id, visual));
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
        // The authored damage frame is the playback marker at its own source position: a source step
        // number is carried as a marker, and its identity is that step's place in the sequence.
        ulong? damageMarker = null;
        for (int index = 0; index < selected.SourceFrames.Count; index++)
            if (selected.SourceFrames[index] == -1) { damageMarker = checked((ulong)index + 1); break; }
        visual.ActiveAttack = new ActiveAttackPresentation(presentationEvent, damageMarker);
        bool hasDamageFrame = damageMarker is not null;

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
        if (viewmodel is not null) viewmodel.PendingAttackVoice = null;
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
        int blood = this.actors.TryGetValue(hit.TargetId, out var visual) ? visual.Sprite.Feedback?.BloodIndex ?? 0 : 0;
        string name = $"blood{blood}";
        // Classic media belongs to presentation policy; the combat fact already
        // owns applied damage. A retry is stopped by deliveredEvents above.
        // These resources are reconstructed from the admitted normalized pack.
        // Missing content deliberately means no invented replacement effect.
        // The effect is world-positioned at the fact's truthful target state.
        // (A future spell fact can select magicSparkle independently.)
        //
        // The selected resource is resolved through the stored normalized input
        // at construction-time via the effect catalog injected below.
        SpawnEffect(name, position, identity);
    }

    /// <summary>Completed compiled effect meaning uses the same admitted effect projection; restore emits no outcome.</summary>
    internal void ReactEffectOutcome(DaggerfallEffectOutcome outcome, ActorsState actors, WorldPoint? player,
        ulong generation, ulong step)
    {
        if (outcome.Kind is DaggerfallEffectOutcomeKind.Cancelled or DaggerfallEffectOutcomeKind.Cured or DaggerfallEffectOutcomeKind.Expired)
        {
            RetireMagic(effect => effect.ActiveInstance == outcome.Instance);
            effectFeedback.RemoveWhere(key => key.Instance == outcome.Instance);
            return;
        }
        if (disposed || outcome.Feedback != DaggerfallEffectFeedback.MagicSparkle
            || outcome.Kind is not (DaggerfallEffectOutcomeKind.Started or DaggerfallEffectOutcomeKind.Refreshed or DaggerfallEffectOutcomeKind.Replaced)) return;
        WorldPoint? position = outcome.TargetId == DaggerfallActorIdentity.PlayerEntityId ? player
            : actors.TryGet(outcome.TargetId, out ActorState target) ? target.Position : null;
        if (position is not WorldPoint targetPosition || !effectFeedback.Add((outcome.Instance, outcome.Kind, generation, step))) return;
        SpawnEffect("magicSparkle", targetPosition, Event(0, outcome.TargetId, generation, step, "effect"),
            outcome.CompletedImmediately ? null : outcome.Instance, outcome.TargetId, outcome.CasterId, outcome.ItemId);
    }

    internal void ReactSpellCast(SpellCastFact cast, ActorsState actors, WorldPoint? player)
    {
        if (disposed || cast.Outcome != DaggerfallCastOutcome.Released || cast.Sequence is not long sequence || cast.CasterId is not long caster) return;
        var identity = Event(caster, caster, checked((ulong)sequence), 0, "spell-release");
        if (!deliveredEvents.Add(identity)) return;
        WorldPoint? position = caster == DaggerfallActorIdentity.PlayerEntityId ? player
            : actors.TryGet(caster, out ActorState source) ? source.Position : null;
        if (position is null) return;
        string clip = cast.Element switch { 0 => "fireCast", 1 => "coldCast", 2 => "poisonCast", 3 => "shockCast", 4 => "magicCast", _ => "" };
        Emit(clip, identity, 0, position);
    }

    internal void RetireUnavailableMagic(ActorsState actors, Func<ulong, bool> itemAvailable)
    {
        bool Alive(long id) => id == actors.Player.DurableId ? !actors.Player.IsDefeated
            : actors.TryGet(id, out ActorState actor) && !actor.IsDefeated;
        RetireMagic(effect => effect.TargetActor is long target && !Alive(target)
            || effect.SourceActor is long source && !Alive(source)
            || effect.SourceItem is ulong item && !itemAvailable(item));
    }

    private void RetireMagic(Func<EffectVisual, bool> predicate)
    {
        foreach (var effect in effects.Where(predicate).ToArray())
        {
            effects.Remove(effect);
            Retire(effect);
        }
    }

    private void SpawnEffect(string name, WorldPoint position, PresentationEventIdentity identity,
        string? activeInstance = null, long? target = null, long? source = null, ulong? item = null)
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
            effects.Add(new EffectVisual(NextVisualEntityId(), position, atlas, visual, playback)
            { ActiveInstance = activeInstance, TargetActor = target, SourceActor = source, SourceItem = item });
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
    private bool StartWeaponStrike(PresentationEventIdentity identity, DaggerfallSwingDirection swing, double frameSeconds = 0d,
        long? target = null, int hitFrame = DaggerfallFormulaPolicy.MeleeWeaponHitFrame)
    {
        if (viewmodel is null) return false;
        // The rules chose the swing: the bow's fixed strike or the direction the player drew. A swing
        // drawn without a gesture is the donor's click attack, which picks uniformly among the six
        // directions UpRight..DownRight, i.e. among the six strike states.
        IReadOnlyList<string> choices = NormalizedClassicWeapon.StrikeActions;
        string name = swing != DaggerfallSwingDirection.None ? NormalizedClassicWeapon.StrikeAction(swing)
            : choices[random is null ? 0 : checked((int)random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, "daggerfall.media.weapon-strike.v1", CombatRandomKey.For(identity.Generation, identity.SimulationStep, identity.Attacker, identity.Target, 44), 0, choices.Count - 1)).Value)];
        if (!viewmodel.Weapon.Actions.TryGetValue(name, out NormalizedClassicWeaponAction? strike)) return false;
        // Admission refuses art whose strikes end before the hit frame the rules use, so a strike that
        // cannot reach it is a broken composition: playing it would spend the swing (and a bow's arrow)
        // and retire its impact unreported.
        if (hitFrame < 0 || hitFrame >= strike.PlayedFrameCount)
            throw new InvalidOperationException($"Classic weapon '{viewmodel.Weapon.ResourceId}' strike '{name}' plays {strike.PlayedFrameCount} frames and cannot reach the swing's hit frame {hitFrame}.");
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
            // A swing's hit frame is an Engine playback marker, so its one crossing is reported even
            // when an update skips past the frame.
            SpritePlaybackMarker[] markers = name != "idle"
                ? [new SpritePlaybackMarker(WeaponHitMarkerId, checked((uint)viewmodel.HitFrame))] : [];
            staged = appearance.CreateSpritePlayback(new SpritePlaybackCreateRequest(viewmodel.Appearance, viewmodel.Atlas, frames, markers, action.Loops ? SpritePlaybackLoopMode.Loop : SpritePlaybackLoopMode.OneShot, 1d));
            appearance.ControlSpritePlayback(new SpritePlaybackControlRequest(staged, SpritePlaybackControl.Start));
            SpritePlayback? old = viewmodel.Playback;
            viewmodel.Playback = staged;
            // Imported cells retain classic placement; Engine fits the complete
            // canvas to the viewport and advances the selected sequence.
            viewmodel.Strike = name != "idle";
            viewmodel.HitCrossed = false;
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

    private ulong NextVisualEntityId() => DaggerfallPresentationObjectIds.Take(ref nextVisualEntityId,
        DaggerfallPresentationObjectIds.TransientVisualFloor, "transient visual");

    private ulong NextLocationVisualId() => DaggerfallPresentationObjectIds.Take(ref nextLocationVisualId,
        DaggerfallPresentationObjectIds.LocationVisualFloor, "location visual");

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

    private void EmitPlayerVampireVoice(PresentationEventIdentity identity)
    {
        // Optional combat voice belongs to the melee attack frame, including an empty or missed
        // swing. The outer voice chance is separate from the vampire's bark/attack selection.
        if (playerVampireFemale() is not bool female
            || DrawCue(identity, "vampire-voice", 1, 100) > audioTuning.VampireAttackChancePercent) return;
        bool bark = DrawCue(identity, "vampire-bark", 1, 100) <= audioTuning.VampireBarkChancePercent;
        Emit(female ? bark ? "sound.199" : "sound.200" : bark ? "sound.205" : "sound.206", identity, 0);
    }

    private int DrawCue(PresentationEventIdentity identity, string purpose, int minimum, int maximum) =>
        random is null ? minimum : checked((int)random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed,
            $"daggerfall.media.{purpose}.v1", CombatRandomKey.For(identity.Generation, identity.SimulationStep,
                identity.Attacker, identity.Target, CombatRandomKey.MediaHitCueSalt), minimum, maximum)).Value);

    private string SelectHitCue(PresentationEventIdentity identity, bool weapon)
    {
        IReadOnlyList<string> family = weapon ? hitCues : hitCues.Where(cue => cue is "hit3" or "hit4").ToArray();
        return family.Count == 0 ? string.Empty : family[DrawCue(identity, "hit-cue", 1, family.Count) - 1];
    }

    private void EmitMiss(DaggerfallStrikeFeedback feedback, PresentationEventIdentity identity, ActorsState? state)
    {
        bool parry = feedback.Weapon && (identity.Attacker == DaggerfallActorIdentity.PlayerEntityId || feedback.SwingCue == "sound.3")
            && actors.TryGetValue(identity.Target, out ActorVisual? target)
            && target.Sprite.Feedback is { ParrySounds: true };
        string cue = parry ? $"sound.{428 + DrawCue(identity, "parry", 0, 8)}" : feedback.SwingCue;
        EmitAtActor(cue, identity, parry ? identity.Target : identity.Attacker, state,
            pitch: parry ? audioTuning.ContactPitch : null);
    }

    private bool CanVoice(DaggerfallActorFeedback? feedback) => feedback is not null
        && (!audioTuning.MuteHumanSounds || feedback.MobileId < 128 || feedback.MobileId == 146);

    /// <summary>Source attract policy advances only by Engine-admitted time; Engine owns spatial audio.</summary>
    internal void AdvanceMobileFeedback(ProductUpdateFacts update, ActorsState state, WorldPoint? player,
        Func<WorldPoint, WorldPoint, bool> blockedByCover)
    {
        if (disposed || update.AdmittedStepCount == 0) return;
        AppearanceOuterUpdate outer = new(update.Generation, update.ControlRevision, update.SimulationStep, update.AdmittedStepCount);
        if (playerBeastForm() is { } beast)
        {
            var identity = Event(DaggerfallActorIdentity.PlayerEntityId, DaggerfallActorIdentity.PlayerEntityId, update.Generation, update.SimulationStep, "beast-move");
            playerBeastVoiceSeconds ??= DrawCue(identity, "beast-delay", audioTuning.BeastMinimumDelaySeconds, audioTuning.BeastMaximumDelaySeconds);
            playerBeastVoiceSeconds -= update.FixedDeltaSeconds * update.AdmittedStepCount;
            if (playerBeastVoiceSeconds <= 0)
            {
                Emit(beast == DaggerfallRacialKind.Werewolf ? "sound.142" : "sound.157", identity, 0);
                playerBeastVoiceSeconds = null;
            }
        }
        else playerBeastVoiceSeconds = null;
        foreach (ActorState actor in state.All)
        {
            if (!actors.TryGetValue(actor.DurableId, out ActorVisual? visual) || actor.IsDefeated
                || !CanVoice(visual.Sprite.Feedback) || visual.LastFeedbackUpdate == outer) continue;
            visual.LastFeedbackUpdate = outer;
            PresentationEventIdentity identity = Event(actor.DurableId, DaggerfallActorIdentity.PlayerEntityId, update.Generation, update.SimulationStep, "attract");
            visual.AttractRemainingSeconds ??= DrawCue(identity, "attract-delay", audioTuning.AttractMinimumDelaySeconds, audioTuning.AttractMaximumDelaySeconds);
            visual.AttractRemainingSeconds -= update.FixedDeltaSeconds * update.AdmittedStepCount;
            if (visual.AttractRemainingSeconds >= 0 || player is not WorldPoint viewpoint
                || Vector3.Distance(actor.Position.ToVector(), viewpoint.ToVector()) >= audioTuning.AttractRadius) continue;
            DaggerfallActorFeedback feedback = visual.Sprite.Feedback!;
            string cue = DrawCue(identity, "attract-choice", 1, 100) <= audioTuning.AttractMoveChancePercent ? feedback.MoveCue : feedback.BarkCue;
            Emit(cue, identity, 0, actor.Position, blockedByCover(actor.Position, viewpoint) ? audioTuning.OccludedVolumeScale : 1F, enemyActor: actor.DurableId);
            visual.AttractRemainingSeconds = DrawCue(identity, "attract-delay", audioTuning.AttractMinimumDelaySeconds, audioTuning.AttractMaximumDelaySeconds);
        }
    }

    private void EmitAtActor(string cue, PresentationEventIdentity identity, long actorId, ActorsState? state, float? pitch = null)
        => Emit(cue, identity, 0, state is not null && state.TryGet(actorId, out ActorState actor) ? actor.Position : null, pitch: pitch, enemyActor: actorId == DaggerfallActorIdentity.PlayerEntityId ? null : actorId);

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
        visual.ActiveAttack = null;
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

    private void Emit(string clipId, PresentationEventIdentity identity, ulong marker, WorldPoint? position = null, float volumeScale = 1F, float? pitch = null, long? enemyActor = null)
    {
        if (audio is null || string.IsNullOrEmpty(clipId)) return;
        if (!audioClips.TryGetValue(clipId, out AudioClip? clip))
        {
            if (audioBundle is null) return;
            clip = audioBundle.OpenClip(audio, clipId);
            audioClips.Add(clipId, clip);
        }
        string signalId = $"daggerfall.media.{identity.Generation}.{identity.SimulationStep}.{identity.Attacker}.{identity.Target}.{identity.Outcome}.{marker}.{clipId}";
        var descriptor = new AudioSourceDescriptor(clip, AudioBus.Sfx, audioTuning.Volume * volumeScale,
            pitch ?? audioTuning.Pitch, false, position is null ? audioTuning.SpatialBlend : 1F,
            enemyActor is not null && position is not null ? enemyAudibleRange() : audioTuning.MaxDistance, AudioRolloff.Linear, 0F,
            position is null ? AudioEmitterKind.Global2d : AudioEmitterKind.World3d, position?.ToVector() ?? Vector3.Zero, 0, Vector3.Zero);
        if (enemyActor is long actorId && position is not null)
        {
            var voice = audio.CreateVoice(descriptor);
            enemyVoices.Add(voice.Handle, new(voice, descriptor, actorId));
        }
        else oneShotSignals.Add(audio.Emit(new AudioEmitRequest(signalId, descriptor)));
    }

    private void RetireRealizedOneShots()
    {
        if (oneShotSignals.Count == 0 && enemyVoices.Count == 0) return;
        AudioRealizationResult realization = audio!.ReadRealization();
        foreach (AudioRealizationFact fact in realization.Facts.Span)
        {
            if (fact.SignalHandle != 0
                && (fact.Kind is AudioRealizationFactKind.NaturalCompletionOneShot or AudioRealizationFactKind.Diagnostic))
                oneShotSignals.Remove(new AudioSignalHandle(fact.SignalHandle));
            if (fact.VoiceValue != 0 && (fact.Kind is AudioRealizationFactKind.NaturalCompletionRetainedVoice or AudioRealizationFactKind.Diagnostic)
                && enemyVoices.Remove(new(fact.VoiceValue), out var voice)) voice.Voice.Dispose();
        }
    }

    private sealed class RetiredLocationResources(IDisposable[] values) : IDisposable
    {
        public void Dispose()
        {
            List<Exception>? failures = null;
            foreach (IDisposable value in values) DaggerfallSiteAppearance.Dispose(value, ref failures);
            if (failures is { Count: > 0 }) throw new AggregateException(failures);
        }
    }

    private sealed class RetiredActorVisual(DaggerfallSiteAppearance owner, ActorVisual visual) : IDisposable
    {
        public void Dispose()
        {
            List<Exception>? failures = null;
            owner.DisposeActorVisual(visual, ref failures);
            if (failures is { Count: > 0 }) throw new AggregateException(failures);
        }
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
        internal AppearanceOuterUpdate? LastFeedbackUpdate { get; set; }
        internal double? AttractRemainingSeconds { get; set; }
        internal void Dispose(ref List<Exception>? failures)
        {
            if (Playback is { } playback) DaggerfallSiteAppearance.Dispose(playback, ref failures);
            Playback = null;
            if (Live is { } live) DaggerfallSiteAppearance.Dispose(live, ref failures);
            Live = null;
            if (Corpse is { } corpse) DaggerfallSiteAppearance.Dispose(corpse, ref failures);
        }
    }

    internal sealed class BillboardVisual(long entityId, Appearance appearance) : IDisposable
    {
        internal long EntityId { get; } = entityId;
        internal Appearance Appearance { get; } = appearance;
        public void Dispose() => Appearance.Dispose();
        internal void Dispose(ref List<Exception>? failures) => DaggerfallSiteAppearance.Dispose(Appearance, ref failures);
    }

    internal sealed class EffectVisual(ulong entityId, WorldPoint position, SpriteAtlas atlas, Appearance appearance, SpritePlayback playback) : IDisposable
    {
        internal ulong EntityId { get; } = entityId;
        internal string? ActiveInstance { get; init; }
        internal long? TargetActor { get; init; }
        internal long? SourceActor { get; init; }
        internal ulong? SourceItem { get; init; }
        internal WorldPoint Position { get; set; } = position;
        internal SpriteAtlas Atlas { get; } = atlas;
        internal Appearance Appearance { get; } = appearance;
        internal SpritePlayback Playback { get; } = playback;
        internal bool CompletedOuterUpdate { get; set; }
        internal AppearanceOuterUpdate? LastOuterUpdate { get; set; }
        internal void Dispose(ref List<Exception>? failures)
        {
            DaggerfallSiteAppearance.Dispose(Playback, ref failures);
            DaggerfallSiteAppearance.Dispose(Appearance, ref failures);
            DaggerfallSiteAppearance.Dispose(Atlas, ref failures);
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
        internal PresentationEventIdentity? PendingAttackVoice { get; set; }
        /// <summary>The frame of this swing's own animation that releases its impact.</summary>
        internal int HitFrame { get; set; } = DaggerfallFormulaPolicy.MeleeWeaponHitFrame;
        /// <summary>Whether the Engine has reported this swing's hit-frame marker crossing.</summary>
        internal bool HitCrossed { get; set; }
        internal bool ImpactReported { get; set; }
        internal bool CompletedOuterUpdate { get; set; }
        internal AppearanceOuterUpdate? LastOuterUpdate { get; set; }
        internal void Dispose(ref List<Exception>? failures)
        {
            if (Playback is { } playback) DaggerfallSiteAppearance.Dispose(playback, ref failures);
            DaggerfallSiteAppearance.Dispose(Appearance, ref failures);
            DaggerfallSiteAppearance.Dispose(Atlas, ref failures);
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
    internal readonly record struct ActiveAttackPresentation(PresentationEventIdentity Identity, ulong? DamageMarkerId = null, bool ImpactReported = false);
}
