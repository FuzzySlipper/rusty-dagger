using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One active-scene actor hit returned by <see cref="DaggerfallActorQueries.QueryNearby"/>.</summary>
internal readonly record struct NearbyActorHit(ActorState Actor, double Distance);

/// <summary>
/// Unified proximity query over the single active-scene <see cref="ActorsState"/> (<c>EntityStore</c>).
/// Obeys "keep no second actor dictionary; enumerate the store": off-stage, removed, overland-band,
/// and <see cref="ActorLocationKind.WithParty"/> HUD-only escorts are never in <c>EntityStore</c> and
/// therefore never appear in proximity results.
/// </summary>
internal static class DaggerfallActorQueries
{
    /// <summary>
    /// Returns every active-scene non-player actor within <paramref name="radius"/> metres of
    /// <paramref name="origin"/>, ordered nearest-first then by <see cref="ActorState.DurableId"/>.
    /// </summary>
    internal static IReadOnlyList<NearbyActorHit> QueryNearby(
        ActorsState actors,
        WorldPoint origin,
        double radius,
        Func<ActorState, bool>? predicate = null,
        Func<long, bool>? actorGameplayActive = null,
        bool includeDefeated = false)
    {
        ArgumentNullException.ThrowIfNull(actors);
        if (!double.IsFinite(radius) || radius < 0d)
            throw new ArgumentOutOfRangeException(nameof(radius), "Query radius must be non-negative and finite.");

        Vector3 center = origin.ToVector();
        double maxSquared = radius * radius;
        List<NearbyActorHit> hits = [];
        foreach (ActorState actor in actors.All)
        {
            if (actor.DurableId == DaggerfallActorIdentity.PlayerEntityId) continue;
            if (!includeDefeated && actor.IsDefeated) continue;
            if (actorGameplayActive is not null && !actorGameplayActive(actor.DurableId)) continue;
            if (predicate is not null && !predicate(actor)) continue;

            double distanceSquared = Vector3.DistanceSquared(center, actor.Position.ToVector());
            if (distanceSquared <= maxSquared)
                hits.Add(new(actor, Math.Sqrt(distanceSquared)));
        }

        hits.Sort(static (left, right) =>
        {
            int byDistance = left.Distance.CompareTo(right.Distance);
            return byDistance != 0 ? byDistance : left.Actor.DurableId.CompareTo(right.Actor.DurableId);
        });
        return hits;
    }
}
