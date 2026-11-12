using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class FidelityGapSessionTests
{
    [Fact]
    public void Current_save_without_character_is_refused_explicitly()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var save = f.Session.CaptureSave();
        var json = JsonNode.Parse(save.Bytes.Span)!.AsObject();
        string key = json.Select(pair => pair.Key).Single(key => key.Equals("character", StringComparison.OrdinalIgnoreCase));
        json.Remove(key);
        var error = Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Read(new RulesetSavePayload(save.Ruleset, Encoding.UTF8.GetBytes(json.ToJsonString()))));
        Assert.Contains("character", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Language_pacification_survives_perception_clear_and_save_until_attacked()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var memory = f.Session.State.Actors.Get(f.Enemy).Actor.Get<DaggerfallEnemyPerceptionMemory>();
        memory.Pacified = true; memory.HasEncounteredPlayer = true;
        memory.Clear(); Assert.True(memory.Pacified); Assert.True(memory.HasEncounteredPlayer);
        using var restored = f.Restore();
        var after = restored.State.Actors.Get(f.Enemy).Actor.Get<DaggerfallEnemyPerceptionMemory>();
        Assert.True(after.LanguagePacified); Assert.True(after.Pacified);
        after.SetForcedHostile(true); Assert.False(after.LanguagePacified); Assert.False(after.Pacified);
    }

    [Fact]
    public void Spellbook_actions_swap_sort_and_restore_the_player_order()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var s = f.Session;
        foreach (var key in new[] { "spell.001", "spell.002", "spell.003" }) s.State.Character.LearnSpell(key);
        var before = s.ReadSpells().Available.Select(spell => spell.Key).ToArray();
        f.Submit(new { action = "spell-swap", key = before[0], item = before[^1] });
        var swapped = s.ReadSpells().Available.Select(spell => spell.Key).ToArray();
        Assert.Equal(before[^1], swapped[0]); Assert.Equal(before[0], swapped[^1]);
        using var restored = f.Restore();
        Assert.Equal(swapped, restored.ReadSpells().Available.Select(spell => spell.Key));
        f.Submit(new { action = "spell-sort" });
        Assert.Equal(s.ReadSpells().Available.Select(spell => spell.Name).Order(StringComparer.OrdinalIgnoreCase),
            s.ReadSpells().Available.Select(spell => spell.Name));
        f.Submit(new { action = "spell-sort" });
        Assert.Equal(before.Order(), s.ReadSpells().Available.Select(spell => spell.Key).Order());
    }

    [Fact]
    public void Disease_reports_when_symptoms_act_not_at_exposure()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var s = f.Session; s.State.Progression.AdvanceTo(0, 2);
        Assert.Equal(DaggerfallDiseaseAdmission.Started, s.InflictDisease(new("fidelity-disease", "test", null, 1,
            [DaggerfallClassicDisease.Consumption], BypassSavingThrows: true)));
        Assert.DoesNotContain("You feel somewhat bad.", s.Presentation.Messages);
        s.AdvanceElapsedTime(86400, idleFatigue: false);
        Assert.Contains("You feel somewhat bad.", s.Presentation.Messages);
    }
}
