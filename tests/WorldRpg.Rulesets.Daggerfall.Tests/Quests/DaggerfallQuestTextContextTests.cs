using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>What a quest's messages name, built without the session over a site directory, an NPC registry and item instances of its own.</summary>
public sealed class DaggerfallQuestTextContextTests
{
    [Fact]
    public void A_bound_place_names_its_site_and_an_unbound_item_names_its_source_spelling()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteContext sites = new(definitions.Locations);
        DaggerfallQuestResourceDefinition place = definitions.QuestSources.Resources.First(value => value.Kind == "place"
            && definitions.QuestSources.Resources.Any(item => item.SourceFile == value.SourceFile && item.Kind == "item"
                && item.Item?.Template is null && item.TargetSourceSpelling is not null));
        DaggerfallQuestResourceDefinition item = definitions.QuestSources.Resources.First(value => value.SourceFile == place.SourceFile
            && value.Kind == "item" && value.Item?.Template is null && value.TargetSourceSpelling is not null);
        DaggerfallQuestSourceDefinition source = definitions.QuestSources.Resolve(place.SourceFile);
        DaggerfallSiteRecord site = sites.Records.First();
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        DaggerfallQuestRuntimeInstance instance = new(new DaggerfallQuestInstanceSave("quest:1", source.SourceFile, source.Name,
            DaggerfallQuestLifecycle.Active, null,
            [new(place.CanonicalId, DaggerfallQuestResourceBinding.Place(new(site.Id.Region, site.Id.Index))),
                new(item.CanonicalId, DaggerfallQuestResourceBinding.Pending())], [])
            { Tasks = DaggerfallQuestTaskCompiler.InitialState(program) }, program);
        DaggerfallTextContext current = DaggerfallTextContext.Empty;
        DaggerfallQuestTextContext texts = new(definitions, sites, new DaggerfallNpcRegistry(), new DaggerfallItemInstances(),
            _ => throw new InvalidOperationException("No live item is bound."), () => current);

        DaggerfallQuestMessageContext context = texts.For(instance);

        Assert.Same(current, context.Text);
        DaggerfallQuestResourceTextContext named = context.Resources[place.CanonicalId];
        Assert.Equal(site.Name, named.Name);
        Assert.Equal(site.Name, named.NameTwo);
        Assert.Equal(site.Name, named.Binding);
        DaggerfallQuestResourceTextContext described = context.Resources[item.CanonicalId];
        Assert.Equal(item.TargetSourceSpelling!.Replace('_', ' '), described.Name);
        Assert.Equal(described.Name, described.Details);
    }
}
