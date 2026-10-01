using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

internal sealed partial class DaggerCombatRules
{
    private const int SpecialArtifactEffect = 26;
    private const int NamiraArtifact = 7;
    private const string NamiraDamage = "artifact.namira";

    // FormulaHelper's caller tests the player's two ring slots once. Current equipment is the
    // source of truth, so unequip, destruction, break and restore need no second active-state store.
    private void ReflectNamira(CombatParticipants incoming, long attacker, long target, ApplyHitEvent applied,
        ulong generation, ulong step, FactBuffer<IProductFact> facts)
    {
        if (target != PlayerId || attacker == PlayerId || applied.Damage <= 0 || _itemCondition is null
            || !_definitions.TryGetValue(attacker, out var enemy)) return;
        int reflected = NamiraReflection(enemy.Team, applied.Damage);
        if (reflected == 0) return;
        var ring = _equipment.Read().Assignments
            .Where(assignment => assignment.Slot.Value is "ring0" or "ring1")
            .OrderBy(assignment => assignment.Slot.Value, StringComparer.Ordinal)
            .Select(assignment => assignment.Item)
            .Where(item =>
            {
                var identity = _equipment.Entities.IdentityOf(new EntityId(item.EntityId));
                if (!_itemInstances.ContainsUnique(identity.Value)) return false;
                var metadata = _itemInstances.RequireUnique(identity.Value);
                return metadata.CurrentCondition > 0 && metadata.Enchantment is { } key
                    && _catalog.Magic.TryEnchantments(key, out var payloads)
                    && payloads.Any(effect => effect.Type == SpecialArtifactEffect && effect.Param == NamiraArtifact);
            })
            .Select(item => (WorldRpg.Kit.Inventory.UniqueInventoryItem?)item).FirstOrDefault();
        if (ring is not { } equippedRing) return;
        ulong sourceItem = _equipment.GetDurableItemId(new EntityId(equippedRing.EntityId)).Value;
        CombatParticipants participants = new(incoming.Target, incoming.Source, NamiraDamage);
        ApplyHitEvent result = Rules.ApplyToHealth(participants, reflected, 0,
            participants.TargetStats.GetTrack(TrackId.Parse(HealthTrack)));
        facts.Append(new ArtifactDamageReflectedFact(sourceItem, target, attacker, reflected, result.ActualHealthLost, generation, step));
        facts.Append(new DamageAppliedFact(target, attacker, DaggerfallDamageCause.Effect,
            result.CalculatedDamage, result.ActualHealthLost, 0, generation, step));
        if (result.ActualHealthLost > 0)
            facts.Append(new ActorDamagedFact(attacker, target, DaggerfallDamageCause.Effect, result.CalculatedDamage, result.ActualHealthLost));
        if (result.Defeated)
            facts.Append(new ActorDiedFact(attacker, target, DaggerfallDamageCause.Effect, result.CalculatedDamage, result.ActualHealthLost, generation, step));
        // The callback's cost is its reflected amount, even when the enemy has fewer health points.
        DamageCondition(equippedRing, target, reflected, generation, step, facts);
    }

    internal static int NamiraReflection(string? team, int damage) => team switch
    {
        "vermin" or "spriggans" or "bears" or "tigers" or "spiders" or "scorpions" => 0,
        "daedra" => damage / 2,
        "undead" => checked(damage * 2),
        _ => damage,
    };
}
