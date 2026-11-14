using Rusty.Engine;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>What the attack clock asks of the player's attack around one admitted step.</summary>
internal interface IDaggerfallPlayerAttacks
{
    /// <summary>The skill of the weapon in hand, whose class sets the timing.</summary>
    string WeaponSkill { get; }

    /// <summary>How many fast attacks the character can chain with this weapon.</summary>
    int ChainLength { get; }

    /// <summary>Whether the character is too winded for a power attack.</summary>
    bool Winded { get; }

    /// <summary>What the character's attributes, skill and state scale an attack's time by.</summary>
    double TimeScale(string attack);

    /// <summary>How a bow's shot is drawn and loosed, or null when the hands hold no bow.</summary>
    DaggerfallShotTuning? Shot { get; }

    /// <summary>Whether a shot could leave now, so that drawing one means something.</summary>
    bool ShotReady { get; }

    /// <summary>Starts an attack through admission; false when it was refused and no motion plays.</summary>
    bool Start(string attack);

    /// <summary>Sweeps the started attack on to <paramref name="cycle"/> (0 to 1) of its hit window; true once it has met something.</summary>
    bool Sweep(double cycle);

    /// <summary>The started attack's hit window is over or it met something: its blow resolves now.</summary>
    void Impact();

    /// <summary>A motion began: presentation plays it. A null timing is an untimed hold.</summary>
    void Motion(string attack, DaggerfallAttackTiming? timing);

    /// <summary>The last motion ended with nothing after it: presentation returns to rest.</summary>
    void Rest();
}

