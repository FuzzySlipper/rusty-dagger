using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Draws human enemies as their shipped skinned-splat characters (<see cref="DaggerfallSplatEnemies"/>) in place of
/// classic directional sprites, supporting live variant switching (<c>gaussians</c>, <c>core</c>, <c>cards</c>,
/// <c>cards-core</c>), classic sprite fallback (<c>enemies.splat off</c>), and developer clip inspection.
/// </summary>
internal sealed class DaggerfallEnemySplatSwitch(
    DaggerfallSplatEnemies? splats,
    DaggerfallAnimations? clips,
    Func<IAnimationService> animation,
    Func<IContentService> content,
    DaggerfallSplatPeopleTuning tuning,
    Func<bool> refreshed,
    Action<string> warn)
    : IEnemySplatCrowd, IDisposable, IDeveloperEnemySplatCommands
{
    private static readonly Transform Unplaced = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
    private static readonly string[] Variants = ["gaussians", "core", "cards", "cards-core"];

    private readonly Dictionary<(string Id, string Variant), (RenderResource Body, SplatSkirtRig? Skirt, Dictionary<string, string> Clips)> bodies = [];
    private readonly Dictionary<(string Id, string Variant, string LiveryKey), (Appearance Appearance, EnemySplatFigure Figure, Color? Tint)> characters = [];
    private readonly Dictionary<string, RenderResource> packs = new(StringComparer.Ordinal);
    private readonly HashSet<(string Id, string Variant)> broken = [];
    private (uint Grid, float Coverage, bool OwnPalette, float PoseRate, SplatShadowCaster Shadow, float FadeNear, float FadeFar, float SurfaceBias) look =
        (tuning.PixelGrid, tuning.PixelCoverage, tuning.OwnPalette, tuning.PoseRate, tuning.ShadowCaster, 0F, 0F, 0F);
    private EnemySplatCrowd? crowd;
    private bool enabled = splats is not null && clips is not null;
    private string? forcedCharacter;
    private string variant = "gaussians";
    private bool yielded;
    private bool letGo;

    public Appearance? Show(ulong id, long actor, int mobileId, string? gender, string state, uint actionSequence, bool defeated, Color? tint, string? liveryKey)
    {
        if (yielded || !enabled || !refreshed() || splats is null || clips is null) return null;
        string? resolvedId = forcedCharacter ?? splats.Resolve(mobileId, gender);
        if (resolvedId is null || !splats.Characters.TryGetValue(resolvedId, out DaggerfallSplatEnemy? enemy)) return null;
        string activeVariant = enemy.Bodies.ContainsKey(variant) ? variant : "gaussians";
        if (!enemy.Bodies.ContainsKey(activeVariant) || broken.Contains((resolvedId, activeVariant))) return null;
        string key = tint is { } c ? (liveryKey ?? DaggerfallHeraldry.Hex(c)) : string.Empty;
        try
        {
            EnemySplatFigure figure = Open(splats, resolvedId, activeVariant, enemy, key, tint);
            crowd ??= new(animation);
            return crowd.Show(id, actor, figure, state, actionSequence, defeated);
        }
        catch (Exception exception)
        {
            broken.Add((resolvedId, activeVariant));
            warn($"Splat enemy {resolvedId} ({activeVariant}) would not open, so enemies using it draw their classic sprites: {exception.Message}");
            return null;
        }
    }

    public void Publishing() => crowd?.Publishing();

    public void Advance(float seconds) => crowd?.Advance(seconds);

    public void Settle()
    {
        crowd?.Settle();
        if (letGo) LetGo();
    }

    public DebugCommandResult EnemiesSplat(string mode)
    {
        if (mode == "off")
        {
            enabled = false;
            forcedCharacter = null;
            letGo = true;
            return DebugCommandResult.Success("Enemies draw their classic directional sprites.");
        }
        if (Unplayable() is { } refused) return refused;
        letGo = false;
        if (mode is "on" or "own")
        {
            enabled = true;
            forcedCharacter = null;
            return DebugCommandResult.Success($"Human enemies draw as their shipped splat characters ({variant}); {crowd?.Count ?? 0} drawn now.");
        }
        if (!splats!.Characters.ContainsKey(mode))
            return DaggerfallDeveloperReplies.Refused($"No splat enemy '{mode}'; available: {string.Join(", ", splats.Characters.Keys.Order(StringComparer.Ordinal))}, on, or off.");
        enabled = true;
        forcedCharacter = mode;
        return DebugCommandResult.Success($"Human enemies draw as splat {mode} ({splats.Characters[mode].Name}, {variant}).");
    }

    public DebugCommandResult EnemiesSplatVariant(string requested)
    {
        if (!Variants.Contains(requested, StringComparer.Ordinal))
            return DaggerfallDeveloperReplies.Refused($"enemies.splat-variant takes one of: {string.Join(", ", Variants)}.");
        if (Unplayable() is { } refused) return refused;
        variant = requested;
        letGo = true;
        return DebugCommandResult.Success($"Enemy splat variant set to '{variant}'.");
    }

    public DebugCommandResult EnemiesSplatClip(string clip)
    {
        if (Unplayable() is { } refused) return refused;
        crowd ??= new(animation);
        if (clip is "auto" or "off")
        {
            crowd.OverrideClip = null;
            return DebugCommandResult.Success("Splat enemies play their live combat states.");
        }
        if (!splats!.Clips.ContainsKey(clip))
            return DaggerfallDeveloperReplies.Refused($"Unknown enemy clip '{clip}'; available: {string.Join(", ", splats.Clips.Keys.Order(StringComparer.Ordinal))}, or auto.");
        crowd.OverrideClip = clip;
        return DebugCommandResult.Success($"Splat enemies forced to clip '{clip}' (run enemies.splat-clip auto to resume live combat states).");
    }

    public DebugCommandResult EnemiesSplatLook(int grid, double coverage, string palette, double poseRate)
    {
        if (grid is < 0 or > 16 || !(coverage is > 0 and <= 1) || palette is not ("own" or "none") || !(poseRate >= 0) || !double.IsFinite(poseRate)
            || palette == "own" && grid == 0)
            return DaggerfallDeveloperReplies.Refused("enemies.splat-look takes GRID 0 to 16, COVERAGE above 0 to 1, own (needs a grid) or none, and POSERATE 0 or more.");
        look = look with { Grid = (uint)grid, Coverage = (float)coverage, OwnPalette = palette == "own", PoseRate = (float)poseRate };
        foreach (var (key, opened) in characters) Finish(opened.Appearance, splats!.Characters[key.Id], opened.Tint);
        return DebugCommandResult.Success(grid == 0
            ? $"Splat enemies draw smooth, posed {(poseRate == 0 ? "every frame" : $"{poseRate:0.#} times a second")}."
            : $"Splat enemies draw on a {grid}-pixel grid, coverage {coverage:0.##}, {(look.OwnPalette ? "own palette" : "own colours")}.");
    }

    public DebugCommandResult EnemiesSplatShadow(string caster)
    {
        if (DaggerfallSplatPeopleTuning.Caster(caster) is not { } shadow)
            return DaggerfallDeveloperReplies.Refused("enemies.splat-shadow takes none, ellipsoids, core or splats.");
        look = look with { Shadow = shadow };
        foreach (var (key, opened) in characters) Finish(opened.Appearance, splats!.Characters[key.Id], opened.Tint);
        return DebugCommandResult.Success($"Splat enemies cast shadows as {caster}.");
    }

    public DebugCommandResult EnemiesSplatFade(double near, double far, double bias)
    {
        if (!double.IsFinite(near) || !double.IsFinite(far) || !double.IsFinite(bias) || near < 0 || far < 0 || bias < 0 || bias > .5 || (far > 0 && far <= near))
            return DaggerfallDeveloperReplies.Refused("enemies.splat-fade takes NEAR >= 0, FAR > NEAR (or 0 to disable), and BIAS in 0..0.5.");
        look = look with { FadeNear = (float)near, FadeFar = (float)far, SurfaceBias = (float)bias };
        foreach (var (key, opened) in characters) Finish(opened.Appearance, splats!.Characters[key.Id], opened.Tint);
        return DebugCommandResult.Success(far > 0
            ? $"Splat enemies fade over {near:0.##}–{far:0.##} m with {bias:0.###} m surface bias."
            : $"Splat enemy distance fade disabled (surface bias {bias:0.###} m).");
    }

    internal void Yield() => yielded = true;

    public void Dispose()
    {
        List<Exception>? failures = null;
        foreach (IDisposable? owned in new IDisposable?[] { crowd }
            .Concat(characters.Values.Select(o => (IDisposable)o.Appearance))
            .Concat(bodies.Values.Select(o => (IDisposable)o.Body))
            .Concat(packs.Values))
        {
            try { owned?.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        characters.Clear();
        bodies.Clear();
        packs.Clear();
        crowd = null;
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private void LetGo()
    {
        letGo = false;
        List<Exception>? failures = null;
        foreach (var (key, opened) in characters.Where(p => !enabled || p.Key.Variant != variant).ToArray())
        {
            characters.Remove(key);
            try { opened.Appearance.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        HashSet<(string Id, string Variant)> retained = [.. characters.Keys.Select(k => (k.Id, k.Variant))];
        foreach (var (key, opened) in bodies.Where(p => !retained.Contains(p.Key)).ToArray())
        {
            bodies.Remove(key);
            try { opened.Body.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (bodies.Count == 0)
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

    private DebugCommandResult? Unplayable() =>
        splats is null ? DaggerfallDeveloperReplies.Refused("The art set carries no splat enemies.")
        : clips is null ? DaggerfallDeveloperReplies.Refused($"{DaggerfallAnimations.ContainerName} is not installed, so splat enemies have no clips.")
        : null;

    private EnemySplatFigure Open(DaggerfallSplatEnemies art, string id, string activeVariant, DaggerfallSplatEnemy enemy, string liveryKey, Color? tint)
    {
        (string Id, string Variant, string LiveryKey) cacheKey = (id, activeVariant, liveryKey);
        if (characters.TryGetValue(cacheKey, out var opened)) return opened.Figure;
        IAnimationService engine = animation();
        (string Id, string Variant) bodyKey = (id, activeVariant);
        if (!bodies.TryGetValue(bodyKey, out var openedBody))
        {
            RenderResource body;
            using (ContentReference reference = Reference(enemy.Bodies[activeVariant]))
                body = engine.OpenAnimatedMeshFromContent(new(reference));
            try
            {
                foreach ((string clipName, DaggerfallSplatClipFit fit) in enemy.Fits)
                {
                    if (!art.Clips.TryGetValue(clipName, out DaggerfallArtFile? clipFile)) continue;
                    if (!packs.TryGetValue(clipName, out RenderResource? pack))
                    {
                        using ContentReference reference = clips!.Open(clipFile);
                        packs.Add(clipName, pack = engine.OpenAnimationClipPackFromContent(new(reference)));
                    }
                    engine.AssociateAnimationClipPackByJointNames(new(body, pack, art.Producer, art.License));
                    engine.FitAnimationClipTranslation(new(body, pack, clipName, art.Joint,
                        fit.Speed is null ? new(fit.Scale) : new(fit.Scale, fit.Scale, 0F), new(0F, fit.Lift, 0F)));
                }
                Dictionary<string, string> boundClips = engine.ReadClips(body).ToArray().ToDictionary(c => c.Name, c => c.Id, StringComparer.Ordinal);
                SplatSkirtRig? skirtRig = enemy.Skirt is { } worn
                    ? SplatSkirtRig.Resolve(worn, engine.ReadJoints(body).Span, engine.SampleModelJointPose(new(body, "", 0, "", 0, 0, false, Unplaced)).Span)
                    : null;
                openedBody = (body, skirtRig, boundClips);
                bodies.Add(bodyKey, openedBody);
            }
            catch
            {
                body.Dispose();
                throw;
            }
        }

        Appearance? appearance = null;
        try
        {
            appearance = engine.CreateAnimatedMeshAppearance(new(openedBody.Body));
            Finish(appearance, enemy, tint);
            string Clip(string name, string fallback) => openedBody.Clips.TryGetValue(name, out string? found) ? found : openedBody.Clips[fallback];
            bool isCaster = enemy.Role is "caster" or "staff";
            string idleClip = Clip(isCaster ? "mage-idle" : "combat-idle", "idle");
            string moveKey = enemy.Skirt is not null ? "walk" : (!isCaster && openedBody.Clips.ContainsKey("run-sword") ? "run-sword" : "run");
            string moveClip = Clip(moveKey, "walk");
            float moveRate = enemy.Fits.TryGetValue(moveKey, out DaggerfallSplatClipFit? mfit) && mfit.Speed is > 0
                ? Math.Clamp(2.2F / mfit.Speed.Value, .8F, 1.4F)
                : 1F;
            string attackClip = Clip(isCaster ? "attack-melee" : "attack-sword", Clip("attack-melee", idleClip));
            string altAttackClip = Clip(isCaster ? "attack-melee-2" : "attack-sword-2", attackClip);
            string castClip = Clip("attack-cast", attackClip);
            string bowClip = Clip("attack-bow", attackClip);
            string hurtClip = Clip("hurt", idleClip);
            string deathClip = Clip("death", idleClip);
            EnemySplatFigure figure = new($"{id} ({enemy.Name}) [{activeVariant}]", appearance,
                idleClip, moveClip, attackClip, altAttackClip, castClip, bowClip, hurtClip, deathClip,
                tuning.CrossfadeSeconds, moveRate, openedBody.Skirt);
            characters.Add(cacheKey, (appearance, figure, tint));
            return figure;
        }
        catch
        {
            appearance?.Dispose();
            throw;
        }
    }

    private void Finish(Appearance appearance, DaggerfallSplatEnemy enemy, Color? tint)
    {
        Vector3 tintVec = tint is { } c ? new Vector3(c.R, c.G, c.B) : Vector3.One;
        ReadOnlyMemory<Color> palette = look.OwnPalette
            ? (tint is { } tc
                ? enemy.Palette.Select(p => new Color(p.R * tc.R, p.G * tc.G, p.B * tc.B, p.A)).ToArray()
                : enemy.Palette.ToArray())
            : ReadOnlyMemory<Color>.Empty;
        animation().SetSplatAppearance(new(appearance, tintVec, look.Grid, look.Coverage,
            palette, look.PoseRate, SplatLighting.Lit, tuning.LightFloor, look.Shadow,
            look.FadeNear, look.FadeFar, look.SurfaceBias));
    }

    private ContentReference Reference(DaggerfallArtFile file) =>
        content().ResolveReference(new ContentResolveRequest(file.Path, file.Sha256));
}
