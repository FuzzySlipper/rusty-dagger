using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
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
    private readonly Func<long, long, ulong, int, int, ulong, ulong, (double Magicka, int Strength)>? _molagBalStrike;

    private (WorldRpg.Kit.Inventory.UniqueInventoryItem Weapon, ulong Identity)? CaptureMolagBalSource(long attacker)
    {
        if (_molagBalStrike is null || EquippedWeapon(attacker) is not { } weapon) return null;
        var equipment = attacker == PlayerId ? _equipment : _actorEquipment(attacker);
        ulong identity = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        if (!_itemInstances.ContainsUnique(identity)) return null;
        var item = _itemInstances.RequireUnique(identity);
        return item.CurrentCondition > 0 && item.Enchantment is { } key
            && _catalog.Magic.TryEnchantments(key, out var payloads)
            && payloads.Any(payload => payload.Type == SpecialArtifactEffect && payload.Param == 2)
            ? (weapon, identity) : null;
    }

    private void ApplyMolagBal(long attacker, long target,
        (WorldRpg.Kit.Inventory.UniqueInventoryItem Weapon, ulong Identity) source,
        int damage, bool enemy, ulong generation, ulong step, FactBuffer<IProductFact> facts)
    {
        if (!TryResolve(target, out var victim)) return;
        var profile = DaggerfallMagicProfiles.Create(victim.Stats, victim.Definition,
            target == PlayerId ? _character() : null, _catalog, _magicDefense(target));
        ExplicitMeleeRequest request = new(attacker, target, generation, step, 1d);
        if (DaggerfallMagicAdmissionPolicy.SavingThrow(DaggerfallMagicResistanceElement.Magic,
            DaggerfallMagicEffectFlags.Magic, profile, 0,
            () => Draw(request, attacker, target, CombatRandomKey.MolagBalSavingThrowSalt, 1, 100, enemy)) == 0) return;
        int strength = victim.Stats.GetTrack(TrackId.Parse("magicka")).Current <= 0
            ? Draw(request, attacker, target, CombatRandomKey.MolagBalStrengthSalt, 1, 6, enemy) : 0;
        var transferred = _molagBalStrike!(attacker, target, source.Identity, damage, strength, generation, step);
        facts.Append(new ArtifactResourceTransferredFact(source.Identity, attacker, target,
            transferred.Magicka, transferred.Strength, generation, step));
        DamageCondition(source.Weapon, attacker, damage, generation, step, facts);
    }

    private ulong? CaptureWabbajackSource(long attacker)
    {
        if (EquippedWeapon(attacker) is not { } weapon) return null;
        var equipment = attacker == PlayerId ? _equipment : _actorEquipment(attacker);
        ulong identity = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        if (!_itemInstances.ContainsUnique(identity)) return null;
        var item = _itemInstances.RequireUnique(identity);
        return item.CurrentCondition > 0 && item.Enchantment is { } key
            && _catalog.Magic.TryEnchantments(key, out var payloads)
            && payloads.Any(payload => payload.Type == SpecialArtifactEffect && payload.Param == 6) ? identity : null;
    }

    private bool TryTransform(AttackRequest request, PreparedAttack attack, FactBuffer<IProductFact> facts, out ActorTransformedFact? transformation)
    {
        transformation = null;
        if (request.TargetId is not long target || attack is not DaggerfallPreparedAttack { WabbajackSource: ulong source }) return false;
        DaggerfallWabbajackResult result = _transformActor?.Invoke(request.AttackerId, target, source, request.Generation, request.SimulationStep)
            ?? new(DaggerfallWabbajackOutcome.InvalidTarget, target);
        transformation = new ActorTransformedFact(source, request.AttackerId, target, result.Outcome, result.Definition, request.Generation, request.SimulationStep);
        if (result.Outcome == DaggerfallWabbajackOutcome.Transformed) facts.Append(transformation);
        return result.Outcome == DaggerfallWabbajackOutcome.Transformed;
    }

    // MehrunesRazorEffect adds the victim's current health to the accepted strike and charges
    // that same amount to its source. The ordinary damage path applies health once and
    // charges physical wear separately from this magic cost.
    private int ApplyRazor(long attacker, long target, int damage, Track health, bool enemy,
        ulong generation, ulong step, FactBuffer<IProductFact> facts, out WorldRpg.Kit.Inventory.UniqueInventoryItem? chargedWeapon, out int chargedUnits)
    {
        chargedWeapon = null; chargedUnits = 0;
        if (health.Current <= health.Minimum || _itemCondition is null
            || EquippedWeapon(attacker) is not { } weapon) return damage;
        var equipment = attacker == PlayerId ? _equipment : _actorEquipment(attacker);
        ulong source = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        if (!_itemInstances.ContainsUnique(source)) return damage;
        var metadata = _itemInstances.RequireUnique(source);
        if (metadata.CurrentCondition <= 0 || metadata.Enchantment is not { } key
            || !_catalog.Magic.TryEnchantments(key, out var payloads)
            || !payloads.Any(effect => effect.Type == SpecialArtifactEffect && effect.Param == 1)) return damage;
        if (!TryResolve(target, out var victim)) return damage;
        var character = target == PlayerId ? _character() : null;
        DaggerfallMagicTargetProfile profile = DaggerfallMagicProfiles.Create(victim.Stats, victim.Definition,
            character, _catalog, _magicDefense(target));
        int save = DaggerfallMagicAdmissionPolicy.SavingThrow(DaggerfallMagicResistanceElement.Magic,
            DaggerfallMagicEffectFlags.Magic, profile, 0,
            () => Draw(new(attacker, target, generation, step, 1d), attacker, target,
                CombatRandomKey.RazorSavingThrowSalt, 1, 100, enemy));
        int added = save == 0 ? 0 : checked((int)Math.Ceiling(health.Current - health.Minimum));
        facts.Append(new ArtifactTerminalStrikeFact(source, attacker, target, added, generation, step));
        if (added > 0) { chargedWeapon = weapon; chargedUnits = added; }
        return checked(damage + added);
    }

    // FormulaHelper's caller tests the player's two ring slots once. Current equipment is the
    // source of truth, so unequip, destruction, break and restore need no second active-state store.
    private void ReflectNamira(CombatParticipants incoming, long attacker, long target, ApplyHitEvent applied,
        ulong generation, ulong step, FactBuffer<IProductFact> facts)
    {
        if (target != PlayerId || attacker == PlayerId || applied.Damage <= 0 || _itemCondition is null
            || !_definitions.TryGetValue(attacker, out var enemy)) return;
        int reflected = NamiraReflection(_actorTeam(attacker), applied.Damage);
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
            facts.Append(new ActorDamagedFact(attacker, target, DaggerfallDamageCause.Effect, result.CalculatedDamage, result.ActualHealthLost) { TargetDefeated = result.Defeated });
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
