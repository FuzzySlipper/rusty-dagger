using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Status values are rebuilt from canonical active effects, including restored sources.</summary>
internal static class DaggerfallMagicPresentation
{
    internal const string Owner = "magic.effects";
    internal static void Publish(DaggerfallEffectLifecycle effects, PlayerActorState player, PresentationSlots slots,
        Func<ulong, string> itemName)
    {
        slots.RetireOwner(Owner);
        if (player.IsDefeated) return;
        foreach (var effect in effects.Active.Where(effect => effect.Context.Target.Value == checked((ulong)player.DurableId)
            && effect.Definition.ShowSpellIcon && effect.Definition.DoorMagic == DaggerfallDoorMagic.None
            && effect.Definition.Perception.Concealment == DaggerfallConcealment.None))
        {
            // One magic round is one game minute.
            string duration = effect.Lifecycle.RemainingRounds is uint rounds
                ? DaggerfallCalendar.DescribeDuration(checked((long)rounds * DaggerfallCalendar.SecondsPerMinute)) + " remaining."
                : "Active until removed.";
            string source = effect.Context.Item is { } item ? $"From {itemName(item.Value)}. " : "";
            string name = DaggerfallEffectCatalog.Label(effect.Definition.Key);
            slots.Publish(new(Owner, effect.Context.Instance.Value, effect.BundleName ?? name,
                $"{name}. {source}{duration}", 35));
        }
    }
}
