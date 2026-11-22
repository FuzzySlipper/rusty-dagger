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
/// How walking townspeople draw, through every site appearance. By default each draws as the art set's gsplat character
/// (<see cref="DaggerfallSplatPeople"/>) of its classic townsperson art, while the refreshed art draws; a walker whose
/// art has no character draws its sprite, said once as a warning. The developer switches draw every walker as one
/// figure instead and back: people.rig as the art set's parts-rig test figure (<see cref="DaggerfallPeopleRig"/>) in one
/// of its view sets, people.splat ID as one gsplat character, people.splat own as their own characters again, and off
/// as their sprites. The gsplat people draw in the tuning's look until people.splat-look changes it for the run. One
/// figure is in use at a time. people.spawn stands a few walkers in front of the player as a gsplat character of their
/// own, whatever the rest are drawn as. The rig crowd is made on its first use, and a gsplat character is opened on its
/// first use and kept for the session. It shows a spawned walker as its character and the rest as the crowd in use, and
/// settles every crowd it made, so one put aside lets go of its walkers. Each walker drawn as a character walks one of the
/// art set's walks and stands in one of its idles, picked by its name seed with a rate about 1 that its clips play at and
/// it walks at, and a phase they start at (<see cref="SplatGait"/>), so the same seed walks alike after a load. A character in a long skirt walks the
/// art set's skirt walks instead, softer, with its skirt pushed open by the legs: by the tuning (or people.splat-pace for
/// the run) its walkers go at their walk's own pace, slower than the rest, or the walk plays faster to keep the walkers' pace.
/// The characters' clips are read from the animations container (<see cref="DaggerfallAnimations"/>); without it every
/// walker draws its sprite and the gsplat switches are refused.
/// A walker drawn as a character whose eyes are marked blinks on its own clock from its name seed, by the tuning, until
/// people.splat-blinks off; the characters without eyes are said once, when the crowd is made.
/// </summary>
/// <param name="rig">The art set's people rig, or null when it carries none.</param>
/// <param name="splats">The art set's gsplat townspeople, or null when it carries none.</param>
/// <param name="clips">The animations container the gsplat townspeople's clips are read from, or null when it is not installed.</param>
/// <param name="partsRigs">The Engine's parts rigs, which the rig figure draws through.</param>
/// <param name="animation">The Engine's animation, which opens, poses and draws the gsplat characters.</param>
/// <param name="content">The product content the gsplat characters' bodies are read from.</param>
/// <param name="walkerSpeed">How fast a townsperson walks, in metres a second, which a character's stride keeps pace with.</param>
/// <param name="tuning">How the gsplat people draw.</param>
/// <param name="actors">The actors, counted for who walks here now.</param>
/// <param name="stage">The staging people.spawn stands its walkers with.</param>
/// <param name="townspeople">Every classic townsperson archive a walker may wear, each of which should have a character.</param>
/// <param name="townsperson">The classic townsperson archive a walker wears, by actor; null when it wears none.</param>
/// <param name="seed">A walker's stable seed, by actor, which picks its gait.</param>
/// <param name="refreshed">Whether the refreshed art draws; while the classic art does, walkers draw their sprites.</param>
/// <param name="warn">Says what the walkers draw that the art set did not mean them to.</param>
internal sealed class DaggerfallPeopleSwitch(DaggerfallPeopleRig? rig, DaggerfallSplatPeople? splats, DaggerfallAnimations? clips, Func<IPartsRigsService> partsRigs,
    Func<IAnimationService> animation, Func<IContentService> content, float walkerSpeed, DaggerfallSplatPeopleTuning tuning, ActorsState actors,
    DaggerfallPeopleStage stage, IReadOnlyCollection<int> townspeople, Func<long, int?> townsperson, Func<long, int> seed, Func<bool> refreshed, Action<string> warn)
    : IWalkerCrowd, IDisposable, IDeveloperPeopleCommands
{
    /// <summary>The rig pages are painted far finer than a classic person's pixels, so they are sampled smooth.</summary>
    private const TextureFilter Filter = TextureFilter.Linear;

    /// <summary>The figure takes the light around it as a person's sprite does, and casts her shape into the sun's shadow.</summary>
    private static readonly SpriteMaterialDescriptor Material = DaggerfallSiteAppearance.CastingSprite;

    /// <summary>Spawned walkers stand this far apart, in the first of these rows ahead that has clear ground.</summary>
    private const float SpawnSpacing = 1.5F;
    private static readonly Transform Unplaced = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
    private static readonly float[] SpawnRows = [4F, 5.5F, 7F, 8.5F];

    private readonly Dictionary<string, (RenderResource Body, Appearance Appearance, SplatFigure Figure)> characters = new(StringComparer.Ordinal);
    /// <summary>How the gsplat people are finished: the tuning's look until a developer sets another for the run.</summary>
    private (uint Grid, float Coverage, bool OwnPalette, float PoseRate, SplatShadowCaster Shadow) look =
        (tuning.PixelGrid, tuning.PixelCoverage, tuning.OwnPalette, tuning.PoseRate, tuning.ShadowCaster);
    /// <summary>The clip packs opened for the characters, by the clip each carries; each opens on the first character that plays it.</summary>
    private readonly Dictionary<string, RenderResource> packs = new(StringComparer.Ordinal);
    /// <summary>The characters that would not open, said once each; walkers wearing their art draw their sprites.</summary>
    private readonly HashSet<string> broken = new(StringComparer.Ordinal);
    private bool unclothed;
    /// <summary>Whether walkers drawn in a long skirt walk at their walk's own pace, not the walkers': the tuning's until people.splat-pace.</summary>
    private bool slow = tuning.SkirtsSlow;
    /// <summary>Whether walkers drawn as a character with eyes blink: the default, until people.splat-blinks off.</summary>
    private bool blinking = true;
    private PartsRigCrowd? rigCrowd;
    private SplatCrowd? splatCrowd;

    /// <summary>Whether walkers draw as the characters of their own townsperson art: the default, until people.rig or people.splat off.</summary>
    private bool own = splats is not null && clips is not null;

    /// <summary>Whether this session has given way to its replacement: its walkers draw their sprites from then on.</summary>
    private bool yielded;

    /// <summary>
    /// Whether the characters no walker draws go once the next snapshot is accepted: open characters cost the Engine's
    /// splat pass even when none is shown, so the sprites cost only what sprites cost.
    /// </summary>
    private bool letGo;

    /// <summary>The crowd walking people are drawn as, or null while they show their sprites.</summary>
    internal IWalkerCrowd? Crowd { get; private set; }

    public Appearance? Show(ulong id, long actor, float heightMetres, bool walking)
    {
        if (yielded) return null;
        if (own) Crowd ??= Splats();
        Appearance? character = splatCrowd?.Show(id, actor, heightMetres, walking);
        // A walker drawn as a character goes at its gait's pace, which its walk's stride keeps; one drawn otherwise at the walkers' own.
        Pace(actor, character is not null && splatCrowd!.Gait(id) is { } gait ? gait.Pace : 1F);
        return character ?? (Crowd == splatCrowd ? null : Crowd?.Show(id, actor, heightMetres, walking));
    }

    public void Publishing()
    {
        rigCrowd?.Publishing();
        splatCrowd?.Publishing();
    }

    /// <summary>Counts the drawn time of an admitted update toward the next settle, which moves the gsplat people's skirts on by it.</summary>
    internal void Advance(float seconds) => splatCrowd?.Advance(seconds);

    public void Settle()
    {
        rigCrowd?.Settle();
        splatCrowd?.Settle();
        if (letGo) LetGo();
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
        own = false;
        letGo = true;
        Crowd = rigCrowd;
        return DebugCommandResult.Success($"Walking people draw as {rig.Name} seen from {figure.Views.Count} directions; {Walking().Length} walk here now.");
    }

    public DebugCommandResult Splat(string character)
    {
        if (character == "off") return Off();
        if (Unplayable() is { } refused) return refused;
        letGo = false;
        if (character == "own")
        {
            rigCrowd?.Use(null);
            own = true;
            Splats().Use(null);
            Crowd = splatCrowd;
            ActorState[] walking = Walking();
            int sprites = walking.Count(walker => townsperson(walker.DurableId) is not { } archive || !splats!.ByArchive.ContainsKey(archive));
            return DebugCommandResult.Success($"Walking people draw as the gsplat characters of their townsperson art, lit; {walking.Length} walk here now" +
                (sprites == 0 ? "." : $", {sprites} of them as sprites, wearing art without a character."));
        }
        if (!splats!.Characters.TryGetValue(character, out DaggerfallSplatPerson? person))
            return DaggerfallDeveloperReplies.Refused($"The art set has no gsplat character '{character}'; it has {string.Join(", ", splats.Characters.Keys.Order(StringComparer.Ordinal))}, own or off.");
        SplatFigure figure = Open(splats, character, person);
        Splats().Use(figure);
        rigCrowd?.Use(null);
        Crowd = splatCrowd;
        return DebugCommandResult.Success($"Walking people draw as gsplat {figure.Name}, lit; {Walking().Length} walk here now.");
    }

    public DebugCommandResult Spawn(string character, int count)
    {
        if (character == "off")
        {
            int released = splatCrowd?.Assigned.Count ?? 0;
            foreach (long actor in splatCrowd?.Assigned.Keys.ToArray() ?? []) splatCrowd!.Assign(actor, null);
            return DebugCommandResult.Success($"{released} spawned walkers draw as the rest do again.");
        }
        if (count is < 1 or > 16) return DaggerfallDeveloperReplies.Refused("people.spawn takes a COUNT of 1 to 16.");
        if (Unplayable() is { } refused) return refused;
        if (!splats!.Characters.TryGetValue(character, out DaggerfallSplatPerson? person))
            return DaggerfallDeveloperReplies.Refused($"The art set has no gsplat character '{character}'; it has {string.Join(", ", splats.Characters.Keys.Order(StringComparer.Ordinal))}, or off.");
        letGo = false;
        SplatFigure figure = Open(splats, character, person);
        Splats();
        HashSet<long> spawned = [.. splatCrowd!.Assigned.Keys];
        DebugCommandResult placed = default!;
        ActorState[] walkers = [];
        // A row already full of spawned walkers is not clear ground, so later spawns stand further back.
        foreach (float metres in SpawnRows)
            if ((placed = stage.Place(count, metres, (count - 1) * SpawnSpacing, crossing: false, spawned, out walkers)).Succeeded) break;
        if (!placed.Succeeded) return placed;
        foreach (ActorState walker in walkers) splatCrowd.Assign(walker.DurableId, figure);
        return DebugCommandResult.Success($"{count} walking {(count == 1 ? "person stands" : "people stand")} in front of the player as gsplat {figure.Name}, until people.unstage.");
    }

    public DebugCommandResult SplatLook(int grid, double coverage, string palette, double poseRate)
    {
        if (grid is < 0 or > 16 || !(coverage is > 0 and <= 1) || palette is not ("own" or "none") || !(poseRate >= 0) || !double.IsFinite(poseRate)
            || palette == "own" && grid == 0)
            return DaggerfallDeveloperReplies.Refused("people.splat-look takes GRID 0 to 16, COVERAGE above 0 to 1, own (needs a grid) or none, and POSERATE 0 or more.");
        look = look with { Grid = (uint)grid, Coverage = (float)coverage, OwnPalette = palette == "own", PoseRate = (float)poseRate };
        foreach ((string id, var opened) in characters) Finish(opened.Appearance, splats!.Characters[id]);
        return DebugCommandResult.Success(grid == 0
            ? $"Gsplat people draw smooth, posed {(poseRate == 0 ? "every frame" : $"{poseRate:0.#} times a second")}."
            : $"Gsplat people draw on a {grid}-pixel grid, blocks where they cover {coverage:0.##}, {(look.OwnPalette ? "in their own palette" : "in their own colours")}, posed {(poseRate == 0 ? "every frame" : $"{poseRate:0.#} times a second")}.");
    }

    public DebugCommandResult SplatPace(string pace)
    {
        if (DaggerfallSplatPeopleTuning.Slow(pace) is not { } walksSlow) return DaggerfallDeveloperReplies.Refused("people.splat-pace takes slow or clip.");
        slow = walksSlow;
        // Each character's walks play at the new rates: its figure is replaced wherever it is drawn.
        foreach ((string id, var opened) in characters.ToArray())
        {
            DaggerfallSplatPerson person = splats!.Characters[id];
            SplatFigure figure = opened.Figure with { Walks = [.. splats.WalksOf(person).Select((clip, at) => Walk(person, clip) with { Clip = opened.Figure.Walks[at].Clip })] };
            characters[id] = opened with { Figure = figure };
            if (splatCrowd?.Figure?.Appearance == opened.Appearance) splatCrowd.Use(figure);
            foreach ((long actor, SplatFigure given) in splatCrowd?.Assigned.ToArray() ?? [])
                if (given.Appearance == opened.Appearance) splatCrowd!.Assign(actor, figure);
        }
        return DebugCommandResult.Success(slow
            ? "Gsplat people in a long skirt walk at their gentle walk's own pace, slower than the rest."
            : "Gsplat people in a long skirt walk at the walkers' pace, their gentle walk played faster to keep up.");
    }

    public DebugCommandResult SplatBlinks(string state)
    {
        if (state is not ("on" or "off")) return DaggerfallDeveloperReplies.Refused("people.splat-blinks takes on or off.");
        blinking = state == "on";
        splatCrowd?.Blink(blinking ? tuning.Blinks : null);
        return DebugCommandResult.Success(blinking
            ? $"Gsplat people with eyes blink every {tuning.Blinks.IntervalMinSeconds:0.#} to {tuning.Blinks.IntervalMaxSeconds:0.#} seconds, each on its own clock."
            : "Gsplat people keep their eyes open.");
    }

    public DebugCommandResult SplatShadow(string caster)
    {
        if (DaggerfallSplatPeopleTuning.Caster(caster) is not { } shadow)
            return DaggerfallDeveloperReplies.Refused("people.splat-shadow takes none, ellipsoids or splats.");
        look = look with { Shadow = shadow };
        foreach ((string id, var opened) in characters) Finish(opened.Appearance, splats!.Characters[id]);
        return DebugCommandResult.Success(shadow switch
        {
            SplatShadowCaster.None => "Gsplat people cast no shadow.",
            SplatShadowCaster.Splats => "Gsplat people cast their own silhouette into the sun's shadow.",
            _ => "Gsplat people cast an ellipsoid a joint into the sun's shadow.",
        });
    }

    /// <summary>
    /// Gives way to another session that will publish the same walkers' objects: from the next snapshot the walkers draw
    /// their sprites, so its publishing lets go of their instances (one left on an object that session shows as a sprite
    /// faults the Engine). Disposing after that snapshot lets go of the figures, whose bodies that session binds again.
    /// </summary>
    internal void Yield() => yielded = true;

    public void Dispose()
    {
        List<Exception>? failures = null;
        // The walkers' instances and rigs go before the characters they draw, and the characters before their clips.
        foreach (IDisposable? owned in new IDisposable?[] { rigCrowd, splatCrowd }
            .Concat(characters.Values.SelectMany(opened => new IDisposable[] { opened.Appearance, opened.Body }))
            .Concat(packs.Values))
        {
            try { owned?.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        characters.Clear();
        packs.Clear();
        rigCrowd = null;
        splatCrowd = null;
        Crowd = null;
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private DebugCommandResult Off()
    {
        rigCrowd?.Use(null);
        splatCrowd?.Use(null);
        own = false;
        letGo = true;
        Crowd = null;
        return DebugCommandResult.Success("Walking people draw their sprites.");
    }

    /// <summary>
    /// Lets go of the characters no spawned walker draws, now that the accepted snapshot shows none of them (the Engine
    /// refuses to destroy an appearance a published snapshot still draws), and of the clips once no character is open.
    /// </summary>
    private void LetGo()
    {
        letGo = false;
        HashSet<SplatFigure> spawned = [.. splatCrowd?.Assigned.Values ?? []];
        List<Exception>? failures = null;
        foreach ((string id, var opened) in characters.Where(opened => !spawned.Contains(opened.Value.Figure)).ToArray())
        {
            characters.Remove(id);
            foreach (IDisposable owned in new IDisposable[] { opened.Appearance, opened.Body })
            {
                try { owned.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
        }
        if (characters.Count == 0)
        {
            foreach (RenderResource pack in packs.Values)
            {
                try { pack.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            packs.Clear();
        }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    /// <summary>Why the gsplat townspeople cannot be drawn: the art set carries none, or their clips are not installed; null when they can.</summary>
    private DebugCommandResult? Unplayable() =>
        splats is null ? DaggerfallDeveloperReplies.Refused("The art set carries no gsplat townspeople.")
        : clips is null ? DaggerfallDeveloperReplies.Refused($"{DaggerfallAnimations.ContainerName} is not installed, so the gsplat townspeople have no clips.")
        : null;

    private ActorState[] Walking() => [.. actors.All.Where(actor => actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian && actor.Wander.State != WanderState.Unloaded)];

    /// <summary>
    /// The gsplat crowd, made on its first use, drawing each walker as its own character while <see cref="own"/>. As it is
    /// made it says which townsperson art has no character.
    /// </summary>
    private SplatCrowd Splats()
    {
        if (splatCrowd is not null) return splatCrowd;
        int[] missing = [.. townspeople.Where(archive => !splats!.ByArchive.ContainsKey(archive)).Distinct().Order()];
        if (missing.Length != 0)
            warn($"Townsperson art {string.Join(", ", missing)} has no gsplat character; walkers wearing it draw their sprites.");
        string[] blind = [.. splats!.Characters.Where(character => character.Value.Eyes is null).Select(character => character.Key).Order(StringComparer.Ordinal)];
        if (blind.Length != 0)
            warn($"Gsplat character{(blind.Length == 1 ? "" : "s")} {string.Join(", ", blind)} {(blind.Length == 1 ? "has" : "have")} no eyes marked, so walkers drawn as them don't blink.");
        splatCrowd = new(animation, seed, tuning.GaitJitter);
        splatCrowd.UseOwn(Own);
        splatCrowd.Blink(blinking ? tuning.Blinks : null);
        return splatCrowd;
    }

    /// <summary>
    /// Walker <paramref name="actor"/>'s own character, opened on its first use, while walkers draw as their own and the
    /// refreshed art draws; null (its sprite) when its art has no character or the character would not open.
    /// </summary>
    private SplatFigure? Own(long actor)
    {
        if (!own || !refreshed()) return null;
        if (townsperson(actor) is not { } archive)
        {
            if (!unclothed) warn("A walker wears no classic townsperson art (its race has none), so it draws its sprite.");
            unclothed = true;
            return null;
        }
        if (!splats!.ByArchive.TryGetValue(archive, out string? id) || broken.Contains(id)) return null;
        try { return Open(splats, id, splats.Characters[id]); }
        catch (Exception exception)
        {
            broken.Add(id);
            warn($"Gsplat character {id} would not open, so walkers wearing townsperson art {archive} draw their sprites: {exception.Message}");
            return null;
        }
    }

    private void Pace(long actor, float pace)
    {
        if (actors.TryGet(actor, out ActorState walker) && walker.Wander.Pace != pace) walker.Wander.SetPace(pace);
    }

    /// <summary>
    /// <paramref name="person"/>'s walk <paramref name="clip"/>: in a long skirt walked slow, at its own pace, which is the
    /// share of the walkers' speed its fitted stride travels, and otherwise at theirs; played at the rate that keeps its
    /// stride at that pace.
    /// </summary>
    private SplatWalk Walk(DaggerfallSplatPerson person, string clip)
    {
        float speed = person.Fits[clip].Speed!.Value, pace = slow && person.Skirt is not null ? speed / walkerSpeed : 1F;
        return new(clip, pace, walkerSpeed * pace / speed);
    }

    /// <summary>
    /// Character <paramref name="id"/>, opened on its first use: its body with each walk and idle its walkers may play
    /// bound by joint names and fitted to its legs, drawn lit. One in a long skirt plays the skirt walks, its skirt fitted
    /// to its rig as it stands at rest.
    /// </summary>
    private SplatFigure Open(DaggerfallSplatPeople art, string id, DaggerfallSplatPerson person)
    {
        if (characters.TryGetValue(id, out var opened)) return opened.Figure;
        IAnimationService engine = animation();
        IReadOnlyList<string> walks = art.WalksOf(person);
        RenderResource body;
        using (ContentReference reference = Reference(person.Body)) body = engine.OpenAnimatedMeshFromContent(new(reference));
        Appearance? appearance = null;
        try
        {
            foreach (string clip in walks.Concat(art.Idles))
            {
                if (!packs.TryGetValue(clip, out RenderResource? pack))
                {
                    using ContentReference reference = clips!.Open(art.Clips[clip]);
                    packs.Add(clip, pack = engine.OpenAnimationClipPackFromContent(new(reference)));
                }
                engine.AssociateAnimationClipPackByJointNames(new(body, pack, art.Producer, art.License));
                // A walk's pelvis carries its travel forward (+z); the walker moves itself, so the walk keeps only its sway.
                DaggerfallSplatClipFit fit = person.Fits[clip];
                engine.FitAnimationClipTranslation(new(body, pack, clip, art.Joint,
                    fit.Speed is null ? new(fit.Scale) : new(fit.Scale, fit.Scale, 0F), new(0F, fit.Lift, 0F)));
            }
            AnimationClipInfo[] bound = engine.ReadClips(body).ToArray();
            SplatWalk[] walking = [.. walks.Select(clip => Walk(person, clip) with { Clip = ClipId(bound, clip) })];
            string[] idles = [.. art.Idles.Select(clip => ClipId(bound, clip))];
            SplatSkirtRig? skirtRig = person.Skirt is { } worn
                ? SplatSkirtRig.Resolve(worn, engine.ReadJoints(body).Span, engine.SampleModelJointPose(new(body, "", 0, "", 0, 0, false, Unplaced)).Span)
                : null;
            appearance = engine.CreateAnimatedMeshAppearance(new(body));
            Finish(appearance, person);
            SplatFigure figure = new($"{id} ({person.Name})", appearance, walking, idles, tuning.CrossfadeSeconds, skirtRig, person.Eyes);
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

    /// <summary>Draws <paramref name="person"/>'s appearance lit, in the run's look.</summary>
    private void Finish(Appearance appearance, DaggerfallSplatPerson person) => animation().SetSplatAppearance(new(appearance, Vector3.One, look.Grid, look.Coverage,
        look.OwnPalette ? person.Palette.ToArray() : ReadOnlyMemory<Color>.Empty, look.PoseRate, SplatLighting.Lit, tuning.LightFloor, look.Shadow));

    private ContentReference Reference(DaggerfallArtFile file) => content().ResolveReference(new ContentResolveRequest(file.Path, file.Sha256));
}
