using Rusty.Engine;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>The player's attack set, as presentation keys its motions.</summary>
internal static class DaggerfallAttackIds
{
    internal const string PowerIn = "powerIn";
    internal const string PowerHold = "powerHold";
    internal const string PowerOut = "powerOut";
    internal const string Kick = "kick";
    internal const string Shot = "shot";

    /// <summary>The fast attacks a chain cycles through.</summary>
    internal static IReadOnlyList<string> Fast { get; } = ["fastA", "fastB", "fastC", "fastD"];

    /// <summary>Every motion a weapon can play.</summary>
    internal static IReadOnlyList<string> All { get; } = [.. Fast, PowerIn, PowerHold, PowerOut, Kick, Shot];
}

/// <summary>What the attack clock asks of the session around one admitted step.</summary>
internal interface IDaggerfallPlayerAttacks
{
    /// <summary>The skill of the weapon in hand, whose class sets the timing.</summary>
    string WeaponSkill { get; }

    /// <summary>The timing of a bow's shot, or null when the hands hold no bow.</summary>
    DaggerfallAttackTiming? ShotTiming { get; }

    /// <summary>Starts an attack through admission; false when it was refused and no motion plays.</summary>
    bool Start(string attack);

    /// <summary>The started attack reached the end of its windup: its blow lands now.</summary>
    void Impact();

    /// <summary>A motion began: presentation plays it. A null timing is an untimed hold.</summary>
    void Motion(string attack, DaggerfallAttackTiming? timing);

    /// <summary>The last motion ended with nothing after it: presentation returns to rest.</summary>
    void Rest();
}

/// <summary>
/// The player's attack as the rules time it. A press released before the power threshold is a fast attack and one
/// held past it charges a power attack, which holds until release. A press during a recovery chains the next fast
/// attack at once, and one made earlier in a swing waits for the recovery; a swing that runs out resets the chain.
/// The blow lands at the end of the windup, with or without anything drawing it. The clock is advanced by admitted
/// steps only, so held time holds it, and it is transient: a save or a site change retires what is in progress.
/// </summary>
internal sealed class DaggerfallPlayerAttackClock(DaggerfallAttackTuning tuning, IDaggerfallPlayerAttacks attacks)
{
    /// <summary>The product input intent the player holds to attack.</summary>
    private static ReadOnlySpan<byte> Intent => "attack"u8;
    private sealed record Playing(string Attack, DaggerfallAttackTiming? Timing, bool Delivers)
    {
        internal double Seconds { get; set; }
        internal bool Landed { get; set; }
        internal bool InRecovery => Timing is { } timing && Seconds >= timing.Windup + timing.Commit;
    }

    private Playing? _playing;
    private bool _held;
    // How long the unresolved press has been down; null with no press waiting.
    private double? _press;
    // A press that became a power attack: repeats of the held button are the same press.
    private bool _pressSpent;
    private bool _kick;
    private int _chain;
    // A motion ended with a press about to follow, so the rest it owed waits on whether that press starts.
    private bool _restOwed;
    // The weapon class a power attack started with, which its release keeps.
    private DaggerfallWeaponClassAttacks? _power;

    /// <summary>Whether a motion is playing or a press is waiting to become one.</summary>
    internal bool Busy => _playing is not null || _press is not null || _kick;

    /// <summary>Whether a new attack could start this step.</summary>
    internal bool Free => _playing is null or { InRecovery: true, Attack: not DaggerfallAttackIds.Shot };

    /// <summary>Reads one update's input for whether the attack button, or a pad button for it, is down.</summary>
    internal void Observe(ReadOnlySpan<ProductInputEvent> inputs, IReadOnlyList<ControllerActionBinding> pad)
    {
        foreach (ProductInputEvent input in inputs)
        {
            if (input.Kind == InputEventKind.Clear) Dropped();
            else if (DaggerfallInput.HeldChange(input, Intent, DaggerfallInput.Attack, pad) is bool held)
            {
                if (held) _held = true;
                else Release();
            }
        }
    }

    // Input went away with the button still down, which is no release: a charge is abandoned rather than loosed.
    private void Dropped()
    {
        Release();
        _press = null;
        if (_playing is not { Attack: DaggerfallAttackIds.PowerIn or DaggerfallAttackIds.PowerHold }) return;
        _playing = null;
        attacks.Rest();
    }

