using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.FirstPerson;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The developer first-person workbench over the 3D first-person art: the motion viewer shows the look one classic
/// weapon visual draws (its look) in the main or off hand with one of its strikes, holds any of the strike's keys
/// exactly, plays the strike at any speed or shows it paused at any moment, all through the held viewmodel the game
/// draws (<see cref="DaggerfallSiteAppearance.ViewHeld"/>), and never through the click attack's random strike. The
/// Engine tunables page (<see cref="Page"/>) adds choosers for the look, strike and hand, the strike's keys as its
/// timeline, and the values the viewmodel draws by: the look's stance and hold, the strike's key timing and curves, the
/// held key's pose, the arms' reach and the rig's palm. Each value applies to the loaded art at once and is saved to
/// the held definition (<see cref="DaggerfallHeldArt.DocumentPath"/>) by the page's Save.
/// </summary>
/// <remarks>
/// A strike is timed as play times it: the visual's classic action at its authored frame rate with the hit on the
/// melee hit frame (<see cref="DaggerfallHeldViewmodel.Timing"/>). Play scales that rate by the player's speed, so a
/// hastened swing is shorter than the viewer's. Adapted from rusty-riders' <c>HeldMotionDebugCommands</c> and
/// <c>HeldWorkbench</c>.
/// </remarks>
/// <param name="art">The art the composition resolved over; null when no first-person container is installed.</param>
/// <param name="style">The viewmodel style the tuning asks for.</param>
/// <param name="appearance">The current site's appearance, which draws the viewmodel.</param>
/// <param name="publish">Republishes the presentation, so a change shows at once.</param>
internal sealed class DaggerfallHeldWorkbench(DaggerfallHeldArt? art, DaggerfallViewmodelStyle style, Func<DaggerfallSiteAppearance> appearance, Action publish)
{
    internal const string Title = "First-person workbench";
    private static readonly string[] Hands = ["main", "off"];

    private string? look;
    private string? strike;
    private bool offHand;
    private int key = -1;

    /// <summary>The look shown (a classic weapon visual the art draws), the strike whose keys it steps, the hand, and the held key (-1: none).</summary>
    internal (string? Look, string? Strike, bool OffHand, int Key) Viewing => (look, strike, offHand, key);

    /// <summary>Why nothing can be viewed, or null when the art is drawn.</summary>
    internal string? Unavailable => art is null
        ? $"No first-person art: {DaggerfallHeldArt.ContainerName} is not installed beside the product. Build it with scripts/fp-arms/build.sh."
        : style == DaggerfallViewmodelStyle.Classic
            ? "viewmodel.style is classic, which draws the weapon sprites: set it to held or heldRetro in the tuning payload."
            : null;

    private HeldMotion Motion => art!.Document.Motion;
    private MotionKey[] Keys => Motion.Tracks[strike!];

    /// <summary>The strikes the art has motion for, in the classic order.</summary>
    internal IReadOnlyList<string> Strikes => art is null ? [] : [.. NormalizedClassicWeapon.StrikeActions.Where(Motion.Tracks.ContainsKey)];

    /// <summary>Shows a look with one of its strikes in the shown hand (main at first), at rest, and lists the strike's keys.</summary>
    internal DebugCommandResult Show(string heldLook, string action) => Show(heldLook, action, Hands[offHand ? 1 : 0]);

    /// <summary>Shows a look with one of its strikes in a hand (main or off), at rest, and lists the strike's keys.</summary>
    internal DebugCommandResult Show(string heldLook, string action, string inHand)
    {
        if (Unavailable is { } unavailable) return Failure(unavailable);
        if (!art!.Draws(heldLook)) return Failure($"The art draws no '{heldLook}'. Looks: {string.Join(", ", art.Document.Weapons)}.");
        if (!Motion.Tracks.ContainsKey(action))
            return Failure($"'{action}' has no motion. Strikes with motion: {string.Join(", ", Strikes)}; the others play their sprites.");
        if (inHand is not ("main" or "off")) return Failure("The hand is main or off.");
        if (Timing(heldLook, action) is null) return Failure($"Classic weapon '{heldLook}' has no '{action}' action to time it by.");
        if (appearance().ViewHeld(inHand == "off") is not { } viewed) return Failure("The current site draws no 3D weapon.");
        (look, strike, offHand, key) = (heldLook, action, inHand == "off", -1);
        viewed.Rest();
        publish();
        return DebugCommandResult.Success(Describe());
    }

