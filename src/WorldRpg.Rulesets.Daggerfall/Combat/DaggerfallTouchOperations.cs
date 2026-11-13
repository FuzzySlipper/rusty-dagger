using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The session's spatial reads touch delivery queries.</summary>
/// <param name="Contact">Actor contact over the session's actor bodies.</param>
/// <param name="Spatial">The session's spatial movement system.</param>
/// <param name="Environment">The current world environment contact queries run against.</param>
internal sealed record DaggerfallTouchOperations(ActorContact Contact, SpatialMovementSystem Spatial, Func<CharacterStepEnvironment?> Environment);
