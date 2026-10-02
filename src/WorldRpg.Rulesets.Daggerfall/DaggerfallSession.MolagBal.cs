using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private Actor? MolagActor(long id) => id == State.Actors.Player.DurableId ? State.Actors.Player.Actor
        : State.Actors.TryGet(id, out var actor) ? actor.Actor : null;

    private bool MolagBalEquipped(long owner, ulong item)
    {
        if (MolagActor(owner) is not { } actor || actor.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).Current <= 0
            || !State.ItemInstances.ContainsUnique(item) || State.ItemInstances.RequireUnique(item).CurrentCondition <= 0)
            return false;
        var metadata = State.ItemInstances.RequireUnique(item);
        if (metadata.Enchantment is not { } key || !_definitions.Magic.TryEnchantments(key, out var payloads)
            || !payloads.Any(payload => payload.Type == 26 && payload.Param == 2)) return false;
        var equipment = owner == State.Actors.Player.DurableId ? State.Equipment : State.ActorInventories.EquipmentFor(owner);
        return equipment.Read().Assignments.Any(assignment => assignment.Slot.Value is "left-hand" or "right-hand"
            && equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == item);
    }

    private (double Magicka, int Strength) TransferMolagBal(long wielder, long target, ulong item,
        int damage, int strength, ulong generation, ulong step)
    {
        // Combat validates this admitted source before ordinary wear. A break during that
        // accepted strike still drains the victim, but cannot retain a wielder bonus.
        if (MolagActor(wielder) is not { } caster || MolagActor(target) is not { } victim) return default;
        var stats = caster.Get<StatsComponent>();
        Track sourceMagicka = stats.GetTrack(TrackId.Parse("magicka"));
        Track targetMagicka = victim.Get<StatsComponent>().GetTrack(TrackId.Parse("magicka"));
        // DEC-11: the donor's magicka transfer follows the artifact description, not observed classic fidelity.
        double transferred = Math.Min(damage, Math.Max(0, targetMagicka.Current - targetMagicka.Minimum));
        double overflow = Math.Max(0, sourceMagicka.Current + transferred - sourceMagicka.Maximum.Value);
        string instance = $"molag-bal.{item}.{generation}.{step}.{target}";
        if (strength > 0)
            DaggerfallAttributeDrainEffects.DrainArtifactStrength(State.Effects, instance + ".drain", wielder, target, item, strength);
        var state = new DaggerfallMolagBalState(DaggerfallMolagBalEffects.Minute(_time.Calendar), overflow, strength);
        if (MolagBalEquipped(wielder, item))
            State.Effects.Start(new(instance, DaggerfallMolagBalEffects.Key, DaggerfallMolagBalEffects.Key,
            wielder, wielder, DaggerfallMolagBalEffects.Key, "Magic", item, 1, null, DaggerfallMolagBalEffects.Encode(state))
            { BundleKind = DaggerfallEffectBundleKind.HeldMagicItem });
        targetMagicka.SetCurrent(targetMagicka.Current - transferred, clamp: true);
        sourceMagicka.SetCurrent(sourceMagicka.Current + transferred, clamp: true);
        return (transferred, strength);
    }
}
