using System.Text.Json.Serialization;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The player's hire as a guard of the ration convoy on band <paramref name="BandId"/> (#11182), taken from
/// <paramref name="Giver"/>'s factor for <paramref name="Pay"/> gold on arrival; <paramref name="DroveOff"/> once they
/// drove off an ambush on it, which earns the bonus too.
/// </summary>
internal sealed record DaggerfallConvoyGuardHire(
    [property: JsonRequired] string BandId,
    [property: JsonRequired] string Giver,
    [property: JsonRequired] int Pay,
    bool DroveOff = false);

/// <summary>The court of seat <paramref name="Seat"/> refuses the player convoy work until day <paramref name="UntilDay"/>: they deserted one of its convoys (#11182).</summary>
internal sealed record DaggerfallConvoyWorkBar(
    [property: JsonRequired] string Seat,
    [property: JsonRequired] long UntilDay);

/// <summary>A factor's offer of the guard job on the convoy on band <paramref name="BandId"/> (#11182), as the conversation lists it.</summary>
internal sealed record DaggerfallConvoyGuardOffer(string BandId, string Label);

/// <summary>The factor's answer to the player asking for the guard job: whether they were hired, what they asked and what they were told.</summary>
internal sealed record DaggerfallConvoyGuardReply(bool Accepted, string Question, string Message);

/// <summary>The convoy-guard job's saved state: the hire the player holds, if any, and the courts that refuse them work.</summary>
internal sealed record DaggerfallConvoyGuardSave(
    DaggerfallConvoyGuardHire? Hire,
    [property: JsonRequired] DaggerfallConvoyWorkBar[] Bars);

/// <summary>
/// Hiring on as a ration convoy's guard (#11182, design <c>wiki/rusty-dagger/ration-convoys</c>, "Hiring on as a guard").
/// A giver's factor hires the player while its convoy musters in town. The job pays on arrival, with a bonus for an
/// ambush driven off, if the player rode in with the convoy; a convoy lost, or one the player left at a stop or let go on
/// without them, pays nothing. Raiding the convoy they were hired to guard is desertion: on top of the raid, a heavier
/// loss of standing with its giver, whose court then refuses them convoy work for a while.
/// </summary>
/// <param name="tuning">The job's pay, bonus, desertion standing and bar.</param>
/// <param name="today">The day number now.</param>
/// <param name="pay">Gives the player gold.</param>
/// <param name="standing">Changes the player's standing with a seat's office faction by an amount.</param>
internal sealed class DaggerfallConvoyGuardJob(DaggerfallPoliticsTuning tuning, Func<long> today, Action<int> pay, Action<string, int> standing)
{
    private readonly List<DaggerfallConvoyWorkBar> _bars = [];
    private DaggerfallConvoyGuardHire? _hire;

    /// <summary>The convoy the player is hired to guard, if any.</summary>
    internal DaggerfallConvoyGuardHire? Hire => _hire;

    /// <summary>Whether <paramref name="seat"/>'s court refuses the player convoy work today.</summary>
    internal bool Refuses(string seat) => _bars.Any(bar => bar.Seat == seat && bar.UntilDay > today());

    /// <summary>Whether the player can be hired to guard <paramref name="convoy"/>: holding no other hire, and not refused by its giver.</summary>
    internal bool CanTake(DaggerfallRationConvoySave convoy) => _hire is null && !Refuses(convoy.Giver);

    /// <summary>Hires the player to guard <paramref name="convoy"/>; false where they cannot be.</summary>
    internal bool Take(DaggerfallRationConvoySave convoy)
    {
        if (!CanTake(convoy)) return false;
        _hire = new DaggerfallConvoyGuardHire(convoy.BandId, convoy.Giver, tuning.ConvoyGuardPay);
        return true;
    }

    /// <summary>Whether the player is hired to guard the convoy on band <paramref name="bandId"/>.</summary>
    internal bool Guards(string bandId) => _hire is { } hire && StringComparer.Ordinal.Equals(hire.BandId, bandId);

    /// <summary>The player leaves the job, forfeiting its pay.</summary>
    internal bool Quit()
    {
        if (_hire is null) return false;
        _hire = null;
        return true;
    }

    /// <summary>The player drove off an ambush on the convoy on band <paramref name="bandId"/>: the bonus is earned with the pay.</summary>
    internal void DroveOff(string bandId)
    {
        if (Guards(bandId)) _hire = _hire! with { DroveOff = true };
    }

    /// <summary>
    /// The convoy on band <paramref name="bandId"/> reached its destination, <paramref name="rodeIn"/> when the player rode
    /// in with it: then the job pays, with the bonus for an ambush driven off. Returns the gold paid, or null when the
    /// player was not hired to guard it; a guard who did not ride in is paid nothing, and the job ends either way.
    /// </summary>
    internal int? Arrived(string bandId, bool rodeIn)
    {
        if (!Guards(bandId)) return null;
        DaggerfallConvoyGuardHire hire = _hire!;
        _hire = null;
        if (!rodeIn) return 0;
        int paid = hire.Pay + (hire.DroveOff ? tuning.ConvoyGuardBonus : 0);
        if (paid > 0) pay(paid);
        return paid;
    }

    /// <summary>The convoy on band <paramref name="bandId"/> was lost: a guard of it is paid nothing.</summary>
    internal void Lost(string bandId)
    {
        if (Guards(bandId)) _hire = null;
    }

    /// <summary>
    /// The player raided the convoy on band <paramref name="bandId"/>: if they were hired to guard it, they deserted, losing
    /// standing with its giver beyond the raid's and barred from its court's convoy work for the tuned days.
    /// </summary>
    internal bool Deserted(string bandId)
    {
        if (!Guards(bandId)) return false;
        string giver = _hire!.Giver;
        _hire = null;
        standing(giver, -tuning.ConvoyDesertionStanding);
        _bars.RemoveAll(bar => bar.Seat == giver);
        _bars.Add(new DaggerfallConvoyWorkBar(giver, checked(today() + tuning.ConvoyDesertionBarDays)));
        return true;
    }

    internal DaggerfallConvoyGuardSave Capture() =>
        new(_hire, [.. _bars.Where(bar => bar.UntilDay > today()).OrderBy(bar => bar.Seat, StringComparer.Ordinal)]);

    internal void Restore(DaggerfallConvoyGuardSave? saved)
    {
        _bars.Clear();
        _hire = null;
        if (saved is null) return;
        if (saved.Hire is { } hire)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(hire.BandId);
            ArgumentException.ThrowIfNullOrWhiteSpace(hire.Giver);
            ArgumentOutOfRangeException.ThrowIfNegative(hire.Pay);
            _hire = hire;
        }
        foreach (DaggerfallConvoyWorkBar bar in saved.Bars)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(bar.Seat);
            _bars.Add(bar);
        }
    }
}
