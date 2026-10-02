using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestPersonSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_retained_Person_selects_published_faction_race_gender_and_independent_display(bool maximum)
    {
        var definitions = TestPayload.Definitions;
        var site = definitions.Locations.Records.First(value => value.Region == 17);
        var giver = Giver();
        var random = maximum ? RandomMaximum.Create() : RandomMinimum.Create();
        var allocator = new DaggerfallQuestPersonAllocator(definitions, random, new(definitions, random),
            _ => new(site, giver, 152));
        var declarations = definitions.QuestSources.Resources.Where(value => value.Kind == "person").ToArray();
        Assert.Equal(574, declarations.Length);
        foreach (var declaration in declarations)
        {
            var selected = allocator.Allocate(Instance(declaration.SourceFile), declaration);
            var person = Assert.IsType<DaggerfallQuestPersonSelection>(selected.SelectedPerson);
            Assert.Contains(person.FactionId, definitions.Factions.Factions.Keys);
            Assert.True(definitions.Catalogs.TryGetRace(person.Race, out _));
            Assert.Contains(person.Gender, new[] { "Male", "Female" });
            Assert.InRange(person.HudFace, 0, 9);
            Assert.Equal(declaration.Person!.Face, person.SourceFace);
            Assert.Equal(person.DisplayName, selected.Text!.Name);
            if (person.Appearance is { } appearance)
            {
                Assert.Equal(person.Race, appearance.Race);
                Assert.Equal(person.Gender, appearance.Gender);
                Assert.Equal(person.FactionId, appearance.FactionId);
                if (person.QuestorId is null)
                {
                    var flat = definitions.Factions.Factions[person.FactionId].FlatVisuals[person.Gender == "Female" ? 1 : 0];
                    Assert.Equal((flat.Archive, flat.Record), (appearance.BillboardArchive, appearance.BillboardRecord));
                }
            }
            else Assert.Empty(definitions.Factions.Factions[person.FactionId].FlatVisuals);
            if (person.QuestorId is long id)
            {
                Assert.Equal(giver.DurableId, id);
                Assert.Equal(giver.DisplayName, person.DisplayName);
                Assert.Equal(giver.Appearance.NameSeed, person.NameSeed);
                Assert.Equal(new[] { giver.DurableId }, selected.Binding.ActorIds);
            }
            else
            {
                Assert.Empty(selected.Binding.ActorIds);
                Assert.Equal(DaggerfallQuestResourceBindingKind.Pending, selected.Binding.Kind);
            }
        }
    }

    [Fact]
    public void Vampire_Person_selects_current_province_clan_without_the_player_clan()
    {
        var definitions = TestPayload.Definitions;
        var declaration = definitions.QuestSources.Resources.Single(value => value.SourceFile == "R0C11Y28.txt" && value.CanonicalId == "vamp");
        var site = definitions.Locations.Records.First(value => value.Region == 17);
        var allocator = new DaggerfallQuestPersonAllocator(definitions, RandomMinimum.Create(), new(definitions, RandomMinimum.Create()),
            _ => new(site, Giver(), 152));
        var selected = allocator.Allocate(Instance(declaration.SourceFile), declaration);
        Assert.Equal(150, selected.SelectedPerson!.FactionId);
        Assert.Equal(150, selected.SelectedPerson.VampireClanFactionId);
        Assert.Equal("Vraseth", selected.Text!.NpcVampireClan);
        Assert.NotEqual(definitions.Factions.Factions[152].Name, selected.Text.NpcVampireClan);
    }

    [Fact]
    public void Missing_quest_giver_and_permanent_player_clan_report_the_required_owner()
    {
        var definitions = TestPayload.Definitions;
        var site = definitions.Locations.Records.First(value => value.Region == 17);
        var allocator = new DaggerfallQuestPersonAllocator(definitions, RandomMinimum.Create(), new(definitions, RandomMinimum.Create()), _ => new(site));
        var giver = definitions.QuestSources.Resources.First(value => value.Kind == "person" && value.Person?.Group == "Questor");
        Assert.Contains("explicitly bound quest giver", Assert.Throws<NotSupportedException>(() => allocator.Allocate(Instance(giver.SourceFile), giver)).Message);
        var vampire = definitions.QuestSources.Resources.First(value => value.Kind == "person" && value.SourceFile.StartsWith("$CUREVAM")
            && value.Person?.FactionType == "Vampire_Clan");
        Assert.Contains("racial owner", Assert.Throws<NotSupportedException>(() => allocator.Allocate(Instance(vampire.SourceFile), vampire)).Message);
    }

    [Fact]
    public void Ordinary_start_saves_each_Person_selection_and_explicit_quest_giver()
    {
        var root = JsonNode.Parse(TestPayload.CombinedText)!.AsObject();
        var rows = root["questSources"]!["resources"]!["declarations"]!.AsArray();
        foreach (string symbol in new[] { "qgiver", "vamp" })
        {
            var row = rows.Single(value => value!["sourceFile"]!.GetValue<string>() == "R0C11Y28.txt"
                && value["symbol"]!["canonicalId"]!.GetValue<string>() == symbol)!.DeepClone();
            row["sourceFile"] = "people.txt"; row["quest"] = "people"; rows.Add(row);
        }
        root["questSources"]!["quests"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"people","displayName":"","sourceFile":"people.txt","disposition":"compiled","messages":[],"blocks":[],"diagnostics":[]}
            """));
        var definitions = DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(root.ToJsonString()));
        var site = definitions.Locations.Records.First(value => value.Region == 17);
        var giver = Giver();
        DaggerfallQuestInstances quests = new(definitions, RandomMinimum.Create());
        var otherGiver = giver with { DurableId = 1235, StableKey = "other-giver", DisplayName = "Other guild quest giver" };
        quests.BindPersonAllocator(new(definitions, RandomMinimum.Create(), new(definitions, RandomMinimum.Create()),
            instance => new(site, instance.QuestorId == giver.DurableId ? giver : otherGiver)));
        var first = quests.Start(new("people-one", "people.txt", "people", DaggerfallQuestLifecycle.Active, null, [], []) { QuestorId = giver.DurableId, FactionId = 41 });
        var second = quests.Start(new("people-two", "people.txt", "people", DaggerfallQuestLifecycle.Active, null, [], []) { QuestorId = otherGiver.DurableId, FactionId = 41 });
        Assert.Equal(2, first.Resources.Length);
        Assert.Equal(giver.DurableId, first.QuestorId);
        Assert.NotEqual(first.InstanceId, second.InstanceId);
        Assert.NotEqual(first.Resources.Single(value => value.Symbol == "qgiver").Text!.Name,
            second.Resources.Single(value => value.Symbol == "qgiver").Text!.Name);
        Assert.Equal(giver.DurableId, first.Resources.Single(value => value.Symbol == "qgiver").Binding.ActorIds.Single());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(quests.Capture(), DaggerfallSaveJsonContext.Default.DaggerfallQuestInstancesSave);
        DaggerfallQuestInstances restored = new(definitions, RandomMaximum.Create());
        restored.Restore(JsonSerializer.Deserialize(bytes, DaggerfallSaveJsonContext.Default.DaggerfallQuestInstancesSave)!);
        Assert.True(restored.TryGet(first.InstanceId, out var saved));
        Assert.Equal(first.QuestorId, saved!.QuestorId);
        foreach (var resource in first.Resources)
        {
            var retained = saved.Resources.Single(value => value.Symbol == resource.Symbol);
            Assert.Equal(resource.SelectedPerson, retained.SelectedPerson);
            Assert.Equal(resource.Text, retained.Text);
        }
    }

    [Fact]
    public void Existing_quest_giver_names_use_the_NPC_identity_across_quest_instances()
    {
        var definitions = TestPayload.Definitions;
        var giver = Giver() with { DisplayName = null };
        var site = definitions.Locations.Records.First(value => value.Region == 17);
        var random = KeyedRandomFake.Create(0);
        var allocator = new DaggerfallQuestPersonAllocator(definitions, random.Service, new(definitions, random.Service), _ => new(site, giver));
        var declaration = definitions.QuestSources.Resources.First(value => value.Kind == "person" && value.Person?.Group == "Questor");
        var first = allocator.Allocate(Instance(declaration.SourceFile) with { InstanceId = "first" }, declaration);
        var firstKeys = random.Requests.Where(value => value.Key.Contains("/name/", StringComparison.Ordinal)).Select(value => value.Key).ToArray();
        random.Requests.Clear();
        var second = allocator.Allocate(Instance(declaration.SourceFile) with { InstanceId = "second" }, declaration);
        Assert.NotEmpty(firstKeys);
        Assert.Equal(firstKeys, random.Requests.Where(value => value.Key.Contains("/name/", StringComparison.Ordinal)).Select(value => value.Key));
        Assert.Equal(first.Text!.Name, second.Text!.Name);
    }

    [Fact]
    public void Retained_vampire_message_uses_the_referenced_NPC_clan_after_encoded_resource_restore()
    {
        var definitions = TestPayload.Definitions;
        var source = definitions.QuestSources.Resolve("R0C11Y28.txt");
        var declaration = definitions.QuestSources.Resources.Single(value => value.SourceFile == source.SourceFile && value.CanonicalId == "vamp");
        var site = definitions.Locations.Records.First(value => value.Region == 17);
        var allocator = new DaggerfallQuestPersonAllocator(definitions, RandomMinimum.Create(), new(definitions, RandomMinimum.Create()),
            _ => new(site, Giver(), 152));
        var selection = allocator.Allocate(Instance(source.SourceFile), declaration);
        var program = DaggerfallQuestTaskCompiler.Compile(source);
        var save = Instance(source.SourceFile) with { Resources = [selection], Tasks = DaggerfallQuestTaskCompiler.InitialState(program) };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(save, DaggerfallSaveJsonContext.Default.DaggerfallQuestInstanceSave);
        var restored = JsonSerializer.Deserialize(bytes, DaggerfallSaveJsonContext.Default.DaggerfallQuestInstanceSave)!;
        var runtime = new DaggerfallQuestRuntimeInstance(restored, program);
        var messages = new DaggerfallQuestMessages(new Presentation.DaggerfallTextResolver(definitions.Text), definitions.QuestSources.Quests);
        messages.Popup(runtime, 1017);
        var context = new DaggerfallQuestMessageContext(Presentation.DaggerfallTextContext.Empty with
            { Faction = new(VampireClan: "Thrafey", NpcVampireClan: "Thrafey") },
            new Dictionary<string, DaggerfallQuestResourceTextContext> { ["vamp"] = restored.Resources.Single().Text! });
        var rendered = messages.Render([runtime], context).Single();
        Assert.Contains("one of the Vraseth", rendered.Text);
        Assert.DoesNotContain("Thrafey", rendered.Text);
        Assert.DoesNotContain(rendered.Diagnostics, value => value.Contains("%vcn", StringComparison.Ordinal));
    }

    private static DaggerfallQuestInstanceSave Instance(string file) => new("instance/" + file, file, Path.GetFileNameWithoutExtension(file),
        DaggerfallQuestLifecycle.Active, null, [], []) { FactionId = 41 };
    private static DaggerfallNpc Giver() => new(1234, DaggerfallNpcKind.Static, "actual-giver", new(17, "Daggerfall", "guild"),
        new("breton", "Male", 182, 0, 417, 41), "questor", ["talk", "quest"], DaggerfallNpcPresence.Active, null, null, null)
        { DisplayName = "Guild quest giver" };
}
