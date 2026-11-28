using Rusty.Engine;

namespace WorldRpg.Kit.Presentation;

/// <summary>
/// Draws enemies as skinned splat (or billboard-card / core) figures in place of their classic directional sprites,
/// driving idle, move, melee/staff/bow/cast attacks, hurt reactions and death playback from each enemy's live state.
/// </summary>
public interface IEnemySplatCrowd
{
    /// <summary>
    /// The appearance object <paramref name="id"/> shows in the snapshot being built for enemy <paramref name="actor"/>,
    /// or null when it should draw its classic sprite or corpse instead: the splat <paramref name="character"/> its look
    /// names, else the one its art binds to <paramref name="mobileId"/> and <paramref name="gender"/>.
    /// </summary>
    Appearance? Show(ulong id, long actor, int mobileId, string? gender, string? character, string state, uint actionSequence, bool defeated, Color? tint, string? liveryKey);

    /// <summary>Before the snapshot is published: releases animation instances for enemies not in the snapshot.</summary>
    void Publishing();

    /// <summary>After the snapshot is accepted: updates clip playback and cloth skirts/cloaks.</summary>
    void Settle();

    /// <summary>Counts drawn time toward the next settle's cloth skirt/cloak simulation.</summary>
    void Advance(float seconds);
}

/// <summary>A skinned splat enemy figure and its bound combat clip names.</summary>
public sealed record EnemySplatFigure(
    string Name,
    Appearance Appearance,
    string IdleClip,
    string MoveClip,
    string AttackClip,
    string AlternateAttackClip,
    string CastClip,
    string BowClip,
    string HurtClip,
    string DeathClip,
    float CrossfadeSeconds,
    float MoveRate,
    SplatSkirtRig? Skirt = null);

/// <summary>
/// Manages per-instance animation playback and cloth skirt/cloak physics for live and defeated splat enemies.
/// </summary>
public sealed class EnemySplatCrowd(Func<IAnimationService> engine) : IDisposable
{
    private readonly Dictionary<ulong, EnemyInstance> shown = [];
    private readonly HashSet<ulong> seen = [];
    private static readonly Transform Unplaced = new(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.One);
    private float seconds;
    private bool disposed;

    /// <summary>Optional forced clip name (for developer animation review); null plays live enemy states.</summary>
    public string? OverrideClip { get; set; }

    /// <summary>How many enemies the last settled snapshot drew as splats.</summary>
    public int Count => shown.Count;

