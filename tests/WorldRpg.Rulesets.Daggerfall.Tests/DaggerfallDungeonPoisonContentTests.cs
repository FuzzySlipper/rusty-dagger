using System.Text;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonPoisonContentTests
{
    private const string Publication = """
        {"world":{"actions":[{"id":"action/n0000007-rdb/0/0/flat-34","sourceOffset":20287,
        "triggerFlag":2,"actionFlag":26,"axis":0,"duration":0,"magnitude":0,"nextObjectOffset":-2,
        "nextActionId":null,"doorId":null,"isFlat":true,"soundIndex":7,"rawIndex":7,
        "poison":{"sourceRecord":"N0000007.RDB","disposition":"source-unresolved","poisonId":null}}]}}
        """;

    [Fact]
    public void Approved_unresolved_record_retains_raw_facts_and_reports_unsupported_without_calling_effect_owner()
    {
        DaggerfallContentDiagnostics diagnostics = new();
        DaggerfallDungeonActionDefinition action = Assert.Single(DaggerfallSiteContent.ReadNormalizedActions(Encoding.UTF8.GetBytes(Publication), [], diagnostics));
        diagnostics.ThrowIfAny();
        Assert.Equal((20287, (byte)7, (byte)7, -2), (action.SourceOffset, action.RawIndex, action.SoundIndex, action.NextObjectOffset));
        int effectCalls = 0;
        DaggerfallDungeonActionGraph graph = new("profile", [action], new DaggerfallVariableStore(new Dictionary<string, int>()),
            executeFamilyAction: _ => { effectCalls++; return new(action.Id, DaggerfallDungeonActionOutcome.Applied); });
        var result = graph.Trigger(action.Id, DaggerfallDungeonActionEvent.Direct);
        Assert.False(result.Applied);
        Assert.True(result.HasUnsupportedAction);
        Assert.Equal(0, effectCalls);
        Assert.Contains("source-unresolved", Assert.Single(result.Executions).Diagnostic);
        Assert.Contains("N0000007.RDB:20287", Assert.Single(result.Executions).Diagnostic);
        DaggerfallDungeonActionGraph restored = new("profile", [action], new DaggerfallVariableStore(new Dictionary<string, int>()), graph.Capture());
        Assert.Equal(DaggerfallDungeonActionOutcome.UnsupportedAction, Assert.Single(restored.Trigger(action.Id, DaggerfallDungeonActionEvent.Direct).Executions).Outcome);
        Assert.Equal(2UL, restored.State[action.Id].ActivationCount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("variant-7")]
    [InlineData("variant-128")]
    [InlineData("variant-139")]
    [InlineData("variant-140")]
    [InlineData("invalid-variant")]
    [InlineData("wrong-record")]
    [InlineData("wrong-disposition")]
    [InlineData("wrong-raw-index")]
    [InlineData("wrong-action")]
    public void Reader_refuses_missing_or_unapproved_poison_authority(string mutation)
    {
        JsonNode root = JsonNode.Parse(Publication)!;
        JsonObject action = root["world"]!["actions"]![0]!.AsObject();
        JsonObject poison = action["poison"]!.AsObject();
        switch (mutation)
        {
            case "missing": action.Remove("poison"); break;
            case "invalid-variant": poison["poisonId"] = "invented"; break;
            case "wrong-record": poison["sourceRecord"] = "S0000007.RDB"; break;
            case "wrong-disposition": poison["disposition"] = "supported"; break;
            case "wrong-raw-index": action["rawIndex"] = 128; break;
            case "wrong-action": action["actionFlag"] = 9; break;
            default: poison["poisonId"] = int.Parse(mutation[8..], System.Globalization.CultureInfo.InvariantCulture); break;
        }
        DaggerfallContentDiagnostics diagnostics = new();
        _ = DaggerfallSiteContent.ReadNormalizedActions(Encoding.UTF8.GetBytes(root.ToJsonString()), [], diagnostics);
        Assert.Throws<DaggerfallContentException>(diagnostics.ThrowIfAny);
    }
}
