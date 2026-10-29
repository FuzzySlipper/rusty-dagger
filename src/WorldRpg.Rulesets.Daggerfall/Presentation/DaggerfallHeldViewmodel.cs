using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The 3D first-person weapon: the art's look published at rest where its stance seats it, moved through a strike by
/// an Engine tween built from its held motion, and the arms reaching its grip (Engine
/// <see cref="FirstPersonArms"/>). It draws only; the classic weapon's sprite playback stays the swing's clock, so
/// the hit is delivered and the attack released exactly as with the sprites. A strike is timed to that playback: its
/// windup spans the frames before the hit frame, its commit the frames from the hit to the last, and its recovery the
/// last frame, so the hit lands as the commit starts.
/// </summary>
/// <remarks>
/// <see cref="DaggerfallViewmodelStyle.HeldRetro"/> plays the same timeline sampled once per classic frame and held
/// between samples (the Engine's own sample of the timeline mid-frame, replayed as held segments), the classic look without
/// redrawing the weapon. The off arm is aimed below and behind the view, as Daggerfall draws one weapon hand.
/// </remarks>
internal sealed class DaggerfallHeldViewmodel : IDisposable
{
    // Where the off hand is sent: below the view and beside the body, so its arm is never drawn in view.
    private static readonly Vector3 OffHandAway = new(-.25F, -.8F, .05F);

    private readonly IEngineContext engine;
    private readonly DaggerfallHeldArt art;
    private readonly bool retro;
    private readonly FirstPersonArms arms;
    private readonly RenderResource model;
    private readonly Appearance look;
    private readonly (Vector3 Grip, Quaternion Turn) seat;
    private (TweenHandle Handle, TweenStartRequest Request)? motion;

    internal DaggerfallHeldViewmodel(IEngineContext engine, DaggerfallHeldArt art, DaggerfallViewmodelStyle style, ulong weaponObject, ulong armsObject)
    {
        this.engine = engine;
        this.art = art;
        retro = style == DaggerfallViewmodelStyle.HeldRetro;
        WeaponObject = weaponObject;
        seat = art.Stance.Seat(Vector3.Zero, Vector3.Zero);
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

    /// <summary>Whether this art has motion for a classic strike action.</summary>
    internal bool Moves(string action) => art.Document.Motion.Tracks.ContainsKey(action);

    /// <summary>
    /// Starts a strike's motion, timed to the classic playback of <paramref name="frames"/> frames of
    /// <paramref name="frameSeconds"/> each with its hit on frame <paramref name="hitFrame"/>; carries on from whatever
    /// the weapon shows.
    /// </summary>
    internal void Strike(string action, double frameSeconds, int hitFrame, int frames)
    {
        float frame = (float)frameSeconds;
        PhaseTiming timing = new(hitFrame * frame, Math.Max(1, frames - 1 - hitFrame) * frame, frame);
        HeldMotion held = art.Document.Motion;
        TweenSegment[] segments = HeldTimelines.Action(held, held.Tracks[action], timing, Place, TweenEasingKind.QuadOut, from: Offset());
        if (retro) segments = Sampled(segments, frame, timing.Duration);
        TweenStartRequest request = new(WeaponObject, segments);
        motion = (engine.Tween.Start(request).Tween, request);
    }

    /// <summary>Ends any motion; the weapon shows at rest.</summary>
    internal void Rest()
    {
        if (motion is { } playing) engine.Tween.Control(new TweenControlRequest(playing.Handle, TweenControl.Cancel));
        motion = null;
    }

    /// <summary>Adds the weapon at rest and the arms aimed at its grip where it is drawn now; the motion plays over it.</summary>
    internal void AppendFacts(List<AppearanceFact> facts, bool shown)
    {
        (Vector3 at, Quaternion turn) = Place(HeldPose.Rest);
        facts.Add(new AppearanceFact(WeaponObject, false, 0, new Transform(at, turn, Vector3.One), look, shown, RenderLayer.Viewmodel));
        arms.Lean = Vector3.Zero;
        arms.Aim(MainAim(), OffAim());
        facts.Add(arms.Fact(shown));
    }

    /// <summary>Once the snapshot with <see cref="AppendFacts"/> is published: reaches the arms for the weapon's grip.</summary>
    internal void Reach() => arms.Reach(MainAim(), OffAim());

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

    // Where a pose puts the weapon (camera space): its grip moved and turned as the pose says from where its stance
    // seats it. The model's origin is its grip, so the grip is where the model is placed.
    private (Vector3 At, Quaternion Turn) Place(HeldPose pose) =>
        (seat.Grip + pose.Position, Quaternion.Normalize(HeldFrames.Turn(pose.Rotation) * seat.Turn));

    // The offset the motion shows now (from rest): what the Engine samples of the tween where it has got to.
    private (Vector3 Move, Quaternion Turn) Offset()
    {
        if (motion is not { } playing) return (Vector3.Zero, Quaternion.Identity);
        TweenReadout readout = engine.Tween.Read(playing.Handle);
        if (readout.State == TweenState.Ended) return (Vector3.Zero, Quaternion.Identity);
        TweenSample sample = engine.Tween.Sample(playing.Request, readout.ElapsedSeconds);
        return (sample.Translation, sample.Rotation);
    }

    private HandAim MainAim()
    {
        (Vector3 at, Quaternion turn) = Place(HeldPose.Rest);
        (Vector3 move, Quaternion offset) = Offset();
        Quaternion now = Quaternion.Normalize(turn * offset);
        DaggerfallHeldLook held = art.Document.Look;
        return new HandAim(at + move + Vector3.Transform(held.Grip, now), now, held.Hold, 0, art.Document.Arms.Pole);
    }

    private HandAim OffAim() => new(OffHandAway, Quaternion.Identity, null, 0, HeldFrames.Mirror(art.Document.Arms.Pole));

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
