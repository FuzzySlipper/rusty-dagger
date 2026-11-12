using System.Numerics;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// What a player's blow does to the body it lands on: the reaction its force earns against the target's weight, and
/// the fact that gives the blow its weight on screen. A sprite shows a reaction by holding its hurt frames while it
/// slides; it has no fallen pose of its own.
/// </summary>
internal sealed class DaggerfallStrikes(DaggerfallStrikeTuning tuning, ActorsState actors, ActorReactions reactions,
    Func<WorldPoint?> playerPosition, Func<long, int?> weightOf, Func<long, ActorNavigationMode> modeOf)
{
    internal void Land(AttackRequest request, long target, string attack, FactBuffer<IProductFact> facts)
    {
        DaggerfallStrikeReaction reaction = default;
        if (actors.TryGet(target, out ActorState victim) && !victim.IsDefeated)
        {
            int strength = actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).ValueInt;
            reaction = tuning.React(attack, strength, weightOf(target));
            if (reaction.Push > 0d || reaction.HoldSeconds > 0d)
                reactions.Begin(target, Away(victim) * (float)reaction.Push, reaction.HoldSeconds, (float)tuning.PushDeceleration, modeOf(target));
        }
        facts.Append(new ActorStruckFact(target, reaction, DaggerfallStrikeTuning.Heavy(attack), request.Generation, request.SimulationStep));
    }

    private Vector3 Away(ActorState victim)
    {
        if (playerPosition() is not WorldPoint player) return Vector3.Zero;
        Vector3 away = victim.Pose.Position.ToVector() - player.ToVector();
        away.Y = 0f;
        return away.LengthSquared() > 1e-6f ? Vector3.Normalize(away) : Vector3.Zero;
    }
}