    private void Release()
    {
        _held = false;
        _pressSpent = false;
    }

    /// <summary>The attack was requested this step.</summary>
    internal void Press()
    {
        if (!_pressSpent) _press ??= 0d;
    }

    /// <summary>The kick was requested this step.</summary>
    internal void PressKick() => _kick = true;

    /// <summary>Advances one admitted step: the motion in progress, then whatever the input now starts.</summary>
    internal void Step(double seconds)
    {
        if (_playing is { } playing)
        {
            playing.Seconds += seconds;
            if (playing.Delivers && !playing.Landed && playing.Seconds >= playing.Timing!.Value.Windup)
            {
                playing.Landed = true;
                attacks.Impact();
            }
            if (playing.Attack == DaggerfallAttackIds.PowerHold)
            {
                if (!_held) Loose();
            }
            else if (playing.Seconds >= playing.Timing!.Value.Duration)
            {
                if (playing.Attack == DaggerfallAttackIds.PowerIn) Charged();
                else
                {
                    _playing = null;
                    _chain = 0;
                    if (Waiting) _restOwed = true;
                    else attacks.Rest();
                }
            }
        }
        if (_press is double down && _held) _press = down + seconds;
        if (!Free) return;
        if (_kick) StartKick();
        else if (_press is double press && (!_held || press >= tuning.PowerThresholdSeconds || attacks.ShotTiming is not null)) StartPressed(!_held);
    }

    /// <summary>Whether a press or kick is about to start a motion, so the rest between them is skipped.</summary>
    private bool Waiting => _kick || _press is not null && !_held;

    private void StartKick()
    {
        _kick = false;
        Begin(DaggerfallAttackIds.Kick, tuning.Kick, delivers: true);
    }

    private void StartPressed(bool tapped)
    {
        _press = null;
        if (attacks.ShotTiming is { } shot)
        {
            _pressSpent = _held;
            Begin(DaggerfallAttackIds.Shot, shot, delivers: true);
            return;
        }
        DaggerfallWeaponClassAttacks weapon = tuning.For(attacks.WeaponSkill);
        if (tapped)
        {
            if (Begin(DaggerfallAttackIds.Fast[_chain], weapon.Fast, delivers: true)) _chain = (_chain + 1) % DaggerfallAttackIds.Fast.Count;
            return;
        }
        _pressSpent = true;
        _chain = 0;
        _power = weapon;
        _playing = new(DaggerfallAttackIds.PowerIn, new(weapon.PowerCharge, 0d, 0d), Delivers: false);
        attacks.Motion(DaggerfallAttackIds.PowerIn, _playing.Timing);
    }

    private void Charged()
    {
        if (!_held) { Loose(); return; }
        _playing = new(DaggerfallAttackIds.PowerHold, null, Delivers: false);
        attacks.Motion(DaggerfallAttackIds.PowerHold, null);
    }

    private void Loose() => Begin(DaggerfallAttackIds.PowerOut, _power!.PowerRelease, delivers: true);

    /// <summary>
    /// Starts a delivering motion through admission. A refused attack leaves the swing it would have cut to finish, and
    /// a refused release leaves the arms at rest.
    /// </summary>
    private bool Begin(string attack, DaggerfallAttackTiming timing, bool delivers)
    {
        bool owed = _restOwed;
        _restOwed = false;
        if (!attacks.Start(attack))
        {
            if (_playing is { Attack: DaggerfallAttackIds.PowerIn or DaggerfallAttackIds.PowerHold }) { _playing = null; attacks.Rest(); }
            else if (owed) attacks.Rest();
            return false;
        }
        _playing = new(attack, timing, delivers);
        attacks.Motion(attack, timing);
        return true;
    }

    /// <summary>Retires what is in progress without landing it: a site change, a held press lost, or the player's defeat.</summary>
    internal void Cancel()
    {
        bool playing = _playing is not null || _restOwed;
        _playing = null;
        _restOwed = false;
        _press = null;
        _kick = false;
        _chain = 0;
        if (playing) attacks.Rest();
    }
}
