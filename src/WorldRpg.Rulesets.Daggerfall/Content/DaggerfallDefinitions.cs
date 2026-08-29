using System.Collections.ObjectModel;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Daggerfall-owned identities. Their string forms are only used at the Engine service edge.</summary>
internal readonly record struct DaggerfallActorId(string Value);
internal readonly record struct DaggerfallItemId(string Value);
internal readonly record struct DaggerfallAppearanceId(string Value);
internal readonly record struct DaggerfallStatId(string Value);
internal readonly record struct DaggerfallTrackId(string Value);

internal static class DaggerfallMechanicsIds
{
    internal static readonly DaggerfallStatId Strength = new("strength");
    internal static readonly DaggerfallStatId Intelligence = new("intelligence");
    internal static readonly DaggerfallStatId Willpower = new("willpower");
    internal static readonly DaggerfallStatId Agility = new("agility");
    internal static readonly DaggerfallStatId Endurance = new("endurance");
    internal static readonly DaggerfallStatId Personality = new("personality");
    internal static readonly DaggerfallStatId Speed = new("speed");
    internal static readonly DaggerfallStatId Luck = new("luck");
    internal static readonly DaggerfallStatId Reflexes = new("reflexes");
    internal static readonly DaggerfallStatId LongBlade = new("long_blade");
    internal static readonly DaggerfallStatId HandToHand = new("hand_to_hand");
    internal static readonly DaggerfallStatId Dodging = new("dodging");
    internal static readonly DaggerfallStatId HealthMaximum = new("health_maximum");
    internal static readonly DaggerfallStatId StaminaMaximum = new("stamina_maximum");
    internal static readonly DaggerfallStatId MagickaMaximum = new("magicka_maximum");
    internal static readonly DaggerfallTrackId Health = new("health");
    internal static readonly DaggerfallTrackId Stamina = new("stamina");
    internal static readonly DaggerfallTrackId Magicka = new("magicka");
}

internal sealed record DaggerfallStatBases(int Strength, int Intelligence, int Willpower, int Agility, int Endurance, int Personality, int Speed, int Luck, int Reflexes, int LongBlade, int HandToHand, int Dodging);

/// <summary>Product policy for turning authored Daggerfall bases into track maxima and initial values.</summary>
internal sealed record DaggerfallVitalValues(int HealthMaximum, int StaminaMaximum, int MagickaMaximum)
{
    internal static DaggerfallVitalValues Player(DaggerfallStatBases stats) => new(25 + ((stats.Endurance * 3) / 2), stats.Strength + stats.Endurance, stats.Intelligence);
}

internal sealed record DaggerfallVitalRange(int Minimum, int Maximum);
internal sealed record DaggerfallCombatProfile(DaggerfallTrackId Health, DaggerfallTrackId? AttackCost);
internal sealed record DaggerfallAttackDefinition(int MinimumDamage, int MaximumDamage);
internal sealed record DaggerfallRewardPolicy(int ExperienceReward, LootDefinition? Loot = null);
internal sealed record LootDefinition(string TableKey, DaggerfallItemId ItemId, int MinimumQuantity, int MaximumQuantity);
internal sealed record DaggerfallActorDefinition(DaggerfallActorId Id, DaggerfallStatBases Stats, DaggerfallVitalRange Health, DaggerfallCombatProfile Combat, DaggerfallRewardPolicy Rewards, int Armor, int? MobileId, DaggerfallAttackDefinition? Attack)
{
    /// <summary>The current slice initializes bounded monster health at the upper value until #7324 owns faithful rolls.</summary>
    internal DaggerfallVitalValues InitialVitals => Id.Value == "player" ? DaggerfallVitalValues.Player(Stats) : new(Health.Maximum, 0, 0);
}

internal sealed record DaggerfallWeaponDefinition(int MinimumDamage, int MaximumDamage, string Skill, string Handedness, int Value, int Weight);
internal sealed record DaggerfallItemDefinition(DaggerfallItemId Id, ulong MaximumQuantity, DaggerfallWeaponDefinition? Weapon = null);
internal sealed record DaggerfallHudResourceDefinition(string Id, string Label, DaggerfallTrackId Track);

/// <summary>Immutable typed definitions loaded from the ordered daggerfall.base payload.</summary>
internal sealed class DaggerfallDefinitions(IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors, IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items, IReadOnlyList<DaggerfallHudResourceDefinition> hudResources)
{
    internal IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> Actors { get; } = new ReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition>(actors.ToDictionary());
    internal IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> Items { get; } = new ReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition>(items.ToDictionary());
    internal IReadOnlyList<DaggerfallHudResourceDefinition> HudResources { get; } = Array.AsReadOnly(hudResources.ToArray());
    internal DaggerfallActorDefinition RequireActor(DaggerfallActorId id) => Actors.TryGetValue(id, out DaggerfallActorDefinition? actor) ? actor : throw new InvalidOperationException($"Daggerfall definitions do not contain actor '{id.Value}'.");
}
