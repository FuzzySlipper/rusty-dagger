using WorldRpg.Rulesets.Daggerfall.Guilds;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Resolves ordinary NPC roles and services from the source faction and building facts carried by
/// the importer. The registry and the static and population admission paths share this policy, so a
/// source person does not become a provider merely because a caller registered a matching faction.
/// </summary>
internal static class DaggerfallNpcServiceFacts
{
    private const int MerchantSocialGroup = 1;
    private const int KnightlyGuardFactionType = 10;

    internal static (string Role, IReadOnlyList<string> Services) Resolve(
        DaggerfallDefinitions definitions,
        DaggerfallFactionDefinition? sourceFaction,
        int sourceBuildingType,
        int sourceBuildingFaction,
        string ordinaryRole)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(ordinaryRole);
        List<string> services = ["talk"];
        string role = sourceFaction?.Type == KnightlyGuardFactionType ||
            StringComparer.Ordinal.Equals(sourceFaction?.TypeName, "KnightlyGuard") ? "guard" : ordinaryRole;

        // The donor's bank and guild provider facts are a relationship between the actual source
        // person faction and the source building slot. A population fallback has no building slot,
        // so it remains an ordinary civilian until an importer record supplies those facts.
        if (sourceBuildingType == 3 && sourceFaction is not null && sourceFaction.SocialGroup == MerchantSocialGroup)
        {
            services.Add("banking");
            role = "bank teller";
        }

        // RMB shop types are source building facts, while the person faction's social group
        // identifies the actual merchant who owns the services. Keep this relationship at content
        // admission so a caller cannot turn an ordinary civilian into a shop provider by faction
        // alone. The repair and identify actions then reuse the Economy provider contract.
        if (sourceFaction is not null && sourceFaction.SocialGroup == MerchantSocialGroup && IsShop(sourceBuildingType))
        {
            services.Add("shop");
            services.Add("merchant");
            services.Add("buy-items");
            services.Add("sell-items");
            if (IsGenericRepairShop(sourceBuildingType))
                services.Add("repair");
            role = "merchant";
        }

        if (sourceBuildingType is 11 or 14)
        {
            foreach (DaggerfallConcreteGuildDefinition guild in DaggerfallConcreteGuildCatalog.All
                .Where(guild => guild.FactionId == sourceBuildingFaction || guild.ParentFactionId == sourceBuildingFaction))
            foreach (DaggerfallConcreteGuildServiceDefinition service in guild.Services
                .Where(service => service.ProviderFactionId == sourceFaction?.Id && service.SourceImplemented))
            {
                string serviceName = DaggerfallConcreteGuildServiceRuntime.ProviderServiceName(service.Service);
                services.Add(serviceName);
                role = service.Service switch
                {
                    DaggerfallConcreteGuildService.BuySpells => "spell seller",
                    DaggerfallConcreteGuildService.MakeSpells => "spellmaker",
                    DaggerfallConcreteGuildService.Training => "trainer",
                    DaggerfallConcreteGuildService.Identify => "identifier",
                    DaggerfallConcreteGuildService.Repair => "repairer",
                    DaggerfallConcreteGuildService.Donate => "priest",
                    DaggerfallConcreteGuildService.CureDisease => "healer",
                    DaggerfallConcreteGuildService.ReceiveArmor => "armorer",
                    DaggerfallConcreteGuildService.ReceiveHouse => "property steward",
                    _ => role,
                };
            }
        }

        return (role, services.Distinct(StringComparer.Ordinal).ToArray());
    }

    // Arena2's filed values are the donor BuildingTypes enum with Alchemist at zero:
    // Alchemist, Armorer, Bookseller, ClothingStore, FurnitureStore, GemStore, GeneralStore,
    // PawnShop, and WeaponSmith are the merchant shops. This is source-format policy, so it stays
    // in the Daggerfall content resolver rather than the shared Kit or Economy runtime.
    internal static bool IsGenericRepairShop(int sourceBuildingType) => sourceBuildingType is 2 or 9 or 13;

    internal static bool IsShop(int sourceBuildingType) => sourceBuildingType is 0 or 2 or 5 or 6 or 7 or 8 or 9 or 12 or 13;
}
