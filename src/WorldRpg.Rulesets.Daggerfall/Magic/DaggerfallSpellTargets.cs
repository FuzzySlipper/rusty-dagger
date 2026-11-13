using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Whom a spell can reach: the actors that take part in spells (every gameplay-active actor but a static NPC), the
/// actor a cast resolves to, its magic profile, and the eligible actors within a radius of a point, read from the
/// Engine's current distances without a second spatial index.
/// </summary>
/// <param name="gameplayActive">Whether an actor takes part in play now.</param>
/// <param name="definitionOf">A site actor's definition, when it has one.</param>
/// <param name="positions">Where the player and every gameplay-active actor stand now.</param>
/// <param name="queryVisibility">The Engine's perception query.</param>
internal sealed class DaggerfallSpellTargets(DaggerfallDefinitions definitions, ActorsState actors, DaggerfallCharacterState character,
    DaggerfallEffectLifecycle effects, SpatialMovementSystem spatial, Func<long, bool> gameplayActive,
    Func<long, DaggerfallActorDefinition?> definitionOf, Func<IReadOnlyDictionary<long, WorldPoint>> positions,
    Func<PerceptionQueryRequest, PerceptionReadoutResult> queryVisibility)
{
    internal DaggerfallMagicTargetProfile MagicProfile(long id) => DaggerfallMagicProfiles.Create(
        (CastActor(id) ?? throw new ArgumentException($"Casting actor {id} is unavailable.")).Get<StatsComponent>(),
        definitionOf(id), id == actors.Player.DurableId ? character : null,
        definitions, effects.MagicDefenseFor(id));

    internal Actor? CastActor(long id) => id == actors.Player.DurableId ? actors.Player.Actor
        : IsEligible(id) && actors.TryGet(id, out ActorState actor) ? actor.Actor : null;

    internal bool IsEligible(long id) =>
        gameplayActive(id)
        && (definitionOf(id) is not { } definition || definition.Kind != DaggerfallActorKinds.StaticNpc);

    /// <summary>The eligible actors within a radius of a point, by id; an area spell's or a sweep's targets.</summary>
    /// <param name="caster">Whose spell it is, observing the query; the player when none.</param>
    /// <param name="passes">Whom the spell goes through.</param>
    internal long[] Area(long? caster, Vector3 center, bool excludeCaster, double radius = 4d, bool exclusive = false, Func<long, bool>? passes = null)
    {
        long observer = caster ?? actors.Player.DurableId;
        return Nearby(observer, center, positions()
                .Where(pair => IsEligible(pair.Key) && passes?.Invoke(pair.Key) != true && (!excludeCaster || caster is not long source || pair.Key != source)), radius, exclusive)
            .Keys.Order().ToArray();
    }

    /// <summary>Engine supplies current distances; callers decide strict range and target eligibility without a second spatial index.</summary>
    internal Dictionary<long, double> Nearby(long observer, Vector3 center,
        IEnumerable<KeyValuePair<long, WorldPoint>> candidatePositions, double radius, bool exclusive)
    {
        var candidates = candidatePositions.Select(pair => new PerceptionTarget(checked((ulong)pair.Key), pair.Value.ToVector())).ToArray();
        if (candidates.Length == 0) return [];
        PerceptionQueryRequest request = new(spatial.Session,
            new[] { new PerceptionObserver(checked((ulong)observer), center, Vector3.UnitZ, radius, -1d, 1d) }, candidates,
            ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 64);
        Dictionary<long, double> contacts = [];
        PerceptionReadoutResult receipt;
        do
        {
            receipt = queryVisibility(request);
            // Classic detection and blast radii do not test line of sight.
            foreach (var pair in receipt.Pairs.ToArray())
                if (exclusive ? pair.Distance < radius : pair.Distance <= radius) contacts[checked((long)pair.Target)] = pair.Distance;
            if (receipt.HasNextPairCursor) request = request with { PairCursor = receipt.NextPairCursor };
        } while (receipt.HasNextPairCursor);
        return contacts;
    }

    internal static bool ValidDirection(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && value.LengthSquared() > .000001f;
}
