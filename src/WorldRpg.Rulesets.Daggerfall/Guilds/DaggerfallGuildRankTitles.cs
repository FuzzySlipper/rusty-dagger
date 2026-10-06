namespace WorldRpg.Rulesets.Daggerfall.Guilds;

/// <summary>
/// The rank titles a guild member is addressed by, as the donor's guild classes name them
/// (<c>Guild.RankTitles</c> and each guild's <c>GetTitle</c>), including the female titles the donor
/// substitutes for "Brother" and "Patriarch".
/// </summary>
internal static class DaggerfallGuildRankTitles
{
    private static readonly string[] Fighters = ["Apprentice", "Journeyman", "Swordsman", "Protector", "Defender", "Warder", "Guardian", "Champion", "Warrior", "Master"];
    private static readonly string[] Mages = ["Apprentice", "Journeyman", "Evoker", "Conjurer", "Magician", "Enchanter", "Warlock", "Wizard", "Master Wizard", "Archmage"];
    private static readonly string[] Thieves = ["Apprentice", "Journeyman", "Filcher", "Crook", "Robber", "Bandit", "Thief", "Ringleader", "Mastermind", "Master Thief"];
    private static readonly string[] DarkBrotherhood = ["Apprentice", "Journeyman", "Operator", "Slayer", "Executioner", "Punisher", "Terminator", "Assassin", "Dark Brother", "Master Assassin"];
    private static readonly string[] Temple = ["Novice", "Initiate", "Acolyte", "Adept", "Curate", "Disciple", "Brother", "Diviner", "Master", "Patriarch"];
    private static readonly string[] KnightlyOrder = ["Aspirant", "Squire", "Gallant", "Chevalier", "Keeper", "Knight Brother", "Commander", "Marshall", "Seneschal", "Paladin"];

    /// <summary>The title of a rank in a concrete guild, or null when the faction is not one.</summary>
    internal static string? Title(int factionId, int rank, bool female)
    {
        if (!DaggerfallConcreteGuildCatalog.TryGet(factionId, out DaggerfallConcreteGuildDefinition guild)) return null;
        if (rank is < 0 or > DaggerfallSocialState.MaximumGuildRank) return null;
        return (guild.Kind, rank, female) switch
        {
            (DaggerfallConcreteGuildKind.DarkBrotherhood, 8, true) => "Dark Sister",
            (DaggerfallConcreteGuildKind.KnightlyOrder, 5, true) => "Knight Sister",
            (DaggerfallConcreteGuildKind.Temple, 6, true) => "Sister",
            (DaggerfallConcreteGuildKind.Temple, 9, true) => "Matriarch",
            _ => guild.Kind switch
            {
                DaggerfallConcreteGuildKind.Fighters => Fighters[rank],
                DaggerfallConcreteGuildKind.Mages => Mages[rank],
                DaggerfallConcreteGuildKind.Thieves => Thieves[rank],
                DaggerfallConcreteGuildKind.DarkBrotherhood => DarkBrotherhood[rank],
                DaggerfallConcreteGuildKind.Temple => Temple[rank],
                DaggerfallConcreteGuildKind.KnightlyOrder => KnightlyOrder[rank],
                _ => null,
            },
        };
    }
}
