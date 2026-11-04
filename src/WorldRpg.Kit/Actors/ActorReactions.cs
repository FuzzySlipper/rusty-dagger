using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;

namespace WorldRpg.Kit.Actors;

/// <summary>An actor that a throw carried off its footing has landed.</summary>
public readonly record struct ActorThrowLanding(long ActorId, float Distance);

/// <summary>
/// Actors a blow has taken out of their own control: thrown across the world, held on the spot, or both.
/// The ruleset decides who reacts and how hard; while an actor is held here its own movement waits.
/// A reaction lasts moments, so it isn't saved.
/// </summary>
public sealed class ActorReactions(ActorNavigationCoordinator navigation)
{
    private sealed class Reaction(Vector3 velocity, double seconds, float deceleration, ActorNavigationMode mode)
    {
        internal Vector3 Velocity = velocity;
        internal double Seconds = seconds;
        internal float Deceleration = deceleration;
        internal ActorNavigationMode Mode = mode;
        internal float? PeakY;
        internal bool Wedged;

        /// <summary>Whether gravity has a say: a flyer or a swimmer is never left falling.</summary>
        internal bool Falls => Mode is not (ActorNavigationMode.Flying or ActorNavigationMode.Swimming);
    }

    private readonly Dictionary<long, Reaction> _active = [];

    public bool Holds(long actorId) => _active.ContainsKey(actorId);

    /// <summary>
    /// Holds an actor for <paramref name="seconds"/>, thrown at <paramref name="velocity"/> until <paramref name="deceleration"/>
    /// stops it. A swimmer is only held: the controller needs its water, and a blow doesn't carry through it.
    /// </summary>
    public void Begin(long actorId, Vector3 velocity, double seconds, float deceleration, ActorNavigationMode mode) =>
        _active[actorId] = new(mode == ActorNavigationMode.Swimming ? Vector3.Zero : velocity, seconds, deceleration, mode);

    /// <summary>Runs every reaction one admitted step. An actor that is gone or defeated drops out.</summary>
    public void Step(ActorsState actors, float seconds, Action<ActorThrowLanding> landed)
    {
        ArgumentNullException.ThrowIfNull(actors);
        foreach (long id in _active.Keys.OrderBy(value => value).ToArray())
        {
            if (!actors.TryGet(id, out ActorState actor) || actor.IsDefeated) { _active.Remove(id); continue; }
            Reaction reaction = _active[id];
            reaction.Seconds -= seconds;
            if (reaction.Velocity != Vector3.Zero || Airborne(actor, reaction)) Carry(actor, reaction, seconds, landed);
            if (reaction.Seconds > 0d || reaction.Velocity != Vector3.Zero || Airborne(actor, reaction)) continue;
            navigation.Settle(actor);
            _active.Remove(id);
        }
    }

    private static bool Airborne(ActorState actor, Reaction reaction) => reaction.Falls && !reaction.Wedged
        && !actor.Actor.Store.Get(actor.Actor.Entity, EngineComponentTypes.CharacterMotion).Grounded;

    private void Carry(ActorState actor, Reaction reaction, float seconds, Action<ActorThrowLanding> landed)
    {
        float y = actor.Pose.Position.Y;
        reaction.PeakY = Math.Max(reaction.PeakY ?? y, y);
        CharacterStepReceipt? step = navigation.Throw(actor, reaction.Velocity, reaction.Mode, seconds);
        float speed = reaction.Velocity.Length();
        if (step is not { } accepted || accepted.Displacement.LengthSquared() < 1e-7f && accepted.BlockFlags != CharacterBlockFlags.None)
        {
            reaction.Velocity = Vector3.Zero; // A wall or a wedge stops the throw dead.
            if (step is null) (reaction.PeakY, reaction.Wedged) = (null, true);
        }
        else if (speed > 0f) reaction.Velocity *= Math.Max(0f, speed - reaction.Deceleration * seconds) / speed;
        CharacterMotion motion = actor.Actor.Store.Get(actor.Actor.Entity, EngineComponentTypes.CharacterMotion);
        if (!motion.Grounded || reaction.PeakY is not float peak) return;
        float fall = peak - actor.Pose.Position.Y;
        reaction.PeakY = null;
        if (fall > 0f) landed(new(actor.DurableId, fall));
    }
}
