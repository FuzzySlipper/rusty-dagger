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

/// <summary>Published actor kinds. The pack admits player, monster and enemy-class only.</summary>
internal static class DaggerfallActorKinds
{
    internal const string Player = "player";
    internal const string Monster = "monster";
    internal const string EnemyClass = "enemy-class";
}

/// <summary>Skill keys the enemy language formulas can answer with, in pack vocabulary spelling.</summary>
internal static class DaggerfallSkills
{
    internal const string Etiquette = "etiquette";
    internal const string Streetwise = "streetwise";
    internal const string Orcish = "orcish";
    internal const string Harpy = "harpy";
    internal const string Giantish = "giantish";
    internal const string Dragonish = "dragonish";
    internal const string Nymph = "nymph";
    internal const string Daedric = "daedric";
    internal const string Spriggan = "spriggan";
    internal const string Centaurian = "centaurian";
    internal const string Impish = "impish";
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
internal sealed record DaggerfallAttackDefinition(string Skill, int MinimumDamage, int MaximumDamage, double CooldownSeconds, string? Material = null, int DamageBonus = 0, double? Reach = null);
internal sealed record DaggerfallRewardPolicy(int ExperienceReward);
internal sealed record DaggerfallLoadoutEntry(DaggerfallItemId ItemId, ulong Quantity, ulong? UniqueEntityId, DaggerfallEquipmentSlotId? EquipSlot);
/// <summary>
/// The published locations, their records, and how many dungeons and gaps they carry.
/// </summary>
/// <param name="Keys">Every (region, index) the section carries, which is what a dungeon must name.</param>
/// <param name="Records">Every location the section publishes, in the order it publishes them.</param>
/// <param name="Dungeons">How many dungeons it publishes.</param>
/// <param name="RegionGaps">How many regions it records as having no usable tables.</param>
/// <param name="Regions">How many regions it records table provenance for, which is every region group.</param>
internal sealed record DaggerfallLocationSet(
    IReadOnlyCollection<(int Region, int Index)> Keys,
    IReadOnlyList<DaggerfallSiteRecord> Records,
    int Dungeons,
    int RegionGaps,
    int Regions);

internal sealed record DaggerfallActorDefinition(DaggerfallActorId Id, string Kind, DaggerfallStatBases Stats, DaggerfallVitalRange Health, DaggerfallCombatProfile Combat, DaggerfallRewardPolicy Rewards, int Armor, int? MobileId, int? HitPointsPerLevel, IReadOnlyList<DaggerfallAttackRange> Attacks, string? Team, string? MinimumMaterial, string? LootTableKey, int? Level, int? Weight, string? ActionId, IReadOnlyList<DaggerfallLoadoutEntry> Loadout, DaggerfallActorPresentationDefinition Presentation, bool GroundOnSpawn = false, string? Race = null, string? Career = null)
{
    internal DaggerfallVitalValues PlayerInitialVitals => DaggerfallVitalValues.Player(Stats);
}

/// <summary>
/// Classic enemy grouping by career identity, as the donor's pacify/charm eligibility reads it.
/// This is the donor <c>DFCareer.EnemyGroups</c> shape in product vocabulary; the pack's finer
/// <c>Team</c> affinity is separate and stays where it is.
/// </summary>
internal enum DaggerfallEnemyGroup
{
    None,
    Animals,
    Humanoid,
    Undead,
    Daedra,
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
internal sealed class DaggerfallDefinitions(DaggerfallCatalogSet catalogs, DaggerfallVocabulary vocabulary, IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors, IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items, IReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> equipmentSlots, IReadOnlyDictionary<string, int> armorValuesByMaterial, IReadOnlyDictionary<string, DaggerfallActionDefinition> actions, IReadOnlyDictionary<string, DaggerfallLootTableDefinition> lootTables, IReadOnlyList<DaggerfallHudResourceDefinition> hudResources, IReadOnlyList<DaggerfallDeferredLootCategoryPool> lootCategoryPools, IReadOnlyList<DaggerfallDonorErratum> donorErrata, DaggerfallItemTemplateLedger itemTemplates,
    DaggerfallCharacterPresentationSet characterPresentation,
    DaggerfallLocationSet locations,
    DaggerfallTextSet text,
    DaggerfallMagicCatalogSet magic,
    DaggerfallMobileCatalogSet mobiles,
    DaggerfallNameTablesSet names,
    DaggerfallRumorCatalogSet rumors,
    DaggerfallBiographiesSet biographies,
    DaggerfallWorldGridsSet grids,
    DaggerfallBooksSet books,
    DaggerfallFactionsSet factions,
    DaggerfallTerrainSet terrain)
{
    /// <summary>The normalized reference catalogs a consumer resolves keys through.</summary>
    internal DaggerfallCatalogSet Catalogs { get; } = catalogs;

    /// <summary>
    /// The published spells and magic-item templates, loaded from the pack alone: a spell resolves by key
    /// to its source identity and effects, and an item enchantment resolves to the spell it names.
    /// </summary>
    internal DaggerfallMagicCatalogSet Magic { get; } = magic;

    /// <summary>
    /// The published donor mobile parameters, loaded from the pack alone: each record resolves to the
    /// actor this product places and to the behaviour, damage, health and media the donor states for it.
    /// </summary>
    internal DaggerfallMobileCatalogSet Mobiles { get; } = mobiles;

    /// <summary>
    /// The published name tables, loaded from the pack alone: each bank resolves to its donor
    /// identity, its composition, and the text keys its fragments read through.
    /// </summary>
    internal DaggerfallNameTablesSet Names { get; } = names;

    /// <summary>
    /// The published rumor catalog, loaded from the pack alone: each record resolves to the
    /// region, type, faction and quest references its consumers match on, and the text key it reads.
    /// </summary>
    internal DaggerfallRumorCatalogSet Rumors { get; } = rumors;

    /// <summary>
    /// The published biographies, loaded from the pack alone: each questionnaire resolves to its
    /// questions, answers and effect references with the text keys and link states they carry.
    /// </summary>
    internal DaggerfallBiographiesSet Biographies { get; } = biographies;

    /// <summary>
    /// The published climate and politic grids, loaded from the pack alone: each stored cell resolves
    /// to the climate and the region or ocean the source states for it. Cells name stored columns
    /// directly; a consumer porting a donor map-file lookup adds one to the world-pixel X.
    /// </summary>
    internal DaggerfallWorldGridsSet Grids { get; } = grids;

    /// <summary>
    /// The published book catalog, loaded from the pack alone: a classic message resolves to the
    /// book it names with the text keys its pages read through.
    /// </summary>
    internal DaggerfallBooksSet Books { get; } = books;

    /// <summary>
    /// The published faction catalog, loaded from the pack alone: each faction resolves to its
    /// filed relations and bindings, and each politic region resolves to the factions that claim
    /// it or to the explicit unclaimed region.
    /// </summary>
    internal DaggerfallFactionsSet Factions { get; } = factions;

    /// <summary>
    /// The published wilderness terrain, loaded from the pack alone: each map pixel resolves to
    /// its height with the cell samples behind it.
    /// </summary>
    internal DaggerfallTerrainSet Terrain { get; } = terrain;

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

    /// <summary>
    /// The native item template ledger: which targets exist, what each rests on, and which
    /// are unresolved because the native source is not supplied.
    /// </summary>
    internal DaggerfallItemTemplateLedger ItemTemplates { get; } = itemTemplates;

    /// <summary>
    /// The published character presentation references a character sheet or social view resolves a
    /// race's background, bodies and heads through.
    /// </summary>
    internal DaggerfallCharacterPresentationSet CharacterPresentation { get; } = characterPresentation;

    /// <summary>
    /// The published locations the site consumers resolve their identity, name and kind through. The
    /// records are retained rather than only counted: a site lookup asks what a location is, and a
    /// section that kept nothing but its shape would leave every caller re-reading the payload.
    /// </summary>
    internal DaggerfallLocationSet Locations { get; } = locations;

    /// <summary>
    /// The published text a caller resolves a value through. A key the pack does not carry answers as a
    /// miss and a value it carries but could not read answers with the reason, so neither is confused
    /// with text that is legitimately empty.
    /// </summary>
    internal DaggerfallTextSet Text { get; } = text;
    internal DaggerfallActorDefinition RequireActor(DaggerfallActorId id) => Actors.TryGetValue(id, out DaggerfallActorDefinition? actor) ? actor : throw new InvalidOperationException($"Daggerfall definitions do not contain actor '{id.Value}'.");
}