    /// <summary>Shows the shown look in the main or off hand.</summary>
    internal DebugCommandResult Hand(string inHand) =>
        look is null ? Unshown() : Show(look, strike!, inHand);

    /// <summary>Holds the look exactly in key <paramref name="n"/>'s pose (-1 is rest).</summary>
    internal DebugCommandResult Key(int n)
    {
        if (Viewed() is not { } viewed) return Unshown();
        if (n < -1 || n >= Keys.Length) return Failure($"Keys run from 0 to {Keys.Length - 1} (-1 is rest).");
        key = n;
        if (n < 0) viewed.Rest();
        else viewed.Hold(Motion.Poses[Keys[n].Pose]);
        publish();
        return DebugCommandResult.Success(n < 0 ? "rest" : Line(n));
    }

    /// <summary>Holds the next key (<paramref name="step"/> 1; after the last, rest) or the previous (-1; before the first, rest).</summary>
    internal DebugCommandResult Step(int step) => look is null ? Unshown()
        : step is not (1 or -1) ? Failure("A step is 1 (next) or -1 (previous).")
        : Key((key + 1 + step + Keys.Length + 1) % (Keys.Length + 1) - 1);

    /// <summary>Plays the strike from rest at <paramref name="speed"/> (1 as in play, 0.25 a quarter), in real time.</summary>
    internal DebugCommandResult Play(float speed)
    {
        if (Viewed() is not { } viewed) return Unshown();
        if (!(speed >= .01F && speed <= 4)) return Failure("Speed runs from 0.01 to 4.");
        var (timing, frame) = Timing(look!, strike!)!.Value;
        key = -1;
        viewed.Play(strike!, timing, frame, speed);
        publish();
        return DebugCommandResult.Success($"Playing {strike} on {look} over {timing.Duration / speed:0.###} s.");
    }

    /// <summary>Shows the strike paused <paramref name="seconds"/> after its start, as the Engine plays it.</summary>
    internal DebugCommandResult At(float seconds)
    {
        if (Viewed() is not { } viewed) return Unshown();
        var (timing, frame) = Timing(look!, strike!)!.Value;
        if (!(seconds >= 0 && seconds <= timing.Duration)) return Failure($"The strike runs from 0 to {timing.Duration:0.###} s.");
        key = -1;
        viewed.Play(strike!, timing, frame, 1, seconds);
        publish();
        string phase = seconds < timing.Windup ? "windup" : seconds < timing.Windup + timing.Commit ? "commit" : "recovery";
        return DebugCommandResult.Success($"{strike} at {seconds:0.###} s ({phase}; the hit is at {timing.Windup:0.###} s).");
    }

    /// <summary>Returns the hands to what they wield.</summary>
    internal DebugCommandResult Off()
    {
        (look, strike, offHand, key) = (null, null, false, -1);
        appearance().EndHeldView();
        publish();
        return DebugCommandResult.Success("Motion viewer off.");
    }

