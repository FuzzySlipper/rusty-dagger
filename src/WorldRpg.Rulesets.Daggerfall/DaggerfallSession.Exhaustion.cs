using Rusty.Engine;
using WorldRpg.Kit;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The donor's fatigue consequences: a Swimming roll for each swimming minute, and the collapse when
/// the fatigue pool empties (PlayerEntity.PlayerEntity_OnExhausted).
/// </summary>
internal sealed partial class DaggerfallSession
{
    private static readonly TrackId ExhaustionFatigueTrack = TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value);
    private static readonly TrackId ExhaustionHealthTrack = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private const string ExhaustedSafelyText = "1071";
    private const string ExhaustedNearEnemiesText = "1072";
    private const string ExhaustedInWaterText = "exhaustedInWater";

    /// <summary>
    /// The line a death caused by exhaustion keeps on the status line when the product enters Dead, in
    /// place of the generic death line; cleared when play resumes or the title returns.
    /// </summary>
    private string? _exhaustionDeathLine;

    /// <summary>
    /// Whether one swimming minute charges the swimming fatigue loss: the donor's
    /// <c>Dice100.FailedRoll</c> against the live Swimming skill, which an Argonian never pays.
    /// </summary>
    internal bool SwimmingFatigueApplies(long minute)
    {
        if (State.Character.Race.Id == "argonian") return false;
        int swimming = State.Actors.Player.Stats.GetStat(StatId.Parse("swimming")).ValueInt;
        int roll = checked((int)_random.DrawKeyed(new KeyedRngRequest(
            CombatRandomKey.Seed, "daggerfall.swimming-fatigue.v1", $"minute:{minute}", 1, 100)).Value);
        return roll > swimming;
    }

    /// <summary>
    /// Collapses a living player whose fatigue pool is empty, once every fatigue sink of the admitted
    /// update has settled. Out of water with no enemy near, the player drops for one hour, recovers one
    /// rest hour of health, fatigue and spell points and tallies Medical; in water or near an enemy the
    /// collapse is fatal. Either outcome leaves the pool non-empty or the player dead, so a pool that
    /// stays empty never collapses twice.
    /// </summary>
    internal void ResolveExhaustion()
    {
        if (_mode != ProductMode.Playing || State.Actors.Player.IsDefeated) return;
        StatsComponent player = State.Actors.Player.Stats;
        if (player.GetTrack(ExhaustionHealthTrack).Current <= 0d || player.GetTrack(ExhaustionFatigueTrack).Current > 0d) return;

        // The donor cancels movement as the player drops.
        _input.Neutralize();
        _locomotion.Neutralize();
        bool swimming = State.Swimming.IsSwimming;
        bool enemiesNearby = HasNearbyRestEnemy();
        if (swimming || enemiesNearby)
        {
            _exhaustionDeathLine = ExhaustionLine(swimming
                ? new DaggerfallTextKey(DaggerfallTextKind.Internal, ExhaustedInWaterText)
                : new DaggerfallTextKey(DaggerfallTextKind.Resource, ExhaustedNearEnemiesText));
            AppendDamage(_vitality.ResolveExhaustionDeath(State.Actors.Player.Actor), DaggerfallDamageCause.Exhaustion, 0);
            DeliverFacts();
            // The collapse text stands over the death's own damage line.
            Presentation.SetOutcome(_exhaustionDeathLine);
            return;
        }

        _ = AdvanceElapsedTime(DaggerfallRestPolicy.SecondsPerRestHour, idleFatigue: false);
        // A consequence of the hour itself (a poison round, a quest) may have ended the player; recovery
        // never resurrects an accepted death.
        DeliverFacts();
        if (State.Actors.Player.IsDefeated || player.GetTrack(ExhaustionHealthTrack).Current <= 0d) return;
        (bool rapidHealing, bool noRegeneration) = RestCharacterTraits();
        DaggerfallRestRecoveryModule.RecoverOneHour(player,
            player.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt,
            player.GetStat(StatId.Parse("medical")).ValueInt,
            rapidHealing, noRegeneration);
        State.SkillUses.Record(new DaggerfallSkillUse("medical", DaggerfallSkillUseReason.MedicalRest, DaggerfallSkillUseOutcome.Accepted));
        DeliverFacts();
        Presentation.SetOutcome(ExhaustionLine(new DaggerfallTextKey(DaggerfallTextKind.Resource, ExhaustedSafelyText)));
    }

    /// <summary>The donor's published collapse text, or a plain report when the pack carries none.</summary>
    private string ExhaustionLine(DaggerfallTextKey key)
    {
        string text = _definitions.TextPresentation.Resolve(key, DaggerfallTextContext.Empty).Text.Trim();
        return text.Length != 0 ? text : "You drop to the ground, completely exhausted.";
    }
}
