using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// Applies the donor's hourly rest recovery around the session's single elapsed-time callback.
/// </summary>
/// <remarks>
/// This module never advances a clock itself. The callback is the session's existing calendar entry,
/// so quests, effects, social time, and encounter deadlines observe exactly the same elapsed seconds.
/// Ten-minute callbacks are retained because the donor can interrupt between recovery hours.
/// </remarks>
internal sealed class DaggerfallRestRecoveryModule
{
    internal DaggerfallRestResult Apply(
        StatsComponent player,
        DaggerfallRestRequest request,
        DaggerfallRestEligibility eligibility,
        int endurance,
        int medical,
        bool rapidHealing,
        bool noRegeneration,
        Func<long, DaggerfallRestTimeAdvance> advanceTime,
        Action? recordMedicalRest = null,
        DaggerfallFormulaTuning? tuning = null,
        Action? advanceSkills = null,
        Func<(int Endurance, int Medical, bool RapidHealing, bool NoRegeneration)>? currentRecoveryInputs = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(advanceTime);
        recordMedicalRest ??= static () => { };
        request.Validate();
        eligibility.Validate();

        if (!eligibility.IsAlive)
            return DaggerfallRestResult.Rejected(request.Mode, eligibility.Message ?? "You cannot rest while defeated.") with
            {
                Interruption = DaggerfallRestInterruption.Defeated,
            };
        if (!eligibility.Allowed)
            return DaggerfallRestResult.Rejected(request.Mode, eligibility.Message ?? "You cannot rest here.");

        if (request.Mode == DaggerfallRestMode.UntilHealed && DaggerfallRestPolicy.IsFullyRecovered(player, noRegeneration))
            return new(true, request.Mode, 0, 0, 0, 0, 0, 0, DaggerfallRestInterruption.None, "You are already fully recovered.");

        long requestedSeconds = request.Mode switch
        {
            DaggerfallRestMode.Timed or DaggerfallRestMode.Loiter => checked((long)request.Hours * DaggerfallRestPolicy.SecondsPerRestHour),
            DaggerfallRestMode.UntilHealed => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        long elapsed = 0;
        int recoveryHours = 0;
        int healthBefore = Current(player, DaggerfallMechanicsIds.Health);
        int fatigueBefore = Current(player, DaggerfallMechanicsIds.Stamina);
        int spellBefore = Current(player, DaggerfallMechanicsIds.Magicka);
        DaggerfallRestInterruption interruption = DaggerfallRestInterruption.None;

        while (request.Mode == DaggerfallRestMode.UntilHealed
            ? !DaggerfallRestPolicy.IsFullyRecovered(player, noRegeneration)
            : elapsed < requestedSeconds)
        {
            long remaining = request.Mode == DaggerfallRestMode.UntilHealed
                ? DaggerfallRestPolicy.SecondsPerRestTick
                : Math.Min(DaggerfallRestPolicy.SecondsPerRestTick, requestedSeconds - elapsed);
            DaggerfallRestTimeAdvance advance = advanceTime(remaining);
            advance.Validate();
            elapsed = checked(elapsed + advance.AppliedSeconds);

            // The donor checks for an enemy or prevented-rest condition at the hour boundary before
            // applying that hour's vitals. Preserve that source order when an interruption arrives on
            // the boundary itself.
            if (advance.Interruption != DaggerfallRestInterruption.None)
            {
                interruption = advance.Interruption;
                break;
            }

            int completeHours = checked((int)(elapsed / DaggerfallRestPolicy.SecondsPerRestHour));
            while (recoveryHours < completeHours)
            {
                recoveryHours++;
                if (request.Mode == DaggerfallRestMode.Loiter) continue;
                var inputs = currentRecoveryInputs?.Invoke()
                    ?? (endurance, medical, rapidHealing, noRegeneration);
                (int healthRate, int fatigueRate, int spellRate) = DaggerfallRestPolicy.RecoveryRates(
                    inputs.Endurance, inputs.Medical,
                    Maximum(player, DaggerfallMechanicsIds.HealthMaximum),
                    Maximum(player, DaggerfallMechanicsIds.StaminaMaximum),
                    Maximum(player, DaggerfallMechanicsIds.MagickaMaximum),
                    inputs.RapidHealing, inputs.NoRegeneration, tuning);
                Recover(player, healthRate, fatigueRate, spellRate);
                recordMedicalRest();
            }

            if (advance.AppliedSeconds == 0)
            {
                interruption = DaggerfallRestInterruption.Stopped;
                break;
            }
        }

        if (elapsed > 0) advanceSkills?.Invoke();

        return new(
            Accepted: true,
            Mode: request.Mode,
            RequestedSeconds: requestedSeconds,
            ElapsedSeconds: elapsed,
            RecoveryHours: recoveryHours,
            HealthRecovered: Current(player, DaggerfallMechanicsIds.Health) - healthBefore,
            FatigueRecovered: Current(player, DaggerfallMechanicsIds.Stamina) - fatigueBefore,
            SpellPointsRecovered: Current(player, DaggerfallMechanicsIds.Magicka) - spellBefore,
            Interruption: interruption,
            Message: Message(request.Mode, interruption, elapsed, recoveryHours));
    }

    private static void Recover(StatsComponent player, int health, int fatigue, int spellPoints)
    {
        Add(player.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)), health);
        Add(player.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)), fatigue);
        Add(player.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value)), spellPoints);
    }

    private static void Add(Track track, int amount)
    {
        if (amount <= 0) return;
        track.SetCurrent(checked(track.Current + amount), clamp: true);
    }

    private static int Current(StatsComponent stats, DaggerfallTrackId id) =>
        checked((int)stats.GetTrack(TrackId.Parse(id.Value)).Current);

    private static int Maximum(StatsComponent stats, DaggerfallStatId id) =>
        checked((int)stats.GetStat(StatId.Parse(id.Value)).ValueInt);

    private static string Message(DaggerfallRestMode mode, DaggerfallRestInterruption interruption, long elapsed, int recoveryHours)
    {
        if (interruption == DaggerfallRestInterruption.Encounter) return "Your rest was interrupted by an encounter.";
        if (interruption == DaggerfallRestInterruption.Prevented) return "You cannot continue resting here.";
        if (interruption == DaggerfallRestInterruption.Defeated) return "Your rest was interrupted.";
        if (interruption == DaggerfallRestInterruption.Stopped) return "Rest stopped.";
        if (mode == DaggerfallRestMode.Loiter) return $"Loitered for {elapsed / DaggerfallRestPolicy.SecondsPerRestHour} hour(s).";
        if (mode == DaggerfallRestMode.UntilHealed) return recoveryHours == 0 ? "You are already fully recovered." : "You are fully recovered.";
        return $"Rested for {elapsed / DaggerfallRestPolicy.SecondsPerRestHour} hour(s).";
    }
}
