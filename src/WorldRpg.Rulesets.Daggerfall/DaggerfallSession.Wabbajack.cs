using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private DaggerfallWabbajackResult TransformWithWabbajack(long attacker, long target, ulong source, ulong generation, ulong step)
    {
        if (!State.ItemInstances.ContainsUnique(source)
            || State.ItemInstances.RequireUnique(source).CurrentCondition <= 0
            || !State.ActorInventories.EquipmentFor(attacker).Read().Assignments.Any(assignment =>
                State.Actors.Entities.IdentityOf(new EntityId(assignment.Item.EntityId)).Value == source))
            return new(DaggerfallWabbajackOutcome.SourceUnavailable, target);
        if (!State.Actors.TryGet(target, out ActorState? actor) || !DefinitionsByActor.TryGetValue(target, out var definition)
            || definition.Kind is not (DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass))
            return new(DaggerfallWabbajackOutcome.InvalidTarget, target);
        if (DaggerfallWabbajack.DefinitionOf(actor.Actor) is not null)
            return new(DaggerfallWabbajackOutcome.AlreadyTransformed, target);
        if (State.Quests.ProtectsActor(target)) return new(DaggerfallWabbajackOutcome.ProtectedQuestTarget, target);
        int[] candidates = DaggerfallWabbajack.MobileIds.Where(id => id != definition.MobileId).ToArray();
        int index = checked((int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, "daggerfall.wabbajack.v1",
            $"source:{source}:attacker:{attacker}:target:{target}:generation:{generation}:step:{step}", 0, candidates.Length - 1)).Value);
        return _roster.Transform(target, candidates[index]);
    }
}
