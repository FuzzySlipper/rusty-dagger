using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine.Mechanics;
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

        // The within-region case, counted rather than asserted by anecdote: 129 (region, name) pairs
        // across 44 distinct names, the worst repeating eight times in one region. A comment claiming a
        // bare "129 names" would be wrong - the pairs outnumber the names they come from.
        IEnumerable<IGrouping<(int Region, string Name), DaggerfallSiteRecord>> crowded =
            site.Records.GroupBy(record => (record.Id.Region, record.Name)).Where(group => group.Count() > 1);
        Assert.Equal(129, crowded.Count());
        Assert.Equal(44, crowded.Select(group => group.Key.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(8, crowded.Max(group => group.Count()));
        Assert.Equal([16, 98, 284, 357, 439, 532, 599, 769], site.FindByName("Reliquary of Sai").Where(record => record.Id.Region == 5).Select(record => record.Id.Index));

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

        // The lookup matches the whole name, exactly. A case-insensitive or substring match would both
        // be a different lookup: the first would merge names the corpus keeps apart, the second would
        // answer with every location whose name merely starts the same way.
        Assert.Empty(site.FindByName("jode shrine"));
        Assert.Empty(site.FindByName("Jode"));
        Assert.Empty(site.FindByName("Jode Shrine "));

        // The complete identity list, in order, rather than a count with a sample: a count of 32 stays
        // 32 while thirty of its members are the wrong sites.
        Assert.Equal(
            [
                (0, 160), (1, 329), (1, 523), (5, 48), (5, 182), (5, 622), (5, 738), (9, 48),
                (16, 3), (16, 87), (16, 1003), (16, 1062), (17, 319), (17, 780), (18, 212), (18, 244),
                (20, 514), (21, 443), (22, 194), (22, 300), (23, 399), (23, 701), (32, 67), (33, 31),
                (33, 57), (35, 93), (36, 6), (41, 119), (41, 188), (46, 51), (51, 144), (51, 594),
            ],
            shrines.Select(record => (record.Id.Region, record.Id.Index)));

        // The membership reads the lookup's own contract: a carried identity resolves, one nothing
        // publishes does not, and asking for it by name is answered rather than guessed.
        Assert.True(site.Contains(new DaggerfallSiteId(17, 179)));
        Assert.False(site.Contains(new DaggerfallSiteId(17, 999999)));
        Assert.True(site.TryFind(new DaggerfallSiteId(17, 179), out DaggerfallSiteRecord found));
        Assert.Equal("Privateer's Hold", found.Name);
        Assert.False(site.TryFind(new DaggerfallSiteId(17, 999999), out _));

        // Discovery resolves against the records too, so an identity nothing publishes is refused
        // rather than reported as undiscovered - which is what would let a caller treat a typo as a
        // site the player has simply not seen yet.
        Assert.Throws<InvalidOperationException>(() => site.IsDiscovered(new DaggerfallSiteId(17, 999999)));
    }

    [Fact]
    public void Orders_sites_by_region_and_index_whatever_order_the_pack_carries_them_in()
    {
        // The published pack arrives already in identity order, so the corpus alone cannot show whether
        // anything sorts it. This set is deliberately scrambled: an implementation that returned the
        // pack's own order would answer differently here, and two identical sessions would write
        // different save bytes.
        static DaggerfallSiteRecord Record(int region, int index) =>
            new(new DaggerfallSiteId(region, index), $"Shared {region}", index, 0, DaggerfallSiteKind.HomeFarms, false);
        DaggerfallLocationSet scrambled = new(
            [(2, 5), (0, 9), (2, 1), (0, 3)],
            [Record(2, 5), Record(0, 9), Record(2, 1), Record(0, 3)],
            0,
            0,
            0);
        DaggerfallSiteContext site = new(scrambled);

        Assert.Equal([(0, 3), (0, 9), (2, 1), (2, 5)], site.Records.Select(record => (record.Id.Region, record.Id.Index)));
        Assert.Equal([(0, 3), (0, 9)], site.FindByName("Shared 0").Select(record => (record.Id.Region, record.Id.Index)));
        Assert.Equal([(2, 1), (2, 5)], site.FindByName("Shared 2").Select(record => (record.Id.Region, record.Id.Index)));

        site.Discover(new DaggerfallSiteId(2, 5));
        site.Discover(new DaggerfallSiteId(0, 3));
        Assert.Equal([new DaggerfallSiteIdSave(0, 3), new DaggerfallSiteIdSave(2, 5)], site.Capture().Discovered);
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

        // A save that carries an authored-discovered site is redundant rather than wrong - the record
        // still answers - but restoring it must not make the context re-persist the authored fact, or a
        // hand-edited or older save would leave the delta carrying something play never did.
        DaggerfallSiteContext restored = new(Read().Locations, null, null, [authored, unrevealed]);
        Assert.True(restored.IsDiscovered(authored));
        Assert.True(restored.IsDiscovered(unrevealed));
        Assert.Equal([new DaggerfallSiteIdSave(0, 2)], restored.Capture().Discovered);

        // The delta is captured in identity order, not in the order play happened to reveal it: a save
        // that reordered itself run to run would make two identical sessions write different bytes.
        DaggerfallSiteContext ordered = Context();
        ordered.Discover(new DaggerfallSiteId(31, 0));
        ordered.Discover(new DaggerfallSiteId(17, 9));
        ordered.Discover(new DaggerfallSiteId(0, 2));
        Assert.Equal(
            [new DaggerfallSiteIdSave(0, 2), new DaggerfallSiteIdSave(17, 9), new DaggerfallSiteIdSave(31, 0)],
            ordered.Capture().Discovered);
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

        // The region is the active site's, and the anchor never overrides it. The two are in different
        // regions here, so an implementation that preferred the anchor would answer 17 rather than 31.
        Assert.NotEqual(settlement.Region, new DaggerfallSiteId(31, 0).Region);
        DaggerfallSiteContext travelling = Context();
        travelling.Enter(settlement);
        travelling.Enter(new DaggerfallSiteId(31, 0));
        Assert.Equal(settlement, travelling.ReturnAnchor);
        Assert.Equal(31, travelling.Region);
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

        DaggerfallSavePayload read = DaggerfallSavePayload.Read(DaggerfallSavePayload.Encode(Payload(before.Capture())));
        DaggerfallSiteSave section = Assert.IsType<DaggerfallSiteSave>(read.Site);

        DaggerfallSiteContext after = new(definitions.Locations, ToId(section.Active), ToId(section.ReturnAnchor), section.Discovered.Select(id => id.Require()));
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
        // collapsing it into an absent section would make a resumed session invent a site on load.
        DaggerfallSavePayload read = DaggerfallSavePayload.Read(
            DaggerfallSavePayload.Encode(Payload(new DaggerfallSiteSave(null, null, []))));
        DaggerfallSiteSave section = Assert.IsType<DaggerfallSiteSave>(read.Site);
        Assert.Null(section.Active);
        Assert.Null(section.ReturnAnchor);
        Assert.Empty(section.Discovered);
    }

    [Fact]
    public void Refuses_a_saved_site_that_names_no_region_or_no_index()
    {
        // A JSON object with an absent member used to default that member to zero, and region 0 index 0
        // is a real location, so a save that named no site at all restored the player at Caarcun Manor
        // with no notice. Absence has to be representable, which is why both members are nullable.
        Assert.Equal("Caarcun Manor", Context().Require(new DaggerfallSiteId(0, 0)).Name);

        foreach (string shape in new[] { "{}", """{"Region":5}""", """{"Index":5}""" })
        {
            JsonObject payload = RoundTrippable();
            payload["Site"]!["Active"] = JsonNode.Parse(shape);
            ArgumentException error = Assert.ThrowsAny<ArgumentException>(() => DaggerfallSavePayload.Read(Encoded(payload)));
            Assert.Contains("must name a non-negative region and index", error.Message, StringComparison.Ordinal);
        }

        // A site that does name both members is still read, including the one at the origin.
        JsonObject origin = RoundTrippable();
        origin["Site"]!["Active"] = JsonNode.Parse("""{"Region":0,"Index":0}""");
        Assert.Equal(new DaggerfallSiteIdSave(0, 0), DaggerfallSavePayload.Read(Encoded(origin)).Site.Active);
    }

    [Fact]
    public void Refuses_a_saved_return_anchor_for_a_player_placed_at_no_site()
    {
        // Nothing in the product produces this pair - Enter sets the anchor only from an active site and
        // Leave clears it - so a save carrying one would leave the first Leave moving a player who is
        // nowhere onto a site they were never shown to be at.
        JsonObject payload = RoundTrippable();
        payload["Site"]!["Active"] = null;
        payload["Site"]!["ReturnAnchor"] = JsonNode.Parse("""{"Region":17,"Index":4}""");
        ArgumentException error = Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Read(Encoded(payload)));
        Assert.Contains("requires an active site", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_saved_site_section_that_records_no_revealed_list()
    {
        // Absent members on the section itself are named for what they are, rather than surfacing as a
        // bare null-argument failure that says nothing about which section or which save.
        foreach (string shape in new[] { "{}", """{"Active":null,"ReturnAnchor":null}""" })
        {
            JsonObject payload = RoundTrippable();
            payload["Site"] = JsonNode.Parse(shape);
            ArgumentException error = Assert.ThrowsAny<ArgumentException>(() => DaggerfallSavePayload.Read(Encoded(payload)));
            Assert.Contains("Discovered", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Refuses_every_published_type_outside_the_fields_vocabulary()
    {
        // The boundary is 0..14 inclusive, and it is checked below the pack as well as through it: a
        // negative value is not a kind, and neither is anything from 15 up to the field's own ceiling of
        // 31 or the donor's out-of-field None. Checking only 15 would let a reader that forgot its lower
        // bound keep accepting -1.
        foreach (int published in new[] { 0, 14 })
        {
            Assert.True(DaggerfallSiteKinds.TryResolve(published, out DaggerfallSiteKind accepted));
            Assert.Equal((DaggerfallSiteKind)published, accepted);
        }

        foreach (int published in new[] { -1, int.MinValue, 15, 16, 31, 32, 0xffff, int.MaxValue })
        {
            Assert.False(DaggerfallSiteKinds.TryResolve(published, out _));
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => DaggerfallSiteKinds.Resolve(published, 17, 179));
            Assert.Contains($"{published}", error.Message, StringComparison.Ordinal);
            Assert.Contains("17", error.Message, StringComparison.Ordinal);
            Assert.Contains("179", error.Message, StringComparison.Ordinal);
            Assert.Contains("(0..14)", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Refuses_a_saved_site_section_whose_identities_are_malformed()
    {
        // Each of these is internally inconsistent rather than a value the selected content disagrees
        // with, so the section is refused before anything resolves a site through it.
        foreach ((string field, string shape, string expected) in new[]
        {
            ("Discovered", """[{"Region":17,"Index":4},{"Region":17,"Index":4}]""", "record each discovered site once"),
            ("Discovered", """[{"Region":-1,"Index":0}]""", "non-negative region and index"),
            ("Discovered", """[{"Region":0,"Index":-1}]""", "non-negative region and index"),
            ("Discovered", "[null]", "Value cannot be null"),
            ("Active", """{"Region":-1,"Index":0}""", "non-negative region and index"),
            ("ReturnAnchor", """{"Region":0,"Index":-1}""", "non-negative region and index"),
        })
        {
            JsonObject payload = RoundTrippable();
            payload["Site"]![field] = JsonNode.Parse(shape);
            ArgumentException error = Assert.ThrowsAny<ArgumentException>(() => DaggerfallSavePayload.Read(Encoded(payload)));
            Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        }
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

    private static DaggerfallSiteId? ToId(DaggerfallSiteIdSave? id) => id?.Require();

    /// <summary>A valid save's JSON, for a test to corrupt one member of.</summary>
    private static JsonObject RoundTrippable() =>
        JsonNode.Parse(System.Text.Encoding.UTF8.GetString(DaggerfallSavePayload.Encode(Payload(new DaggerfallSiteSave(null, null, []))).Bytes.Span))!.AsObject();

    private static RulesetSavePayload Encoded(JsonObject payload) =>
        new(DaggerfallRuleset.Identity, System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()));

    /// <summary>A save whose only interesting state is the site section it is given.</summary>
    private static DaggerfallSavePayload Payload(DaggerfallSiteSave? site) => new(
        new DaggerfallPlayerSave(0f, 0f, 0f, 0f, 0f, EmptyStats()),
        [],
        [],
        0,
        1,
        new DaggerfallInventorySave([], [], []),
        [],
        new DurableIdentityState([new KindAllocatorState(DurableIdentityKind.Actor, 1, [], []), new KindAllocatorState(DurableIdentityKind.Item, 1, [], [])]),
        [],
        new DaggerfallCalendarSave(1, 1, 1, 0, 0, 0, 0d),
        site ?? throw new ArgumentNullException(nameof(site)),
        [],
        new DaggerfallVariablesSave([]),
        new DaggerfallNpcSave([]),
        [],
        new DaggerfallSkillProgressionSave([], 0, []),
        new DaggerfallSocialSave([], [], [], []),
        new DaggerfallCharacterSave("Nameless", "breton", DaggerfallCharacterGender.Male, 0,
            DaggerfallCharacterReflexes.Average, "class00"))
    {
        Quests = new([]),
    };

    private static DaggerfallStatsSave EmptyStats() => new(StatsComponentCapture.Capture(new StatsComponent()), []);

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
