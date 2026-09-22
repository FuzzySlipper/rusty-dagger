using WorldRpg.Rulesets.Daggerfall;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Control settings: catalog defaults, conflict and reserved refusal, swap, reset and persisted
/// round trips.
/// </summary>
public sealed class DaggerfallControlSettingsTests
{
    [Fact]
    public void Rebinds_refuse_conflicts_and_reserved_keys()
    {
        DaggerfallControlSettings settings = new();
        Assert.Equal(["KeyW"], settings.KeysFor("move.forward"));

        // A collision names its holder rather than doubling the key.
        InvalidOperationException conflict = Assert.Throws<InvalidOperationException>(() => settings.Rebind("move.forward", ["KeyS"]));
        Assert.Contains("move.backward", conflict.Message, StringComparison.Ordinal);
        Assert.Equal(["KeyW"], settings.KeysFor("move.forward"));

        // Escape never rebinds.
        Assert.Throws<ArgumentException>(() => settings.Rebind("attack", ["Escape"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Rebind("no-such-action", ["KeyX"]));

        // An explicit swap moves both bindings.
        settings.Rebind("move.forward", ["KeyS"], swap: true);
        Assert.Equal(["KeyS"], settings.KeysFor("move.forward"));
        Assert.Equal(["KeyW"], settings.KeysFor("move.backward"));

        settings.Reset();
        Assert.Equal(["KeyW"], settings.KeysFor("move.forward"));
        Assert.Equal(["KeyS"], settings.KeysFor("move.backward"));
    }

    [Fact]
    public void Persisted_settings_round_trip_and_refuse_tampering()
    {
        DaggerfallControlSettings settings = new();
        settings.Rebind("spell", ["KeyQ"]);
        string json = settings.Serialize();

        DaggerfallControlSettings restored = DaggerfallControlSettings.Parse(json);
        Assert.Equal(["KeyQ"], restored.KeysFor("spell"));
        Assert.Equal(["KeyW"], restored.KeysFor("move.forward"));

        Assert.Throws<ArgumentException>(() => DaggerfallControlSettings.Parse("not json"));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallControlSettings.Parse("""{"no-such-action":["KeyX"]}"""));
        Assert.Throws<ArgumentException>(() => DaggerfallControlSettings.Parse("""{"attack":["Escape"]}"""));
    }
}
