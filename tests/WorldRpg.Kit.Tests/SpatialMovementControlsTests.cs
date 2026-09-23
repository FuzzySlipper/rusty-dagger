using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class SpatialMovementControlsTests
{
    [Fact]
    public void Vertical_velocity_zero_starts_controlled_motion_without_jump_gravity_or_buffered_jump()
    {
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.DefaultConfig = ControllerConfig();
        ContentDouble content = ContentDouble.Create();
        using SpatialMovementSystem system = new(spatial.Service, content.Service,
            new SpatialContentArtifact("spatial/test", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5d, 8, 8, 1));
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f)
        {
            Motion = default(CharacterMotion) with
            {
                ControlledVelocity = new Vector3(1f, 5f, 3f),
                ExternalVelocity = new Vector3(4f, 6f, 7f),
                Grounded = true,
                JumpBufferRemaining = .3f,
                CoyoteRemaining = .2f,
            },
        };
        ProductUpdateState update = new(1f / 60f) { PlanarIntent = new Vector2(.25f, 0f) };

        CharacterStepReceipt? receipt = system.Step(player, update, controls: new CharacterStepControls(
            JumpPressed: true, JumpHeld: true, VerticalVelocity: 0f, PlanarIntent: new Vector2(0f, 1f)));

        Assert.NotNull(receipt);
        CharacterStepRequest request = Assert.IsType<CharacterStepRequest>(spatial.Request);
        Assert.Equal(new Vector3(1f, 0f, 3f), request.Motion.ControlledVelocity);
        Assert.Equal(new Vector3(4f, 6f, 7f), request.Motion.ExternalVelocity);
        Assert.Equal(0f, request.Motion.JumpBufferRemaining);
        Assert.Equal(0f, request.Motion.CoyoteRemaining);
        Assert.Equal(0f, request.Config.Vertical.Gravity);
        Assert.Equal(0f, request.Config.Jump.BufferSeconds);
        Assert.Equal(0f, request.Config.Jump.CoyoteSeconds);
        Assert.False(request.Command.JumpPressed);
        Assert.False(request.Command.JumpHeld);
        Assert.Equal(new Vector2(0f, 1f), request.Command.PlanarIntent);
    }

    [Fact]
    public void Releasing_vertical_drive_restores_base_gravity_and_resets_only_mode_owned_motion()
    {
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.DefaultConfig = ControllerConfig();
        ContentDouble content = ContentDouble.Create();
        using SpatialMovementSystem system = new(spatial.Service, content.Service,
            new SpatialContentArtifact("spatial/test", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5d, 8, 8, 1));
        PlayerControlState player = new(new WorldPoint(1f, 12f, 3f), 0f, 0f);
        ProductUpdateState update = new(1f / 60f);

        Assert.NotNull(system.Step(player, update, controls: new CharacterStepControls(VerticalVelocity: 3f)));
        player.Motion = player.Motion with
        {
            ControlledVelocity = new Vector3(2f, 3f, 4f),
            ExternalVelocity = new Vector3(5f, 6f, 7f),
            FallOriginY = -10f,
            PeakY = 30f,
        };

        Assert.NotNull(system.Step(player, update));

        CharacterStepRequest released = Assert.IsType<CharacterStepRequest>(spatial.Request);
        Assert.Equal(ControllerConfig().Vertical.Gravity, released.Config.Vertical.Gravity);
        Assert.Equal(new Vector3(2f, 0f, 4f), released.Motion.ControlledVelocity);
        Assert.Equal(new Vector3(5f, 6f, 7f), released.Motion.ExternalVelocity);
        Assert.Equal(12f, released.Motion.FallOriginY);
        Assert.Equal(12f, released.Motion.PeakY);
    }

    [Fact]
    public void Releasing_restored_zero_gravity_checkpoint_uses_ordinary_base_configuration()
    {
        CharacterControllerConfig baseConfig = ControllerConfig();
        CharacterControllerConfig verticalConfig = baseConfig with
        {
            Vertical = baseConfig.Vertical with { Gravity = 0f },
            Jump = baseConfig.Jump with { BufferSeconds = 0f, CoyoteSeconds = 0f },
        };
        CharacterMotion checkpointMotion = default(CharacterMotion) with
        {
            ControlledVelocity = new Vector3(1f, 4f, 3f),
            ExternalVelocity = new Vector3(7f, 8f, 9f),
            FallOriginY = -1f,
            PeakY = 50f,
        };
        CharacterContinuationCheckpoint checkpoint = new(1, 1, 1, 1, 1, verticalConfig, checkpointMotion);
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.DefaultConfig = baseConfig;
        ContentDouble content = ContentDouble.Create();
        using SpatialMovementSystem system = new(spatial.Service, content.Service,
            new SpatialContentArtifact("spatial/test", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5d, 8, 8, 1));
        system.RestoreContinuation(checkpoint);
        PlayerControlState player = new(new WorldPoint(1f, 6f, 3f), 0f, 0f) { Motion = checkpointMotion };

        Assert.NotNull(system.Step(player, new ProductUpdateState(1f / 60f)));

        CharacterStepRequest released = Assert.IsType<CharacterStepRequest>(spatial.Request);
        Assert.Equal(baseConfig.Vertical.Gravity, released.Config.Vertical.Gravity);
        Assert.Equal(new Vector3(1f, 0f, 3f), released.Motion.ControlledVelocity);
        Assert.Equal(new Vector3(7f, 8f, 9f), released.Motion.ExternalVelocity);
        Assert.Equal(6f, released.Motion.FallOriginY);
        Assert.Equal(6f, released.Motion.PeakY);
    }

    [Fact]
    public void Wall_probes_use_facing_direction_lower_stance_edge_and_solver_contact_margin()
    {
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.DefaultConfig = ControllerConfig();
        spatial.QueryHit = new SpatialHit { Present = true, Kind = SpatialHitKind.Voxel, Normal = Vector3.UnitZ };
        ContentDouble content = ContentDouble.Create();
        using SpatialMovementSystem system = new(spatial.Service, content.Service,
            new SpatialContentArtifact("spatial/test", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5d, 8, 8, 1));
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);

        Assert.True(system.TryProbeClimbWall(player, CharacterWallProbeDirection.Forward, .6f, out SpatialHit forwardHit));
        Assert.True(system.TryProbeClimbWall(player, CharacterWallProbeDirection.Backward, out SpatialHit backwardHit));

        Assert.Equal(spatial.QueryHit, forwardHit);
        Assert.Equal(spatial.QueryHit, backwardHit);
        Assert.True(system.TryProbeClimbWallAtFeet(player, CharacterWallProbeDirection.Forward, out _));
        player.Motion = player.Motion with { Stance = CharacterStance.Crouched };
        Assert.True(system.TryProbeClimbWallAtFeet(player, CharacterWallProbeDirection.Forward, out _));
        SpatialRaycastRequest forward = spatial.RayRequests[0];
        SpatialRaycastRequest backward = spatial.RayRequests[1];
        SpatialRaycastRequest standingFeet = spatial.RayRequests[2];
        SpatialRaycastRequest crouchedFeet = spatial.RayRequests[3];
        Assert.Equal(new Vector3(1f, 2.6f, 3f), forward.Origin);
        Assert.Equal(-Vector3.UnitZ, forward.Direction);
        Assert.Equal(Vector3.UnitZ, backward.Direction);
        Assert.Equal(new Vector3(1f, 1.13f, 3f), standingFeet.Origin);
        Assert.Equal(new Vector3(1f, 1.43f, 3f), crouchedFeet.Origin);
        Assert.Equal(.421d, forward.MaxDistance, 4);
        Assert.Equal((uint.MaxValue, uint.MaxValue), (forward.Filter.CollisionGroup, forward.Filter.CollisionMask));
        Assert.Equal(0, forward.Entities.Length);
        Assert.Equal(0, forward.IgnoredEntities.Length);
        Assert.Equal(0, forward.HitboxOverrides.Length);
    }

    [Fact]
    public void Wall_probe_converts_enabled_character_obstacles_to_world_space_ray_colliders()
    {
        SpatialDouble spatial = SpatialDouble.Create();
        spatial.DefaultConfig = ControllerConfig();
        spatial.QueryHit = new SpatialHit { Present = true, Kind = SpatialHitKind.Entity, Entity = 42 };
        ContentDouble content = ContentDouble.Create();
        using SpatialMovementSystem system = new(spatial.Service, content.Service,
            new SpatialContentArtifact("spatial/test", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5d, 8, 8, 1));
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);
        Transform transform = new(new Vector3(4f, 1f, 6f), Quaternion.Identity, Vector3.One);
        CharacterObstacle enabled = new(
            42, transform, new Vector3(-.5f, 0f, -.1f), new Vector3(.5f, 2f, .1f), true, Vector3.Zero, Vector3.Zero);
        CharacterObstacle disabled = new(
            43, transform, new Vector3(-.5f, 0f, -.1f), new Vector3(.5f, 2f, .1f), false, Vector3.Zero, Vector3.Zero);
        CharacterStepEnvironment environment = new(default, new[] { enabled, disabled });

        Assert.True(system.TryProbeClimbWall(player, CharacterWallProbeDirection.Forward, out SpatialHit hit, environment));

        Assert.Equal(spatial.QueryHit, hit);
        SpatialEntityCollider[] colliders = spatial.RayRequests.Single().Entities.ToArray();
        SpatialEntityCollider collider = Assert.Single(colliders);
        Assert.Equal(42UL, collider.Entity);
        Assert.Equal(new Vector3(3.5f, 1f, 5.9f), collider.Min);
        Assert.Equal(new Vector3(4.5f, 3f, 6.1f), collider.Max);
        Assert.Equal(0u, collider.CollisionGroup);
        Assert.Equal(0u, collider.CollisionMask);
        Assert.True(collider.Enabled);
        Assert.False(collider.StaticCollider);
        Assert.False(collider.Trigger);
    }

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
        internal CharacterControllerConfig DefaultConfig { get; set; }
        internal CharacterStepRequest? Request { get; private set; }
        internal SpatialHit QueryHit { get; set; }
        internal List<SpatialRaycastRequest> RayRequests { get; } = [];
        internal static SpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialDouble>();
            SpatialDouble result = (SpatialDouble)(object)service;
            result.Service = service;
            return result;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ISpatialService.DefaultCharacterControllerConfig) => DefaultConfig,
            nameof(ISpatialService.ValidateCharacterControllerConfig) => null,
            nameof(ISpatialService.CreateSession) => new SpatialSession(new SpatialSessionHandle(1), static () => { }),
            nameof(ISpatialService.ReplaceContentArtifact) => new SpatialContentArtifactReplaceReceipt(),
            nameof(ISpatialService.ProposeCharacterStep) => Step((CharacterStepRequest)arguments![0]!),
            nameof(ISpatialService.CastRay) => Cast((SpatialRaycastRequest)arguments![0]!),
            nameof(ISpatialService.RestoreCharacterContinuation) => Restore((CharacterContinuationRestoreRequest)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private SpatialHit Cast(SpatialRaycastRequest request)
        {
            RayRequests.Add(request);
            return QueryHit;
        }

        private static CharacterContinuationRestoreReceipt Restore(CharacterContinuationRestoreRequest request) =>
            new(request.Checkpoint.SourceGeneration, request.Checkpoint.Motion);

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

    private static CharacterControllerConfig ControllerConfig() => default(CharacterControllerConfig) with
    {
        Shape = new CharacterShapeConfig(1.8f, 1.2f, .4f, .02f, .01f),
        Vertical = new CharacterVerticalConfig(9.8f, 20f, 30f, 8f, 1f),
        Jump = new CharacterJumpConfig(.25f, .15f, .1f, false),
        Recovery = new CharacterRecoveryConfig(2f, 10f, .001f, .002f),
    };
}
