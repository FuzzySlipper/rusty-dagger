using System.Numerics;
using WorldRpg.Kit.Combat;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.Targeting;

namespace WorldRpg.Kit.Actors;

/// <summary>
/// Optional Kit-owned components a ruleset opts an actor into when it constructs it. Every actor
/// always carries its stats, effects and defeat track; the rest is the ruleset's choice.
/// </summary>
[Flags]
public enum ActorCapabilities
{
    None = 0,
    /// <summary>Current-target memory read and written by the targeting services.</summary>
    Targeting = 1 << 0,
    /// <summary>Attack readiness, cooldown and pending-impact state owned by attack execution.</summary>
    Attacks = 1 << 1,
    /// <summary>Experience and level state.</summary>
    Progression = 1 << 2,
}

/// <summary>Session actor construction and durable lookup over canonical Engine entities.</summary>
public sealed class ActorsState : IDisposable
{
    public EntityDirectory Entities { get; } = new();
    public EntityStore Store => Entities.Store;
    public PlayerActorState Player { get; private set; } = null!;

    public ActorsState()
    {
        // NPC character motion is projected through the same Engine built-ins as the player.
        // Registration is explicit so EntityCharacterController never falls back to an
        // automatic product component family with a different key.
        Store.Register(EngineComponentTypes.Transform);
        Store.Register(EngineComponentTypes.CharacterMotion);
    }
    public IEnumerable<ActorState> All => Store.Query<ActorBody>()
        .Where(entry => entry.Entity != Player?.Actor.Entity)
        .Select(entry => new ActorState(new Actor(Store, entry.Entity)));

    public PlayerActorState CreatePlayer(long id, EntityTypeId type, StatsComponent stats, string defeatTrack,
        ActorCapabilities capabilities = ActorCapabilities.None)
    {
        if (Player is not null) throw new InvalidOperationException("The session already has a player.");
        Actor actor = Construct(id, type, stats, defeatTrack, capabilities);
        Player = new PlayerActorState(actor);
        return Player;
    }

    public ActorState CreateActor(long id, EntityTypeId type, StatsComponent stats, ActorPose pose, string defeatTrack,
        ActorCapabilities capabilities = ActorCapabilities.None)
    {
        Actor actor = Construct(id, type, stats, defeatTrack, capabilities);
        actor.Add(new ActorBody());
        Store.Set(actor.Entity, EngineComponentTypes.Transform, ActorTransform.FromPose(pose, Vector3.One));
        Store.Set(actor.Entity, EngineComponentTypes.CharacterMotion, InitialCharacterMotion());
        return new ActorState(actor);
    }

    private static CharacterMotion InitialCharacterMotion() => default(CharacterMotion) with
    {
        Grounded = true,
        Stance = CharacterStance.Standing,
        SupportPreviousRotation = Quaternion.Identity,
        FallOriginY = 0f,
        PeakY = 0f,
    };

    private Actor Construct(long id, EntityTypeId type, StatsComponent stats, string defeatTrack, ActorCapabilities capabilities)
    {
        if ((capabilities & ~(ActorCapabilities.Targeting | ActorCapabilities.Attacks | ActorCapabilities.Progression)) != 0)
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        EntityId entity = Entities.Create(Identity(id), type);
        Actor actor = new(Store, entity);
        actor.Add(stats);
        if (capabilities.HasFlag(ActorCapabilities.Targeting)) actor.Add(new TargetingComponent());
        if (capabilities.HasFlag(ActorCapabilities.Attacks)) actor.Add(new AttackState());
        actor.Add(new EffectsComponent(entity));
        actor.Add(new ActorVitals(TrackId.Parse(defeatTrack)));
        if (capabilities.HasFlag(ActorCapabilities.Progression)) actor.Add(new ProgressionState());
        return actor;
    }

    public ActorState Get(long id) => new(new Actor(Store, Entities.Resolve(Identity(id))));
    public bool TryGet(long id, out ActorState actor)
    {
        if (Entities.TryResolve(Identity(id), out EntityId entity) && Store.Has<ActorBody>(entity))
        { actor = new ActorState(new Actor(Store, entity)); return true; }
        actor = null!;
        return false;
    }
    public static DurableIdentityReference Identity(long id) => new(DurableIdentityKind.Actor, checked((ulong)id));
    public void Dispose() => Entities.Dispose();
}

