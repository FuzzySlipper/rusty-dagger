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
            _ => throw new InvalidOperationException("No live item is bound."), () => current, () => null, RandomMinimum.Create());

        DaggerfallQuestMessageContext context = texts.For(instance);

        Assert.Same(current.Player, context.Text.Player);
        Assert.Same(current.Location, context.Text.Location);
        DaggerfallQuestResourceTextContext named = context.Resources[place.CanonicalId];
        Assert.Equal(site.Name, named.Name);
        Assert.Equal(site.Name, named.NameTwo);
        Assert.Equal(site.Name, named.Binding);
        DaggerfallQuestResourceTextContext described = context.Resources[item.CanonicalId];
        Assert.Equal(item.TargetSourceSpelling!.Replace('_', ' '), described.Name);
        Assert.Equal(described.Name, described.Details);
    }

    [Fact]
    public void The_Stolen_Item_swears_its_questors_oath_by_the_god_of_the_temple_the_player_stands_in_or_else_of_the_region()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        // Daggerfall's region: its province is Breton and its quests swear by the Temple of Kynareth.
        DaggerfallSiteRecord town = definitions.Locations.Records.First(site => site.Id.Region == 17);
        DaggerfallSiteContext sites = new(definitions.Locations, town.Id, null, []);
        DaggerfallNpcRegistry npcs = new() { Identities = new WorldRpg.Kit.World.DurableIdentityAllocator(WorldRpg.Kit.World.DurableIdentityKind.Actor, 100) };
        // A Temple of Kynareth priest: the temple faction has no race, so the oath is the province's.
        long priest = npcs.RegisterStable(DaggerfallNpcKind.Questor, "priest", new(17, town.Name, ""),
            new("Breton", "Male", 0, 0, 0, 36), "quest giver", ["talk"]);
        DaggerfallQuestSourceDefinition source = definitions.QuestSources.Resolve("C0C00Y12.txt");
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        DaggerfallQuestRuntimeInstance instance = new(new DaggerfallQuestInstanceSave("quest:1", source.SourceFile, source.Name,
            DaggerfallQuestLifecycle.Active, null, [], []) { QuestorId = priest, Tasks = DaggerfallQuestTaskCompiler.InitialState(program) }, program);
        DaggerfallInteriorBuilding? building = new(0, 0, new("TEMPAA01.RMB", 0), 15, 98);
        DaggerfallQuestTextContext texts = new(definitions, sites, npcs, new DaggerfallItemInstances(),
            _ => throw new InvalidOperationException("No live item is bound."), () => DaggerfallTextContext.Empty, () => building, RandomMinimum.Create());
        DaggerfallQuestMessages messages = new(definitions, RandomMinimum.Create());
        // The message as read: its centred lines joined by single spaces.
        string Render(int message) => string.Join(' ', messages.RenderDelivery(instance, new("quest:1", message, DaggerfallQuestMessageDelivery.Popup),
            texts.For(instance)).Text.Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries));

        Assert.True(definitions.Text.Resolve(new(DaggerfallTextKind.Resource, "204"), out DaggerfallTextValue? breton) == DaggerfallTextResolution.Resolved);
        string oath = breton!.TextRuns.First();
        Assert.False(string.IsNullOrWhiteSpace(oath));
        Assert.StartsWith(oath + "!", Render(1011));
        // Inside the House of Dibella the god is Dibella; outside, the region's temple names Kynareth.
        Assert.EndsWith("the favor of Dibella.", Render(1004));
        building = null;
        Assert.EndsWith("the favor of Kynareth.", Render(1004));
        Assert.Equal(definitions.QuestSources.Tables.RegionTemples[17], 36);
    }
}
