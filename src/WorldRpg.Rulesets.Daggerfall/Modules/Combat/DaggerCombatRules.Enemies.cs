using System.Numerics;
using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.Content;
using KitItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

internal sealed partial class DaggerCombatRules
{
    internal double? EnemyReachAtDistance(long actorId, long targetId, double distance)
    {
        // Spending the last arrow does not cancel the already accepted draw/release. The
        // canonical pending attack supplies its reach until its release frame consumes it.
        var pending = _actors.Get(actorId).Actor.Get<WorldRpg.Kit.Combat.AttackState>().Pending;
        if (pending is { Request.TargetId: var target, Attack: DaggerfallPreparedAttack { IsRanged: true, Action: { } action } }
            && target == targetId) return _actions[action].Reach;
        return TryResolve(actorId, out var actor) ? EnemyAction(actor, distance).Action?.Reach : null;
    }

    private (DaggerfallActionDefinition? Action, KitItem? Bow) EnemyAction(Combatant actor, double distance)
    {
        if (actor.Definition.Kind == DaggerfallActorKinds.EnemyClass
            && actor.Definition.MobileId is int mobileId && _catalog.Mobiles.Mobiles[mobileId].HasBowAttack)
        {
            var ranged = _actions["archer-shot"];
            if (distance > _catalog.EnemyRanged.MinimumDistance && distance < ranged.Reach
                && _actorInventories(actor.Id) is { } inventory
                && inventory.Read().Stacks.Any(stack => IsArrow(stack) && stack.Quantity > 0))
            {
                foreach (var item in inventory.Read().UniqueItems.OrderBy(item => _actors.Entities.IdentityOf(item.Entity).Value))
                {
                    var definition = _catalog.RequireItem(new(item.Definition.Value));
                    if (definition.Weapon?.IsBow != true) continue;
                    ulong id = _actors.Entities.IdentityOf(item.Entity).Value;
                    if (_itemInstances.RequireUnique(id).CurrentCondition <= 0) continue;
                    return (ranged, new KitItem(item.Entity.Value, new(item.Definition.Value)));
                }
            }
            return (_actions[DaggerfallEncounterActors.ClassEquippedMeleeAction], null);
        }
        return (actor.Definition.ActionId is { } action ? _actions.GetValueOrDefault(action) : null, null);
    }

    private double EnemyTargetDistance(long attacker, long? target)
    {
        if (target is not long id) return double.PositiveInfinity;
        var position = id == PlayerId ? _playerPosition() : _actors.Get(id).Position;
        return position is { } value ? Vector3.Distance(_actors.Get(attacker).Position.ToVector(), value.ToVector()) : double.PositiveInfinity;
    }

    private DaggerfallAttackDefinition CarriedBowAttack(KitItem item, DaggerfallActionDefinition action)
    {
        var weapon = _catalog.RequireItem(new(item.Definition.Value)).Weapon!;
        var metadata = _itemInstances.RequireUnique(_actors.Entities.IdentityOf(new EntityId(item.EntityId)).Value);
        return new(weapon.Skill, weapon.MinimumDamage, weapon.MaximumDamage, action.CooldownSeconds!.Value,
            metadata.Material, Reach: action.Reach);
    }
}
