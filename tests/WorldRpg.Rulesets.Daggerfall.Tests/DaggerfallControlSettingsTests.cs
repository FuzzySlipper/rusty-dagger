using System.Text.Json;
using System.Xml.Linq;
using System.Text;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Control settings validate complete changes before making them observable.</summary>
public sealed class DaggerfallControlSettingsTests
{
    [Fact]
    public void Controller_buttons_rebind_with_conflicts_named_swaps_explicit_and_the_menu_and_movement_kept()
    {
        IReadOnlyList<WorldRpg.Kit.Controls.ControllerActionBinding> pad = DaggerfallTuning.Defaults.ControllerInput!.Actions;
        DaggerfallControlSettings settings = new();
        Assert.Equal([0], settings.ControllerButtons(pad)["attack"]);
        Assert.Equal([8], settings.ControllerButtons(pad)["inventory"]);
        // A free button: the action moves there.
        settings.RebindButton("spell", 5, pad);
        Assert.Equal([5], settings.ControllerButtons(pad)["spell"]);
        // A held button is refused naming its holder, unless swapped; the holder then takes the old buttons.
        InvalidOperationException held = Assert.Throws<InvalidOperationException>(() => settings.RebindButton("inventory", 0, pad));
        Assert.Contains("'attack'", held.Message, StringComparison.Ordinal);
        settings.RebindButton("inventory", 0, pad, swap: true);
        Assert.Equal([0], settings.ControllerButtons(pad)["inventory"]);
        Assert.Equal([8], settings.ControllerButtons(pad)["attack"]);
        // The menu keeps its button, and movement rides the stick.
        Assert.Throws<InvalidOperationException>(() => settings.RebindButton("rest", 9, pad));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.RebindButton("jump", 3, pad));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.RebindButton("menu", 3, pad));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.RebindButton("move.forward", 3, pad));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.RebindButton("rest", 16, pad));
        // The choice persists, and reset returns the authored layout.
        DaggerfallControlSettings restored = DaggerfallControlSettings.Parse(settings.Serialize());
        Assert.Equal([0], restored.ControllerButtons(pad)["inventory"]);
        Assert.Contains(restored.ControllerActions(pad), binding => binding.Action == DaggerfallControlSettings.InputAction("menu") && binding.Button == ControllerButton.Button9);
        restored.Reset();
        Assert.Equal([8], restored.ControllerButtons(pad)["inventory"]);
    }

    [Fact]
    public void Preferences_written_in_an_older_layout_are_refused_naming_what_is_missing_and_current_ones_round_trip()
    {
        // An earlier build stored the bindings as a bare action map, without the bindings wrapper or the look settings.
        ArgumentException old = Assert.Throws<ArgumentException>(() => DaggerfallControlSettings.Parse("""{"move.forward":["KeyW"]}"""));
        Assert.Contains("not the current preference layout", old.Message, StringComparison.Ordinal);
        Assert.Contains("bindings", old.Message, StringComparison.Ordinal);
        DaggerfallControlSettings current = new();
        current.SetUiScale(1.5f);
        Assert.Equal(current.Serialize(), DaggerfallControlSettings.Parse(current.Serialize()).Serialize());
    }

    [Fact]
    public void Every_physical_binding_names_a_digital_intent_declared_to_the_Engine()
    {
        XDocument project = XDocument.Load(Path.Combine(TestData.RepositoryRoot, "src/WorldRpg.Host/WorldRpg.Host.csproj"));
        var declared = project.Descendants("RustyEngineProductInputIntent")
            .ToDictionary(element => (string)element.Attribute("Include")!, element => (string)element.Attribute("Value")!);
        foreach (var mapping in new DaggerfallControlSettings().PhysicalMappings())
        {
            string intent = Encoding.UTF8.GetString(mapping.Intent.Span);
            Assert.True(declared.TryGetValue(intent, out string? kind), $"Undeclared physical intent: {intent}");
            Assert.Equal("digital", kind);
        }
    }

    [Fact]
    public void Physical_attack_mapping_uses_the_active_engine_gameplay_context()
    {
        DaggerfallControlSettings settings = new();
        settings.Rebind("attack", ["KeyB"]);

        ProductInputMapping attack = Assert.Single(settings.PhysicalMappings(), mapping =>
            mapping.Intent.Span.SequenceEqual("attack"u8));
        Assert.Equal(KeyboardControl.KeyB, attack.Keyboard);
        Assert.Equal("gameplay.default", Encoding.UTF8.GetString(attack.Context.Value.Span));
    }

    [Fact]
    public void Swap_round_trips_as_one_complete_binding_set()
    {
        DaggerfallControlSettings settings = new();

        settings.Rebind("move.forward", ["KeyS"], swap: true);
        DaggerfallControlSettings restored = DaggerfallControlSettings.Parse(settings.Serialize());

        Assert.Equal(["KeyS"], restored.KeysFor("move.forward"));
        Assert.Equal(["KeyW"], restored.KeysFor("move.backward"));
        Assert.Equal(settings.Serialize(), restored.Serialize());
    }

    [Fact]
    public void Rebind_rejects_invalid_complete_changes_without_mutating_bindings()
    {
        DaggerfallControlSettings settings = new();
        string before = settings.Serialize();

        InvalidOperationException multiConflict = Assert.Throws<InvalidOperationException>(() => settings.Rebind("move.forward", ["KeyS", "KeyD"], swap: true));
        Assert.Contains("multiple actions", multiConflict.Message, StringComparison.Ordinal);
        Assert.Equal(before, settings.Serialize());

        ArgumentException reservedAfterConflict = Assert.Throws<ArgumentException>(() => settings.Rebind("move.forward", ["KeyS", "Escape"], swap: true));
        Assert.Contains("reserved", reservedAfterConflict.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, settings.Serialize());

        ArgumentException menu = Assert.Throws<ArgumentException>(() => settings.Rebind("menu", ["KeyB"]));
        Assert.Contains("cannot be rebound", menu.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, settings.Serialize());
    }

    [Theory]
    [InlineData("")]
    [InlineData("NoSuchKey")]
    [InlineData("1")]
    public void Rebind_reports_empty_and_unknown_controls_without_mutating_bindings(string key)
    {
        DaggerfallControlSettings settings = new();
        string before = settings.Serialize();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => settings.Rebind("attack", [key]));

        Assert.Contains(string.IsNullOrEmpty(key) ? "empty" : "not a supported", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, settings.Serialize());
    }

    [Fact]
    public void Rebind_reports_null_controls_without_mutating_bindings()
    {
        DaggerfallControlSettings settings = new();
        string before = settings.Serialize();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => settings.Rebind("attack", [null!]));

        Assert.Contains("null", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, settings.Serialize());
    }

    [Fact]
    public void Persisted_controls_refuse_unknown_actions_and_invalid_controls()
    {
        Assert.Throws<ArgumentException>(() => DaggerfallControlSettings.Parse("not json"));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallControlSettings.Parse(PersistedBindings(new("no-such-action", "KeyX"))));

        ArgumentException nullControl = Assert.Throws<ArgumentException>(() => DaggerfallControlSettings.Parse(PersistedBindings(new("attack", null))));
        Assert.Contains("null", nullControl.Message, StringComparison.OrdinalIgnoreCase);

        ArgumentException emptyControl = Assert.Throws<ArgumentException>(() => DaggerfallControlSettings.Parse(PersistedBindings(new("attack", ""))));
        Assert.Contains("empty", emptyControl.Message, StringComparison.OrdinalIgnoreCase);

        ArgumentException unknownControl = Assert.Throws<ArgumentException>(() => DaggerfallControlSettings.Parse(PersistedBindings(new("attack", "NoSuchKey"))));
        Assert.Contains("not a supported", unknownControl.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string PersistedBindings(KeyValuePair<string, string?> replacement)
    {
        Dictionary<string, List<string>?> bindings = new(StringComparer.Ordinal);
        foreach (DaggerfallControlAction action in DaggerfallControlSettings.Catalog)
        {
            bindings.Add(action.Id, action.DefaultKeys.ToList());
        }
        bindings[replacement.Key] = replacement.Value is null ? null : [replacement.Value];
        return JsonSerializer.Serialize(new { bindings, lookSensitivity = 1f, invertVertical = true });
    }
}
