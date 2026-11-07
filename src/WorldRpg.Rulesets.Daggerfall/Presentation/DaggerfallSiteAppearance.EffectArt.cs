using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// World effects drawn in refreshed art, and the spells in flight. A spell flies as its element's missile and lands
/// as its impact, both classic animated sprites. With refreshed art each effect draws its refreshed atlas at the
/// classic world size and anchor, the bright elements (fire, shock, magic) and the sparkle added to the scene rather
/// than laid over it, and a flying spell trails glowing particles that burst where it lands. A human enemy whose corpse
/// is the sprite every human class shares lies as the refreshed bone pile mesh instead, which is no one in particular,
/// and a bone pile placed in a dungeon draws as the same mesh. Classic art draws the classic sprites alone.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private readonly Dictionary<string, (RenderResource Texture, DaggerfallEffectArt Art)> refreshedEffects = new(StringComparer.Ordinal);
    private readonly Dictionary<long, (EffectVisual Visual, int Element, PresentationEmitter? Trail, ulong TrailId)> spellFlightVisuals = [];
    private Func<bool> drawRefreshedEffects = () => false;
    private readonly Dictionary<string, SpriteAtlas> personAtlases = new(StringComparer.Ordinal);
    private IPresentationService? particles;
    private RenderResource? particleGlow;
    private IAnimationService? meshes;
    private readonly Dictionary<(int Archive, int Record), (RenderResource Model, double Footprint)> worldMeshes = [];

    /// <summary>
    /// Admits the refreshed set's world effects and particle glow, drawn whenever <paramref name="refreshed"/> answers
    /// that the player has not chosen classic art.
    /// </summary>
    /// <exception cref="InvalidOperationException">A refreshed effect does not have its classic effect's frame count,
    /// so its classic sequence would play the wrong frames.</exception>
    internal void UseEffectArt(IPresentationService presentation, IAnimationService animation, DaggerfallArtReplacements art, Func<bool> refreshed)
    {
        particles = presentation;
        meshes = animation;
        drawRefreshedEffects = refreshed;
        foreach (NormalizedClassicEffect effect in classicEffects.Values.OrderBy(effect => effect.Name, StringComparer.Ordinal))
        {
            if (!art.Effects.TryGetValue(effect.MediaId, out DaggerfallEffectArt? effectArt)) continue;
            if (effectArt.Frames.Count != effect.Frames.Count)
                throw new InvalidOperationException($"Refreshed effect '{effect.MediaId}' has {effectArt.Frames.Count} frames; its classic effect has {effect.Frames.Count}.");
            RenderResourceInfo texture = appearance.OpenTexture(content, effectArt.Path, effectArt.Sha256, TextureFilter.Linear);
            ownedResources.Add(texture.Handle);
            refreshedEffects.Add(effect.Name, (texture.Handle, effectArt));
        }
        if (art.EffectGlow is { } glow)
        {
            RenderResourceInfo texture = appearance.OpenTexture(content, glow.Path, glow.Sha256, TextureFilter.Linear);
            ownedResources.Add(texture.Handle);
            particleGlow = texture.Handle;
        }
        Dictionary<string, RenderResource> models = new(StringComparer.Ordinal);
        foreach (var ((archive, record), mesh) in art.Meshes)
        {
            if (!models.TryGetValue(mesh.File.Path, out RenderResource? model))
            {
                using ContentReference reference = content.ResolveReference(new ContentResolveRequest(mesh.File.Path, mesh.File.Sha256));
                model = animation.OpenAnimatedMeshFromContent(new(reference));
                ownedResources.Add(model);
                models.Add(mesh.File.Path, model);
            }
            worldMeshes.Add((archive, record), (model, mesh.Footprint));
        }
        foreach (ActorVisual visual in actors.Values) AttachCorpseMesh(visual);
        foreach (DecorationVisual flat in decorations) AttachDecorationMesh(flat);
        UseSurfaceArt(art);
    }

    /// <summary>
    /// A refreshed mesh standing in for a classic sprite cut from <paramref name="source"/>, and the scale that draws it
    /// at <paramref name="width"/>, the sprite's world width; null when the refreshed set has no mesh for it.
    /// </summary>
    private (Appearance Appearance, float Scale)? WorldMesh((int Archive, int Record)? source, float width) =>
        source is { } key && meshes is not null && worldMeshes.TryGetValue(key, out var mesh)
            ? (meshes.CreateAnimatedMeshAppearance(new(mesh.Model)), (float)(width / mesh.Footprint)) : null;

    /// <summary>Gives an actor whose corpse sprite has a refreshed mesh its own appearance of that mesh.</summary>
    private void AttachCorpseMesh(ActorVisual visual)
    {
        if (visual.CorpseMesh is not null || visual.Sprite.Corpse is not { } corpse) return;
        if (WorldMesh(corpse.SourceTexture, corpse.Size.X) is { } mesh) (visual.CorpseMesh, visual.CorpseMeshScale) = mesh;
    }

    /// <summary>Gives a placed decoration whose sprite has a refreshed mesh its own appearance of that mesh.</summary>
    private void AttachDecorationMesh(DecorationVisual flat)
    {
        if (flat.Mesh is null && WorldMesh(flat.Definition.Source, flat.Definition.Summer.Size.X) is { } mesh) (flat.Mesh, flat.MeshScale) = mesh;
    }

    /// <summary>Gives a standing person the set has art for a refreshed sprite, at the classic size.</summary>
    private void AttachRefreshedPerson(ActorVisual visual)
    {
        NormalizedActorSprite sprite = visual.Sprite;
        if (visual.Refreshed is not null || visual.Live is null || sprite.SourceTexture is not { } source
            || surfaceArt?.People.GetValueOrDefault(source) is not { } art) return;
        if (!personAtlases.TryGetValue(art.File.Path, out SpriteAtlas? atlas))
        {
            RenderResourceInfo texture = appearance.OpenTexture(content, art.File.Path, art.File.Sha256, TextureFilter.Nearest);
            ownedResources.Add(texture.Handle);
            DaggerfallArtRect frame = art.Frames[0];
            atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, SpriteAtlasAdapter.ToAtlasFrames(art.Width, art.Height,
                [new NormalizedSpriteFrame(sprite.InitialFrameId, (int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height)])));
            atlases.Add(atlas);
            personAtlases.Add(art.File.Path, atlas);
        }
        // A painted hem or foot can reach below the classic frame, so the person stands on the art's own base.
        (Vector2 size, Vector2 pivot) = art.Placed(sprite.Size, sprite.Pivot);
        visual.Refreshed = appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(atlas, sprite.InitialFrameId, pivot with { Y = 0F }, size,
            BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F)));
    }

    /// <summary>The sprite a living actor shows: its refreshed one while refreshed art is drawn, else its classic one.</summary>
    private Appearance? DrawnLive(ActorVisual visual) => drawRefreshedEffects() && visual.Refreshed is { } refreshed ? refreshed : visual.Live;

    /// <summary>The corpse a defeated actor shows: the refreshed mesh while refreshed art is drawn, else its sprite.</summary>
    private Appearance? DrawnCorpse(ActorVisual visual) => drawRefreshedEffects() && visual.CorpseMesh is { } mesh ? mesh : visual.Corpse;

    private static bool Additive(string effect) => effect == "magicSparkle"
        || effect.StartsWith("fire", StringComparison.Ordinal) || effect.StartsWith("shock", StringComparison.Ordinal) || effect.StartsWith("magic", StringComparison.Ordinal);

    /// <summary>Starts one effect where it happens, in refreshed art when that is drawn; null when the effect is not published.</summary>
    private EffectVisual? SpawnEffect(string name, WorldPoint position, PresentationEventIdentity identity,
        string? activeInstance = null, long? target = null, long? source = null, ulong? item = null)
    {
        if (EffectSprite(name) is not { } sprite) return null;
        EffectVisual spawned = new(NextVisualEntityId(), position, sprite.Atlas, sprite.Visual, sprite.Playback)
        { ActiveInstance = activeInstance, TargetActor = target, SourceActor = source, SourceItem = item, Refreshed = sprite.Refreshed };
        effects.Add(spawned);
        return spawned;
    }

    /// <summary>
    /// One effect's sprite, playing, in refreshed art when that is drawn: at its classic world size, or <paramref name="width"/>
    /// metres wide; null when the effect is not published.
    /// </summary>
    private (SpriteAtlas Atlas, Appearance Visual, SpritePlayback Playback, bool Refreshed)? EffectSprite(string name, float? width = null)
    {
        if (!classicEffects.TryGetValue(name, out NormalizedClassicEffect? effect)) return null;
        float scale = width is { } wanted ? wanted / effect.DisplaySize.X : 1F;
        bool refreshed = drawRefreshedEffects() && refreshedEffects.ContainsKey(name);
        SpriteAtlas? atlas = null;
        Appearance? visual = null;
        SpritePlayback? playback = null;
        try
        {
            SpriteFromAtlasRequest request;
            uint initialFrame = effect.Frames.Single(frame => frame.Id == effect.Sequence[0]).Id;
            if (refreshed)
            {
                (RenderResource texture, DaggerfallEffectArt art) = refreshedEffects[name];
                SpriteAtlasFrame[] frames = SpriteAtlasAdapter.ToAtlasFrames(art.Width, art.Height, art.Frames.Select((frame, index) =>
                    new NormalizedSpriteFrame((uint)index, (int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height)).ToArray());
                atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture, frames));
                // The refreshed frame is larger than the classic one it holds: scale the classic size by the share the
                // classic frame takes of it, and move the classic anchor (a fraction of its frame, from the bottom) into it.
                DaggerfallArtRect classic = art.Classic;
                Vector2 size = effect.DisplaySize * scale / new Vector2((float)classic.Width, (float)classic.Height);
                Vector2 pivot = new((float)(classic.X + effect.Pivot.X * classic.Width), (float)(1 - classic.Y - classic.Height + effect.Pivot.Y * classic.Height));
                request = new SpriteFromAtlasRequest(atlas, initialFrame, pivot, size, BillboardMode.Spherical, SpriteSizeMode.World, 0,
                    SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F), new SpriteMaterialDescriptor(SpriteLightingMode.Unlit, default, default, 1f, 0f,
                        SpriteAlphaMode.Blend, .5f, SpriteShadowPolicy.None, Additive(name) ? SpriteBlendMode.Additive : SpriteBlendMode.Alpha, .15f));
            }
            else
            {
                RenderResourceInfo texture = classicTextures[effect.TexturePath];
                SpriteAtlasFrame[] frames = SpriteAtlasAdapter.ToAtlasFrames(effect.AtlasWidth, effect.AtlasHeight,
                    effect.Frames.Select(frame => new NormalizedSpriteFrame(frame.Id, frame.X, frame.Y, frame.Width, frame.Height)).ToArray());
                atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, frames));
                request = new SpriteFromAtlasRequest(atlas, initialFrame, effect.Pivot, effect.DisplaySize * scale, BillboardMode.Cylindrical, SpriteSizeMode.World, 0,
                    SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F));
            }
            visual = appearance.CreateSpriteFromAtlas(request);
            SpritePlaybackFrame[] playbackFrames = SpriteAtlasAdapter.ToPlaybackFrames(effect.Sequence.Select(index => effect.Frames.Single(frame => frame.Id == index).Id).ToArray(), effect.FramesPerSecond);
            playback = appearance.CreateSpritePlayback(new SpritePlaybackCreateRequest(visual, atlas, playbackFrames, Array.Empty<SpritePlaybackMarker>(), effect.Loops ? SpritePlaybackLoopMode.Loop : SpritePlaybackLoopMode.OneShot, 1d));
            appearance.ControlSpritePlayback(new SpritePlaybackControlRequest(playback, SpritePlaybackControl.Start));
            return (atlas, visual, playback, refreshed);
        }
        catch
        {
            playback?.Dispose();
            visual?.Dispose();
            atlas?.Dispose();
            throw;
        }
    }

    /// <summary>Keeps one missile visual (and, in refreshed art, its trail) on each spell in flight, and retires those that landed.</summary>
    private void ReconcileSpellFlights(IReadOnlyList<DaggerfallDungeonSpellFlightView> flights)
    {
        HashSet<long> live = [.. flights.Select(flight => flight.Sequence)];
        foreach (long landed in spellFlightVisuals.Keys.Where(sequence => !live.Contains(sequence)).ToArray())
        {
            var (visual, _, trail, _) = spellFlightVisuals[landed];
            spellFlightVisuals.Remove(landed);
            effects.Remove(visual);
            Retire(visual);
            if (trail is not null) Retire(trail);
        }
        foreach (DaggerfallDungeonSpellFlightView flight in flights)
        {
            if (spellFlightVisuals.TryGetValue(flight.Sequence, out var flying))
            {
                flying.Visual.Position = flight.Position;
                if (flying.Trail is { } trail) particles!.UpdateEmitter(trail, Trail(flying.TrailId, flight.Sequence, flight.Element, flight.Position.ToVector()));
                continue;
            }
            if (DaggerfallSpellElements.Name(flight.Element) is not { } element
                || SpawnEffect($"{element}Missile", flight.Position, Event(0, 0, checked((ulong)flight.Sequence), 0, "spell-flight")) is not { } spawned) continue;
            ulong trailId = NextVisualEntityId();
            PresentationEmitter? emitter = spawned.Refreshed && particles is not null && particleGlow is not null
                ? particles.CreateEmitter(Trail(trailId, flight.Sequence, flight.Element, flight.Position.ToVector())) : null;
            spellFlightVisuals.Add(flight.Sequence, (spawned, flight.Element, emitter, trailId));
        }
    }

    /// <summary>Starts a landed spell's impact where it struck, bursting into particles in refreshed art.</summary>
    private void SpawnSpellImpact(long sequence, int element, Vector3 position)
    {
        if (DaggerfallSpellElements.Name(element) is not { } name
            || SpawnEffect($"{name}Impact", WorldPoint.From(position), Event(0, 0, checked((ulong)sequence), 0, "spell-impact")) is not { Refreshed: true }
            || particles is null || particleGlow is null) return;
        particles.EmitParticles(Particles(NextVisualEntityId(), $"daggerfall.spell-impact.{sequence}", element, position,
            rate: 0, burst: 24, lifetime: (.35f, .7f), speed: 2.5f, size: .3f));
    }

    private PresentationParticleDescriptor Trail(ulong id, long sequence, int element, Vector3 position) =>
        Particles(id, $"daggerfall.spell-trail.{sequence}", element, position, rate: 40, burst: 0, lifetime: (.25f, .5f), speed: .3f, size: .25f);

    private PresentationParticleDescriptor Particles(ulong id, string signal, int element, Vector3 position, float rate, uint burst,
        (float Min, float Max) lifetime, float speed, float size)
    {
        Color color = ParticleColor(element);
        return new PresentationParticleDescriptor(id, signal, new PresentationAnchor(PresentationAnchorKind.World, position, 0, Vector3.Zero),
            PresentationParticleVisual.Billboard, new RenderResourceReference(particleGlow!), 1, rate, burst, lifetime.Min, lifetime.Max,
            new Vector3(-speed), new Vector3(speed), Vector3.Zero,
            new PresentationParticleScalarKey[] { new(0, size), new(1, size * .2f) },
            new PresentationParticleColorKey[] { new(0, color), new(1, color with { A = 0 }) },
            0, (ulong)id, 96, true, false, default, ReadOnlyMemory<PresentationParticleCollisionVolume>.Empty, PresentationParticleSizeMode.World,
            Additive($"{DaggerfallSpellElements.Name(element)}Missile") ? PresentationParticleBlendMode.Additive : PresentationParticleBlendMode.Alpha, .1f);
    }

    /// <summary>Each element's particle colour: brighter, fuller versions of its light.</summary>
    private static Color ParticleColor(int element) => element switch
    {
        0 => new(1f, .55f, .2f, .9f),
        1 => new(.75f, .9f, 1f, .8f),
        2 => new(.45f, .85f, .3f, .7f),
        3 => new(.6f, .8f, 1f, .9f),
        _ => new(.55f, .95f, .8f, .9f),
    };
}
