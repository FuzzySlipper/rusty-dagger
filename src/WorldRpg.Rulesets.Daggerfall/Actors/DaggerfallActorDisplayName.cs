using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Player-facing display name for any live or known actor: prefers a person's display name, then a catalogued mobile
/// or spawner title, then a readable role, so internal capability or definition slugs never reach player-facing text.
/// </summary>
internal static class DaggerfallActorDisplayName
{
    private const string GenericPerson = "Townsperson";
    private const string UnknownActor = "Someone";

    internal static string For(
        long actorId,
        DaggerfallNpcRegistry? npcs,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions,
        DaggerfallMobileCatalogSet mobiles,
        DaggerfallActorCatalog? actorCatalog = null)
    {
        bool hasDefinition = definitions.TryGetValue(actorId, out DaggerfallActorDefinition? definition);

        if (npcs is not null && npcs.TryGet(actorId, out DaggerfallNpc? npc))
        {
            if (CleanDisplayName(npc.DisplayName) is { } personName)
                return personName;

            if (SpecificRole(npc.Role) is { } specificRole)
                return specificRole;

            if (hasDefinition && SpawnerDisplayName(definition!, actorCatalog) is { } spawnerName)
                return spawnerName;

            if (ReadableRole(npc.Role) is { } roleName)
                return roleName;
        }

        if (hasDefinition)
            return ForDefinition(definition!, mobiles, actorCatalog);

        return UnknownActor;
    }

    internal static string ForDefinition(
        DaggerfallActorDefinition definition,
        DaggerfallMobileCatalogSet mobiles,
        DaggerfallActorCatalog? actorCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        DaggerfallMobileDefinition? mobile = definition.MobileId is int id && mobiles.Mobiles.TryGetValue(id, out DaggerfallMobileDefinition? byId)
            ? byId
            : mobiles.ForActor(definition.Id.Value);
        if (mobile is not null && !string.IsNullOrWhiteSpace(mobile.DonorName))
            return mobile.DonorName;

        if (SpawnerDisplayName(definition, actorCatalog) is { } specName)
            return specName;

        return SpecificRole(definition.SpawnerId)
            ?? SpecificRole(definition.Id.Value)
            ?? SpecificRole(definition.TemplateId)
            ?? ReadableRole(definition.SpawnerId)
            ?? ReadableRole(definition.Id.Value)
            ?? ReadableRole(definition.TemplateId)
            ?? ReadableRole(definition.Kind)
            ?? GenericPerson;
    }

    internal static string ForDefinitionId(
        string? definitionId,
        DaggerfallMobileCatalogSet mobiles,
        DaggerfallActorCatalog? actorCatalog = null)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return UnknownActor;
        if (mobiles.ForActor(definitionId) is { DonorName: { Length: > 0 } donor }) return donor;
        if (actorCatalog is not null && actorCatalog.Specs.TryGetValue(definitionId, out DaggerfallActorSpawnerSpec? spec)
            && CleanSpecDisplayName(spec.Id, spec.DisplayName) is { } specName)
            return specName;
        return ReadableRole(definitionId) ?? UnknownActor;
    }

    internal static string? ReadableRole(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string trimmed = raw.Trim();
        string normalized = NormalizeSlug(trimmed);
        return normalized switch
        {
            DaggerfallArchetypeIds.StaticNpc
                or DaggerfallArchetypeIds.Civilian
                or DaggerfallArchetypeIds.ClutterPerson
                or "town-clutter-person"
                or "clutter person"
                or "person"
                or "bystander"
                or DaggerfallArchetypeIds.Questor
                or DaggerfallActorKinds.EnemyClass => GenericPerson,
            DaggerfallArchetypeIds.Guard => "Guard",
            "city-watch" or "city watch" => "City Watch",
            "road-guard" or "road guard" or "hire-road-guard" => "Road Guard",
            "convoy-guard" or "convoy guard" => "Convoy Guard",
            "drover" => "Drover",
            "mercenary" or "hire-mercenary" => "Mercenary Scout",
            "knight-retainer" or "hire-knight-retainer" => "Knightly Retainer",
            "battle-mage" or "hire-battle-mage" => "Guild Battlemage",
            DaggerfallArchetypeIds.Combatant => "Fighter",
            _ => FormatFallbackLabel(trimmed),
        };
    }

    private static string? SpecificRole(string? raw)
    {
        string? role = ReadableRole(raw);
        return role is not null && !string.Equals(role, GenericPerson, StringComparison.Ordinal) ? role : null;
    }

    private static string? CleanDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string trimmed = name.Trim();
        return IsInternalSlug(trimmed) ? null : trimmed;
    }

    private static string? SpawnerDisplayName(DaggerfallActorDefinition definition, DaggerfallActorCatalog? actorCatalog)
    {
        if (actorCatalog is null) return null;
        string lookupId = definition.SpawnerId ?? definition.Id.Value;
        return actorCatalog.Specs.TryGetValue(lookupId, out DaggerfallActorSpawnerSpec? spec)
            ? CleanSpecDisplayName(spec.Id, spec.DisplayName)
            : null;
    }

    private static string? CleanSpecDisplayName(string specId, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        string normalizedSpec = NormalizeSlug(specId);
        if (normalizedSpec is "town-clutter-person" or DaggerfallArchetypeIds.ClutterPerson
            or DaggerfallArchetypeIds.StaticNpc or DaggerfallArchetypeIds.Civilian)
            return null;
        string trimmed = displayName.Trim();
        return IsInternalSlug(trimmed) || trimmed.Equals("Clutter Person", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    private static bool IsInternalSlug(string value)
    {
        string normalized = NormalizeSlug(value);
        return normalized is DaggerfallArchetypeIds.StaticNpc
                or DaggerfallArchetypeIds.Civilian
                or DaggerfallArchetypeIds.ClutterPerson
                or DaggerfallArchetypeIds.Questor
                or DaggerfallActorKinds.EnemyClass
                or "town-clutter-person"
                or "road-guard"
                or "convoy-guard"
                or "knight-retainer"
                or "battle-mage"
                or "city-watch"
            || normalized.StartsWith("hire-", StringComparison.Ordinal)
            || normalized.StartsWith("town-", StringComparison.Ordinal)
            || normalized.StartsWith("encounter-", StringComparison.Ordinal);
    }

    private static string FormatFallbackLabel(string raw)
    {
        string stripped = raw.Trim();
        foreach (string prefix in new[] { "encounter-", "town-", "hire-" })
        {
            if (stripped.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && stripped.Length > prefix.Length)
            {
                stripped = stripped[prefix.Length..];
                break;
            }
        }
        string cleaned = stripped.Replace('_', '-');
        return DaggerfallIdLabel.For(cleaned);
    }

    private static string NormalizeSlug(string value) => value.Trim().ToLowerInvariant().Replace('_', '-');
}