/// <summary>A facade over an existing entity; wrapping does not attach components or own lifetime.</summary>
public sealed class PlayerActorState(Actor actor)
{
    public ProgressionState Progression => Actor.Get<ProgressionState>();
    public Actor Actor { get; } = actor;
    public long DurableId => checked((long)Actor.Get<DurableEntityIdentity>().Identity.Value);
    public AttackState Attack => Actor.Get<AttackState>();
    public TargetingComponent Targeting => Actor.Get<TargetingComponent>();
    public StatsComponent Stats => Actor.Get<StatsComponent>();
    public EffectsComponent Effects => Actor.Get<EffectsComponent>();
    public InventoryComponent Inventory => Actor.Get<InventoryComponent>();
    public EquipmentComponent Equipment => Actor.Get<EquipmentComponent>();
    public bool IsDefeated
    {
        get
        {
            Track track = Stats.GetTrack(Actor.Get<ActorVitals>().DefeatTrack);
            return track.Current <= track.Minimum;
        }
    }
}

/// <summary>
/// Authoritative actor placement. Heading follows the Engine world convention:
/// zero faces negative Z and positive yaw turns toward positive X.
/// </summary>
public readonly record struct ActorPose
{
    [System.Text.Json.Serialization.JsonConstructor]
    public ActorPose(WorldPoint position, float headingYawRadians)
    {
        position.Validate();
        if (!float.IsFinite(headingYawRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(headingYawRadians));
        }

        Position = position;
        HeadingYawRadians = headingYawRadians;
    }

    public WorldPoint Position { get; }
    public float HeadingYawRadians { get; }
}

/// <summary>Live named access to the components of one existing actor.</summary>
public sealed class ActorState(Actor actor)
{
    public PursuitMemoryComponent Pursuit => Actor.Get<PursuitMemoryComponent>();
    /// <summary>Ordinary civilian activity memory, present only on actors admitted for wandering.</summary>
    public WanderMemoryComponent Wander => Actor.Get<WanderMemoryComponent>();
    public Actor Actor { get; } = actor;
    public long DurableId => checked((long)Actor.Get<DurableEntityIdentity>().Identity.Value);
    public AttackState Attack => Actor.Get<AttackState>();
    public TargetingComponent Targeting => Actor.Get<TargetingComponent>();
    public StatsComponent Stats => Actor.Get<StatsComponent>();
    public EffectsComponent Effects => Actor.Get<EffectsComponent>();
    public InventoryComponent Inventory => Actor.Get<InventoryComponent>();
    public EquipmentComponent Equipment => Actor.Get<EquipmentComponent>();
    public ActorPose Pose => ActorTransform.ToPose(CanonicalTransform);
    public WorldPoint Position => WorldPoint.From(CanonicalTransform.Translation);
    public float Heading => ActorTransform.Heading(CanonicalTransform.Rotation);
    public float HeadingYawRadians => Heading;
    public void ApplyPose(ActorPose pose)
    {
        Transform current = CanonicalTransform;
        Actor.Store.Set(Actor.Entity, EngineComponentTypes.Transform, ActorTransform.FromPose(pose, current.Scale));
    }

    private Transform CanonicalTransform => Actor.Store.Get(Actor.Entity, EngineComponentTypes.Transform);

    public bool IsDefeated
    {
        get
        {
            Track track = Stats.GetTrack(Actor.Get<ActorVitals>().DefeatTrack);
            return track.Current <= track.Minimum;
        }
    }
}

/// <summary>Marker for an admitted non-player actor; placement lives in the Engine Transform.</summary>
public sealed class ActorBody { }

public sealed record ActorVitals(TrackId DefeatTrack);

internal static class ActorTransform
{
    internal static Transform FromPose(ActorPose pose, Vector3 scale) => new(
        pose.Position.ToVector(),
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, -pose.HeadingYawRadians),
        scale);

    internal static ActorPose ToPose(Transform transform) => new(
        WorldPoint.From(transform.Translation),
        Heading(transform.Rotation));

    /// <summary>Reads yaw using the actor convention: zero faces -Z and positive yaw faces +X.</summary>
    internal static float Heading(Quaternion rotation)
    {
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, Quaternion.Normalize(rotation));
        return (float)Math.Atan2(forward.X, -forward.Z);
    }
}
