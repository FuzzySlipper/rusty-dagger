using System.Globalization;
using System.Numerics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>Each live source owns saved duration/settings; contacts are rebuilt from current site owners on publication.</summary>
    internal IReadOnlyList<DaggerfallDetectorView> ReadDetectors()
    {
        if (State.PlayerControl.Position is not WorldPoint origin) return [];
        var sources = State.Effects.Active.Where(effect => effect.Context.Target.Value == DaggerfallActorIdentity.PlayerEntityId
            && effect.Definition.Detection != DaggerfallDetection.None).ToArray();
        if (sources.Length == 0) return [];
        Dictionary<DaggerfallDetection, IReadOnlyList<DaggerfallDetectionFact>> resolved = [];
        return sources.Select(source =>
        {
            var kind = source.Definition.Detection;
            if (!resolved.TryGetValue(kind, out var contacts)) resolved[kind] = contacts = ResolveDetector(kind, origin);
            return new DaggerfallDetectorView(source.Context.Instance.Value, kind.ToString().ToLowerInvariant(), contacts);
        }).ToArray();
    }
    private IReadOnlyList<DaggerfallDetectionFact> ResolveDetector(DaggerfallDetection kind, WorldPoint origin)
    {
        Dictionary<long, WorldPoint> positions = [];
        Dictionary<long, IReadOnlyList<DaggerfallDetectedItem>> items = [];
        if (kind == DaggerfallDetection.Treasure)
        {
            foreach (var ground in _groundContainers.All.Values)
                AddContainer(ground.Id, ground.Owner, ground.Position);
            foreach (var corpse in _corpseLoot.Corpses.Values.Where(value => value.IsRegistered && value.IsInteractable))
                AddContainer(checked((long)corpse.ContainerIdentity.Value), corpse.Owner, State.Actors.Get(corpse.ActorId).Position);
        }
        else
        {
            var magical = State.Effects.Active.Select(effect => effect.Context.Target.Value).ToHashSet();
            foreach (var actor in State.Actors.All.Where(actor => !actor.IsDefeated && State.Npcs.IsGameplayActive(actor.DurableId)))
            {
                if (!_roster.Definitions.TryGetValue(actor.DurableId, out var definition)) continue;
                bool enemy = definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass;
                bool civilian = definition.Kind == DaggerfallActorKinds.Civilian;
                if (kind == DaggerfallDetection.Enemy ? enemy : (enemy || civilian) && magical.Contains(checked((ulong)actor.DurableId)))
                    positions.Add(actor.DurableId, actor.Position);
            }
        }
        return NearbyContacts(DaggerfallActorIdentity.PlayerEntityId, origin.ToVector(), positions, _tuning.Detection.MaximumDistance, true)
            .OrderBy(pair => pair.Value).ThenBy(pair => pair.Key).Select(pair => new DaggerfallDetectionFact(
                kind == DaggerfallDetection.Treasure ? "container" : "actor", pair.Key.ToString(CultureInfo.InvariantCulture), pair.Value,
                Math.Atan2(positions[pair.Key].X - origin.X, positions[pair.Key].Z - origin.Z), items.GetValueOrDefault(pair.Key) ?? [])
            {
                Label = kind == DaggerfallDetection.Treasure ? "Treasure"
                    : _roster.Definitions.TryGetValue(pair.Key, out var detected) ? WorldRpg.Rulesets.Daggerfall.Presentation.DaggerfallInventoryPresentation.Label(detected.Id.Value) : "Someone",
            })
            .ToArray();

        void AddContainer(long id, Rusty.Engine.Entities.EntityId owner, WorldPoint position)
        {
            var inventory = State.Containers.Read(owner);
            var contents = inventory.Stacks.Select(stack => new DaggerfallDetectedItem(stack.Id.Value, stack.Definition.Value, stack.Quantity))
                .Concat(inventory.UniqueItems.Select(item => new DaggerfallDetectedItem(
                    State.Containers.Entities.IdentityOf(item.Entity).Value.ToString(CultureInfo.InvariantCulture), item.Definition.Value, 1)))
                .ToArray();
            if (contents.Length == 0) return;
            positions.Add(id, position); items.Add(id, contents);
        }
    }
}
