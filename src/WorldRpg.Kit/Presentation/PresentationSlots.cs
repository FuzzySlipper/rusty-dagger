namespace WorldRpg.Kit.Presentation;

/// <summary>
/// One ordered thing a domain owner asks the product to show, with the owner's own identity so
/// the product never has to know what the owner means.
/// </summary>
/// <param name="Owner">The owner that published it, such as an effect or a quest source.</param>
/// <param name="Id">The owner's own identifier for this slot.</param>
/// <param name="Label">What the player reads first.</param>
/// <param name="Detail">The supporting line, or empty when there is none.</param>
/// <param name="Order">Where it sorts among the slots; lower comes first.</param>
public sealed record PresentationSlot(string Owner, string Id, string Label, string Detail, int Order);

/// <summary>
/// The slots a product publishes for owners that live elsewhere: effects, escorts, quests and any
/// later domain that wants a status row. One owner cannot disturb another's rows, and every row
/// carries the owner's identity so a teardown is possible without the product knowing the meaning.
/// </summary>
/// <remarks>
/// Publishing the same owner and id twice replaces the row rather than adding a second one, which is
/// what a status that changes value needs. Retiring an owner drops all of its rows at once, which is
/// what a source going away needs. Both are the product's only teardown paths, so a slot cannot
/// outlive the state that justified it without someone forgetting to retire it.
/// </remarks>
public sealed class PresentationSlots
{
    private readonly List<PresentationSlot> _slots = [];

    /// <summary>The published slots, ordered by <see cref="PresentationSlot.Order"/> then label.</summary>
    public IReadOnlyList<PresentationSlot> Read() =>
        [.. _slots.OrderBy(slot => slot.Order).ThenBy(slot => slot.Label, StringComparer.Ordinal).ThenBy(slot => slot.Owner, StringComparer.Ordinal).ThenBy(slot => slot.Id, StringComparer.Ordinal)];

    /// <summary>Publishes a slot, replacing the same owner's slot with the same id.</summary>
    public void Publish(PresentationSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot.Owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot.Id);
        ArgumentNullException.ThrowIfNull(slot.Label);
        ArgumentNullException.ThrowIfNull(slot.Detail);
        int existing = _slots.FindIndex(current => current.Owner == slot.Owner && current.Id == slot.Id);
        if (existing >= 0) _slots[existing] = slot;
        else _slots.Add(slot);
    }

    /// <summary>Retires one owner's slot, and reports whether it was published.</summary>
    public bool Retire(string owner, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _slots.RemoveAll(current => current.Owner == owner && current.Id == id) > 0;
    }

    /// <summary>Retires every slot one owner published, and reports how many went away.</summary>
    public int RetireOwner(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        return _slots.RemoveAll(current => current.Owner == owner);
    }
}
