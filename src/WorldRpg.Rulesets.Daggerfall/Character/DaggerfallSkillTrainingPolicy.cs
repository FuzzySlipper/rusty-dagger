namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>One classic guild or temple trainer and the skills it offers.</summary>
internal sealed record DaggerfallSkillTrainingProviderPolicy(
    int NpcServiceFactionId,
    int MembershipFactionId,
    bool RequiresMembership,
    IReadOnlyList<string> Skills,
    int MaximumPermanentSkill);

/// <summary>Daggerfall's source-authored guild and temple training rules.</summary>
/// <remarks>
/// Service faction IDs and skill lists follow the donor's GuildNpcServices, guild TrainingSkills,
/// and Temple.GetTrainingSkills definitions. Membership is checked against the owning guild or
/// temple membership variant, rather than the trainer's separate service faction.
/// </remarks>
internal static class DaggerfallSkillTrainingPolicy
{
    internal const string ServiceName = "training";
    internal const int MaximumPermanentSkill = 50;
    internal const int MemberCostPerLevel = 100;
    internal const int NonMemberCostPerLevel = 400;
    internal const long TrainingDurationSeconds = 3L * 60 * 60;
    internal const long TrainingCooldownSeconds = 720L * 60;

    private static readonly IReadOnlyDictionary<int, DaggerfallSkillTrainingProviderPolicy> Providers =
        new Dictionary<int, DaggerfallSkillTrainingProviderPolicy>
        {
            [61] = Provider(61, 40, true,
                "alteration", "daedric", "destruction", "dragonish", "harpy", "illusion", "impish", "mysticism", "orcish", "restoration", "spriggan", "thaumaturgy"),
            [849] = Provider(849, 41, true,
                "archery", "axe", "blunt-weapon", "critical-strike", "giantish", "jumping", "long-blade", "orcish", "running", "short-blade", "swimming"),
            [803] = Provider(803, 42, true,
                "backstabbing", "blunt-weapon", "climbing", "dodging", "jumping", "lockpicking", "pickpocket", "short-blade", "stealth", "streetwise", "swimming"),
            [839] = Provider(839, 108, true,
                "archery", "backstabbing", "climbing", "critical-strike", "daedric", "destruction", "dodging", "running", "short-blade", "stealth", "streetwise", "swimming"),

            // Temples admit training without membership; their member price follows the
            // concrete group-17 temple membership rather than its deity parent.
            [247] = Provider(247, Guilds.DaggerfallConcreteGuildCatalog.AkatoshTempleFactionId, false,
                "alteration", "archery", "daedric", "destruction", "dragonish", "long-blade", "running", "stealth", "swimming"),
            [241] = Provider(241, Guilds.DaggerfallConcreteGuildCatalog.ArkayTempleFactionId, false,
                "axe", "backstabbing", "climbing", "critical-strike", "daedric", "destruction", "medical", "restoration", "short-blade"),
            [250] = Provider(250, Guilds.DaggerfallConcreteGuildCatalog.DibellaTempleFactionId, false,
                "daedric", "etiquette", "harpy", "illusion", "lockpicking", "long-blade", "nymph", "orcish", "restoration", "streetwise"),
            [249] = Provider(249, Guilds.DaggerfallConcreteGuildCatalog.JulianosTempleFactionId, false,
                "alteration", "critical-strike", "daedric", "impish", "lockpicking", "mercantile", "mysticism", "short-blade", "thaumaturgy"),
            [254] = Provider(254, Guilds.DaggerfallConcreteGuildCatalog.KynarethTempleFactionId, false,
                "archery", "climbing", "daedric", "destruction", "dodging", "dragonish", "harpy", "illusion", "jumping", "running", "stealth"),
            [245] = Provider(245, Guilds.DaggerfallConcreteGuildCatalog.MaraTempleFactionId, false,
                "archery", "critical-strike", "daedric", "etiquette", "harpy", "illusion", "medical", "nymph", "restoration", "streetwise"),
            [252] = Provider(252, Guilds.DaggerfallConcreteGuildCatalog.StendarrTempleFactionId, false,
                "axe", "blunt-weapon", "critical-strike", "daedric", "dodging", "medical", "orcish", "restoration", "spriggan"),
            [243] = Provider(243, Guilds.DaggerfallConcreteGuildCatalog.ZenitharTempleFactionId, false,
                "blunt-weapon", "centaurian", "daedric", "etiquette", "giantish", "harpy", "mercantile", "orcish", "pickpocket", "spriggan", "streetwise", "thaumaturgy"),
        };

    internal static IReadOnlyCollection<DaggerfallSkillTrainingProviderPolicy> AllProviders => Providers.Values.ToArray();

    internal static bool TryGetProvider(int npcServiceFactionId, out DaggerfallSkillTrainingProviderPolicy provider) =>
        Providers.TryGetValue(npcServiceFactionId, out provider!);

    internal static ulong Price(int playerLevel, bool member)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(playerLevel, 1);
        return checked((ulong)playerLevel * (ulong)(member ? MemberCostPerLevel : NonMemberCostPerLevel));
    }

    private static DaggerfallSkillTrainingProviderPolicy Provider(int serviceFactionId, int membershipFactionId, bool requiresMembership,
        params string[] skills) =>
        new(serviceFactionId, membershipFactionId, requiresMembership, Array.AsReadOnly(skills), MaximumPermanentSkill);
}
