using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Control settings validate complete changes before making them observable.</summary>
public sealed class DaggerfallControlSettingsTests
{
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

        ArgumentException menu = Assert.Throws<ArgumentException>(() => settings.Rebind("menu", ["KeyQ"]));
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
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallControlSettings.Parse("""{"no-such-action":["KeyX"]}"""));

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
        return JsonSerializer.Serialize(bindings);
    }
}
