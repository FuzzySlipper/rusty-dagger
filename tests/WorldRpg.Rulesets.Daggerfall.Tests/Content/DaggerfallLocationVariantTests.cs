using System.Text;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The location variants the per-block publication carries, as the assembly and a session's catalog view apply them: a
/// selected variant redraws its location's exterior, never its interiors, and each session sees only its own selection.
/// </summary>
public sealed class DaggerfallLocationVariantTests
{
    private static readonly DaggerfallSiteId Charing = new(17, 4);
    private static readonly DaggerfallWorldProfileKey Exterior = DaggerfallWorldProfileIds.Exterior(Charing);

    private static readonly Lazy<DaggerfallLocationAssembly> Assembly = new(() =>
    {
        ProductContent content = FullContent(TestData.RepositoryRoot);
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(TestSessions.Data(content));
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(TestSessions.Data(content), media, TestPayload.Definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(TestSessions.Data(content), media));
        return new DaggerfallLocationAssembly(TestPayload.Definitions, new DaggerfallWorldBlocks(TestSessions.Data(content), WorldVariants(content)), () => product.Value, () => meshes.Value);
    });

    [Fact]
    public void A_published_location_variant_redraws_the_exterior_and_leaves_its_interiors_and_other_places_alone()
    {
        DaggerfallLocationAssembly assembly = Assembly.Value;
        DaggerfallWorldProfileKey interior = DaggerfallWorldProfileIds.Interior(Charing, new(3, 4, 0));
        Assert.True(assembly.Varies(Exterior, "closed"));
        Assert.False(assembly.Varies(interior, "closed"));
        Assert.False(assembly.Varies(DaggerfallWorldProfileIds.Exterior(PrivateersHold), "closed"));
        Assert.False(assembly.Varies(Exterior, "flooded"));
        Assert.Contains("has no variant 'flooded' in the selected packs",
            Assert.Throws<InvalidOperationException>(() => assembly.Assemble(Exterior, "flooded")).Message, StringComparison.Ordinal);

        DaggerfallSiteProfile open = assembly.Assemble(Exterior), closed = assembly.Assemble(Exterior, "closed");
        Assert.Equal(open.ProfileKey, closed.ProfileKey);
        Assert.NotEqual(open.Geometry.Spatial.Single(part => part.Id == "block/3/3"), closed.Geometry.Spatial.Single(part => part.Id == "block/3/3"));
        Assert.Equal(open.Anchors.Keys.Order(StringComparer.Ordinal), closed.Anchors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_cell_placing_another_block_draws_it_and_its_interiors_when_its_building_slots_match_and_is_refused_when_they_do_not()
    {
        // Charing's palace cell (2, 2) places PALAAA02; PALAAA01 states the same building slot, the market's tavern block does not.
        ProductContent content = FullContent(TestData.RepositoryRoot);
        DaggerfallWorldVariantContent variants = DaggerfallWorldVariantContent.Read([new("fixture", Encoding.UTF8.GetBytes("""
            { "worldVariants": { "blocks": [], "locations": [
              { "region": 17, "location": 4, "variant": "rebuilt", "blocks": [ { "x": 2, "y": 2, "block": "PALAAA01.RMB" } ] },
              { "region": 17, "location": 4, "variant": "tavern", "blocks": [ { "x": 3, "y": 3, "block": "TVRNAL06.RMB" } ] } ] } }
            """), "fixture/variants", [])], TestPayload.Definitions.Locations.Records);
        DaggerfallData data = TestSessions.Data(content);
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(data);
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(data, media, TestPayload.Definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(data, media));
        DaggerfallLocationAssembly assembly = new(TestPayload.Definitions, new DaggerfallWorldBlocks(data, variants), () => product.Value, () => meshes.Value);

        DaggerfallSiteProfile open = Assembly.Value.Assemble(Exterior), rebuilt = assembly.Assemble(Exterior, "rebuilt");
        Assert.NotEqual(open.Geometry.Spatial.Single(part => part.Id == "block/2/2"), rebuilt.Geometry.Spatial.Single(part => part.Id == "block/2/2"));
        Assert.Equal(open.Geometry.Spatial.Where(part => part.Id != "block/2/2"), rebuilt.Geometry.Spatial.Where(part => part.Id != "block/2/2"));
        // The doors are the placed block's own; every other cell's keep their ids, so their saved state still names them.
        static IEnumerable<string> Elsewhere(DaggerfallSiteProfile profile) => profile.Doors.Select(door => door.Id.ToString()).Where(id => !id.Contains("@2,2", StringComparison.Ordinal));
        Assert.Equal(Elsewhere(open), Elsewhere(rebuilt));
        Assert.Contains(rebuilt.Doors, door => door.Id.ToString().StartsWith("PALAAA01", StringComparison.Ordinal));
        // The palace in the swapped cell is entered as the block placed there builds it; a building elsewhere is untouched.
        DaggerfallWorldProfileKey palace = DaggerfallWorldProfileIds.Interior(Charing, new(2, 2, 0)), elsewhere = DaggerfallWorldProfileIds.Interior(Charing, new(3, 4, 0));
        Assert.True(assembly.Varies(palace, "rebuilt"));
        Assert.False(assembly.Varies(elsewhere, "rebuilt"));
        Assert.NotEqual(assembly.Assemble(palace).Geometry.Spatial.Single().Path, assembly.Assemble(palace, "rebuilt").Geometry.Spatial.Single().Path);
        Assert.Contains("places 'TVRNAL06.RMB' in cell (3, 3), whose building slots are not those of the 'MARKAA01.RMB' it replaces",
            Assert.Throws<DaggerfallContentException>(() => assembly.Assemble(Exterior, "tavern")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_location_variant_naming_a_cell_the_location_does_not_place_or_a_variant_no_pack_bakes_is_refused_at_load()
    {
        static string Refusal(string locations) => Assert.Throws<DaggerfallContentException>(() => DaggerfallWorldVariantContent.Read([new("fixture",
            Encoding.UTF8.GetBytes($$"""{ "worldVariants": { "blocks": [], "locations": [ {{locations}} ] } }"""), "fixture/variants", [])],
            TestPayload.Definitions.Locations.Records)).Message;
        Assert.Contains("is not one the location places a block in",
            Refusal("""{ "region": 17, "location": 4, "variant": "v", "blocks": [ { "x": 9, "y": 9, "block": "PALAAA01.RMB" } ] }"""));
        Assert.Contains("which no selected pack bakes",
            Refusal("""{ "region": 17, "location": 4, "variant": "v", "blocks": [ { "x": 3, "y": 3, "variant": "closed" } ] }"""));
        Assert.Contains("changes nothing", Refusal("""{ "region": 17, "location": 4, "variant": "v", "blocks": [ { "x": 3, "y": 3 } ] }"""));
        Assert.Contains("cannot select", Refusal("""{ "region": 17, "location": 4, "variant": "-", "blocks": [ { "x": 2, "y": 2, "block": "PALAAA01.RMB" } ] }"""));
    }

    [Fact]
    public void Each_session_resolves_its_own_selection_and_a_variant_nothing_publishes_shows_the_base()
    {
        DaggerfallSiteProfiles catalog = DaggerfallSiteProfiles.Resolve(Assembly.Value, null);
        DaggerfallSiteProfile open = catalog.Require(Exterior);
        DaggerfallSiteProfiles session = catalog.ForSession([new(Charing.Region, Charing.Index, "closed")]);
        DaggerfallSiteProfile closed = session.Require(Exterior);
        Assert.NotSame(open, closed);
        Assert.Same(closed, session.Require(Exterior));
        Assert.Same(open, catalog.Require(Exterior));
        Assert.Same(open, catalog.ForSession().Require(Exterior));
        // A location variant redraws the exterior alone: the town's buildings are entered as they always are.
        DaggerfallWorldProfileKey interior = DaggerfallWorldProfileIds.Interior(Charing, new(3, 4, 0));
        Assert.Same(catalog.Require(interior), session.Require(interior));

        session.SetLocationVariant(Charing, "flooded");
        Assert.Same(open, session.Require(Exterior));
        Assert.Equal([new DaggerfallWorldVariantSave(Charing.Region, Charing.Index, "flooded")], session.CaptureVariants());
        session.SetLocationVariant(Charing, "-");
        Assert.Same(open, session.Require(Exterior));
        Assert.Empty(session.CaptureVariants());
    }
}
