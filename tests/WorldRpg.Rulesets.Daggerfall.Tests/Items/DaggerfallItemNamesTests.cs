using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallItemNamesTests
{
    [Fact]
    public void An_effects_item_is_named_by_its_label_while_it_lives_and_as_an_enchanted_item_after()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallItemDefinition definition = definitions.Items.Values.First();
        DaggerfallItemInstances instances = new();
        instances.RegisterDefaultUnique(7, definition, DaggerfallItemOwner.Actor(1));

        Assert.Equal(DaggerfallItemNames.Label(definitions, definition, instances.RequireUnique(7)),
            DaggerfallItemNames.EffectItemName(definitions, instances, 7));
        Assert.Equal("an enchanted item", DaggerfallItemNames.EffectItemName(definitions, instances, 8));
    }
}
