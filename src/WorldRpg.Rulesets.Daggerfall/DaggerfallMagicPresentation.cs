using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Status values are rebuilt from canonical active effects, including restored sources.</summary>
internal static class DaggerfallMagicPresentation
{
    internal const string Owner = "magic.effects";
    internal static void Publish(DaggerfallEffectLifecycle effects, PlayerActorState player, PresentationSlots slots)
    {
        slots.RetireOwner(Owner);
        if (player.IsDefeated) return;
        foreach (var effect in effects.Active.Where(effect => effect.Context.Target.Value == checked((ulong)player.DurableId)
            && effect.Definition.ShowSpellIcon && effect.Definition.DoorMagic == DaggerfallDoorMagic.None
            && effect.Definition.Perception.Concealment == DaggerfallConcealment.None))
        {
            string duration = effect.Lifecycle.RemainingRounds is uint rounds ? $"{rounds} magic rounds remaining." : "Active until removed.";
            string source = effect.Context.Item is { } item ? $"Item {item.Value}. " : "";
            slots.Publish(new(Owner, effect.Context.Instance.Value, effect.BundleName ?? effect.Definition.Key,
                $"{effect.Definition.Key}. {source}{duration}", 35));
        }
    }
}
