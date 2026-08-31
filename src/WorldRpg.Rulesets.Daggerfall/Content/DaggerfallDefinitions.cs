using System.Collections.ObjectModel;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Daggerfall-owned identities. Their string forms cross into Mechanics only at the named Engine edge.</summary>
internal readonly record struct DaggerfallActorId(string Value);
internal readonly record struct DaggerfallItemId(string Value);
internal readonly record struct DaggerfallEquipmentSlotId(string Value);
internal readonly record struct DaggerfallStatId(string Value);
internal readonly record struct DaggerfallTrackId(string Value);
internal readonly record struct DaggerfallActionId(string Value);

internal static class DaggerfallActorIdentity
{
    // The compiled session reserves this identity for the authored player.
    internal const long PlayerEntityId = 1;
}

internal static class DaggerfallMechanicsIds
{
    internal static readonly DaggerfallStatId Strength = new("strength");
    internal static readonly DaggerfallStatId Intelligence = new("intelligence");
    internal static readonly DaggerfallStatId Agility = new("agility");
    internal static readonly DaggerfallStatId Endurance = new("endurance");
    internal static readonly DaggerfallStatId Luck = new("luck");
    internal static readonly DaggerfallStatId Dodging = new("dodging");
    internal static readonly DaggerfallStatId LongBlade = new("long-blade");
    internal static readonly DaggerfallStatId HandToHand = new("hand-to-hand");
    internal static readonly DaggerfallStatId HealthMaximum = new("health-maximum");
    internal static readonly DaggerfallStatId StaminaMaximum = new("stamina-maximum");
    internal static readonly DaggerfallStatId MagickaMaximum = new("magicka-maximum");
    internal static readonly DaggerfallTrackId Health = new("health");
    internal static readonly DaggerfallTrackId Stamina = new("stamina");
    internal static readonly DaggerfallTrackId Magicka = new("magicka");
    internal static readonly DaggerfallTrackId PhysicalDamage = new("physical");
}

internal sealed class DaggerfallStatBases(IReadOnlyDictionary<DaggerfallStatId, int> values)
{
    internal IReadOnlyDictionary<DaggerfallStatId, int> Values { get; } = new ReadOnlyDictionary<DaggerfallStatId, int>(values.ToDictionary());
    internal int this[DaggerfallStatId id] => Values.TryGetValue(id, out int value) ? value : 0;
    internal int Strength => this[DaggerfallMechanicsIds.Strength];
    internal int Intelligence => this[DaggerfallMechanicsIds.Intelligence];
    internal int Endurance => this[DaggerfallMechanicsIds.Endurance];
}

/// <summary>Product policy for turning authored Daggerfall bases into track maxima and initial values.</summary>
internal sealed record DaggerfallVitalValues(int HealthMaximum, int StaminaMaximum, int MagickaMaximum)
{
    internal static DaggerfallVitalValues Player(DaggerfallStatBases stats) => new(25 + ((stats.Endurance * 3) / 2), stats.Strength + stats.Endurance, stats.Intelligence);
}

internal sealed record DaggerfallVitalRange(int Minimum, int Maximum);
internal sealed record DaggerfallCombatProfile(DaggerfallTrackId Health, DaggerfallTrackId? AttackCost);
internal sealed record DaggerfallAttackRange(int MinimumDamage, int MaximumDamage);
internal sealed record DaggerfallAttackDefinition(string Skill, int MinimumDamage, int MaximumDamage, double CooldownSeconds, string? Material = null, int DamageBonus = 0);
internal sealed record DaggerfallRewardPolicy(int ExperienceReward);
internal sealed record DaggerfallLoadoutEntry(DaggerfallItemId ItemId, ulong Quantity, ulong? UniqueEntityId, DaggerfallEquipmentSlotId? EquipSlot);
internal sealed record DaggerfallActorDefinition(DaggerfallActorId Id, string Kind, DaggerfallStatBases Stats, DaggerfallVitalRange Health, DaggerfallCombatProfile Combat, DaggerfallRewardPolicy Rewards, int Armor, int? MobileId, int? HitPointsPerLevel, IReadOnlyList<DaggerfallAttackRange> Attacks, string? Team, string? MinimumMaterial, string? LootTableKey, int? Level, int? Weight, string? ActionId, IReadOnlyList<DaggerfallLoadoutEntry> Loadout, DaggerfallActorPresentationDefinition Presentation)
{
    internal DaggerfallVitalValues PlayerInitialVitals => DaggerfallVitalValues.Player(Stats);
}

/// <summary>Authored Daggerfall presentation policy layered over normalized imported actor media.</summary>
internal sealed record DaggerfallActorPresentationDefinition(string? PreferredRestState, IReadOnlyDictionary<string, float> EffectiveFramesPerSecond)
{
    internal static DaggerfallActorPresentationDefinition None { get; } = new(null, new ReadOnlyDictionary<string, float>(new Dictionary<string, float>()));
}

