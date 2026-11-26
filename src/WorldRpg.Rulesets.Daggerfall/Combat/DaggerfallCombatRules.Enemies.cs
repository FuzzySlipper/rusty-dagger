using System.Numerics;
using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.Content;
using KitItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// One attack an enemy carries, as a tactic weighs it: the action it is made with, how far it carries, the nearest a
/// shot may be loosed, whether it is a shot, and the arrows left for one (null for an attack that spends none).
/// </summary>
internal readonly record struct DaggerfallEnemyAttackOption(string Action, double Reach, double MinimumDistance, bool Ranged, int? Ammunition);

internal sealed partial class DaggerfallCombatRules
{
    /// <summary>The action a class enemy's carried bow shoots with.</summary>
    internal const string EnemyBowAction = "archer-shot";

    /// <summary>The attack this enemy would use at <paramref name="distance"/>: its reach and whether it is a ranged shot.</summary>
    internal (double Reach, bool Ranged)? EnemyAttackAtDistance(long actorId, long targetId, double distance)
    {
        // Spending the last arrow does not cancel the already accepted draw/release. The
        // canonical pending attack supplies its reach until its release frame consumes it.
        var pending = _actors.Get(actorId).Actor.Get<WorldRpg.Kit.Combat.AttackState>().Pending;
        if (pending is { Request.TargetId: var target, Attack: DaggerfallPreparedAttack { IsRanged: true, Action: { } action } }
            && target == targetId) return _actions[action].Reach is { } pendingReach ? (pendingReach, true) : null;
        if (!TryResolve(actorId, out var actor)) return null;
        var (selected, bow) = EnemyAction(actor, distance);
        return selected?.Reach is { } reach ? (reach, bow is not null) : null;
    }

    /// <summary>
    /// Every attack the enemy carries now, each a separate choice for a tactic that weighs them: a class enemy's bow
    /// while it holds one unbroken (with the arrows left, none being still an option it can see it lacks) and its
    /// equipped melee weapon; any other enemy's own authored attack.
    /// </summary>
    internal IReadOnlyList<DaggerfallEnemyAttackOption> EnemyAttackOptions(long actorId)
    {
        if (!TryResolve(actorId, out var actor)) return [];
        if (CarriesBowAttack(actor))
        {
            List<DaggerfallEnemyAttackOption> options = [];
            var ranged = _actions[EnemyBowAction];
            if (UsableBow(actor) is not null && ranged.Reach is double bowReach)
                options.Add(new(ranged.Id, bowReach, _catalog.EnemyRanged.MinimumDistance, true, ArrowsCarried(actor)));
            if (_actions[DaggerfallEncounterActors.ClassEquippedMeleeAction] is { Reach: double meleeReach } melee)
                options.Add(new(melee.Id, meleeReach, 0d, false, null));
            return options;
        }
        return actor.Definition.ActionId is { } id && _actions.GetValueOrDefault(id) is { Reach: double reach } own
            ? [new(own.Id, reach, 0d, own.Interpretation is "fixed-ranged" or "enemy-equipped-ranged", null)]
            : [];
    }

    /// <summary>Whether a shot from the enemy's eye at its target's would meet the target first: the line a carried bow holds fire on.</summary>
    internal bool LineOfFireClear(long actorId, long targetId) => !_delivery.HoldsFire(actorId, targetId);

    /// <summary>
    /// The attack an enemy makes at <paramref name="distance"/>. With no <paramref name="chosen"/> attack it is the
    /// donor's EnemyAction: a class enemy's bow inside its band while it has arrows and an unbroken bow, else its melee
    /// weapon, and any other enemy's own attack. A chosen attack is made only while it can be: a bow inside its band
    /// with arrows and a bow, the melee weapon, or the enemy's own attack; otherwise none.
    /// </summary>
    private (DaggerfallActionDefinition? Action, KitItem? Bow) EnemyAction(Combatant actor, double distance, string? chosen = null)
    {
        if (CarriesBowAttack(actor))
        {
            var ranged = _actions[EnemyBowAction];
            if (chosen is null or EnemyBowAction && distance > _catalog.EnemyRanged.MinimumDistance && distance < ranged.Reach
                && ArrowsCarried(actor) > 0 && UsableBow(actor) is { } bow)
                return (ranged, bow);
            return chosen is null or DaggerfallEncounterActors.ClassEquippedMeleeAction
                ? (_actions[DaggerfallEncounterActors.ClassEquippedMeleeAction], null) : (null, null);
        }
        DaggerfallActionDefinition? own = actor.Definition.ActionId is { } action ? _actions.GetValueOrDefault(action) : null;
        return (chosen is null || chosen == own?.Id ? own : null, null);
    }

    private bool CarriesBowAttack(Combatant actor) =>
        (actor.Definition.Capabilities.GearPolicy == ActorGearPolicy.GeneratedClassGear
            || (actor.Definition.TemplateId is null && actor.Definition.Kind == DaggerfallActorKinds.EnemyClass))
        && (actor.Definition.Capabilities.HasBowAttack
            || (actor.Definition.MobileId is int mobileId && _catalog.Mobiles.Mobiles[mobileId].HasBowAttack));

    /// <summary>The arrows the enemy carries, or none when it has no managed inventory.</summary>
    private int ArrowsCarried(Combatant actor) => _actorInventories(actor.Id) is { } inventory
        ? checked((int)inventory.Read().Stacks.Where(IsArrow).Sum(stack => (long)stack.Quantity)) : 0;

    /// <summary>The first unbroken bow the enemy carries, by item identity.</summary>
    private KitItem? UsableBow(Combatant actor)
    {
        if (_actorInventories(actor.Id) is not { } inventory) return null;
        foreach (var item in inventory.Read().UniqueItems.OrderBy(item => _actors.Entities.IdentityOf(item.Entity).Value))
        {
            var definition = _catalog.RequireItem(new(item.Definition.Value));
            if (definition.Weapon?.IsBow != true) continue;
            ulong id = _actors.Entities.IdentityOf(item.Entity).Value;
            if (_itemInstances.RequireUnique(id).CurrentCondition <= 0) continue;
            return new KitItem(item.Entity.Value, new(item.Definition.Value));
        }
        return null;
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
