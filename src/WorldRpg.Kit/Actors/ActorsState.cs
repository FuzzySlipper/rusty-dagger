using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Combat;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.Targeting;

namespace WorldRpg.Kit.Actors;

/// <summary>Session actor construction and durable lookup over canonical Engine entities.</summary>
public sealed class ActorsState : IDisposable
{
    public EntityDirectory Entities { get; } = new();
    public EntityStore Store => Entities.Store;
    public PlayerActorState Player { get; private set; } = null!;
    public IEnumerable<ActorState> All => Store.Query<ActorBody>()
        .Where(entry => entry.Entity != Player?.Actor.Entity)
        .Select(entry => new ActorState(new Actor(Store, entry.Entity)));

    public PlayerActorState CreatePlayer(long id, EntityTypeId type, StatsComponent stats, string defeatTrack)
    {
        if (Player is not null) throw new InvalidOperationException("The session already has a player.");
        Actor actor = Construct(id, type, stats, defeatTrack);
        actor.Add(new ProgressionState());
        Player = new PlayerActorState(actor);
        return Player;
    }

    public ActorState CreateActor(long id, EntityTypeId type, StatsComponent stats, ActorPose pose, string defeatTrack)
    {
        Actor actor = Construct(id, type, stats, defeatTrack);
        actor.Add(new ActorBody(pose));
        return new ActorState(actor);
    }

    private Actor Construct(long id, EntityTypeId type, StatsComponent stats, string defeatTrack)
    {
        EntityId entity = Entities.Create(Identity(id), type);
        Actor actor = new(Store, entity);
        actor.Add(stats);
        actor.Add(new TargetingComponent());
        actor.Add(new AttackState());
        actor.Add(new EffectsComponent(entity));
        actor.Add(new ActorVitals(TrackId.Parse(defeatTrack)));
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
    public Actor Actor { get; } = actor;
    public long DurableId => checked((long)Actor.Get<DurableEntityIdentity>().Identity.Value);
    public AttackState Attack => Actor.Get<AttackState>();
    public TargetingComponent Targeting => Actor.Get<TargetingComponent>();
    public StatsComponent Stats => Actor.Get<StatsComponent>();
    public EffectsComponent Effects => Actor.Get<EffectsComponent>();
    public InventoryComponent Inventory => Actor.Get<InventoryComponent>();
    public EquipmentComponent Equipment => Actor.Get<EquipmentComponent>();
    public ActorPose Pose => Actor.Get<ActorBody>().Pose;
    public WorldPoint Position => Pose.Position;
    public float Heading => Pose.HeadingYawRadians;
    public float HeadingYawRadians => Pose.HeadingYawRadians;
    public void ApplyPose(ActorPose pose) => Actor.Get<ActorBody>().Pose = pose;
    public bool IsDefeated
    {
        get
        {
            Track track = Stats.GetTrack(Actor.Get<ActorVitals>().DefeatTrack);
            return track.Current <= track.Minimum;
        }
    }
}

public sealed class ActorBody(ActorPose pose)
{
    public ActorPose Pose { get; set; } = pose;
}

public sealed record ActorVitals(TrackId DefeatTrack);
