using System.Text.Json;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Admitted source placement and presentation, before a session allocates its durable person.</summary>
internal sealed record DaggerfallStaticNpcPlacement(string Id, WorldPoint Position,
    DaggerfallNpcAppearance Appearance, string Role, IReadOnlyList<string> Services, NormalizedActorSprite Sprite,
    ActorInteractionPolicyKind Interaction = ActorInteractionPolicyKind.FullDialogue,
    ActorHarmPolicyKind Harm = ActorHarmPolicyKind.Protected,
    string? SpawnerId = null,
    bool DeriveRegionalFaction = false,
    string? FamilyId = null)
{
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
            if (site is null)
            {
                diagnostics.Add($"Normalized static person '{id}' lacks its source site.");
                continue;
            }
            string? race = person.TryGetProperty("race", out JsonElement raceEl) && raceEl.ValueKind == JsonValueKind.String
                ? raceEl.GetString()
                : null;
            if (race is null && definitions.BuildingNames.TryGetNameBank(site.Value.Region, out int bank))
                race = bank == 0 ? "breton" : "redguard";
            string gender = person.GetProperty("gender").GetString()!;
            int faction = person.GetProperty("factionId").GetInt32();
            bool deriveRegionalFaction = person.TryGetProperty("deriveRegionalFaction", out JsonElement deriveEl)
                && deriveEl.ValueKind == JsonValueKind.True;
            if (deriveRegionalFaction && faction == 0)
                faction = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, site.Value.Region)?.Id ?? 0;
            if ((race is null || !definitions.Catalogs.TryGetRace(race, out _)) || gender is not ("Male" or "Female")
                || faction < 0)
                diagnostics.Add($"Normalized static person '{id}' names an unavailable race, gender or faction.");
            JsonElement point = person.GetProperty("position");
            WorldPoint position = new(point.GetProperty("x").GetSingle(), point.GetProperty("y").GetSingle(), point.GetProperty("z").GetSingle());
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                diagnostics.Add($"Normalized static person '{id}' has a non-finite pose.");
            string? interactionRaw = person.TryGetProperty("interaction", out JsonElement intEl) && intEl.ValueKind == JsonValueKind.String
                ? intEl.GetString()
                : null;
            string? harmRaw = person.TryGetProperty("harm", out JsonElement harmEl) && harmEl.ValueKind == JsonValueKind.String
                ? harmEl.GetString()
                : null;
            string? spawnerId = person.TryGetProperty("spawnerId", out JsonElement spawnerEl) && spawnerEl.ValueKind == JsonValueKind.String
                ? spawnerEl.GetString()
                : null;
            string? familyId = person.TryGetProperty("familyId", out JsonElement familyEl) && familyEl.ValueKind == JsonValueKind.String
                ? familyEl.GetString()
                : null;
            ActorInteractionPolicyKind interaction = DaggerfallPeopleSlots.ParseInteractionPolicy(interactionRaw);
            ActorHarmPolicyKind harm = DaggerfallPeopleSlots.ParseHarmPolicy(harmRaw,
                fallback: interaction == ActorInteractionPolicyKind.None
                    ? ActorHarmPolicyKind.LawProtectedCivilian
                    : ActorHarmPolicyKind.Protected);
            bool interior = world.TryGetProperty("interiorBuilding", out JsonElement building) && building.ValueKind == JsonValueKind.Object;
            int type = interior ? building.GetProperty("buildingType").GetInt32() : -1;
            int buildingFaction = interior ? building.GetProperty("factionId").GetInt32() : 0;
            DaggerfallFactionDefinition? sourceFaction = definitions.Factions.Factions.GetValueOrDefault(faction);
            string role;
            IReadOnlyList<string> services;
            if (interaction == ActorInteractionPolicyKind.None)
            {
                role = spawnerId ?? "town-clutter-person";
                services = [];
            }
            else if (interaction == ActorInteractionPolicyKind.AmbientBark)
            {
                role = spawnerId ?? "bystander";
                services = ["bark"];
            }
            else
            {
                (role, services) = DaggerfallNpcServiceFacts.Resolve(
                    definitions, sourceFaction, type, buildingFaction, "person");
            }
            result.Add(new(id, position, new(race ?? string.Empty, gender, archive, record,
                person.GetProperty("nameSeed").GetInt32(), faction), role,
                services, billboard.Standing((archive, record)), interaction, harm, spawnerId, deriveRegionalFaction, familyId));
        }
        return result;
    }
}
