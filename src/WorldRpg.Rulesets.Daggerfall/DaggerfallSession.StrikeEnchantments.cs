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
        foreach (long id in AreaSpellTargets(State.Actors.Player.DurableId, origin.ToVector(),
            excludeCaster: true, radius: _tuning.StrikeEnchantments.VampiricRange))
        {
            if (!State.Actors.TryGet(id, out var actor) || actor.IsDefeated || !_roster.Definitions.TryGetValue(id, out var definition)
                || definition.Kind is not (DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass)
                || EffectiveFoeTeam(id) == "player-ally") continue;
            AppendSpellTransfer(_vitality.ResolveSpellTransfer(State.Actors.Player.Actor, actor.Actor, 1, fatigue: false, permitsFatigueLoss: true));
        }
    }
}
