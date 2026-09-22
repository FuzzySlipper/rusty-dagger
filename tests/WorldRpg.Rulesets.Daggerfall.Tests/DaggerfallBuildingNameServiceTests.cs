using System.Reflection;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallBuildingNameServiceTests
{
    private static readonly Lazy<(DaggerfallDefinitions Definitions, DaggerfallBlocksSnapshot Blocks)> Inputs = new(ReadInputs);

    [Fact]
    public void Generates_every_donor_building_type_from_actual_rmb_fields()
    {
        (DaggerfallDefinitions definitions, DaggerfallBlocksSnapshot blocks) = Inputs.Value;
        DaggerfallSiteContext sites = new(definitions.Locations);
        DaggerfallSiteId ordinarySite = definitions.Locations.Records[0].Id;
        int[] generated = [0, 2, 3, 5, 6, 7, 8, 9, 10, 12, 13, 15, 23];
        foreach (int type in generated)
        {
            DaggerfallRmbBuildingSource building = Building(blocks, type);
            DaggerfallBuildingNameResult first = Service(definitions, blocks, sites).Resolve(ordinarySite, building.Id);
            DaggerfallBuildingNameResult second = Service(definitions, blocks, sites).Resolve(ordinarySite, building.Id);
            Assert.True(first.IsResolved, first.Unresolved);
            Assert.NotEmpty(first.Name);
            Assert.Equal(first, second);
        }

        DaggerfallRmbBuildingSource guild = Building(blocks, 11, building => definitions.Factions.Factions.ContainsKey(building.FactionId));
        DaggerfallRmbBuildingSource temple = Building(blocks, 14, building =>
            definitions.Factions.Factions.TryGetValue(building.FactionId, out DaggerfallFactionDefinition? faction)
            && faction.Children.Count > 0
            && definitions.Factions.Factions.ContainsKey(faction.Children[0]));
        DaggerfallBuildingNameResult guildName = Service(definitions, blocks, sites).Resolve(ordinarySite, guild.Id);
        DaggerfallBuildingNameResult templeName = Service(definitions, blocks, sites).Resolve(ordinarySite, temple.Id);
        Assert.True(guildName.IsResolved, guildName.Unresolved);
        Assert.True(templeName.IsResolved, templeName.Unresolved);
        Assert.Equal(guildName, Service(definitions, blocks, sites).Resolve(ordinarySite, guild.Id));
        Assert.Equal(templeName, Service(definitions, blocks, sites).Resolve(ordinarySite, temple.Id));

        DaggerfallSiteRecord daggerfall = Assert.Single(definitions.Locations.Records, site => site.Name == "Daggerfall");
        DaggerfallRmbBuildingSource palace = Building(blocks, 16);
        DaggerfallBuildingNameResult palaceName = Service(definitions, blocks, sites).Resolve(daggerfall.Id, palace.Id);
        Assert.Equal("Castle Daggerfall", palaceName.Name);
        Assert.Equal(palaceName, Service(definitions, blocks, sites).Resolve(daggerfall.Id, palace.Id));

        DaggerfallRmbBuildingSource unsupported = Building(blocks, 17);
        Assert.Equal(string.Empty, Service(definitions, blocks, sites).Resolve(ordinarySite, unsupported.Id).Name);
    }

    [Fact]
    public void Store_template_expands_the_internal_first_name_macro_with_the_donor_draw_sequence()
    {
        (DaggerfallDefinitions definitions, DaggerfallBlocksSnapshot blocks) = Inputs.Value;
        DaggerfallSiteContext sites = new(definitions.Locations);
        DaggerfallRmbBuildingId id = new("ALCHBM00.RMB", 0);
        (DaggerfallBuildingNameService service, RecordingLcg random) = ServiceWithRecordingRandom(definitions, blocks, sites);

        DaggerfallBuildingNameResult result = service.Resolve(definitions.Locations.Records[0].Id, id);

        Assert.Equal("Doctor Rochem's Herbs", result.Name);
        Assert.Equal(
            [(0u, 23u), (12345u, 19u), (3554416254u, 32768u), (2802067423u, 51u),
             (3596950572u, 5u), (229283573u, 26u), (3256818826u, 100u), (1051550459u, 22u)],
            random.Requests);
    }

    [Fact]
    public void Tavern_draws_b_then_a_and_fresh_owners_repeat_the_known_seed()
    {
        (DaggerfallDefinitions definitions, DaggerfallBlocksSnapshot blocks) = Inputs.Value;
        DaggerfallSiteContext sites = new(definitions.Locations);
        DaggerfallRmbBuildingSource tavern = blocks.RmbBuildings[new DaggerfallRmbBuildingId("ALCHAM00.RMB", 8)];
        DaggerfallSiteId site = definitions.Locations.Records[0].Id;

        (DaggerfallBuildingNameService first, RecordingLcg firstLcg) = ServiceWithRecordingRandom(definitions, blocks, sites);
        (_, RecordingLcg secondLcg) = ServiceWithRecordingRandom(definitions, blocks, sites);
        DaggerfallBuildingNameResult firstName = first.Resolve(site, tavern.Id);
        sites.AdmitBuildingNames(secondLcg.Service, definitions, blocks);
        DaggerfallBuildingNameResult secondName = sites.ResolveBuildingName(site, tavern.Id);

        Assert.Equal("The Dancing Chasm", firstName.Name);
        Assert.Equal(firstName, secondName);
        Assert.Equal([(0u, 36u), (12345u, 36u)], firstLcg.Requests);
        Assert.Equal(firstLcg.Requests, secondLcg.Requests);
    }

    [Fact]
    public void House_for_sale_reads_its_localized_value_without_a_random_draw()
    {
        (DaggerfallDefinitions definitions, DaggerfallBlocksSnapshot blocks) = Inputs.Value;
        DaggerfallSiteContext sites = new(definitions.Locations);
        DaggerfallRmbBuildingSource house = Building(blocks, 1);
        (DaggerfallBuildingNameService service, RecordingLcg random) = ServiceWithRecordingRandom(definitions, blocks, sites);

        DaggerfallBuildingNameResult result = service.Resolve(definitions.Locations.Records[0].Id, house.Id);

        Assert.Equal("House for sale", result.Name);
        Assert.Empty(random.Requests);
    }

    [Fact]
    public void Missing_localized_input_returns_a_precise_unresolved_result()
    {
        (DaggerfallDefinitions definitions, DaggerfallBlocksSnapshot blocks) = Inputs.Value;
        DaggerfallTextSet withoutStores = new(
            definitions.Text.Values.Where(pair => pair.Key != new DaggerfallTextKey(DaggerfallTextKind.Internal, "StoresA")).ToDictionary(),
            definitions.Text.PendingKinds,
            definitions.Text.Macros.Select(macro => macro.Symbol == "%ef" ? macro with { Records = macro.Records - 1 } : macro).ToArray());
        DaggerfallDefinitions altered = new(
            definitions.Catalogs, definitions.Vocabulary, definitions.Actors, definitions.Items, definitions.EquipmentSlots,
            definitions.ArmorValuesByMaterial, definitions.Actions, definitions.LootTables, definitions.HudResources,
            definitions.LootCategoryPools, definitions.DonorErrata, definitions.ItemTemplates, definitions.CharacterPresentation,
            definitions.Locations, withoutStores, definitions.Magic, definitions.Mobiles, definitions.Names, definitions.Rumors,
            definitions.Biographies, definitions.Grids, definitions.Books, definitions.Factions, definitions.Terrain,
            definitions.ItemTemplateCatalog, definitions.QuestSources, definitions.Cinematics)
        {
            BuildingNames = definitions.BuildingNames,
        };
        DaggerfallRmbBuildingSource armorer = Building(blocks, 2);

        DaggerfallBuildingNameResult result = Service(altered, blocks, new DaggerfallSiteContext(altered.Locations)).Resolve(altered.Locations.Records[0].Id, armorer.Id);

        Assert.False(result.IsResolved);
        Assert.Contains("Internal:StoresA", result.Unresolved, StringComparison.Ordinal);
    }

    private static DaggerfallRmbBuildingSource Building(DaggerfallBlocksSnapshot blocks, int type, Func<DaggerfallRmbBuildingSource, bool>? predicate = null) =>
        blocks.RmbBuildings.Values.First(building => building.BuildingType == type && (predicate?.Invoke(building) ?? true));

    private static DaggerfallBuildingNameService Service(DaggerfallDefinitions definitions, DaggerfallBlocksSnapshot blocks, DaggerfallSiteContext sites) =>
        new(RecordingLcg.Create().Service, definitions, blocks, sites);

    private static (DaggerfallBuildingNameService Service, RecordingLcg Random) ServiceWithRecordingRandom(DaggerfallDefinitions definitions, DaggerfallBlocksSnapshot blocks, DaggerfallSiteContext sites)
    {
        RecordingLcg random = RecordingLcg.Create();
        return (new DaggerfallBuildingNameService(random.Service, definitions, blocks, sites), random);
    }

    private static (DaggerfallDefinitions Definitions, DaggerfallBlocksSnapshot Blocks) ReadInputs()
    {
        string root = RepositoryRoot();
        return (
            DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json"))),
            DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.blocks.json"))));
    }

    private class RecordingLcg : DispatchProxy
    {
        internal IRandomService Service { get; private set; } = null!;
        internal List<(uint State, uint UpperExclusive)> Requests { get; } = [];

        internal static RecordingLcg Create()
        {
            IRandomService service = DispatchProxy.Create<IRandomService, RecordingLcg>();
            RecordingLcg proxy = (RecordingLcg)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawLcg15)) throw new NotSupportedException(method?.Name);
            Lcg15Request request = Assert.IsType<Lcg15Request>(Assert.Single(arguments!));
            Requests.Add((request.State, request.UpperExclusive));
            uint state = unchecked(request.State * 1103515245u + 12345u);
            uint value = ((state >> 16) & 0x7fffu) % request.UpperExclusive;
            return new Lcg15Receipt(state, value);
        }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