    /// <summary>The tunables page; shows the first look and strike when nothing is shown yet.</summary>
    internal TunablesPage Page()
    {
        if (Unavailable is { } unavailable) return new(Title, []) { Notice = unavailable };
        if (look is null || strike is null)
        {
            if (Strikes is not [var first, ..]) return new(Title, []) { Notice = "The art has motion for no strike." };
            DebugCommandResult shown = Show(art!.Document.Weapons[0], first, "main");
            if (!shown.Succeeded) return new(Title, []) { Notice = shown.Message };
        }
        var (timing, _) = Timing(look!, strike!)!.Value;
        MotionKey[] keys = Keys;
        string[] still = [.. NormalizedClassicWeapon.StrikeActions.Except(Strikes)];
        return new(Title, DaggerfallHeldTunables.For(art!.Document, strike!, timing, key, publish))
        {
            Choosers =
            [
                new("look", "Look", look!, art!.Document.Weapons),
                new("strike", "Strike", strike!, Strikes),
                new("hand", "Hand", Hands[offHand ? 1 : 0], Hands)
            ],
            Timeline = new([.. keys.Select((k, i) => new TunableKey(i, k.Pose, k.Time(timing)))], timing.Duration, key)
            {
                Speeds = [.1F, .25F, .5F, 1F, 2F]
            },
            Commands = new Dictionary<string, string>
            {
                ["key"] = "held.motion.key {key}",
                ["at"] = "held.motion.at {seconds}",
                ["play"] = "held.motion.play {speed}"
            },
            Notice = string.Join(" ", new[]
            {
                Motion.OrderProblem(strike!, timing),
                still.Length == 0 ? null : $"No motion yet for {string.Join(", ", still)} (their sprites play).",
                $"Save writes content/{DaggerfallHeldArt.DocumentPath}."
            }.OfType<string>())
        };
    }

    /// <summary>A chooser picks the look, the strike or the hand; null when it is shown, else why not.</summary>
    internal string? Choose(string chooser, string value) => chooser switch
    {
        "look" => Refusal(Show(value, strike ?? Strikes.FirstOrDefault() ?? "")),
        "strike" => look is null ? "Choose a look first." : Refusal(Show(look, value)),
        "hand" => look is null ? "Choose a look first." : Refusal(Show(look, strike!, value)),
        _ => $"Unknown chooser '{chooser}'."
    };

    // The strike's timing as play times it, and one classic frame; null when the visual has no such action.
    private (PhaseTiming Timing, float Frame)? Timing(string visual, string action)
    {
        if (appearance().ClassicWeapon(visual)?.Actions.GetValueOrDefault(action) is not { } classic) return null;
        float frame = 1F / classic.FramesPerSecond;
        return (DaggerfallHeldViewmodel.Timing(frame, DaggerfallFormulaPolicy.MeleeWeaponHitFrame, classic.PlayedFrameCount), frame);
    }

    // The viewed 3D weapon, kept in the shown hand; null when nothing is shown.
    private DaggerfallHeldViewmodel? Viewed() => look is null ? null : appearance().ViewHeld(offHand);

    private string Describe()
    {
        var (timing, frame) = Timing(look!, strike!)!.Value;
        return $"{look} in the {Hands[offHand ? 1 : 0]} hand · {strike} ({1 / frame:0.##} frames a second; windup {timing.Windup:0.###} s, commit {timing.Commit:0.###} s, recovery {timing.Recovery:0.###} s; the hit is at {timing.Windup:0.###} s)\n" +
            string.Join("\n", Enumerable.Range(0, Keys.Length).Select(Line));
    }

    private string Line(int i)
    {
        MotionKey k = Keys[i];
        HeldPose pose = Motion.Poses[k.Pose];
        var (timing, _) = Timing(look!, strike!)!.Value;
        return FormattableString.Invariant($"{i}: {k.Pose} at {k.Phase} {k.At:0.####} = {k.Time(timing):0.###} s, {k.Ease}; position [{pose.Position.X}, {pose.Position.Y}, {pose.Position.Z}] rotation [{pose.Rotation.X}, {pose.Rotation.Y}, {pose.Rotation.Z}]");
    }

    private DebugCommandResult Unshown() => Failure(Unavailable ?? "Choose a look first: held.motion <look> <strike>.");

    private static string? Refusal(DebugCommandResult result) => result.Succeeded ? null : result.Message;

    private static DebugCommandResult Failure(string message) => DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, message);
}
