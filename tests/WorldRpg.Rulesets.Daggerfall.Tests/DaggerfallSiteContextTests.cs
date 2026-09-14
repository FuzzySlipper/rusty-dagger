using System.Text.Json.Nodes;
using WorldRpg.Kit;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The session's location context: what a site is, how one is told from another when names collide,
/// where an exit returns to, and what survives a save. Everything here reads the published corpus
/// through its real records rather than a fixture, because the facts under test - shared names,
/// repeated identities, an out-of-enum kind - are properties of that corpus.
/// </summary>
public sealed class DaggerfallSiteContextTests
{
    [Fact]
    public void Identifies_a_site_by_region_and_index_when_its_name_is_shared()
    {
        DaggerfallSiteContext site = Context();

        // The corpus before any lookup: more locations than names, so the name is not a key.
        Assert.Equal(15251, site.RecordCount);
        Assert.Equal(12672, site.Records.Select(record => record.Name).Distinct(StringComparer.Ordinal).Count());

        IReadOnlyList<DaggerfallSiteRecord> shrines = site.FindByName("Jode Shrine");
        Assert.Equal(32, shrines.Count);
        Assert.True(shrines.Select(record => record.Id.Region).Distinct().Count() > 1, "the shared name has to span regions for the ambiguity to be real");

        // Two locations in the *same* region share the name and must still be two sites: a lookup that
        // resolved by name would answer one of these for the other and there would be no way to tell.
        DaggerfallSiteRecord first = site.Require(new DaggerfallSiteId(1, 329));
        DaggerfallSiteRecord second = site.Require(new DaggerfallSiteId(1, 523));
        Assert.Equal("Jode Shrine", first.Name);
        Assert.Equal("Jode Shrine", second.Name);
        Assert.Equal(first.Region, second.Region);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.MapId, second.MapId);
        Assert.Contains(shrines, record => record.Id == first.Id);
        Assert.Contains(shrines, record => record.Id == second.Id);

