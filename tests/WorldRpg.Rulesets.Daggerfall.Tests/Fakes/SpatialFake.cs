using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

internal class SpatialFake : DispatchProxy
{
    internal bool KeepPosition { get; set; }
    internal Func<CharacterStepRequest, CharacterMovementFact> MovementFact { get; set; } = _ => default;
    internal Func<SpatialCapsuleQueryRequest, SpatialHit> OverlapHit { get; set; } = _ => default;
    internal List<SpatialCapsuleQueryRequest> OverlapRequests { get; } = [];
    private SpatialHit Overlap(SpatialCapsuleQueryRequest request) { OverlapRequests.Add(request); return OverlapHit(request); }
    internal Func<SpatialCapsuleQueryRequest, SpatialHit> CapsuleCastHit { get; set; } = _ => default;
    internal List<SpatialCapsuleQueryRequest> CapsuleCastRequests { get; } = [];
    private SpatialHit CastCapsule(SpatialCapsuleQueryRequest request) { CapsuleCastRequests.Add(request); return CapsuleCastHit(request); }
    internal Func<SpatialRaycastRequest, SpatialHit> FloorHit { get; set; } = _ => default;
    internal List<SpatialRaycastRequest> FloorProbes { get; } = [];
    private SpatialHit ProbeFloor(SpatialRaycastRequest request) { FloorProbes.Add(request); return FloorHit(request); }
    private ContentSha256 hash = Hash;
    private List<string> releases = null!;
    internal ISpatialService Service { get; private set; } = null!;
    internal int ReplaceCalls { get; private set; }
    internal List<SpatialContentArtifactResidencyRequest> ContentResidencyRequests { get; } = [];
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
    internal List<SpatialTriggerRegisterRequest> TriggerRegistrations { get; } = [];
    internal List<SpatialTriggerSetActiveRequest> TriggerLifecycleRequests { get; } = [];
    /// <summary>Every residency delta the product applied, in order, including one the fake refused.</summary>
    internal List<CollisionResidencyRequest> CollisionResidencyRequests { get; } = [];
    /// <summary>The collision assets resident in the most recently created spatial session.</summary>
    internal IReadOnlyCollection<ulong> ResidentCollisionAssets => CurrentSession.Assets;
    /// <summary>The collision instances resident in the most recently created session, each with the asset it uses.</summary>
    internal IReadOnlyDictionary<ulong, ulong> ResidentCollisionInstances => CurrentSession.Instances;
    /// <summary>Whether the product released its most recent spatial session, which drops every collider it retained.</summary>
    internal bool SessionReleased => CurrentSession.Released;
    /// <summary>
    /// How many assets and instances the product still had resident when it released its most recent
    /// session, which is what it left for the release to drop instead of removing itself.
    /// </summary>
    internal (int Assets, int Instances) CollidersLeftAtRelease => CurrentSession.LeftAtRelease
        ?? throw new InvalidOperationException("The most recent spatial session has not been released.");
    private readonly Dictionary<ulong, SessionColliders> sessions = [];
    private ulong latestSession;
    private SessionColliders CurrentSession => sessions.TryGetValue(latestSession, out SessionColliders? current)
        ? current
        : throw new InvalidOperationException("No spatial session was created.");
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
        nameof(ISpatialService.OverlapCapsule) => Overlap((SpatialCapsuleQueryRequest)arguments![0]!),
        nameof(ISpatialService.CastCapsule) => CastCapsule((SpatialCapsuleQueryRequest)arguments![0]!),
        nameof(ISpatialService.CastRay) => ProbeFloor((SpatialRaycastRequest)arguments![0]!),
        nameof(ISpatialService.ValidateCharacterControllerConfig) => ValidateConfig((CharacterControllerConfig)arguments![0]!),
        nameof(ISpatialService.ValidateCharacterControllerCommand) => ValidateCommand((CharacterControllerValidationRequest)arguments![0]!),
        nameof(ISpatialService.ReplaceContentArtifact) => Replace((SpatialContentArtifactReplaceRequest)arguments![0]!),
        nameof(ISpatialService.ApplyContentArtifactResidency) => ApplyContentResidency((SpatialContentArtifactResidencyRequest)arguments![0]!),
        nameof(ISpatialService.ApplyCollisionResidency) => ApplyCollisionResidency((CollisionResidencyRequest)arguments![0]!),
        nameof(ISpatialService.ReadContentArtifact) => Read(),
        nameof(ISpatialService.ProposeCharacterStep) => Step((CharacterStepRequest)arguments![0]!),
        // These general session tests have no authored trigger contact. Action trigger
        // edge behavior is exercised by its dedicated Spatial fake.
        nameof(ISpatialService.RegisterTrigger) => RegisterTrigger((SpatialTriggerRegisterRequest)arguments![0]!),
        nameof(ISpatialService.SetTriggerActive) => SetTriggerActive((SpatialTriggerSetActiveRequest)arguments![0]!),
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
        ReadOnlyMemory<PlanarNavCell>.Empty, ReadOnlyMemory<NavigationPathEdge>.Empty, NavigationPathOutcome.NoPath, request.Target, default, default, 0f, 0, 0, 0, 0, 0, false, default, default);

    private object? RegisterTrigger(SpatialTriggerRegisterRequest request)
    {
        TriggerRegistrations.Add(request);
        return null;
    }

    private SpatialTriggerLifecycleResult SetTriggerActive(SpatialTriggerSetActiveRequest request)
    {
        TriggerLifecycleRequests.Add(request);
        return new SpatialTriggerLifecycleResult(ReadOnlyMemory<SpatialTriggerFact>.Empty, request.Trigger, request.Active, 0);
    }

    private SpatialContentArtifactReplaceReceipt Replace(SpatialContentArtifactReplaceRequest request)
    {
        ReplaceCalls++;
        if (RejectContentReplacement) throw new InvalidOperationException("Rejected spatial content replacement.");
        LastRequest = request;
        // A content artifact replaces the session's complete static collision set, so every collider a
        // residency delta admitted is gone afterwards and has to be admitted again.
        SessionColliders colliders = Live(request.Session);
        colliders.Assets.Clear();
        colliders.Instances.Clear();
        return new(request.Content.Handle.Value, hash, 1, 2, 3, 4, 5, 6, 7, 8);
    }

    private SpatialContentArtifactResidencyReceipt ApplyContentResidency(SpatialContentArtifactResidencyRequest request)
    {
        ContentResidencyRequests.Add(request);
        // Keep the historical counter as a count of accepted/attempted static world admissions so
        // the existing transition tests continue to assert one Engine admission per profile change.
        ReplaceCalls++;
        if (RejectContentReplacement) throw new InvalidOperationException("Rejected spatial content replacement.");
        return new SpatialContentArtifactResidencyReceipt(0UL, (ulong)request.Admitted.Length,
            (ulong)request.Admitted.Length, 0UL, 0UL, 0UL, 0UL);
    }

    /// <summary>
    /// Applies one residency delta the way Engine Spatial does: removals precede upserts and a missing
    /// removal is harmless; each asset reads its own vertex and triangle slice, with triangle indices
    /// local to that slice; an identity may appear once per delta; every retained instance must name a
    /// resident asset. A delta that breaks any of these is refused whole and changes nothing.
    /// </summary>
    private CollisionReplaceReceipt ApplyCollisionResidency(CollisionResidencyRequest request)
    {
        CollisionResidencyRequests.Add(request);
        SessionColliders colliders = Live(request.Session);
        HashSet<ulong> nextAssets = [.. colliders.Assets];
        foreach (ulong removed in request.RemovedAssets.Span) nextAssets.Remove(removed);
        HashSet<ulong> admitted = [];
        foreach (StaticMeshAsset asset in request.Assets.Span)
        {
            if (!admitted.Add(asset.Id)) throw new InvalidOperationException($"Collision asset {asset.Id:X} appears twice in one delta.");
            if ((ulong)asset.FirstVertex + asset.VertexCount > (ulong)request.Vertices.Length
                || (ulong)asset.FirstTriangle + asset.TriangleCount > (ulong)request.Triangles.Length)
                throw new InvalidOperationException($"Collision asset {asset.Id:X} names a slice outside the delta's arrays.");
            foreach (Triangle triangle in request.Triangles.Span.Slice(checked((int)asset.FirstTriangle), checked((int)asset.TriangleCount)))
            {
                if (triangle.A >= asset.VertexCount || triangle.B >= asset.VertexCount || triangle.C >= asset.VertexCount)
                    throw new InvalidOperationException($"Collision asset {asset.Id:X} has a triangle outside its own vertex slice.");
            }
            nextAssets.Add(asset.Id);
        }
        Dictionary<ulong, ulong> nextInstances = new(colliders.Instances);
        foreach (ulong removed in request.RemovedInstances.Span) nextInstances.Remove(removed);
        HashSet<ulong> upserted = [];
        foreach (StaticMeshInstance instance in request.Instances.Span)
        {
            if (!upserted.Add(instance.Id)) throw new InvalidOperationException($"Collision instance {instance.Id:X} appears twice in one delta.");
            nextInstances[instance.Id] = instance.Asset;
        }
        foreach ((ulong instance, ulong asset) in nextInstances)
        {
            if (!nextAssets.Contains(asset)) throw new InvalidOperationException($"Collision instance {instance:X} names asset {asset:X}, which is not resident.");
        }
        colliders.Assets.Clear();
        colliders.Assets.UnionWith(nextAssets);
        colliders.Instances.Clear();
        foreach ((ulong instance, ulong asset) in nextInstances) colliders.Instances.Add(instance, asset);
        ulong before = colliders.Revision++;
        return new CollisionReplaceReceipt(before, colliders.Revision, (ulong)colliders.Assets.Count, (ulong)colliders.Instances.Count, 0);
    }

    private SessionColliders Live(SpatialSession session)
    {
        if (!sessions.TryGetValue(session.Handle.Value, out SessionColliders? colliders))
            throw new InvalidOperationException($"Spatial session {session.Handle.Value} was never created.");
        if (colliders.Released) throw new InvalidOperationException($"Spatial session {session.Handle.Value} was already released.");
        return colliders;
    }

    private SpatialSession CreateSession()
    {
        CreateSessionCalls++;
        ulong handle = checked((ulong)CreateSessionCalls);
        SessionColliders colliders = new();
        sessions.Add(handle, colliders);
        latestSession = handle;
        return new SpatialSession(new SpatialSessionHandle(handle), () =>
        {
            releases.Add("session");
            // Releasing a session releases every collider it retained.
            colliders.Released = true;
            colliders.LeftAtRelease = (colliders.Assets.Count, colliders.Instances.Count);
            colliders.Assets.Clear();
            colliders.Instances.Clear();
        });
    }

    /// <summary>The static colliders one spatial session retains.</summary>
    private sealed class SessionColliders
    {
        internal HashSet<ulong> Assets { get; } = [];
        internal Dictionary<ulong, ulong> Instances { get; } = [];
        internal ulong Revision { get; set; }
        internal bool Released { get; set; }
        internal (int Assets, int Instances)? LeftAtRelease { get; set; }
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
            Movement = MovementFact(request),
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
