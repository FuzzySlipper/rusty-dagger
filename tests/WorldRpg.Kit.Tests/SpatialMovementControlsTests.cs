using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class SpatialMovementControlsTests
{
    [Fact]
    public void Per_step_controls_reach_the_engine_command_and_only_override_the_selected_controller_values()
    {
        SpatialDouble spatial = SpatialDouble.Create();
        ContentDouble content = ContentDouble.Create();
        using SpatialMovementSystem system = new(spatial.Service, content.Service,
            new SpatialContentArtifact("spatial/test", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5d, 8, 8, 1));
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), .25f, 0f);
        ProductUpdateState update = new(1f / 60f) { PlanarIntent = new Vector2(.5f, 1f) };

        CharacterStepReceipt? receipt = system.Step(player, update, controls: new CharacterStepControls(
            JumpPressed: true, JumpHeld: true, CrouchRequested: true,
            ForwardSpeed: 6f, BackwardSpeed: 5f, StrafeSpeed: 4f, JumpSpeed: 8f));

        Assert.NotNull(receipt);
        CharacterStepRequest request = Assert.IsType<CharacterStepRequest>(spatial.Request);
        Assert.True(request.Command.JumpPressed);
        Assert.True(request.Command.JumpHeld);
        Assert.True(request.Command.CrouchRequested);
        Assert.Equal(new Vector2(.5f, 1f), request.Command.PlanarIntent);
        Assert.Equal(6f, request.Config.Ground.ForwardSpeed);
        Assert.Equal(5f, request.Config.Ground.BackwardSpeed);
        Assert.Equal(4f, request.Config.Ground.StrafeSpeed);
        Assert.Equal(8f, request.Config.Vertical.JumpSpeed);
        Assert.Equal(1UL, player.Motion.LastCommandSequence);
    }

    private class ContentDouble : DispatchProxy
    {
        internal IContentService Service { get; private set; } = null!;
        internal static ContentDouble Create()
        {
            IContentService service = DispatchProxy.Create<IContentService, ContentDouble>();
            ContentDouble result = (ContentDouble)(object)service;
            result.Service = service;
            return result;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IContentService.ResolveReference)
            ? new ContentReference(new ContentReferenceHandle(1), static () => { }) : throw new NotSupportedException(method?.Name);
    }

    private class SpatialDouble : DispatchProxy
    {
        internal ISpatialService Service { get; private set; } = null!;
        internal CharacterStepRequest? Request { get; private set; }
        internal static SpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialDouble>();
            SpatialDouble result = (SpatialDouble)(object)service;
            result.Service = service;
            return result;
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
