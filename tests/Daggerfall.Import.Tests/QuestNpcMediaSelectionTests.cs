using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class QuestNpcMediaSelectionTests
{
    [CorpusFact("TEXTURE.171", "TEXTURE.175", "TEXTURE.180", "TEXTURE.357")]
    public void World_media_publishes_every_record_of_the_people_archives_once()
    {
        IReadOnlyList<string> people = Arena2SitePublication.RuntimeNpcResources(Arena2SiteSources.ForClassicMedia(TestData.CorpusRoot));

        // A faction's flats, a quest person and a captioned townsfolk are drawn from these archives, by records the authored
        // factions and captions name, which the import does not read; so it publishes all of them.
        Assert.Equal(people.Count, people.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("sprite/texture-171-4", people);
        Assert.Contains("sprite/texture-180-2", people);
        Assert.All(people, id => Assert.Contains(int.Parse(id.Split('-')[1]), Arena2SitePublication.PeopleArchives));
    }

    [Fact]
    public void World_media_publishes_a_sprite_for_every_mobile_the_source_metadata_draws()
    {
        IReadOnlyList<string> actors = Arena2SitePublication.RuntimeActorResources();

        Assert.Contains("actor/mobile-0", actors);
        Assert.Contains("actor/mobile-146", actors);
        // The horse has no sprite of its own in the classic tables, so nothing draws it.
        Assert.DoesNotContain("actor/mobile-39", actors);
    }

    [Fact]
    public void Exterior_media_closure_names_all_donor_nature_and_climate_variants()
    {
        IReadOnlyList<string> nature = Arena2SitePublication.RuntimeNatureResources();
        IReadOnlyList<string> terrain = Arena2SitePublication.RuntimeTerrainResources();

        Assert.Equal(12 * 31, nature.Count);
        Assert.Equal(12 * 56, terrain.Count);
        Assert.Equal(nature.Count, nature.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(terrain.Count, terrain.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("sprite/texture-500-1", nature);
        Assert.Contains("sprite/texture-511-31", nature);
        Assert.Contains("terrain/texture-303-0", terrain);
        Assert.Contains("terrain/texture-403-55", terrain);
        Assert.Contains("terrain/texture-404-55", terrain);
    }
}
