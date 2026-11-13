using System.Globalization;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What the player's Detect spells sense: enemies, or magic on creatures, or treasure in ground piles and corpses,
/// within the detection distance, nearest first. Each live source owns its saved duration and settings; the contacts
/// are read again from the current site at publication.
/// </summary>
/// <param name="gameplayActive">Whether an actor takes part in play now.</param>
/// <param name="definitionOf">A site actor's definition, when it has one.</param>
/// <param name="treasure">The ground piles, then the corpses that can be searched: each container's id, owner and position.</param>
/// <param name="nearbyContacts">The candidates within a radius of a point (strictly, when exclusive), with their distances.</param>
internal sealed class DaggerfallDetectors(DaggerfallEffectLifecycle effects, ActorsState actors, MechanicsInventoryContainerCoordinator containers,
    double maximumDistance, Func<WorldPoint?> playerPosition, Func<long, bool> gameplayActive, Func<long, DaggerfallActorDefinition?> definitionOf,
    Func<IEnumerable<(long Id, EntityId Owner, WorldPoint Position)>> treasure,
    Func<WorldPoint, IEnumerable<KeyValuePair<long, WorldPoint>>, double, Dictionary<long, double>> nearbyContacts)
{
    internal IReadOnlyList<DaggerfallDetectorView> Read()
    {
        if (playerPosition() is not WorldPoint origin) return [];
        var sources = effects.Active.Where(effect => effect.Context.Target.Value == DaggerfallActorIdentity.PlayerEntityId
            && effect.Definition.Detection != DaggerfallDetection.None).ToArray();
        if (sources.Length == 0) return [];
        Dictionary<DaggerfallDetection, IReadOnlyList<DaggerfallDetectionFact>> resolved = [];
        return sources.Select(source =>
        {
            var kind = source.Definition.Detection;
            if (!resolved.TryGetValue(kind, out var contacts)) resolved[kind] = contacts = Resolve(kind, origin);
            return new DaggerfallDetectorView(source.Context.Instance.Value, kind.ToString().ToLowerInvariant(), contacts);
        }).ToArray();
    }

    private IReadOnlyList<DaggerfallDetectionFact> Resolve(DaggerfallDetection kind, WorldPoint origin)
    {
        Dictionary<long, WorldPoint> positions = [];
        Dictionary<long, IReadOnlyList<DaggerfallDetectedItem>> items = [];
        if (kind == DaggerfallDetection.Treasure)
        {
            foreach (var (id, owner, position) in treasure())
                AddContainer(id, owner, position);
        }
        else
        {
            var magical = effects.Active.Select(effect => effect.Context.Target.Value).ToHashSet();
            foreach (var actor in actors.All.Where(actor => !actor.IsDefeated && gameplayActive(actor.DurableId)))
            {
                if (definitionOf(actor.DurableId) is not { } definition) continue;
                bool enemy = definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass;
                bool civilian = definition.Kind == DaggerfallActorKinds.Civilian;
                if (kind == DaggerfallDetection.Enemy ? enemy : (enemy || civilian) && magical.Contains(checked((ulong)actor.DurableId)))
                    positions.Add(actor.DurableId, actor.Position);
            }
        }
        return nearbyContacts(origin, positions, maximumDistance)
            .OrderBy(pair => pair.Value).ThenBy(pair => pair.Key).Select(pair => new DaggerfallDetectionFact(
                kind == DaggerfallDetection.Treasure ? "container" : "actor", pair.Key.ToString(CultureInfo.InvariantCulture), pair.Value,
                // Clockwise from north (world -Z), as the player's yaw reads.
                Math.Atan2(positions[pair.Key].X - origin.X, origin.Z - positions[pair.Key].Z), items.GetValueOrDefault(pair.Key) ?? [])
            {
                Label = kind == DaggerfallDetection.Treasure ? "Treasure"
                    : definitionOf(pair.Key) is { } detected ? DaggerfallIdLabel.For(detected.Id.Value) : "Someone",
            })
            .ToArray();

        void AddContainer(long id, EntityId owner, WorldPoint position)
        {
            var inventory = containers.Read(owner);
            var contents = inventory.Stacks.Select(stack => new DaggerfallDetectedItem(stack.Id.Value, stack.Definition.Value, stack.Quantity))
                .Concat(inventory.UniqueItems.Select(item => new DaggerfallDetectedItem(
                    containers.Entities.IdentityOf(item.Entity).Value.ToString(CultureInfo.InvariantCulture), item.Definition.Value, 1)))
                .ToArray();
            if (contents.Length == 0) return;
            positions.Add(id, position); items.Add(id, contents);
        }
    }
}

internal sealed record DaggerfallDetectionTuning(double MaximumDistance)
{
    internal DaggerfallDetectionTuning Validate()
    {
        if (!double.IsFinite(MaximumDistance) || MaximumDistance <= 0d)
            throw new ArgumentOutOfRangeException(nameof(MaximumDistance));
        return this;
    }
}
