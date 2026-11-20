using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The rule numbers of the scored tactics (the intelligent tactic and the beast preset): how much each kind of option is
/// worth, the noise that keeps two alike enemies from moving in step, when and how far a hurt enemy flees and how long
/// it then leaves its target alone, and the tendencies each preset assumes where its mobile's record states none.
/// </summary>
/// <param name="Noise">The most keyed noise added to any option's score.</param>
/// <param name="CastIntervalSeconds">The least time between one enemy's spells, so a caster does not spend its magicka in a burst.</param>
internal sealed record DaggerfallEnemyTacticTuning(
    double Noise,
    float CastIntervalSeconds,
    DaggerfallEnemyTacticWeights Weights,
    DaggerfallEnemyFleeTuning Flee,
    DaggerfallTacticTendencies Intelligent,
    DaggerfallBeastTuning Beast)
{
    internal static DaggerfallEnemyTacticTuning Default { get; } = new(.05d, 2.5f,
        new(Melee: .6d, Bow: .8d, Spell: 1d, Reposition: .7d, Flee: 1d, Finisher: .2d, Closing: .7d),
        new(Health: .6d, AllyCourage: .15d, AllyRadius: 8d, Distance: 15f, Seconds: 6f, SpeedMultiplier: 1f, DisengageMinutes: 15, Arrival: 1.5d),
        new(Courage: .5d, Discipline: .5d),
        new(new(Courage: .25d, Discipline: 0d), LungeDistance: 4.5d, LungeSpeedMultiplier: 1.6f));

    internal DaggerfallEnemyTacticTuning Validate()
    {
        if (!double.IsFinite(Noise) || Noise < 0d) throw new ArgumentOutOfRangeException(nameof(Noise));
        if (!float.IsFinite(CastIntervalSeconds) || CastIntervalSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(CastIntervalSeconds));
        Weights.Validate();
        Flee.Validate();
        if (!Intelligent.IsValid) throw new ArgumentOutOfRangeException(nameof(Intelligent));
        Beast.Validate();
        return this;
    }

    internal static DaggerfallEnemyTacticTuning Read(JsonElement section)
    {
        JsonElement weights = section.GetProperty("weights"), flee = section.GetProperty("flee"), beast = section.GetProperty("beast");
        return new DaggerfallEnemyTacticTuning(
            section.GetProperty("noise").GetDouble(),
            section.GetProperty("castIntervalSeconds").GetSingle(),
            new(weights.GetProperty("melee").GetDouble(), weights.GetProperty("bow").GetDouble(), weights.GetProperty("spell").GetDouble(),
                weights.GetProperty("reposition").GetDouble(), weights.GetProperty("flee").GetDouble(), weights.GetProperty("finisher").GetDouble(),
                weights.GetProperty("closing").GetDouble()),
            new(flee.GetProperty("health").GetDouble(), flee.GetProperty("allyCourage").GetDouble(), flee.GetProperty("allyRadius").GetDouble(),
                flee.GetProperty("distance").GetSingle(), flee.GetProperty("seconds").GetSingle(), flee.GetProperty("speedMultiplier").GetSingle(),
                flee.GetProperty("disengageMinutes").GetInt32(), flee.GetProperty("arrival").GetDouble()),
            Tendencies(section.GetProperty("intelligent")),
            new(Tendencies(beast), beast.GetProperty("lungeDistance").GetDouble(), beast.GetProperty("lungeSpeedMultiplier").GetSingle())).Validate();

        static DaggerfallTacticTendencies Tendencies(JsonElement value) =>
            new(value.GetProperty("courage").GetDouble(), value.GetProperty("discipline").GetDouble());
    }
}

/// <summary>
/// What each kind of option is worth before its considerations: a melee strike in reach, a bow shot, a spell, moving
/// for a clear lane, fleeing; what a nearly beaten target adds to an attack (<paramref name="Finisher"/>), and the
/// share of a melee strike's worth left to closing in on a target out of reach (<paramref name="Closing"/>).
/// </summary>
internal sealed record DaggerfallEnemyTacticWeights(double Melee, double Bow, double Spell, double Reposition, double Flee, double Finisher, double Closing)
{
    internal void Validate()
    {
        foreach (double value in new[] { Melee, Bow, Spell, Reposition, Flee, Finisher })
            if (!double.IsFinite(value) || value < 0d) throw new ArgumentOutOfRangeException(nameof(value));
        if (!double.IsFinite(Closing) || Closing is < 0d or > 1d) throw new ArgumentOutOfRangeException(nameof(Closing));
    }
}

/// <summary>
/// When a hurt enemy flees and what it then does. It may flee below <paramref name="Health"/> of its health, a
/// threshold its courage and each ally fighting beside it within <paramref name="AllyRadius"/> metres
/// (<paramref name="AllyCourage"/> apiece, up to three) lower. It runs <paramref name="Distance"/> metres by
/// navigation for at most <paramref name="Seconds"/>, arriving within <paramref name="Arrival"/> metres, then leaves
/// that target alone for <paramref name="DisengageMinutes"/> game minutes unless it is struck.
/// </summary>
internal sealed record DaggerfallEnemyFleeTuning(double Health, double AllyCourage, double AllyRadius, float Distance, float Seconds,
    float SpeedMultiplier, int DisengageMinutes, double Arrival)
{
    internal void Validate()
    {
        if (!double.IsFinite(Health) || Health is < 0d or > 1d) throw new ArgumentOutOfRangeException(nameof(Health));
        if (!double.IsFinite(AllyCourage) || AllyCourage is < 0d or > 1d) throw new ArgumentOutOfRangeException(nameof(AllyCourage));
        foreach (double value in new[] { AllyRadius, Distance, Seconds, SpeedMultiplier, Arrival })
            if (!double.IsFinite(value) || value <= 0d) throw new ArgumentOutOfRangeException(nameof(value));
        if (DisengageMinutes < 0) throw new ArgumentOutOfRangeException(nameof(DisengageMinutes));
    }
}

/// <summary>
/// The beast preset's fixed numbers: its tendencies, which no mobile record changes, and the lunge it makes at a gap
/// (a target paralysed, turned away or with its guard down) from within <paramref name="LungeDistance"/> metres.
/// </summary>
internal sealed record DaggerfallBeastTuning(DaggerfallTacticTendencies Tendencies, double LungeDistance, float LungeSpeedMultiplier)
{
    internal void Validate()
    {
        if (!Tendencies.IsValid) throw new ArgumentOutOfRangeException(nameof(Tendencies));
        if (!double.IsFinite(LungeDistance) || LungeDistance <= 0d) throw new ArgumentOutOfRangeException(nameof(LungeDistance));
        if (!float.IsFinite(LungeSpeedMultiplier) || LungeSpeedMultiplier <= 0f) throw new ArgumentOutOfRangeException(nameof(LungeSpeedMultiplier));
    }
}
