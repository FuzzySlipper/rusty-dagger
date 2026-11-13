using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Hurt traps reaching past the body that set them off: one the player sets off catches the bodies around the player, and
/// a body the player throws onto one springs it, and each trap it links to, on that body once a throw.
/// </summary>
/// <param name="playerPosition">Where the player stands now, if anywhere.</param>
/// <param name="dungeonActions">The loaded site's dungeon actions, which a trap's links name.</param>
/// <param name="contactActionsAt">The contact actions a body at a point (in the site's frame) touches.</param>
/// <param name="level">The player's level, which a trap's harm reads.</param>
/// <param name="harm">How a trap's health damage is applied.</param>
internal sealed class DaggerfallTraps(DaggerfallHazardTuning tuning, ActorsState actors, ActorReactions reactions,
    Func<WorldPoint?> playerPosition, Func<IReadOnlyList<DaggerfallDungeonActionDefinition>> dungeonActions,
    Func<Vector3, IEnumerable<DaggerfallDungeonActionDefinition>> contactActionsAt, Func<int> level,
    CombatResolution combat, IRandomService random, Action<DamageResult> harm)
{
    /// <summary>A trap springs on a thrown body at the donor's sporadic Hurt21 gate, every twentieth count: this is such a count.</summary>
    private const ulong ThrownTrapActivation = 20;

    // Which trap each thrown body has sprung, so one throw springs each only once.
    private readonly HashSet<(long Actor, string Action)> _sprung = [];

    /// <summary>A hurt trap the player set off reaches the bodies around the player as well.</summary>
    internal void SpreadHurt(DaggerfallDungeonActionDefinition action, ulong count)
    {
        if (!DaggerfallDungeonHazardActions.Hurts(action) || playerPosition() is not WorldPoint player) return;
        foreach (ActorState actor in actors.All.ToArray())
            if (!actor.IsDefeated && Vector3.Distance(actor.Position.ToVector(), player.ToVector()) <= tuning.TrapRadius)
                Hurt(action, actor, count);
    }

    /// <summary>Forgets the traps sprung by bodies no longer held, so a body thrown again springs them again.</summary>
    internal void ForgetLanded() => _sprung.RemoveWhere(sprung => !reactions.Holds(sprung.Actor));

    /// <summary>A thrown body at a point springs each hurt trap it touches there, and the traps those link to.</summary>
    internal void SpringOn(ActorState thrown, Vector3 at)
    {
        IReadOnlyList<DaggerfallDungeonActionDefinition> actions = dungeonActions();
        foreach (DaggerfallDungeonActionDefinition trigger in contactActionsAt(at))
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
        if (DaggerfallDungeonHazardActions.Execute(action, new(actors.Player.Actor, target.Actor, level(), count), combat, random)
            is { HealthDamage: { } damage }) harm(damage);
    }
}
