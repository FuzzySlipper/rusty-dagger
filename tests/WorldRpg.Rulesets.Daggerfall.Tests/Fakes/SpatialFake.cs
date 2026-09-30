using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

internal class SpatialFake : DispatchProxy
{
    internal bool KeepPosition { get; set; }
    internal Func<SpatialRaycastRequest, SpatialHit> FloorHit { get; set; } = _ => default;
    internal List<SpatialRaycastRequest> FloorProbes { get; } = [];
    private SpatialHit ProbeFloor(SpatialRaycastRequest request) { FloorProbes.Add(request); return FloorHit(request); }
    private ContentSha256 hash = Hash;
    private List<string> releases = null!;
    internal ISpatialService Service { get; private set; } = null!;
    internal int ReplaceCalls { get; private set; }
    internal int ReadCalls { get; private set; }
    internal int CreateSessionCalls { get; private set; }
    internal int StepCalls { get; private set; }
    internal int ConfigValidationCalls { get; private set; }
    internal int CommandValidationCalls { get; private set; }
    internal bool RejectConfigValidation { get; set; }
    internal bool RejectProposedStep { get; set; }
    internal bool RejectContentReplacement { get; set; }
    internal SpatialContentArtifactReplaceRequest? LastRequest { get; private set; }
    internal List<CharacterStepRequest> StepRequests { get; } = [];
    internal List<CollisionResidencyRequest> CollisionResidencyRequests { get; } = [];
    // Representative fixture only: Engine owns the actual default and validity contract.
    internal CharacterControllerConfig RepresentativeValidConfig { get; } = default(CharacterControllerConfig) with
    {
        Shape = new CharacterShapeConfig(2.2f, 1.3f, .45f, .03f, .02f),
        Ground = new CharacterGroundConfig(6f, 5f, 4f, 31f, 42f, 7f, 3f, 2f),
        Air = new CharacterAirConfig(4f, 10f, 1f, 4f, 1f, 0f),
        Vertical = new CharacterVerticalConfig(18f, 48f, 46f, 6f, .4f),
        Jump = new CharacterJumpConfig(.2f, .15f, 0f, false),
        Surface = new CharacterSurfaceConfig(.9f, .02f, 16f, 9f, .35f, .04f, .2f, 8f, .2f),
        Recovery = new CharacterRecoveryConfig(.7f, 18f, .002f, .003f),
        Platform = new CharacterPlatformConfig(true, true, true, .8f, 0f, .03f),
        ExternalMotion = new CharacterExternalMotionConfig(1f, 0f, 40f, 70f, 1f, 400f),
        Solver = new CharacterSolverConfig(4, 7, 3, 24, 1, 8f, 48),
    };

