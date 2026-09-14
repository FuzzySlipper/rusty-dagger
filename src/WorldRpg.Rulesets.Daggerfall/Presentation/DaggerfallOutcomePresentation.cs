using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Daggerfall-specific attack and reward wording; generic presentation state only stores the resolved message.</summary>
internal sealed class DaggerfallOutcomePresentation(
    PresentationState presentation,
    IReadOnlyDictionary<long, DaggerfallActorDefinition> actors,
    Func<DaggerfallMeleeTargetingEvidence?>? meleeEvidence = null)
{
    internal void React(IProductFact fact)
    {
        switch (fact)
        {
            case AttackRejectedFact rejected:
                presentation.SetOutcome(rejected.Reason switch
                {
                    AttackRejection.MissingPlayerPosition => "No authored player position",
                    AttackRejection.NoTargetInReach => NothingInMeleeReach(),
                    AttackRejection.Cooldown => "Cooldown",
                    AttackRejection.InsufficientStamina => "Too exhausted to attack",
                    AttackRejection.InsufficientWeaponMaterial => "Weapon material cannot harm this target",
                    AttackRejection.TargetDefeated => "Target already defeated",
                    // An actor that reached its swing with no authored policy is a missing capability,
                    // not a silent miss, so the line says which actor and what is missing.
                    AttackRejection.NoAttackPolicy => rejected.ActorId is { } attacker ? $"No authored attack for {Name(attacker)}" : "No authored attack policy",
                    _ => "Melee request rejected",
                });
                break;
            case AttackMissedFact missed when Actor(missed.TargetId, out DaggerfallActorDefinition definition):
                presentation.SetOutcome(missed.EnemyAttack ? $"Missed {definition.Id.Value} ({missed.Roll} vs {missed.Chance})" : $"Missed {definition.Id.Value} ({missed.Roll} vs {missed.Chance})");
                break;
            case AttackHitFact hit when Actor(hit.TargetId, out DaggerfallActorDefinition definition):
                presentation.SetOutcome(hit.EnemyAttack ? $"Hit {definition.Id.Value} for {hit.Damage} damage" : $"Hit {definition.Id.Value} for {hit.Damage} damage");
                break;
            case ActorDiedFact died when Actor(died.ActorId, out DaggerfallActorDefinition definition):
                presentation.SetOutcome($"Defeated {definition.Id} for {died.AppliedDamage} damage; gained {definition.Rewards.ExperienceReward} XP");
                break;
            case LootAwardedFact loot:
                presentation.AppendOutcome($"looted {loot.Quantity} {loot.ItemId}");
                break;
            case CorpseSearchedEmptyFact:
                presentation.SetOutcome("Corpse is empty");
                break;
        }
    }

    /// <summary>
    /// A melee request that found nothing has to say what the query actually saw. The Engine already
    /// returns those counts on the receipt, and a miss that drops them is indistinguishable from a
    /// world where nothing is visible, which is the confusion this line exists to end.
    /// </summary>
    private string NothingInMeleeReach()
    {
        if (meleeEvidence?.Invoke() is not { } evidence) return "No target in melee reach";
        PerceptionReadoutLeaseReceipt receipt = evidence.Receipt;
        return $"No target in melee reach ({receipt.SelectedObservers} observer(s) against {receipt.SelectedTargets} target(s), {receipt.SelectionComparisons} compared: {receipt.DistanceRejects} out of range, {receipt.FacingRejects} out of cone, {receipt.VisibilityCasts} cast, {receipt.OcclusionRejects} occluded)";
    }

    private string Name(long entityId) => Actor(entityId, out DaggerfallActorDefinition definition) ? definition.Id.Value : $"actor {entityId}";

    private bool Actor(long entityId, out DaggerfallActorDefinition definition) => actors.TryGetValue(entityId, out definition!);
}
