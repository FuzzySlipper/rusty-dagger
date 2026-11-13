using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The player's condition outside a blow: Stamina between spends, the donor's Swimming roll for each swimming
/// minute, and the collapse when the Fatigue pool empties (PlayerEntity.PlayerEntity_OnExhausted). It is handed
/// the player's stats and what is known of the player's surroundings; what a collapse does in other domains
/// (movement stops, an hour passes, facts are delivered, the status line) is the session's collapse list,
/// <c>ResolveExhaustion</c> in DaggerfallSession.Update.cs.
/// </summary>
internal sealed class DaggerfallPlayerCondition(DaggerfallVitalityConsequences vitality, DaggerfallStaminaTuning stamina,
    DaggerfallRestTuning rest, IRandomService random, ulong randomSeed)
{
    private static readonly TrackId Fatigue = TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value);
    private static readonly TrackId Health = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private static readonly StatId Swimming = StatId.Parse("swimming");
    private const string ExhaustedSafelyText = "1071";
    private const string ExhaustedNearEnemiesText = "1072";
    private const string ExhaustedInWaterText = "exhaustedInWater";

    /// <summary>The player's combat Stamina between spends.</summary>
    internal DaggerfallPlayerStamina Stamina { get; } = new(stamina);

    /// <summary>
    /// The line a death caused by exhaustion keeps on the status line when the product enters Dead, in place of
    /// the generic death line; cleared when play resumes or the title returns.
    /// </summary>
    internal string? ExhaustionDeathLine { get; private set; }

    /// <summary>
    /// Whether one swimming minute charges the swimming fatigue loss: the donor's <c>Dice100.FailedRoll</c>
    /// against the live Swimming skill, which an Argonian never pays.
    /// </summary>
    internal bool SwimmingFatigueApplies(string raceId, StatsComponent player, long minute)
    {
        if (raceId == "argonian") return false;
        int swimming = player.GetStat(Swimming).ValueInt;
        int roll = checked((int)random.DrawKeyed(new KeyedRngRequest(
            randomSeed, "daggerfall.swimming-fatigue.v1", $"minute:{minute}", 1, 100)).Value);
        return roll > swimming;
    }

    /// <summary>Whether a living player's Fatigue pool is empty, so the player collapses.</summary>
    internal bool Exhausted(StatsComponent player) =>
        player.GetTrack(Health).Current > 0d && player.GetTrack(Fatigue).Current <= 0d;

    /// <summary>
    /// The collapse that cannot be survived, in water or with enemies near: the player's terminal accepted
    /// damage, and the donor's collapse text, which the death keeps as its line.
    /// </summary>
    internal (DamageResult Death, string Line) CollapseFatally(Actor player, bool inWater, DaggerfallTextResolver text)
    {
        string line = Line(text, inWater
            ? new DaggerfallTextKey(DaggerfallTextKind.Internal, ExhaustedInWaterText)
            : new DaggerfallTextKey(DaggerfallTextKind.Resource, ExhaustedNearEnemiesText));
        ExhaustionDeathLine = line;
        return (vitality.ResolveExhaustionDeath(player), line);
    }

    /// <summary>
    /// The safe collapse's hour of recovery: one rest hour of health, fatigue and spell points, Fatigue at a rough
    /// camp's share. False, recovering nothing, when the hour itself ended the player: recovery never resurrects
    /// an accepted death.
    /// </summary>
    internal bool RecoverFromCollapse(StatsComponent player, bool rapidHealing, bool noRegeneration)
    {
        if (player.GetTrack(Health).Current <= 0d) return false;
        DaggerfallRestRecoveryModule.RecoverOneHour(player,
            player.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt,
            player.GetStat(StatId.Parse("medical")).ValueInt,
            rapidHealing, noRegeneration, rest.InnFatigueMultiplier);
        return true;
    }

    /// <summary>The donor's line for waking from a safe collapse.</summary>
    internal string AwakeningLine(DaggerfallTextResolver text) =>
        Line(text, new DaggerfallTextKey(DaggerfallTextKind.Resource, ExhaustedSafelyText));

    internal void ClearExhaustionDeathLine() => ExhaustionDeathLine = null;

    /// <summary>The donor's published collapse text, or a plain report when the pack carries none.</summary>
    private static string Line(DaggerfallTextResolver text, DaggerfallTextKey key)
    {
        string resolved = text.Resolve(key, DaggerfallTextContext.Empty).Text.Trim();
        return resolved.Length != 0 ? resolved : "You drop to the ground, completely exhausted.";
    }
}