/// <summary>
/// The player's attack as the rules time it. With a bow a press draws, and the arrow leaves once it is drawn and let go.
/// Otherwise a press released before the power threshold is a fast attack and one
/// held past it charges a power attack, which holds until release. A press during a recovery chains the next fast
/// attack at once, and one made earlier in a swing waits for the recovery; a swing that runs out resets the chain.
/// The hit window opens as the windup ends and the attack sweeps through it, with or without anything drawing it, and
/// the blow resolves when it meets something or the window closes. A cast is a commitment: it can start from a swing's
/// recovery, as the next fast attack can, its spell releases as its windup ends, and no attack starts until it has
/// recovered. The clock is advanced by admitted steps only, so held time holds it, and it is transient: a save or a
/// site change retires what is in progress.
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
    private bool _cast;
    private int _chain;
    // A motion ended with a press about to follow, so the rest it owed waits on whether that press starts.
    private bool _restOwed;
    // What a drawn shot or charged power attack plays when let go, as it started.
    private (string Attack, DaggerfallAttackTiming Timing)? _release;

    /// <summary>Whether a motion is playing or a press is waiting to become one.</summary>
    internal bool Busy => _playing is not null || _press is not null || _kick || _cast;

    /// <summary>Whether a cast is playing or waiting to start, which needs no weapon drawn.</summary>
    internal bool Casting => _cast || _playing is { Attack: DaggerfallAttackIds.Cast };

    /// <summary>Whether a new attack could start this step.</summary>
    internal bool Free => _playing is null or { InRecovery: true, Attack: not (DaggerfallAttackIds.Shot or DaggerfallAttackIds.Cast) };

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
        if (_playing is not { Attack: DaggerfallAttackIds.PowerIn or DaggerfallAttackIds.PowerHold or DaggerfallAttackIds.ShotDraw }) return;
        _playing = null;
        attacks.Rest();
    }

    private void Release()
    {
        _held = false;
        _pressSpent = false;
    }

    /// <summary>The attack in motion, or null at rest.</summary>
    internal string? Current => _playing?.Attack;

    /// <summary>The attack was requested this step.</summary>
    internal void Press()
    {
        if (!_pressSpent) _press ??= 0d;
    }

    /// <summary>The kick was requested this step.</summary>
    internal void PressKick() => _kick = true;

    /// <summary>A cast was requested this step.</summary>
    internal void PressCast() => _cast = true;

    /// <summary>Advances one admitted step: the motion in progress, then whatever the input now starts.</summary>
    internal void Step(double seconds)
    {
        bool struck = false;
        if (_playing is { } playing)
        {
            playing.Seconds += seconds;
            if (playing.Delivers && !playing.Landed && playing.Seconds >= playing.Timing!.Value.Windup)
            {
                double cycle = Math.Min(1d, (playing.Seconds - playing.Timing.Value.Windup) / playing.Timing.Value.Commit);
                if (attacks.Sweep(cycle) || cycle >= 1d)
                {
                    playing.Landed = struck = true;
                    attacks.Impact();
                }
            }
            if (playing.Attack == DaggerfallAttackIds.PowerHold)
            {
                if (!_held) Loose();
            }
            else if (playing.Seconds >= playing.Timing!.Value.Duration)
            {
                if (playing.Attack is DaggerfallAttackIds.PowerIn or DaggerfallAttackIds.ShotDraw) Charged();
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
        // A blow resolves at the head of the next update, and the rules admit nothing until it has.
        if (!Free || struck) return;
        if (_cast) StartCast();
        else if (_kick) StartKick();
        else if (_press is double press && (!_held || press >= tuning.PowerThresholdSeconds && !attacks.Winded || attacks.Shot is not null)) StartPressed(!_held);
    }

    /// <summary>Whether a press or kick is about to start a motion, so the rest between them is skipped.</summary>
    private bool Waiting => _cast || _kick || _press is not null && !_held;

    private void StartCast()
    {
        _cast = false;
        Begin(DaggerfallAttackIds.Cast, tuning.Cast, delivers: true);
    }

    private void StartKick()
    {
        _kick = false;
        Begin(DaggerfallAttackIds.Kick, tuning.Kick, delivers: true);
    }

    private void StartPressed(bool tapped)
    {
        _press = null;
        if (attacks.Shot is { } shot)
        {
            _pressSpent = _held;
            // A shot that couldn't leave isn't drawn: the refusal says why.
            if (!attacks.ShotReady) { Begin(DaggerfallAttackIds.Shot, shot.Release, delivers: true); return; }
            _restOwed = false;
            _release = (DaggerfallAttackIds.Shot, shot.Release);
            _playing = new(DaggerfallAttackIds.ShotDraw, Scaled(DaggerfallAttackIds.ShotDraw, new(shot.Draw, 0d, 0d)), Delivers: false);
            attacks.Motion(DaggerfallAttackIds.ShotDraw, _playing.Timing);
            return;
        }
        DaggerfallWeaponClassAttacks weapon = tuning.For(attacks.WeaponSkill);
        if (tapped)
        {
            if (_chain >= attacks.ChainLength) _chain = 0;
            if (Begin(DaggerfallAttackIds.Fast[_chain], weapon.Fast, delivers: true)) _chain = (_chain + 1) % attacks.ChainLength;
            return;
        }
        _pressSpent = true;
        _chain = 0;
        _release = (DaggerfallAttackIds.PowerOut, weapon.PowerRelease);
        _playing = new(DaggerfallAttackIds.PowerIn, Scaled(DaggerfallAttackIds.PowerIn, new(weapon.PowerCharge, 0d, 0d)), Delivers: false);
        attacks.Motion(DaggerfallAttackIds.PowerIn, _playing.Timing);
    }

    private void Charged()
    {
        if (!_held) { Loose(); return; }
        _playing = new(DaggerfallAttackIds.PowerHold, null, Delivers: false);
        attacks.Motion(DaggerfallAttackIds.PowerHold, null);
    }

    private void Loose() => Begin(_release!.Value.Attack, _release.Value.Timing, delivers: true);

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
            if (_playing is { Attack: DaggerfallAttackIds.PowerIn or DaggerfallAttackIds.PowerHold or DaggerfallAttackIds.ShotDraw }) { _playing = null; attacks.Rest(); }
            else if (owed) attacks.Rest();
            return false;
        }
        _playing = new(attack, Scaled(attack, timing), delivers);
        attacks.Motion(attack, _playing.Timing);
        return true;
    }

    private DaggerfallAttackTiming Scaled(string attack, DaggerfallAttackTiming timing)
    {
        double scale = attacks.TimeScale(attack);
        return new(timing.Windup * scale, timing.Commit * scale, timing.Recovery * scale);
    }

    /// <summary>Retires what is in progress without landing it: a site change, a held press lost, or the player's defeat.</summary>
    internal void Cancel()
    {
        bool playing = _playing is not null || _restOwed;
        _playing = null;
        _restOwed = false;
        _press = null;
        _kick = false;
        _cast = false;
        _chain = 0;
        if (playing) attacks.Rest();
    }
}
