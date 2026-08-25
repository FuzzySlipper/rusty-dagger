namespace RustyDagger.Product;

/// <summary>Small C# catalog slice adapted from gameplay/src/catalogs at fbec614.</summary>
public static class Catalogs
{
    public static readonly WeaponDefinition IronLongsword = new("iron-longsword", 2, 16, "long-blade");

    public static readonly ActorDefinition Player = new(
        "player", 0, 50, 50, 50, 60, 40, 0, 85, 85, 2, 16, 0, 2.25f, .75f);

    public static readonly ActorDefinition Rat = new(
        "rat", 30, 40, 80, 50, 0, 35, 0, 9, 16, 1, 4, 6f, 1.25f, 1.5f);

    public static readonly ActorDefinition SkeletalWarrior = new(
        "skeletal-warrior", 10, 50, 80, 50, 75, 75, 75, 17, 66, 5, 15, 8f, 1.5f, 2f);

    public static readonly IReadOnlyDictionary<string, ActorDefinition> Actors =
        new Dictionary<string, ActorDefinition>(StringComparer.Ordinal)
        {
            [Player.Id] = Player,
            [Rat.Id] = Rat,
            [SkeletalWarrior.Id] = SkeletalWarrior,
        };

    public static readonly IReadOnlyDictionary<string, EncounterDefinition> Encounters =
        new Dictionary<string, EncounterDefinition>(StringComparer.Ordinal)
        {
            ["rat-introduction"] = new("rat-introduction", "Rat Cellar", "Defeat the rat and inspect its classic corpse marker.", 2007),
            ["skeletal-guardroom"] = new("skeletal-guardroom", "Skeletal Guardroom", "Defeat the tougher Skeletal Warrior and survive its slower, heavier attacks.", 2000),
        };
}

public sealed record EncounterDefinition(string Id, string Name, string Objective, long MemberEntityId);
