using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.FirstPerson;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The developer first-person workbench over the 3D first-person art: the motion viewer shows what draws a classic
/// weapon visual or a named item (its look, <c>item.120</c> for an item template) with one of its strikes, holds the strike's clip at any of its marks, plays the
/// strike at any speed or shows it paused at any moment, all through the held viewmodel the game draws
/// (<see cref="DaggerfallSiteAppearance.ViewHeld"/>), and never through the click attack's random strike. The Engine
/// tunables page (<see cref="Page"/>) adds choosers for the look, strike and arm set, the strike's marks as its timeline, and the
/// values the viewmodel draws by: the strike's marks and fade, the family's idle loop, the look's grip and hold, the
/// arms' shoulder and the rig's palm. Each value applies to the loaded art at once and is saved to the held definition
/// (<see cref="DaggerfallHeldArt.DocumentPath"/>) by the page's Save.
/// </summary>
/// <remarks>
/// An attack is timed as play times it: the attack tuning for the family's weapon class, with the hit where the
/// windup ends, which the clip's strike mark lands on. Adapted from rusty-riders'
/// <c>HeldMotionDebugCommands</c> and <c>HeldWorkbench</c>.
/// </remarks>
/// <param name="art">The art the composition resolved over; null when no first-person container is installed.</param>
/// <param name="attacks">The attack tuning, which times what the viewer plays.</param>
/// <param name="style">The viewmodel style the tuning asks for.</param>
/// <param name="appearance">The current site's appearance, which draws the viewmodel.</param>
/// <param name="publish">Republishes the presentation, so a change shows at once.</param>
internal sealed class DaggerfallHeldWorkbench(DaggerfallHeldArt? art, DaggerfallAttackTuning attacks, DaggerfallViewmodelStyle style, Func<DaggerfallSiteAppearance> appearance, Action publish)
{
    internal const string Title = "First-person workbench";
    private const string WornArms = "worn";
    private static readonly string[] MarkNames = ["start", "strike", "recover", "end"];

    private string? look;
    private string? strike;
    // The arm set shown; null for the one the player wears.
    private string? arms;
    private int key = -1;
    // The strikes whose marks were set on the page, which it keeps binding (so Save writes them) after the viewer moves on.
    private readonly HashSet<string> edited = [];

    /// <summary>The look shown (a classic weapon visual or item the art draws), the strike it plays, and the held mark (-1: none).</summary>
    internal (string? Look, string? Strike, int Key) Viewing => (look, strike, key);

    /// <summary>Why nothing can be viewed, or null when the art is drawn.</summary>
    internal string? Unavailable => art is null
        ? $"No first-person art: {DaggerfallHeldArt.ContainerName} is not installed beside the product. Build it with scripts/fp-arms/build.sh."
        : style == DaggerfallViewmodelStyle.Classic
            ? "viewmodel.style is classic, which draws the weapon sprites: set it to held or heldRetro in the tuning payload."
            : null;

    /// <summary>The classic weapon visuals the art draws, family by family, then the items with their own place in it.</summary>
    internal IReadOnlyList<string> Looks => art is null ? [] :
        [.. art.Document.Families.Values.SelectMany(family => family.Weapons), .. art.Document.Items.Keys.Order().Select(template => $"{DaggerfallHeldArt.ItemLookPrefix}{template}")];

    /// <summary>The strikes the shown look's family has clips for (the first look's before one is shown), in the classic order.</summary>
    internal IReadOnlyList<string> Strikes => art is null ? [] : StrikesOf(look ?? Looks[0]);

    private DaggerfallHeldDrawing Drawing => art!.Look(look!)! with { Arms = arms };
    private string Family => Drawing.Family;
    private ArmClip Clip => art!.Document.Families[Family].Strikes[strike!];

    /// <summary>Shows a look's family at rest with one of its strikes, and lists the strike's marks.</summary>
    internal DebugCommandResult Show(string heldLook, string action)
    {
        if (Unavailable is { } unavailable) return Failure(unavailable);
        if (art!.Look(heldLook) is not { } drawing) return Failure($"The art draws no '{heldLook}'. Looks: {string.Join(", ", Looks)}.");
        if (!art.Document.Families[drawing.Family].Strikes.ContainsKey(action))
            return Failure($"'{action}' has no clip for {heldLook}. Strikes with clips: {string.Join(", ", StrikesOf(heldLook))}; the others play their sprites.");
        if (AttackTiming(heldLook, action) is null) return Failure($"The rules don't time '{action}'.");
        if (appearance().ViewHeld(drawing with { Arms = arms }) is not { } viewed) return Failure("The current site draws no 3D weapon.");
        (look, strike, key) = (heldLook, action, -1);
        viewed.Rest(0);
        publish();
        return DebugCommandResult.Success(Describe());
    }