internal sealed record DaggerfallWeaponDefinition(int MinimumDamage, int MaximumDamage, string Material, string Skill, string Handedness, int Value, int Weight);
internal sealed record DaggerfallArmorDefinition(string Material, string Part);
internal sealed record DaggerfallShieldDefinition(int Armor);
internal enum DaggerfallItemKind { Fungible, Unique }
internal sealed record DaggerfallEquipmentDefinition(IReadOnlyList<string> Classifications, ushort RequiredSlots, string? ExclusiveGroup);
internal sealed record DaggerfallEquipmentSlotDefinition(DaggerfallEquipmentSlotId Id, IReadOnlyList<string> AllowedClassifications);
internal sealed record DaggerfallItemDefinition(DaggerfallItemId Id, DaggerfallItemKind Kind, ulong MaximumQuantity, int Weight, int Value, DaggerfallWeaponDefinition? Weapon = null, DaggerfallArmorDefinition? Armor = null, DaggerfallShieldDefinition? Shield = null, DaggerfallEquipmentDefinition? Equipment = null)
{
    internal bool IsFungible => Kind == DaggerfallItemKind.Fungible;
}
internal sealed record DaggerfallHudResourceDefinition(string Id, string Label, DaggerfallTrackId Track);
/// <summary>Action-owned damage is allowed only for a fixed action without a donor actor range (the thief).</summary>
internal sealed record DaggerfallActionDefinition(string Id, IReadOnlyList<string> Tags, string Interpretation, string Skill, int? AttackRangeIndex, int? MinimumDamage, int? MaximumDamage, int? StaminaCost, double? Reach, double? CooldownSeconds, int DamageBonus = 0);
internal sealed record DaggerfallLootTableDefinition(string Key, int MinimumGold, int MaximumGold, IReadOnlyDictionary<string, int> Categories);
/// <summary>Catalog metadata deliberately retained as data until a later ruleset-owned loot slice interprets it.</summary>
internal sealed record DaggerfallDeferredLootCategoryPool(string Id, string Status, string Reason);
/// <summary>Named, reviewed deviations from the donor corpus; these are documentation data, not behavior switches.</summary>
internal sealed record DaggerfallDonorErratum(string Id);
internal sealed record DaggerfallVocabulary(IReadOnlyList<DaggerfallStatId> Attributes, IReadOnlyList<DaggerfallStatId> Skills, IReadOnlyList<DaggerfallTrackId> Tracks, IReadOnlyList<string> ArmorParts, IReadOnlyList<DaggerfallStatId> Progression)
{
    internal IReadOnlyList<DaggerfallStatId> ActorStats { get; } = Array.AsReadOnly(Attributes.Concat(Skills).ToArray());
}

/// <summary>Immutable typed definitions loaded from the ordered daggerfall.base payload.</summary>
internal sealed class DaggerfallDefinitions(DaggerfallVocabulary vocabulary, IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors, IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items, IReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> equipmentSlots, IReadOnlyDictionary<string, int> armorValuesByMaterial, IReadOnlyDictionary<string, DaggerfallActionDefinition> actions, IReadOnlyDictionary<string, DaggerfallLootTableDefinition> lootTables, IReadOnlyList<DaggerfallHudResourceDefinition> hudResources, IReadOnlyList<DaggerfallDeferredLootCategoryPool> lootCategoryPools, IReadOnlyList<DaggerfallDonorErratum> donorErrata)
{
    internal DaggerfallVocabulary Vocabulary { get; } = vocabulary;
    internal IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> Actors { get; } = new ReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition>(actors.ToDictionary());
    internal IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> Items { get; } = new ReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition>(items.ToDictionary());
    internal IReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> EquipmentSlots { get; } = new ReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition>(equipmentSlots.ToDictionary());
    internal IReadOnlyDictionary<string, int> ArmorValuesByMaterial { get; } = new ReadOnlyDictionary<string, int>(armorValuesByMaterial.ToDictionary());
    internal IReadOnlyDictionary<string, DaggerfallActionDefinition> Actions { get; } = new ReadOnlyDictionary<string, DaggerfallActionDefinition>(actions.ToDictionary());
    internal IReadOnlyDictionary<string, DaggerfallLootTableDefinition> LootTables { get; } = new ReadOnlyDictionary<string, DaggerfallLootTableDefinition>(lootTables.ToDictionary());
    internal IReadOnlyList<DaggerfallHudResourceDefinition> HudResources { get; } = Array.AsReadOnly(hudResources.ToArray());
    internal IReadOnlyList<DaggerfallDeferredLootCategoryPool> LootCategoryPools { get; } = Array.AsReadOnly(lootCategoryPools.ToArray());
    internal IReadOnlyList<DaggerfallDonorErratum> DonorErrata { get; } = Array.AsReadOnly(donorErrata.ToArray());
    internal DaggerfallActorDefinition RequireActor(DaggerfallActorId id) => Actors.TryGetValue(id, out DaggerfallActorDefinition? actor) ? actor : throw new InvalidOperationException($"Daggerfall definitions do not contain actor '{id.Value}'.");
}
