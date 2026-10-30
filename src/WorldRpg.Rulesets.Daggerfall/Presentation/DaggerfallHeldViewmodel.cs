using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The 3D first-person weapon: the art's look published at rest where its stance seats it, moved through a strike by
/// an Engine tween built from its held motion, and the arms reaching its grip (Engine
/// <see cref="FirstPersonArms"/>). It draws only; the classic weapon's sprite playback stays the swing's clock, so
/// the hit is delivered and the attack released exactly as with the sprites. A strike is timed to that playback
/// (<see cref="Timing"/>): its windup spans the frames before the hit frame, its commit the frames from the hit to the
/// last, and its recovery the last frame, so the hit lands as the commit starts.
/// </summary>
/// <remarks>
/// <see cref="DaggerfallViewmodelStyle.HeldRetro"/> plays the same timeline sampled once per classic frame and held
/// between samples (the Engine's own sample of the timeline mid-frame, replayed as held segments), the classic look without
/// redrawing the weapon. The hand that does not hold the look is aimed below and behind the view, as Daggerfall draws
/// one weapon hand. The developer workbench holds a pose, plays a strike at any speed or paused at a moment, and shows
/// the look in the off hand, mirrored: its turn and grip are mirrored, not its model, which suits a look symmetric
/// across its blade.
/// </remarks>
internal sealed class DaggerfallHeldViewmodel : IDisposable
{
    // Where the hand that holds nothing is sent (the left's place): below the view and beside the body, so its arm is
    // never drawn in view.
    private static readonly Vector3 HandAway = new(-.25F, -.8F, .05F);

    private readonly IEngineContext engine;
    private readonly DaggerfallHeldArt art;
    private readonly bool retro;
    private readonly FirstPersonArms arms;
    private readonly RenderResource model;
    private readonly Appearance look;
    private (TweenHandle Handle, TweenStartRequest Request)? motion;
    // A pose the look is held in, drawn as it now is, while no motion plays; null shows rest.
    private HeldPose? holding;

    internal DaggerfallHeldViewmodel(IEngineContext engine, DaggerfallHeldArt art, DaggerfallViewmodelStyle style, ulong weaponObject, ulong armsObject)
    {
        this.engine = engine;
        this.art = art;
        retro = style == DaggerfallViewmodelStyle.HeldRetro;
        WeaponObject = weaponObject;
        using (ContentReference armsModel = art.Container.OpenReference(art.Document.Rig.Model))
            arms = new FirstPersonArms(engine, art.Document.Arms, art.Document.Rig, armsObject, armsModel);
        try
        {
            using ContentReference lookModel = art.Container.OpenReference(art.Document.Look.Model);
            model = engine.Animation.OpenAnimatedMeshFromContent(new(lookModel));
            try { look = engine.Animation.CreateAnimatedMeshAppearance(new(model)); }
            catch { model.Dispose(); throw; }
        }
        catch { arms.Dispose(); throw; }
    }

    /// <summary>The published object the weapon is drawn on (its motion's tween plays there).</summary>
    internal ulong WeaponObject { get; }

    /// <summary>Whether the off (left) hand holds the look, mirrored; play always holds it in the main hand.</summary>
    internal bool OffHand { get; set; }

    /// <summary>Whether this art has motion for a classic strike action.</summary>
    internal bool Moves(string action) => art.Document.Motion.Tracks.ContainsKey(action);

    /// <summary>
    /// A strike's phases timed to the classic playback of <paramref name="frames"/> frames of <paramref name="frame"/>
    /// seconds each with its hit on frame <paramref name="hitFrame"/>.
    /// </summary>
    internal static PhaseTiming Timing(float frame, int hitFrame, int frames) =>
        new(hitFrame * frame, Math.Max(1, frames - 1 - hitFrame) * frame, frame);

    /// <summary>
    /// Starts a strike's motion, timed to the classic playback of <paramref name="frames"/> frames of
    /// <paramref name="frameSeconds"/> each with its hit on frame <paramref name="hitFrame"/>; carries on from whatever
    /// the weapon shows.
    /// </summary>
    internal void Strike(string action, double frameSeconds, int hitFrame, int frames)
    {
        float frame = (float)frameSeconds;
        (Vector3, Quaternion) from = Offset();
        holding = null;
        Start(action, Timing(frame, hitFrame, frames), frame, 1, from);
    }

    /// <summary>
    /// Plays a strike's motion from rest over <paramref name="timing"/> (<paramref name="frame"/> is one classic frame,
    /// which the retro style samples), <paramref name="speed"/> times as fast, or shows it paused
    /// <paramref name="pausedAt"/> seconds into the action, as the Engine plays it.
    /// </summary>
    internal void Play(string action, PhaseTiming timing, float frame, float speed, float? pausedAt = null)
    {
        Rest();
        TweenHandle handle = Start(action, timing, frame, speed, null);
        if (pausedAt is not { } at) return;
        engine.Tween.Control(new TweenControlRequest(handle, TweenControl.Pause));
        engine.Tween.Control(new TweenControlRequest(handle, TweenControl.Seek, at / speed));
    }

