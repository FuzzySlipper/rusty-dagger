using System.Text.Json;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Admitted source placement and presentation, before a session allocates its durable person.</summary>
internal sealed record DaggerfallStaticNpcPlacement(string Id, WorldPoint Position,
    DaggerfallNpcAppearance Appearance, string Role, IReadOnlyList<string> Services, NormalizedActorSprite Sprite)
{
    private const int MerchantSocialGroup = 1;

    internal static IReadOnlyList<DaggerfallStaticNpcPlacement> Read(ReadOnlyMemory<byte>? bytes,
        IReadOnlyDictionary<(int Archive, int Record), NormalizedBillboardSprite> sprites, DaggerfallDefinitions definitions,
        DaggerfallSiteId? site, DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null) return [];
        using JsonDocument document = JsonDocument.Parse(bytes.Value);
        JsonElement world = document.RootElement.GetProperty("world");
        if (!world.TryGetProperty("staticNpcs", out JsonElement people)) return [];
        List<DaggerfallStaticNpcPlacement> result = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (JsonElement person in people.EnumerateArray())
        {
            string id = person.GetProperty("id").GetString()!;
            int archive = person.GetProperty("billboardArchive").GetInt32();
            int record = person.GetProperty("billboardRecord").GetInt32();
            string resource = $"sprite/texture-{archive}-{record}";
            if (!DaggerfallBaseContent.ValidId(id.Replace('/', '-')) || !ids.Add(id))
                diagnostics.Add($"Normalized static person '{id}' has an invalid or repeated identity.");
            if (!sprites.TryGetValue((archive, record), out NormalizedBillboardSprite? billboard))
            {
                diagnostics.Add($"Normalized static person '{id}' lacks admitted billboard '{resource}'.");
                continue;
            }
            if (site is null || !world.TryGetProperty("interiorBuilding", out JsonElement building)
                || building.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add($"Normalized static person '{id}' lacks its source interior/site.");
                continue;
            }
            string? race = person.GetProperty("race").GetString();
            if (race is null && definitions.BuildingNames.TryGetNameBank(site.Value.Region, out int bank))
                race = bank == 0 ? "breton" : "redguard";
            string gender = person.GetProperty("gender").GetString()!;
            int faction = person.GetProperty("factionId").GetInt32();
            if ((race is null || !definitions.Catalogs.TryGetRace(race, out _)) || gender is not ("Male" or "Female")
                || faction < 0 || faction != 0 && !definitions.Factions.Factions.ContainsKey(faction))
                diagnostics.Add($"Normalized static person '{id}' names an unavailable race, gender or faction.");
            JsonElement point = person.GetProperty("position");
            WorldPoint position = new(point.GetProperty("x").GetSingle(), point.GetProperty("y").GetSingle(), point.GetProperty("z").GetSingle());
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                diagnostics.Add($"Normalized static person '{id}' has a non-finite pose.");
            int type = building.GetProperty("buildingType").GetInt32();
            int buildingFaction = building.GetProperty("factionId").GetInt32();
            List<string> services = ["talk"];
            string role = "person";
            // Only a published source placement enters this policy. Arbitrary registry entries never
            // gain services from their faction. Shared guild definitions retain provider/rank authority.
            if (type == 3 && definitions.Factions.Factions.TryGetValue(faction, out DaggerfallFactionDefinition? sourceFaction)
                && sourceFaction.SocialGroup == MerchantSocialGroup)
            {
                services.Add("banking");
                role = "bank teller";
            }
            if (type is 11 or 14)
            {
                foreach (DaggerfallConcreteGuildDefinition guild in DaggerfallConcreteGuildCatalog.All
                    .Where(guild => guild.FactionId == buildingFaction || guild.ParentFactionId == buildingFaction))
                foreach (DaggerfallConcreteGuildServiceDefinition service in guild.Services
                    .Where(service => service.ProviderFactionId == faction && service.SourceImplemented))
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
            result.Add(new(id, position, new(race ?? string.Empty, gender, archive, record,
                person.GetProperty("nameSeed").GetInt32(), faction), role,
                services.Distinct(StringComparer.Ordinal).ToArray(), new(billboard.TexturePath, billboard.TextureSha256,
                    billboard.AtlasWidth, billboard.AtlasHeight, billboard.Frames, billboard.InitialFrameId,
                    new System.Numerics.Vector2(.5F, 0F), billboard.Size)));
        }
        return result;
    }
}
