using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Daggerfall-owned mechanics identities. Their string forms are only used at the Engine service edge.</summary>
internal readonly record struct DaggerfallStatId(string Value);
internal readonly record struct DaggerfallTrackId(string Value);

internal static class DaggerfallMechanicsIds
{
    internal static readonly DaggerfallStatId Strength = new("strength");
    internal static readonly DaggerfallStatId Agility = new("agility");
    internal static readonly DaggerfallStatId Intelligence = new("intelligence");
    internal static readonly DaggerfallStatId Endurance = new("endurance");
    internal static readonly DaggerfallStatId Luck = new("luck");
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

internal sealed record DaggerfallStatBases(int Strength, int Agility, int Intelligence, int Endurance, int Luck, int LongBlade, int HandToHand, int Dodging);

/// <summary>Product policy for turning authored Daggerfall bases into track maxima and initial values.</summary>
internal sealed record DaggerfallVitalValues(int HealthMaximum, int StaminaMaximum, int MagickaMaximum)
{
    internal static DaggerfallVitalValues Player(DaggerfallStatBases stats) => new(25 + ((stats.Endurance * 3) / 2), stats.Strength + stats.Endurance, stats.Intelligence);
    internal static DaggerfallVitalValues Fixed(int health, int stamina = 0, int magicka = 0) => new(health, stamina, magicka);
}

internal sealed record DaggerfallCombatProfile(DaggerfallStatId AttackSkill, DaggerfallStatId Strength, DaggerfallStatId Agility, DaggerfallStatId Luck, DaggerfallStatId Dodging, DaggerfallTrackId Health, DaggerfallTrackId? AttackCost, int ArmorValue, EnemyAttackDefinition? Attack = null)
{
    internal CombatantProfile ToCombatantProfile() => new(AttackSkill.Value, Strength.Value, Agility.Value, Luck.Value, Dodging.Value, Health.Value, AttackCost?.Value, ArmorValue, Attack);
}

internal sealed record DaggerfallRewardPolicy(int ExperienceReward, LootDefinition? Loot = null);
internal sealed record DaggerfallActorDefinition(string Id, DaggerfallStatBases Stats, DaggerfallVitalValues Vitals, DaggerfallCombatProfile Combat, DaggerfallRewardPolicy Rewards);
internal sealed record LootDefinition(string TableKey, string ItemId, int MinimumQuantity, int MaximumQuantity);
internal sealed record DaggerfallHudResourceDefinition(string Id, string Label, DaggerfallTrackId Track);

/// <summary>Small Daggerfall catalog for the active slice; broader content assembly remains the next campaign task.</summary>
internal sealed class DaggerfallCatalog
{
    internal DaggerfallCatalog()
    {
        DaggerfallStatBases playerStats = new(50, 50, 50, 40, 50, 60, 40, 0);
        Player = new("player", playerStats, DaggerfallVitalValues.Player(playerStats), new(DaggerfallMechanicsIds.LongBlade, DaggerfallMechanicsIds.Strength, DaggerfallMechanicsIds.Agility, DaggerfallMechanicsIds.Luck, DaggerfallMechanicsIds.Dodging, DaggerfallMechanicsIds.Health, DaggerfallMechanicsIds.Stamina, 0), new(0));
        Rat = new("rat", new(40, 80, 0, 0, 50, 0, 35, 0), DaggerfallVitalValues.Fixed(16), new(DaggerfallMechanicsIds.HandToHand, DaggerfallMechanicsIds.Strength, DaggerfallMechanicsIds.Agility, DaggerfallMechanicsIds.Luck, DaggerfallMechanicsIds.Dodging, DaggerfallMechanicsIds.Health, null, 30, new("rat_bite", 1, 4, 1.25f, 1.5f)), new(50));
        SkeletalWarrior = new("skeletal-warrior", new(50, 80, 0, 0, 50, 75, 75, 75), DaggerfallVitalValues.Fixed(66), new(DaggerfallMechanicsIds.HandToHand, DaggerfallMechanicsIds.Strength, DaggerfallMechanicsIds.Agility, DaggerfallMechanicsIds.Luck, DaggerfallMechanicsIds.Dodging, DaggerfallMechanicsIds.Health, null, 10, new("skeleton_strike", 5, 15, 1.5f, 2f)), new(450, new("H", "gold-piece", 2, 10)));
        IronLongsword = new("iron-longsword", 2, 16, DaggerfallMechanicsIds.LongBlade.Value);
        Encounters = [new("rat-introduction", "Rat Cellar", "Defeat the rat and inspect its classic corpse marker.", 2007, 12f), new("skeletal-guardroom", "Skeletal Guardroom", "Defeat the tougher Skeletal Warrior and survive its slower, heavier attacks.", 2000, 12f)];
        HudResources = [new("health", "Health", DaggerfallMechanicsIds.Health), new("stamina", "Stamina", DaggerfallMechanicsIds.Stamina), new("magicka", "Magicka", DaggerfallMechanicsIds.Magicka)];
    }

    internal DaggerfallActorDefinition Player { get; }
    internal DaggerfallActorDefinition Rat { get; }
    internal DaggerfallActorDefinition SkeletalWarrior { get; }
    internal WeaponDefinition IronLongsword { get; }
    internal IReadOnlyList<EncounterTarget> Encounters { get; }
    internal IReadOnlyList<DaggerfallHudResourceDefinition> HudResources { get; }
    internal DaggerfallActorDefinition? ForAuthoredName(string name) => name.StartsWith("enemy-rat", StringComparison.Ordinal) ? Rat : name.StartsWith("enemy-skeletalwarrior", StringComparison.Ordinal) ? SkeletalWarrior : null;
}