    internal static SpatialFake Create(ContentSha256 contentHash, List<string> releaseLog)
    {
        ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialFake>();
        SpatialFake fake = (SpatialFake)(object)service;
        fake.Service = service;
        fake.hash = contentHash;
        fake.releases = releaseLog;
        return fake;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
    {
        nameof(ISpatialService.CreateSession) => CreateSession(),
        nameof(ISpatialService.DefaultCharacterControllerConfig) => RepresentativeValidConfig,
        nameof(ISpatialService.CastRay) => ProbeFloor((SpatialRaycastRequest)arguments![0]!),
        nameof(ISpatialService.ValidateCharacterControllerConfig) => ValidateConfig((CharacterControllerConfig)arguments![0]!),
        nameof(ISpatialService.ValidateCharacterControllerCommand) => ValidateCommand((CharacterControllerValidationRequest)arguments![0]!),
        nameof(ISpatialService.ReplaceContentArtifact) => Replace((SpatialContentArtifactReplaceRequest)arguments![0]!),
        nameof(ISpatialService.ApplyCollisionResidency) => ApplyCollisionResidency((CollisionResidencyRequest)arguments![0]!),
        nameof(ISpatialService.ReadContentArtifact) => Read(),
        nameof(ISpatialService.ProposeCharacterStep) => Step((CharacterStepRequest)arguments![0]!),
        // These general session tests have no authored trigger contact. Action trigger
        // edge behavior is exercised by its dedicated Spatial fake.
        nameof(ISpatialService.RegisterTrigger) => null,
        nameof(ISpatialService.SetTriggerActive) => default(SpatialTriggerLifecycleResult),
        nameof(ISpatialService.RestoreTriggers) => default(SpatialTriggerRestoreReceipt),
        nameof(ISpatialService.ReconcileTriggers) => default(SpatialTriggerReconcileResult),
        // Navigation is not under test here: an honest no-path receipt leaves the
        // actor's pose intact instead of reporting a bogus waypoint.
        nameof(ISpatialService.EvaluateNavigationStep) => NoNavigationPath((NavigationStepRequest)arguments![0]!),
        nameof(ISpatialService.CaptureCharacterContinuation) => Capture((CharacterContinuationCaptureRequest)arguments![0]!),
        nameof(ISpatialService.RestoreCharacterContinuation) => Restore((CharacterContinuationRestoreRequest)arguments![0]!),
        _ => throw new NotSupportedException(method?.Name),
    };

    private static NavigationStepResult NoNavigationPath(NavigationStepRequest request) => new(
        ReadOnlyMemory<PlanarNavCell>.Empty, NavigationPathOutcome.NoPath, request.Target, default, 0, 0, 0, 0, 0);

    private SpatialContentArtifactReplaceReceipt Replace(SpatialContentArtifactReplaceRequest request)
    {
        ReplaceCalls++;
        if (RejectContentReplacement) throw new InvalidOperationException("Rejected spatial content replacement.");
        LastRequest = request;
        return new(request.Content.Handle.Value, hash, 1, 2, 3, 4, 5, 6, 7, 8);
    }

    private CollisionReplaceReceipt ApplyCollisionResidency(CollisionResidencyRequest request)
    {
        CollisionResidencyRequests.Add(request);
        return new CollisionReplaceReceipt();
    }

    private SpatialSession CreateSession()
    {
        CreateSessionCalls++;
        return new SpatialSession(new SpatialSessionHandle(1), () => releases.Add("session"));
    }

    private object? ValidateConfig(CharacterControllerConfig config)
    {
        ConfigValidationCalls++;
        if (RejectConfigValidation) throw new InvalidOperationException("Rejected controller configuration.");
        return null;
    }

    private object? ValidateCommand(CharacterControllerValidationRequest request)
    {
        CommandValidationCalls++;
        return null;
    }

    private SpatialContentArtifactReadout Read()
    {
        ReadCalls++;
        SpatialContentArtifactReplaceRequest request = LastRequest ?? throw new InvalidOperationException("Read before replace.");
        return new(true, request.Content.Handle.Value, hash, 2, 3, 4, 5, 6, 7, 8);
    }

    private CharacterStepReceipt Step(CharacterStepRequest request)
    {
        if (RejectProposedStep) throw new InvalidOperationException("Rejected controller command.");
        StepCalls++;
        StepRequests.Add(request);
        return default(CharacterStepReceipt) with
        {
            Generation = checked((ulong)StepCalls),
            Transform = new Transform(KeepPosition ? request.Position : request.Position + new Vector3(1f, 0f, 0f), Quaternion.Identity, Vector3.One),
            Displacement = !KeepPosition && request.Command.PlanarIntent != Vector2.Zero ? Vector3.UnitX : Vector3.Zero,
            Motion = request.Motion with { Grounded = true, LastCommandSequence = request.Command.Sequence },
            Ground = default(CharacterGround) with { Present = true },
        };
    }

    private CharacterContinuationCheckpoint Capture(CharacterContinuationCaptureRequest request)
    {
        if (request.ExpectedGeneration != checked((ulong)StepCalls)) throw new InvalidOperationException("Stale checkpoint generation.");
        CharacterMotion motion = StepRequests.Last().Motion with { LastCommandSequence = StepRequests.Last().Command.Sequence };
        return new CharacterContinuationCheckpoint(1, request.ExpectedGeneration, 1, 1, 1, RepresentativeValidConfig, motion);
    }

    private static CharacterContinuationRestoreReceipt Restore(CharacterContinuationRestoreRequest request) =>
        new(request.Checkpoint.SourceGeneration, request.Checkpoint.Motion);
}
