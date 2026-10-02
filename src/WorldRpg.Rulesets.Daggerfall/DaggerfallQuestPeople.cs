using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestPersonHome(DaggerfallQuestResourceBinding Binding, DaggerfallQuestResourceTextContext Text);

/// <summary>Selected Person meaning before its world projection is admitted.</summary>
internal sealed record DaggerfallQuestPersonSelection(int FactionId, string Race, string Gender,
    int HudFace, int? SourceFace, ushort NameSeed, string DisplayName, bool Individual,
    int? VampireClanFactionId, long? QuestorId = null)
{
    public DaggerfallNpcAppearance? Appearance { get; init; }
    public DaggerfallQuestPersonHome? Home { get; init; }
}

/// <summary>Explicit current-world inputs; permanent player transformation is supplied by its racial owner.</summary>
internal sealed record DaggerfallQuestPersonContext(DaggerfallSiteRecord Site, DaggerfallNpc? Questor = null,
    int? PermanentPlayerClanFactionId = null, DaggerfallWorldProfileKey? CurrentProfile = null, DaggerfallInteriorBuilding? Interior = null);

/// <summary>Person source policy over the published faction and race catalogs.</summary>
internal sealed class DaggerfallQuestPersonAllocator(DaggerfallDefinitions definitions, IRandomService random,
    DaggerfallNames names, Func<DaggerfallQuestInstanceSave, DaggerfallQuestPersonContext> context,
    Action<long, string>? retainNpcName = null, DaggerfallQuestPlaceAllocator? places = null)
{
    internal DaggerfallQuestResourceState Allocate(DaggerfallQuestInstanceSave instance, DaggerfallQuestResourceDefinition declaration,
        IEnumerable<DaggerfallQuestResourceState>? parentResources = null, IEnumerable<DaggerfallQuestResourceState>? activeResources = null)
    {
        if (declaration.Kind != "person" || declaration.Person is not { } options)
            throw new ArgumentException("Person allocation requires a normalized Person declaration.");
        var current = context(instance);
        int region = current.Site.Region;
        string key = instance.InstanceId + "/" + declaration.CanonicalId;
        bool questor = options.Named is null && options.Group?.Equals("Questor", StringComparison.OrdinalIgnoreCase) == true;
        bool individual = options.Named is not null;
        DaggerfallNpc? existing = questor ? current.Questor
            ?? throw new NotSupportedException($"Quest Person '{declaration.CanonicalId}' requires an explicitly bound quest giver.") : null;
        int factionId = existing?.Appearance.FactionId ?? ResolveFaction(instance, options, region, key);
        DaggerfallFactionDefinition faction = RequireFaction(factionId);
        if (individual && faction.Type is not (0 or 4))
            throw new ArgumentException($"Named Person '{options.Named}' faction {factionId} is not an individual or Daedra.");
        if (!definitions.BuildingNames.TryGetNameBank(region, out int bank))
            throw new NotSupportedException($"Quest Person region {region} has no published name bank.");
        int raceId = faction.Race is >= 0 and <= 7 ? faction.Race + 1 : bank + 1;
        string race = existing?.Appearance.Race ?? definitions.Catalogs.Races.Single(value => value.DonorRaceId == raceId).Id;
        string gender = existing?.Appearance.Gender ?? (options.Gender?.ToLowerInvariant() switch
        {
            "male" => "Male", "female" => "Female", null => Draw(key + "/gender", 0, 1) == 0 ? "Male" : "Female",
            _ => throw new ArgumentException($"Quest Person '{declaration.CanonicalId}' has an invalid gender."),
        });
        if (faction.Type == 8 || faction.Id == 512) gender = "Female";
        ushort seed = existing?.Appearance.NameSeed ?? checked((ushort)Draw(key + "/name-seed", 0, ushort.MaxValue));
        string name = existing?.DisplayName ?? (faction.Type is 0 or 4 ? faction.Name : names.FullName(bank, gender == "Female",
            existing is null ? key + "/name/" + seed : "npc/" + existing.DurableId + "/name/" + seed));
        if (existing is not null && existing.DisplayName is null) retainNpcName?.Invoke(existing.DurableId, name);
        int face = Draw(key + "/hud-face", 0, 9); // Person.AssignHUDFace ignores the source face option.
        int? clan = faction.Type == 6 ? RegionClan(region).Id : null;
        DaggerfallFactionFlatDefinition? flat = faction.FlatVisuals.Count > (gender == "Female" ? 1 : 0)
            ? faction.FlatVisuals[gender == "Female" ? 1 : 0] : null;
        DaggerfallNpcAppearance? appearance = existing?.Appearance
            ?? (flat is null ? null : new(race, gender, flat.Archive, flat.Record, seed, factionId));
        var home = places?.AllocatePersonHome(instance.InstanceId, declaration, individual, questor,
            current.CurrentProfile ?? throw new NotSupportedException("Person home allocation requires the actual current profile."), current.Interior,
            parentResources ?? [], activeResources ?? []);
        var selection = new DaggerfallQuestPersonSelection(factionId, race, gender, face, options.Face, seed, name, individual, clan, existing?.DurableId)
            { Appearance = appearance, Home = home is null ? null : new(home.Binding, home.Text!) };
        return new(declaration.CanonicalId, existing is null ? DaggerfallQuestResourceBinding.Pending() : DaggerfallQuestResourceBinding.Actors(existing.DurableId))
        {
            SelectedPerson = selection,
            Text = new(Name: name, NameTwo: home?.Text?.Name, NameThree: home?.Text?.NameThree, NameFour: home?.Text?.NameFour,
                Details: FlatDetails(flat, race),
                Faction: questor && instance.FactionId != 0 ? RequireFaction(instance.FactionId).Name : faction.Name,
                NpcVampireClan: clan is int id ? ClanName(id) : null),
        };
    }

    private string FlatDetails(DaggerfallFactionFlatDefinition? flat, string race)
    {
        if (flat is { } visual)
        {
            if (definitions.Factions.NpcCaptions.Count == 0)
                throw new NotSupportedException("Person details require the published NPC flat-caption catalog.");
            if (definitions.Factions.NpcCaptions.TryGetValue((visual.Archive, visual.Record), out var caption))
                return caption;
        }
        string key = race switch { "dark-elf" => "darkElf", "high-elf" => "highElf", "wood-elf" => "woodElf", _ => race };
        return definitions.Text.RequireInternalEntry(key, 0);
    }

    private string ClanName(int factionId)
    {
        // FormulaHelper.GetVampireClan maps the province's 150..158 identity to these localized race keys.
        string[] keys = ["vraseth", "haarvenu", "thrafey", "lyrezi", "montalion", "khulari", "garlythi", "anthotis", "selenu"];
        if (factionId is < 150 or > 158) return RequireFaction(factionId).Name;
        return definitions.Text.RequireInternalEntry(keys[factionId - 150], 0);
    }

    private int ResolveFaction(DaggerfallQuestInstanceSave instance, DaggerfallQuestPersonOptions options, int region, string key)
    {
        var table = definitions.QuestSources.Tables.ActorItemTables.Factions;
        if (options.Named is { } named) return table.Resolve(named).P3;
        if (options.Group is { } group)
        {
            var row = table.Resolve(group);
            int career = row.P2 ?? throw new NotSupportedException($"Career group '{group}' has unresolved source P2 '{row.P2Text}'.");
            if (career < 0) career = row.P3;
            if (career == 10000) career = 0;
            return career switch
            {
                0 or 1 or 2 or 3 or 5 or 6 or 7 or 8 or 9 or 10 or 12 or 13 or 15 => 510,
                11 => DaggerfallConcreteGuildCatalog.MagesFactionId,
                14 => 450, 16 => 242,
                _ => RegionFaction(region, 15, social: 0, guild: 4).Id,
            };
        }
        if (options.FactionType is { } typeName)
        {
            int type = table.Resolve(typeName).P3;
            // Preserve the donor's actual numeric random-type branch (0..3), not its unused explanatory array.
            if (type == -1) type = Draw(key + "/random-type", 0, 3);
            return type switch
            {
                6 => instance.DefinitionName.StartsWith("P0", StringComparison.OrdinalIgnoreCase)
                    || instance.DefinitionName.StartsWith("$CUREVAM", StringComparison.OrdinalIgnoreCase)
                    ? context(instance).PermanentPlayerClanFactionId
                        ?? throw new NotSupportedException("This Person requires the permanent player vampire clan from the racial owner.")
                    : RegionClan(region).Id,
                7 => RegionFaction(region, 7).Id,
                10 => Choose(DaggerfallConcreteGuildCatalog.All.Where(value => value.Kind == DaggerfallConcreteGuildKind.KnightlyOrder)
                    .Select(value => value.FactionId).Order().ToArray(), key + "/order"),
                11 => DaggerfallConcreteGuildCatalog.MagesFactionId,
                12 => Draw(key + "/generic", 0, 1) == 0 ? 450 : 844,
                13 => DaggerfallConcreteGuildCatalog.ThievesFactionId,
                14 => RegionFaction(region, 14, guild: 15).Id,
                15 => RegionFaction(region, 15, social: 0, guild: 4).Id,
                >= 0 and <= 9 => Choose(definitions.Factions.Factions.Values.Where(value => value.Type == type
                    && (type != 9 || value.Id != 450)).Select(value => value.Id).ToArray(), key + "/faction-type"),
                _ => throw new NotSupportedException($"Person faction type '{typeName}' has unsupported source type {type}."),
            };
        }
        if (options.Faction is { } alliance) return table.Resolve(alliance).P3;
        throw new ArgumentException("A Person requires a named individual, career group, faction type or alliance.");
    }

    internal DaggerfallFactionDefinition RegionClan(int region)
    {
        var clan = RequireFaction(RegionFaction(region, 7).Vampire);
        if (clan.Type != 6) throw new NotSupportedException($"Region {region} province names non-clan faction {clan.Id}.");
        return clan;
    }
    private DaggerfallFactionDefinition RegionFaction(int region, int type, int? social = null, int? guild = null)
    {
        var matches = definitions.Factions.Factions.Values.Where(value => value.Region == region && value.Type == type
            && (social is null || value.SocialGroup == social) && (guild is null || value.GuildGroup == guild)).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new NotSupportedException($"Region {region} requires exactly one type {type} faction with social {social} and guild {guild}; found {matches.Length}.");
    }
    private DaggerfallFactionDefinition RequireFaction(int id) => definitions.Factions.Factions.TryGetValue(id, out var faction)
        ? faction : throw new NotSupportedException($"Quest Person faction {id} is not published.");
    private int Choose(IReadOnlyList<int> pool, string key) => pool.Count > 0 ? pool[Draw(key, 0, pool.Count - 1)]
        : throw new NotSupportedException($"Quest Person selection '{key}' has no eligible published factions.");
    private int Draw(string key, int low, int high) => checked((int)random.DrawKeyed(new(0, "daggerfall.quest.person", key, low, high)).Value);
}
