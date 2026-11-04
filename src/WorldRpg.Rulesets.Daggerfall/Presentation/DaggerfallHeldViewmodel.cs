using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The 3D first-person weapon of one drawing (its family, its item's look and its metal): clip-led arms (Engine <see cref="FirstPersonArms.Lead"/>) that loop the
/// family's idle clip at rest and play a strike's clip fitted to the strike's timing, with the look riding the main
/// hand where the clip holds it (<see cref="FirstPersonArms.Ride"/>). It draws only; the classic weapon's sprite
/// playback stays the swing's clock, so the hit is delivered and the attack released exactly as with the sprites. A
/// strike is timed to that playback (<see cref="Timing"/>): its windup spans the frames before the hit frame, its commit
/// the frames from the hit to the last, and its recovery the last frame, so the clip's strike mark lands with the hit.
/// The clips play on the world time the sprite playback reads (<see cref="Step"/>), so a held world holds them too.
/// </summary>
/// <remarks>
/// The clips are baked as their own camera saw them, with the eye at the model's origin, so the arms are placed with
/// their right upper arm at the definition's shoulder, where the clips' sits from the eye. The off hand rests below and
/// behind the view, as Daggerfall draws one weapon hand, unless the family holds its look in both hands or the strike
/// moves it. <see cref="DaggerfallViewmodelStyle.HeldRetro"/> samples the clips once per classic frame and holds the
/// sample for the frame, so the hit frame shows the strike mark, and draws the retro arms (their colour map alone,
/// small and unfiltered): the classic look without redrawing the weapon. The developer workbench plays a
/// strike at any speed or shows it paused at a moment.
/// </remarks>
internal sealed class DaggerfallHeldViewmodel : IDisposable
{
    // The longest the retro style's chain waits for the hand: about one classic frame.
    private const float RetroChainSeconds = .1F;

    // Where a hand that holds nothing is sent (the left's place; the right's mirrors it): below the view and beside the
    // body, so its arm is never drawn in view.
    private static readonly Vector3 HandAway = new(-.25F, -.8F, .05F);

    private readonly DaggerfallHeldArt art;
    private readonly DaggerfallHeldFamily family;
    private readonly DaggerfallHeldItem? item;
    private readonly bool retro;
    private readonly FirstPersonArms arms;
    private readonly RenderResource? model;
    private readonly Appearance look;
    // The model's chain, for an item that has one, and the world seconds it has not yet been stepped.
    private readonly HeldChain? chain;
    private float chainSeconds;
    // The clip frame last drawn, and whether the hand has moved to it since the chain last stepped.
    private ArmClipFrame drawn;
    private bool handMoved;
    // The strike playing: its action, timing, one classic frame, and how far into it (seconds); null at rest.
    private (string Action, PhaseTiming Timing, float Frame, float Seconds)? strike;
    // How fast the strike plays (the workbench plays it slower or holds it still); 1 in play.
    private float rate = 1;
    // World seconds at rest, which the idle loop plays on, and one classic idle frame.
    private double idle;
    private float idleFrame;
    // World seconds since the arms were last led.
    private float since;

    internal DaggerfallHeldViewmodel(IEngineContext engine, DaggerfallHeldArt art, DaggerfallHeldDrawing drawing, DaggerfallDyeTint? metal, DaggerfallViewmodelStyle style, ulong weaponObject, ulong armsObject)
    {
        this.art = art;
        Drawing = drawing;
        family = art.Document.Families[drawing.Family];
        item = drawing.Item is int template ? art.Document.Items[template] : null;
        retro = style == DaggerfallViewmodelStyle.HeldRetro;
        WeaponObject = weaponObject;
        using (ContentReference armsModel = art.Container.OpenReference(retro ? art.Document.RetroModel : art.Document.Rig.Model))
            arms = new FirstPersonArms(engine, art.Document.Arms, art.Document.Rig, armsObject, armsModel);
        try
        {
            string[] missing = [.. new[] { family.Idle.Clip }.Concat(family.Strikes.Values.Select(clip => clip.Clip)).Where(clip => !arms.Clips.Contains(clip))];
            if (missing.Length > 0)
                throw new InvalidDataException($"{DaggerfallHeldArt.DocumentPath}: families.{drawing.Family} names clips the arms lack: {string.Join(", ", missing)}. Rebuild {DaggerfallHeldArt.ContainerName} with scripts/fp-arms/build.sh.");
            if ((item?.Model ?? family.Look.Model) is { } path)
            {
                using ContentReference lookModel = art.Container.OpenReference(path);
                model = engine.Animation.OpenAnimatedMeshFromContent(new(lookModel));
                try
                {
                    look = engine.Animation.CreateAnimatedMeshAppearance(new(model));
                    if (metal is not null && art.MetalSlot(path) is uint slot)
                        engine.Animation.UpdateAnimatedMeshMaterialFactors(new(look, new[] { MeshMaterialFactors.Tint(slot, DaggerfallHeldArt.MetalColor(metal)) }));
                    if (item?.Chain is { } hanging) chain = new HeldChain(engine, hanging, model, look, weaponObject);
                }
                catch { look?.Dispose(); model.Dispose(); throw; }
            }
            else
            {
                // A look with no model yet keeps its object, never shown, so its place is still published.
                look = engine.Graphics.CreatePrimitive(new(PrimitiveGeometry.Cube, false, new Color(1F, 1F, 1F, 1F)));
            }
        }
        catch { arms.Dispose(); throw; }
    }

    /// <summary>What this weapon is drawn as: its family, its item's look and its metal.</summary>
    internal DaggerfallHeldDrawing Drawing { get; }

    /// <summary>The published object the weapon's look is drawn on.</summary>
    internal ulong WeaponObject { get; }

    /// <summary>What the arms are sampling: the clip, its time and phase, and any clip fading out under it.</summary>
    internal ArmClipReading Clip => arms.Clip;

    /// <summary>Whether the family has a clip for a classic strike action.</summary>
    internal bool Moves(string action) => family.Strikes.ContainsKey(action);

    /// <summary>
    /// A strike's phases timed to the classic playback of <paramref name="frames"/> frames of <paramref name="frame"/>
    /// seconds each with its hit on frame <paramref name="hitFrame"/>.
    /// </summary>
    internal static PhaseTiming Timing(float frame, int hitFrame, int frames) =>
        new(hitFrame * frame, Math.Max(1, frames - 1 - hitFrame) * frame, frame);

    /// <summary>
    /// Starts a strike's clip, timed to the classic playback of <paramref name="frames"/> frames of
    /// <paramref name="frameSeconds"/> each with its hit on frame <paramref name="hitFrame"/>; it fades in over what the
    /// arms play now.
    /// </summary>
    internal void Strike(string action, double frameSeconds, int hitFrame, int frames)
    {
        float frame = (float)frameSeconds;
        Play(action, Timing(frame, hitFrame, frames), frame, 1);
    }

    /// <summary>
    /// Plays a strike's clip over <paramref name="timing"/> (<paramref name="frame"/> is one classic frame, which the
    /// retro style samples) <paramref name="speed"/> times as fast, or holds it <paramref name="pausedAt"/> seconds in.
    /// </summary>
    internal void Play(string action, PhaseTiming timing, float frame, float speed, float? pausedAt = null)
    {
        strike = (action, timing, frame, pausedAt ?? 0);
        rate = pausedAt is null ? speed : 0;
    }

    /// <summary>Ends any strike: the arms loop the idle clip, sampled once per <paramref name="frame"/> seconds by the retro style.</summary>
    internal void Rest(float frame)
    {
        strike = null;
        rate = 1;
        idleFrame = frame;
    }

    /// <summary>World seconds admitted, on which the clips play and fade.</summary>
    internal void Step(float seconds)
    {
        since += seconds;
        if (strike is { } playing) strike = playing with { Seconds = playing.Seconds + seconds * rate };
        else idle += seconds;
        StepChain(seconds);
    }

    /// <summary>Leads the arms by the clip playing now, and adds the look where the main hand holds it and the arms.</summary>
    internal void AppendFacts(List<AppearanceFact> facts, bool shown)
    {
        ArmClipFrame now = Frame();
        handMoved |= now != drawn;
        drawn = now;
        arms.Lead(now, since);
        since = 0;
        (Vector3 grip, Quaternion turn) = arms.Ride(MainAim());
        // The model's origin is its own grip; the hand holds the look's grip point.
        Vector3 at = grip - Vector3.Transform(item?.Grip ?? family.Look.Grip, turn);
        facts.Add(new AppearanceFact(WeaponObject, false, 0, new Transform(at, turn, Vector3.One), look, shown && model is not null, RenderLayer.Viewmodel));
        facts.Add(arms.Fact(shown));
    }

    /// <summary>Once the snapshot with <see cref="AppendFacts"/> is published: poses the arms by their clip and aims.</summary>
    internal void Reach()
    {
        arms.Reach(MainAim(), OffAim());
        chain?.Published();
    }

    public void Dispose()
    {
        try
        {
            chain?.Dispose();
            arms.Dispose();
        }
        finally
        {
            look.Dispose();
            model?.Dispose();
        }
    }

    // Swings the chain. The retro style moves the hand once per classic frame, which would tear a chain stepped between
    // those jumps, so its chain moves with the hand: one step over the seconds since it last moved, through which the
    // Engine carries the jump. A hand that stays still must not hold the chain up, so it also moves at the classic pace.
    private void StepChain(float seconds)
    {
        if (chain is null) return;
        chainSeconds += seconds;
        if (retro && !handMoved && chainSeconds < RetroChainSeconds)
        {
            chain.Step(0);
            return;
        }
        chain.Step(chainSeconds);
        chainSeconds = 0;
        handMoved = false;
    }

    // The strike's clip where it has got to, or the idle loop at rest and once the strike is over.
    private ArmClipFrame Frame()
    {
        if (strike is { } playing && playing.Seconds <= playing.Timing.Duration)
            return family.Strikes[playing.Action].At(playing.Timing, Sampled(playing.Seconds, playing.Frame, playing.Timing.Duration));
        double seconds = retro && idleFrame > 0 ? Math.Floor(idle / idleFrame) * idleFrame : idle;
        return new(family.Idle.Clip, (float)(seconds / family.Idle.Seconds % 1));
    }

    // The moment the clip shows: as it is, or for the retro style the start of the classic frame it is in, so the hit
    // frame shows the strike mark; the first frame shows its middle, as a sprite's first frame already shows the swing
    // begun.
    private float Sampled(float seconds, float frame, float duration)
    {
        if (!retro || frame <= 0) return seconds;
        float start = MathF.Floor(seconds / frame) * frame;
        return MathF.Min(start > 0 ? start : frame / 2, duration);
    }

    // The main hand rides its clip, holding the look as the family's look says; before the clip has faded in it rises
    // from where an empty hand rests.
    private HandAim MainAim() =>
        new(HeldFrames.Mirror(HandAway), Quaternion.Identity, item?.Hold ?? family.Look.Hold, 0, art.Document.Arms.Pole);

    // The off hand rests out of view, unless the family's look is held in both hands or the strike moves it: then it
    // plays the clip (reach 0).
    private HandAim OffAim() =>
        new(HandAway, Quaternion.Identity, null, 0, HeldFrames.Mirror(art.Document.Arms.Pole), Reach: family.MovesLeft(strike?.Action) ? 0 : 1);
}
