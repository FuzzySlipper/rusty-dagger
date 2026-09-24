using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonMeshCharacterStepTests
{
    [Fact]
    public void Spatial_movement_forwards_dungeon_mesh_identity_and_velocity_to_engine_character_step()
    {
        SpatialDouble spatial = SpatialDouble.Create();
        ContentDouble content = ContentDouble.Create();
        using SpatialMovementSystem movement = new(
            spatial.Service,
            content.Service,
            new SpatialContentArtifact("spatial/test", new ContentSha256(1, 2, 3, 4), 1),
            new SpatialTuning(.5d, 8, 8, 1));
        CharacterMeshInstance mesh = new(0xD900_0000_0000_0001UL, 0x1234UL,
            new Vector3(-1f, 0f, 0f), new Vector3(0f, 2f, 0f));
        CharacterStepEnvironment environment = new(
            default,
            ReadOnlyMemory<CharacterObstacle>.Empty,
            new[] { mesh });
        PlayerControlState player = new(new WorldPoint(2f, 1f, 3f), 0f, 0f);

        Assert.NotNull(movement.Step(player, new ProductUpdateState(1f / 60f), environment));

        CharacterStepRequest request = Assert.IsType<CharacterStepRequest>(spatial.Request);
        CharacterMeshInstance forwarded = Assert.Single(request.MeshInstances.ToArray());
        Assert.Equal(mesh.Instance, forwarded.Instance);
        Assert.Equal(mesh.Entity, forwarded.Entity);
        Assert.Equal(mesh.LinearVelocity, forwarded.LinearVelocity);
        Assert.Equal(mesh.AngularVelocity, forwarded.AngularVelocity);
    }

    private class ContentDouble : DispatchProxy
    {
        internal IContentService Service { get; private set; } = null!;

        internal static ContentDouble Create()
        {
            IContentService service = DispatchProxy.Create<IContentService, ContentDouble>();
            ((ContentDouble)(object)service).Service = service;
            return (ContentDouble)(object)service;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) =>
            method?.Name == nameof(IContentService.ResolveReference)
                ? new ContentReference(new ContentReferenceHandle(1), static () => { })
                : throw new NotSupportedException(method?.Name);
    }

    private class SpatialDouble : DispatchProxy
    {
        internal ISpatialService Service { get; private set; } = null!;
        internal CharacterStepRequest? Request { get; private set; }

        internal static SpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialDouble>();
            ((SpatialDouble)(object)service).Service = service;
            return (SpatialDouble)(object)service;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ISpatialService.DefaultCharacterControllerConfig) => default(CharacterControllerConfig),
            nameof(ISpatialService.ValidateCharacterControllerConfig) => null,
            nameof(ISpatialService.CreateSession) => new SpatialSession(new SpatialSessionHandle(1), static () => { }),
            nameof(ISpatialService.ReplaceContentArtifact) => new SpatialContentArtifactReplaceReceipt(),
            nameof(ISpatialService.ProposeCharacterStep) => Step((CharacterStepRequest)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private CharacterStepReceipt Step(CharacterStepRequest request)
        {
            Request = request;
            return default(CharacterStepReceipt) with
            {
                Generation = 1,
                Transform = new Transform(request.Position, Quaternion.Identity, Vector3.One),
                Motion = request.Motion with { LastCommandSequence = request.Command.Sequence },
            };
        }
    }
}
