using Rusty.Engine.Entities;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Meaningful equipped-callback and cadence state, carried by the actual durable item.</summary>
internal sealed record DaggerfallItemStrikeSource(ulong ItemId, string Enchantment);

internal sealed record DaggerfallHeldCastState(long CasterId, long LastRerollMinute, string[] ActiveEffectInstances, bool RerollPending = false);

/// <summary>Item trigger policy; casting, equipment, calendar, condition and effects keep their one owners.</summary>
internal sealed class DaggerfallItemCastTriggers(DaggerfallMagicCatalogSet magic, DaggerfallItemInstances instances,
    DaggerfallCasting casting, DaggerfallEffectLifecycle effects, EntityDirectory entities,
    Func<long, MechanicsEquipmentCoordinator?> equipmentFor, DaggerfallItemConditionService condition,
    Func<long> minuteIndex, Action<long, DaggerfallItemConditionResult>? worn = null)
{
    private const int HeldType = 1, StrikeType = 2, UsedType = 0;
    private const int ActivationWear = 10, RerollMinutes = 6 * 60;
    private bool _refreshing;

    internal bool OwnsItem(long casterId, ulong itemId) => instances.ContainsUnique(itemId)
        && instances.RequireUnique(itemId).Owner == Owner(casterId);

    private static DaggerfallItemOwner Owner(long casterId) => casterId == DaggerfallActorIdentity.PlayerEntityId
        ? DaggerfallItemOwner.Player : DaggerfallItemOwner.Actor(casterId);

    private IReadOnlyList<DaggerfallMagicEnchantmentDefinition> Payloads(DaggerfallItemInstanceMetadata metadata, int type) =>
        metadata.Enchantment is { } key && magic.TryEnchantments(key, out var payloads)
            ? payloads.Where(value => value.Type == type).ToArray() : [];

    private UniqueInventoryItem? Equipped(long casterId, ulong id) => equipmentFor(casterId)?.Read().Assignments
        .Select(value => value.Item).DistinctBy(value => value.EntityId)
        .Where(item => entities.IdentityOf(new EntityId(item.EntityId)).Value == id)
        .Select(item => (UniqueInventoryItem?)item).SingleOrDefault();

    /// <summary>Reconciles real equipment after accepted changes, also covering non-player equipment and restore.</summary>
    internal void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            foreach (var entry in instances.UniqueItems.ToArray())
            {
                var metadata = entry.Value;
                if (metadata.HeldCast is null && Payloads(metadata, HeldType).Count == 0) continue;
                long? casterId = metadata.Owner.Scope is "player" or "actor" ? metadata.Owner.Id : null;
                var payloads = Payloads(metadata, HeldType);
                UniqueInventoryItem? item = casterId is long caster ? Equipped(caster, entry.Key) : null;
                bool held = item is not null && payloads.Count > 0 && metadata.CurrentCondition > 0;
                if (!held)
                {
                    if (metadata.HeldCast is null) continue;
                    effects.CancelHeldItem(entry.Key);
                    instances.ReplaceUnique(entry.Key, metadata with { HeldCast = null });
                    continue;
                }
                if (metadata.HeldCast is { } previous && previous.CasterId == casterId) continue;
                effects.CancelHeldItem(entry.Key);
                instances.ReplaceUnique(entry.Key, metadata with { HeldCast = new(casterId!.Value, minuteIndex(), []) });
                foreach (var payload in payloads)
                {
                    if (!Available(entry.Key)) break;
                    var result = Trigger(casterId.Value, entry.Key, payload, DaggerfallCastSource.ItemHeld, casterId.Value);
                    if (result.Bundle is not null && casting.AvailableSpellCost(casterId.Value, payload.SpellKey!) is int cost)
                        Wear(casterId.Value, item!.Value, Math.Max(1, cost));
                }
                RememberActiveEffects(entry.Key);
            }
        }
        finally { _refreshing = false; }
    }

    internal DaggerfallInventoryUseResult Use(UniqueInventoryItem item)
    {
        ulong id = entities.IdentityOf(new EntityId(item.EntityId)).Value;
        var metadata = instances.RequireUnique(id);
        var payloads = Payloads(metadata, UsedType);
        if (payloads.Count == 0) return new(false, "This enchantment has no use effect.");
        if (!Available(id))
        {
            Wear(metadata.Owner.Id, item, ActivationWear); // The condition owner reports AlreadyBroken.
            return new(false, "This magic item is broken.");
        }
        bool accepted = false;
        DaggerfallCastOutcome outcome = DaggerfallCastOutcome.UnknownSpell;
        foreach (var payload in payloads)
        {
            if (!Available(id)) break;
            if (payload.SpellKey is not { } key || !magic.Spells.TryGetValue(key, out var spell))
            { outcome = DaggerfallCastOutcome.UnknownSpell; continue; }
            DaggerfallCastResult result = DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType) == DaggerfallSpellTarget.CasterOnly
                ? casting.Trigger(metadata.Owner.Id, key, id, DaggerfallCastSource.ItemUse, metadata.Owner.Id)
                : casting.Ready(metadata.Owner.Id, key, id, DaggerfallCastSource.ItemUse);
            outcome = result.Outcome;
            if (result.Bundle is null && outcome != DaggerfallCastOutcome.Ready) continue;
            accepted = true;
            Wear(metadata.Owner.Id, item, ActivationWear);
        }
        return new(accepted, !Available(id) ? "The magic item broke." : outcome == DaggerfallCastOutcome.Ready
            ? "Item spell ready." : accepted ? "Item spell cast." : $"Item spell unavailable: {outcome}.");
    }

    /// <summary>The accepted attack retains this source before equipment or wear can change.</summary>
    internal static DaggerfallItemStrikeSource? CaptureStrike(DaggerfallMagicCatalogSet magic, DaggerfallItemInstances instances,
        EntityDirectory entities, UniqueInventoryItem? weapon)
    {
        if (weapon is not { } item) return null;
        ulong id = entities.IdentityOf(new EntityId(item.EntityId)).Value;
        if (!instances.ContainsUnique(id)) return null;
        var metadata = instances.RequireUnique(id);
        return metadata.CurrentCondition > 0 && metadata.Enchantment is { } key && magic.TryEnchantments(key, out var payloads)
            && payloads.Any(value => value.Type == StrikeType) ? new(id, key) : null;
    }

    internal void Strike(long casterId, long targetId, DaggerfallItemStrikeSource source, int sourceDamage)
    {
        ulong id = source.ItemId;
        if (sourceDamage <= 0 || !OwnsItem(casterId, id) || instances.RequireUnique(id).Enchantment != source.Enchantment || !Available(id) || Equipped(casterId, id) is not { } item) return;
        foreach (var payload in Payloads(instances.RequireUnique(id), StrikeType))
        {
            if (!Available(id)) break;
            var result = Trigger(casterId, id, payload, DaggerfallCastSource.ItemStrike, targetId);
            if (result.Bundle is not null) Wear(casterId, item, ActivationWear);
        }
    }

    /// <summary>Called by the one calendar fan-out. Synthetic intervals reroll but do not deteriorate held items.</summary>
    internal void AdvanceRounds(long rounds, bool synthetic, bool resting = false, long roundBefore = 0)
    {
        Refresh();
        int count = checked((int)Math.Min(Math.Max(0, rounds), DaggerfallEffectLifecycle.MaximumElapsedCatchupRounds));
        foreach (var entry in instances.UniqueItems.Where(value => value.Value.HeldCast is not null).ToArray())
        {
            var held = entry.Value.HeldCast!;
            if (Equipped(held.CasterId, entry.Key) is not { } item || !Available(entry.Key)) continue;
            if (!synthetic)
            {
                int cadence = resting ? 60 : 4;
                long ticks = (roundBefore + count + cadence - 1) / cadence - (roundBefore + cadence - 1) / cadence;
                if (ticks > 0) Wear(held.CasterId, item, checked((int)ticks));
            }
            if (held.CasterId == DaggerfallActorIdentity.PlayerEntityId
                && minuteIndex() - held.LastRerollMinute >= RerollMinutes && Available(entry.Key))
                instances.ReplaceUnique(entry.Key, instances.RequireUnique(entry.Key) with
                { HeldCast = instances.RequireUnique(entry.Key).HeldCast! with { RerollPending = true } });
        }
    }

    /// <summary>Sleep/synthetic-time completion executes scheduled rerolls once at the final calendar.</summary>
    internal void CompleteTimeIncrease()
    {
        Refresh();
        foreach (var entry in instances.UniqueItems.Where(value => value.Value.HeldCast?.RerollPending == true).ToArray())
        {
            var held = entry.Value.HeldCast!;
            if (!Available(entry.Key) || Equipped(held.CasterId, entry.Key) is null) continue;
            effects.CancelHeldItem(entry.Key);
            instances.ReplaceUnique(entry.Key, instances.RequireUnique(entry.Key) with
            { HeldCast = held with { LastRerollMinute = minuteIndex(), RerollPending = false, ActiveEffectInstances = [] } });
            foreach (var payload in Payloads(instances.RequireUnique(entry.Key), HeldType))
                Trigger(held.CasterId, entry.Key, payload, DaggerfallCastSource.ItemHeld, held.CasterId);
            RememberActiveEffects(entry.Key);
        }
    }

    private void RememberActiveEffects(ulong id)
    {
        if (!instances.ContainsUnique(id) || instances.RequireUnique(id).HeldCast is not { } held) return;
        string[] active = effects.Capture().Where(value => value.ItemId == id && value.BundleKind == DaggerfallEffectBundleKind.HeldMagicItem)
            .Select(value => value.Instance).ToArray();
        instances.ReplaceUnique(id, instances.RequireUnique(id) with { HeldCast = held with { ActiveEffectInstances = active } });
    }

    internal void EffectCompleted(DaggerfallEffectOutcome outcome)
    {
        if (outcome.Kind is not (DaggerfallEffectOutcomeKind.Cancelled or DaggerfallEffectOutcomeKind.Cured or DaggerfallEffectOutcomeKind.Expired)) return;
        foreach (var entry in instances.UniqueItems.Where(value => value.Value.HeldCast?.ActiveEffectInstances.Contains(outcome.Instance) == true).ToArray())
            instances.ReplaceUnique(entry.Key, entry.Value with { HeldCast = entry.Value.HeldCast! with
            { ActiveEffectInstances = entry.Value.HeldCast.ActiveEffectInstances.Where(value => value != outcome.Instance).ToArray() } });
    }

    private DaggerfallCastResult Trigger(long casterId, ulong id, DaggerfallMagicEnchantmentDefinition payload,
        DaggerfallCastSource source, long target) => casting.Trigger(casterId, payload.SpellKey ?? "", id, source, target);
    private bool Available(ulong id) => instances.ContainsUnique(id) && instances.RequireUnique(id).CurrentCondition > 0;
    private void Wear(long casterId, UniqueInventoryItem item, int units)
    {
        if (equipmentFor(casterId) is { } equipment)
        {
            var result = condition.Damage(item, Owner(casterId), equipment, units);
            if (result.PreviousCondition != result.Metadata.CurrentCondition) worn?.Invoke(casterId, result);
        }
    }
}
