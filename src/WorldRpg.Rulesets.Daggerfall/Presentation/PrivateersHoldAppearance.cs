using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Publishes the normalized Privateer's Hold visual closure through Engine-owned resources and sprite atlases.</summary>
internal sealed class PrivateersHoldAppearance : IDisposable
{
    private readonly IAppearanceService appearance;
    private readonly IAudioService? audio;
    private readonly IRandomService? random;
    private readonly DaggerfallPresentationAudioTuning audioTuning;
    private readonly Dictionary<string, AudioClipHandle> audioClips = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<string> hitCues;
    private readonly Dictionary<long, ActorVisual> actors = [];
    private readonly HashSet<PresentationEventIdentity> deliveredEvents = [];
    private readonly List<SpriteAtlas> atlases = [];
    private readonly List<Material> materials = [];
    private readonly List<IDisposable> priorRetired = [];
    private readonly List<IDisposable> nextRetired = [];
    private Appearance? world;
    private readonly AuthoredWorldAppearance worldAppearance;
    private bool disposed;

    internal PrivateersHoldAppearance(IContentService content, IAppearanceService appearance, PrivateersHoldInputs inputs, IAudioService? audio = null, DaggerfallPresentationAudioTuning? audioTuning = null, IRandomService? random = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(inputs);
        this.appearance = appearance;
        this.audio = audio;
        this.random = random;
        this.audioTuning = (audioTuning ?? DaggerfallTuning.Defaults.PresentationAudio).Validate();
        hitCues = inputs.Audio.Count == 0 ? [] : PrivateersHoldContent.OrderedHitCues(inputs.Audio);
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
            foreach (NormalizedAudioClip clip in inputs.Audio)
            {
                VerifyContent(content, new ContentArtifact(clip.Path, clip.Sha256));
                audioClips.Add(clip.Id, audio?.OpenClip(new AudioClipRequest(clip.Path)) ?? default);
            }
        }
        catch { Dispose(); throw; }
    }

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
        appearance.PublishSnapshot([.. facts]);
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
    internal PresentationCheckpoint Checkpoint() => new(actors.ToDictionary(pair => pair.Key, pair => ActorSnapshot.From(pair.Value)), deliveredEvents.ToHashSet(), priorRetired.ToArray(), nextRetired.ToArray());
    internal void Restore(PresentationCheckpoint checkpoint)
    {
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
        priorRetired.Clear(); priorRetired.AddRange(checkpoint.PriorRetired);
        nextRetired.Clear(); nextRetired.AddRange(checkpoint.NextRetired);
    }

    /// <summary>Interprets Daggerfall combat facts while Engine owns playback timing and frame staging.</summary>
    internal void React(IProductFact fact)
    {
        if (disposed) return;
        switch (fact)
        {
            case AttackHitFact hit:
                PresentationEventIdentity hitEvent = Event(hit.AttackerId, hit.TargetId, hit.OriginatingGeneration, hit.OriginatingSimulationStep, "hit");
                if (deliveredEvents.Contains(hitEvent)) break;
                StartAttack(hit.AttackerId, hit.TargetId, hit.OriginatingGeneration, hit.OriginatingSimulationStep, hitEvent);
                StartState(hit.TargetId, "hurt", null);
                if (hit.AttackerId == DaggerfallActorIdentity.PlayerEntityId) Emit("swing", hitEvent, 0);
                deliveredEvents.Add(hitEvent);
                break;
            case AttackMissedFact miss:
                PresentationEventIdentity missEvent = Event(miss.AttackerId, miss.TargetId, miss.OriginatingGeneration, miss.OriginatingSimulationStep, "miss");
                if (deliveredEvents.Contains(missEvent)) break;
                StartAttack(miss.AttackerId, miss.TargetId, miss.OriginatingGeneration, miss.OriginatingSimulationStep, missEvent);
                if (miss.AttackerId == DaggerfallActorIdentity.PlayerEntityId) Emit("swing", missEvent, 0);
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
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? failures = null;
        try { appearance.PublishSnapshot(ReadOnlySpan<AppearanceFact>.Empty); }
        catch (Exception exception) { failures = [exception]; }
        foreach (ActorVisual visual in actors.Values.Reverse()) visual.Dispose(ref failures);
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
        SpriteAtlasFrame[] frames = sprite.Frames.Select(frame => new SpriteAtlasFrame(frame.Id, new Vector2((float)frame.X / sprite.AtlasWidth, (float)frame.Y / sprite.AtlasHeight), new Vector2((float)(frame.X + frame.Width) / sprite.AtlasWidth, (float)(frame.Y + frame.Height) / sprite.AtlasHeight), true, new Vector2(frame.Width, frame.Height))).ToArray();
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

    internal readonly record struct PresentationEventIdentity(ulong Generation, ulong SimulationStep, long Attacker, long Target, string Outcome);
    internal readonly record struct AppearanceOuterUpdate(ulong Generation, ulong ControlRevision, ulong SimulationStep, uint AdmittedStepCount);
    internal sealed record PresentationCheckpoint(IReadOnlyDictionary<long, ActorSnapshot> Actors, IReadOnlySet<PresentationEventIdentity> Events, IReadOnlyList<IDisposable> PriorRetired, IReadOnlyList<IDisposable> NextRetired);
    internal sealed record ActorSnapshot(Appearance? Live, SpritePlayback? Playback, string State, bool Defeated, bool Completed, ulong Marker, uint PlaybackFrame, NormalizedSpriteState? ActiveState, IReadOnlyList<int> SourceFrames, int Orientation, ActiveAttackPresentation? ActiveAttack, AppearanceOuterUpdate? LastOuterUpdate)
    {
        internal static ActorSnapshot From(ActorVisual visual) => new(visual.Live, visual.Playback, visual.State, visual.Defeated, visual.CompletedOuterUpdate, visual.LastMarkerCrossing, visual.LastPlaybackFrameIndex, visual.ActiveState, visual.SourceFrameIndices, visual.Orientation, visual.ActiveAttack, visual.LastOuterUpdate);
        internal void Apply(ActorVisual visual) { visual.Live = Live; visual.Playback = Playback; visual.State = State; visual.Defeated = Defeated; visual.CompletedOuterUpdate = Completed; visual.LastMarkerCrossing = Marker; visual.LastPlaybackFrameIndex = PlaybackFrame; visual.ActiveState = ActiveState; visual.SourceFrameIndices = SourceFrames; visual.Orientation = Orientation; visual.ActiveAttack = ActiveAttack; visual.LastOuterUpdate = LastOuterUpdate; }
    }
    internal readonly record struct ActiveAttackPresentation(PresentationEventIdentity Identity, string HitCue);
}
