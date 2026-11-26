using System.Text;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallLocationOverridesTests
{
    private static readonly DaggerfallSiteId Hearthwing = new(17, 1045);
    private static readonly DaggerfallSiteId Wayrest = new(23, 601);
    private static readonly DaggerfallSiteId Darkimor = new(16, 559);

    [Fact]
    public void The_base_pack_lays_out_Hearthwing_Manor_anew_as_the_reserved_Blades_hall()
    {
        DaggerfallSiteRecord manor = Assert.Single(TestPayload.Definitions.Locations.Records, site => site.Id == Hearthwing);
        DaggerfallSiteExterior exterior = manor.Exterior!;

        Assert.Equal("Hearthwing Manor", manor.Name);
        Assert.True(manor.Reserved);
        Assert.Equal((2, 2, 48, 48), (exterior.Width, exterior.Height, exterior.TileOriginX, exterior.TileOriginY));
        Assert.Equal(["MANRAS01.RMB", "PALAAA00.RMB", "FILLAA03.RMB", "FILLAA02.RMB"],
            exterior.Blocks.OrderBy(block => block.Y).ThenBy(block => block.X).Select(block => block.SourceName));
        DaggerfallSiteBuildingSource hall = exterior.Buildings[new(1, 0, 0)];
        Assert.Equal(("PALAAA00.RMB", 16, 129), (hall.Source.Id.SourceKey, hall.Source.BuildingType, hall.Source.FactionId));
        // The classic estate keeps its own city buildings, and the grid's ground is the baked frame, levelled over all four blocks.
        Assert.Equal(13, exterior.Buildings.Values.Count(building => building.Id.BlockX == 0 && building.Id.BlockY == 0));
        Assert.Equal((46, 81, 46, 81), (exterior.MinX, exterior.MaxX, exterior.MinY, exterior.MaxY));
        Assert.True(exterior.GroundTiles.Contains(64 * 128 + 64));
        Assert.Null(exterior.Detail);
        // Nothing else is laid out anew.
        Assert.False(Assert.Single(TestPayload.Definitions.Locations.Records, site => site.Region == 17 && site.Name == "Hearthfield Manor").Reserved);
    }

    [Fact]
    public void The_shipped_content_carries_the_same_overrides_the_tests_read_from_the_pack()
    {
        ProductContent content = FullContent(TestData.RepositoryRoot);
        const string payload = "worldrpg/packs/daggerfall.base/payload.json";
        Assert.True(content.TryReadFile(payload, out ProductContentFile file));

        DaggerfallLocationOverrides shipped = DaggerfallLocationOverrides.Read(content, payload,
            DaggerfallBaseContent.AssembleAuthored(DaggerfallBaseContent.Files(content, "daggerfall.base", payload, file.Bytes)));

        Assert.Equal(TestPayload.LocationOverrides().Count, shipped.Count);
        Assert.Equal(1, shipped.Count);
    }

    [Fact]
    public void An_override_must_be_baked_and_every_baked_file_must_be_an_override()
    {
        ReadOnlyMemory<byte> payload = Encoding.UTF8.GetBytes("""{"locationOverrides":[{"region":17,"index":1045,"reserved":true}]}""");
        DaggerfallContentException unbaked = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallLocationOverrides.Read(payload, new Dictionary<string, ReadOnlyMemory<byte>>(), "locations"));
        Assert.Contains("location override 17/1045 is not baked below 'locations'; run daggerfall-source-tool location-bake.", unbaked.Message, StringComparison.Ordinal);

        DaggerfallContentException stale = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallLocationOverrides.Read(Encoding.UTF8.GetBytes("{}"), new Dictionary<string, ReadOnlyMemory<byte>> { ["17/1045.ground"] = new byte[1] }, "locations"));
        Assert.Contains("'locations/17/1045.ground' is baked for no location override", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reserved_location_offers_no_building_to_a_random_quest()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallBlocksSnapshot blocks = DaggerfallBlocksContent.Read(TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")));
        DaggerfallSiteRecord manor = definitions.Locations.Records.Single(site => site.Id == Hearthwing);
        DaggerfallSiteContext sites = new(definitions.Locations, manor.Id, null, []);
        sites.AdmitBuildingNames(RandomMinimum.Create(), definitions, blocks);
        DaggerfallQuestPlaceAllocator inspect = new(definitions, sites, RandomMinimum.Create(), (_, _) => false, _ => null, (_, _) => "unused");

        Assert.DoesNotContain(sites.BuildingsAt(manor.Id), building => inspect.IsQuestBuilding(manor, building, -1, 0));
        // The same estate unreserved offers its tavern and houses.
        DaggerfallSiteRecord open = manor with { Reserved = false };
        Assert.Contains(sites.BuildingsAt(manor.Id), building => inspect.IsQuestBuilding(open, building, -1, 0));
    }

    [Fact]
    public void Brisienna_takes_the_Blades_hall_lord_slot_and_the_Squid_only_Wayrest_thieves_house()
    {
        DaggerfallStaticNpcPlacement brisienna = Assert.Single(Assembled(DaggerfallWorldProfileIds.Interior(Hearthwing, new(1, 0, 0))).StaticNpcs,
            npc => npc.Appearance.FactionId == 352);
        Assert.Equal((180, 1), (brisienna.Appearance.BillboardArchive, brisienna.Appearance.BillboardRecord));

        DaggerfallSiteExterior wayrest = TestPayload.Definitions.Locations.Records.Single(site => site.Id == Wayrest).Exterior!;
        DaggerfallSiteBlock thieves = Assert.Single(wayrest.Blocks, block => block.SourceName == "THIEAL00.RMB");
        Assert.Single(Assembled(DaggerfallWorldProfileIds.Interior(Wayrest, new(thieves.X, thieves.Y, 16))).StaticNpcs, npc => npc.Appearance.FactionId == 399);
        // Daggerfall city's own thieves house, the same block, keeps its classic people.
        DaggerfallSiteRecord daggerfall = TestPayload.Definitions.Locations.Records.Single(site => site.Region == 17 && site.Name == "Daggerfall" && site.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteBlock other = Assert.Single(daggerfall.Exterior!.Blocks, block => block.SourceName == "THIEAL00.RMB");
        Assert.DoesNotContain(Assembled(DaggerfallWorldProfileIds.Interior(daggerfall.Id, new(other.X, other.Y, 16))).StaticNpcs, npc => npc.Appearance.FactionId == 399);
    }

    [Fact]
    public void Blades_guards_stand_at_the_hall_door_and_road_entrances_of_Hearthwing_alone()
    {
        DaggerfallStaticNpcPlacement[] guards = [.. Assembled(DaggerfallWorldProfileIds.Exterior(Hearthwing)).StaticNpcs
            .Where(npc => npc.SpawnerId == "blades-guard")];

        Assert.Equal(8, guards.Length);
        Assert.All(guards, guard =>
        {
            Assert.Equal(129, guard.Appearance.FactionId);
            Assert.Equal(ActorInteractionPolicyKind.AmbientBark, guard.Interaction);
            Assert.Equal(ActorHarmPolicyKind.LawProtectedCivilian, guard.Harm);
        });
        DaggerfallSiteId hearthfield = TestPayload.Definitions.Locations.Records.Single(site => site.Region == 17 && site.Name == "Hearthfield Manor").Id;
        Assert.DoesNotContain(Assembled(DaggerfallWorldProfileIds.Exterior(hearthfield)).StaticNpcs, npc => npc.SpawnerId == "blades-guard");
    }

    [Fact]
    public void A_site_cell_is_keyed_by_location_and_cell_and_offers_no_dialogue()
    {
        DaggerfallPeoplePackFiles pack = TestPayload.PeoplePack();
        DaggerfallPeoplePackFiles tampered = pack with
        {
            BlockFiles = [new DaggerfallPeopleBlockFile("blocks/bad-sites.json", Encoding.UTF8.GetBytes("""
                {"kind":"site-exterior","blocks":{
                  "17/1045":[],
                  "17/1045/0,0":[{"slotId":"s:t00","spawner":"blades-guard","position":[0,0,0],"interaction":"full-dialogue","factionId":129,"visual":{"archive":183,"record":2,"gender":"Male"}}]
                }}
                """))],
        };

        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallPeopleContent.Read(tampered, TestPayload.Definitions.Factions, TestPayload.Definitions.Vocabulary, TestPayload.Definitions.Items));

        Assert.Contains("People site cell '17/1045' in 'blocks/bad-sites.json' must be keyed region/index/x,y", error.Message, StringComparison.Ordinal);
        Assert.Contains("People site cell '17/1045/0,0' slot 's:t00' must be a spawner's and not offer dialogue", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Underking_is_set_at_his_home_position_deep_in_the_Crypt_of_Darkimor()
    {
        DaggerfallSiteRecord crypt = TestPayload.Definitions.Locations.Records.Single(site => site.Id == Darkimor);
        DaggerfallSiteDungeonBlock home = Assert.Single(crypt.DungeonBlocks, block => block.SourceKey == "N0000048.RDB");

        DaggerfallStaticNpcPlacement underking = Assert.Single(Assembled(DaggerfallWorldProfileIds.Dungeon(Darkimor)).StaticNpcs, npc => npc.Appearance.FactionId == 353);

        Assert.Equal((182, 54), (underking.Appearance.BillboardArchive, underking.Appearance.BillboardRecord));
        Assert.EndsWith("/cast-the-underking", underking.Id, StringComparison.Ordinal);
        Assert.NotEqual(0, home.X);
    }
}
