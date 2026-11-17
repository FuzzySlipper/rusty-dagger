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
            _ => throw new InvalidOperationException("No live item is bound."), () => current, () => null, RandomMinimum.Create(), _ => null, (_, _) => null);

        DaggerfallQuestMessageContext context = texts.For(instance);

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
            _ => throw new InvalidOperationException("No live item is bound."), () => DaggerfallTextContext.Empty, () => building, RandomMinimum.Create(), _ => null, (_, _) => null);
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

    [Fact]
    public void A_pronoun_reads_the_last_person_the_message_named_and_a_direction_its_last_place()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallQuestSourceDefinition source = new("pronouns", "", "pronouns.txt", DaggerfallQuestDisposition.Compiled,
            [new(1000, ["%G is waiting, %di of here. _woman_ said %g2self that %g3 house is at __woman_, %di of here.", "_man_ said %g2 that %G1 knew."])], [], []);
        DaggerfallQuestMessages messages = new(new DaggerfallTextResolver(definitions.Text), new Dictionary<string, DaggerfallQuestSourceDefinition> { ["pronouns.txt"] = source });
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        DaggerfallQuestRuntimeInstance instance = new(new DaggerfallQuestInstanceSave("quest:1", source.SourceFile, source.Name,
            DaggerfallQuestLifecycle.Active, null, [], []) { Tasks = DaggerfallQuestTaskCompiler.InitialState(program) }, program);
        DaggerfallQuestPronouns Words(bool female) => female ? new("she", "her", "herself", "her") : new("he", "him", "himself", "his");
        DaggerfallQuestMessageContext context = new(DaggerfallTextContext.Empty with { Story = Words(false).Story(DaggerfallTextContext.Empty.Story) },
            new Dictionary<string, DaggerfallQuestResourceTextContext>(StringComparer.Ordinal)
            {
                ["woman"] = new(Name: "Agnes", NameTwo: "The Rusty Guard") { Pronouns = Words(true), Direction = "north" },
                ["man"] = new(Name: "Bob") { Pronouns = Words(false) },
            });

        var rendered = messages.RenderDelivery(instance, new("quest:1", 1000, DaggerfallQuestMessageDelivery.Popup), context);

        Assert.Empty(rendered.Diagnostics);
        // Before any person the words are the male ones; a %di before any place reads the one the message names next.
        Assert.Equal("He is waiting, north of here. Agnes said herself that her house is at The Rusty Guard, north of here.\nBob said him that He knew.",
            rendered.Text);
    }

    [Fact]
    public void A_quest_names_its_region_rulers_order_guild_title_names_and_a_joke_and_keeps_them_on_every_redraw()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord town = definitions.Locations.Records.First(site => site.Id.Region == 17);
        DaggerfallSiteContext sites = new(definitions.Locations, town.Id, null, []);
        DaggerfallQuestSourceDefinition source = definitions.QuestSources.Resolve("C0C00Y12.txt");
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        // A Knights of the Dragon quest, in Daggerfall's region.
        DaggerfallQuestRuntimeInstance instance = new(new DaggerfallQuestInstanceSave("quest:1", source.SourceFile, source.Name,
            DaggerfallQuestLifecycle.Active, null, [], []) { FactionId = 368, Tasks = DaggerfallQuestTaskCompiler.InitialState(program) }, program);
        DaggerfallTextContext current = DaggerfallTextContext.Empty with
        {
            Player = new(Name: "Nameless"), Calendar = new(Date: "Sundas the 1st of Midyear, 3E405"), Location = new(Region: "Daggerfall"),
        };
        DaggerfallQuestTextContext texts = new(definitions, sites, new DaggerfallNpcRegistry(), new DaggerfallItemInstances(),
            _ => throw new InvalidOperationException("No live item is bound."), () => current, () => null, SummonRandom.Create(),
            faction => faction == 368 ? "Squire" : null, (_, _) => null);

        DaggerfallTextContext text = texts.For(instance).Text;

        Assert.Equal("Daggerfall", text.Faction.RegionInContext);
        Assert.Equal("King", text.Faction.RegentTitle);
        Assert.Equal("King Gothryd", text.Faction.RegentName);
        Assert.Equal("King Gothryd", text.Faction.CurrentRegionLeader);
        Assert.Equal("Knights of the Dragon", text.Faction.FactionOrder);
        Assert.Equal("Squire", text.Player.GuildTitle);
        Assert.Equal("Sundas the 1st of Midyear, 3E405", text.Story.QuestDate);
        Assert.True(definitions.Text.Resolve(new(DaggerfallTextKind.Resource, "200"), out DaggerfallTextValue? jokes) == DaggerfallTextResolution.Resolved);
        Assert.Contains(text.Story.Joke, jokes!.TextRuns);
        Assert.All(new[] { text.Story.Name, text.Story.MaleName, text.Story.FemaleName }, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(text, texts.For(instance).Text);
        // Not a member of the quest's guild, the player is named instead (MacroHelper.GuildTitle).
        Assert.Equal("Nameless", new DaggerfallQuestTextContext(definitions, sites, new DaggerfallNpcRegistry(), new DaggerfallItemInstances(),
            _ => throw new InvalidOperationException("No live item is bound."), () => current, () => null, SummonRandom.Create(),
            _ => null, (_, _) => null).For(instance).Text.Player.GuildTitle);
    }

    [Fact]
    public void A_finished_journal_keeps_its_pronouns_and_directions_through_a_save()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallQuestSourceDefinition source = new("kept", "", "kept.txt", DaggerfallQuestDisposition.Compiled,
            [new(1000, ["_woman_ kept to %g2self at __woman_, %di of here."])], [], []);
        Dictionary<string, DaggerfallQuestSourceDefinition> sources = new() { ["kept.txt"] = source };
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        DaggerfallQuestRuntimeInstance instance = new(new DaggerfallQuestInstanceSave("quest:1", source.SourceFile, source.Name,
            DaggerfallQuestLifecycle.Active, null, [], []) { Tasks = DaggerfallQuestTaskCompiler.InitialState(program) }, program);
        DaggerfallQuestMessageContext context = new(DaggerfallTextContext.Empty with { Story = new DaggerfallQuestPronouns("he", "him", "himself", "his").Story(DaggerfallTextContext.Empty.Story) },
            new Dictionary<string, DaggerfallQuestResourceTextContext>(StringComparer.Ordinal)
            {
                ["woman"] = new(Name: "Agnes", NameTwo: "The Rusty Guard") { Pronouns = new("she", "her", "herself", "her"), Direction = "north" },
            });
        DaggerfallQuestMessages messages = new(new DaggerfallTextResolver(definitions.Text), sources);
        messages.Log(instance, 1000, 0, 0);
        Assert.True(messages.RetainJournal(instance, _ => context));

        string json = System.Text.Json.JsonSerializer.Serialize(messages.Capture(), typeof(DaggerfallQuestMessagesSave), DaggerfallSaveJsonContext.Default);
        DaggerfallQuestMessages restored = new(new DaggerfallTextResolver(definitions.Text), sources);
        restored.Restore((DaggerfallQuestMessagesSave)System.Text.Json.JsonSerializer.Deserialize(json, typeof(DaggerfallQuestMessagesSave), DaggerfallSaveJsonContext.Default)!,
            new Dictionary<string, DaggerfallQuestRuntimeInstance>());

        DaggerfallQuestRenderedMessage entry = Assert.Single(restored.RenderJournal([]));
        Assert.Empty(entry.Diagnostics);
        Assert.Equal("Agnes kept to herself at The Rusty Guard, north of here.", entry.Text);
    }

    [Fact]
    public void A_letter_is_dated_by_its_quests_start_after_time_passes_and_after_a_save()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = SummonRandom.Create();
        using var game = fixture.Start(fixture.KnightlyProfile);
        game.State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, "letter-clan", 0, vampireClan: 153);
        long questor = game.State.Npcs.All.First(value => value.Appearance.FactionId == 368).DurableId;
        game.AdvanceElapsedTime(3 * 86400);
        string started = game.CurrentTextContext().Calendar.Date!;
        var source = TestPayload.Definitions.QuestSources.Resolve("$CUREVAM.txt");
        DaggerfallQuestInstanceSave quest = game.State.Quests.Start(new("letter:1", source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], [])
            { QuestorId = questor, FactionId = 368 });
        string Letter(DaggerfallQuestInstanceSave save) => game.State.Quests.RenderMessage(save, 1020, DaggerfallQuestMessageDelivery.Letter, 0).Text;
        string written = Letter(quest);
        Assert.StartsWith(started, written.TrimStart());

        game.AdvanceElapsedTime(5 * 86400);
        Assert.NotEqual(started, game.CurrentTextContext().Calendar.Date);
        Assert.Equal(written, Letter(quest));
        string json = System.Text.Json.JsonSerializer.Serialize(quest, typeof(DaggerfallQuestInstanceSave), DaggerfallSaveJsonContext.Default);
        Assert.Equal(written, Letter((DaggerfallQuestInstanceSave)System.Text.Json.JsonSerializer.Deserialize(json, typeof(DaggerfallQuestInstanceSave), DaggerfallSaveJsonContext.Default)!));
    }
}
