using Rusty.Engine;

namespace WorldRpg.Kit.Inventory;

public readonly record struct InventoryItemId(string Value);
public sealed record InventoryGrant(string Operation, string SourceInstance, InventoryItemId Item, ulong Quantity)
{
    public InventoryGrant Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceInstance);
        ArgumentException.ThrowIfNullOrWhiteSpace(Item.Value);
        ArgumentOutOfRangeException.ThrowIfZero(Quantity);
        return this;
    }
}

/// <summary>Thin revision-guarded product coordinator over Engine-authoritative inventory state.</summary>
public sealed class MechanicsInventoryCoordinator(IMechanicsService mechanics, MechanicsEntity owner)
{
    private readonly IMechanicsService _mechanics = mechanics ?? throw new ArgumentNullException(nameof(mechanics));
    private readonly MechanicsEntity _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public MechanicsInventoryViewLeaseReceipt Read() => _mechanics.ReadInventoryView(_owner);

    public MechanicsInventoryMutationLeaseReceipt Grant(InventoryGrant grant)
    {
        grant.Validate();
        MechanicsInventoryViewLeaseReceipt current = Read();
        return _mechanics.GrantInventory(new MechanicsInventoryMutationRequest(
            _owner,
            grant.Operation,
            MechanicsActiveEffectProvenanceKind.Request,
            0,
            string.Empty,
            0,
            string.Empty,
            0,
            string.Empty,
            0,
            0,
            string.Empty,
            grant.Operation,
            grant.SourceInstance,
            grant.Item.Value,
            grant.Quantity,
            MechanicsRevisionGuard.Exact,
            current.InventoryRevision));
    }
}