    public void Advance(float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsed), elapsed, "Drawn time advances by 0 or more seconds.");
        seconds += elapsed;
    }

    public Appearance? Show(ulong id, long actor, EnemySplatFigure figure, string state, uint actionSequence, bool defeated)
    {
        if (disposed) return null;
        seen.Add(id);
        if (!shown.TryGetValue(id, out EnemyInstance? entry))
        {
            shown[id] = entry = new(figure, Engine.CreateInstance(new(figure.Appearance, id)));
        }
        else if (entry.Figure != figure)
        {
            entry.Instance = Engine.ReplaceInstance(entry.Instance, new(figure.Appearance, id));
            entry.Figure = figure;
            entry.PlayedClip = null;
            entry.PlayedSequence = 0;
            entry.Skirt = null;
        }
        (string wantedClip, bool loop, float speed, uint seq) = SelectPlayback(figure, actor, state, actionSequence, defeated);
        entry.WantedClip = wantedClip;
        entry.WantedLoop = loop;
        entry.WantedSpeed = speed;
        entry.WantedSequence = seq;
        return figure.Appearance;
    }

    public void Publishing()
    {
        if (disposed) return;
        List<Exception>? failures = null;
        foreach (ulong id in shown.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            Release(shown[id].Instance, ref failures);
            shown.Remove(id);
        }
        seen.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    public void Settle()
    {
        if (disposed) return;
        foreach (EnemyInstance entry in shown.Values)
        {
            bool changed = entry.PlayedClip != entry.WantedClip || (!entry.WantedLoop && entry.PlayedSequence != entry.WantedSequence);
            if (!changed) continue;
            bool fades = entry.PlayedClip is not null;
            float fade = fades ? (entry.WantedLoop ? entry.Figure.CrossfadeSeconds : MathF.Min(entry.Figure.CrossfadeSeconds, .12F)) : 0F;
            float phase = entry.WantedLoop && !fades ? (float)((entry.Instance.Handle.Value * 2654435761UL % 10000UL) / 10000.0) : 0F;
            Engine.SetPlayback(new(entry.Instance, AnimationPlaybackKind.Play, entry.WantedClip,
                entry.WantedLoop ? AnimationLoopMode.Repeat : AnimationLoopMode.Once,
                entry.WantedSpeed, 1F, !fades || !entry.WantedLoop, fade, fades, phase));
            entry.PlayedClip = entry.WantedClip;
            entry.PlayedSequence = entry.WantedSequence;
        }
        float elapsed = seconds;
        seconds = 0;
        DriveSkirts(elapsed);
    }

    private (string Clip, bool Loop, float Speed, uint Sequence) SelectPlayback(EnemySplatFigure figure, long actor, string state, uint actionSequence, bool defeated)
    {
        if (!string.IsNullOrEmpty(OverrideClip))
        {
            bool loopOverride = OverrideClip is "idle" or "combat-idle" or "mage-idle" or "walk" or "gentle" or "run" or "run-sword";
            return (OverrideClip, loopOverride, OverrideClip == figure.MoveClip ? figure.MoveRate : 1F, 1);
        }
        if (defeated) return (figure.DeathClip, false, 1.1F, 1);
        return state switch
        {
            "move" => (figure.MoveClip, true, figure.MoveRate, 0),
            "primaryAttack" => ((actor + actionSequence) % 2 == 0 ? figure.AttackClip : figure.AlternateAttackClip, false, 1.85F, actionSequence),
            "rangedAttack1" or "rangedAttack2" => (figure.BowClip, false, 1.5F, actionSequence),
            "cast" => (figure.CastClip, false, 1.6F, actionSequence),
            "hurt" => (figure.HurtClip, false, 1.7F, actionSequence),
            _ => (figure.IdleClip, true, 1F, 0),
        };
    }

    private void DriveSkirts(float elapsed)
    {
        List<(EnemyInstance Entry, JointPose[] Pose)> driven = [];
        foreach (EnemyInstance entry in shown.Values)
        {
            if (entry.Figure.Skirt is not { } skirt) continue;
            if (entry.PlayedClip == entry.Figure.DeathClip)
            {
                // Release cloth joint overrides on death so supine robes follow the animated skeleton instead of spiking upward.
                if (entry.Skirt is not null)
                {
                    entry.Skirt = null;
                    Engine.SetPose(new(entry.Instance, ReadOnlyMemory<TwoBoneIk>.Empty, ReadOnlyMemory<JointOverride>.Empty));
                }
                continue;
            }
            if (entry.Skirt is not null && elapsed == 0) continue;
            entry.Skirt ??= skirt.Sheet();
            driven.Add((entry, Engine.SampleJointPose(AnimationPoseSampleRequest.OfCurrentPlayback(entry.Instance, Unplaced, applyPoseControls: false)).ToArray()));
        }
        JointOverride[][] placed = new JointOverride[driven.Count][];
        if (driven.Count > 1) Parallel.For(0, driven.Count, index => placed[index] = driven[index].Entry.Skirt!.Step(elapsed, driven[index].Pose));
        else if (driven.Count == 1) placed[0] = driven[0].Entry.Skirt!.Step(elapsed, driven[0].Pose);
        for (int index = 0; index < driven.Count; index++)
            Engine.SetPose(new(driven[index].Entry.Instance, ReadOnlyMemory<TwoBoneIk>.Empty, placed[index]));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? failures = null;
        foreach (EnemyInstance entry in shown.Values) Release(entry.Instance, ref failures);
        shown.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private IAnimationService Engine => engine();

    private static void Release(IDisposable value, ref List<Exception>? failures)
    {
        try { value.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }

    private sealed class EnemyInstance(EnemySplatFigure figure, AnimationInstance instance)
    {
        internal EnemySplatFigure Figure { get; set; } = figure;
        internal AnimationInstance Instance { get; set; } = instance;
        internal string WantedClip { get; set; } = string.Empty;
        internal bool WantedLoop { get; set; }
        internal float WantedSpeed { get; set; } = 1F;
        internal uint WantedSequence { get; set; }
        internal string? PlayedClip { get; set; }
        internal uint PlayedSequence { get; set; }
        internal SplatSkirtSheet? Skirt { get; set; }
    }
}
