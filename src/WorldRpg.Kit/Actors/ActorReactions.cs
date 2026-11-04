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
    }

    private readonly Dictionary<long, Reaction> _active = [];

    public bool Holds(long actorId) => _active.ContainsKey(actorId);

    /// <summary>Holds an actor for <paramref name="seconds"/>, thrown at <paramref name="velocity"/> until <paramref name="deceleration"/> stops it.</summary>
    public void Begin(long actorId, Vector3 velocity, double seconds, float deceleration, ActorNavigationMode mode) =>
        _active[actorId] = new(velocity, seconds, deceleration, mode);

    /// <summary>Runs every reaction one admitted step. An actor that is gone or defeated drops out.</summary>
    public void Step(ActorsState actors, float seconds, Action<ActorThrowLanding> landed)
    {
        ArgumentNullException.ThrowIfNull(actors);
        foreach (long id in _active.Keys.OrderBy(value => value).ToArray())
        {
            if (!actors.TryGet(id, out ActorState actor) || actor.IsDefeated) { _active.Remove(id); continue; }
            Reaction reaction = _active[id];
            reaction.Seconds -= seconds;
            bool grounded = actor.Actor.Store.Get(actor.Actor.Entity, EngineComponentTypes.CharacterMotion).Grounded;
            bool flying = reaction.Mode == ActorNavigationMode.Flying;
            bool carried = reaction.Velocity != Vector3.Zero || !grounded && !flying;
            if (carried && reaction.Mode != ActorNavigationMode.Swimming) Carry(actor, reaction, seconds, landed);
            bool settled = reaction.Velocity == Vector3.Zero
                && (flying || actor.Actor.Store.Get(actor.Actor.Entity, EngineComponentTypes.CharacterMotion).Grounded);
            if (reaction.Seconds > 0d || !settled) continue;
            navigation.Settle(actor);
            _active.Remove(id);
        }
    }

    private void Carry(ActorState actor, Reaction reaction, float seconds, Action<ActorThrowLanding> landed)
    {
        float y = actor.Pose.Position.Y;
        reaction.PeakY = Math.Max(reaction.PeakY ?? y, y);
        CharacterStepReceipt? step = navigation.Throw(actor, reaction.Velocity, reaction.Mode, seconds);
        float speed = reaction.Velocity.Length();
        if (step is not { } accepted || accepted.Displacement.LengthSquared() < 1e-7f && accepted.BlockFlags != CharacterBlockFlags.None)
        {
            reaction.Velocity = Vector3.Zero; // A wall or a wedge stops the throw dead.
            if (step is null) reaction.PeakY = null;
        }
        else if (speed > 0f) reaction.Velocity *= Math.Max(0f, speed - reaction.Deceleration * seconds) / speed;
        CharacterMotion motion = actor.Actor.Store.Get(actor.Actor.Entity, EngineComponentTypes.CharacterMotion);
        if (!motion.Grounded || reaction.PeakY is not float peak) return;
        float fall = peak - actor.Pose.Position.Y;
        reaction.PeakY = null;
        if (fall > 0f) landed(new(actor.DurableId, fall));
    }
}
