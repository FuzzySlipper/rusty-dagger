using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One active effect on the player, as the HUD lists it.</summary>
/// <param name="Id">The effect instance, stable while the effect lasts.</param>
/// <param name="Name">The effect's player name ("Shield", "Restore Health").</param>
/// <param name="Spell">The spell or bundle that applied it, which is the effect's name when it has none.</param>
/// <param name="Source">The item the effect comes from, or null when it was cast.</param>
/// <param name="RemainingSeconds">Game seconds left, or null for an effect that lasts until removed.</param>
/// <param name="Remaining">Player wording for the remaining time.</param>
/// <param name="Detail">The one sentence that describes the row.</param>
internal sealed record DaggerfallActiveEffectView(string Id, string Name, string Spell, string? Source, long? RemainingSeconds,
    string Remaining, string Detail);

/// <summary>The player's active-effect list, rebuilt from canonical active effects including restored sources.</summary>
internal static class DaggerfallMagicPresentation
{
    internal static IReadOnlyList<DaggerfallActiveEffectView> Read(DaggerfallEffectLifecycle effects, PlayerActorState player,
        Func<ulong, string> itemName)
    {
        if (player.IsDefeated) return [];
        List<DaggerfallActiveEffectView> rows = [];
        foreach (var effect in effects.Active.Where(effect => effect.Context.Target.Value == checked((ulong)player.DurableId)
            && effect.Definition.ShowSpellIcon && effect.Definition.DoorMagic == DaggerfallDoorMagic.None
            && effect.Definition.Perception.Concealment == DaggerfallConcealment.None))
        {
            // One magic round is one game minute.
            long? seconds = effect.Lifecycle.RemainingRounds is uint rounds ? checked((long)rounds * DaggerfallCalendar.SecondsPerMinute) : null;
            string remaining = seconds is long value ? DaggerfallCalendar.DescribeDuration(value) + " remaining" : "Active until removed";
            string? source = effect.Context.Item is { } item ? itemName(item.Value) : null;
            string name = DaggerfallEffectCatalog.Label(effect.Definition.Key);
            string detail = $"{name}. {(source is null ? "" : $"From {source}. ")}{remaining}.";
            rows.Add(new(effect.Context.Instance.Value, name, effect.BundleName ?? name, source, seconds, remaining, detail));
        }
        return rows;
    }
}
