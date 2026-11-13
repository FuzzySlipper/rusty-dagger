using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What becoming a lycanthrope or a vampire heals: every classic disease and poison, every drained attribute,
/// and the player's vital tracks to their maxima.
/// </summary>
internal sealed class DaggerfallTransformationHealing(DaggerfallDiseases diseases, DaggerfallPoisoning poisoning,
    DaggerfallEffectLifecycle effects, ActorsState actors, DaggerfallCharacterState character)
{
    internal void Heal()
    {
        foreach (var disease in Enum.GetValues<DaggerfallClassicDisease>()) diseases.Cure(disease);
        poisoning.CurePlayer();
        foreach (var attribute in DaggerfallMechanicsIds.Attributes)
            DaggerfallAttributeDrainEffects.Heal(effects, DaggerfallActorIdentity.PlayerEntityId, attribute,
                int.MaxValue, () => character.Career);
        foreach (string track in new[] { "health", "fatigue", "stamina", "magicka" })
        {
            var value = actors.Player.Stats.GetTrack(TrackId.Parse(track));
            value.SetCurrent(value.Maximum.Value);
        }
    }
}