    /// <summary>
    /// Moves the shields' seat on the hand joint (<paramref name="part"/> <c>seat</c>: where, then the turn) or the hand's
    /// carry (<c>carry</c>: the grip, then the hold) in the loaded art, shown at the next draw; the answer is what to save.
    /// </summary>
    internal DebugCommandResult Shield(string part, Vector3 at, Vector3 turn)
    {
        if (Unavailable is { } unavailable) return Failure(unavailable);
        if (art!.Document.Shield is not { } shield) return Failure("The held definition draws no shields.");
        switch (part)
        {
            case "seat": (shield.Seat.At, shield.Seat.Turn) = (at, turn); break;
            case "carry": (shield.Carry.Grip, shield.Carry.Hold) = (at, turn); break;
            default: return Failure("A part is seat or carry.");
        }
        return DebugCommandResult.Success($"shield.{part}: {at}, {turn}");
    }

    /// <summary>Holds the strike's clip exactly at mark <paramref name="n"/> (0 start, 1 strike, 2 recover, 3 end; -1 is rest).</summary>
    internal DebugCommandResult Key(int n)
    {
        if (Viewed() is not { } viewed) return Unshown();
        if (n < -1 || n >= MarkNames.Length) return Failure($"Marks run from 0 to {MarkNames.Length - 1} (start, strike, recover, end; -1 is rest).");
        key = n;
        var (timing, frame) = AttackTiming(look!, strike!)!.Value;
        if (n < 0) viewed.Rest(0);
        else viewed.Play(strike!, timing, frame, 1, MarkTimes(timing)[n]);
        publish();
        return DebugCommandResult.Success(n < 0 ? "rest" : Line(n, timing));
    }

    /// <summary>Holds the next mark (<paramref name="step"/> 1; after the last, rest) or the previous (-1; before the first, rest).</summary>
    internal DebugCommandResult Step(int step) => look is null ? Unshown()
        : step is not (1 or -1) ? Failure("A step is 1 (next) or -1 (previous).")
        : Key((key + 1 + step + MarkNames.Length + 1) % (MarkNames.Length + 1) - 1);

    /// <summary>Plays the strike from rest at <paramref name="speed"/> (1 as in play, 0.25 a quarter), on world time.</summary>
    internal DebugCommandResult Play(float speed)
    {
        if (Viewed() is not { } viewed) return Unshown();
        if (!(speed >= .01F && speed <= 4)) return Failure("Speed runs from 0.01 to 4.");
        var (timing, frame) = AttackTiming(look!, strike!)!.Value;
        key = -1;
        viewed.Play(strike!, timing, frame, speed);
        publish();
        return DebugCommandResult.Success($"Playing {strike} on {look} over {timing.Duration / speed:0.###} s.");
    }

    /// <summary>Shows the strike paused <paramref name="seconds"/> after its start, as the arms play it.</summary>
    internal DebugCommandResult At(float seconds)
    {
        if (Viewed() is not { } viewed) return Unshown();
        var (timing, frame) = AttackTiming(look!, strike!)!.Value;
        if (!(seconds >= 0 && seconds <= timing.Duration)) return Failure($"The strike runs from 0 to {timing.Duration:0.###} s.");
        key = -1;
        viewed.Play(strike!, timing, frame, 1, seconds);
        publish();
        ArmClipFrame shown = Clip.At(timing, seconds);
        return DebugCommandResult.Success($"{strike} at {seconds:0.###} s ({shown.Phase?.ToString().ToLowerInvariant()}; {shown.Clip} at {shown.Time:0.###}; the hit is at {timing.Windup:0.###} s).");
    }

    /// <summary>What the drawn 3D weapon's arms play now: its family, the clip, its time and phase, what fades out under it, and how far the arms are clip-led.</summary>
    internal DebugCommandResult Read()
    {
        if (Unavailable is { } unavailable) return Failure(unavailable);
        if (appearance().DrawnHeld is not { } drawn) return Failure("No 3D weapon is drawn: the hands wield no weapon the art draws, or a strike without a clip shows its sprite.");
        ArmClipReading reading = drawn.Clip;
        string playing = reading.Playing is { } frame ? FormattableString.Invariant($"{frame.Clip} at {frame.Time:0.###}{(frame.Phase is { } phase ? $" ({phase.ToString().ToLowerInvariant()})" : "")}") : "no clip";
        string under = reading.From is { } from ? FormattableString.Invariant($", over {from} at {reading.FromTime:0.###} faded in {reading.Blend:0.##}") : "";
        return DebugCommandResult.Success(FormattableString.Invariant($"{drawn.Drawing.Family}: {playing}{under}; clip-led {reading.Led:0.##}"));
    }

