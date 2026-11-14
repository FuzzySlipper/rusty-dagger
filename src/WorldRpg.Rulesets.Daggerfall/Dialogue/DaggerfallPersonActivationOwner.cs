using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What activating a person does by mode: talking goes to the dialogue, stealing to a pickpocket attempt, talking to a
/// guard handling the player's case to surrender, and a click on a quest's person to the quest. People are the
/// dialogue's targets, the quests' bound people and, as the mode admits them, actors that can be robbed or that guard the case.
/// </summary>
internal sealed class DaggerfallPersonActivationOwner(DaggerfallDialogueService dialogue, ActorsState actors,
    Func<long, bool> eligible, Func<long, DaggerfallActivationOutcome> pickpocket,
    Func<long, bool> lawGuard, Func<long, DaggerfallActivationOutcome> surrender, Func<long, bool> questActor, Func<long, bool> questClick,
    Func<long, double?> sightHeight) : IDaggerfallNpcActivationOwner
{
    public IEnumerable<DaggerfallActivationTarget> NpcTargets() => dialogue.NpcTargets().Select(Sighted);
    public IEnumerable<DaggerfallActivationTarget> NpcTargets(DaggerfallActivationMode mode) => People(mode).Select(Sighted);
    // A person stands where their position is; they are sighted at the middle of their body.
    private DaggerfallActivationTarget Sighted(DaggerfallActivationTarget person) =>
        person with { SightHeight = sightHeight(checked((long)person.Identity.Value)) };
    private IEnumerable<DaggerfallActivationTarget> People(DaggerfallActivationMode mode)
    {
        HashSet<long> included = [];
        if (mode != DaggerfallActivationMode.Steal)
            foreach (var person in dialogue.NpcTargets()) { included.Add(checked((long)person.Identity.Value)); yield return person; }
        if (mode is DaggerfallActivationMode.Grab or DaggerfallActivationMode.Talk or DaggerfallActivationMode.Steal)
        {
            foreach (var person in dialogue.QuestNpcTargets(questActor))
                if (included.Add(checked((long)person.Identity.Value))) yield return person;
            foreach (var actor in actors.All.Where(actor => !actor.IsDefeated && questActor(actor.DurableId) && included.Add(actor.DurableId)))
                yield return Target(actor);
        }
        foreach (var actor in actors.All.Where(actor => !actor.IsDefeated && actor.DurableId != DaggerfallActorIdentity.PlayerEntityId
            && (mode == DaggerfallActivationMode.Steal ? eligible(actor.DurableId) : mode == DaggerfallActivationMode.Talk && lawGuard(actor.DurableId))
            && included.Add(actor.DurableId)))
            yield return Target(actor);
    }
    private static DaggerfallActivationTarget Target(ActorState actor) =>
        new(DaggerfallActivationTargetKind.Npc, ActorsState.Identity(actor.DurableId), actor.Actor.Entity,
            checked((ulong)actor.DurableId), actor.Position, 1, ReachDistance: 3.2);
    public DaggerfallActivationOutcome ActivateNpc(DaggerfallActivationSelection selection)
    {
        long id = checked((long)selection.Target.Identity.Value);
        if (!(actors.TryGet(id, out var actor) && !actor.IsDefeated && actor.Actor.Entity == selection.Target.Entity)
            && !dialogue.IsCurrentNpcSelection(selection))
            return new(false, "That person is no longer available here.");
        if (selection.Mode is DaggerfallActivationMode.Grab or DaggerfallActivationMode.Talk or DaggerfallActivationMode.Steal && questClick(id))
            return new(true, "You address them.");
        if (selection.Mode == DaggerfallActivationMode.Steal) return pickpocket(id);
        return lawGuard(id) ? surrender(id) : dialogue.ActivateNpc(selection);
    }
}
