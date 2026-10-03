using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class QuestNpcMediaSelectionTests
{
    [Fact]
    public void Site_closure_includes_both_faction_genders_and_static_questor_addresses_once()
    {
        string payload = """
            {"factions":{"factions":[{"flatVisuals":[{"id":23041,"archive":180,"record":1},{"id":23042,"archive":180,"record":2}]}],
                "npcCaptions":[{"archive":180,"record":1,"caption":"person"},{"archive":171,"record":7,"caption":"questor"}]}}
            """;
        Assert.Equal(["sprite/texture-171-7", "sprite/texture-180-1", "sprite/texture-180-2"],
            Arena2SitePublication.RuntimeNpcResources(payload));
    }

    [Theory]
    [InlineData(180, 128)]
    [InlineData(1000, 0)]
    public void Missing_source_meaning_or_malformed_addresses_do_not_publish_an_incomplete_npc_catalog(int archive, int record)
    {
        Assert.Throws<InvalidOperationException>(() => Arena2SitePublication.RuntimeNpcResources("{}"));
        Assert.Throws<InvalidOperationException>(() => Arena2SitePublication.RuntimeNpcResources(
            System.Text.Json.JsonSerializer.Serialize(new { factions = new { factions = Array.Empty<object>(),
                npcCaptions = new[] { new { archive, record } } } })));
    }
}
