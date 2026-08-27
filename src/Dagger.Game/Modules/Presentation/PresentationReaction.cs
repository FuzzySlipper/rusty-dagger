using RustyDagger.Game.Daggerfall;
using RustyDagger.Game.Facts;
using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.Combat;

namespace RustyDagger.Game.Modules.Presentation;

internal sealed class PresentationReaction(PresentationState presentation)
{
    internal void React(IProductFact fact, DaggerfallState state)
    {
        switch (fact)
        {
            case AttackRejectedFact rejected:
                presentation.SetOutcome(rejected.Reason switch
                {
                    AttackRejection.WeaponRecovering => "Weapon recovering",
                    AttackRejection.TooExhausted => "Too exhausted to attack",
                    AttackRejection.MissingPlayerPosition => "No authored player position",
                    _ => "No active encounter target in melee reach",
                });
                break;
            case AttackMissedFact missed when state.Actors.TryGet(missed.ActorId, out ActorState actor):
                presentation.SetOutcome(missed.EnemyAttack ? $"{actor.Definition.Id} missed ({missed.Roll} vs {missed.Chance})" : $"Missed {actor.Definition.Id} ({missed.Roll} vs {missed.Chance})");
                break;
            case AttackHitFact hit when state.Actors.TryGet(hit.AttackerId, out ActorState actor):
                presentation.SetOutcome(hit.EnemyAttack ? $"{actor.Definition.Id} hit for {hit.Damage} damage" : $"Hit {actor.Definition.Id} for {hit.Damage} damage");
                break;
            case ActorDiedFact died when state.Actors.TryGet(died.ActorId, out ActorState actor):
                presentation.SetOutcome($"Defeated {actor.Definition.Id} for {died.AppliedDamage} damage; gained {actor.Definition.ExperienceReward} XP");
                break;
            case LootAwardedFact loot:
                presentation.SetOutcome($"{presentation.LastOutcome}; looted {loot.Quantity} {loot.ItemId}");
                break;
        }
    }
}
