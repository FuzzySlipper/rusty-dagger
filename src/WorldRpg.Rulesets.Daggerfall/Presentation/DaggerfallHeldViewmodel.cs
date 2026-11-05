using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The 3D first-person weapon of one drawing (its family, its item's look and its metal): clip-led arms (Engine <see cref="FirstPersonArms.Lead"/>) that loop the
/// family's idle clip at rest and play an attack's clip fitted to the rules' timing, with the look riding the main
/// hand where the clip holds it (<see cref="FirstPersonArms.Ride"/>). It draws only: the rules' attack clock delivers the
/// hit, and the clip's strike mark lands with it as the windup ends. The clips play on world time
/// (<see cref="Step"/>), so a held world holds them too.
/// </summary>
/// <remarks>
/// The clips are baked as their own camera saw them, with the eye at the model's origin, so the arms are placed with
/// their right upper arm at the definition's shoulder, where the clips' sits from the eye. The off hand rests below and
/// behind the view, as Daggerfall draws one weapon hand, unless the family holds its look in both hands or the strike
/// moves it. <see cref="DaggerfallViewmodelStyle.HeldRetro"/> samples the clips at a sprite's cadence and holds the
/// sample for the frame, so the hit shows the strike mark, and draws the set's retro arms (their colour map alone,
/// small and unfiltered): the classic look without redrawing the weapon. The developer workbench plays a
/// strike at any speed or shows it paused at a moment.
/// </remarks>
internal sealed class DaggerfallHeldViewmodel : IDisposable
{
    // The longest the retro style's chain waits for the hand: about one classic frame.
    private const float RetroChainSeconds = .1F;

    // How long the off hand takes to leave its shield carry for a cast's clip, and to return.
    private const float CarryEase = .12F;

    // Where a hand that holds nothing is sent (the left's place; the right's mirrors it): below the view and beside the
    // body, so its arm is never drawn in view.
    private static readonly Vector3 HandAway = new(-.25F, -.8F, .05F);

    private readonly DaggerfallHeldArt art;
    private readonly DaggerfallHeldFamily family;
    private readonly DaggerfallHeldItem? item;
    private readonly bool retro;
    private readonly IEngineContext engine;
    private readonly FirstPersonArms arms;
    // The arms' model for sampling where a clip puts the off hand, which the arms don't publish.
    private readonly RenderResource armsPose;
    private readonly RenderResource? model;
    // The shield on the off arm and its model: its appearance, drawn on the off hand's joint by the Engine, and where it sits.
    private readonly RenderResource? shieldModel;
    private readonly Appearance? shieldLook;
    private readonly DaggerfallHeldSeat? shieldSeat;
    private bool shieldAttached;
    // How this drawing raises its guard, and how far into raising, holding or lowering it (seconds); null with no guard up.
    private readonly DaggerfallHeldGuard? guard;
    private float? guarding;
    private bool lowering;
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
    // Whether these arms have yet to be led since taking over from others, which they don't fade in from.
    private bool takingOver;
    // World seconds since the arms were last led.
    private float since;
    // Whether the charged power attack is held, and for how long, which a looping hold plays on.
    private bool holding;
    private float held;
    // The shortest phase a clip is fitted to: a power attack's charge is all windup.
    private const float MinimumPhase = .001F;

    internal DaggerfallHeldViewmodel(IEngineContext engine, DaggerfallHeldArt art, DaggerfallHeldDrawing drawing, DaggerfallDyeTint? metal, DaggerfallDyeTint? shieldMetal, DaggerfallViewmodelStyle style, ulong weaponObject, ulong armsObject, ulong shieldObject)
    {
        this.art = art;
        Drawing = drawing;
        family = art.Document.Families[drawing.Family];
        item = drawing.Item is int template ? art.Document.Items[template] : null;
        retro = style == DaggerfallViewmodelStyle.HeldRetro;
        WeaponObject = weaponObject;
        this.engine = engine;
        DaggerfallHeldArmSet armSet = art.ArmSet(drawing.Arms);
        using (ContentReference armsModel = art.Container.OpenReference(retro ? armSet.RetroModel : armSet.Model))
        {
            arms = new FirstPersonArms(engine, art.Document.Arms, art.Document.Rig, armsObject, armsModel);
            armsPose = engine.Animation.OpenAnimatedMeshFromContent(new(armsModel));
        }
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
        catch { armsPose.Dispose(); arms.Dispose(); throw; }
        ShieldObject = shieldObject;
        guard = family.Guard;
        if (drawing.Shield is int shield && art.Document.Shield is { } shields)
        {
            guard = shields.Guard;
            DaggerfallHeldShieldItem carried = shields.Items[shield];
            shieldSeat = carried.Seat ?? shields.Seat;
            using ContentReference shieldContent = art.Container.OpenReference(carried.Model);
            shieldModel = engine.Animation.OpenAnimatedMeshFromContent(new(shieldContent));
            shieldLook = engine.Animation.CreateAnimatedMeshAppearance(new(shieldModel));
            if (shieldMetal is not null && art.MetalSlot(carried.Model) is uint slot)
                engine.Animation.UpdateAnimatedMeshMaterialFactors(new(shieldLook, new[] { MeshMaterialFactors.Tint(slot, DaggerfallHeldArt.MetalColor(shieldMetal)) }));
        }
    }

    /// <summary>What this weapon is drawn as: its family, its item's look and its metal.</summary>
    internal DaggerfallHeldDrawing Drawing { get; }

    /// <summary>The published object a shield is drawn on.</summary>
    internal ulong ShieldObject { get; }

    /// <summary>The published object the weapon's look is drawn on.</summary>
    internal ulong WeaponObject { get; }

    /// <summary>What the arms are sampling: the clip, its time and phase, and any clip fading out under it.</summary>
    internal ArmClipReading Clip => arms.Clip;

    /// <summary>The action whose clip the arms play now; null while they rest.</summary>
    internal string? Playing => strike?.Action;

    /// <summary>Whether the arms are in a strike, a held charge or a guard, or fading back from one, which another set of arms would cut short.</summary>
    internal bool Moving => strike is not null || holding || guarding is not null || arms.Clip.From is not null;

    /// <summary>World seconds into the idle loop.</summary>
    internal double IdleSeconds => idle;

    /// <summary>Takes over from <paramref name="earlier"/> at rest: the same moment of the idle loop, led by it at once, so the arms hold their pose.</summary>
    internal void TakeOver(DaggerfallHeldViewmodel earlier)
    {
        idle = earlier.idle;
        takingOver = true;
    }

    /// <summary>Where the off hand's palm is drawn (camera space) while a cast has yet to let its spell go; null otherwise.</summary>
    internal Vector3? CastPalm()
    {
        if (strike is not { Action: DaggerfallAttackIds.Cast } cast || cast.Seconds >= cast.Timing.Windup + cast.Timing.Commit) return null;
        HandRig hand = arms.Hand(true);
        ReadOnlySpan<JointPose> joints = engine.Animation.SampleModelJointPose(
            AnimationModelPoseSampleRequest.OfClip(armsPose, drawn.Clip, drawn.Time, new(Vector3.Zero, Quaternion.Identity, Vector3.One))).Span;
        Vector3 palm = (joints[(int)hand.Wrist].Model.Translation + joints[(int)hand.Middle[0]].Model.Translation) / 2;
        Transform placed = arms.Fact(true).Transform;
        return placed.Translation + Vector3.Transform(palm * placed.Scale, placed.Rotation);
    }

    /// <summary>Whether the family has a clip for a classic strike action.</summary>
    internal bool Moves(string action) => action == DaggerfallAttackIds.PowerHold ? family.PowerHold is not null : family.Strikes.ContainsKey(action);

    /// <summary>
    /// Starts an attack's clip to the rules' <paramref name="timing"/>; it fades in over what the arms play now, which is
    /// also how a chained attack cuts the last one's recovery. <paramref name="hitFrame"/> sets the retro style's cadence.
    /// </summary>
    internal void Attack(string action, DaggerfallAttackTiming timing, int hitFrame) =>
        Play(action, new PhaseTiming((float)timing.Windup, MathF.Max((float)timing.Commit, MinimumPhase), MathF.Max((float)timing.Recovery, MinimumPhase)),
            (float)(timing.Windup / hitFrame), 1);

    /// <summary>Raises the guard or lowers it, for a drawing that has one.</summary>
    internal void Guard(bool raised)
    {
        if (guard is null || raised == (guarding is not null && !lowering)) return;
        guarding = 0;
        lowering = !raised;
    }

    /// <summary>Keeps the charged power attack where the family's <c>powerHold</c> says, until the release plays.</summary>
    internal void Hold()
    {
        strike = null;
        rate = 1;
        held = 0;
        holding = true;
    }

    /// <summary>
    /// Plays a strike's clip over <paramref name="timing"/> (<paramref name="frame"/> is one classic frame, which the
    /// retro style samples) <paramref name="speed"/> times as fast, or holds it <paramref name="pausedAt"/> seconds in.
    /// </summary>
    internal void Play(string action, PhaseTiming timing, float frame, float speed, float? pausedAt = null)
    {
        holding = false;
        guarding = null;
        strike = (action, timing, frame, pausedAt ?? 0);
        rate = pausedAt is null ? speed : 0;
    }

    /// <summary>Ends any strike: the arms loop the idle clip, sampled once per <paramref name="frame"/> seconds by the retro style.</summary>
    internal void Rest(float frame)
    {
        holding = false;
        strike = null;
        rate = 1;
        idleFrame = frame;
    }

    /// <summary>World seconds admitted, on which the clips play and fade.</summary>
    internal void Step(float seconds)
    {
        since += seconds;
        if (holding) held += seconds;
        if (guarding is { } raised)
            guarding = lowering && raised + seconds >= guard!.Lower.Seconds ? null : raised + seconds;
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
        arms.Lead(takingOver ? now with { Fade = 0 } : now, since);
        takingOver = false;
        since = 0;
        (Vector3 grip, Quaternion turn) = arms.Ride(MainAim());
        // The model's origin is its own grip; the hand holds the look's grip point.
        Vector3 at = grip - Vector3.Transform(item?.Grip ?? family.Look.Grip, turn);
        facts.Add(new AppearanceFact(WeaponObject, false, 0, new Transform(at, turn, Vector3.One), look, shown && model is not null, RenderLayer.Viewmodel));
        facts.Add(arms.Fact(shown));
        if (shieldLook is not null) facts.Add(ShieldFact(shown));
    }

    // The shield's fact: a child of the arms, its transform local to the joint the Engine binds it to.
    private AppearanceFact ShieldFact(bool shown) => new(ShieldObject, true, arms.Object,
        new Transform(shieldSeat!.At, HeldFrames.Turn(shieldSeat.Turn), new Vector3(shieldSeat.Scale)), shieldLook!, shown, RenderLayer.Viewmodel);

    /// <summary>Once the snapshot with <see cref="AppendFacts"/> is published: poses the arms by their clip and aims.</summary>
    internal void Reach()
    {
        arms.Reach(MainAim(), OffAim());
        chain?.Published();
        if (shieldLook is null || shieldAttached) return;
        // Once: a later snapshot keeps the binding, and the renderer moves the shield with the hand from then on.
        engine.Graphics.PublishChanges(new(new[] { ShieldFact(true) }, ReadOnlyMemory<ulong>.Empty, new MeshJointAttachment[] { new(ShieldObject, art.Document.Rig.Off.Hand) }));
        shieldAttached = true;
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
            shieldLook?.Dispose();
            shieldModel?.Dispose();
            armsPose.Dispose();
        }
    }

    // Swings the chain. The retro style moves the hand once per classic frame, which would tear a chain stepped between
    // those jumps, so its chain moves with the hand: one step over the seconds since it last moved, through which the
    // Engine carries the jump. A hand that stays still must not hold the chain up, so it also moves at the classic pace.
    private void StepChain(float seconds)
    {
        if (chain is null) return;
        chain.Swing(strike is { } playing && playing.Seconds <= playing.Timing.Duration);
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
        if (holding && family.PowerHold is { } hold)
            return new(hold.Clip, hold.At ?? held / hold.Seconds!.Value % 1);
        if (guarding is { } up && guard is { } stance)
            return lowering ? new(stance.Lower.Clip, up / stance.Lower.Seconds)
                : up < stance.Raise.Seconds ? new(stance.Raise.Clip, up / stance.Raise.Seconds)
                : new(stance.Hold.Clip, (up - stance.Raise.Seconds) / stance.Hold.Seconds % 1);
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

    // The off hand carries a shield in view. Without one it rests out of view, unless the family's look is held in both
    // hands or the strike moves it: then it plays the clip (reach 0).
    private HandAim OffAim() => Drawing.Shield is not null && art.Document.Shield is { Carry: var carry }
        ? new(carry.Grip, Quaternion.Identity, carry.Hold, 0, HeldFrames.Mirror(art.Document.Arms.Pole), HoldMirrored: false, Reach: CarryReach())
        : new(HandAway, Quaternion.Identity, null, 0, HeldFrames.Mirror(art.Document.Arms.Pole), Reach: family.MovesLeft(strike?.Action) ? 0 : 1);

    // How far the off hand keeps to its carry: fully, but for the guard it raises and the cast it throws, which it plays
    // from their clips, easing out and back so the shield doesn't jump.
    private float CarryReach()
    {
        if (guarding is { } up) return lowering ? Math.Clamp(up / guard!.Lower.Seconds, 0, 1) : 1 - Math.Clamp(up / guard!.Raise.Seconds, 0, 1);
        if (strike is not { Action: DaggerfallAttackIds.Cast } cast) return 1;
        return Math.Clamp(MathF.Max(1 - cast.Seconds / CarryEase, 1 - (cast.Timing.Duration - cast.Seconds) / CarryEase), 0, 1);
    }
}