    /// <summary>Returns the hands to what they wield.</summary>
    internal DebugCommandResult Off()
    {
        (look, strike, key) = (null, null, -1);
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
            DebugCommandResult shown = Show(Looks[0], StrikesOf(Looks[0])[0]);
            if (!shown.Succeeded) return new(Title, []) { Notice = shown.Message };
        }
        var (timing, _) = AttackTiming(look!, strike!)!.Value;
        string[] still = [.. DaggerfallAttackIds.Held.Except([DaggerfallAttackIds.PowerHold, DaggerfallAttackIds.Shot]).Except(Strikes)];
        float[] marks = MarkTimes(timing);
        return new(Title, DaggerfallHeldTunables.For(art!.Document, Drawing, strike!, timing, edited, publish))
        {
            Choosers =
            [
                new("look", "Look", look!, Looks),
                new("strike", "Strike", strike!, Strikes),
                new("arms", "Arms", arms ?? WornArms, [WornArms, .. art.Document.ArmSets.Keys])
            ],
            Timeline = new([.. MarkNames.Select((name, i) => new TunableKey(i, name, marks[i]))], timing.Duration, key)
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
                $"{look} plays {Clip.Clip} ({Family}).",
                still.Length == 0 ? null : $"No clip for {string.Join(", ", still)} (their sprites play).",
                $"Save writes content/{DaggerfallHeldArt.DocumentPath}."
            }.OfType<string>())
        };
    }

    /// <summary>
    /// Shows the shown look in arm set <paramref name="set"/>, or in what the player wears (<see cref="WornArms"/>).
    /// </summary>
    internal DebugCommandResult Arms(string set)
    {
        if (Unavailable is { } unavailable) return Failure(unavailable);
        if (look is null || strike is null) return Unshown();
        if (set != WornArms && !art!.Document.ArmSets.ContainsKey(set)) return Failure($"No arm set '{set}'. Sets: {WornArms}, {string.Join(", ", art.Document.ArmSets.Keys)}.");
        arms = set == WornArms ? null : set;
        return Show(look, strike);
    }

    /// <summary>A chooser picks the look, the strike or the arm set; null when it is shown, else why not.</summary>
    internal string? Choose(string chooser, string value) => chooser switch
    {
        "look" => Refusal(Show(value, art?.Look(value) is not null && strike is not null && StrikesOf(value).Contains(strike) ? strike : StrikesOf(value).FirstOrDefault() ?? "")),
        "strike" => look is null ? "Choose a look first." : Refusal(Show(look, value)),
        "arms" => Refusal(Arms(value)),
        _ => $"Unknown chooser '{chooser}'."
    };

    private IReadOnlyList<string> StrikesOf(string shown) => art!.Look(shown) is { } drawing
        ? [.. DaggerfallAttackIds.Held.Where(art.Document.Families[drawing.Family].Strikes.ContainsKey)] : [];

    // The attack's timing as play times it, and the retro style's frame; null for an attack the rules don't time.
    private (PhaseTiming Timing, float Frame)? AttackTiming(string shown, string action)
    {
        string skill = art!.Document.Families[art.Look(shown)!.Family].Class;
        if (attacks.Timing(action, skill) is not { } timing) return null;
        return (new PhaseTiming((float)timing.Windup, MathF.Max((float)timing.Commit, .001F), MathF.Max((float)timing.Recovery, .001F)),
            (float)(timing.Windup / DaggerfallFormulaPolicy.MeleeWeaponHitFrame));
    }

    // When each mark plays in the strike: the start at its beginning, the strike at the hit, the recovery once the commit
    // is over and the end at its close.
    private static float[] MarkTimes(PhaseTiming timing) => [0, timing.Windup, timing.Windup + timing.Commit, timing.Duration];

    // The viewed 3D weapon; null when nothing is shown.
    private DaggerfallHeldViewmodel? Viewed() => look is null ? null : appearance().ViewHeld(Drawing);

    private string Describe()
    {
        var (timing, frame) = AttackTiming(look!, strike!)!.Value;
        return $"{look} ({Family}) · {strike} plays {Clip.Clip} ({1 / frame:0.##} frames a second; windup {timing.Windup:0.###} s, commit {timing.Commit:0.###} s, recovery {timing.Recovery:0.###} s; the hit is at {timing.Windup:0.###} s, fade {Clip.Fade:0.##} s)\n" +
            string.Join("\n", Enumerable.Range(0, MarkNames.Length).Select(i => Line(i, timing)));
    }

    private string Line(int i, PhaseTiming timing)
    {
        ClipMarks marks = Clip.Marks;
        float at = new[] { marks.Start, marks.Strike, marks.Recover, marks.End }[i];
        return FormattableString.Invariant($"{i}: {MarkNames[i]} at {MarkTimes(timing)[i]:0.###} s = {Clip.Clip} at {at:0.###}");
    }

    private DebugCommandResult Unshown() => Failure(Unavailable ?? "Choose a look first: held.motion <look> <strike>.");

    private static string? Refusal(DebugCommandResult result) => result.Succeeded ? null : result.Message;

    private static DebugCommandResult Failure(string message) => DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, message);
}
