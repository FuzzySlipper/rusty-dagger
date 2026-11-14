using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Where a dungeon action model stands now, by its action, or false while it has no entity.</summary>
internal delegate bool DaggerfallActionModelPose(string actionId, out Transform pose);

/// <summary>
/// What the site appearance draws of a location's moving parts, read through the runtimes that move them each time it
/// publishes: the doors (read again only once their revision moves), the city gates and whether they stand open, and the
/// dungeon's action models. The site's composition binds them; the appearance never asks the runtimes themselves.
/// </summary>
internal sealed record DaggerfallLocationParts(
    Func<int> DoorRevision,
    Func<IEnumerable<(DaggerfallRdbDoorId Id, Transform Pose)>> Doors,
    Func<bool> GatesOpen,
    Func<IEnumerable<(DaggerfallCityGateDefinition Gate, EntityId Entity, DaggerfallDoorVisual Visual, Transform Pose)>> Gates,
    Func<IReadOnlyList<(DaggerfallDungeonActionModelDefinition Model, EntityId Entity)>> ActionModels,
    DaggerfallActionModelPose ActionModelPose);
