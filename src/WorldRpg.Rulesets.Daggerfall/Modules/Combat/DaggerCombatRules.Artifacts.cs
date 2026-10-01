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
        var career = character?.Career ?? (victim.Definition.Career is string careerId ? _catalog.Catalogs.RequireCareer(careerId) : null);
        var mobile = _catalog.Mobiles.ForActor(victim.Definition.Id.Value);
        DaggerfallMagicTolerance Tolerance(int flag) => (career is not null
            ? DaggerfallCareerTolerances.Tolerance(career, flag)
            : DaggerfallCareerTolerances.Tolerance(mobile?.ResistanceFlags ?? 0, mobile?.ImmunityFlags ?? 0,
                mobile?.LowToleranceFlags ?? 0, mobile?.CriticalWeaknessFlags ?? 0, flag)) switch
        {
            DaggerfallDiseaseCareerTolerance.Immune => DaggerfallMagicTolerance.Immune,
            DaggerfallDiseaseCareerTolerance.Resistant => DaggerfallMagicTolerance.Resistant,
            DaggerfallDiseaseCareerTolerance.LowTolerance => DaggerfallMagicTolerance.LowTolerance,
            DaggerfallDiseaseCareerTolerance.CriticalWeakness => DaggerfallMagicTolerance.CriticalWeakness,
            _ => DaggerfallMagicTolerance.Normal,
        };
        var biography = character?.Background?.Modifiers;
        DaggerfallMagicTargetProfile profile = new(ReadStat(victim, DaggerfallMechanicsIds.Willpower),
            new(Tolerance(1), Tolerance(2), Tolerance(4), Tolerance(8), Tolerance(16), Tolerance(32), Tolerance(64)),
            character is not null ? _catalog.Catalogs.RequireRace(character.Identity.RaceId) : null,
            biography?.MagicResistance ?? 0, biography?.PoisonResistance ?? 0, biography?.DiseaseResistance ?? 0,
            new(0, 0, 0, 0, 0), []);
        // This donor direct-input overload deliberately passes modifier zero. The current compiled
        // effect families admit no resistance-spell channel; ordinary defensive stats are not that channel.
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
