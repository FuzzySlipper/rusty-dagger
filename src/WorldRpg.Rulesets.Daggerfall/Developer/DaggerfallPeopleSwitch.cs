using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The developer switches that draw every walking townsperson as one figure, through every site appearance, and back
/// to their own art: people.rig as the art set's parts-rig test figure (<see cref="DaggerfallPeopleRig"/>) in one of
/// its view sets, people.splat as one of its gsplat characters (<see cref="DaggerfallSplatPeople"/>). One figure is in
/// use at a time. Each crowd is made on its first use, and a gsplat character is opened on its first use and kept for
/// the session. Site appearances draw walkers through this switch: it shows them as the crowd in use, and settles every
/// crowd it made, so one put aside lets go of its walkers.
/// </summary>
/// <param name="rig">The art set's people rig, or null when it carries none.</param>
/// <param name="splats">The art set's gsplat townspeople, or null when it carries none.</param>
/// <param name="partsRigs">The Engine's parts rigs, which the rig figure draws through.</param>
/// <param name="animation">The Engine's animation, which opens, poses and draws the gsplat characters.</param>
/// <param name="content">The product content the gsplat characters and their clips are read from.</param>
/// <param name="walkerSpeed">How fast a townsperson walks, in metres a second, which a character's stride keeps pace with.</param>
/// <param name="actors">The actors, counted for who walks here now.</param>
internal sealed class DaggerfallPeopleSwitch(DaggerfallPeopleRig? rig, DaggerfallSplatPeople? splats, Func<IPartsRigsService> partsRigs,
    Func<IAnimationService> animation, Func<IContentService> content, float walkerSpeed, ActorsState actors) : IWalkerCrowd, IDisposable, IDeveloperPeopleCommands
{
    /// <summary>The rig pages are painted far finer than a classic person's pixels, so they are sampled smooth.</summary>
    private const TextureFilter Filter = TextureFilter.Linear;

    /// <summary>The figure takes the light around it as a person's sprite does, and casts her shape into the sun's shadow.</summary>
    private static readonly SpriteMaterialDescriptor Material = DaggerfallSiteAppearance.CastingSprite;

    /// <summary>
    /// A gsplat character is lit per pixel by the world's lights and keeps this share of its paint however dark they
    /// are, and casts an ellipsoid a joint into the sun's shadow: the Engine's choice for a crowd.
    /// </summary>
    private const float SplatLightFloor = .15F;

    /// <summary>A gsplat character crossfades this long between its walk and its idle.</summary>
    private const float SplatCrossfadeSeconds = .25F;

    private readonly Dictionary<string, (RenderResource Body, Appearance Appearance, SplatFigure Figure)> characters = new(StringComparer.Ordinal);
    private (RenderResource Walk, RenderResource Idle)? clips;
    private PartsRigCrowd? rigCrowd;
    private SplatCrowd? splatCrowd;

    /// <summary>The crowd walking people are drawn as, or null while they show their own art.</summary>
    internal IWalkerCrowd? Crowd { get; private set; }

    public Appearance? Show(ulong id, float heightMetres, bool walking) => Crowd?.Show(id, heightMetres, walking);

    public void Publishing()
    {
        rigCrowd?.Publishing();
        splatCrowd?.Publishing();
    }

    public void Settle()
    {
        rigCrowd?.Settle();
        splatCrowd?.Settle();
    }

    public DebugCommandResult Rig(string set)
    {
        if (set == "off") return Off();
        if (rig is null) return DaggerfallDeveloperReplies.Refused("The art set carries no people rig.");
        if (!rig.Directions.ContainsKey(set))
            return DaggerfallDeveloperReplies.Refused($"People rig {rig.Name} has no view set '{set}'; it has {string.Join(", ", rig.Directions.Keys.Order(StringComparer.Ordinal))}, or off.");
        PartsRigFigure figure = rig.Figure(set, Filter, Material);
        (rigCrowd ??= new(partsRigs)).Use(figure);
        splatCrowd?.Use(null);
        Crowd = rigCrowd;
        return DebugCommandResult.Success($"Walking people draw as {rig.Name} seen from {figure.Views.Count} directions; {Walking()} walk here now.");
    }

    public DebugCommandResult Splat(string character)
    {
        if (character == "off") return Off();
        if (splats is null) return DaggerfallDeveloperReplies.Refused("The art set carries no gsplat townspeople.");
        if (!splats.Characters.TryGetValue(character, out DaggerfallSplatPerson? person))
            return DaggerfallDeveloperReplies.Refused($"The art set has no gsplat character '{character}'; it has {string.Join(", ", splats.Characters.Keys.Order(StringComparer.Ordinal))}, or off.");
        SplatFigure figure = Open(splats, character, person);
        (splatCrowd ??= new(animation)).Use(figure);
        rigCrowd?.Use(null);
        Crowd = splatCrowd;
        return DebugCommandResult.Success($"Walking people draw as gsplat {figure.Name}, lit; {Walking()} walk here now.");
    }

    public void Dispose()
    {
        List<Exception>? failures = null;
        // The walkers' instances and rigs go before the characters they draw, and the characters before their clips.
        foreach (IDisposable? owned in new IDisposable?[] { rigCrowd, splatCrowd }
            .Concat(characters.Values.SelectMany(opened => new IDisposable[] { opened.Appearance, opened.Body }))
            .Concat(clips is { } packs ? [packs.Walk, packs.Idle] : []))
        {
            try { owned?.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        characters.Clear();
        clips = null;
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private DebugCommandResult Off()
    {
        rigCrowd?.Use(null);
        splatCrowd?.Use(null);
        Crowd = null;
        return DebugCommandResult.Success("Walking people show their own art again.");
    }

    private int Walking() => actors.All.Count(actor => actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian && actor.Wander.State != WanderState.Unloaded);

    /// <summary>
    /// Character <paramref name="id"/>, opened on its first use: its body with the walk and idle bound by joint names and
    /// fitted to its legs, drawn lit.
    /// </summary>
    private SplatFigure Open(DaggerfallSplatPeople art, string id, DaggerfallSplatPerson person)
    {
        if (characters.TryGetValue(id, out var opened)) return opened.Figure;
        IAnimationService engine = animation();
        if (clips is null)
        {
            using ContentReference walk = Reference(art.Walk), idle = Reference(art.Idle);
            clips = (engine.OpenAnimationClipPackFromContent(new(walk)), engine.OpenAnimationClipPackFromContent(new(idle)));
        }
        RenderResource body;
        using (ContentReference reference = Reference(person.Body)) body = engine.OpenAnimatedMeshFromContent(new(reference));
        Appearance? appearance = null;
        try
        {
            foreach (RenderResource pack in new[] { clips.Value.Walk, clips.Value.Idle })
                engine.AssociateAnimationClipPackByJointNames(new(body, pack, art.Producer, art.License));
            // The walk's pelvis carries its travel forward (+z); the walker moves itself, so the walk keeps only its sway.
            engine.FitAnimationClipTranslation(new(body, clips.Value.Walk, DaggerfallSplatPeople.WalkClip, art.Joint,
                new(person.Walk.Scale, person.Walk.Scale, 0F), new(0F, person.Walk.Lift, 0F)));
            engine.FitAnimationClipTranslation(new(body, clips.Value.Idle, DaggerfallSplatPeople.IdleClip, art.Joint,
                new(person.Idle.Scale), new(0F, person.Idle.Lift, 0F)));
            AnimationClipInfo[] bound = engine.ReadClips(body).ToArray();
            string walkClip = ClipId(bound, DaggerfallSplatPeople.WalkClip), idleClip = ClipId(bound, DaggerfallSplatPeople.IdleClip);
            appearance = engine.CreateAnimatedMeshAppearance(new(body));
            engine.SetSplatAppearance(new(appearance, Vector3.One, 0, .5F, ReadOnlyMemory<Color>.Empty, 0F, SplatLighting.Lit, SplatLightFloor, SplatShadowCaster.Ellipsoids));
            SplatFigure figure = new($"{id} ({person.Name})", appearance, walkClip, idleClip, walkerSpeed / person.WalkSpeed, SplatCrossfadeSeconds);
            characters.Add(id, (body, appearance, figure));
            return figure;
        }
        catch
        {
            appearance?.Dispose();
            body.Dispose();
            throw;
        }

        string ClipId(AnimationClipInfo[] bound, string name) => bound.Where(clip => clip.Name == name).Select(clip => clip.Id).FirstOrDefault()
            ?? throw new InvalidOperationException($"Gsplat character {id} carries no clip '{name}' after its clip packs were bound; it has {string.Join(", ", bound.Select(clip => clip.Name))}.");
    }

    private ContentReference Reference(DaggerfallArtFile file) => content().ResolveReference(new ContentResolveRequest(file.Path, file.Sha256));
}
