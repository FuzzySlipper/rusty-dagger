using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Publishes the normalized Privateer's Hold visual closure through Engine-owned resources and sprite atlases.</summary>
internal sealed class PrivateersHoldAppearance : IDisposable
{
    private readonly IAppearanceService appearance;
    private readonly IContentService content;
    private readonly IAudioService? audio;
    private readonly IRandomService? random;
    private readonly DaggerfallPresentationAudioTuning audioTuning;
    private readonly Dictionary<string, AudioClipHandle> audioClips = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<string> hitCues;
    private readonly IReadOnlyDictionary<string, NormalizedClassicEffect> classicEffects;
    private readonly NormalizedClassicPresentation classicPresentation;
    // Engine seals render-resource selection once Product.Create finishes.
    // Classic effects and the optional weapon remain lazy visual instances, but
    // their normalized texture handles must be admitted with the initial closure.
    private readonly Dictionary<string, RenderResourceInfo> classicTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<long, ActorVisual> actors = [];
    private readonly List<EffectVisual> effects = [];
    private ViewmodelVisual? viewmodel;
    private readonly HashSet<PresentationEventIdentity> deliveredEvents = [];
    private readonly List<SpriteAtlas> atlases = [];
    private readonly List<Material> materials = [];
    private readonly List<IDisposable> priorRetired = [];
    private readonly List<IDisposable> nextRetired = [];
    private AppearanceFact[] lastPublishedSnapshot = [];
    private Appearance? world;
    private readonly AuthoredWorldAppearance worldAppearance;
    private bool disposed;

