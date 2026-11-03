using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.FirstPerson;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The values the first-person workbench's page shows for a strike of a family of the held art: the strike's marks
/// (where its windup starts, its hit lands, its commit ends and its recovery ends in the clip) and fade, the family's
/// idle loop and the look's grip and hold, and the arms' shoulder and the rig's palm. Save writes only the values the
/// page binds, so marks and looks set on the page stay on it, marked edited, after the viewer moves to another strike
/// or family. Each
/// control applies its value to the loaded art in place, as the held viewmodel reads it at every draw, and names where
/// it is saved: its JSON pointer in the held definition (<see cref="DaggerfallHeldArt.DocumentPath"/>), so the page's
/// Save writes it there. Adapted from rusty-riders' <c>HeldTunables</c>.
/// </summary>
internal static class DaggerfallHeldTunables
{
    internal static List<TunableGroup> For(DaggerfallHeldArtDocument document, string family, string strike, PhaseTiming timing, ISet<string> edited, Action publish)
    {
        List<TunableGroup> groups = [Marks(family, strike, document, $"Clip · {strike} ({timing.Duration:0.###} s, the hit at {timing.Windup:0.###} s)", edited, publish)];
        // The page binds only what it shows, and Save writes only bound values: marks and looks edited on another strike
        // or family stay on the page until the workbench ends.
        foreach (var (name, other) in document.Families)
            foreach (string action in other.Strikes.Keys.Where(action => (name, action) != (family, strike) && edited.Contains(MarksId(name, action))))
                groups.Add(Marks(name, action, document, $"Clip · {name} {action} (edited)", edited, publish));
        groups.Add(Look(family, document, $"Look · {family} (the hand's hold, and the idle loop)", edited, publish));
        foreach (string name in document.Families.Keys.Where(name => name != family && edited.Contains(LookId(name))))
            groups.Add(Look(name, document, $"Look · {name} (edited)", edited, publish));

        // The arms: where the clips' right shoulder sits from the eye, and the rig's palm from the wrist (where a look is held).
        HeldArms arms = document.Arms;
        ArmRig rig = document.Rig;
        groups.Add(new("arms", "Arms and rig",
        [
            .. Vector("arms.shoulder", "shoulder", () => arms.Shoulder, v => arms.Shoulder = v, -.6F, .6F, .005F, "/arms/shoulder", publish),
            .. Vector("rig.palm", "palm", () => rig.Palm, v => rig.Palm = v, -.15F, .15F, .002F, "/rig/palm", publish)
        ]) { File = DaggerfallHeldArt.DocumentPath });
        return groups;
    }

    // A family's look and idle loop: where the hand holds the look and how, and how long the idle loop lasts. Setting one
    // records the group as edited.
    private static TunableGroup Look(string family, DaggerfallHeldArtDocument document, string title, ISet<string> edited, Action publish)
    {
        const string file = DaggerfallHeldArt.DocumentPath;
        string id = LookId(family), at = $"/families/{Escape(family)}";
        Action changed = () => { edited.Add(id); publish(); };
        DaggerfallHeldFamily drawn = document.Families[family];
        DaggerfallHeldLook held = drawn.Look;
        return new(id, title,
        [
            .. Vector($"{id}.grip", "hand's grip (look space)", () => held.Grip, v => held.Grip = v, -.3F, .3F, .002F, $"{at}/look/grip", changed),
            .. Vector($"{id}.hold", "hand's hold", () => held.Hold, v => held.Hold = v, -180, 180, 1, $"{at}/look/hold", changed),
            new(new($"{id}.idle", "idle loop seconds", TunableKind.Slider, .5F, 20, .01F, drawn.Idle.Seconds) { Source = new(file, $"{at}/idle/seconds") },
                v => { drawn.Idle.Seconds = v; changed(); })
        ]) { File = file };
    }

    // A strike's clip: its four marks, as fractions of the clip, and its fade. Setting one records the group as edited.
    private static TunableGroup Marks(string family, string strike, DaggerfallHeldArtDocument document, string title, ISet<string> edited, Action publish)
    {
        const string file = DaggerfallHeldArt.DocumentPath;
        string id = MarksId(family, strike);
        Action changed = () => { edited.Add(id); publish(); };
        ArmClip clip = document.Families[family].Strikes[strike];
        string at = $"/families/{Escape(family)}/strikes/{Escape(strike)}";
        Tunable Mark(string name, Func<ClipMarks, float> get, Func<ClipMarks, float, ClipMarks> set) =>
            new(new($"{id}.{name}", $"{name} mark", TunableKind.Slider, 0, 1, .001F, get(clip.Marks)) { Reshapes = true, Source = new(file, $"{at}/marks/{name}") },
                v => { clip.Marks = set(clip.Marks, v); changed(); });
        return new(id, title,
        [
            Mark("start", m => m.Start, (m, v) => m with { Start = v }),
            Mark("strike", m => m.Strike, (m, v) => m with { Strike = v }),
            Mark("recover", m => m.Recover, (m, v) => m with { Recover = v }),
            Mark("end", m => m.End, (m, v) => m with { End = v }),
            new(new($"{id}.fade", "fade in (seconds)", TunableKind.Slider, 0, .5F, .01F, clip.Fade) { Source = new(file, $"{at}/fade") },
                v => { clip.Fade = v; changed(); })
        ]) { File = file };
    }

    private static string MarksId(string family, string strike) => $"clip.{family}.{strike}";
    private static string LookId(string family) => $"look.{family}";

    // Three sliders over one authored point, applied in place and saved to its elements.
    private static IEnumerable<Tunable> Vector(string id, string label, Func<Vector3> get, Action<Vector3> set, float min, float max, float step,
        string pointer, Action publish)
    {
        string[] axes = ["x", "y", "z"];
        for (int i = 0; i < 3; i++)
        {
            int axis = i;
            yield return new(new($"{id}.{axes[i]}", $"{label} {axes[i]}", TunableKind.Slider, min, max, step, get()[i])
                { Source = new(DaggerfallHeldArt.DocumentPath, $"{pointer}/{i}") },
                v =>
                {
                    Vector3 now = get();
                    now[axis] = v;
                    set(now);
                    publish();
                });
        }
    }

    // A JSON pointer reference token (RFC 6901).
    private static string Escape(string token) => token.Replace("~", "~0").Replace("/", "~1");
}
