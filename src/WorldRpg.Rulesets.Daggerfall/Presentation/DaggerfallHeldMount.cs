using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// What the player rides, drawn around the eye in the held styles: the horse, and for a cart the cart with the horse in
/// front of it. The view rides with the mount, so it is drawn on the viewmodel layer and turned back against the view's
/// pitch, which leaves it level: looking down shows the saddle and flanks. It turns about the eye by as far as its heading
/// lies from the view. Its gait follows the player's speed.
/// </summary>
internal sealed class DaggerfallHeldMount : IDisposable
{
    // How long the gaits take to fade into each other, in seconds.
    private const float GaitFade = .25F;

    private readonly IEngineContext engine;
    private readonly DaggerfallHeldMountArt art;
    private readonly Vector3 seat;
    private readonly RenderResource horseModel;
    private readonly Appearance horse;
    private readonly AnimationInstance horsePose;
    private readonly RenderResource? cartModel;
    private readonly Appearance? cart;
    private readonly ulong horseObject, cartObject;
    private readonly AnimationLayerSet layers = new();
    private readonly string[] clips;
    private bool playing;
    // How far through the gait's loop, which every gait shares so they fade in step; and each gait's share now.
    private float phase;
    private readonly float[] shares;

    internal DaggerfallHeldMount(IEngineContext engine, DaggerfallHeldArt art, DaggerfallHeldMountArt mount, bool cart, bool retro, ulong horseObject, ulong cartObject)
    {
        this.engine = engine;
        this.art = mount;
        this.horseObject = horseObject;
        this.cartObject = cartObject;
        seat = cart ? mount.CartSeat : mount.Seat;
        DaggerfallHeldArmSet drawn = cart ? mount.Harnessed : mount.Horse;
        using (ContentReference content = art.Container.OpenReference(retro ? drawn.RetroModel : drawn.Model))
            horseModel = engine.Animation.OpenAnimatedMeshFromContent(new(content));
        horse = engine.Animation.CreateAnimatedMeshAppearance(new(horseModel));
        horsePose = engine.Animation.CreateInstance(new(horse, horseObject));
        clips = [.. engine.Animation.ReadClips(horseModel).ToArray().Select(clip => clip.Id)];
        shares = new float[mount.Gaits.Count];
        shares[0] = 1;
        if (!cart) return;
        using ContentReference cartContent = art.Container.OpenReference(retro ? mount.Cart.RetroModel : mount.Cart.Model);
        cartModel = engine.Animation.OpenAnimatedMeshFromContent(new(cartContent));
        this.cart = engine.Animation.CreateAnimatedMeshAppearance(new(cartModel));
    }

    /// <summary>How far the eye sits above the ground now, in metres above where it stands on foot, with the gait's bob.</summary>
    internal float EyeLift(float standing) => seat.Y - standing + Bob;

    // The rider rises and falls twice a loop, by the gaits' bob as they share the moment.
    private float Bob
    {
        get
        {
            float amount = 0;
            for (int gait = 0; gait < shares.Length; gait++) amount += shares[gait] * art.Gaits[gait].Bob;
            return amount * MathF.Sin(phase * 4 * MathF.PI);
        }
    }

    /// <summary>World seconds admitted at <paramref name="speed"/> metres a second: the gait that speed calls for fades in, and the loop plays on.</summary>
    internal void Step(float seconds, float speed)
    {
        int wanted = 0;
        for (int gait = 0; gait < art.Gaits.Count; gait++)
            if (speed >= art.Gaits[gait].From) wanted = gait;
        float fade = seconds / GaitFade;
        for (int gait = 0; gait < shares.Length; gait++)
            shares[gait] = gait == wanted ? MathF.Min(1, shares[gait] + fade) : MathF.Max(0, shares[gait] - fade);
        DaggerfallHeldGait now = art.Gaits[wanted];
        // A gait plays at the speed it was made for, faster or slower as the mount is.
        float rate = now.Speed > 0 ? MathF.Max(speed / now.Speed, .5F) : 1;
        phase = (phase + seconds * rate / now.Seconds) % 1;
    }

    /// <summary>The turn that keeps the mount level whatever the view's <paramref name="pitch"/>, its heading <paramref name="turn"/> right of the view.</summary>
    internal Quaternion Level(float pitch, float turn) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, -pitch) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -turn);

    /// <summary>Adds the mount, level whatever the view's <paramref name="pitch"/> and <paramref name="turn"/> right of it, with the eye on its seat.</summary>
    internal void AppendFacts(List<AppearanceFact> facts, float pitch, float turn, bool shown)
    {
        Quaternion level = Level(pitch, turn);
        Vector3 at = Vector3.Transform(-seat, level);
        if (cart is null)
        {
            facts.Add(new AppearanceFact(horseObject, false, 0, new Transform(at, level, Vector3.One), horse, shown, RenderLayer.Viewmodel, ShadowCasting.None));
            return;
        }
        facts.Add(new AppearanceFact(cartObject, false, 0, new Transform(at, level, Vector3.One), cart, shown, RenderLayer.Viewmodel, ShadowCasting.None));
        facts.Add(new AppearanceFact(horseObject, false, 0, new Transform(at + Vector3.Transform(art.CartHorse, level), level, Vector3.One), horse, shown, RenderLayer.Viewmodel, ShadowCasting.None));
    }

    /// <summary>Once the snapshot is published: poses the horse in its gaits.</summary>
    internal void Posed()
    {
        if (!playing)
        {
            engine.Animation.SetPlayback(new(horsePose, AnimationPlaybackKind.Play, art.Gaits[0].Clip, AnimationLoopMode.Repeat, 1, 1, true, 0, true, 0));
            playing = true;
        }
        layers.Clear();
        for (int gait = 0; gait < shares.Length; gait++)
            if (shares[gait] > 0) layers.Override((uint)Array.IndexOf(clips, art.Gaits[gait].Clip), phase * art.Gaits[gait].Seconds, shares[gait]);
        engine.Animation.SetLayers(layers.ToRequest(horsePose));
    }

    public void Dispose()
    {
        horsePose.Dispose();
        horse.Dispose();
        horseModel.Dispose();
        cart?.Dispose();
        cartModel?.Dispose();
    }
}
