using Rusty.Engine.Entities;

namespace WorldRpg.Kit.World;

/// <summary>Session mapping from product instance identity to Engine-generated entities.</summary>
public sealed class EntityDirectory : IDisposable
{
    private readonly Dictionary<DurableIdentityReference, EntityId> _entities = [];
    public EntityStore Store { get; } = new();

    public EntityId Create(DurableIdentityReference identity, EntityTypeId type)
    {
        identity.Validate();
        if (TryResolve(identity, out _)) throw new InvalidOperationException($"{identity} already exists.");
        EntityId entity = Store.Create(type);
        Store.Add(entity, new DurableEntityIdentity(identity));
        _entities.Add(identity, entity);
        return entity;
    }

    /// <summary>
    /// Creates one live item entity from its durable item identity. The caller owns failure
    /// cleanup through <see cref="Destroy"/>: grant, equipment, and container paths share this
    /// construction but keep their own transaction semantics.
    /// </summary>
    public EntityId CreateItemEntity(DurableIdentityReference identity, EntityTypeId type)
    {
        identity.Validate();
        if (identity.Kind != DurableIdentityKind.Item)
            throw new ArgumentException("An inventory item requires a durable item identity.", nameof(identity));
        return Create(identity, type);
    }

    public EntityId Resolve(DurableIdentityReference identity) =>
        TryResolve(identity, out EntityId entity) ? entity : throw new KeyNotFoundException($"No live entity for {identity}.");

    public bool TryResolve(DurableIdentityReference identity, out EntityId entity)
    {
        if (_entities.TryGetValue(identity, out entity) && Store.IsAlive(entity)) return true;
        _entities.Remove(identity);
        entity = default;
        return false;
    }

    public DurableIdentityReference IdentityOf(EntityId entity) => Store.Get<DurableEntityIdentity>(entity).Identity;

    public bool Destroy(DurableIdentityReference identity)
    {
        if (!_entities.Remove(identity, out EntityId entity)) return false;
        if (Store.IsAlive(entity)) Store.Destroy(entity);
        return true;
    }

    public void Dispose() { _entities.Clear(); Store.Dispose(); }
}

public sealed record DurableEntityIdentity(DurableIdentityReference Identity);
