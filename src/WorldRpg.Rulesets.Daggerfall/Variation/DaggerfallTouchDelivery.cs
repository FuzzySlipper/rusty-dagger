using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One touch spell at its cast frame: who casts it, from where and which way, and whom an AI caster's behavior already admitted in reach.</summary>
/// <param name="CasterId">The casting actor.</param>
/// <param name="Eye">Where the touch starts.</param>
/// <param name="Forward">The unit direction the caster reaches along.</param>
/// <param name="Admitted">The target an enemy's behavior already admitted in touch reach, or none for an aimed player cast.</param>
internal readonly record struct DaggerfallTouchCast(long CasterId, Vector3 Eye, Vector3 Forward, long? Admitted);

/// <summary>What a touch reaches: whether the spell is released at all, and every actor it reaches.</summary>
internal readonly record struct DaggerfallTouchReach(bool Releases, IReadOnlyList<long> Targets)
{
    internal static DaggerfallTouchReach Refused { get; } = new(false, []);
}

/// <summary>The session's spatial reads a touch owner queries.</summary>
/// <param name="Contact">Actor contact over the session's actor bodies.</param>
/// <param name="Spatial">The session's spatial movement system.</param>
/// <param name="Environment">The current world environment contact queries run against.</param>
internal sealed record DaggerfallTouchOperations(ActorContact Contact, SpatialMovementSystem Spatial, Func<CharacterStepEnvironment?> Environment);

/// <summary>
/// The touch delivery seam (<see cref="DaggerfallRulesSeams.TouchDelivery"/>): whom a touch spell reaches
/// at its cast frame. Casting owns readiness, cost and effects; this owner only decides the reach.
/// </summary>
internal interface IDaggerfallTouchDelivery
{
    DaggerfallTouchReach Reach(DaggerfallTouchCast cast);
}