    /// <summary>Ends any motion and holds the look in <paramref name="pose"/>, read as it now is at every draw.</summary>
    internal void Hold(HeldPose pose)
    {
        Rest();
        holding = pose;
    }

    /// <summary>Ends any motion or held pose; the weapon shows at rest.</summary>
    internal void Rest()
    {
        if (motion is { } playing) engine.Tween.Control(new TweenControlRequest(playing.Handle, TweenControl.Cancel));
        motion = null;
        holding = null;
    }

    /// <summary>Adds the weapon where it is held and the arms aimed at its grip where it is drawn now; the motion plays over it.</summary>
    internal void AppendFacts(List<AppearanceFact> facts, bool shown)
    {
        (Vector3 at, Quaternion turn) = Place(holding ?? HeldPose.Rest);
        facts.Add(new AppearanceFact(WeaponObject, false, 0, new Transform(at, turn, Vector3.One), look, shown, RenderLayer.Viewmodel));
        arms.Lean = Vector3.Zero;
        var (main, off) = Aims();
        arms.Aim(main, off);
        facts.Add(arms.Fact(shown));
    }

    /// <summary>Once the snapshot with <see cref="AppendFacts"/> is published: reaches the arms for the weapon's grip.</summary>
    internal void Reach()
    {
        var (main, off) = Aims();
        arms.Reach(main, off);
    }

    public void Dispose()
    {
        Rest();
        try { arms.Dispose(); }
        finally
        {
            look.Dispose();
            model.Dispose();
        }
    }

    private TweenHandle Start(string action, PhaseTiming timing, float frame, float speed, (Vector3, Quaternion)? from)
    {
        HeldMotion held = art.Document.Motion;
        TweenSegment[] segments = HeldTimelines.Action(held, held.Tracks[action], timing, Place, TweenEasingKind.QuadOut, speed: speed, from: from);
        if (retro) segments = Sampled(segments, frame / speed, timing.Duration / speed);
        TweenStartRequest request = new(WeaponObject, segments);
        TweenHandle handle = engine.Tween.Start(request).Tween;
        motion = (handle, request);
        return handle;
    }

    // Where a pose puts the weapon (camera space): its grip moved and turned as the pose says from where its stance
    // seats it, mirrored in the off hand. The model's origin is its grip, so the grip is where the model is placed.
    private (Vector3 At, Quaternion Turn) Place(HeldPose pose)
    {
        (Vector3 grip, Quaternion turn) = art.Stance.Seat(Vector3.Zero, Vector3.Zero);
        (Vector3 at, Quaternion placed) = (grip + pose.Position, Quaternion.Normalize(HeldFrames.Turn(pose.Rotation) * turn));
        return OffHand ? (HeldFrames.Mirror(at), HeldFrames.Mirror(placed)) : (at, placed);
    }

    // The offset the motion shows now (from where the weapon is published): what the Engine samples of the tween where
    // it has got to.
    private (Vector3 Move, Quaternion Turn) Offset()
    {
        if (motion is not { } playing) return (Vector3.Zero, Quaternion.Identity);
        TweenReadout readout = engine.Tween.Read(playing.Handle);
        if (readout.State == TweenState.Ended) return (Vector3.Zero, Quaternion.Identity);
        TweenSample sample = engine.Tween.Sample(playing.Request, readout.ElapsedSeconds);
        return (sample.Translation, sample.Rotation);
    }

    // The holding hand reaches the look's grip where it is drawn now; the other is sent away.
    private (HandAim Main, HandAim Off) Aims()
    {
        (Vector3 at, Quaternion turn) = Place(holding ?? HeldPose.Rest);
        (Vector3 move, Quaternion offset) = Offset();
        Quaternion now = Quaternion.Normalize(turn * offset);
        DaggerfallHeldLook held = art.Document.Look;
        Vector3 pole = art.Document.Arms.Pole;
        Vector3 grip = OffHand ? HeldFrames.Mirror(held.Grip) : held.Grip;
        HandAim holder = new(at + move + Vector3.Transform(grip, now), now, held.Hold, 0, OffHand ? HeldFrames.Mirror(pole) : pole);
        return OffHand
            ? (new HandAim(HeldFrames.Mirror(HandAway), Quaternion.Identity, null, 0, pole), holder)
            : (holder, new HandAim(HandAway, Quaternion.Identity, null, 0, HeldFrames.Mirror(pole)));
    }

    // The timeline as the classic look plays it: the Engine's sample of it in the middle of each frame, held for the
    // frame. The middle, not the start: the timeline starts at rest, and a sprite's first frame already shows the
    // swing begun.
    private TweenSegment[] Sampled(TweenSegment[] segments, float frame, float duration)
    {
        TweenStartRequest smooth = new(WeaponObject, segments);
        List<TweenSegment> held = [];
        for (int k = 0; k * frame < duration - frame * .5F; k++)
        {
            TweenSample sample = engine.Tween.Sample(smooth, (k + .5F) * frame);
            held.Add(TweenSegment.Move(sample.Translation, sample.Translation, frame, TweenEasingKind.Linear).At(k * frame));
            held.Add(TweenSegment.Rotate(sample.Rotation, sample.Rotation, frame, TweenEasingKind.Linear).At(k * frame));
        }
        return [.. held];
    }
}
