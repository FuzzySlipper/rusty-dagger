using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.FirstPerson;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The values the first-person workbench's page shows for a strike of the held art: the look's stance and the hand's
/// grip and hold on it, the strike's key timing and curves, the held key's pose, and the arms' reach and the rig's
/// model and palm. Save writes only the values the page binds, so keys and poses set on the page stay on it, marked
/// edited, after the viewer moves to another strike or key. Each control applies its value to the loaded art in place, as the held viewmodel reads it at every
/// draw, and names where it is saved: its JSON pointer in the held definition (<see cref="DaggerfallHeldArt.DocumentPath"/>),
/// so the page's Save writes it there. Adapted from rusty-riders' <c>HeldTunables</c>.
/// </summary>
internal static class DaggerfallHeldTunables
{
    private static readonly MotionPhase[] Phases = Enum.GetValues<MotionPhase>();

    internal static List<TunableGroup> For(DaggerfallHeldArtDocument document, string strike, PhaseTiming timing, int key, ISet<string> edited, Action publish)
    {
        const string file = DaggerfallHeldArt.DocumentPath;
        DaggerfallHeldLook held = document.Look;
        HeldStance stance = held.Stance;
        List<TunableGroup> groups =
        [
            new("look", "Look · stance and hold (every weapon it draws)",
            [
                .. Vector("stance.grip", "stance grip", () => stance.Grip, v => stance.Grip = v, -.8F, .8F, .005F, "/look/stance/grip", publish),
                .. Vector("stance.toward", "stance toward", () => stance.Toward, v => stance.Toward = v, -2, 2, .01F, "/look/stance/toward", publish),
                .. Vector("stance.up", "stance up", () => stance.Up, v => stance.Up = v, -1, 1, .01F, "/look/stance/up", publish),
                .. Vector("look.grip", "hand's grip (look space)", () => held.Grip, v => held.Grip = v, -.2F, .2F, .002F, "/look/grip", publish),
                .. Vector("look.hold", "hand's hold", () => held.Hold, v => held.Hold = v, -180, 180, 1, "/look/hold", publish)
            ]) { File = file }
        ];

        groups.Add(Keys(document, strike, $"Keys · {strike} ({timing.Duration:0.###} s, the hit at {timing.Windup:0.###} s)", edited, publish));
        // The page binds only what it shows, and Save writes only bound values: keys edited on another strike stay
        // on the page until the workbench ends.
        foreach (string other in document.Motion.Tracks.Keys.Where(other => other != strike && edited.Contains(KeysId(other))))
            groups.Add(Keys(document, other, $"Keys · {other} (edited)", edited, publish));

        // The viewer holds this key and draws its pose as it is now; poses edited at other keys stay on the page.
        MotionKey[] track = document.Motion.Tracks[strike];
        string? heldPose = key >= 0 && key < track.Length ? track[key].Pose : null;
        if (heldPose is not null)
            groups.Add(Pose(document, heldPose, $"Pose · {heldPose} (from the stance: grip moved, look turned about it)", edited, publish));
        foreach (string other in document.Motion.Poses.Keys.Where(other => other != heldPose && edited.Contains(PoseId(other))))
            groups.Add(Pose(document, other, $"Pose · {other} (edited)", edited, publish));

        // The arms: where the right shoulder sits and the right elbow's pole (the left's are mirrored), and the rig's
        // model turn and scale and its palm from the wrist.
        HeldArms arms = document.Arms;
        ArmRig rig = document.Rig;
        groups.Add(new("arms", "Arms and rig (the left mirrors the right)",
        [
            .. Vector("arms.shoulder", "shoulder", () => arms.Shoulder, v => arms.Shoulder = v, -.6F, .6F, .005F, "/arms/shoulder", publish),
            .. Vector("arms.pole", "elbow toward", () => arms.Pole, v => arms.Pole = v, -2, 2, .01F, "/arms/pole", publish),
            .. Vector("rig.turn", "model turn", () => rig.Turn, v => rig.Turn = v, -180, 180, 1, "/rig/turn", publish),
            new(new("rig.scale", "model scale", TunableKind.Slider, .1F, 3, .01F, rig.Scale) { Source = new(file, "/rig/scale") }, v => { rig.Scale = v; publish(); }),
            .. Vector("rig.palm", "palm", () => rig.Palm, v => rig.Palm = v, -.15F, .15F, .002F, "/rig/palm", publish)
        ]) { File = file });
        return groups;
    }

    // A strike's keys: each key's phase, its moment through that phase and the curve it eases in by. Moving a key
    // moves the timeline. Setting one records the group as edited.
    private static TunableGroup Keys(DaggerfallHeldArtDocument document, string strike, string title, ISet<string> edited, Action publish)
    {
        const string file = DaggerfallHeldArt.DocumentPath;
        string id = KeysId(strike);
        Action changed = () => { edited.Add(id); publish(); };
        MotionKey[] track = document.Motion.Tracks[strike];
        List<Tunable> controls = [];
        for (int i = 0; i < track.Length; i++)
        {
            int index = i;
            MotionKey k = track[i];
            string at = $"/motion/tracks/{Escape(strike)}/{i}";
            string prefix = $"{id}.{i}";
            controls.Add(new(new($"{prefix}.phase", $"{i} {k.Pose} phase", TunableKind.Choice, 0, Phases.Length - 1, 1, Array.IndexOf(Phases, k.Phase))
                { Options = [.. Phases.Select(p => p.ToString())], Reshapes = true, Source = new(file, $"{at}/phase") },
                v => { track[index] = track[index] with { Phase = Phases[(int)MathF.Round(v)] }; changed(); }));
            controls.Add(new(new($"{prefix}.at", $"{i} {k.Pose} at", TunableKind.Slider, 0, 1, .0001F, k.At)
                { Reshapes = true, Source = new(file, $"{at}/at") },
                v => { track[index] = track[index] with { At = v }; changed(); }));
            controls.Add(new(new($"{prefix}.ease", $"{i} {k.Pose} ease", TunableKind.Choice, 0, HeldEases.Named.Length - 1, 1, Array.IndexOf(HeldEases.Named, k.Ease))
                { Options = [.. HeldEases.Named.Select(e => e.ToString())], Source = new(file, $"{at}/ease") },
                v => { track[index] = track[index] with { Ease = HeldEases.Named[(int)MathF.Round(v)] }; changed(); }));
        }
        return new(id, title, controls) { File = file };
    }

    // A pose: its grip moved and its look turned from the stance. Setting one records the group as edited.
    private static TunableGroup Pose(DaggerfallHeldArtDocument document, string name, string title, ISet<string> edited, Action publish)
    {
        string id = PoseId(name);
        Action changed = () => { edited.Add(id); publish(); };
        HeldPose pose = document.Motion.Poses[name];
        string pointer = $"/motion/poses/{Escape(name)}";
        return new(id, title,
        [
            .. Vector($"{id}.position", "move", () => pose.Position, v => pose.Position = v, -.5F, .5F, .0001F, $"{pointer}/position", changed),
            .. Vector($"{id}.rotation", "turn", () => pose.Rotation, v => pose.Rotation = v, -180, 180, .01F, $"{pointer}/rotation", changed)
        ]) { File = DaggerfallHeldArt.DocumentPath };
    }

    private static string KeysId(string strike) => $"keys.{strike}";
    private static string PoseId(string name) => $"pose.{name}";

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
