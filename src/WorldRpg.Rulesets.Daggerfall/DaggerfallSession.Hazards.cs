using System.Numerics;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What the world does to bodies by touch, always with the player involved: open fires burn the player and bodies the
/// player threw into them, a hurt trap the player sets off catches everyone close by, and a body the player throws into
/// one springs it on that body. A monster wandering over a trap or through a fire sets nothing off.
/// </summary>
internal sealed partial class DaggerfallSession
{
    /// <summary>A trap springs on a thrown body at the donor's sporadic Hurt21 gate, every twentieth count: this is such a count.</summary>
    private const ulong ThrownTrapActivation = 20;

    private DaggerfallFires _fires = null!;
    private DaggerfallSiteProfile? _fireInputs;
    private Vector3[] _fireSpots = [];
    // Which trap each thrown body has sprung, so one throw springs each only once.
    private readonly HashSet<(long Actor, string Action)> _sprung = [];

    private void StepHazards(double seconds)
    {
        Vector3[] fires = FireSpots();
        if (State.PlayerControl.Position is WorldPoint player)
        {
            int burn = _fires.BurnPlayer(_fires.Near(_sites.LocalToProfile(player.ToVector()), fires), seconds);
            if (burn > 0)
            {
                Burn(State.Actors.Player.Actor, burn);
                Presentation.SetOutcome("The fire burns you.");
            }
        }
        _sprung.RemoveWhere(sprung => !_reactions.Holds(sprung.Actor));
        foreach (long thrown in _reactions.Held)
        {
            if (!State.Actors.TryGet(thrown, out ActorState actor) || actor.IsDefeated) continue;
            Vector3 at = _sites.LocalToProfile(actor.Position.ToVector());
            if (_fires.Near(at, fires)) _fires.Ignite(thrown);
            SpringTraps(actor, at);
        }
        foreach ((long actor, int damage) in _fires.BurnBodies(seconds))
            if (State.Actors.TryGet(actor, out ActorState burning) && !burning.IsDefeated) Burn(burning.Actor, damage);
    }

    private Vector3[] FireSpots()
    {
        DaggerfallSiteProfile inputs = _sites.Projection.Inputs;
        if (ReferenceEquals(inputs, _fireInputs)) return _fireSpots;
        _fireInputs = inputs;
        return _fireSpots = [.. inputs.Decorations.Where(flat => _tuning.Hazards.BurningFlats.Contains(flat.Source)).Select(flat => flat.Position.ToVector())];
    }

    private void Burn(Rusty.Engine.Entities.Actor target, int damage)
    {
        int resistance = target.Get<StatsComponent>().TryGetStat(StatId.Parse(DaggerfallMechanicsIds.ResistanceFire.Value), out Stat? stat) ? stat.ValueInt : 0;
        int felt = damage * Math.Clamp(100 - resistance, 0, 200) / 100;
        if (felt <= 0) return;
        AppendDamage(_vitality.ResolveBurn(State.Actors.Player.Actor, target, felt), DaggerfallDamageCause.Hazard, 0);
    }

    /// <summary>A hurt trap the player set off reaches the bodies around the player as well.</summary>
    internal void SpreadHurt(DaggerfallDungeonActionDefinition action, ulong count)
    {
        if (!DaggerfallDungeonHazardActions.Hurts(action) || State.PlayerControl.Position is not WorldPoint player) return;
        foreach (ActorState actor in State.Actors.All.ToArray())
            if (!actor.IsDefeated && Vector3.Distance(actor.Position.ToVector(), player.ToVector()) <= _tuning.Hazards.TrapRadius)
                Hurt(action, actor, count);
    }

    private void SpringTraps(ActorState thrown, Vector3 at)
    {
        IReadOnlyList<DaggerfallDungeonActionDefinition> actions = _sites.Projection.Inputs.DungeonActions;
        foreach (DaggerfallDungeonActionDefinition trigger in _sites.ActionTriggers.ContactActionsAt(at))
        {
            DaggerfallDungeonActionDefinition? action = trigger;
            for (int link = 0; link < actions.Count && action is not null; link++)
            {
                if (DaggerfallDungeonHazardActions.Hurts(action) && _sprung.Add((thrown.DurableId, action.Id))) Hurt(action, thrown, ThrownTrapActivation);
                action = action.NextActionId is { } next ? actions.FirstOrDefault(value => value.Id == next) : null;
            }
        }
    }

    private void Hurt(DaggerfallDungeonActionDefinition action, ActorState target, ulong count)
    {
        if (DaggerfallDungeonHazardActions.Execute(action, new(State.Actors.Player.Actor, target.Actor, State.Progression.Level, count), _combatResolution, _random)
            is { HealthDamage: { } damage }) AppendDamage(damage, DaggerfallDamageCause.Hazard, 0);
    }
}
