using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonMotionProjectionTests
{
    [Fact]
    public void Non_motion_action_models_keep_their_model_entity_render_and_collision_admission()
    {
        SpatialDouble spatial = SpatialDouble.Create();
        using EntityDirectory entities = new();
        DaggerfallDoorRuntime doors = new(entities, RandomDouble.Create(), [], "fixture/dungeon");
        DaggerfallDungeonActionDefinition action = new(
            "action/hazard",
            SourceOffset: 1,
            TriggerFlag: 2,
            ActionFlag: 0x15,
            Axis: 1,
            Duration: 0,
            Magnitude: 5,
            NextObjectOffset: -1,
            NextActionId: null);
        DaggerfallDungeonActionModelDefinition model = new(
            action.Id,
            DoorId: null,
            DoorIdentity: null,
            Description: "HAZ",
            ModelIndex: 0,
            RawIndex: 0,
            Visual: new DaggerfallDoorVisual("fixture/hazard.json", new ContentSha256(1, 2, 3, 4), [new(0, 0)]),
            InitialTransform: new Transform(new Vector3(1, 0, 1), Quaternion.Identity, Vector3.One),
            LocalBoundsMin: new Vector3(-1, -1, -1),
            LocalBoundsMax: new Vector3(1, 1, 1),
            CollisionVertices: [Vector3.Zero, Vector3.UnitX, Vector3.UnitY],
            CollisionTriangles: [new(0, 1, 2)]);

        using DaggerfallDungeonMotionProjection projection = new(
            entities,
            spatial.Service,
            new SpatialSession(new SpatialSessionHandle(1), static () => { }),
            doors,
            "fixture/dungeon",
            [action],
            [model]);

        Assert.Single(projection.Visuals);
        Assert.Single(Assert.Single(spatial.Requests).Triangles.ToArray());
        Assert.Empty(projection.CharacterEnvironment().Obstacles.ToArray());
    }

    [Fact]
    public void Action_model_motion_updates_its_real_entity_and_engine_triangle_residency()
    {
        SpatialDouble spatial = SpatialDouble.Create();
        using EntityDirectory entities = new();
        DaggerfallDoorRuntime doors = new(entities, RandomDouble.Create(), [], "fixture/dungeon");
        Transform start = new(new Vector3(2, 0, 3), Quaternion.Identity, Vector3.One);
        DaggerfallDungeonActionDefinition action = new(
            "action/platform",
            SourceOffset: 1,
            TriggerFlag: 2,
            ActionFlag: 1,
            Axis: 2,
            Duration: 20,
            Magnitude: 40,
            NextObjectOffset: -1,
            NextActionId: null);
        DaggerfallDungeonActionModelDefinition model = new(
            action.Id,
            DoorId: null,
            DoorIdentity: null,
            Description: "PLT",
            ModelIndex: 0,
            RawIndex: 0,
            Visual: new DaggerfallDoorVisual("fixture/action.json", new ContentSha256(1, 2, 3, 4), [new(0, 0)]),
            InitialTransform: start,
            LocalBoundsMin: new Vector3(-1, -.1f, -1),
            LocalBoundsMax: new Vector3(1, .1f, 1),
            CollisionVertices: [new(-1, 0, -1), new(1, 0, -1), new(0, 0, 1)],
            CollisionTriangles: [new(0, 2, 1)]);

        using DaggerfallDungeonMotionProjection projection = new(
            entities,
            spatial.Service,
            new SpatialSession(new SpatialSessionHandle(1), static () => { }),
            doors,
            "fixture/dungeon",
            [action],
            [model]);

        CollisionResidencyRequest admitted = Assert.Single(spatial.Requests);
        Assert.Single(admitted.Assets.ToArray());
        Assert.Equal(3, admitted.Vertices.Length);
        Assert.Equal(1, admitted.Triangles.Length);
        StaticMeshInstance startInstance = Assert.Single(admitted.Instances.ToArray());
        Assert.Equal(start, startInstance.Transform);
        Assert.True(projection.TryGetEntity(action.Id, out EntityId entity));
        Assert.Equal(entity, Assert.Single(projection.Visuals).Entity);
        Assert.Equal(start, entities.Store.Get(entity, EngineComponentTypes.Transform));
        Assert.Empty(projection.CharacterEnvironment().Obstacles.ToArray());
        Assert.False(projection.CharacterEnvironment().Support.Present);

        Assert.Equal(DaggerfallDungeonMotionActivation.StartedForward, projection.Activate(action.Id));
        projection.Advance(0.5d);
        Transform moved = entities.Store.Get(entity, EngineComponentTypes.Transform);
        Assert.Equal(new Vector3(1.5f, 0, 3), moved.Translation);
        StaticMeshInstance movedInstance = Assert.Single(Assert.Single(spatial.Requests.Skip(1).ToArray()).Instances.ToArray());
        Assert.Equal(startInstance.Id, movedInstance.Id);
        Assert.Equal(moved, movedInstance.Transform);
        Assert.Empty(projection.CharacterEnvironment().Obstacles.ToArray());
    }

    private class SpatialDouble : DispatchProxy
    {
        internal ISpatialService Service { get; private set; } = null!;
        internal List<CollisionResidencyRequest> Requests { get; } = [];

        internal static SpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialDouble>();
            SpatialDouble proxy = (SpatialDouble)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name == nameof(ISpatialService.ApplyCollisionResidency))
            {
                Requests.Add((CollisionResidencyRequest)arguments![0]!);
                return new CollisionReplaceReceipt();
            }
            if (method?.ReturnType == typeof(void)) return null;
            if (method?.ReturnType.IsValueType == true) return Activator.CreateInstance(method.ReturnType);
            throw new NotSupportedException(method?.Name);
        }
    }

    private class RandomDouble : DispatchProxy
    {
        internal IRandomService Service { get; private set; } = null!;

        internal static IRandomService Create()
        {
            IRandomService service = DispatchProxy.Create<IRandomService, RandomDouble>();
            ((RandomDouble)(object)service).Service = service;
            return service;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.ReturnType == typeof(void)) return null;
            if (method?.ReturnType.IsValueType == true) return Activator.CreateInstance(method.ReturnType);
            throw new NotSupportedException(method?.Name);
        }
    }
}
