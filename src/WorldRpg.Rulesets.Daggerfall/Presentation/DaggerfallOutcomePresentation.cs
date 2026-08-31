using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Daggerfall-specific attack and reward wording; generic presentation state only stores the resolved message.</summary>
internal sealed class DaggerfallOutcomePresentation(PresentationState presentation, IReadOnlyDictionary<long, DaggerfallActorDefinition> actors)
{
    internal void React(IProductFact fact)
    {
        switch (fact)
        {
            case AttackRejectedFact rejected:
                presentation.SetOutcome(rejected.Reason switch
                {
                    AttackRejection.MissingPlayerPosition => "No authored player position",
                    AttackRejection.NoTargetInReach => "No target in melee reach",
                    AttackRejection.Cooldown => "Cooldown",
                    AttackRejection.InsufficientStamina => "Too exhausted to attack",
                    AttackRejection.InsufficientWeaponMaterial => "Weapon material cannot harm this target",
                    AttackRejection.TargetDefeated => "Target already defeated",
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
                presentation.SetOutcome($"{presentation.LastOutcome}; looted {loot.Quantity} {loot.ItemId}");
                break;
            case CorpseSearchedEmptyFact:
                presentation.SetOutcome("Corpse is empty");
                break;
        }
    }

    private bool Actor(long entityId, out DaggerfallActorDefinition definition) => actors.TryGetValue(entityId, out definition!);
}
