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
                    _ => "No target in melee reach",
                });
                break;
            case AttackMissedFact missed when Actor(missed.ActorId, out DaggerfallActorDefinition definition):
                presentation.SetOutcome(missed.EnemyAttack ? $"{definition.Id} missed ({missed.Roll} vs {missed.Chance})" : $"Missed {definition.Id} ({missed.Roll} vs {missed.Chance})");
                break;
            case AttackHitFact hit when Actor(hit.AttackerId, out DaggerfallActorDefinition definition):
                presentation.SetOutcome(hit.EnemyAttack ? $"{definition.Id} hit for {hit.Damage} damage" : $"Hit {definition.Id} for {hit.Damage} damage");
                break;
            case ActorDiedFact died when Actor(died.ActorId, out DaggerfallActorDefinition definition):
                presentation.SetOutcome($"Defeated {definition.Id} for {died.AppliedDamage} damage; gained {definition.Rewards.ExperienceReward} XP");
                break;
            case LootAwardedFact loot:
                presentation.SetOutcome($"{presentation.LastOutcome}; looted {loot.Quantity} {loot.ItemId}");
                break;
        }
    }

    private bool Actor(long entityId, out DaggerfallActorDefinition definition) => actors.TryGetValue(entityId, out definition!);
}
