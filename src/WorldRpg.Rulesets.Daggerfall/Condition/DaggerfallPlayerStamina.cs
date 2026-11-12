using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// The player's combat Stamina between spends: it keeps the pool's maximum, which follows Strength, Endurance and
/// Fatigue, and refills the pool once the player has stopped spending it. Anything that spends Stamina does so on the
/// track; a drop since the last step is what restarts the quiet period.
/// </summary>
internal sealed class DaggerfallPlayerStamina(DaggerfallStaminaTuning tuning)
{
    private static readonly TrackId Stamina = TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value);
    private static readonly TrackId Fatigue = TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value);

    private readonly PassiveTrackRecovery _recovery = new(tuning.RefillPerSecond, tuning.QuietSeconds);
    private double _lastSeen;

    internal void Step(StatsComponent player, double seconds)
    {
        Track stamina = player.GetTrack(Stamina);
        if (stamina.Current < _lastSeen) _recovery.RestartQuietPeriod();
        Refresh(player);
        if (seconds > 0d) _recovery.Advance(stamina, seconds * tuning.RefillScale(FatigueShare(player)));
        _lastSeen = stamina.Current;
    }

    /// <summary>Sets the pool's maximum from the player's attributes and Fatigue.</summary>
    internal void Refresh(StatsComponent player) =>
        player.GetStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value)).BaseValue = tuning.Maximum(
            player.GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).ValueInt,
            player.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt,
            FatigueShare(player));

    /// <summary>Refills the pool outright, for time that passes outside admitted updates.</summary>
    internal void Fill(StatsComponent player)
    {
        Refresh(player);
        Track stamina = player.GetTrack(Stamina);
        stamina.SetCurrent(stamina.Maximum.Value);
    }

    private static double FatigueShare(StatsComponent player)
    {
        Track fatigue = player.GetTrack(Fatigue);
        return fatigue.Maximum.Value > 0d ? 1d - fatigue.Current / fatigue.Maximum.Value : 0d;
    }
}
