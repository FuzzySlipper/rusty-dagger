using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestDiseaseTests
{
    [Fact]
    public void Source_disease_bypasses_saving_throw_and_save_load_preserves_apply_and_cure_once()
    {
        var definitions = Definitions("make pc ill with Witches'_Pox");
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        f.Session.State.Progression.AdvanceTo(0, 2);
        Start(f.Session); Advance(f.Session);
        Assert.Single(f.Session.State.Effects.Active, effect => effect.Definition.Key == "disease-witches-pox");
        using var restored = f.Restore();
        Advance(restored); Advance(restored);
        Assert.Single(restored.State.Effects.Active, effect => effect.Definition.Key == "disease-witches-pox");
        restored.State.Quests.Start(new("cure", "quest-cure.txt", "quest-cure", DaggerfallQuestLifecycle.Active, null, [], []));
        Advance(restored); Advance(restored);
        Assert.DoesNotContain(restored.State.Effects.Active, effect => effect.Definition.Key == "disease-witches-pox");
        var saved = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.All(saved.Quests.Instances, quest => Assert.All(quest.Tasks, task => Assert.All(task.OperationCompleted, Assert.True)));
        using var cured = DaggerfallSession.Restore(f.Engine.Context, f.Composition, restored.CaptureSave());
        Advance(cured);
        Assert.DoesNotContain(cured.State.Effects.Active, effect => effect.Definition.Key == "disease-witches-pox");
    }

    [Fact]
    public void Unknown_disease_reports_unavailable_without_running_the_next_action()
    {
        using var f = new SanguineRoseSessionTests.Fixture(definitions: Definitions("make pc ill with MissingDisease", "end quest"));
        Start(f.Session); Advance(f.Session);
        var quest = Assert.Single(f.Session.State.Quests.All);
        Assert.Equal(DaggerfallQuestLifecycle.Active, quest.Lifecycle);
        Assert.All(quest.Tasks[0].OperationCompleted, done => Assert.False(done));
        Assert.Contains("MissingDisease", quest.Tasks[0].OperationState[0].UnavailableReason);
        using var restored = f.Restore(); Advance(restored);
        Assert.Empty(restored.State.Effects.Active);
    }

    [Theory]
    [InlineData("cure vampirism", "Vampire")]
    [InlineData("cure lycanthropy", "Werewolf")]
    public void Transformation_cure_uses_permanent_owner_and_is_not_an_ordinary_disease(string action, string racialKind)
    {
        using var f = new SanguineRoseSessionTests.Fixture(definitions: Definitions(action));
        var kind = Enum.Parse<DaggerfallRacialKind>(racialKind);
        f.Session.State.RacialOverrides.Select(kind, "quest-test-racial", 0, vampireClan: kind == DaggerfallRacialKind.Vampire ? 153 : null);
        Start(f.Session); Advance(f.Session); Advance(f.Session);
        Assert.Null(f.Session.State.RacialOverrides.Current);
        using var restored = f.Restore(); Advance(restored);
        Assert.Null(restored.State.RacialOverrides.Current);
        Assert.True(Assert.Single(restored.State.Quests.All).Tasks[0].OperationCompleted[0]);
    }

    internal static DaggerfallDefinitions Definitions(params string[] actions)
    {
        var root = JsonNode.Parse(TestPayload.CombinedText)!;
        Add("quest-actions", actions); Add("quest-cure", ["cure Witches'_Pox"]);
        root["questSources"]!["quests"]!.AsArray()[^2]!["blocks"]!.AsArray().Add(new JsonObject {
            ["kind"] = "task", ["firstLine"] = 100, ["lines"] = new JsonArray("done task:"), ["global"] = null });
        return DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(root.ToJsonString()));
        void Add(string name, string[] lines) => root["questSources"]!["quests"]!.AsArray().Add(new JsonObject {
            ["name"] = name, ["displayName"] = "", ["sourceFile"] = name + ".txt", ["disposition"] = "compiled",
            ["messages"] = new JsonArray(), ["diagnostics"] = new JsonArray(), ["blocks"] = new JsonArray(new JsonObject {
                ["kind"] = "headless", ["firstLine"] = 1, ["lines"] = JsonSerializer.SerializeToNode(lines), ["global"] = null }) });
    }
    internal static void Start(DaggerfallSession session) => session.State.Quests.Start(new("actions", "quest-actions.txt", "quest-actions", DaggerfallQuestLifecycle.Active, null, [], []));
    internal static void Advance(DaggerfallSession session, double elapsed = 0) => session.State.Quests.Advance(session.State.Variables, DaggerfallCalendar.Start, elapsed);
}
