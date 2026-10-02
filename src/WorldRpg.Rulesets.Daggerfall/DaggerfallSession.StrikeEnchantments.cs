using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private Actor RequireEffectActor(long id) => id == State.Actors.Player.DurableId
        ? State.Actors.Player.Actor : State.Actors.Get(id).Actor;

    private void DrainNearbyHealth()
    {
        if (State.PlayerControl.Position is not { } origin) return;
        double rangeSquared = _tuning.StrikeEnchantments.VampiricRange * _tuning.StrikeEnchantments.VampiricRange;
        foreach (var actor in State.Actors.All.ToArray())
        {
            if (actor.IsDefeated || !_roster.Definitions.TryGetValue(actor.DurableId, out var definition)
                || definition.Kind is not (DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass) || (actor.Position.ToVector() - origin.ToVector()).LengthSquared() > rangeSquared) continue;
            AppendSpellTransfer(_vitality.ResolveSpellTransfer(State.Actors.Player.Actor, actor.Actor, 1, fatigue: false, permitsFatigueLoss: true));
        }
    }
}
