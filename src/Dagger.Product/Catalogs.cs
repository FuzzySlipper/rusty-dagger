namespace RustyDagger.Product;

public sealed record StatBlock(int Strength, int Agility, int Luck, int LongBlade, int HandToHand, int Dodging);
public sealed record ActorDefinition(string Id, StatBlock Stats, int ArmorValue, int MaximumHealth, int MaximumStamina, int MaximumMagicka, int ExperienceReward, EnemyAttackDefinition? Attack = null, LootDefinition? Loot = null);
public sealed record WeaponDefinition(string Id, int MinimumDamage, int MaximumDamage, string Skill);
public sealed record EncounterDefinition(string Id, string Name, string Objective, long MemberEntityId);
public sealed record EnemyAttackDefinition(string Id, int MinimumDamage, int MaximumDamage, float Reach, float CooldownSeconds);
public sealed record LootDefinition(string TableKey, string ItemId, int MinimumQuantity, int MaximumQuantity);

/// <summary>Direct C# catalog slice adapted from gameplay/src/catalogs.</summary>
public static class DaggerCatalogs
{
    public static readonly WeaponDefinition IronLongsword = new("iron-longsword", 2, 16, "long-blade");
    public static readonly ActorDefinition Player = new("player", new StatBlock(50, 50, 50, 60, 40, 0), 0, 85, 90, 50, 0);
    public static readonly ActorDefinition Rat = new("rat", new StatBlock(40, 80, 50, 0, 35, 0), 30, 16, 0, 0, 50, new("rat-bite", 1, 4, 1.25f, 1.5f));
    public static readonly ActorDefinition SkeletalWarrior = new("skeletal-warrior", new StatBlock(50, 80, 50, 75, 75, 75), 10, 66, 0, 0, 450, new("skeleton-strike", 5, 15, 1.5f, 2f), new("H", "gold-piece", 2, 10));

    public static readonly IReadOnlyDictionary<string, EncounterDefinition> Encounters =
        new Dictionary<string, EncounterDefinition>(StringComparer.Ordinal)
        {
            ["rat-introduction"] = new("rat-introduction", "Rat Cellar", "Defeat the rat and inspect its classic corpse marker.", 2007),
            ["skeletal-guardroom"] = new("skeletal-guardroom", "Skeletal Guardroom", "Defeat the tougher Skeletal Warrior and survive its slower, heavier attacks.", 2000),
        };

    public static ActorDefinition? ForAuthoredName(string name) => name.StartsWith("enemy-rat", StringComparison.Ordinal)
        ? Rat
        : name.StartsWith("enemy-skeletalwarrior", StringComparison.Ordinal)
            ? SkeletalWarrior
            : null;
}