        // A name nothing publishes is an empty answer rather than an exception, which is what lets a
        // caller distinguish "no such name" from "more than one site has it".
        Assert.Empty(site.FindByName("A Name No Location Has"));
    }

    [Fact]
    public void Discovers_a_site_the_corpus_already_marks_and_only_the_delta_is_play_s()
    {
        DaggerfallSiteContext site = Context();

        // Charing is authored discovered; the Caorchom ruin is not. Both facts come from the record, so
        // a session that has never moved still answers the first truthfully.
        DaggerfallSiteId authored = new(17, 4);
        DaggerfallSiteId unrevealed = new(0, 2);
        Assert.True(site.Require(authored).Discovered);
        Assert.False(site.Require(unrevealed).Discovered);
        Assert.True(site.IsDiscovered(authored));
        Assert.False(site.IsDiscovered(unrevealed));

        // What play adds is the delta and nothing else: revealing a site the corpus already marked
        // discovered must not put it in the save, or every reload would carry authored facts it can
        // re-derive from the bundle instead.
        site.Enter(unrevealed);
        Assert.True(site.IsDiscovered(unrevealed));
        Assert.Equal([new DaggerfallSiteIdSave(0, 2)], site.Capture().Discovered);

        site.Discover(authored);
        Assert.Equal([new DaggerfallSiteIdSave(0, 2)], site.Capture().Discovered);
    }

    [Fact]
    public void Reads_the_kind_the_map_tables_type_field_names()
    {
        DaggerfallSiteContext site = Context();

        Assert.Equal(DaggerfallSiteKind.TownCity, site.Require(new DaggerfallSiteId(17, 4)).Kind);
        Assert.Equal("Charing", site.Require(new DaggerfallSiteId(17, 4)).Name);
        Assert.Equal(DaggerfallSiteKind.DungeonLabyrinth, site.Require(new DaggerfallSiteId(17, 179)).Kind);
        Assert.Equal(DaggerfallSiteKind.DungeonRuin, site.Require(new DaggerfallSiteId(0, 2)).Kind);

        // Fourteen is the donor's HomeYourShips and the highest value its five-bit type field carries.
        // A vocabulary that stopped at Coven would refuse locations the corpus really publishes.
        DaggerfallSiteRecord ship = site.Require(new DaggerfallSiteId(31, 1));
        Assert.Equal(DaggerfallSiteKind.HomeYourShips, ship.Kind);
        Assert.Equal("Your Ship", ship.Name);

        // Every kind the enum names is one the corpus uses, so none of the fifteen is vocabulary this
        // ruleset invented and none of the corpus's kinds is missing from it.
        foreach (DaggerfallSiteKind kind in Enum.GetValues<DaggerfallSiteKind>())
        {
            Assert.Contains(site.Records, record => record.Kind == kind);
        }
    }

    [Fact]
    public void Anchors_a_return_from_a_dungeon_to_the_settlement_it_was_entered_from()
    {
        DaggerfallSiteContext site = Context();
        DaggerfallSiteId settlement = new(17, 4);
        DaggerfallSiteId dungeon = new(17, 9);
        Assert.Null(site.Active);
        Assert.Null(site.ReturnAnchor);
        Assert.Null(site.Region);

        // Entering a site the bundle does not carry would leave every later read answering from a
        // location nobody published, so it is refused rather than held.
        Assert.Throws<InvalidOperationException>(() => site.Enter(new DaggerfallSiteId(17, 999999)));
        Assert.Throws<InvalidOperationException>(() => site.Require(new DaggerfallSiteId(17, 999999)));

        site.Enter(settlement);
        Assert.Equal(settlement, site.Active);
        Assert.Null(site.ReturnAnchor);
        Assert.Equal(17, site.Region);
        Assert.Equal("Charing", site.ActiveSite!.Name);

        site.Enter(dungeon);
        Assert.Equal(dungeon, site.Active);
        Assert.Equal(settlement, site.ReturnAnchor);
        Assert.Equal("Castle Necromoghan", site.ActiveSite!.Name);

        // Re-entering where the player already is must not orphan the anchor: the site they came from
        // is still the settlement, not the dungeon they are standing in.
        site.Enter(dungeon);
        Assert.Equal(dungeon, site.Active);
        Assert.Equal(settlement, site.ReturnAnchor);

        site.Leave();
        Assert.Equal(settlement, site.Active);
        Assert.Null(site.ReturnAnchor);

        site.Leave();
        Assert.Null(site.Active);
        Assert.Null(site.Region);
    }

    [Fact]
    public void Round_trips_a_saved_site_the_scenario_never_names()
    {
        DaggerfallDefinitions definitions = Read();
        DaggerfallSiteId active = new(31, 0);
        DaggerfallSiteId revealed = new(0, 2);

        // Neither site is Privateer's Hold, nor even in its region, and both are authored undiscovered:
        // the discovery below is play's delta rather than a flag the bundle already carried.
        Assert.False(definitions.Locations.Records.Single(record => record.Id == active).Discovered);
        Assert.False(definitions.Locations.Records.Single(record => record.Id == revealed).Discovered);
        Assert.False(Context().IsDiscovered(active));

        DaggerfallSiteContext before = Context();
        before.Enter(active);
        before.Discover(revealed);
        Assert.True(before.IsDiscovered(active));
        Assert.True(before.IsDiscovered(revealed));

        DaggerfallSavePayload read = DaggerfallSavePayload.Read(DaggerfallSavePayload.Encode(Payload(before.Capture()))).Payload;
        DaggerfallSiteSave section = Assert.IsType<DaggerfallSiteSave>(read.Site);

        DaggerfallSiteContext after = new(definitions.Locations, ToId(section.Active), ToId(section.ReturnAnchor), section.Discovered.Select(id => new DaggerfallSiteId(id.Region, id.Index)));
        Assert.Equal(active, after.Active);
        Assert.Null(after.ReturnAnchor);
        Assert.Equal(31, after.Region);
        Assert.Equal("Mantellan Crux", after.ActiveSite!.Name);
        Assert.Equal(DaggerfallSiteKind.DungeonLabyrinth, after.ActiveSite.Kind);
        Assert.True(after.IsDiscovered(active));
        Assert.True(after.IsDiscovered(revealed));
        Assert.Equal("Ruins of The Caorchom Farmstead", after.Require(revealed).Name);
    }

    [Fact]
    public void Round_trips_a_save_that_records_the_player_at_no_site()
    {
        // A section that records no active site is a fact the save states, and it has to survive as one:
        // collapsing it into an absent section would make a resumed session believe its save predated
        // site persistence and start the player at the bundle's own site instead.
        DaggerfallSavePayload read = DaggerfallSavePayload.Read(
            DaggerfallSavePayload.Encode(Payload(new DaggerfallSiteSave(null, null, [])))).Payload;
        DaggerfallSiteSave section = Assert.IsType<DaggerfallSiteSave>(read.Site);
        Assert.Null(section.Active);
        Assert.Null(section.ReturnAnchor);
        Assert.Empty(section.Discovered);
    }

    [Fact]
    public void Reports_a_save_written_before_the_session_persisted_its_site()
    {
        DaggerfallSaveRead read = DaggerfallSavePayload.Read(DaggerfallSavePayload.Encode(Payload(null)));
        Assert.Null(read.Payload.Site);
        SaveRestoreNotice notice = Assert.Single(read.Notices, value => value.Code == "site-section-absent");
        Assert.Contains("starts at the bundle's own starting site", notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_published_location_type_the_map_tables_type_field_cannot_name()
    {
        // Fifteen is one past the highest value the five-bit field carries. Every consumer that branches
        // on a kind would otherwise answer from a kind nobody published.
        JsonNode pack = JsonNode.Parse(File.ReadAllText(PackPath()))!;
        JsonNode location = pack["locations"]!["locations"]![0]!;
        Assert.Equal(0, (int)location["region"]!);
        Assert.Equal(0, (int)location["index"]!);
        location["locationType"] = 15;

        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(
            () => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(pack.ToJsonString())));
        Assert.Contains("location 0 of region 0 carries locationType 15", error.Message, StringComparison.Ordinal);
        Assert.Contains("site kinds the map table's type field names (0..14)", error.Message, StringComparison.Ordinal);
    }

    private static DaggerfallSiteId? ToId(DaggerfallSiteIdSave? id) => id is null ? null : new DaggerfallSiteId(id.Region, id.Index);

    /// <summary>A save whose only interesting state is the site section it is given.</summary>
    private static DaggerfallSavePayload Payload(DaggerfallSiteSave? site) => new(
        DaggerfallSavePayload.CurrentSchemaVersion,
        new DaggerfallPlayerSave(0f, 0f, 0f, 0f, 0f, 10, 10, 10),
        [],
        0,
        1,
        new DaggerfallInventorySave([], [], []),
        [],
        new DurableIdentityState([new KindAllocatorState(DurableIdentityKind.Item, 1, [], [])]),
        [],
        null,
        [],
        null,
        site);

    private static DaggerfallSiteContext Context() => new(Read().Locations);

    private static DaggerfallDefinitions Read() => DaggerfallBaseContent.Read(File.ReadAllBytes(PackPath()));

    private static string PackPath() => Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json");

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
