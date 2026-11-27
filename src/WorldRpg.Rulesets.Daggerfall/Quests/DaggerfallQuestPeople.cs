using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestPersonHome(DaggerfallQuestResourceBinding Binding, DaggerfallQuestResourceTextContext Text);

/// <summary>Selected Person meaning before its world projection is admitted.</summary>
internal sealed record DaggerfallQuestPersonSelection(int FactionId, string Race, string Gender,
    int HudFace, int? SourceFace, int NameSeed, string DisplayName, bool Individual,
    int? VampireClanFactionId, long? QuestorId = null)
{
    public DaggerfallNpcAppearance? Appearance { get; init; }
    public DaggerfallQuestPersonHome? Home { get; init; }
    public string? CastId { get; init; }
    public int? OfficeFactionId { get; init; }
    public long? OfficeActorId { get; init; }
}

/// <summary>Explicit current-world inputs; permanent player transformation is supplied by its racial owner.</summary>
internal sealed record DaggerfallQuestPersonContext(DaggerfallSiteRecord Site, DaggerfallNpc? Questor = null,
    int? PermanentPlayerClanFactionId = null, DaggerfallWorldProfileKey? CurrentProfile = null, DaggerfallInteriorBuilding? Interior = null);

/// <summary>Person source policy over the published faction, unique cast, office holder, and race catalogs.</summary>
internal sealed class DaggerfallQuestPersonAllocator(DaggerfallDefinitions definitions, IRandomService random,
    DaggerfallNames names, Func<DaggerfallQuestInstanceSave, DaggerfallQuestPersonContext> context,
    Action<long, string>? retainNpcName = null, DaggerfallQuestPlaceAllocator? places = null,
    DaggerfallOfficeHolders? officeHolders = null)
{
    private readonly DaggerfallOfficeHolders _officeHolders = officeHolders ?? new DaggerfallOfficeHolders(definitions);

    internal DaggerfallQuestResourceState Allocate(DaggerfallQuestInstanceSave instance, DaggerfallQuestResourceDefinition declaration,
        IEnumerable<DaggerfallQuestResourceState>? parentResources = null, IEnumerable<DaggerfallQuestResourceState>? activeResources = null)
    {
        if (declaration.Kind != "person" || declaration.Person is not { } options)
            throw new ArgumentException("Person allocation requires a normalized Person declaration.");
        var current = context(instance);
        int region = current.Site.Region;
        string key = instance.InstanceId + "/" + declaration.CanonicalId;
        bool questor = options.Named is null && options.Office is null
            && options.Group?.Equals("Questor", StringComparison.OrdinalIgnoreCase) == true;
        bool officeAddressed = options.Office is not null;
        bool individual = options.Named is not null;
        DaggerfallNpc? existing = questor ? current.Questor
            ?? throw new NotSupportedException($"Quest Person '{declaration.CanonicalId}' requires an explicitly bound quest giver.") : null;

        int? officeFactionId = null;
        DaggerfallOfficeHolder? officeHolder = null;
        if (officeAddressed)
        {
            if (options.Office!.Equals("ExiledProvince", StringComparison.OrdinalIgnoreCase))
            {
                if (!_officeHolders.TryResolveExiledRegionalRuler(region, out officeHolder) || officeHolder is null)
                    throw new NotSupportedException($"Quest Person '{declaration.CanonicalId}' requires an exiled provincial ruler in region {region}.");
                officeFactionId = officeHolder.OfficeFactionId;
            }
            else
            {
                officeFactionId = ResolveOfficeFaction(options.Office!, region);
                officeHolder = _officeHolders.ResolveOffice(officeFactionId.Value, region);
                if (officeHolder is not { HasLivingHolder: true })
                    throw new NotSupportedException($"Quest Person '{declaration.CanonicalId}' office faction {officeFactionId.Value} has no living office holder.");
            }
        }

        int factionId = existing?.Appearance.FactionId
            ?? officeHolder?.HolderFactionId
            ?? officeFactionId
            ?? ResolveFaction(instance, options, region, key);
        DaggerfallFactionDefinition faction = RequireFaction(factionId);
        if (individual && faction.Type is not (0 or 4))
            throw new ArgumentException($"Named Person '{options.Named}' faction {factionId} is not an individual or Daedra.");

        // Resolve canonical unique cast individual (for named characters in cast/individuals.json or cast office holders).
        DaggerfallCastIndividualDefinition? cast = null;
        DaggerfallCastHomeLocation? castHome = null;
        if (officeHolder is { CastId: { Length: > 0 } officeCastId }
            && definitions.TryGetCastOrNoble(officeCastId, out DaggerfallCastIndividualDefinition? resolvedOfficeCast)
            && resolvedOfficeCast is not null)
        {
            cast = resolvedOfficeCast;
            castHome = officeHolder.Home;
            if (castHome is null && places is not null)
                DaggerfallCastHomes.TryResolve(places.Sites, cast, region, out castHome);
            individual = true;
        }
        else if (individual && _officeHolders.TryResolveCastIndividual(factionId, region, out DaggerfallCastIndividualDefinition? namedCast, out DaggerfallCastHomeLocation? namedHome))
        {
            cast = namedCast;
            castHome = namedHome;
            if (castHome is null && cast is not null && places is not null)
                DaggerfallCastHomes.TryResolve(places.Sites, cast, region, out castHome);
        }

        long? boundActorId = existing?.DurableId;
        long? officeActorId = null;
        DaggerfallNpc? existingWorldNpc = existing;
        if (boundActorId is null && cast is not null)
        {
            boundActorId = DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
            if (_officeHolders.TryGetNpc(boundActorId.Value, out DaggerfallNpc? liveCastNpc))
                existingWorldNpc = liveCastNpc;
        }
        else if (boundActorId is null && officeHolder is { ActorId: long holderActorId })
        {
            boundActorId = holderActorId;
            officeActorId = holderActorId;
            if (_officeHolders.TryGetNpc(holderActorId, out DaggerfallNpc? liveHolderNpc))
                existingWorldNpc = liveHolderNpc;
        }

        if (!definitions.BuildingNames.TryGetNameBank(region, out int bank))
            throw new NotSupportedException($"Quest Person region {region} has no published name bank.");
        int raceId = faction.Race is >= 0 and <= 7 ? faction.Race + 1 : bank + 1;
        string defaultRace = definitions.Catalogs.Races.Single(value => value.DonorRaceId == raceId).Id;
        string race = existingWorldNpc?.Appearance.Race
            ?? officeHolder?.Race
            ?? cast?.Race
            ?? defaultRace;
        if (!definitions.Catalogs.TryGetRace(race, out _))
            race = defaultRace;

        string gender = existingWorldNpc?.Appearance.Gender
            ?? officeHolder?.Gender
            ?? cast?.Gender
            ?? (options.Gender?.ToLowerInvariant() switch
            {
                "male" => "Male", "female" => "Female", null => Draw(key + "/gender", 0, 1) == 0 ? "Male" : "Female",
                _ => throw new ArgumentException($"Quest Person '{declaration.CanonicalId}' has an invalid gender."),
            });
        if (existingWorldNpc is null && cast is null && officeHolder?.Gender is null && (faction.Type == 8 || faction.Id == 512))
            gender = "Female";

        int seed = existingWorldNpc?.Appearance.NameSeed
            ?? (cast is not null || officeActorId is not null ? 0 : Draw(key + "/name-seed", 0, ushort.MaxValue));
        string name = existingWorldNpc?.DisplayName
            ?? (officeHolder is not null && !string.IsNullOrWhiteSpace(officeHolder.DisplayName) ? officeHolder.DisplayName : null)
            ?? cast?.Name
            ?? (faction.Type is 0 or 4 ? faction.Name : names.FullName(bank, gender == "Female",
                existingWorldNpc is null ? key + "/name/" + seed : "npc/" + existingWorldNpc.DurableId + "/name/" + seed));
        if (existing is not null && existing.DisplayName is null) retainNpcName?.Invoke(existing.DurableId, name);

        int face = Draw(key + "/hud-face", 0, 9); // Person.AssignHUDFace ignores the source face option.
        int? clan = faction.Type == 6 ? RegionClan(region).Id : null;
        DaggerfallFactionSpriteDefinition? sprite = faction.SpriteVisuals.Count > (gender == "Female" ? 1 : 0)
            ? faction.SpriteVisuals[gender == "Female" ? 1 : 0] : null;
        var visualSprite = individual ? faction.SpriteVisuals.FirstOrDefault() : sprite;
        DaggerfallNpcAppearance? appearance = existingWorldNpc?.Appearance
            ?? (officeHolder is { BillboardArchive: int oa, BillboardRecord: int or }
                ? new DaggerfallNpcAppearance(race, gender, oa, or, seed, factionId)
                : cast is not null
                    ? new DaggerfallNpcAppearance(race, gender, cast.Archive, cast.Record, seed, factionId)
                    : visualSprite is null ? null : new(race, gender, visualSprite.Archive, visualSprite.Record, seed, factionId));

        var home = places?.AllocatePersonHome(instance.InstanceId, declaration, individual, questor,
            current.CurrentProfile ?? throw new NotSupportedException("Person home allocation requires the actual current profile."), current.Interior,
            parentResources ?? [], activeResources ?? [], castHome);

        var selection = new DaggerfallQuestPersonSelection(factionId, race, gender, face, options.Face, seed, name, individual, clan, existing?.DurableId)
        {
            Appearance = appearance,
            Home = home is null ? null : new(home.Binding, home.Text!),
            CastId = cast?.Id,
            OfficeFactionId = officeFactionId,
            OfficeActorId = officeActorId,
        };
        DaggerfallQuestResourceBinding binding = boundActorId is long actorId
            ? DaggerfallQuestResourceBinding.Actors(actorId)
            : DaggerfallQuestResourceBinding.Pending();
        string? factionName = questor && instance.FactionId != 0
            ? RequireFaction(instance.FactionId).Name
            : officeFactionId is int offId && definitions.Factions.Factions.TryGetValue(offId, out var offFaction)
                ? offFaction.Name
                : faction.Name;
        return new(declaration.CanonicalId, binding)
        {
            SelectedPerson = selection,
            Text = new(Name: name, NameTwo: home?.Text?.Name ?? home?.Text?.NameTwo, NameThree: home?.Text?.NameThree, NameFour: home?.Text?.NameFour,
                Details: SpriteDetails(cast is not null || officeHolder is not null ? appearance : null, sprite, race),
                Faction: factionName,
                NpcVampireClan: clan is int id ? ClanName(id) : null),
        };
    }

    private int ResolveOfficeFaction(string officeName, int region)
    {
        if (officeName.Equals("Province", StringComparison.OrdinalIgnoreCase))
            return RegionFaction(region, 7).Id;
        if (int.TryParse(officeName, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int numeric)
            && definitions.Factions.Factions.ContainsKey(numeric))
            return numeric;
        var table = definitions.QuestSources.Tables.ActorItemTables.Factions;
        if (table.Rows.FirstOrDefault(row => row.Name.Equals(officeName, StringComparison.OrdinalIgnoreCase)) is { } row
            && definitions.Factions.Factions.ContainsKey(row.P3))
            return row.P3;
        string normalized = officeName.Replace('_', ' ');
        if (definitions.Factions.Factions.Values.FirstOrDefault(f => f.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase)) is { } byName)
            return byName.Id;
        throw new NotSupportedException($"Quest Person office '{officeName}' does not resolve to a published faction.");
    }

    private string SpriteDetails(DaggerfallNpcAppearance? appearance, DaggerfallFactionSpriteDefinition? sprite, string race)
    {
        if (appearance is { } look && definitions.Factions.NpcCaptions.Count > 0
            && definitions.Factions.NpcCaptions.TryGetValue((look.BillboardArchive, look.BillboardRecord), out var lookCaption))
            return lookCaption;
        if (sprite is { } visual)
        {
            if (definitions.Factions.NpcCaptions.Count == 0)
                throw new NotSupportedException("Person details require the published NPC caption catalog.");
            if (definitions.Factions.NpcCaptions.TryGetValue((visual.Archive, visual.Record), out var caption))
                return caption;
        }
        string key = race switch { "dark-elf" => "darkElf", "high-elf" => "highElf", "wood-elf" => "woodElf", _ => race };
        return definitions.Text.RequireInternalEntry(key, 0);
    }

    private string ClanName(int factionId) => DaggerfallVampirismPolicy.ClanName(definitions, factionId);

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

    internal DaggerfallFactionDefinition RegionClan(int region) => DaggerfallVampirismPolicy.GetVampireClan(definitions.Factions, region);
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