    internal PrivateersHoldAppearance(IContentService content, IAppearanceService appearance, PrivateersHoldInputs inputs, IAudioService? audio = null, DaggerfallPresentationAudioTuning? audioTuning = null, IRandomService? random = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(inputs);
        this.appearance = appearance;
        this.content = content;
        this.audio = audio;
        this.random = random;
        this.audioTuning = (audioTuning ?? DaggerfallTuning.Defaults.PresentationAudio).Validate();
        hitCues = inputs.Audio.Count == 0 ? [] : PrivateersHoldContent.OrderedHitCues(inputs.Audio);
        classicPresentation = inputs.ClassicPresentation;
        classicEffects = inputs.ClassicPresentation.Effects.ToDictionary(effect => effect.Name, StringComparer.Ordinal);
        worldAppearance = inputs.WorldAppearance;
        try
        {
            VerifyContent(content, inputs.StaticMesh);
            world = appearance.CreateStaticMeshFromContent(new StaticMeshContentAppearanceRequest(inputs.StaticMesh.Path, worldAppearance.Tint));
            foreach (NormalizedMaterial material in inputs.Materials)
            {
                VerifyContent(content, new ContentArtifact(material.TexturePath, material.TextureSha256));
                RenderResourceInfo texture = appearance.OpenResource(new RenderResourceRequest(material.TexturePath));
                materials.Add(appearance.CreateMaterial(new MaterialRequest(new Color(1F, 1F, 1F, 1F), texture.Handle, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false)));
            }
            appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(world, inputs.Materials.Select((material, index) => new MeshMaterialBinding(material.Slot, materials[index])).ToArray()));
            foreach ((long entityId, NormalizedActorSprite sprite) in inputs.ActorSprites.OrderBy(pair => pair.Key))
            {
                ActorVisual visual = CreateActorVisual(content, entityId, sprite);
                actors.Add(entityId, visual);
            }
            AdmitClassicTextures();
            foreach (NormalizedAudioClip clip in inputs.Audio)
            {
                VerifyContent(content, new ContentArtifact(clip.Path, clip.Sha256));
                audioClips.Add(clip.Id, audio?.OpenClip(new AudioClipRequest(clip.Path)) ?? default);
            }
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Publishes authored local viewmodel placement; Engine owns camera-relative rebasing and orientation.</summary>
    internal void Publish(ActorsState actors)
    {
        if (disposed) return;
        List<AppearanceFact> facts = [];
        if (world is { } staticWorld) facts.Add(new AppearanceFact(1, worldAppearance.Transform, staticWorld, worldAppearance.Visible, worldAppearance.Layer));
        foreach (ActorState actor in actors.All.Values)
        {
            if (!this.actors.TryGetValue(actor.EntityId, out ActorVisual? visual)) continue;
            Appearance? chosen = actor.IsDefeated ? visual.Corpse : visual.Live;
            if (chosen is not null) facts.Add(new AppearanceFact(checked((ulong)actor.EntityId), new Transform(actor.Position.ToVector(), Quaternion.Identity, Vector3.One), chosen, true, RenderLayer.Scene));
        }
        foreach (EffectVisual effect in effects)
            facts.Add(new AppearanceFact(effect.EntityId, new Transform(effect.Position.ToVector(), Quaternion.Identity, Vector3.One), effect.Appearance, true, RenderLayer.Scene));
        if (viewmodel is { } weapon)
        {
            facts.Add(new AppearanceFact(weapon.EntityId, weapon.Transform, weapon.Appearance, true, RenderLayer.Viewmodel));
        }
        AppearanceFact[] snapshot = [.. facts];
        // Record before publication: an Engine callback may stage this exact
        // snapshot and then report a later failure in the same admitted update.
        lastPublishedSnapshot = snapshot;
        appearance.PublishSnapshot(snapshot);
    }

    /// <summary>
    /// Maps the Engine playback's current normalized source-frame index to the
    /// camera-relative classic directional sector.  Direction is a frame
    /// selection only; it never recreates or restarts Engine playback.
    /// </summary>
    internal void UpdateDirections(ActorsState actorState, WorldPoint viewpoint)
    {
        if (disposed) return;
        foreach (ActorState actor in actorState.All.Values)
        {
            if (!actors.TryGetValue(actor.EntityId, out ActorVisual? visual)
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
        foreach (IDisposable value in priorRetired) value.Dispose();
    }

    internal void CompleteAdmittedUpdate()
    {
        priorRetired.Clear();
        priorRetired.AddRange(nextRetired);
        nextRetired.Clear();
    }
    internal PresentationCheckpoint Checkpoint() => new(actors.ToDictionary(pair => pair.Key, pair => ActorSnapshot.From(pair.Value)), ViewmodelSnapshot.From(viewmodel), effects.Select(EffectSnapshot.From).ToArray(), deliveredEvents.ToHashSet(), priorRetired.ToArray(), nextRetired.ToArray(), lastPublishedSnapshot.ToArray());
    internal void Restore(PresentationCheckpoint checkpoint)
    {
        // A failing update may already have staged a snapshot containing
        // discarded effect/viewmodel appearances. Restore the checkpoint's
        // Engine-visible snapshot before releasing any discarded wrappers.
        // That ordering keeps Engine ownership and managed disposal coherent.
        appearance.PublishSnapshot(checkpoint.PublishedSnapshot.ToArray());
        lastPublishedSnapshot = checkpoint.PublishedSnapshot.ToArray();
        DisposeRetiredIntermediates(checkpoint);
        deliveredEvents.Clear();
        deliveredEvents.UnionWith(checkpoint.Events);
        foreach ((long id, ActorSnapshot snapshot) in checkpoint.Actors)
        {
            if (!actors.TryGetValue(id, out ActorVisual? visual)) continue;
            // Only products created in this discarded Engine call are released;
            // checkpoint references remain reachable committed wrappers.
            if (visual.Playback is { } currentPlayback && !ReferenceEquals(currentPlayback, snapshot.Playback)) currentPlayback.Dispose();
            if (visual.Live is { } currentLive && !ReferenceEquals(currentLive, snapshot.Live)) currentLive.Dispose();
            snapshot.Apply(visual);
        }
        if (viewmodel is { } currentWeapon && !ReferenceEquals(currentWeapon, checkpoint.Viewmodel?.Visual)) currentWeapon.Dispose();
        else if (viewmodel is { } retainedWeapon && checkpoint.Viewmodel is { } weaponSnapshot && !ReferenceEquals(retainedWeapon.Playback, weaponSnapshot.Playback)) retainedWeapon.Playback?.Dispose();
        viewmodel = checkpoint.Viewmodel?.Visual;
        checkpoint.Viewmodel?.Apply(viewmodel!);
        foreach (EffectVisual effect in effects.Where(current => checkpoint.Effects.All(snapshot => !ReferenceEquals(snapshot.Visual, current))).ToArray()) effect.Dispose();
        effects.Clear();
        foreach (EffectSnapshot snapshot in checkpoint.Effects) { snapshot.Apply(); effects.Add(snapshot.Visual); }
        priorRetired.Clear(); priorRetired.AddRange(checkpoint.PriorRetired);
        nextRetired.Clear(); nextRetired.AddRange(checkpoint.NextRetired);
    }

    /// <summary>Interprets Daggerfall combat facts while Engine owns playback timing and frame staging.</summary>
    internal void React(IProductFact fact) => React(fact, null);

    /// <summary>Interprets facts with the current authoritative actor state; presentation keeps no position mirror.</summary>
    internal void React(IProductFact fact, ActorsState? actorState)
    {
        if (disposed) return;
        switch (fact)
        {
            case AttackHitFact hit:
                PresentationEventIdentity hitEvent = Event(hit.AttackerId, hit.TargetId, hit.OriginatingGeneration, hit.OriginatingSimulationStep, "hit");
                if (deliveredEvents.Contains(hitEvent)) break;
                StartAttack(hit.AttackerId, hit.TargetId, hit.OriginatingGeneration, hit.OriginatingSimulationStep, hitEvent);
                StartState(hit.TargetId, "hurt", null);
                if (hit.AttackerId == DaggerfallActorIdentity.PlayerEntityId && actorState is not null) SpawnBlood(hit, hitEvent, actorState);
                if (hit.AttackerId == DaggerfallActorIdentity.PlayerEntityId) StartWeaponStrike(hitEvent);
                if (hit.AttackerId == DaggerfallActorIdentity.PlayerEntityId) Emit("swing", hitEvent, 0);
                deliveredEvents.Add(hitEvent);
                break;
            case AttackMissedFact miss:
                PresentationEventIdentity missEvent = Event(miss.AttackerId, miss.TargetId, miss.OriginatingGeneration, miss.OriginatingSimulationStep, "miss");
                if (deliveredEvents.Contains(missEvent)) break;
                StartAttack(miss.AttackerId, miss.TargetId, miss.OriginatingGeneration, miss.OriginatingSimulationStep, missEvent);
                if (miss.AttackerId == DaggerfallActorIdentity.PlayerEntityId) Emit("swing", missEvent, 0);
                if (miss.AttackerId == DaggerfallActorIdentity.PlayerEntityId) StartWeaponStrike(missEvent);
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

    /// <summary>Reads the Engine-backed equipment projection; only an authored exact mapping may reveal the classic art.</summary>
    internal void UpdateRightHandEquipment(EquipmentRead equipment)
    {
        ArgumentNullException.ThrowIfNull(equipment);
        string? itemId = equipment.TryGet(new EquipmentSlotId("right-hand"), out UniqueInventoryItem item) ? item.Definition.Value : null;
        bool compatible = itemId is not null
            && classicPresentation.Weapon is { } weapon
            && classicPresentation.CompatibleItemVisuals.TryGetValue(itemId, out string? resource)
            && resource == weapon.ResourceId
            && classicPresentation.Viewmodel is not null;
        if (compatible && viewmodel is null) CreateViewmodel();
        else if (!compatible && viewmodel is not null) RetireViewmodel();
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
                    if (visual.ActiveAttack is { Identity.Outcome: "hit" } attack) Emit(attack.HitCue, attack.Identity, crossing.CrossingSequence);
                }
            }
            if (!receipt.Readout.Completed || visual.State is "idle" or "move") { visual.LastOuterUpdate = identity; continue; }
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
            if (weapon.Strike && receipt.Readout.Completed)
            {
                if (weapon.CompletedOuterUpdate) StartWeaponAction("idle");
                else if (receipt.Advanced) weapon.CompletedOuterUpdate = true;
            }
            weapon.LastOuterUpdate = identity;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? failures = null;
        try { appearance.PublishSnapshot(ReadOnlySpan<AppearanceFact>.Empty); }
        catch (Exception exception) { failures = [exception]; }
        foreach (ActorVisual visual in actors.Values.Reverse()) visual.Dispose(ref failures);
        foreach (EffectVisual effect in effects.AsEnumerable().Reverse()) effect.Dispose(ref failures);
        effects.Clear();
        if (viewmodel is { } weapon) { viewmodel = null; weapon.Dispose(ref failures); }
        foreach (IDisposable value in nextRetired.AsEnumerable().Reverse()) Dispose(value, ref failures);
        foreach (IDisposable value in priorRetired.AsEnumerable().Reverse()) Dispose(value, ref failures);
        nextRetired.Clear(); priorRetired.Clear();
        actors.Clear();
        if (world is { } staticWorld) { world = null; Dispose(staticWorld, ref failures); }
        foreach (SpriteAtlas atlas in atlases.AsEnumerable().Reverse()) Dispose(atlas, ref failures);
        atlases.Clear();
        foreach (Material material in materials.AsEnumerable().Reverse()) Dispose(material, ref failures);
        materials.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private static void VerifyContent(IContentService content, ContentArtifact artifact)
    {
        using ContentReference reference = content.ResolveReference(new ContentResolveRequest(artifact.Path, artifact.Sha256));
        ReadOnlyMemory<ContentReferenceInfo> info = content.ReadReferenceInfo(reference);
        if (info.Length != 1 || info.Span[0].Path != artifact.Path || info.Span[0].Sha256 != artifact.Sha256) throw new InvalidOperationException($"Engine content did not preserve identity for '{artifact.Path}'.");
    }

    private void AdmitClassicTextures()
    {
        foreach (NormalizedClassicEffect effect in classicEffects.Values.OrderBy(effect => effect.Name, StringComparer.Ordinal))
            AdmitClassicTexture(new ContentArtifact(effect.TexturePath, effect.TextureSha256));
        if (classicPresentation.Weapon is { } weapon)
            AdmitClassicTexture(new ContentArtifact(weapon.TexturePath, weapon.TextureSha256));
    }

    private void AdmitClassicTexture(ContentArtifact artifact)
    {
        VerifyContent(content, artifact);
        RenderResourceInfo texture = appearance.OpenResource(new RenderResourceRequest(artifact.Path));
        if (!classicTextures.TryAdd(artifact.Path, texture))
            throw new InvalidOperationException($"Classic presentation repeats normalized texture path '{artifact.Path}'.");
    }

    private void DisposeRetiredIntermediates(PresentationCheckpoint checkpoint)
    {
        HashSet<IDisposable> checkpointOwned = new(ReferenceEqualityComparer.Instance);
        foreach (ActorSnapshot actor in checkpoint.Actors.Values)
        {
            if (actor.Live is not null) checkpointOwned.Add(actor.Live);
            if (actor.Playback is not null) checkpointOwned.Add(actor.Playback);
        }
        if (checkpoint.Viewmodel is { } weapon)
        {
            checkpointOwned.Add(weapon.Visual);
            if (weapon.Playback is not null) checkpointOwned.Add(weapon.Playback);
        }
        foreach (EffectSnapshot effect in checkpoint.Effects) checkpointOwned.Add(effect.Visual);
        foreach (IDisposable value in checkpoint.PriorRetired) checkpointOwned.Add(value);
        foreach (IDisposable value in checkpoint.NextRetired) checkpointOwned.Add(value);

        HashSet<IDisposable> released = new(ReferenceEqualityComparer.Instance);
        foreach (IDisposable value in priorRetired.Concat(nextRetired))
        {
            if (!checkpointOwned.Contains(value) && released.Add(value)) value.Dispose();
        }
    }

    private static void Dispose(IDisposable value, ref List<Exception>? failures)
    {
        try { value.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
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
        VerifyContent(content, new ContentArtifact(sprite.TexturePath, sprite.TextureSha256));
        RenderResourceInfo texture = appearance.OpenResource(new RenderResourceRequest(sprite.TexturePath));
        SpriteAtlasFrame[] frames = sprite.Frames.Select(frame => new SpriteAtlasFrame(frame.Id, new Vector2((float)frame.X / sprite.AtlasWidth, (float)frame.Y / sprite.AtlasHeight), new Vector2((float)(frame.X + frame.Width) / sprite.AtlasWidth, (float)(frame.Y + frame.Height) / sprite.AtlasHeight), false, Vector2.Zero)).ToArray();
        SpriteAtlas atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, frames));
        atlases.Add(atlas);
        Appearance value = appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(atlas, sprite.InitialFrameId, sprite.Pivot, sprite.Size, BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F)));
        return (atlas, value);
    }

    private void StartAttack(long entityId, long targetId, ulong generation, ulong simulationStep, PresentationEventIdentity presentationEvent)
    {
        if (!actors.TryGetValue(entityId, out ActorVisual? visual) || visual.Live is null || visual.Sprite.AttackSequences.Count == 0) return;
        NormalizedAttackSequence selected = SelectAttack(visual.Sprite.AttackSequences, generation, simulationStep, entityId, targetId);
        StartState(entityId, "primaryAttack", visual, selected);
        string hitCue = SelectHitCue(presentationEvent);
        visual.ActiveAttack = new ActiveAttackPresentation(presentationEvent, hitCue);
        if (presentationEvent.Outcome == "hit" && !selected.SourceFrames.Contains(-1)) Emit(hitCue, presentationEvent, 0);
    }

    private void SpawnBlood(AttackHitFact hit, PresentationEventIdentity identity, ActorsState actors)
    {
        if (!actors.All.TryGetValue(hit.TargetId, out ActorState? target)) return;
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
            VerifyContent(content, new ContentArtifact(effect.TexturePath, effect.TextureSha256));
            RenderResourceInfo texture = classicTextures[effect.TexturePath];
            SpriteAtlasFrame[] frames = effect.Frames.Select(frame => new SpriteAtlasFrame(frame.Id,
                new Vector2((float)frame.X / effect.AtlasWidth, (float)frame.Y / effect.AtlasHeight),
                new Vector2((float)(frame.X + frame.Width) / effect.AtlasWidth, (float)(frame.Y + frame.Height) / effect.AtlasHeight),
                false, Vector2.Zero)).ToArray();
            atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, frames));
            uint initialFrame = effect.Sequence.Select(index => effect.Frames.Single(frame => frame.Id == index).Id).First();
            visual = appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(atlas, initialFrame, effect.Pivot, effect.DisplaySize, BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F)));
            SpritePlaybackFrame[] playbackFrames = effect.Sequence.Select(index => new SpritePlaybackFrame(effect.Frames.Single(frame => frame.Id == index).Id, 1d / effect.FramesPerSecond)).ToArray();
            playback = appearance.CreateSpritePlayback(new SpritePlaybackCreateRequest(visual, atlas, playbackFrames, Array.Empty<SpritePlaybackMarker>(), effect.Loops ? SpritePlaybackLoopMode.Loop : SpritePlaybackLoopMode.OneShot, 1d));
            appearance.ControlSpritePlayback(new SpritePlaybackControlRequest(playback, SpritePlaybackControl.Start));
            effects.Add(new EffectVisual(EffectEntityId(identity, name), position, atlas, visual, playback));
        }
        catch
        {
            playback?.Dispose();
            visual?.Dispose();
            atlas?.Dispose();
            throw;
        }
    }

    private void CreateViewmodel()
    {
        NormalizedClassicWeapon weapon = classicPresentation.Weapon ?? throw new InvalidOperationException("No admitted classic weapon sprite is available.");
        ClassicViewmodelStyle style = classicPresentation.Viewmodel ?? throw new InvalidOperationException("No authored classic viewmodel style is available.");
        SpriteAtlas? atlas = null;
        Appearance? visual = null;
        try
        {
            VerifyContent(content, new ContentArtifact(weapon.TexturePath, weapon.TextureSha256));
            RenderResourceInfo texture = classicTextures[weapon.TexturePath];
            SpriteAtlasFrame[] frames = weapon.Frames.Select(frame => new SpriteAtlasFrame(frame.Id,
                new Vector2((float)frame.X / weapon.AtlasWidth, (float)frame.Y / weapon.AtlasHeight),
                new Vector2((float)(frame.X + frame.Width) / weapon.AtlasWidth, (float)(frame.Y + frame.Height) / weapon.AtlasHeight),
                false, Vector2.Zero)).ToArray();
            atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, frames));
            visual = appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(atlas, weapon.Frames[0].Id, style.Pivot, style.Size, BillboardMode.None, SpriteSizeMode.World, style.RenderOrder, SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F)));
            viewmodel = new ViewmodelVisual(AtlasEntityId(weapon.ResourceId), new Transform(style.Position.ToVector(), Quaternion.Identity, Vector3.One), atlas, visual);
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

    private void StartWeaponStrike(PresentationEventIdentity identity)
    {
        if (viewmodel is null) return;
        string[] choices = ["strikeDown", "strikeDownLeft", "strikeLeft", "strikeRight", "strikeDownRight", "strikeUp"];
        int selected = random is null ? 0 : checked((int)random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, "daggerfall.media.weapon-strike.v1", CombatRandomKey.For(identity.Generation, identity.SimulationStep, identity.Attacker, identity.Target, 44), 0, choices.Length - 1)).Value);
        StartWeaponAction(choices[selected]);
    }

    private void StartWeaponAction(string name)
    {
        if (viewmodel is null || classicPresentation.Weapon is not { } weapon || !weapon.Actions.TryGetValue(name, out NormalizedClassicWeaponAction? action)) return;
        SpritePlayback? staged = null;
        try
        {
            SpritePlaybackFrame[] frames = Enumerable.Range(action.FrameStart, action.FrameCount)
                .Select(index => new SpritePlaybackFrame(weapon.Frames.Single(frame => frame.Id == index).Id, 1d / action.FramesPerSecond)).ToArray();
            staged = appearance.CreateSpritePlayback(new SpritePlaybackCreateRequest(viewmodel.Appearance, viewmodel.Atlas, frames, Array.Empty<SpritePlaybackMarker>(), action.Loops ? SpritePlaybackLoopMode.Loop : SpritePlaybackLoopMode.OneShot, 1d));
            appearance.ControlSpritePlayback(new SpritePlaybackControlRequest(staged, SpritePlaybackControl.Start));
            SpritePlayback? old = viewmodel.Playback;
            viewmodel.Playback = staged;
            // The importer already placed each fixed 320x200 weapon frame from
            // alignment and screenOffset. Engine owns camera-relative rebasing;
            // Daggerfall retains this authored local transform unchanged.
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
        viewmodel = null;
        Retire(weapon);
    }

    private static ulong AtlasEntityId(string resourceId)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char value in resourceId) hash = (hash ^ value) * 1099511628211UL;
        return hash == 0 ? 1UL : hash;
    }

    private static ulong EffectEntityId(PresentationEventIdentity identity, string name)
    {
        // Product entity ids are positive; this is presentation-only identity
        // derived solely from the already idempotent authoritative combat fact.
        ulong hash = 14695981039346656037UL;
        foreach (char value in $"{identity.Generation}:{identity.SimulationStep}:{identity.Attacker}:{identity.Target}:{name}") hash = (hash ^ value) * 1099511628211UL;
        return hash == 0 ? 1UL : hash;
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
        List<SpritePlaybackFrame> frames = [];
        List<SpritePlaybackMarker> markers = [];
        List<int> sourceFrameIndices = [];
        double duration = 1d / state.EffectiveFramesPerSecond;
        for (int index = 0; index < source.Count; index++)
        {
            int value = source[index];
            if (value == -1) { markers.Add(new SpritePlaybackMarker(checked((ulong)index + 1), checked((uint)frames.Count))); continue; }
            IReadOnlyList<uint> canonical = state.SelectOrientation(0);
            if (value < 0 || value >= canonical.Count) throw new InvalidOperationException("Normalized Daggerfall playback source index is outside the selected orientation.");
            frames.Add(new SpritePlaybackFrame(canonical[value], duration));
            sourceFrameIndices.Add(value);
        }
        // A marker needs a following visible frame.  Engine owns crossing
        // timing; Daggerfall merely preserves its semantic position.
        if (frames.Count == 0 || markers.Any(marker => marker.FrameIndex >= frames.Count))
            throw new InvalidOperationException("Normalized Daggerfall playback cannot end with a marker without a following visible frame.");
        SpritePlayback staged = appearance.CreateSpritePlayback(new SpritePlaybackCreateRequest(visual.Live, visual.Atlas, frames.ToArray(), markers.ToArray(), state.Loops ? SpritePlaybackLoopMode.Loop : SpritePlaybackLoopMode.OneShot, 1d));
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
        StartState(entityId, requested, visual);
    }

    private void TransitionToCorpse(long entityId)
    {
        if (!actors.TryGetValue(entityId, out ActorVisual? visual) || visual.Defeated) return;
        if (visual.Playback is { } playback) Retire(playback);
        if (visual.Live is { } live) Retire(live);
        visual.Playback = null;
        visual.Live = null;
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
        if (audio is null || !audioClips.TryGetValue(clipId, out AudioClipHandle clip)) return;
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

    internal sealed class ViewmodelVisual(ulong entityId, Transform transform, SpriteAtlas atlas, Appearance appearance) : IDisposable
    {
        internal ulong EntityId { get; } = entityId;
        internal Transform Transform { get; set; } = transform;
        internal SpriteAtlas Atlas { get; } = atlas;
        internal Appearance Appearance { get; } = appearance;
        internal SpritePlayback? Playback { get; set; }
        internal bool Strike { get; set; }
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
    internal sealed record PresentationCheckpoint(IReadOnlyDictionary<long, ActorSnapshot> Actors, ViewmodelSnapshot? Viewmodel, IReadOnlyList<EffectSnapshot> Effects, IReadOnlySet<PresentationEventIdentity> Events, IReadOnlyList<IDisposable> PriorRetired, IReadOnlyList<IDisposable> NextRetired, IReadOnlyList<AppearanceFact> PublishedSnapshot);
    internal sealed record ActorSnapshot(Appearance? Live, SpritePlayback? Playback, string State, bool Defeated, bool Completed, ulong Marker, uint PlaybackFrame, NormalizedSpriteState? ActiveState, IReadOnlyList<int> SourceFrames, int Orientation, ActiveAttackPresentation? ActiveAttack, AppearanceOuterUpdate? LastOuterUpdate)
    {
        internal static ActorSnapshot From(ActorVisual visual) => new(visual.Live, visual.Playback, visual.State, visual.Defeated, visual.CompletedOuterUpdate, visual.LastMarkerCrossing, visual.LastPlaybackFrameIndex, visual.ActiveState, visual.SourceFrameIndices, visual.Orientation, visual.ActiveAttack, visual.LastOuterUpdate);
        internal void Apply(ActorVisual visual) { visual.Live = Live; visual.Playback = Playback; visual.State = State; visual.Defeated = Defeated; visual.CompletedOuterUpdate = Completed; visual.LastMarkerCrossing = Marker; visual.LastPlaybackFrameIndex = PlaybackFrame; visual.ActiveState = ActiveState; visual.SourceFrameIndices = SourceFrames; visual.Orientation = Orientation; visual.ActiveAttack = ActiveAttack; visual.LastOuterUpdate = LastOuterUpdate; }
    }
    internal sealed record ViewmodelSnapshot(ViewmodelVisual Visual, SpritePlayback? Playback, Transform Transform, bool Strike, bool Completed, AppearanceOuterUpdate? LastOuterUpdate)
    {
        internal static ViewmodelSnapshot? From(ViewmodelVisual? visual) => visual is null ? null : new(visual, visual.Playback, visual.Transform, visual.Strike, visual.CompletedOuterUpdate, visual.LastOuterUpdate);
        internal void Apply(ViewmodelVisual visual) { visual.Playback = Playback; visual.Transform = Transform; visual.Strike = Strike; visual.CompletedOuterUpdate = Completed; visual.LastOuterUpdate = LastOuterUpdate; }
    }
    internal sealed record EffectSnapshot(EffectVisual Visual, bool Completed, AppearanceOuterUpdate? LastOuterUpdate)
    {
        internal static EffectSnapshot From(EffectVisual visual) => new(visual, visual.CompletedOuterUpdate, visual.LastOuterUpdate);
        internal void Apply() { Visual.CompletedOuterUpdate = Completed; Visual.LastOuterUpdate = LastOuterUpdate; }
    }
    internal readonly record struct ActiveAttackPresentation(PresentationEventIdentity Identity, string HitCue);
}
